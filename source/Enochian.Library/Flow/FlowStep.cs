using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Enochian.Flow
{
    public abstract class FlowStep
    {
        public Type InputItemType { get; set; }
        public Type OutputItemType { get; set; }

        public FlowContainer Parent { get; }
        protected FlowStep Previous { get; }

        public FlowStep(FlowContainer parent, FlowStep previous)
        {
            Parent = parent;
            Previous = previous;
        }

        internal IEnumerable<object> GetResults()
        {
            if (Previous == null)
                yield break;

            object output;
            foreach (var input in Previous.GetResults())
            {
                if (Process(input, out output))
                    yield return output;
            }
        }

        protected abstract bool Process(object input, out object output);
    }

    public abstract class FlowStep<TIn, TOut> : FlowStep
    {
        public FlowStep(FlowContainer parent, FlowStep previous)
            : base(parent, previous)
        {
            InputItemType = typeof(TIn);
            OutputItemType = typeof(TOut);
        }

        public IEnumerable<TOut> GetTypedResults()
        {
            return GetResults().Cast<TOut>();
        }
    }
}
