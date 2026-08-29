namespace Whb.Core

open Types

/// <summary>
/// Solves WHB natural-circulation flow distribution using thermal duty, riser/downcomer hydraulics, and two-phase pressure balance.
/// </summary>
/// <remarks>
/// Combines process heat balance, two-phase hydraulics, and circulation-loop pressure balance calculations. Review the selected correlations, riser/downcomer geometry, saturation properties, and SI-unit basis when interpreting circulation margins.
/// </remarks>
module Circulation =
    type Distribution = CirculationContracts.Distribution

    let heights = CirculationHydraulics.heights
    let branchArea = CirculationHydraulics.branchArea
    let branchDescription = CirculationHydraulics.branchDescription
    let dpParallelLiquid = CirculationHydraulics.dpParallelLiquid
    let dpLineTwoPhase = CirculationHydraulics.dpLineTwoPhase
    let dpParallelTwoPhase = CirculationHydraulics.dpParallelTwoPhase
    let lineFlows = CirculationHydraulics.lineFlows
    let dpDrumInternalsDefault = CirculationHydraulics.dpDrumInternalsDefault
    let bandDutyFractions = CirculationHydraulics.bandDutyFractions
    let dpFieldColumnWith = CirculationHydraulics.dpFieldColumnWith
    let dpFieldColumn = CirculationHydraulics.dpFieldColumn
    let dpFieldFriction = CirculationHydraulics.dpFieldFriction
    let driftVelocity = CirculationHydraulics.driftVelocity
    let annulusState = CirculationHydraulics.annulusState
    let splitSlice = CirculationHydraulics.splitSlice
    let axialVelocities = CirculationHydraulics.axialVelocities

    let solve (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
              (bandDuty: float[]) (steamLin: float[]) (dzArr: float[]) : Distribution =
        let prepared = CirculationPipeline.prepareSolve case sat bands bandDuty steamLin dzArr
        let operatingPoint = CirculationPipeline.solveOperatingPoint prepared
        let slices =
            [| 0 .. prepared.SteamLin.Length - 1 |]
            |> Array.map (CirculationPipeline.solveSlice prepared operatingPoint)
        let totals = CirculationPipeline.summarizeSlices prepared operatingPoint slices
        CirculationPipeline.assembleGlobal prepared operatingPoint slices totals
