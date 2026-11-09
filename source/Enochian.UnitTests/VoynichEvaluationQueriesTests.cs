using Enochian.Provenance;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public sealed class VoynichEvaluationQueriesTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string[] RepeatedTypeLoci = ["f1r", "f1v"];
    private static readonly double[] ZeroVector = [0];
    private static readonly JsonSerializerOptions JsonOptions =
        new(PronunciationAudit.JsonOptions) { WriteIndented = true };

    [TestMethod]
    public void CrossLocusTypesAreAggregatedAndEveryEvaluationLocusIsRepresented()
    {
        using var fixture = new Fixture();

        var first = fixture.Run();
        var outputs = fixture.Outputs();
        var second = fixture.Run();

        Assert.AreEqual(6, first.AcceptedTokens);
        Assert.AreEqual(5, first.UniqueTypes);
        Assert.IsTrue(first.ReadyForPreflight);
        Assert.IsTrue(first.MappingApproved);
        Assert.IsFalse(first.HoldoutOpened);
        Assert.IsTrue(first.Loci.All(locus => locus.RepresentativeTypes > 0));
        var repeated = first.Queries.Single(query => query.Type == "ab");
        Assert.AreEqual(2, repeated.TokenFrequency);
        CollectionAssert.AreEqual(
            RepeatedTypeLoci,
            repeated.Sources.Select(source => source.Locus).ToArray());
        Assert.AreEqual(first.QueriesSha256, second.QueriesSha256);
        foreach (var output in outputs)
        {
            CollectionAssert.AreEqual(output.Value, File.ReadAllBytes(fixture.Path(output.Key)), output.Key);
        }

        Assert.IsFalse(File.ReadAllText(fixture.Path("artifacts/queries.jsonl"))
            .Contains("holdout", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AlteredInputPinIsRejected()
    {
        using var fixture = new Fixture();
        fixture.WriteQueryPlan(mappingHash: new string('0', 64));

        _ = Assert.ThrowsExactly<InvalidDataException>(fixture.Run);
    }

    [TestMethod]
    public void PendingMappingDecisionCannotMaterializeQueries()
    {
        using var fixture = new Fixture();
        fixture.WriteApproval(approved: false);
        fixture.WriteQueryPlan();

        var exception = Assert.ThrowsExactly<InvalidDataException>(fixture.Run);
        StringAssert.Contains(exception.Message, "not approved");
    }

    [TestMethod]
    public void QueryOutputCannotOverwritePinnedInput()
    {
        using var fixture = new Fixture();
        fixture.WriteQueryPlan(queryPath: "artifacts/mapping.json");

        _ = Assert.ThrowsExactly<InvalidDataException>(fixture.Run);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "enochian-voynich-queries-" + Guid.NewGuid().ToString("N"));
        private readonly VoynichEvaluationQueries materializer;

        public Fixture()
        {
            foreach (var file in new[]
            {
                "experiments/schemas/voynich-inventory-extraction.schema.json",
                "experiments/schemas/mapping-symbol-inventory.schema.json",
                "experiments/schemas/voynich-query-materialization.schema.json",
                "experiments/schemas/voynich-evaluation-query.schema.json",
            })
            {
                Copy(file);
            }

            WriteSource(
                """
                <f1r.P.1;H> ab.ac=
                <f1v.P.1;H> ab.de=
                <f2r.P.1;H> fg=
                <f2v.P.1;H> hi=
                <f3r.P.1;H> {holdout text is intentionally malformed
                """);
            WriteInventoryPlan();
            _ = new VoynichEvaluationInventory(root).Run(Path("resources/inventory-plan.json"));
            WriteMapping();
            WriteApproval();
            WriteQueryPlan();
            materializer = new(root);
        }

        public string Path(string relative) =>
            System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public VoynichQueryReport Run() =>
            materializer.Run(Path("resources/query-plan.json"));

        public SortedDictionary<string, byte[]> Outputs() =>
            new(StringComparer.Ordinal)
            {
                ["artifacts/queries.jsonl"] = File.ReadAllBytes(Path("artifacts/queries.jsonl")),
                ["reports/materialization.json"] = File.ReadAllBytes(Path("reports/materialization.json")),
                ["reports/materialization.md"] = File.ReadAllBytes(Path("reports/materialization.md")),
            };

        public void WriteApproval(bool approved = true)
        {
            var mappingHash = PronunciationAudit.HashConfiguration(Path("artifacts/mapping.json"));
            WriteJson("reports/approval.json", new JsonObject
            {
                ["status"] = approved ? "approved" : "pending",
                ["proposal_candidate_id"] = "fixture-proposal",
                ["decision"] = approved
                    ? new JsonObject
                    {
                        ["status"] = "approved",
                        ["candidate_id"] = "fixture-proposal",
                        ["mapping_sha256"] = mappingHash,
                    }
                    : null,
                ["ready_for_confirmatory"] = approved,
                ["blockers"] = approved
                    ? []
                    : new JsonArray("explicit_researcher_decision_required"),
                ["candidates"] = new JsonArray(
                    new JsonObject
                    {
                        ["id"] = "fixture-proposal",
                        ["materialized_mapping"] = "artifacts/mapping.json",
                        ["mapping_sha256"] = mappingHash,
                    }),
            });
        }

        public void WriteQueryPlan(
            string? mappingHash = null,
            string queryPath = "artifacts/queries.jsonl")
        {
            WriteJson("resources/query-plan.json", new VoynichQueryPlan
            {
                Schema = "../../experiments/schemas/voynich-query-materialization.schema.json",
                SchemaVersion = "1.0.0",
                MaterializationId = "fixture-queries",
                MaterializerVersion = "voynich-evaluation-queries-v1",
                Frozen = true,
                InventoryPlan = Pin("resources/inventory-plan.json"),
                Inventory = Pin("artifacts/inventory.json"),
                MappingPreregistration = Pin("reports/approval.json"),
                Mapping = new(
                    "artifacts/mapping.json",
                    mappingHash ?? PronunciationAudit.HashConfiguration(Path("artifacts/mapping.json"))),
                EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"],
                HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"],
                Aggregation = new(
                    "exact-normalized-symbol-sequence",
                    "sum-accepted-token-occurrences-across-evaluation-loci",
                    "maximize-source-frequency-then-frozen-locus-order-subject-to-all-loci-represented"),
                Outputs = new(
                    queryPath,
                    "reports/materialization.json",
                    "reports/materialization.md"),
            });
        }

        public void Dispose()
        {
            Directory.Delete(root, true);
        }

        private InventoryPin Pin(string relative) =>
            new(relative, PronunciationAudit.HashConfiguration(Path(relative)));

        private void WriteMapping()
        {
            var mapping = "abcdefghi".ToDictionary(
                character => character.ToString(),
                _ => ZeroVector,
                StringComparer.Ordinal);
            WriteJson("artifacts/mapping.json", mapping);
        }

        private void WriteInventoryPlan()
        {
            WriteJson("resources/inventory-plan.json", new VoynichInventoryPlan
            {
                Schema = "../../experiments/schemas/voynich-inventory-extraction.schema.json",
                SchemaVersion = "1.0.0",
                ExtractionId = "fixture-inventory",
                ExtractorVersion = "voynich-evaluation-inventory-v1",
                Frozen = true,
                Transcription = new()
                {
                    Id = "reeds-landini-stolfi-interlinear",
                    Release = "1.6e6",
                    TranscriberCode = "H",
                    Convention = "basic EVA, uncapitalized",
                    CharacterEncoding = "ISO-8859-1",
                    Snapshot = new(
                        "artifacts/source.evt",
                        PronunciationAudit.Hash(Path("artifacts/source.evt"))),
                    ReleaseUrl = "https://example.test/release",
                    PermissionUrl = "https://example.test/permission",
                    License = "LicenseRef-Stolfi-Attribution-NoAdditionalRestrictions",
                    Attribution = "Fixture attribution.",
                },
                EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"],
                HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"],
                Normalization = new()
                {
                    UnicodeNormalization = "NFC",
                    CaseTreatment = "preserve frozen uncapitalized H layer",
                    BreakCharacters = [".", ",", "-", "="],
                    AlignmentFillers = ["!", "%"],
                    InlineComments = "remove brace-delimited transcription metadata after locus selection",
                    AcceptedSymbols = "ASCII lowercase basic EVA a-z",
                    UncertainTokenPolicy =
                        "exclude whole token and report source line, locator, token, and reason",
                },
                Outputs = new(
                    "artifacts/inventory.json",
                    "reports/exclusions.jsonl",
                    "reports/inventory.json",
                    "reports/inventory.md"),
            });
        }

        private void WriteSource(string value)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path("artifacts/source.evt"))!);
            File.WriteAllText(
                Path("artifacts/source.evt"),
                value.ReplaceLineEndings("\n"),
                Encoding.Latin1);
        }

        private void Copy(string relative)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(relative))!);
            File.Copy(
                System.IO.Path.Combine(RepositoryRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)),
                Path(relative));
        }

        private void WriteJson<T>(string relative, T value)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(relative))!);
            File.WriteAllText(
                Path(relative),
                JsonSerializer.Serialize(value, JsonOptions).ReplaceLineEndings("\n") + "\n",
                new UTF8Encoding(false));
        }
    }
}
