namespace Whb.Core.Designers

open Whb.Core.Optimizer

/// <summary>
/// Provides designer functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Designer =

    [<CLIMutable>]
    /// <summary>
    /// Represents designbasis data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type DesignBasis =
        { Name: string
          Duty: float
          SteamPressure: float
          GasInletTemperature: float
          GasMassFlow: float
          Constraints: Optimization.Constraint list }

    [<CLIMutable>]
    /// <summary>
    /// Represents designcandidate data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type DesignCandidate =
        { Name: string
          TubeCount: int
          TubeLength: float
          TubeOuterDiameter: float
          ShellInnerDiameter: float
          Score: float
          Notes: string list }
