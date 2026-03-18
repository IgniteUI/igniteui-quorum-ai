using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Validation;

namespace MAKER
{
    public interface IExecutor
    {
        string Format { get; set; }

        List<IAIRedFlagValidator> DefaultPlanningValidators { get; set; }

        #region Events
        event Action<IList<Step>, IList<Step>>? OnStepsAdded;
        event Action<IList<Step>>? OnStepsProposed;
        event Action<AIVoteException>? OnStepsRejected;
        event Action<VoteState>? OnPlanVoteChanged;
        event Action<IList<Step>, IList<Step>>? OnExecutionStarted;
        event Action<string>? OnStateChanged;
        event Action<VoteState>? OnExecutionVoteChanged;
        #endregion

        /// <summary>
        /// Generates a sequence of plan Steps based on the specified prompt and configuration parameters.
        /// MCP servers configured in <see cref="Configuration.ExecutorConfig.McpServers"/> are automatically included.
        /// </summary>
        /// <param name="prompt">The input prompt that guides the planning process. This should clearly describe the objective or task to be planned.</param>
        /// <param name="batchSize">The number of Steps to generate in each batch. Must be greater than zero.</param>
        /// <param name="k">The difference in votes required for a voting decision to be made.</param>
        /// <param name="maxSteps">The maximum number of steps the plan may contain. Must be greater than zero.</param>
        /// <param name="prependSteps">An optional list of steps to prepend to the generated plan. If provided, these steps will appear at the beginning of the returned sequence.</param>
        /// <param name="validators">An optional list of validators used to check for red flags in the generated steps. Each validator is applied
        /// to ensure the plan meets safety or compliance requirements.</param>
        /// <param name="tools">An optional set of tools available to the AI during planning.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A list of steps representing the generated plan.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize"/>, <paramref name="k"/>, or <paramref name="maxSteps"/> is less than or equal to zero.</exception>
        Task<IList<Step>> Plan(string prompt, int batchSize = 2, int k = 10, int maxSteps = 10, IList<Step>? prependSteps = null, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a sequence of steps using the specified prompt and returns the resulting state as a string.
        /// MCP servers configured in <see cref="Configuration.ExecutorConfig.McpServers"/> are automatically included.
        /// </summary>
        /// <param name="steps">The list of steps to execute. Each step defines an operation in the workflow.</param>
        /// <param name="prompt">The input prompt that guides the execution of the steps.</param>
        /// <param name="batchSize">The number of steps to process in each batch. Must be greater than zero.</param>
        /// <param name="k">The difference in votes required for a voting decision to be made.</param>
        /// <param name="validators">A list of validators used to check for AI-generated red flags during execution. If null, no validation is performed.</param>
        /// <param name="tools">An optional set of tools available to the AI during execution.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>The resulting state of the Step execution.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize"/> or <paramref name="k"/> is less than or equal to zero.</exception>
        Task<string> Execute(IList<Step> steps, string prompt, int batchSize = 2, int k = 10, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default);
    }
}
