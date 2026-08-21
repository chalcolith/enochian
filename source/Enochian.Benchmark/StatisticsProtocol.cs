using Json.Schema;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class StatisticsProtocol
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string StatisticsId { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string ExperimentPath { get; init; } = string.Empty;
    public string InputPath { get; init; } = string.Empty;
    public string InputSchemaPath { get; init; } = string.Empty;
    public string CalibrationNullKind { get; init; } = string.Empty;
    public string ScoreMetric { get; init; } = string.Empty;
    public int Seed { get; init; }
    public int PermutationCount { get; init; }
    public int BootstrapCount { get; init; }
    public double ConfidenceLevel { get; init; }
    public IReadOnlyList<StatisticalContrast> Contrasts { get; init; } = [];
    public StatisticsOutputPaths Outputs { get; init; } = new();

    public static StatisticsProtocol Load(string path)
    {
        var json = File.ReadAllText(path);
        using var instance = JsonDocument.Parse(json);
        var schemaPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(path)!,
            (instance.RootElement.GetProperty("$schema").GetString() ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)));
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(instance.RootElement).IsValid)
        {
            throw new InvalidDataException($"'{path}' does not conform to '{schemaPath}'.");
        }

        var protocol = JsonSerializer.Deserialize<StatisticsProtocol>(json, BenchmarkProtocol.SerializerOptions)
            ?? throw new InvalidDataException("Unable to deserialize statistics protocol.");
        protocol.Validate();
        protocol.ValidateExperiment(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(path)!,
            protocol.ExperimentPath.Replace('/', Path.DirectorySeparatorChar))));
        return protocol;
    }

    private void Validate()
    {
        if (Contrasts.Select(contrast => contrast.ContrastId).Distinct(StringComparer.Ordinal).Count() != Contrasts.Count)
        {
            throw new InvalidDataException("Statistical contrast IDs must be unique.");
        }

        if (Phase == "confirmatory" && Contrasts.Count(contrast => contrast.Primary) != 1)
        {
            throw new InvalidDataException("Confirmatory protocols require exactly one registered primary contrast.");
        }

        var paths = Outputs.AllPaths.ToArray();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
        {
            throw new InvalidDataException("Every statistics output path must be distinct.");
        }
    }

    private void ValidateExperiment(string path)
    {
        using var experiment = JsonDocument.Parse(File.ReadAllText(path));
        var root = experiment.RootElement;
        if (Phase == "confirmatory" && (!root.GetProperty("frozen").GetBoolean() ||
            root.GetProperty("phase").GetString() != "confirmatory"))
        {
            throw new InvalidDataException("Confirmatory statistics require a frozen confirmatory experiment config.");
        }

        var registered = root.GetProperty("planned_contrasts").EnumerateArray()
            .ToDictionary(element => element.GetProperty("id").GetString()!, StringComparer.Ordinal);
        foreach (var contrast in Contrasts)
        {
            if (!registered.TryGetValue(contrast.ContrastId, out var declaration))
            {
                throw new InvalidDataException($"Contrast '{contrast.ContrastId}' is not registered in '{path}'.");
            }

            var expectedAlternative = declaration.GetProperty("expected_direction").GetString() switch
            {
                "lower" => "greater",
                "higher" => "less",
                _ => "two-sided",
            };
            if (contrast.Primary != declaration.GetProperty("primary").GetBoolean() ||
                contrast.Alternative != expectedAlternative ||
                !SetEquals(contrast.TargetFamilies, declaration.GetProperty("target_groups")) ||
                !SetEquals(contrast.ControlFamilies, declaration.GetProperty("control_groups")))
            {
                throw new InvalidDataException($"Contrast '{contrast.ContrastId}' does not match its frozen declaration.");
            }
        }

        if (Phase == "confirmatory" && !registered.Keys.ToHashSet(StringComparer.Ordinal)
            .SetEquals(Contrasts.Select(contrast => contrast.ContrastId)))
        {
            throw new InvalidDataException("Confirmatory statistics must include the complete frozen contrast set.");
        }
    }

    private static bool SetEquals(IEnumerable<string> values, JsonElement array) =>
        values.ToHashSet(StringComparer.Ordinal).SetEquals(
            array.EnumerateArray().Select(element => element.GetString()!));
}

public sealed class StatisticalContrast
{
    public string ContrastId { get; init; } = string.Empty;
    public bool Primary { get; init; }
    public IReadOnlyList<string> TargetFamilies { get; init; } = [];
    public IReadOnlyList<string> ControlFamilies { get; init; } = [];
    public string Alternative { get; init; } = string.Empty;
}

public sealed class StatisticsOutputPaths
{
    public string CalibratedScores { get; init; } = string.Empty;
    public string Estimates { get; init; } = string.Empty;
    public string Intervals { get; init; } = string.Empty;
    public string Tests { get; init; } = string.Empty;
    public string AdjustedPValues { get; init; } = string.Empty;
    public string Diagnostics { get; init; } = string.Empty;

    internal IEnumerable<string> AllPaths =>
    [
        CalibratedScores,
        Estimates,
        Intervals,
        Tests,
        AdjustedPValues,
        Diagnostics,
    ];
}
