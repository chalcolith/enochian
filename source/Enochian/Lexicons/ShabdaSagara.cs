using Enochian.Flow;
using Enochian.Text;
using System.Text.RegularExpressions;

namespace Enochian.Lexicons;

public class ShabdaSagara(IConfigurable parent, IFlowResources resources) : Lexicon(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<ShabdaSagara>();

    public override ILogger Log => Logger;

    public override IConfigurable Configure(JsonObject config)
    {
        return base.Configure(config);
    }

    private static readonly Regex LemmaLineRegex = new(@"<L>(\d+).*<k1>(.*)<k2>", RegexOptions.Compiled);
    private static readonly Regex FirstLineRegex = new(@"(.*)¦\s+(\S+)\s+(.*)", RegexOptions.Compiled);
    private static readonly Regex MidLineRegex = new(@"<>(.*)", RegexOptions.Compiled);
    private static readonly Regex InlineRegex = new(@"{#([^#]+)#}", RegexOptions.Compiled);

    protected override void LoadLexicon(string path)
    {
        try
        {
            if (Features == null || Encoding == null)
            {
                _ = AddError("Shabda-Sagara requires configured features and encoding");
                return;
            }

            path = Path.GetFullPath(path);
            Log.LogInformation("loading SHS from {Path}", path);

            var encoder = new Encoder(Features, Encoding);
            var entries = new List<LexiconEntry>();
            var entriesByLemma = new Dictionary<string, LexiconEntry>();

            int num = 0;
            LexiconEntry? currentEntry = null;
            using (var sr = new StreamReader(path))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    var match = LemmaLineRegex.Match(line);
                    if (match.Success)
                    {
                        if (currentEntry != null && !string.IsNullOrWhiteSpace(currentEntry.Lemma) && !string.IsNullOrWhiteSpace(currentEntry.Definition))
                        {
                            if (entriesByLemma.TryGetValue(currentEntry.Lemma, out var existingEntry))
                            {
                                existingEntry.Definition = existingEntry.Definition + "\n\n" + currentEntry.Definition;
                            }
                            else
                            {
                                entries.Add(currentEntry);
                                entriesByLemma[currentEntry.Lemma] = currentEntry;
                            }

                            if ((++num % 1000) == 0)
                            {
                                Log.LogInformation("  loaded {Count} entries", num);
                            }
                        }

                        string lemmaSlp1 = match.Groups[2].Value;
                        (string text, string lemma, IList<double[]> phones) = encoder.GetTextAndPhones(lemmaSlp1);
                        currentEntry = new LexiconEntry
                        {
                            Lexicon = this,
                            Language = "san",
                            Family = "Indo-European",
                            SourceId = "cdsl-shs",
                            SourceRecordId = match.Groups[1].Value,
                            Lemma = lemma,
                            Text = text,
                            Form = text,
                            EntryKind = LexiconEntryKind.Lemma,
                            SourceEncoding = "slp1",
                            Definition = "(" + lemmaSlp1 + ") ",
                            Phones = phones,
                        };
                    }
                    else if (currentEntry != null)
                    {
                        if ((match = FirstLineRegex.Match(line)).Success)
                        {
                            currentEntry.Definition = currentEntry.Definition
                                + ReplaceSlp1(encoder, match.Groups[2].Value) + " "
                                + ReplaceSlp1(encoder, match.Groups[3].Value);
                        }
                        else if ((match = MidLineRegex.Match(line)).Success)
                        {
                            currentEntry.Definition = currentEntry.Definition + " "
                                + ReplaceSlp1(encoder, match.Groups[1].Value);
                        }
                    }
                }

                if (currentEntry != null)
                {
                    if (entriesByLemma.TryGetValue(currentEntry.Lemma, out var existingEntry))
                    {
                        existingEntry.Definition = existingEntry.Definition + "\n" + currentEntry.Definition;
                    }
                    else
                    {
                        entries.Add(currentEntry);
                        entriesByLemma[currentEntry.Lemma] = currentEntry;
                    }
                }
            }

            Log.LogInformation("loaded {Count} entries from SHS", num);

            SetEntries(entries);
        }
        catch (Exception e)
        {
            _ = AddError("unable to load SHS lexicon: {0}", e.Message);
        }
    }

    private static string ReplaceSlp1(Encoder encoder, string str)
    {
        var match = InlineRegex.Match(str);
        while (match.Success)
        {
            (_, string devanagari, _) = encoder.GetTextAndPhones(match.Groups[1].Value);
            str = str[..match.Index]
                + devanagari + " (" + match.Groups[1].Value + ")"
                + str[(match.Index + match.Length)..];

            match = InlineRegex.Match(str);
        }
        return str;
    }
}
