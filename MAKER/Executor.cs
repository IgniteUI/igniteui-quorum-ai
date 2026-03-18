using MAKER.AI.Clients;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Orchestrators;
using MAKER.AI.Validation;
using MAKER.Configuration;

namespace MAKER
{
    public sealed class Executor : IExecutor
    {
        private readonly PlanningOrchestrator _planningOrchestrator;
        private readonly ExecutionOrchestrator _executionOrchestrator;
        private readonly List<MCPServerInfo> _mcpServers;

        public string Format { get; set; } = "plaintext";

        #region Events
        public event Action<IList<Step>, IList<Step>>? OnStepsAdded;
        public event Action<IList<Step>>? OnStepsProposed;
        public event Action<AIVoteException>? OnStepsRejected;
        public event Action<VoteState>? OnPlanVoteChanged;
        public event Action<IList<Step>, IList<Step>>? OnExecutionStarted;
        public event Action<string>? OnStateChanged;
        public event Action<VoteState>? OnExecutionVoteChanged;
        #endregion

        public List<IAIRedFlagValidator> DefaultPlanningValidators
        {
            get => _planningOrchestrator.DefaultPlanningValidators;
            set => _planningOrchestrator.DefaultPlanningValidators = value;
        }

        public Executor(ExecutorConfig config, string format)
            : this(config, format, new AIClientFactory(config))
        {
        }

        public Executor(ExecutorConfig config, string format, IAIClientFactory clientFactory)
        {
            Format = format;
            _mcpServers = config.McpServers;

            _planningOrchestrator = new PlanningOrchestrator(config,
                clientFactory.CreateClient(config.Clients.Planning),
                clientFactory.CreateClient(config.Clients.PlanVoting));
            _executionOrchestrator = new ExecutionOrchestrator(config,
                clientFactory.CreateClient(config.Clients.Execution),
                clientFactory.CreateClient(config.Clients.ExecutionVoting));

            _planningOrchestrator.OnStepsAccepted += (proposed, existing) => OnStepsAdded?.Invoke(proposed, existing);
            _planningOrchestrator.OnStepsProposed += steps => OnStepsProposed?.Invoke(steps);
            _planningOrchestrator.OnStepsRejected += ex => OnStepsRejected?.Invoke(ex);
            _planningOrchestrator.OnVoteChanged += state => OnPlanVoteChanged?.Invoke(state);
            _executionOrchestrator.OnExecutionStarted += (batch, completed) => OnExecutionStarted?.Invoke(batch, completed);
            _executionOrchestrator.OnStateChanged += state => OnStateChanged?.Invoke(state);
            _executionOrchestrator.OnVoteChanged += state => OnExecutionVoteChanged?.Invoke(state);
        }

        /// <inheritdoc />
        public async Task<IList<Step>> Plan(string prompt, int batchSize = 2, int k = 10, int maxSteps = 10, IList<Step>? prependSteps = null, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");
            if (maxSteps <= 0) throw new ArgumentOutOfRangeException($"{nameof(maxSteps)} must be greater than zero");

            return await _planningOrchestrator.Plan(prompt, Format, batchSize, k, maxSteps, prependSteps, validators, tools, _mcpServers, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<string> Execute(IList<Step> steps, string prompt, int batchSize = 2, int k = 10, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _executionOrchestrator.Execute(steps, prompt, Format, batchSize, k, validators, tools, _mcpServers, cancellationToken);
        }
    }
}
