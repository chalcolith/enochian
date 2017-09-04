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

        public IList<TextChunk> Chunks { get; set; }

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

            Chunks = new List<TextChunk>();

            string path = config.Get<string>("path", this);
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    var sourcePath = GetChildPath(AbsoluteFilePath, path);
                    Log.Info("reading {0}", sourcePath);

                    TextChunk currentChunk = null;
                    bool needNewChunk = true;

                    using (var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
                    using (var sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                needNewChunk = true;
                                continue;
                            }

                            if (needNewChunk)
                            {
                                Chunks.Add(currentChunk = new TextChunk { Lines = new List<TextLine>() });
                                needNewChunk = false;
                            }

                            currentChunk.Lines.Add(GetInterline(line));
                        }
                    }
                    Log.Info("read {0} chunks; {1} lines", Chunks.Count, Chunks.Sum(chunk => chunk.Lines.Count));
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
                {
                    Chunks.Add(new TextChunk
                    {
                        Lines = new[] { GetInterline(text) }
                    });
                }
                else
                {
                    AddError("no sample text specified");
                }
            }

            return this;
        }

        static readonly Regex WORD = new Regex(@"\w+", RegexOptions.Compiled);

        TextLine GetInterline(string text)
        {
            var segs = new List<TextSegment>();

            if (!string.IsNullOrWhiteSpace(text))
            {
                var match = WORD.Match(text);
                while (match.Success)
                {
                    segs.Add(new TextSegment
                    {
                        Options = new [] { new SegmentOption { Text = match.Value } }
                    });
                    match = match.NextMatch();
                }
            }

            return new TextLine
            {
                SourceStep = this,
                Text = text,
                Segments = segs,
            };
        }

        internal override IEnumerable<object> GetOutputs()
        {
            return Chunks;
        }
    }
}
