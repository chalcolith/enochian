using Enochian.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Enochian.Flow
{
    public abstract class FlowStep : Configurable
    {
        public FlowStep(IConfigurable parent, IFlowResources resources, Type inputType, Type outputType)
            : base(parent)
        {
            Resources = resources;
            InputType = inputType;
            OutputType = outputType;
        }

        public FlowStep(IConfigurable parent, IFlowResources resources, Type inputType, Type outputType, 
            FlowContainer container, FlowStep previous, IDictionary<string, object> config)
            : this(parent, resources, inputType, outputType)
        {
            Container = container;
            Previous = previous;

            if (config != null)
                Configure(config);
        }

        protected IFlowResources Resources { get; }

        public Type InputType { get; internal set; }
        public Type OutputType { get; internal set; }
        public FlowContainer Container { get; internal set; }
        public FlowStep Previous { get; internal set; }

        internal virtual IEnumerable<object> GetOutputs()
        {
            if (InputType == null)
            {
                AddError("Input type is not defined");
                yield break;
            }

            if (OutputType == null)
            {
                AddError("Output type is not defined");
                yield break;
            }

            if (Previous == null)
            {
                yield break;
            }

            foreach (var input in Previous.GetOutputs())
            {
                if (input == null) continue;
                if (!InputType.GetTypeInfo().IsAssignableFrom(input.GetType().GetTypeInfo()))
                {
                    AddError("Expected input of type {0}; found {1}", InputType.FullName, input.GetType().FullName);
                    yield break;
                }

                var output = Process(input);
                if (output != null)
                    yield return output;
            }
        }

        protected virtual object Process(object input)
        {
            throw new NotImplementedException();
        }
    }

    public abstract class FlowStep<TIn, TOut> : FlowStep
        where TIn : class
        where TOut : class
    {
        public FlowStep(IConfigurable parent, IFlowResources resources)
            : base(parent, resources, typeof(TIn), typeof(TOut))
        {
        }

        public FlowStep(IConfigurable parent, IFlowResources resources, FlowContainer container, FlowStep previous, IDictionary<string, object> config)
            : base(parent, resources, typeof(TIn), typeof(TOut), container, previous, config)
        {
        }

        internal IEnumerable<TOut> GetOutputsTyped()
        {
            return GetOutputs().OfType<TOut>();
        }

        protected override object Process(object input)
        {
            var inputTyped = input as TIn;
            if (inputTyped == null)
            {
                AddError("Input is not of type {0}", typeof(TIn).FullName);
                return null;
            }

            var outputTyped = ProcessTyped(inputTyped);
            return outputTyped;
        }

        protected virtual TOut ProcessTyped(TIn input)
        {
            throw new NotImplementedException();
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
