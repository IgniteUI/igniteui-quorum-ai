using MAKER.AI.Attributes;

namespace MAKER.Utils
{
    public class VoterTool(string format)
    {
        [AIDescription("Gets the required output format for the task. Verify format when unsure.")]
        public string GetFormat() => format;
    }
}
