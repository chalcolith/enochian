using Enochian.Text;

namespace Enochian.Flow;

public enum ReportType
{
    Text,
    Html,
};

public interface IFlowStep : IConfigurable
{
    IFlowResources Resources { get; }
    FlowContainer? Container { get; }
    Type? InputType { get; }
    Type? OutputType { get; }
    IFlowStep GetFirstStep();
    string? GenerateReport(ReportType reportType);
}

public abstract class FlowStep : Configurable, IFlowStep
{
    public FlowStep(IConfigurable parent, IFlowResources resources)
        : this(parent, resources, null, null)
    {
    }

    public FlowStep(IConfigurable parent, IFlowResources resources, FlowContainer? container, JsonObject? config)
        : base(parent)
    {
        Resources = resources;
        Container = container;

        if (config != null)
        {
            _ = Configure(config);
        }
    }

    public IFlowResources Resources { get; internal set; }
    public FlowContainer? Container { get; internal set; }

    public virtual Type? InputType => null;
    public virtual Type? OutputType => null;
    internal virtual void SetPrevious(IFlowStep? previous) { }
    public abstract IFlowStep GetFirstStep();

    public virtual string? GenerateReport(ReportType reportType)
    {
        return null;
    }
}

public interface IFlowStep<TOut> : IFlowStep
{
    IEnumerable<TOut> GetOutputs();
}

public abstract class FlowStep<TIn, TOut>(IConfigurable parent, IFlowResources resources,
    FlowContainer? container, IFlowStep<TIn>? previous, JsonObject? config) : FlowStep(parent, resources, container, config), IFlowStep<TOut>
{
    public FlowStep(IConfigurable parent, IFlowResources resources)
        : this(parent, resources, null, null, null)
    {
    }

    public override Type InputType => typeof(TIn);
    public override Type OutputType => typeof(TOut);

    public IFlowStep<TIn>? Previous { get; internal set; } = previous;

    internal override void SetPrevious(IFlowStep? previous)
    {
        if (previous == null)
        {
            Previous = null;
        }
        else if ((Previous = previous as IFlowStep<TIn>) == null)
        {
            _ = AddError("Cannot set Previous of {0} to {1}", GetType().Name, previous.GetType().Name);
        }
    }

    public override IFlowStep GetFirstStep()
    {
        IFlowStep step = this;
        if (Previous != null)
        {
            step = Previous.GetFirstStep();
        }

        return step;
    }

    public virtual IEnumerable<TOut> GetOutputs()
    {
        if (Previous == null)
        {
            yield break;
        }

        foreach (var input in Previous.GetOutputs())
        {
            if (input == null)
            {
                continue;
            }

            var output = Process(input);
            if (output != null)
            {
                yield return output;
            }
        }
    }

    protected virtual TOut Process(TIn input)
    {
        throw new NotImplementedException("FlowStep.Process must be implemented in subclasses");
    }
}

public abstract class TextFlowStep(IConfigurable parent, IFlowResources resources) : FlowStep<TextChunk, TextChunk>(parent, resources)
{
}
