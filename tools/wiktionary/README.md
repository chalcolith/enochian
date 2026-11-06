# Isolated Wiktionary generator replay (M2-11)

This acquisition adapter uses a Linux/amd64 MediaWiki image with bundled
Scribunto and LuaSandbox. It does not copy Wiktionary modules or GPL runtime
code into Enochian. Docker must be running; replay does not install a host PHP
or Lua runtime.

From `source/`, retain the M2-10 snapshot and profile artifacts, then run:

```powershell
docker pull mediawiki@sha256:9118f6e0ef4192b87d4bc602818a2812b2f471b5596928f0d1fd8256cde98be1
..\tools\wiktionary\smoke.ps1
dotnet run --project Enochian.Provenance -- pronunciation-replay-acquire
dotnet run --project Enochian.Provenance -- pronunciation-replay
dotnet run --project Enochian.Benchmark -- admit ..\experiments\confirmatory.replay.v1.json
```

The first command explicitly downloads the immutable runtime. Acquisition
downloads additional English Wiktionary revisions at the M2-10 cutoff, at most
one request per second. Content-addressed responses are cached locally under
`.enoch/pronunciation-replay/v1/`; keep this directory for offline reproduction.
HTTP/API failures do not seal a partial package. Safety limits are 100 dependency
rounds and 10,000 pages; each container has a 20-minute execution timeout.
Literal module references in generator roots and execution-requested modules are
prefetched to avoid serial missing-module failures. Unexecuted candidates are
not recursively scanned, and unrelated cached pages are not imported.
This scan is only acquisition acceleration, never evidence of complete execution
closure; every request must still undergo full traced offline expansion.
Pinned absence is an execution input too: successful optional existence checks
retain revision-zero dependencies. Missing modules that cause execution errors
remain blocked, as do any requested titles absent from the snapshot.
Alternative distributions are compared against all IPA spans owned by the
original Parsoid transclusion, including repeated and phonetic alternatives.
Selected lexical rows are not a proxy for that distribution: selection can
omit alternatives or repeat one across parts of speech. Publication still
preserves exactly the original selected rows, memberships, and weights.

Every expansion runs in a fresh, network-disabled container with no published
ports, no Linux capabilities, and read-only mounts containing only the external
adapter and prepared inputs. No workspace-wide mount or host credentials are
provided. The ephemeral SQLite database and administrator credential exist only
inside that container and are destroyed afterward.

The explicit smoke uses only synthetic template/module inputs. It checks raw
alternative output, parent-frame respelling, page-title/content inputs, dynamic
module/data tracing, changed-frame failure, unsupported runtime access, and two
byte-identical fresh executions. Both present and absent JSON-data requests are
traced before MediaWiki's content-model/existence checks. It is not independent
pronunciation evidence.

## Reproduction contract

The plan pins all four frozen M2-10 envelopes and their amendment. Requests are
derived again from their verified, language/etymology/POS-scoped transclusions.
The canonical frame preserves the page title, generator name, original
respelling/parameter wikitext, and all bound reading memberships. Ambiguous
metadata or unresolved positional whitespace semantics fails explicitly.
Numeric arguments use explicit keys so a literal `=` cannot be reinterpreted as
a different frame parameter.

MediaWiki expands the original template through Scribunto, not a replacement
spelling-to-IPA implementation. Parser content callbacks and an external
LuaSandbox title-library adapter trace template/module/data loads, dynamic
`require`/`mw.loadData`/`mw.loadJsonData`, title existence, redirects,
page-content inputs, and on-wiki message/fallback inputs.
Missing dynamically requested titles are acquired and expansion repeats until
no execution dependency is unretained. Required missing template/module content,
interpreter errors, and unsupported title APIs remain explicit blockers.
Negative lexical-title lookups are retained as pinned absence at the cutoff.

The declared offline site uses English MediaWiki, UTC, case-sensitive titles,
the pinned image's built-in language data, and the checked-in settings adapter.
The parser clock is fixed to the cutoff and source revision IDs/timestamps are
restored in the parser callbacks; relative date formatting and unretained
category/file/protection/site-query data are explicit unsupported inputs.
It is not a reconstruction of Wikimedia's entire historical deployment or proof
that Parsoid used these revisions. Only exact original IPA reproduction under
this explicitly pinned environment is reported; differing readings are not
substituted, even if normalization would make them agree.

Two fresh executions must have identical definitions-free IPA and dependency
outputs before sealing. A separate `2.0.0` source package pins the image through
the plan, adapter bytes, full API revision bytes, requests, and execution output.
New `1.2.0` profile envelopes pin that package and per-reading proofs. Admission
re-expands the package offline and checks every transitive pin, unchanged
profile/entry content, source memberships, alternatives, and original weights.
Existing M2-07 through M2-10 artifacts and validators are unchanged.

Successfully reproduced generated readings receive `wiktionary:<template>` and
`mediawiki-replay-v1:<package-sha256>` identities. They remain exploratory and
have **unknown independence**. The explicit `2.1.0` independent-reference
contract additionally binds the generator ID and package revision, while keeping
the existing held-out sample, independence, length-band, and agreement gates.
Unresolved generation cannot inherit agreement from reproduced readings.
Wiktionary or its extraction mirrors are not independent references for these
generators. This work does not populate balanced lexicons, change retrieval
thresholds, approve a manuscript mapping, or score the manuscript.

## Verified replay coverage

The sealed package contains 785 source records. Fresh offline execution
reproduces all 592 selected generated readings across 476 transclusions:
Hindi 214, Sanskrit 172, Turkish 64, and Hungarian 142.
See [coverage.json](../../reports/pronunciation-replay/v1/coverage.json).
These counts establish reproducibility only; independent-reference agreement
and the existing admission/readiness gates remain required.

## Licensing and retained assets

Raw Wiktionary API revisions may contain definitions and third-party module
code. They remain gitignored acquisition inputs, not definitions-free published
exports. The parent snapshot's verified CC-BY-SA-4.0 rights metadata and
Wiktionary contributor attribution remain transitive inputs. Revision URLs are
`https://en.wiktionary.org/w/index.php?oldid=<revision>`.
Derivative exports retain attribution and share-alike obligations.

MediaWiki and Scribunto are GPL-2.0-or-later; LuaSandbox and bundled libraries
have their own notices in the pinned image. If redistributing the runtime or
combined adapter, retain those notices and provide the corresponding source
required by its licenses. Downloaded modules remain separate licensed data/code
assets; do not relicense them as the repository's MIT core library.
