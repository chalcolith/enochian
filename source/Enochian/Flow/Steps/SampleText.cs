using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class SampleText : FlowStep<TextChunk, TextChunk>
    {
        public SampleText(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public FeatureSet Features { get; private set; }

        public IList<Interline> Chunks { get; set; }

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
                AddError("no resources specified");
            }

            Chunks = new List<Interline>();

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
                            Chunks.Add(GetInterline(line));
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
                    Chunks.Add(GetInterline(text));
                else
                    AddError("no sample text specified");
            }

            return this;
        }

        static readonly Regex WORD = new Regex(@"\w+", RegexOptions.Compiled);

        Interline GetInterline(string text)
        {            
            var segs = new List<Segment>();

            if (!string.IsNullOrWhiteSpace(text))
            {
                var match = WORD.Match(text);
                while (match.Success)
                {
                    segs.Add(new Segment { Text = match.Value });
                    match = match.NextMatch();
                }
            }

            return new Interline
            {
                Text = text,
                Encoding = Encoding.Default,
                Segments = segs,
            };
        }

        internal override IEnumerable<object> GetOutputs()
        {
            if (Chunks == null)
                yield break;

            foreach (var chunk in Chunks)
            {
                if (chunk == null)
                    continue;

                yield return new TextChunk
                {
                    Lines = new[]
                    {
                        new Interline
                        {
                            SourceStep = this,
                            Text = chunk.Text,
                            Encoding = Encoding.Default,
                            Segments = chunk.Segments,
                        }
                    }
                };
            }
        }
    }
}
