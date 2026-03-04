using MAKER.AI.Clients;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Orchestrators;
using MAKER.AI.Redflag;
using MAKER.Configuration;

namespace MAKER
{
    public class Executor
    {
        private readonly ExecutorConfig _config;
        private readonly PlanningOrchestrator _planningOrchestrator;
        private readonly ExecutionOrchestrator _executionOrchestrator;

        public string Format { get; set; } = "plaintext";

        #region Events
        public Action<IList<Step>, IList<Step>> OnStepsAdded
        {
            get => _planningOrchestrator.OnStepsAccepted;
            set => _planningOrchestrator.OnStepsAccepted = value;
        }

        public Action<IList<Step>> OnStepsProposed
        {
            get => _planningOrchestrator.OnStepsProposed;
            set => _planningOrchestrator.OnStepsProposed = value;
        }

        public Action<AIVoteException> OnStepsRejected
        {
            get => _planningOrchestrator.OnStepsRejected;
            set => _planningOrchestrator.OnStepsRejected = value;
        }

        public Action<VoteState> OnPlanVoteChanged
        {
            get => _planningOrchestrator.OnVoteChanged;
            set => _planningOrchestrator.OnVoteChanged = value;
        }

        public Action<IList<Step>, IList<Step>> OnExecutionStarted
        {
            get => _executionOrchestrator.OnExecutionStarted;
            set => _executionOrchestrator.OnExecutionStarted = value;
        }

        public Action<string> OnStateChanged
        {
            get => _executionOrchestrator.OnStateChanged;
            set => _executionOrchestrator.OnStateChanged = value;
        }

        public Action<VoteState> OnExecutionVoteChanged
        {
            get => _executionOrchestrator.OnVoteChanged;
            set => _executionOrchestrator.OnVoteChanged = value;
        }
        #endregion

        public List<IAIRedFlagValidator> DefaultPlanningValidators
        {
            get => _planningOrchestrator.DefaultPlanningValidators;
            set => _planningOrchestrator.DefaultPlanningValidators = value;
        }

        public Executor(ExecutorConfig config, string format)
        {
            _config = config;
            Format = format;

            var planningClient = InstantiateClient(_config.Clients.Planning);
            var planVotingClient = InstantiateClient(_config.Clients.PlanVoting);
            var executionClient = InstantiateClient(_config.Clients.Execution);
            var executionVotingClient = InstantiateClient(_config.Clients.ExecutionVoting);

            _planningOrchestrator = new PlanningOrchestrator(_config, planningClient, planVotingClient);
            _executionOrchestrator = new ExecutionOrchestrator(_config, executionClient, executionVotingClient);
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
        public async Task<IList<Step>> Plan(string prompt, int batchSize = 2, int k = 10, IList<Step> prependSteps = null!, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _planningOrchestrator.Plan(prompt, Format, batchSize, k, prependSteps, validators, tools);
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
        public async Task<string> Execute(IList<Step> steps, string prompt, int batchSize = 2, int k = 10, List<IAIRedFlagValidator> validators = null!, object? tools = null!)
        {
            if (batchSize <= 0) throw new ArgumentOutOfRangeException($"{nameof(batchSize)} must be greater than zero");
            if (k <= 0) throw new ArgumentOutOfRangeException($"{nameof(k)} must be greater than zero");

            return await _executionOrchestrator.Execute(steps, prompt, Format, batchSize, k, validators, tools);
        }

        private AIClientBase InstantiateClient(ClientProviderConfig config)
        {
            var type = config.Provider;
            var model = config.Model;

            return type switch
            {
                "OpenAI" => new OpenAIClient(_config, model, priority: false),
                "Google" => new GoogleAIClient(_config, model),
                "Anthropic" => new AnthropicAIClient(_config, model),
                _ => throw new NotImplementedException($"{type} IAIClient not implemented."),
            };
        }
    }
}
