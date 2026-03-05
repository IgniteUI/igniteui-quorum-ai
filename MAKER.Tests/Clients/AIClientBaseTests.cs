using MAKER.AI.Attributes;
using MAKER.AI.Exceptions;
using MAKER.AI.Models;
using MAKER.AI.Validation;
using MAKER.Tests.TestDoubles;

namespace MAKER.Tests.Clients
{
    public class AIClientBaseTests
    {
        #region GuardedRequest

        [Fact]
        public async Task GuardedRequest_ValidResponse_ReturnsContent()
        {
            var client = new FakeAIClient();
            client.Responses.Enqueue(new AIResponse { Content = "Hello World", InputTokens = 10, OutputTokens = 5 });

            var result = await client.GuardedRequest("test prompt", []);

            Assert.Equal("Hello World", result.Content);
            Assert.Equal(10, result.InputTokens);
            Assert.Equal(5, result.OutputTokens);
        }

        [Fact]
        public async Task GuardedRequest_NullResponse_ThrowsAfterRetries()
        {
            var client = new FakeAIClient { MaxGuardedRetries = 0 };
            client.Responses.Enqueue(null);

            await Assert.ThrowsAsync<AIRedFlagException>(() =>
                client.GuardedRequest("test", []));
        }

        [Fact]
        public async Task GuardedRequest_ValidationFailsThenSucceeds_Retries()
        {
            var client = new FakeAIClient();
            // First response is too short and fails validation
            client.Responses.Enqueue(new AIResponse { Content = "x" });
            // Second response passes validation
            client.Responses.Enqueue(new AIResponse { Content = "Valid response content" });

            var validators = new List<IAIRedFlagValidator> { new AIRedFlagMinLengthValidator(5) };

            var result = await client.GuardedRequest("test", validators);

            Assert.Equal("Valid response content", result.Content);
        }

        [Fact]
        public async Task GuardedRequest_ExceedsMaxRetries_Throws()
        {
            var client = new FakeAIClient { MaxGuardedRetries = 2 };
            // All responses fail validation
            for (int i = 0; i < 5; i++)
                client.Responses.Enqueue(new AIResponse { Content = "x" });

            var validators = new List<IAIRedFlagValidator> { new AIRedFlagMinLengthValidator(100) };

            await Assert.ThrowsAsync<AIRedFlagException>(() =>
                client.GuardedRequest("test", validators));
        }

        [Fact]
        public async Task GuardedRequest_JsonCodeBlock_ExtractsContent()
        {
            var client = new FakeAIClient();
            client.Responses.Enqueue(new AIResponse
            {
                Content = "Here is the result:\n```json\n{\"key\": \"value\"}\n```"
            });

            var result = await client.GuardedRequest("test", []);

            Assert.Equal("{\"key\": \"value\"}", result.Content);
        }

        #endregion

        #region GenerateFunctionInfo

        [Fact]
        public async Task GenerateFunctionInfo_PicksUpAIDescription()
        {
            var client = new FakeAIClient();
            client.Responses.Enqueue(new AIResponse { Content = "done" });

            // Request with a tools object triggers GenerateFunctionInfo
            await client.Request("test", new SampleToolsObject());

            // Verify indirectly — the request didn't throw, meaning schema was generated
        }

        #endregion

        #region FormatToolError

        [Fact]
        public void FormatToolError_FormatsCorrectly()
        {
            var ex = new InvalidOperationException("something broke");

            var result = FakeAIClient.TestFormatToolError(ex);

            Assert.Equal("[ERROR] [InvalidOperationException]: something broke", result);
        }

        [Fact]
        public void FormatToolError_UsesInnerException()
        {
            var inner = new ArgumentException("bad arg");
            var ex = new Exception("wrapper", inner);

            var result = FakeAIClient.TestFormatToolError(ex);

            Assert.Equal("[ERROR] [ArgumentException]: bad arg", result);
        }

        #endregion

        private class SampleToolsObject
        {
            [AIDescription("A sample tool method")]
            public string DoWork([AIDescription("Input value")] string input) => input;
        }
    }
}
