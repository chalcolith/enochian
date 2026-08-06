using Enochian.Text;

namespace Enochian.Flow.Steps;

public class Transducer(IConfigurable parent, IFlowResources resources) : TextFlowStep(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<Transducer>();

    public override ILogger Log => Logger;

    public FeatureSet? Features { get; private set; }
    public Encoding? Encoding { get; private set; }
    private Encoder? encoder;

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        if (Resources != null)
        {
            var features = config.Get<string>("features", this);
            Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
            if (Features == null)
            {
                _ = AddError("invalid features name '{0}'", features);
            }

            var outputEncoding = config.Get<string>("encoding", this);
            Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == outputEncoding);
            if (Encoding == null)
            {
                _ = AddError("invalid encoding name '{0}'", outputEncoding);
            }

            if (Features != null && Encoding != null)
            {
                encoder = new Encoder(Features, Encoding);
            }
        }
        else
        {
            _ = AddError("no resources specified");
        }

        return this;
    }

    public override string GenerateReport(ReportType reportType)
    {
        return string.Format(CultureInfo.InvariantCulture, "&nbsp;&nbsp;Encoding: {0}: {1}<br/>&nbsp;&nbsp;Path: {2}", Encoding?.Id, Encoding?.Description, Encoding?.AbsoluteFilePath);
    }

    protected override TextChunk Process(TextChunk input)
    {
        if (encoder == null)
        {
            _ = AddError("transducer is not configured with features and encoding");
            return input;
        }

        var outputLines = input.Lines
            .Where(srcLine => ReferenceEquals(srcLine.SourceStep, Previous))
            .Select(srcLine => new TextLine
            {
                SourceStep = this,
                SourceLine = srcLine,
                Text = srcLine.Text,
                Segments = [.. srcLine.Segments.Select(seg => encoder.ProcessSegment(seg))],
            });
        var output = new TextChunk
        {
            Lines = [.. input.Lines, .. outputLines],
        };
        return output;
    }
}
