using MAKER.AI.Models;

namespace MAKER.AI.Exceptions
{
    public class AIVoteException : Exception
    {
        public VoteCancellationReason Reason { get; init; } = VoteCancellationReason.Rejected;

        public IEnumerable<string> RejectionReasons { get; init; } = [];

        public IEnumerable<Step> ProposedSteps { get; init; } = [];

        public AIVoteException() { }

        public AIVoteException(VoteCancellationReason reason)
        {
            Reason = reason;
        }

        public AIVoteException(string message) : base(message) { }

        public AIVoteException(string message, IEnumerable<Step> proposedSteps, IEnumerable<string> rejectionReasons) : base(message)
        {
            ProposedSteps = proposedSteps;
            RejectionReasons = rejectionReasons;
        }

        public AIVoteException(string message, VoteCancellationReason reason) : base(message)
        {
            Reason = reason;
        }

        public AIVoteException(string message, Exception inner) : base(message, inner) { }
    }
}
