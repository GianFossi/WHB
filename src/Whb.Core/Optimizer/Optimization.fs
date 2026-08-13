namespace Whb.Core.Optimizer

/// <summary>
/// Provides optimization functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Optimization =

    [<CLIMutable>]
    /// <summary>
    /// Represents constraint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Constraint =
        { Name: string; Min: float option; Max: float option; Unit: string; Weight: float }

    [<CLIMutable>]
    /// <summary>
    /// Represents variable data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Variable =
        { Name: string; Current: float; Lower: float; Upper: float; Step: float; Unit: string }

    [<CLIMutable>]
    /// <summary>
    /// Represents optimizationproblem data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type OptimizationProblem =
        { Name: string
          Variables: Variable list
          Constraints: Constraint list
          Objective: string
          MaxIterations: int
          Tolerance: float }

    /// <summary>
    /// Calculates or returns defaultconstraints for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let defaultConstraints =
        [ { Name = "DNBR"; Min = Some 2.0; Max = None; Unit = "-"; Weight = 1.0 }
          { Name = "T metal max"; Min = None; Max = Some 450.0; Unit = "degC"; Weight = 1.0 }
          { Name = "Gas pressure drop"; Min = None; Max = Some 300.0; Unit = "mbar"; Weight = 0.5 }
          { Name = "FIV V/Vcrit"; Min = None; Max = Some 0.8; Unit = "-"; Weight = 1.0 } ]
