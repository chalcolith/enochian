# M3-06 masking-distance investigation

Runner: `masked-query-distance-v2`; policy: `m3-06-masked-query-retrieval`.

The v1 query mask wrote numeric zero into Voice, SpreadGlottis, and ConstrictedGlottis, then ordinary
Euclidean cost compared those zeros with candidate values. For real IPA `/p/`, even self-retrieval had
a path-mean floor of `1.7320508075688772`, above the unchanged `0.35` threshold.

The versioned correction excludes only explicitly masked query dimensions from local Euclidean cost.
Candidate vectors are unchanged; genuine unset zeros in every other dimension remain measurable. DTW
path normalization, reading weights, rankings, ties, thresholds, and all non-masking degradations are unchanged.

| Degradation | Band | Queries | R@1 | R@5 | R@20 | MRR | Mean distance | Passed | Blockers |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| delete-every-fourth | 03-05 | 25 | 1 | 1 | 1 | 1 | 0.9625278198009954 | false | mean_normalized_distance |
| delete-every-fourth | 06-09 | 25 | 0.96 | 1 | 1 | 0.98 | 0.8276430402659287 | false | mean_normalized_distance |
| delete-every-fourth | 10-20 | 25 | 1 | 1 | 1 | 1 | 0.8379263571728508 | false | mean_normalized_distance |
| identity | 03-05 | 25 | 1 | 1 | 1 | 1 | 0.03771236166328254 | true |  |
| identity | 06-09 | 25 | 1 | 1 | 1 | 1 | 0.006666666666666666 | true |  |
| identity | 10-20 | 25 | 1 | 1 | 1 | 1 | 0 | true |  |
| mask-laryngeal | 03-05 | 25 | 1 | 1 | 1 | 1 | 0.035276684147527874 | true |  |
| mask-laryngeal | 06-09 | 25 | 1 | 1 | 1 | 1 | 0.006666666666666666 | true |  |
| mask-laryngeal | 10-20 | 25 | 1 | 1 | 1 | 1 | 0 | true |  |
| merge-height | 03-05 | 25 | 1 | 1 | 1 | 1 | 1.345073972540408 | false | mean_normalized_distance |
| merge-height | 06-09 | 25 | 1 | 1 | 1 | 1 | 1.2961921979793878 | false | mean_normalized_distance |
| merge-height | 10-20 | 25 | 1 | 1 | 1 | 1 | 1.3842273927674524 | false | mean_normalized_distance |

All three corrected masking cells pass. Identity also passes. Deletion and height merger retain mostly exact
retrieval ranks but fail the unchanged mean-distance threshold. Hindi, Sanskrit, and Hungarian cannot run
real retrieval because their pronunciation/capacity gates remain blocked. M4-01 therefore remains blocked.
