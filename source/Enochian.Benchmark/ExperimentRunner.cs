using Enochian.Provenance;
using Enochian.Text;
using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class ExperimentRunner(string repositoryRoot, FeatureSet features, Text.Encoding encoding)
{
    public const string RunnerVersion = "experiment-runner-v1";

    private readonly string repositoryRoot = Path.GetFullPath(repositoryRoot);
    private readonly ExperimentMatchStage matchStage = new(features, encoding);
    private readonly SamplingRunner samplingRunner = new(repositoryRoot, features, encoding);
    private readonly StatisticsRunner statisticsRunner = new(repositoryRoot);

    public int Run(string protocolPath)
    {
        var resolvedProtocol = Path.GetFullPath(protocolPath);
        var root = Path.GetDirectoryName(resolvedProtocol)!;
        var protocol = ExperimentRunProtocol.Load(resolvedProtocol);
        var paths = ResolvePaths(protocol, root);
        try
        {
            Validate(protocol, paths);
        }
        catch (Exception exception)
        {
            throw StageException("validation", protocol.ConfigId, exception);
        }

        var inputs = GetInputs(resolvedProtocol, paths, root);
        var runId = ExperimentHashing.HashValues(inputs.Select(value => $"{value.Path}:{value.Sha256}"));
        var previous = LoadManifest(paths.RunManifest);
        var stages = new List<ExperimentStageRecord>();
        RunStage("sampling", protocol, runId, inputs.Select(value => value.Sha256).Append(runId), GetSamplingOutputs(paths.SamplingProtocol),
            () => samplingRunner.Run(paths.SamplingProtocol), previous, stages, paths.RunManifest, inputs, root);
        RunStage("matching", protocol, runId,
            stages.SelectMany(value => value.Outputs).Select(value => value.Sha256)
                .Append(protocol.DtwTolerance.ToString("R", CultureInfo.InvariantCulture)).Append(runId),
            [paths.MatchScores, paths.NearestDistances],
            () =>
            {
                var result = matchStage.Run(paths.SamplingProtocol, protocol.Families, protocol.DtwTolerance);
                WriteJsonLines(paths.MatchScores, result.Scores);
                WriteJsonLines(paths.NearestDistances, result.NearestDistances);
                return 0;
            }, previous, stages, paths.RunManifest, inputs, root);
        RunStage("statistics", protocol, runId,
            [ExperimentHashing.HashFile(paths.NearestDistances), ExperimentHashing.HashFile(paths.StatisticsProtocol), runId],
            GetStatisticsOutputs(paths.StatisticsProtocol),
            () => statisticsRunner.Run(paths.StatisticsProtocol), previous, stages, paths.RunManifest, inputs, root);
        RunStage("report-inputs", protocol, runId,
            stages.Where(value => value.StageId == "statistics").SelectMany(value => value.Outputs)
                .Select(value => value.Sha256).Append(runId),
            [paths.ReportInputs],
            () =>
            {
                var tables = stages.Single(value => value.StageId == "statistics").Outputs;
                WriteJson(paths.ReportInputs, new ExperimentReportInputs("1.0.0", runId, protocol.ConfigId, tables));
                return 0;
            }, previous, stages, paths.RunManifest, inputs, root);
        WriteManifest(paths.RunManifest, CreateManifest(protocol, runId, inputs, stages));
        return 0;
    }

    private void Validate(ExperimentRunProtocol protocol, ResolvedPaths paths)
    {
        if (protocol.RunnerVersion != RunnerVersion)
        {
            throw new InvalidDataException($"Runner version '{protocol.RunnerVersion}' is not supported; expected '{RunnerVersion}'.");
        }

        ValidateJson(paths.Experiment, paths.ExperimentSchema);
        using var document = JsonDocument.Parse(File.ReadAllText(paths.Experiment));
        var experiment = document.RootElement;
        if (experiment.GetProperty("experiment_id").GetString() != protocol.ConfigId ||
            experiment.GetProperty("phase").GetString() != protocol.Phase)
        {
            throw new InvalidDataException("Runner config ID and phase must match the experiment protocol.");
        }

        ValidatePartitions(experiment);
        var manifestValidator = new ManifestValidator(repositoryRoot, paths.ManifestSchema);
        var report = manifestValidator.Validate(paths.Manifests);
        if (!report.IsValid)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, report.Issues));
        }

        var sampling = SamplingProtocol.Load(paths.SamplingProtocol);
        var statistics = StatisticsProtocol.Load(paths.StatisticsProtocol);
        if (Path.GetFullPath(ResolveStatisticsInput(paths.StatisticsProtocol, statistics.InputPath)) != paths.NearestDistances)
        {
            throw new InvalidDataException("Statistics input_path must be the runner's nearest_distances_path.");
        }

        if (protocol.Phase == "confirmatory")
        {
            ValidateConfirmatory(protocol, experiment, sampling, statistics, paths.Manifests);
        }
    }

    private static void ValidateConfirmatory(
        ExperimentRunProtocol protocol,
        JsonElement experiment,
        SamplingProtocol sampling,
        StatisticsProtocol statistics,
        IReadOnlyList<string> manifests)
    {
        if (!experiment.GetProperty("frozen").GetBoolean())
        {
            throw new InvalidDataException("Confirmatory experiment protocols must be frozen.");
        }

        if (experiment.GetProperty("dtw").GetProperty("mapping_selection").GetString() != "predeclared_single")
        {
            throw new InvalidDataException("Confirmatory runs require one predeclared mapping.");
        }

        if (!string.IsNullOrWhiteSpace(protocol.DefinitionsPath))
        {
            throw new InvalidDataException("Confirmatory scoring exports must not include definitions.");
        }

        var randomization = experiment.GetProperty("randomization");
        var seeds = randomization.GetProperty("seeds");
        if (sampling.Seed != seeds.GetProperty("sampling").GetInt32() ||
            sampling.Seed != seeds.GetProperty("null_generation").GetInt32() ||
            sampling.Repetitions != randomization.GetProperty("sample_count").GetInt32() ||
            statistics.Seed != randomization.GetProperty("seeds").GetProperty("permutation").GetInt32() ||
            statistics.PermutationCount != randomization.GetProperty("permutation_count").GetInt32() ||
            statistics.ConfidenceLevel != experiment.GetProperty("statistics").GetProperty("confidence_level").GetDouble())
        {
            throw new InvalidDataException("Confirmatory stage protocols do not match frozen randomization settings.");
        }

        var entryKinds = experiment.GetProperty("lexicon_filters").GetProperty("entry_kinds")
            .EnumerateArray().Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
        if (sampling.Analyses.Any(analysis => !entryKinds.SetEquals(analysis.IncludedEntryKinds)))
        {
            throw new InvalidDataException("Confirmatory sampling filters do not match the frozen lexicon filters.");
        }

        foreach (var path in manifests)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(path));
            if (manifest.RootElement.GetProperty("status").GetString() != "acquired" ||
                manifest.RootElement.GetProperty("revision").GetProperty("kind").GetString() == "unresolved" ||
                manifest.RootElement.GetProperty("sha256").ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Confirmatory source manifest '{path}' is not pinned and acquired.");
            }
        }
    }

    private static void ValidatePartitions(JsonElement experiment)
    {
        var split = experiment.GetProperty("corpus_split");
        var evaluation = split.GetProperty("evaluation_partition").GetProperty("loci").EnumerateArray()
            .Select(value => value.GetString()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var holdout = split.GetProperty("holdout_partition").GetProperty("loci").EnumerateArray()
            .Select(value => value.GetString()).OfType<string>();
        if (evaluation.Overlaps(holdout))
        {
            throw new InvalidDataException("Evaluation and holdout partitions overlap.");
        }
    }

    private static void RunStage(
        string stageId,
        ExperimentRunProtocol protocol,
        string runId,
        IEnumerable<string> inputHashes,
        IReadOnlyList<string> outputPaths,
        Func<int> execute,
        ExperimentRunManifest? previous,
        List<ExperimentStageRecord> stages,
        string manifestPath,
        IReadOnlyList<HashedArtifact> inputs,
        string root)
    {
        var inputHash = ExperimentHashing.HashValues(inputHashes);
        var oldStage = previous?.Stages.SingleOrDefault(value => value.StageId == stageId);
        if (oldStage != null && oldStage.InputSha256 == inputHash && OutputsMatch(oldStage.Outputs, root))
        {
            stages.Add(oldStage);
            return;
        }

        try
        {
            if (execute() != 0)
            {
                throw new InvalidOperationException("Stage returned a non-zero exit code.");
            }
        }
        catch (Exception exception)
        {
            throw StageException(stageId, protocol.ConfigId, exception);
        }

        var outputs = outputPaths.Order(StringComparer.Ordinal)
            .Select(path => ExperimentHashing.Artifact(path, root)).ToArray();
        stages.Add(new(stageId, inputHash, outputs));
        WriteManifest(manifestPath, CreateManifest(protocol, runId, inputs, stages));
    }

    private static bool OutputsMatch(IEnumerable<HashedArtifact> outputs, string root) => outputs.All(output =>
    {
        var path = Path.GetFullPath(Path.Combine(root, output.Path.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(path) && ExperimentHashing.HashFile(path) == output.Sha256;
    });

    private static ExperimentRunManifest CreateManifest(
        ExperimentRunProtocol protocol,
        string runId,
        IReadOnlyList<HashedArtifact> inputs,
        IReadOnlyList<ExperimentStageRecord> stages)
    {
        var artifacts = stages.SelectMany(value => value.Outputs).OrderBy(value => value.Path, StringComparer.Ordinal).ToArray();
        return new("1.0.0", RunnerVersion, runId, protocol.ConfigId, protocol.Phase, false,
            inputs, [.. stages.OrderBy(value => value.StageId, StringComparer.Ordinal)], artifacts);
    }

    private static HashedArtifact[] GetInputs(
        string protocolPath,
        ResolvedPaths paths,
        string root)
    {
        var sampling = SamplingProtocol.Load(paths.SamplingProtocol);
        var sourceFiles = sampling.Analyses.SelectMany(value => value.Sources)
            .Select(value => ResolveStatisticsInput(paths.SamplingProtocol, value.LexiconPath));
        string[] queryFiles = [ResolveStatisticsInput(paths.SamplingProtocol, sampling.QueriesPath)];
        return [.. new[] { protocolPath, paths.Experiment, paths.ExperimentSchema, paths.SamplingProtocol,
                paths.StatisticsProtocol, paths.ManifestSchema }
            .Concat(paths.Manifests).Concat(sourceFiles).Concat(queryFiles)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(path => ExperimentHashing.Artifact(path, root))];
    }

    private static IReadOnlyList<string> GetSamplingOutputs(string protocolPath)
    {
        var protocol = SamplingProtocol.Load(protocolPath);
        var root = Path.GetDirectoryName(protocolPath)!;
        return [.. protocol.Analyses.SelectMany(value => new[]
        {
            ExperimentRunProtocol.Resolve(value.Outputs.Memberships, root),
            ExperimentRunProtocol.Resolve(value.Outputs.Nulls, root),
            ExperimentRunProtocol.Resolve(value.Outputs.Report, root),
        })];
    }

    private static IReadOnlyList<string> GetStatisticsOutputs(string protocolPath)
    {
        var protocol = StatisticsProtocol.Load(protocolPath);
        var root = Path.GetDirectoryName(protocolPath)!;
        return [.. protocol.Outputs.AllPaths.Select(value => ExperimentRunProtocol.Resolve(value, root))];
    }

    private static string ResolveStatisticsInput(string protocolPath, string value) =>
        ExperimentRunProtocol.Resolve(value, Path.GetDirectoryName(protocolPath)!);

    private static void ValidateJson(string instancePath, string schemaPath)
    {
        using var instance = JsonDocument.Parse(File.ReadAllText(instancePath));
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(instance.RootElement).IsValid)
        {
            throw new InvalidDataException($"'{instancePath}' does not conform to '{schemaPath}'.");
        }
    }

    private static ExperimentRunManifest? LoadManifest(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<ExperimentRunManifest>(File.ReadAllText(path), BenchmarkProtocol.SerializerOptions)
        : null;

    private static void WriteJsonLines<T>(string path, IEnumerable<T> rows) => WriteAtomically(path, temporary =>
    {
        using var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var row in rows)
        {
            writer.WriteLine(JsonSerializer.Serialize(row, BenchmarkProtocol.LineSerializerOptions));
        }
    });

    private static void WriteJson<T>(string path, T value) => WriteAtomically(path, temporary => File.WriteAllText(
        temporary, JsonSerializer.Serialize(value, BenchmarkProtocol.SerializerOptions).ReplaceLineEndings("\n") + "\n",
        new UTF8Encoding(false)));

    private static void WriteManifest(string path, ExperimentRunManifest manifest) => WriteJson(path, manifest);

    private static void WriteAtomically(string path, Action<string> write)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            write(temporary);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static InvalidOperationException StageException(string stage, string configId, Exception exception) =>
        new($"Stage '{stage}' failed for config '{configId}': {exception.Message}", exception);

    private static ResolvedPaths ResolvePaths(ExperimentRunProtocol protocol, string root) => new(
        ExperimentRunProtocol.Resolve(protocol.ExperimentPath, root),
        ExperimentRunProtocol.Resolve(protocol.ExperimentSchemaPath, root),
        ExperimentRunProtocol.Resolve(protocol.SamplingProtocolPath, root),
        ExperimentRunProtocol.Resolve(protocol.StatisticsProtocolPath, root),
        ExperimentRunProtocol.Resolve(protocol.ManifestSchemaPath, root),
        [.. protocol.Manifests.Select(value => ExperimentRunProtocol.Resolve(value, root))],
        ExperimentRunProtocol.Resolve(protocol.MatchScoresPath, root),
        ExperimentRunProtocol.Resolve(protocol.NearestDistancesPath, root),
        ExperimentRunProtocol.Resolve(protocol.ReportInputsPath, root),
        ExperimentRunProtocol.Resolve(protocol.RunManifestPath, root));

    private sealed record ResolvedPaths(
        string Experiment,
        string ExperimentSchema,
        string SamplingProtocol,
        string StatisticsProtocol,
        string ManifestSchema,
        IReadOnlyList<string> Manifests,
        string MatchScores,
        string NearestDistances,
        string ReportInputs,
        string RunManifest);
}
