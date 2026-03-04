namespace MAKER.AI.Models
{
    public class Step
    {
        // A description of the step to be performed.
        public required string Task { get; set; }

        // A list of indices representing steps that must be completed before this step can be executed.
        public List<int> RequiredSteps { get; set; } = [];

        public string ExtraContext { get; set; } = string.Empty;

        // Whether the execution of the step requires information about the output format.
        public bool RequiresFormat { get; set; }
    }
}
