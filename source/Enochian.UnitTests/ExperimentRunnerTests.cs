using Enochian.Benchmark;
using Json.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public sealed class ExperimentRunnerTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void RunsSyntheticExperimentAndRecoversPlantedSignal()
    {
        using var fixture = new ExperimentFixture();

        Assert.AreEqual(0, fixture.Run());

        AssertJsonLinesValid("experiment-match.schema.json", fixture.ReadLines("match-scores.jsonl"));
        AssertJsonLinesValid("nearest-distance.schema.json", fixture.ReadLines("nearest-distances.jsonl"));
        AssertJsonValid("experiment-report-inputs.schema.json", fixture.OutputPath("report-inputs.json"));
        AssertJsonValid("experiment-run-manifest.schema.json", fixture.OutputPath("run-manifest.json"));

        var tests = fixture.ReadLines("tests.jsonl");
        var primary = tests.Single(row => row.GetProperty("analysis_mode").GetString() == "type-primary");
        Assert.IsGreaterThan(0, primary.GetProperty("estimate").GetDouble());
        Assert.IsLessThanOrEqualTo(0.1, primary.GetProperty("p_value").GetDouble());

        var nearest = fixture.ReadLines("nearest-distances.jsonl");
        var observedTarget = nearest.Where(row =>
            !row.GetProperty("is_null").GetBoolean() &&
            row.GetProperty("family").GetString() == "target").ToArray();
        Assert.IsNotEmpty(observedTarget);
        Assert.IsTrue(observedTarget.All(row => row.GetProperty("distance").GetDouble() == 0));

        var nullTarget = nearest.Where(row =>
            row.GetProperty("is_null").GetBoolean() &&
            row.GetProperty("family").GetString() == "target").ToArray();
        Assert.IsNotEmpty(nullTarget);
        Assert.AreEqual(4, nullTarget.Select(row => row.GetProperty("null_kind").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(nullTarget.GroupBy(row => row.GetProperty("null_kind").GetString(), StringComparer.Ordinal)
            .All(group => group.Average(row => row.GetProperty("distance").GetDouble()) > 0));
    }

    [TestMethod]
    public void CleanRunsAreByteIdenticalAndInputChangesAlterRunId()
    {
        using var first = new ExperimentFixture();
        using var second = new ExperimentFixture();
        Assert.AreEqual(0, first.Run());
        Assert.AreEqual(0, second.Run());

        foreach (var name in first.OutputNames)
        {
            CollectionAssert.AreEqual(File.ReadAllBytes(first.OutputPath(name)), File.ReadAllBytes(second.OutputPath(name)), name);
        }

        var baseline = first.RunId;
        using var source = new ExperimentFixture();
        source.MutateSource();
        Assert.AreNotEqual(baseline, source.RunAndGetId());
        using var seed = new ExperimentFixture();
        seed.MutateSampling(root => root["seed"] = 107);
        Assert.AreNotEqual(baseline, seed.RunAndGetId());
        using var mapping = new ExperimentFixture();
        mapping.MutateSampling(root => root["mapping"]!["t"]![0] = 1);
        Assert.AreNotEqual(baseline, mapping.RunAndGetId());
        using var filter = new ExperimentFixture();
        filter.MutateExperiment(root => root["lexicon_filters"]!["proper_names"] = "include");
        Assert.AreNotEqual(baseline, filter.RunAndGetId());
    }

    [TestMethod]
    public void ResumesOnlyHashCompatibleCompletedStages()
    {
        using var fixture = new ExperimentFixture();
        Assert.AreEqual(0, fixture.Run());
        fixture.KeepOnlySamplingCheckpoint();

        using var membershipLock = new FileStream(
            fixture.OutputPath("memberships.jsonl"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.AreEqual(0, fixture.Run());
        Assert.IsTrue(File.Exists(fixture.OutputPath("report-inputs.json")));

        File.AppendAllText(fixture.OutputPath("match-scores.jsonl"), "corrupt\n", new UTF8Encoding(false));
        Assert.AreEqual(0, fixture.Run());
        Assert.IsFalse(File.ReadAllText(fixture.OutputPath("match-scores.jsonl")).EndsWith("corrupt\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ChangedRunInputsInvalidateDependentStages()
    {
        using var fixture = new ExperimentFixture();
        Assert.AreEqual(0, fixture.Run());
        var previousRunId = fixture.RunId;

        fixture.MutateRun(value =>
        {
            value["families"]!["tar"] = "control";
            value["families"]!["ctl"] = "target";
        });
        Assert.AreEqual(0, fixture.Run());

        Assert.AreNotEqual(previousRunId, fixture.RunId);
        Assert.IsTrue(fixture.ReadLines("nearest-distances.jsonl")
            .Any(row => row.GetProperty("language").GetString() == "tar" &&
                row.GetProperty("family").GetString() == "control"));
        using var report = JsonDocument.Parse(File.ReadAllBytes(fixture.OutputPath("report-inputs.json")));
        Assert.AreEqual(fixture.RunId, report.RootElement.GetProperty("run_id").GetString());
    }

    [TestMethod]
    [DataRow("dirty-fields")]
    [DataRow("unpinned-source")]
    [DataRow("ad-hoc-contrast")]
    [DataRow("definitions")]
    [DataRow("overlap")]
    [DataRow("multiple-mappings")]
    public void ConfirmatoryModeRejectsProhibitedMutation(string mutation)
    {
        using var fixture = new ExperimentFixture();
        fixture.MakeConfirmatory();
        fixture.ApplyConfirmatoryMutation(mutation);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run());
        StringAssert.Contains(exception.Message, "Stage 'validation' failed for config 'synthetic-smoke'");
    }

    private static void AssertJsonLinesValid(string schemaName, IEnumerable<JsonElement> rows)
    {
        var schema = LoadSchema(schemaName);
        Assert.IsTrue(rows.All(row => schema.Evaluate(row).IsValid), schemaName);
    }

    private static void AssertJsonValid(string schemaName, string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.IsTrue(LoadSchema(schemaName).Evaluate(document.RootElement).IsValid, schemaName);
    }

    private static JsonSchema LoadSchema(string name) => JsonSchema.FromText(
        File.ReadAllText(Path.Combine(RepositoryRoot, "experiments", "schemas", name)),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    private sealed class ExperimentFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"enochian-experiment-{Guid.NewGuid():N}");
        private readonly ExperimentRunner runner;

        public ExperimentFixture()
        {
            CopyDirectory(Path.Combine(RepositoryRoot, "experiments", "synthetic-smoke"), ProfileRoot, "outputs");
            CopyDirectory(Path.Combine(RepositoryRoot, "experiments", "schemas"), Path.Combine(root, "experiments", "schemas"));
            CopyDirectory(
                Path.Combine(RepositoryRoot, "resources", "lexicons", "schemas"),
                Path.Combine(root, "resources", "lexicons", "schemas"));

            var flow = new Enochian.Flow.Flow(Path.Combine(RepositoryRoot, "samples", "ipatransducer.json"));
            AssertUtils.NoErrors(flow);
            runner = new ExperimentRunner(
                root,
                flow.FeatureSets.Single(featureSet => featureSet.Id == "Default"),
                flow.Encodings.Single(encoding => encoding.Id == "IPA"));
        }

        public string ProfileRoot => Path.Combine(root, "experiments", "synthetic-smoke");
        public string ProtocolPath => Path.Combine(ProfileRoot, "run.json");
        public string RunId
        {
            get
            {
                using var manifest = JsonDocument.Parse(File.ReadAllBytes(OutputPath("run-manifest.json")));
                return manifest.RootElement.GetProperty("run_id").GetString()!;
            }
        }

        public IEnumerable<string> OutputNames => Directory.EnumerateFiles(Path.Combine(ProfileRoot, "outputs"))
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal);

        public int Run() => runner.Run(ProtocolPath);

        public string RunAndGetId()
        {
            Assert.AreEqual(0, Run());
            return RunId;
        }

        public string OutputPath(string name) => Path.Combine(ProfileRoot, "outputs", name);

        public JsonElement[] ReadLines(string name) =>
            [.. File.ReadLines(OutputPath(name), new UTF8Encoding(false, true))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

        public void MutateSampling(Action<JsonObject> mutation) => MutateJson("sampling.json", mutation);

        public void MutateExperiment(Action<JsonObject> mutation) => MutateJson("experiment.json", mutation);

        public void MutateRun(Action<JsonObject> mutation) => MutateJson("run.json", mutation);

        public void MutateSource()
        {
            var path = Path.Combine(ProfileRoot, "control.jsonl");
            File.AppendAllText(path, " ", new UTF8Encoding(false));
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            MutateJson("control.manifest.json", manifest => manifest["sha256"] = hash);
        }

        public void KeepOnlySamplingCheckpoint()
        {
            var manifest = ReadObject(OutputPath("run-manifest.json"));
            var sampling = manifest["stages"]!.AsArray().OfType<JsonObject>()
                .Single(stage => stage["stage_id"]!.GetValue<string>() == "sampling");
            manifest["stages"] = new JsonArray(sampling.DeepClone());
            manifest["artifacts"] = sampling["outputs"]!.DeepClone();
            WriteObject(OutputPath("run-manifest.json"), manifest);
            foreach (var path in Directory.EnumerateFiles(Path.Combine(ProfileRoot, "outputs")))
            {
                if (Path.GetFileName(path) is not ("memberships.jsonl" or "nulls.jsonl" or "sampling-report.json" or "run-manifest.json"))
                {
                    File.Delete(path);
                }
            }
        }

        public void MakeConfirmatory()
        {
            MutateJson("run.json", value => value["phase"] = "confirmatory");
            MutateExperiment(value =>
            {
                value["phase"] = "confirmatory";
                value["frozen"] = true;
                value["randomization"]!["sample_count"] = 2;
            });
            MutateJson("statistics.json", value => value["phase"] = "confirmatory");
        }

        public void ApplyConfirmatoryMutation(string mutation)
        {
            switch (mutation)
            {
                case "dirty-fields":
                    MutateSampling(value => value["seed"] = 107);
                    break;
                case "unpinned-source":
                    MutateJson("control.manifest.json", value => value["revision"]!["kind"] = "unresolved");
                    break;
                case "ad-hoc-contrast":
                    MutateJson("statistics.json", value => value["contrasts"]![0]!["contrast_id"] = "ad-hoc");
                    break;
                case "definitions":
                    MutateJson("run.json", value => value["definitions_path"] = "definitions.jsonl");
                    break;
                case "overlap":
                    MutateExperiment(value => value["corpus_split"]!["holdout_partition"]!["loci"] = new JsonArray("q01"));
                    break;
                case "multiple-mappings":
                    MutateExperiment(value => value["dtw"]!["mapping_selection"] = "predeclared_sensitivity_set");
                    break;
                default:
                    Assert.Fail($"Unknown mutation '{mutation}'.");
                    break;
            }
        }

        public void Dispose() => Directory.Delete(root, true);

        private void MutateJson(string name, Action<JsonObject> mutation)
        {
            var path = Path.Combine(ProfileRoot, name);
            var value = ReadObject(path);
            mutation(value);
            WriteObject(path, value);
        }

        private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        private static void WriteObject(string path, JsonObject value) => File.WriteAllText(
            path,
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false));

        private static void CopyDirectory(string source, string destination, string? excludedDirectory = null)
        {
            _ = Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                if (Path.GetFileName(directory) != excludedDirectory)
                {
                    CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
                }
            }
        }
    }
}
