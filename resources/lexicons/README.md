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
