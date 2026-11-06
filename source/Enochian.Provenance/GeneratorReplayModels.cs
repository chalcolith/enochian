namespace Enochian.Provenance;

public sealed record GeneratorReplayPlan(
    string SchemaVersion, RecoveryPin ParentProtocol, RecoveryPin[] Profiles,
    string Cutoff, string Image, string OutputDirectory);

public sealed record GeneratorReplayRequest(
    string Id, string Title, long PageRevision, string Template,
    string TransclusionId, string Invocation, string[] EntryIds);

public sealed record GeneratorReplaySource(
    string Title, long Revision, string? Timestamp, long PageId, string Model,
    string Language, string Content);

public sealed record GeneratorReplayResult(
    string Id, string[] Readings, string[] Requested, string[] Errors, string[] Unsupported);

public sealed record GeneratorReplayExecution(
    string Mediawiki, string Php, string Lua, GeneratorReplayResult[] Results);

public sealed record GeneratorReplayPackage(
    string SchemaVersion, RecoveryPin Plan, RecoveryPin[] Adapter,
    RecoveryPage[] Sources, RecoveryPin Requests, RecoveryPin Execution);

public sealed record GeneratorReplayProof(
    string SchemaVersion, string EntryId, string ParentRecordSha256,
    string Language, string LexicalIdentity, string Convention, string OriginalIpa,
    string? RequestId, string Status, string? Generator, string? GeneratorRevision,
    string[] Dependencies);
