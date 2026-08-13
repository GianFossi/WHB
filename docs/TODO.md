# Future Implementation TODO

This list records candidate improvements for later WHB versions.

## Thermal Precision Improvement Status

| Improvement | Current status |
|---|---|
| Adaptive bypass map | Implemented. `calculation.bypassMapMode` supports `adaptive`, `fast`, `full`, and `fixed`. Adaptive mode starts from the useful base points and adds points until the target mixed temperature is bracketed or `calculation.bypassTargetToleranceK` is met. |
| Gas/property calculation cache | Partially implemented. `calculation.gasPropertyCache` reuses repeated `GasProps.mixReal` evaluations during one design run. Local tabulation for shift calculations and inverse enthalpy remains future work. |
| Separate tube-bundle solve and bypass solve | Not fully implemented. The adaptive bypass map reduces unnecessary complete solves, but a validated surrogate/interpolation between few full bundle solves is still future work. |
| Explicit convergence tolerances | Partially implemented. `calculation.bypassTargetToleranceK` is used for bypass targeting and `calculation.dutyToleranceFraction` is reserved for regression/report acceptance checks. Additional tolerances for outlet gas temperature, circulation and steam production remain future work. |
| Correlation validity checks | Partially implemented. Current findings cover gas Reynolds number, gas Prandtl number and ideal-gas use at high pressure. Additional checks for vapor quality, pressure, heat flux, mass flux, boiling and two-phase methods remain future work. |
| More validated radiation model | Not implemented as a new model. Future work should improve gas emissivity, optical path length, CO2/H2O pressure broadening and local wall emissivity handling. |
| Complete steam/water properties | Not implemented in this increment. Region-3 and high-pressure coverage, with explicit out-of-range warnings, remain future work. |
| Published/vendor benchmark campaign | Partially covered by existing regression tests. Extended heat-transfer, pressure-drop, boiling, circulation and bypass benchmarks against published or vendor examples remain future work. |

## Validation

- Add additional regression tests against independent published heat-transfer and pressure-drop examples.
- Add more benchmark cases for alternate gas-side correlations, boiling correlations, and two-phase multipliers.
- Add benchmark timing cases for `fast`, `adaptive`, `full`, and `fixed`
  bypass-map modes.
- Add tolerance-based report comparisons for user-supplied acceptance cases.
- Extend the vibration and mechanical screening campaign with independent vendor or standard examples.

## Thermodynamics And Properties

- Extend steam/water support where needed for higher-pressure or region-3 conditions.
- Add more gas species and validated transport-property data.
- Add stronger real-gas models for high-pressure syngas service beyond the current `realistico`/virial option.
- Add optional reaction-chemistry models beyond water-gas shift.

## Thermal And Hydraulic Model

- Extend correlation validity checks to additional boiling, radiation, and
  two-phase pressure-drop methods.
- Add an optional higher-fidelity radiation model with documented emissivity,
  optical-path, participating-species and wall-emissivity assumptions.
- Separate the tube-bundle thermal solve from bypass-only sweeps with a
  validated surrogate when the bypass heat loss is small.
- Add selectable boiling-crisis and CHF methods with documented applicability ranges.
- Improve maldistribution handling and blocked/plugged tube modeling.
- Add optional internal recirculation and alternate circulation-network models.

## Equipment Modeling

- Allow vendor drum-internals data to override default estimates more completely.
- Add cyclone separators in the steam-drum internals model with documented
  applicability and pressure-drop basis.
- Add riser-mounted chimneys with top hats as an alternative to shared calm
  boxes.
- Add more detailed bypass-valve characteristic input.
- Add nozzle reinforcement and code-check placeholders.
- Add additional tube layouts, support patterns, and ferrule details.

## Reporting And Workflow

- Add machine-readable JSON output for integration with other tools.
- Add richer CSV exports for audit trails.
- Add versioned input schema documentation.
- Add examples for NuGet library use and CLI use.
- Add configurable client PDS comparison data instead of relying only on the built-in reference values.
- Add explicit pipe OD / wall-thickness input for material take-off accuracy.

## Quality

- Add XML documentation quality review for public APIs.
- Add static analysis and documentation link checks in CI.
- Add package signing and release automation before public NuGet distribution.
- Add Markdown link checks in CI.
