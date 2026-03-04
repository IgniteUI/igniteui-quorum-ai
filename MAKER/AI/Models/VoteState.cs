namespace MAKER.AI.Models
{
    public class VoteState
    {
        public required int KValue { get; init; }

        public Dictionary<string, int> Votes { get; init; } = [];
    }
}
