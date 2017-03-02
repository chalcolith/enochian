using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Text;
using System.IO;

namespace Enochian.Flow.Steps
{
    public class SampleText : FlowStep<TextChunk, TextChunk>
    {
        public SampleText(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public FeatureSet Features { get; private set; }

        public IList<IList<string>> Lines { get; set; }

        public static readonly char[] WHITESPACE = new[] { ' ', '\t', '\n', '\r' };

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
                if (Features == null)
                    AddError("invalid features name '{0}'", features);
            }
            else
            {
                AddError("no resources specified for SampleText");
            }

            Lines = new List<IList<string>>();

            string path = config.Get<string>("path", this);
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    var configPath = GetChildPath(AbsoluteFilePath, path);
                    using (var fs = new FileStream(configPath, FileMode.Open, FileAccess.Read))
                    using (var sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            Lines.Add(line.Split(WHITESPACE, StringSplitOptions.RemoveEmptyEntries));
                        }
                    }
                }
                catch (Exception e)
                {
                    AddError(e.Message);
                }
            }
            else
            {
                string text = config.Get<string>("text", this);
                if (!string.IsNullOrWhiteSpace(text))
                    Lines.Add(text.Split(WHITESPACE, StringSplitOptions.RemoveEmptyEntries));
            }

            return this;
        }

        internal override IEnumerable<object> GetOutputs()
        {
            if (Lines == null)
                yield break;

            foreach (var line in Lines)
            {
                if (line == null)
                    continue;

                var chunk = new TextChunk
                {
                    Lines = new[]
                    {
                        new Interline
                        {
                            Encoding = Encoding.Default,
                            Segments = line.Select(token => new Segment { Text = token }).ToArray(),
                        }
                    }
                };
                yield return chunk;
            }
        }
    }
}
