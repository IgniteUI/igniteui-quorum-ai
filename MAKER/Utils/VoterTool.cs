using MAKER.AI.Attributes;
using System.ComponentModel;

namespace MAKER.Utils
{
    public class VoterTool
    {
        private readonly string _format;

        public VoterTool(string format) => this._format = format;

        [Description("Gets the required output format.")]
        [AIDescription("Gets the required output format.")]
        public string GetFormat() => _format;
    }
}
