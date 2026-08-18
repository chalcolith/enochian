namespace Enochian.Controls;

public sealed record ControlLanguagePlan(
    string ProfileId,
    string SourceEncoding,
    bool ConfirmatoryCandidate,
    string? AdequacyBlocker,
    bool RequireArabicVowelMarks = false)
{
    public static ControlLanguagePlan For(string language) => language switch
    {
        "tur" => new("tur-Latn", "Latin", true, null),
        "hun" => new("hun-Latn", "Latin", true, null),
        "hin" => new("hin-Deva", "Devanagari", true, null),
        "ben" => new("ben-Beng", "Bengali", false, "insufficient_auditable_lemmas"),
        "guj" => new("", "Gujarati", false, "inadequate_or_unavailable_g2p_profile"),
        "fas" => new("fas-Arab", "Perso-Arabic", false, "unvocalized_orthography", true),
        _ => throw new InvalidDataException($"No predeclared control-language plan for '{language}'."),
    };
}
