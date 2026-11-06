namespace Enochian.Provenance;

public sealed record PronunciationLanguage
{
    public required string Language { get; init; }
    public required string DictionaryCode { get; init; }
    public required string Family { get; init; }
    public required string Convention { get; init; }
    public string[] RequiredLabels { get; init; } = [];
    public string[] RejectedLabels { get; init; } = [];
    public string? LexiconPath { get; init; }
    public string? ReferencePath { get; init; }
}

public sealed record PronunciationPolicy
{
    public int MinimumLemmas { get; init; } = 100;
    public int MinimumPerBand { get; init; } = 25;
    public int MinimumReferences { get; init; } = 100;
    public double MinimumAgreement { get; init; } = 0.95;
    public int MinimumPhonemes { get; init; } = 3;
    public int MaximumPhonemes { get; init; } = 20;
    public string[] RequiredBands { get; init; } = ["03-05", "06-09", "10-20"];
    public string[] AdmittedOrigins { get; init; } = ["dictionary-supplied"];
}

public sealed record PronunciationPlan
{
    public required string SchemaVersion { get; init; }
    public required string PolicyId { get; init; }
    public required bool Frozen { get; init; }
    public required string DumpDate { get; init; }
    public required string ExtractionDate { get; init; }
    public required string ExtractorRevision { get; init; }
    public required string ProcessorRevision { get; init; }
    public required string[] SourceManifests { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ReportDirectory { get; init; }
    public required PronunciationPolicy Policy { get; init; }
    public required PronunciationLanguage[] Languages { get; init; }
}

public sealed record PronunciationReading(
    string Source,
    string Snapshot,
    string SourceEntry,
    string Language,
    string Lemma,
    string PartOfSpeech,
    string LexicalIdentity,
    string OriginalIpa,
    string[] Labels,
    string Origin,
    string? Generator,
    string[] Dependencies,
    string License,
    string EntryKind = "lemma",
    string? OriginalForm = null);

public sealed record PronunciationLexeme(
    string Source,
    string Identity,
    string? Exclusion,
    PronunciationReading[] Readings);

public sealed record PronunciationEvidence
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public required string EntryId { get; init; }
    public required string Source { get; init; }
    public required string Snapshot { get; init; }
    public required string SourceEntry { get; init; }
    public required string Language { get; init; }
    public required string Lemma { get; init; }
    public required string PartOfSpeech { get; init; }
    public required string LexicalIdentity { get; init; }
    public required string Convention { get; init; }
    public required string[] Labels { get; init; }
    public required string OriginalIpa { get; init; }
    public required string NormalizedIpa { get; init; }
    public required string Origin { get; init; }
    public string? Generator { get; init; }
    public string? GeneratorRevision { get; init; }
    public required string[] Dependencies { get; init; }
    public string Independence { get; init; } = "unknown";
    public string NormalizationPolicy { get; init; } = "ipa-nfc-delimiters-stress-syllables-v1";
    public string AlternativePolicy { get; init; } = "one-lexical-unit-equal-reading-weights-v1";
    public int AlternativeCount { get; init; }
    public double AlternativeWeight { get; init; }
    public int PhonemeLength { get; init; }
    public required string LengthBand { get; init; }
    public required string[] UnknownSegments { get; init; }
    public required string[] Exclusions { get; init; }
    public required string EvidenceStatus { get; init; }
}

public sealed record PronunciationNormalizedEntry
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public required string EntryId { get; init; }
    public required string SourceRecordId { get; init; }
    public required string Language { get; init; }
    public required string Family { get; init; }
    public required string Source { get; init; }
    public required string SourceVersion { get; init; }
    public required string Lemma { get; init; }
    public required string OriginalForm { get; init; }
    public required string Form { get; init; }
    public string EntryKind { get; init; } = "lemma";
    public string? Dialect { get; init; }
    public required string PartOfSpeech { get; init; }
    public string? Definition { get; init; }
    public double? Frequency { get; init; }
    public string SourceEncoding { get; init; } = "IPA";
    public required string Ipa { get; init; }
    public string UnicodeNormalization { get; init; } = "NFC";
    public required string License { get; init; }
}

public sealed record PronunciationJoin(
    string EntryId,
    string Lemma,
    string? PartOfSpeech,
    string Status,
    string[] DictionaryEntryIds);

public sealed record PronunciationReference
{
    public required string LemmaId { get; init; }
    public required string GeneratedIpa { get; init; }
    public required string ReferenceIpa { get; init; }
    public required string ReferenceOrigin { get; init; }
    public required string[] GeneratorDependencies { get; init; }
    public required string[] ReferenceDependencies { get; init; }
    public required bool HeldOut { get; init; }
    public required bool IndependenceEstablished { get; init; }
}

public sealed record PronunciationAlternativeReference
{
    public required string LemmaId { get; init; }
    public required string[] SourceRecordIds { get; init; }
    public required string[] GeneratedIpa { get; init; }
    public required string[] ReferenceIpa { get; init; }
    public required string ReferenceOrigin { get; init; }
    public required string[] GeneratorDependencies { get; init; }
    public required string[] ReferenceDependencies { get; init; }
    public required bool HeldOut { get; init; }
    public required bool TuningExample { get; init; }
    public required bool IndependenceEstablished { get; init; }
}

public sealed record IndependentReferenceSource(RecoveryPin Manifest, string[] Dependencies);

public sealed record GeneratorReferenceSet
{
    public required string SchemaVersion { get; init; }
    public required string ProfileSha256 { get; init; }
    public required string Generator { get; init; }
    public required string GeneratorRevision { get; init; }
    public required string Convention { get; init; }
    public required int SelectionSeed { get; init; }
    public required string SelectionPolicy { get; init; }
    public required IndependentReferenceSource[] ReferenceSources { get; init; }
    public required PronunciationAlternativeReference[] Records { get; init; }
}

public sealed record PronunciationAgreement(
    string Status,
    int SuppliedReferences,
    int IndependentReferences,
    int SampledReferences,
    double? ExactAgreement,
    double? MeanPhoneEditDistance,
    IReadOnlyDictionary<string, int> SampledBands,
    IReadOnlyDictionary<string, int> Diagnostics);

public sealed record PronunciationCoverage
{
    public required string Language { get; init; }
    public required string Convention { get; init; }
    public int SourceRecords { get; init; }
    public int EligibleLexicalUnits { get; init; }
    public int LemmasWithIpa { get; init; }
    public int ConventionMatchedLemmas { get; init; }
    public int UsableLemmas { get; init; }
    public int SupportedEvidenceLemmas { get; init; }
    public int DuplicateReadings { get; init; }
    public required IReadOnlyDictionary<string, int> LengthBands { get; init; }
    public required IReadOnlyDictionary<string, int> Origins { get; init; }
    public required IReadOnlyDictionary<string, int> Labels { get; init; }
    public required IReadOnlyDictionary<string, int> Exclusions { get; init; }
    public required IReadOnlyDictionary<string, int> UnknownSegments { get; init; }
    public required IReadOnlyDictionary<string, int> SourceOverlap { get; init; }
    public required string LexiconStatus { get; init; }
    public int TargetLemmas { get; init; }
    public required IReadOnlyDictionary<string, int> Joins { get; init; }
    public required PronunciationAgreement ConverterAgreement { get; init; }
    public required string[] Blockers { get; init; }
    public string Status { get; init; } = "exploratory";
}
