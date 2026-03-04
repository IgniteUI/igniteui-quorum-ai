using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MAKER.AI
{
    [AttributeUsage(AttributeTargets.Method|AttributeTargets.Parameter, Inherited = false)]
    public class AIDescription : Attribute
    {
        public string Description { get; init; }

        public AIDescription(string description)
        {
            this.Description = description;
        }
    }
}
