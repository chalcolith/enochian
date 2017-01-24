using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Enochian.Flow
{
    public class FlowContainer : FlowStep
    {
        IList<FlowStep> steps;

        public FlowContainer(IFlowResources resources, Type inputType, Type outputType, 
            FlowContainer parent, FlowStep previous, IDictionary<string, object> config)
            : base(resources, inputType, outputType, parent, previous, null)
        {
        }

        public FlowContainer(IFlowResources resources, IDictionary<string, object> config)
            : this(resources, null, null, null, null, config)
        {
        }

        protected IList<FlowStep> Steps
        {
            get { return steps ?? (steps = new List<FlowStep>()); }
        }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            try
            {
                FlowStep previous = null;
                var steps = config.GetChildren("Steps", this);
                foreach (var stepConfig in steps)
                {
                    string typeName = stepConfig.Get<string>("Type", this);
                    if (string.IsNullOrWhiteSpace(typeName))
                    {
                        AddError("empty step type name");
                        continue;
                    }

                    Type stepType = Type.GetType(typeName, false);
                    if (stepType == null)
                        stepType = Type.GetType("Enochian.Flow.Steps." + typeName, false);
                    if (stepType == null)
                    {
                        AddError("unknown step type name '{0}'", typeName);
                        continue;
                    }

                    if (!typeof(FlowStep).GetTypeInfo().IsAssignableFrom(stepType))
                    {
                        AddError("step type '{0}' is not a subtype of '{1}'", stepType.FullName, nameof(FlowStep));
                        continue;
                    }

                    var ctor = stepType.GetTypeInfo().GetConstructor(new[] { typeof(IFlowResources) });
                    if (ctor == null)
                    {
                        AddError("step type '{0}' does not contain a constructor with a single parameter of type '{1}'",
                            stepType.FullName, nameof(IFlowResources));
                        continue;
                    }

                    var step = ctor.Invoke(new object[] { Resources }) as FlowStep;
                    step.Container = this;
                    step.Previous = previous;

                    string inputTypeName = stepConfig.Get<string>("InputType", this);
                    if (!string.IsNullOrWhiteSpace(inputTypeName))
                    {
                        var inputType = Type.GetType(inputTypeName, false);
                        if (inputType != null)
                            step.InputType = inputType;
                        else
                            AddError("unknown inputType name '{0}'", inputTypeName);
                    }

                    string outputTypeName = stepConfig.Get<string>("OutputType", this);
                    if (!string.IsNullOrWhiteSpace(outputTypeName))
                    {
                        var outputType = Type.GetType(outputTypeName, false);
                        if (outputType != null)
                            step.OutputType = outputType;
                        else
                            AddError("unknown outputType name '{0}'", outputTypeName);
                    }

                    Steps.Add(step);
                    previous = step;
                }
            }
            catch (Exception e)
            {
                AddError("steps needs to be a list of step configs: {0}", e.Message);
            }

            return this;
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
