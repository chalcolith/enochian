using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Enochian.Flow
{
    public abstract class FlowContainer : FlowStep
    {
        IList<FlowStep> steps;

        public FlowContainer(Type inputType, Type outputType, FlowContainer parent, FlowStep previous, dynamic config)
            : base(inputType, outputType, parent, previous, (object)config)
        {
        }

        protected IList<FlowStep> Steps
        {
            get { return steps ?? (steps = new List<FlowStep>()); }
        }

        internal override IEnumerable<object> GetOutputs()
        {
            if (steps == null || steps.Count == 0)
                yield break;

            foreach (var output in steps.Last().GetOutputs())
            {
                if (output != null)
                    yield return output;
            }
        }
    }
}
