# Future Implementation TODO

This list records candidate improvements for later WHB versions.

## Validation

- Add additional regression tests against independent published heat-transfer and pressure-drop examples.
- Add more benchmark cases for alternate gas-side correlations, boiling correlations, and two-phase multipliers.
- Add tolerance-based report comparisons for user-supplied acceptance cases.
- Extend validation tables for vibration and mechanical screening checks with independent vendor or standard examples.

## Thermodynamics And Properties

- Extend steam/water support where needed for higher-pressure or region-3 conditions.
- Add more gas species and validated transport-property data.
- Add stronger real-gas models for high-pressure syngas service.
- Add optional reaction-chemistry models beyond water-gas shift.

## Thermal And Hydraulic Model

- Add more explicit correlation validity checks and warnings.
- Add selectable boiling-crisis and CHF methods with documented applicability ranges.
- Improve maldistribution handling and blocked/plugged tube modeling.
- Add optional internal recirculation and alternate circulation-network models.

## Equipment Modeling

- Allow vendor drum-internals data to override default estimates more completely.
- Add more detailed bypass-valve characteristic input.
- Add nozzle reinforcement and code-check placeholders.
- Add additional tube layouts, support patterns, and ferrule details.

## Reporting And Workflow

- Add machine-readable JSON output for integration with other tools.
- Add richer CSV exports for audit trails.
- Add versioned input schema documentation.
- Add examples for NuGet library use and CLI use.

## Quality

- Add XML documentation quality review for public APIs.
- Add static analysis and documentation link checks in CI.
- Add package signing and release automation before public NuGet distribution.
