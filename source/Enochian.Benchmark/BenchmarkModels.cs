namespace Enochian.Benchmark;

public sealed record BenchmarkEntry(
    string EntryId,
    string Source,
    string SourceRecordId,
    string Language,
    string Ipa,
    IReadOnlyList<double[]> Phones,
    string LengthBand,
    string UnusualCategory);

public sealed record BenchmarkQuery(
    string QueryId,
    string Source,
    string SourceRecordId,
    string Language,
    IReadOnlyList<double[]> Phones,
    IReadOnlySet<string> RelevantEntryIds,
    string LengthBand,
    string UnusualCategory);

public sealed record RankedCandidate(
    string EntryId,
    double RawDistance,
    int PathLength,
    double NormalizedDistance,
    bool Relevant);

public sealed record RetrievalMetrics(
    bool RecallAt1,
    bool RecallAt5,
    bool RecallAt20,
    double ReciprocalRank,
    int? RelevantRank,
    double? RelevantNormalizedDistance,
    double? NearestNormalizedDistance,
    int CandidateCount);

public sealed record DtwMeasurement(double Cost, int PathLength)
{
    public double NormalizedDistance => PathLength == 0 ? 0 : Cost / PathLength;
}
