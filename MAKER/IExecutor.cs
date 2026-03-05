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
        /// </summary>
        Task<IList<Step>> Plan(string prompt, int batchSize = 2, int k = 10, IList<Step>? prependSteps = null, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a sequence of steps using the specified prompt and returns the resulting state as a string.
        /// </summary>
        Task<string> Execute(IList<Step> steps, string prompt, int batchSize = 2, int k = 10, List<IAIRedFlagValidator>? validators = null, object? tools = null, CancellationToken cancellationToken = default);
    }
}
