using Enochian.Benchmark;
using Enochian.Provenance;
using Microsoft.VisualBasic.FileIO;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class M4ContextualAppendixTests
{
    private const string RunId = "73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1";
    private const string ManifestSnapshot =
        "4d47e22aa928aa076bceff76646b86bb03ae08610bd5886ce912c9c66fcf381c";
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string AppendixRoot = Path.Combine(
        RepositoryRoot, "reports", "contextual-appendix", "m4-03");

    [TestMethod]
    public void CdslJoinReportsMatchedMissingAndAmbiguousRecords()
    {
        const string input = """
            <L>1<k1>alpha<k2>alpha
            first definition
            <LEND>
            <L>2<k1>beta<k2>beta
            second definition
            <LEND>
            <L>2<k1>beta<k2>beta
            duplicate definition
            <LEND>
            """;
        M4ContextRequest[] requests =
        [
            new("candidate-c", "san", "gamma", "entry-c", "cdsl-shs", "3:gamma"),
            new("candidate-b", "san", "beta", "entry-b", "cdsl-shs", "2:beta"),
            new("candidate-a", "san", "alpha", "entry-a", "cdsl-shs", "1:alpha"),
        ];

        var first = M4ContextualAppendixGenerator.JoinCdsl(
            new StringReader(input), requests, "cdsl-manifest");
        var second = M4ContextualAppendixGenerator.JoinCdsl(
            new StringReader(input), requests, "cdsl-manifest");

        AssertUtils.SequenceEquals(first, second);
        Assert.AreEqual("matched", first.Single(row => row.CandidateId == "candidate-a").JoinStatus);
        Assert.IsTrue(first.Where(row => row.CandidateId == "candidate-b")
            .All(row => row.JoinStatus == "ambiguous_source_record"));
        Assert.AreEqual("missing_source_record",
            first.Single(row => row.CandidateId == "candidate-c").JoinStatus);
    }

    [TestMethod]
    public void WiktextractJoinRetainsEveryGlossAndReportsAmbiguity()
    {
        const string input = """
            {"lang_code":"tr","word":"alpha","pos":"noun","etymology_number":0,"senses":[{"glosses":["first","second"],"topics":["topic-b","topic-a"]}]}
            {"lang_code":"tr","word":"beta","pos":"verb","etymology_number":0,"senses":[{"glosses":["one"]}]}
            {"lang_code":"tr","word":"beta","pos":"verb","etymology_number":0,"senses":[{"glosses":["two"]}]}
            {"lang_code":"en","word":"alpha","pos":"noun","etymology_number":0,"senses":[{"glosses":["wrong language"]}]}
            """;
        M4ContextRequest[] requests =
        [
            new("candidate-missing", "tur", "missing", "entry-m", "kaikki-raw", "missing\u001fnoun\u001f0"),
            new("candidate-beta", "tur", "beta", "entry-b", "kaikki-raw", "beta\u001fverb\u001f0"),
            new("candidate-alpha", "tur", "alpha", "entry-a", "kaikki-raw", "alpha\u001fnoun\u001f0"),
        ];

        var rows = M4ContextualAppendixGenerator.JoinWiktextract(
            new StringReader(input), requests, "tr", "kaikki-manifest");

        var alpha = rows.Where(row => row.CandidateId == "candidate-alpha").ToArray();
        Assert.HasCount(2, alpha);
        AssertUtils.SequenceEquals(["first", "second"], alpha.Select(row => row.Context));
        AssertUtils.SequenceEquals(["topic-a", "topic-b"], alpha[0].Labels);
        Assert.IsTrue(rows.Where(row => row.CandidateId == "candidate-beta")
            .All(row => row.JoinStatus == "ambiguous_source_record"));
        Assert.AreEqual("missing_source_record",
            rows.Single(row => row.CandidateId == "candidate-missing").JoinStatus);
    }

    [TestMethod]
    public void PublishedAppendixIsSecondaryCompleteAndPartitionSafe()
    {
        var manifestPath = Path.Combine(AppendixRoot, "manifest.json");
        Assert.AreEqual(ManifestSnapshot, PronunciationAudit.Hash(manifestPath));
        var manifest = Read<M4ContextManifest>(manifestPath);
        Assert.AreEqual(RunId, manifest.RunId);
        Assert.IsTrue(manifest.SecondaryEvidence);
        Assert.IsFalse(manifest.HumanAnnotation);
        Assert.IsFalse(manifest.AutomatedSemanticScoring);
        Assert.IsTrue(manifest.DefinitionsPresent);
        Assert.IsFalse(manifest.PrimaryArtifactsModified);
        Assert.IsFalse(manifest.HoldoutPresent);
        foreach (var artifact in manifest.AppendixArtifacts)
        {
            Assert.AreEqual(artifact.Sha256, PronunciationAudit.Hash(PathOf(artifact.Path)), artifact.Path);
        }
        foreach (var path in new[]
                 {
                     "experiments/contextual-appendix.m4-03.json",
                     "source/Enochian.Benchmark/M4ContextualAppendixGenerator.cs",
                 })
        {
            var artifact = manifest.SourceArtifacts.Single(value => value.Path == path);
            Assert.AreEqual(artifact.Sha256, PronunciationAudit.Hash(PathOf(path)), path);
        }

        var contexts = ReadCsv(Path.Combine(AppendixRoot, "candidate-contexts.csv"));
        var contextHeader = contexts[0];
        var candidateIndex = Array.IndexOf(contextHeader, "candidate_id");
        var languageIndex = Array.IndexOf(contextHeader, "language");
        var contextIndex = Array.IndexOf(contextHeader, "context");
        Assert.AreEqual(242, contexts.Skip(1).Select(row => row[candidateIndex]).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(120, contexts.Skip(1).Where(row => row[languageIndex] == "san")
            .Select(row => row[candidateIndex]).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(122, contexts.Skip(1).Where(row => row[languageIndex] == "tur")
            .Select(row => row[candidateIndex]).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(contexts.Skip(1).All(row => row[contextIndex].Length != 0));
        CollectionAssert.DoesNotContain(contextHeader, "query_id");
        CollectionAssert.DoesNotContain(contextHeader, "score");
        CollectionAssert.DoesNotContain(contextHeader, "rank");

        var languages = ReadCsv(Path.Combine(AppendixRoot, "astronomy-languages.csv"));
        var pairs = ReadCsv(Path.Combine(AppendixRoot, "astronomy-pairs.csv"));
        Assert.HasCount(17, languages.Skip(1).ToArray());
        Assert.HasCount(16, pairs.Skip(1).ToArray());
        var statusIndex = Array.IndexOf(pairs[0], "status");
        var sharedIndex = Array.IndexOf(pairs[0], "shared_catalog_ids");
        Assert.IsTrue(pairs.Skip(1).All(row =>
            row[statusIndex] == "not_estimable" && row[sharedIndex] == "0"));

        var appendixText = string.Join('\n', Directory.EnumerateFiles(AppendixRoot, "*.csv")
            .Select(File.ReadAllText));
        foreach (var locus in new[] { "f3r", "f3v", "f4r", "f4v" })
        {
            Assert.IsFalse(appendixText.Contains(locus, StringComparison.Ordinal));
        }

        var quantitative = Read<M4ReportManifest>(
            PathOf("reports/evaluation-report/m4-02/manifest.json"));
        Assert.IsFalse(quantitative.SourceArtifacts.Any(artifact =>
            artifact.Path.Contains("contextual-appendix", StringComparison.Ordinal)));
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(path, new UTF8Encoding(false, true)),
            AutomatedProtocol.JsonOptions)
        ?? throw new AssertFailedException($"Empty JSON document: {path}");

    private static List<string[]> ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, new UTF8Encoding(false, true))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
        };
        parser.SetDelimiters(",");
        var rows = new List<string[]>();
        while (!parser.EndOfData)
        {
            rows.Add(parser.ReadFields() ?? throw new AssertFailedException($"Empty CSV row: {path}"));
        }

        return rows;
    }

    private static string PathOf(string relativePath) =>
        Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
