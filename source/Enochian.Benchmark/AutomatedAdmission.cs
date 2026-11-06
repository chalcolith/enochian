using Enochian.Provenance;
using Enochian.Text;
using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public sealed class AutomatedAdmission(string repositoryRoot, FeatureSet features, Text.Encoding encoding)
{
    private readonly AutomatedFiles files = new(repositoryRoot);
    private readonly IpaEncoder encoder = new(features, encoding);
    private readonly Dictionary<string, SourceMetadata> sourceMetadata = new(StringComparer.Ordinal);

    public AutomatedAdmissionResult Evaluate(string protocolPath)
    {
        var protocol = AutomatedProtocol.Load(protocolPath, repositoryRoot);
        files.Track(protocolPath);
        foreach (var pin in protocol.Sources.SelectMany(source => new[] { source.ProfileManifest, source.References })
            .Concat([protocol.Queries, protocol.Mapping, protocol.Astronomy.Registration, protocol.Astronomy.Policy, protocol.Astronomy.Manifest])
            .OfType<PinnedInput>())
        {
            files.Protect(files.Resolve(pin.Path));
        }

        _ = files.Pin(protocol.Phonology.Flow);
        var featurePath = files.Pin(protocol.Phonology.Features);
        var encodingPath = files.Pin(protocol.Phonology.Encoding);
        if (Path.GetFullPath(features.AbsoluteFilePath) != featurePath ||
            Path.GetFullPath(encoding.AbsoluteFilePath) != encodingPath)
        {
            throw new InvalidDataException("Runtime phonology does not match the pinned v2 feature and encoding inputs.");
        }

        var panelPath = files.Pin(protocol.Panel);
        var panel = PronunciationPanel.Load(panelPath, repositoryRoot);
        var dictionaryPath = files.Pin(new PinnedInput(panel.DictionaryProtocol, panel.DictionaryProtocolSha256));
        var dictionary = JsonSerializer.Deserialize<PronunciationPlan>(File.ReadAllText(dictionaryPath), PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Missing frozen dictionary evidence policy.");
        Require(dictionary.Frozen && dictionary.Policy.MinimumLemmas == 100 &&
            dictionary.Policy.MinimumReferences == 100 && dictionary.Policy.MinimumPerBand == 25 &&
            dictionary.Policy.MinimumAgreement == 0.95 && dictionary.Policy.MinimumPhonemes == 3 &&
            dictionary.Policy.MaximumPhonemes == 20 &&
            dictionary.Policy.RequiredBands.Order(StringComparer.Ordinal).SequenceEqual(protocol.LengthQuotas.Keys) &&
            dictionary.Policy.AdmittedOrigins.SequenceEqual(["dictionary-supplied"]), "modified_evidence_policy");
        var retrieval = BenchmarkProtocol.Load(files.Pin(protocol.RetrievalPolicy));
        if (retrieval.RequiredLengthBands.Count != 3 ||
            !retrieval.RequiredLengthBands.ToHashSet(StringComparer.Ordinal).SetEquals(protocol.LengthQuotas.Keys) ||
            retrieval.Thresholds != new BenchmarkThresholds(100, 0.95, 0.8, 0.95, 0.99, 0.85, 0.35) ||
            retrieval.SamplesPerStratum != 25 || protocol.Degradations.Length != retrieval.DegradationProfiles.Count)
        {
            throw new InvalidDataException("V2 must retain the frozen retrieval thresholds, bands and degradation inventory.");
        }

        var profiles = panel.Profiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        if (!profiles.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(protocol.Sources.Select(source => source.ProfileId)))
        {
            throw new InvalidDataException("Every frozen pronunciation profile needs an explicit eligibility decision.");
        }

        var degradations = protocol.Degradations.Select(pin =>
        {
            var path = files.Pin(pin);
            BenchmarkProtocol.Validate(File.ReadAllText(path), path);
            return JsonSerializer.Deserialize<DegradationProfile>(File.ReadAllText(path), BenchmarkProtocol.SerializerOptions)
                ?? throw new InvalidDataException("Missing degradation profile.");
        }).OrderBy(profile => profile.Id, StringComparer.Ordinal).ToArray();
        var retrievalRoot = Path.GetDirectoryName(files.Resolve(protocol.RetrievalPolicy.Path))!;
        var expectedDegradations = retrieval.DegradationProfiles
            .Select(path => Path.GetFullPath(Path.Combine(retrievalRoot, path.Replace('/', Path.DirectorySeparatorChar))))
            .Select(PronunciationAudit.HashConfiguration).ToHashSet(StringComparer.Ordinal);
        if (!expectedDegradations.SetEquals(protocol.Degradations.Select(pin => pin.Sha256)) ||
            degradations.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != degradations.Length)
        {
            throw new InvalidDataException("Missing, duplicated or substituted frozen degradation profiles.");
        }

        TrackRuntime();
        var decisions = new List<AutomatedSourceDecision>();
        var admitted = new SortedDictionary<string, EvidenceUnit[]>(StringComparer.Ordinal);
        foreach (var source in protocol.Sources.OrderBy(source => source.ProfileId, StringComparer.Ordinal))
        {
            try
            {
                var (decision, units) = EvaluateSource(source, profiles[source.ProfileId], dictionary.Policy,
                    protocol, retrieval, degradations);
                decisions.Add(decision);
                if (decision.Status == "admitted")
                {
                    admitted.Add(source.Language, units);
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or KeyNotFoundException or DecoderFallbackException)
            {
                Console.Error.WriteLine($"{source.ProfileId}: automated admission rejected input: {exception.Message}");
                decisions.Add(new(source.ProfileId, source.Language, source.Family, source.Required, "excluded",
                    0, 0, 0, new SortedDictionary<string, int>(StringComparer.Ordinal), "failed", null,
                    "not_run_invalid_provenance", [], ["invalid_or_missing_provenance",
                        exception is InvalidDataException ? exception.Message : exception.GetType().Name]));
            }
        }

        var blockers = decisions.Where(decision => decision.Required && decision.Status != "admitted")
            .Select(decision => decision.ProfileId + ":not_admitted").ToList();
        foreach (var contrast in protocol.Contrasts.Where(contrast => contrast.Required))
        {
            if (!AutomatedProtocol.Languages(contrast.TargetGroups, protocol.Groups).Any(admitted.ContainsKey) ||
                !AutomatedProtocol.Languages(contrast.ControlGroups, protocol.Groups).Any(admitted.ContainsKey))
            {
                blockers.Add(contrast.Id + ":no_admitted_comparison");
            }
        }

        var report = new AutomatedAdmissionReport("2.0.0", AutomatedExperiment.RunnerVersion,
            PronunciationAudit.Hash(typeof(AutomatedAdmission).Assembly.Location), protocol.ConfigId, PronunciationAudit.HashConfiguration(protocolPath),
            blockers.Count == 0, false, [.. decisions], [.. blockers.Order(StringComparer.Ordinal)]);
        var reportPath = files.Resolve(protocol.AdmissionReport);
        if (!reportPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            files.ProtectedPaths.Contains(reportPath, StringComparer.OrdinalIgnoreCase) ||
            files.ProtectedPaths.Contains(Path.ChangeExtension(reportPath, ".md"), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Admission reports must not overwrite pinned inputs.");
        }

        AutomatedFiles.Write(reportPath, report);
        WriteCoverage(reportPath, report);
        files.Protect(reportPath);
        files.Protect(Path.ChangeExtension(reportPath, ".md"));
        return new(protocol, report, admitted, files.Inputs, files.ProtectedPaths);
    }

    private (AutomatedSourceDecision Decision, EvidenceUnit[] Units) EvaluateSource(
        AutomatedSource source,
        PronunciationProfile profile,
        PronunciationPolicy policy,
        AutomatedProtocol protocol,
        BenchmarkProtocol retrieval,
        DegradationProfile[] degradations)
    {
        Require(source.Language == profile.Language, "profile_language_mismatch");
        using var manifest = files.Read(source.ProfileManifest);
        var root = manifest.RootElement;
        var recovered = root.GetProperty("schema_version").GetString() == "1.1.0";
        Require(LoadSchema(recovered ? "profile-artifact-manifest.v1.1.schema.json" : "profile-artifact-manifest.schema.json").Evaluate(root).IsValid,
            "invalid_profile_artifact_manifest");
        if (recovered)
        {
            foreach (var pin in new PronunciationRecovery(repositoryRoot).VerifyProfile(root))
            {
                files.Track(files.Resolve(pin.Path));
            }

            files.TrackSoftware(typeof(PronunciationRecovery).Assembly.Location);
            files.TrackSoftware(typeof(HtmlAgilityPack.HtmlDocument).Assembly.Location);
        }
        var recordedProfile = JsonSerializer.Deserialize<PronunciationProfile>(root.GetProperty("profile"), PronunciationAudit.JsonOptions);
        Require(recordedProfile?.Fingerprint == profile.Fingerprint &&
            root.GetProperty("profile_sha256").GetString() == profile.Fingerprint &&
            root.GetProperty("panel_sha256").GetString() == protocol.Panel.Sha256, "profile_or_panel_hash_mismatch");
        Require(root.GetProperty("feature_sha256").GetString() == PronunciationAudit.Hash(files.Resolve(protocol.Phonology.Features.Path)) &&
            root.GetProperty("encoding_sha256").GetString() == PronunciationAudit.Hash(files.Resolve(protocol.Phonology.Encoding.Path)),
            "phonological_space_hash_mismatch");
        var manifests = root.GetProperty("source_manifests").EnumerateArray().Select(value =>
            new PinnedInput(value.GetProperty("path").GetString()!, value.GetProperty("sha256").GetString()!)).ToArray();
        var upstream = new Dictionary<string, SourceMetadata>(StringComparer.Ordinal);
        foreach (var pin in manifests)
        {
            // M2-08 records exact source-manifest bytes, unlike canonical v2 configuration pins.
            var path = files.Pin(pin, configuration: false);
            var metadata = ValidateSourceManifest(path);
            Require(upstream.TryAdd(metadata.Id, metadata), "duplicate_upstream_source");
        }

        var artifacts = root.GetProperty("artifacts").EnumerateArray()
            .Select(value => new PinnedInput(value.GetProperty("path").GetString()!, value.GetProperty("sha256").GetString()!))
            .ToArray();
        Require(artifacts.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == artifacts.Length &&
            artifacts.Count(pin => Path.GetFileName(pin.Path) == profile.ArtifactStem + ".jsonl") == 1 &&
            artifacts.Count(pin => Path.GetFileName(pin.Path) == profile.ArtifactStem + ".evidence.jsonl") == 1,
            "missing_or_duplicate_profile_artifacts");
        foreach (var pin in artifacts)
        {
            _ = files.Pin(pin, configuration: false);
        }

        var prefix = profile.ArtifactStem;
        var lexiconPath = files.Resolve(artifacts.Single(pin => Path.GetFileName(pin.Path) == prefix + ".jsonl").Path);
        var evidencePath = files.Resolve(artifacts.Single(pin => Path.GetFileName(pin.Path) == prefix + ".evidence.jsonl").Path);
        var entrySchema = LoadSchema("normalized-entry.schema.json");
        var evidenceSchema = LoadSchema("pronunciation-evidence.schema.json");
        var entries = new Dictionary<string, PronunciationNormalizedEntry>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(lexiconPath, new UTF8Encoding(false, true)))
        {
            using var document = JsonDocument.Parse(line);
            Require(entrySchema.Evaluate(document.RootElement).IsValid, "invalid_normalized_entry");
            var entry = JsonSerializer.Deserialize<PronunciationNormalizedEntry>(document.RootElement, PronunciationAudit.JsonOptions)!;
            Require(entry.Language == source.Language && entry.Family == source.Family && entry.EntryKind == "lemma" &&
                entry.SourceEncoding == "IPA" && entry.UnicodeNormalization == "NFC" && entry.Ipa.IsNormalized(NormalizationForm.FormC) &&
                entry.Dialect == profile.ArtifactStem && entry.PartOfSpeech is not ("name" or "abbrev") &&
                entries.TryAdd(entry.EntryId, entry), "incompatible_or_duplicate_entry");
        }

        var rows = new Dictionary<string, PronunciationEvidence>(StringComparer.Ordinal);
        foreach (var line in entries.Count == 0 ? [] : File.ReadLines(evidencePath, new UTF8Encoding(false, true)))
        {
            using var document = JsonDocument.Parse(line);
            Require(document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("entry_id", out var entryId) && entryId.ValueKind == JsonValueKind.String,
                "invalid_evidence_identity");
            var id = document.RootElement.GetProperty("entry_id").GetString()!;
            if (!entries.TryGetValue(id, out var entry))
            {
                continue;
            }

            Require(evidenceSchema.Evaluate(document.RootElement).IsValid, "invalid_evidence");
            var evidence = JsonSerializer.Deserialize<PronunciationEvidence>(document.RootElement, PronunciationAudit.JsonOptions)!;
            Require(rows.TryAdd(id, evidence), "duplicate_evidence_id");
            ValidateReading(entry, evidence, profile, upstream);
        }

        Require(entries.Count == rows.Count, "missing_entry_evidence");
        var grouped = rows.Values.GroupBy(row => row.Source + "\u001f" + row.LexicalIdentity, StringComparer.Ordinal).ToArray();
        var compatible = grouped.Select(group => CreateUnit(profile, [.. group])).ToArray();
        var agreement = source.References == null ? null : EvaluateReferences(source.References, profile, rows.Values, policy, upstream);
        var supportedIds = grouped.Where(group => group.All(row => row.Origin == "dictionary-supplied" ||
            (row.Origin is "converter-generated" or "template-generated" && agreement?.Status == "pass" &&
                !string.IsNullOrWhiteSpace(row.Generator) && !string.IsNullOrWhiteSpace(row.GeneratorRevision))))
            .Select(group => UnitId(profile, group.Key)).ToHashSet(StringComparer.Ordinal);
        var supported = compatible.Where(unit => supportedIds.Contains(unit.Id)).ToArray();
        var units = supported.GroupBy(DistributionKey, StringComparer.Ordinal)
            .Select(group => group.OrderBy(unit => unit.Id, StringComparer.Ordinal).First() with
            {
                Memberships = [.. group.SelectMany(unit => unit.Memberships).Distinct()
                    .OrderBy(value => value.EntryId, StringComparer.Ordinal)],
            }).OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
        var blockers = new List<string>();
        if (!source.ConfirmatoryEligible)
        {
            blockers.Add("profile_is_exploratory_only");
        }

        if (supported.Length < policy.MinimumLemmas)
        {
            blockers.Add("insufficient_supported_pronunciation_evidence");
        }

        foreach (var band in protocol.LengthQuotas)
        {
            if (units.Count(unit => unit.LengthBand == band.Key) < band.Value)
            {
                blockers.Add("insufficient_balanced_units:" + band.Key);
            }
        }

        var pronunciationPassed = supported.Length >= policy.MinimumLemmas;
        var cells = pronunciationPassed && blockers.Count == 0
            ? AutomatedRetrieval.Evaluate(units, protocol, retrieval, degradations, features) : [];
        if (cells.Any(cell => cell.CandidateMode == "source-included" && !cell.Passed))
        {
            blockers.Add("retrieval_threshold_failed");
        }

        var retrieved = cells.Length != 0 && cells.Where(cell => cell.CandidateMode == "source-included").All(cell => cell.Passed);
        var status = blockers.Count == 0 && retrieved ? "admitted" : compatible.Length == 0 ? "excluded" : "exploratory";
        return (new(source.ProfileId, source.Language, source.Family, source.Required, status,
            compatible.Length, supported.Length, units.Length,
            PronunciationPolicyEvaluator.Counts(compatible.Select(unit => unit.LengthBand)),
            pronunciationPassed ? "passed" : "failed", agreement,
            cells.Length == 0 ? "not_run_pronunciation_or_coverage_gate_failed" : retrieved ? "passed" : "failed",
            cells, [.. blockers.Order(StringComparer.Ordinal)]), units);
    }

    private void ValidateReading(
        PronunciationNormalizedEntry entry, PronunciationEvidence row,
        PronunciationProfile profile, IReadOnlyDictionary<string, SourceMetadata> upstream)
    {
        Require(row.Source == entry.Source && row.Snapshot == entry.SourceVersion && row.Language == entry.Language &&
            row.Lemma == entry.Lemma && row.PartOfSpeech == entry.PartOfSpeech &&
            row.Convention == profile.Convention && row.Exclusions.Length == 0 && row.UnknownSegments.Length == 0 &&
            row.NormalizedIpa == entry.Ipa &&
            row.NormalizationPolicy == "ipa-nfc-delimiters-stress-syllables-v1" &&
            row.AlternativePolicy == "one-lexical-unit-equal-reading-weights-v1" &&
            entry.SourceRecordId == row.EntryId[(row.EntryId.LastIndexOf(':') + 1)..] &&
            PronunciationPolicyEvaluator.NormalizeIpa(row.OriginalIpa) == entry.Ipa, "incompatible_evidence_or_convention");
        Require((profile.RequiredLabels.Length == 0 ||
            row.Labels.Intersect(profile.RequiredLabels, StringComparer.OrdinalIgnoreCase).Any()) &&
            !row.Labels.Intersect(profile.RejectedLabels, StringComparer.OrdinalIgnoreCase).Any(), "wrong_pronunciation_label");
        Require(!string.IsNullOrWhiteSpace(row.LexicalIdentity) && row.Dependencies.Length != 0 &&
            Uri.TryCreate(row.SourceEntry, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
            upstream.TryGetValue(row.Source, out var metadata) && metadata.Revision == row.Snapshot &&
            metadata.License == entry.License, "missing_source_provenance");
        var (_, _, phones) = encoder.GetTextAndPhones(entry.Ipa.Normalize(NormalizationForm.FormD), out var unknown);
        Require(unknown.Count == 0 && phones.Count >= 3 && phones.Count <= 20 &&
            row.PhonemeLength == phones.Count && row.LengthBand == BenchmarkSampling.GetLengthBand(phones.Count),
            "unsupported_or_inconsistent_phonology");
    }

    private EvidenceUnit CreateUnit(PronunciationProfile profile, PronunciationEvidence[] rows)
    {
        Require(rows.All(row => row.AlternativeCount == rows.Length &&
            row.Lemma == rows[0].Lemma && row.PartOfSpeech == rows[0].PartOfSpeech &&
            double.IsFinite(row.AlternativeWeight) &&
            System.Math.Abs(row.AlternativeWeight - (1.0 / rows.Length)) < 1e-12),
            "invalid_alternative_weights");
        var variants = rows.GroupBy(row => row.NormalizedIpa, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
            {
                var row = group.OrderBy(value => value.EntryId, StringComparer.Ordinal).First();
                var (_, _, phones) = encoder.GetTextAndPhones(row.NormalizedIpa.Normalize(NormalizationForm.FormD));
                return new EvidenceReading(row.EntryId, row.NormalizedIpa, group.Sum(value => value.AlternativeWeight), [.. phones]);
            }).ToArray();
        var first = rows[0];
        return new(UnitId(profile, first.Source + "\u001f" + first.LexicalIdentity), first.Language, first.Lemma,
            BenchmarkSampling.GetLengthBand(variants[0].Phones.Count), variants,
            [.. rows.OrderBy(row => row.EntryId, StringComparer.Ordinal)
                .Select(row => new SamplingSourceMembership(row.EntryId, row.Source, row.LexicalIdentity))]);
    }

    private PronunciationAgreement EvaluateReferences(
        PinnedInput pin, PronunciationProfile profile, IEnumerable<PronunciationEvidence> evidence,
        PronunciationPolicy policy, IReadOnlyDictionary<string, SourceMetadata> generators)
    {
        using var document = files.Read(pin);
        Require(LoadSchema("automated-reference-set.v2.schema.json").Evaluate(document.RootElement).IsValid,
            "invalid_reference_set");
        var set = JsonSerializer.Deserialize<AutomatedReferenceSet>(document.RootElement, AutomatedProtocol.JsonOptions)!;
        Require(set.ProfileSha256 == profile.Fingerprint, "reference_profile_hash_mismatch");
        var rows = evidence.ToArray();
        var generatorRoots = rows.SelectMany(row => row.Dependencies).Concat(rows.Select(row => row.Source))
            .ToHashSet(StringComparer.Ordinal);
        Require(!generatorRoots.Any(IsUnknown) && rows.Where(row => row.Origin is "converter-generated" or "template-generated")
            .All(row => row.GeneratorRevision == set.GeneratorRevision && !string.IsNullOrWhiteSpace(row.Generator)) &&
            rows.Where(row => row.Origin is "converter-generated" or "template-generated")
                .Select(row => row.Generator).Distinct(StringComparer.Ordinal).Count() == 1 &&
            !IsUnknown(set.GeneratorRevision), "unknown_generator_dependencies_or_revision");
        var referenceRoots = new HashSet<string>(StringComparer.Ordinal);
        Require(set.ReferenceSources.Length != 0, "missing_reference_sources");
        foreach (var source in set.ReferenceSources)
        {
            var reference = ValidateSourceManifest(files.Pin(source.Manifest));
            Require(source.Dependencies.Length != 0 && !source.Dependencies.Any(IsUnknown) &&
                !generators.Values.Any(generator => generator.Id == reference.Id || generator.Hash == reference.Hash ||
                    generator.Url == reference.Url), "shared_or_unknown_reference_source");
            referenceRoots.UnionWith(source.Dependencies);
            _ = referenceRoots.Add(reference.Id);
        }

        Require(!generatorRoots.Overlaps(referenceRoots), "shared_reference_dependencies");
        var byId = rows.GroupBy(row => UnitId(profile, row.Source + "\u001f" + row.LexicalIdentity), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        Require(set.Records.All(row => byId.TryGetValue(row.LemmaId, out var candidates) &&
            candidates.Any(candidate => candidate.NormalizedIpa == PronunciationPolicyEvaluator.NormalizeIpa(row.GeneratedIpa)) &&
            generatorRoots.IsSubsetOf(row.GeneratorDependencies) && referenceRoots.IsSubsetOf(row.ReferenceDependencies)),
            "unbound_or_incomplete_reference_evidence");
        return PronunciationPolicyEvaluator.EvaluateReferences(set.Records, policy, encoder);
    }

    private SourceMetadata ValidateSourceManifest(string path)
    {
        if (sourceMetadata.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var validation = new ManifestValidator(repositoryRoot, files.Resolve("resources/lexicons/schemas/source-manifest.schema.json"))
            .Validate([path]);
        Require(validation.IsValid, "invalid_source_manifest");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.TryGetProperty("raw_path", out var raw) && raw.ValueKind == JsonValueKind.String)
        {
            files.Protect(files.Resolve(raw.GetString()!));
        }

        Require(root.GetProperty("status").GetString() == "acquired" &&
            root.GetProperty("license_status").GetString() == "verified" &&
            root.GetProperty("usage_policy").GetString() == "unrestricted" &&
            root.GetProperty("sha256").ValueKind == JsonValueKind.String, "source_license_or_pin_not_eligible");
        var metadata = new SourceMetadata(root.GetProperty("source_id").GetString()!, root.GetProperty("revision").GetProperty("value").GetString()!,
            root.GetProperty("license").GetString()!, root.GetProperty("sha256").GetString()!, root.GetProperty("url").GetString()!);
        sourceMetadata.Add(path, metadata);
        return metadata;
    }

    private void TrackRuntime()
    {
        foreach (var path in new[]
        {
            "resources/encodings/features.json", "resources/encodings/ipa.json",
            "resources/lexicons/schemas/source-manifest.schema.json",
            "resources/lexicons/schemas/normalized-entry.schema.json",
            "resources/lexicons/schemas/pronunciation-evidence.schema.json",
            "resources/lexicons/schemas/automated-reference-set.v2.schema.json",
            "resources/lexicons/schemas/pronunciation-panel.schema.json",
            "resources/lexicons/schemas/profile-artifact-manifest.schema.json",
            "resources/lexicons/schemas/profile-artifact-manifest.v1.1.schema.json",
            "resources/lexicons/schemas/pronunciation-recovery.schema.json",
            "experiments/schemas/automated-experiment.v2.schema.json",
            "experiments/schemas/automated-admission.v2.schema.json",
        })
        {
            files.Track(files.Resolve(path));
        }
    }

    private JsonSchema LoadSchema(string name) => JsonSchema.FromText(File.ReadAllText(files.Resolve(
        "resources/lexicons/schemas/" + name)), new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    private static string UnitId(PronunciationProfile profile, string identity) =>
        profile.Id + ":" + PronunciationPolicyEvaluator.StableId(identity);

    private static string DistributionKey(EvidenceUnit unit) => string.Join('\u001f',
        unit.Readings.GroupBy(reading => string.Join(';', reading.Phones.Select(phone =>
                string.Join(',', phone.Select(value => value.ToString("R", CultureInfo.InvariantCulture))))), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key + ":" + group.Sum(reading => reading.Weight).ToString("R", CultureInfo.InvariantCulture)));

    private static void WriteCoverage(string reportPath, AutomatedAdmissionReport report)
    {
        var lines = new List<string>
        {
            "# Automated dictionary-first admission v2",
            string.Empty,
            "Protocol: `" + report.ConfigId + "`; canonical SHA-256: `" + report.ProtocolSha256 + "`.",
            "Validator: `" + report.RunnerVersion + "`; binary SHA-256: `" + report.ValidatorSha256 + "`.",
            string.Empty,
            "Required source gates: **" + (report.ReadyForEvaluation ? "ready" : "blocked") + "**. No human annotation is required.",
            "This admission step does not run manuscript comparative scoring. Execution additionally requires query/mapping preflight.",
            string.Empty,
            "Compatible counts and bands precede full pronunciation-distribution deduplication. Supported counts require",
            "dictionary-supplied readings or independently validated generation. Unique counts include only supported distributions.",
            string.Empty,
            "| Language | Required | Compatible | 03-05 | 06-09 | 10-20 | Supported | Unique supported | Status | Blockers |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |",
        };
        foreach (var source in report.Sources)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {source.Language} | {source.Required.ToString().ToLowerInvariant()} | {source.CompatibleLexicalUnits} | {source.LengthBands.GetValueOrDefault("03-05")} | {source.LengthBands.GetValueOrDefault("06-09")} | {source.LengthBands.GetValueOrDefault("10-20")} | {source.SupportedLexicalUnits} | {source.UniquePhonologicalUnits} | {source.Status} | {string.Join("; ", source.Blockers)} |"));
        }

        lines.AddRange([string.Empty,
            "Pronunciation and retrieval are separate gates; self-retrieval is not pronunciation validation.",
            "Unspecified-dialect Romani is exploratory-only. Hebrew is descriptive-only. Arabic has a separate registered contrast.",
            "Missing Arabic/Greek/Hebrew evidence limits historical-language and star-name claims; same-family languages are not independent replications.",
            "All required sources must pass before M4-01; excluded languages cannot be described as having passed or having a null effect.",
            string.Empty]);
        File.WriteAllText(Path.ChangeExtension(reportPath, ".md"), string.Join('\n', lines), new UTF8Encoding(false));
    }

    private static bool IsUnknown(string dependency) => dependency.Contains("unknown", StringComparison.OrdinalIgnoreCase);

    private static void Require(bool condition, string code)
    {
        if (!condition)
        {
            throw new InvalidDataException(code);
        }
    }

    private sealed record SourceMetadata(string Id, string Revision, string License, string Hash, string Url);
}
