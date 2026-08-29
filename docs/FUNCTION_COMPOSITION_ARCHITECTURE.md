# Function-Composition Architecture

This repository is now organized so that the public calculation flows read as
top-down compositions of pure functions, while console, file-system and logging
side effects stay at the CLI boundary.

## Main Pipelines

### Shared geometry verification

```text
Design.runWithSettingsAndProgress
  -> createRequest
  -> runThermalStage
  -> runMechanicalStage
  -> assessStages
  -> toDesignResult
```

The shared verification path used by all geometry modes is:

```text
VerificationEngine.evaluate
  -> Design.runWithSettingsAndProgress
```

`DesignThermalProcess.runPure` is the single thermal/process verification
source of truth. `DesignMechanical.runPure` consumes only the typed output of
that stage (`DesignContracts.ThermalProcessStageResult`) and produces the
mechanical screening result. Inside that stage, the future code-level
mechanical sizing interface is now prepared as a separate pure hand-off
package:

```text
DesignMechanical.runPure
  -> existing screening calculations
  -> MechanicalDesignInterface.runPure
  -> MechanicalStageResult
```

No mode duplicates the verification formulas.

### Rating

```text
Rating.run
  -> evaluateLoadCases
  -> assessPerformance
```

### Optimize

```text
Optimize.run
  -> planOptimization
  -> solveOptimization
  -> evaluateBestCandidate
  -> buildResult
```

### Greenfield design

```text
GreenfieldDesign.run
  -> buildSeedSet
  -> evaluateCandidates
  -> rankCandidates
  -> buildResult
```

## Data Flow

The data flows from general intent to detailed calculations through typed
immutable records:

1. CLI reads JSON and builds a `DesignCase`.
2. Mode input records (`RatingInput`, `OptimizeInput`, `DesignInput`) add load
   cases, constraints, objectives and variable/design-space definitions.
3. `LoadCases.runAll` derives one immutable `DesignCase` per operating case.
4. `VerificationEngine.evaluate` verifies each geometry through the same shared
   `Design` pipeline.
5. `PerformanceAssessment.assess` converts raw design results into constraint
   readings, feasibility and governing cases.
6. Reporting consumes those structured results downstream without recalculating
   engineering values.

Inside the shared verification path, the thermal/process stage solves the
bundle, circulation, bypass and derived thermal metrics first; only then does
the mechanical stage consume the published contract and add stress, expansion,
riser and line checks.

Inside the internal solvers, the same rule now applies one level lower:

```text
BundleSolver.fs
  -> process/material setup
  -> axial/band orchestration
  -> output assembly

BundleSolver.CellKernel
  -> build cell geometry
  -> compute fixed resistances
  -> solve heat balance
  -> relax temperatures
```

```text
Circulation.fs
  -> prepareSolve
  -> solveOperatingPoint
  -> solveSlice[]
  -> summarizeSlices
  -> assembleGlobal
```

The large solver files were physically split as well so the public orchestration
files stay short and readable:

- `BundleSolver.Contracts.fs`, `BundleSolver.Foundation.fs`,
  `BundleSolver.CellKernel.fs`, `BundleSolver.Support.fs`, `BundleSolver.fs`
- `Circulation.Contracts.fs`, `Circulation.Hydraulics.fs`,
  `Circulation.Pipeline.fs`, `Circulation.fs`

## Side-Effect Boundary

The core architectural rule is:

```text
pure domain pipeline -> explicit boundary wrapper -> I/O
```

In practice:

- `Whb.Core` public mode pipelines are built from pure transformations on input
  records.
- Progress callbacks are injected explicitly and are used only by wrapper
  functions.
- File writes, console output, directory creation and JSON file reads remain in
  `Whb.Cli`.
- Local mutable variables used inside numerical kernels do not mutate caller
  state and do not break referential transparency; they are implementation
  details of pure functions.

## Executable Proof

The test suite contains deterministic pipeline checks that:

1. snapshot the observable input case before execution;
2. run the same pipeline twice with the same input;
3. verify that the input snapshot is unchanged after each run;
4. verify that the observable outputs are equal across repeated runs.

The key proof test is:

- `shared verification and mode pipelines are deterministic and do not mutate their inputs`

It covers the shared design verification path plus the `rating`, `optimize`,
and `design` mode pipelines under the same deterministic settings.
