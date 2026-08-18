using System.Text;
using System.Text.RegularExpressions;

namespace Enochian.Controls;

public static partial class ZemberekDictionaryAdapter
{
    public static ControlSourceResult Parse(string path)
    {
        var lemmas = new List<ControlSourceLemma>();
        var rejections = new List<ControlSourceRejection>();
        var lineNumber = 0;
        foreach (var sourceLine in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            lineNumber++;
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var recordId = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var match = EntryPattern().Match(line);
            if (!match.Success)
            {
                rejections.Add(new(recordId, "malformed", "Line does not match the Zemberek dictionary syntax."));
                continue;
            }

            var original = match.Groups["lemma"].Value.Normalize(NormalizationForm.FormC);
            var metadata = ParseMetadata(match.Groups["metadata"].Value);
            var partsOfSpeech = metadata
                .Where(item => item.StartsWith("P:", StringComparison.Ordinal))
                .SelectMany(item => item[2..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
            if (partsOfSpeech.Contains("Abbrv", StringComparer.Ordinal))
            {
                rejections.Add(new(recordId, "abbreviation", "Zemberek marks the entry as Abbrv."));
                continue;
            }

            if (partsOfSpeech.Contains("Prop", StringComparer.Ordinal) || StartsWithUppercase(original))
            {
                rejections.Add(new(recordId, "proper_name", "Zemberek marks the entry as proper or capitalizes its lemma."));
                continue;
            }

            if (partsOfSpeech.Contains("Punc", StringComparer.Ordinal) || !original.Any(char.IsLetter))
            {
                rejections.Add(new(recordId, "malformed", "Punctuation is not a lexical stem."));
                continue;
            }

            var primary = partsOfSpeech.FirstOrDefault();
            lemmas.Add(new(
                recordId,
                original,
                original.ToLower(new System.Globalization.CultureInfo("tr-TR")),
                primary,
                metadata));
        }

        return new(
            [.. lemmas.GroupBy(lemma => (lemma.NormalizedForm, lemma.PartOfSpeech))
                .Select(group => group.First())],
            rejections);
    }

    private static IReadOnlyList<string> ParseMetadata(string value)
    {
        return value.Length == 0
            ? []
            : [.. value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    private static bool StartsWithUppercase(string value)
    {
        return Rune.GetRuneAt(value, 0).ToString().Any(char.IsUpper);
    }

    [GeneratedRegex("^(?<lemma>[^\\s\\[\\]]+)(?:\\s+\\[(?<metadata>[^\\]]+)\\])?$")]
    private static partial Regex EntryPattern();
}
