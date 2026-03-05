using MAKER.AI.Orchestrators;

namespace MAKER.Tests.TestDoubles
{
    internal sealed partial class TestableOrchestrator : OrchestratorBase
    {
        public new string ClearUnusedTemplateVariables(string prompt)
            => base.ClearUnusedTemplateVariables(prompt);

        public new Task<string> ReadPromptTemplate(string path, CancellationToken cancellationToken = default)
            => base.ReadPromptTemplate(path, cancellationToken);
    }
}
