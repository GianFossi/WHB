# What The Software Does

WHB is an F#/.NET calculation tool for preliminary and diagnostic engineering studies of fire-tube Waste Heat Boilers and Process Gas Coolers.

## Main Workflow

1. Read a built-in or JSON-defined WHB case.
2. Calculate gas and water/steam properties.
3. Solve bundle heat transfer and pressure drop.
4. Solve natural circulation and two-phase behavior.
5. Evaluate bypass, drum, nozzle, vibration, and mechanical diagnostics.
6. Generate text, CSV, and HTML reports.

## Main Results

The software can estimate:

- exchanged duty;
- gas outlet temperature;
- steam generation;
- local heat flux;
- metal temperature;
- DNBR and boiling-crisis margin;
- natural-circulation ratio;
- gas-side and water-side pressure losses;
- bypass fraction and valve opening;
- riser and downcomer velocities;
- nozzle velocity and rho-v-squared checks;
- tube vibration screening indicators;
- mechanical screening indicators;
- report tables for engineering review.

## Intended Use

WHB is intended for:

- feasibility studies;
- model alignment against reference datasheets;
- sensitivity checks;
- internal calculation review;
- preliminary diagnostics;
- software experimentation and engineering-method comparison.

## Not Intended For

WHB is not intended to be the sole basis for:

- code-stamped pressure-part design;
- relief-system sizing;
- safety-instrumented-system settings;
- mechanical guarantees;
- vendor warranty guarantees;
- final material selection;
- final vibration approval;
- construction release.
