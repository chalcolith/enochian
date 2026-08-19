using Enochian.Benchmark;
using Enochian.Provenance;
using Enochian.Text;
using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowConfiguration = Enochian.Flow.Flow;

namespace Enochian.UnitTests;

[TestClass]
public sealed class RetrievalBenchmarkTests
{
    [TestMethod]
    public void IdentityDataAchievesPerfectRetrieval()
    {
        var entries = new[]
        {
            Entry("a", "record-a", 0, 0),
            Entry("b", "record-b", 1, 1),
        };
        var query = Query(entries[0], Relevant("a"));

        var metrics = RetrievalEvaluator.Evaluate(RetrievalEvaluator.Rank(query, entries, false));

        Assert.IsTrue(metrics.RecallAt1);
        Assert.IsTrue(metrics.RecallAt5);
        Assert.IsTrue(metrics.RecallAt20);
        Assert.AreEqual(1, metrics.ReciprocalRank);
        Assert.AreEqual(0, metrics.RelevantNormalizedDistance);
    }

    [TestMethod]
    public void PlantedNoiseHasDeterministicRankAndMetrics()
    {
        var entries = new[]
        {
            Entry("a-distractor", "other-a", 0.1, 0.1),
            Entry("b-relevant", "record", 0.2, 0.2),
            Entry("c-distractor", "other-c", 1, 1),
        };
        var query = new BenchmarkQuery("query", "source", "record", "syn", Phones(0, 0),
            new HashSet<string>(["b-relevant"], StringComparer.Ordinal), "short", "none");

        var ranking = RetrievalEvaluator.Rank(query, entries, false);
        var metrics = RetrievalEvaluator.Evaluate(ranking);

        AssertUtils.SequenceEquals(["a-distractor", "b-relevant", "c-distractor"],
            ranking.Select(candidate => candidate.EntryId));
        Assert.AreEqual(2, metrics.RelevantRank);
        Assert.AreEqual(0.5, metrics.ReciprocalRank);
        Assert.IsFalse(metrics.RecallAt1);
        Assert.IsTrue(metrics.RecallAt5);
    }

    [TestMethod]
    public void SourceRemovalExcludesAllPronunciationsForRecord()
    {
        var entries = new[]
        {
            Entry("pronunciation-a", "record", 0, 0),
            Entry("pronunciation-b", "record", 0, 0.1),
            Entry("other", "other", 1, 1),
        };
        var query = Query(entries[0], Relevant("pronunciation-a", "pronunciation-b"));

        var ranking = RetrievalEvaluator.Rank(query, entries, true);

        AssertUtils.SequenceEquals(["other"], ranking.Select(candidate => candidate.EntryId));
        Assert.AreEqual(0, RetrievalEvaluator.Evaluate(ranking).ReciprocalRank);
    }

    [TestMethod]
    public void MetricsHandleTiesNoCandidatesAndMultiplePronunciations()
    {
        var tied = new[]
        {
            Entry("a", "other", 0, 0),
            Entry("b", "record", 0, 0),
        };
        var query = Query(tied[1], Relevant("b", "unused-pronunciation"));

        var tiedMetrics = RetrievalEvaluator.Evaluate(RetrievalEvaluator.Rank(query, tied, false));
        var emptyMetrics = RetrievalEvaluator.Evaluate([]);

        Assert.AreEqual(2, tiedMetrics.RelevantRank);
        Assert.AreEqual(0.5, tiedMetrics.ReciprocalRank);
        Assert.AreEqual(0, emptyMetrics.CandidateCount);
        Assert.IsNull(emptyMetrics.NearestNormalizedDistance);
        Assert.IsNull(emptyMetrics.RelevantRank);
    }

    [TestMethod]
    public void SamplingIsStableForSeedAndChangesForDifferentSeed()
    {
        var entries = Enumerable.Range(0, 20)
            .Select(index => Entry($"entry-{index:D2}", $"record-{index:D2}", index))
            .ToArray();

        var first = BenchmarkSampling.Sample(entries, 5, 15485863);
        var repeated = BenchmarkSampling.Sample(entries.Reverse(), 5, 15485863);
        var changed = BenchmarkSampling.Sample(entries, 5, 15485867);

        AssertUtils.SequenceEquals(first.Select(entry => entry.EntryId), repeated.Select(entry => entry.EntryId));
        Assert.IsFalse(first.Select(entry => entry.EntryId).SequenceEqual(
            changed.Select(entry => entry.EntryId), StringComparer.Ordinal));
    }

    [TestMethod]
    public void AppliesLanguageNeutralDeletionMergerAndMasking()
    {
        var features = CreateFeatureSet();
        var phones = new[]
        {
            features.GetFeatureVector(["+High", "+Low", "+Voice"], []),
            features.GetFeatureVector(["-High", "+Low", "-Voice"], []),
            features.GetFeatureVector(["+High", "-Low", "+Voice"], []),
        };
        var profile = new DegradationProfile(
            "language-neutral-test",
            "1.0.0",
            true,
            [
                new("deletion", Every: 3, Offset: 1),
                new("feature_merger", Features: ["High", "Low"]),
                new("feature_masking", Features: ["Voice"]),
            ]);

        var degraded = BenchmarkDegrader.Apply(phones, profile, features);

        Assert.HasCount(2, degraded);
        var high = features.FeatureList.IndexOf("High");
        var low = features.FeatureList.IndexOf("Low");
        var voice = features.FeatureList.IndexOf("Voice");
        Assert.AreEqual(1, degraded[0][high]);
        Assert.AreEqual(features.UnsetValue, degraded[0][low]);
        Assert.AreEqual(features.UnsetValue, degraded[0][voice]);
    }

    [TestMethod]
    public void CompletedReviewComputesAccuracyAndErrorCategories()
    {
        using var fixture = new TemporaryDirectory();
        var reviewPath = Path.Combine(fixture.Path, "review.jsonl");
        var rows = Enumerable.Range(0, 100)
            .Select(index => new IpaReviewRow
            {
                BlindedId = new string(index < 10 ? 'a' : 'b', 64),
                SourceForm = $"form-{index}",
                NormalizedForm = $"form-{index}",
                GeneratedIpa = "ipa",
                ExpectedIpa = "ipa",
                Decision = index < 5 ? "reject" : "accept",
                ErrorCategory = index < 5 ? "segment" : null,
            });
        File.WriteAllLines(reviewPath, rows.Select(row => JsonSerializer.Serialize(row, SerializerOptions)), new UTF8Encoding(false));

        var summary = ReviewEvaluator.Evaluate(reviewPath, Thresholds());

        Assert.IsTrue(summary.Passed);
        Assert.AreEqual(0.95, summary.Accuracy);
        Assert.AreEqual(5, summary.ErrorCategories["segment"]);
    }

    [TestMethod]
    public void PendingOrUncategorizedReviewBlocksProgression()
    {
        using var fixture = new TemporaryDirectory();
        var reviewPath = Path.Combine(fixture.Path, "review.jsonl");
        var rows = new[]
        {
            Review("accept", null),
            Review("reject", null),
            Review("pending", null),
        };
        File.WriteAllLines(reviewPath, rows.Select(row => JsonSerializer.Serialize(row, SerializerOptions)), new UTF8Encoding(false));

        var summary = ReviewEvaluator.Evaluate(reviewPath, Thresholds());

        Assert.IsFalse(summary.Passed);
        CollectionAssert.Contains(summary.Blockers.ToArray(), "insufficient_completed_review_records");
        CollectionAssert.Contains(summary.Blockers.ToArray(), "review_accuracy");
        CollectionAssert.Contains(summary.Blockers.ToArray(), "missing_error_category");
    }

    [TestMethod]
    public void FrozenMetricThresholdsBlockFailingLengthBand()
    {
        var summary = new MetricSummary(10, 0.7, 0.9, 1, 0.8, 0.2);

        var decision = ThresholdEvaluator.Evaluate(summary, Thresholds());

        Assert.IsFalse(decision.Passed);
        AssertUtils.SequenceEquals(
            ["recall_at_1", "recall_at_5", "mean_reciprocal_rank"],
            decision.Blockers);
    }

    [TestMethod]
    public void RunnerEmitsDeterministicSchemaValidTidyOutputs()
    {
        using var fixture = new TemporaryDirectory();
        CreateRunnerFixture(fixture.Path);
        var flow = new FlowConfiguration(GetPath("samples/ipatransducer.json"));
        AssertUtils.NoErrors(flow);
        var runner = new BenchmarkRunner(
            RepositoryRoot,
            flow.FeatureSets.Single(featureSet => featureSet.Id == "Default"),
            flow.Encodings.Single(encoding => encoding.Id == "IPA"));
        var protocolPath = Path.Combine(fixture.Path, "protocol.json");

        var firstExitCode = runner.Run(protocolPath);
        var firstScores = File.ReadAllBytes(Path.Combine(fixture.Path, "output", "scores.jsonl"));
        var firstSummaries = File.ReadAllBytes(Path.Combine(fixture.Path, "output", "summaries.jsonl"));
        var secondExitCode = runner.Run(protocolPath);

        Assert.AreEqual(0, firstExitCode);
        Assert.AreEqual(0, secondExitCode);
        CollectionAssert.AreEqual(firstScores, File.ReadAllBytes(Path.Combine(fixture.Path, "output", "scores.jsonl")));
        CollectionAssert.AreEqual(firstSummaries, File.ReadAllBytes(Path.Combine(fixture.Path, "output", "summaries.jsonl")));
        AssertJsonLinesValid("experiments/schemas/retrieval-score.schema.json", firstScores);
        AssertJsonLinesValid("experiments/schemas/retrieval-summary.schema.json", firstSummaries);
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(firstScores).Contains("definition", StringComparison.Ordinal));
        var qualityPath = Path.Combine(fixture.Path, "output", "quality.json");
        var quality = JsonNode.Parse(File.ReadAllText(qualityPath))!.AsObject();
        AssertJsonValid("experiments/schemas/retrieval-quality-report.schema.json", File.ReadAllText(qualityPath));
        Assert.IsTrue(quality["passed"]!.GetValue<bool>());
    }

    [TestMethod]
    public void ReviewIngestionRejectsLanguageIdentifyingFields()
    {
        using var fixture = new TemporaryDirectory();
        var reviewPath = Path.Combine(fixture.Path, "review.jsonl");
        // lang=json
        const string IdentifyingReview =
            """{"blinded_id":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","source_form":"pat","normalized_form":"pat","generated_ipa":"pat","expected_ipa":"pat","decision":"accept","error_category":null,"notes":null,"language":"syn"}""";
        File.WriteAllText(reviewPath, IdentifyingReview, new UTF8Encoding(false));

        var exception = Assert.Throws<InvalidDataException>(() => ReviewEvaluator.Evaluate(reviewPath, Thresholds()));

        StringAssert.Contains(exception.Message, "prohibited field 'language'");
    }

    private static BenchmarkEntry Entry(string id, string recordId, params double[] values) =>
        new(id, "source", recordId, "syn", id, Phones(values), "short", "none");

    private static BenchmarkQuery Query(BenchmarkEntry entry, IReadOnlySet<string> relevant) =>
        new("query", entry.Source, entry.SourceRecordId, entry.Language, entry.Phones, relevant, "short", "none");

    private static HashSet<string> Relevant(params string[] entryIds) =>
        new(entryIds, StringComparer.Ordinal);

    private static double[][] Phones(params double[] values) =>
        [.. values.Select(value => new[] { value })];

    private static FeatureSet CreateFeatureSet()
    {
        var features = new FeatureSet(null);
        _ = features.Configure(JsonNode.Parse(
            """{"id":"test","plusValue":1,"minusValue":-1,"features":["High","Low","Voice"]}""")!.AsObject());
        AssertUtils.NoErrors(features);
        return features;
    }

    private static BenchmarkThresholds Thresholds() => new(100, 0.95, 0.8, 0.95, 0.99, 0.85, 0.35);

    private static void CreateRunnerFixture(string root)
    {
        _ = Directory.CreateDirectory(Path.Combine(root, "schemas"));
        _ = Directory.CreateDirectory(Path.Combine(root, "profiles"));
        File.Copy(GetPath("experiments/schemas/retrieval-benchmark.schema.json"),
            Path.Combine(root, "schemas", "retrieval-benchmark.schema.json"));
        File.Copy(GetPath("experiments/schemas/degradation-profile.schema.json"),
            Path.Combine(root, "schemas", "degradation-profile.schema.json"));
        for (var index = 1; index <= 4; index++)
        {
            File.WriteAllText(Path.Combine(root, "profiles", $"identity-{index}.json"), $$"""
                {
                  "$schema": "../schemas/degradation-profile.schema.json",
                  "id": "identity-{{index}}",
                  "version": "1.0.0",
                  "language_neutral": true,
                  "operations": []
                }
                """, new UTF8Encoding(false));
        }

        var entries = new[]
        {
            NormalizedEntry("short", "pat"),
            NormalizedEntry("medium", "pataka"),
            NormalizedEntry("long", "patakatamana"),
        };
        File.WriteAllLines(Path.Combine(root, "lexicon.jsonl"), entries, new UTF8Encoding(false));
        var reviews = Enumerable.Range(0, 100).Select(index => JsonSerializer.Serialize(new IpaReviewRow
        {
            BlindedId = index.ToString("x64", CultureInfo.InvariantCulture),
            SourceForm = $"form-{index}",
            NormalizedForm = $"form-{index}",
            GeneratedIpa = "pat",
            ExpectedIpa = "pat",
            Decision = "accept",
        }, SerializerOptions));
        File.WriteAllLines(Path.Combine(root, "review.jsonl"), reviews, new UTF8Encoding(false));
        var protocol = new JsonObject
        {
            ["$schema"] = "schemas/retrieval-benchmark.schema.json",
            ["schema_version"] = "1.0.0",
            ["benchmark_id"] = "synthetic-retrieval",
            ["frozen"] = true,
            ["sampling_seed"] = 15485863,
            ["samples_per_stratum"] = 1,
            ["minimum_phonemes"] = 3,
            ["maximum_phonemes"] = 20,
            ["required_length_bands"] = new JsonArray("03-05", "06-09", "10-20"),
            ["thresholds"] = new JsonObject
            {
                ["minimum_review_records"] = 100,
                ["minimum_review_accuracy"] = 0.95,
                ["minimum_recall_at_1"] = 0.8,
                ["minimum_recall_at_5"] = 0.95,
                ["minimum_recall_at_20"] = 0.99,
                ["minimum_mean_reciprocal_rank"] = 0.85,
                ["maximum_mean_normalized_distance"] = 0.35,
            },
            ["sources"] = new JsonArray(new JsonObject
            {
                ["source_id"] = "synthetic",
                ["language"] = "syn",
                ["lexicon_path"] = "lexicon.jsonl",
                ["review_path"] = "review.jsonl",
                ["confirmatory"] = true,
                ["required"] = true,
            }),
            ["degradation_profiles"] = new JsonArray(
                "profiles/identity-1.json",
                "profiles/identity-2.json",
                "profiles/identity-3.json",
                "profiles/identity-4.json"),
            ["outputs"] = new JsonObject
            {
                ["scores"] = "output/scores.jsonl",
                ["summaries"] = "output/summaries.jsonl",
                ["quality_report"] = "output/quality.json",
            },
        };
        File.WriteAllText(
            Path.Combine(root, "protocol.json"),
            protocol.ToJsonString(),
            new UTF8Encoding(false));
    }

    private static string NormalizedEntry(string id, string ipa) => $$"""
        {"entry_id":"synthetic:syn:{{id}}","source_record_id":"{{id}}","source":"synthetic","language":"syn","entry_kind":"lemma","ipa":"{{ipa}}"}
        """;

    private static void AssertJsonLinesValid(string schemaPath, byte[] content)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(GetPath(schemaPath)),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        foreach (var line in System.Text.Encoding.UTF8.GetString(content).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            Assert.IsTrue(schema.Evaluate(document.RootElement).IsValid, $"Output does not match {schemaPath}.");
        }
    }

    private static void AssertJsonValid(string schemaPath, string content)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(GetPath(schemaPath)),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        using var document = JsonDocument.Parse(content);
        Assert.IsTrue(schema.Evaluate(document.RootElement).IsValid, $"Output does not match {schemaPath}.");
    }

    private static string GetPath(string relativePath) =>
        Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static IpaReviewRow Review(string decision, string? category) => new()
    {
        BlindedId = new string('c', 64),
        SourceForm = "form",
        NormalizedForm = "form",
        GeneratedIpa = "ipa",
        Decision = decision,
        ErrorCategory = category,
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enochian-benchmark-tests", Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
