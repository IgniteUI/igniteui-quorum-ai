namespace MAKER.AI.Models
{
    public class AIFunctionInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public List<AIFunctionParameterInfo> Parameters { get; init; } = [];
    }
}
