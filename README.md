# whb

Thermal, hydraulic and diagnostic calculations for fire-tube Waste Heat Boilers
(WHB) / Process Gas Coolers (PGC), written in F# for .NET 8.

The model is intended for engineering study of WHB units where process gas flows
inside tubes and boiling water/steam circulates naturally on the shell side with
an elevated steam drum.

## Documentation Map

- [What the software does](docs/SCOPE.md)
- [Main assumptions](docs/ASSUMPTIONS.md)
- [Theory recap](docs/THEORY.md)
- [Limitations and simplifications](docs/LIMITATIONS.md)
- [Acronyms and terms](docs/ACRONYMS.md)
- [Validation and regression benchmarks](docs/VALIDATION.md)
- [Future implementation TODO](docs/TODO.md)
- [Detailed correlation and issue notes](DOC_correlazioni_e_problematiche.md)

> Warning: this software is an engineering calculation aid, not a certified
> pressure-vessel, boiler-code or safety-integrity tool. Always review results
> against project datasheets, vendor drawings, applicable codes and qualified
> engineering judgment before using them for design decisions.

## Prerequisites

- .NET SDK 8.0 or newer.
- PowerShell 7 or Windows PowerShell for the helper scripts.
- Windows, Linux or macOS for `dotnet` build/test; the provided scripts are
  PowerShell-oriented.
- Optional: GitHub Actions, if you want to use the included CI workflow.

Check your SDK:

```bash
dotnet --info
```

## Projects

- `Whb.Core`: calculation library and domain model.
- `Whb.Cli`: command-line application, JSON input loader and report generator.
- `Whb.Tests`: xUnit test project for core numerical and utility behavior.

## Quick Start

Build and test:

```powershell
.\build.ps1
```

Run the built-in reference case:

```bash
dotnet run --project src/Whb.Cli
```

Generate an editable JSON input file:

```bash
dotnet run --project src/Whb.Cli -- --template caso.json
```

Run a custom case:

```bash
dotnet run --project src/Whb.Cli -- caso.json --out risultati
```

During long calculations the CLI shows a console status window with an
estimated progress bar, the current task description, elapsed time and estimated
remaining time.

Run internal correlation checks:

```bash
dotnet run --project src/Whb.Cli -- --selftest
```

## CLI Commands

```text
whb [caso.json] [--out <cartella>]
whb --template [file.json]
whb --options-template [file.json]
whb --selftest
whb --carichi [caso.json] [--out <cartella>]
whb --github-plan [options.json]
whb --github-push [options.json]
```

If no case file is supplied, the reference case is executed.

## Local Tasks

```powershell
.\build.ps1                         # restore + build + test in Release
.\build.ps1 -Task Build             # restore + build
.\build.ps1 -Task Test              # restore + build + test
.\build.ps1 -Task Rebuild           # clean + restore + build + test
.\build.ps1 -Task Clean             # clean bin/obj/publish/TestResults
.\clean.ps1                         # shortcut for clean
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
  diagnostics.
- Natural-circulation loop model with downcomer/riser branch losses.
- Steam drum and drum-internals pressure-drop representation.
- Internal bypass model with mixing temperature and valve-position checks.
- Water-gas shift modes: frozen, equilibrium above a freeze temperature, and
  fractional approach.
- Gas mixture properties with Wilke and molar-average options.
- IAPWS-IF97 water/steam helper properties for regions used by the model.
- Metal temperature estimates through fouling, ferrule, wall and water-side
  deposit resistances.
- Materials catalogue with indicative thermal/mechanical limits and metal
  dusting windows.
- Vibration, nozzle, maldistribution and mechanical-stress diagnostics.
- Text, CSV and self-contained HTML reports.
- Partial-load curve generation from 50% to 110% gas flow.

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
- tube vibration margins and load diagnostics;
- output tables suitable for review in spreadsheets.

## Output Files

Default output is written to `risultati/` unless `--out` is provided.

| File | Content |
|---|---|
| `report.txt` | Engineering report |
| `report.html` | Self-contained HTML report with maps, charts and tables |
| `criticita.txt` | Summary of findings and warnings |
| `pds_comparison.txt` | Mandatory comparison between app output and available client PDS data |
| `pds_comparison.csv` | Spreadsheet-ready PDS comparison table |
| `celle.csv` | One row per calculation cell |
| `profilo_assiale.csv` | Axial aggregate profile |
| `tensioni.csv` | Stress/check table |
| `valvola_bypass.csv` | Bypass valve data |
| `maldistribuzione.txt` | Maldistribution notes |
| `vibrazioni.txt` | Vibration checks |

## Validation

The built-in `--selftest` command checks:

- IAPWS-IF97 reference points for saturation and regions 1/2;
- viscosity and thermal conductivity reference values;
- selected gas-property values for air and the reference syngas mixture;
- real-gas virial correction traces used by the gas model.

The repository test suite currently covers core numerical utilities, unit
conversions, grid generation, piping geometry helpers and material lookup.

Run:

```powershell
.\build.ps1 -Task Test
```

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
- The default gas model is ideal-gas with user-provided compressibility factor;
  optional virial corrections are limited.
- The shell-side model assumes a distributed bundle representation, not a full
  CFD simulation.
- Axial distribution uses a local circulation-ratio approach; the alternate
  internal recirculation mode is available but disabled by default.
- Drum-internals pressure drop defaults to an engineering estimate and should be
  replaced with vendor data when available.
- Palen bundle CHF is conservative for forced crossflow and should be reviewed
  against project boiling-crisis criteria.
- CKTI/Blokh-style gas emissivity assumptions should be used carefully at high
  pressure and short optical path length.

## Repository Layout

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

## CI

The repository includes a GitHub Actions workflow at
`.github/workflows/ci.yml`. It restores, builds and tests the solution on
`windows-latest` using .NET 8.

## License

Add the project license before publishing to GitHub or NuGet. NuGet packages
should include license metadata in the project file or package settings.
