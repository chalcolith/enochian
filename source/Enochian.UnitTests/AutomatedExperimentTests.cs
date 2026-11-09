using Enochian.Benchmark;
using Enochian.Provenance;
using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public sealed class AutomatedExperimentTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void ReadinessAmendmentPreservesFrozenRetrievalProtocol()
    {
        var baseline = AutomatedProtocol.Load(
            Path.Combine(RepositoryRoot, "experiments", "confirmatory.retrieval.v1.json"),
            RepositoryRoot);
        var amendment = AutomatedProtocol.Load(
            Path.Combine(RepositoryRoot, "experiments", "confirmatory.readiness.v1.json"),
            RepositoryRoot);

        Assert.IsNotNull(amendment.BaseProtocol);
        Assert.IsNotNull(amendment.Queries);
        Assert.IsNotNull(amendment.Mapping);
        Assert.IsNotNull(amendment.MappingApproval);
        Assert.AreEqual(baseline.SamplingSeed, amendment.SamplingSeed);
        Assert.AreEqual(baseline.NullSeed, amendment.NullSeed);
        Assert.AreEqual(baseline.PermutationSeed, amendment.PermutationSeed);
        Assert.AreEqual(baseline.Repetitions, amendment.Repetitions);
        Assert.AreEqual(baseline.NullRepetitions, amendment.NullRepetitions);
        Assert.AreEqual(baseline.PermutationCount, amendment.PermutationCount);
        Assert.AreEqual(baseline.BootstrapCount, amendment.BootstrapCount);
        CollectionAssert.AreEqual(baseline.EvaluationLoci, amendment.EvaluationLoci);
        CollectionAssert.AreEqual(baseline.HoldoutLoci, amendment.HoldoutLoci);
        CollectionAssert.AreEqual(
            baseline.Contrasts.Select(contrast => contrast.Id).ToArray(),
            amendment.Contrasts.Select(contrast => contrast.Id).ToArray());
        CollectionAssert.AreEqual(
            baseline.LengthQuotas.ToArray(),
            amendment.LengthQuotas.ToArray());
    }

    [TestMethod]
    public void AnchorRevisionOverridesOnlyDeclaredReadinessInputs()
    {
        var baseline = AutomatedProtocol.Load(
            Path.Combine(RepositoryRoot, "experiments", "confirmatory.retrieval.v1.json"),
            RepositoryRoot);
        var amendment = AutomatedProtocol.Load(
            Path.Combine(RepositoryRoot, "experiments", "confirmatory.readiness.m3-07.json"),
            RepositoryRoot);

        Assert.AreEqual("degradation-aware-retrieval-v3", amendment.RetrievalRunner);
        Assert.AreEqual(
            "resources/lexicons/pronunciation-panel.m3-07.json",
            amendment.Panel.Path);
        Assert.AreEqual(
            "experiments/retrieval-benchmark.v3.protocol.json",
            amendment.RetrievalPolicy.Path);
        var sanskrit = amendment.Sources.Single(source => source.ProfileId == "san-dictionary");
        Assert.IsTrue(sanskrit.Required);
        Assert.IsTrue(sanskrit.ConfirmatoryEligible);
        Assert.IsNotNull(sanskrit.GeneratorApproval);
        foreach (var id in new[] { "hin-dictionary", "hun-dictionary" })
        {
            var source = amendment.Sources.Single(value => value.ProfileId == id);
            Assert.IsFalse(source.Required);
            Assert.IsFalse(source.ConfirmatoryEligible);
        }

        Assert.AreEqual(baseline.SamplingSeed, amendment.SamplingSeed);
        Assert.AreEqual(baseline.NullSeed, amendment.NullSeed);
        Assert.AreEqual(baseline.PermutationSeed, amendment.PermutationSeed);
        CollectionAssert.AreEqual(baseline.EvaluationLoci, amendment.EvaluationLoci);
        CollectionAssert.AreEqual(baseline.HoldoutLoci, amendment.HoldoutLoci);
    }

    [TestMethod]
    public void SyntheticRunNeedsNoHumanFilesAndPreservesEveryRetrievalThreshold()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(0, fixture.Run());
        var report = fixture.Report();
        Assert.IsTrue(report.ReadyForEvaluation);
        Assert.IsFalse(report.HumanAnnotationRequired);
        foreach (var source in report.Sources)
        {
            Assert.AreEqual(100, source.SupportedLexicalUnits);
            Assert.AreEqual(100, source.UniquePhonologicalUnits);
            var cells = source.Retrieval.Where(cell => cell.CandidateMode == "source-included").ToArray();
            Assert.HasCount(12, cells);
            Assert.IsTrue(cells.All(cell => cell.LexicalQueries == 25 && cell.Passed &&
                cell.Metrics.RecallAt1 >= 0.8 && cell.Metrics.RecallAt5 >= 0.95 && cell.Metrics.RecallAt20 >= 0.99 &&
                cell.Metrics.MeanReciprocalRank >= 0.85 && cell.Metrics.MeanNormalizedDistance <= 0.35));
            Assert.HasCount(12, source.Retrieval.Where(cell => cell.CandidateMode == "source-excluded"));
        }

        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(fixture.Root, "experiments", "schemas", "automated-admission.v2.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        using var document = JsonDocument.Parse(File.ReadAllText(fixture.Path("admission.json")));
        Assert.IsTrue(schema.Evaluate(document.RootElement).IsValid);
        var memberships = fixture.Lines<AutomatedMembership>("sampling.jsonl");
        foreach (var sample in memberships.GroupBy(row => (row.SampleId, row.Unit.Language)))
        {
            Assert.HasCount(100, sample);
            Assert.HasCount(34, sample.Where(row => row.Unit.LengthBand == "03-05"));
            Assert.HasCount(33, sample.Where(row => row.Unit.LengthBand == "06-09"));
            Assert.HasCount(33, sample.Where(row => row.Unit.LengthBand == "10-20"));
        }

        Assert.IsFalse(Directory.EnumerateFiles(fixture.Root, "*review*.jsonl", SearchOption.AllDirectories).Any());
        Assert.IsTrue(fixture.Lines<AutomatedScore>("matching.jsonl").All(row =>
            row.Family == "synthetic-" + row.Language));
        Assert.IsTrue(fixture.Lines<AutomatedTest>("statistics.jsonl").All(test => test.Status == "estimated" && test.CorrectionFamilySize == 2));
    }

    [TestMethod]
    public void RepeatedRunsAreByteStableAndResumeVerifiedStages()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(0, fixture.Run());
        var first = fixture.Outputs();
        var sentinel = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(fixture.Output("sampling.jsonl"), sentinel);
        Assert.AreEqual(0, fixture.Run());
        Assert.AreEqual(sentinel, File.GetLastWriteTimeUtc(fixture.Output("sampling.jsonl")));
        CollectionAssert.AreEquivalent(first.Keys.ToArray(), fixture.Outputs().Keys.ToArray());
        foreach (var file in first)
        {
            CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(fixture.Output(file.Key)), file.Key);
        }

        File.WriteAllText(fixture.Output("sampling.jsonl"), "corrupt checkpoint\n", new UTF8Encoding(false));
        Assert.AreEqual(0, fixture.Run());
        CollectionAssert.AreEqual(first["sampling.jsonl"], File.ReadAllBytes(fixture.Output("sampling.jsonl")));
    }

    [TestMethod]
    public void EvidenceChangesInvalidateCheckpointIdentityEvenWhenNormalizedPhonesStayIdentical()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(0, fixture.Run());
        var first = fixture.RunId();
        fixture.Evidence(rows => rows[0]!["original_ipa"] = "/" + rows[0]!["normalized_ipa"]!.GetValue<string>() + "/");
        Assert.AreEqual(0, fixture.Run());
        Assert.AreNotEqual(first, fixture.RunId());
    }

    [TestMethod]
    public void OldCheckpointsCannotBeResumedByV2()
    {
        using var fixture = new Fixture();
        _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fixture.Output("run-manifest.json"))!);
        Fixture.Write(fixture.Output("run-manifest.json"), new JsonObject
        {
            ["schema_version"] = "1.0.0",
            ["runner_version"] = ExperimentRunner.RunnerVersion,
        });
        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run());
    }

    [TestMethod]
    [DataRow("missing-evidence")]
    [DataRow("missing-provenance")]
    [DataRow("wrong-convention")]
    [DataRow("wrong-label")]
    [DataRow("unknown-origin")]
    [DataRow("unvalidated-converter")]
    [DataRow("bad-weight")]
    [DataRow("wrong-normalization")]
    public void UnsuitableEvidenceCannotAdmitASource(string mutation)
    {
        using var fixture = new Fixture();
        fixture.Evidence(rows =>
        {
            switch (mutation)
            {
                case "missing-evidence": rows.RemoveAt(0); break;
                case "missing-provenance": rows[0]!["source_entry"] = null; break;
                case "wrong-convention": rows[0]!["convention"] = "wrong"; break;
                case "wrong-label": rows[0]!["labels"] = new JsonArray("Wrong"); break;
                case "unknown-origin":
                    foreach (var row in rows) { row!["origin"] = "unknown"; }
                    break;
                case "unvalidated-converter":
                    foreach (var row in rows) { row!["origin"] = "converter-generated"; }
                    break;
                case "bad-weight": rows[0]!["alternative_weight"] = 0.5; break;
                case "wrong-normalization": rows[0]!["normalization_policy"] = "invented"; break;
                default: Assert.Fail(mutation); break;
            }
        });
        Assert.AreEqual(1, fixture.Run("admit"));
        Assert.AreNotEqual("admitted", fixture.Report().Sources.Single(source => source.Language == "tar").Status);
    }

    [TestMethod]
    public void ValidIndependentConverterReferencesReplaceHumanReviewButInsufficientOnesDoNot()
    {
        using var fixture = new Fixture();
        fixture.References(2, false);
        Assert.AreEqual(1, fixture.Run("admit"));
        Assert.AreEqual("exploratory", fixture.Report().Sources.Single(source => source.Language == "tar").ReferenceAgreement!.Status);
        fixture.References(100, false);
        Assert.AreEqual(0, fixture.Run("admit"));
        var source = fixture.Report().Sources.Single(source => source.Language == "tar");
        Assert.AreEqual("pass", source.ReferenceAgreement!.Status);
        Assert.AreEqual(100, source.ReferenceAgreement.IndependentReferences);
        Assert.AreEqual(1, source.ReferenceAgreement.ExactAgreement);
    }

    [TestMethod]
    public void SharedSourceDependenciesAreNotIndependentReferences()
    {
        using var fixture = new Fixture();
        fixture.References(100, true);
        Assert.AreEqual(1, fixture.Run("admit"));
        Assert.AreEqual("excluded", fixture.Report().Sources.Single(source => source.Language == "tar").Status);
    }

    [TestMethod]
    public void WrongReferenceProfileCannotAdmitAConverter()
    {
        using var fixture = new Fixture();
        fixture.References(100, false);
        fixture.Mutate("references.json", value => value["profile_sha256"] = new string('0', 64));
        fixture.PinReferences();
        Assert.AreEqual(1, fixture.Run("admit"));
    }

    [TestMethod]
    public void FrozenNumericalRetrievalFailureBlocksOtherwiseSupportedEvidence()
    {
        using var fixture = new Fixture();
        fixture.Mutate("synthetic-ipa.json", value =>
        {
            foreach (var pattern in value["patterns"]!.AsArray())
            {
                var features = pattern!["features"]!.AsArray();
                features.Add("+Voice");
                features.Add("+SG");
                features.Add("+CG");
            }
        });
        fixture.RepinEncoding();
        Assert.AreEqual(1, fixture.Run("admit"));
        Assert.IsTrue(fixture.Report().Sources.All(source => source.PronunciationGate == "passed" &&
            source.RetrievalGate == "failed" && source.Blockers.Contains("retrieval_threshold_failed", StringComparer.Ordinal)));
    }

    [TestMethod]
    [DataRow("holdout")]
    [DataRow("outside-evaluation")]
    [DataRow("definition")]
    [DataRow("missing-locus")]
    [DataRow("unmapped")]
    [DataRow("duplicate-type")]
    public void QueryLeakageAndDefinitionsCannotEnterScoring(string mutation)
    {
        using var fixture = new Fixture();
        fixture.Queries(rows =>
        {
            switch (mutation)
            {
                case "holdout": rows[0]!["locus"] = "synthetic-holdout"; break;
                case "outside-evaluation": rows[0]!["locus"] = "unregistered"; break;
                case "definition": rows[0]!["definition"] = "semantic leak"; break;
                case "missing-locus": _ = rows[0]!.AsObject().Remove("locus"); break;
                case "unmapped": rows[0]!["symbols"] = new JsonArray("unmapped"); break;
                case "duplicate-type": rows[1]!["symbols"] = rows[0]!["symbols"]!.DeepClone(); break;
                default: Assert.Fail(mutation); break;
            }
        });
        Assert.AreEqual(1, fixture.Run("preflight"));
        Assert.IsFalse(File.Exists(fixture.Output("matching.jsonl")));
    }

    [TestMethod]
    public void DefinitionsInSourceEntriesAreIgnoredAndNeverExported()
    {
        using var fixture = new Fixture();
        var rows = fixture.ReadRows("tar-synthetic-v1.0.0.jsonl");
        foreach (var row in rows)
        {
            row!["definition"] = "DO-NOT-EXPORT-SEMANTIC-CONTENT";
        }

        fixture.WriteRows("tar-synthetic-v1.0.0.jsonl", rows);
        fixture.RepinTarget();
        Assert.AreEqual(0, fixture.Run());
        Assert.IsFalse(fixture.Outputs().Values.Any(bytes =>
            Encoding.UTF8.GetString(bytes).Contains("DO-NOT-EXPORT-SEMANTIC-CONTENT", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void UnverifiedSourceLicenseCannotBeAdmitted()
    {
        using var fixture = new Fixture();
        fixture.Mutate("tar-synthetic.source.json", value => value["license_status"] = "unverified");
        fixture.RepinTarget();
        Assert.AreEqual(1, fixture.Run("admit"));
        Assert.AreEqual("excluded", fixture.Report().Sources.Single(source => source.Language == "tar").Status);
    }

    [TestMethod]
    public void IdenticalPronunciationsDoNotInflateBalancedSampleSize()
    {
        using var fixture = new Fixture();
        var entries = fixture.ReadRows("tar-synthetic-v1.0.0.jsonl");
        foreach (var entry in entries) { entry!["ipa"] = "ptp"; }
        fixture.WriteRows("tar-synthetic-v1.0.0.jsonl", entries);
        fixture.Evidence(rows =>
        {
            foreach (var row in rows)
            {
                row!["normalized_ipa"] = "ptp";
                row["original_ipa"] = "ptp";
                row["phoneme_length"] = 3;
                row["length_band"] = "03-05";
            }
        });
        Assert.AreEqual(1, fixture.Run("admit"));
        var source = fixture.Report().Sources.Single(source => source.Language == "tar");
        Assert.AreEqual(100, source.SupportedLexicalUnits);
        Assert.AreEqual(1, source.UniquePhonologicalUnits);
    }

    [TestMethod]
    public void AlternativePronunciationsUseExpectedDistanceNotTheFavorableReading()
    {
        var unit = new EvidenceUnit("unit", "tar", "homograph", "03-05",
            [new("one", "p", 0.5, [[0]]), new("two", "t", 0.5, [[2]])], []);
        Assert.AreEqual(1, AutomatedRetrieval.Distance([[0]], unit));
    }

    [TestMethod]
    public void HomographsRetainDistinctLexicalIdentities()
    {
        using var fixture = new Fixture();
        var entries = fixture.ReadRows("tar-synthetic-v1.0.0.jsonl");
        foreach (var entry in entries) { entry!["lemma"] = "homograph"; }
        fixture.WriteRows("tar-synthetic-v1.0.0.jsonl", entries);
        fixture.Evidence(rows =>
        {
            foreach (var row in rows) { row!["lemma"] = "homograph"; }
        });
        Assert.AreEqual(0, fixture.Run());
        var memberships = fixture.Lines<AutomatedMembership>("sampling.jsonl").Where(row => row.Unit.Language == "tar").ToArray();
        Assert.AreEqual(100, memberships.Select(row => row.Unit.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(memberships.All(row => row.Unit.Lemma == "homograph"));
    }

    [TestMethod]
    public void MissingOptionalContrastKeepsItsMultiplicitySlots()
    {
        using var fixture = new Fixture();
        fixture.Mutate("run.json", value =>
        {
            value["contrasts"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "unavailable-secondary",
                ["primary"] = false,
                ["required"] = false,
                ["target_groups"] = new JsonArray("absent"),
                ["control_groups"] = new JsonArray("control"),
            });
            value["groups"]!["absent"] = new JsonArray("opt");
            var optional = value["sources"]![0]!.DeepClone();
            optional["language"] = "opt";
            optional["profile_id"] = "optional-synthetic";
            optional["required"] = false;
            value["sources"]!.AsArray().Add(optional);
        });
        var panel = JsonSerializer.Deserialize<PronunciationPanel>(File.ReadAllText(fixture.Path("panel.json")), PronunciationAudit.JsonOptions)!;
        Fixture.Write(fixture.Path("panel.json"), JsonSerializer.SerializeToNode(panel with
        {
            Profiles = [.. panel.Profiles, panel.Profiles[0] with { Id = "optional-synthetic", Language = "opt" }],
        }, PronunciationAudit.JsonOptions)!);
        fixture.RepinPanel();
        Assert.AreEqual(0, fixture.Run());
        var tests = fixture.Lines<AutomatedTest>("statistics.jsonl");
        Assert.HasCount(4, tests);
        Assert.IsTrue(tests.All(test => test.CorrectionFamilySize == 4));
        Assert.HasCount(2, tests.Where(test => test.Status == "not_estimable" && test.AdjustedPValue == null));
    }

    [TestMethod]
    public void SyntheticEncodingCannotBePromotedByChangingThePhaseFlag()
    {
        using var fixture = new Fixture();
        fixture.Mutate("run.json", value => value["phase"] = "confirmatory");
        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run());
    }

    [TestMethod]
    public void ReportsCannotOverwriteFrozenInputs()
    {
        using var fixture = new Fixture();
        var hash = PronunciationAudit.Hash(fixture.Path("panel.json"));
        fixture.Mutate("run.json", value => value["admission_report"] = "experiments/automated-smoke/panel.json");
        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("admit"));
        Assert.AreEqual(hash, PronunciationAudit.Hash(fixture.Path("panel.json")));
    }

    [TestMethod]
    public void PublishedPreflightCannotOverwriteFrozenInputs()
    {
        using var fixture = new Fixture();
        var hash = PronunciationAudit.Hash(fixture.Path("panel.json"));
        fixture.Mutate("run.json", value =>
            value["preflight_report"] = "experiments/automated-smoke/panel.json");

        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("preflight"));
        Assert.AreEqual(hash, PronunciationAudit.Hash(fixture.Path("panel.json")));
    }

    [TestMethod]
    [DataRow("profile-hash")]
    [DataRow("profile-version")]
    [DataRow("panel-hash")]
    public void IncompatibleProfileIdentitiesCannotAdmitOrResume(string mutation)
    {
        using var fixture = new Fixture();
        Assert.AreEqual(0, fixture.Run());
        var id = fixture.RunId();
        fixture.Mutate("tar-synthetic-v1.0.0.manifest.json", value =>
        {
            switch (mutation)
            {
                case "profile-hash": value["profile_sha256"] = new string('0', 64); break;
                case "profile-version": value["profile"]!["version"] = "2.0.0"; break;
                case "panel-hash": value["panel_sha256"] = new string('0', 64); break;
                default: Assert.Fail(mutation); break;
            }
        });
        fixture.RepinTarget();
        Assert.AreEqual(1, fixture.Run());
        Assert.AreEqual("excluded", fixture.Report().Sources.Single(source => source.Language == "tar").Status);
        Assert.AreEqual(id, fixture.RunId());
        Assert.IsFalse(JsonNode.Parse(File.ReadAllText(fixture.Output("preflight.json")))!["ready"]!.GetValue<bool>());
    }

    [TestMethod]
    public void EveryRegisteredEvaluationLocusMustBeRepresented()
    {
        using var fixture = new Fixture();
        fixture.Mutate("run.json", value =>
            value["evaluation_loci"]!.AsArray().Add("unrepresented-evaluation"));

        Assert.AreEqual(1, fixture.Run("preflight"));
        Assert.IsFalse(JsonNode.Parse(File.ReadAllText(fixture.Output("preflight.json")))!["ready"]!.GetValue<bool>());
    }

    [TestMethod]
    public void WeakerRetrievalThresholdsAreRejectedEvenWithAnUpdatedPin()
    {
        using var fixture = new Fixture();
        var path = System.IO.Path.Combine(fixture.Root, "experiments", "retrieval-benchmark.protocol.json");
        var value = JsonNode.Parse(File.ReadAllText(path))!;
        value["thresholds"]!["minimum_recall_at_1"] = 0.1;
        Fixture.Write(path, value);
        fixture.Mutate("run.json", protocol => protocol["retrieval_policy"]!["sha256"] = PronunciationAudit.HashConfiguration(path));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("admit"));
    }

    [TestMethod]
    public void VersionedRetrievalRunnerRejectsLegacyPolicyAndMaskingProfile()
    {
        using var fixture = new Fixture();
        fixture.Mutate("run.json", protocol =>
            protocol["retrieval_runner"] = "masked-query-distance-v2");
        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run("admit"));
    }

    [TestMethod]
    public void UnpinnedEvidenceChangesCannotReuseAnAdmittedSource()
    {
        using var fixture = new Fixture();
        Assert.AreEqual(0, fixture.Run());
        File.AppendAllText(fixture.Path("tar-synthetic-v1.0.0.evidence.jsonl"), "\n", new UTF8Encoding(false));
        Assert.AreEqual(1, fixture.Run());
        Assert.AreEqual("excluded", fixture.Report().Sources.Single(source => source.Language == "tar").Status);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Directory.CreateTempSubdirectory("enochian-automated-").FullName;
            foreach (var directory in new[] { "resources/encodings", "resources/lexicons/schemas", "experiments/schemas", "experiments/degradation-profiles" })
            {
                Copy(directory);
            }

            foreach (var file in new[]
            {
                "resources/lexicons/pronunciation-audit.protocol.json",
                "resources/lexicons/manifests/hyg-4.1.manifest.json",
                "experiments/retrieval-benchmark.protocol.json", "experiments/astronomical-names.v1.json",
                "reports/pronunciation-profiles/v1/astronomy-registration.json",
            })
            {
                var destination = System.IO.Path.Combine(Root, file.Replace('/', System.IO.Path.DirectorySeparatorChar));
                _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                File.Copy(System.IO.Path.Combine(RepositoryRoot, file.Replace('/', System.IO.Path.DirectorySeparatorChar)), destination);
            }

            Copy("experiments/automated-smoke", "outputs", "admission.json", "admission.md");
        }

        public string Root { get; }
        public string Path(string name) => System.IO.Path.Combine(Root, "experiments", "automated-smoke", name);
        public string Output(string name) => Path(System.IO.Path.Combine("outputs", name));
        public int Run(string mode = "experiment")
        {
            var flow = AutomatedExperiment.CreateFlow(Path("run.json"), Root);
            AssertUtils.NoErrors(flow);
            return new AutomatedExperiment(Root, flow.FeatureSets.Single(item => item.Id == "Default"),
                flow.Encodings.Single(item => item.Id == "IPA")).Run(Path("run.json"), mode);
        }

        public AutomatedAdmissionReport Report() => JsonSerializer.Deserialize<AutomatedAdmissionReport>(
            File.ReadAllText(Path("admission.json")), AutomatedProtocol.JsonOptions)!;
        public T[] Lines<T>(string name) => [.. File.ReadLines(Output(name))
            .Select(line => JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)!)];
        public Dictionary<string, byte[]> Outputs() => Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(Output("sampling.jsonl"))!)
            .ToDictionary(file => System.IO.Path.GetFileName(file), File.ReadAllBytes, StringComparer.Ordinal);
        public string RunId() => JsonNode.Parse(File.ReadAllText(Output("run-manifest.json")))!["run_id"]!.GetValue<string>();
        public List<JsonNode?> ReadRows(string name) => [.. File.ReadLines(Path(name)).Select(line => JsonNode.Parse(line))];
        public void WriteRows(string name, List<JsonNode?> rows) => File.WriteAllText(Path(name),
            string.Join('\n', rows.Select(row => row!.ToJsonString())) + "\n", new UTF8Encoding(false));
        public void Evidence(Action<List<JsonNode?>> mutation)
        {
            var rows = ReadRows("tar-synthetic-v1.0.0.evidence.jsonl");
            mutation(rows);
            WriteRows("tar-synthetic-v1.0.0.evidence.jsonl", rows);
            RepinTarget();
        }

        public void Queries(Action<List<JsonNode?>> mutation)
        {
            var rows = ReadRows("queries.jsonl");
            mutation(rows);
            WriteRows("queries.jsonl", rows);
            Mutate("run.json", value => value["queries"]!["sha256"] = PronunciationAudit.Hash(Path("queries.jsonl")));
        }

        public void Mutate(string name, Action<JsonObject> mutation)
        {
            var value = JsonNode.Parse(File.ReadAllText(Path(name)))!.AsObject();
            mutation(value);
            Write(Path(name), value);
        }

        public void RepinTarget()
        {
            Mutate("tar-synthetic.source.json", value => value["sha256"] = PronunciationAudit.Hash(Path("tar-synthetic-v1.0.0.jsonl")));
            RepinProfile("tar-synthetic");
        }

        public void RepinEncoding()
        {
            foreach (var id in new[] { "tar-synthetic", "ctl-synthetic" })
            {
                Mutate(id + "-v1.0.0.manifest.json", value => value["encoding_sha256"] = PronunciationAudit.Hash(Path("synthetic-ipa.json")));
                RepinProfile(id);
            }

            Mutate("run.json", value => value["phonology"]!["encoding"]!["sha256"] = PronunciationAudit.HashConfiguration(Path("synthetic-ipa.json")));
        }

        public void RepinPanel()
        {
            var hash = PronunciationAudit.HashConfiguration(Path("panel.json"));
            foreach (var id in new[] { "tar-synthetic", "ctl-synthetic" })
            {
                Mutate(id + "-v1.0.0.manifest.json", value => value["panel_sha256"] = hash);
                RepinProfile(id);
            }

            Mutate("run.json", value => value["panel"]!["sha256"] = hash);
        }

        public void PinReferences() => Mutate("run.json", value =>
            value["sources"]!.AsArray().Single(source => source!["language"]!.GetValue<string>() == "tar")!["references"] =
                JsonSerializer.SerializeToNode(new PinnedInput("experiments/automated-smoke/references.json",
                    PronunciationAudit.HashConfiguration(Path("references.json"))), AutomatedProtocol.JsonOptions));

        public void References(int count, bool shared)
        {
            Evidence(rows =>
            {
                foreach (var row in rows)
                {
                    row!["origin"] = "converter-generated";
                    row["generator"] = "synthetic-generator";
                    row["generator_revision"] = "synthetic-generator-v1";
                }
            });
            var reference = JsonNode.Parse(File.ReadAllText(Path("tar-synthetic.source.json")))!;
            reference["source_id"] = "independent-reference";
            reference["url"] = "https://example.com/independent";
            File.WriteAllText(Path("independent.raw"), "Independent artificial fixture reference\n", new UTF8Encoding(false));
            reference["raw_path"] = "experiments/automated-smoke/independent.raw";
            reference["sha256"] = PronunciationAudit.Hash(Path("independent.raw"));
            Write(Path("reference.source.json"), reference);
            var rows = ReadRows("tar-synthetic-v1.0.0.evidence.jsonl");
            var roots = new[] { shared ? "tar-synthetic" : "independent-reference" };
            var set = new AutomatedReferenceSet
            {
                SchemaVersion = "2.0.0",
                ProfileSha256 = JsonNode.Parse(File.ReadAllText(Path("tar-synthetic-v1.0.0.manifest.json")))!["profile_sha256"]!.GetValue<string>(),
                GeneratorRevision = "synthetic-generator-v1",
                ReferenceSources = [new(new("experiments/automated-smoke/reference.source.json",
                    PronunciationAudit.HashConfiguration(Path("reference.source.json"))), roots)],
                Records = [.. rows.Take(count).Select(row => new PronunciationReference
                {
                    LemmaId = "tar-synthetic:" + PronunciationPolicyEvaluator.StableId(
                        "tar-synthetic\u001f" + row!["lexical_identity"]!.GetValue<string>()),
                    GeneratedIpa = row["normalized_ipa"]!.GetValue<string>(),
                    ReferenceIpa = row["normalized_ipa"]!.GetValue<string>(),
                    ReferenceOrigin = "dictionary-supplied",
                    GeneratorDependencies = ["tar-synthetic"],
                    ReferenceDependencies = [.. roots.Append("independent-reference").Distinct(StringComparer.Ordinal)],
                    HeldOut = true,
                    IndependenceEstablished = true,
                })],
            };
            Write(Path("references.json"), JsonSerializer.SerializeToNode(set, AutomatedProtocol.JsonOptions)!);
            PinReferences();
        }

        public void Dispose() => Directory.Delete(Root, true);

        public static void Write(string path, JsonNode value) => File.WriteAllText(path,
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));

        private void RepinProfile(string id)
        {
            var name = id + "-v1.0.0.manifest.json";
            Mutate(name, value =>
            {
                foreach (var artifact in value["artifacts"]!.AsArray().Concat(value["source_manifests"]!.AsArray()))
                {
                    artifact!["sha256"] = PronunciationAudit.Hash(System.IO.Path.Combine(Root,
                        artifact["path"]!.GetValue<string>().Replace('/', System.IO.Path.DirectorySeparatorChar)));
                }
            });
            Mutate("run.json", value =>
            {
                foreach (var source in value["sources"]!.AsArray().Where(source => source!["profile_id"]!.GetValue<string>() == id))
                {
                    source!["profile_manifest"]!["sha256"] = PronunciationAudit.HashConfiguration(Path(name));
                }
            });
        }

        private void Copy(string relative, params string[] excluded)
        {
            var source = System.IO.Path.Combine(RepositoryRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            var destination = System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                if (!excluded.Contains(System.IO.Path.GetFileName(file), StringComparer.Ordinal))
                {
                    File.Copy(file, System.IO.Path.Combine(destination, System.IO.Path.GetFileName(file)));
                }
            }

            foreach (var child in Directory.EnumerateDirectories(source))
            {
                if (!excluded.Contains(System.IO.Path.GetFileName(child), StringComparer.Ordinal))
                {
                    Copy(relative + "/" + System.IO.Path.GetFileName(child), excluded);
                }
            }
        }
    }
}
