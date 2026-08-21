using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public static class SamplingInputLoader
{
    public static IReadOnlyList<SamplingCandidateInput> LoadCandidates(
        string path,
        string expectedLanguage,
        IpaEncoder encoder)
    {
        var entries = new List<SamplingCandidateInput>();
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (GetString(root, "language") != expectedLanguage)
            {
                continue;
            }

            var ipa = GetString(root, "ipa").Normalize(NormalizationForm.FormC);
            var (_, _, phones) = encoder.GetTextAndPhones(ipa, out var unknown);
            if (unknown.Count != 0 || phones.Count == 0)
            {
                throw new InvalidDataException($"Entry '{GetString(root, "entry_id")}' has empty or unknown IPA segments.");
            }

            entries.Add(new(
                GetString(root, "entry_id"),
                expectedLanguage,
                GetString(root, "lemma"),
                ipa,
                [.. phones],
                GetNullableDouble(root, "frequency"),
                GetString(root, "entry_kind"),
                GetString(root, "source"),
                GetString(root, "source_record_id")));
        }

        return [.. entries.OrderBy(entry => entry.EntryId, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<SamplingQuery> LoadQueries(string path)
    {
        var queries = new List<SamplingQuery>();
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            queries.Add(new(
                GetString(root, "query_id"),
                GetString(root, "text"),
                [.. root.GetProperty("symbols").EnumerateArray().Select(symbol => symbol.GetString() ?? string.Empty)],
                root.GetProperty("token_frequency").GetInt32(),
                GetOptionalString(root, "section", "unknown"),
                GetOptionalString(root, "frequency_band", "unknown")));
        }

        return [.. queries.OrderBy(query => query.QueryId, StringComparer.Ordinal)];
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : throw new InvalidDataException($"Sampling input lacks string field '{name}'.");

    private static string GetOptionalString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;

    private static double? GetNullableDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.GetDouble();
    }
}
