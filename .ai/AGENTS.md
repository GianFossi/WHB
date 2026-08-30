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
.\macro\build.ps1           # restore + build + test in Release
.\macro\build.ps1 -Task Build
                             # restore + build
.\macro\build.ps1 -Task Test
                             # restore + build + test
.\macro\build.ps1 -Task Rebuild
                             # clean + restore + build + test
.\macro\build.ps1 -Task Clean
                             # clean build outputs and generated folders
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
  Components/Equipment/BundleGeometry.fs
                             pure bundle-envelope alignment helpers used by
                             shared optimize/design geometry updates
  Solvers/BundleSolver*.fs   coupled gas/water bundle solve split into
                             contracts, low-level kernels, support and orchestration
  Solvers/Circulation*.fs    natural-circulation loop solve split into
                             contracts, hydraulics, pipeline and orchestration
  Designers/Design.fs        top-level composition and result assembly
  Designers/DesignThermalProcess.fs
                             shared thermal/process verification stage
  Designers/DesignMechanical.fs
                             mechanical screening stage on shared contracts
  Designers/DesignContracts.fs
                             typed handoff between verification stages
  Designers/DesignRuntime.fs shared run settings and progress contracts
  Modes/*.fs                 rating / optimize / design shared-engine modes
  Reports/*.fs               text, CSV and HTML reports
  Options/Defaults.fs        built-in reference case

src/Whb.Cli/
  Program.fs                 CLI, mode dispatch, reports and self-test
  Json.fs                    JSON readers and input helpers

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
   - Respect the stable project keys already used in `whb.options.json`
     (`folders`, `logging`, `reporting`, `calculation`). New keys must
     have documented defaults.
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
    - Never commit API keys, tokens or credentials. Keep any local
      machine/user state such as recent-file history outside the
      versioned project options file.

## Modification Memory Log

Record here notable, non-obvious modification decisions so future AI
sessions can reuse the context. Append new entries at the top with an
ISO date. Keep each entry short (what / why / where).

- 2026-08-29 — Refreshed the repository maps and backlog wording after the
  shared-verification split so the docs no longer conflate the new
  `--optimize` geometry optimizer with `--optimize-legacy`, and the
  canonical layout now lists the separated thermal/process, mechanical and
  mode-orchestration modules. Keep future documentation aligned with the
  shared-engine `rating` / `optimize` / `design` scheme.
- 2026-08-30 — Centralized CLI output-path normalization in
  `Whb.Cli/OutputPaths.fs`. Report-producing commands now always write under
  the configured `results` root: `--out` chooses a subpath beneath that root,
  and `--steamtable` / `--sulphur` positional file paths are folded back under
  it too if they point elsewhere. Keep future report artifacts on that same
  path policy instead of writing ad-hoc files beside the repo root.
- 2026-08-30 — Removed `RecentFiles` from `Options.ProjectOptions` and moved
  local history into `Whb.Cli/RecentFilesStore.fs`, persisted as
  `.user/recent-files.json`. `whb.options.json` is now reserved for stable
  project/runtime configuration (`folders`, `logging`, `reporting`,
  `calculation`), while volatile machine-local state stays outside the
  versioned options template.
- 2026-08-30 — Added `macro/test.ps1` as the shared live test runner for the
  solution. `macro/build.ps1 -Task Test|Rebuild` and the VS Code `test` task
  now route through it so long xUnit runs emit periodic heartbeats (elapsed
  time, related `dotnet`/`testhost`, other concurrent test processes, and a
  possible-stall signal) instead of staying silent until completion.
- 2026-08-30 — Added `Components/Equipment/BundleGeometry.fs` as the shared
  pure geometry-alignment module for the shared `optimize` / `design` variable
  path. Changing tube count, OD, or pitch now recomputes `OTL`, shell ID, and
  baffle OD top-down from the current-case calibration instead of leaving stale
  envelope dimensions behind. The shell rebuild follows
  `Shell.ID = OTL + 2 * (3 * Thk.Tubesheet + Rknuckle)` with `Rknuckle` fixed
  at `120 mm`; the tubesheet-thickness proxy is inferred from the current
  geometry so unchanged cases remain unchanged. An explicitly varied shell ID
  still overrides the auto-derived one.
- 2026-08-30 — Extended the shared geometry-variable path with tube outer
  diameter and coupled tube-size options. `Optimize.VariableKey` now includes
  `TubeOuterDiameterM`, keeping the original tube wall thickness when only the
  OD is moved, and greenfield `design.spazio` now accepts `taglie_tubo`
  entries with paired `do_mm` / `passo_mm` (or `od_mm` / `pitch_mm`) so
  discrete studies can respect fixed pitch-vs-OD tables instead of generating
  arbitrary pairings. `design.txt`, `README.md`, `docs/INPUT_SCHEMA.md`, and
  tests were updated accordingly.
- 2026-08-29 — Audited the DNBR thresholds against the repository documents.
  No repo-traceable external source was found for the old local `1.43`
  criterion (`1/0.7`), while the docs and default constraints already
  declared `DNBR >= 2.0` as the project criterion. `WaterSide.dnbrRequired`,
  findings text, validation notes and tests are now aligned to the single
  documented threshold.
- 2026-08-29 — Promoted the DNBR project criterion into the case options as
  `vapore.dnbr_min` (default `2.0`). The local boiling-crisis screening, the
  default shared-mode constraint set, the legacy optimizer defaults, the
  sizing defaults, and the report text/HTML thresholds now all read the same
  case-level value instead of hard-coding `2.0` in multiple places.
- 2026-08-29 — Refined bypass-map progress reporting so the CLI no longer
  keeps the last completed fraction while a bypass point is already running.
  `Designers/DesignBypass.fs` now reports both point launch and point
  completion with structured fractions, which makes the CLI ETA less jumpy
  during the parallel adaptive-map phase without mixing solver logic and UI.
- 2026-08-29 — Pushed structured progress one level deeper inside the bypass
  point solve. `Designers/DesignThermalProcess.fs` now reports coupled
  bundle/circulation correction steps for each map point, and
  `Components/Equipment/Bypass.fs` exposes `marchWithProgress` so the axial
  bypass march can contribute its own coarse subprogress. `DesignBypass.fs`
  aggregates those per-point fractions across concurrent map points, which
  makes the CLI ETA and activity text more informative during the longest
  internal solve phase. Covered by `tests/Whb.Tests/ProgressTests.fs`.
- 2026-08-29 — Moved the repository PowerShell helper scripts from the root
  into `macro/` (`macro/build.ps1`, `macro/clean.ps1`). `macro/build.ps1`
  now resolves the repo root from its parent directory, and docs/pointers
  were updated accordingly. VS Code `tasks.json` and `launch.json` were
  checked after the move: they use `dotnet` directly, so no path change was
  required there.
- 2026-08-29 — Reworked CLI progress reporting to use typed progress updates
  instead of plain text wherever the shared verification flow knows real work
  completion. `Options/ProgressModel.fs` now carries the reusable progress
  contract, `Design` / `LoadCases` / `Rating` / `Optimize` / `GreenfieldDesign`
  propagate structured fractions, and `Whb.Cli/Progress.fs` computes ETA from
  reported fraction first, falling back to nominal command duration only when
  no real progress fraction is available.
- 2026-08-29 — Exposed the prepared mechanical-sizing interface as a public
  report artifact. `Reports/Report.MechanicalInterface.fs` renders the
  immutable future-calculation inputs from the same shared verification path,
  `MechanicalDesignInterface.fromDesignResult` rebuilds that interface from a
  `DesignResult` without duplicating formulas, and the CLI now writes
  `interfaccia_meccanica.txt` for normal runs plus the shared-engine
  `--rating`, `--optimize`, and `--design` modes.
- 2026-08-29 — Fixed a scoring defect in `Modes/Optimize.scoreObjective`:
  an omitted objective `Scale` must mean "use raw engineering units", not
  "renormalize by the candidate itself". The previous behavior collapsed
  positive single-metric objectives to a constant and could leave `optimize`
  pinned to its starting point unless a required constraint forced movement.
  `README.md`, `docs/INPUT_SCHEMA.md`, and `tests/Whb.Tests/ModeConstraintAuditTests.fs`
  now document and lock the corrected behavior.
- 2026-08-29 — Added a separate mechanical-sizing interface layer in
  `Designers/MechanicalDesignContracts.fs` and
  `Designers/MechanicalDesignInterface.fs`. It does NOT perform code
  thickness calculations yet; it prepares typed, immutable inputs for future
  tube, shell, channel, bypass, crevice-free weld, and tubesheet sizing,
  while explicitly marking missing geometry such as channel dimensions or
  external-pressure design cases. `DesignMechanical.runPure` now returns that
  package under `MechanicalStageResult.CalculationInterface`.
- 2026-08-29 — Split the two largest internal solver modules by functional
  unit so the top-level files now read as orchestration only.
  `BundleSolver` is separated into `BundleSolver.Contracts`,
  `BundleSolver.Foundation`, `BundleSolver.CellKernel`, `BundleSolver.Support`
  and a thin `BundleSolver.fs`; `Circulation` is separated into
  `Circulation.Contracts`, `Circulation.Hydraulics`, `Circulation.Pipeline`
  and a thin `Circulation.fs`. Keep future solver work flowing top-down in the
  public file and push detail into focused companion files before those files
  become monolithic again.
- 2026-08-29 — Reframed the public core workflows as explicit top-down
  function compositions. `Design.fs`, `VerificationEngine.fs`, `LoadCases.fs`,
  `PerformanceAssessment.fs`, `Rating.fs`, `Optimize.fs`, and
  `GreenfieldDesign.fs` now read as staged pipelines from general intent to
  detailed evaluation, while `DesignThermalProcess.runPure` and
  `DesignMechanical.runPure` expose side-effect-free shared verification
  entry points. `docs/FUNCTION_COMPOSITION_ARCHITECTURE.md` and the added
  determinism tests document and verify the purity boundary.
- 2026-08-29 — Wired the shared-verification mode architecture into the CLI.
  `src/Whb.Cli/Program.fs` now exposes first-class `--rating`,
  `--optimize`, and `--design` commands that all reuse
  `Modes/VerificationEngine` through the mode modules, with optional JSON
  sections `vincoli`, `rating`, `optimize`, and `design`. The previous
  maximize-duty search remains available as `--optimize-legacy`, so future
  changes should treat the new `--optimize` as the user-facing geometry
  optimizer and avoid adding parallel solver/report paths around it.
- 2026-08-29 — Split the top-level WHB orchestration into
  `Designers/DesignThermalProcess.fs`, `Designers/DesignMechanical.fs`,
  `Designers/DesignRuntime.fs` and `Designers/DesignContracts.fs`, with
  `Design.fs` reduced to composition plus findings/result assembly. The
  thermal/process verification engine remains the single source of truth;
  the mechanical stage now consumes a typed shared contract instead of
  sharing orchestration state. Keep future rating/optimize/design modes
  layered on that same contract rather than duplicating solver logic.
- 2026-08-29 — Added first-class shared-verification modes in
  `Modes/VerificationEngine.fs`, `LoadCases.fs`, `ConstraintModel.fs`,
  `ConstraintReaders.fs`, `PerformanceAssessment.fs`, `Rating.fs`,
  `Optimize.fs` and `GreenfieldDesign.fs`. Constraints are now explicit
  data that can cover process, thermal, hydraulic, numerical, weight and
  envelope metrics, while `Rating`, `Optimize` and greenfield candidate
  selection all call the same verification engine instead of embedding
  parallel solver logic.
- 2026-08-28 — Adopted AI Engineering Project Standard v2.1 in
  `.ai/AI_ENGINEERING_PROJECT_STANDARD.md`: purity/statelessness is now
  explicit by default for calculation work, top-down decomposition has a
  dedicated architecture rule, and the assistant workflow now ends with a
  formal validation checklist. Project files only reference that guidance
  rather than duplicating it.
- 2026-08-28 — Adopted AI Engineering Project Standard v2.2 in
  `.ai/AI_ENGINEERING_PROJECT_STANDARD.md`: non-trivial tasks now require a
  structure-first pass (`DOC-8`) that fixes task hierarchy, typed pipeline,
  intermediate contracts and pure-vs-I/O boundaries before atomic
  implementation begins.
- 2026-08-28 — Adopted AI Engineering Project Standard v2.3 in
  `.ai/AI_ENGINEERING_PROJECT_STANDARD.md`: English XML-style documentation
  comments are now the default for public APIs and important code elements,
  and concise inline comments are expected for non-obvious steps, units,
  assumptions, invariants and numerical guards. The standard explicitly avoids
  trivial line-by-line narration.
- 2026-08-28 — Extracted the post-processing campaign blocks from
  `Design.fs` into `Designers/DesignVibration.fs`,
  `Designers/DesignSensitivity.fs` and `Designers/DesignTransient.fs`.
  This keeps the top-level run focused on orchestration while preserving
  the existing calculations, thresholds and result assembly verbatim.
- 2026-08-28 — Extracted bypass-map adaptation, interpolation/inversion
  and valve-window assembly from `Design.fs` into
  `Designers/DesignBypass.fs`. `Design.fs` still owns the coupled solve
  and point evaluation; the new module only organizes how those points
  are sampled and interpreted, so the numerical path stays unchanged.
- 2026-08-28 — Extracted the thermal post-processing block
  (`CHF` comparisons, local correlation sensitivity, fouling cases) from
  `Design.fs` into `Designers/DesignThermalPost.fs`. This is report-side
  analysis built on already solved cells, so it reduces the size of the
  main orchestrator without touching the primary solve.
- 2026-08-28 — Split `Design.buildFindings` into
  `Designers/Findings.fs` and left `Design.fs` with a thin wrapper.
  This is a structural-only extraction to reduce the main orchestration
  file without changing finding thresholds, wording or result order.
- 2026-08-28 — Expanded `build.ps1` `Clean` to remove root generated
  outputs too (`results*`, `risultati*`, `tmp`, `logs`,
  `artifacts/packages`, `tmp_run_*.txt`) and added a repo-root guard
  around recursive deletes. This keeps stale `net8.0` migration debris
  and probe outputs from accumulating while making cleanup safer.
- 2026-08-28 — Added `Process/SulphurCondenser.fs` plus
  `DesignCase.SulphurCondenser` / `DesignResult.SulphurCondenserResult`.
  This is a dedicated 1D Claus sulphur-condenser rating/screening unit with
  its own feed, target outlet temperature, assumed wall/coolant temperatures,
  assumed `U`, residence time, pressure drop, text report and axial CSV.
  Normal WHB runs now execute it when `condensatore_zolfo.presente = true`; it
  may take inlet either from `condensatore_zolfo.gas_ingresso` or from the
  solved mixed WHB outlet when `usa_uscita_whb = true`. Keep docs explicit
  that this is a downstream integration and not a detailed exchanger geometry
  model.
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
- 2026-08-28 — Consolidated every AI instruction file under `.ai/`:
  `AGENTS.md` moved from the repository root to `.ai/AGENTS.md`, the
  standard and the project profile moved from `docs/` to `.ai/`, and
  `.ai/README.md` rewritten as the folder index. Removed `.ai/CLAUDE.md`,
  `.ai/GEMINI.md`, `.ai/PROJECT_MEMORY.md` and the old `.ai/README.md`:
  all four were copies from the unrelated BismarckGame project (564 lines,
  zero WHB content) and gave assistants wrong context. Removed `.ai/` from
  `.gitignore` — the folder now holds the canonical memory and must be
  versioned. Root `AGENTS.md`, `CLAUDE.md` and
  `.github/copilot-instructions.md` remain as pointers because each tool
  discovers them at those paths; they carry no rule text.

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
