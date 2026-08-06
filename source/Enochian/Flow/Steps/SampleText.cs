using Enochian.Text;
using System.Text.RegularExpressions;

namespace Enochian.Flow.Steps;

public class SampleText(IConfigurable parent, IFlowResources resources) : TextFlowStep(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<SampleText>();
    private IList<TextChunk>? chunks;

    public override ILogger Log => Logger;

    public bool PreserveCase { get; set; }
    public FeatureSet? Features { get; private set; }
    public string? SourcePath { get; private set; }

    public IList<TextChunk> Chunks { set => chunks = value; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        PreserveCase = config.Get<bool>("preserveCase", this);

        if (Resources != null)
        {
            var features = config.Get<string>("features", this);
            Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
            if (Features == null)
            {
                _ = AddError("invalid features name '{0}'", features);
            }
        }
        else
        {
            _ = AddError("no resources specified");
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

        return this;
    }

    public override string GenerateReport(ReportType reportType)
    {
        var sourcePath = string.IsNullOrWhiteSpace(SourcePath) ? string.Empty : GetChildPath(AbsoluteFilePath, SourcePath);
        return string.Format(CultureInfo.InvariantCulture, "&nbsp;&nbsp;Path: {0}", sourcePath);
    }

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
            int numChunks = 0;
            int numLines = 0;
            try
            {
                var sourcePath = GetChildPath(AbsoluteFilePath, SourcePath);
                Log.LogInformation("reading {SourcePath}", sourcePath);

                try
                {
                    fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
                    sr = new StreamReader(fs);
                }
                catch (Exception e)
                {
                    _ = AddError(e.Message);
                }

                if (fs != null && sr != null)
                {
                    chunks = [];
                    TextChunk? currentChunk = null;
                    bool needNewChunk = true;

                    string? line;
                    while (true)
                    {
                        try
                        {
                            line = sr.ReadLine();
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

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            needNewChunk = true;
                            continue;
                        }

                        if (needNewChunk)
                        {
                            if (currentChunk != null)
                            {
                                chunks.Add(currentChunk);
                                yield return currentChunk;
                            }

                            currentChunk = new TextChunk { Lines = [] };
                            numChunks++;
                            needNewChunk = false;
                        }

                        currentChunk ??= new TextChunk { Lines = [] };
                        currentChunk.Lines.Add(GetInterline(line));
                        numLines++;
                    }

                    if (currentChunk != null && currentChunk.Lines.Count != 0)
                    {
                        chunks.Add(currentChunk);
                        yield return currentChunk;
                    }
                }
            }
            finally
            {
                sr?.Dispose();

                fs?.Dispose();

                Log.LogInformation("read {ChunkCount} chunks; {LineCount} lines", numChunks, numLines);
            }
        }
    }

    private static readonly Regex WORD = new(@"\w+", RegexOptions.Compiled);

    private TextLine GetInterline(string text)
    {
        var segs = new List<TextSegment>();

        if (!string.IsNullOrWhiteSpace(text))
        {
            var match = WORD.Match(text);
            while (match.Success)
            {
                segs.Add(new TextSegment
                {
                    Options = [new SegmentOption { Text = PreserveCase ? match.Value : match.Value.ToLowerInvariant() }]
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
