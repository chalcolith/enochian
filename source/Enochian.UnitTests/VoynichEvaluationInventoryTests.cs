using Enochian.Provenance;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class VoynichEvaluationInventoryTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string[] ExpectedSymbols = ["a", "b", "e", "f", "g", "h", "i", "j"];
    private static readonly JsonSerializerOptions JsonOptions =
        new(PronunciationAudit.JsonOptions) { WriteIndented = true };

    [TestMethod]
    public void EvaluationInventoryIsByteStableAndNeverTokenizesHoldoutOrOtherLayers()
    {
        using var fixture = new Fixture();

        var first = fixture.Run();
        var outputs = fixture.Outputs();
        var second = fixture.Run();

        Assert.AreEqual(4, first.AcceptedTokens);
        Assert.AreEqual(1, first.ExcludedTokens);
        Assert.AreEqual(8, first.UniqueSymbols);
        Assert.IsTrue(first.ReadyForMappingDecision);
        Assert.IsFalse(first.HoldoutOpened);
        Assert.AreEqual(first.InventorySha256, second.InventorySha256);
        Assert.AreEqual(first.AcceptedTokens, second.AcceptedTokens);
        Assert.AreEqual(first.ExcludedTokens, second.ExcludedTokens);
        foreach (var output in outputs)
        {
            CollectionAssert.AreEqual(output.Value, File.ReadAllBytes(fixture.Path(output.Key)), output.Key);
        }

        var inventory = fixture.Inventory();
        CollectionAssert.AreEqual(
            ExpectedSymbols,
            inventory.Symbols.Select(item => item.Symbol).ToArray());
        var exclusion = File.ReadAllText(fixture.Path("reports/exclusions.jsonl"));
        StringAssert.Contains(exclusion, "\"token\":\"cd*\"");
        Assert.IsFalse(exclusion.Contains("holdout", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AlteredSourcePinIsRejected()
    {
        using var fixture = new Fixture();
        fixture.WritePlan(sourceHash: new string('0', 64));

        _ = Assert.ThrowsExactly<InvalidDataException>(fixture.Run);
    }

    [TestMethod]
    public void SelectedMalformedInlineCommentIsRejected()
    {
        using var fixture = new Fixture();
        fixture.WriteSource(
            """
            <f1r.P.1;H> ab{broken-
            <f1v.P.1;H> ef=
            <f2r.P.1;H> gh=
            <f2v.P.1;H> ij=
            """);
        fixture.WritePlan();

        _ = Assert.ThrowsExactly<InvalidDataException>(fixture.Run);
    }

    [TestMethod]
    public void OutputCannotOverwritePinnedSource()
    {
        using var fixture = new Fixture();
        fixture.WritePlan(inventoryPath: "artifacts/source.evt");

        _ = Assert.ThrowsExactly<InvalidDataException>(fixture.Run);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "enochian-voynich-inventory-" + Guid.NewGuid().ToString("N"));
        private readonly VoynichEvaluationInventory inventory;

        public Fixture()
        {
            Copy("experiments/schemas/voynich-inventory-extraction.schema.json");
            Copy("experiments/schemas/mapping-symbol-inventory.schema.json");
            WriteSource(
                """
                <f1r.P.1;H> ab.cd*-
                <f1r.P.1;C> {malformed
                <f1v.P.1;H> ef=
                <f2r.P.1;H> gh=
                <f2v.P.1;H> ij=
                <f3r.P.1;H> {holdout text is intentionally malformed
                """);
            WritePlan();
            inventory = new(root);
        }

        public string Path(string relative) =>
            System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        public VoynichInventoryReport Run() => inventory.Run(Path("resources/plan.json"));

        public VoynichSymbolInventory Inventory() =>
            JsonSerializer.Deserialize<VoynichSymbolInventory>(
                File.ReadAllText(Path("artifacts/inventory.json")),
                PronunciationAudit.JsonOptions)!;

        public SortedDictionary<string, byte[]> Outputs() =>
            new(StringComparer.Ordinal)
            {
                ["artifacts/inventory.json"] = File.ReadAllBytes(Path("artifacts/inventory.json")),
                ["reports/exclusions.jsonl"] = File.ReadAllBytes(Path("reports/exclusions.jsonl")),
                ["reports/coverage.json"] = File.ReadAllBytes(Path("reports/coverage.json")),
                ["reports/coverage.md"] = File.ReadAllBytes(Path("reports/coverage.md")),
            };

        public void WriteSource(string value)
        {
            _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path("artifacts/source.evt"))!);
            File.WriteAllText(Path("artifacts/source.evt"), value.ReplaceLineEndings("\n"), Encoding.Latin1);
        }

        public void WritePlan(string? sourceHash = null, string inventoryPath = "artifacts/inventory.json")
        {
            WriteJson("resources/plan.json", new VoynichInventoryPlan
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
                        sourceHash ?? PronunciationAudit.Hash(Path("artifacts/source.evt"))),
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
                    inventoryPath,
                    "reports/exclusions.jsonl",
                    "reports/coverage.json",
                    "reports/coverage.md"),
            });
        }

        public void Dispose()
        {
            Directory.Delete(root, true);
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
                JsonSerializer.Serialize(value, JsonOptions) + "\n",
                new UTF8Encoding(false));
        }
    }
}
