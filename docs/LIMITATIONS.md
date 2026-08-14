# Limitations And Simplifications

This document lists important limitations and simplifications to keep visible during review.

## General Limits

- WHB is an engineering calculation aid, not a certified design program.
- Correlations may be used outside their original test range if inputs are unusual.
- The model depends strongly on correct geometry, material, fouling, and process inputs.
- Results should be reviewed together with warnings and diagnostic findings.
- CLI messages and generated labels may include both English and Italian terms; teams should align naming conventions before issuing external documents.

## Thermal Simplifications

- The bundle is discretized into axial sections and vertical bands.
- Local three-dimensional effects are not resolved.
- Fouling, tube wall, ferrule, deposit, and shell-side resistances are simplified as thermal resistances.
- Gas radiation is approximate and sensitive to optical path assumptions.
- Boiling correlations are preliminary engineering methods and should be checked for the project pressure, heat flux, and quality range.

## Hydraulic Simplifications

- Two-phase behavior is represented by empirical or semi-empirical correlations.
- Branch loss coefficients may need calibration against vendor or plant data.
- Drum-internals pressure drop is an estimate unless vendor data is provided.
- Bypass pressure-drop and valve-opening estimates depend on simplified geometry and empirical coefficients.

## Process Simplifications

- Methanation is not modeled.
- Detailed reaction kinetics are not modeled.
- Water-gas shift behavior is simplified to selected operating modes.
- Real-gas behavior is limited and should be validated for high-pressure syngas conditions.
- The `realistico` gas option currently means virial real-gas correction, not a full equation-of-state package.

## Mechanical And Vibration Simplifications

- Mechanical checks are screening calculations only.
- Vibration checks are screening calculations only.
- Detailed finite-element analysis is not included.
- Acoustic resonance is deliberately not checked: it concerns compressible gas on
  the shell side, and a fire-tube WHB has boiling water there. Acoustically induced
  vibration downstream of the bypass valve is not screened either, and that one is a
  genuine gap rather than a decision.
- Slug forces on two-phase lines are reported as a screening estimate per bend, with
  a unit-cell assumption; they are not a substitute for a pipe-stress analysis.
- Support clearances, fabrication tolerances, tube wear, and local damage mechanisms are not fully represented.

## Current Technical Limits

- Gas-side tube flow is not redistributed between parallel tubes: every band marches
  with the same mass flow per tube and ends at a slightly different outlet pressure,
  where physically the tubes share one pressure drop and redistribute. The reported
  gas pressure drop is a tube-count weighted mean over the bands.
- Feedwater temperature drives the reported net steam flow and the downcomer
  flashing margin, but the bundle evaporation rate is still quoted on the saturated
  basis, which is the basis the reference datasheet uses.
- Fouling resistances are constant: the self-accelerating deposit mechanism the
  findings describe is not simulated.
- Circulation stability is screened statically (Ledinegg slope and root count).
  Density-wave and geysering screening is not implemented.
- IAPWS region 3 is not implemented.
- Steam pressure is practically limited to about 165 bar for this model.
- Vendor-specific proprietary methods are not included.
- Input schema validation is limited.
- Inventory metal weights are estimates and are not a substitute for vendor material take-off or certified shipping weights.
- Documentation and validation coverage should continue to grow before public production use.
