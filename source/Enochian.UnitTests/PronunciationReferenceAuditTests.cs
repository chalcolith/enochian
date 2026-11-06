using Enochian.Provenance;
using Json.Schema;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.UnitTests;

[TestClass]
public sealed class PronunciationReferenceAuditTests
{
    private static readonly string RepositoryRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string[] ExpectedAlternatives = ["/pat/", "[bat]"];
    private static readonly string[] ExpectedRejectionReasons =
    [
        "license_unverified",
        "missing_acquired_source_manifest",
        "missing_definitions_free_reference_records",
        "missing_or_unknown_reference_dependencies",
        "not_direct_dictionary_ipa",
        "pronunciation_convention_mismatch",
        "shared_generator_dependency",
        "usage_not_unrestricted",
    ];

    [TestMethod]
    public void FrozenAuditPlanAndAlternativeReferenceContractAreSchemaValid()
    {
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot, "resources", "lexicons", "pronunciation-reference-audit.v1.json")));
        Assert.IsTrue(Schema("pronunciation-reference-audit.schema.json").Evaluate(plan.RootElement).IsValid);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot, "reports", "pronunciation-references", "v1", "coverage.json")));
        Assert.IsTrue(Schema("pronunciation-reference-audit-report.schema.json").Evaluate(report.RootElement).IsValid);
        var set = PronunciationReferenceAudit.BuildSet(Profile(), new string('a', 64), "wiktionary:hi-IPA",
            "mediawiki-replay-v1:" + new string('b', 64), 7, Evidence(),
            [Reference("/pat/", "[bat]")],
            [new(new("independent.json", new string('c', 64)), ["independent-root"])],
            ["independent-source"]);
        Assert.IsTrue(Schema("automated-reference-set.v2.2.schema.json")
            .Evaluate(JsonSerializer.SerializeToElement(set, PronunciationAudit.JsonOptions)).IsValid);
        Assert.IsFalse(Schema("automated-reference-set.v2.2.schema.json").Evaluate(
            JsonSerializer.SerializeToElement(set with { GeneratorRevision = "wrong" },
                PronunciationAudit.JsonOptions)).IsValid);
        Assert.HasCount(1, set.Records);
        CollectionAssert.AreEqual(ExpectedAlternatives, set.Records[0].GeneratedIpa);
        CollectionAssert.AreEqual(ExpectedAlternatives, set.Records[0].ReferenceIpa);
    }

    [TestMethod]
    public void EligibilityRejectsSharedGeneratedUnlicensedAndUnpinnedSources()
    {
        var candidate = Candidate() with
        {
            EvidenceKind = "generated-pronunciation",
            LicenseStatus = "unverified",
            UsagePolicy = "unverified",
            Independence = "shared",
            Convention = "unknown",
            Manifest = null,
            Records = null,
            Dependencies = ["unknown-source"],
        };
        CollectionAssert.AreEqual(ExpectedRejectionReasons,
            PronunciationReferenceAudit.AssessmentReasons(candidate, "hindi"));
        Assert.HasCount(0, PronunciationReferenceAudit.AssessmentReasons(Candidate(), "hindi"));
    }

    [TestMethod]
    public void FullAlternativeDistributionsCannotBeCherryPickedOrTuned()
    {
        var policy = new PronunciationPolicy
        {
            MinimumReferences = 1,
            MinimumPerBand = 1,
            RequiredBands = ["03-05"],
        };
        var exact = AlternativeReference(["/pat/", "[bat]"], ["/pat/", "[bat]"]);
        var report = PronunciationPolicyEvaluator.EvaluateReferences([exact], policy, Encoder());
        Assert.AreEqual("pass", report.Status);
        Assert.AreEqual(1, report.ExactAgreement);
        report = PronunciationPolicyEvaluator.EvaluateReferences(
            [exact with { ReferenceIpa = ["/pat/"] }], policy, Encoder());
        Assert.AreEqual("exploratory", report.Status);
        Assert.AreEqual(0, report.ExactAgreement);
        report = PronunciationPolicyEvaluator.EvaluateReferences(
            [exact with { TuningExample = true }], policy, Encoder());
        Assert.AreEqual("exploratory", report.Status);
        Assert.AreEqual(1, report.Diagnostics["tuning_leakage"]);
    }

    [TestMethod]
    public void AmbiguousIndependentDistributionsAreExcludedRatherThanSelected()
    {
        var set = PronunciationReferenceAudit.BuildSet(Profile(), new string('a', 64), "wiktionary:hi-IPA",
            "mediawiki-replay-v1:" + new string('b', 64), 7, Evidence(),
            [Reference("/pat/", "[bat]"), Reference("/pat/", "[cat]") with { SourceRecordId = "reference-2" }],
            [new(new("independent.json", new string('c', 64)), ["independent-root"])],
            ["independent-source"]);
        Assert.HasCount(0, set.Records);
        _ = Assert.ThrowsExactly<InvalidDataException>(() => PronunciationReferenceAudit.BuildSet(
            Profile(), new string('a', 64), "wiktionary:hi-IPA",
            "mediawiki-replay-v1:" + new string('b', 64), 7, Evidence(),
            [Reference("/pat/", "[bat]")],
            [new(new("independent.json", new string('c', 64)), ["generator-root"])],
            ["independent-source"]));
    }

    private static ReferenceAuditCandidate Candidate() => new()
    {
        Id = "independent",
        Language = "hin",
        Url = "https://example.com/reference",
        Revision = "v1",
        License = "CC-BY-4.0",
        LicenseStatus = "verified",
        UsagePolicy = "unrestricted",
        EvidenceKind = "dictionary-ipa",
        MachineReadable = true,
        Immutable = true,
        Independence = "established",
        Convention = "hindi",
        Manifest = new("manifest.json", new string('a', 64)),
        Records = new("records.jsonl", new string('b', 64)),
        Dependencies = ["independent-root"],
        Decision = "eligible",
        Reasons = [],
    };

    private static IndependentPronunciationRecord Reference(params string[] ipa) => new()
    {
        SourceRecordId = "reference-1",
        Language = "hin",
        Lemma = "lemma",
        Convention = "hindi",
        Ipa = ipa,
        ReferenceOrigin = "dictionary-supplied",
        Dependencies = ["independent-root"],
        TuningExample = false,
        IndependenceEstablished = true,
    };

    private static PronunciationAlternativeReference AlternativeReference(
        string[] generated, string[] reference) => new()
        {
            LemmaId = "lemma-1",
            SourceRecordIds = ["reference-1"],
            GeneratedIpa = generated,
            ReferenceIpa = reference,
            ReferenceOrigin = "dictionary-supplied",
            GeneratorDependencies = ["generator"],
            ReferenceDependencies = ["independent"],
            HeldOut = true,
            TuningExample = false,
            IndependenceEstablished = true,
        };

    private static PronunciationProfile Profile() => new()
    {
        Id = "hin-dictionary",
        Version = "1.0.0",
        Language = "hin",
        Convention = "hindi",
        RequiredLabels = [],
        RejectedLabels = [],
        Fallback = "exploratory",
        Rules = new(StringComparer.Ordinal),
    };

    private static PronunciationEvidence[] Evidence() =>
    [
        Evidence("entry-1", "/pat/"),
        Evidence("entry-2", "[bat]"),
    ];

    private static PronunciationEvidence Evidence(string id, string ipa) => new()
    {
        EntryId = id,
        Source = "generator-root",
        Snapshot = "snapshot",
        SourceEntry = "https://example.com/lemma",
        Language = "hin",
        Lemma = "lemma",
        PartOfSpeech = "noun",
        LexicalIdentity = "lemma\u001fnoun\u001f0",
        Convention = "hindi",
        Labels = [],
        OriginalIpa = ipa,
        NormalizedIpa = PronunciationPolicyEvaluator.NormalizeIpa(ipa),
        Origin = "template-generated",
        Generator = "wiktionary:hi-IPA",
        GeneratorRevision = "mediawiki-replay-v1:" + new string('b', 64),
        Dependencies = ["generator-root"],
        AlternativeCount = 2,
        AlternativeWeight = 0.5,
        PhonemeLength = 3,
        LengthBand = "03-05",
        UnknownSegments = [],
        Exclusions = [],
        EvidenceStatus = "exploratory",
    };

    private static IpaEncoder Encoder()
    {
        var flow = new Enochian.Flow.Flow(Path.Combine(RepositoryRoot, "samples", "ipatransducer.json"));
        var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var encoding = flow.Encodings.Single(candidate =>
            string.Equals(candidate.Id, "IPA", StringComparison.OrdinalIgnoreCase));
        return new(features, encoding);
    }

    private static JsonSchema Schema(string name) => JsonSchema.FromText(File.ReadAllText(Path.Combine(
        RepositoryRoot, "resources", "lexicons", "schemas", name)),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() });
}
