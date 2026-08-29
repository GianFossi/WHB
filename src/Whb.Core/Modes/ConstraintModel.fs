namespace Whb.Core

open Types

/// <summary>
/// Shared constraint metadata used by rating, optimization, and design modes.
/// </summary>
module ConstraintModel =

    type ConstraintDomain =
        | Process
        | Thermal
        | Hydraulic
        | Mechanical
        | Vibration
        | Numerical
        | Geometry
        | Weight
        | Envelope

    type ConstraintValueKey =
        | Duty
        | SteamProduction
        | GasOutletTemperature
        | GasPressureDrop
        | MaxHeatFlux
        | MinDNBR
        | MinCirculationRatio
        | MaxFeiRatio
        | MaxTubeMetalTemperature
        | MaxBypassLinerTemperature
        | MaxBypassPipeTemperature
        | DowncomerSubcoolingMargin
        | CoupledResidual
        | NonConvergedCells
        | WhbWeightKg
        | ExternalPipingWeightKg
        | WhbOuterDiameter
        | DrumOuterDiameter
        | WhbIdTimesLength
        | DrumIdTimesLength
        | DrumCenterlineHeight

    type LimitKind =
        | Min of float
        | Max of float
        | Range of float * float

    type ConstraintTarget =
        { Key: ConstraintValueKey
          Name: string
          Domain: ConstraintDomain
          Unit: string
          Limit: LimitKind
          Required: bool
          Weight: float }

    type ConstraintReading =
        { Target: ConstraintTarget
          Value: float
          GoverningLoadCase: string
          Passed: bool
          LimitScore: float
          NormalizedViolation: float }

    type ConstraintSet =
        { Targets: ConstraintTarget list }

    let defaultRatingConstraints (caseIn: DesignCase) =
        { Targets =
            [ { Key = MinDNBR
                Name = "DNBR"
                Domain = Thermal
                Unit = "-"
                Limit = Min caseIn.Water.MinDNBR
                Required = true
                Weight = 1.0 }
              { Key = MaxTubeMetalTemperature
                Name = "Tube metal temperature"
                Domain = Thermal
                Unit = "K"
                Limit = Max (Constants.cToK 450.0)
                Required = true
                Weight = 1.0 }
              { Key = GasPressureDrop
                Name = "Gas pressure drop"
                Domain = Hydraulic
                Unit = "Pa"
                Limit = Max 30000.0
                Required = true
                Weight = 0.5 }
              { Key = MaxFeiRatio
                Name = "FIV V/Vcrit"
                Domain = Vibration
                Unit = "-"
                Limit = Max 0.8
                Required = true
                Weight = 1.0 } ] }
