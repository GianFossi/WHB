namespace Whb.Core.Optimizer

module Optimization =

    [<CLIMutable>]
    type Constraint =
        { Name: string; Min: float option; Max: float option; Unit: string; Weight: float }

    [<CLIMutable>]
    type Variable =
        { Name: string; Current: float; Lower: float; Upper: float; Step: float; Unit: string }

    [<CLIMutable>]
    type OptimizationProblem =
        { Name: string
          Variables: Variable list
          Constraints: Constraint list
          Objective: string
          MaxIterations: int
          Tolerance: float }

    let defaultConstraints =
        [ { Name = "DNBR"; Min = Some 2.0; Max = None; Unit = "-"; Weight = 1.0 }
          { Name = "T metal max"; Min = None; Max = Some 450.0; Unit = "degC"; Weight = 1.0 }
          { Name = "Gas pressure drop"; Min = None; Max = Some 300.0; Unit = "mbar"; Weight = 0.5 }
          { Name = "FIV V/Vcrit"; Min = None; Max = Some 0.8; Unit = "-"; Weight = 1.0 } ]
