namespace Enochian.Flow;

public class FlowContainer(IConfigurable parent, IFlowResources resources, JsonObject? config) : FlowStep(parent, resources, null, config)
{
    private static readonly ILogger Logger = Logging.CreateLogger<FlowContainer>();

    private IList<FlowStep>? steps;

    public FlowContainer(IConfigurable parent, IFlowResources resources)
        : this(parent, resources, null)
    {
    }

    public override ILogger Log => Logger;

    public override IEnumerable<IConfigurable> Children => steps ??= [];

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        try
        {
            FlowStep? previous = null;
            steps = [];
            var children = config.GetChildren("steps", this);
            foreach (var child in children)
            {
                var typeName = child.Get<string>("type", this);
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    _ = AddError("empty step type name");
                    continue;
                }

                var stepType = Type.GetType(typeName, false) ?? Type.GetType("Enochian.Flow.Steps." + typeName, false);
                if (stepType == null)
                {
                    _ = AddError("unknown step type name '{0}'", typeName);
                    continue;
                }

                if (!typeof(FlowStep).IsAssignableFrom(stepType))
                {
                    _ = AddError("step type '{0}' is not a subtype of '{1}'", stepType.FullName, nameof(FlowStep));
                    continue;
                }

                var ctor = stepType.GetConstructor([typeof(IConfigurable), typeof(IFlowResources)]);
                if (ctor == null)
                {
                    _ = AddError("step type '{0}' does not contain a constructor with parameters of type '{1}' and '{2}'",
                        stepType.FullName, nameof(IConfigurable), nameof(IFlowResources));
                    continue;
                }

                if (ctor.Invoke([this, Resources]) is not FlowStep step)
                {
                    _ = AddError("unable to construct step type '{0}'", stepType.FullName);
                    continue;
                }

                step.Parent = this;
                step.Container = this;
                step.SetPrevious(previous);
                _ = step.Configure(child);

                steps.Add(step);
                previous = step;
            }
        }
        catch (Exception e)
        {
            _ = AddError("steps needs to be a list of step configs: {0}", e.Message);
        }

        return this;
    }

    public override IFlowStep GetFirstStep()
    {
        return this;
    }
}
