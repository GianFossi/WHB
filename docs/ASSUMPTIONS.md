# Main Assumptions

This document summarizes the main engineering assumptions used by WHB.

## Equipment Scope

- The model targets fire-tube Waste Heat Boilers (WHB) and Process Gas Coolers (PGC).
- CLI preflight checks (process lock, file and folder access, disk-space probe) are operational checks only and do not replace process-safety or design validation.
- Hot process gas is assumed to flow inside the tubes.
- Boiling water and steam are represented on the shell side with natural circulation to an elevated steam drum.
- The bundle is represented by axial sections and vertical tube bands, not by a full three-dimensional CFD model.
- Vendor-specific features such as exact baffle clearances, detailed drum internals, plugged connections, and local construction tolerances must be checked separately.

## Units

- Internal calculations use SI units.
- Temperatures are generally handled in kelvin inside the calculation model.
- Operating and thermodynamic pressures are handled as absolute pressures in pascal inside the calculation model.
- Pressure drops are handled as differential pressures in pascal.
- CLI input names using `bara` are absolute pressures in bar. Reported pressure drops in `bar`, `mbar`, or `Pa` are differential values unless explicitly marked as absolute.
- CLI input and report values may expose common engineering units such as degC, bara, bar, mbar, mm, kg/h, and MW.

## Process Basis

- The reference gas side is a syngas/process-gas mixture.
- Gas composition must be normalized before property calculations.
- Water-gas shift can be frozen, equilibrium above a freeze temperature, or a fractional approach to equilibrium.
- Methanation and detailed reaction kinetics are not modeled.
- The CLI supports `gas.modello_gas`: use `ideale` for ideal gas, or `realistico`/`viriale` for the currently implemented real-gas virial correction.
- The `realistico` option is the most realistic gas model currently implemented, but it remains a limited virial correction and must be validated for high-pressure syngas service.

## Thermal Basis

- Gas-side heat transfer uses empirical forced-convection correlations with optional radiation contribution.
- Water-side heat transfer uses boiling and convection correlations suitable for preliminary engineering checks.
- Fouling, ferrule, tube wall, deposit, and shell-side resistances are treated as one-dimensional thermal resistances.
- Ferrule pressure drop is checked as a component estimate using ferrule bore, weighted length, inlet gas properties and bore-to-tube expansion.
- Ferrule insulation paper thickness is checked geometrically from tube ID and sleeve OD; drawing tolerances remain a vendor/design check.
- Local peaks depend on grid resolution and input maldistribution assumptions.

## Hydraulic Basis

- Gas-side pressure drop is estimated from friction and local-loss methods.
- Natural circulation is calculated from loop driving head and branch losses.
- Two-phase density, void fraction, and friction behavior are correlation based.
- Drum and internals pressure losses are preliminary unless vendor data is entered.
- Steam-drum calm boxes are treated as simple boxes attached to one or more
  risers. The pressure-drop method includes riser discharge into the box,
  transit through the box, the top opening or outlet window, optional water fall
  from the top opening, and water entry into downcomers with a vortex breaker.
- Cyclones are not considered in the current drum-internals method.

## Mechanical And Vibration Basis

- Mechanical checks are screening calculations, not code-stamped pressure-part design.
- Vibration checks are screening checks based on empirical and theoretical relationships.
- The automated vibration testing campaign covers empirical coefficients, theoretical frequency scaling, velocity sensitivity, allowable-span response and one frozen validation row.
- Tube support, damping and boundary conditions must be validated against project
  rules and vendor data. Damping is taken from the case input; the void-dependent
  two-phase shape is reported only as a sensitivity.
- Steam production is quoted as the evaporation rate inside the bundle, with the
  water entering the tubes at saturation. This is the basis the reference datasheet
  uses. The net steam flow leaving the drum, which accounts for heating the
  feedwater from `vapore.t_alimento_C` up to saturation, is reported alongside it.

## Inventory Basis

- Water-volume summaries are geometric inventories.
- WHB shell-side water volume subtracts tube displacement and bypass-pipe displacement from the shell internal volume.
- Riser and downcomer water volumes use modeled internal diameter and developed length.
- Steam drum water volume is calculated at normal level and excludes internals displacement.
- Metal weights are estimates based on representative material densities.
- Riser and downcomer metal weights infer pipe outside diameter from NPS when no explicit OD is available; vendor material take-off data remains authoritative.
