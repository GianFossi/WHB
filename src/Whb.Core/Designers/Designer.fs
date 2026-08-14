namespace Whb.Core.Designers

open Whb.Core.Optimizer

/// <summary>
/// Provides high-level WHB design orchestration over the thermal, process, vibration, and mechanical calculation modules.
/// </summary>
/// <remarks>
/// Exposes high-level design orchestration for WHB calculations. Results inherit the assumptions and validity limits of the underlying empirical and theoretical correlations.
/// </remarks>
module Designer =

    [<CLIMutable>]
    type DesignBasis =
        { Name: string
          Duty: float
          SteamPressure: float
          GasInletTemperature: float
          GasMassFlow: float
          Constraints: Optimization.Constraint list }

    [<CLIMutable>]
    type DesignCandidate =
        { Name: string
          TubeCount: int
          TubeLength: float
          TubeOuterDiameter: float
          ShellInnerDiameter: float
          Score: float
          Notes: string list }




