using System.Text;
using System.Text.Json;

namespace Enochian.Bhsa;

public static class BhsaOccurrenceAdapter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static BhsaSourceResult Parse(string path)
    {
        var occurrences = new List<BhsaOccurrence>();
        var rejections = new List<BhsaRejection>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            lineNumber++;
            try
            {
                var occurrence = JsonSerializer.Deserialize<BhsaOccurrence>(line, SerializerOptions)
                    ?? throw new JsonException("Occurrence is null.");
                if (occurrence.SchemaVersion != "1.0.0" || occurrence.CorpusLabel != "Biblical Hebrew" ||
                    string.IsNullOrWhiteSpace(occurrence.SourceRecordId) || string.IsNullOrWhiteSpace(occurrence.LexemeId))
                {
                    rejections.Add(new(lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "malformed", "Occurrence lacks the pinned schema, Biblical Hebrew label, or source identity."));
                    continue;
                }

                occurrences.Add(occurrence);
            }
            catch (JsonException exception)
            {
                rejections.Add(new(lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "malformed", exception.Message));
            }
        }

        var lexemes = new List<BhsaLexeme>();
        foreach (var group in occurrences.GroupBy(occurrence => occurrence.LexemeId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            if (!group.All(occurrence => string.Equals(occurrence.Language, first.Language, StringComparison.Ordinal) &&
                string.Equals(occurrence.Lexeme, first.Lexeme, StringComparison.Ordinal) &&
                string.Equals(occurrence.VocalizedForm, first.VocalizedForm, StringComparison.Ordinal)))
            {
                rejections.Add(new(group.Key, "ambiguous_metadata", "Lexeme occurrences disagree on language or form."));
                continue;
            }

            if (!string.Equals(first.Language, "Hebrew", StringComparison.Ordinal))
            {
                rejections.Add(new(group.Key, "aramaic", "BHSA marks the lexeme as Aramaic, not Biblical Hebrew."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(first.Lexeme) || string.IsNullOrWhiteSpace(first.VocalizedForm))
            {
                rejections.Add(new(group.Key, "missing_form", "Lexeme or vocalized form is absent."));
                continue;
            }

            var frequency = group.Count();
            var declaredFrequencies = group.Where(occurrence => occurrence.SourceFrequency.HasValue)
                .Select(occurrence => occurrence.SourceFrequency!.Value).Distinct().ToArray();
            if (declaredFrequencies.Length != 1 || declaredFrequencies[0] != frequency)
            {
                rejections.Add(new(group.Key, "frequency_mismatch",
                    $"Aggregated {frequency} occurrences but BHSA declares {string.Join(", ", declaredFrequencies)}."));
                continue;
            }

            var readings = group.Select(occurrence => occurrence.Phono)
                .Where(reading => !string.IsNullOrWhiteSpace(reading))
                .Select(reading => reading!.Normalize(NormalizationForm.FormC))
                .GroupBy(reading => reading, StringComparer.Ordinal)
                .Select(readingGroup => new BhsaReading(readingGroup.Key, readingGroup.Count()))
                .OrderByDescending(reading => reading.Frequency)
                .ThenBy(reading => reading.Ipa, StringComparer.Ordinal)
                .ToArray();
            if (readings.Length == 0)
            {
                rejections.Add(new(group.Key, "missing_phono", "No ETCBC phono reading is available."));
                continue;
            }

            lexemes.Add(new(
                group.Key,
                first.Lexeme.Normalize(NormalizationForm.FormC),
                first.VocalizedForm.Normalize(NormalizationForm.FormC),
                first.Gloss,
                first.PartOfSpeech,
                frequency,
                first.Rank,
                readings));
        }

        return new(lexemes, rejections, occurrences.Count);
    }
}
