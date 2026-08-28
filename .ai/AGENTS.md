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
- Language / runtime: F# on .NET 10.
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

CI runs on `windows-latest` via `.github/workflows/ci.yml` (.NET 10,
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

- 2026-08-28 — `Claus.advance` now keeps backward-compatible default kinetics,
  while `Claus.advanceWith` takes explicit `GasStream.ClausKinetics`
  parameters (`SeverityFactor`, `TauFactor`, `SubSteps`, Arrhenius pairs for
  Claus/COS/CS2). Default `kinetic` was intentionally softened so it no
  longer collapses toward `equilibrium` in short-residence WHB segments, and
  `Claus.calibrateSeverity` exists as the minimal hook for matching a real
  Claus case without reopening the solver architecture. Keep docs explicit:
  this is still a tunable surrogate, not rigorous reactor kinetics.
- 2026-08-28 — Added `Process/Claus.fs` and `gas.modello_claus` with
  `frozen | equilibrium | kinetic`. This is a bounded simplified closure for
  COS/CS2 hydrolysis and Claus conversion that marches composition cell by cell
  through `BundleSolver` and `Bypass`, then hands the generated elemental
  sulphur to `Process/Sulphur.fs` for allotrope equilibrium and condensation.
  It is intentionally a surrogate 1D process model, not rigorous equilibrium or
  catalyst-specific kinetics. When updating findings/docs/tests, keep that
  distinction explicit.
- 2026-08-28 — The Claus/sulphur work is now partially wired into the main WHB
  physics. `Process/Sulphur.fs` still remains the standalone SRU helper and
  `--sulphur` still exports the dedicated CSV sweep, but `BundleSolver`,
  `Bypass` and `Design` now couple only explicit elemental sulphur
  (`S2`/`S6`/`S8`) into the enthalpy/temperature inversion. This preserves the
  legacy path for ordinary syngas while making Claus-like inputs more physical
  without inventing conversion from `H2S`/`SO2`/`COS`/`CS2`. Reports expose
  `DesignResult.SulphurCoupling`; findings must continue to say clearly that
  generic Claus species remain screening-only unless elemental sulphur is in
  the input composition.
- 2026-08-28 — Added CLI command `--sulphur` for standalone sulphur CSV
  sweeps (pressure, S-atoms, inerts, temperature range) and integrated
  lightweight Claus screening into `Design.buildFindings`: service note,
  sulfidation, wet-H2S and sulphur-dew-point warning when explicit S2/S6/S8
  vapour is present. Deliberately STILL not coupled into `BundleSolver`: the
  main WHB heat balance remains unchanged and sulphur condensation stays a
  report-level screening unless a user explicitly asks for full process
  coupling.
- 2026-08-28 — Added `Process/Sulphur.fs` as a standalone Claus/SRU helper
  module and registered it in `Whb.Core.fsproj`. It covers S2/S6/S8 vapour
  equilibrium, dew point, saturation-capped condenser state, Colburn-Hougen
  condensation with dominant non-condensables, fogging, lambda-transition
  liquid properties, wall-window and H2S-related checks. Deliberately NOT
  wired into `BundleSolver` / `Design`: the standard WHB reference workflow
  must stay numerically untouched unless a user explicitly asks for Claus
  integration.
- 2026-08-28 — Integrated the low-risk part of the thermal-precision bundle:
  `GasProps` now supports 29 species with centralized `tryParseSpecies`
  parsing and ideal fallback for S2/S6/S8 in virial mode; `Json.composition`
  warns instead of dropping unknown species silently. Added shell-side
  `Water.FlowBoiling` (`chen` default, `kandlikar` optional), IF97 helpers
  `Steam.satT` / `Steam.saturationTable` plus CLI `--steamtable`. The
  standalone sulphur process module from the attachment was deliberately NOT
  merged yet because it is large, unwired into the current solve/report flow,
  and would have expanded the core beyond the requested update scope.
- 2026-08-14 — `--help` now prints a full manual (commands, options, project
  options keys, output files, exit codes) while `printUsage` stays as the
  short form shown on a usage error. Exit codes were previously undocumented
  anywhere: 0 ok, 1 unhandled, 2 usage, 3 GitHub transfer, 4 invalid JSON,
  5 file access. Open work moved to `TODO.md`, which a test keeps in step
  with the code so a stale claim there cannot survive a build.
- 2026-08-14 — Worked the improvement register from level 0 to level 4.
  Highlights and the reasoning behind them:
  - BUG FIXED: the adaptive bypass map stopped with the condition inverted
    (`last.TMix <= target`, but TMix RISES with bypass), so the grid never
    extended, `invertMap` clamped every valve limit to the map edge and the
    reference case missed its target by 0.8 K while reporting a valve window
    of zero width. Adaptive now reproduces the full-mode answer.
  - `ConvergenceReport` on `DesignResult` carries the numerical health of a
    run - coupled convergence, quality clamps, non-converged cells,
    circulation root count and slope, downcomer flashing margin, whether the
    bypass map brackets its target - and each of those raises a finding.
    A run that did not converge can no longer look like one that did.
  - `bisectWithStatus` and `countSignChanges` in `Constants.fs` exist so a
    clamped endpoint is distinguishable from a root and multiple roots are
    reported rather than silently resolved. See the Circulation entry below:
    this is the visibility that was missing, NOT a licence to change solver.
  - `TFeed` is finally used: net steam production alongside the bundle
    evaporation rate, and the feedwater subcooling that drives the new
    downcomer flashing check.
  - `WaterSide.chfLocal` is wired into the cell loop through
    `Water.ChfModel` (`vapore.modello_chf`), Palen kept as default so the
    reference numbers do not move.
  - `BundleSolver.BandDuty` feeds `Circulation.dpFieldColumnWith`: the
    hydraulics now sees the real per-band duty instead of a flat split.
  - Gas-side momentum term added and `DpGas` weighted by tube count; the
    reference dp moves 0.113 -> 0.099 bar, which is the expected pressure
    recovery of a decelerating, cooling stream.
  - Turbulent buffeting now takes part in the vibration verdict, and slug
    forces per bend plus their passing frequency are reported for two-phase
    lines. `Vibration.twoPhaseDamping` is a screening SHAPE reported as a
    sensitivity only - the case damping still decides the verdict.
  - `Optimizer/Optimization.fs` implements a bounded, deterministic
    constrained search reporting `OptimumKind`: interior / at constraint /
    at search bound / no feasible point. That classification is the point of
    the module - never report a position without saying what holds it.
  - NOT DONE, deliberately: parallel-tube flow redistribution (FIS-3) is a
    structural change to the marching solver, and the fouling runaway
    scenario and valve AIV screening were left out. They are described in the
    register and none of them is blocked by anything above.
- 2026-08-14 — Phase logging made effective on every calculation command.
  `--sizing` and `--loads` previously ran with `ignore` as the progress
  callback and created no logger at all, and `Options.load` deserialized
  straight onto the record, so an options file that omitted a section got
  `false`/`null`/`0` for it - i.e. any partial file silently disabled
  logging. `Options.load` now overlays the file onto `defaultOptions`
  (`overlay`, in `Options.fs`), so an absent key keeps its documented
  default and only an explicit `"enabled": false` turns logging off.
  Because bypass-map points run concurrently, the map now logs the
  completion and elapsed time of each point; a start-only message printed
  the whole grid at once and then went silent.
- 2026-08-14 — Performance pass on the calculation kernel: reference case
  38.7 s -> 2.7 s, with cell/axial/stress tables unchanged except in the
  last printed digit. What / where:
  - `Constants.fs`: added `brent` (bracketed, superlinear) and
    `newtonIncreasing` (safeguarded Newton for monotone residuals).
    `bisect` is unchanged and still used everywhere else.
  - `Shift.stateFromEnthalpyAt` now inverts enthalpy with
    `newtonIncreasing` using the mixture cp as derivative
    (`GasProps.enthalpyAbsRealWithCp`) instead of a 1e-4 K bisection:
    ~4 property evaluations instead of ~25, and converged to 1e-9 K.
    Safe because h(T) is strictly increasing, so the root is unique.
  - `Shift.compositionFromEnthalpyAt` skips the inversion entirely in
    `Frozen` mode, where the composition cannot change. This removed one
    of the two full inversions per cell in `BundleSolver`.
  - `GasProps.Virial`: pair pseudo-critical constants are temperature
    independent, so they are built once per species set and the symmetric
    double sum in `bMix` now runs over the upper triangle only.
  - `SteamIF97.reg2`: distinct powers of pi/b/tau are built once per call
    with `Math.Pow` (same values, ~1/3 of the calls). `viscosity` and
    `conductivity` short-circuit the density residual series at rho = 0,
    where its factor is exactly exp(0) = 1. Self-test output is
    bit-identical before/after.
  - `BundleSolver`: `shellContext`/`shellHtcWith` hoist the flux
    independent Chen and Zukauskas terms out of the heat-flux iteration
    (bit-identical, term order preserved); inner solve uses `brent` at
    1e-4 W/m instead of `bisect` at 1e-2 W/m.
  - `Design`: bypass-map base grid is evaluated concurrently
    (`RunSettings.Parallelism`, wired to `calculation.parallelism`), gas
    property cache moved to a `ConcurrentDictionary`. Map points are
    independent solves, so results and their order are unchanged.
  - DO NOT convert the `Circulation` root finds from `bisect` to `brent`.
    This was tried and reverted: the circulation residual has multiple
    roots and brent selects a different one, moving the circulation flow
    by roughly 9x and steam output by ~300 kg/h. Bisection's root is the
    established design answer there.
- 2026-08-13 — Introduced this `AGENTS.md` plus `CLAUDE.md` and
  `.github/copilot-instructions.md` shims to give AI coding assistants
  persistent memory of project conventions.
- 2026-08-28 — Adopted the AI Engineering Project Standard v2.0
  (`.ai/AI_ENGINEERING_PROJECT_STANDARD.md`, project-independent rules)
  plus the project profile (`.ai/AI_STARTER_INSTRUCTIONS.md`, which holds
  the WHB-specific stack, module map and declared exceptions). Rules are
  cited by stable id (`PRIN-3`, `PERS-2`, ...); never copy rule text into
  `AGENTS.md`, and record a deviation as an exception row in the profile
  instead of editing the standard.
- 2026-08-28 — Retargeted `net8.0` -> `net10.0` on all three projects,
  added `global.json` (SDK 10.0.201, `rollForward: latestFeature`) and
  moved CI to `dotnet-version: 10.0.x`. Verified: 66/66 tests pass and
  `--selftest` output is byte-identical between the `net8.0` and `net10.0`
  Release builds of the same source, so no numerical drift. `Ganfoss.ROP`
  stays at 1.0.2. `NumericInput.Core` / `NumericInput.WPF` 1.3.0 are
  declared in the profile for the future WPF UI and deliberately NOT
  referenced from `Whb.Core` — a UI input package in the calculation core
  would violate `PRIN-3`.

## Related AI Memory Files

- `.ai/AI_ENGINEERING_PROJECT_STANDARD.md` — project-independent rules,
  cited by stable id.
- `.ai/AI_STARTER_INSTRUCTIONS.md` — WHB project profile: stack, module
  map, declared exceptions to the standard.
- `.ai/README.md` — what each file in this folder is for.

Root-level pointers, kept where each tool discovers them:

- `AGENTS.md` — generic entry point; delegates here.
- `CLAUDE.md` — entry point for Claude Code; delegates here.
- `.github/copilot-instructions.md` — entry point for GitHub Copilot;
  delegates here.

Keep those files pointing at this document; do not fork the content.
