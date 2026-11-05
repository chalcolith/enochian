# Dictionary-first protocol amendment (M2-09)

This is the frozen replacement design in [confirmatory.v2.json](confirmatory.v2.json).
It supersedes human annotation gates **only for v2**. The [v1 protocol](protocol.md),
schemas, review validators, examples, and historical outputs remain unchanged.
No comparative manuscript results were inspected to select this design.

## Status and commands

The [admission table](../reports/automated-admission/v2/coverage.md) and its
[machine-readable report](../reports/automated-admission/v2/coverage.json) cover
all 17 profiles. Every real profile currently has zero supported-origin lexical
units. Arabic and Bhojpuri are excluded; the other profiles are exploratory.
These are evidence exclusions, not measured null effects. Retrieval is **not
run** for unsupported profiles. M4-01 remains blocked.

Run from `source/`, after retaining the pinned M2-08 profile artifacts:

```powershell
dotnet run --project Enochian.Benchmark -- admit ..\experiments\confirmatory.v2.json
dotnet run --project Enochian.Benchmark -- preflight ..\experiments\confirmatory.v2.json
dotnet run --project Enochian.Benchmark -- experiment ..\experiments\confirmatory.v2.json
```

`admit` writes JSON/Markdown source decisions without manuscript scoring.
`preflight` additionally checks queries, mapping, partitions, and astronomical
registration. Either returns exit code 1 when required gates fail.
The benchmark's default command also dispatches v2 to admission. The combined
v2 experiment owns sampling, matching, statistics, and report inputs; its
configuration is not accepted by standalone v1 `sample`/`statistics` commands.

The real configuration deliberately leaves `queries` and `mapping` null.
M4-01 must materialize and pin definitions-free, locus-bearing query types and
one predeclared mapping in a new protocol amendment; this implementation does
not invent manuscript data or silently choose a mapping. Evaluation retains
`f1r`, `f1v`, `f2r`, `f2v`; the unopened holdout retains `f3r`, `f3v`, `f4r`,
`f4v`. Each query has `query_id`, `locus`, `symbols`, and positive
`token_frequency`. Duplicate IDs or symbol sequences, holdout/out-of-partition
loci, missing loci, unmapped symbols, and extra fields including definitions
are rejected. Mapping vectors must match the pinned feature dimensions and
allowed feature values.

## Source and pronunciation admission

- Freeze the complete M2-08 panel, profile versions/fingerprints, generated
  manifests, normalized candidates, evidence, upstream manifests, licenses,
  feature set, IPA encoding, and M2-07 evidence policy.
- Require acquired, revision/checksum-pinned sources with verified,
  unrestricted licenses. Optional/non-commercial BHSA is not silently admitted.
- Match language, convention, positive/negative source labels, snapshot,
  source URL, lexical identity, normalization/alternative-policy versions,
  IPA/phone length, and one-to-one normalized-entry/evidence IDs.
- Directly admit only `dictionary-supplied` origins. Unknown origins remain
  exploratory even if the spelling or IPA looks plausible.
- Generated/template readings additionally require a nonempty generator and
  revision and a profile/revision-bound independent reference envelope. Require
  at least 100 held-out lexical references, at least 25 in each length band,
  and at least 95% normalized exact agreement under the unchanged M2-07 policy.
  Exact agreement and phone edit distance are **not human accuracy**.
- Reference source manifests and dependency roots must be pinned. Reject
  shared source IDs, hashes, URLs, overlapping/unknown dependencies, references
  not bound to actual generated outputs, tuning examples, and unvalidated
  reference origins. Renaming a shared-source reference does not establish
  independence.
- Retain homograph identities. Alternatives receive the frozen equal-reading
  weights; all alternatives of a lexical unit must have supported evidence.
  Deduplicate complete weighted **phone-vector distributions**, preserving
  original source memberships. Orthographic IPA aliases do not inflate size.

### Separate retrieval gate

After pronunciation and balanced-capacity gates pass, sample 25 lexical queries
per length band and evaluate **all four unchanged frozen degradation profiles**.
Every required source-included cell must meet Recall@1 >= 0.80, Recall@5 >= 0.95,
Recall@20 >= 0.99, MRR >= 0.85, and mean normalized DTW distance <= 0.35.
Source-excluded results remain separate diagnostics, not substitutes for these
cells. Rankings use expected candidate distance over pronunciation weights;
query-reading metrics are also weighted rather than selecting a favorable
reading. Ties resolve by stable unit ID. The existing DTW/degradation semantics
and numerical thresholds are not weakened.

Self-retrieval establishes robustness under these degradations, not correct
pronunciation. A passing retrieval cell cannot rescue unknown derivation,
wrong conventions, missing independent references, or an ineligible license.

## Balanced design and inference

Use 100 unique supported lexical units per admitted language, with fixed quotas
**34 / 33 / 33** for **03-05 / 06-09 / 10-20** phones. The lexicographically
first normalized IPA determines the unit's band, as in M2-07. Draw 20
deterministic lexicon samples; do not reduce size/quotas after exclusions.
Hindi, Sanskrit, Turkish, and Hungarian are required anchors. Other profiles
can enter only through the same predeclared gates. Unspecified-dialect Romani
is permanently exploratory in this version. Hebrew is descriptive-only.

| Contrast | Target | Comparison | Status |
| --- | --- | --- | --- |
| Indo-Aryan vs unrelated controls | Hindi, Sanskrit, Assamese, Bengali, Bhojpuri, Gujarati, Marathi, Punjabi, eligible Romani | Turkish, Hungarian | Required primary |
| Indo-Aryan vs Latin/Greek | Same Indo-Aryan group | Italianate ecclesiastical Latin, 4th-century CE Koine Greek | Optional secondary |
| Iranian bridge vs unrelated controls | Persian, Pashto | Turkish, Hungarian | Optional secondary |
| Arabic vs Indo-Aryan | Classical Arabic citation forms | Indo-Aryan group | Optional separate secondary |

Preserve actual genealogical family metadata; group membership does not
relabel families. Latin/Greek and Iranian languages are not unrelated-family
controls. Multiple Indo-Aryan or Semitic languages are not independent family
replications. Report actual admitted/excluded members alongside every claim.

For each query and language, take the nearest lexical unit's **expected
normalized DTW distance** across its weighted pronunciation alternatives.
Generate 100 repetitions of each existing null: mapping-assignment shuffle,
within-query shuffle, unigram pseudowords, and biphone pseudowords. For each
null repetition, deterministically draw one reading per lexical unit according
to its weights when fitting the phone model; never silently replace a mixture
with its first reading. Observed and null matching still score complete
weighted distributions.

The frozen test metric is the biphone-null empirical percentile (lower is
closer); ties use midranks. This remains defined for zero-variance nulls.
Within each sample/query, use the median across admitted target languages and
the median across admitted comparison languages. Collapse lexicon repetitions
by query-type medians before the one-sided paired permutation (`less`, 10,000
permutations). Use a hierarchical bootstrap over lexicon samples and query
types (2,000 replicates, 95% interval). Languages are not statistical replicates.

Run type-primary and token-frequency-weighted sensitivity modes. **Holm correct
the single fixed family of four contrasts x two modes = eight slots**, at
alpha 0.05. Reserve slots for unavailable optional contrasts; report
`not_estimable` rather than shrinking the family or inventing a p-value.
Required unestimable contrasts fail execution. Only the type-primary result
of the required Indo-Aryan contrast is primary; token weighting is sensitivity.
Seeds are separately frozen for sampling, null generation, and permutation.

## Separate astronomical appendix

Keep the pinned HYG 4.1 source, license, M2-08 registry, and exact-link/name
selection rules: all nonempty `proper` names except Solar ID 0, retained
catalog/companion IDs, trim/NFC/case-sensitive keys, explicit dictionary
translation links only, all alternatives retained, no transliteration or
score/gloss-selected links. Require each language's frozen pronunciation
profile. Catalog spellings do not establish Arabic etymology or Arabic IPA.

For each Arabic-versus-other-panel-language pair, use common catalog objects
only, with at least **25 distinct shared IDs**. Include the common set in
stable seeded-hash order, with equal object weights in both languages and
equal weighting of an object's retained name alternatives. No unmatched object
is a zero score. Missing/ambiguous links and insufficient coverage are reported.
HYG constellation identifiers are descriptive external catalog context only;
no manuscript semantic labels or proposed Voynich star identifications are
ground truth.

Predeclare catalog-name-to-object label permutations within each language and
the three pronunciation-length bands, plus within-query phone shuffles.
Catalog contexts are not inferred or used to select favorable subsets.
Use the frozen null/permutation seeds and counts above. Reserve a **separate
Holm family of 16 Arabic-versus-other-language comparisons**, including slots
for unavailable pairs. All astronomical results are exploratory; they cannot
rescue the primary claim. The runner reports `registered_not_evaluated`:
multilingual catalog links and astronomical scoring are not implemented or
fabricated by M2-09, and this is not a hidden prerequisite for primary scoring.

## Compatibility, reproducibility, and migration

The [combined protocol schema](schemas/automated-experiment.v2.schema.json),
[admission schema](schemas/automated-admission.v2.schema.json), and
[reference envelope schema](../resources/lexicons/schemas/automated-reference-set.v2.schema.json)
are new contracts. They consume M2-08 normalized/evidence artifacts without
changing those v1 interchange formats. Do not copy automated decisions into
old review sheets or label agreement as human judgment.

All v2 paths are repository-relative and must remain inside the repository.
Configuration pins hash canonical UTF-8/LF text; generated JSONL/source data and
M2-08 embedded artifact/source-manifest pins verify exact bytes. Retain the
original acquired artifacts: reserializing a pinned manifest or changing
checkout line endings may require a new explicit artifact/protocol amendment,
not an automatic pin update. The synthetic fixture has targeted LF attributes.
Never overwrite frozen inputs with report/output paths.

Runner `automated-experiment-v2` binds protocol, panel/profile/evidence,
references, upstream licenses, schemas, degradations, phonology, queries,
mapping, runtime identifier/architecture, .NET version, and loaded application
directory DLL bytes into the run identity. `@runtime/` input names are logical
software identities, not repository data paths. A stage resumes only if that
identity **and every output byte hash** match; changed evidence/profile pins
force fresh stages. V1/other-runner checkpoints in the output directory are
rejected. Sampling/matching/calibration/statistics checkpoints remain separate.
Raw matching rows stream to disk; calibration retains only one lexicon sample's
observations and biphone-null distances at a time, rather than loading every
null-score record into memory. The compact calibration export retains null
counts, percentiles, means, standard deviations, and diagnostic statuses.

To migrate, retain old artifacts/outputs, rebuild or retain M2-08 artifacts,
run v2 admission, and resolve exclusions through better licensed,
independently traceable machine evidence. Then prepare the explicit
query/mapping amendment and run preflight in a fresh v2 output directory.
`register-v2 <repository-root>` creates the initial registration only when
absent; it refuses to overwrite an existing frozen file. Changing source pins
or research choices requires an explicit new amendment, never post-result
threshold tuning.

### Offline synthetic smoke

```powershell
dotnet run --project Enochian.Benchmark -- experiment ..\experiments\automated-smoke\run.json
```

The checked-in fixture has 100 distinct units per artificial language and
passes all 24 required retrieval cells without human files. Its one-hot
artificial encoding intentionally has no height/laryngeal features; repeated
phone runs survive deletion. It is a plumbing test, **not pronunciation
evidence or a realistic difficulty estimate**. Changing its phase flag cannot
promote this encoding to the confirmatory real-language protocol. To regenerate
the fixture deterministically:

```powershell
dotnet run --project Enochian.Benchmark -- synthetic-v2 experiments\automated-smoke <repository-root>
```
