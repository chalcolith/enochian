namespace Enochian.Benchmark;

public sealed record HashedArtifact(string Path, string Sha256, long Bytes);

public sealed record ExperimentStageRecord(
    string StageId,
    string InputSha256,
    IReadOnlyList<HashedArtifact> Outputs);

public sealed record ExperimentRunManifest(
    string SchemaVersion,
    string RunnerVersion,
    string RunId,
    string ConfigId,
    string Phase,
    bool NetworkAllowedAfterValidation,
    IReadOnlyList<HashedArtifact> Inputs,
    IReadOnlyList<ExperimentStageRecord> Stages,
    IReadOnlyList<HashedArtifact> Artifacts);

public sealed record ExperimentReportInputs(
    string SchemaVersion,
    string RunId,
    string ConfigId,
    IReadOnlyList<HashedArtifact> StatisticalTables);
