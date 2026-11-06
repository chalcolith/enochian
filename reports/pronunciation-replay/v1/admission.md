# Automated dictionary-first admission v2

Protocol: `dictionary-first-generator-replay-v1`; canonical SHA-256: `9651cfc005d9fa96bf124c7d2ba43d31315efc99916e99701ab5d583c5dac486`.
Validator: `automated-experiment-v2`; binary SHA-256: `9434eb7caf937f88cbc8380aeaae3e44b83a980a35a96bebb83338b300d9d16e`.

Required source gates: **blocked**. No human annotation is required.
This admission step does not run manuscript comparative scoring. Execution additionally requires query/mapping preflight.

Compatible counts and bands precede full pronunciation-distribution deduplication. Supported counts require
dictionary-supplied readings or independently validated generation. Unique counts include only supported distributions.

| Language | Required | Compatible | 03-05 | 06-09 | 10-20 | Supported | Unique supported | Status | Blockers |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| ara | false | 0 | 0 | 0 | 0 | 0 | 0 | excluded | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| asm | false | 2940 | 2072 | 822 | 46 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| ben | false | 5491 | 3107 | 2070 | 314 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| bho | false | 0 | 0 | 0 | 0 | 0 | 0 | excluded | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| fas | false | 4324 | 2383 | 1717 | 224 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| grc | false | 16464 | 3704 | 10190 | 2570 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| guj | false | 3213 | 1973 | 1168 | 72 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| hbo | false | 571 | 396 | 171 | 4 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| hin | true | 16019 | 7358 | 7694 | 967 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| hun | true | 35158 | 8403 | 18270 | 8485 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| lat | false | 26064 | 3337 | 15317 | 7410 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| mar | false | 2815 | 1528 | 1155 | 132 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| pan | false | 1112 | 851 | 255 | 6 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| pus | false | 871 | 555 | 305 | 11 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| rom | false | 26 | 19 | 7 | 0 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence; profile_is_exploratory_only |
| san | true | 6353 | 2755 | 3333 | 265 | 0 | 0 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |
| tur | true | 10163 | 3765 | 5354 | 1044 | 85 | 85 | exploratory | insufficient_balanced_units:03-05; insufficient_balanced_units:06-09; insufficient_balanced_units:10-20; insufficient_supported_pronunciation_evidence |

Pronunciation and retrieval are separate gates; self-retrieval is not pronunciation validation.
Unspecified-dialect Romani is exploratory-only. Hebrew is descriptive-only. Arabic has a separate registered contrast.
Missing Arabic/Greek/Hebrew evidence limits historical-language and star-name claims; same-family languages are not independent replications.
All required sources must pass before M4-01; excluded languages cannot be described as having passed or having a null effect.
