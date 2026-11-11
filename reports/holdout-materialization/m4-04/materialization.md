# M4-04 holdout query materialization

The pre-open leakage audit **passed** before the frozen
`f3r`, `f3v`, `f4r`, and `f4v` partition was read.

- Accepted token occurrences: 343
- Excluded token occurrences: 2
- Unique definitions-free query types: 227
- Frozen mapping: `4bc43e1e9d0fd2f29a6f4e8de804fe460f367f75bc3f796af5e7e48a7ecfb16e`
- Query checksum: `d7d116ac9f13c4c87b35744bc65345b9bc054434ad51c45e6005f3d691d8a9ff`
- Definitions present: **false**
- Holdout opened: **true**

The same normalization, exact-type aggregation, representative-locus rule,
and approved mapping used for M4-01 were applied without inspecting dictionary
definitions or comparative scores. Tokens containing symbols absent from the
frozen evaluation mapping are excluded as complete tokens and reported.
