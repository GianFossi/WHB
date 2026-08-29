namespace Whb.Core

open System
open Constants
open Types

/// <summary>
/// Solves WHB bundle heat transfer, gas cooling, steam generation, wall temperatures, and pressure losses.
/// </summary>
/// <remarks>
/// Solves coupled WHB bundle thermal performance using gas-side heat transfer, water-side boiling, wall resistance, fouling, and axial discretization. Results depend on empirical correlation choices, grid resolution, material properties, and process boundary conditions.
/// </remarks>
module BundleSolver =
    type ShellContext = BundleSolverContracts.ShellContext
    type SolveOutput = BundleSolverContracts.SolveOutput

    let ferruleResistance = BundleSolverFoundation.ferruleResistance
    let ferruleInsulationThickness = BundleSolverFoundation.ferruleInsulationThickness
    let ferruleInsulationFitStatus = BundleSolverFoundation.ferruleInsulationFitStatus
    let ferrulePressureDropEstimate = BundleSolverFoundation.ferrulePressureDropEstimate
    let ferruleClasses = BundleSolverFoundation.ferruleClasses
    let shellContext = BundleSolverFoundation.shellContext
    let shellHtcWith = BundleSolverFoundation.shellHtcWith
    let shellHtc = BundleSolverFoundation.shellHtc

    let private buildShellHtcAt (case: DesignCase) (sat: Steam.SatProps) (x: float) (gCross: float) =
        let ctx = shellContext case sat x gCross
        fun qOut -> shellHtcWith case sat ctx qOut

    let solve (case: DesignCase) (bands: Bundle.Band list)
              (wLinField: float[]) (xInField: float[]) : SolveOutput =
        let tube = case.Tube
        let sat = Steam.sat case.Water.DrumPressure
        let processModel = BundleSolverSupport.buildProcessModel case
        let properties = BundleSolverSupport.buildPropertyResolvers case processModel.Composition0
        let bandArr = bands |> List.toArray
        let classes = ferruleClasses case.Ferrule
        let clsArr = classes |> List.toArray
        let ny = bandArr.Length
        let nc = clsArr.Length
        let nz = max 6 case.NZ
        let (zc, dzArr) = gradedAxialGrid tube.Length nz case.AxialRefine
        let mdotPerTube = case.Gas.MassFlow / float tube.NTubes

        let cells = Array3D.zeroCreate<CellResult> nz ny nc
        let h0 = BundleSolverSupport.initialGasEnthalpy case processModel
        let hGas = Array2D.create ny nc h0
        let pGas = Array2D.create ny nc case.Gas.PIn
        let procComp = Array2D.create ny nc processModel.Composition0
        let twiPrev = Array2D.create ny nc (sat.Tsat + 0.6 * (case.Gas.TIn - sat.Tsat))
        let steamLin = Array.zeroCreate nz
        let dutyLin = Array.zeroCreate nz
        let axial = ResizeArray<AxialResult>()
        let mutable steamCum = 0.0
        let mutable dutyCum = 0.0
        let mutable qualityClamped = 0
        let mutable qualityClampFirstZ = nan
        let mutable nonConvergedCells = 0
        let mutable sulphurCondensingCells = 0
        let mutable sulphurFirstCondensationZ = nan
        let bandDuty = Array.zeroCreate ny
        let qCritTube =
            min (WaterSide.chfHorizontalTube tube.Do sat) (WaterSide.chfMostinski sat.P Pc_water)
        let phiB = WaterSide.palenPhiB tube.Otl tube.Length (Math.PI * tube.Do * tube.Length * float tube.NTubes)

        for i in 0 .. nz - 1 do
            let z = zc.[i]
            let dz = dzArr.[i]
            let wl = if i < wLinField.Length then max 1e-3 wLinField.[i] else 1.0
            let mutable x = if i < xInField.Length then xInField.[i] else 0.0
            let mutable dutySlice = 0.0

            for j in 0 .. ny - 1 do
                let band = bandArr.[j]
                let gCross = wl / band.FieldFreeArea
                let shellHtcAt = buildShellHtcAt case sat x gCross
                let res =
                    Array.init nc (fun c ->
                        let (frac, ferruleLength) = clsArr.[c]
                        let inFerrule = case.Ferrule.Enabled && z < ferruleLength
                        let compIn = procComp.[j, c]
                        let sulphurState = processModel.StateFromEnthalpyAt pGas.[j, c] compIn hGas.[j, c]
                        let tGas = sulphurState.T
                        let gasComp = sulphurState.VapourComposition
                        let props =
                            GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas gasComp tGas pGas.[j, c] case.Gas.Z
                        let rH2O = GasProps.molFrac sulphurState.VapourComposition GasProps.H2O
                        let rCO2 = GasProps.molFrac sulphurState.VapourComposition GasProps.CO2
                        let cell =
                            BundleSolverCellKernel.solveCell case sat props z mdotPerTube inFerrule rH2O rCO2
                                twiPrev.[j, c] ferruleResistance shellHtcAt properties.WallMu properties.TubeK
                        (frac, ferruleLength, inFerrule, props, sulphurState, cell))
                let dQband =
                    res |> Array.sumBy (fun (frac, _, _, _, _, (q, _, _, _, _, _, _, _, _, _)) -> q * dz * band.NTubes * frac)
                let xRaw = x + dQband / (max 1e-6 (wl * dz) * sat.Hfg)
                if xRaw > 0.95 then
                    qualityClamped <- qualityClamped + 1
                    if Double.IsNaN qualityClampFirstZ then qualityClampFirstZ <- z
                let xOut = min 0.95 xRaw
                let xMean = 0.5 * (x + xOut)
                let alpha = TwoPhase.voidFraction case.Loop.VoidModel xMean sat gCross
                let rhoH = TwoPhase.homogeneousDensity xMean sat
                let qCritLocal =
                    match case.Water.ChfModel with
                    | WaterSide.PalenBundle -> qCritTube * phiB * max 0.05 (1.0 - xOut)
                    | model -> WaterSide.chfLocal model tube.Do (gCross / rhoH) xOut 1.0 phiB qCritTube sat

                for c in 0 .. nc - 1 do
                    let (frac, ferruleLength, inFerrule, props, stIn, cell) = res.[c]
                    let (qLin, gasRes, hBoil, twi, tmi, tmo, rBoil, rFoulOut, _, cellOk) = cell
                    if not cellOk then nonConvergedCells <- nonConvergedCells + 1
                    twiPrev.[j, c] <- twi
                    let bore = if inFerrule then case.Ferrule.Bore else tube.Di
                    let qOut = qLin / (Math.PI * tube.Do)
                    let km = properties.TubeK (kToC (0.5 * (tmi + tmo)))
                    let rm = 0.5 * (tube.Di + tube.Do) / 2.0
                    let ri = tube.Di / 2.0
                    let ro = tube.Do / 2.0
                    let tWallAvg =
                        tmo + qLin / (2.0 * Math.PI * km)
                              * (0.5 - ri * ri * log (ro / ri) / (ro * ro - ri * ri))
                    cells.[i, j, c] <-
                        { I = i; J = j; C = c; Frac = frac; FerruleLen = ferruleLength
                          Z = z; Y = band.Y; NTubes = band.NTubes * frac
                          TGas = props.T; PGas = pGas.[j, c]
                          VelGas = gasRes.Velocity; ReGas = gasRes.Re
                          HConvGas = gasRes.HConv; HRadGas = gasRes.HRad; EpsGas = gasRes.EpsGas
                          XIn = x; XOut = xOut; Alpha = alpha
                          GCross = gCross; VelCross = gCross / rhoH
                          HBoil = hBoil
                          U_o = (if props.T > sat.Tsat then qOut / (props.T - sat.Tsat) else 0.0)
                          QLin = qLin
                          QFluxIn = qLin / (Math.PI * bore)
                          QFluxOut = qOut
                          TMetalIn = tmi
                          TMetalMid = tmo + qLin / (2.0 * Math.PI * km) * log ((tube.Do / 2.0) / rm)
                          TMetalOut = tmo
                          TMetalWallAvg = tWallAvg
                          TWallBoil = sat.Tsat + qLin * rBoil
                          DTsatWall = qLin * rBoil
                          DTDeposit = qLin * rFoulOut
                          DTMetalSat = tmo - sat.Tsat
                          QCritLocal = qCritLocal
                          DNBR = (if qOut > 0.0 then qCritLocal / qOut else 999.0)
                          InFerrule = inFerrule }
                    let (dpF, dpLoc) =
                        BundleSolverSupport.cellPressureDrop case props gasRes bore z dz ferruleLength inFerrule i nz
                    let hOut = hGas.[j, c] - qLin * dz / mdotPerTube
                    let pOut = pGas.[j, c] - dpF - dpLoc
                    let areaFlow = Math.PI * bore * bore / 4.0
                    let tau = dz * areaFlow * props.Rho / max 1e-9 mdotPerTube
                    let tRef = 0.5 * (stIn.T + tmo)
                    let compOut = BundleSolverSupport.advanceProcessComposition case tRef tau procComp.[j, c]
                    hGas.[j, c] <- hOut
                    pGas.[j, c] <- pOut
                    procComp.[j, c] <- compOut
                    let stOut = processModel.StateFromEnthalpyAt pOut compOut hOut
                    if processModel.Active && stOut.Condensing then
                        sulphurCondensingCells <- sulphurCondensingCells + 1
                        if Double.IsNaN sulphurFirstCondensationZ then sulphurFirstCondensationZ <- z

                x <- xOut
                dutySlice <- dutySlice + dQband
                bandDuty.[j] <- bandDuty.[j] + dQband

            dutyLin.[i] <- dutySlice / dz
            steamLin.[i] <- dutySlice / dz / sat.Hfg
            dutyCum <- dutyCum + dutySlice
            steamCum <- steamCum + dutySlice / sat.Hfg
            axial.Add (BundleSolverSupport.summarizeAxialSlice case sat cells steamLin dutyLin steamCum dutyCum wl i ny nc)

        let outletStates = BundleSolverSupport.outletStates processModel pGas procComp hGas ny nc
        let tOut = BundleSolverSupport.outletTemperatures case outletStates pGas procComp hGas ny nc
        let dpMomentum =
            BundleSolverSupport.momentumPressureDrop case processModel outletStates pGas tOut procComp mdotPerTube ny nc
        let dpGas = BundleSolverSupport.weightedGasPressureDrop case bandArr clsArr pGas dpMomentum ny nc
        let sulphurCoupling =
            BundleSolverSupport.sulphurSummary outletStates processModel.InitialState bandArr clsArr ny nc
                sulphurCondensingCells sulphurFirstCondensationZ

        { Cells = cells
          Axial = List.ofSeq axial
          Duty = dutyCum
          Steam = steamCum
          DpGas = dpGas
          SteamLin = steamLin
          TGasOutBandClass = tOut
          NTubesBand = bandArr |> Array.map (fun b -> b.NTubes)
          Classes = classes
          Dz = dzArr
          ZC = zc
          QualityClamped = qualityClamped
          QualityClampFirstZ = qualityClampFirstZ
          NonConvergedCells = nonConvergedCells
          BandDuty = bandDuty
          OutletCompositionBandClass = procComp
          SulphurCoupling = sulphurCoupling }
