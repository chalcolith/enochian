# Lexical Expansion and Comparative Evaluation Plan

Initial resource research checked 2026-08-05. Automation-first plan revised
2026-11-05; new resource coverage and licensing remain to be measured.

## Current direction and implementation status

The project must be runnable by one researcher without recruiting annotators.
Prefer existing online pronunciation dictionaries over generating pronunciation
from spelling. Automate acquisition, normalization, evidence checks, retrieval
benchmarks, exclusions, and reporting. Human pronunciation review and semantic
annotation are not prerequisites.

M0 through M3 implementation is merged, but the existing converters, review
sheets, schemas, and frozen configurations implement the earlier plan. Complete
M2-07 through M2-09 in the [PR specifications](milestone-pr-specs.md) before
M4-01. This revision changes the research design, not the current executable
behavior. Preserve old protocols and artifacts; version and freeze replacements
before inspecting comparative Voynich results. Do not mark pending human review
rows as accepted or reinterpret automated checks as human judgments.

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
| Biblical Hebrew, Semitic | Convention-labelled pronunciation dictionaries where available; [ETCBC BHSA](https://github.com/ETCBC/bhsa)/[`phono`](https://github.com/ETCBC/phono) and [Open Scriptures Hebrew Lexicon](https://github.com/openscriptures/HebrewLexicon) supply candidate lexical/vocalization data. | Use **Tiberian** pronunciation. Prefer explicitly Tiberian dictionary IPA; otherwise implement a pinned, documented Tiberian conversion from sufficiently vocalized forms and validate against independent references. | Neither niqqud nor a generic Hebrew IPA field establishes Tiberian pronunciation. Do not substitute Modern Israeli or Sephardi readings. BHSA remains optional, non-commercial, metadata-only, and never bundled by default; cite DOI `10.17026/dans-z6y-skyh`. |
| Latin, Italic Indo-European | Pronunciation-labelled Wiktionary data via Kaikki/Wiktextract; [PerseusDL Lexica](https://github.com/PerseusDL/lexica) supplies Lewis and Short lemmas and definitions where needed. | Use **ecclesiastical Latin**, with an explicitly named Italianate convention. Prefer matching dictionary IPA and use a versioned converter only for gaps. | This is the selected research assumption, not a claim that all late-medieval Latin used one pronunciation. Reject Classical-only IPA from this profile; do not silently relabel the existing Classical converter. Check component-level Perseus licenses. |
| Classical Arabic, Semitic | Audit Wiktionary/Kaikki pronunciation and vocalized lexical entries first; seek additional licensed pronunciation dictionaries if coverage is inadequate. | Use explicitly Classical Arabic readings or a pinned conversion from fully vocalized Classical forms, including a frozen case-ending, pause, and article-assimilation policy. | Modern Standard Arabic or dialect IPA is not automatically Classical Arabic. Do not guess missing short vowels. Include Arabic as a full lexical test language, with a separate astronomical-name analysis below. |
| Ancient/Classical Greek, Hellenic Indo-European | Audit Ancient Greek Wiktionary/Kaikki entries with dated pronunciation labels; use licensed historical lexica to extend lexical metadata where useful. | Use **Koine pronunciation only** for this lexicon. Freeze one dated/regional Koine profile and source-label mapping before acquisition is finalized. | Koine changed over time. Do not pool Classical Attic, different Koine stages, Byzantine, or Modern Greek IPA. A Classical Attic pronunciation arm is not requested. |
| Turkish, Turkic | Prefer dictionary IPA matched to [Zemberek-NLP](https://github.com/ahmetaa/zemberek-nlp) lexical stems, or a sufficiently covered pronunciation-bearing lemma dictionary. | Use dictionary pronunciations first; pinned Epitran `tur-Latn` is a gap-filling candidate, not an independent reference. | Zemberek is Apache 2.0. Exclude proper names, abbreviations, and obsolete entries in the primary run. Measure coverage and join ambiguity instead of assuming every lemma has pronunciation data. |
| Hungarian, Uralic | Prefer dictionary IPA matched to [Magyar Ispell](https://github.com/laszlonemeth/magyarispell) or UniMorph lemmas, or a pronunciation-bearing lemma dictionary. | Use dictionary pronunciations first; pinned Epitran `hun-Latn` may fill validated gaps. | Record the selected Magyar Ispell license option. UniMorph Hungarian is CC BY-SA 3.0. Do not expand morphology to inflate coverage; test digraphs, long vowels, and geminates automatically. |

These comparisons are intentionally diverse. Latin and Greek represent other
Indo-European branches; Hebrew and Arabic represent Semitic; Turkish represents
Turkic; and Hungarian represents Uralic. Arabic is also a separately reported
historical candidate, not merely a pooled control. Retain Persian or Pashto as
an Indo-Iranian but non-Indo-Aryan bridge. Freeze group membership and additional
contrasts before evaluation, and include all confirmatory contrasts in the
multiplicity policy. Do not count Hebrew/Arabic or Latin/Greek as independent
family replications.

### Pronunciation dictionaries first

Start with [Kaikki raw Wiktextract downloads](https://kaikki.org/dictionary/rawdata.html),
which expose Wiktionary pronunciation data in structured JSONL. Prefer the raw
extraction over the deprecated postprocessed downloads. Pin the dump date,
extraction/tool revision, raw checksum, and attribution/license terms. Entry
counts do not establish IPA coverage; measure usable lemma coverage separately
for every requested pronunciation convention.

[IPA Dictionary](https://github.com/open-dict-data/ipa-dict) is another candidate,
including Persian and Modern Standard Arabic. Its Arabic data are not evidence
for Classical Arabic without additional provenance, and its manual/semi-automatic
generation must be recorded. Audit each selected component's license and
derivation rather than assuming independent hand-transcribed reference data.

For every language, including Sanskrit and modern Indo-Aryan sources, prefer
usable dictionary IPA, then sufficiently specified source phonology, then a
versioned deterministic converter. Join by normalized lemma, lexical identity,
and pronunciation convention, never by an attractive gloss or Voynich score.
Keep homographs and pronunciation alternatives identifiable. Unsupported,
ambiguous, or unvocalized entries are excluded and counted rather than guessed.
Automated exclusions or exploratory status replace a requirement to find human
annotators.

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

Record pronunciation provenance in a versioned per-entry artifact joined by
stable ID: source entry URL/ID, source snapshot, original IPA, normalized IPA,
convention, lexical join decision, and whether the reading is dictionary-supplied,
template-generated, or converter-generated. Preserve the generating module or
provider revision when available; mark unknown derivation explicitly. Dictionary
publication does not by itself establish independent pronunciation evidence.

Never silently discard an unknown character. A record with incomplete
conversion is excluded and counted. Pin CDSL and UniMorph commits, Epitran/CLTK
versions, and every custom G2P profile in experiment metadata.

## Software work

### 1. Generalize lexical ingestion

1. Add a generic normalized lexicon loader rather than one C# class per source.
2. Extend source adapters for CDSL, Zemberek, Magyar Ispell/UniMorph, Perseus,
   and BHSA/Open Scriptures with pronunciation-dictionary ingestion and joins.
   Add Classical Arabic and Ancient Greek/Koine adapters to the common artifact.
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
phoneme inventory makes it artificially easy or hard to match. It tests
retrieval, not whether the input pronunciation is historically correct.

Replace the 100-item human-review gate with machine-readable evidence:

- Report total eligible lemmas, dictionary IPA coverage, convention-matched
  coverage, joins, ambiguities, exclusions, unknown symbols, evidence origin,
  and counts by phoneme-length band. Publish both numerator and denominator.
- Direct dictionary IPA requires a traceable snapshot, compatible convention,
  deterministic normalization, and zero unknown segments in admitted entries.
  Template-generated or unknown-origin IPA remains labelled as such; it does not
  acquire independent-reference status by being published in a dictionary.
- Converter gap-filling requires at least 100 overlapping reference lemmas,
  deterministically stratified by length and unusual graphemes, and at least
  95% exact agreement after a frozen IPA normalization. Report phone edit
  distance as a diagnostic. These are agreement statistics, not human accuracy.
  Hold reference lemmas out of converter tuning and include disagreement counts.
- References generated by the same converter, shared Wiktionary module, copied
  dictionary, or common upstream source are not independent. If independence
  is unknown, record it as unknown rather than counting a second website as
  corroboration. Unvalidated generated readings remain exploratory.
- M2-07 must measure coverage and freeze minimum eligible lexicon sizes,
  reference evidence policy, join ambiguity policy, and length-band coverage
  before any Voynich comparison. Missing data or failed gates produce explicit
  exclusion/exploratory decisions, not a human-annotation task or relaxed cutoff.
- Retain the frozen retrieval thresholds in every required length band and
  degradation profile. M2-09 must implement/version the replacement evidence
  gate; never manufacture accepted human-review sheets to satisfy old code.

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

### Stage E: optional automated contextual follow-up

Human semantic annotation is removed from the required workflow. The primary
result and holdout replication depend on phonological evidence only. A
reproducible post-scoring appendix may join glosses and existing dictionary
domain labels, but must not claim independently validated semantic fits.

For the Arabic star-name motivation, predeclare a separate astronomical
vocabulary/name set across comparison languages, including proper names only in
that secondary analysis. Pin a licensed external catalog and deterministic
selection/join rules before viewing matches. Do not select only attractive
Arabic names or visually identified Voynich words. Freeze manuscript contexts
from external metadata, or omit contextual scoring if suitable metadata do not
exist. Keep names out of the primary lemma-only experiment. Control vocabulary
size, compare languages and nulls, and report unmatched or ambiguous names.

Do not substitute LLM-generated correctness labels for human ground truth.
Any automated semantic scores require a separately frozen method and are
exploratory, not an admission gate, mapping-selection tool, or prerequisite to
the holdout. The appendix may be omitted entirely.

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

- Add ecclesiastical Latin, Turkish, Hungarian, Tiberian Biblical Hebrew,
  Classical Arabic, and Ancient/Classical Greek with Koine pronunciation.
- Prefer online dictionary pronunciation; implement/pin converters only for
  gaps and complete automated evidence and coverage checks.
- Add Persian or Pashto as the bridge control and selected modern Indo-Aryan
  UniMorph lemma sets as positive-family breadth.

**Exit criterion:** each admitted profile meets the newly frozen automated
evidence/coverage policy and passes Stage A in all required length bands.
Unavailable profiles are explicitly exploratory/excluded; no human labels are
needed. Coverage limitations and source dependencies remain visible.

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
- Evaluate the untouched folio holdout once without waiting for semantic
  annotation. Optionally publish the automated contextual appendix.

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
- [Kaikki raw Wiktextract data](https://kaikki.org/dictionary/rawdata.html),
  downloadable pronunciation-bearing Wiktionary extractions; verify each
  selected snapshot's license, coverage, and generating templates.
- [IPA Dictionary](https://github.com/open-dict-data/ipa-dict), word-to-IPA
  lists with mixed manual/semi-automatic derivation.
- [Wiktionary Ancient Greek pronunciation periods](https://en.wiktionary.org/wiki/Wiktionary:Ancient_Greek_transliteration),
  distinguishing Classical, Koine, medieval, and modern conventions.
- [PerseusDL Lexica](https://github.com/PerseusDL/lexica), TEI lexica and repository reuse notice.
- [Zemberek-NLP](https://github.com/ahmetaa/zemberek-nlp), Turkish morphology dictionaries and Apache 2.0 license.
- [Magyar Ispell](https://github.com/laszlonemeth/magyarispell), Hungarian spelling/morphology sources and licenses.
- Mortensen, Dalmia, and Littell (2018), [Epitran: Precision G2P for Many Languages](https://aclanthology.org/L18-1266/), and the [Epitran implementation](https://github.com/dmort27/epitran).
- [CLTK](https://github.com/cltk/cltk), pre-modern-language NLP tooling under the MIT license.
- [eSpeak NG](https://github.com/espeak-ng/espeak-ng), multilingual phoneme generation under GPL 3.0 or later.

All source versions, counts, and license statements must be rechecked when the
data are actually acquired; upstream catalogs and repository terms can change.
