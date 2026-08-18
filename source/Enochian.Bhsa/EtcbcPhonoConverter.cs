using Enochian.Provenance;
using System.Text;

namespace Enochian.Bhsa;

public sealed record EtcbcPhonoConversion(
    string Ipa,
    IReadOnlyList<IpaConversionDiagnostic> Diagnostics,
    IReadOnlyList<string> UnknownSymbols);

public static class EtcbcPhonoConverter
{
    private static readonly Dictionary<Rune, string> Symbols = new()
    {
        [new Rune('a')] = "a",
        [new Rune('b')] = "b",
        [new Rune('d')] = "d",
        [new Rune('e')] = "e",
        [new Rune('f')] = "f",
        [new Rune('g')] = "g",
        [new Rune('h')] = "h",
        [new Rune('i')] = "i",
        [new Rune('k')] = "k",
        [new Rune('l')] = "l",
        [new Rune('m')] = "m",
        [new Rune('n')] = "n",
        [new Rune('o')] = "o",
        [new Rune('p')] = "p",
        [new Rune('q')] = "q",
        [new Rune('r')] = "r",
        [new Rune('s')] = "s",
        [new Rune('t')] = "t",
        [new Rune('u')] = "u",
        [new Rune('v')] = "v",
        [new Rune('w')] = "w",
        [new Rune('y')] = "j",
        [new Rune('z')] = "z",
        [new Rune('ð')] = "ð",
        [new Rune('ə')] = "ə",
        [new Rune('ɣ')] = "ɣ",
        [new Rune('ħ')] = "ħ",
        [new Rune('ʃ')] = "ʃ",
        [new Rune('θ')] = "θ",
        [new Rune('ː')] = "ː",
        [new Rune('ˤ')] = "ˤ",
        [new Rune('ê')] = "eː",
        [new Rune('î')] = "iː",
        [new Rune('ô')] = "oː",
        [new Rune('û')] = "uː",
        [new Rune('ā')] = "aː",
        [new Rune('ē')] = "eː",
        [new Rune('ō')] = "oː",
        [new Rune('ś')] = "s",
        [new Rune('š')] = "ʃ",
        [new Rune('ʔ')] = "ʔ",
        [new Rune('ʕ')] = "ʕ",
        [new Rune('ʸ')] = "j",
        [new Rune('ᵃ')] = "a",
        [new Rune('ᵉ')] = "e",
        [new Rune('ᵊ')] = "ə",
        [new Rune('ᵒ')] = "o",
        [new Rune('ḏ')] = "ð",
        [new Rune('ḡ')] = "ɣ",
        [new Rune('ḥ')] = "ħ",
        [new Rune('ḵ')] = "x",
        [new Rune('ṣ')] = "sˤ",
        [new Rune('ṭ')] = "tˤ",
        [new Rune('ṯ')] = "θ",
        [new Rune('ₐ')] = "a",
    };
    private static readonly HashSet<Rune> StructuralSymbols =
    [
        new Rune(' '), new Rune('*'), new Rune('-'), new Rune('.'), new Rune('['), new Rune(']'),
    ];
    private static readonly HashSet<Rune> StressSymbols = [new Rune('ˈ'), new Rune('ˌ')];
    private static readonly HashSet<Rune> ReducedVowels =
    [
        new Rune('ᵃ'), new Rune('ᵉ'), new Rune('ᵊ'), new Rune('ᵒ'), new Rune('ₐ'),
    ];

    public static EtcbcPhonoConversion Convert(string phono)
    {
        var ipa = new StringBuilder();
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        var removedStructural = false;
        var removedStress = false;
        var mappedReducedVowel = false;
        foreach (var symbol in phono.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (StructuralSymbols.Contains(symbol))
            {
                removedStructural = true;
                continue;
            }

            if (StressSymbols.Contains(symbol))
            {
                removedStress = true;
                continue;
            }

            if (Symbols.TryGetValue(symbol, out var replacement))
            {
                _ = ipa.Append(replacement);
                mappedReducedVowel |= ReducedVowels.Contains(symbol);
                continue;
            }

            _ = unknown.Add(symbol.ToString());
        }

        var diagnostics = new List<IpaConversionDiagnostic>
        {
            new()
            {
                Code = "etcbc_phono_source",
                Message = "Preserved the original ETCBC phono transcription.",
                Text = phono,
            },
        };
        if (removedStructural)
        {
            diagnostics.Add(new()
            {
                Code = "removed_structural_marker",
                Message = "Removed ETCBC qere, Tetragrammaton, word-boundary, or punctuation markers from lexical IPA.",
            });
        }

        if (removedStress)
        {
            diagnostics.Add(new()
            {
                Code = "removed_nonphonetic_stress",
                Message = "Removed ETCBC stress marks, which upstream documents as not consistently phonetic.",
            });
        }

        if (mappedReducedVowel)
        {
            diagnostics.Add(new()
            {
                Code = "normalized_reduced_vowel",
                Message = "Mapped ETCBC superscript reduced-vowel symbols to the corresponding segmental IPA vowel or schwa.",
            });
        }

        foreach (var symbol in unknown)
        {
            diagnostics.Add(new()
            {
                Code = "unconverted_grapheme",
                Message = "No declared segmental IPA mapping for ETCBC phono symbol.",
                Text = symbol,
            });
        }

        return new(ipa.ToString().Normalize(NormalizationForm.FormC), diagnostics, [.. unknown]);
    }
}
