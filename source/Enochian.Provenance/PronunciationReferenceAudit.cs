using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.Provenance;

public sealed partial class PronunciationReferenceAudit(string repositoryRoot)
{
    private const string SelectionPolicy = "all-eligible-joins-then-band-unusual-ipa-stable-lemma-hash-v1";
    private static readonly string[] Languages = ["hin", "san", "tur", "hun"];
    private static readonly string[] NoEligibleBlockers = ["no_eligible_independent_reference_source"];
    private static readonly string[] GateNotMetBlockers = ["independent_reference_gate_not_met"];
    private static readonly JsonSerializerOptions Options = new(PronunciationAudit.JsonOptions)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public void Run(string planPath)
    {
        var plan = ReadPlan(planPath);
        VerifyPin(plan.ParentProtocol);
        var protocol = JsonNode.Parse(File.ReadAllText(Resolve(plan.ParentProtocol.Path)))!.AsObject();
        var replay = new GeneratorReplay(repositoryRoot);
        var profiles = plan.Profiles.Select(item => LoadProfile(item, protocol, replay)).ToArray();
        var candidates = plan.Candidates.Select(candidate => LoadCandidate(candidate, profiles)).ToArray();
        var package = new ReferenceAuditPackage("1.0.0", Pin(planPath), plan.ParentProtocol,
            [.. plan.Profiles.Select(profile => profile.Manifest)],
            [.. plan.Candidates.Where(candidate => candidate.Manifest != null).Select(candidate => candidate.Manifest!)],
            [.. plan.Candidates.Where(candidate => candidate.Records != null).Select(candidate => candidate.Records!)]);
        var packagePath = Resolve(plan.OutputDirectory + "/audit.json");
        WriteSealed(packagePath, package);
        var decisions = new List<object>();
        foreach (var profile in profiles)
        {
            var eligible = candidates.Where(candidate => candidate.Plan.Language == profile.Plan.Language &&
                candidate.Plan.Decision == "eligible").ToArray();
            GeneratorReferenceSet? set = null;
            PronunciationAgreement? agreement = null;
            RecoveryPin? referencePin = null;
            if (eligible.Length != 0)
            {
                set = BuildSet(profile.Profile, profile.Fingerprint, profile.Plan.Generator,
                    profile.GeneratorRevision, plan.SelectionSeed, profile.Evidence,
                    [.. eligible.SelectMany(candidate => candidate.Records)],
                    [.. eligible.Select(candidate => new IndependentReferenceSource(
                        PinConfiguration(Resolve(candidate.Plan.Manifest!.Path)), candidate.Plan.Dependencies))],
                    [.. eligible.Select(candidate => candidate.SourceId)]);
                var referencePath = Resolve(plan.OutputDirectory + "/references/" + profile.Plan.Language + ".json");
                WriteSealed(referencePath, set);
                referencePin = PinConfiguration(referencePath);
                agreement = Evaluate(set);
            }

            var source = protocol["sources"]!.AsArray().Single(node =>
                node!["language"]!.GetValue<string>() == profile.Plan.Language)!.AsObject();
            source["references"] = referencePin == null ? null : JsonSerializer.SerializeToNode(referencePin, Options);
            decisions.Add(new
            {
                profile.Plan.Language,
                profile.Plan.Generator,
                generator_revision = profile.GeneratorRevision,
                generated_readings = profile.Evidence.Count(row => row.Origin == "template-generated"),
                audited_candidates = candidates.Count(candidate => candidate.Plan.Language == profile.Plan.Language),
                eligible_sources = eligible.Length,
                joined_reference_lemmas = set?.Records.Length ?? 0,
                agreement,
                status = agreement?.Status ?? "exploratory",
                blockers = agreement?.Status == "pass" ? [] :
                    eligible.Length == 0 ? NoEligibleBlockers : GateNotMetBlockers,
            });
        }

        protocol["config_id"] = "dictionary-first-independent-reference-audit-v1";
        protocol["output_directory"] = ".enoch/experiments/independent-reference-audit-v1";
        protocol["admission_report"] = "reports/pronunciation-references/v1/admission.json";
        WriteSealed(Resolve(plan.AmendmentPath), protocol);
        WriteSealed(Resolve(plan.ReportPath), new
        {
            schema_version = "1.0.0",
            package = Pin(packagePath),
            selection_seed = plan.SelectionSeed,
            selection_policy = SelectionPolicy,
            candidates = candidates.Select(candidate => new
            {
                candidate.Plan.Id,
                candidate.Plan.Language,
                candidate.Plan.Url,
                candidate.Plan.Revision,
                candidate.Plan.License,
                candidate.Plan.LicenseStatus,
                candidate.Plan.UsagePolicy,
                candidate.Plan.EvidenceKind,
                candidate.Plan.MachineReadable,
                candidate.Plan.Immutable,
                candidate.Plan.Independence,
                candidate.Plan.Convention,
                candidate.Plan.Decision,
                candidate.Plan.Reasons,
            }),
            languages = decisions,
            pronunciation_admission = false,
            manuscript_scoring = false,
        });
        Console.WriteLine(FormattableString.Invariant(
            $"Audited {candidates.Length} reference candidates: {candidates.Count(candidate => candidate.Plan.Decision == "eligible")} eligible; no gate was relaxed."));
    }

    public static string[] AssessmentReasons(ReferenceAuditCandidate candidate, string requiredConvention)
    {
        var reasons = new List<string>();
        Add(candidate.EvidenceKind != "dictionary-ipa", "not_direct_dictionary_ipa");
        Add(!candidate.MachineReadable, "not_machine_readable");
        Add(!candidate.Immutable, "source_revision_not_immutable");
        Add(candidate.LicenseStatus != "verified", "license_unverified");
        Add(candidate.UsagePolicy != "unrestricted", "usage_not_unrestricted");
        Add(candidate.Independence != "established", candidate.Independence == "shared" ?
            "shared_generator_dependency" : "independence_not_established");
        Add(candidate.Convention != requiredConvention, "pronunciation_convention_mismatch");
        Add(candidate.Manifest == null, "missing_acquired_source_manifest");
        Add(candidate.Records == null, "missing_definitions_free_reference_records");
        Add(candidate.Dependencies.Length == 0 || candidate.Dependencies.Any(IsUnknown),
            "missing_or_unknown_reference_dependencies");
        return [.. reasons.Order(StringComparer.Ordinal)];

        void Add(bool condition, string reason)
        {
            if (condition)
            {
                reasons.Add(reason);
            }
        }
    }

    public static GeneratorReferenceSet BuildSet(
        PronunciationProfile profile, string fingerprint, string generator, string generatorRevision,
        int selectionSeed, PronunciationEvidence[] evidence, IndependentPronunciationRecord[] sourceRecords,
        IndependentReferenceSource[] sources, string[] sourceIds)
    {
        var generated = evidence.Where(row => row.Origin == "template-generated" &&
            row.Generator == generator && row.GeneratorRevision == generatorRevision).ToArray();
        Require(generated.Length != 0, "No generated evidence matches the pinned generator identity and revision.");
        var generatorRoots = generated.SelectMany(row => row.Dependencies).Concat(generated.Select(row => row.Source))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(!generatorRoots.Any(IsUnknown), "Generator dependencies are incomplete.");
        var referenceRoots = sources.SelectMany(source => source.Dependencies).Concat(sourceIds)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Require(referenceRoots.Length != 0 && !referenceRoots.Any(IsUnknown) &&
            !generatorRoots.Intersect(referenceRoots, StringComparer.Ordinal).Any(),
            "Reference dependencies are missing, unknown, or shared with the generator.");
        var units = generated.GroupBy(row => row.Source + "\u001f" + row.LexicalIdentity, StringComparer.Ordinal)
            .Select(group => new
            {
                Identity = group.Key,
                Lemma = group.First().Lemma.Normalize(NormalizationForm.FormC),
                Ipa = group.Select(row => row.OriginalIpa).Order(StringComparer.Ordinal).ToArray(),
            }).GroupBy(unit => unit.Lemma, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var records = new List<PronunciationAlternativeReference>();
        foreach (var group in sourceRecords.Where(record => record.Language == profile.Language &&
            record.Convention == profile.Convention).GroupBy(record => record.Lemma.Normalize(NormalizationForm.FormC),
                StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            if (!units.TryGetValue(group.Key, out var unit))
            {
                continue;
            }

            var distributions = group.Select(record => record.Ipa.Order(StringComparer.Ordinal).ToArray())
                .Distinct(SequenceComparer.Instance).ToArray();
            if (distributions.Length != 1)
            {
                continue;
            }

            records.Add(new()
            {
                LemmaId = profile.Id + ":" + PronunciationPolicyEvaluator.StableId(unit.Identity),
                SourceRecordIds = [.. group.Select(record => record.SourceRecordId).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)],
                GeneratedIpa = unit.Ipa,
                ReferenceIpa = distributions[0],
                ReferenceOrigin = group.All(record => record.ReferenceOrigin == "dictionary-supplied")
                    ? "dictionary-supplied" : "unknown",
                GeneratorDependencies = generatorRoots,
                ReferenceDependencies = [.. referenceRoots.Concat(group.SelectMany(record => record.Dependencies))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                HeldOut = group.All(record => !record.TuningExample),
                TuningExample = group.Any(record => record.TuningExample),
                IndependenceEstablished = group.All(record => record.IndependenceEstablished),
            });
        }

        return new()
        {
            SchemaVersion = "2.2.0",
            ProfileSha256 = fingerprint,
            Generator = generator,
            GeneratorRevision = generatorRevision,
            Convention = profile.Convention,
            SelectionSeed = selectionSeed,
            SelectionPolicy = SelectionPolicy,
            ReferenceSources = sources,
            Records = [.. records.OrderBy(record => record.LemmaId, StringComparer.Ordinal)],
        };
    }

    private PronunciationAgreement Evaluate(GeneratorReferenceSet set)
    {
        var flow = new Enochian.Flow.Flow(Resolve("samples/ipatransducer.json"));
        Require(!flow.Errors.Any(), "IPA flow configuration is invalid.");
        var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var encoding = flow.Encodings.Single(candidate =>
            string.Equals(candidate.Id, "IPA", StringComparison.OrdinalIgnoreCase));
        return PronunciationPolicyEvaluator.EvaluateReferences(set.Records, new PronunciationPolicy(),
            new Enochian.Text.Encoder(features, encoding), set.SelectionSeed);
    }

    private ProfileInput LoadProfile(ReferenceAuditProfile item, JsonObject protocol, GeneratorReplay replay)
    {
        VerifyPin(item.Manifest);
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve(item.Manifest.Path)));
        var envelope = document.RootElement.Clone();
        Require(envelope.GetProperty("schema_version").GetString() == "1.2.0", "Reference audit requires replay v1.2 profiles.");
        var profile = JsonSerializer.Deserialize<PronunciationProfile>(envelope.GetProperty("profile"), Options)
            ?? throw new InvalidDataException("Empty replay profile.");
        Require(profile.Language == item.Language, "Reference audit profile language mismatch.");
        var protocolSource = protocol["sources"]!.AsArray().Single(node =>
            node!["language"]!.GetValue<string>() == item.Language)!["profile_manifest"]!.AsObject();
        Require(protocolSource["path"]!.GetValue<string>() == item.Manifest.Path &&
            protocolSource["sha256"]!.GetValue<string>() == PronunciationAudit.HashConfiguration(Resolve(item.Manifest.Path)),
            "Reference audit profile is not the frozen replay amendment profile.");
        _ = replay.VerifyProfile(envelope);
        var evidencePin = envelope.GetProperty("artifacts").EnumerateArray().Select(Deserialize<RecoveryPin>)
            .Single(pin => pin.Path.EndsWith(".evidence.jsonl", StringComparison.Ordinal));
        VerifyPin(evidencePin);
        var evidence = ReadRows<PronunciationEvidence>(Resolve(evidencePin.Path));
        var revisions = evidence.Where(row => row.Origin == "template-generated")
            .Select(row => row.GeneratorRevision).Distinct(StringComparer.Ordinal).ToArray();
        Require(revisions.Length == 1 && revisions[0] != null &&
            evidence.Where(row => row.Origin == "template-generated").All(row => row.Generator == item.Generator),
            "Replay profile has mixed or unresolved generator identity.");
        return new(item, profile, envelope.GetProperty("profile_sha256").GetString()!, revisions[0]!, evidence);
    }

    private CandidateInput LoadCandidate(ReferenceAuditCandidate candidate, ProfileInput[] profiles)
    {
        var profile = profiles.Single(item => item.Plan.Language == candidate.Language);
        var reasons = AssessmentReasons(candidate, profile.Profile.Convention);
        Require(candidate.Reasons.Order(StringComparer.Ordinal).SequenceEqual(reasons, StringComparer.Ordinal) &&
            candidate.Decision == (reasons.Length == 0 ? "eligible" : "excluded"),
            "Reference candidate decision does not follow the frozen eligibility gates: " + candidate.Id);
        var sourceId = candidate.Id;
        if (candidate.Manifest != null)
        {
            VerifyPin(candidate.Manifest);
            var path = Resolve(candidate.Manifest.Path);
            var validation = new ManifestValidator(repositoryRoot,
                Resolve("resources/lexicons/schemas/source-manifest.schema.json")).Validate([path]);
            Require(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));
            using var manifest = JsonDocument.Parse(File.ReadAllText(path));
            var root = manifest.RootElement;
            sourceId = root.GetProperty("source_id").GetString()!;
            Require(root.GetProperty("language").GetString() == candidate.Language &&
                root.GetProperty("url").GetString() == candidate.Url &&
                root.GetProperty("revision").GetProperty("value").GetString() == candidate.Revision &&
                root.GetProperty("license").GetString() == candidate.License &&
                root.GetProperty("license_status").GetString() == candidate.LicenseStatus &&
                root.GetProperty("usage_policy").GetString() == candidate.UsagePolicy &&
                candidate.Dependencies.Contains(sourceId, StringComparer.Ordinal),
                "Reference candidate metadata differs from its pinned source manifest: " + candidate.Id);
        }

        IndependentPronunciationRecord[] records = [];
        if (candidate.Records != null)
        {
            VerifyPin(candidate.Records);
            var schema = LoadSchema("independent-pronunciation-record.schema.json");
            records = ReadRows<IndependentPronunciationRecord>(Resolve(candidate.Records.Path));
            Require(records.All(record => schema.Evaluate(JsonSerializer.SerializeToElement(record, Options)).IsValid),
                "Invalid independent pronunciation record: " + candidate.Id);
        }

        return new(candidate, sourceId, records);
    }

    private PronunciationReferenceAuditPlan ReadPlan(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        Require(LoadSchema("pronunciation-reference-audit.schema.json").Evaluate(document.RootElement).IsValid,
            "Invalid pronunciation reference audit plan.");
        var plan = JsonSerializer.Deserialize<PronunciationReferenceAuditPlan>(document.RootElement, Options)
            ?? throw new InvalidDataException("Empty pronunciation reference audit plan.");
        Require(plan.Profiles.Select(profile => profile.Language).Order(StringComparer.Ordinal)
            .SequenceEqual(Languages.Order(StringComparer.Ordinal)) &&
            plan.Candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() == plan.Candidates.Length &&
            Languages.All(language => plan.Candidates.Any(candidate => candidate.Language == language)),
            "Reference audit must cover each required language exactly once and use unique candidates.");
        return plan;
    }

    private JsonSchema LoadSchema(string name) => JsonSchema.FromText(File.ReadAllText(
        Resolve("resources/lexicons/schemas/" + name)), new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    private string Resolve(string path)
    {
        Require(!Path.IsPathRooted(path), "Reference audit paths must be repository-relative.");
        var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        Require(resolved.StartsWith(Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase), "Reference audit path escapes the repository.");
        return resolved;
    }

    private RecoveryPin Pin(string path) => new(
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
        PronunciationAudit.Hash(path));

    private RecoveryPin PinConfiguration(string path) => new(
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
        PronunciationAudit.HashConfiguration(path));

    private void VerifyPin(RecoveryPin pin) =>
        Require(PronunciationAudit.Hash(Resolve(pin.Path)) == pin.Sha256,
            "Reference audit artifact hash mismatch: " + pin.Path);

    private static T Deserialize<T>(JsonElement element) =>
        JsonSerializer.Deserialize<T>(element, Options) ?? throw new InvalidDataException("Empty reference audit value.");

    private static T[] ReadRows<T>(string path) =>
        [.. File.ReadLines(path, new UTF8Encoding(false, true)).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, Options)
                ?? throw new InvalidDataException("Empty reference audit row: " + path))];

    private static void WriteSealed<T>(string path, T value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options).ReplaceLineEndings("\n") + "\n");
        if (File.Exists(path))
        {
            Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes),
                "Refusing to replace sealed reference audit artifact: " + path);
            return;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static bool IsUnknown(string value) =>
        value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("unresolved", StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed record ProfileInput(
        ReferenceAuditProfile Plan, PronunciationProfile Profile, string Fingerprint,
        string GeneratorRevision, PronunciationEvidence[] Evidence);

    private sealed record CandidateInput(
        ReferenceAuditCandidate Plan, string SourceId, IndependentPronunciationRecord[] Records);

    private sealed class SequenceComparer : IEqualityComparer<string[]>
    {
        public static SequenceComparer Instance { get; } = new();

        public bool Equals(string[]? left, string[]? right) =>
            ReferenceEquals(left, right) || (left != null && right != null &&
            left.SequenceEqual(right, StringComparer.Ordinal));

        public int GetHashCode(string[] value)
        {
            var hash = new HashCode();
            foreach (var item in value)
            {
                hash.Add(item, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }
    }
}
