# Agent Guide

## Start Here

- Read [README.md](README.md) for the current research goal and flow model.
- Treat [docs/improvement-plan.md](docs/improvement-plan.md) as the research and data roadmap, and [docs/milestone-pr-specs.md](docs/milestone-pr-specs.md) as the source of scope, determinism, provenance, and acceptance criteria for milestone work.
- Keep changes within the requested project or milestone PR. Do not pull later milestone work into an adjacent change.

## Build and Test

- Run .NET commands from `source/`; this is where `global.json` pins .NET SDK 10.0.100 and selects Microsoft Testing Platform.
- Primary checks are `dotnet build Enochian.slnx` and `dotnet test Enochian.slnx`.
- Inspect `Enochian.slnx` before assuming solution-wide coverage. Projects not listed there require an explicit project build or test, such as `dotnet test Enochian.IntegrationTests/Enochian.IntegrationTests.csproj`.
- The repository may be partway through the M0-00 migration described in the milestone specs. Report pre-existing failures separately; do not hide them or broaden the requested fix.
- Shared target-framework and analyzer settings belong in `Directory.Build.props`; package versions belong only in `Directory.Packages.props`.
- Warnings, code-style diagnostics, and StyleCop diagnostics are errors. Fix relevant diagnostics in code; do not suppress them or weaken the shared build policy.

## Code Boundaries

- `source/Enochian/Flow/` owns configurable, lazily evaluated processing pipelines. Preserve `GetOutputs()`/`yield return` behavior when changing flow steps.
- `source/Enochian/Text/` owns feature sets, encodings, and text chunks; `source/Enochian/Lexicons/` owns lexicon loading; `source/Enochian/Math/` owns sequence-distance logic.
- `source/Enochian.Console/` is the CLI host. `GenVoynichRunner` and `RomlexScraper` are utilities, not core-library APIs.
- JSON flow configurations under `samples/` are executable examples. Resolve their resource paths relative to the configuration file, as the flow loader does.
- Follow nearby MSTest patterns and reuse `source/Enochian.UnitTests/AssertUtils.cs` where applicable. Add focused tests for behavior changes.

## Research Data

- Treat files under `resources/lexicons/` and `resources/voynich/` as third-party source snapshots, not ordinary fixtures or generated scratch data.
- Before adding or regenerating data, follow the licensing, attribution, normalization, and reproducibility rules in the improvement plan and milestone specs.
- Keep generated outputs deterministic: sort records, use invariant-culture formatting, normalize line endings, and do not depend on filesystem enumeration order.

## Workflow

- Don't force-push things; make separate commits.
