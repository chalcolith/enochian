namespace Enochian.Benchmark;

public sealed record SamplingCandidateInput(
    string EntryId,
    string Language,
    string Lemma,
    string Phonology,
    IReadOnlyList<double[]> Phones,
    double? Frequency,
    string EntryKind,
    string SourceId,
    string SourceRecordId);

public sealed record SamplingSourceMembership(
    string EntryId,
    string SourceId,
    string SourceRecordId);

public sealed record SamplingCandidate(
    string CandidateId,
    string Language,
    string Lemma,
    string Phonology,
    IReadOnlyList<double[]> Phones,
    double? Frequency,
    string EntryKind,
    IReadOnlyList<SamplingSourceMembership> SourceMemberships);

public sealed record SamplingFrequencyBand(
    string Id,
    double? Minimum,
    double? Maximum);

public sealed record SamplingMembership(
    string SchemaVersion,
    string AnalysisId,
    string AnalysisSet,
    string SampleId,
    int Repetition,
    int RequestedSize,
    int Seed,
    string GeneratorVersion,
    string Language,
    string LengthBand,
    string FrequencyBand,
    string CandidateId,
    string Lemma,
    string Phonology,
    string EntryKind,
    IReadOnlyList<SamplingSourceMembership> SourceMemberships);

public sealed record SamplingShortage(
    string Language,
    string LengthBand,
    string FrequencyBand,
    int Available,
    int CommonCapacity,
    int ExcludedByBalance);

public sealed record BalancedSamplingResult(
    int LargestCommonSize,
    IReadOnlyList<int> SampleSizes,
    IReadOnlyList<SamplingMembership> Memberships,
    IReadOnlyList<SamplingShortage> Shortages);

public sealed record SamplingQuery(
    string QueryId,
    string Text,
    IReadOnlyList<string> Symbols,
    int TokenFrequency);

public sealed record SequenceNullRecord(
    string SchemaVersion,
    string AnalysisId,
    string SampleId,
    int RequestedSize,
    string NullId,
    bool IsNull,
    string NullKind,
    string AnalysisMode,
    int Weight,
    int Repetition,
    int Seed,
    string GeneratorVersion,
    string Language,
    string QueryId,
    int QueryLength,
    IReadOnlyList<double[]> Phones);
