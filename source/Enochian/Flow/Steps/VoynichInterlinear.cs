using Enochian.Text;
using System.Text.RegularExpressions;

namespace Enochian.Flow.Steps;

public class VoynichInterlinear(IConfigurable parent, IFlowResources resources) : TextFlowStep(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<VoynichInterlinear>();

    private IList<TextChunk>? chunks;

    public override ILogger Log => Logger;

    public Encoding? Encoding { get; private set; }
    private Encoder? Encoder { get; set; }

    public string? SourcePath { get; private set; }

    public IList<string>? Locuses { get; set; }

    public IList<TextChunk> Chunks { set => chunks = value; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        if (Resources != null)
        {
            var encoding = config.Get<string>("encoding", this);
            Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
            if (Encoding?.Features != null)
            {
                Encoder = new Encoder(Encoding.Features, Encoding);
            }
            else
            {
                _ = AddError("invalid encoding name '{0}'", encoding);
            }
        }
        else
        {
            _ = AddError("no encoding specified");
        }

        var path = config.Get<string>("path", this);
        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePath = path;
        }
        else
        {
            var text = config.Get<string>("text", this);
            if (!string.IsNullOrWhiteSpace(text))
            {
                chunks =
                [
                    new TextChunk
                    {
                        Lines = [GetInterline(text)]
                    }
                ];
            }
            else
            {
                _ = AddError("no sample text specified");
            }
        }

        var locuses = config.Get<IEnumerable<string>>("locuses", this);
        Locuses = locuses?.ToList();

        return this;
    }

    public override string GenerateReport(ReportType reportType)
    {
        var sourcePath = string.IsNullOrWhiteSpace(SourcePath) ? string.Empty : GetChildPath(AbsoluteFilePath, SourcePath);
        return string.Format(CultureInfo.InvariantCulture, "&nbsp;&nbsp;Path: {0}<br/>&nbsp;&nbsp;Encoding: {1}: {2}<br/>&nbsp;&nbsp;Path: {3}",
            sourcePath,
            Encoding?.Id, Encoding?.Description, Encoding?.AbsoluteFilePath);
    }

    private static readonly Regex LineRegex = new(@"^\s*(<[^>]+>)\s+(.*)[-=]", RegexOptions.Compiled);
    private static readonly Regex ExtComment = new(@"\*{&(\d+)}", RegexOptions.Compiled);
    private static readonly Regex ReplComment = new(@"{&[^}]+}", RegexOptions.Compiled);
    private static readonly char[] Punctuation = ['.', '\'', '-', '=', '?', '%'];

    public override IEnumerable<TextChunk> GetOutputs()
    {
        if (chunks != null)
        {
            foreach (var chunk in chunks)
            {
                yield return chunk;
            }
        }
        else if (!string.IsNullOrWhiteSpace(SourcePath))
        {
            FileStream? fs = null;
            StreamReader? sr = null;
            int numLines = 0;
            int numChunks = 0;
            chunks = [];
            var chunksPerLocus = new Dictionary<string, TextChunk>();

            try
            {
                var sourcePath = GetChildPath(AbsoluteFilePath, SourcePath);
                Log.LogInformation("reading {SourcePath}", sourcePath);

                try
                {
                    fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                    sr = new StreamReader(fs, System.Text.Encoding.GetEncoding("ISO-8859-1"));
                }
                catch (Exception e)
                {
                    _ = AddError(e.Message);
                }

                if (fs != null && sr != null)
                {
                    string? line;
                    while (true)
                    {
                        try
                        {
                            line = sr.ReadLine();
                            numLines++;
                        }
                        catch (Exception e)
                        {
                            _ = AddError(e.Message);
                            break;
                        }

                        if (line == null)
                        {
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                        {
                            continue;
                        }

                        var lineMatch = LineRegex.Match(line);
                        if (lineMatch.Success)
                        {
                            var locus = lineMatch.Groups[1].Value;
                            if (Locuses != null && !Locuses.Contains(locus))
                            {
                                continue;
                            }

                            var text = lineMatch.Groups[2].Value;

                            var extendedMatch = ExtComment.Match(text);
                            while (extendedMatch.Success)
                            {
                                if (int.TryParse(extendedMatch.Groups[1].Value, out int code))
                                {
                                    var repl = new string((char)code, 1);
                                    text = text[..extendedMatch.Index]
                                        + repl
                                        + text[(extendedMatch.Index + extendedMatch.Length)..];
                                }
                                extendedMatch = extendedMatch.NextMatch();
                            }

                            var replMatch = ReplComment.Match(text);
                            while (replMatch.Success)
                            {
                                text = text[..System.Math.Min(text.Length, replMatch.Index)]
                                    + replMatch.Groups[1].Value
                                    + text[System.Math.Min(text.Length, replMatch.Index + replMatch.Length)..];
                                replMatch = replMatch.NextMatch();
                            }

                            var chunk = new TextChunk
                            {
                                Description = locus,
                                Lines = [GetInterline(text)]
                            };
                            chunks.Add(chunk);
                            numChunks++;

                            yield return chunk;

                            if (Locuses != null && Locuses.Any())
                            {
                                chunksPerLocus[locus] = chunk;
                                if (chunksPerLocus.Count == Locuses.Count)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                sr?.Dispose();

                fs?.Dispose();

                Log.LogInformation("read {LineCount} lines, {ChunkCount} chunks", numLines, numChunks);
            }
        }
    }

    private TextLine GetInterline(string line)
    {
        var tokens = line.Split(Punctuation, StringSplitOptions.RemoveEmptyEntries);
        var optionComparer = new OptionComparer();

        return new TextLine
        {
            SourceStep = this,
            Text = line,
            Segments = [.. tokens
                .Select(token =>
                {
                    string option = token;
                    string repr = "";
                    IList<double[]> phones = [];
                    if (Encoder != null)
                    {
                        (option, repr, phones) = Encoder.GetTextAndPhones(token);
                    }

                    var options = new List<SegmentOption>
                    {
                        new() {
                            Text = option,
                            Encoding = Encoding,
                            Phones = phones,
                        }
                    };

                    if (!string.IsNullOrEmpty(repr))
                    {
                        options.Add(new SegmentOption
                        {
                            Tags = TextTag.Repr,
                            Text = repr,
                        });
                    }

                    options.Sort(optionComparer);

                    return new TextSegment
                    {
                        Text = token,
                        Options = options,
                    };
                })]
        };
    }
}
