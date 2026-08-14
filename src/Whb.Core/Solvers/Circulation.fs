namespace Whb.Core

open System
open Constants
open Types

/// <summary>
/// Solves WHB natural-circulation flow distribution using thermal duty, riser/downcomer hydraulics, and two-phase pressure balance.
/// </summary>
/// <remarks>
/// Combines process heat balance, two-phase hydraulics, and circulation-loop pressure balance calculations. Review the selected correlations, riser/downcomer geometry, saturation properties, and SI-unit basis when interpreting circulation margins.
/// </remarks>
module Circulation =
    let heights (case: DesignCase) =
        let l = case.Loop
        let ro = case.Tube.Otl / 2.0
        let zWl = l.DzDrumWhb + l.DrumLevelOffset
        (zWl + ro, case.Tube.Otl, max 0.1 (zWl - ro))
    let branchArea (bs: Piping.Line list) = Piping.totalArea bs
    let branchDescription (bs: Piping.Line list) =
        bs |> List.map (fun b -> sprintf "%d x %s %s (ID %.1f mm)" b.Count b.Tag b.Nps (b.Id * 1000.0))
           |> String.concat " + "
    let private velLiquid (rho: float) (mu: float) (l: Piping.Line) (dp: float) =
        let mutable v = 1.0
        let mutable i = 0
        let mutable running = true
        // Contraction on velocity and friction: stop once it stops moving in double
        // precision, keeping the original iteration cap as the worst case.
        while running && i < 6 do
            let re = max 100.0 (rho * v * l.Id / mu)
            let f = GasSide.darcyFriction re (4.5e-5 / l.Id)
            let vNext = sqrt (2.0 * dp / (rho * max 0.1 (Piping.totalK f l)))
            running <- abs (vNext - v) > 1e-14 * abs vNext
            v <- vNext
            i <- i + 1
        v
    let dpParallelLiquid (bs: Piping.Line list) (rho: float) (mu: float) (wTot: float) =
        let flowAt (dp: float) =
            bs |> List.sumBy (fun l -> float l.Count * rho * velLiquid rho mu l dp * Piping.area l)
        let dp = bisect (fun dp -> flowAt dp - wTot) 1.0 5.0e6 1e-3 200
        let mutable vMax = 0.0
        for l in bs do
            let v = velLiquid rho mu l dp
            if v > vMax then vMax <- v
        (dp, vMax)
    let dpLineTwoPhase (case: DesignCase) (sat: Steam.SatProps) (x: float) (l: Piping.Line) (w: float) =
        let rhoH = TwoPhase.homogeneousDensity x sat
        let g_ = w / Piping.area l
        let reLO = max 1.0 (g_ * l.Id / sat.MuL)
        let f = GasSide.darcyFriction reLO (4.5e-5 / l.Id)
        let kLoc = (l.Elbows |> List.sumBy (Piping.elbowK f)) + l.ExtraK + 0.5 + 1.0
        TwoPhase.dpFrictionTwoPhase case.Loop.FrictionModel x g_ l.Id (Piping.developedLength l) sat
        + kLoc * TwoPhase.phi2LO case.Loop.FrictionModel x g_ l.Id sat * g_ * g_ / (2.0 * sat.RhoL)
        + TwoPhase.dpAcceleration case.Loop.VoidModel 0.0 x g_ sat
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
    let lineFlows (case: DesignCase) (sat: Steam.SatProps) (bs: Piping.Line list)
                  (twoPhase: bool) (x: float) (wTot: float) =
        if twoPhase then
            let dp = fst (dpParallelTwoPhase case bs sat x wTot)
            bs |> List.map (fun l ->
                (l, bisect (fun w -> dpLineTwoPhase case sat x l w - dp) 1e-6 (wTot * 4.0 + 1.0) 1e-4 60))
        else
            let dp = fst (dpParallelLiquid bs sat.RhoL sat.MuL wTot)
            bs |> List.map (fun l -> (l, sat.RhoL * velLiquid sat.RhoL sat.MuL l dp * Piping.area l))
    let dpDrumInternalsDefault = 5000.0
    /// <summary>
    /// Fraction of the bundle duty raised in each vertical band.
    /// </summary>
    /// <remarks>
    /// The thermal solver knows the real profile - the bottom bands face the hot gas and raise
    /// far more steam than the top ones - so the hydraulics must use it rather than assume an
    /// even split, otherwise the quality that drives the circulation is not the quality the
    /// heat transfer produced. Falls back to an even split when no profile is supplied.
    /// </remarks>
    let bandDutyFractions (bandDuty: float[]) (nBands: int) =
        let even = if nBands > 0 then 1.0 / float nBands else 1.0
        if isNull (box bandDuty) || bandDuty.Length <> nBands then Array.create (max 1 nBands) even
        else
            let total = Array.sum bandDuty
            if total <= 0.0 then Array.create (max 1 nBands) even
            else bandDuty |> Array.map (fun q -> q / total)
    let dpFieldColumnWith (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                          (bandFrac: float[]) (wLin: float) (steamLin: float) (xIn: float) =
        let t = case.Tube
        let a = t.Pitch / t.Do
        let b2 = t.Pitch * 0.8660254 / t.Do
        let mutable x = xIn
        let mutable dp = 0.0
        let mutable rhoAcc = 0.0
        let mutable hAcc = 0.0
        let mutable bi = 0
        for bd in bands do
            let dQfrac = if bi < bandFrac.Length then bandFrac.[bi] else 0.0
            bi <- bi + 1
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
    /// Even-split form, kept for callers that do not have a duty profile to hand.
    let dpFieldColumn (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                      (wLin: float) (steamLin: float) (xIn: float) =
        dpFieldColumnWith case sat bands (bandDutyFractions null (List.length bands)) wLin steamLin xIn
    let dpFieldFriction (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                        (bandFrac: float[]) (wLin: float) (steamLin: float) (xIn: float) =
        let (dp, _, rho) = dpFieldColumnWith case sat bands bandFrac wLin steamLin xIn
        dp - rho * g * case.Tube.Otl
    let driftVelocity (sat: Steam.SatProps) =
        1.53 * Math.Pow(sat.Sigma * g * (sat.RhoL - sat.RhoV) / (sat.RhoL * sat.RhoL), 0.25)
    let annulusState (sat: Steam.SatProps) (aByp: float) (alphaTop: float) (wLin: float) =
        if wLin >= 0.0 || aByp <= 1e-9 then (sat.RhoL, 0.0, 0.0)
        else
            let vl = abs wLin / (sat.RhoL * aByp)
            let vgj = driftVelocity sat
            let f = if vl <= vgj then 0.0 else 1.0 - vgj / vl
            let a = max 0.0 (min 0.95 (alphaTop * f))
            let rho = a * sat.RhoV + (1.0 - a) * sat.RhoL
            (rho, a, a * sat.RhoV / max 1e-9 rho)
    let private dpAnnulus (sat: Steam.SatProps) (aByp: float) (alphaTop: float)
                          (h: float) (wLin: float) =
        let (rho, _, _) = annulusState sat aByp alphaTop wLin
        let kTot = 3.0
        let v = abs wLin / (rho * max 1e-9 aByp)
        rho * g * h + float (sign wLin) * kTot * rho * v * v / 2.0
    let splitSlice (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                   (bandFrac: float[]) (aFieldMean: float) (aByp: float) (wExt: float) (steamLin: float) =
        let h = case.Tube.Otl
        if aByp <= 1e-9 then
            let (dp, _, _) = dpFieldColumnWith case sat bands bandFrac wExt steamLin 0.0
            (wExt, 0.0, dp, 0.0, 0.0, 0.0)
        else
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
                let (dpF, _, _) = dpFieldColumnWith case sat bands bandFrac wField steamLin xi
                dpF - dpAnnulus sat aByp alphaTop h wByp
            let wField = bisect resid (max 1e-3 (0.2 * wExt)) (60.0 * wExt + 20.0) 1e-5 140
            let (wByp, _, aB, xc, xi) = stateOf wField
            let (dpF, _, _) = dpFieldColumnWith case sat bands bandFrac wField steamLin xi
            (wField, wByp, dpF, xi, aB, xc)
    type Distribution =
        { WExtLin: float[]
          WFieldLin: float[]
          WBypLin: float[]
          XInField: float[]
          /// Sign changes of the loop balance over its bracket. More than one means the
          /// operating point is not unique.
          RootCount: int
          /// False when the bracket held no sign change at all, so the reported flow is a
          /// clamped endpoint rather than a solution.
          BracketOk: bool
          /// Slope of the loop balance at the operating point. Negative is stable (Ledinegg).
          BalanceSlope: float
          Global: CirculationResult }
    let solve (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
              (bandDuty: float[]) (steamLin: float[]) (dzArr: float[]) : Distribution =
        // Duty split per band as produced by the thermal solver, not assumed flat.
        let bandFrac = bandDutyFractions bandDuty (List.length bands)
        let (hDc, hF, hR) = heights case
        let l = case.Loop
        let t = case.Tube
        let nz = steamLin.Length
        let mutable steamIntegral = 0.0
        for i in 0 .. nz - 1 do
            steamIntegral <- steamIntegral + steamLin.[i] * dzArr.[i]
        let steamTot = max 1e-6 steamIntegral
        let aDc = branchArea l.Downcomers
        let aR = branchArea l.Risers
        let gravDc = sat.RhoL * g * hDc
        let aByp =
            if case.AllowInternalRecirculation then
                Bundle.openAnnulusArea t.ShellId t.BaffleOd t.Otl
                * max 0.0 (min 1.0 case.BypassOpenFraction)
            else 0.0
        let aFieldMean = bands |> List.averageBy (fun b -> b.FieldFreeArea)

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
                let (_, _, dp, _, _, _) = splitSlice case sat bands bandFrac aFieldMean aByp wl steamLin.[i]
                dem <- dem + dp * wl * dzArr.[i] / wTot
            dem

        let balance (w: float) =
            (let (av, _, _, _, _, _) = available w in av) - fieldDemand w
        let wLo = 1.5 * steamTot
        let wHi = 200.0 * steamTot
        // The loop balance is the driving head minus the bundle demand. The demand of a
        // boiling channel is S-shaped in flow, so the balance can cross zero more than once
        // (Ledinegg): scan the bracket first and report the multiplicity instead of silently
        // returning whichever root the solver happens to walk into.
        let rootCount = countSignChanges balance wLo wHi 40
        let (wTot, wStatus) = bisectWithStatus balance wLo wHi 1e-3 90
        // Ledinegg criterion. Writing the balance as supply minus demand, a stable operating
        // point needs the demand to rise faster than the supply with flow, i.e. the balance
        // must cross zero downwards. A crossing with the opposite slope is a flow-excursion
        // point: a small disturbance drives the loop away from it instead of back to it.
        let balanceSlope =
            let h = max 1e-6 (0.01 * wTot)
            (balance (wTot + h) - balance (wTot - h)) / (2.0 * h)
        let (av, dpDc, dpR, rhoR, alphaR, xBar) = available wTot
        let cr = wTot / steamTot

        let wExt = Array.init nz (fun i -> max 1e-4 (cr * steamLin.[i]))
        let wField = Array.zeroCreate nz
        let wByp = Array.zeroCreate nz
        let xIn = Array.zeroCreate nz
        let mutable aBypAcc = 0.0
        let mutable xcAcc = 0.0
        let mutable fricAcc = 0.0
        let mutable wFieldTot = 0.0
        let mutable wFieldSum = 0.0
        let mutable wBypTot = 0.0
        let mutable steamSum = 0.0
        let mutable xInSum = 0.0
        // A slice is starved when the liquid crossing it is less than the steam raised in it:
        // the local circulation ratio drops below one and the slice cannot stay wetted.
        let mutable starved = 0
        for i in 0 .. nz - 1 do
            let (wf, wb, _, xi, ab, xc) = splitSlice case sat bands bandFrac aFieldMean aByp wExt.[i] steamLin.[i]
            if steamLin.[i] > 1e-9 && wf / steamLin.[i] < 1.0 then starved <- starved + 1
            wField.[i] <- wf
            wByp.[i] <- wb
            xIn.[i] <- xi
            wFieldTot <- wFieldTot + wf * dzArr.[i]
            wFieldSum <- wFieldSum + wf
            wBypTot <- wBypTot + wb * dzArr.[i]
            steamSum <- steamSum + steamLin.[i]
            xInSum <- xInSum + xi
            let wgt = wExt.[i] * dzArr.[i] / wTot
            aBypAcc <- aBypAcc + wgt * ab
            xcAcc <- xcAcc + wgt * xc
            fricAcc <- fricAcc + wgt * dpFieldFriction case sat bands bandFrac wf steamLin.[i] xi

        let effCr = wFieldTot / steamTot
        let invNz = 1.0 / float nz
        let wFieldMean = wFieldSum * invNz
        let steamMean = steamSum * invNz
        let xInMean = xInSum * invNz
        let xField = min 0.95 (1.0 / effCr + xInMean)
        let alphaB =
            TwoPhase.voidFraction l.VoidModel xField sat (wFieldMean / aFieldMean)
        let rhoField =
            let (_, _, rho) =
                dpFieldColumnWith case sat bands bandFrac wFieldMean steamMean xInMean
            rho
        let dpDrumFinal = drumDp wTot xBar

        { WExtLin = wExt
          WFieldLin = wField
          WBypLin = wByp
          XInField = xIn
          RootCount = rootCount
          BracketOk = (wStatus <> NoSignChange)
          BalanceSlope = balanceSlope
          Global =
            { CirculationRatio = cr
              CircFlow = wTot
              SteamFlow = steamTot
              DrivingHead = gravDc - rhoR * g * hR - rhoField * g * hF
              DpDowncomer = dpDc
              DpBundle = fricAcc
              DpRiser = dpR
              DpNozzles = dpDrumFinal
              DpTotal = dpDc + dpR + fricAcc + dpDrumFinal
              XOutBundle = xField
              XOutRiser = xBar
              AlphaOutBundle = alphaB
              AlphaOutRiser = alphaR
              VelDowncomer = wTot / (sat.RhoL * aDc)
              VelRiserMix = wTot / (TwoPhase.homogeneousDensity xBar sat * aR)
              HDowncomer = hDc
              HShell = hF
              HRiser = hR
              BypassFraction = wBypTot / max 1e-9 wTot
              EffectiveCR = effCr
              BypassAlpha = aBypAcc
              XCarryUnder = xcAcc
              OpenAnnulus = aByp
              StarvedSlices = starved
              Converged = av > 0.0 } }
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
            let nearest (positions: float[]) (z: float) =
                let mutable best = 0
                let mutable bestDist = Double.PositiveInfinity
                for i in 0 .. positions.Length - 1 do
                    let d = abs (positions.[i] - z)
                    if d < bestDist then
                        best <- i
                        bestDist <- d
                best
            let axialFlow (positions: float list) =
                let pos = positions |> List.toArray
                let idx = arr |> Array.map (fun a -> nearest pos a.Z)
                let res = Array.zeroCreate n
                for k in 0 .. pos.Length - 1 do
                    let zN = pos.[k]
                    let left = ResizeArray<int>()
                    let right = ResizeArray<int>()
                    for i in 0 .. n - 1 do
                        if idx.[i] = k then
                            if arr.[i].Z <= zN then left.Add i else right.Add i
                    if left.Count + right.Count > 0 then
                        left.Sort()
                        right.Sort()
                        let mutable acc = 0.0
                        for p in left.Count - 1 .. -1 .. 0 do
                            let i = left.[p]
                            acc <- acc + wExtLin.[i] * dzArr.[i]
                            res.[i] <- acc
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




