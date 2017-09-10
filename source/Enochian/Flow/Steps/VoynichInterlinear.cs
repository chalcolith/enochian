using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class VoynichInterlinear : TextFlowStep
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        IList<TextChunk> chunks;

        public VoynichInterlinear(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

        public Encoding Encoding { get; private set; }
        Encoder Encoder { get; set; }

        public string SourcePath { get; private set; }

        public IList<string> Locuses { get; private set; }

        public IList<TextChunk> Chunks { set => chunks = value; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var encoding = config.Get<string>("encoding", this);
                Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
                if (Encoding != null)
                {
                    Encoder = new Encoder(Encoding.Features, Encoding);
                }
                else
                {
                    AddError("invalid encoding name '{0}'", encoding);
                }
            }
            else
            {
                AddError("no encoding specified");
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
                    chunks = new List<TextChunk>
                    {
                        new TextChunk
                        {                            
                            Lines = new[] { GetInterline(text) }
                        }
                    };
                }
                else
                {
                    AddError("no sample text specified");
                }
            }

            var locuses = config.Get<IEnumerable<string>>("locuses", this);
            Locuses = locuses?.ToList();

            return this;
        }

        public override string GenerateReport(ReportType reportType)
        {
            return string.Format("&nbsp;&nbsp;Path: {0}<br/>&nbsp;&nbsp;Encoding: {1}: {2}<br/>&nbsp;&nbsp;Path: {3}",
                GetChildPath(AbsoluteFilePath, SourcePath),
                Encoding.Id, Encoding.Description, Encoding.AbsoluteFilePath);
        }

        static readonly Regex LineRegex = new Regex(@"^\s*(<[^>]+>)\s+(.*)[-=]", RegexOptions.Compiled);
        static readonly Regex ExtComment = new Regex(@"\*{&(\d+)}", RegexOptions.Compiled);
        static readonly Regex ReplComment = new Regex(@"{&[^}]+}", RegexOptions.Compiled);
        static readonly char[] Punctuation = new[] { '.', '\'', '-', '=', '?', '%' };

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
                int numLines = 0;
                int numChunks = 0;
                chunks = new List<TextChunk>();
                var chunksPerLocus = new Dictionary<string, TextChunk>();

                try
                {
                    var sourcePath = GetChildPath(AbsoluteFilePath, SourcePath);
                    Log.Info("reading {0}", sourcePath);

                    try
                    {
                        fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                        sr = new StreamReader(fs, System.Text.Encoding.GetEncoding("ISO-8859-1"));
                    }
                    catch (Exception e)
                    {
                        AddError(e.Message);
                    }

                    if (fs != null && sr != null)
                    {
                        string line;
                        while (true)
                        {
                            try
                            {
                                line = sr.ReadLine();
                                numLines++;
                            }
                            catch (Exception e)
                            {
                                AddError(e.Message);
                                break;
                            }

                            if (line == null)
                                break;

                            if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                                continue;

                            var lineMatch = LineRegex.Match(line);
                            if (lineMatch.Success)
                            {
                                var locus = lineMatch.Groups[1].Value;
                                if (Locuses != null && !Locuses.Contains(locus))
                                    continue;

                                var text = lineMatch.Groups[2].Value;

                                var extendedMatch = ExtComment.Match(text);
                                while (extendedMatch.Success)
                                {
                                    if (int.TryParse(extendedMatch.Groups[1].Value, out int code))
                                    {
                                        var repl = new string((char)code, 1);
                                        text = text.Substring(0, extendedMatch.Index)
                                            + repl 
                                            + text.Substring(extendedMatch.Index + extendedMatch.Length);
                                    }
                                    extendedMatch = extendedMatch.NextMatch();
                                }

                                var replMatch = ReplComment.Match(text);
                                while (replMatch.Success)
                                {
                                    text = text.Substring(0, replMatch.Index)
                                        + replMatch.Groups[1].Value
                                        + text.Substring(replMatch.Index + replMatch.Length);
                                    replMatch = replMatch.NextMatch();
                                }

                                var chunk = new TextChunk
                                {
                                    Description = locus,
                                    Lines = new [] { GetInterline(text) }
                                };
                                chunks.Add(chunk);
                                numChunks++;

                                yield return chunk;

                                if (Locuses != null && Locuses.Any())
                                {
                                    chunksPerLocus[locus] = chunk;
                                    if (chunksPerLocus.Count == Locuses.Count)
                                        break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    if (sr != null) sr.Dispose();
                    if (fs != null) fs.Dispose();
                    Log.Info("read {0} lines, {1} chunks", numLines, numChunks);
                }
            }
        }

        TextLine GetInterline(string line)
        {
            var tokens = line.Split(Punctuation, StringSplitOptions.RemoveEmptyEntries);

            return new TextLine
            {
                SourceStep = this,
                Text = line,
                Segments = tokens
                    .Select(token =>
                    {
                        string option = token;
                        string repr = "";
                        IList<double[]> phones = null;
                        if (Encoder != null)
                        {
                            (option, repr, phones) = Encoder.GetTextAndPhones(token);
                        }
                        var options = new List<SegmentOption>
                        {
                            new SegmentOption
                            {
                                Text = option,
                                Encoding = Encoding,
                                Phones = phones,
                            }
                        };

                        if (!string.IsNullOrEmpty(repr))
                        {
                            options.Add(new SegmentOption
                            {
                                Text = repr,
                            });
                        }

                        return new TextSegment
                        {
                            Text = token,
                            Options = options,
                        };
                    })
                    .ToList()
            };
        }
    }
}
