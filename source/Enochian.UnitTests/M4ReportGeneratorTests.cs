using Enochian.Benchmark;
using Enochian.Provenance;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Enochian.UnitTests;

[TestClass]
public sealed partial class M4ReportGeneratorTests
{
    private const string RunId = "73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1";
    private const string ManifestSnapshot =
        "979c3f5fe50e0dee6330db42220dbed24b41f101337d6a9bbc175a0351d61541";
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string ReportRoot = Path.Combine(
        RepositoryRoot, "reports", "evaluation-report", "m4-02");

    [TestMethod]
    public void RebuildMatchesFrozenSnapshotsAndPreservesSourceArtifacts()
    {
        var sourceManifest = Path.Combine(
            RepositoryRoot, "reports", "evaluation-run", "m4-01", "manifest.json");
        var committedManifestPath = Path.Combine(ReportRoot, "manifest.json");
        Assert.AreEqual(ManifestSnapshot, PronunciationAudit.Hash(committedManifestPath));
        var committed = Read<M4ReportManifest>(committedManifestPath);
        foreach (var artifact in committed.ReportArtifacts)
        {
            Assert.AreEqual(artifact.Sha256, PronunciationAudit.Hash(PathOf(artifact.Path)), artifact.Path);
        }

        var sourceState = committed.SourceArtifacts
            .Append(committed.SourceRunManifest)
            .ToDictionary(
                artifact => artifact.Path,
                artifact => (PronunciationAudit.Hash(PathOf(artifact.Path)), File.GetLastWriteTimeUtc(PathOf(artifact.Path))),
                StringComparer.Ordinal);
        var relativeOutput = "reports/evaluation-report/m4-02-test-" + Guid.NewGuid().ToString("N");
        var output = PathOf(relativeOutput);
        try
        {
            Assert.AreEqual(0, new M4ReportGenerator(RepositoryRoot).Run(sourceManifest, relativeOutput));
            foreach (var artifact in committed.ReportArtifacts.Where(
                artifact => !artifact.Path.EndsWith("figure-metadata.json", StringComparison.Ordinal)))
            {
                var suffix = artifact.Path["reports/evaluation-report/m4-02/".Length..];
                CollectionAssert.AreEqual(
                    File.ReadAllBytes(PathOf(artifact.Path)),
                    File.ReadAllBytes(Path.Combine(output, suffix)),
                    suffix);
            }

            var expectedFigures = Read<M4FigureMetadata[]>(Path.Combine(ReportRoot, "figure-metadata.json"));
            var actualFigures = Read<M4FigureMetadata[]>(Path.Combine(output, "figure-metadata.json"));
            Assert.HasCount(expectedFigures.Length, actualFigures);
            for (var index = 0; index < expectedFigures.Length; index++)
            {
                Assert.AreEqual(expectedFigures[index].Id, actualFigures[index].Id);
                Assert.AreEqual(expectedFigures[index].Title, actualFigures[index].Title);
                Assert.AreEqual(expectedFigures[index].AltText, actualFigures[index].AltText);
                Assert.AreEqual(expectedFigures[index].Encoding, actualFigures[index].Encoding);
                Assert.AreEqual(expectedFigures[index].RunId, actualFigures[index].RunId);
                Assert.AreEqual(expectedFigures[index].Sha256, actualFigures[index].Sha256);
                CollectionAssert.AreEqual(
                    expectedFigures[index].SourceArtifacts,
                    actualFigures[index].SourceArtifacts);
            }

            var rebuilt = Read<M4ReportManifest>(Path.Combine(output, "manifest.json"));
            Assert.AreEqual(RunId, rebuilt.RunId);
            Assert.IsFalse(rebuilt.MatchingOrStatisticsRerun);
            Assert.IsFalse(rebuilt.DefinitionsPresent);
            Assert.IsFalse(rebuilt.HoldoutPresent);
            foreach (var source in sourceState)
            {
                Assert.AreEqual(source.Value.Item1, PronunciationAudit.Hash(PathOf(source.Key)), source.Key);
                Assert.AreEqual(source.Value.Item2, File.GetLastWriteTimeUtc(PathOf(source.Key)), source.Key);
            }
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ReportMatchesFrozenStatisticsAndIsAccessibleOffline()
    {
        var reportPath = Path.Combine(ReportRoot, "report.md");
        var report = File.ReadAllText(reportPath, new UTF8Encoding(false, true));
        var tests = ReadGzipLines<AutomatedTest>(
            PathOf("reports/evaluation-run/m4-01/artifacts/statistics.jsonl.gz"));
        var primary = tests.Single(test => test.Primary);
        var effect = Format(primary.Estimate);
        var lower = Format(primary.Interval?.Lower);
        var upper = Format(primary.Interval?.Upper);
        var pValue = Format(primary.PValue);
        var adjusted = Format(primary.AdjustedPValue);
        StringAssert.Contains(
            report,
            $"effect was **{effect}** with a 95% hierarchical-bootstrap interval of **[{lower}, {upper}]**, " +
            $"one-sided permutation p = **{pValue}**, and Holm-adjusted p = **{adjusted}**.");
        Assert.HasCount(
            tests.Count(test => test.Status == "not_estimable"),
            File.ReadLines(Path.Combine(ReportRoot, "tables", "contrasts.csv"))
                .Where(line => line.Contains(",not_estimable,", StringComparison.Ordinal)));

        foreach (Match match in MarkdownLinkRegex().Matches(report))
        {
            var target = match.Groups[1].Value;
            Assert.IsFalse(Uri.TryCreate(target, UriKind.Absolute, out _), target);
            Assert.IsTrue(
                File.Exists(Path.GetFullPath(Path.Combine(ReportRoot, target.Replace('/', Path.DirectorySeparatorChar)))),
                target);
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(ReportRoot, "tables"), "*.csv"))
        {
            var header = File.ReadLines(path).First();
            StringAssert.Contains(header, "run_id");
            StringAssert.Contains(header, "source_artifact");
            Assert.IsFalse(ForbiddenSemanticFieldRegex().IsMatch(header), Path.GetFileName(path));
            Assert.IsFalse(File.ReadAllText(path).Contains("f3r", StringComparison.Ordinal));
            Assert.IsFalse(File.ReadAllText(path).Contains("f3v", StringComparison.Ordinal));
            Assert.IsFalse(File.ReadAllText(path).Contains("f4r", StringComparison.Ordinal));
            Assert.IsFalse(File.ReadAllText(path).Contains("f4v", StringComparison.Ordinal));
        }

        var metadata = Read<M4FigureMetadata[]>(Path.Combine(ReportRoot, "figure-metadata.json"));
        foreach (var figure in metadata)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(figure.AltText));
            Assert.IsTrue(
                figure.Encoding.Contains("black", StringComparison.OrdinalIgnoreCase) ||
                figure.Encoding.Contains("no color", StringComparison.OrdinalIgnoreCase));
            var document = XDocument.Load(PathOf(figure.Path));
            var root = document.Root ?? throw new AssertFailedException("SVG has no root element.");
            Assert.AreEqual("img", root.Attribute("role")?.Value);
            Assert.IsFalse(string.IsNullOrWhiteSpace(root.Attribute("aria-labelledby")?.Value));
            Assert.IsNotNull(root.Elements().SingleOrDefault(element => element.Name.LocalName == "title"));
            Assert.IsNotNull(root.Elements().SingleOrDefault(element => element.Name.LocalName == "desc"));
            Assert.AreEqual(figure.Sha256, PronunciationAudit.Hash(PathOf(figure.Path)));
        }
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(path, new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
        ?? throw new AssertFailedException($"Empty JSON document: {path}");

    private static List<T> ReadGzipLines<T>(string path)
    {
        using var stream = File.OpenRead(path);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
        var rows = new List<T>();
        while (reader.ReadLine() is { } line)
        {
            rows.Add(JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)
                ?? throw new AssertFailedException($"Empty JSON line in {path}."));
        }

        return rows;
    }

    private static string PathOf(string relativePath) =>
        Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Format(double? value) =>
        value?.ToString("0.000", CultureInfo.InvariantCulture) ?? "NA";

    [GeneratedRegex(@"!?\[[^\]]*\]\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(^|,)(definition|definitions|gloss|meaning)(,|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenSemanticFieldRegex();
}
