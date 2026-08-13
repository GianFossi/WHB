# AGENTS.md — AI Assistant Memory & Modification Guide

This file is persistent context for AI coding assistants (Claude, Codex,
ChatGPT, GitHub Copilot, Cursor, and similar). It records what this
repository is, how it is organized, and the conventions any AI must
follow when proposing or applying modifications.

Read this file before making changes. When conventions change, update
this file in the same commit so the memory stays accurate.

## Project Overview

- Name: WHB (Waste Heat Boiler / Process Gas Cooler)
- Purpose: Thermal, hydraulic and diagnostic calculations for fire-tube
  WHB / PGC units.
- Language / runtime: F# on .NET 8.
- Solution: `WhbDesign.sln`.
- Projects:
  - `src/Whb.Core` — calculation library and domain model.
  - `src/Whb.Cli` — CLI, JSON input loader, report generator.
  - `tests/Whb.Tests` — xUnit test project.

See `README.md` and `docs/` for engineering scope, theory, assumptions
and limitations.

## Build, Test, Run

Use the existing scripts and standard `dotnet` commands. Do not add new
build tooling.

```powershell
.\build.ps1                 # restore + build + test in Release
.\build.ps1 -Task Build     # restore + build
.\build.ps1 -Task Test      # restore + build + test
.\build.ps1 -Task Rebuild   # clean + restore + build + test
.\build.ps1 -Task Clean     # clean bin/obj/publish/TestResults
```

Cross-platform equivalent:

```bash
dotnet restore WhbDesign.sln
dotnet build   WhbDesign.sln -c Release
dotnet test    WhbDesign.sln -c Release
dotnet run --project src/Whb.Cli
dotnet run --project src/Whb.Cli -- --selftest
```

CI runs on `windows-latest` via `.github/workflows/ci.yml` (.NET 8,
restore + build + test).

## Repository Layout (canonical)

```text
src/Whb.Core/
  Options/Constants.fs       constants, unit conversions, bisection, fixed point
  Materials/SteamIF97.fs     IAPWS-IF97 helper properties
  Materials/GasProps.fs      gas species and mixture properties
  Materials/Materials.fs     material catalogue and limits
  Solvers/GasSide.fs         gas-side HTC correlations
  Solvers/WaterSide.fs       boiling and CHF correlations
  Solvers/TwoPhase.fs        void fraction and two-phase friction
  Options/Shift.fs           water-gas shift equilibrium helpers
  Components/Equipment/*.fs  bundle, drum, bypass, valves, nozzles
  Solvers/BundleSolver.fs    coupled gas/water bundle solve
  Solvers/Circulation.fs     natural-circulation loop solve
  Designers/Design.fs        top-level design orchestration and diagnostics
  Reports/*.fs               text, CSV and HTML reports
  Options/Defaults.fs        built-in reference case

src/Whb.Cli/
  Program.fs                 CLI, JSON input, reports and self-test

tests/Whb.Tests/
  Tests.fs                   xUnit tests
```

F# file compile order matters. When adding a new `.fs` file, register it
in the correct position in the project's `.fsproj` (dependencies must
appear before dependents).

## Modification Rules for AI Agents

1. Preserve engineering accuracy.
   - Do not change numerical correlations, constants, defaults or
     reference-case values without an explicit request.
   - Preserve units: inputs and reports use the units documented in
     `docs/INPUT_SCHEMA.md`. Pressures marked `bara` are absolute; drops
     are differential.
2. Keep changes minimal and surgical.
   - Modify only what the task requires. Do not refactor unrelated code,
     rename identifiers, or reformat files.
   - Do not add comments, docstrings or type annotations to code you did
     not change.
3. Do not remove or weaken tests.
   - Extend `tests/Whb.Tests` when adding new numeric behavior.
   - Never delete a test to make a change pass.
4. Follow the F# style already present.
   - Match indentation, naming (camelCase for values, PascalCase for
     types/modules), and module structure of neighboring files.
5. Reports and output files.
   - The output file contract is documented in `README.md` ("Output
     Files"). Do not rename, drop or silently change columns of
     `celle.csv`, `profilo_assiale.csv`, `pds_comparison.csv`,
     `inventory_summary.csv`, `tensioni.csv`, `valvola_bypass.csv`
     without updating the docs and tests.
6. CLI surface.
   - The commands listed under "CLI Commands" in `README.md` are the
     public surface. Adding a flag is allowed; removing or renaming one
     is a breaking change and must be flagged explicitly.
7. Options file.
   - Respect the keys already used in `whb.options.json` (logging,
     reporting, calculation, preflight, github). New keys must have
     documented defaults.
8. Documentation.
   - When behavior visible to a user changes, update the relevant file
     under `docs/` and, if applicable, `README.md`.
9. Safety warnings.
   - Do not remove the disclaimers in `README.md` and `docs/` about the
     software not being a certified pressure-vessel/boiler-code tool.
10. Dependencies.
    - Do not add NuGet packages unless strictly required. Prefer the
      standard library and the code already present.
11. Secrets and credentials.
    - Never commit API keys, tokens or credentials. `whb.options.json`
      `github` section must remain a template.

## Modification Memory Log

Record here notable, non-obvious modification decisions so future AI
sessions can reuse the context. Append new entries at the top with an
ISO date. Keep each entry short (what / why / where).

- 2026-08-13 — Introduced this `AGENTS.md` plus `CLAUDE.md` and
  `.github/copilot-instructions.md` shims to give AI coding assistants
  persistent memory of project conventions.

## Related AI Memory Files

- `CLAUDE.md` — entry point for Claude Code; delegates here.
- `.github/copilot-instructions.md` — entry point for GitHub Copilot;
  delegates here.

Keep those files pointing at this document; do not fork the content.
