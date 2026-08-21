using Enochian.Benchmark;
using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public sealed class StatisticsRunnerTests
{
    private static readonly string[] ExpectedAnalysisModes = ["type-primary", "token-weighted"];
    private static readonly string[] ExpectedScopes = ["overall", "length", "section", "frequency-band"];
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void RunsPlantedComparisonDeterministicallyAndEmitsTidySchemas()
    {
        using var fixture = new StatisticsFixture();
        var runner = new StatisticsRunner(RepositoryRoot);

        Assert.AreEqual(0, runner.Run(fixture.ProtocolPath));
        var first = fixture.OutputFiles.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        Assert.AreEqual(0, runner.Run(fixture.ProtocolPath));
        foreach (var (path, content) in first)
        {
            CollectionAssert.AreEqual(content, File.ReadAllBytes(path));
        }

        var schemas = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["calibrated.jsonl"] = "calibrated-score.schema.json",
            ["estimates.jsonl"] = "statistical-estimate.schema.json",
            ["intervals.jsonl"] = "statistical-interval.schema.json",
            ["tests.jsonl"] = "statistical-test.schema.json",
            ["adjusted.jsonl"] = "adjusted-p-value.schema.json",
            ["diagnostics.jsonl"] = "statistical-diagnostic.schema.json",
        };
        foreach (var (output, schema) in schemas)
        {
            AssertJsonLinesValid(schema, ReadLines(fixture.PathFor(output)));
        }

        var tests = ReadLines(fixture.PathFor("tests.jsonl"));
        Assert.HasCount(2, tests);
        Assert.IsTrue(tests.All(row => row.GetProperty("estimate").GetDouble() > 0));
        Assert.IsTrue(tests.All(row => row.GetProperty("configured_randomization_count").GetInt32() == 100));
        Assert.IsTrue(tests.All(row => row.GetProperty("randomization_count").GetInt32() == 8));
        Assert.IsTrue(tests.All(row => row.GetProperty("planned_correction_method").GetString() == "holm"));
        CollectionAssert.AreEquivalent(
            ExpectedAnalysisModes,
            tests.Select(row => row.GetProperty("analysis_mode").GetString()).ToArray());

        var estimates = ReadLines(fixture.PathFor("estimates.jsonl"));
        CollectionAssert.IsSubsetOf(
            ExpectedScopes,
            estimates.Select(row => row.GetProperty("scope").GetString()).Distinct().ToArray());
        Assert.IsTrue(estimates.Any(row => row.GetProperty("metric").GetString() == "winner-proportion"));

        var intervals = ReadLines(fixture.PathFor("intervals.jsonl"));
        Assert.IsTrue(intervals.All(row => row.GetProperty("confidence_level").GetDouble() == 0.8));
        Assert.IsTrue(intervals.All(row => row.GetProperty("bootstrap_count").GetInt32() == 100));
        var adjusted = ReadLines(fixture.PathFor("adjusted.jsonl"));
        Assert.IsTrue(adjusted.All(row => row.GetProperty("correction_method").GetString() == "holm"));
        Assert.IsTrue(adjusted.All(row => row.GetProperty("family_size").GetInt32() == 1));
        var diagnostics = ReadLines(fixture.PathFor("diagnostics.jsonl"));
        Assert.IsTrue(diagnostics.Any(row => row.GetProperty("code").GetString() == "missing-language-query-pair"));
        Assert.IsTrue(diagnostics.Any(row => row.GetProperty("code").GetString() == "missing-null-distribution"));
        Assert.IsTrue(ReadLines(fixture.PathFor("calibrated.jsonl"))
            .Any(row => row.GetProperty("empirical_percentile").ValueKind == JsonValueKind.Null));
    }

    [TestMethod]
    public void RejectsUnregisteredConfirmatoryContrast()
    {
        using var fixture = new StatisticsFixture(unregisteredContrast: true);

        var exception = Assert.ThrowsExactly<InvalidDataException>(() => StatisticsProtocol.Load(fixture.ProtocolPath));
        StringAssert.Contains(exception.Message, "not registered");
    }

    [TestMethod]
    public void ReportsInsufficientLexiconSamplesWithoutFabricatingInterval()
    {
        using var fixture = new StatisticsFixture(singleSample: true);

        Assert.AreEqual(0, new StatisticsRunner(RepositoryRoot).Run(fixture.ProtocolPath));
        Assert.IsEmpty(ReadLines(fixture.PathFor("intervals.jsonl")));
        Assert.IsTrue(ReadLines(fixture.PathFor("diagnostics.jsonl"))
            .Any(row => row.GetProperty("code").GetString() == "insufficient-bootstrap-samples"));
    }

    private static JsonElement[] ReadLines(string path) =>
        [.. File.ReadLines(path, new UTF8Encoding(false, true))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    private static void AssertJsonLinesValid(string schemaName, IEnumerable<JsonElement> rows)
    {
        var schema = JsonSchema.FromText(
            File.ReadAllText(Path.Combine(RepositoryRoot, "experiments", "schemas", schemaName)),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Assert.IsTrue(rows.All(row => schema.Evaluate(row).IsValid));
    }

    private sealed class StatisticsFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"enochian-statistics-{Guid.NewGuid():N}");

        public StatisticsFixture(bool unregisteredContrast = false, bool singleSample = false)
        {
            _ = Directory.CreateDirectory(root);
            File.Copy(
                Path.Combine(RepositoryRoot, "experiments", "schemas", "statistics-protocol.schema.json"),
                Path.Combine(root, "statistics-protocol.schema.json"));
            File.Copy(
                Path.Combine(RepositoryRoot, "experiments", "schemas", "nearest-distance.schema.json"),
                Path.Combine(root, "nearest-distance.schema.json"));
            WriteExperiment();
            WriteInputs(singleSample);
            var contrastId = unregisteredContrast ? "unregistered" : "target-vs-control";
            var protocol = new JsonObject
            {
                ["$schema"] = "statistics-protocol.schema.json",
                ["schema_version"] = "1.0.0",
                ["statistics_id"] = "statistics-fixture",
                ["phase"] = "confirmatory",
                ["experiment_path"] = "experiment.json",
                ["input_path"] = "nearest.jsonl",
                ["input_schema_path"] = "nearest-distance.schema.json",
                ["calibration_null_kind"] = "biphone-pseudoword",
                ["score_metric"] = "null-standardized-nearest-distance",
                ["seed"] = 31,
                ["permutation_count"] = 100,
                ["bootstrap_count"] = 100,
                ["confidence_level"] = 0.8,
                ["contrasts"] = new JsonArray(new JsonObject
                {
                    ["contrast_id"] = contrastId,
                    ["primary"] = true,
                    ["target_families"] = new JsonArray("target"),
                    ["control_families"] = new JsonArray("control"),
                    ["alternative"] = "greater",
                }),
                ["outputs"] = new JsonObject
                {
                    ["calibrated_scores"] = "calibrated.jsonl",
                    ["estimates"] = "estimates.jsonl",
                    ["intervals"] = "intervals.jsonl",
                    ["tests"] = "tests.jsonl",
                    ["adjusted_p_values"] = "adjusted.jsonl",
                    ["diagnostics"] = "diagnostics.jsonl",
                },
            };
            ProtocolPath = Path.Combine(root, "statistics.json");
            File.WriteAllText(ProtocolPath, protocol.ToJsonString(), new UTF8Encoding(false));
        }

        public string ProtocolPath { get; }
        public IEnumerable<string> OutputFiles => Directory.EnumerateFiles(root, "*.jsonl")
            .Where(path => Path.GetFileName(path) != "nearest.jsonl")
            .Order(StringComparer.Ordinal);

        public string PathFor(string name) => Path.Combine(root, name);

        public void Dispose() => Directory.Delete(root, true);

        private void WriteExperiment()
        {
            var experiment = new JsonObject
            {
                ["phase"] = "confirmatory",
                ["frozen"] = true,
                ["planned_contrasts"] = new JsonArray(new JsonObject
                {
                    ["id"] = "target-vs-control",
                    ["primary"] = true,
                    ["target_groups"] = new JsonArray("target"),
                    ["control_groups"] = new JsonArray("control"),
                    ["expected_direction"] = "lower",
                }),
            };
            File.WriteAllText(Path.Combine(root, "experiment.json"), experiment.ToJsonString(), new UTF8Encoding(false));
        }

        private void WriteInputs(bool singleSample)
        {
            var rows = new List<object>();
            var samples = singleSample
                ? new[] { (Id: "sample-1", Repetition: 1) }
                : [(Id: "sample-1", Repetition: 1), (Id: "sample-2", Repetition: 2)];
            foreach (var (sampleId, repetition) in samples)
            {
                foreach (var query in new[] { (Id: "q1", Length: 3, Section: "a", Band: "common"), (Id: "q2", Length: 4, Section: "a", Band: "rare"), (Id: "q3", Length: 5, Section: "b", Band: "rare") })
                {
                    AddRows(rows, sampleId, repetition, query, "tar", "target", 1);
                    if (sampleId != "sample-2" || query.Id != "q3")
                    {
                        AddRows(rows, sampleId, repetition, query, "ctl", "control", 4);
                    }
                }
            }

            File.WriteAllLines(
                Path.Combine(root, "nearest.jsonl"),
                rows.Select(row => JsonSerializer.Serialize(row)),
                new UTF8Encoding(false));
        }

        private static void AddRows(
            List<object> rows,
            string sampleId,
            int repetition,
            (string Id, int Length, string Section, string Band) query,
            string language,
            string family,
            double observed)
        {
            AddMode("type-primary", 1);
            AddMode("token-weighted", query.Id == "q1" ? 1 : 5);

            void AddMode(string analysisMode, int weight)
            {
                rows.Add(Row(sampleId, repetition, query, language, family, analysisMode, weight, false, null, observed));
                if (sampleId == "sample-2" && query.Id == "q2" && language == "tar")
                {
                    return;
                }

                foreach (var distance in new[] { 2.0, 3.0, 4.0 })
                {
                    rows.Add(Row(sampleId, repetition, query, language, family, analysisMode, weight, true, "biphone-pseudoword", distance));
                }
            }
        }

        private static object Row(
            string sampleId,
            int repetition,
            (string Id, int Length, string Section, string Band) query,
            string language,
            string family,
            string analysisMode,
            int weight,
            bool isNull,
            string? nullKind,
            double distance) => new
            {
                schema_version = "1.0.0",
                analysis_id = "primary",
                analysis_mode = analysisMode,
                sample_id = sampleId,
                requested_size = 10,
                repetition,
                query_id = query.Id,
                query_length = query.Length,
                section = query.Section,
                frequency_band = query.Band,
                weight,
                language,
                family,
                is_null = isNull,
                null_kind = nullKind,
                distance,
            };
    }
}
