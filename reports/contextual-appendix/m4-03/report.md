# M4-03 automated contextual appendix

Run ID: `73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1`. This optional appendix was generated only after the M4-02 quantitative report was frozen.

## Interpretation boundary

This is secondary exploratory metadata, not semantic ground truth. It did not use human or LLM annotation, did not score gloss plausibility, did not tune the phonological mapping, and did not read holdout-derived data. Definitions and proper-name metadata remain separate from every primary matching and statistical artifact.

## Frozen candidate contexts

242 unique sampled lexical candidates were joined by frozen candidate/source identifiers; 242 have at least one source context. Every available source gloss and label is retained without choosing an appealing sense. Repeated lexicon-sample membership does not duplicate candidates.

| Language | Candidates | With context | Context rows | Missing/ambiguous rows |
| --- | ---: | ---: | ---: | ---: |
| san | 120 | 120 | 120 | 0 |
| tur | 122 | 122 | 185 | 0 |

Source: [candidate-contexts.csv](candidate-contexts.csv), run `73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1`.

The frozen M4-01 outputs do not retain nearest-candidate identities by query, so these contexts cannot be associated with individual Voynich query scores without rerunning or changing the frozen analysis. No such association is inferred here.

## Separately registered astronomical names

| Language | Profile records | Linked catalog IDs | Ambiguous keys | Status |
| --- | ---: | ---: | ---: | --- |
| ara | 0 | 0 | 0 | no_exact_links |
| asm | 3165 | 0 | 0 | no_exact_links |
| ben | 11135 | 0 | 0 | no_exact_links |
| bho | 0 | 0 | 0 | no_exact_links |
| fas | 9233 | 0 | 0 | no_exact_links |
| grc | 16465 | 0 | 0 | no_exact_links |
| guj | 3446 | 0 | 0 | no_exact_links |
| hbo | 776 | 0 | 0 | no_exact_links |
| hin | 23735 | 0 | 0 | no_exact_links |
| hun | 35456 | 0 | 0 | no_exact_links |
| lat | 26158 | 0 | 0 | no_exact_links |
| mar | 3984 | 0 | 0 | no_exact_links |
| pan | 1205 | 0 | 0 | no_exact_links |
| pus | 1062 | 0 | 0 | no_exact_links |
| rom | 42 | 0 | 0 | no_exact_links |
| san | 8650 | 0 | 0 | no_exact_links |
| tur | 11036 | 0 | 0 | no_exact_links |

Source: [astronomy-languages.csv](astronomy-languages.csv), run `73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1`.

All 16 preregistered Arabic-versus-panel-language pairs are reported in [astronomy-pairs.csv](astronomy-pairs.csv). No pair reached the frozen minimum of 25 shared catalog IDs, so balanced scoring and null generation were not estimable; unmatched objects were not assigned zero scores.

Catalog names are descriptive external context. They do not establish Arabic etymology, pronunciation, or a claim that any Voynich token denotes a star.

## Reproduction

From `source/`, with the pinned dictionary and HYG snapshots retained:

```powershell
dotnet run --project Enochian.Benchmark -- context-m4 ..\experiments\contextual-appendix.m4-03.json
```

The appendix is optional. M4-02 and the future holdout workflow do not consume its outputs.

## Provenance and permissions

Sanskrit definitions are from the pinned CDSL Shabda-Sagara snapshot; Turkish glosses and labels are from the pinned English Wiktionary/Kaikki extraction; astronomical names are from pinned HYG 4.1. All three sources are recorded as CC BY-SA 4.0 in their checked manifests. The redistributed contextual text remains under CC BY-SA 4.0 rather than the repository's software license. Source identifiers are retained in the CSV tables and every input/output byte hash is recorded in [manifest.json](manifest.json).
