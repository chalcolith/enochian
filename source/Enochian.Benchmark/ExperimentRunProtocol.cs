using Json.Schema;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class ExperimentRunProtocol
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string RunnerVersion { get; init; } = string.Empty;
    public string ConfigId { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string ExperimentPath { get; init; } = string.Empty;
    public string ExperimentSchemaPath { get; init; } = string.Empty;
    public string SamplingProtocolPath { get; init; } = string.Empty;
    public string StatisticsProtocolPath { get; init; } = string.Empty;
    public string ManifestSchemaPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Manifests { get; init; } = [];
    public IReadOnlyDictionary<string, string> Families { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public double DtwTolerance { get; init; }
    public string MatchScoresPath { get; init; } = string.Empty;
    public string NearestDistancesPath { get; init; } = string.Empty;
    public string ReportInputsPath { get; init; } = string.Empty;
    public string RunManifestPath { get; init; } = string.Empty;
    public string? DefinitionsPath { get; init; }

    public static ExperimentRunProtocol Load(string path)
    {
        var json = File.ReadAllText(path);
        using var instance = JsonDocument.Parse(json);
        var schemaPath = Resolve(
            instance.RootElement.GetProperty("$schema").GetString() ?? string.Empty,
            Path.GetDirectoryName(path)!);
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(instance.RootElement).IsValid)
        {
            throw new InvalidDataException($"'{path}' does not conform to '{schemaPath}'.");
        }

        return JsonSerializer.Deserialize<ExperimentRunProtocol>(json, BenchmarkProtocol.SerializerOptions)
            ?? throw new InvalidDataException("Unable to deserialize experiment-run protocol.");
    }

    internal static string Resolve(string path, string root) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
}
