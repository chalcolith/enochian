using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Enochian.Flow
{
    public class FlowContainer : FlowStep
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        IList<FlowStep> steps;

        public FlowContainer(IConfigurable parent, IFlowResources resources)
            : this(parent, resources, null)
        {
        }

        public FlowContainer(IConfigurable parent, IFlowResources resources, IDictionary<string, object> config)
            : base(parent, resources, null, config)
        {
        }

        public override NLog.Logger Log => logger;

        public override IEnumerable<IConfigurable> Children => steps ?? (steps = new List<FlowStep>());

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            try
            {
                FlowStep previous = null;
                steps = new List<FlowStep>();
                var children = config.GetChildren("steps", this);
                foreach (var child in children)
                {
                    string typeName = child.Get<string>("type", this);
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

                    if (!typeof(FlowStep).IsAssignableFrom(stepType))
                    {
                        AddError("step type '{0}' is not a subtype of '{1}'", stepType.FullName, nameof(FlowStep));
                        continue;
                    }

                    var ctor = stepType.GetConstructor(new[] { typeof(IConfigurable), typeof(IFlowResources) });
                    if (ctor == null)
                    {
                        AddError("step type '{0}' does not contain a constructor with parameters of type '{1}' and '{2}'",
                            stepType.FullName, nameof(IConfigurable), nameof(IFlowResources));
                        continue;
                    }

                    var step = ctor.Invoke(new object[] { this, Resources }) as FlowStep;
                    step.Parent = this;
                    step.Container = this;
                    step.SetPrevious(previous);
                    step.Configure(child);

                    steps.Add(step);
                    previous = step;
                }
            }
            catch (Exception e)
            {
                AddError("steps needs to be a list of step configs: {0}", e.Message);
            }

            return this;
        }

        public override IFlowStep GetFirstStep()
        {
            return this;
        }
    }
}
