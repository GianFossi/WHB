# Theory Recap

WHB combines thermal, hydraulic, process, vibration, and mechanical screening calculations. This recap is intentionally short; it explains the model basis without replacing design codes, vendor methods, or textbooks.

## Energy Balance

The main thermal balance links gas cooling to water/steam heat uptake:

- gas enthalpy decreases along the tube length;
- transferred duty produces steam and heats boiling-side fluid;
- bypass flow, if enabled, is mixed with tube-outlet gas to obtain the final outlet condition.

The solver marches through axial cells and tube-band classes so local heat flux and temperature differences can be estimated.

## Gas-Side Heat Transfer

Gas-side convection is estimated with empirical Nusselt correlations such as Dittus-Boelter, Sieder-Tate, Colburn, Gnielinski, Petukhov-Kirillov, and Hausen. These methods depend on Reynolds number, Prandtl number, viscosity correction, and geometry assumptions.

Radiation can be added using gas emissivity estimates for species such as water vapor and carbon dioxide. Radiation assumptions are sensitive to pressure, optical path length, gas composition, and wall emissivity.

## Water-Side Heat Transfer

Water-side calculations include convection, nucleate boiling, boiling-crisis margin, wall superheat, and deposit/fouling resistance. The results are correlation based and must be reviewed when mass flux, vapor quality, pressure, heat flux, or geometry move outside the intended range.

## Two-Phase Flow And Circulation

Natural circulation is driven by density difference between the heated riser side and colder downcomer side. The model estimates:

- homogeneous or slip-corrected mixture density;
- void fraction;
- branch pressure losses;
- riser and downcomer velocities;
- circulation ratio and hydraulic margin.

Two-phase pressure-drop methods are empirical and should be validated for the selected geometry and flow regime.

## Steam Drum Calm Box Methodology

The calm-box model is a preliminary sizing and pressure-drop method for drum
internals connected to one or multiple risers. The implemented pressure-loss
path is:

- two-phase riser discharge into the calm box by sudden area-change loss;
- liquid/mixture transit through the calm-box duct, including friction, bend and
  an extra local-loss coefficient;
- release through the top opening or outlet window;
- optional water-fall head when the opening discharges above the normal water
  level;
- liquid entry into the downcomers, including the selected vortex-breaker minor
  loss coefficient.

The method uses homogeneous two-phase density for abrupt two-phase singularities
and saturated-liquid density for the downcomer-entry term. It is suitable for
early engineering comparison only. Cyclones are intentionally outside the
current scope. A future alternative may model individual riser chimneys with
top hats instead of shared calm boxes.

## Process Chemistry

The process-gas model can treat water-gas shift as frozen, equilibrium above a freeze temperature, or fractional approach to equilibrium. This affects molecular composition, enthalpy, gas properties, and therefore calculated thermal performance.

## Gas Model Selection

The base gas model uses ideal-gas mixture relationships with selectable
transport-property mixing rules. The `realistico` / `viriale` option applies the
implemented virial real-gas correction for density, residual enthalpy and heat
capacity. This is useful for syngas screening, but it is not a complete
high-pressure equation of state.

## Vibration Screening

The vibration analysis estimates tube natural frequency, vortex shedding risk, acoustic behavior, damping, and flow-induced instability indicators. It is intended for screening and comparison, not as a final specialist vibration report.

## Mechanical Screening

Mechanical calculations estimate effects such as thermal expansion, stress utilization, local load indicators, and simple component checks. Final design still requires the applicable pressure-vessel, boiler, piping, and project standards.

## Inventory Summary

Water inventory is calculated from modeled geometry: shell-side volume, connected
riser/downcomer internal volume, and steam-drum liquid volume at normal level.
Metal weight is estimated from tube, shell, baffle, ferrule, piping, drum and
bypass geometry. These values support review and early estimating; vendor
material take-off remains authoritative.
