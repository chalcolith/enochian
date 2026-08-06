# Frozen Research Protocol

This document defines the choices represented by version 1 of the experiment,
source-manifest, and normalized-entry schemas. It freezes analysis choices
before language-comparison output is produced. The checked-in examples contain
configuration and synthetic provenance only; they contain no comparison
results.

## Versioning

Every instance has a `schema_version` in semantic-version form. Version 1
validators accept `1.x.y` and reject other major versions. A major version
changes field meaning or compatibility. Minor and patch revisions may add
backward-compatible clarification, but released schema IDs remain stable.

## Experiment Fields

- `experiment_id`, `phase`, and `frozen` identify the analysis. A confirmatory
  protocol must be frozen; an exploratory configuration may remain editable.
- `corpus_split` names evaluation and holdout loci. Loci must be unique within
  each partition and disjoint across partitions. Selection uses manuscript
  metadata and folio labels only, never comparative language scores.
- `transcription`, `mapping`, `tokenization`, and `feature_set` pin every text
  conversion. Text is normalized to NFC. The confirmatory protocol uses one
  predeclared mapping; exploratory work may use a declared sensitivity set.
- `dtw` fixes the local distance, path normalization, optional window, and
  mapping-selection policy. `null` window means unconstrained DTW.
- `length_filters` count phoneme segments after mapping and before matching.
- `lexicon_filters` fixes entry kinds, parts of speech, duplicate handling, and
  whether proper names, abbreviations, or inflected forms are eligible. An
  empty part-of-speech list means no POS restriction.
- `randomization.seeds` provides separate non-negative seeds for balanced
  sampling, null generation, and permutation. `sample_count` is the number of
  sampled entries per comparison group; `permutation_count` is the number of
  label permutations.
- `planned_contrasts` declares exactly one primary contrast and any sensitivity
  contrasts. `lower` means the target group is expected to have lower normalized
  DTW distance than its controls.
- `statistics` freezes the primary statistic, family-wise correction, alpha,
  and confidence level. The primary statistic is target-group mean normalized
  DTW minus control-group mean normalized DTW. The expected primary effect is
  therefore negative. Holm correction is applied across all reported planned
  contrasts.
- `outputs` contains repository-relative destinations. Output artifacts must
  record the experiment, schema, source, and software hashes when implemented.

## Corpus Inclusion and Exclusion

- Exclude Voynich tokens containing uncertain transcription markers, damaged
  or unreadable glyphs, editorial alternatives, or unresolved locus metadata.
  Do not choose between alternatives after inspecting a language score.
- Include mapped tokens with 3 through 20 phonemes, inclusive. Count length
  after NFC normalization, tokenization, and the pinned mapping.
- Exclude proper names and abbreviations from the primary analysis.
- Include lemma records only in the primary analysis. Exclude inflected forms;
  they may appear only in a separately declared sensitivity contrast.
- Apply no part-of-speech restriction in the primary analysis. Any future POS
  restriction requires a new frozen protocol version.
- Deduplicate identical normalized forms within each source. Preserve source
  membership across sources; balanced source sampling prevents repeated forms
  from creating source-size evidence.
- Exclude records with missing IDs, empty IPA, invalid Unicode, unknown IPA
  segments, or incomplete conversion. Count and report every exclusion rather
  than silently dropping it.

## Source Manifest Fields

- `source_id`, `language`, and `family` identify the source and its comparison
  grouping. `language` is a constrained BCP 47 language tag.
- `url` and `revision` pin the upstream location and commit, tag, or release.
- `retrieval_date` records the UTC calendar date of acquisition. `sha256` is
  the lowercase SHA-256 digest of the unmodified raw input bytes.
- `license` is an SPDX expression or identifier. `citation` is the complete
  attribution text required in generated reports.
- `parser` pins the adapter ID and version. `raw_path` and
  `generated_artifact_path` separate acquired bytes from generated records.
- `filters.include` and `filters.exclude` state every acquisition-time record
  filter in human-readable, deterministic terms.

## Normalized Entry Fields

The normalized entry contains all fields listed in the corpus design. Nullable
metadata fields are explicit JSON `null`, not omitted. `lemma`, `original_form`,
`form`, and `ipa` are non-empty NFC strings. `form` is normalized orthography or
transliteration; `original_form` preserves source spelling. `entry_kind` is one
of `lemma`, `inflected`, `proper-name`, or `abbreviation`. `frequency` is a
non-negative source frequency or `null` when unavailable.

Construct `entry_id` as
`source:language:percent-encoded-source-record-id`, using lowercase source and
language IDs and RFC 3986 percent encoding for the final component. This keeps
IDs deterministic without deriving identity from mutable definitions or IPA.

## Confirmatory Decision Rule

The primary contrast is Indo-Aryan versus the predeclared non-Indo-Aryan
controls. Support requires a negative mean normalized-DTW difference whose
confidence interval excludes zero after Holm correction, robustness to balanced
lexicon subsampling and declared mapping/null controls, and replication on the
untouched holdout loci. Attractive individual glosses, uncorrected rankings, or
an effect confined to one source do not satisfy the rule. The holdout is opened
once, only after evaluation choices and exclusions are finalized.
