using Enochian.Benchmark;
using Enochian.Provenance;
using Json.Schema;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class GeneratorReplayTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string[] ExpectedLiteralDependencies =
        ["Module:aliased", "Module:json", "Module:literal one", "Module:literal two", "Module:shorthand"];
    private static readonly string[] ExpectedOriginalReadings = ["/pat/", "/pat/", "[pat]", "\u00a0/pat/\u00a0"];

    [TestMethod]
    public void LiteralPrefetchNeverClaimsComputedDependencyClosure()
    {
        var code = """
            local a = require("Module:literal_one")
            local b = mw.loadData('Module:literal two')
            local c = mw.loadJsonData([[Module:json]])
            local d = require "Module:shorthand"
            local e = require("Module:prefix/" .. language)
            local f = mw.loadData(name)
            local g = "Module:aliased"
            local h = "Module:prefix/"
            local i = "Module:formatted/%s"
            """;
        CollectionAssert.AreEqual(ExpectedLiteralDependencies, GeneratorReplay.LiteralDependencies(code));
    }

    [TestMethod]
    public void ReplayProofAndIndependentReferenceShapesAreExplicitlyVersioned()
    {
        var row = Row();
        var request = Request(row);
        var proof = GeneratorReplay.Bind(row, request, Result(request), Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.IsTrue(Schema("pronunciation-replay-proof.schema.json")
            .Evaluate(JsonSerializer.SerializeToElement(proof, PronunciationAudit.JsonOptions)).IsValid);
        Assert.IsFalse(Schema("pronunciation-replay-proof.schema.json")
            .Evaluate(JsonSerializer.SerializeToElement(proof with { GeneratorRevision = null }, PronunciationAudit.JsonOptions)).IsValid);
        var references = new AutomatedReferenceSet
        {
            SchemaVersion = "2.1.0",
            ProfileSha256 = new string('b', 64),
            Generator = proof.Generator,
            GeneratorRevision = proof.GeneratorRevision!,
            ReferenceSources = [new(new("independent.json", new string('c', 64)), ["independent"])],
            Records = [],
        };
        var json = JsonSerializer.SerializeToElement(references, AutomatedProtocol.JsonOptions);
        Assert.IsTrue(Schema("automated-reference-set.v2.1.schema.json").Evaluate(json).IsValid);
        Assert.IsFalse(Schema("automated-reference-set.v2.schema.json").Evaluate(json).IsValid);
        var legacy = references with { SchemaVersion = "2.0.0", Generator = null, GeneratorRevision = "legacy-v1" };
        Assert.IsTrue(Schema("automated-reference-set.v2.schema.json")
            .Evaluate(JsonSerializer.SerializeToElement(legacy, AutomatedProtocol.JsonOptions)).IsValid);
        Assert.IsFalse(Schema("automated-reference-set.v2.1.schema.json")
            .Evaluate(JsonSerializer.SerializeToElement(references with { GeneratorRevision = "unresolved" }, AutomatedProtocol.JsonOptions)).IsValid);
    }

    [TestMethod]
    public void ReproducedGeneratorRetainsWeightsOriginalIpaAndExploratoryStatus()
    {
        var row = Row();
        var request = Request(row);
        var proof = GeneratorReplay.Bind(row, request, Result(request), Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.AreEqual("reproduced", proof.Status);
        Assert.AreEqual("wiktionary:hi-IPA", proof.Generator);
        Assert.AreEqual("mediawiki-replay-v1:" + new string('a', 64), proof.GeneratorRevision);
        var enriched = GeneratorReplay.Apply(row, proof);
        Assert.AreEqual(row.OriginalIpa, enriched.OriginalIpa);
        Assert.AreEqual(row.NormalizedIpa, enriched.NormalizedIpa);
        Assert.AreEqual(row.AlternativeWeight, enriched.AlternativeWeight);
        Assert.AreEqual(row.AlternativeCount, enriched.AlternativeCount);
        Assert.AreEqual(row.Source, enriched.Source);
        Assert.AreEqual(row.SourceEntry, enriched.SourceEntry);
        Assert.AreEqual(row.Snapshot, enriched.Snapshot);
        Assert.AreEqual(row.LexicalIdentity, enriched.LexicalIdentity);
        Assert.AreEqual("exploratory", enriched.EvidenceStatus);
        Assert.AreEqual("unknown", enriched.Independence);
        CollectionAssert.DoesNotContain(enriched.Dependencies, "unknown-generator-dependencies");
        CollectionAssert.Contains(enriched.Dependencies, "enwiktionary:Module:hi-IPA@43");
    }

    [TestMethod]
    [DataRow("/pˈat/")]
    [DataRow("[pat]")]
    [DataRow("/pa.t/")]
    public void NormalizedAgreementCannotReplaceOriginalBytes(string ipa)
    {
        var row = Row();
        var request = Request(row);
        var proof = GeneratorReplay.Bind(row, request, Result(request) with { Readings = [ipa] },
            Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.AreEqual("original_ipa_mismatch", proof.Status);
        Assert.IsNull(proof.GeneratorRevision);
        Assert.AreEqual(row, GeneratorReplay.Apply(row, proof));
    }

    [TestMethod]
    public void MissingDynamicDependenciesAndUnavailableModulesAreExplicitlyBlocked()
    {
        var row = Row();
        var request = Request(row);
        var missing = Result(request) with { Requested = ["Module:dynamic"] };
        var proof = GeneratorReplay.Bind(row, request, missing, Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.AreEqual("missing_execution_dependency", proof.Status);
        var sources = Sources();
        sources["Module:hi-IPA"] = sources["Module:hi-IPA"] with { Revision = 0, Timestamp = null };
        proof = GeneratorReplay.Bind(row, request, Result(request) with { Errors = ["Lua error: module not found"] },
            sources, new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.AreEqual("missing_generator_dependency", proof.Status);
        Assert.IsNull(proof.GeneratorRevision);
    }

    [TestMethod]
    public void PinnedOptionalModuleAbsenceDoesNotBlockSuccessfulExecution()
    {
        var row = Row();
        var request = Request(row);
        var sources = Sources();
        const string optional = "Module:labels/data/lang/hi/functions";
        sources.Add(optional, sources["Module:hi-IPA"] with { Title = optional, Revision = 0, Timestamp = null });
        var result = Result(request) with { Requested = [.. Result(request).Requested, optional] };
        var proof = GeneratorReplay.Bind(row, request, result, sources,
            new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.AreEqual("reproduced", proof.Status);
        CollectionAssert.Contains(proof.Dependencies, "enwiktionary:" + optional + "@0");
    }

    [TestMethod]
    public void OriginalDistributionUsesOwnedParsoidReadingsNotSelectedLexicalRows()
    {
        var request = Request(Row());
        var html = "<div about=\"" + request.TransclusionId + "\"><span class=\"IPA\">/pat/</span>" +
            "<span class=\"IPA\"> /pat/ </span><span class=\"IPA\">[p&#97;t]</span>" +
            "<span class=\"IPA\">&nbsp;/pat/&nbsp;</span>" +
            "<div about=\"#other\"><span class=\"IPA\">/other/</span></div></div>" +
            "<span class=\"IPA\">/unowned/</span>";
        CollectionAssert.AreEqual(ExpectedOriginalReadings,
            GeneratorReplay.OriginalReadings(request, html));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => GeneratorReplay.OriginalReadings(request,
            "<span class=\"IPA\">/unowned/</span>"));
    }

    [TestMethod]
    public void RepeatedOriginalAlternativesRetainTheirMultiplicity()
    {
        var row = Row();
        var request = Request(row);
        var result = Result(request) with { Readings = ["/pat/", "/pat/"] };
        var proof = GeneratorReplay.Bind(row, request, result, Sources(),
            new string('a', 64), ["runtime:pinned"], ["/pat/", "/pat/"]);
        Assert.AreEqual("reproduced", proof.Status);
    }

    [TestMethod]
    public void RuntimeInputsAndGeneratorErrorsCannotGrantReproducibility()
    {
        var row = Row();
        var request = Request(row);
        var result = Result(request) with { Unsupported = ["mw.title.getCategories"] };
        Assert.AreEqual("unresolved_runtime_input", GeneratorReplay.Bind(row, request, result, Sources(),
            new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]).Status);
        result = Result(request) with { Errors = ["Lua error"] };
        Assert.AreEqual("generator_execution_error", GeneratorReplay.Bind(row, request, result, Sources(),
            new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]).Status);
    }

    [TestMethod]
    public void ChangedOwnershipConventionAlternativeOrWeightInvalidatesProof()
    {
        var row = Row();
        var request = Request(row);
        var proof = GeneratorReplay.Bind(row, request, Result(request), Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        foreach (var changed in new[]
        {
            row with { Convention = "other" },
            row with { LexicalIdentity = "test\u001fverb\u001f2" },
            row with { SourceEntry = "https://en.wiktionary.org/wiki/other" },
            row with { AlternativeWeight = 1 },
            row with { AlternativeCount = 1 },
            row with { OriginalIpa = "[pat]" },
        })
        {
            _ = Assert.ThrowsExactly<InvalidDataException>(() => GeneratorReplay.Apply(changed, proof));
        }

        _ = Assert.ThrowsExactly<InvalidDataException>(() => GeneratorReplay.Bind(row, request,
            Result(request) with { Id = "other" }, Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => GeneratorReplay.Bind(row, request with { PageRevision = 43 },
            Result(request), Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]));
    }

    [TestMethod]
    [DataRow("respelling")]
    [DataRow("respelling=literal")]
    public void CanonicalOriginalFrameIncludesRespellingAndPageTitle(string respelling)
    {
        var row = Row();
        var origin = new PronunciationOriginProof("1.0.0", row.EntryId, "parent", row.Language,
            row.LexicalIdentity, row.Convention, row.OriginalIpa, 42, "html", "generated_revision_and_dependencies_unresolved",
            "template-generated", "hi-IPA", "#mwt1");
        var metadata = JsonSerializer.Serialize(new
        {
            parts = new[]
            {
                new
                {
                    template = new
                    {
                        target = new { wt = "hi-IPA" },
                        @params = new Dictionary<string, object>
                        {
                            ["1"] = new { wt = respelling },
                            ["q"] = new { wt = "regional" },
                        },
                    },
                },
            },
        });
        var html = "<span about='#mwt1' data-mw='" + metadata + "'><span class='IPA'>/pat/</span></span>";
        var request = GeneratorReplay.CreateRequest(origin, [row], html);
        Assert.AreEqual($"{{{{hi-IPA|1={respelling}|q=regional}}}}", request.Invocation);
        Assert.AreEqual(row.Lemma, request.Title);
        Assert.AreEqual(42L, request.PageRevision);
        CollectionAssert.AreEqual(new[] { row.EntryId }, request.EntryIds);
        _ = Assert.ThrowsExactly<InvalidDataException>(() =>
            GeneratorReplay.CreateRequest(origin, [row], html + html));
        _ = Assert.ThrowsExactly<InvalidDataException>(() =>
            GeneratorReplay.CreateRequest(origin with { Template = "sa-IPA" }, [row], html));
    }

    private static PronunciationEvidence Row() => new()
    {
        EntryId = "fixture:reading",
        Source = "fixture",
        Snapshot = "fixture-v1",
        SourceEntry = "https://en.wiktionary.org/wiki/test",
        Language = "hin",
        Lemma = "test",
        PartOfSpeech = "noun",
        LexicalIdentity = "test\u001fnoun\u001f0",
        Convention = "hindi",
        Labels = [],
        OriginalIpa = "/pat/",
        NormalizedIpa = "pat",
        Origin = "template-generated",
        Generator = "wiktionary:hi-IPA",
        Dependencies = ["enwiktionary", "unknown-generator-dependencies"],
        AlternativeCount = 2,
        AlternativeWeight = 0.5,
        PhonemeLength = 3,
        LengthBand = "03-05",
        UnknownSegments = [],
        Exclusions = [],
        EvidenceStatus = "exploratory",
    };

    private static GeneratorReplayRequest Request(PronunciationEvidence row) =>
        new("request", row.Lemma, 42, "hi-IPA", "#mwt1", "{{hi-IPA}}", [row.EntryId]);

    private static GeneratorReplayResult Result(GeneratorReplayRequest request) =>
        new(request.Id, ["/pat/", "/bat/"], ["test", "Template:hi-IPA", "Module:hi-IPA"], [], []);

    private static Dictionary<string, RecoveryPage> Sources() => new(StringComparer.Ordinal)
    {
        ["test"] = new("test", 42, "2026-09-01T00:00:00Z", new("page.json", "hash"), null),
        ["Template:hi-IPA"] = new("Template:hi-IPA", 42, "2026-09-01T00:00:00Z", new("template.json", "hash"), null),
        ["Module:hi-IPA"] = new("Module:hi-IPA", 43, "2026-09-01T00:00:00Z", new("module.json", "hash"), null),
    };

    [TestMethod]
    [DataRow("/pat/")]
    [DataRow("/pat/", "/pat/")]
    [DataRow("/pat/", "/cat/")]
    [DataRow("/pat/", "/bat/", "/cat/")]
    public void RemovedDuplicatedChangedOrAddedAlternativesCannotReproduce(params string[] readings)
    {
        var row = Row();
        var request = Request(row);
        var proof = GeneratorReplay.Bind(row, request, Result(request) with { Readings = readings },
            Sources(), new string('a', 64), ["runtime:pinned"], ["/pat/", "/bat/"]);
        Assert.AreEqual("alternative_distribution_mismatch", proof.Status);
        Assert.IsNull(proof.GeneratorRevision);
        Assert.AreEqual(row, GeneratorReplay.Apply(row, proof));
    }

    private static JsonSchema Schema(string name) => JsonSchema.FromText(
        File.ReadAllText(Path.Combine(RepositoryRoot, "resources", "lexicons", "schemas", name)),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() });
}
