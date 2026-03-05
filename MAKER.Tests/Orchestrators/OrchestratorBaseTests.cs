using MAKER.Tests.TestDoubles;

namespace MAKER.Tests.Orchestrators
{
    public class OrchestratorBaseTests
    {
        private readonly TestableOrchestrator _orchestrator = new();

        [Fact]
        public void ClearUnusedTemplateVariables_RemovesAllPlaceholders()
        {
            var input = "Hello {TASK}, your {STATE} is {UNKNOWN}.";

            var result = _orchestrator.ClearUnusedTemplateVariables(input);

            Assert.Equal("Hello , your  is .", result);
        }

        [Fact]
        public void ClearUnusedTemplateVariables_PreservesNonPlaceholderBraces()
        {
            var input = "JSON: {\"key\": \"value\"} and {TASK}";

            var result = _orchestrator.ClearUnusedTemplateVariables(input);

            Assert.Equal("JSON: {\"key\": \"value\"} and ", result);
        }

        [Fact]
        public void ClearUnusedTemplateVariables_NoPlaceholders_ReturnsUnchanged()
        {
            var input = "No placeholders here.";

            var result = _orchestrator.ClearUnusedTemplateVariables(input);

            Assert.Equal(input, result);
        }

        [Fact]
        public void ClearUnusedTemplateVariables_EmptyString_ReturnsEmpty()
        {
            var result = _orchestrator.ClearUnusedTemplateVariables(string.Empty);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public async Task ReadPromptTemplate_FileNotFound_ThrowsFileNotFoundException()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                _orchestrator.ReadPromptTemplate("nonexistent/path.txt"));
        }
    }
}
