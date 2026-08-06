using Enochian.Flow;
using Enochian.Text;
using System.Text.Json;

namespace Enochian.Lexicons;

public class Romlex(IConfigurable parent, IFlowResources resources) : Lexicon(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<Romlex>();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public override ILogger Log => Logger;

    public override IConfigurable Configure(JsonObject config)
    {
        return base.Configure(config);
    }

    protected override void LoadLexicon(string path)
    {
        try
        {
            if (Features == null || Encoding == null)
            {
                _ = AddError("ROMLEX requires configured features and encoding");
                return;
            }

            var encoder = new Encoder(Features, Encoding);
            RomlexLexicon lexicon;
            var entries = new List<LexiconEntry>();
            var entriesByLemma = new Dictionary<string, LexiconEntry>();

            path = Path.GetFullPath(path);
            Log.LogInformation("loading ROMLEX from {Path}", path);

            using var stream = File.OpenRead(path);
            lexicon = JsonSerializer.Deserialize<RomlexLexicon>(stream, SerializerOptions)
                ?? throw new JsonException("The ROMLEX file did not contain a lexicon.");

            int num = 0;
            var romLookup = lexicon.Entries.ToLookup(e => e.Lemma);
            foreach (var romEntry in romLookup)
            {
                var lemma = romEntry.Key;
                if (string.IsNullOrWhiteSpace(lemma))
                {
                    _ = AddError("ROMLEX entry has no lemma");
                    continue;
                }

                (_, _, var phones) = encoder.GetTextAndPhones(lemma);

                var text = romEntry.Select(e => e.Entry).FirstOrDefault(t => t != lemma) ?? lemma;

                var defsAndLanguages = romEntry
                    .Select(e =>
                    {
                        var lang = lexicon.Languages.FirstOrDefault(l => l.Code == e.SrcLangCode);
                        return (string.Format(CultureInfo.InvariantCulture, "{0}: {1}", e.PartOfSpeech, e.Definition), lang);
                    })
                    .ToLookup(de => de.Item1);

                var def = string.Join("\n", defsAndLanguages
                    .OrderBy(dl => dl.Key)
                    .Select(dl =>
                    {
                        var d = dl.Key;
                        if (dl.Any())
                        {
                            d += string.Format(CultureInfo.InvariantCulture, " ({0})", string.Join(", ", dl.Where(de => de.lang != null)
                                .Distinct().Select((de, i) => (((i + 1) % 4) == 0 ? "\n" : "") + de.lang?.Name)));
                        }

                        return d;
                    }));

                var entry = new LexiconEntry
                {
                    Lexicon = this,
                    Lemma = lemma,
                    Text = text,
                    Definition = def,
                    Phones = phones,
                };

                entries.Add(entry);

                if (!entriesByLemma.TryAdd(entry.Lemma, entry))
                {
                    _ = AddError("duplicate lemma '{0}'", entry.Lemma);
                }

                if ((++num % 1000) == 0)
                {
                    Log.LogInformation("  loaded {Count} entries", num);
                }
            }
            Log.LogInformation("loaded {Count} total entries", num);

            Entries = entries;
            EntriesByLemma = entriesByLemma;
        }
        catch (Exception e)
        {
            _ = AddError("unable to load ROMLX lexicon: {0}", e.Message);
        }
    }
}

public class RomlexLexicon
{
    public string? Created { get; set; }
    public IList<RomlexLanguage> Languages { get; set; } = [];
    public IList<RomlexEntry> Entries { get; set; } = [];
}

public class RomlexLanguage
{
    public string? Code { get; set; }
    public string? Name { get; set; }
}

public class RomlexEntry
{
    public string? SrcLangCode { get; set; }
    public string? DefLangCode { get; set; }

    public string? Lemma { get; set; }
    public string Entry
    {
        get => field ?? Lemma ?? string.Empty;
        set;
    }
    public string? PartOfSpeech { get; set; }
    public string? Definition { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj is not RomlexEntry other)
        {
            return false;
        }

        return SrcLangCode == other.SrcLangCode
            && DefLangCode == other.DefLangCode
            && Lemma == other.Lemma
            && Entry == other.Entry
            && PartOfSpeech == other.PartOfSpeech
            && Definition == other.Definition;
    }

    public override int GetHashCode()
    {
        var hash = base.GetHashCode();
        hash ^= SrcLangCode?.GetHashCode() ?? 0;
        hash ^= DefLangCode?.GetHashCode() ?? 0;
        hash ^= Lemma?.GetHashCode() ?? 0;
        hash ^= Entry?.GetHashCode() ?? 0;
        hash ^= PartOfSpeech?.GetHashCode() ?? 0;
        hash ^= Definition?.GetHashCode() ?? 0;
        return hash;
    }
}
