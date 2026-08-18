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

public sealed record ControlInflectedForm(
    string RecordId,
    string Lemma,
    string Form,
    IReadOnlyList<string> Features);

public sealed record ControlSourceResult(
    IReadOnlyList<ControlSourceLemma> Lemmas,
    IReadOnlyList<ControlSourceRejection> Rejections,
    IReadOnlyList<ControlInflectedForm>? InflectedForms = null);
