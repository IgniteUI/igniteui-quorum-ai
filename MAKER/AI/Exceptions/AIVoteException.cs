using MAKER.AI.Models;

namespace MAKER.AI.Exceptions
{
    public class AIVoteException : Exception
    {
        public VoteCancellationReason Reason { get; private set; } = VoteCancellationReason.Rejected;

        public IEnumerable<string> RejectionReasons { get; private set; } = [];

        public IEnumerable<Step> ProposedSteps { get; private set; } = [];

        public AIVoteException() { }

        public AIVoteException(VoteCancellationReason reason) {
            Reason = reason;
        }

        public AIVoteException(string message) : base(message) { }

        public AIVoteException(string message, IEnumerable<Step> proposedSteps, IEnumerable<string> rejectionReasons) : base(message) {
            ProposedSteps = proposedSteps;
            RejectionReasons = rejectionReasons;
        }

        public AIVoteException(string message, VoteCancellationReason reason) : base(message) {
            Reason = reason;
        }

        public AIVoteException(string message, Exception inner) : base(message, inner) { }
    }

    public enum VoteCancellationReason
    {
        Rejected,
        Ended,
        Contentious
    }
}
