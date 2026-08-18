# Modern Indo-Aryan and Indo-Iranian panel

This selection was frozen on 2026-08-18 before any Voynich comparison. A
language needed a pinned UniMorph repository with at least 100 unique lemmas,
verified redistribution terms, and a native-script dataset. Confirmatory
candidates additionally needed a pinned Epitran 1.35.2 profile and at least 100
auditable outputs available for blinded review.

| Language | UniMorph forms | Unique lemmas | Epitran profile | Status | Reason |
| --- | ---: | ---: | --- | --- | --- |
| Hindi (`hin`) | 54,438 | 258 | `hin-Deva` | candidate | Required anchor; dataset and profile meet thresholds. |
| Bengali (`ben`) | 4,443 | 136 | `ben-Beng` | exploratory | Only 48 readings pass strict IPA audit, below the 100-row review threshold. |
| Gujarati (`guj`) | 7,505 | 397 | unavailable | exploratory | Dataset meets the size threshold, but Epitran 1.35.2 has no Gujarati profile. |
| Persian (`fas`) | 37,128 | 273 | `fas-Arab` | exploratory | Bridge control selected, but ordinary Perso-Arabic lemmas omit vowels needed for auditable lexical IPA. |

Marathi, Punjabi, and Bhojpuri were not selected because no current UniMorph
language repository was available at freeze time. Assamese had a current
dataset but no Epitran 1.35.2 profile; Gujarati was chosen among unsupported
candidates because it had more unique lemmas and supplied a distinct script.
These decisions use source availability, lemma count, script coverage, and G2P
support only. They do not use observed matching scores.

The parser reads the three UniMorph columns as lemma, inflected form, and
features. It writes unique NFC lemmas to `unimorph-<language>.jsonl` and every
valid source row to `unimorph-<language>.inflected-forms.jsonl`. Primary IDs
begin `lemma:`; morphology IDs begin `form:`. Inflected forms are never sent to
the IPA provider. Script text, optional transliteration, and IPA occupy separate
fields.

`samples/modern-indo-aryan-panel.json` contains only Hindi. Hindi remains
ineligible for confirmatory analysis until its IPA audit satisfies the common
unknown-symbol threshold and its 100-row blinded review passes the protocol
threshold. Bengali, Gujarati, and Persian cannot enter that panel without a new
versioned profile, quality report, and review decision.

At the pinned revisions, deterministic strict audit emits 149 of 258 Hindi
lemmas and prepares 100 review rows. The 109 rejected Hindi outputs contain
spaces or the unsupported breathy-voice combining mark. Bengali emits 48 of
136 lemmas; unsupported affricates, non-syllabic and breathy marks,
chandrabindu, and spaces account for 88 rejections. Gujarati withholds all 397
lemmas because no profile exists. Persian excludes all 273 lemmas because none
has explicit vowel marks. These are machine-readable blockers, not
normalization rules.
