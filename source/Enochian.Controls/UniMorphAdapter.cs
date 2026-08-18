using System.Buffers;
using System.Globalization;
using System.Text;

namespace Enochian.Controls;

public static class UniMorphAdapter
{
    private static readonly SearchValues<char> ArabicVowelMarks = SearchValues.Create(
        "\u064B\u064C\u064D\u064E\u064F\u0650\u0651\u0652\u0670");

    public static ControlSourceResult Parse(string path, bool requireArabicVowelMarks = false)
    {
        var parsedForms = new List<(string Lemma, string Form, string[] Features)>();
        var rejections = new List<ControlSourceRejection>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split('\t');
            var sourceRecordId = lineNumber.ToString(CultureInfo.InvariantCulture);
            if (columns.Length != 3 || columns.Any(string.IsNullOrWhiteSpace))
            {
                rejections.Add(new(sourceRecordId, "malformed_record", "UniMorph records must contain lemma, form, and features columns."));
                continue;
            }

            var lemma = columns[0].Normalize(NormalizationForm.FormC);
            var form = columns[1].Normalize(NormalizationForm.FormC);
            var features = columns[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            parsedForms.Add((lemma, form, features));
        }

        var lemmas = parsedForms
            .Select(record => record.Lemma)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select((lemma, index) => new ControlSourceLemma(
                $"lemma:{index + 1:D8}",
                lemma,
                lemma,
                null,
                []))
            .ToList();
        if (requireArabicVowelMarks)
        {
            foreach (var lemma in lemmas.Where(lemma => !lemma.NormalizedForm.AsSpan().ContainsAny(ArabicVowelMarks)).ToArray())
            {
                rejections.Add(new(lemma.RecordId, "uncertain_unvocalized_orthography", "Perso-Arabic lemma has no explicit vowel marks for auditable phonology."));
                _ = lemmas.Remove(lemma);
            }
        }

        var forms = parsedForms
            .OrderBy(record => record.Lemma, StringComparer.Ordinal)
            .ThenBy(record => record.Form, StringComparer.Ordinal)
            .ThenBy(record => string.Join(';', record.Features), StringComparer.Ordinal)
            .Select((record, index) => new ControlInflectedForm(
                $"form:{index + 1:D8}",
                record.Lemma,
                record.Form,
                record.Features))
            .ToArray();
        return new(lemmas, rejections, forms);
    }
}
