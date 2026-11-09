# Evaluation-only Voynich symbol inventory

Extraction: `voynich-evaluation-symbol-inventory-v1` (`voynich-evaluation-inventory-v1`)
Ready for the mapping decision: **true**

## Source and permission

- Release: reeds-landini-stolfi-interlinear 1.6e6, transcriber layer `H`
- Convention: basic EVA, uncapitalized
- Snapshot SHA-256: `f764f41d02b47810bdbb48946fc24b88e85746ee7fe5f229957770e7c781e319`
- Permission: LicenseRef-Stolfi-Attribution-NoAdditionalRestrictions
- Attribution: Reeds/Landini interlinear release 1.6e6 edited by Jorge Stolfi; evaluation inventory uses Takeshi Takahashi's H transcription layer.
- Archived permission statement: https://web.archive.org/web/20030911021322id_/http://www.ic.unicamp.br/~stolfi/voynich/98-12-28-interln16e6/#Permissions

## Partition boundary

- Evaluation: `f1r`, `f1v`, `f2r`, `f2v`
- Holdout, unopened: `f3r`, `f3v`, `f4r`, `f4v`

## Counts

| Locus | H-layer lines | Accepted tokens | Excluded uncertain tokens | Symbol occurrences |
|---|---:|---:|---:|---:|
| f1r | 28 | 202 | 10 | 958 |
| f1v | 10 | 90 | 0 | 392 |
| f2r | 13 | 99 | 0 | 455 |
| f2v | 8 | 61 | 0 | 288 |

Unique accepted EVA symbols: **18**.
Symbols: `a`, `c`, `d`, `e`, `f`, `h`, `i`, `k`, `l`, `m`, `n`, `o`, `p`, `q`, `r`, `s`, `t`, `y`.
Inventory SHA-256: `d6bb73fa6fe2f897a890a6568d33a83cd43a67675cbd2652d10b0408dae55ac9`.

Tokens containing `*`, `?`, extended characters, or other non-basic EVA symbols are excluded under the frozen uncertain-token rule and retained in the machine-readable exclusions file.
