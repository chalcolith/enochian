using System.Collections.Generic;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class Transducer : TextFlowStep
    {
        Encoder encoder;
        Encoder Encoder => encoder ?? (encoder = new Encoder(Features, Encoding));

        public Transducer(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public FeatureSet Features { get; private set; }

        public Encoding Encoding { get; private set; }

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

        protected override TextChunk ProcessTyped(TextChunk input)
        {
            var inputLines = input.Lines.Where(line => line.SourceStep == Previous);
            var outputLines = inputLines.Select(line => new TextLine
            {
                SourceStep = this,
                Segments = line.Segments.Select(seg =>
                {
                    var newSeg = Encoder.ProcessSegment(seg);
                    newSeg.SourceSegments = new[] { seg };
                    return newSeg;
                }).ToList(),
            });
            var output = new TextChunk
            {
                Lines = input.Lines.Concat(outputLines).ToList(),
            };
            return output;
        }
    }
}
