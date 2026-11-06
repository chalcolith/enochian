namespace Enochian.Provenance;

public sealed record PronunciationReferenceAuditPlan(
    string SchemaVersion, RecoveryPin ParentProtocol, int SelectionSeed,
    ReferenceAuditProfile[] Profiles, ReferenceAuditCandidate[] Candidates,
    string OutputDirectory, string AmendmentPath, string ReportPath);

public sealed record ReferenceAuditProfile(string Language, RecoveryPin Manifest, string Generator);

public sealed record ReferenceAuditCandidate
{
    public required string Id { get; init; }
    public required string Language { get; init; }
    public required string Url { get; init; }
    public required string Revision { get; init; }
    public required string License { get; init; }
    public required string LicenseStatus { get; init; }
    public required string UsagePolicy { get; init; }
    public required string EvidenceKind { get; init; }
    public required bool MachineReadable { get; init; }
    public required bool Immutable { get; init; }
    public required string Independence { get; init; }
    public required string Convention { get; init; }
    public RecoveryPin? Manifest { get; init; }
    public RecoveryPin? Records { get; init; }
    public required string[] Dependencies { get; init; }
    public required string Decision { get; init; }
    public required string[] Reasons { get; init; }
}

public sealed record IndependentPronunciationRecord
{
    public required string SourceRecordId { get; init; }
    public required string Language { get; init; }
    public required string Lemma { get; init; }
    public required string Convention { get; init; }
    public required string[] Ipa { get; init; }
    public required string ReferenceOrigin { get; init; }
    public required string[] Dependencies { get; init; }
    public required bool TuningExample { get; init; }
    public required bool IndependenceEstablished { get; init; }
}

public sealed record ReferenceAuditPackage(
    string SchemaVersion, RecoveryPin Plan, RecoveryPin ParentProtocol,
    RecoveryPin[] Profiles, RecoveryPin[] CandidateManifests, RecoveryPin[] CandidateRecords);
