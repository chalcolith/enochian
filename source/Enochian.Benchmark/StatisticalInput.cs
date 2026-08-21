using Json.Schema;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed record NearestDistanceRecord(
    string SchemaVersion,
    string AnalysisId,
    string AnalysisMode,
    string SampleId,
    int RequestedSize,
    int Repetition,
    string QueryId,
    int QueryLength,
    string Section,
    string FrequencyBand,
    int Weight,
    string Language,
    string Family,
    bool IsNull,
    string? NullKind,
    double Distance);

public static class StatisticalInput
{
    public static IReadOnlyList<NearestDistanceRecord> Load(string path, string schemaPath)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        var rows = File.ReadLines(path, new UTF8Encoding(false, true))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => Parse(line, path, schema, schemaPath))
            .OrderBy(row => row.AnalysisId, StringComparer.Ordinal)
            .ThenBy(row => row.AnalysisMode, StringComparer.Ordinal)
            .ThenBy(row => row.RequestedSize)
            .ThenBy(row => row.SampleId, StringComparer.Ordinal)
            .ThenBy(row => row.QueryId, StringComparer.Ordinal)
            .ThenBy(row => row.Language, StringComparer.Ordinal)
            .ThenBy(row => row.IsNull)
            .ThenBy(row => row.NullKind, StringComparer.Ordinal)
            .ToArray();
        Validate(rows);
        return rows;
    }

    private static NearestDistanceRecord Parse(string line, string path, JsonSchema schema, string schemaPath)
    {
        using var document = JsonDocument.Parse(line);
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"A row in '{path}' does not conform to '{schemaPath}'.");
        }

        return document.RootElement.Deserialize<NearestDistanceRecord>(BenchmarkProtocol.LineSerializerOptions)
            ?? throw new InvalidDataException($"Unable to deserialize a nearest-distance row from '{path}'.");
    }

    private static void Validate(NearestDistanceRecord[] rows)
    {
        if (rows.Any(row => !double.IsFinite(row.Distance) || row.Distance < 0))
        {
            throw new InvalidDataException("Nearest distances must be finite and non-negative.");
        }

        if (rows.Any(row => row.IsNull == string.IsNullOrWhiteSpace(row.NullKind)))
        {
            throw new InvalidDataException("Null rows require a null kind and observed rows must not declare one.");
        }

        var duplicateObserved = rows.Where(row => !row.IsNull)
            .GroupBy(row => (row.AnalysisId, row.AnalysisMode, row.SampleId, row.QueryId, row.Language))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateObserved != null)
        {
            throw new InvalidDataException("Observed nearest-distance rows must be unique by analysis, mode, sample, query, and language.");
        }

        if (rows.Where(row => row.AnalysisMode == "type-primary").Any(row => row.Weight != 1))
        {
            throw new InvalidDataException("Primary type rows must have weight one; token occurrences are not independent observations.");
        }
    }
}
