using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class SampleText : TextFlowStep
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        IList<TextChunk> chunks;

        public SampleText(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

        public FeatureSet Features { get; private set; }

        public string SourcePath { get; private set; }
        public IList<TextChunk> Chunks { set => chunks = value; }

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

            string path = config.Get<string>("path", this);
            if (!string.IsNullOrWhiteSpace(path))
            {
                SourcePath = path;
            }
            else
            {
                string text = config.Get<string>("text", this);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    chunks = new List<TextChunk>();
                    chunks.Add(new TextChunk
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

        public override IEnumerable<TextChunk> GetOutputs()
        {
            if (chunks != null)
            {
                foreach (var chunk in chunks)
                    yield return chunk;
            }
            else if (!string.IsNullOrWhiteSpace(SourcePath))
            {
                FileStream fs = null;
                StreamReader sr = null;
                int numChunks = 0;
                int numLines = 0;
                try
                {
                    var sourcePath = GetChildPath(AbsoluteFilePath, SourcePath);
                    Log.Info("reading {0}", sourcePath);

                    try
                    {
                        fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                        sr = new StreamReader(fs);
                    }
                    catch (Exception e)
                    {
                        AddError(e.Message);
                    }

                    if (fs != null && sr != null)
                    {
                        TextChunk currentChunk = null;
                        bool needNewChunk = true;

                        string line;
                        while (true)
                        {
                            try
                            {
                                line = sr.ReadLine();
                            }
                            catch (Exception e)
                            {
                                AddError(e.Message);
                                break;
                            }

                            if (line == null)
                                break;

                            if (string.IsNullOrWhiteSpace(line))
                            {
                                needNewChunk = true;
                                continue;
                            }

                            if (needNewChunk)
                            {
                                if (currentChunk != null)
                                    yield return currentChunk;

                                currentChunk = new TextChunk { Lines = new List<TextLine>() };
                                numChunks++;
                                needNewChunk = false;
                            }

                            currentChunk.Lines.Add(GetInterline(line));
                            numLines++;
                        }

                        if (currentChunk != null)
                            yield return currentChunk;
                    }
                }
                finally
                {
                    if (sr != null) sr.Dispose();
                    if (fs != null) fs.Dispose();
                }

                Log.Info("read {0} chunks; {1} lines", numChunks, numLines);
            }
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
                        Options = new[] { new SegmentOption { Text = match.Value } }
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
    }
}
