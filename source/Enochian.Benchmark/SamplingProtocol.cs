using Json.Schema;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class SamplingProtocol
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string SamplingId { get; init; } = string.Empty;
    public string GeneratorVersion { get; init; } = string.Empty;
    public int Seed { get; init; }
    public int Repetitions { get; init; }
    public int NullRepetitions { get; init; } = 1;
    public string QueriesPath { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, double[]> Mapping { get; init; } = new Dictionary<string, double[]>(StringComparer.Ordinal);
    public IReadOnlyList<SamplingAnalysis> Analyses { get; init; } = [];

    public static SamplingProtocol Load(string path)
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

        var protocol = JsonSerializer.Deserialize<SamplingProtocol>(json, BenchmarkProtocol.SerializerOptions)
            ?? throw new InvalidDataException("Unable to deserialize sampling protocol.");
        protocol.ValidateAnalyses();
        return protocol;
    }

    private void ValidateAnalyses()
    {
        if (Analyses.Select(analysis => analysis.AnalysisId).Distinct(StringComparer.Ordinal).Count() != Analyses.Count)
        {
            throw new InvalidDataException("Sampling analysis IDs must be unique.");
        }

        var paths = Analyses.SelectMany(analysis => new[]
        {
            analysis.Outputs.Memberships,
            analysis.Outputs.Nulls,
            analysis.Outputs.Report,
        }).ToArray();
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
        {
            throw new InvalidDataException("Every sampling analysis output path must be distinct.");
        }
    }
}

public sealed class SamplingAnalysis
{
    public string AnalysisId { get; init; } = string.Empty;
    public string AnalysisSet { get; init; } = string.Empty;
    public IReadOnlyList<string> IncludedEntryKinds { get; init; } = [];
    public IReadOnlyList<int> SmallerSampleSizes { get; init; } = [];
    public IReadOnlyList<SamplingFrequencyBand> FrequencyBands { get; init; } = [];
    public IReadOnlyList<SamplingSource> Sources { get; init; } = [];
    public SamplingOutputPaths Outputs { get; init; } = new();
}

public sealed class SamplingSource
{
    public string SourceId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string LexiconPath { get; init; } = string.Empty;
}

public sealed class SamplingOutputPaths
{
    public string Memberships { get; init; } = string.Empty;
    public string Nulls { get; init; } = string.Empty;
    public string Report { get; init; } = string.Empty;
}

public sealed record SamplingReport(
    string SchemaVersion,
    string SamplingId,
    string AnalysisId,
    string AnalysisSet,
    int Seed,
    string GeneratorVersion,
    int LargestCommonSize,
    IReadOnlyList<int> SampleSizes,
    IReadOnlyDictionary<string, int> CandidateCounts,
    IReadOnlyList<SamplingShortage> Shortages,
    int MembershipRows,
    int NullRows);
