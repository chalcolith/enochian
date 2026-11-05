using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enochian.Provenance;

public sealed record ArabicConversion(string? Ipa, string[] Diagnostics);

public static class ClassicalArabicConverter
{
    public const string Version = "classical-arabic-vocalized-v1";

    private const char Fatha = '\u064e';
    private const char Damma = '\u064f';
    private const char Kasra = '\u0650';
    private const char Shadda = '\u0651';
    private const char Sukun = '\u0652';
    private const char DaggerAlif = '\u0670';
    private const string VowelMarks = "\u064b\u064c\u064d\u064e\u064f\u0650\u0652";
    private const string SunLetters = "\u062a\u062b\u062f\u0630\u0631\u0632\u0633\u0634\u0635\u0636\u0637\u0638\u0644\u0646";
    private static readonly Dictionary<char, string> Consonants = new()
    {
        ['\u0621'] = "ʔ",
        ['\u0623'] = "ʔ",
        ['\u0625'] = "ʔ",
        ['\u0624'] = "ʔ",
        ['\u0626'] = "ʔ",
        ['\u0628'] = "b",
        ['\u062a'] = "t",
        ['\u062b'] = "θ",
        ['\u062c'] = "d͡ʒ",
        ['\u062d'] = "ħ",
        ['\u062e'] = "x",
        ['\u062f'] = "d",
        ['\u0630'] = "ð",
        ['\u0631'] = "r",
        ['\u0632'] = "z",
        ['\u0633'] = "s",
        ['\u0634'] = "ʃ",
        ['\u0635'] = "sˤ",
        ['\u0636'] = "dˤ",
        ['\u0637'] = "tˤ",
        ['\u0638'] = "ðˤ",
        ['\u0639'] = "ʕ",
        ['\u063a'] = "ɣ",
        ['\u0641'] = "f",
        ['\u0642'] = "q",
        ['\u0643'] = "k",
        ['\u0644'] = "l",
        ['\u0645'] = "m",
        ['\u0646'] = "n",
        ['\u0647'] = "h",
        ['\u0648'] = "w",
        ['\u064a'] = "j",
        ['\u0629'] = "t",
    };

    public static ArabicConversion Convert(string form)
    {
        var letters = new List<(char Letter, string Marks)>();
        var diagnostics = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var character in form.Normalize(NormalizationForm.FormC))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                if (letters.Count == 0 || (!VowelMarks.Contains(character) && character != Shadda && character != DaggerAlif))
                {
                    _ = diagnostics.Add("unsupported_or_unattached_mark");
                    continue;
                }

                var (previousLetter, previousMarks) = letters[^1];
                letters[^1] = (previousLetter, previousMarks + character);
            }
            else if (Consonants.ContainsKey(character) || character is '\u0627' or '\u0622' or '\u0649' or '\u0671')
            {
                letters.Add((character, string.Empty));
            }
            else
            {
                _ = diagnostics.Add("unsupported_grapheme_or_word_boundary");
            }
        }

        var ipa = new StringBuilder();
        var start = 0;
        if (letters.Count >= 3 && letters[0].Letter is '\u0627' or '\u0671' &&
            letters[1].Letter == '\u0644' && letters[0].Marks.All(mark => mark == Fatha) &&
            letters[1].Marks.All(mark => mark == Sukun))
        {
            var sun = SunLetters.Contains(letters[2].Letter);
            if (sun && !letters[2].Marks.Contains(Shadda))
            {
                _ = diagnostics.Add("article_assimilation_requires_explicit_shadda");
            }

            _ = ipa.Append(sun ? "ʔa" : "ʔal");
            start = 2;
        }

        for (var index = start; index < letters.Count; index++)
        {
            var (letter, marks) = letters[index];
            var vowels = marks.Where(VowelMarks.Contains).ToArray();
            if (vowels.Length > 1 || marks.Distinct().Count() != marks.Length)
            {
                _ = diagnostics.Add("conflicting_vocalization");
            }

            if (letter == '\u0622')
            {
                if (marks.Length != 0)
                {
                    _ = diagnostics.Add("unsupported_madda_context");
                }

                _ = ipa.Append("ʔaː");
                continue;
            }

            var consonant = Consonants.GetValueOrDefault(letter);
            if (consonant == null && index == 0 && letter is '\u0627' or '\u0671' && vowels.Length == 1)
            {
                consonant = "ʔ";
            }

            if (consonant == null)
            {
                _ = diagnostics.Add("unresolved_mater_or_alif_context");
                continue;
            }

            if (vowels.Length == 0)
            {
                _ = diagnostics.Add("missing_vocalization");
            }

            _ = ipa.Append(consonant);
            if (marks.Contains(Shadda))
            {
                _ = ipa.Append(consonant);
            }

            var vowel = vowels.FirstOrDefault();
            if (letter == '\u0629' && (index + 1 != letters.Count || vowel is '\0' or Sukun))
            {
                _ = diagnostics.Add("unresolved_ta_marbuta");
            }

            var vowelIpa = vowel switch
            {
                Fatha => "a",
                Damma => "u",
                Kasra => "i",
                '\u064b' => "an",
                '\u064c' => "un",
                '\u064d' => "in",
                _ => string.Empty,
            };
            if (marks.Contains(DaggerAlif))
            {
                if (vowel != Fatha)
                {
                    _ = diagnostics.Add("dagger_alif_requires_fatha");
                }

                vowelIpa = "aː";
            }
            else if (index + 1 < letters.Count && letters[index + 1].Marks.Length == 0)
            {
                var next = letters[index + 1].Letter;
                if ((vowel == Fatha && next is '\u0627' or '\u0649') ||
                    (vowel == Damma && next == '\u0648') || (vowel == Kasra && next == '\u064a'))
                {
                    vowelIpa += "ː";
                    index++;
                }
                else if (vowel == '\u064b' && next == '\u0627' && index + 2 == letters.Count)
                {
                    index++;
                }
            }

            _ = ipa.Append(vowelIpa);
        }

        if (letters.Count == 0 || ipa.Length == 0)
        {
            _ = diagnostics.Add("empty_phonology");
        }

        return new ArabicConversion(diagnostics.Count == 0 ? ipa.ToString() : null, [.. diagnostics]);
    }

    public static IEnumerable<PronunciationLexeme> ReadLexemes(TextReader reader, PronunciationSnapshot source)
    {
        if (source.Language != "ara")
        {
            throw new InvalidDataException($"{source.Id}: Classical Arabic lexical manifests must declare language ara.");
        }

        var identities = new Dictionary<string, (string Lemma, string Pos)>(StringComparer.Ordinal);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var row = JsonSerializer.Deserialize<VocalizedArabicLemma>(line, PronunciationAudit.JsonOptions)
                ?? throw new InvalidDataException($"{source.Id}:{lineNumber}: empty Classical Arabic lexeme.");
            if (new[] { row.Language, row.Convention, row.Lemma, row.Form, row.PartOfSpeech, row.LexicalIdentity, row.EntryKind }
                .Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException($"{source.Id}:{lineNumber}: missing Classical Arabic lexical metadata.");
            }

            var lexicalKey = (row.Lemma.Normalize(NormalizationForm.FormC), row.PartOfSpeech);
            if (identities.TryGetValue(row.LexicalIdentity, out var previous) && previous != lexicalKey)
            {
                throw new InvalidDataException($"{source.Id}:{lineNumber}: lexical identity refers to conflicting lemmas or parts of speech.");
            }

            identities[row.LexicalIdentity] = lexicalKey;
            var exclusion = row.Language != "ara" || row.Convention != "classical-arabic"
                ? "classical_source_not_established"
                : row.EntryKind != "lemma" || row.PartOfSpeech is "name" or "abbrev" ? "entry_kind" : null;
            var conversion = exclusion == null ? Convert(row.Form) : null;
            if (conversion?.Ipa == null && exclusion == null)
            {
                exclusion = "arabic_" + string.Join("+", conversion!.Diagnostics);
            }

            yield return new PronunciationLexeme(source.Id, row.LexicalIdentity, exclusion,
                exclusion != null ? [] :
                [
                    new PronunciationReading(source.Id, source.Revision, source.Url, "ara",
                        row.Lemma.Normalize(NormalizationForm.FormC), row.PartOfSpeech, row.LexicalIdentity,
                        conversion!.Ipa!, ["Classical-Arabic"], "converter-generated", Version,
                        [source.Id, Version], source.License, OriginalForm: row.Form),
                ]);
        }
    }
}

public sealed record VocalizedArabicLemma
{
    public required string Language { get; init; }
    public required string Convention { get; init; }
    public required string Lemma { get; init; }
    public required string Form { get; init; }
    public required string PartOfSpeech { get; init; }
    public required string LexicalIdentity { get; init; }
    public required string EntryKind { get; init; }
}
