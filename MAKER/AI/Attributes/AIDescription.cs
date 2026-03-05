namespace MAKER.AI.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, Inherited = false)]
    public sealed class AIDescription : Attribute
    {
        public string Description { get; init; }

        public AIDescription(string description)
        {
            Description = description;
        }
    }
}
