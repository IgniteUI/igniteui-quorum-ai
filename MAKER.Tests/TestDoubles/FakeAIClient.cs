using MAKER.AI.Clients;
using MAKER.AI.Models;
using Microsoft.Extensions.AI;

namespace MAKER.Tests.TestDoubles
{
    internal sealed class FakeAIClient : AIClientBase
    {
        public Queue<AIResponse?> Responses { get; } = new();

        protected override string Model => "fake-model";

        protected override IChatClient GetClient() =>
            throw new NotSupportedException("FakeAIClient does not use a real chat client.");

        protected override Task<AIResponse?> RequestInternal(string prompt, List<AIFunctionInfo>? tools = null, List<MCPServerInfo>? mcpServers = null, CancellationToken cancellationToken = default)
        {
            if (Responses.Count == 0)
                throw new InvalidOperationException("No more responses configured in FakeAIClient.");

            return Task.FromResult(Responses.Dequeue());
        }

        public static string TestFormatToolError(Exception ex) => FormatToolError(ex);
    }
}
