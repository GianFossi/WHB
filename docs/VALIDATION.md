# Validation And Regression Benchmarks

These tables describe the regression tests used by the repository. They are not a replacement for independent engineering validation, but they make numerical drift visible during development.

The current automated suite contains 14 tests.

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

## Mechanical Screening

| Quantity | Expected value | Test tolerance |
|---|---:|---:|
| Axial expansion | 0.02944 m | 1e-12 |
| Equivalent temperature | 460.634814065415 degC | 1e-9 |
| Mean expansion coefficient | 0.000013362539256 1/K | 1e-15 |
| Lamé radial stress | -10000000 Pa | 1e-6 |
| Lamé hoop stress | 55454545.45454548 Pa | 1e-5 |
| Von Mises check value | 108.972473588517 | 1e-12 |
