using MAKER.AI.Clients;
using MAKER.AI.Constants;
using MAKER.Configuration;

namespace MAKER.Tests.Clients
{
    public class AIClientFactoryTests
    {
        private readonly AIClientFactory _factory;

        public AIClientFactoryTests()
        {
            var config = new ExecutorConfig
            {
                AIProviderKeys = new AIProviderKeysConfig
                {
                    OpenAI = "test-key",
                    Google = "test-key",
                    Anthropic = "test-key"
                },
                Clients = new ClientsConfig
                {
                    Planning = new ClientProviderConfig { Provider = "OpenAI", Model = "gpt-4" },
                    PlanVoting = new ClientProviderConfig { Provider = "Google", Model = "gemini-2.5-flash" },
                    Execution = new ClientProviderConfig { Provider = "Anthropic", Model = "ClaudeHaiku4_5" },
                    ExecutionVoting = new ClientProviderConfig { Provider = "OpenAI", Model = "gpt-4" }
                },
                Instructions = new InstructionsConfig
                {
                    Plan = "", PlanVote = "", PlanRules = "", PlanFormat = "",
                    Execute = "", ExecuteVote = "", ExecuteRules = ""
                }
            };

            _factory = new AIClientFactory(config);
        }

        [Theory]
        [InlineData("OpenAI", "gpt-4")]
        [InlineData("Google", "gemini-2.5-flash")]
        [InlineData("Anthropic", "ClaudeHaiku4_5")]
        public void CreateClient_KnownProvider_ReturnsClient(string provider, string model)
        {
            var config = new ClientProviderConfig { Provider = provider, Model = model };

            var client = _factory.CreateClient(config);

            Assert.NotNull(client);
            Assert.IsAssignableFrom<AIClientBase>(client);
        }

        [Fact]
        public void CreateClient_UnknownProvider_ThrowsNotSupportedException()
        {
            var config = new ClientProviderConfig { Provider = "Unknown", Model = "test" };

            Assert.Throws<NotSupportedException>(() => _factory.CreateClient(config));
        }
    }
}
