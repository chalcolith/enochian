using Enochian.Benchmark;
using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public sealed class SamplingRunnerTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void RunsDistinctPrimaryAndInflectedAnalysesDeterministically()
    {
        using var fixture = new SamplingFixture();
        var runner = CreateRunner();

        Assert.AreEqual(0, runner.Run(fixture.ProtocolPath));
        var first = fixture.OutputFiles.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        Assert.AreEqual(0, runner.Run(fixture.ProtocolPath));
        foreach (var (path, content) in first)
        {
            CollectionAssert.AreEqual(content, File.ReadAllBytes(path));
        }

        foreach (var analysis in new[] { "primary", "inflected", "full" })
        {
            var memberships = ReadLines(fixture.PathFor($"{analysis}.memberships.jsonl"));
            var nulls = ReadLines(fixture.PathFor($"{analysis}.nulls.jsonl"));
            Assert.HasCount(analysis == "full" ? 20 : 12, memberships);
            Assert.HasCount(48, nulls);
            AssertJsonLinesValid("experiments/schemas/sample-membership.schema.json", memberships);
            AssertJsonLinesValid("experiments/schemas/sequence-null.schema.json", nulls);
            Assert.IsTrue(memberships.All(row => row.GetProperty("analysis_set").GetString() == analysis));
            var expectedKinds = analysis switch
            {
                "primary" => new HashSet<string>(["lemma"], StringComparer.Ordinal),
                "inflected" => new HashSet<string>(["inflected_form"], StringComparer.Ordinal),
                _ => new HashSet<string>(["lemma", "inflected_form"], StringComparer.Ordinal),
            };
            Assert.IsTrue(memberships.Select(row => row.GetProperty("entry_kind").GetString())
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(expectedKinds));
            Assert.IsTrue(nulls.All(row => row.GetProperty("is_null").GetBoolean()));
            Assert.IsTrue(nulls.All(row => memberships.Any(membership =>
                membership.GetProperty("sample_id").GetString() == row.GetProperty("sample_id").GetString() &&
                membership.GetProperty("requested_size").GetInt32() == row.GetProperty("requested_size").GetInt32())));
            Assert.AreEqual(4, nulls.Select(row => row.GetProperty("null_kind").GetString()).Distinct(StringComparer.Ordinal).Count());
            Assert.IsTrue(memberships
                .GroupBy(row => (row.GetProperty("sample_id").GetString(), row.GetProperty("language").GetString()))
                .All(group => group.Count() == group.First().GetProperty("requested_size").GetInt32()));
            Assert.IsTrue(memberships
                .GroupBy(row => row.GetProperty("sample_id").GetString())
                .All(group => group.Select(row => row.GetProperty("candidate_id").GetString()).Distinct(StringComparer.Ordinal).Count() == group.Count()));

            using var report = JsonDocument.Parse(File.ReadAllBytes(fixture.PathFor($"{analysis}.report.json")));
            Assert.AreEqual(analysis == "full" ? 4 : 2, report.RootElement.GetProperty("largest_common_size").GetInt32());
            Assert.AreEqual(analysis == "full" ? 20 : 12, report.RootElement.GetProperty("membership_rows").GetInt32());
            Assert.AreEqual(48, report.RootElement.GetProperty("null_rows").GetInt32());
            var counts = report.RootElement.GetProperty("candidate_counts");
            Assert.IsTrue(counts.EnumerateObject().All(property => property.Value.GetInt32() >= 2));
        }
    }

    [TestMethod]
    public void RejectsReusedAnalysisOutputPaths()
    {
        using var fixture = new SamplingFixture(reuseOutputPath: true);

        _ = Assert.ThrowsExactly<InvalidDataException>(() => SamplingProtocol.Load(fixture.ProtocolPath));
    }

    private static SamplingRunner CreateRunner()
    {
        var flow = new Enochian.Flow.Flow(Path.Combine(RepositoryRoot, "samples", "ipatransducer.json"));
        AssertUtils.NoErrors(flow);
        return new SamplingRunner(
            RepositoryRoot,
            flow.FeatureSets.Single(featureSet => featureSet.Id == "Default"),
            flow.Encodings.Single(encoding => encoding.Id == "IPA"));
    }

    private static JsonElement[] ReadLines(string path) =>
        [.. File.ReadLines(path, new UTF8Encoding(false, true))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    private static void AssertJsonLinesValid(string schemaPath, IEnumerable<JsonElement> rows)
    {
        var schema = JsonSchema.FromText(
            File.ReadAllText(Path.Combine(RepositoryRoot, schemaPath.Replace('/', Path.DirectorySeparatorChar))),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Assert.IsTrue(rows.All(row => schema.Evaluate(row).IsValid));
    }

    private sealed class SamplingFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"enochian-sampling-{Guid.NewGuid():N}");

        public SamplingFixture(bool reuseOutputPath = false)
        {
            _ = Directory.CreateDirectory(root);
            File.Copy(
                Path.Combine(RepositoryRoot, "experiments", "schemas", "sampling-protocol.schema.json"),
                Path.Combine(root, "sampling-protocol.schema.json"));
            WriteLexicon("eng", [
                ("eng-lemma-a", "lemma", "tat"),
                ("eng-lemma-b", "lemma", "dad"),
                ("eng-form-a", "inflected_form", "kak"),
                ("eng-form-b", "inflected_form", "nan")]);
            WriteLexicon("tur", [
                ("tur-lemma-a", "lemma", "mam"),
                ("tur-lemma-b", "lemma", "sas"),
                ("tur-form-a", "inflected_form", "lal"),
                ("tur-form-b", "inflected_form", "rar")]);
            File.WriteAllText(
                Path.Combine(root, "queries.jsonl"),
                "{\"query_id\":\"query-1\",\"text\":\"aba\",\"symbols\":[\"a\",\"b\",\"a\"],\"token_frequency\":3}\n",
                new UTF8Encoding(false));
            var protocol = new JsonObject
            {
                ["$schema"] = "sampling-protocol.schema.json",
                ["schema_version"] = "1.0.0",
                ["sampling_id"] = "sampling-fixture",
                ["generator_version"] = "balanced-nulls-v1",
                ["seed"] = 101,
                ["repetitions"] = 2,
                ["queries_path"] = "queries.jsonl",
                ["mapping"] = new JsonObject
                {
                    ["a"] = new JsonArray(1.0),
                    ["b"] = new JsonArray(-1.0),
                },
                ["analyses"] = new JsonArray(
                    Analysis("primary", "lemma", "primary"),
                    Analysis("inflected", "inflected_form", reuseOutputPath ? "primary" : "inflected"),
                    FullAnalysis()),
            };
            ProtocolPath = Path.Combine(root, "sampling.json");
            File.WriteAllText(ProtocolPath, protocol.ToJsonString(), new UTF8Encoding(false));
        }

        public string ProtocolPath { get; }
        public IEnumerable<string> OutputFiles => Directory.EnumerateFiles(root, "*.json*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Contains('.', StringComparison.Ordinal) &&
                (path.Contains("memberships", StringComparison.Ordinal) ||
                 path.Contains("nulls", StringComparison.Ordinal) ||
                 path.Contains("report", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal);

        public string PathFor(string name) => Path.Combine(root, name);

        public void Dispose()
        {
            Directory.Delete(root, true);
        }

        private static JsonObject Analysis(string analysisSet, string entryKind, string prefix) =>
            new()
            {
                ["analysis_id"] = analysisSet,
                ["analysis_set"] = analysisSet,
                ["included_entry_kinds"] = new JsonArray(entryKind),
                ["smaller_sample_sizes"] = new JsonArray(1),
                ["frequency_bands"] = new JsonArray(),
                ["sources"] = new JsonArray(
                    new JsonObject { ["source_id"] = "eng-source", ["language"] = "eng", ["lexicon_path"] = "eng.jsonl" },
                    new JsonObject { ["source_id"] = "tur-source", ["language"] = "tur", ["lexicon_path"] = "tur.jsonl" }),
                ["outputs"] = new JsonObject
                {
                    ["memberships"] = prefix + ".memberships.jsonl",
                    ["nulls"] = prefix + ".nulls.jsonl",
                    ["report"] = prefix + ".report.json",
                },
            };

        private static JsonObject FullAnalysis()
        {
            var analysis = Analysis("full", "lemma", "full");
            analysis["included_entry_kinds"] = new JsonArray("lemma", "inflected_form");
            return analysis;
        }

        private void WriteLexicon(string language, IEnumerable<(string Id, string Kind, string Ipa)> entries)
        {
            var lines = entries.Select(entry => JsonSerializer.Serialize(new
            {
                entry_id = $"fixture:{language}:{entry.Id}",
                source_record_id = entry.Id,
                source = "fixture",
                language,
                lemma = entry.Id,
                entry_kind = entry.Kind,
                ipa = entry.Ipa,
                frequency = 1.0,
            }));
            File.WriteAllLines(Path.Combine(root, language + ".jsonl"), lines, new UTF8Encoding(false));
        }
    }
}
