namespace Enochian.Controls;

public sealed record ControlSourceLemma(
    string RecordId,
    string OriginalForm,
    string NormalizedForm,
    string? PartOfSpeech,
    IReadOnlyList<string> Morphology);

public sealed record ControlSourceRejection(
    string RecordId,
    string Category,
    string Reason);

public sealed record ControlSourceResult(
    IReadOnlyList<ControlSourceLemma> Lemmas,
    IReadOnlyList<ControlSourceRejection> Rejections);
