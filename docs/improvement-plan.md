# Lexical Expansion and Comparative Evaluation Plan

Research checked 2026-08-05.

## Goal

The next stage of Enochian should test a predeclared claim rather than only
produce plausible-looking matches:

> Under the selected Voynich-to-phonology hypothesis, Voynich word types are
> phonologically closer to Indo-Aryan lexicons than to comparably constructed
> lexicons from unrelated language families.

This requires two related changes. First, add substantially more Indo-Aryan
data. Second, turn matching into a reproducible comparative experiment that
records scores for every language, controls dictionary size and word length,
and reports uncertainty. Definitions are useful after a match is found, but
semantic plausibility must not be the primary test because it is too easy to
select an attractive gloss after inspecting many candidates.

## Resource findings

### Indo-Aryan resources

| Resource | Data and access | License/status | Recommended use |
| --- | --- | --- | --- |
| [Cologne Digital Sanskrit Dictionaries](https://www.sanskrit-lexicon.uni-koeln.de/) (CDSL) | 43 dictionaries with downloadable XML in SLP1. Relevant works include Monier-Williams 1899 (`MW`), Apte 1890 (`AP90`), revised Apte 1957 (`AP`), the large Böhtlingk-Roth Petersburg dictionary (`PWG`), and Böhtlingk's shorter dictionary (`PW`). | The current canonical [`csl-orig`](https://github.com/sanskrit-lexicon/csl-orig) source repository identifies its data as CC BY-SA 4.0. Pin a commit and preserve CDSL citation and attribution. | Highest-priority expansion. Existing SLP1 encoding can be reused. Parse the canonical source or generated XML rather than scrape web results. |
| [CDSL `csl-orig`](https://github.com/sanskrit-lexicon/csl-orig) and [`csl-pywork`](https://github.com/sanskrit-lexicon/csl-pywork) | Canonical sources are in `v02/<dict>/<dict>.txt`; generation tooling can produce XML, SQLite, headword lists, and downloads. | Data repository: CC BY-SA 4.0. Generation tooling: GPL 3.0. | Prefer a pinned `csl-orig` snapshot. Use `csl-pywork` only as an external acquisition/build tool if generated XML is needed; do not copy GPL tooling into the library unintentionally. |
| [UniMorph](https://unimorph.github.io/) | Common tabular lemma-form-feature schema. Current catalog includes Sanskrit (33,847 forms/917 paradigms), Hindi (54,438/258), Bengali (4,443/136), Gujarati (19,404/6,995), Assamese (94,147/1,877), and other Indo-Aryan languages. Individual repositories are downloadable. | Dataset-specific; the cited language repositories generally state CC BY-SA 3.0. Verify every selected repository and pin its commit. | Add modern Indo-Aryan positive-family comparisons in phase 2. Use unique lemmas for the primary analysis and inflected forms only in a separately labelled analysis. |
| Existing RomLex and Shabda-Sagara data | Romani dialect data and an existing CDSL Sanskrit dictionary already integrated in Enochian. | Retain the provenance and use restrictions recorded for the local snapshots; confirm RomLex redistribution terms before publishing a derived bundle. | Baseline only. Preserve dialect identifiers and deduplicate identical phonological forms so dialect duplication does not inflate evidence. |

CDSL dictionaries overlap heavily and are not independent samples. Report MW,
AP/AP90, PWG/PW, and SHS separately, then also report a deduplicated Sanskrit
union. Agreement among these dictionaries must not be presented as independent
replication of a Sanskrit effect.

### Comparison resources

| Language/family | Primary resource | Phonological route | Important caveat |
| --- | --- | --- | --- |
| Hebrew, Semitic | [ETCBC BHSA](https://github.com/ETCBC/bhsa) provides Biblical Hebrew lexemes, glosses, frequencies, vocalized forms, and a companion [`phono`](https://github.com/ETCBC/phono) dataset. [Open Scriptures Hebrew Lexicon](https://github.com/openscriptures/HebrewLexicon) is a simpler XML alternative containing BDB, Strong, and an index. | Prefer BHSA's vocalized/phonological representation. As a redistributable fallback, use the vocalized UniMorph Hebrew file and a documented niqqud-to-IPA converter. | BHSA data are CC BY-NC 4.0 and require attribution to DOI `10.17026/dans-z6y-skyh`; they must remain an optional research download rather than a dependency that silently imposes non-commercial terms. Open Scriptures is CC BY 4.0 but is a work in progress. This is Biblical, not Modern, Hebrew. |
| Latin, Italic Indo-European | [PerseusDL Lexica](https://github.com/PerseusDL/lexica), especially Lewis and Short in TEI XML; UniMorph Latin supplies 509,182 forms and 17,214 paradigms. | Implement and test an explicit Classical Latin orthography-to-phoneme profile, or use a pinned CLTK phonology implementation after verifying its output. | Perseus repository content is generally CC BY-SA 4.0 but component-level notices must be checked. Do not mix Classical and ecclesiastical pronunciation in one run. |
| Turkish, Turkic | [Zemberek-NLP](https://github.com/ahmetaa/zemberek-nlp) has a UTF-8 `master-dictionary.dict`, morphology, and frequency-oriented resources. UniMorph Turkish has 275,460 forms and 3,579 paradigms. | [Epitran](https://github.com/dmort27/epitran) supports `tur-Latn`; pin its version and retain the IPA it generates. | Zemberek is Apache 2.0 and in slow maintenance mode. Exclude proper names, abbreviations, and obsolete entries in the primary run. UniMorph nouns/adjectives are sourced from unverified Wiktionary data, so use Zemberek as the primary lexicon. |
| Hungarian, Uralic | [Magyar Ispell](https://github.com/laszlonemeth/magyarispell) is an actively maintained spelling and morphological dictionary; UniMorph Hungarian documents 21,963 noun/adjective/verb lemmas and over one million inflected forms. | Epitran supports `hun-Latn`. Validate digraphs, long vowels, and geminates against a hand-reviewed sample. | Magyar Ispell is multi-licensed GPL/LGPL/MPL; select and record the applicable option before redistribution. UniMorph Hungarian is CC BY-SA 3.0 and derived from English Wiktionary. Use lemmas, not its million inflections, in the primary run. |

These controls are intentionally diverse. Latin controls for a different
Indo-European branch; Hebrew for Semitic; Turkish for Turkic; and Hungarian for
Uralic. Add Persian or Pashto as an Indo-Iranian but non-Indo-Aryan bridge
control in phase 2. This helps distinguish a specifically Indo-Aryan result from
a broad Indo-European or areal result.

### Shared phonology tooling

[Epitran](https://github.com/dmort27/epitran) is MIT-licensed and emits IPA for
Turkish, Hungarian, Hindi, Bengali, Bhojpuri, Marathi, Punjabi, Urdu, and many
other languages. Its output can be converted through Enochian's existing IPA
feature encoding. It does not currently list Hebrew or Latin, so those profiles
need separate, documented implementations. Epitran's PanPhon feature vectors
should not be mixed directly with Enochian's feature vectors; use IPA as the
interchange representation so every language passes through the same Enochian
feature set.

eSpeak NG covers more than 100 languages and can emit phoneme codes, but it is
GPL 3.0 and pronunciation quality varies by voice. Use it only as an external
sensitivity check, not as the canonical converter or a linked dependency.

## Corpus and provenance design

Create a generated, language-neutral lexicon artifact (JSON Lines or TSV) with
at least these fields:

```text
entry_id, language, family, source, source_version, lemma, form,
entry_kind, dialect, part_of_speech, definition, frequency,
source_encoding, ipa, license
```

`entry_kind` must distinguish `lemma`, `inflected`, `proper-name`, and
`abbreviation`. The loader should reject missing IDs, invalid Unicode, empty
phonology, and IPA segments unknown to the configured feature set. It should
write a quality report with source counts, unique lemma/form counts, duplicates,
excluded records, unknown graphemes/segments, and phoneme-length distributions.

Acquisition scripts should download or transform pinned upstream versions and
write a manifest containing URL, commit/tag, retrieval date, SHA-256 checksum,
license, citation, parser version, and all filters. Do not commit third-party
data until its redistribution terms have been checked. Generated data and raw
downloads should live in separate directories so a clean rebuild can be tested.

Normalize Unicode to NFC, retain the original spelling, and make every
conversion explicit:

```text
source headword -> normalized orthography/transliteration -> IPA ->
Enochian feature vectors
```

Never silently discard an unknown character. A record with incomplete
conversion is excluded and counted. Pin CDSL and UniMorph commits, Epitran/CLTK
versions, and every custom G2P profile in experiment metadata.

## Software work

### 1. Generalize lexical ingestion

1. Add a generic normalized lexicon loader rather than one C# class per source.
2. Write source adapters for CDSL, Zemberek, Magyar Ispell/UniMorph, Perseus,
   and BHSA/Open Scriptures that produce the common artifact.
3. Extend `LexiconEntry` with stable ID, language/source metadata, IPA, POS,
   frequency, and entry kind. Version the binary cache cookie when its serialized
   shape changes.
4. Allow multiple entries with the same lemma. The current cache path rebuilds
   `EntriesByLemma` using `ToDictionary`, which will fail on duplicate lemmas;
   use a one-to-many index or a stable composite key.
5. Test each parser with a small committed fixture covering Unicode,
   duplicates, malformed records, multiple pronunciations, and unknown symbols.

### 2. Make matching measurable

The current `DTWMatcher` pools lexicons, returns only the top entries, and drops
the numeric distance. Add an experiment-oriented result record containing query
ID, query phonemes, lexicon/language, candidate ID, raw DTW cost, warping-path
length, normalized cost, query/candidate lengths, rank, and configuration ID.

Use mean path cost as the initial normalized distance:

$$
d_{norm}(x,y)=\frac{d_{DTW}(x,y)}{|P(x,y)|},
$$

where $P(x,y)$ is the selected warping path. The current DTW function returns
only accumulated cost, so it must also recover path length. Preserve raw cost
for audit and compare this normalization with cost divided by
$(|x|+|y|)/2$ as a sensitivity analysis.

Run each lexicon independently. Export tidy CSV/JSON results before rendering
HTML. The report should summarize distributions and confidence intervals, while
still linking individual candidates and definitions for later interpretation.

### 3. Add an experiment runner

Add a checked-in experiment configuration that fixes:

- Voynich transcription version, selected loci, transcriber layer, tokenization,
  and whether uncertain/readability markers are included;
- Vogt/Bax mapping version and every mapping variant being tested;
- feature set, segment weights, DTW tolerance, normalization, and length bounds;
- lexicon manifests, filters, sample size, random seed, bootstrap/permutation
  counts, and output paths;
- primary hypothesis, primary statistic, planned contrasts, and correction for
  multiple comparisons.

The runner must produce the same result hashes from the same manifests, config,
and seed. Keep exploratory runs visibly separate from the confirmatory run.

## Evaluation protocol

### Stage A: validate the machinery on known languages

Before interpreting Voynich results, construct retrieval tests for every
language profile:

1. Sample held-out lexicon words.
2. Convert their known pronunciation to a degraded representation using a fixed
   deletion, merger, and feature-masking process comparable in severity to the
   proposed Voynich mapping.
3. Search a lexicon that contains the source item and report exact-form
   recall@1, recall@5, recall@20, mean reciprocal rank, and normalized distance.
4. Repeat after removing the source item to measure retrieval of morphological
   or lexical neighbors rather than identity.

This extends the existing CMU demonstration and reveals whether one language's
G2P or phoneme inventory makes it artificially easy or hard to match. Hand-check
at least 100 G2P outputs per language, stratified by word length and unusual
graphemes. Set an acceptable conversion-error threshold before the Voynich run.

### Stage B: construct comparable lexicons

Use unique lemmas as the primary unit. Apply the same POS and entry-kind policy
to every language, exclude entries outside a predeclared phoneme-length range,
and deduplicate identical phoneme sequences within each language.

Nearest-neighbor scores improve automatically as a lexicon gets larger. For the
primary comparison, repeatedly draw the same number of entries from each
language without replacement, stratified by phoneme length (and frequency bands
where comparable frequencies exist). Use the largest sample size supported by
all primary lexicons after filtering, with a predeclared minimum; repeat at
smaller common sizes as a robustness check. Run the full lexicons only as a
secondary analysis.

Do not pool all inflected UniMorph forms with headword dictionaries. Analyze
inflected forms separately, with equal paradigm/form sampling across languages.

### Stage C: define nulls and scores

Analyze unique Voynich word types first so repeated manuscript tokens do not
create false precision. Provide a secondary token-frequency-weighted result.
Stratify or model query phoneme length.

For each query and language, retain the nearest normalized distance. Calibrate
it against pseudowords generated from that language's phoneme unigram and
biphone distributions with the same query-length distribution. Report an
empirical percentile or standardized score in addition to raw distance. Also
run two negative controls:

- shuffle the Voynich character-to-phone assignments while preserving the
  number of characters mapped to each phone class;
- shuffle the order of phonemes within each query while preserving its phoneme
  inventory and length.

These nulls ask whether the proposed mapping captures sequence information,
rather than merely favoring a compatible inventory or short words.

### Stage D: compare language families

Predeclare the primary statistic as the paired difference in median calibrated
nearest-neighbor score between the deduplicated Indo-Aryan group and the pooled
unrelated controls. Also report every language separately; a pooled family score
must not conceal one unusually permissive lexicon.

Use a paired permutation test over Voynich word types, bootstrap word types and
lexicon subsamples for 95% confidence intervals, and report an effect size as
well as a p-value. Apply Holm correction to the predeclared pairwise contrasts.
If many mapping variants are tried, either reserve a held-out set of manuscript
folios for confirmation or include mapping choice in the multiplicity
correction. Never select a mapping on all folios and report its score on those
same folios as confirmatory evidence.

Report at minimum:

- median and distribution of raw and calibrated nearest-neighbor distance;
- paired family/language differences with 95% intervals and corrected p-values;
- proportion of query types whose best language is each candidate language;
- results by query length, manuscript section, and frequent versus rare types;
- stability across lexicon subsamples, G2P alternatives, DTW normalization,
  feature weights, and mapping variants;
- Stage A retrieval accuracy and all data-quality exclusion counts.

### Stage E: semantic follow-up

Only after phonological scoring is frozen, reveal definitions for the top
matches. Have at least two annotators, blind to source language and score, label
whether a gloss fits a predeclared manuscript context. Measure inter-annotator
agreement and compare acceptance rates across languages. Treat this as secondary
evidence: illustrated pages, broad dictionary senses, and post-hoc gloss choice
create substantial researcher degrees of freedom.

## Delivery sequence

The implementation is divided into reviewable pull requests in the
[milestone PR specifications](milestone-pr-specs.md). That document is the
execution checklist; the milestones below remain the research-level exit
criteria.

### Milestone 0: protocol and manifests

- Freeze a representative Voynich evaluation set and a held-out confirmation
  set.
- Write the experiment schema, source manifest schema, inclusion rules, primary
  contrast, and randomization plan.
- Confirm redistribution terms for every selected snapshot.

**Exit criterion:** a fresh checkout can validate all manifests, and the primary
analysis choices are recorded before looking at comparative results.

### Milestone 1: Sanskrit expansion

- Import pinned MW, AP, PWG, and PW sources from CDSL through one tested adapter.
- Re-import SHS through the same normalized artifact where practical.
- Produce per-dictionary and deduplicated-union quality reports.

**Exit criterion:** fixture tests pass; unknown SLP1 symbols and exclusions are
zero or explicitly reviewed; duplicate handling is deterministic.

### Milestone 2: control panel

- Add Latin, Turkish, Hungarian, and Biblical Hebrew lemma lexicons.
- Implement/pin IPA conversion profiles and complete blinded G2P review.
- Add Persian or Pashto as the bridge control and selected modern Indo-Aryan
  UniMorph lemma sets as positive-family breadth.

**Exit criterion:** every profile meets the predeclared G2P quality threshold and
passes the Stage A retrieval benchmark at all reported length bands.

### Milestone 3: scored comparative runner

- Return raw and normalized DTW scores per candidate and language.
- Implement balanced subsampling, pseudoword/null generation, fixed seeds,
  tidy exports, confidence intervals, permutation tests, and Holm correction.
- Add unit tests for DTW path normalization and statistical calculations, plus
  an end-to-end fixture with a deliberately detectable language signal.

**Exit criterion:** the synthetic fixture recovers the planted language, null
fixtures do not, and a repeated run is byte-identical apart from timestamps.

### Milestone 4: confirmatory run and report

- Run the frozen protocol on the evaluation set without inspecting definitions.
- Publish complete configs, manifests, software commit, quality reports, tidy
  results, plots, and null/sensitivity analyses.
- Run semantic annotation, then evaluate the untouched folio holdout once.

**Exit criterion:** another researcher can reproduce every reported number from
the pinned sources, or can obtain the restricted BHSA input by following a
documented optional-data step.

## Decision rule

The theory gains quantitative support only if the predeclared Indo-Aryan
contrast has a meaningful, corrected effect in the expected direction, its
confidence interval excludes zero, it survives matched lexicon subsampling and
mapping/null controls, and the effect replicates on held-out folios. A few
attractive Sanskrit or Romani glosses, an uncorrected best score among many
mappings, or an effect confined to one dictionary is not sufficient.

Failure to separate Indo-Aryan from controls is also informative: report it
directly, together with the power and Stage A validation results, rather than
continuing to add lexicons or mapping variants until a favorable comparison
appears.

## References and access notes

- [Cologne Digital Sanskrit Dictionaries](https://www.sanskrit-lexicon.uni-koeln.de/), dictionary catalog, downloads, citation, and credits.
- [CDSL canonical source data](https://github.com/sanskrit-lexicon/csl-orig) and [generation pipeline](https://github.com/sanskrit-lexicon/csl-pywork).
- [UniMorph catalog and schema](https://unimorph.github.io/), including [Turkish](https://github.com/unimorph/tur), [Hungarian](https://github.com/unimorph/hun), and [Hebrew](https://github.com/unimorph/heb) source notes.
- [ETCBC BHSA](https://github.com/ETCBC/bhsa) documentation, provenance, Text-Fabric access, and CC BY-NC terms.
- [Open Scriptures Hebrew Lexicon](https://github.com/openscriptures/HebrewLexicon), XML contents and CC BY 4.0 terms.
- [PerseusDL Lexica](https://github.com/PerseusDL/lexica), TEI lexica and repository reuse notice.
- [Zemberek-NLP](https://github.com/ahmetaa/zemberek-nlp), Turkish morphology dictionaries and Apache 2.0 license.
- [Magyar Ispell](https://github.com/laszlonemeth/magyarispell), Hungarian spelling/morphology sources and licenses.
- Mortensen, Dalmia, and Littell (2018), [Epitran: Precision G2P for Many Languages](https://aclanthology.org/L18-1266/), and the [Epitran implementation](https://github.com/dmort27/epitran).
- [CLTK](https://github.com/cltk/cltk), pre-modern-language NLP tooling under the MIT license.
- [eSpeak NG](https://github.com/espeak-ng/espeak-ng), multilingual phoneme generation under GPL 3.0 or later.

All source versions, counts, and license statements must be rechecked when the
data are actually acquired; upstream catalogs and repository terms can change.
