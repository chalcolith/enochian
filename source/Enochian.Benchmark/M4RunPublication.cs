using Enochian.Provenance;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Enochian.Benchmark;

public sealed record M4ScoreCheck(
    string SampleId,
    string LengthBand,
    string QueryId,
    string Language,
    double RecordedDistance,
    double ReproducedDistance,
    double AbsoluteDifference);

public sealed record M4VerificationReport(
    string SchemaVersion,
    string RunId,
    string Status,
    bool OutputHashesVerified,
    bool SchemasVerified,
    bool DefinitionsAbsent,
    bool HoldoutAbsent,
    string SampleId,
    IReadOnlyDictionary<string, long> RowCounts,
    M4ScoreCheck[] ScoreChecks,
    string[] Blockers);

public sealed record M4ExecutionLog(
    string SchemaVersion,
    string RunId,
    string Command,
    int ExitCode,
    string RunnerCommit,
    string RunnerVersion,
    string CompletedAtUtc,
    string SmokeStatus,
    string PreflightStatus,
    string RerunPolicy);

public sealed record M4RunPublicationManifest(
    string SchemaVersion,
    string PublicationId,
    string RunId,
    string Status,
    string RunnerCommit,
    string RunnerVersion,
    PinnedInput Protocol,
    string[] EvaluationLoci,
    string[] HoldoutLoci,
    HashedArtifact AdmissionReport,
    HashedArtifact PreflightReport,
    HashedArtifact ExecutionLog,
    HashedArtifact VerificationReport,
    HashedArtifact[] RawArtifacts,
    HashedArtifact[] PackagedArtifacts,
    bool DefinitionsPresent,
    bool HoldoutPresent,
    string FailurePolicy);

public sealed partial class M4RunPublication(string repositoryRoot)
{
    private static readonly string[] EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"];
    private static readonly string[] HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"];
    private static readonly string[] OutputNames =
    [
        "sampling.jsonl",
        "matching.jsonl",
        "calibration.jsonl",
        "statistics.jsonl",
        "report-inputs.json",
        "run-manifest.json",
    ];
    private static readonly string[] ForbiddenSemanticProperties =
        ["\"definition\"", "\"definitions\"", "\"gloss\"", "\"meaning\""];

    public int Run(string protocolPath, string runnerCommit) =>
        RunPartition(protocolPath, runnerCommit, holdout: false);

    public int RunHoldout(string protocolPath, string runnerCommit) =>
        RunPartition(protocolPath, runnerCommit, holdout: true);

    private int RunPartition(string protocolPath, string runnerCommit, bool holdout)
    {
        if (!CommitPattern().IsMatch(runnerCommit))
        {
            throw new InvalidDataException("Runner commit must be a full lowercase Git SHA-1.");
        }

        var protocol = AutomatedProtocol.Load(protocolPath, repositoryRoot);
        if (protocol.Phase != "confirmatory" || protocol.PreflightReport == null)
        {
            throw new InvalidDataException("M4 publication requires a frozen confirmatory protocol with preflight.");
        }

        var files = new AutomatedFiles(repositoryRoot);
        var activeLoci = holdout ? HoldoutLoci : EvaluationLoci;
        var excludedLoci = holdout ? EvaluationLoci : HoldoutLoci;
        if (!protocol.EvaluationLoci.SequenceEqual(activeLoci, StringComparer.Ordinal) ||
            !protocol.HoldoutLoci.SequenceEqual(excludedLoci, StringComparer.Ordinal))
        {
            throw new InvalidDataException("M4 publication protocol identifies the wrong active partition.");
        }

        var publicationDirectoryName = holdout
            ? "reports/holdout-run/m4-04"
            : "reports/evaluation-run/m4-01";
        var publicationId = holdout
            ? "m4-04-holdout-partition-v1"
            : "m4-01-evaluation-partition-v1";
        var publicationDirectory = files.Resolve(publicationDirectoryName);
        var publicationManifestPath = Path.Combine(publicationDirectory, "manifest.json");
        if (File.Exists(publicationManifestPath))
        {
            VerifyExistingPublication(
                publicationManifestPath,
                protocolPath,
                runnerCommit,
                files);
            Console.WriteLine("The immutable M4-01 publication already exists and all published hashes match.");
            return 0;
        }

        var outputDirectory = files.Resolve(protocol.OutputDirectory);
        var runManifestPath = Path.Combine(outputDirectory, "run-manifest.json");
        var reportInputsPath = Path.Combine(outputDirectory, "report-inputs.json");
        var run = JsonSerializer.Deserialize<AutomatedRun>(
            File.ReadAllText(runManifestPath, new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Missing automated run manifest.");
        if (run.SchemaVersion != "2.0.0" || run.RunnerVersion != AutomatedExperiment.RunnerVersion ||
            run.Status != "completed")
        {
            throw new InvalidDataException("Only a completed automated v2 run can be published.");
        }

        using var reportInputs = JsonDocument.Parse(
            File.ReadAllText(reportInputsPath, new UTF8Encoding(false, true)));
        if (reportInputs.RootElement.GetProperty("run_id").GetString() != run.RunId ||
            reportInputs.RootElement.GetProperty("phase").GetString() != "confirmatory")
        {
            throw new InvalidDataException("Run manifest and report inputs do not identify the same confirmatory run.");
        }

        var rawArtifacts = VerifyRawArtifacts(run, outputDirectory);
        var verification = VerifyRows(protocol, run, outputDirectory, excludedLoci, holdout);
        if (verification.Blockers.Length != 0)
        {
            throw new InvalidDataException(
                "M4 run verification failed: " + string.Join(", ", verification.Blockers));
        }

        var artifactDirectory = Path.Combine(publicationDirectory, "artifacts");
        _ = Directory.CreateDirectory(artifactDirectory);
        var packagedArtifacts = OutputNames.Select(name =>
            Compress(
                Path.Combine(outputDirectory, name),
                Path.Combine(artifactDirectory, name + ".gz"))).ToArray();
        var executionLogPath = Path.Combine(publicationDirectory, "execution-log.json");
        AutomatedFiles.Write(executionLogPath, new M4ExecutionLog(
            "1.0.0",
            run.RunId,
            "dotnet run --project Enochian.Benchmark --no-build -- experiment " +
                (holdout
                    ? "../experiments/confirmatory.holdout.m4-04.json"
                    : "../experiments/confirmatory.readiness.m3-07.json"),
            0,
            runnerCommit,
            run.RunnerVersion,
            File.GetLastWriteTimeUtc(runManifestPath).ToString("O", CultureInfo.InvariantCulture),
            "passed_fresh_synthetic_fixture",
            "passed",
            "Never patch or overwrite this run. Any input or software defect invalidates the run; " +
                "version the fix and execute a fresh output directory."));
        var verificationPath = Path.Combine(publicationDirectory, "verification.json");
        AutomatedFiles.Write(verificationPath, verification);
        var admissionPath = files.Resolve(protocol.AdmissionReport);
        var preflightPath = files.Resolve(protocol.PreflightReport);
        var manifest = new M4RunPublicationManifest(
            "1.0.0",
            publicationId,
            run.RunId,
            "completed",
            runnerCommit,
            run.RunnerVersion,
            new(
                Path.GetRelativePath(repositoryRoot, protocolPath).Replace('\\', '/'),
                PronunciationAudit.HashConfiguration(protocolPath)),
            EvaluationLoci,
            HoldoutLoci,
            ExperimentHashing.Artifact(admissionPath, repositoryRoot),
            ExperimentHashing.Artifact(preflightPath, repositoryRoot),
            ExperimentHashing.Artifact(executionLogPath, repositoryRoot),
            ExperimentHashing.Artifact(verificationPath, repositoryRoot),
            rawArtifacts,
            packagedArtifacts,
            false,
            holdout,
            "Defective runs are labelled invalid and retained unchanged. Fixes require a versioned " +
                "runner or input, a new output directory, and a complete restart.");
        AutomatedFiles.Write(publicationManifestPath, manifest);
        WriteSummary(Path.Combine(publicationDirectory, "README.md"), manifest, verification, holdout);
        Console.WriteLine($"Published verified {(holdout ? "M4-04 holdout" : "M4-01 evaluation")} run {run.RunId}.");
        return 0;
    }

    private static void VerifyExistingPublication(
        string manifestPath,
        string protocolPath,
        string runnerCommit,
        AutomatedFiles files)
    {
        var manifest = JsonSerializer.Deserialize<M4RunPublicationManifest>(
            File.ReadAllText(manifestPath, new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty M4 publication manifest.");
        if (manifest.Status != "completed" ||
            manifest.RunnerCommit != runnerCommit ||
            manifest.Protocol.Sha256 != PronunciationAudit.HashConfiguration(protocolPath))
        {
            throw new InvalidDataException(
                "The existing M4 publication identifies a different run, runner, or protocol.");
        }

        foreach (var artifact in manifest.PackagedArtifacts.Concat(
            [
                manifest.AdmissionReport,
                manifest.PreflightReport,
                manifest.ExecutionLog,
                manifest.VerificationReport,
            ]))
        {
            var path = files.Resolve(artifact.Path);
            if (!File.Exists(path) ||
                new FileInfo(path).Length != artifact.Bytes ||
                ExperimentHashing.HashFile(path) != artifact.Sha256)
            {
                throw new InvalidDataException(
                    "Published M4 artifact is missing or modified: " + artifact.Path);
            }
        }
    }

    private HashedArtifact[] VerifyRawArtifacts(AutomatedRun run, string outputDirectory)
    {
        var expectedStages = new[] { "sampling", "matching", "calibration", "statistics", "report-inputs" };
        if (!run.Stages.Select(stage => stage.Name).SequenceEqual(expectedStages))
        {
            throw new InvalidDataException("Run manifest has an incomplete or reordered stage inventory.");
        }

        var artifacts = new List<HashedArtifact>();
        foreach (var name in OutputNames)
        {
            var path = Path.Combine(outputDirectory, name);
            var artifact = ExperimentHashing.Artifact(path, repositoryRoot);
            if (name != "run-manifest.json")
            {
                var recorded = run.Stages.Single(stage =>
                    stage.Outputs.Any(output => Path.GetFileName(output.Path) == name)).Outputs.Single();
                if (artifact.Sha256 != recorded.Sha256 || artifact.Bytes != recorded.Bytes)
                {
                    throw new InvalidDataException("Run output hash mismatch: " + name);
                }
            }

            artifacts.Add(artifact);
        }

        return [.. artifacts];
    }

    private M4VerificationReport VerifyRows(
        AutomatedProtocol protocol,
        AutomatedRun run,
        string outputDirectory,
        string[] excludedLoci,
        bool holdout)
    {
        var blockers = new List<string>();
        var forbidden = excludedLoci.Select(value => "\"" + value + "\"").ToArray();
        var queries = File.ReadLines(new AutomatedFiles(repositoryRoot).Pin(protocol.Queries!, configuration: false))
            .Select(line => JsonSerializer.Deserialize<AutomatedQuery>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty evaluation query."))
            .OrderBy(query => query.QueryId, StringComparer.Ordinal).ToArray();
        var mapping = JsonSerializer.Deserialize<SortedDictionary<string, double[]>>(
            File.ReadAllText(new AutomatedFiles(repositoryRoot).Pin(protocol.Mapping!)),
            AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty evaluation mapping.");
        if (queries.Any(query => excludedLoci.Contains(query.Locus, StringComparer.Ordinal)))
        {
            blockers.Add(holdout ? "evaluation_query" : "holdout_query");
        }

        var memberships = ReadAndValidate<AutomatedMembership>(
            Path.Combine(outputDirectory, "sampling.jsonl"), forbidden, blockers, holdout).ToArray();
        var sampleId = memberships.Select(row => row.SampleId).Order(StringComparer.Ordinal).First();
        var sample = memberships.Where(row => row.SampleId == sampleId).ToArray();
        var selectedQueries = queries
            .GroupBy(query => BenchmarkSampling.GetLengthBand(query.Symbols.Length), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
        if (selectedQueries.Length < 3)
        {
            blockers.Add("missing_query_length_stratum");
        }

        var targets = (from query in selectedQueries
                       from language in sample.Select(row => row.Unit.Language).Distinct(StringComparer.Ordinal)
                           .Order(StringComparer.Ordinal)
                       select (Sample: sampleId, Query: query.QueryId, Language: language))
            .ToHashSet();
        var observed = new Dictionary<(string Sample, string Query, string Language), AutomatedScore>();
        long matchingCount = 0;
        foreach (var line in File.ReadLines(
            Path.Combine(outputDirectory, "matching.jsonl"), new UTF8Encoding(false, true)))
        {
            CheckForbidden(line, forbidden, blockers, holdout);
            var row = JsonSerializer.Deserialize<AutomatedScore>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty matching row.");
            matchingCount++;
            if (!double.IsFinite(row.Distance) || row.SchemaVersion != "2.0.0")
            {
                blockers.Add("invalid_matching_row");
            }

            var key = (row.SampleId, row.QueryId, row.Language);
            if (row.NullKind == "observed" && targets.Contains(key) && !observed.TryAdd(key, row))
            {
                blockers.Add("duplicate_reproduction_row");
            }
        }

        var checks = new List<M4ScoreCheck>();
        foreach (var target in targets.OrderBy(value => value.Query, StringComparer.Ordinal)
            .ThenBy(value => value.Language, StringComparer.Ordinal))
        {
            if (!observed.TryGetValue(target, out var row))
            {
                blockers.Add("missing_reproduction_row");
                continue;
            }

            var query = selectedQueries.Single(value => value.QueryId == target.Query);
            var queryPhones = query.Symbols.Select(symbol => mapping[symbol]).ToArray();
            var reproduced = sample.Where(value => value.Unit.Language == target.Language)
                .Min(value => IndependentCandidateDistance(queryPhones, value.Unit));
            var difference = System.Math.Abs(row.Distance - reproduced);
            if (difference > 1e-12)
            {
                blockers.Add("score_reproduction_mismatch");
            }

            checks.Add(new(
                target.Sample,
                BenchmarkSampling.GetLengthBand(query.Symbols.Length),
                query.QueryId,
                target.Language,
                row.Distance,
                reproduced,
                difference));
        }

        var calibrationCount = ReadAndValidate<AutomatedCalibration>(
            Path.Combine(outputDirectory, "calibration.jsonl"), forbidden, blockers, holdout).LongCount();
        var statisticsCount = ReadAndValidate<AutomatedTest>(
            Path.Combine(outputDirectory, "statistics.jsonl"), forbidden, blockers, holdout).LongCount();
        foreach (var name in new[] { "report-inputs.json", "run-manifest.json" })
        {
            CheckForbidden(
                File.ReadAllText(Path.Combine(outputDirectory, name), new UTF8Encoding(false, true)),
                forbidden,
                blockers,
                holdout);
        }

        var rowCounts = new SortedDictionary<string, long>(StringComparer.Ordinal)
        {
            ["calibration"] = calibrationCount,
            ["matching"] = matchingCount,
            ["sampling"] = memberships.LongLength,
            ["statistics"] = statisticsCount,
        };
        var distinctBlockers = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(
            "1.0.0",
            run.RunId,
            distinctBlockers.Length == 0 ? "passed" : "failed",
            true,
            distinctBlockers.All(value => !value.StartsWith("invalid_", StringComparison.Ordinal)),
            !distinctBlockers.Contains("semantic_property_present", StringComparer.Ordinal),
            !holdout,
            sampleId,
            rowCounts,
            [.. checks],
            distinctBlockers);
    }

    private static IEnumerable<T> ReadAndValidate<T>(
        string path,
        string[] forbidden,
        List<string> blockers,
        bool holdout)
    {
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            CheckForbidden(line, forbidden, blockers, holdout);
            yield return JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty " + typeof(T).Name + " row.");
        }
    }

    private static void CheckForbidden(
        string text,
        string[] forbidden,
        List<string> blockers,
        bool holdout)
    {
        if (ForbiddenSemanticProperties.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add("semantic_property_present");
        }

        if (forbidden.Any(value => text.Contains(value, StringComparison.Ordinal)))
        {
            blockers.Add(holdout ? "evaluation_locus_present" : "holdout_locus_present");
        }
    }

    private static double IndependentCandidateDistance(
        IReadOnlyList<double[]> query,
        EvidenceUnit candidate) =>
        candidate.Readings.Sum(reading => reading.Weight * IndependentDtw(query, reading.Phones));

    private static double IndependentDtw(
        IReadOnlyList<double[]> source,
        IReadOnlyList<double[]> target)
    {
        var costs = new double[source.Count + 1, target.Count + 1];
        var lengths = new int[source.Count + 1, target.Count + 1];
        for (var sourceIndex = 0; sourceIndex <= source.Count; sourceIndex++)
        {
            for (var targetIndex = 0; targetIndex <= target.Count; targetIndex++)
            {
                costs[sourceIndex, targetIndex] = double.MaxValue;
            }
        }

        costs[0, 0] = 0;
        for (var sourceIndex = 1; sourceIndex <= source.Count; sourceIndex++)
        {
            for (var targetIndex = 1; targetIndex <= target.Count; targetIndex++)
            {
                var (predecessorCost, predecessorLength, _) = new[]
                {
                    (Cost: costs[sourceIndex - 1, targetIndex - 1],
                        Length: lengths[sourceIndex - 1, targetIndex - 1], Step: 0),
                    (Cost: costs[sourceIndex - 1, targetIndex],
                        Length: lengths[sourceIndex - 1, targetIndex], Step: 1),
                    (Cost: costs[sourceIndex, targetIndex - 1],
                        Length: lengths[sourceIndex, targetIndex - 1], Step: 2),
                }.OrderBy(value => value.Cost)
                    .ThenBy(value => value.Length)
                    .ThenBy(value => value.Step)
                    .First();
                costs[sourceIndex, targetIndex] = predecessorCost +
                    IndependentEuclidean(source[sourceIndex - 1], target[targetIndex - 1]);
                lengths[sourceIndex, targetIndex] = predecessorLength + 1;
            }
        }

        return costs[source.Count, target.Count] / lengths[source.Count, target.Count];
    }

    private static double IndependentEuclidean(double[] left, double[] right)
    {
        if (left.Length != right.Length)
        {
            throw new InvalidDataException("Independent score reproduction found mismatched feature dimensions.");
        }

        double sum = 0;
        for (var index = 0; index < left.Length; index++)
        {
            var difference = left[index] - right[index];
            sum += difference * difference;
        }

        return System.Math.Sqrt(sum);
    }

    private HashedArtifact Compress(string inputPath, string outputPath)
    {
        using (var input = File.OpenRead(inputPath))
        using (var output = File.Create(outputPath))
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            input.CopyTo(gzip);
        }

        return ExperimentHashing.Artifact(outputPath, repositoryRoot);
    }

    private static void WriteSummary(
        string path,
        M4RunPublicationManifest manifest,
        M4VerificationReport verification,
        bool holdout)
    {
        if (holdout)
        {
            var holdoutText = $"""
                # M4-04 frozen holdout run

                Run `{manifest.RunId}` completed once under unchanged runner `{manifest.RunnerVersion}`
                at commit `{manifest.RunnerCommit}`. This bundle records execution and verification;
                interpretation is published in the M4-04 final report.

                - Evaluation loci excluded: `{string.Join("`, `", manifest.EvaluationLoci)}`
                - Holdout loci executed: `{string.Join("`, `", manifest.HoldoutLoci)}`
                - Raw outputs: {manifest.RawArtifacts.Length}
                - Packaged outputs: {manifest.PackagedArtifacts.Length}
                - Verification: **{verification.Status}**
                - Independently reproduced score cells: {verification.ScoreChecks.Length}
                - Definitions present: **{manifest.DefinitionsPresent.ToString().ToLowerInvariant()}**
                - Holdout present: **{manifest.HoldoutPresent.ToString().ToLowerInvariant()}**

                ## Failure and rerun policy

                {manifest.FailurePolicy}
                """;
            File.WriteAllText(
                path,
                holdoutText.ReplaceLineEndings("\n") + "\n",
                new UTF8Encoding(false));
            return;
        }

        var text = $"""
            # M4-01 frozen evaluation run

            Run `{manifest.RunId}` completed under runner `{manifest.RunnerVersion}` at commit
            `{manifest.RunnerCommit}`. This bundle records execution and verification only;
            interpretation belongs to M4-02.

            - Evaluation loci: `{string.Join("`, `", manifest.EvaluationLoci)}`
            - Holdout loci excluded: `{string.Join("`, `", manifest.HoldoutLoci)}`
            - Raw outputs: {manifest.RawArtifacts.Length}
            - Packaged outputs: {manifest.PackagedArtifacts.Length}
            - Verification: **{verification.Status}**
            - Independently reproduced score cells: {verification.ScoreChecks.Length}
            - Definitions present: **{manifest.DefinitionsPresent.ToString().ToLowerInvariant()}**
            - Holdout present: **{manifest.HoldoutPresent.ToString().ToLowerInvariant()}**

            ## Failure and rerun policy

            {manifest.FailurePolicy}
            """;
        File.WriteAllText(path, text.ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));
    }

    [GeneratedRegex("^[a-f0-9]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}
