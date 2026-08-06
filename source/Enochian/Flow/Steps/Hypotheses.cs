using Enochian.Text;

namespace Enochian.Flow.Steps;

public class HypothesisFile(IConfigurable parent, IFlowResources resources) : RelativeConfigurable(parent)
{
    private static readonly ILogger Logger = Logging.CreateLogger<HypothesisFile>();

    public override ILogger Log => Logger;

    public IFlowResources Resources { get; } = resources;
    public Encoding? Encoding { get; protected set; }

    public IList<HypothesisGroup> Groups { get; protected set; } = [];

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        var encoding = config.Get<string>("encoding", this);
        if (!string.IsNullOrWhiteSpace(encoding))
        {
            Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
            if (Encoding == null)
            {
                _ = AddError("invalid encoding id '{0}'", encoding);
            }
        }
        else
        {
            _ = AddError("no encoding specified");
        }

        Groups = [];
        var groups = config.GetChildren("hypotheses", this);
        if (groups != null)
        {
            try
            {
                foreach (var group in groups)
                {
                    Groups.Add(new HypothesisGroup(this, group));
                }
            }
            catch (Exception e)
            {
                _ = AddError("hypotheses needs to be a list of hypothesis groups: {0}", e.Message);
            }
        }

        return this;
    }
}

public class HypothesisGroup : Configurable
{
    private static readonly ILogger Logger = Logging.CreateLogger<HypothesisGroup>();

    public HypothesisGroup(HypothesisFile parent, JsonObject group)
        : base(parent)
    {
        SourceFile = parent;
        _ = Configure(group);
    }

    public HypothesisFile SourceFile { get; }
    public IList<HypothesisEntry> Entries { get; protected set; } = [];

    public override ILogger Log => Logger;

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        Entries = [];
        var entries = config.GetChildren("entries", this);
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                var inputs = entry["input"] switch
                {
                    JsonValue input => [input.GetValue<string>()],
                    JsonArray inputArray => inputArray
                        .Select(input => input?.GetValue<string>())
                        .Where(input => input != null)
                        .Cast<string>(),
                    _ => [],
                };

                foreach (var input in inputs)
                {
                    Entries.Add(new HypothesisEntry
                    {
                        SourceGroup = this,
                        Input = input,
                        Lemma = entry.Get<string>("lemma", this) ?? string.Empty,
                        Definition = entry.Get<string>("definition", this) ?? string.Empty,
                    });
                }
            }
        }

        return this;
    }
}

public class HypothesisEntry
{
    public HypothesisGroup? SourceGroup { get; set; }
    public string Input { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;

    public Encoding? Encoding => SourceGroup?.SourceFile.Encoding;
}
