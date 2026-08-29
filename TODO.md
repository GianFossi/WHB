# TODO

Single work list for the WHB project: near-term open items first, then the
longer-term backlog. The reasoning behind each closed item is in the Modification
Memory Log of [`.ai/AGENTS.md`](./.ai/AGENTS.md).

Status: **24 of 28 register items implemented** (levels 0-4). 85 tests green,
self-test bit-identical, `adaptive` and `full` bypass-map modes now agree.

---

## 1. Open, near term

Four items, **none blocked by the others**: they can be taken in any order.

### T-20 - Cancellation token

There is no `CancellationToken`: a partial-load campaign or a `full` bypass map
can only be stopped by killing the process, which leaves partial files in the
output folder.

To do: propagate a token to `Parallel.For` and to the axial loops, hook it to
Ctrl+C, and write reports only once the calculation has completed.

*Effort: 1-2 days. Moves no number.*

### T-23 - Fouling runaway scenario

`FoulingIn` and `FoulingOut` are constants, and a sensitivity campaign already
exists. The deposit finding correctly describes the mechanism as self-accelerating
- hotter means more deposit means hotter - but the calculation does not show it.

To do: iterate `Rf` as a function of wall temperature to a fixed point or to
divergence. The useful result is not a number but an answer: does the bundle settle
or run away, and in what time. Keep it **separate** from the design case.

*Effort: 1 week. Separate scenario, does not touch the base case.*

### T-26 - AIV screening downstream of the bypass valve

The valve is already checked on `rho*v^2` at the vena contracta and on Mach <= 0.3,
with an explicit note about noise. Those are the right checks for erosion and for
the valve itself. What is missing is an estimate of the emitted acoustic power:
throttling gas at tens of bar generates broadband energy that travels into the
downstream pipe and fatigues small-bore connections - a different mechanism from
flow-induced vibration, and one that velocity does not reveal.

To do: estimate the sound power level from the pressure ratio and mass flow, and
compare it against the usual screening criteria for the downstream pipe. This only
makes sense if the bypass line is inside the scope of the calculation; if the
boundary is the valve, saying so and closing the item is legitimate.

*Effort: 1 week.*

### T-25 - Flow redistribution between parallel tubes

Every band and ferrule class marches with the same `mdotPerTube` and accumulates
its own pressure drop, ending at a different outlet pressure. Physically the tubes
are in parallel between the same two chambers: they share one pressure drop and
redistribute the flow. The hot tubes, which lose more, take less of it - which
damps exactly the peak heat flux and peak metal temperature the calculation is
looking for.

To do: an outer loop that imposes equal pressure drop and solves the split of
`mdotPerTube` across bands and classes.

> **This is a structural change to the marching solver and must not be improvised.**
> It touches the core of the thermal calculation and moves the design numbers: it
> needs dedicated time and a full validation, not to be squeezed into a broader
> piece of work.

The near-zero-cost interim step is **already done**: `DpGas` is now weighted by
tube count per band instead of being an arithmetic mean.

*Effort: 2 weeks. Moves the numbers.*

---

## 2. Backlog

Candidate improvements for later versions. Items closed in the current round are
marked and kept for context.

### Thermal precision

| Improvement | Status |
|---|---|
| Adaptive bypass map | **Done.** `calculation.bypassMapMode` supports `adaptive`, `fast`, `full`, `fixed`. The termination condition was inverted and has been fixed: the grid now extends until the target mixed temperature is bracketed, and `adaptive` reproduces the `full` answer. |
| Selectable CHF method | **Done.** `vapore.modello_chf` drives the cell-by-cell DNBR field: `palen` (default), `lienhard`, `zuber`, or a practical limit in kW/m2. |
| Explicit convergence tolerances | **Done.** `ConvergenceReport` on the result carries coupled-loop convergence, quality clamps, non-converged cells, circulation root count and slope, downcomer flashing margin, and bypass-map coverage. Each raises a finding. |
| Gas/property calculation cache | Partially done. `calculation.gasPropertyCache` reuses repeated `GasProps.mixReal` evaluations. The enthalpy inversion is now a safeguarded Newton iteration rather than a bisection, so tabulating it is no longer the lever it was; shift tabulation remains future work. |
| Correlation validity checks | Partially done. Findings now cover gas Reynolds and Prandtl numbers, ideal-gas use at high pressure, and vapour quality hitting the 0.95 barrier. Checks on pressure, heat flux, mass flux and the two-phase methods remain future work. |
| Separate bundle solve from bypass solve | Not done. The adaptive map avoids unnecessary full solves, but a validated surrogate between few full bundle solves is still future work. |
| More validated radiation model | Not done. Gas emissivity, optical path length, CO2/H2O pressure broadening and local wall emissivity all deserve better treatment. |
| Complete steam/water properties | Not done. Region-3 and high-pressure coverage, with explicit out-of-range warnings. |
| Published/vendor benchmark campaign | Partially covered by the regression tests. Extended heat-transfer, pressure-drop, boiling, circulation and bypass benchmarks against published or vendor examples remain future work. |

### Validation

- Additional regression tests against independent published heat-transfer and
  pressure-drop examples.
- More benchmark cases for alternate gas-side correlations, boiling correlations
  and two-phase multipliers.
- Benchmark timing cases for the `fast`, `adaptive`, `full` and `fixed` bypass-map
  modes.
- Tolerance-based report comparisons for user-supplied acceptance cases.
- Extend the vibration and mechanical screening campaign with independent vendor or
  standard examples.

### Thermodynamics and properties

- Extend steam/water support for higher-pressure or region-3 conditions.
- Add more gas species and validated transport-property data.
- Stronger real-gas models for high-pressure syngas beyond the current virial
  option.
- Optional reaction-chemistry models beyond water-gas shift.

### Thermal and hydraulic model

- Extend correlation validity checks to further boiling, radiation and two-phase
  pressure-drop methods.
- Optional higher-fidelity radiation model with documented emissivity,
  optical-path, participating-species and wall-emissivity assumptions.
- Separate the bundle thermal solve from bypass-only sweeps with a validated
  surrogate when the bypass heat loss is small.
- Improve maldistribution handling and blocked/plugged tube modelling.
- Optional internal recirculation and alternate circulation-network models.
- Density-wave (dynamic) stability screening for the circulation loop. The static
  Ledinegg criterion and the root count are already reported; a subcooling number
  and a phase number would complete the picture.

### Equipment modelling

- Allow vendor drum-internals data to override default estimates more completely.
- Cyclone separators in the steam-drum internals model, with documented
  applicability and pressure-drop basis.
- Riser-mounted chimneys with top hats as an alternative to shared calm boxes.
- More detailed bypass-valve characteristic input.
- Nozzle reinforcement and code-check placeholders.
- Additional tube layouts, support patterns and ferrule details.

### Reporting and workflow

- Improve the CLI progress bar: current calculation phase, a stable progress
  estimate, a second line below the bar naming the task in flight, and a rotating
  spinner so a long phase still looks alive.
- Machine-readable JSON output for integration with other tools.
- Richer CSV exports for audit trails.
- Versioned input schema documentation.
- More end-to-end sample datasets with expected output snippets in the README.
- Configurable client PDS comparison data instead of the built-in reference values
  only.
- Explicit pipe OD / wall-thickness input for material take-off accuracy.
- Widen the built-in `--optimize` defaults and add richer examples/templates for
  the now case-file-driven variable set.

### Quality

- XML documentation quality review for public APIs.
- Static analysis and documentation link checks in CI.
- Package signing and release automation before public NuGet distribution.
- Markdown link checks in CI.

---

## 3. Closed in the current round

- **Bug fixed**: the adaptive bypass map stopped with its condition inverted. The
  reference case missed its target by 0.8 K and reported a valve window of zero
  width; `adaptive` now reproduces `full`.
- `ConvergenceReport` on the result, with a finding for every diagnostic.
- `TFeed` finally used: net steam production alongside bundle evaporation, and the
  feedwater subcooling that drives the new downcomer flashing check.
- Selectable CHF model wired into the DNBR field.
- Per-band duty profile shared between the thermal and hydraulic solvers.
- Gas-side momentum term; `DpGas` weighted by tube count.
- Turbulent buffeting in the vibration verdict; slug forces per bend with their
  passing frequency; two-phase damping reported as a sensitivity.
- Ledinegg criterion and root count on the circulation balance.
- Legacy constrained search (`--optimize-legacy`) reporting *what holds* the
  optimum: active constraint, search bound, interior stationary point, or no
  feasible point.
- Full `--help`: commands, options, project-options keys, output files, exit codes.
- Performance: reference case 38.7 s -> under 6 s, `full` mode 124.6 s -> ~5 s,
  `--loads` 131.8 s -> ~15 s, with the cell and stress tables unchanged except in
  the last printed digit.

---

## Rule that still holds

The root finders in [`Circulation.Pipeline.fs`](./src/Whb.Core/Solvers/Circulation.Pipeline.fs) must
stay bisection. Replacing them with Brent was tried and reverted: it converges to a
different root and moves the circulation flow by roughly 9x. The residual has
multiple roots, so the choice of solver there is a modelling decision, not a speed
knob. The root count and the Ledinegg criterion now make that visible; they do not
authorise changing it.
