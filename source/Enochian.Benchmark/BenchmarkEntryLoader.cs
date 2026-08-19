using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public static class BenchmarkEntryLoader
{
    public static IReadOnlyList<BenchmarkEntry> Load(
        string path,
        string expectedLanguage,
        IpaEncoder encoder,
        int minimumPhonemes,
        int maximumPhonemes)
    {
        var entries = new List<BenchmarkEntry>();
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (Get(root, "entry_kind") != "lemma" || Get(root, "language") != expectedLanguage)
            {
                continue;
            }

            var ipa = Get(root, "ipa").Normalize(NormalizationForm.FormC);
            var (_, _, phones) = encoder.GetTextAndPhones(ipa, out var unknown);
            if (unknown.Count != 0 || phones.Count == 0)
            {
                throw new InvalidDataException($"Entry '{Get(root, "entry_id")}' has empty or unknown IPA segments.");
            }

            if (phones.Count < minimumPhonemes || phones.Count > maximumPhonemes)
            {
                continue;
            }

            entries.Add(new(
                Get(root, "entry_id"),
                Get(root, "source"),
                Get(root, "source_record_id"),
                expectedLanguage,
                ipa,
                [.. phones],
                BenchmarkSampling.GetLengthBand(phones.Count),
                BenchmarkSampling.GetUnusualCategory(ipa)));
        }

        return
        [
            .. entries.OrderBy(entry => entry.EntryId, StringComparer.Ordinal),
        ];
    }

    private static string Get(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : throw new InvalidDataException($"Normalized benchmark entry lacks string field '{name}'.");
}
