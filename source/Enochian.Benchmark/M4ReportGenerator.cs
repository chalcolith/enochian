using Enochian.Provenance;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed record M4LanguageDistribution(
    string Language,
    int RecordCount,
    double PercentileMinimum,
    double PercentileQ1,
    double PercentileMedian,
    double PercentileQ3,
    double PercentileMaximum,
    double StandardizedQ1,
    double StandardizedMedian,
    double StandardizedQ3,
    double RawDistanceMedian);

public sealed record M4WinnerSummary(
    string Level,
    int ComparisonCount,
    int SanskritWins,
    int TurkishWins,
    int Ties,
    double SanskritProportion,
    double TurkishProportion,
    double TieProportion);

public sealed record M4StratumSummary(
    string Dimension,
    string Stratum,
    int QueryCount,
    int ComparisonCount,
    double MedianDifference,
    int SanskritWins,
    int TurkishWins,
    int Ties);

public sealed record M4StabilitySummary(
    int Repetition,
    string SampleId,
    int QueryCount,
    double MedianDifference,
    int SanskritWins,
    int TurkishWins,
    int Ties);

public sealed record M4NullSummary(
    string Language,
    int RecordCount,
    int NullCountPerCell,
    double ObservedDistanceMedian,
    double NullMeanMedian,
    double NullStandardDeviationMedian,
    double StandardizedScoreMedian,
    double EmpiricalPercentileMedian);

public sealed record M4QualitySummary(
    string Language,
    string ProfileId,
    string Convention,
    string EvidenceOrigin,
    string Dependencies,
    int CompatibleUnits,
    int SupportedUnits,
    int UniqueUnits,
    int Band0305,
    int Band0609,
    int Band1020,
    string PronunciationGate,
    string RetrievalGate,
    string Status);

public sealed record M4RetrievalSummary(
    string Language,
    string Degradation,
    string LengthBand,
    int Queries,
    double RecallAt1,
    double RecallAt5,
    double RecallAt20,
    double MeanReciprocalRank,
    double? MeanNormalizedDistance,
    bool Passed);

public sealed record M4ExclusionSummary(
    string Language,
    bool Required,
    string Status,
    int SupportedUnits,
    string Blockers);

public sealed record M4FigureMetadata(
    string Id,
    string Path,
    string Title,
    string AltText,
    string Encoding,
    string RunId,
    string[] SourceArtifacts,
    string Sha256);

public sealed record M4ReportManifest(
    string SchemaVersion,
    string ReportId,
    string RunId,
    string Status,
    HashedArtifact SourceRunManifest,
    HashedArtifact[] SourceArtifacts,
    HashedArtifact[] ReportArtifacts,
    string DecisionRuleOutcome,
    bool MatchingOrStatisticsRerun,
    bool DefinitionsPresent,
    bool HoldoutPresent);

public sealed class M4ReportGenerator(string repositoryRoot)
{
    private const string EvaluationRunId = "73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1";
    private const string EvaluationCalibrationArtifact =
        "reports/evaluation-run/m4-01/artifacts/calibration.jsonl.gz";
    private const string EvaluationStatisticsArtifact =
        "reports/evaluation-run/m4-01/artifacts/statistics.jsonl.gz";
    private const string EvaluationAdmissionArtifact = "reports/evaluation-readiness/m3-07/admission.json";
    private readonly AutomatedFiles files = new(repositoryRoot);
    private string runId = EvaluationRunId;
    private string calibrationArtifact = EvaluationCalibrationArtifact;
    private string statisticsArtifact = EvaluationStatisticsArtifact;
    private string admissionArtifact = EvaluationAdmissionArtifact;
    private bool holdoutReport;

    public int Run(string publicationManifestPath, string outputDirectory)
    {
        var publication = JsonSerializer.Deserialize<M4RunPublicationManifest>(
            File.ReadAllText(publicationManifestPath, new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty M4-01 publication manifest.");
        if (publication.Status != "completed" || publication.DefinitionsPresent ||
            (!publication.HoldoutPresent && publication.RunId != EvaluationRunId))
        {
            throw new InvalidDataException("M4 reporting requires a completed, definition-free M4 run.");
        }

        VerifyPublication(publication);
        holdoutReport = publication.HoldoutPresent;
        runId = publication.RunId;
        calibrationArtifact = FindPackaged(publication, "calibration.jsonl.gz");
        statisticsArtifact = FindPackaged(publication, "statistics.jsonl.gz");
        admissionArtifact = publication.AdmissionReport.Path;
        var protocolPath = files.Resolve(publication.Protocol.Path);
        if (PronunciationAudit.HashConfiguration(protocolPath) != publication.Protocol.Sha256)
        {
            throw new InvalidDataException("Frozen report protocol hash mismatch.");
        }

        var protocol = AutomatedProtocol.Load(protocolPath, repositoryRoot);
        var queries = File.ReadLines(files.Pin(protocol.Queries!, configuration: false))
            .Select(line => JsonSerializer.Deserialize<AutomatedQuery>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty evaluation query."))
            .ToDictionary(query => query.QueryId, StringComparer.Ordinal);
        var calibration = ReadGzipLines<AutomatedCalibration>(
            files.Resolve(calibrationArtifact)).ToArray();
        var tests = ReadGzipLines<AutomatedTest>(files.Resolve(statisticsArtifact)).ToArray();
        var admission = JsonSerializer.Deserialize<AutomatedAdmissionReport>(
            File.ReadAllText(files.Resolve(admissionArtifact), new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty Stage A admission report.");
        if (calibration.Any(row => !queries.ContainsKey(row.QueryId)) ||
            tests.Any(test => test.Status is not ("estimated" or "not_estimable")))
        {
            throw new InvalidDataException("Report inputs do not match the frozen query or test inventory.");
        }

        var primary = tests.Single(test => test.Primary);
        var distributions = BuildDistributions(calibration);
        var pairs = BuildPairs(calibration, queries);
        var winners = BuildWinners(pairs);
        var strata = BuildStrata(pairs);
        var stability = BuildStability(pairs);
        var nulls = BuildNulls(calibration);
        var (quality, retrieval, exclusions) = BuildQuality(protocol, admission);

        var outputPath = files.Resolve(outputDirectory);
        var tablesPath = Path.Combine(outputPath, "tables");
        var figuresPath = Path.Combine(outputPath, "figures");
        _ = Directory.CreateDirectory(tablesPath);
        _ = Directory.CreateDirectory(figuresPath);
        WriteTables(tablesPath, tests, distributions, winners, strata, stability, nulls,
            quality, retrieval, exclusions);
        var figures = WriteFigures(figuresPath, primary, distributions, stability);
        var figureMetadataPath = Path.Combine(outputPath, "figure-metadata.json");
        AutomatedFiles.Write(figureMetadataPath, figures);
        var reportPath = Path.Combine(outputPath, "report.md");
        if (holdoutReport)
        {
            WriteHoldoutReport(
                reportPath,
                outputPath,
                publicationManifestPath,
                primary,
                tests,
                distributions,
                winners,
                strata,
                stability,
                nulls,
                quality,
                exclusions,
                figures);
        }
        else
        {
            WriteReport(
                reportPath,
                outputPath,
                publicationManifestPath,
                primary,
                tests,
                distributions,
                winners,
                strata,
                stability,
                nulls,
                quality,
                exclusions,
                figures);
        }
        var sourceArtifacts = new[]
        {
            ExperimentHashing.Artifact(files.Resolve(calibrationArtifact), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(statisticsArtifact), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(admissionArtifact), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(protocol.Queries!.Path), repositoryRoot),
        }.Concat(holdoutReport
            ? new[]
            {
                ExperimentHashing.Artifact(
                    files.Resolve("reports/evaluation-run/m4-01/manifest.json"), repositoryRoot),
                ExperimentHashing.Artifact(
                    files.Resolve(EvaluationStatisticsArtifact), repositoryRoot),
                ExperimentHashing.Artifact(
                    files.Resolve("reports/evaluation-report/m4-02/manifest.json"), repositoryRoot),
                ExperimentHashing.Artifact(
                    files.Resolve("reports/contextual-appendix/m4-03/manifest.json"), repositoryRoot),
                ExperimentHashing.Artifact(
                    files.Resolve("reports/holdout-materialization/m4-04/materialization.json"),
                    repositoryRoot),
                ExperimentHashing.Artifact(
                    files.Resolve("reports/holdout-materialization/m4-04/leakage-audit.json"),
                    repositoryRoot),
            }
            : []).ToArray();
        var reportArtifacts = Directory.EnumerateFiles(outputPath, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "manifest.json")
            .Order(StringComparer.Ordinal)
            .Select(path => ExperimentHashing.Artifact(path, repositoryRoot)).ToArray();
        AutomatedFiles.Write(Path.Combine(outputPath, "manifest.json"), new M4ReportManifest(
            "1.0.0",
            holdoutReport
                ? "m4-04-final-holdout-report-v1"
                : "m4-02-comparative-quantitative-report-v1",
            publication.RunId,
            "completed",
            ExperimentHashing.Artifact(publicationManifestPath, repositoryRoot),
            sourceArtifacts,
            reportArtifacts,
            OverallDecision(primary, stability) ? "met" : "not_met",
            false,
            false,
            holdoutReport));
        Console.WriteLine(
            $"Rendered {(holdoutReport ? "M4-04 final" : "M4-02 quantitative")} report for run {publication.RunId}.");
        return 0;
    }

    private static string FindPackaged(M4RunPublicationManifest publication, string fileName) =>
        publication.PackagedArtifacts.Single(artifact =>
            Path.GetFileName(artifact.Path) == fileName).Path;

    private static bool PartitionDecision(
        AutomatedTest primary,
        M4StabilitySummary[] stability) =>
        primary.Status == "estimated" &&
        primary.Estimate < 0 &&
        primary.Interval?.Upper < 0 &&
        primary.AdjustedPValue <= 0.05 &&
        stability.All(row => row.MedianDifference < 0);

    private bool OverallDecision(
        AutomatedTest primary,
        M4StabilitySummary[] stability)
    {
        if (!holdoutReport || !PartitionDecision(primary, stability))
        {
            return !holdoutReport && PartitionDecision(primary, stability);
        }

        var evaluationPrimary = ReadGzipLines<AutomatedTest>(
            files.Resolve(EvaluationStatisticsArtifact)).Single(test => test.Primary);
        var evaluationQueries = File.ReadLines(
                files.Resolve("resources/voynich/evaluation-queries.v1.jsonl"))
            .Select(line => JsonSerializer.Deserialize<AutomatedQuery>(
                line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty evaluation query."))
            .ToDictionary(query => query.QueryId, StringComparer.Ordinal);
        var evaluationStability = BuildStability(BuildPairs(
            [.. ReadGzipLines<AutomatedCalibration>(
                files.Resolve(EvaluationCalibrationArtifact))],
            evaluationQueries));
        return PartitionDecision(evaluationPrimary, evaluationStability) &&
            System.Math.Sign(evaluationPrimary.Estimate ?? 0) ==
            System.Math.Sign(primary.Estimate ?? 0);
    }

    private void VerifyPublication(M4RunPublicationManifest publication)
    {
        foreach (var artifact in publication.PackagedArtifacts.Concat(
            [publication.AdmissionReport, publication.PreflightReport]))
        {
            var path = files.Resolve(artifact.Path);
            if (!File.Exists(path) ||
                new FileInfo(path).Length != artifact.Bytes ||
                ExperimentHashing.HashFile(path) != artifact.Sha256)
            {
                throw new InvalidDataException("Frozen M4-01 artifact mismatch: " + artifact.Path);
            }
        }
    }

    private static M4LanguageDistribution[] BuildDistributions(AutomatedCalibration[] rows) =>
        [.. rows.GroupBy(row => row.Language, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var percentiles = group.Select(row => Required(row.Calibration.EmpiricalPercentile)).Order().ToArray();
                var standardized = group.Select(row => Required(row.Calibration.StandardizedScore)).Order().ToArray();
                var distances = group.Select(row => row.RawDistance).Order().ToArray();
                return new M4LanguageDistribution(
                    group.Key,
                    group.Count(),
                    percentiles[0],
                    Quantile(percentiles, 0.25),
                    Quantile(percentiles, 0.5),
                    Quantile(percentiles, 0.75),
                    percentiles[^1],
                    Quantile(standardized, 0.25),
                    Quantile(standardized, 0.5),
                    Quantile(standardized, 0.75),
                    Quantile(distances, 0.5));
            })];

    private static ReportPair[] BuildPairs(
        AutomatedCalibration[] rows,
        Dictionary<string, AutomatedQuery> queries) =>
        [.. rows.GroupBy(row => (row.SampleId, row.QueryId))
            .OrderBy(group => group.Key.SampleId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.QueryId, StringComparer.Ordinal)
            .Select(group =>
            {
                var sanskrit = group.Single(row => row.Language == "san");
                var turkish = group.Single(row => row.Language == "tur");
                var query = queries[group.Key.QueryId];
                return new ReportPair(
                    sanskrit.SampleId,
                    sanskrit.Repetition,
                    query.QueryId,
                    query.Locus,
                    QueryLengthBand(query.Symbols.Length),
                    query.TokenFrequency,
                    Required(sanskrit.Calibration.EmpiricalPercentile),
                    Required(turkish.Calibration.EmpiricalPercentile));
            })];

    private static M4WinnerSummary[] BuildWinners(ReportPair[] pairs)
    {
        var collapsed = pairs.GroupBy(pair => pair.QueryId, StringComparer.Ordinal)
            .Select(group => new Winner(Median(group.Select(pair => pair.Sanskrit)),
                Median(group.Select(pair => pair.Turkish)))).ToArray();
        return
        [
            WinnerSummary("sample-query", pairs.Select(pair => new Winner(pair.Sanskrit, pair.Turkish))),
            WinnerSummary("query-median", collapsed),
        ];
    }

    private static M4WinnerSummary WinnerSummary(string level, IEnumerable<Winner> values)
    {
        var winners = values.ToArray();
        var sanskrit = winners.Count(value => value.Sanskrit < value.Turkish);
        var turkish = winners.Count(value => value.Turkish < value.Sanskrit);
        var ties = winners.Length - sanskrit - turkish;
        return new(
            level,
            winners.Length,
            sanskrit,
            turkish,
            ties,
            (double)sanskrit / winners.Length,
            (double)turkish / winners.Length,
            (double)ties / winners.Length);
    }

    private static M4StratumSummary[] BuildStrata(ReportPair[] pairs)
    {
        var values = new List<M4StratumSummary>();
        Add("length", pairs.GroupBy(pair => pair.LengthBand, StringComparer.Ordinal));
        Add("section", pairs.GroupBy(pair => pair.Locus, StringComparer.Ordinal));
        return [.. values.OrderBy(value => value.Dimension, StringComparer.Ordinal)
            .ThenBy(value => value.Stratum, StringComparer.Ordinal)];

        void Add(string dimension, IEnumerable<IGrouping<string, ReportPair>> groups)
        {
            foreach (var group in groups)
            {
                var items = group.ToArray();
                values.Add(new(
                    dimension,
                    group.Key,
                    items.Select(item => item.QueryId).Distinct(StringComparer.Ordinal).Count(),
                    items.Length,
                    Median(items.Select(item => item.Difference)),
                    items.Count(item => item.Difference < 0),
                    items.Count(item => item.Difference > 0),
                    items.Count(item => item.Difference == 0)));
            }
        }
    }

    private static M4StabilitySummary[] BuildStability(ReportPair[] pairs) =>
        [.. pairs.GroupBy(pair => (pair.Repetition, pair.SampleId))
            .OrderBy(group => group.Key.Repetition)
            .Select(group =>
            {
                var items = group.ToArray();
                return new M4StabilitySummary(
                    group.Key.Repetition,
                    group.Key.SampleId,
                    items.Length,
                    Median(items.Select(item => item.Difference)),
                    items.Count(item => item.Difference < 0),
                    items.Count(item => item.Difference > 0),
                    items.Count(item => item.Difference == 0));
            })];

    private static M4NullSummary[] BuildNulls(AutomatedCalibration[] rows) =>
        [.. rows.GroupBy(row => row.Language, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new M4NullSummary(
                group.Key,
                group.Count(),
                group.Select(row => row.Calibration.NullCount).Distinct().Single(),
                Median(group.Select(row => row.RawDistance)),
                Median(group.Select(row => Required(row.Calibration.NullMean))),
                Median(group.Select(row => Required(row.Calibration.NullStandardDeviation))),
                Median(group.Select(row => Required(row.Calibration.StandardizedScore))),
                Median(group.Select(row => Required(row.Calibration.EmpiricalPercentile)))))];

    private (M4QualitySummary[] Quality, M4RetrievalSummary[] Retrieval, M4ExclusionSummary[] Exclusions)
        BuildQuality(AutomatedProtocol protocol, AutomatedAdmissionReport admission)
    {
        var profiles = PronunciationPanel.Load(
            files.Resolve(protocol.Panel.Path),
            repositoryRoot)
            .Profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        var sources = protocol.Sources.ToDictionary(source => source.ProfileId, StringComparer.Ordinal);
        var quality = admission.Sources.Where(source => source.Status == "admitted")
            .OrderBy(source => source.Language, StringComparer.Ordinal).Select(source =>
            {
                var configured = sources[source.ProfileId];
                var generated = configured.GeneratorApproval != null;
                return new M4QualitySummary(
                    source.Language,
                    source.ProfileId,
                    profiles[source.ProfileId].Convention,
                    generated ? "converter-generated; researcher-approved deterministic mapping" : "dictionary-supplied",
                    generated
                        ? "CDSL SHS b7297b9; resources/encodings/slp1.json; resources/encodings/features.json"
                        : configured.ProfileManifest.Path + " @ " + configured.ProfileManifest.Sha256,
                    source.CompatibleLexicalUnits,
                    source.SupportedLexicalUnits,
                    source.UniquePhonologicalUnits,
                    source.LengthBands.GetValueOrDefault("03-05"),
                    source.LengthBands.GetValueOrDefault("06-09"),
                    source.LengthBands.GetValueOrDefault("10-20"),
                    source.PronunciationGate,
                    source.RetrievalGate,
                    source.Status);
            }).ToArray();
        var retrieval = admission.Sources.Where(source => source.Status == "admitted")
            .SelectMany(source => source.Retrieval.Where(cell => cell.CandidateMode == "source-included")
                .Select(cell => new M4RetrievalSummary(
                    source.Language,
                    cell.DegradationId,
                    cell.LengthBand,
                    cell.LexicalQueries,
                    cell.Metrics.RecallAt1,
                    cell.Metrics.RecallAt5,
                    cell.Metrics.RecallAt20,
                    cell.Metrics.MeanReciprocalRank,
                    cell.Metrics.MeanNormalizedDistance,
                    cell.Passed)))
            .OrderBy(row => row.Language, StringComparer.Ordinal)
            .ThenBy(row => row.Degradation, StringComparer.Ordinal)
            .ThenBy(row => row.LengthBand, StringComparer.Ordinal).ToArray();
        var exclusions = admission.Sources.Where(source => source.Status != "admitted")
            .OrderBy(source => source.Language, StringComparer.Ordinal)
            .Select(source => new M4ExclusionSummary(
                source.Language,
                source.Required,
                source.Status,
                source.SupportedLexicalUnits,
                string.Join("; ", source.Blockers))).ToArray();
        return (quality, retrieval, exclusions);
    }

    private void WriteTables(
        string path,
        AutomatedTest[] tests,
        M4LanguageDistribution[] distributions,
        M4WinnerSummary[] winners,
        M4StratumSummary[] strata,
        M4StabilitySummary[] stability,
        M4NullSummary[] nulls,
        M4QualitySummary[] quality,
        M4RetrievalSummary[] retrieval,
        M4ExclusionSummary[] exclusions)
    {
        WriteCsv(Path.Combine(path, "contrasts.csv"),
            ["id", "contrast_id", "analysis_mode", "primary", "required", "status", "query_count",
                "estimate", "ci_lower", "ci_upper", "p_value", "adjusted_p_value",
                "correction_family_size", "diagnostic", "run_id", "source_artifact"],
            tests.Select(test => new[]
            {
                test.Id, test.ContrastId, test.AnalysisMode, Bool(test.Primary), Bool(test.Required),
                test.Status, Integer(test.QueryCount), Number(test.Estimate), Number(test.Interval?.Lower),
                Number(test.Interval?.Upper), Number(test.PValue), Number(test.AdjustedPValue),
                Integer(test.CorrectionFamilySize), test.Diagnostic ?? string.Empty, runId, statisticsArtifact,
            }));
        WriteCsv(Path.Combine(path, "language-distributions.csv"),
            ["language", "record_count", "percentile_minimum", "percentile_q1", "percentile_median",
                "percentile_q3", "percentile_maximum", "standardized_q1", "standardized_median",
                "standardized_q3", "raw_distance_median", "run_id", "source_artifact"],
            distributions.Select(row => new[]
            {
                row.Language, Integer(row.RecordCount), Number(row.PercentileMinimum), Number(row.PercentileQ1),
                Number(row.PercentileMedian), Number(row.PercentileQ3), Number(row.PercentileMaximum),
                Number(row.StandardizedQ1), Number(row.StandardizedMedian), Number(row.StandardizedQ3),
                Number(row.RawDistanceMedian), runId, calibrationArtifact,
            }));
        WriteCsv(Path.Combine(path, "winner-proportions.csv"),
            ["level", "comparison_count", "sanskrit_wins", "turkish_wins", "ties",
                "sanskrit_proportion", "turkish_proportion", "tie_proportion", "run_id", "source_artifact"],
            winners.Select(row => new[]
            {
                row.Level, Integer(row.ComparisonCount), Integer(row.SanskritWins), Integer(row.TurkishWins),
                Integer(row.Ties), Number(row.SanskritProportion), Number(row.TurkishProportion),
                Number(row.TieProportion), runId, calibrationArtifact,
            }));
        WriteCsv(Path.Combine(path, "strata.csv"),
            ["dimension", "stratum", "query_count", "comparison_count", "median_difference",
                "sanskrit_wins", "turkish_wins", "ties", "run_id", "source_artifact"],
            strata.Select(row => new[]
            {
                row.Dimension, row.Stratum, Integer(row.QueryCount), Integer(row.ComparisonCount),
                Number(row.MedianDifference), Integer(row.SanskritWins), Integer(row.TurkishWins),
                Integer(row.Ties), runId, calibrationArtifact,
            }));
        WriteCsv(Path.Combine(path, "stability.csv"),
            ["repetition", "sample_id", "query_count", "median_difference", "sanskrit_wins",
                "turkish_wins", "ties", "run_id", "source_artifact"],
            stability.Select(row => new[]
            {
                Integer(row.Repetition), row.SampleId, Integer(row.QueryCount), Number(row.MedianDifference),
                Integer(row.SanskritWins), Integer(row.TurkishWins), Integer(row.Ties), runId, calibrationArtifact,
            }));
        WriteCsv(Path.Combine(path, "null-summary.csv"),
            ["language", "record_count", "null_count_per_cell", "observed_distance_median",
                "null_mean_median", "null_standard_deviation_median", "standardized_score_median",
                "empirical_percentile_median", "run_id", "source_artifact"],
            nulls.Select(row => new[]
            {
                row.Language, Integer(row.RecordCount), Integer(row.NullCountPerCell),
                Number(row.ObservedDistanceMedian), Number(row.NullMeanMedian),
                Number(row.NullStandardDeviationMedian), Number(row.StandardizedScoreMedian),
                Number(row.EmpiricalPercentileMedian), runId, calibrationArtifact,
            }));
        WriteCsv(Path.Combine(path, "quality-summary.csv"),
            ["language", "profile_id", "convention", "evidence_origin", "dependencies",
                "compatible_units", "supported_units", "unique_units", "band_03_05", "band_06_09",
                "band_10_20", "pronunciation_gate", "retrieval_gate", "status", "run_id", "source_artifact"],
            quality.Select(row => new[]
            {
                row.Language, row.ProfileId, row.Convention, row.EvidenceOrigin, row.Dependencies,
                Integer(row.CompatibleUnits), Integer(row.SupportedUnits), Integer(row.UniqueUnits),
                Integer(row.Band0305), Integer(row.Band0609), Integer(row.Band1020),
                row.PronunciationGate, row.RetrievalGate, row.Status, runId, admissionArtifact,
            }));
        WriteCsv(Path.Combine(path, "retrieval.csv"),
            ["language", "degradation", "length_band", "queries", "recall_at_1", "recall_at_5",
                "recall_at_20", "mean_reciprocal_rank", "mean_normalized_distance", "passed",
                "run_id", "source_artifact"],
            retrieval.Select(row => new[]
            {
                row.Language, row.Degradation, row.LengthBand, Integer(row.Queries), Number(row.RecallAt1),
                Number(row.RecallAt5), Number(row.RecallAt20), Number(row.MeanReciprocalRank),
                Number(row.MeanNormalizedDistance), Bool(row.Passed), runId, admissionArtifact,
            }));
        WriteCsv(Path.Combine(path, "exclusions.csv"),
            ["language", "required", "status", "supported_units", "blockers", "run_id", "source_artifact"],
            exclusions.Select(row => new[]
            {
                row.Language, Bool(row.Required), row.Status, Integer(row.SupportedUnits), row.Blockers,
                runId, admissionArtifact,
            }));
    }

    private M4FigureMetadata[] WriteFigures(
        string path,
        AutomatedTest primary,
        M4LanguageDistribution[] distributions,
        M4StabilitySummary[] stability)
    {
        var primaryPath = Path.Combine(path, "primary-effect.svg");
        var intervalAlso = primary.Estimate == primary.Interval?.Lower &&
            primary.Estimate == primary.Interval?.Upper ? "also " : string.Empty;
        var intervalCrosses = primary.Interval?.Lower <= 0 && primary.Interval?.Upper >= 0
            ? "crosses"
            : "does not cross";
        var primaryAlt = $"Primary Sanskrit minus Turkish calibrated-percentile effect is " +
            $"{Format(primary.Estimate)}; the 95 percent interval is {intervalAlso}" +
            $"{Format(primary.Interval?.Lower)} to {Format(primary.Interval?.Upper)} and " +
            $"{intervalCrosses} the no-effect line.";
        WritePrimaryFigure(primaryPath, primary, primaryAlt);
        var distributionPath = Path.Combine(path, "language-distributions.svg");
        var distributionAlt = "Box-and-whisker summaries of standardized scores. " +
            "Sanskrit median is " + Format(distributions.Single(row => row.Language == "san").StandardizedMedian) +
            " and Turkish median is " + Format(distributions.Single(row => row.Language == "tur").StandardizedMedian) +
            "; more negative is farther above the biphone-null distance.";
        WriteDistributionFigure(distributionPath, distributions, distributionAlt);
        var stabilityPath = Path.Combine(path, "subsample-stability.svg");
        var stabilityAlt = stability.All(row => row.MedianDifference == 0)
            ? $"All {stability.Length} lexicon subsamples have median Sanskrit minus Turkish calibrated-percentile " +
                "difference 0.000, so every point lies on the no-effect line."
            : $"{stability.Count(row => row.MedianDifference < 0)} of {stability.Length} lexicon subsamples " +
                "have a negative median Sanskrit minus Turkish calibrated-percentile difference.";
        WriteStabilityFigure(stabilityPath, stability, stabilityAlt);
        return
        [
            Metadata("primary-effect", primaryPath, "Primary calibrated effect", primaryAlt,
                "Black diamond and horizontal interval against a dashed zero line.", [statisticsArtifact]),
            Metadata("language-distributions", distributionPath, "Language score distributions", distributionAlt,
                "Outlined box plots with circle and square median markers; no color distinction.",
                [calibrationArtifact]),
            Metadata("subsample-stability", stabilityPath, "Lexicon-subsample stability", stabilityAlt,
                "Numbered black circles connected by a solid line against a dashed zero line.",
                [calibrationArtifact]),
        ];
    }

    private M4FigureMetadata Metadata(
        string id,
        string path,
        string title,
        string alt,
        string encoding,
        string[] sources) =>
        new(
            id,
            Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
            title,
            alt,
            encoding,
            runId,
            sources,
            ExperimentHashing.HashFile(path));

    private static void WritePrimaryFigure(string path, AutomatedTest primary, string alt)
    {
        var estimate = Required(primary.Estimate);
        var lower = Required(primary.Interval?.Lower);
        var upper = Required(primary.Interval?.Upper);
        var x = Scale(estimate, -0.02, 0.02, 80, 680);
        var lowerX = Scale(lower, -0.02, 0.02, 80, 680);
        var upperX = Scale(upper, -0.02, 0.02, 80, 680);
        var svg = SvgStart("primary-title", "primary-desc", "Primary calibrated effect", alt) + $"""
              <line x1="80" y1="170" x2="680" y2="170" stroke="black" />
              <line x1="380" y1="70" x2="380" y2="210" stroke="black" stroke-dasharray="8 6" />
              <line x1="{lowerX}" y1="130" x2="{upperX}" y2="130" stroke="black" stroke-width="4" />
              <polygon points="{x},118 {x + 12},130 {x},142 {x - 12},130" fill="white" stroke="black" stroke-width="3" />
              <text x="80" y="195" font-size="16">-0.020</text>
              <text x="360" y="195" font-size="16">0</text>
              <text x="635" y="195" font-size="16">0.020</text>
              <text x="185" y="35" font-size="18">Sanskrit closer</text>
              <text x="445" y="35" font-size="18">Turkish closer</text>
              <text x="80" y="235" font-size="16">Sanskrit minus Turkish empirical percentile (lower is better)</text>
            </svg>
            """;
        WriteText(path, svg);
    }

    private static void WriteDistributionFigure(
        string path,
        M4LanguageDistribution[] rows,
        string alt)
    {
        var svg = new StringBuilder(SvgStart(
            "distribution-title", "distribution-desc", "Language score distributions", alt));
        _ = svg.AppendLine("""  <line x1="100" y1="220" x2="680" y2="220" stroke="black" />""");
        foreach (var (row, index) in rows.Select((value, index) => (value, index)))
        {
            var y = 90 + (index * 80);
            var q1 = Scale(row.StandardizedQ1, -12, -2, 100, 680);
            var median = Scale(row.StandardizedMedian, -12, -2, 100, 680);
            var q3 = Scale(row.StandardizedQ3, -12, -2, 100, 680);
            _ = svg.AppendLine(
                FormattableString.Invariant(
                    $"""  <rect x="{q1}" y="{y - 20}" width="{System.Math.Max(1, q3 - q1)}" height="40" fill="white" stroke="black" stroke-width="2" />"""));
            if (row.Language == "san")
            {
                _ = svg.AppendLine(FormattableString.Invariant(
                    $"""  <circle cx="{median}" cy="{y}" r="8" fill="black" />"""));
            }
            else
            {
                _ = svg.AppendLine(FormattableString.Invariant(
                    $"""  <rect x="{median - 7}" y="{y - 7}" width="14" height="14" fill="black" />"""));
            }

            _ = svg.AppendLine(
                FormattableString.Invariant(
                    $"""  <text x="25" y="{y + 6}" font-size="18">{row.Language.ToUpperInvariant()}</text>"""));
        }

        _ = svg.AppendLine("""  <text x="90" y="245" font-size="15">-12</text>""");
        _ = svg.AppendLine("""  <text x="655" y="245" font-size="15">-2</text>""");
        _ = svg.AppendLine("""  <text x="210" y="275" font-size="16">Standardized score (more negative = farther above null)</text>""");
        _ = svg.AppendLine("</svg>");
        WriteText(path, svg.ToString());
    }

    private static void WriteStabilityFigure(string path, M4StabilitySummary[] rows, string alt)
    {
        var points = string.Join(" ", rows.Select((row, index) =>
            FormattableString.Invariant($"{70 + (index * 32)},{Scale(row.MedianDifference, -0.02, 0.02, 210, 50)}")));
        var svg = new StringBuilder(SvgStart(
            "stability-title", "stability-desc", "Lexicon-subsample stability", alt));
        _ = svg.AppendLine("""  <line x1="70" y1="130" x2="678" y2="130" stroke="black" stroke-dasharray="8 6" />""");
        _ = svg.AppendLine(FormattableString.Invariant(
            $"""  <polyline points="{points}" fill="none" stroke="black" stroke-width="2" />"""));
        foreach (var (row, index) in rows.Select((value, index) => (value, index)))
        {
            var x = 70 + (index * 32);
            var y = Scale(row.MedianDifference, -0.02, 0.02, 210, 50);
            _ = svg.AppendLine(FormattableString.Invariant(
                $"""  <circle cx="{x}" cy="{y}" r="5" fill="white" stroke="black" stroke-width="2" />"""));
            _ = svg.AppendLine(FormattableString.Invariant(
                $"""  <text x="{x - 5}" y="245" font-size="11">{row.Repetition}</text>"""));
        }

        _ = svg.AppendLine("""  <text x="260" y="275" font-size="16">Lexicon repetition</text>""");
        _ = svg.AppendLine("</svg>");
        WriteText(path, svg.ToString());
    }

    private static string SvgStart(string titleId, string descriptionId, string title, string alt) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="760" height="300" viewBox="0 0 760 300"
             role="img" aria-labelledby="{titleId} {descriptionId}">
          <title id="{titleId}">{title}</title>
          <desc id="{descriptionId}">{alt}</desc>
          <rect width="760" height="300" fill="white" />
        """;

    private void WriteReport(
        string path,
        string outputPath,
        string publicationManifestPath,
        AutomatedTest primary,
        AutomatedTest[] tests,
        M4LanguageDistribution[] distributions,
        M4WinnerSummary[] winners,
        M4StratumSummary[] strata,
        M4StabilitySummary[] stability,
        M4NullSummary[] nulls,
        M4QualitySummary[] quality,
        M4ExclusionSummary[] exclusions,
        M4FigureMetadata[] figures)
    {
        var token = tests.Single(test =>
            test.ContrastId == primary.ContrastId && test.AnalysisMode == "token-weighted");
        var sampleWinners = winners.Single(row => row.Level == "sample-query");
        var queryWinners = winners.Single(row => row.Level == "query-median");
        var sourceManifestLink = Link(outputPath, publicationManifestPath);
        var lines = new List<string>
        {
            "# M4-02 comparative quantitative report",
            string.Empty,
            $"Run ID: `{runId}`. Frozen source bundle: [M4-01 manifest]({sourceManifestLink}).",
            "This report renders committed tidy outputs only. It did not rerun matching or statistics.",
            string.Empty,
            "## Primary confirmatory result",
            string.Empty,
            $"The preregistered Sanskrit-versus-Turkish comparison did **not** separate the languages. " +
                $"The Sanskrit-minus-Turkish calibrated-percentile effect was **{Format(primary.Estimate)}** " +
                $"with a 95% hierarchical-bootstrap interval of **[{Format(primary.Interval?.Lower)}, " +
                $"{Format(primary.Interval?.Upper)}]**, one-sided permutation p = **{Format(primary.PValue)}**, " +
                $"and Holm-adjusted p = **{Format(primary.AdjustedPValue)}**.",
            string.Empty,
            "| Analysis | Effect | 95% CI | p | Holm p | Queries | Decision |",
            "| --- | ---: | --- | ---: | ---: | ---: | --- |",
            $"| Type-primary | {Format(primary.Estimate)} | [{Format(primary.Interval?.Lower)}, " +
                $"{Format(primary.Interval?.Upper)}] | {Format(primary.PValue)} | " +
                $"{Format(primary.AdjustedPValue)} | {primary.QueryCount} | Not met |",
            string.Empty,
            $"Source: [contrasts.csv](tables/contrasts.csv), run `{runId}`, derived from " +
                $"[{statisticsArtifact}]({Link(outputPath, statisticsArtifact)}).",
            string.Empty,
            $"![{figures[0].AltText}](figures/primary-effect.svg)",
            string.Empty,
            "The expected direction was negative because lower calibrated percentiles indicate closer matches. " +
                "The observed effect was zero, its interval did not exclude zero, the corrected p-value exceeded " +
                "0.05, every lexicon-subsample median was zero, and holdout replication has not occurred. " +
                "**The full primary decision rule was not met.** This is a negative confirmatory finding, not " +
                "evidence that Sanskrit outperformed the unrelated Turkish control.",
            string.Empty,
            "## Planned sensitivity",
            string.Empty,
            "| Analysis | Effect | 95% CI | p | Holm p | Status |",
            "| --- | ---: | --- | ---: | ---: | --- |",
            $"| Token-frequency weighted | {Format(token.Estimate)} | [{Format(token.Interval?.Lower)}, " +
                $"{Format(token.Interval?.Upper)}] | {Format(token.PValue)} | " +
                $"{Format(token.AdjustedPValue)} | {token.Status} |",
            string.Empty,
            "The planned token-weighted sensitivity agrees with the type-primary result. No mapping variant or " +
                "post-run sensitivity was introduced.",
            string.Empty,
            "## Language distributions and winners",
            string.Empty,
            "| Language | N | Calibrated percentile min / median / max | Standardized score Q1 / median / Q3 | Raw distance median |",
            "| --- | ---: | --- | --- | ---: |",
        };
        lines.AddRange(distributions.Select(row =>
            $"| {LanguageName(row.Language)} | {row.RecordCount} | {Format(row.PercentileMinimum)} / " +
            $"{Format(row.PercentileMedian)} / {Format(row.PercentileMaximum)} | " +
            $"{Format(row.StandardizedQ1)} / {Format(row.StandardizedMedian)} / " +
            $"{Format(row.StandardizedQ3)} | {Format(row.RawDistanceMedian)} |"));
        lines.AddRange(
        [
            string.Empty,
            $"At the sample-query level, Sanskrit won {sampleWinners.SanskritWins} of " +
                $"{sampleWinners.ComparisonCount} comparisons, Turkish won {sampleWinners.TurkishWins}, " +
                $"and {sampleWinners.Ties} tied. After collapsing lexicon repetitions by query medians, " +
                $"all {queryWinners.ComparisonCount} query types tied.",
            string.Empty,
            $"Sources: [language-distributions.csv](tables/language-distributions.csv) and " +
                $"[winner-proportions.csv](tables/winner-proportions.csv), run `{runId}`, from " +
                $"[{calibrationArtifact}]({Link(outputPath, calibrationArtifact)}).",
            string.Empty,
            $"![{figures[1].AltText}](figures/language-distributions.svg)",
            string.Empty,
            "## Length and section strata",
            string.Empty,
            "| Dimension | Stratum | Queries | Comparisons | Median effect | Sanskrit wins | Turkish wins | Ties |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |",
        ]);
        lines.AddRange(strata.Select(row =>
            $"| {row.Dimension} | {row.Stratum} | {row.QueryCount} | {row.ComparisonCount} | " +
            $"{Format(row.MedianDifference)} | {row.SanskritWins} | {row.TurkishWins} | {row.Ties} |"));
        lines.AddRange(
        [
            string.Empty,
            $"Source: [strata.csv](tables/strata.csv), run `{runId}`, from calibrated tidy rows joined only " +
                "to frozen query length and locus metadata.",
            string.Empty,
            "## Lexicon-subsample stability",
            string.Empty,
            $"All {stability.Length} preregistered lexicon repetitions had median effect 0.000. " +
                "The result is stable, but stably null rather than supportive.",
            string.Empty,
            $"Source: [stability.csv](tables/stability.csv), run `{runId}`, from " +
                $"[{calibrationArtifact}]({Link(outputPath, calibrationArtifact)}).",
            string.Empty,
            $"![{figures[2].AltText}](figures/subsample-stability.svg)",
            string.Empty,
            "## Null calibration",
            string.Empty,
            "| Language | Cells | Null draws/cell | Observed distance median | Null mean median | Null SD median | Standardized median | Percentile median |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |",
        ]);
        lines.AddRange(nulls.Select(row =>
            $"| {LanguageName(row.Language)} | {row.RecordCount} | {row.NullCountPerCell} | " +
            $"{Format(row.ObservedDistanceMedian)} | {Format(row.NullMeanMedian)} | " +
            $"{Format(row.NullStandardDeviationMedian)} | {Format(row.StandardizedScoreMedian)} | " +
            $"{Format(row.EmpiricalPercentileMedian)} |"));
        lines.AddRange(
        [
            string.Empty,
            "Observed distances were generally larger than the biphone-pseudoword null, placing almost every " +
                "cell at the upper empirical-percentile boundary. This ceiling produced the widespread ties; " +
                "it was not adjusted after inspection.",
            string.Empty,
            $"Source: [null-summary.csv](tables/null-summary.csv), run `{runId}`, from " +
                $"[{calibrationArtifact}]({Link(outputPath, calibrationArtifact)}).",
            string.Empty,
            "## Stage A pronunciation and retrieval quality",
            string.Empty,
            "| Language | Convention | Evidence basis | Compatible / supported / unique | Bands 03-05 / 06-09 / 10-20 | Pronunciation | Retrieval |",
            "| --- | --- | --- | ---: | --- | --- | --- |",
        ]);
        lines.AddRange(quality.Select(row =>
            $"| {LanguageName(row.Language)} | {row.Convention} | {row.EvidenceOrigin} | " +
            $"{row.CompatibleUnits} / {row.SupportedUnits} / {row.UniqueUnits} | " +
            $"{row.Band0305} / {row.Band0609} / " +
            $"{row.Band1020} | {row.PronunciationGate} | {row.RetrievalGate} |"));
        lines.AddRange(
        [
            string.Empty,
            "Sanskrit uses the explicitly approved deterministic CDSL SHS/SLP1 converter; this is researcher " +
                "approval of a pinned mapping, not human pronunciation accuracy. Turkish uses dictionary-supplied " +
                "readings. Automated reference agreement is not required for either admission route. Both passed " +
                "every required source-included retrieval cell.",
            string.Empty,
            $"Sources: [quality-summary.csv](tables/quality-summary.csv) and " +
                $"[retrieval.csv](tables/retrieval.csv), run `{runId}`, from " +
                $"[{admissionArtifact}]({Link(outputPath, admissionArtifact)}).",
            string.Empty,
            "## Exclusions and missing comparisons",
            string.Empty,
            "| Language | Required | Status | Supported units | Reason |",
            "| --- | --- | --- | ---: | --- |",
        ]);
        lines.AddRange(exclusions.Select(row =>
            $"| {row.Language} | {Bool(row.Required)} | {row.Status} | {row.SupportedUnits} | " +
            $"{row.Blockers.Replace("|", "\\|", StringComparison.Ordinal)} |"));
        lines.AddRange(
        [
            string.Empty,
            $"Source: [exclusions.csv](tables/exclusions.csv), run `{runId}`.",
            string.Empty,
            "Only Sanskrit and Turkish were admitted. The three optional exploratory contrasts that require " +
                "Arabic, Latin/Greek, or Persian were `not_estimable`; they are reported below rather than omitted.",
            string.Empty,
            "## Exploratory contrasts",
            string.Empty,
            "| Contrast | Mode | Status | Diagnostic |",
            "| --- | --- | --- | --- |",
        ]);
        lines.AddRange(tests.Where(test => !test.Required).Select(test =>
            $"| {test.ContrastId} | {test.AnalysisMode} | {test.Status} | {test.Diagnostic} |"));
        lines.AddRange(
        [
            string.Empty,
            $"Source: [contrasts.csv](tables/contrasts.csv), run `{runId}`. These contrasts are separate " +
                "from the primary confirmatory claim and supply no effect estimate.",
            string.Empty,
            "## Multiplicity, precision, and provenance",
            string.Empty,
            "- Multiplicity family: four registered contrasts x two analysis modes = eight fixed Holm slots.",
            "- Display precision: effects, intervals, p-values, distances, and proportions are rounded to three decimals; CSV tables retain six decimals.",
            "- Replication unit: query type. Languages are not treated as independent replicates.",
            "- Holdout loci `f3r`, `f3v`, `f4r`, and `f4v` remain unopened.",
            "- Definitions and semantic judgments are absent.",
            $"- Figure metadata: [figure-metadata.json](figure-metadata.json), run `{runId}`.",
            string.Empty,
            "Rebuild from the frozen bundle without rerunning matching or statistics:",
            string.Empty,
            "```powershell",
            "dotnet run --project Enochian.Benchmark -- report-m4 ..\\reports\\evaluation-run\\m4-01\\manifest.json",
            "```",
        ]);
        WriteText(path, string.Join("\n", lines) + "\n");
    }

    private void WriteHoldoutReport(
        string path,
        string outputPath,
        string publicationManifestPath,
        AutomatedTest primary,
        AutomatedTest[] tests,
        M4LanguageDistribution[] distributions,
        M4WinnerSummary[] winners,
        M4StratumSummary[] strata,
        M4StabilitySummary[] stability,
        M4NullSummary[] nulls,
        M4QualitySummary[] quality,
        M4ExclusionSummary[] exclusions,
        M4FigureMetadata[] figures)
    {
        var evaluationPrimary = ReadGzipLines<AutomatedTest>(
            files.Resolve(EvaluationStatisticsArtifact)).Single(test => test.Primary);
        var evaluationQueries = File.ReadLines(
                files.Resolve("resources/voynich/evaluation-queries.v1.jsonl"))
            .Select(line => JsonSerializer.Deserialize<AutomatedQuery>(
                line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty evaluation query."))
            .ToDictionary(query => query.QueryId, StringComparer.Ordinal);
        var evaluationStability = BuildStability(BuildPairs(
            [.. ReadGzipLines<AutomatedCalibration>(
                files.Resolve(EvaluationCalibrationArtifact))],
            evaluationQueries));
        var evaluationMet = PartitionDecision(evaluationPrimary, evaluationStability);
        var holdoutMet = PartitionDecision(primary, stability);
        var replicationMet = evaluationMet && holdoutMet &&
            System.Math.Sign(evaluationPrimary.Estimate ?? 0) == System.Math.Sign(primary.Estimate ?? 0);
        var token = tests.Single(test =>
            test.ContrastId == primary.ContrastId && test.AnalysisMode == "token-weighted");
        var sampleWinners = winners.Single(row => row.Level == "sample-query");
        var queryWinners = winners.Single(row => row.Level == "query-median");
        var lines = new List<string>
        {
            "# M4-04 holdout replication and final report",
            string.Empty,
            $"Holdout run ID: `{runId}`. Frozen source bundle: " +
                $"[M4-04 manifest]({Link(outputPath, publicationManifestPath)}).",
            "The preregistered holdout was opened once after the automated leakage audit passed. " +
                "The unchanged M4-01 runner, mapping, contrast family, seeds, thresholds, and statistics were used.",
            string.Empty,
            "## Replication decision",
            string.Empty,
            "| Partition | Effect | 95% CI | Holm p | Partition rule |",
            "| --- | ---: | --- | ---: | --- |",
            $"| Evaluation | {Format(evaluationPrimary.Estimate)} | " +
                $"[{Format(evaluationPrimary.Interval?.Lower)}, {Format(evaluationPrimary.Interval?.Upper)}] | " +
                $"{Format(evaluationPrimary.AdjustedPValue)} | {(evaluationMet ? "Met" : "Not met")} |",
            $"| Holdout | {Format(primary.Estimate)} | " +
                $"[{Format(primary.Interval?.Lower)}, {Format(primary.Interval?.Upper)}] | " +
                $"{Format(primary.AdjustedPValue)} | {(holdoutMet ? "Met" : "Not met")} |",
            string.Empty,
            $"**The frozen replication rule was {(replicationMet ? "met" : "not met")}.** " +
                "Support required a negative effect, an interval excluding zero, Holm-adjusted p <= 0.05, " +
                "negative effects in every lexicon subsample, and the same result in both partitions. " +
                "A negative replication is reported without changing the mapping or analysis.",
            string.Empty,
            $"Sources: [holdout contrasts](tables/contrasts.csv), " +
                $"[{statisticsArtifact}]({Link(outputPath, statisticsArtifact)}), and " +
                $"[{EvaluationStatisticsArtifact}]({Link(outputPath, EvaluationStatisticsArtifact)}).",
            string.Empty,
            $"![{figures[0].AltText}](figures/primary-effect.svg)",
            string.Empty,
            "## Holdout primary and planned sensitivity",
            string.Empty,
            "| Analysis | Effect | 95% CI | p | Holm p | Queries | Status |",
            "| --- | ---: | --- | ---: | ---: | ---: | --- |",
            $"| Type-primary | {Format(primary.Estimate)} | [{Format(primary.Interval?.Lower)}, " +
                $"{Format(primary.Interval?.Upper)}] | {Format(primary.PValue)} | " +
                $"{Format(primary.AdjustedPValue)} | {primary.QueryCount} | {primary.Status} |",
            $"| Token-frequency weighted | {Format(token.Estimate)} | [{Format(token.Interval?.Lower)}, " +
                $"{Format(token.Interval?.Upper)}] | {Format(token.PValue)} | " +
                $"{Format(token.AdjustedPValue)} | {token.QueryCount} | {token.Status} |",
            string.Empty,
            "## Holdout distributions and winners",
            string.Empty,
            "| Language | N | Percentile min / median / max | Standardized median | Raw distance median |",
            "| --- | ---: | --- | ---: | ---: |",
        };
        lines.AddRange(distributions.Select(row =>
            $"| {LanguageName(row.Language)} | {row.RecordCount} | {Format(row.PercentileMinimum)} / " +
            $"{Format(row.PercentileMedian)} / {Format(row.PercentileMaximum)} | " +
            $"{Format(row.StandardizedMedian)} | {Format(row.RawDistanceMedian)} |"));
        lines.AddRange(
        [
            string.Empty,
            $"Sanskrit won {sampleWinners.SanskritWins} of {sampleWinners.ComparisonCount} sample-query " +
                $"comparisons; Turkish won {sampleWinners.TurkishWins}; {sampleWinners.Ties} tied. " +
                $"After collapsing repetitions, {queryWinners.Ties} of {queryWinners.ComparisonCount} query types tied.",
            string.Empty,
            $"![{figures[1].AltText}](figures/language-distributions.svg)",
            string.Empty,
            "## Holdout length and locus strata",
            string.Empty,
            "| Dimension | Stratum | Queries | Comparisons | Median effect | Sanskrit wins | Turkish wins | Ties |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |",
        ]);
        lines.AddRange(strata.Select(row =>
            $"| {row.Dimension} | {row.Stratum} | {row.QueryCount} | {row.ComparisonCount} | " +
            $"{Format(row.MedianDifference)} | {row.SanskritWins} | {row.TurkishWins} | {row.Ties} |"));
        lines.AddRange(
        [
            string.Empty,
            "## Stability and null calibration",
            string.Empty,
            $"{stability.Count(row => row.MedianDifference < 0)} of {stability.Length} frozen repetitions " +
                "had an effect in the expected negative direction.",
            string.Empty,
            $"![{figures[2].AltText}](figures/subsample-stability.svg)",
            string.Empty,
            "| Language | Cells | Null draws/cell | Observed median | Null mean median | Percentile median |",
            "| --- | ---: | ---: | ---: | ---: | ---: |",
        ]);
        lines.AddRange(nulls.Select(row =>
            $"| {LanguageName(row.Language)} | {row.RecordCount} | {row.NullCountPerCell} | " +
            $"{Format(row.ObservedDistanceMedian)} | {Format(row.NullMeanMedian)} | " +
            $"{Format(row.EmpiricalPercentileMedian)} |"));
        lines.AddRange(
        [
            string.Empty,
            "## Stage A quality and exclusions",
            string.Empty,
            "| Language | Compatible / supported / unique | Pronunciation | Retrieval |",
            "| --- | ---: | --- | --- |",
        ]);
        lines.AddRange(quality.Select(row =>
            $"| {LanguageName(row.Language)} | {row.CompatibleUnits} / {row.SupportedUnits} / " +
            $"{row.UniqueUnits} | {row.PronunciationGate} | {row.RetrievalGate} |"));
        lines.AddRange(
        [
            string.Empty,
            $"{exclusions.Count(row => row.Status != "admitted")} profiles remained excluded; " +
                "the holdout did not change admission.",
            string.Empty,
            "## Final research bundle",
            string.Empty,
            "| Component | Frozen artifact |",
            "| --- | --- |",
            $"| Protocol and decision rule | [protocol-v2]({Link(outputPath, "experiments/protocol-v2.md")}) |",
            $"| Holdout protocol | [confirmatory.holdout.m4-04.json]({Link(outputPath, "experiments/confirmatory.holdout.m4-04.json")}) |",
            $"| Mapping approval | [decision package]({Link(outputPath, "reports/mapping-preregistration/v1/decision-package.json")}) |",
            $"| Pre-open leakage audit | [leakage audit]({Link(outputPath, "reports/holdout-materialization/m4-04/leakage-audit.json")}) |",
            $"| Holdout query provenance | [materialization]({Link(outputPath, "reports/holdout-materialization/m4-04/materialization.md")}) |",
            $"| Evaluation run and report | [M4-01]({Link(outputPath, "reports/evaluation-run/m4-01/README.md")}); " +
                $"[M4-02]({Link(outputPath, "reports/evaluation-report/m4-02/report.md")}) |",
            $"| Holdout run | [M4-04 bundle]({Link(outputPath, "reports/holdout-run/m4-04/README.md")}) |",
            $"| Optional context | [M4-03]({Link(outputPath, "reports/contextual-appendix/m4-03/report.md")}) |",
            $"| Licenses and acquisition | [lexicons]({Link(outputPath, "resources/lexicons/README.md")}); " +
                $"[Voynich transcription]({Link(outputPath, "resources/voynich/links.md")}) |",
            string.Empty,
            "Every displayed holdout number is present in the CSV tables and traces to the holdout run ID. " +
                "Definitions and glosses were absent from both primary partitions. The optional contextual appendix " +
                "was never consumed by this analysis.",
            string.Empty,
            "## Reproduction",
            string.Empty,
            "From `source/` with the pinned SDK and retained licensed snapshots:",
            string.Empty,
            "```powershell",
            "dotnet run --project Enochian.Provenance -- voynich-holdout ..\\experiments\\holdout-materialization.m4-04.json",
            "dotnet run --project Enochian.Benchmark -- preflight ..\\experiments\\confirmatory.holdout.m4-04.json",
            "dotnet run --project Enochian.Benchmark -- experiment ..\\experiments\\confirmatory.holdout.m4-04.json",
            "dotnet run --project Enochian.Benchmark -- publish-holdout-m4 ..\\experiments\\confirmatory.holdout.m4-04.json c8b258128eb161ff5ef0d82551bc15460f69af65",
            "dotnet run --project Enochian.Benchmark -- report-m4 ..\\reports\\holdout-run\\m4-04\\manifest.json reports/final-report/m4-04",
            "```",
            string.Empty,
            "The materializer refuses a second write after publication; reproduce in a fresh checkout/output path. " +
                "The unrestricted experiment does not require BHSA. BHSA is optional only for rebuilding the " +
                "separate Tiberian Hebrew source; the lexicon guide documents its authorized acquisition.",
            string.Empty,
            "## Limitations",
            string.Empty,
            "- The fixed Vogt-derived mapping remains speculative.",
            "- Calibrated-percentile ceiling effects can create extensive ties.",
            "- The holdout has no scorable 10-20-symbol query type; no long-band result was imputed.",
            "- Language profiles differ in source coverage; languages are not independent replicates.",
            "- The holdout result is final under this preregistration; no mapping or threshold was refit.",
            "- Semantic context and astronomy remain secondary and cannot rescue failed phonological replication.",
            string.Empty,
            "## Artifact inventory",
            string.Empty,
            "- [contrasts.csv](tables/contrasts.csv)",
            "- [language-distributions.csv](tables/language-distributions.csv)",
            "- [winner-proportions.csv](tables/winner-proportions.csv)",
            "- [strata.csv](tables/strata.csv)",
            "- [stability.csv](tables/stability.csv)",
            "- [null-summary.csv](tables/null-summary.csv)",
            "- [quality-summary.csv](tables/quality-summary.csv)",
            "- [retrieval.csv](tables/retrieval.csv)",
            "- [exclusions.csv](tables/exclusions.csv)",
            "- [figure-metadata.json](figure-metadata.json)",
        ]);
        WriteText(path, string.Join("\n", lines) + "\n");
    }

    private static IEnumerable<T> ReadGzipLines<T>(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
        while (reader.ReadLine() is { } line)
        {
            yield return JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty compressed report row.");
        }
    }

    private static void WriteCsv(string path, string[] headers, IEnumerable<string[]> rows)
    {
        var lines = new[] { headers }.Concat(rows).Select(row => string.Join(",", row.Select(Csv)));
        WriteText(path, string.Join("\n", lines) + "\n");
    }

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    private static double Quantile(double[] sorted, double probability)
    {
        var position = (sorted.Length - 1) * probability;
        var lower = (int)System.Math.Floor(position);
        var upper = (int)System.Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    private static double Median(IEnumerable<double> values) =>
        Quantile([.. values.Order()], 0.5);

    private static double Required(double? value) =>
        value ?? throw new InvalidDataException("Required report statistic is missing.");

    private static string QueryLengthBand(int length) => length switch
    {
        <= 2 => "01-02",
        <= 5 => "03-05",
        <= 9 => "06-09",
        _ => "10-20",
    };

    private static int Scale(double value, double minimum, double maximum, int start, int end)
    {
        var proportion = (value - minimum) / (maximum - minimum);
        return (int)System.Math.Round(start + (proportion * (end - start)));
    }

    private static string Number(double? value) =>
        value?.ToString("0.000000", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Format(double? value) =>
        value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "NA";

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string LanguageName(string language) => language switch
    {
        "san" => "Sanskrit",
        "tur" => "Turkish",
        _ => language,
    };

    private string Link(string outputPath, string targetPath)
    {
        var fullTarget = Path.IsPathRooted(targetPath)
            ? targetPath
            : files.Resolve(targetPath);
        return Path.GetRelativePath(outputPath, fullTarget).Replace('\\', '/');
    }

    private static void WriteText(string path, string text)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    private sealed record ReportPair(
        string SampleId,
        int Repetition,
        string QueryId,
        string Locus,
        string LengthBand,
        int TokenFrequency,
        double Sanskrit,
        double Turkish)
    {
        public double Difference => Sanskrit - Turkish;
    }

    private sealed record Winner(double Sanskrit, double Turkish);
}
