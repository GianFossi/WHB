namespace Whb.Core

open System
open Constants
open Types

module BundleSolverCellKernel =
    [<Struct>]
    type CellGeometry =
        { Tsat: float
          Tg: float
          AreaOutPerM: float }

    [<Struct>]
    type CellIterationState =
        { TWallInner: float
          TMetalInner: float
          TMetalOuter: float
          QLin: float
          HBoil: float
          RBoil: float
          RFoulOut: float
          RMetal: float
          GasResult: GasSide.GasHtcResult
          Sweep: int
          Moved: float }

    [<Struct>]
    type CellResistances =
        { RGas: float
          RFoulIn: float
          RFerrule: float
          RMetal: float
          RFoulOut: float
          TotalFixed: float }

    let private buildCellGeometry (case: DesignCase) (sat: Steam.SatProps) (props: GasProps.MixProps) =
        { Tsat = sat.Tsat
          Tg = props.T
          AreaOutPerM = Math.PI * case.Tube.Do }

    let private initialCellState (sat: Steam.SatProps) (twiGuess: float) =
        { TWallInner = twiGuess
          TMetalInner = sat.Tsat + 40.0
          TMetalOuter = sat.Tsat + 10.0
          QLin = 0.0
          HBoil = 0.0
          RBoil = 0.0
          RFoulOut = 0.0
          RMetal = 0.0
          GasResult = Unchecked.defaultof<GasSide.GasHtcResult>
          Sweep = 0
          Moved = infinity }

    let private computeCellResistances (case: DesignCase) (props: GasProps.MixProps) (z: float)
                                       (mdotPerTube: float) (inFerrule: bool)
                                       (rH2O: float) (rCO2: float)
                                       (ferruleResistance: Ferrule -> float -> float -> float)
                                       (wallMu: float -> float -> float) (tubeK: float -> float)
                                       (state: CellIterationState) =
        let bore = if inFerrule then case.Ferrule.Bore else case.Tube.Di
        let muWall = wallMu (max 300.0 state.TWallInner) props.P
        let gasResult =
            GasSide.localHtc case.Gas.Correlation props muWall bore
                mdotPerTube z case.Gas.EntranceC state.TWallInner rH2O rCO2 case.Gas.EpsWall case.Gas.Radiation
        let rGas = 1.0 / (gasResult.HTot * Math.PI * bore)
        let rFoulIn = case.Gas.FoulingIn / (Math.PI * bore)
        let rFerrule =
            if inFerrule then
                ferruleResistance case.Ferrule case.Tube.Di (kToC (0.5 * (state.TWallInner + state.TMetalInner)))
            else 0.0
        let km = tubeK (kToC (0.5 * (state.TMetalInner + state.TMetalOuter)))
        let rMetal = log (case.Tube.Do / case.Tube.Di) / (2.0 * Math.PI * km)
        let rFoulOut = case.Water.FoulingOut / (Math.PI * case.Tube.Do)
        { RGas = rGas
          RFoulIn = rFoulIn
          RFerrule = rFerrule
          RMetal = rMetal
          RFoulOut = rFoulOut
          TotalFixed = rGas + rFoulIn + rFerrule + rMetal + rFoulOut },
        gasResult

    let private solveCellHeatBalance (geometry: CellGeometry) (shellHtcAt: float -> float)
                                     (resistances: CellResistances) =
        let residual q' =
            let hbl = shellHtcAt (q' / geometry.AreaOutPerM)
            (geometry.Tg - geometry.Tsat) / (resistances.TotalFixed + 1.0 / (max hbl 1.0 * geometry.AreaOutPerM)) - q'
        let qMax = (geometry.Tg - geometry.Tsat) / (resistances.TotalFixed + 1e-9)
        let qLin = brent residual 1e-3 (max 1.0 qMax) 1e-4 60
        let hBoil = shellHtcAt (qLin / geometry.AreaOutPerM)
        let rBoil = 1.0 / (max hBoil 1.0 * geometry.AreaOutPerM)
        (qLin, hBoil, rBoil)

    let private relaxCellTemperatures (geometry: CellGeometry) (resistances: CellResistances)
                                      (qLin: float) (state: CellIterationState) =
        let tMetalOuterNew = geometry.Tsat + qLin * (state.RBoil + resistances.RFoulOut)
        let dTmo = 0.7 * (tMetalOuterNew - state.TMetalOuter)
        let dTmi = 0.7 * (tMetalOuterNew + qLin * resistances.RMetal - state.TMetalInner)
        let dTwi = 0.7 * (geometry.Tg - qLin * (resistances.RGas + resistances.RFoulIn) - state.TWallInner)
        let moved = max (abs dTmo) (max (abs dTmi) (abs dTwi))
        { state with
            TMetalOuter = state.TMetalOuter + dTmo
            TMetalInner = state.TMetalInner + dTmi
            TWallInner = state.TWallInner + dTwi
            Moved = moved
            Sweep = state.Sweep + 1 }

    let private advanceCellSweep (case: DesignCase) (props: GasProps.MixProps)
                                 (z: float) (mdotPerTube: float) (inFerrule: bool)
                                 (rH2O: float) (rCO2: float)
                                 (geometry: CellGeometry)
                                 (ferruleResistance: Ferrule -> float -> float -> float)
                                 (shellHtcAt: float -> float)
                                 (wallMu: float -> float -> float)
                                 (tubeK: float -> float)
                                 (state: CellIterationState) =
        let (resistances, gasResult) =
            computeCellResistances case props z mdotPerTube inFerrule rH2O rCO2 ferruleResistance wallMu tubeK state
        let (qLin, hBoil, rBoil) = solveCellHeatBalance geometry shellHtcAt resistances
        relaxCellTemperatures geometry resistances
            qLin
            { state with
                QLin = qLin
                HBoil = hBoil
                RBoil = rBoil
                RFoulOut = resistances.RFoulOut
                RMetal = resistances.RMetal
                GasResult = gasResult }

    let private shouldContinueCellSweep (state: CellIterationState) =
        state.Sweep < 14 || (state.Sweep < 40 && state.Moved > 1e-3)

    let solveCell
        (case: DesignCase)
        (sat: Steam.SatProps)
        (props: GasProps.MixProps)
        (z: float)
        (mdotPerTube: float)
        (inFerrule: bool)
        (rH2O: float)
        (rCO2: float)
        (twiGuess: float)
        (ferruleResistance: Ferrule -> float -> float -> float)
        (shellHtcAt: float -> float)
        (wallMu: float -> float -> float)
        (tubeK: float -> float)
        =
        let geometry = buildCellGeometry case sat props
        let mutable state = initialCellState sat twiGuess
        while shouldContinueCellSweep state do
            state <- advanceCellSweep case props z mdotPerTube inFerrule rH2O rCO2 geometry ferruleResistance shellHtcAt wallMu tubeK state

        (state.QLin, state.GasResult, state.HBoil, state.TWallInner, state.TMetalInner, state.TMetalOuter,
         state.RBoil, state.RFoulOut, state.RMetal, state.Moved <= 1e-3)
