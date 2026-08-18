#!/usr/bin/env python3
import argparse
import json
import sys
import unicodedata

import epitran


def diagnostic(code, message, text=None):
    return {"code": code, "message": message, "text": text}


def convert_line(converter, profile, request):
    source_form = request["source_form"]
    normalized_form = unicodedata.normalize("NFC", source_form)
    diagnostics = []
    try:
        ipa = unicodedata.normalize("NFC", converter.transliterate(normalized_form))
        status = "complete" if ipa else "incomplete"
        if not ipa:
            diagnostics.append(diagnostic("empty_output", "Epitran returned no IPA output."))
    except Exception as exception:
        ipa = ""
        status = "incomplete"
        diagnostics.append(diagnostic("provider_error", str(exception)))

    return {
        "$schema": "ipa-conversion-artifact.schema.json",
        "schema_version": "1.0.0",
        "record_id": request["record_id"],
        "source": request["source"],
        "language": request["language"],
        "source_form": source_form,
        "normalized_form": normalized_form,
        "ipa": ipa,
        "provider_id": "epitran",
        "provider_version": "1.35.2",
        "profile_id": profile,
        "profile_version": "1.0.0",
        "status": status,
        "diagnostics": diagnostics,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("profile", choices=("tur-Latn", "hun-Latn"))
    arguments = parser.parse_args()
    converter = epitran.Epitran(arguments.profile)
    for line_number, line in enumerate(sys.stdin, start=1):
        if not line.strip():
            continue
        try:
            request = json.loads(line)
            artifact = convert_line(converter, arguments.profile, request)
        except Exception as exception:
            print(f"request line {line_number}: {exception}", file=sys.stderr)
            return 1
        print(json.dumps(artifact, ensure_ascii=False, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
