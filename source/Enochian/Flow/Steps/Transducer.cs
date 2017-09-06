using System.Collections.Generic;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class Transducer : TextFlowStep
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        Encoder encoder;

        public Transducer(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

        public FeatureSet Features { get; private set; }
        public Encoding Encoding { get; private set; }

        Encoder Encoder => encoder ?? (encoder = new Encoder(Features, Encoding));

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
                if (Features == null)
                    AddError("invalid features name '{0}'", features);

                var outputEncoding = config.Get<string>("encoding", this);
                Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == outputEncoding);
                if (Encoding == null)
                    AddError("invalid encoding name '{0}'", outputEncoding);
            }
            else
            {
                AddError("no resources specified");
            }

            return this;
        }

        public override string GenerateReport(ReportType reportType)
        {
            return string.Format("Encoding: {0}: {1}", Encoder.Encoding?.Id, Encoder.Encoding?.Description);
        }

        protected override TextChunk Process(TextChunk input)
        {
            var inputLines = input.Lines;
            var outputLines = input.Lines
                .Where(srcLine => object.ReferenceEquals(srcLine.SourceStep, Previous))
                .Select(srcLine => new TextLine
                {
                    SourceStep = this,
                    SourceLine = srcLine,
                    Text = srcLine.Text,
                    Segments = srcLine.Segments
                        .Select(seg => Encoder.ProcessSegment(seg))
                        .ToList(),
                });
            var output = new TextChunk
            {
                Lines = input.Lines.Concat(outputLines).ToList(),
            };
            return output;
        }
    }
}
