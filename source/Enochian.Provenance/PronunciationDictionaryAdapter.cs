using System.Text;
using System.Text.Json;

namespace Enochian.Provenance;

public static class PronunciationDictionaryAdapter
{
    public static PronunciationLexeme ReadLexeme(
        JsonElement entry, string source, string snapshot, string language, string license)
    {
        var exclusion = string.IsNullOrWhiteSpace(GetString(entry, "word")) ? "missing_word"
            : !IsEligible(entry) ? "entry_kind" : null;
        return new PronunciationLexeme(source, LexicalIdentity(entry), exclusion,
            exclusion == null ? [.. ReadWiktextract(entry, source, snapshot, language, license)] : []);
    }

    public static bool IsEligible(JsonElement entry)
    {
        var pos = GetString(entry, "pos");
        if (pos is "" or "name" or "abbrev" or "suffix" or "prefix" or "character" or "symbol")
        {
            return false;
        }

        if (entry.TryGetProperty("senses", out var senses) && senses.ValueKind == JsonValueKind.Array &&
            senses.GetArrayLength() > 0)
        {
            return senses.EnumerateArray().Any(sense =>
                !sense.TryGetProperty("form_of", out _) &&
                !Strings(sense, "tags").Any(tag => tag is "form-of" or "abbreviation" or "proper-name"));
        }

        return true;
    }

    public static string LexicalIdentity(JsonElement entry) =>
        string.Join('\u001f', GetString(entry, "word").Normalize(NormalizationForm.FormC),
            GetString(entry, "pos"), entry.TryGetProperty("etymology_number", out var number) ? number.ToString() : "0");

    public static IEnumerable<PronunciationReading> ReadWiktextract(
        JsonElement entry,
        string source,
        string snapshot,
        string language,
        string license)
    {
        if (!entry.TryGetProperty("sounds", out var sounds))
        {
            yield break;
        }

        if (sounds.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Wiktextract sounds must be an array.");
        }

        var originalForm = GetString(entry, "word");
        var lemma = originalForm.Normalize(NormalizationForm.FormC);
        var templates = entry.TryGetProperty("sound_templates", out var templateArray) &&
            templateArray.ValueKind == JsonValueKind.Array
            ? templateArray.EnumerateArray().Select(template => GetString(template, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name)).Order(StringComparer.Ordinal).ToArray()
            : [];
        foreach (var sound in sounds.EnumerateArray())
        {
            var ipa = GetString(sound, "ipa");
            if (string.IsNullOrWhiteSpace(ipa))
            {
                continue;
            }

            var note = GetString(sound, "note");
            var labels = Strings(sound, "tags").Concat(Strings(sound, "raw_tags"))
                .Concat(string.IsNullOrWhiteSpace(note) ? [] : new[] { note })
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            yield return new PronunciationReading(
                source, snapshot, "https://en.wiktionary.org/wiki/" + Uri.EscapeDataString(lemma),
                language, lemma, GetString(entry, "pos"), LexicalIdentity(entry), ipa, labels,
                templates.Length == 0 ? "unknown" : "template-generated",
                templates.Length == 0 ? null : string.Join(",", templates),
                ["enwiktionary"], license, OriginalForm: originalForm);
        }
    }

    public static IEnumerable<PronunciationReading> ReadIpaDictionary(
        TextReader reader,
        string source,
        string snapshot,
        string language,
        string convention,
        string license)
    {
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var columns = line.Split('\t');
            if (columns.Length != 2 || string.IsNullOrWhiteSpace(columns[0]) || string.IsNullOrWhiteSpace(columns[1]))
            {
                throw new InvalidDataException($"{source}:{lineNumber}: expected a word and IPA separated by one tab.");
            }

            var lemma = columns[0].Normalize(NormalizationForm.FormC);
            foreach (var ipa in columns[1].Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                yield return new PronunciationReading(
                    source, snapshot, $"https://github.com/open-dict-data/ipa-dict/tree/{snapshot}/data",
                    language, lemma, "unknown", lemma, ipa, [convention], "unknown",
                    "mixed-manual-semi-automatic", [source, "ipa-dict-upstreams-unknown"], license, "unknown", columns[0]);
            }
        }
    }

    public static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Expected a JSON object.");
        }

        if (!element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw new InvalidDataException($"{property} must be a string.");
    }

    private static string[] Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var values))
        {
            return [];
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{property} must be an array.");
        }

        return [.. values.EnumerateArray().Select(value => value.GetString()
            ?? throw new InvalidDataException($"{property} cannot contain null."))];
    }
}
