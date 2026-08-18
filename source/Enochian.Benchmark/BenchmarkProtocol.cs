using Json.Schema;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class BenchmarkProtocol
{
    public string SchemaVersion { get; init; } = string.Empty;

    public string BenchmarkId { get; init; } = string.Empty;

    public int SamplingSeed { get; init; }

    public int SamplesPerStratum { get; init; }

    public int MinimumPhonemes { get; init; } = 3;

    public int MaximumPhonemes { get; init; } = 20;

    public IReadOnlyList<string> RequiredLengthBands { get; init; } = [];

    public BenchmarkThresholds Thresholds { get; init; } = new(100, 0.95, 0.8, 0.95, 0.99, 0.85, 0.35);

    public IReadOnlyList<BenchmarkSource> Sources { get; init; } = [];

    public IReadOnlyList<string> DegradationProfiles { get; init; } = [];

    public BenchmarkOutputPaths Outputs { get; init; } = new();

    public static BenchmarkProtocol Load(string path)
    {
        var json = File.ReadAllText(path);
        Validate(json, path);
        return JsonSerializer.Deserialize<BenchmarkProtocol>(json, SerializerOptions)
            ?? throw new InvalidDataException("Unable to deserialize benchmark protocol.");
    }

    internal static void Validate(string json, string path)
    {
        using var instance = JsonDocument.Parse(json);
        if (!instance.RootElement.TryGetProperty("$schema", out var schemaProperty))
        {
            throw new InvalidDataException($"'{path}' does not declare $schema.");
        }

        var schemaPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(path)!,
            (schemaProperty.GetString() ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)));
        var schema = JsonSchema.FromText(
            File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(instance.RootElement).IsValid)
        {
            throw new InvalidDataException($"'{path}' does not conform to '{schemaPath}'.");
        }
    }

    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    internal static JsonSerializerOptions LineSerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

public sealed class BenchmarkSource
{
    public string SourceId { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string LexiconPath { get; init; } = string.Empty;

    public string ReviewPath { get; init; } = string.Empty;

    public bool Confirmatory { get; init; }

    public bool Required { get; init; }
}

public sealed class BenchmarkOutputPaths
{
    public string Scores { get; init; } = "scores.jsonl";

    public string Summaries { get; init; } = "summaries.jsonl";

    public string QualityReport { get; init; } = "quality-report.json";
}
