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

        /// <summary>
        /// Generates a sequence of plan Steps based on the specified prompt and configuration parameters.
        /// </summary>
        /// <param name="prompt">The input prompt that guides the planning process. This should clearly describe the objective or task to be planned.</param>
        /// <param name="batchSize">The number of Steps to generate in each batch. Must be greater than zero.</param>
        /// <param name="k">The difference in votes required for a voting decision to be made.</param>
        /// <param name="prependSteps">An optional list of steps to prepend to the generated plan. If provided, these steps will appear at the beginning of the returned sequence.</param>
        /// <param name="validators">An optional list of validators used to check for red flags in the generated steps. Each validator is applied
        /// to ensure the plan meets safety or compliance requirements.</param>
        /// <returns>A list of steps representing the generated plan.</returns>
        public async Task<IList<Step>> Plan(string prompt, int batchSize = 2, int k = 10, IList<Step>? prependSteps = null, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _planningOrchestrator.Plan(prompt, Format, batchSize, k, prependSteps, validators, tools, cancellationToken);
        }

        /// <summary>
        /// Executes a sequence of steps using the specified prompt and returns the resulting state as a string.
        /// </summary>
        /// <param name="steps">The list of steps to execute. Each step defines an operation in the workflow.</param>
        /// <param name="prompt">The input prompt that guides the execution of the steps.</param>
        /// <param name="batchSize">The number of steps to process in each batch. Must be greater than zero.</param>
        /// <param name="k">The difference in votes required for a voting decision to be made.</param>
        /// <param name="validators">A list of validators used to check for AI-generated red flags during execution. If null, no validation is performed.</param>
        /// <returns>The resulting state of the Step execution.</returns>
        public async Task<string> Execute(IList<Step> steps, string prompt, int batchSize = 2, int k = 10, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _executionOrchestrator.Execute(steps, prompt, Format, batchSize, k, validators, tools, cancellationToken);
        }
    }
}
