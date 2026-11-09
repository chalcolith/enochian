using Enochian.Benchmark;
using Enochian.Provenance;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class MappingPreregistrationTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly JsonSerializerOptions JsonOptions =
        new(AutomatedProtocol.JsonOptions) { WriteIndented = true };

    [TestMethod]
    public void PendingPackageMaterializesCompleteProposalButRequiresExplicitDecision()
    {
        using var fixture = new Fixture();
        fixture.WriteInventory();
        fixture.WriteConfig("pending", null);

        Assert.AreEqual(1, fixture.Run());
        var report = fixture.Report();
        Assert.IsFalse(report.ReadyForConfirmatory);
        CollectionAssert.Contains(report.Blockers, "explicit_researcher_decision_required");
        Assert.IsTrue(File.Exists(fixture.Path("artifacts/mapping.json")));
        Assert.AreEqual(30, report.FeatureDimensions.Length);
        CollectionAssert.AreEqual(new[] { -1d, 0d, 1d }, report.AllowedFeatureValues);
    }

    [TestMethod]
    public void ExactApprovedProposalIsMaterializedAndByteStable()
    {
        using var fixture = new Fixture();
        fixture.WriteInventory();
        fixture.WriteConfig("pending", null);
        Assert.AreEqual(1, fixture.Run());
        var first = File.ReadAllBytes(fixture.Path("artifacts/mapping.json"));
        var hash = PronunciationAudit.HashConfiguration(fixture.Path("artifacts/mapping.json"));
        fixture.WriteConfig("approved", new MappingDecision
        {
            Status = "approved",
            Researcher = "researcher",
            DecidedOn = new DateOnly(2026, 11, 9),
            Rationale = "Approve the exact unscored proposal and normalization assumptions.",
            CandidateId = "test-proposal",
            MappingSha256 = hash,
        });

        Assert.AreEqual(0, fixture.Run());
        Assert.IsTrue(fixture.Report().ReadyForConfirmatory);
        CollectionAssert.AreEqual(first, File.ReadAllBytes(fixture.Path("artifacts/mapping.json")));
    }

    [TestMethod]
    public void IncompleteProposalCannotBeApproved()
    {
        using var fixture = new Fixture();
        fixture.WriteInventory();
        _ = fixture.Mappings.Remove("d");
        fixture.WriteConfig("approved", new MappingDecision
        {
            Status = "approved",
            Researcher = "researcher",
            DecidedOn = new DateOnly(2026, 11, 9),
            Rationale = "Attempt to approve an incomplete proposal.",
            CandidateId = "test-proposal",
            MappingSha256 = new string('0', 64),
        });

        _ = Assert.ThrowsExactly<InvalidDataException>(() => fixture.Run());
    }

    [TestMethod]
    public void ExplicitDeclineIsRecordedWithoutMaterializingAMapping()
    {
        using var fixture = new Fixture();
        fixture.WriteConfig("declined", new MappingDecision
        {
            Status = "declined",
            Researcher = "researcher",
            DecidedOn = new DateOnly(2026, 11, 9),
            Rationale = "Decline to run the confirmatory mapping hypothesis.",
            CandidateId = null,
            MappingSha256 = null,
        }, includeProposal: false);

        Assert.AreEqual(0, fixture.Run());
        Assert.IsFalse(fixture.Report().ReadyForConfirmatory);
        Assert.IsFalse(File.Exists(fixture.Path("artifacts/mapping.json")));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "enochian-mapping-" + Guid.NewGuid().ToString("N"));
        private readonly MappingPreregistration runner;

        public Fixture()
        {
            Copy("resources/encodings/features.json");
            Copy("resources/encodings/ipa.json");
            Copy("experiments/schemas/mapping-preregistration.schema.json");
            Copy("experiments/schemas/mapping-symbol-inventory.schema.json");
            WriteText("artifacts/transcription.evt", "evaluation-only fixture\n");
            var flow = new Enochian.Flow.Flow(
                System.IO.Path.Combine(RepositoryRoot, "samples", "ipatransducer.json"));
            runner = new MappingPreregistration(
                root,
                flow.FeatureSets.Single(item => item.Id == "Default"),
                flow.Encodings.Single(item => item.Id == "IPA"));
        }

        public SortedDictionary<string, string> Mappings { get; } = new(StringComparer.Ordinal)
        {
            ["a"] = "p",
            ["b"] = "t",
            ["c"] = "k",
            ["d"] = "m",
        };

        public string Path(string relative) =>
            System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public void WriteInventory()
        {
            WriteJson("artifacts/inventory.json", new MappingSymbolInventory
            {
                SchemaVersion = "1.0.0",
                InventoryId = "test-evaluation-inventory",
                Transcription = new PinnedInput(
                    "artifacts/transcription.evt",
                    PronunciationAudit.Hash(Path("artifacts/transcription.evt"))),
                EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"],
                HoldoutOpened = false,
                Symbols =
                [
                    new("a", 1, ["f1r"]),
                    new("b", 1, ["f1v"]),
                    new("c", 1, ["f2r"]),
                    new("d", 1, ["f2v"]),
                ],
            });
        }

        public void WriteConfig(string status, MappingDecision? decision, bool includeProposal = true)
        {
            MappingCandidate[] candidates = includeProposal
                ?
                [
                    new MappingCandidate
                    {
                        Id = "test-proposal",
                        Version = "1.0.0",
                        Kind = "proposal",
                        Description = "Unscored test proposal.",
                        Source = null,
                        LinguisticRationale = "Fixture rationale.",
                        DifferencesFromLegacy = ["Fixture difference."],
                        Normalization = new("NFC", "preserve", "reject"),
                        Segmentation = new("longest-match", [], "longest then ordinal"),
                        TranscriptionAmbiguities = [],
                        SymbolToIpa = Mappings,
                        MaterializedMapping = "artifacts/mapping.json",
                    },
                ]
                :
                [
                    new MappingCandidate
                    {
                        Id = "legacy",
                        Version = "1.0.0",
                        Kind = "legacy",
                        Description = "Legacy reference.",
                        Source = null,
                        LinguisticRationale = "Historical reference only.",
                        DifferencesFromLegacy = [],
                        Normalization = new("NFC", "legacy", "legacy"),
                        Segmentation = new("legacy", [], "legacy"),
                        TranscriptionAmbiguities = [],
                        SymbolToIpa = null,
                        MaterializedMapping = null,
                    },
                ];
            WriteJson("experiments/config.json", new MappingPreregistrationConfig
            {
                Schema = "schemas/mapping-preregistration.schema.json",
                SchemaVersion = "1.0.0",
                PreregistrationId = "test-preregistration",
                Status = status,
                ComparativeResultsInspected = false,
                Partitions = new(["f1r", "f1v", "f2r", "f2v"], ["f3r", "f3v", "f4r", "f4v"], false),
                Features = ConfigurationPin("resources/encodings/features.json"),
                IpaEncoding = ConfigurationPin("resources/encodings/ipa.json"),
                Inventory = includeProposal
                    ? new PinnedInput("artifacts/inventory.json", PronunciationAudit.Hash(Path("artifacts/inventory.json")))
                    : null,
                Candidates = candidates,
                ProposalCandidateId = includeProposal ? "test-proposal" : null,
                Decision = decision,
                Outputs = new("reports/decision.json", "reports/decision.md"),
            });
        }

        public int Run() => runner.Run(Path("experiments/config.json"));

        public MappingPreregistrationReport Report() =>
            JsonSerializer.Deserialize<MappingPreregistrationReport>(
                File.ReadAllText(Path("reports/decision.json")),
                AutomatedProtocol.JsonOptions)!;

        public void Dispose()
        {
            Directory.Delete(root, true);
        }

        private PinnedInput ConfigurationPin(string relative) =>
            new(relative, PronunciationAudit.HashConfiguration(Path(relative)));

        private void Copy(string relative)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(relative))!);
            File.Copy(System.IO.Path.Combine(RepositoryRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)),
                Path(relative));
        }

        private void WriteJson<T>(string relative, T value)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(relative))!);
            File.WriteAllText(
                Path(relative),
                JsonSerializer.Serialize(value, JsonOptions) + "\n",
                new UTF8Encoding(false));
        }

        private void WriteText(string relative, string value)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(relative))!);
            File.WriteAllText(Path(relative), value, new UTF8Encoding(false));
        }
    }
}
