using MAKER.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace MAKER.AI.Clients
{
    internal sealed class OpenAIClient(ExecutorConfig config, string model) : AIClientBase
    {
        private readonly ChatClient _client = new(model, config.AIProviderKeys.OpenAI);

        protected override string Model => model;

        protected override IChatClient GetClient() => _client.AsIChatClient();
    }
}