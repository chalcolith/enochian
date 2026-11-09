# Voynich mapping preregistration

Preregistration: `voynich-mapping-preregistration-v1`
Status: **approved**
Ready for confirmatory use: **true**

Comparative results inspected before decision: **no**
Evaluation loci: `f1r`, `f1v`, `f2r`, `f2v`
Unopened holdout loci: `f3r`, `f3v`, `f4r`, `f4v`

## Pinned phonological space

Dimensions (30): `AdvancedTongueRoot,ATR`, `Anterior,Ant`, `Approximant`, `Back`, `Close`, `Consonantal,Cons`, `ConstrictedGlottis,CG`, `Continuant,Cont`, `Coronal,Cor`, `DelayedRelease,DelRel`, `Distributed,Dist`, `Dorsal`, `Front`, `Glottal`, `High`, `Labial,Lab`, `Lateral`, `Low`, `Nasal`, `Pharyngeal,Phar`, `Raised`, `Retracted`, `RetractedTongueRoot,RTR`, `Round`, `Sonorant,Son`, `SpreadGlottis,SG`, `Strident`, `Syllabic,Syll`, `Tense`, `Voice`
Allowed values: `-1`, `0`, `1`

## Evaluation inventory

Inventory `voynich-evaluation-symbol-inventory-v1`: `a`, `c`, `d`, `e`, `f`, `h`, `i`, `k`, `l`, `m`, `n`, `o`, `p`, `q`, `r`, `s`, `t`, `y`

## Candidates

### vogt-basic-eva-complete-v1 1.0.0 (proposal)

A complete, character-level, single-segment operationalization of the repository's existing Vogt-derived exploratory hypothesis for the frozen 18-symbol evaluation inventory.

Source: `resources/encodings/vogt.json` (`d08b88f5c7ad0f87c67cbc94c5715a548086d2c9becf507f70c8fd9794da7b55`)
Rationale: Preserve each legacy single-character, single-segment Vogt value where available; make the previously missing EVA i and q assumptions explicit as /i/ and /q/; and reduce legacy multi-phone EVA o to its initial vowel /ae/ so every frozen basic-EVA character has exactly one auditable vector. This is an unscored completion hypothesis, not evidence that the assignments are correct.
Normalization: Unicode NFC; case preserve frozen uncapitalized Takeshi Takahashi H layer; uncertain symbols exclude whole tokens containing star, question-mark, extended, or non-basic EVA forms under the pinned evaluation-inventory policy.
Segmentation: consume one accepted basic-EVA character as one phonological segment; overlap resolution not applicable; characters are consumed left-to-right without multigraph replacement; multigraphs none.
Differences from legacy: Uses deterministic one-character-to-one-segment parsing and disables all legacy multigraph/context patterns.; Adds explicit i to /i/ and q to /q/ assumptions because the legacy encoding has no values for those evaluation symbols.; Maps o to the legacy initial vowel /ae/ represented by IPA ae ligature, omitting the legacy following rhotic segment.; Uses canonical IPA symbols rather than legacy display aliases S, N, and superscript-t s.
Transcription ambiguities: i -> map to /i/ (The legacy Vogt encoding has no i rule; this transparent completion is explicit and requires researcher approval.); q -> map to /q/ (The legacy Vogt encoding has no q rule; this transparent completion is explicit and requires researcher approval.); o -> map to /ae/ represented by IPA ae ligature (The legacy o rule emits a vowel followed by a rhotic vector; the proposal retains only its initial vowel to satisfy the frozen one-vector-per-symbol contract.); c and h -> both map to /h/ (This preserves the legacy single-character values while making their phonological merger explicit.); m, n, and r -> all map to /r/ (This preserves the legacy single-character values while making their phonological merger explicit.); uncertain or non-basic EVA token -> exclude the complete token before mapping (The frozen evaluation inventory reports ten such tokens and does not silently map or drop individual unknown characters.)
Mapped symbols: 18

| EVA symbol | Proposed IPA |
|---|---|
| `a` | `w` |
| `c` | `h` |
| `d` | `t` |
| `e` | `o` |
| `f` | `p` |
| `h` | `h` |
| `i` | `i` |
| `k` | `k` |
| `l` | `ʃ` |
| `m` | `r` |
| `n` | `r` |
| `o` | `æ` |
| `p` | `b` |
| `q` | `q` |
| `r` | `r` |
| `s` | `t͡s` |
| `t` | `g` |
| `y` | `ŋ` |
Materialized mapping SHA-256: `4bc43e1e9d0fd2f29a6f4e8de804fe460f367f75bc3f796af5e7e48a7ecfb16e`

### vogt-legacy-exploratory 1.0.0 (legacy)

Historical exploratory EVA encoding retained only for an explicit before/after comparison.

Source: `resources/encodings/vogt.json` (`d08b88f5c7ad0f87c67cbc94c5715a548086d2c9becf507f70c8fd9794da7b55`)
Rationale: This records the repository's existing Derek Vogt-derived exploratory hypothesis; it is not automatically nominated for confirmatory approval.
Normalization: Unicode NFC; case legacy case-sensitive patterns; uncertain symbols legacy flow behavior; not approved for confirmatory use.
Segmentation: legacy encoding-pattern precedence; overlap resolution legacy encoder longest-input ordering; multigraphs `ch`, `ee`, `eo`, `cfh`, `fch`, `cFh`, `cKh`, `ckh`, `kch`, `cPh`, `cph`, `pch`, `Sh`, `sh`, `cTh`, `cth`, `tch`.
Differences from legacy: none recorded.
Transcription ambiguities: none recorded.
Mapped symbols: 0

## Researcher decision

Decision: **approved**
Researcher: chalcolith
Date: 2026-11-09
Rationale: The purpose of this project is to evaluate the proposed Voynich phonology.

## Blockers

None.
