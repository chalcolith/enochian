using Enochian.Benchmark;
using Enochian.Provenance;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Enochian.UnitTests;

[TestClass]
public sealed partial class M4HoldoutTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string MaterializationRoot = Path.Combine(
        RepositoryRoot, "reports", "holdout-materialization", "m4-04");
    private static readonly string RunRoot = Path.Combine(
        RepositoryRoot, "reports", "holdout-run", "m4-04");
    private static readonly string ReportRoot = Path.Combine(
        RepositoryRoot, "reports", "final-report", "m4-04");
    private static readonly string[] HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"];

    [TestMethod]
    public void HoldoutWasOpenedAfterPassedLeakageAuditWithFrozenMapping()
    {
        var report = Read<HoldoutQueryReport>(Path.Combine(MaterializationRoot, "materialization.json"));
        Assert.AreEqual("completed", report.Status);
        Assert.IsTrue(report.HoldoutOpened);
        Assert.AreEqual(343, report.AcceptedTokens);
        Assert.AreEqual(2, report.ExcludedTokens);
        Assert.AreEqual(227, report.UniqueTypes);
        Assert.AreEqual("passed", report.LeakageAudit.Status);
        Assert.IsEmpty(report.LeakageAudit.Blockers);
        Assert.AreEqual(83, report.LeakageAudit.ExperimentConfigsChecked);
        Assert.AreEqual(3, report.LeakageAudit.RunManifestsChecked);
        Assert.AreEqual(
            "4bc43e1e9d0fd2f29a6f4e8de804fe460f367f75bc3f796af5e7e48a7ecfb16e",
            report.MappingSha256);

        var queriesPath = PathOf("resources/voynich/holdout-queries.m4-04.jsonl");
        Assert.AreEqual(report.QueriesSha256, PronunciationAudit.Hash(queriesPath));
        var queries = File.ReadLines(queriesPath, new UTF8Encoding(false, true))
            .Select(line => JsonSerializer.Deserialize<AutomatedQuery>(
                line, AutomatedProtocol.JsonOptions)
                ?? throw new AssertFailedException("Empty holdout query."))
            .ToArray();
        Assert.HasCount(report.UniqueTypes, queries);
        CollectionAssert.AreEquivalent(
            HoldoutLoci,
            queries.Select(query => query.Locus).Distinct(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(queries.All(query =>
            query.QueryId.StartsWith("holdout-eva-type-", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void HoldoutChangesOnlyPartitionQueriesAndOutputIdentity()
    {
        var evaluation = AutomatedProtocol.Load(
            PathOf("experiments/confirmatory.readiness.m3-07.json"), RepositoryRoot);
        var holdout = AutomatedProtocol.Load(
            PathOf("experiments/confirmatory.holdout.m4-04.json"), RepositoryRoot);
        Assert.AreEqual(evaluation.Phase, holdout.Phase);
        Assert.AreEqual(evaluation.Phonology, holdout.Phonology);
        Assert.AreEqual(evaluation.Panel, holdout.Panel);
        Assert.AreEqual(evaluation.RetrievalPolicy, holdout.RetrievalPolicy);
        Assert.AreEqual(evaluation.RetrievalRunner, holdout.RetrievalRunner);
        Assert.AreEqual(evaluation.SamplingSeed, holdout.SamplingSeed);
        Assert.AreEqual(evaluation.NullSeed, holdout.NullSeed);
        Assert.AreEqual(evaluation.PermutationSeed, holdout.PermutationSeed);
        Assert.AreEqual(evaluation.Repetitions, holdout.Repetitions);
        Assert.AreEqual(evaluation.NullRepetitions, holdout.NullRepetitions);
        Assert.AreEqual(evaluation.PermutationCount, holdout.PermutationCount);
        Assert.AreEqual(evaluation.BootstrapCount, holdout.BootstrapCount);
        Assert.AreEqual(evaluation.ConfidenceLevel, holdout.ConfidenceLevel);
        Assert.AreEqual(evaluation.Alpha, holdout.Alpha);
        Assert.AreEqual(evaluation.CalibrationNull, holdout.CalibrationNull);
        Assert.AreEqual(evaluation.Mapping, holdout.Mapping);
        Assert.AreEqual(evaluation.MappingApproval, holdout.MappingApproval);
        AssertJsonEqual(evaluation.Degradations, holdout.Degradations);
        AssertJsonEqual(evaluation.Sources, holdout.Sources);
        AssertJsonEqual(evaluation.Groups, holdout.Groups);
        AssertJsonEqual(evaluation.Contrasts, holdout.Contrasts);
        AssertJsonEqual(evaluation.LengthQuotas, holdout.LengthQuotas);
        AssertJsonEqual(evaluation.Astronomy, holdout.Astronomy);
        CollectionAssert.AreEqual(evaluation.HoldoutLoci, holdout.EvaluationLoci);
        CollectionAssert.AreEqual(evaluation.EvaluationLoci, holdout.HoldoutLoci);
    }

    [TestMethod]
    public void PublishedHoldoutIsTraceableAndPreservesEvaluation()
    {
        Assert.AreEqual(
            "4cf9b8644cca920ef24612e0b78fcfcce28d07f84dec7508ae5e53ed8e196906",
            PronunciationAudit.Hash(PathOf("reports/evaluation-run/m4-01/manifest.json")));
        Assert.AreEqual(
            "979c3f5fe50e0dee6330db42220dbed24b41f101337d6a9bbc175a0351d61541",
            PronunciationAudit.Hash(PathOf("reports/evaluation-report/m4-02/manifest.json")));

        var publication = Read<M4RunPublicationManifest>(Path.Combine(RunRoot, "manifest.json"));
        Assert.AreEqual("m4-04-holdout-partition-v1", publication.PublicationId);
        Assert.AreEqual("completed", publication.Status);
        Assert.AreEqual(
            "c8b258128eb161ff5ef0d82551bc15460f69af65",
            publication.RunnerCommit);
        Assert.IsTrue(publication.HoldoutPresent);
        Assert.IsFalse(publication.DefinitionsPresent);
        foreach (var artifact in publication.PackagedArtifacts.Append(publication.VerificationReport))
        {
            Assert.AreEqual(artifact.Sha256, PronunciationAudit.Hash(PathOf(artifact.Path)), artifact.Path);
        }

        var verification = Read<M4VerificationReport>(Path.Combine(RunRoot, "verification.json"));
        Assert.AreEqual("passed", verification.Status);
        Assert.IsFalse(verification.HoldoutAbsent);
        Assert.IsTrue(verification.DefinitionsAbsent);
        Assert.IsEmpty(verification.Blockers);
        Assert.IsTrue(verification.ScoreChecks.All(check => check.AbsoluteDifference <= 1e-12));
    }

    [TestMethod]
    public void FinalReportMatchesHoldoutStatisticsAndLinksResolve()
    {
        var manifest = Read<M4ReportManifest>(Path.Combine(ReportRoot, "manifest.json"));
        Assert.AreEqual("m4-04-final-holdout-report-v1", manifest.ReportId);
        Assert.IsTrue(manifest.HoldoutPresent);
        Assert.IsFalse(manifest.DefinitionsPresent);
        Assert.IsFalse(manifest.MatchingOrStatisticsRerun);
        foreach (var artifact in manifest.ReportArtifacts)
        {
            Assert.AreEqual(artifact.Sha256, PronunciationAudit.Hash(PathOf(artifact.Path)), artifact.Path);
        }

        var publication = Read<M4RunPublicationManifest>(Path.Combine(RunRoot, "manifest.json"));
        var statisticsPath = PathOf(publication.PackagedArtifacts.Single(artifact =>
            Path.GetFileName(artifact.Path) == "statistics.jsonl.gz").Path);
        var primary = ReadGzipLines<AutomatedTest>(statisticsPath).Single(test => test.Primary);
        var report = File.ReadAllText(Path.Combine(ReportRoot, "report.md"), new UTF8Encoding(false, true));
        StringAssert.Contains(
            report,
            $"| Holdout | {Format(primary.Estimate)} | " +
            $"[{Format(primary.Interval?.Lower)}, {Format(primary.Interval?.Upper)}] | " +
            $"{Format(primary.AdjustedPValue)} |");
        StringAssert.Contains(report, "**The frozen replication rule was not met.**");

        foreach (Match match in MarkdownLinkRegex().Matches(report))
        {
            var target = match.Groups[1].Value;
            Assert.IsFalse(Uri.TryCreate(target, UriKind.Absolute, out _), target);
            Assert.IsTrue(
                File.Exists(Path.GetFullPath(Path.Combine(
                    ReportRoot, target.Replace('/', Path.DirectorySeparatorChar)))),
                target);
        }
    }

    [TestMethod]
    public void FinalReportRebuildsWithoutRerunningMatchingOrStatistics()
    {
        var relativeOutput = "reports/final-report/m4-04-test-" + Guid.NewGuid().ToString("N");
        var output = PathOf(relativeOutput);
        try
        {
            Assert.AreEqual(
                0,
                new M4ReportGenerator(RepositoryRoot).Run(
                    Path.Combine(RunRoot, "manifest.json"),
                    relativeOutput));
            var committed = Read<M4ReportManifest>(Path.Combine(ReportRoot, "manifest.json"));
            foreach (var artifact in committed.ReportArtifacts.Where(artifact =>
                !artifact.Path.EndsWith("figure-metadata.json", StringComparison.Ordinal)))
            {
                var suffix = artifact.Path["reports/final-report/m4-04/".Length..];
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(PathOf(artifact.Path)),
                    File.ReadAllBytes(Path.Combine(output, suffix)),
                    suffix);
            }

            var rebuilt = Read<M4ReportManifest>(Path.Combine(output, "manifest.json"));
            Assert.AreEqual(committed.RunId, rebuilt.RunId);
            Assert.AreEqual(committed.DecisionRuleOutcome, rebuilt.DecisionRuleOutcome);
            Assert.IsFalse(rebuilt.MatchingOrStatisticsRerun);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static string Format(double? value) =>
        value?.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) ?? "NA";

    private static IEnumerable<T> ReadGzipLines<T>(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
        while (reader.ReadLine() is { } line)
        {
            yield return JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)
                ?? throw new AssertFailedException("Empty gzip row.");
        }
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(path, new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
        ?? throw new AssertFailedException("Empty JSON file: " + path);

    private static void AssertJsonEqual<T>(T expected, T actual) =>
        Assert.AreEqual(
            JsonSerializer.Serialize(expected, AutomatedProtocol.JsonOptions),
            JsonSerializer.Serialize(actual, AutomatedProtocol.JsonOptions));

    private static string PathOf(string relative) =>
        Path.GetFullPath(Path.Combine(
            RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    [GeneratedRegex(@"!?\[[^\]]*\]\(([^)#]+)(?:#[^)]+)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();
}
