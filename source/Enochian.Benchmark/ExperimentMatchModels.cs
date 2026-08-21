namespace Enochian.Benchmark;

public sealed record ExperimentMatchRecord(
    string SchemaVersion,
    string AnalysisId,
    string AnalysisMode,
    string SampleId,
    int RequestedSize,
    int Repetition,
    string QueryId,
    int QueryLength,
    string Section,
    string FrequencyBand,
    int Weight,
    string Language,
    string Family,
    bool IsNull,
    string? NullId,
    string? NullKind,
    string CandidateId,
    double RawCost,
    int PathLength,
    double MeanPathCost,
    double MeanInputLengthCost,
    int WithinSampleRank);

public sealed record ExperimentMatchResult(
    IReadOnlyList<ExperimentMatchRecord> Scores,
    IReadOnlyList<NearestDistanceRecord> NearestDistances);
