using Google.GenAI;
using MAKER.Configuration;
using Microsoft.Extensions.AI;

namespace MAKER.AI.Clients
{
    internal sealed class GoogleAIClient(ExecutorConfig config, string model) : AIClientBase
    {
        private readonly Client _client = new(apiKey: config.AIProviderKeys.Google);

        protected override string Model => model;

        protected override IChatClient GetClient() => _client.AsIChatClient(model);
    }
}
