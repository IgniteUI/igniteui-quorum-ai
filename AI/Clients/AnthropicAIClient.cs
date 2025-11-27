using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using MAKER.Configuration;

namespace MAKER.AI.Clients
{
    public class AnthropicAIClient : IAIClient
    {
        private readonly AnthropicClient _client;
        private readonly Model _model;

        public AnthropicAIClient(ExecutorConfig config, string model)
        {
            _model = Enum.Parse<Model>(model);
            _client = new AnthropicClient()
            {
                APIKey = config.AIProviderKeys.Anthropic
            };
        }

        public async Task<string?> Request(string prompt)
        {
            MessageCreateParams parameters = new()
            {
                MaxTokens = 1024,
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = prompt,
                    },
                ],
                Model = _model,
            };

            try
            {
                var response = await _client.Messages.Create(parameters);
                response.Content[response.Content.Count - 1].TryPickText(out var text);

                return text?.Text;
            }
            catch (AnthropicBadRequestException)
            {
                throw;
            }
        }
    }
}
