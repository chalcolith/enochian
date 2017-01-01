using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Enochian.Flow
{
    public class FlowContainer : FlowStep
    {
        public IList<FlowStep> Steps { get; } = new List<FlowStep>();

        public FlowContainer(FlowContainer parent = null, FlowStep previous = null)
            : base(parent, previous)
        {
        }

        public IEnumerable<object> GetAllResults()
        {
            if (Steps == null) yield break;

            if (Steps.Any())
            {
                foreach (var output in Steps.Last().GetResults())
                    yield return output;
            }
        }

        protected override bool Process(object input, out object output)
        {
            output = null;
            return false;            
        }
    }
}
