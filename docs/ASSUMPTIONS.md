# Main Assumptions

This document summarizes the main engineering assumptions used by WHB.

## Equipment Scope

- The model targets fire-tube Waste Heat Boilers (WHB) and Process Gas Coolers (PGC).
- Hot process gas is assumed to flow inside the tubes.
- Boiling water and steam are represented on the shell side with natural circulation to an elevated steam drum.
- The bundle is represented by axial sections and vertical tube bands, not by a full three-dimensional CFD model.
- Vendor-specific features such as exact baffle clearances, detailed drum internals, plugged connections, and local construction tolerances must be checked separately.

## Units

- Internal calculations use SI units.
- Temperatures are generally handled in kelvin inside the calculation model.
- Pressures are generally handled in pascal inside the calculation model.
- CLI input and report values may expose common engineering units such as degC, bar, mm, kg/h, and MW.

## Process Basis

- The reference gas side is a syngas/process-gas mixture.
- Gas composition must be normalized before property calculations.
- Water-gas shift can be frozen, equilibrium above a freeze temperature, or a fractional approach to equilibrium.
- Methanation and detailed reaction kinetics are not modeled.
- The default gas model is ideal-gas with optional limited real-gas corrections.

## Thermal Basis

- Gas-side heat transfer uses empirical forced-convection correlations with optional radiation contribution.
- Water-side heat transfer uses boiling and convection correlations suitable for preliminary engineering checks.
- Fouling, ferrule, tube wall, deposit, and shell-side resistances are treated as one-dimensional thermal resistances.
- Local peaks depend on grid resolution and input maldistribution assumptions.

## Hydraulic Basis

- Gas-side pressure drop is estimated from friction and local-loss methods.
- Natural circulation is calculated from loop driving head and branch losses.
- Two-phase density, void fraction, and friction behavior are correlation based.
- Drum and internals pressure losses are preliminary unless vendor data is entered.

## Mechanical And Vibration Basis

- Mechanical checks are screening calculations, not code-stamped pressure-part design.
- Vibration checks are screening checks based on empirical and theoretical relationships.
- Tube support, damping, acoustic behavior, and boundary conditions must be validated against project rules and vendor data.
