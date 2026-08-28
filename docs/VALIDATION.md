# Validation And Regression Benchmarks

These tables describe the regression tests used by the repository. They are not a replacement for independent engineering validation, but they make numerical drift visible during development.

The automated suite covers the legacy WHB regression anchors plus the extended
gas database, saturation-table helpers, and the new flow-boiling screening
utilities. It also regression-checks the sulphur process helpers and the
explicit elemental-sulphur coupling now wired into the main bundle solve, plus
the simplified closed Claus marching modes.

## Heat Transfer And Pressure Drop

| Area | Case | Expected value | Test tolerance |
|---|---|---:|---:|
| Gas friction | Blasius, Re = 100000 | 0.017792479529 | 1e-12 |
| Gas friction | Filonenko, Re = 100000 | 0.017968935305 | 1e-12 |
| Gas friction | Colebrook, Re = 100000, e/D = 0.0001 | 0.018513866077 | 1e-12 |
| Gas heat transfer | Dittus-Boelter, Re = 100000, Pr = 1.2 | 242.930592741029 | 1e-9 |
| Gas heat transfer | Gnielinski, Re = 100000, Pr = 1.2 | 247.579318998907 | 1e-9 |
| Boiling | Mostinski, q = 250000 W/m2, p = 100 bar | 77737.810533342505 W/m2/K | 1e-6 |
| Boiling | Cooper, q = 250000 W/m2, p = 100 bar | 82353.547887270834 W/m2/K | 1e-6 |
| Two-phase | Homogeneous void fraction, x = 0.10, G = 800 kg/m2/s | 0.579724363025 | 1e-12 |
| Two-phase | Lockhart-Martinelli multiplier, x = 0.10 | 8.034491265929 | 1e-12 |
| Two-phase | Two-phase friction pressure drop, L = 10 m | 9833.788951545117 Pa | 1e-6 |

Additional behavioral tests check that:

- gas-side forced-convection heat transfer remains positive;
- enabling gas radiation increases total gas-side heat transfer;
- water-side natural convection and boiling correlations return usable positive
  preliminary coefficients;
- shell-side HTC combines boiling, bundle factor and convection consistently.
- the extended gas database returns physical properties for all supported
  species and keeps sulphur allotropes on the ideal-gas path in virial mode;
- `satT`, `saturationTable`, and the explicit saturation helper correlations
  stay aligned with IF97/IAPWS anchors within their stated screening accuracy;
- the Kandlikar/NBD helper functions and proposal DNBR thresholds stay on their
  intended regime boundaries.
- the sulphur process-state enthalpy inversion round-trips through condensing
  cases;
- the simplified Claus closure conserves S/C/H/O atoms while generating
  elemental sulphur from `H2S`/`SO2`/`COS`/`CS2`;
- the default `kinetic` Claus branch remains less aggressive than
  `equilibrium` on the same segment, and its outlet conversion increases
  monotonically when `gas.claus_cinetica.fattore_severita` is raised;
- a Claus-service case with `gas.modello_claus = frozen` stays report-level
  screening, while cases with explicit `S2` or with
  `gas.modello_claus = equilibrium` raise a coupled bundle-condensation result
  and finding.
- the dedicated sulphur-condenser solver returns positive duty, required area,
  and liquid sulphur flow for a representative Claus-service feed;
- a normal WHB design run can execute the integrated dedicated sulphur
  condenser and surface its own findings under `CONDENSATORE ZOLFO`.

The ferrule component test campaign checks that:

- ferrule thermal resistance is positive when a ferrule is installed;
- ferrule pressure-drop estimate is positive for the reference geometry;
- insulation paper radial thickness matches the 1.0 mm reference fit;
- ferrule fit status reports `OK` for the reference geometry.

The steam-drum calm-box tests check that:

- the circulation-loss path includes riser discharge into the calm box, box
  outlet/waterfall behavior and downcomer entry with vortex breaker;
- increasing the downcomer vortex-breaker loss coefficient increases the
  calculated circulation pressure loss.

## Reference Case Report

| Quantity | Expected value | Test tolerance |
|---|---:|---:|
| Exchanged duty | 116.674 MW | 0.25 MW |
| Steam production | 347798 kg/h | 1500 kg/h |
| Gas outlet temperature | 348.5 degC | 1.5 K |
| Gas-side pressure drop | 0.113 bar | 0.01 bar |
| Calculation cells | NZ x NY | exact |

The report text is also checked for key output markers so formatting changes do not accidentally remove main report sections.

Normal CLI runs additionally write `pds_comparison.txt` and `pds_comparison.csv`.
Those files compare the calculated output against the available client PDS values
for exchanged duty, steam production, gas outlet temperature, and gas-side
pressure drop.

Normal CLI runs also write `inventory_summary.txt` and `inventory_summary.csv`
for water-volume and estimated metal-weight review.

## Vibration Screening

| Quantity | Expected value | Test tolerance |
|---|---:|---:|
| Natural frequency | 81.677476876970 Hz | 1e-9 |
| Critical velocity | 33.901544440230 m/s | 1e-9 |
| Fluid-elastic ratio | 0.235977449762 | 1e-12 |
| Vortex ratio | 0.855396975251 | 1e-12 |
| Buffet ratio | 1.212356527264 | 1e-12 |
| Screening result | false | exact |

The vibration testing campaign also checks:

- empirical added-mass, Strouhal and Connors coefficient behavior;
- theoretical natural-frequency scaling with unsupported span and boundary
  condition eigenvalue;
- low-velocity versus high-velocity screening response;
- allowable-span reduction when the fluid-elastic ratio is high.

## Mechanical Screening

| Quantity | Expected value | Test tolerance |
|---|---:|---:|
| Axial expansion | 0.02944 m | 1e-12 |
| Equivalent temperature | 460.634814065415 degC | 1e-9 |
| Mean expansion coefficient | 0.000013362539256 1/K | 1e-15 |
| Lamé radial stress | -10000000 Pa | 1e-6 |
| Lamé hoop stress | 55454545.45454548 Pa | 1e-5 |
| Von Mises check value | 108.972473588517 | 1e-12 |

## Sulphur Module

| Area | Case | Expected value | Test tolerance |
|---|---|---:|---:|
| Equilibrium | `exp(lnKpS6)` at 600 K | 7.7119e7 | 0.02e7 |
| Equilibrium | `exp(lnKpS8)` at 600 K | 1.5592e11 | 0.02e11 |
| Speciation | Sulphur mole fraction at 300 degC, 1.7 bara, 8 mol/s S in 100 mol/s inert | 0.01118250 | 1e-6 |
| Polymerisation duty | 300 -> 170 degC, 1.7 bara | 9815 W | 1 W |
| Dew point / vapour pressure | `pSatTotal(150 degC)` | 32.2973 Pa | 1e-3 |
| Dew point / vapour pressure | `pSatTotal(300 degC)` | 6158.65 Pa | 1 |
| Condenser state | Sulphur partial pressure at 170 degC | 86.432079 Pa | 1e-3 |
| Condenser state | Condensed fraction at 170 degC | 0.952407 | 1e-4 |
| Condensation | Colburn-Hougen interface temperature | 426.499368 K | 1e-3 |
| Condensation | Colburn-Hougen molar flux | 0.01478073 mol/m2/s | 1e-6 |
| Liquid sulphur | Viscosity at 187 degC | 93 Pa s | 0.1 |

Behavioral checks also verify that:

- heavier sulphur allotropes become dominant on cooling while conserving the
  sulphur atom balance;
- the dew-point inversion round-trips the saturation curve;
- the lambda-transition cliff is visible in liquid viscosity;
- wall-window, sulfidation, wet-H2S and fogging checks fire in the intended
  regimes.

## Numerical Methods

| Check | What it guards |
|---|---|
| `brent` against `bisect` | Same bracket and tolerance must not give a less accurate root, and the no-sign-change fallback must behave identically. |
| `bisectWithStatus` | Returns exactly the value `bisect` returns, plus the reason it stopped, so a clamped endpoint is distinguishable from a converged root. |
| `countSignChanges` | Detects a single root and three roots, including a root landing exactly on a sample point. |
| `newtonIncreasing` | Quadratic convergence from a poor start, and correct fallback to bisection when the derivative is unusable. |
| Enthalpy inversion | Recovers the temperature an enthalpy was built from, to 1e-6 K. |
| `enthalpyAbsRealWithCp` | Enthalpy identical to the scalar evaluation; derivative matches a central difference of it. |
| Dilute-gas limit | The rho = 0 short-circuit of the IAPWS transport properties sits on the full curve. |
| Shell-side context split | The cell-level factorisation reproduces `shellHtc` term by term. |

## Constrained Search

| Check | Expected classification |
|---|---|
| Objective capped by a constraint | `AtConstraint`, with the constraint named |
| Objective limited only by the search range | `AtSearchBound`, with the variable named |
| Parabola with its minimum inside the feasible region | `Interior` |
| Constraint that nothing in range can satisfy | `NoFeasiblePoint`, never `Interior` |
| Feasible point versus a better infeasible one | The feasible point wins |

## Options And Work List

| Check | What it guards |
|---|---|
| Partial options file | A section or key absent from `whb.options.json` keeps its documented default; only an explicit `false` disables a feature. |
| `TODO.md` consistency | The open items named in the work list still match the state of the code. |
