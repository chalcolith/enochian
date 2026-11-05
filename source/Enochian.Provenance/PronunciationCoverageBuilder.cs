using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Provenance;

public sealed class PronunciationCoverageBuilder(
    PronunciationLanguage language,
    PronunciationPolicy policy,
    IpaEncoder encoder)
{
    private readonly HashSet<string> eligible = new(StringComparer.Ordinal);
    private readonly List<PronunciationReading> readings = [];
    private readonly SortedDictionary<string, int> sourceExclusions = new(StringComparer.Ordinal);
    private int sourceRecords;

    public void AddWiktextract(JsonElement entry, string source, string snapshot, string license)
    {
        AddLexeme(PronunciationDictionaryAdapter.ReadLexeme(entry, source, snapshot, language.Language, license));
    }

    public void AddLexeme(PronunciationLexeme lexeme)
    {
        sourceRecords++;
        if (lexeme.Exclusion != null)
        {
            PronunciationPolicyEvaluator.Increment(sourceExclusions, "source_" + lexeme.Exclusion);
            readings.AddRange(lexeme.Readings);
            return;
        }

        _ = eligible.Add(lexeme.Source + "\u001f" + lexeme.Identity);
        readings.AddRange(lexeme.Readings);
    }

    public void AddReading(PronunciationReading reading)
    {
        AddLexeme(new PronunciationLexeme(reading.Source, reading.LexicalIdentity,
            reading.EntryKind == "lemma" ? null : "unverified_entry_kind", [reading]));
    }

    public (PronunciationCoverage Coverage, PronunciationEvidence[] Evidence,
        PronunciationNormalizedEntry[] Entries, PronunciationJoin[] Joins) Build(
            string? lexiconPath = null,
            IEnumerable<PronunciationReference>? references = null)
    {
        var unique = readings.GroupBy(ReadingKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.OrderBy(reading => reading.OriginalIpa, StringComparer.Ordinal)
                .ThenBy(reading => reading.OriginalForm ?? reading.Lemma, StringComparer.Ordinal).First())
            .ToArray();
        var evidence = unique.Select(CreateEvidence).OrderBy(row => row.EntryId, StringComparer.Ordinal).ToArray();
        var usable = evidence.Where(row => row.Exclusions.Length == 0).ToArray();
        var alternatives = usable.GroupBy(row => row.Source + "\u001f" + row.LexicalIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        evidence = [.. evidence.Select(row =>
        {
            var count = row.Exclusions.Length == 0 ? alternatives[row.Source + "\u001f" + row.LexicalIdentity] : 0;
            return row with { AlternativeCount = count, AlternativeWeight = count == 0 ? 0 : 1.0 / count };
        })];
        usable = [.. evidence.Where(row => row.Exclusions.Length == 0)];
        var evidenceById = unique.ToDictionary(CreateEntryId, StringComparer.Ordinal);
        var entries = usable.Select(row => new PronunciationNormalizedEntry
        {
            EntryId = row.EntryId,
            SourceRecordId = row.EntryId[(row.EntryId.LastIndexOf(':') + 1)..],
            Language = language.Language,
            Family = language.Family,
            Source = row.Source,
            SourceVersion = row.Snapshot,
            Lemma = row.Lemma,
            OriginalForm = evidenceById[row.EntryId].OriginalForm ?? row.Lemma,
            Form = row.Lemma,
            PartOfSpeech = row.PartOfSpeech,
            Ipa = row.NormalizedIpa,
            License = evidenceById[row.EntryId].License,
        }).ToArray();
        var joins = lexiconPath == null || !File.Exists(lexiconPath) ? [] : Join(lexiconPath, usable);
        var bands = PronunciationPolicyEvaluator.Counts(usable
            .GroupBy(row => row.Source + "\u001f" + row.LexicalIdentity, StringComparer.Ordinal)
            .Select(group => group.OrderBy(row => row.NormalizedIpa, StringComparer.Ordinal).First().LengthBand));
        var supported = usable.Where(row => row.EvidenceStatus == "supported").ToArray();
        var eligibleEvidence = evidence.Where(row => eligible.Contains(row.Source + "\u001f" + row.LexicalIdentity)).ToArray();
        var blockers = new List<string>();
        if (LexicalCount(usable) < policy.MinimumLemmas)
        {
            blockers.Add("insufficient_usable_lemmas");
        }

        if (policy.RequiredBands.Any(band => bands.GetValueOrDefault(band) < policy.MinimumPerBand))
        {
            blockers.Add("insufficient_length_band_coverage");
        }

        if (LexicalCount(supported) < policy.MinimumLemmas)
        {
            blockers.Add("insufficient_supported_evidence");
        }

        // M2-07 audits candidate data; M2-08/M2-09 must implement profiles and admission.
        blockers.Add("profile_and_retrieval_admission_pending_m2_08_m2_09");
        var exclusions = new SortedDictionary<string, int>(sourceExclusions, StringComparer.Ordinal);
        foreach (var code in evidence.SelectMany(row => row.Exclusions))
        {
            PronunciationPolicyEvaluator.Increment(exclusions, code);
        }

        var overlap = evidence.GroupBy(row => row.Lemma + "\u001f" + row.NormalizedIpa, StringComparer.Ordinal)
            .Where(group => group.Select(row => row.Source).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => string.Join("+", group.Select(row => row.Source)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        var coverage = new PronunciationCoverage
        {
            Language = language.Language,
            Convention = language.Convention,
            SourceRecords = sourceRecords,
            EligibleLexicalUnits = eligible.Count,
            LemmasWithIpa = LexicalCount(eligibleEvidence),
            ConventionMatchedLemmas = LexicalCount(eligibleEvidence.Where(row => !row.Exclusions.Contains("convention_mismatch"))),
            UsableLemmas = LexicalCount(usable),
            SupportedEvidenceLemmas = LexicalCount(supported),
            DuplicateReadings = readings.Count - unique.Length,
            LengthBands = bands,
            Origins = PronunciationPolicyEvaluator.Counts(evidence.Select(row => row.Origin)),
            Labels = PronunciationPolicyEvaluator.Counts(evidence.SelectMany(row => row.Labels)),
            Exclusions = exclusions,
            UnknownSegments = PronunciationPolicyEvaluator.Counts(evidence.SelectMany(row => row.UnknownSegments)),
            SourceOverlap = PronunciationPolicyEvaluator.Counts(overlap),
            LexiconStatus = lexiconPath == null ? "not_configured" : File.Exists(lexiconPath) ? "available" : "missing",
            TargetLemmas = joins.Length,
            Joins = PronunciationPolicyEvaluator.Counts(joins.Select(row => row.Status)),
            ConverterAgreement = PronunciationPolicyEvaluator.EvaluateReferences(references ?? [], policy, encoder),
            Blockers = [.. blockers.Order(StringComparer.Ordinal)],
        };
        return (coverage, evidence, entries, joins);
    }

    public static PronunciationJoin[] Join(string lexiconPath, IReadOnlyList<PronunciationEvidence> evidence)
    {
        var targets = new List<(string Id, string Lemma, string Pos)>();
        using var reader = new StreamReader(lexiconPath, new UTF8Encoding(false, true));
        while (reader.ReadLine() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var kind = PronunciationDictionaryAdapter.GetString(root, "entry_kind");
            if (kind is not ("lemma" or "inflected" or "proper-name" or "abbreviation"))
            {
                throw new InvalidDataException($"{lexiconPath}: invalid or missing entry_kind.");
            }

            if (kind != "lemma")
            {
                continue;
            }

            var id = PronunciationDictionaryAdapter.GetString(root, "entry_id");
            var lemma = PronunciationDictionaryAdapter.GetString(root, "lemma").Normalize(NormalizationForm.FormC);
            var pos = root.TryGetProperty("part_of_speech", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()! : string.Empty;
            if (id.Length == 0 || lemma.Length == 0)
            {
                throw new InvalidDataException($"{lexiconPath}: lemma records require entry_id and lemma.");
            }

            targets.Add((id, lemma, pos));
        }

        if (targets.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count() != targets.Count)
        {
            throw new InvalidDataException($"{lexiconPath}: duplicate target entry IDs.");
        }

        var byLemma = evidence.GroupBy(row => row.Lemma, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var targetAmbiguities = targets.GroupBy(target => (target.Lemma, target.Pos))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        return [.. targets.OrderBy(target => target.Id, StringComparer.Ordinal).Select(target =>
        {
            var candidates = byLemma.GetValueOrDefault(target.Lemma, [])
                .Where(row => target.Pos.Length == 0 || row.PartOfSpeech == target.Pos).ToArray();
            var identities = candidates.Select(row => row.LexicalIdentity).Distinct(StringComparer.Ordinal).Count();
            var status = candidates.Length == 0 ? "unmatched"
                : identities > 1 || targetAmbiguities.Contains((target.Lemma, target.Pos)) ? "ambiguous"
                : "matched";
            return new PronunciationJoin(target.Id, target.Lemma, target.Pos.Length == 0 ? null : target.Pos,
                status, status == "matched" ? [.. candidates.Select(row => row.EntryId).Order(StringComparer.Ordinal)] : []);
        })];
    }

    private PronunciationEvidence CreateEvidence(PronunciationReading reading)
    {
        var normalized = PronunciationPolicyEvaluator.NormalizeIpa(reading.OriginalIpa);
        var (_, _, phones) = encoder.GetTextAndPhones(normalized.Normalize(NormalizationForm.FormD), out var unknown);
        var labels = reading.Labels;
        var compatible = (language.RequiredLabels.Length == 0 ||
            labels.Intersect(language.RequiredLabels, StringComparer.OrdinalIgnoreCase).Any()) &&
            !labels.Intersect(language.RejectedLabels, StringComparer.OrdinalIgnoreCase).Any();
        var exclusions = new List<string>();
        if (reading.EntryKind != "lemma")
        {
            exclusions.Add("unverified_entry_kind");
        }

        if (!compatible)
        {
            exclusions.Add("convention_mismatch");
        }

        if (unknown.Count != 0)
        {
            exclusions.Add("unknown_ipa");
        }

        if (phones.Count == 0)
        {
            exclusions.Add("empty_phonology");
        }

        if (phones.Count < policy.MinimumPhonemes || phones.Count > policy.MaximumPhonemes)
        {
            exclusions.Add("outside_length");
        }

        return new PronunciationEvidence
        {
            EntryId = CreateEntryId(reading),
            Source = reading.Source,
            Snapshot = reading.Snapshot,
            SourceEntry = reading.SourceEntry,
            Language = language.Language,
            Lemma = reading.Lemma.Normalize(NormalizationForm.FormC),
            PartOfSpeech = reading.PartOfSpeech,
            LexicalIdentity = reading.LexicalIdentity,
            Convention = language.Convention,
            Labels = [.. labels.Order(StringComparer.Ordinal)],
            OriginalIpa = reading.OriginalIpa,
            NormalizedIpa = normalized,
            Origin = reading.Origin,
            Generator = reading.Generator,
            Dependencies = [.. reading.Dependencies.Order(StringComparer.Ordinal)],
            PhonemeLength = phones.Count,
            LengthBand = PronunciationPolicyEvaluator.LengthBand(phones.Count),
            UnknownSegments = [.. unknown],
            Exclusions = [.. exclusions.Order(StringComparer.Ordinal)],
            EvidenceStatus = exclusions.Count == 0 && policy.AdmittedOrigins.Contains(reading.Origin, StringComparer.Ordinal)
                ? "supported" : "exploratory",
        };
    }

    private static string ReadingKey(PronunciationReading reading) =>
        string.Join('\u001f', reading.Source, reading.Snapshot, reading.Language,
            reading.LexicalIdentity, PronunciationPolicyEvaluator.NormalizeIpa(reading.OriginalIpa),
            string.Join('\u001e', reading.Labels.Order(StringComparer.Ordinal)),
            reading.Origin, reading.Generator, string.Join('\u001e', reading.Dependencies.Order(StringComparer.Ordinal)),
            reading.EntryKind);

    private static string CreateEntryId(PronunciationReading reading) =>
        reading.Source + ":" + reading.Language + ":" + PronunciationPolicyEvaluator.StableId(ReadingKey(reading));

    private static int LexicalCount(IEnumerable<PronunciationEvidence> rows) =>
        rows.Select(row => row.Source + "\u001f" + row.LexicalIdentity).Distinct(StringComparer.Ordinal).Count();
}
