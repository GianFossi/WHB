namespace Whb.Core

open System
open Constants
open Types

/// Risolutore bidimensionale: Nz sezioni lungo l'asse dell'apparecchio
/// × Ny bande orizzontali del fascio tubiero, × Nc classi di lunghezza
/// della ferrula.
///
/// - il gas marcia lungo z indipendentemente per ciascuna banda e classe
///   (i tubi sono canali paralleli);
/// - l'acqua attraversa il fascio dal basso verso l'alto, quindi il titolo
///   cresce da banda a banda nella stessa sezione assiale;
/// - le classi di ferrula rappresentano una popolazione di tubi con protezione
///   d'imbocco di lunghezza diversa: il tubo con la ferrula più corta è quello
///   che dimensiona il progetto.
module BundleSolver =

    /// Resistenza termica della ferrula per unità di lunghezza [m·K/W]
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

    /// Classi di ferrula normalizzate: (frazione, lunghezza [m])
    let ferruleClasses (f: Ferrule) =
        if not f.Enabled || f.Lengths.IsEmpty then [ (1.0, 0.0) ]
        else
            let s = f.Lengths |> List.sumBy fst
            if s <= 0.0 then [ (1.0, 0.0) ] else f.Lengths |> List.map (fun (a, b) -> (a / s, b))

    /// Coefficiente lato mantello per una cella, con titolo e flusso locali.
    let shellHtc (case: DesignCase) (sat: Steam.SatProps) (qOut: float) (x: float) (gCross: float) =
        let t = case.Tube
        let d = t.Do
        let wc = case.Water
        let hnb = WaterSide.hPool wc.Correlation qOut d sat wc.RoughnessUm wc.Csf
        let gl = gCross * (1.0 - x)
        let reMax = max 10.0 (gl * d / sat.MuL)
        let hLo = WaterSide.hZukauskas reMax sat.PrL sat.PrL sat.KL d t.Staggered (1.0 / 0.8660254)
        let fChen = WaterSide.chenF x sat
        let s = WaterSide.chenS reMax fChen
        let hnc = WaterSide.hNaturalConvection d (max 1.0 (qOut / 5000.0)) sat
        hnb * WaterSide.bundleFactor wc.BundleFactor * s + max (hLo * fChen) hnc

    let private solveCell
        (case: DesignCase) (sat: Steam.SatProps) (props: GasProps.MixProps)
        (z: float) (mdotPerTube: float) (inFerrule: bool)
        (rH2O: float) (rCO2: float) (x: float) (gCross: float) (twiGuess: float) =

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

        for _ in 1 .. 14 do
            let muWall =
                (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas case.Gas.Composition (max 300.0 twi) props.P case.Gas.Z).Mu
            let g = case.Gas
            gasRes <-
                GasSide.localHtc g.Correlation props muWall bore mdotPerTube z
                    g.EntranceC twi rH2O rCO2 g.EpsWall g.Radiation
            let rGas = 1.0 / (gasRes.HTot * Math.PI * bore)
            let rFoulIn = g.FoulingIn / (Math.PI * bore)
            let rFerr =
                if inFerrule then ferruleResistance case.Ferrule t.Di (kToC (0.5 * (twi + tmi)))
                else 0.0
            let km = case.Material.K (kToC (0.5 * (tmi + tmo)))
            rMetal <- log (t.Do / t.Di) / (2.0 * Math.PI * km)
            rFoulOut <- case.Water.FoulingOut / (Math.PI * t.Do)
            let rFixed = rGas + rFoulIn + rFerr + rMetal + rFoulOut
            let f (q': float) =
                let hbl = shellHtc case sat (q' / areaOutPerM) x gCross
                (tg - tsat) / (rFixed + 1.0 / (max hbl 1.0 * areaOutPerM)) - q'
            let qMax = (tg - tsat) / (rFixed + 1e-9)
            let q' = bisect f 1e-3 (max 1.0 qMax) 1e-2 60
            hb <- shellHtc case sat (q' / areaOutPerM) x gCross
            rBoil <- 1.0 / (max hb 1.0 * areaOutPerM)
            qlin <- q'
            let tmoNew = tsat + q' * (rBoil + rFoulOut)
            tmo <- tmo + 0.7 * (tmoNew - tmo)
            tmi <- tmi + 0.7 * (tmoNew + q' * rMetal - tmi)
            twi <- twi + 0.7 * (tg - q' * (rGas + rFoulIn) - twi)

        (qlin, gasRes, hb, twi, tmi, tmo, rBoil, rFoulOut, rMetal)

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
          ZC: float[] }

    /// Esegue la marcia 2-D.
    ///   wLinField : portata nel campo tubi per metro assiale [kg/(s·m)]
    ///   xInField  : titolo con cui l'acqua entra nella banda più bassa
    let solve (case: DesignCase) (bands: Bundle.Band list)
              (wLinField: float[]) (xInField: float[]) : SolveOutput =
        let t = case.Tube
        let sat = Steam.sat case.Water.DrumPressure
        let comp0 = GasProps.normalize case.Gas.Composition
        let rH2O = GasProps.molFrac comp0 GasProps.H2O
        let rCO2 = GasProps.molFrac comp0 GasProps.CO2
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
                        let r = solveCell case sat props z mdotPerTube inFerrule rH2O rCO2 x gCross twiPrev.[j, c]
                        (frac, fl, inFerrule, props, r))
                let dQband =
                    res |> Array.sumBy (fun (frac, _, _, _, (q, _, _, _, _, _, _, _, _)) ->
                        q * dz * b.NTubes * frac)
                let xOut = min 0.95 (x + dQband / (max 1e-6 (wl * dz) * sat.Hfg))
                let xMean = 0.5 * (x + xOut)
                let alpha = TwoPhase.voidFraction case.Loop.VoidModel xMean sat gCross
                let rhoH = TwoPhase.homogeneousDensity xMean sat
                let qCritLocal = qCritTube * phiB * max 0.05 (1.0 - xOut)

                for c in 0 .. nc - 1 do
                    let (frac, fl, inFerrule, props, r) = res.[c]
                    let (qlin, gr, hb, twi, tmi, tmo, rBoil, rFoulOut, rMetal) = r
                    twiPrev.[j, c] <- twi
                    let bore = if inFerrule then case.Ferrule.Bore else t.Di
                    let qOut = qlin / (Math.PI * t.Do)
                    let km = case.Material.K (kToC (0.5 * (tmi + tmo)))
                    let rm = 0.5 * (t.Di + t.Do) / 2.0
                    // temperatura media dello spessore pesata sull'area:
                    // T(r) = Tmo + q'/(2 pi k) ln(ro/r)
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
                    let (_, cN) = Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pGas.[j, c] comp0 hGas.[j, c]
                    compBand.[j, c] <- cN
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

            dutyLin.[i] <- dutySlice / dz
            steamLin.[i] <- dutySlice / dz / sat.Hfg
            dutyCum <- dutyCum + dutySlice
            steamCum <- steamCum + dutySlice / sat.Hfg

            let col = [ for j in 0 .. ny - 1 do for c in 0 .. nc - 1 -> cells.[i, j, c] ]
            let wSum = col |> List.sumBy (fun x -> x.NTubes)
            let top = cells.[i, ny - 1, 0]
            let bot = cells.[i, 0, 0]
            let alphaTop = TwoPhase.voidFraction case.Loop.VoidModel top.XOut sat top.GCross
            let rhoHTop = TwoPhase.homogeneousDensity top.XOut sat
            axial.Add
                { Z = z
                  TGasMean = (col |> List.sumBy (fun x -> x.TGas * x.NTubes)) / wSum
                  TGasMin = col |> List.map (fun x -> x.TGas) |> List.min
                  TGasMax = col |> List.map (fun x -> x.TGas) |> List.max
                  QFluxMean = (col |> List.sumBy (fun x -> x.QFluxOut * x.NTubes)) / wSum
                  QFluxMax = col |> List.map (fun x -> x.QFluxOut) |> List.max
                  TMetalInMax = col |> List.map (fun x -> x.TMetalIn) |> List.max
                  TMetalOutMax = col |> List.map (fun x -> x.TMetalOut) |> List.max
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
                  DNBRMin = col |> List.map (fun x -> x.DNBR) |> List.min
                  PGas = (col |> List.sumBy (fun x -> x.PGas * x.NTubes)) / wSum
                  SteamCum = steamCum
                  DutyCum = dutyCum }

        let tOut =
            Array2D.init ny nc (fun j c -> fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pGas.[j, c] comp0 hGas.[j, c]))
        let mutable dpAcc = 0.0
        for j in 0 .. ny - 1 do
            for c in 0 .. nc - 1 do
                dpAcc <- dpAcc + (case.Gas.PIn - pGas.[j, c])
        { Cells = cells
          Axial = List.ofSeq axial
          Duty = dutyCum
          Steam = steamCum
          DpGas = dpAcc / float (ny * nc)
          SteamLin = steamLin
          TGasOutBandClass = tOut
          NTubesBand = bandArr |> Array.map (fun b -> b.NTubes)
          Classes = classes
          Dz = dzArr
          ZC = zc }
