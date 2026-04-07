using System.Reflection;

namespace MAKER.AI.Models
{
    public class AIFunctionInfo
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public List<AIFunctionParameterInfo> Parameters { get; init; } = [];
        public required MethodInfo Info { get; init; }
        public required object Target { get; init; }
    }
}
