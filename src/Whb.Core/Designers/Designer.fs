namespace Whb.Core.Designers

open Whb.Core.Optimizer

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
