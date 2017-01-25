using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class SampleText : FlowStep<TextChunk, TextChunk>
    {
        public SampleText(IFlowResources resources)
            : base(resources)
        {
        }

        public FeatureSet Features { get; private set; }

        public IList<string> Tokens { get; set; }

        public static readonly char[] WHITESPACE = new[] { ' ', '\t', '\n', '\r' };

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Name == features);
                if (Features == null)
                    AddError("invalid features name '{0}'", features);
            }
            else
            {
                AddError("no resources specified for SampleText");
            }

            string text = config.Get<string>("text", this);
            if (!string.IsNullOrWhiteSpace(text))
                Tokens = text.Split(WHITESPACE, StringSplitOptions.RemoveEmptyEntries);

            return this;
        }

        internal override IEnumerable<object> GetOutputs()
        {
            if (Tokens == null)
                yield break;

            foreach (var token in Tokens)
            {
                yield return new TextChunk
                {
                    Lines = new[]
                    {
                        new Interline
                        {
                            Encoding = Encoding.Default,
                            Segments = new[] { new Segment { Text = token } },
                        },
                    },
                };
            }
        }
    }
}
