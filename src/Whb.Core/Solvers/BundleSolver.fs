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
    let ferruleResistance (f: Ferrule) (di: float) (tMeanC: float) =
        if not f.Enabled then 0.0
        else
            let rSleeve =
                if f.SleeveOd > f.Bore then log (f.SleeveOd / f.Bore) / (2.0 * Math.PI * f.SleeveK tMeanC)
                else 0.0
            let rIns =
                if di > f.SleeveOd then log (di / f.SleeveOd) / (2.0 * Math.PI * f.InsulK tMeanC)
                else 0.0
            rSleeve + rIns
    let ferruleInsulationThickness (f: Ferrule) (di: float) =
        if not f.Enabled then 0.0 else max 0.0 (0.5 * (di - f.SleeveOd))
    let ferruleInsulationFitStatus (f: Ferrule) (di: float) =
        if not f.Enabled then "NOT INSTALLED"
        elif f.Bore <= 0.0 || f.SleeveOd <= 0.0 || di <= 0.0 then "CHECK - invalid ferrule geometry"
        elif f.Bore >= f.SleeveOd then "CHECK - bore is not smaller than sleeve OD"
        elif f.SleeveOd >= di then "CHECK - no radial space for insulation paper"
        else
            let thk = ferruleInsulationThickness f di
            if thk < 0.0005 then "CHECK - insulation paper thinner than 0.5 mm"
            elif thk > 0.003 then "CHECK - insulation paper thicker than 3 mm"
            else "OK"

    /// <summary>
    /// Estimates the gas-side pressure drop across the ferrule component.
    /// </summary>
    /// <remarks>
    /// The estimate includes friction through the smaller ferrule bore and the local expansion from ferrule bore to tube ID.
    /// </remarks>
    let ferrulePressureDropEstimate
        (f: Ferrule)
        (tubeDi: float)
        (roughness: float)
        (mdotPerTube: float)
        (props: GasProps.MixProps)
        (length: float)
        =
        if not f.Enabled || length <= 0.0 then 0.0
        else
            let bore = max 1e-6 f.Bore
            let area = Math.PI * bore * bore / 4.0
            let gFlux = mdotPerTube / area
            let velocity = gFlux / props.Rho
            let re = gFlux * bore / props.Mu
            let fDarcy = GasSide.darcyFriction re (roughness / bore)
            let dpFriction = GasSide.dpFrictionPerM fDarcy bore props.Rho velocity * length
            let kExpansion = (1.0 - (bore / tubeDi) ** 2.0) ** 2.0
            let dpExpansion = GasSide.dpLocal kExpansion props.Rho velocity
            dpFriction + dpExpansion
    let ferruleClasses (f: Ferrule) =
        if not f.Enabled || f.Lengths.IsEmpty then [ (1.0, 0.0) ]
        else
            let s = f.Lengths |> List.sumBy fst
            if s <= 0.0 then [ (1.0, 0.0) ] else f.Lengths |> List.map (fun (a, b) -> (a / s, b))
    /// <summary>
    /// Part of the shell-side coefficient that is independent of the local heat flux.
    /// </summary>
    /// <remarks>
    /// The Chen convective term, the Chen suppression factor and the bundle factor depend
    /// only on quality and cross-flow mass flux, both fixed within a cell. Evaluating them
    /// once per cell instead of once per heat-flux iteration leaves the coefficient
    /// unchanged term by term.
    /// </remarks>
    [<Struct>]
    type ShellContext =
        { BundleFactor: float
          Suppression: float
          HConvChen: float }
    let shellContext (case: DesignCase) (sat: Steam.SatProps) (x: float) (gCross: float) =
        let t = case.Tube
        let d = t.Do
        let gl = gCross * (1.0 - x)
        let reMax = max 10.0 (gl * d / sat.MuL)
        let hLo = WaterSide.hZukauskas reMax sat.PrL sat.PrL sat.KL d t.Staggered (1.0 / 0.8660254)
        let fChen = WaterSide.chenF x sat
        { BundleFactor = WaterSide.bundleFactor case.Water.BundleFactor
          Suppression = WaterSide.chenS reMax fChen
          HConvChen = hLo * fChen }
    let shellHtcWith (case: DesignCase) (sat: Steam.SatProps) (ctx: ShellContext) (qOut: float) =
        let d = case.Tube.Do
        let wc = case.Water
        let hnb = WaterSide.hPool wc.Correlation qOut d sat wc.RoughnessUm wc.Csf
        let hnc = WaterSide.hNaturalConvection d (max 1.0 (qOut / 5000.0)) sat
        hnb * ctx.BundleFactor * ctx.Suppression + max ctx.HConvChen hnc
    let shellHtc (case: DesignCase) (sat: Steam.SatProps) (qOut: float) (x: float) (gCross: float) =
        shellHtcWith case sat (shellContext case sat x gCross) qOut
    let private solveCell
        (case: DesignCase) (sat: Steam.SatProps) (props: GasProps.MixProps)
        (z: float) (mdotPerTube: float) (inFerrule: bool)
        (rH2O: float) (rCO2: float) (x: float) (gCross: float) (twiGuess: float)
        (wallMu: float -> float -> float) (tubeK: float -> float) =

        let t = case.Tube
        let bore = if inFerrule then case.Ferrule.Bore else t.Di
        let tsat = sat.Tsat
        let tg = props.T
        let areaOutPerM = Math.PI * t.Do
        let mutable twi = twiGuess
        let mutable tmo = tsat + 10.0
        let mutable tmi = tsat + 40.0
        let mutable qlin = 0.0
        let mutable hb = 0.0
        let mutable rBoil = 0.0
        let mutable rFoulOut = 0.0
        let mutable rMetal = 0.0
        let mutable gasRes = Unchecked.defaultof<GasSide.GasHtcResult>
        let shellCtx = shellContext case sat x gCross

        // 14 relaxed sweeps settle an ordinary cell to well under a micro-kelvin, so that
        // count is kept as the minimum and the numbers are unchanged. A cell still moving by
        // more than a milli-kelvin after them is genuinely not converged - it happens near the
        // critical heat flux, where the boiling curve turns over - so it is given more sweeps
        // and reported if it still has not settled.
        let mutable sweep = 0
        let mutable moved = infinity
        while sweep < 14 || (sweep < 40 && moved > 1e-3) do
            let muWall = wallMu (max 300.0 twi) props.P
            let g = case.Gas
            gasRes <-
                GasSide.localHtc g.Correlation props muWall bore mdotPerTube z
                    g.EntranceC twi rH2O rCO2 g.EpsWall g.Radiation
            let rGas = 1.0 / (gasRes.HTot * Math.PI * bore)
            let rFoulIn = g.FoulingIn / (Math.PI * bore)
            let rFerr =
                if inFerrule then ferruleResistance case.Ferrule t.Di (kToC (0.5 * (twi + tmi)))
                else 0.0
            let km = tubeK (kToC (0.5 * (tmi + tmo)))
            rMetal <- log (t.Do / t.Di) / (2.0 * Math.PI * km)
            rFoulOut <- case.Water.FoulingOut / (Math.PI * t.Do)
            let rFixed = rGas + rFoulIn + rFerr + rMetal + rFoulOut
            let f (q': float) =
                let hbl = shellHtcWith case sat shellCtx (q' / areaOutPerM)
                (tg - tsat) / (rFixed + 1.0 / (max hbl 1.0 * areaOutPerM)) - q'
            let qMax = (tg - tsat) / (rFixed + 1e-9)
            // 1e-4 W/m on a linear duty of order 1e4 W/m keeps the residual two orders
            // below the convergence the outer relaxation loop below actually delivers.
            let q' = brent f 1e-3 (max 1.0 qMax) 1e-4 60
            hb <- shellHtcWith case sat shellCtx (q' / areaOutPerM)
            rBoil <- 1.0 / (max hb 1.0 * areaOutPerM)
            qlin <- q'
            let tmoNew = tsat + q' * (rBoil + rFoulOut)
            let dTmo = 0.7 * (tmoNew - tmo)
            let dTmi = 0.7 * (tmoNew + q' * rMetal - tmi)
            let dTwi = 0.7 * (tg - q' * (rGas + rFoulIn) - twi)
            tmo <- tmo + dTmo
            tmi <- tmi + dTmi
            twi <- twi + dTwi
            moved <- max (abs dTmo) (max (abs dTmi) (abs dTwi))
            sweep <- sweep + 1

        (qlin, gasRes, hb, twi, tmi, tmo, rBoil, rFoulOut, rMetal, moved <= 1e-3)
    type SolveOutput =
        { Cells: CellResult[,,]
          Axial: AxialResult list
          Duty: float
          Steam: float
          DpGas: float
          SteamLin: float[]
          TGasOutBandClass: float[,]
          NTubesBand: float[]
          Classes: (float * float) list
          Dz: float[]
          ZC: float[]
          /// Cells whose outlet quality hit the 0.95 barrier, and where the first one was.
          QualityClamped: int
          QualityClampFirstZ: float
          /// Cells whose wall-temperature fixed point was still moving at the iteration cap.
          NonConvergedCells: int
          /// Duty raised in each vertical band, integrated over the tube length [W].
          BandDuty: float[] }
    let solve (case: DesignCase) (bands: Bundle.Band list)
              (wLinField: float[]) (xInField: float[]) : SolveOutput =
        let t = case.Tube
        let sat = Steam.sat case.Water.DrumPressure
        let comp0 = GasProps.normalize case.Gas.Composition
        let rH2O = GasProps.molFrac comp0 GasProps.H2O
        let rCO2 = GasProps.molFrac comp0 GasProps.CO2
        let wallMuCache = Collections.Generic.Dictionary<struct (float * float), float>()
        let tubeKCache = Collections.Generic.Dictionary<float, float>()
        let wallMu tK pPa =
            let tRound = Math.Round(tK * 2.0) / 2.0
            let key = struct (tRound, Math.Round(pPa / 100.0) * 100.0)
            match wallMuCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v =
                    (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0
                         tRound pPa case.Gas.Z).Mu
                wallMuCache.[key] <- v
                v
        let tubeK tC =
            let key = Math.Round(tC * 2.0) / 2.0
            match tubeKCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v = case.Material.K key
                tubeKCache.[key] <- v
                v
        let bandArr = bands |> List.toArray
        let ny = bandArr.Length
        let classes = ferruleClasses case.Ferrule
        let clsArr = classes |> List.toArray
        let nc = clsArr.Length
        let nz = max 6 case.NZ
        let (zc, dzArr) = gradedAxialGrid t.Length nz case.AxialRefine
        let mdotPerTube = case.Gas.MassFlow / float t.NTubes

        let cells = Array3D.zeroCreate<CellResult> nz ny nc
        let hGas = Array2D.create ny nc (GasProps.enthalpyAbsReal case.Gas.RealGas comp0 case.Gas.TIn case.Gas.PIn)
        let pGas = Array2D.create ny nc case.Gas.PIn
        let compBand = Array2D.create ny nc comp0
        let twiPrev = Array2D.create ny nc (sat.Tsat + 0.6 * (case.Gas.TIn - sat.Tsat))

        let steamLin = Array.zeroCreate nz
        let dutyLin = Array.zeroCreate nz
        let axial = ResizeArray<AxialResult>()
        let mutable steamCum = 0.0
        let mutable dutyCum = 0.0
        let mutable qualityClamped = 0
        let mutable qualityClampFirstZ = nan
        let mutable nonConvergedCells = 0
        // Duty raised per vertical band: the circulation solver needs the real profile, not a
        // flat split, because the bottom bands see the hot gas and raise far more steam.
        let bandDuty = Array.zeroCreate ny
        let qCritTube =
            min (WaterSide.chfHorizontalTube t.Do sat) (WaterSide.chfMostinski sat.P Pc_water)
        let phiB = WaterSide.palenPhiB t.Otl t.Length (Math.PI * t.Do * t.Length * float t.NTubes)

        for i in 0 .. nz - 1 do
            let z = zc.[i]
            let dz = dzArr.[i]
            let wl = if i < wLinField.Length then max 1e-3 wLinField.[i] else 1.0
            let mutable x = if i < xInField.Length then xInField.[i] else 0.0
            let mutable dutySlice = 0.0

            for j in 0 .. ny - 1 do
                let b = bandArr.[j]
                let gCross = wl / b.FieldFreeArea
                let res =
                    Array.init nc (fun c ->
                        let (frac, fl) = clsArr.[c]
                        let inFerrule = case.Ferrule.Enabled && z < fl
                        let tG = fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pGas.[j, c] comp0 hGas.[j, c])
                        let props =
                            GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas compBand.[j, c] tG pGas.[j, c] case.Gas.Z
                        let r = solveCell case sat props z mdotPerTube inFerrule rH2O rCO2 x gCross twiPrev.[j, c] wallMu tubeK
                        (frac, fl, inFerrule, props, r))
                let dQband =
                    res |> Array.sumBy (fun (frac, _, _, _, (q, _, _, _, _, _, _, _, _, _)) ->
                        q * dz * b.NTubes * frac)
                let xRaw = x + dQband / (max 1e-6 (wl * dz) * sat.Hfg)
                if xRaw > 0.95 then
                    // The barrier is kept, but a run that leans on it is working outside the
                    // range the two-phase correlations were built for and must say so.
                    qualityClamped <- qualityClamped + 1
                    if Double.IsNaN qualityClampFirstZ then qualityClampFirstZ <- z
                let xOut = min 0.95 xRaw
                let xMean = 0.5 * (x + xOut)
                let alpha = TwoPhase.voidFraction case.Loop.VoidModel xMean sat gCross
                let rhoH = TwoPhase.homogeneousDensity xMean sat
                // The DNBR field is built with the CHF model selected for the case. Palen's
                // bundle factor with the quality derating is the default and reproduces the
                // previous behaviour exactly; the others exist so the same case can be
                // re-solved against a different limit and the whole map compared, instead of
                // only the single worst cell as in the comparison table.
                let qCritLocal =
                    match case.Water.ChfModel with
                    | WaterSide.PalenBundle -> qCritTube * phiB * max 0.05 (1.0 - xOut)
                    | model ->
                        WaterSide.chfLocal model t.Do (gCross / rhoH) xOut 1.0 phiB qCritTube sat

                for c in 0 .. nc - 1 do
                    let (frac, fl, inFerrule, props, r) = res.[c]
                    let (qlin, gr, hb, twi, tmi, tmo, rBoil, rFoulOut, rMetal, cellOk) = r
                    if not cellOk then nonConvergedCells <- nonConvergedCells + 1
                    twiPrev.[j, c] <- twi
                    let bore = if inFerrule then case.Ferrule.Bore else t.Di
                    let qOut = qlin / (Math.PI * t.Do)
                    let km = tubeK (kToC (0.5 * (tmi + tmo)))
                    let rm = 0.5 * (t.Di + t.Do) / 2.0
                    let ri = t.Di / 2.0
                    let ro = t.Do / 2.0
                    let tWallAvg =
                        tmo + qlin / (2.0 * Math.PI * km)
                              * (0.5 - ri * ri * log (ro / ri) / (ro * ro - ri * ri))
                    cells.[i, j, c] <-
                        { I = i; J = j; C = c; Frac = frac; FerruleLen = fl
                          Z = z; Y = b.Y; NTubes = b.NTubes * frac
                          TGas = props.T; PGas = pGas.[j, c]
                          VelGas = gr.Velocity; ReGas = gr.Re
                          HConvGas = gr.HConv; HRadGas = gr.HRad; EpsGas = gr.EpsGas
                          XIn = x; XOut = xOut; Alpha = alpha
                          GCross = gCross; VelCross = gCross / rhoH
                          HBoil = hb
                          U_o = (if props.T > sat.Tsat then qOut / (props.T - sat.Tsat) else 0.0)
                          QLin = qlin
                          QFluxIn = qlin / (Math.PI * bore)
                          QFluxOut = qOut
                          TMetalIn = tmi
                          TMetalMid = tmo + qlin / (2.0 * Math.PI * km) * log ((t.Do / 2.0) / rm)
                          TMetalOut = tmo
                          TMetalWallAvg = tWallAvg
                          TWallBoil = sat.Tsat + qlin * rBoil
                          DTsatWall = qlin * rBoil
                          DTDeposit = qlin * rFoulOut
                          DTMetalSat = tmo - sat.Tsat
                          QCritLocal = qCritLocal
                          DNBR = (if qOut > 0.0 then qCritLocal / qOut else 999.0)
                          InFerrule = inFerrule }
                    hGas.[j, c] <- hGas.[j, c] - qlin * dz / mdotPerTube
                    compBand.[j, c] <-
                        Shift.compositionFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pGas.[j, c] comp0 hGas.[j, c]
                    let f = GasSide.darcyFriction gr.Re (t.Roughness / bore)
                    let dpF = GasSide.dpFrictionPerM f bore props.Rho gr.Velocity * dz
                    let dpLoc =
                        if i = 0 then GasSide.dpLocal 0.5 props.Rho gr.Velocity
                        elif inFerrule && (z + dz) >= fl then
                            GasSide.dpLocal ((1.0 - (case.Ferrule.Bore / t.Di) ** 2.0) ** 2.0) props.Rho gr.Velocity
                        elif i = nz - 1 then GasSide.dpLocal 1.0 props.Rho gr.Velocity
                        else 0.0
                    pGas.[j, c] <- pGas.[j, c] - dpF - dpLoc

                x <- xOut
                dutySlice <- dutySlice + dQband
                bandDuty.[j] <- bandDuty.[j] + dQband

            dutyLin.[i] <- dutySlice / dz
            steamLin.[i] <- dutySlice / dz / sat.Hfg
            dutyCum <- dutyCum + dutySlice
            steamCum <- steamCum + dutySlice / sat.Hfg

            let mutable wSum = 0.0
            let mutable tGasWeighted = 0.0
            let mutable tGasMin = Double.PositiveInfinity
            let mutable tGasMax = Double.NegativeInfinity
            let mutable qFluxWeighted = 0.0
            let mutable qFluxMax = Double.NegativeInfinity
            let mutable tMetalInMax = Double.NegativeInfinity
            let mutable tMetalOutMax = Double.NegativeInfinity
            let mutable dnbrMin = Double.PositiveInfinity
            let mutable pGasWeighted = 0.0
            for j in 0 .. ny - 1 do
                for c in 0 .. nc - 1 do
                    let cell = cells.[i, j, c]
                    let wt = cell.NTubes
                    wSum <- wSum + wt
                    tGasWeighted <- tGasWeighted + cell.TGas * wt
                    pGasWeighted <- pGasWeighted + cell.PGas * wt
                    qFluxWeighted <- qFluxWeighted + cell.QFluxOut * wt
                    if cell.TGas < tGasMin then tGasMin <- cell.TGas
                    if cell.TGas > tGasMax then tGasMax <- cell.TGas
                    if cell.QFluxOut > qFluxMax then qFluxMax <- cell.QFluxOut
                    if cell.TMetalIn > tMetalInMax then tMetalInMax <- cell.TMetalIn
                    if cell.TMetalOut > tMetalOutMax then tMetalOutMax <- cell.TMetalOut
                    if cell.DNBR < dnbrMin then dnbrMin <- cell.DNBR
            let top = cells.[i, ny - 1, 0]
            let bot = cells.[i, 0, 0]
            let alphaTop = TwoPhase.voidFraction case.Loop.VoidModel top.XOut sat top.GCross
            let rhoHTop = TwoPhase.homogeneousDensity top.XOut sat
            let invWSum = 1.0 / max 1e-12 wSum
            axial.Add
                { Z = z
                  TGasMean = tGasWeighted * invWSum
                  TGasMin = tGasMin
                  TGasMax = tGasMax
                  QFluxMean = qFluxWeighted * invWSum
                  QFluxMax = qFluxMax
                  TMetalInMax = tMetalInMax
                  TMetalOutMax = tMetalOutMax
                  SteamLin = steamLin.[i]
                  DutyLin = dutyLin.[i]
                  WFieldLin = wl
                  WBypassLin = 0.0
                  XTop = top.XOut
                  AlphaTop = alphaTop
                  GCross = top.GCross
                  VelLiqIn = bot.GCross / sat.RhoL
                  VelMixOut = top.GCross / rhoHTop
                  VelVapOut = (if alphaTop > 1e-6 then top.GCross * top.XOut / (sat.RhoV * alphaTop) else 0.0)
                  VelAxialBottom = 0.0
                  VelAxialTop = 0.0
                  DNBRMin = dnbrMin
                  PGas = pGasWeighted * invWSum
                  SteamCum = steamCum
                  DutyCum = dutyCum }

        let tOut =
            Array2D.init ny nc (fun j c -> fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pGas.[j, c] comp0 hGas.[j, c]))
        // Momentum term. The gas is cooled at nearly constant pressure, so it roughly doubles
        // in density and halves in velocity along the tube: decelerating flow recovers
        // pressure. Summed cell by cell the term telescopes to the inlet and outlet states,
        // so it is applied once here rather than accumulated inside the march.
        let aTube = Math.PI * t.Di * t.Di / 4.0
        let gTube = mdotPerTube / aTube
        let rhoIn =
            (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0 case.Gas.TIn case.Gas.PIn case.Gas.Z).Rho
        let dpMomentum =
            Array2D.init ny nc (fun j c ->
                let rhoOut =
                    (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas compBand.[j, c]
                        tOut.[j, c] pGas.[j, c] case.Gas.Z).Rho
                gTube * gTube * (1.0 / rhoOut - 1.0 / rhoIn))
        // Tube-count weighted: bands do not carry the same number of tubes, so a plain mean
        // over bands is not the pressure drop the gas stream actually sees.
        let mutable dpAcc = 0.0
        let mutable dpWeight = 0.0
        for j in 0 .. ny - 1 do
            for c in 0 .. nc - 1 do
                let w = bandArr.[j].NTubes * fst clsArr.[c]
                dpAcc <- dpAcc + w * (case.Gas.PIn - pGas.[j, c] + dpMomentum.[j, c])
                dpWeight <- dpWeight + w
        { Cells = cells
          Axial = List.ofSeq axial
          Duty = dutyCum
          Steam = steamCum
          DpGas = dpAcc / max 1e-12 dpWeight
          SteamLin = steamLin
          TGasOutBandClass = tOut
          NTubesBand = bandArr |> Array.map (fun b -> b.NTubes)
          Classes = classes
          Dz = dzArr
          ZC = zc
          QualityClamped = qualityClamped
          QualityClampFirstZ = qualityClampFirstZ
          NonConvergedCells = nonConvergedCells
          BandDuty = bandDuty }




