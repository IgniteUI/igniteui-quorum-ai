namespace MAKER.AI.Models
{
    public class AIResponse
    {
        public string? Content { get; init; }

        public int InputTokens { get; init; }

        public int OutputTokens { get; init; }

        public AIResponse() { }
    }
}
