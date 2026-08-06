using Enochian.Flow;
using Enochian.Text;

namespace Enochian.Lexicons;

public class CMUDict(IConfigurable parent, IFlowResources resources) : Lexicon(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<CMUDict>();

    public override ILogger Log => Logger;

    public override IConfigurable Configure(JsonObject config)
    {
        return base.Configure(config);
    }

    private static readonly char[] WS = [' ', '\t'];

    protected override void LoadLexicon(string path)
    {
        try
        {
            if (Features == null || Encoding == null)
            {
                _ = AddError("CMUDICT requires configured features and encoding");
                return;
            }

            var encoder = new Encoder(Features, Encoding);

            var entries = new List<LexiconEntry>();
            var entriesByLemma = new Dictionary<string, LexiconEntry>();

            path = Path.GetFullPath(path);
            Log.LogInformation("loading CMUDICT from {Path}", path);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var sr = new StreamReader(fs, System.Text.Encoding.ASCII))
            {
                string? line;
                int num = 0;
                while ((line = sr.ReadLine()) != null && num++ < MaxEntriesToLoad)
                {
                    if (line.StartsWith(";;;", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var tokens = line.Split(WS, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 2)
                    {
                        continue;
                    }

                    var sb = new System.Text.StringBuilder();
                    var lemma = tokens[0].ToUpperInvariant();
                    var phones = tokens
                        .Skip(1)
                        .SelectMany(t =>
                        {
                            _ = sb.Append(t);
                            var input = new TextSegment
                            {
                                Options = [new SegmentOption { Text = t }]
                            };
                            var result = encoder.ProcessSegment(input);
                            return (result.Options ?? []).SelectMany(o => o.Phones ?? []);
                        })
                        .ToArray();

                    var entry = new LexiconEntry
                    {
                        Lexicon = this,
                        Text = tokens[0],
                        Lemma = lemma,
                        Encoded = sb.ToString(),
                        Phones = phones,
                    };

                    entries.Add(entry);
                    if (!entriesByLemma.TryAdd(lemma, entry))
                    {
                        _ = AddError("duplicate lemma '{0}'", lemma);
                    }

                    if ((num % 1000) == 0)
                    {
                        Log.LogInformation("  loaded {Count} entries", num);
                    }
                }
                Log.LogInformation("loaded {Count} total entries", num);
            }

            Entries = entries;
            EntriesByLemma = entriesByLemma;
        }
        catch (Exception e)
        {
            _ = AddError("unable to load CMUDICT lexicon: {0}", e.Message);
        }
    }
}
