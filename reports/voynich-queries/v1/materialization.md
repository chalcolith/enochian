# Evaluation-only Voynich query materialization

Materialization: `voynich-evaluation-queries-v1` (`voynich-evaluation-queries-v1`)
Materializer assembly SHA-256: `b0ead2ddf1b04cc71ca0f3ae5e5940fb22e930c99244ab8e10ae928cc155b0fb`
Ready for preflight: **true**
Approved mapping SHA-256: `4bc43e1e9d0fd2f29a6f4e8de804fe460f367f75bc3f796af5e7e48a7ecfb16e`

## Frozen aggregation

- Type identity: `exact-normalized-symbol-sequence`
- Frequency: `sum-accepted-token-occurrences-across-evaluation-loci`
- Representative locus: `maximize-source-frequency-then-frozen-locus-order-subject-to-all-loci-represented`
- Representative loci maximize retained source-locus frequency under the constraint that every evaluation locus is represented; ties use frozen locus order.

## Partition and counts

- Evaluation: `f1r`, `f1v`, `f2r`, `f2v`
- Holdout, unopened: `f3r`, `f3v`, `f4r`, `f4v`
- Accepted token occurrences: **452**
- Unique query types: **284**
- Query artifact SHA-256: `b48f63126ba788b98d78878b3b248bd518564873dc18504e35718242a6c2089a`

| Locus | Accepted occurrences | Source types | Representative types |
|---|---:|---:|---:|
| f1r | 202 | 155 | 144 |
| f1v | 90 | 65 | 53 |
| f2r | 99 | 80 | 62 |
| f2v | 61 | 47 | 25 |

The JSON audit records every query type's source-locus memberships and reconciled frequencies. The scoring artifact contains no definitions or semantic annotations.
