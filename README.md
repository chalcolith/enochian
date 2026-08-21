# Enochian

This project provides some tools to do exploratory phonological comparisons
between texts in unknown languages and entries one or more lexicons.

You may see the [results of a recent Voynich Manuscript test
run](http://chalcolith.github.io/enochian).

## Build and Test

The .NET and test SDK versions are pinned by `source/global.json`. Run the
repository build from `source/` so the pinned SDK configuration is applied:

```powershell
cd source
dotnet restore Enochian.slnx
dotnet build Enochian.slnx --no-restore
dotnet test Enochian.slnx --no-build
```

At the start of the .NET 10 baseline migration, the repository discovered six
tests: two unit tests and four integration-test cases. M0-00 completes with ten
tests: six unit tests and four integration-test cases. No tests are skipped.

The integration tests use only checked-in fixtures and do not require network
access. `EnglishTestSimple` writes `reports/english_test_report.html`, which is
ignored by Git. `RomlexScraper` does perform live network acquisition when run,
but it is a utility and is not invoked by the test suite or CI.

Warnings are treated as errors. Missing XML documentation (`CS1591`) is
suppressed as the temporary M0-00 migration baseline; all other compiler,
analyzer, and style diagnostics fail the build.

## Research Protocol

The versioned experiment and lexicon interchange contracts live under
`experiments/schemas/` and `resources/lexicons/schemas/`. See the
[frozen research protocol](experiments/protocol.md) for field definitions,
inclusion and exclusion rules, planned contrasts, and the confirmatory decision
rule. Schema conformance is enforced by the unit-test suite.

Source ownership, revisions, checksums, license status, and distribution
decisions are recorded in `resources/lexicons/manifests/`. Validate them or
regenerate the attribution report offline from `source/`:

```powershell
dotnet run --project Enochian.Provenance -- validate
dotnet run --project Enochian.Provenance -- attribution ../resources/lexicons/manifests ../resources/lexicons/ATTRIBUTION.md
```

See the [lexicon provenance guide](resources/lexicons/README.md) before adding,
acquiring, or packaging a source.

## Introduction

The initial goal is to investigate whether a particular theory of a possible
phonological interpretation of the script in the Voynich manuscript can be used
to find possible lexical matches in various machine-readable lexicons.

[Stephen Bax](https://stephenbax.net/?page_id=11) in 2014 proposed some
phonological values for various Voynich characters, based on identifications of
plant and star names in some of the illustrated pages. [Derek
Vogt](https://www.youtube.com/channel/UC-sW5dOlDxxu0EgdNn2pMaQ/videos) has
elaborated on this work and proposed a more extensive phonological scheme. In
addition, he has analyzed the phonological inventory of the scheme and proposed
that the language of the Voynich manuscript is based on some variety of Romani.

At present, the Enochian software tool can take arbitrary lines from the
[Reed-Landini-Stolfi
Interlinear](http://www.ic.unicamp.br/~stolfi/voynich/98-12-28-interln16e6/)
transcription of the Voynich manuscript, encode each word as a sequence of
vectors in phonological feature space, and then search the
[RomLex](http://romani.uni-graz.at/romlex/) lexicon of Romani and the
[Shabda-Sagara Sanskrit
dictionary](http://www.sanskrit-lexicon.uni-koeln.de/scans/csldoc/dictionaries/shs.html),
using dynamic time warping to look for for the closest phonological sequence
matches.

You can see a sample of this kind of flow in the
[voynich.json](https://github.com/chalcolith/enochian/blob/master/samples/voynich.json)
flow configuration. This flow reads the RomLex lexicon and the specified lines
of the Voynich transcription and produces an HTML file containing a report on
the possible phonological matches.

### Status

Current results are inconclusive. Possible matches for words meaning "sun",
"moon", "house", and "sky" appear on the first page of the Voynich manuscript,
which are suggestive of references to astrological content, but much more work
needs to be done.

You may see the [results of a recent Voynich Manuscript test
run](http://chalcolith.github.io/enochian/index.html).

### Roadmap

The RomLex lexicon has fewer than 30,000 entries, many of which are duplicates,
due to the lexicon containing data from multiple Romani dialects. This means it
does not provide very conclusive results on its own.

The Shabda-Sagara dictionary also has fewer than 30,000 entries.

## General Functionality

At the most general level, the Enochian library provides a system for
configuring and running "flows" of arbitrary data transformations. This is
implemented by the
[Flow](https://github.com/chalcolith/enochian/blob/master/source/Enochian/Flow/Flow.cs)
class, which contains a
[FlowContainer](https://github.com/chalcolith/enochian/blob/master/source/Enochian/Flow/FlowContainer.cs)
which can have a number of
[FlowStep](https://github.com/chalcolith/enochian/blob/master/source/Enochian/Flow/FlowStep.cs)
objects (which can themselves be containers).

When you iterate over the enumerable returned by `FlowStep.GetOutputs()`, each
step will grab an output from its previous sibling and call its `Process()`
method on it, returning the resulting output. If you implement only
`FlowStep.Process()`, or if you implement `FlowStep.GetOutputs()` using `yield
return`, the flow process will be asynchronous; it will only process as many
items as are needed to return one output.

### Dynamic time warping results

`DynamicTimeWarp.GetSequenceResult` returns the accumulated cost, selected path
length, both input lengths, and an optional diagnostic path. The existing
`GetSequenceDistance` method remains a compatibility wrapper that returns only
the accumulated cost. `DTWMatcher` stores the full result on each matched
`SegmentOption` without adding the values to the HTML report.

Mean-path normalization is `cost / pathLength`. Mean-input-length
normalization is `cost / ((sourceLength + targetLength) / 2)`. Two empty inputs
have zero cost and path length; one empty input has positive-infinite cost and
zero path length. In both cases, the diagnostic path is empty when requested.

For equal predecessor costs, DTW selects the shorter predecessor path and then
uses match, insertion, deletion order. Tolerance must be finite and between
zero and one. Zero tolerance starts at the matrix origin; positive tolerance
permits a free prefix of up to the corresponding fraction of each input.
Feature vectors must have equal dimensions and finite values, and element
distance functions must return finite, non-negative costs. Euclidean element
distance overflow throws `OverflowException`; accumulated matrix-cost overflow
produces positive infinity.

`DTWMatcher` applies `numOptions` independently to every configured lexicon.
Equal-cost candidates are ordered by entry ID and source-record ID. Each match
has a one-based within-lexicon rank and a stable scored-record ID; the existing
`MatchReport` HTML remains unchanged.

Add `scoredExport` to a matcher to write auditable quantitative artifacts:

```json
{
	"id": "Search panel",
	"type": "DTWMatcher",
	"lexicons": ["latin", "turkish"],
	"numOptions": 20,
	"scoredExport": {
		"jsonl": "output/scored-matches.jsonl",
		"csv": "output/scored-matches.csv",
		"metadata": "output/scored-matches.metadata.json",
		"schema": "../experiments/schemas/scored-match.schema.json",
		"definitions": "output/scored-match-definitions.jsonl"
	}
}
```

Paths are relative to the flow configuration. JSONL rows use UTF-8 and LF;
CSV follows RFC 4180 with UTF-8, CRLF, invariant-culture numbers, and round-trip
double precision. Rows sort by query ID, lexicon ID, rank, and candidate ID.
The metadata records schema, configuration, and core software SHA-256 hashes.
Quantitative rows never contain definitions. The optional `definitions` path
writes a separate candidate-ID join artifact; omit it for blinded runs. A
matcher accumulates records across input chunks and atomically rewrites the
artifacts after each chunk. Enumerate all matcher outputs to complete a
multi-chunk export.

### Balanced sampling and sequence nulls

Run a validated M3-03 sampling protocol from `source/`:

```powershell
dotnet run --project Enochian.Benchmark -- sample ../experiments/sampling.json ..
```

The protocol follows
`experiments/schemas/sampling-protocol.schema.json`. It freezes the seed,
generator version, repetition count, character-to-feature mapping, query file,
named analyses, included entry kinds, optional frequency bands, source
lexicons, smaller sample sizes, and output paths. Analyses are explicitly
labelled `primary`, `full`, or `inflected`; all analysis IDs and membership,
null, and report paths must be distinct.

The query file is UTF-8 JSONL. Each row contains a stable `query_id`, source
`text`, an ordered `symbols` array, and positive `token_frequency`:

```json
{"query_id":"voynich-type-0001","text":"qokeedy","symbols":["q","o","k","e","e","d","y"],"token_frequency":12}
```

For each analysis, candidate construction first keeps one deterministic
pronunciation per normalized lemma, then collapses identical phonologies while
retaining all matching source entry memberships. Entry kinds not named by the
analysis are excluded. Sampling strata use phoneme-length bands and, when
configured, frequency bands; missing frequencies form an explicit `missing`
stratum. The largest common size is the sum of each stratum's minimum capacity
across languages. Every language receives exactly that size and each smaller
predeclared size without replacement for every repetition. Reports include all
per-language/stratum shortages and counts excluded by balancing.

The runner emits deterministic UTF-8 JSONL membership and null artifacts plus
a JSON report. Membership rows record analysis/sample IDs, repetition,
requested size, seed, generator version, stratum, candidate, entry kind, and
source memberships. Null rows conform to
`experiments/schemas/sequence-null.schema.json` and require `is_null: true`, a
`null.*` ID, and one of `unigram-pseudoword`, `biphone-pseudoword`,
`mapping-assignment-shuffle`, or `within-query-shuffle`. Language-conditioned
pseudowords are fitted to each exact balanced membership set and match each
query length; every null row records its sample ID and requested size. Every null is emitted once as
`type-primary` with weight 1 and once as `token-weighted` with the frozen token
frequency.

### Calibrated scores and statistical comparisons

Run a validated M3-04 statistics protocol from `source/`:

```powershell
dotnet run --project Enochian.Benchmark -- statistics ../experiments/statistics.json ..
```

The protocol follows
`experiments/schemas/statistics-protocol.schema.json`. It freezes the input and
schema paths, calibration null kind, randomization seed, permutation and
bootstrap counts, confidence level, contrasts, and distinct output paths. A
confirmatory protocol must name exactly one primary contrast, and every
contrast must exactly match its ID, primary status, groups, and expected
direction in the referenced frozen confirmatory experiment config. Because a
smaller raw distance is better, a frozen `lower` direction maps to a `greater`
alternative for the standardized score.

The UTF-8 nearest-distance JSONL input conforms to
`experiments/schemas/nearest-distance.schema.json`. Each observed or null row
explicitly records its analysis mode, sample and repetition, requested
dictionary size, unique query type, length/section/frequency strata, frozen
weight, language/family, null status and kind, and nearest normalized distance.
Observed rows must be unique by analysis, mode, sample, query, and language.
Rows marked `type-primary` must have weight 1; token frequencies are used only
as weights in the secondary analysis and are never expanded into independent
observations.

For observed distance `d`, calibration against `N` matched null distances uses
the midrank empirical percentile `(count(null < d) + 0.5 * count(null = d)) / N`.
The standardized score is `(null mean - d) / null sample standard deviation`,
so larger values indicate a closer-than-null match. Empty, singleton, and
zero-variance null distributions produce diagnostics and nullable standardized
scores rather than fabricated values.

Contrasts use one target/control pair per unique query type after collapsing
repeated lexicon samples. The runner reports weighted paired median
differences, matched-pairs rank-biserial effects, sign-flip permutation tests,
and percentile intervals from a hierarchical bootstrap over lexicon samples
and query types. Exact sign enumeration is used when it fits within the
configured permutation count; outputs record both configured and actual
counts. Holm adjustment is applied within each analysis/mode/dictionary-size
family. Per-language raw, percentile, standardized, and winner summaries are
emitted overall and by query length, manuscript section, and frequency band.

All outputs are deterministic UTF-8 JSONL written atomically. Separate tidy
tables contain calibrated scores, estimates, intervals, tests, adjusted
p-values, and diagnostics; each has a versioned schema under
`experiments/schemas/`. Missing query/language pairs and insufficient bootstrap
samples remain explicit diagnostics and never become zero-valued estimates.

## Linguistic Resources

In order to do phonological analysis, the Enochian library provides a way to
specify a phonological feature set (see
[features.json](https://github.com/chalcolith/enochian/blob/master/resources/encodings/features.json)
for an example using a pretty standard set of phonological features). The
[FeatureSet](https://github.com/chalcolith/enochian/blob/master/source/Enochian/Text/FeatureSet.cs)
class is used to load and use these feature sets.

You can also define text "encodings". These take input strings in Unicode and
produce sequences of vectors in the multi-dimensional space defined by the
phonological feature set. A single phonological segment consists of an
`N`-dimensional vector, where `N` is the number of features in your feature set.
If a particular feature has a `+` value for that segment, its corresponding
vector element will be `1`; if it has a `-` value, its vector element will be
`-`. If the feature is unspecified, its vector element will be `0`.

## Lexicons

The systems includes several lexicons:

### Lexicon entry and cache compatibility

`LexiconEntry` records include stable entry/source identifiers, language and
family, form and entry kind, dialect, part of speech, frequency, source
encoding, and IPA metadata. `Dialect`, `PartOfSpeech`, `Frequency`, and `Ipa`
are optional for legacy sources. Existing loaders populate deterministic
defaults for required metadata that their source format does not provide.

`Lexicon.EntriesByLemma` is a one-to-many, ordinally ordered index because a
lemma may have homographs or multiple pronunciations. Callers that intentionally
need only the first deterministic match can use `GetEntryByLemma`.

The binary cache format is versioned. Caches from older versions are ignored and
rebuilt from source. Cache identity includes the canonical source path, lexicon
type and ID, feature set, encoding, and debug limit, so equal source filenames
or different configurations cannot collide. Cache replacement is atomic; an
interrupted write does not replace the previous valid cache.

### CMU Pronouncing Dictionary

This is used for testing the underlying assumption behind the project, that we
can find slightly dissimilar phonological sequences in a lexicon by means of
dynamic time warping. The
[english_test.json](https://github.com/chalcolith/enochian/blob/master/samples/english_test.json)
contains a sample flow that compares a defective encoding of English text with
the CMU dictionary to produce matches for English words. Running this flow
demonstrates that the process is capable of finding many such valid matches.

### RomLex

This is a dictionary of words in various Romani dialects. The database is only
available via the web, so there is a project `RomlexScraper` that scrapes the
web interface to assemble a complete version of the lexicon.

### Shabda-Sagara

This is a 19th-century dictionary of classical Sanskrit.
