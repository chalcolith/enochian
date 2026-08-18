# Lexicon Provenance

Every current or planned lexicon source has one `*.manifest.json` file in
`manifests/`. The files conform to `schemas/source-manifest.schema.json` and are
the sole input to `ATTRIBUTION.md`. Validation is offline and never downloads a
source.

## Lifecycle

- `planned` records identify an owner, upstream URL, license status,
  distribution decision, and intended parser before acquisition. A planned
  record may use revision kind `unresolved` and null acquisition fields.
- `acquired` records pin a commit, tag, or release and require retrieval date,
  SHA-256, and repository-relative raw path. If that path exists, validation
  hashes its bytes and compares them with the manifest.
- Change a source to `acquired` only after recording the exact upstream
  revision and artifact. Never replace an unresolved revision with `main`,
  `master`, `HEAD`, or `latest`.

## Distribution

- `vendored` means the exact bytes are checked into this repository.
- `download-on-demand` means an explicit future acquisition step may fetch the
  pinned artifact, but packaging does not include it automatically.
- `metadata-only` means only provenance may be distributed by this repository.

`license_status: unverified` blocks default bundling even when a tentative
license value is recorded as `NOASSERTION`. A `non-commercial` source must be
optional and cannot be bundled by default. BHSA is therefore optional,
metadata-only, and absent from all default acquisition and package paths.

The legacy RomLex and SHS snapshots are still present for existing flows, but
their exact redistribution terms remain unverified. Their manifests preserve
real local checksums and explicitly prevent default bundling. CMUdict's embedded
BSD-2-Clause terms are verified and permit its current vendored use.

## Normalized lexicons

`NormalizedLexicon` streams one normalized-entry JSON object per UTF-8 line and
converts its `ipa` value through the configured IPA encoding. A minimal flow
lexicon configuration is:

```json
{
  "id": "example-normalized",
  "type": "NormalizedLexicon",
  "features": "Default",
  "encoding": "IPA",
  "path": "../resources/lexicons/generated/example.jsonl",
  "manifest": "../resources/lexicons/manifests/example.manifest.json",
  "qualityReport": "../reports/example.quality.json"
}
```

By default, `lemma`, `form`, `dialect`, `part_of_speech`, `definition`, and
`ipa` are normalized to NFC. `original_form` is retained byte-for-byte as the
source spelling. Override the normalized set with `normalizeFields`, using only
those field names.

Malformed records, duplicate entry IDs, missing or invalid fields, invalid
UTF-8, unknown IPA segments, and IPA that produces no phonological segments are
excluded and counted. The deterministic quality report records accepted and
rejected totals, unique and duplicate lemma/form/phonology counts, rejection
reasons, unknown symbols, phoneme-length counts, and each rejection's source
ID, line, reason code, and detail. Loading requires only checked-in artifacts;
no acquisition tool or network access occurs at runtime.

## CDSL acquisition and normalization

The `Enochian.Cdsl` command acquires AP, MW, PW, PWG, and SHS from the exact
`csl-orig` commit pinned in their manifests, verifies each raw file's SHA-256,
and writes one normalized JSONL file and one quality report per dictionary:

```powershell
dotnet run --project Enochian.Cdsl -- acquire-normalize
```

Run the command from `source/`. Raw snapshots are stored under
`.enoch/csl-orig-<commit>/` at the repository root, and normalized outputs are
stored under `.enoch/cdsl-generated/`. Both locations are ignored and are not
runtime dependencies. Use `normalize` instead of `acquire-normalize` for an
offline regeneration from an already verified snapshot.

All dictionaries share `<L>` headline parsing, `<LEND>` prefix boundaries,
record IDs, `k1` SLP1 headwords, and `k2` display-headword extraction. The MW
profile removes XML-like tags. AP, PW, PWG, and SHS additionally unwrap CDSL
Sanskrit (`{#...#}`), translation (`{%...%}`), and editorial (`{@...@}`)
markup. PW `{{Lbody=...}}` cross-reference markers are removed from definitions
but do not create duplicate entries. These rules live only in the acquisition
adapter; `NormalizedLexicon` remains source-neutral.

For commit `b7297b97cf9f7112277ea98f7969291eb1d5f495`, deterministic quality counts
are AP 90,846, MW 286,525, PW 170,556, PWG 123,366, and SHS 47,326 emitted
records. The sole rejected headword is AP record `6082.002`,
`asaMBAVitopamA`: uppercase `V` is not an SLP1 symbol. It is intentionally
excluded rather than assigned a guessed phonological value. This is the
reviewed unknown-symbol allowlist for adapter version 1.0.0; any other unknown
SLP1 symbol requires review before release.

## Sanskrit corpus panel

`samples/sanskrit-panel.json` configures normalized MW, AP, PWG, PW, and SHS
lexicons independently and searches each one in a separate matcher step. Run
the panel only after acquisition and normalization have produced the five
artifacts under `.enoch/cdsl-generated/`.

The primary Sanskrit union includes lemma entries with 2 through 24 phonemes
and excludes records whose lemma, display form, or definition retains angle
bracket markup. Exclusions are counted as `entry_kind`, `phoneme_length`,
`malformed_markup`, or `missing_phonology`. Included entries are deduplicated
by ordinally compared canonical IPA and, secondarily, normalized lemma. Each
union entry retains its source ID, source record ID, and entry ID memberships,
sorted ordinally. Inputs, union entries, source counts, overlap-matrix axes,
memberships, and exclusion reasons are all ordered independently of source
file or command-line order.

After normalization, produce the aggregate union/overlap report and the
legacy-to-normalized SHS comparison offline:

```powershell
dotnet run --project Enochian.Cdsl -- corpus-report
```

The command writes `sanskrit-corpus-report.json` and
`shs-comparison-report.json` under `.enoch/cdsl-generated/`. Per-source counts
represent dictionary evidence; `union_count` represents deduplicated evidence
and must not be interpreted as another independent dictionary. The overlap
matrix counts union entries shared by each source pair. Report generation
fails if any normalized loader reports an unknown IPA symbol.

The SHS discrepancy tolerance is declared as zero in `CdslPipeline`. Adapter
rejections carry their exact reason, and normalized homographs absent from the
legacy view are explained by the legacy loader's lemma collapse. Any remaining
unexplained discrepancy makes `corpus-report` fail.

`samples/voynich.json` intentionally remains on the legacy SHS snapshot for
M1-04, so its lexicon composition and matching behavior do not change. The new
panel is additive. Migrating the Voynich sample to normalized SHS requires a
separate recorded result comparison rather than silently changing its search
space.

## IPA conversion boundary

External and custom grapheme-to-phoneme converters are preprocessing tools,
not runtime dependencies. They exchange UTF-8 JSONL using
`schemas/ipa-conversion-request.schema.json` and
`schemas/ipa-conversion-artifact.schema.json`. A converter invocation must:

- read one request per line and emit exactly one artifact per request in the
  same order;
- write UTF-8 without a byte-order mark, use LF line endings, and produce the
  same bytes for the same ordered input and pinned profile;
- identify the provider and profile with nonempty pinned versions from a file
  conforming to `schemas/ipa-conversion-profile.schema.json`;
- mark unmapped graphemes as `incomplete`, include an `unconverted_grapheme`
  diagnostic, and exit nonzero if any record is incomplete; and
- write a machine-readable summary conforming to
  `schemas/ipa-audit-summary.schema.json`.

Epitran and custom profile examples are checked in under `examples/`. Package
licenses and versions belong in profile metadata. GPL-licensed tools may be
run only out of process; their code and environment are not linked, imported,
or packaged with the Enochian library. Generated IPA and the profile/provider
identity are preserved in normalized entries under `ipa_conversion`.

Audit a frozen conversion batch from `source/`:

```powershell
dotnet run --project Enochian.Provenance -- ipa-audit `
    <artifacts-jsonl> <profile-json> <review-jsonl> <summary-json> [sample-size]
```

The audit rejects malformed or incomplete records, missing or mismatched
versions, empty IPA, and segments unknown to the checked-in IPA encoding. It
always writes the machine summary and exits nonzero when any record is
rejected. Review rows conform to `schemas/ipa-review-sheet.schema.json`; they
omit language, source, record, provider, and profile metadata. Sampling favors
unusual graphemes and longer forms, then sorts rows by a stable SHA-256 blinded
ID so source ordering cannot reveal hidden groups.

## Latin control

`Enochian.Perseus` downloads the Lewis and Short TEI component from the exact
PerseusDL commit and URL pinned in
`manifests/perseus-lewis-short.manifest.json`, verifies its SHA-256, and writes
normalized, conversion, quality, audit, and blinded-review artifacts under
`.enoch/perseus-generated/`:

```powershell
dotnet run --project Enochian.Perseus -- acquire-normalize ..
```

Run from `source/`. Use `normalize` for an offline regeneration from the
verified raw file under `.enoch/perseus/`. Runtime matching uses only the
frozen normalized JSONL; `samples/latin-panel.json` does not invoke the
acquirer or converter.

The single named convention is Restored Classical Latin, profile
`lat-classical-restored` version 1.0.0. `c` and `g` remain velar, `v` is /w/;
vocalic and consonantal `u` and `i` are selected contextually, `qu` is /kw/,
`x` is /ks/, and Greek `ch`, `ph`, and `th` retain aspiration. Macron and breve
marks are honored; unmarked vocalic nuclei use the predeclared conservative
short-vowel rule and are counted. Stress is omitted because the shared feature
space does not encode it. Definitions and POS are retained as metadata but do
not enter conversion.

Lewis and Short editorial separators are removed before conversion. Its legacy
Cyrillic `ў` breve-u encoding is normalized to `ŭ`; ligatures and accented or
diaeresis vowel variants are normalized explicitly. At pinned commit
`40038e40937fa639639802e73dac15e6c938496b`, the deterministic baseline is
50,522 parsed records, 50,520 emitted records, 48,925 records with 88,778
assumed-short vowels, and 100 blinded review rows. Records `n10474` and
`n39823` contain bare `q` outside `qu`; both are rejected and counted rather
than assigned a guessed pronunciation. The G2P audit and command therefore
exit nonzero until those source forms receive an explicit reviewed rule.

## Turkish and Hungarian controls

`Enochian.Controls` acquires the Zemberek Turkish master dictionary and Magyar
Ispell 1.9.1 source archive from the exact revisions and URLs pinned in
`manifests/zemberek.manifest.json` and
`manifests/magyar-ispell.manifest.json`. It verifies each SHA-256 before
parsing explicit lexical stems and never expands productive morphology. Run
from `source/` with the Python 3.11 environment containing the packages pinned
in `tools/epitran/requirements.txt`:

```powershell
dotnet run --project Enochian.Controls -- acquire-normalize .. <python-path>
```

Use `normalize` for an offline rerun from the verified files under
`.enoch/controls/`. Outputs are written under `.enoch/controls-generated/`.
Each language receives its own normalized JSONL, conversion JSONL, quality
report, audit summary, and 100-row blinded review sheet. Runtime consumers use
only frozen normalized JSONL; they do not invoke Python, Epitran, or either
acquirer. `samples/turkish-hungarian-panel.json` loads both normalized controls
as separate runtime lexicons for exploratory comparison.

Both languages share the out-of-process Epitran 1.35.2 worker and retain its
output unchanged. Turkish uses `tur-Latn`; Hungarian uses `hun-Latn`. The
process boundary forces Python UTF-8 mode and BOM-free UTF-8 JSONL. The
Zemberek adapter excludes proper names, abbreviations, punctuation, and
malformed records. The Magyar adapter reads the frozen default source-module
list, excludes proper-name, place-name, abbreviation, obsolete, and correction
sources, and records source flags without applying affix rules.

At Zemberek commit `ae2fbe31438dda4dddc674a2a8991d518984d392`, the
deterministic baseline is 28,821 lemmas, 27,677 emitted records, 1,181 total
exclusions, zero generated morphology records, and 100 review rows. Epitran
passes circumflex orthography through in 1,129 records and 15 records retain a
hyphen, so 1,144 conversions fail the checked-in IPA inventory. At Magyar
Ispell tag `v1.9.1`, commit
`1ecfd0b086fecb4d02b38148bceeb00b86dd3b6e`, the baseline is 58,112 lemmas,
57,596 emitted records, 38,433 total exclusions, zero generated morphology
records, and 100 review rows. Of those exclusions, 35,339 are frozen
proper-name sources, 1,370 are correction-source records, and 516 are rejected
by the IPA audit. Both languages are therefore blocked from confirmatory use by
unknown IPA and pending blinded review; these records are counted rather than
silently rewritten.

## Modern Indo-Aryan and Indo-Iranian bridge controls

The predeclared selection and thresholds are recorded in
`docs/modern-indo-iranian-panel.md`. Hindi is the confirmatory candidate because
its pinned UniMorph dataset yields at least 100 auditable readings with Epitran
1.35.2. Bengali is exploratory because only 48 readings pass strict IPA audit.
Gujarati is retained as an exploratory source because no pinned profile exists.
Persian is the Indo-Iranian bridge and remains exploratory because ordinary
Perso-Arabic spelling omits vowels needed for auditable lexical IPA.

Acquire exact source bytes, verify their hashes, and regenerate all artifacts
from `source/`:

```powershell
dotnet run --project Enochian.Controls -- acquire-normalize .. <python-path>
```

UniMorph raw files are stored under `.enoch/unimorph/`; outputs are stored
under `.enoch/indo-iranian-generated/`. Unique lemma records use `lemma:` IDs
and are the only records passed to Epitran or written to the primary
`unimorph-<language>.jsonl`. Every valid source row is retained separately in
`unimorph-<language>.inflected-forms.jsonl` with a `form:` ID and its UniMorph
feature bundle. Script, nullable transliteration, and generated IPA are
separate fields. Each language receives a quality report and review artifact;
unsupported exploratory languages receive an empty review artifact plus an
explicit adequacy blocker rather than guessed IPA.

`samples/modern-indo-aryan-panel.json` loads only Hindi. Unknown IPA, fewer
than 100 auditable lemmas, or pending/failed blinded review blocks it from
confirmatory use under the same rules as other controls.

The pinned real-data baseline emits 149 of 258 Hindi lemmas and prepares 100
review rows; 109 outputs fail strict IPA audit. Bengali emits 48 of 136 lemmas,
below the review threshold, with 88 strict audit rejections. Gujarati reports
397 converter-blocked lemmas. Persian excludes all 273 unvocalized lemmas from
phonological output. Repeated normalization produces byte-identical primary,
inflected, conversion, audit, review, and quality artifacts.

## Optional Biblical Hebrew control

`Enochian.Bhsa` exports unique Biblical Hebrew lexemes from an authorized local
BHSA/Text-Fabric snapshot. BHSA is CC BY-NC 4.0, metadata-only, optional, and
never downloaded, bundled, published, or required by the default build and test
path. Its manifest pins BHSA `v1.8.1`, commit
`b112c161cfd21eae403d51a2733740d8743460e7`, Text-Fabric version 2021, and DOI
`10.17026/dans-z6y-skyh`.

A fresh checkout reports the optional state successfully:

```powershell
dotnet run --project Enochian.Bhsa -- status ..
```

Authorized users must obtain the source archives under their own license terms
and place them at these ignored paths:

- `.enoch/bhsa/complete.zip`: BHSA `v1.8.1` release asset, SHA-256
  `8104fae1151c926cfcfd01f7e8a30a09af8c607546f14482990833b624b73168`.
- `.enoch/bhsa/tf-2021.zip`: ETCBC phono `v2.1` TF-2021 asset, SHA-256
  `8b46294e98f54fc5b70c1892159a320da78e889555478b20a43e7bbe8a9310ab`.

Install the optional Python environment from `tools/bhsa/requirements.txt`,
then validate, extract, export, aggregate, and audit both snapshots:

```powershell
dotnet run --project Enochian.Bhsa -- export-normalize .. <python-path>
```

No command downloads either archive. The command rejects checksum mismatches
and unsafe ZIP paths, loads only local Text-Fabric data, writes occurrence JSONL
under `.enoch/bhsa/`, and writes normalized, conversion, quality, audit, and
blinded-review artifacts under `.enoch/bhsa-generated/`. Runtime consumers use
only a deliberately frozen normalized artifact; none is included by default.
`samples/biblical-hebrew-panel.json` is the corresponding runtime-only flow and
does not invoke Text-Fabric or access the source archives.

The adapter groups word occurrences by BHSA lexeme, verifies the resulting
frequency against `freq_lex`, retains BHSA rank and gloss, and emits one unique
lexeme. Distinct occurrence-level `phono` values remain separate conversion
artifacts. The normalized lexeme selects the most frequent accepted reading,
breaking ties by ordinal IPA order. Aramaic lexemes and records lacking a
vocalized form or `phono` reading are reported and excluded from the explicitly
Biblical Hebrew output.

Profile `hbo-etcbc-phono` version 1.0.0 means the ETCBC BHSA phonological
transcription from phono `v2.1`; it does not claim a complete reconstructed
historical pronunciation. Tiberian vocalization is retained as NFC source
orthography. Matres lectionis, niqqud, shewa, dagesh, begadkefat, gutturals,
and silent marks follow the pinned ETCBC algorithm. Profile 1.0.0 maps its
documented alphabet to segmental IPA, omits structural markers and stress that
upstream says is not consistently phonetic, and preserves the raw transcription
in conversion diagnostics. Any residual symbol blocks confirmatory use. Multiple
readings are auditable in conversion diagnostics. Normalized records and
quality reports say `Biblical Hebrew`; review sheets deliberately omit corpus
identity under the shared blinded-review contract.

At least 100 accepted readings are required for the deterministic stratified
review set. Pending review, insufficient samples, or unknown IPA yield a
nonzero normalization result and a machine-readable blocker rather than silent
eligibility.

Ordinary tests use selected vocalized forms from Open Scriptures Hebrew Lexicon
at commit `21c9add13bc727d3a951361778e97e3ff7afd1ce` under CC BY 4.0, plus
synthetic occurrence counts and IPA. The fixture contains no BHSA or ETCBC
phono data and is explicitly not evidence of equivalent coverage or quality.

## Commands

Run from `source/`:

```powershell
dotnet run --project Enochian.Provenance -- validate
dotnet run --project Enochian.Provenance -- attribution `
    ../resources/lexicons/manifests ../resources/lexicons/ATTRIBUTION.md
```

Validation failures use `manifest: field: message` format. The attribution
command sorts by source ID and emits no timestamp, making repeated generation
byte-stable.
