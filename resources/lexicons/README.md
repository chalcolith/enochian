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
