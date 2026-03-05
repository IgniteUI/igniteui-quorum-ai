using MAKER.AI.Clients;
using MAKER.AI.Models;

namespace MAKER.Tests.TestDoubles
{
    internal sealed class FakeAIClient : AIClientBase
    {
        public Queue<AIResponse?> Responses { get; } = new();

        protected override Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, object? toolsObject = null, CancellationToken cancellationToken = default)
        {
            if (Responses.Count == 0)
                throw new InvalidOperationException("No more responses configured in FakeAIClient.");

            return Task.FromResult(Responses.Dequeue());
        }

        public static string TestFormatToolError(Exception ex) => FormatToolError(ex);
    }
}
