namespace MAKER.AI.Models
{
    public class Step
    {
        public required string Task { get; set; }

        public List<int> RequiredSteps { get; set; } = [];

        /// <summary>
        /// Additional context to be included in the execution prompt for this step.
        /// </summary>
        public string ExtraContext { get; set; } = string.Empty;

        public bool RequiresFormat { get; set; }
    }
}
