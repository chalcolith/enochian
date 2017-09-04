using Enochian.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Enochian.Flow
{
    public interface IFlowStep : IConfigurable
    {
        IFlowResources Resources { get; }
        FlowContainer Container { get; }
        Type InputType { get; }
        Type OutputType { get; }
    }

    public abstract class FlowStep : Configurable, IFlowStep
    {
        public FlowStep(IConfigurable parent, IFlowResources resources)
            : this(parent, resources, null, null)
        {
        }

        public FlowStep(IConfigurable parent, IFlowResources resources, FlowContainer container, IDictionary<string, object> config)
            : base(parent)
        {
            Resources = resources;
            Container = container;

            if (config != null)
                Configure(config);
        }

        public IFlowResources Resources { get; internal set; }
        public FlowContainer Container { get; internal set; }

        public virtual Type InputType => null;
        public virtual Type OutputType => null;
        internal virtual void SetPrevious(IFlowStep previous) { }
    }

    public interface IFlowStep<TOut> : IFlowStep
    {
        IEnumerable<TOut> GetOutputs();
    }

    public abstract class FlowStep<TIn, TOut> : FlowStep, IFlowStep<TOut>
    {
        public FlowStep(IConfigurable parent, IFlowResources resources)
            : this(parent, resources, null, null, null)
        {
        }

        public FlowStep(IConfigurable parent, IFlowResources resources, 
            FlowContainer container, IFlowStep<TIn> previous, IDictionary<string, object> config)
            : base(parent, resources, container, config)
        {
            Previous = previous;
        }

        public override Type InputType => typeof(TIn);
        public override Type OutputType => typeof(TOut);

        public IFlowStep<TIn> Previous { get; internal set; }

        internal override void SetPrevious(IFlowStep previous)
        {
            if (previous == null)
            {
                Previous = null;
            }
            else if ((Previous = previous as IFlowStep<TIn>) == null)
            {
                AddError("Cannot set Previous of {0} to {1}", GetType().Name, previous.GetType().Name);
            }
        }

        public virtual IEnumerable<TOut> GetOutputs()
        {
            if (Previous == null)
            {
                yield break;
            }

            foreach (var input in Previous.GetOutputs())
            {
                if (input == null) continue;

                var output = Process(input);
                if (output != null)
                    yield return output;
            }
        }

        protected virtual TOut Process(TIn input)
        {
            throw new NotImplementedException("FlowStep.Process must be implemented in subclasses");
        }
    }

    public abstract class TextFlowStep : FlowStep<TextChunk, TextChunk>
    {
        public TextFlowStep(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }
    }
}
