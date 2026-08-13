# What The Software Does

WHB is an F#/.NET calculation tool for preliminary and diagnostic engineering studies of fire-tube Waste Heat Boilers and Process Gas Coolers.

## Main Workflow

1. Read a built-in or JSON-defined WHB case.
2. Run preflight checks on active WHB processes, case readability, output/log/temp write access, and available disk space.
3. Calculate gas and water/steam properties.
4. Solve bundle heat transfer and pressure drop.
5. Solve natural circulation and two-phase behavior.
6. Evaluate bypass, drum, nozzle, vibration, and mechanical diagnostics.
7. Generate text, CSV, and HTML reports, including mandatory client PDS
   comparison and water/metal inventory summaries.

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
- mandatory comparison against available client PDS values;
- partial and total water volume in the WHB, risers, downcomers and steam drum;
- estimated component metal weights;
- report tables for engineering review.

## Intended Use

WHB is intended for:

- feasibility studies;
- model alignment against reference datasheets;
- sensitivity checks;
- internal calculation review;
- preliminary diagnostics;
- client PDS comparison checks;
- water-volume and metal-weight inventory review;
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
