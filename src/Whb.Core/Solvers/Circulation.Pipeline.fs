namespace Whb.Core

open Constants
open Types

module CirculationPipeline =
    type PreparedSolve =
        { Case: DesignCase
          Sat: Steam.SatProps
          Bands: Bundle.Band list
          BandFrac: float[]
          SteamLin: float[]
          DzArr: float[]
          HDowncomer: float
          HField: float
          HRiser: float
          SteamTot: float
          DowncomerArea: float
          RiserArea: float
          GravDowncomer: float
          OpenAnnulus: float
          FieldAreaMean: float }

    [<Struct>]
    type AvailableHead =
        { Net: float
          DpDowncomer: float
          DpRiser: float
          RhoRiser: float
          AlphaRiser: float
          XOutRiser: float }

    [<Struct>]
    type OperatingPoint =
        { CircFlow: float
          RootCount: int
          BracketOk: bool
          BalanceSlope: float
          Available: AvailableHead
          CirculationRatio: float }

    [<Struct>]
    type SliceResult =
        { WExt: float
          WField: float
          WByp: float
          XIn: float
          BypassAlpha: float
          XCarryUnder: float
          DpFriction: float
          Starved: bool }

    [<Struct>]
    type SliceTotals =
        { WFieldTot: float
          WFieldSum: float
          WBypTot: float
          SteamSum: float
          XInSum: float
          BypassAlphaAcc: float
          XCarryUnderAcc: float
          FrictionAcc: float
          StarvedSlices: int }

    let prepareSolve (case: DesignCase) (sat: Steam.SatProps) (bands: Bundle.Band list)
                     (bandDuty: float[]) (steamLin: float[]) (dzArr: float[]) =
        let (hDc, hF, hR) = CirculationHydraulics.heights case
        let steamTot =
            Array.map2 (fun steam dz -> steam * dz) steamLin dzArr
            |> Array.sum
            |> max 1e-6
        let openAnnulus =
            if case.AllowInternalRecirculation then
                Bundle.openAnnulusArea case.Tube.ShellId case.Tube.BaffleOd case.Tube.Otl
                * max 0.0 (min 1.0 case.BypassOpenFraction)
            else 0.0
        { Case = case
          Sat = sat
          Bands = bands
          BandFrac = CirculationHydraulics.bandDutyFractions bandDuty (List.length bands)
          SteamLin = steamLin
          DzArr = dzArr
          HDowncomer = hDc
          HField = hF
          HRiser = hR
          SteamTot = steamTot
          DowncomerArea = CirculationHydraulics.branchArea case.Loop.Downcomers
          RiserArea = CirculationHydraulics.branchArea case.Loop.Risers
          GravDowncomer = sat.RhoL * g * hDc
          OpenAnnulus = openAnnulus
          FieldAreaMean = bands |> List.averageBy (fun b -> b.FieldFreeArea) }

    let private drumDp (prepared: PreparedSolve) (wTot: float) (xBar: float) =
        if prepared.Case.Loop.Drum.Enabled then
            (Drum.solve prepared.Case.Loop.Drum prepared.Sat wTot xBar prepared.SteamTot prepared.RiserArea prepared.DowncomerArea).DpCirculation
        else prepared.Case.Loop.DrumInternalsDp

    let availableHead (prepared: PreparedSolve) (wTot: float) =
        let xBar = min 0.6 (prepared.SteamTot / max 1e-6 wTot)
        let (dpDc, _) =
            CirculationHydraulics.dpParallelLiquid prepared.Case.Loop.Downcomers prepared.Sat.RhoL prepared.Sat.MuL wTot
        let (dpR, _) =
            CirculationHydraulics.dpParallelTwoPhase prepared.Case prepared.Case.Loop.Risers prepared.Sat xBar wTot
        let alphaR =
            TwoPhase.voidFraction prepared.Case.Loop.VoidModel xBar prepared.Sat (wTot / prepared.RiserArea)
        let rhoR = TwoPhase.mixtureDensity alphaR prepared.Sat
        let dpDrum = drumDp prepared wTot xBar
        { Net = prepared.GravDowncomer - dpDc - dpR - dpDrum - rhoR * g * prepared.HRiser
          DpDowncomer = dpDc
          DpRiser = dpR
          RhoRiser = rhoR
          AlphaRiser = alphaR
          XOutRiser = xBar }

    let private externalSliceFlow (prepared: PreparedSolve) (circulationRatio: float) (index: int) =
        max 1e-4 (circulationRatio * prepared.SteamLin.[index])

    let fieldDemand (prepared: PreparedSolve) (wTot: float) =
        let circulationRatio = wTot / prepared.SteamTot
        [| 0 .. prepared.SteamLin.Length - 1 |]
        |> Array.sumBy (fun i ->
            let wExt = externalSliceFlow prepared circulationRatio i
            let (_, _, dp, _, _, _) =
                CirculationHydraulics.splitSlice prepared.Case prepared.Sat prepared.Bands prepared.BandFrac
                    prepared.FieldAreaMean prepared.OpenAnnulus wExt prepared.SteamLin.[i]
            dp * wExt * prepared.DzArr.[i] / wTot)

    let balance (prepared: PreparedSolve) (wTot: float) =
        (availableHead prepared wTot).Net - fieldDemand prepared wTot

    let solveOperatingPoint (prepared: PreparedSolve) =
        let wLo = 1.5 * prepared.SteamTot
        let wHi = 200.0 * prepared.SteamTot
        let rootCount = countSignChanges (balance prepared) wLo wHi 40
        let (wTot, wStatus) = bisectWithStatus (balance prepared) wLo wHi 1e-3 90
        let h = max 1e-6 (0.01 * wTot)
        let balanceSlope =
            (balance prepared (wTot + h) - balance prepared (wTot - h)) / (2.0 * h)
        let available = availableHead prepared wTot
        { CircFlow = wTot
          RootCount = rootCount
          BracketOk = (wStatus <> NoSignChange)
          BalanceSlope = balanceSlope
          Available = available
          CirculationRatio = wTot / prepared.SteamTot }

    let solveSlice (prepared: PreparedSolve) (operatingPoint: OperatingPoint) (index: int) =
        let wExt = externalSliceFlow prepared operatingPoint.CirculationRatio index
        let (wField, wByp, _, xIn, bypassAlpha, xCarryUnder) =
            CirculationHydraulics.splitSlice prepared.Case prepared.Sat prepared.Bands prepared.BandFrac
                prepared.FieldAreaMean prepared.OpenAnnulus wExt prepared.SteamLin.[index]
        { WExt = wExt
          WField = wField
          WByp = wByp
          XIn = xIn
          BypassAlpha = bypassAlpha
          XCarryUnder = xCarryUnder
          DpFriction =
            CirculationHydraulics.dpFieldFriction prepared.Case prepared.Sat prepared.Bands prepared.BandFrac
                wField prepared.SteamLin.[index] xIn
          Starved = prepared.SteamLin.[index] > 1e-9 && wField / prepared.SteamLin.[index] < 1.0 }

    let private emptySliceTotals =
        { WFieldTot = 0.0
          WFieldSum = 0.0
          WBypTot = 0.0
          SteamSum = 0.0
          XInSum = 0.0
          BypassAlphaAcc = 0.0
          XCarryUnderAcc = 0.0
          FrictionAcc = 0.0
          StarvedSlices = 0 }

    let private accumulateSlice (prepared: PreparedSolve) (circFlow: float) (index: int)
                                (totals: SliceTotals) (slice: SliceResult) =
        let dz = prepared.DzArr.[index]
        let wgt = slice.WExt * dz / circFlow
        { WFieldTot = totals.WFieldTot + slice.WField * dz
          WFieldSum = totals.WFieldSum + slice.WField
          WBypTot = totals.WBypTot + slice.WByp * dz
          SteamSum = totals.SteamSum + prepared.SteamLin.[index]
          XInSum = totals.XInSum + slice.XIn
          BypassAlphaAcc = totals.BypassAlphaAcc + wgt * slice.BypassAlpha
          XCarryUnderAcc = totals.XCarryUnderAcc + wgt * slice.XCarryUnder
          FrictionAcc = totals.FrictionAcc + wgt * slice.DpFriction
          StarvedSlices = totals.StarvedSlices + if slice.Starved then 1 else 0 }

    let summarizeSlices (prepared: PreparedSolve) (operatingPoint: OperatingPoint) (slices: SliceResult[]) =
        slices
        |> Array.mapi (fun i slice -> (i, slice))
        |> Array.fold (fun totals (i, slice) -> accumulateSlice prepared operatingPoint.CircFlow i totals slice) emptySliceTotals

    let private buildArrays (selector: SliceResult -> float) (slices: SliceResult[]) =
        slices |> Array.map selector

    let assembleGlobal (prepared: PreparedSolve) (operatingPoint: OperatingPoint)
                       (slices: SliceResult[]) (totals: SliceTotals) : CirculationContracts.Distribution =
        let nz = prepared.SteamLin.Length
        let effCr = totals.WFieldTot / prepared.SteamTot
        let invNz = 1.0 / float nz
        let wFieldMean = totals.WFieldSum * invNz
        let steamMean = totals.SteamSum * invNz
        let xInMean = totals.XInSum * invNz
        let xField = min 0.95 (1.0 / effCr + xInMean)
        let alphaB =
            TwoPhase.voidFraction prepared.Case.Loop.VoidModel xField prepared.Sat (wFieldMean / prepared.FieldAreaMean)
        let rhoField =
            let (_, _, rho) =
                CirculationHydraulics.dpFieldColumnWith prepared.Case prepared.Sat prepared.Bands prepared.BandFrac
                    wFieldMean steamMean xInMean
            rho
        let dpDrumFinal = drumDp prepared operatingPoint.CircFlow operatingPoint.Available.XOutRiser
        { WExtLin = buildArrays (fun slice -> slice.WExt) slices
          WFieldLin = buildArrays (fun slice -> slice.WField) slices
          WBypLin = buildArrays (fun slice -> slice.WByp) slices
          XInField = buildArrays (fun slice -> slice.XIn) slices
          RootCount = operatingPoint.RootCount
          BracketOk = operatingPoint.BracketOk
          BalanceSlope = operatingPoint.BalanceSlope
          Global =
            { CirculationRatio = operatingPoint.CirculationRatio
              CircFlow = operatingPoint.CircFlow
              SteamFlow = prepared.SteamTot
              DrivingHead = prepared.GravDowncomer - operatingPoint.Available.RhoRiser * g * prepared.HRiser - rhoField * g * prepared.HField
              DpDowncomer = operatingPoint.Available.DpDowncomer
              DpBundle = totals.FrictionAcc
              DpRiser = operatingPoint.Available.DpRiser
              DpNozzles = dpDrumFinal
              DpTotal = operatingPoint.Available.DpDowncomer + operatingPoint.Available.DpRiser + totals.FrictionAcc + dpDrumFinal
              XOutBundle = xField
              XOutRiser = operatingPoint.Available.XOutRiser
              AlphaOutBundle = alphaB
              AlphaOutRiser = operatingPoint.Available.AlphaRiser
              VelDowncomer = operatingPoint.CircFlow / (prepared.Sat.RhoL * prepared.DowncomerArea)
              VelRiserMix = operatingPoint.CircFlow / (TwoPhase.homogeneousDensity operatingPoint.Available.XOutRiser prepared.Sat * prepared.RiserArea)
              HDowncomer = prepared.HDowncomer
              HShell = prepared.HField
              HRiser = prepared.HRiser
              BypassFraction = totals.WBypTot / max 1e-9 operatingPoint.CircFlow
              EffectiveCR = effCr
              BypassAlpha = totals.BypassAlphaAcc
              XCarryUnder = totals.XCarryUnderAcc
              OpenAnnulus = prepared.OpenAnnulus
              StarvedSlices = totals.StarvedSlices
              Converged = operatingPoint.Available.Net > 0.0 } }
