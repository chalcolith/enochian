using Enochian.Lexicons;
using Enochian.Text;

namespace Enochian.Flow;

public interface IFlowResources
{
    IList<FeatureSet> FeatureSets { get; }
    IList<Encoding> Encodings { get; }
    IList<Lexicon> Lexicons { get; }
}

public class Flow : Configurable, IFlowResources
{
    private static readonly ILogger Logger = Logging.CreateLogger<Flow>();
    private IEnumerable<IConfigurable>? children;

    public Flow(string fname)
        : base(null)
    {
        _ = Load(fname, this);
    }

    public override ILogger Log => Logger;

    public override IEnumerable<IConfigurable> Children
    {
        get
        {
            return children ??= [.. FeatureSets
                .Concat<IConfigurable>(Encodings)
                .Concat(Steps != null ? [Steps] : [])];
        }
    }

    public IList<FeatureSet> FeatureSets { get; } = [];
    public IList<Encoding> Encodings { get; } = [];
    public IList<Lexicon> Lexicons { get; } = [];

    public FlowContainer? Steps { get; private set; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        ConfigureFeatures(config);
        ConfigureEncodings(config);
        ConfigureLexicons(config);

        Steps = new FlowContainer(this, this, config)
        {
            Id = "steps"
        };

        PostConfigureChildren(this);
        PostConfigure();
        return this;
    }

    private void ConfigureFeatures(JsonObject config)
    {
        var features = config.GetChildren("features", this);
        if (features != null)
        {
            foreach (var fset in features)
            {
                var featureSet = new FeatureSet(this)
                {
                    Id = fset.Get<string>("id", this),
                    RelativePath = fset.Get<string>("path", this),
                };

                if (!string.IsNullOrWhiteSpace(featureSet.RelativePath))
                {
                    FeatureSets.Add(Load(this, featureSet, featureSet.RelativePath));
                }
                else
                {
                    _ = AddError("feature set '{0}' has no path", featureSet.Id);
                }
            }
        }
    }

    private void ConfigureEncodings(JsonObject config)
    {
        var encodings = config.GetChildren("encodings", this);
        if (encodings != null)
        {
            foreach (var enc in encodings)
            {
                var featuresName = enc.Get<string>("features", this);
                var encoding = new Encoding(this)
                {
                    Id = enc.Get<string>("id", this),
                    RelativePath = enc.Get<string>("path", this),
                    Features = FeatureSets.FirstOrDefault(fs => fs.Id == featuresName),
                };

                if (encoding.Features == null)
                {
                    _ = AddError("unknown feature set '{0}' encoding '{1}'", featuresName, encoding.Id);
                }

                if (!string.IsNullOrWhiteSpace(encoding.RelativePath))
                {
                    Encodings.Add(Load(this, encoding, encoding.RelativePath));
                }
                else
                {
                    _ = AddError("encoding '{0}' has no path", encoding.Id);
                }
            }
        }

        if (!Encodings.Any(e => e.Id == Encoding.Default.Id))
        {
            Encodings.Add(Encoding.Default);
        }
    }

    private void ConfigureLexicons(JsonObject config)
    {
        var lexicons = config.GetChildren("lexicons", this);
        if (lexicons != null)
        {
            foreach (var lex in lexicons)
            {
                var id = lex.Get<string>("id", this);

                var typeName = lex.Get<string>("type", this);
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    _ = AddError("no type name for lexicon '{0}'", id ?? "?");
                    continue;
                }

                var lexType = Type.GetType(typeName, false) ?? Type.GetType("Enochian.Lexicons." + typeName, false);
                if (lexType == null)
                {
                    _ = AddError("unable to find lexicon type '{0}'", typeName);
                    continue;
                }

                if (!typeof(Lexicon).IsAssignableFrom(lexType))
                {
                    _ = AddError("type '{0}' is not a subtype of Enochian.Lexicons.Lexicon", lexType.FullName);
                    continue;
                }

                var ctor = lexType.GetConstructor([typeof(IConfigurable), typeof(IFlowResources)]);
                if (ctor == null)
                {
                    _ = AddError("type '{0}' does not have a constructor with parameters of type IConfigurable and IFlowResources");
                    continue;
                }

                if (ctor.Invoke([this, this]) is not Lexicon child)
                {
                    _ = AddError("unable to construct lexicon type '{0}'", lexType.FullName);
                    continue;
                }

                child.Parent = this;
                _ = child.Configure(lex);

                Lexicons.Add(child);
            }
        }
    }

    public override void PostConfigure()
    {
    }

    private static void PostConfigureChildren(IConfigurable obj)
    {
        foreach (var child in obj.Children)
        {
            PostConfigureChildren(child);
        }
        obj.PostConfigure();
    }

    public IEnumerable<object> GetOutputs()
    {
        var lastStep = Steps?.Children.LastOrDefault();
        if (lastStep == null)
        {
            yield break;
        }

        var getOutputs = lastStep.GetType().GetMethod(nameof(FlowStep<,>.GetOutputs));
        if (getOutputs == null)
        {
            yield break;
        }

        if (getOutputs.Invoke(lastStep, null) is not System.Collections.IEnumerable enumerable)
        {
            yield break;
        }

        foreach (var output in enumerable)
        {
            yield return output;
        }
    }

    public void ProcessAll()
    {
        object lastOutput;
        foreach (var output in GetOutputs())
        {
            lastOutput = output;
        }
    }
}
