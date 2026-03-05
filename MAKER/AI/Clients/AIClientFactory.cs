using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    internal sealed class AIClientFactory(ExecutorConfig executorConfig) : IAIClientFactory
    {
        public IAIClient CreateClient(ClientProviderConfig config)
        {
            return config.Provider switch
            {
                "OpenAI" => new OpenAIClient(executorConfig, config.Model, priority: false),
                "Google" => new GoogleAIClient(executorConfig, config.Model),
                "Anthropic" => new AnthropicAIClient(executorConfig, config.Model),
                _ => throw new NotSupportedException($"AI provider '{config.Provider}' is not supported."),
            };
        }
    }
}
