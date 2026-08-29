namespace Whb.Core

open System
open Types

/// <summary>
/// Reads process, thermal, hydraulic, and geometry metrics from one verified geometry result.
/// </summary>
module ConstraintReaders =

    type ConstraintValue =
        { Key: ConstraintModel.ConstraintValueKey
          Name: string
          Unit: string
          Value: float }

    let readValues (result: DesignResult) : ConstraintValue list =
        let hot = result.Cells |> List.filter (fun c -> not c.InFerrule)
        let qMax =
            if hot.IsEmpty then nan
            else hot |> List.maxBy (fun c -> c.QFluxOut) |> fun c -> c.QFluxOut
        let dnbrMin =
            if hot.IsEmpty then nan
            else hot |> List.minBy (fun c -> c.DNBR) |> fun c -> c.DNBR
        let tMetalMax =
            if result.Cells.IsEmpty then nan
            else result.Cells |> List.maxBy (fun c -> c.TMetalIn) |> fun c -> c.TMetalIn
        let feiMax =
            if result.Vibration.IsEmpty then 0.0
            else result.Vibration |> List.maxBy (fun v -> v.FeiRatio) |> fun v -> v.FeiRatio
        let linerT =
            result.BypassResult |> Option.map (fun b -> b.TLinerMax) |> Option.defaultValue nan
        let pipeT =
            result.BypassResult |> Option.map (fun b -> b.TPipeMax) |> Option.defaultValue nan
        let downcomerMargin =
            result.Convergence.DowncomerSubcooling - result.Convergence.DowncomerSubcoolingRequired
        let sizing = Sizing.evaluate (Sizing.defaultTargets result) result
        [ { Key = ConstraintModel.Duty
            Name = "Duty"
            Unit = "W"
            Value = result.Duty }
          { Key = ConstraintModel.SteamProduction
            Name = "Steam production"
            Unit = "kg/s"
            Value = result.SteamProduction }
          { Key = ConstraintModel.GasOutletTemperature
            Name = "Gas outlet temperature"
            Unit = "K"
            Value = result.TGasOutMean }
          { Key = ConstraintModel.GasPressureDrop
            Name = "Gas pressure drop"
            Unit = "Pa"
            Value = result.DpGas }
          { Key = ConstraintModel.MaxHeatFlux
            Name = "Maximum heat flux"
            Unit = "W/m2"
            Value = qMax }
          { Key = ConstraintModel.MinDNBR
            Name = "Minimum DNBR"
            Unit = "-"
            Value = dnbrMin }
          { Key = ConstraintModel.MinCirculationRatio
            Name = "Minimum circulation ratio"
            Unit = "-"
            Value = result.Circulation.CirculationRatio }
          { Key = ConstraintModel.MaxFeiRatio
            Name = "Maximum FIV ratio"
            Unit = "-"
            Value = feiMax }
          { Key = ConstraintModel.MaxTubeMetalTemperature
            Name = "Maximum tube metal temperature"
            Unit = "K"
            Value = tMetalMax }
          { Key = ConstraintModel.MaxBypassLinerTemperature
            Name = "Maximum bypass liner temperature"
            Unit = "K"
            Value = linerT }
          { Key = ConstraintModel.MaxBypassPipeTemperature
            Name = "Maximum bypass pipe temperature"
            Unit = "K"
            Value = pipeT }
          { Key = ConstraintModel.DowncomerSubcoolingMargin
            Name = "Downcomer subcooling margin"
            Unit = "K"
            Value = downcomerMargin }
          { Key = ConstraintModel.CoupledResidual
            Name = "Coupled residual"
            Unit = "-"
            Value = result.Convergence.CoupledResidual }
          { Key = ConstraintModel.NonConvergedCells
            Name = "Non-converged cells"
            Unit = "-"
            Value = float result.Convergence.NonConvergedCells }
          { Key = ConstraintModel.WhbWeightKg
            Name = "Estimated WHB weight"
            Unit = "kg"
            Value = sizing.WeightEstimateKg }
          { Key = ConstraintModel.ExternalPipingWeightKg
            Name = "Estimated riser and downcomer weight"
            Unit = "kg"
            Value = sizing.Geometry.RiserWeightKg + sizing.Geometry.DowncomerWeightKg }
          { Key = ConstraintModel.WhbOuterDiameter
            Name = "WHB outer diameter"
            Unit = "m"
            Value = sizing.Geometry.WhbOuterDiameter }
          { Key = ConstraintModel.DrumOuterDiameter
            Name = "Steam drum outer diameter"
            Unit = "m"
            Value = sizing.Geometry.DrumOuterDiameter }
          { Key = ConstraintModel.WhbIdTimesLength
            Name = "WHB ID x L"
            Unit = "m2"
            Value = sizing.Geometry.WhbIdLength }
          { Key = ConstraintModel.DrumIdTimesLength
            Name = "Drum ID x L"
            Unit = "m2"
            Value = sizing.Geometry.DrumIdLength }
          { Key = ConstraintModel.DrumCenterlineHeight
            Name = "Drum centerline elevation"
            Unit = "m"
            Value = sizing.Geometry.DrumCenterlineHeight } ]

    let tryFindValue (key: ConstraintModel.ConstraintValueKey) (result: DesignResult) =
        readValues result |> List.tryFind (fun v -> v.Key = key)
