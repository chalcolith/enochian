#!/usr/bin/env python3
import argparse
import json
import os
import tempfile
import unicodedata

from tf.fabric import Fabric


FEATURES = "otype lex lex_utf8 g_lex_utf8 voc_lex_utf8 gloss language sp freq_lex rank_lex phono"


def nfc(value):
    return unicodedata.normalize("NFC", value) if value else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("bhsa_tf")
    parser.add_argument("phono_tf")
    parser.add_argument("output")
    arguments = parser.parse_args()

    fabric = Fabric(locations=[arguments.bhsa_tf, arguments.phono_tf], silent="deep")
    api = fabric.load(FEATURES, silent="deep")
    if not api:
        raise RuntimeError("Unable to load the pinned BHSA and phono Text-Fabric features.")

    features = api.F
    locality = api.L
    output_directory = os.path.dirname(os.path.abspath(arguments.output))
    os.makedirs(output_directory, exist_ok=True)
    descriptor, temporary = tempfile.mkstemp(prefix="bhsa-", suffix=".jsonl", dir=output_directory)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as writer:
            for lexeme_node in features.otype.s("lex"):
                words = locality.d(lexeme_node, otype="word")
                source_frequency = features.freq_lex.v(lexeme_node)
                for word_node in words:
                    record = {
                        "schema_version": "1.0.0",
                        "corpus_label": "Biblical Hebrew",
                        "source_record_id": str(word_node),
                        "lexeme_id": str(lexeme_node),
                        "language": features.language.v(lexeme_node),
                        "lexeme": nfc(features.lex_utf8.v(lexeme_node)),
                        "vocalized_form": nfc(
                            features.g_lex_utf8.v(lexeme_node)
                            or features.voc_lex_utf8.v(lexeme_node)
                        ),
                        "gloss": features.gloss.v(lexeme_node),
                        "part_of_speech": features.sp.v(lexeme_node),
                        "phono": nfc(features.phono.v(word_node)),
                        "source_frequency": source_frequency,
                        "rank": features.rank_lex.v(lexeme_node),
                    }
                    writer.write(json.dumps(record, ensure_ascii=False, separators=(",", ":"), sort_keys=True))
                    writer.write("\n")
        os.replace(temporary, arguments.output)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
