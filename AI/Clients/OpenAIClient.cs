using MAKER.Configuration;
using OpenAI.Chat;

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
namespace MAKER.AI.Clients
{
    internal class OpenAIClient(ExecutorConfig config, string model, bool priority = false) : IAIClient
    {
        private readonly ChatClient _client = new(model: model, apiKey: config.AIProviderKeys.OpenAI);

        public async Task<string?> Request(string prompt)
        {
            var opts = new ChatCompletionOptions();

            if (priority)
            {
                opts.ServiceTier = new ChatServiceTier("priority");
            }

            var request = await _client.CompleteChatAsync([new UserChatMessage(prompt)], opts);

            return request.Value?.Content[0].Text;
        }
    }
}
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
