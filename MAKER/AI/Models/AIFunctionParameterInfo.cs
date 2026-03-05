namespace MAKER.AI.Models
{
    public class AIFunctionParameterInfo
    {
        public required string Name { get; init; }
        public required Type ParameterType { get; init; }
        public required string Description { get; init; }
        public bool IsRequired { get; init; }
    }
}
