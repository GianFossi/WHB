# whb

Thermal, hydraulic and diagnostic calculations for fire-tube Waste Heat Boilers
(WHB) / Process Gas Coolers (PGC), written in F# for .NET 10.

The model is intended for engineering study of WHB units where process gas flows
inside tubes and boiling water/steam circulates naturally on the shell side with
an elevated steam drum.

## Documentation Map

- [What the software does](docs/SCOPE.md)
- [Main assumptions](docs/ASSUMPTIONS.md)
- [Theory recap](docs/THEORY.md)
- [Function-composition architecture](docs/FUNCTION_COMPOSITION_ARCHITECTURE.md)
- [Limitations and simplifications](docs/LIMITATIONS.md)
- [Acronyms and terms](docs/ACRONYMS.md)
- [Input schema and options](docs/INPUT_SCHEMA.md)
- [Validation and regression benchmarks](docs/VALIDATION.md)
- [Work list and backlog](TODO.md)
- [Detailed correlation and issue notes](DOC_correlazioni_e_problematiche.md)
- [AI assistant memory and modification rules](.ai/AGENTS.md)

> Warning: this software is an engineering calculation aid, not a certified
> pressure-vessel, boiler-code or safety-integrity tool. Always review results
> against project datasheets, vendor drawings, applicable codes and qualified
> engineering judgment before using them for design decisions.

## Prerequisites

- .NET SDK 10.0 or newer.
- PowerShell 7 or Windows PowerShell for the helper scripts.
- Windows, Linux or macOS for `dotnet` build/test; the provided scripts are
  PowerShell-oriented.
- Optional: GitHub Actions, if you want to use the included CI workflow.

Check your SDK:

```bash
dotnet --info
```

## Projects

- `Whb.Equipment`: shared physical model for components, assembled equipment,
  piping routes, BOM items and material-property lookup.
- `Whb.Core`: calculation library and domain model.
- `Whb.Cli`: command-line application, JSON input loader and report generator.
- `Whb.Tests`: xUnit test project for core numerical and utility behavior.

## Current Release

- Version: `1.0.0`
- Default branch: `main`
- Repository: <https://github.com/GianFossi/WHB>
- Main package/readme target: GitHub and NuGet package consumers.

## Quick Start

Build and test:

```powershell
.\macro\build.ps1
```

Run the built-in reference case:

```bash
dotnet run --project src/Whb.Cli
```

Generate an editable JSON input file:

```bash
dotnet run --project src/Whb.Cli -- --template case.json
```

Run a custom case:

```bash
dotnet run --project src/Whb.Cli -- case.json --out results
```

During long calculations the CLI shows a console status window with an
estimated progress bar, the current task description, elapsed time and estimated
remaining time.

The CLI also supports timestamped phase logging through `whb.options.json`.
Logging is on by default for every calculation command (the default run,
`--sizing` and `--loads`); keys omitted from an options file keep their
documented defaults, so logging stays on unless `logging.enabled` is set to
`false` explicitly. By default logs are written to `logs/whb-run.log`,
temporary/service files should be placed under `temp/` (`tmp/` remains ignored
for backward compatibility), and preflight checks verify active runs,
read/write access and disk space before the calculation starts.

Recent case/options history is not kept inside `whb.options.json`: it is stored
locally in `.user/recent-files.json`, which is intended as machine-local state
and is ignored by Git.

Bypass-map points are solved concurrently (see `calculation.parallelism`), so
the log records the completion and elapsed time of each point rather than only
its start.

The same options file controls whether full engineering reports are generated.
Summary, criticality, PDS comparison and inventory outputs are always written;
`reporting.generateFullReport` and `reporting.generateHtmlReport` add the full
text and HTML reports.

Thermal precision/performance options include `calculation.bypassMapMode`
(`adaptive`, `fast`, `full`, `fixed`), `calculation.bypassTargetToleranceK`,
`calculation.gasPropertyCache`, and
`calculation.correlationValidityWarnings`.

Run internal correlation checks:

```bash
dotnet run --project src/Whb.Cli -- --selftest
```

Export a saturation table for quick checks or datasheet work:

```bash
dotnet run --project src/Whb.Cli -- --steamtable steam.csv --tmin 20 --tmax 310 --step 10
```

Export a standalone sulphur sweep for Claus/SRU screening:

```bash
dotnet run --project src/Whb.Cli -- --sulphur sulphur.csv --pressure-bara 1.7 --s-atoms-mols 8 --inert-mols 100 --tmin 120 --tmax 350 --step 10
```

## Examples

Generate both input templates:

```bash
dotnet run --project src/Whb.Cli -- --template my-case.json
dotnet run --project src/Whb.Cli -- --options-template my-options.json
```

Run the reference case with explicit options and output folder:

```bash
dotnet run --project src/Whb.Cli -- --options whb.options.json --out results/reference
```

Run a custom case and store outputs in a dedicated folder:

```bash
dotnet run --project src/Whb.Cli -- my-case.json --options whb.options.json --out results/my-case
```

Curated study inputs used during design/optimization work are kept under
`demo/cases/` so the repository root stays reserved for the main project files.

Generate partial-load curves from a case file:

```bash
dotnet run --project src/Whb.Cli -- --loads my-case.json --out results/load-curves
```

Run the shared-engine rating mode on configured load cases:

```bash
dotnet run --project src/Whb.Cli -- --rating my-case.json --out results/rating
```

Optimize an existing geometry against shared constraints and objectives:

```bash
dotnet run --project src/Whb.Cli -- --optimize my-case.json --out results/optimize
```

Explore a discrete greenfield design space:

```bash
dotnet run --project src/Whb.Cli -- --design my-case.json --out results/design
```

For greenfield studies where tube pitch depends on tube OD, use
`design.spazio.taglie_tubo` to pass explicit `(do_mm, passo_mm)` pairs instead
of separate OD and pitch lists.

When `numero_tubi`, `do_mm`, or `passo_tubi_mm` move through the shared
`--optimize` / `--design` geometry path, the dependent envelope is now
realigned automatically. `OTL` is recomputed from tube count, pitch, and the
current `ITL`, then the WHB shell ID is rebuilt from
`OTL + 2 * (3 * Thk.Tubesheet + Rknuckle)` with `Rknuckle = 120 mm`, while the
current shell-to-baffle gap is preserved. If `mantello_id_mm` is also varied
explicitly, that manual shell ID still overrides the auto-derived one.

Generate the automatic sizing report:

```bash
dotnet run --project src/Whb.Cli -- --sizing my-case.json --out results/sizing
```

Check installed correlations and reference values:

```bash
dotnet run --project src/Whb.Cli -- --selftest
```

Write a steam saturation table:

```bash
dotnet run --project src/Whb.Cli -- --steamtable results/steam.csv --tmin 50 --tmax 300 --step 25
```

Write a sulphur-process CSV for dew-point and condensation screening:

```bash
dotnet run --project src/Whb.Cli -- --sulphur results/sulphur.csv --pressure-bara 1.7 --s-atoms-mols 8 --inert-mols 100 --tmin 130 --tmax 300 --step 5
```

## CLI Commands

```text
whb [case.json] [--out <folder>] [--options <whb.options.json>]
whb --template [file.json]
whb --options-template [file.json]
whb --selftest
whb --steamtable [file.csv] [--tmin <C>] [--tmax <C>] [--step <C>]
whb --sulphur [file.csv] [--pressure-bara <bar>] [--s-atoms-mols <mol>] [--inert-mols <mol>] [--tmin <C>] [--tmax <C>] [--step <C>]
whb --sulphur-condenser [case.json] [--out <folder>]
whb --rating [case.json] [--out <folder>]
whb --design [case.json] [--out <folder>]
whb --loads [case.json] [--out <folder>]
whb --sizing [case.json] [--out <folder>]
whb --optimize [case.json] [--out <folder>]
whb --optimize-legacy [case.json] [--out <folder>]
whb --github-plan [options.json]
whb --github-push [options.json]
whb --help
```

`whb --help` prints the complete manual: every command, every option, the keys of
the project options file, the files each command writes, and the exit codes.

If no case file is supplied, the reference case is executed.

The shared-engine mode family is now split explicitly into three commands:

- `--rating` verifies one fixed geometry against the configured load cases and
  constraints, writing `rating.txt`, `rating.csv`, and
  `interfaccia_meccanica.txt`.
- `--optimize` changes an existing geometry within configured bounds to minimize
  weight and envelope objectives while reusing the exact same thermal/process
  and mechanical verification engine used by rating, also exporting
  `interfaccia_meccanica.txt` for the best geometry across the governing load
  cases.
- `--design` explores a discrete greenfield geometry space, again with the same
  verification engine and the same constraint language, also exporting
  `interfaccia_meccanica.txt` for the best candidate.

The optional JSON sections are `vincoli`, `rating`, `optimize`, and `design`.
See `docs/INPUT_SCHEMA.md` and the generated template for the exact shape.
For mixed-unit objectives, provide `scala` on each objective term; if omitted,
the term is evaluated in its raw engineering units.

`--optimize-legacy` preserves the previous constrained search over ferrule
length and tube length for the largest duty that still satisfies the design
limits. It now writes `ottimizzazione_legacy.txt`.

`--sulphur` is a standalone Claus/SRU utility. It writes a temperature sweep with
S2/S6/S8 equilibrium, sulphur saturation pressure, condensation onset and
condensed fraction. Normal WHB runs raise sulphur-related findings when the gas
contains Claus species. If `gas.modello_claus` is left at `frozen`, only
explicit elemental sulphur (`S2`/`S6`/`S8`) is coupled into the main WHB
thermal solve. If `gas.modello_claus = equilibrium|kinetic`, the solve also
closes `H2S`/`SO2`/`COS`/`CS2` to a bounded Claus surrogate, generates
elemental sulphur, and couples its dew point and condensation into the bundle
enthalpy balance. The `kinetic` branch now exposes `gas.claus_cinetica.*`
parameters so aggressiveness can be reduced and later calibrated on a real
Claus case without editing source constants.

`--sulphur-condenser` is the dedicated Claus sulphur-condenser path. It reads
the `condensatore_zolfo` section of the case file and writes a dedicated text
report plus axial CSV. If `condensatore_zolfo.usa_uscita_whb = true`, the WHB
calculation is run first and the solved mixed outlet stream becomes the inlet of
the condenser module. If `false`, the condenser runs on its own
`condensatore_zolfo.gas_ingresso` feed.

## Local Tasks

```powershell
.\macro\build.ps1                   # restore + build + test in Release
.\macro\build.ps1 -Task Build       # restore + build
.\macro\build.ps1 -Task Test        # restore + build + test
.\macro\build.ps1 -Task Rebuild     # clean + restore + build + test
.\macro\build.ps1 -Task Clean       # clean build outputs and generated folders
.\macro\clean.ps1                   # shortcut for clean
```

## NuGet Usage

The calculation library is `Whb.Core`. If published as a NuGet package, consume it
from another .NET project with:

```bash
dotnet add package Whb.Core
```

Then reference the public namespaces from F#:

```fsharp
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types

let case0 = Defaults.referenceCase
let result = Design.run case0
printfn "Duty: %.3f MW" (result.Duty / 1e6)
```

For CLI distribution, publish `Whb.Cli` as an executable package or tool according
to your release process. This repository currently builds the CLI from source.

## Features

- Two-dimensional WHB calculation grid: axial sections (`NZ`) by vertical bundle
  bands (`NY`).
- Gas-side enthalpy march for each tube-band class.
- Shell-side boiling model with local heat flux, void fraction and circulation
  diagnostics, with `chen` as the historical default and optional
  `kandlikar` flow-boiling screening.
- Natural-circulation loop model with downcomer/riser branch losses.
- Steam drum and drum-internals pressure-drop representation, including
  preliminary calm-box sizing, top-opening/waterfall losses, and downcomer
  entry loss with vortex breaker.
- Internal bypass model with mixing temperature and valve-position checks.
- Water-gas shift modes: frozen, equilibrium above a freeze temperature, and
  fractional approach.
- Gas mixture properties with Wilke and molar-average options.
- Extended gas-species set for syngas, Claus/SRU, TLE and flue-gas studies.
- Gas model selection with ideal-gas or `realistico`/virial real-gas correction.
- IAPWS-IF97 water/steam helper properties for regions used by the model.
- Saturation-table export and explicit saturation cross-check correlations for
  quick screening work.
- Standalone sulphur-process module for Claus/SRU studies: allotrope
  equilibrium, sulphur dew point, condensation with non-condensables, lambda
  transition and wall-window checks, plus CLI CSV export and Claus-aware report
  screening in normal WHB runs. Explicit `S2`/`S6`/`S8` are also coupled into
  the main bundle solve, and `gas.modello_claus = equilibrium|kinetic` adds a
  simplified closed conversion path for `H2S`/`SO2`/`COS`/`CS2`, with exposed
  `gas.claus_cinetica.*` tuning parameters for the kinetic branch.
- Dedicated sulphur-condenser module for Claus projects, with its own case
  section, solver, report and axial CSV, usable either as a standalone unit or
  as an integrated downstream extension of the base WHB calculation.
- Metal temperature estimates through fouling, ferrule, wall and water-side
  deposit resistances.
- Ferrule component checks for pressure drop and insulation paper radial
  thickness fit.
- Materials catalogue with indicative thermal/mechanical limits and metal
  dusting windows.
- Vibration, nozzle, maldistribution and mechanical-stress diagnostics.
- Text, CSV and self-contained HTML reports.
- Partial-load curve generation from 50% to 110% gas flow.
- Shared-engine `rating`, `optimize`, and greenfield `design` modes with
  explicit load cases, constraints, objectives, and design spaces from JSON.

## Capabilities

The code can estimate:

- gas outlet temperature and axial temperature profile;
- exchanged duty and steam production;
- local and peak heat flux;
- metal temperatures and hot spots;
- DNBR and boiling-crisis margins;
- natural-circulation ratio and hydraulic driving head;
- gas-side and water-side pressure losses;
- bypass fraction, mixed outlet temperature and valve opening;
- riser/downcomer velocities and `rho v^2`;
- calm-box hydraulic losses for riser discharge, box transit, outlet opening,
  water fall and downcomer entry;
- tube vibration margins and load diagnostics;
- partial and total water inventory in the WHB shell, risers, downcomers and
  steam drum;
- estimated metal weight for tubes, shell, baffles, ferrules, risers,
  downcomers, steam drum shell and bypass pipe/liner;
- mandatory comparison against available client PDS values;
- output tables suitable for review in spreadsheets.

## Output Files

Default output is written to `results/` unless `--out` is provided.
When `--out` is provided, the requested path is still normalized under
`results/`: `--out run1` writes to `results/run1`, while an external or
absolute path is folded back under the same `results/` tree.

| File | Content |
|---|---|
| `report.txt` | Full engineering report, when `reporting.generateFullReport` is enabled |
| `report.html` | Self-contained full HTML report, when `reporting.generateHtmlReport` is enabled |
| `criticita.txt` | Summary of findings and warnings |
| `pds_comparison.txt` | Mandatory comparison between app output and available client PDS data |
| `pds_comparison.csv` | Spreadsheet-ready PDS comparison table |
| `inventory_summary.txt` | Water-volume and estimated metal-weight summary |
| `inventory_summary.csv` | Spreadsheet-ready inventory summary |
| `interfaccia_meccanica.txt` | Prepared interface for future detailed mechanical code calculations, generated from the same shared verification path |
| `celle.csv` | One row per calculation cell |
| `profilo_assiale.csv` | Axial aggregate profile |
| `tensioni.csv` | Stress/check table |
| `valvola_bypass.csv` | Bypass valve data |
| `maldistribuzione.txt` | Maldistribution notes |
| `vibrazioni.txt` | Vibration checks |
| `dimensionamento.txt` | Sizing sheet; the only file written by `--sizing` |
| `rating.txt` / `rating.csv` | Shared-engine geometry rating over configured load cases |
| `carichi.txt` / `carichi.csv` | Partial-load curves, written by `--loads` |
| `ottimizzazione.txt` | Shared-engine optimize result, written by `--optimize` |
| `ottimizzazione_legacy.txt` | Legacy maximize-duty search result, written by `--optimize-legacy` |
| `design.txt` | Shared-engine greenfield design result, written by `--design` |
| `sulphur_table.csv` | Standalone sulphur temperature sweep, written by `--sulphur` |
| `sulphur_condenser.txt` | Dedicated sulphur-condenser report, written by `--sulphur-condenser` and by normal runs when `condensatore_zolfo.presente = true` |
| `sulphur_condenser_profile.csv` | Segment-by-segment sulphur-condenser profile |

## Validation

The built-in `--selftest` command checks:

- IAPWS-IF97 reference points for saturation and regions 1/2;
- viscosity and thermal conductivity reference values;
- selected gas-property values for air and the reference syngas mixture;
- real-gas virial correction traces used by the gas model.

The repository test suite currently covers 103 tests across core numerical
utilities and root finders, unit conversions, grid generation, piping geometry
helpers, material lookup, heat-transfer behavior, two-phase multipliers, the
enthalpy inversion, the constrained search and its optimum classification,
validation tables, options-file merging, Claus/sulphur behavior, and the
dedicated sulphur-condenser path, plus the standalone equipment/component
model.

Run:

```powershell
.\macro\build.ps1 -Task Test
```

Or with the official live test frontend:

```bash
dotnet build WhbDesign.sln -c Release
pwsh ./macro/test.ps1 -Target ./WhbDesign.sln -Configuration Release -NoRestore
```

`macro/test.ps1` is the repository's official test frontend. It keeps the
underlying `dotnet test` output live and prints a periodic heartbeat with
elapsed time, related `dotnet` / `testhost` counts, other concurrent test
processes, and a possible-stall warning when the run goes quiet. CI also uses
this script instead of calling `dotnet test` directly.

## Reference Case Check

The included reference case represents a secondary-reformer WHB/PGC. Current
alignment against the available datasheet is:

| Quantity | Datasheet, flows +10% | Calculated | Difference |
|---|---:|---:|---:|
| Exchanged duty | 116.614 MW | 116.674 MW | +0.05% |
| Steam production | 347,743 kg/h | 347,798 kg/h | +0.02% |
| Gas outlet temperature | 355.0 °C | 348.5 °C | -6.5 K |
| Gas-side pressure drop | <= 0.30 bar | 0.113 bar | within limit |

Every normal CLI run also writes `pds_comparison.txt` and `pds_comparison.csv`.
These files must be reviewed with the main report so calculated output is always
checked against the available client PDS data.

## Warnings

- Do not use results as the sole basis for pressure-part design, relief-system
  sizing, trip settings, warranty guarantees or code compliance.
- Operating pressures are absolute (`bara` in inputs and reports where marked);
  pressure drops are differential values.
- Material properties are indicative and must be checked against the governing
  code edition, material certificates and design temperature range.
- Correlations may be outside their original experimental range for unusual
  pressure, composition, geometry, heat flux or quality conditions.
- Vendor drawing dimensions, plugged/no-flow nozzles, baffle clearances, drum
  internals and bypass geometry can dominate the result. Confirm them before
  accepting calculated margins.
- HTML/CSV reports are calculation outputs, not controlled design documents.

## Known Limits

- IAPWS region 3 is not implemented; steam pressure is practically limited to
  about 165 bar for this model.
- Methanation is not modeled.
- Water-gas shift is frozen by default, matching the reference datasheet
  assumption of unchanged molecular weight.
- The base gas model is ideal-gas with user-provided compressibility factor.
  The built-in reference case and the `realistico` option enable the currently
  implemented virial correction, which is still limited and should be validated
  for high-pressure syngas.
- The shell-side model assumes a distributed bundle representation, not a full
  CFD simulation.
- Axial distribution uses a local circulation-ratio approach; the alternate
  internal recirculation mode is available but disabled by default.
- Drum-internals pressure drop defaults to an engineering estimate and should be
  replaced with vendor data when available.
- Palen bundle CHF is conservative for forced crossflow and should be reviewed
  against the project boiling-crisis criterion configured in
  `vapore.dnbr_min`.
- CKTI/Blokh-style gas emissivity assumptions should be used carefully at high
  pressure and short optical path length.

## Repository Layout

```text
src/Whb.Equipment/
  Common/Bom.fs              `BomItem = { Id; Description; Quantity; Unit }`
  Common/Metrics.fs          derived weight/volume/area breakdowns
  Materials/MaterialCatalog.fs
                             material-property lookup abstraction and built-in data
  Components/Geometry.fs     reusable primitive/composite geometries
  Components/ComponentModel.fs
                             common recursive component record for leaves and assemblies
  Components/PressureParts.fs
                             standard component builders for shells, tubes, heads, nozzles
  Components/Piping.fs       pipeline segments and nozzle-to-nozzle route model
  Equipment/Assemblies.fs    WHB/steam-drum assemblies and component groupings such as tube bundle and central bypass
  Interop/WhbCoreContracts.fs
                             interface describing the equipment snapshot `Whb.Core` can expose
  Equipment/Package.fs       package-level aggregation of WHB, drum and pipelines

src/Whb.Core/
  Options/Constants.fs       constants, unit conversions, bisection, fixed point
  Materials/SteamIF97.fs     IAPWS-IF97 helper properties
  Materials/GasProps.fs      gas species and mixture properties
  Materials/Materials.fs     material catalogue and limits
  Solvers/GasSide.fs         gas-side HTC correlations
  Solvers/WaterSide.fs       boiling and CHF correlations
  Solvers/TwoPhase.fs        void fraction and two-phase friction
  Options/Shift.fs           water-gas shift equilibrium helpers
  Components/Equipment/*.fs  solver-side bundle, drum, bypass, valve and nozzle calculations
  Solvers/BundleSolver*.fs   coupled gas/water bundle solve split into
                             contracts, low-level kernels, support and orchestration
  Solvers/Circulation*.fs    natural-circulation loop solve split into
                             contracts, hydraulics, pipeline and orchestration
  Components/Piping/Piping.fs
                             hydraulic line geometry and loss helpers used by the solver
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
  Options/Package.fs         thin bridge to the standalone equipment package model
  Options/Defaults.fs        built-in reference case

src/Whb.Cli/
  Input/Json.fs              JSON readers and input helpers
  Input/CaseLoader.fs        case JSON -> DesignCase loader
  Input/ModeInputs.fs        rating/optimize/design input readers and formatting
  Runtime/OutputPaths.fs     results-root path policy and path normalization
  Runtime/RecentFilesStore.fs
                             machine-local recent case/options history
  Runtime/PhaseLogger.fs     timestamped CLI phase logging
  Runtime/Preflight.fs       input/output preflight checks
  Runtime/Progress.fs        CLI progress/ETA rendering
  Reports/PdsComparison.fs   client datasheet comparison output
  Commands/CommandSupport.fs template/help/self-test and small CLI helpers
  Commands/CommandRunners.fs command execution and report writing
  Program.fs                 thin CLI entry point and dispatch

tests/Whb.Tests/
  Tests.fs                   xUnit tests
```

## CI

The repository includes a GitHub Actions workflow at
`.github/workflows/ci.yml`. It restores and builds the solution, then runs
tests through `macro/test.ps1` on `windows-latest` using .NET 10. `dotnet
test` remains the underlying engine, but the script is the user-facing and
CI-facing entry point.

## License

Add the project license before publishing to GitHub or NuGet. NuGet packages
should include license metadata in the project file or package settings.
