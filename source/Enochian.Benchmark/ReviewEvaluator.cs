using Enochian.Provenance;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed record ReviewSummary(
    int TotalRecords,
    int CompletedRecords,
    int AcceptedRecords,
    int RejectedRecords,
    double Accuracy,
    bool Passed,
    IReadOnlyList<string> Blockers,
    IReadOnlyDictionary<string, int> ErrorCategories);

public static class ReviewEvaluator
{
    private static readonly HashSet<string> AllowedFields = new(
        [
            "$schema",
            "schema_version",
            "blinded_id",
            "source_form",
            "normalized_form",
            "generated_ipa",
            "expected_ipa",
            "decision",
            "error_category",
            "notes",
        ],
        StringComparer.Ordinal);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static ReviewSummary Evaluate(string path, BenchmarkThresholds thresholds)
    {
        var rows = new List<IpaReviewRow>();
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var identifyingField = document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .FirstOrDefault(name => !AllowedFields.Contains(name));
            if (identifyingField != null)
            {
                throw new InvalidDataException($"Blinded review row contains prohibited field '{identifyingField}'.");
            }

            rows.Add(JsonSerializer.Deserialize<IpaReviewRow>(line, SerializerOptions)
                ?? throw new InvalidDataException("Unable to deserialize blinded review row."));
        }

        var completed = rows.Where(row => row.Decision is "accept" or "reject").ToArray();
        var accepted = completed.Count(row => row.Decision == "accept");
        var rejected = completed.Length - accepted;
        var accuracy = completed.Length == 0 ? 0 : accepted / (double)completed.Length;
        var blockers = new List<string>();
        if (completed.Length < thresholds.MinimumReviewRecords)
        {
            blockers.Add("insufficient_completed_review_records");
        }

        if (completed.Length != rows.Count)
        {
            blockers.Add("pending_review_records");
        }

        if (accuracy < thresholds.MinimumReviewAccuracy)
        {
            blockers.Add("review_accuracy");
        }

        var missingCategory = completed.Any(row => row.Decision == "reject" && string.IsNullOrWhiteSpace(row.ErrorCategory));
        if (missingCategory)
        {
            blockers.Add("missing_error_category");
        }

        var categories = completed
            .Where(row => row.Decision == "reject")
            .GroupBy(row => row.ErrorCategory ?? "unspecified", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new(
            rows.Count,
            completed.Length,
            accepted,
            rejected,
            accuracy,
            blockers.Count == 0,
            blockers,
            new SortedDictionary<string, int>(categories, StringComparer.Ordinal));
    }
}
