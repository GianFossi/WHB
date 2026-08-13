namespace Whb.Core

open System
open Constants
open Types

/// <summary>
/// Provides circulation functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Circulation =

    /// <summary>
    /// Calculates or returns heights for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let heights (case: DesignCase) =
        let l = case.Loop
        let ro = case.Tube.Otl / 2.0
        let zWl = l.DzDrumWhb + l.DrumLevelOffset
        (zWl + ro, case.Tube.Otl, max 0.1 (zWl - ro))

    /// <summary>
    /// Calculates or returns brancharea for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let branchArea (bs: Piping.Line list) = Piping.totalArea bs

    /// <summary>
    /// Calculates or returns branchdescription for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let branchDescription (bs: Piping.Line list) =
        bs |> List.map (fun b -> sprintf "%d x %s %s (ID %.1f mm)" b.Count b.Tag b.Nps (b.Id * 1000.0))
           |> String.concat " + "

    /// <summary>
    /// Calculates or returns velLiquid for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private velLiquid (rho: float) (mu: float) (l: Piping.Line) (dp: float) =
        let mutable v = 1.0
        for _ in 1 .. 6 do
            let re = max 100.0 (rho * v * l.Id / mu)
            let f = GasSide.darcyFriction re (4.5e-5 / l.Id)
            v <- sqrt (2.0 * dp / (rho * max 0.1 (Piping.totalK f l)))
        v

    /// <summary>
    /// Calculates or returns dpparallelliquid for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpParallelLiquid (bs: Piping.Line list) (rho: float) (mu: float) (wTot: float) =
        let flowAt (dp: float) =
            bs |> List.sumBy (fun l -> float l.Count * rho * velLiquid rho mu l dp * Piping.area l)
        let dp = bisect (fun dp -> flowAt dp - wTot) 1.0 5.0e6 1e-3 200
        let vMax = bs |> List.map (fun l -> velLiquid rho mu l dp) |> List.max
        (dp, vMax)

    /// <summary>
    /// Calculates or returns dplinetwophase for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpLineTwoPhase (case: DesignCase) (sat: Steam.SatProps) (x: float) (l: Piping.Line) (w: float) =
        let rhoH = TwoPhase.homogeneousDensity x sat
        let g_ = w / Piping.area l
        let reLO = max 1.0 (g_ * l.Id / sat.MuL)
        let f = GasSide.darcyFriction reLO (4.5e-5 / l.Id)
        let kLoc = (l.Elbows |> List.sumBy (Piping.elbowK f)) + l.ExtraK + 0.5 + 1.0
        TwoPhase.dpFrictionTwoPhase case.Loop.FrictionModel x g_ l.Id (Piping.developedLength l) sat
        + kLoc * TwoPhase.phi2LO case.Loop.FrictionModel x g_ l.Id sat * g_ * g_ / (2.0 * sat.RhoL)
        + TwoPhase.dpAcceleration case.Loop.VoidModel 0.0 x g_ sat

    /// <summary>
    /// Calculates or returns dpparalleltwophase for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpParallelTwoPhase (case: DesignCase) (bs: Piping.Line list)
                           (sat: Steam.SatProps) (x: float) (wTot: float) =
        let rhoH = TwoPhase.homogeneousDensity x sat
        let flowAt (dp: float) =
            bs |> List.sumBy (fun l ->
                float l.Count * bisect (fun w -> dpLineTwoPhase case sat x l w - dp) 1e-6 (wTot * 4.0 + 1.0) 1e-4 60)
        let dp = bisect (fun dp -> flowAt dp - wTot) 1.0 5.0e6 1e-3 200
        let vMix =
            bs |> List.map (fun l ->
                let w = bisect (fun w -> dpLineTwoPhase case sat x l w - dp) 1e-6 (wTot * 4.0 + 1.0) 1e-4 60
                w / (rhoH * Piping.area l))
            |> List.max
        (dp, vMix)

    /// <summary>
    /// Calculates or returns lineflows for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let lineFlows (case: DesignCase) (sat: Steam.SatProps) (bs: Piping.Line list)
                  (twoPhase: bool) (x: float) (wTot: float) =
        if twoPhase then
            let dp = fst (dpParallelTwoPhase case bs sat x wTot)
            bs |> List.map (fun l ->
                (l, bisect (fun w -> dpLineTwoPhase case sat x l w - dp) 1e-6 (wTot * 4.0 + 1.0) 1e-4 60))
        else
            let dp = fst (dpParallelLiquid bs sat.RhoL sat.MuL wTot)
            bs |> List.map (fun l -> (l, sat.RhoL * velLiquid sat.RhoL sat.MuL l dp * Piping.area l))

    /// <summary>
    /// Calculates or returns dpdruminternalsdefault for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpDrumInternalsDefault = 5000.0

    /// <summary>
    /// Calculates or returns dpfieldcolumn for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpFieldColumn (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                      (wLin: float) (steamLin: float) (xIn: float) =
        let t = case.Tube
        let a = t.Pitch / t.Do
        let b2 = t.Pitch * 0.8660254 / t.Do
        let nb = List.length bands
        let dQfrac = if nb > 0 then 1.0 / float nb else 1.0
        let mutable x = xIn
        let mutable dp = 0.0
        let mutable rhoAcc = 0.0
        let mutable hAcc = 0.0
        for bd in bands do
            let g_ = wLin / bd.FieldFreeArea
            let xOut = min 0.95 (x + steamLin * dQfrac / (max 1e-6 wLin))
            let xm = 0.5 * (x + xOut)
            dp <- dp + TwoPhase.dpCrossflowTwoPhase xm bd.Rows g_ sat t.Do a b2 t.Staggered
            let alpha = TwoPhase.voidFraction case.Loop.VoidModel xm sat g_
            rhoAcc <- rhoAcc + TwoPhase.mixtureDensity alpha sat * bd.Height
            hAcc <- hAcc + bd.Height
            x <- xOut
        let rhoMean = if hAcc > 0.0 then rhoAcc / hAcc else sat.RhoL
        (dp + rhoMean * g * hAcc, x, rhoMean)

    /// <summary>
    /// Calculates or returns dpfieldfriction for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpFieldFriction (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                        (wLin: float) (steamLin: float) (xIn: float) =
        let (dp, _, rho) = dpFieldColumn case sat bands wLin steamLin xIn
        dp - rho * g * case.Tube.Otl

    /// <summary>
    /// Calculates or returns driftvelocity for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let driftVelocity (sat: Steam.SatProps) =
        1.53 * Math.Pow(sat.Sigma * g * (sat.RhoL - sat.RhoV) / (sat.RhoL * sat.RhoL), 0.25)

    /// <summary>
    /// Calculates or returns annulusstate for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let annulusState (sat: Steam.SatProps) (aByp: float) (alphaTop: float) (wLin: float) =
        if wLin >= 0.0 || aByp <= 1e-9 then (sat.RhoL, 0.0, 0.0)
        else
            let vl = abs wLin / (sat.RhoL * aByp)
            let vgj = driftVelocity sat
            let f = if vl <= vgj then 0.0 else 1.0 - vgj / vl
            let a = max 0.0 (min 0.95 (alphaTop * f))
            let rho = a * sat.RhoV + (1.0 - a) * sat.RhoL
            (rho, a, a * sat.RhoV / max 1e-9 rho)

    /// <summary>
    /// Calculates or returns dpAnnulus for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private dpAnnulus (sat: Steam.SatProps) (aByp: float) (alphaTop: float)
                          (h: float) (wLin: float) =
        let (rho, _, _) = annulusState sat aByp alphaTop wLin
        let kTot = 3.0
        let v = abs wLin / (rho * max 1e-9 aByp)
        rho * g * h + float (sign wLin) * kTot * rho * v * v / 2.0

    /// <summary>
    /// Calculates or returns splitslice for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let splitSlice (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                   (aByp: float) (wExt: float) (steamLin: float) =
        let h = case.Tube.Otl
        if aByp <= 1e-9 then
            let (dp, _, _) = dpFieldColumn case sat bands wExt steamLin 0.0
            (wExt, 0.0, dp, 0.0, 0.0, 0.0)
        else
            let aFieldMean = bands |> List.averageBy (fun b -> b.FieldFreeArea)
            let stateOf (wField: float) =
                let wByp = wExt - wField
                let xTop0 = min 0.95 (steamLin / max 1e-6 wField)
                let alphaTop =
                    TwoPhase.voidFraction case.Loop.VoidModel xTop0 sat (wField / aFieldMean)
                let (_, aB, xc) = annulusState sat aByp alphaTop wByp
                let xi = if wByp < 0.0 then min 0.5 (abs wByp * xc / max 1e-6 wField) else 0.0
                (wByp, alphaTop, aB, xc, xi)
            let resid (wField: float) =
                let (wByp, alphaTop, _, _, xi) = stateOf wField
                let (dpF, _, _) = dpFieldColumn case sat bands wField steamLin xi
                dpF - dpAnnulus sat aByp alphaTop h wByp
            let wField = bisect resid (max 1e-3 (0.2 * wExt)) (60.0 * wExt + 20.0) 1e-5 140
            let (wByp, _, aB, xc, xi) = stateOf wField
            let (dpF, _, _) = dpFieldColumn case sat bands wField steamLin xi
            (wField, wByp, dpF, xi, aB, xc)

    /// <summary>
    /// Represents distribution data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Distribution =
        { WExtLin: float[]
          WFieldLin: float[]
          WBypLin: float[]
          XInField: float[]
          Global: CirculationResult }

    /// <summary>
    /// Calculates or returns solve for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let solve (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
              (steamLin: float[]) (dzArr: float[]) : Distribution =
        let (hDc, hF, hR) = heights case
        let l = case.Loop
        let t = case.Tube
        let nz = steamLin.Length
        let steamTot = max 1e-6 (Array.map2 (*) steamLin dzArr |> Array.sum)
        let aDc = branchArea l.Downcomers
        let aR = branchArea l.Risers
        let gravDc = sat.RhoL * g * hDc
        let aByp =
            if case.AllowInternalRecirculation then
                Bundle.openAnnulusArea t.ShellId t.BaffleOd t.Otl
                * max 0.0 (min 1.0 case.BypassOpenFraction)
            else 0.0

        let drumDp (wTot: float) (xBar: float) =
            if l.Drum.Enabled then
                (Drum.solve l.Drum sat wTot xBar steamTot aR aDc).DpCirculation
            else l.DrumInternalsDp

        let available (wTot: float) =
            let xBar = min 0.6 (steamTot / max 1e-6 wTot)
            let (dpDc, _) = dpParallelLiquid l.Downcomers sat.RhoL sat.MuL wTot
            let (dpR, _) = dpParallelTwoPhase case l.Risers sat xBar wTot
            let alphaR = TwoPhase.voidFraction l.VoidModel xBar sat (wTot / aR)
            let rhoR = TwoPhase.mixtureDensity alphaR sat
            let rhoH = TwoPhase.homogeneousDensity xBar sat
            let dpDrum = drumDp wTot xBar
            (gravDc - dpDc - dpR - dpDrum - rhoR * g * hR,
             dpDc, dpR, rhoR, alphaR, xBar)

        let fieldDemand (wTot: float) =
            let cr = wTot / steamTot
            let mutable dem = 0.0
            for i in 0 .. nz - 1 do
                let wl = max 1e-4 (cr * steamLin.[i])
                let (_, _, dp, _, _, _) = splitSlice case sat bands aByp wl steamLin.[i]
                dem <- dem + dp * wl * dzArr.[i] / wTot
            dem

        let wTot =
            bisect (fun w -> (let (av, _, _, _, _, _) = available w in av) - fieldDemand w)
                   (1.5 * steamTot) (200.0 * steamTot) 1e-3 90
        let (av, dpDc, dpR, rhoR, alphaR, xBar) = available wTot
        let cr = wTot / steamTot

        let wExt = Array.init nz (fun i -> max 1e-4 (cr * steamLin.[i]))
        let wField = Array.zeroCreate nz
        let wByp = Array.zeroCreate nz
        let xIn = Array.zeroCreate nz
        let mutable aBypAcc = 0.0
        let mutable xcAcc = 0.0
        let mutable fricAcc = 0.0
        for i in 0 .. nz - 1 do
            let (wf, wb, _, xi, ab, xc) = splitSlice case sat bands aByp wExt.[i] steamLin.[i]
            wField.[i] <- wf
            wByp.[i] <- wb
            xIn.[i] <- xi
            let wgt = wExt.[i] * dzArr.[i] / wTot
            aBypAcc <- aBypAcc + wgt * ab
            xcAcc <- xcAcc + wgt * xc
            fricAcc <- fricAcc + wgt * dpFieldFriction case sat bands wf steamLin.[i] xi

        let wFieldTot = Array.map2 (*) wField dzArr |> Array.sum
        let effCr = wFieldTot / steamTot
        let xField = min 0.95 (1.0 / effCr + (xIn |> Array.average))
        let aFieldMean = bands |> List.averageBy (fun b -> b.FieldFreeArea)
        let alphaB =
            TwoPhase.voidFraction l.VoidModel xField sat ((wField |> Array.average) / aFieldMean)
        let rhoField =
            let (_, _, rho) =
                dpFieldColumn case sat bands (wField |> Array.average)
                    (steamLin |> Array.average) (xIn |> Array.average)
            rho

        { WExtLin = wExt
          WFieldLin = wField
          WBypLin = wByp
          XInField = xIn
          Global =
            { CirculationRatio = cr
              CircFlow = wTot
              SteamFlow = steamTot
              DrivingHead = gravDc - rhoR * g * hR - rhoField * g * hF
              DpDowncomer = dpDc
              DpBundle = fricAcc
              DpRiser = dpR
              DpNozzles = drumDp wTot xBar
              DpTotal = dpDc + dpR + fricAcc + drumDp wTot xBar
              XOutBundle = xField
              XOutRiser = xBar
              AlphaOutBundle = alphaB
              AlphaOutRiser = alphaR
              VelDowncomer = wTot / (sat.RhoL * aDc)
              VelRiserMix = wTot / (TwoPhase.homogeneousDensity xBar sat * aR)
              HDowncomer = hDc
              HShell = hF
              HRiser = hR
              BypassFraction = (Array.map2 (*) wByp dzArr |> Array.sum) / max 1e-9 wTot
              EffectiveCR = effCr
              BypassAlpha = aBypAcc
              XCarryUnder = xcAcc
              OpenAnnulus = aByp
              StarvedSlices = 0
              Converged = av > 0.0 } }

    /// <summary>
    /// Calculates or returns axialvelocities for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let axialVelocities (case: DesignCase) (sat: Steam.SatProps)
                        (axial: AxialResult list) (wExtLin: float[]) (dzArr: float[])
                        (dcPositions: float list) (riserPositions: float list) =
        let t = case.Tube
        let arr = axial |> List.toArray
        let n = arr.Length
        if n < 2 then axial
        else
            let rs = t.ShellId / 2.0
            let ro = t.Otl / 2.0
            let segArea (r: float) (h: float) =
                let h = max 0.0 (min (2.0 * r) h)
                r * r * acos ((r - h) / r) - (r - h) * sqrt (max 0.0 (2.0 * r * h - h * h))
            let aPlenum = max 1e-3 (segArea rs (rs - ro))
            let nearest (positions: float list) (z: float) =
                positions |> List.mapi (fun i p -> (i, abs (p - z))) |> List.minBy snd |> fst
            let axialFlow (positions: float list) =
                let idx = arr |> Array.map (fun a -> nearest positions a.Z)
                let res = Array.zeroCreate n
                for k in 0 .. (List.length positions - 1) do
                    let members = [ for i in 0 .. n - 1 do if idx.[i] = k then yield i ]
                    if not members.IsEmpty then
                        let zN = positions.[k]
                        let left = members |> List.filter (fun i -> arr.[i].Z <= zN) |> List.sortDescending
                        let mutable acc = 0.0
                        for i in left do
                            acc <- acc + wExtLin.[i] * dzArr.[i]
                            res.[i] <- acc
                        let right = members |> List.filter (fun i -> arr.[i].Z > zN) |> List.sort
                        acc <- 0.0
                        for i in right do
                            acc <- acc + wExtLin.[i] * dzArr.[i]
                            res.[i] <- acc
                res
            let wB = axialFlow dcPositions
            let wT = axialFlow riserPositions
            arr
            |> Array.mapi (fun i a ->
                let rhoH = TwoPhase.homogeneousDensity a.XTop sat
                { a with
                    VelAxialBottom = wB.[i] / (sat.RhoL * aPlenum)
                    VelAxialTop = wT.[i] / (rhoH * aPlenum) })
            |> List.ofArray
