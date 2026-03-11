using Anthropic;
using MAKER.Configuration;
using Microsoft.Extensions.AI;

namespace MAKER.AI.Clients
{
    internal sealed class AnthropicAIClient(ExecutorConfig config, string model) : AIClientBase
    {
        private readonly AnthropicClient _client = new() { ApiKey = config.AIProviderKeys.Anthropic };

        protected override string Model => model;

        protected override IChatClient GetClient() => _client.AsIChatClient(model);

        protected override IEnumerable<AITool> GetAdditionalTools() => [new HostedCodeInterpreterTool()];
    }
}
