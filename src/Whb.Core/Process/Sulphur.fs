namespace Whb.Core

open System
open Constants

module Sulphur =

    let clausSpecies =
        [ GasProps.H2S; GasProps.SO2; GasProps.COS; GasProps.CS2
          GasProps.S2; GasProps.S6; GasProps.S8 ]

    let elementalSulphurSpecies =
        [ GasProps.S2; GasProps.S6; GasProps.S8 ]

    let private sulphurAtomMolarMass = GasProps.molarMass GasProps.S8 / 8.0

    let private isElementalSulphur =
        function
        | GasProps.S2 | GasProps.S6 | GasProps.S8 -> true
        | _ -> false

    let private elementalSulphurAtoms =
        function
        | GasProps.S2 -> 2.0
        | GasProps.S6 -> 6.0
        | GasProps.S8 -> 8.0
        | _ -> 0.0

    let lnKpS6 (tK: float) =
        -55.406869 + 34964.0289 / tK + 2.390900 * log tK

    let lnKpS8 (tK: float) =
        -86.455533 + 51071.8992 / tK + 4.237615 * log tK

    let dhReactionS6 (tK: float) = -284.5e3 + 19.1 * (tK - 298.15)

    let dhReactionS8 (tK: float) = -413.1e3 + 32.6 * (tK - 298.15)

    type Speciation =
        { NS2: float
          NS6: float
          NS8: float
          PS2: float
          PS6: float
          PS8: float
          NTotal: float
          YSulphur: float
          MeanAtomicity: float }

    let speciate (tK: float) (pPa: float) (nSAtoms: float) (nInert: float) : Speciation =
        let pBar = pPa / 1.0e5
        if nSAtoms <= 0.0 then
            { NS2 = 0.0; NS6 = 0.0; NS8 = 0.0
              PS2 = 0.0; PS6 = 0.0; PS8 = 0.0
              NTotal = nInert; YSulphur = 0.0; MeanAtomicity = 0.0 }
        else
            let kp6 = exp (lnKpS6 tK)
            let kp8 = exp (lnKpS8 tK)
            let atomsAt (p2: float) =
                let p6 = kp6 * Math.Pow(p2, 3.0)
                let p8 = kp8 * Math.Pow(p2, 4.0)
                let pS = p2 + p6 + p8
                if pS >= pBar then Double.PositiveInfinity
                else
                    let nTot = nInert / (1.0 - pS / pBar)
                    (2.0 * p2 + 6.0 * p6 + 8.0 * p8) / pBar * nTot
            let f p2 = atomsAt p2 - nSAtoms
            let hi = 0.999 * pBar
            let p2 = bisect f 1e-30 hi 1e-14 300
            let p6 = kp6 * Math.Pow(p2, 3.0)
            let p8 = kp8 * Math.Pow(p2, 4.0)
            let pS = p2 + p6 + p8
            let nTot = nInert / (max 1e-12 (1.0 - pS / pBar))
            let n2 = p2 / pBar * nTot
            let n6 = p6 / pBar * nTot
            let n8 = p8 / pBar * nTot
            let nMol = n2 + n6 + n8
            { NS2 = n2; NS6 = n6; NS8 = n8
              PS2 = p2 * 1.0e5; PS6 = p6 * 1.0e5; PS8 = p8 * 1.0e5
              NTotal = nTot
              YSulphur = (if nTot > 0.0 then nMol / nTot else 0.0)
              MeanAtomicity = (if nMol > 0.0 then (2.0 * n2 + 6.0 * n6 + 8.0 * n8) / nMol else 0.0) }

    let vapourEnthalpy (tK: float) (sp: Speciation) =
        sp.NS2 * GasProps.hMolarAbs GasProps.S2 tK
        + sp.NS6 * GasProps.hMolarAbs GasProps.S6 tK
        + sp.NS8 * GasProps.hMolarAbs GasProps.S8 tK

    let polymerisationDuty (tHotK: float) (tColdK: float) (pPa: float)
                           (nSAtoms: float) (nInert: float) =
        let hot = speciate tHotK pPa nSAtoms nInert
        let cold = speciate tColdK pPa nSAtoms nInert
        let hCold = vapourEnthalpy tColdK cold
        let hFrozen = vapourEnthalpy tColdK hot
        hFrozen - hCold

    let pSatS2 (tK: float) = exp (50.019937 - 16564.8090 / tK - 4.616361 * log tK) * 1.0e5

    let pSatS6 (tK: float) = exp (94.774521 - 14739.2800 / tK - 11.474858 * log tK) * 1.0e5

    let pSatS8 (tK: float) = exp (115.199396 - 15302.2546 / tK - 14.443866 * log tK) * 1.0e5

    let pSatTotal (tK: float) = pSatS2 tK + pSatS6 tK + pSatS8 tK

    let dewPoint (pSulphurPa: float) =
        let lo = cToK 120.0
        let hi = cToK 350.0
        if pSulphurPa <= pSatTotal lo then lo
        elif pSulphurPa >= pSatTotal hi then hi
        else bisect (fun t -> pSatTotal t - pSulphurPa) lo hi 1e-6 200

    let hasElementalSulphur (composition: GasProps.Composition) =
        composition
        |> GasProps.normalize
        |> List.exists (fun (sp, y) -> isElementalSulphur sp && y > 1e-10)

    type ClausScreening =
        { PresentSpecies: string list
          HasClausSpecies: bool
          HasElementalSulphurVapour: bool
          YH2S: float
          YH2O: float
          YElementalSulphur: float
          WaterDewPoint: float option
          SulphurDewPoint: float option }

    type ProcessState =
        { T: float
          VapourComposition: GasProps.Composition
          TotalSpecificEnthalpy: float
          CpApprox: float
          PSulphur: float
          YElementalSulphurVapour: float
          SulphurDewPoint: float option
          Condensing: bool
          CondensedAtoms: float
          CondensedFraction: float }

    type CouplingSummary =
        { InletElementalSulphurVapour: float
          InletSulphurDewPoint: float option
          CondensingCells: int
          FirstCondensationZ: float
          OutletCondensedFraction: float
          OutletElementalSulphurVapour: float }

    let clausScreening (pPa: float) (composition: GasProps.Composition) : ClausScreening =
        let comp = GasProps.normalize composition
        let yOf sp = GasProps.molFrac comp sp
        let present =
            clausSpecies
            |> List.choose (fun sp ->
                if yOf sp > 1e-8 then Some(GasProps.speciesName sp) else None)
        let yH2S = yOf GasProps.H2S
        let yH2O = yOf GasProps.H2O
        let yElementalSulphur = elementalSulphurSpecies |> List.sumBy yOf
        { PresentSpecies = present
          HasClausSpecies = not present.IsEmpty
          HasElementalSulphurVapour = yElementalSulphur > 1e-8
          YH2S = yH2S
          YH2O = yH2O
          YElementalSulphur = yElementalSulphur
          WaterDewPoint =
            (if yH2O > 1e-12 then Some((Steam.sat (yH2O * pPa)).Tsat) else None)
          SulphurDewPoint =
            (if yElementalSulphur > 1e-12 then Some(dewPoint (yElementalSulphur * pPa)) else None) }

    type CondenserState =
        { Vapour: Speciation
          PSulphur: float
          NCondensed: float
          NVapour: float
          CondensedFraction: float
          Condensing: bool }

    let condenserState (tK: float) (pPa: float) (nSAtoms: float) (nInert: float) : CondenserState =
        let dry = speciate tK pPa nSAtoms nInert
        let pDry = dry.PS2 + dry.PS6 + dry.PS8
        let pSat = pSatTotal tK
        if pDry <= pSat then
            { Vapour = dry; PSulphur = pDry; NCondensed = 0.0; NVapour = nSAtoms
              CondensedFraction = 0.0; Condensing = false }
        else
            let ySat = pSat / pPa
            let nGasTot = nInert / max 1e-12 (1.0 - ySat)
            let nMolSat = ySat * nGasTot
            let atomicity =
                let kp6 = exp (lnKpS6 tK)
                let kp8 = exp (lnKpS8 tK)
                let pSatBar = pSat / 1.0e5
                let f p2 = p2 + kp6 * Math.Pow(p2, 3.0) + kp8 * Math.Pow(p2, 4.0) - pSatBar
                let p2 = bisect f 1e-30 pSatBar 1e-16 300
                let p6 = kp6 * Math.Pow(p2, 3.0)
                let p8 = kp8 * Math.Pow(p2, 4.0)
                let tot = p2 + p6 + p8
                if tot <= 0.0 then 8.0 else (2.0 * p2 + 6.0 * p6 + 8.0 * p8) / tot
            let nVap = min nSAtoms (nMolSat * atomicity)
            let nCond = max 0.0 (nSAtoms - nVap)
            let sat = speciate tK pPa nVap nInert
            { Vapour = sat
              PSulphur = pSat
              NCondensed = nCond
              NVapour = nVap
              CondensedFraction = (if nSAtoms > 0.0 then nCond / nSAtoms else 0.0)
              Condensing = true }

    let supersaturation (pSulphurPa: float) (tGasK: float) =
        pSulphurPa / max 1e-12 (pSatTotal tGasK)

    let TMelt = cToK 115.21

    let TLambda = cToK 159.0

    let rhoLiquid (tK: float) = 1900.0 - 0.80 * kToC tK

    let cpLiquid (_tK: float) = 1000.0

    let kLiquid (_tK: float) = 0.15

    let muLiquid (tK: float) =
        let tC = kToC tK
        let anchors =
            [ 115.0, 12.0e-3; 120.0, 11.0e-3; 140.0, 8.0e-3; 155.0, 7.0e-3
              159.0, 12.0e-3; 161.0, 0.4; 165.0, 4.0; 175.0, 40.0
              187.0, 93.0; 200.0, 75.0; 250.0, 20.0; 300.0, 5.0 ]
        let rec walk =
            function
            | (t1, m1) :: ((t2, m2) :: _ as rest) ->
                if tC <= t1 then m1
                elif tC <= t2 then
                    let f = (tC - t1) / (t2 - t1)
                    exp (log m1 + f * (log m2 - log m1))
                else walk rest
            | [ (_, m) ] -> m
            | [] -> 1.0
        walk anchors

    let sulphurMolarMass (sp: Speciation) =
        let n = sp.NS2 + sp.NS6 + sp.NS8
        if n <= 0.0 then 0.2565
        else
            (sp.NS2 * GasProps.molarMass GasProps.S2
             + sp.NS6 * GasProps.molarMass GasProps.S6
             + sp.NS8 * GasProps.molarMass GasProps.S8) / n

    let latentHeatPerAtom (tK: float) = 10.4e3 - 8.0 * (kToC tK - 130.0)

    let private vapourCompositionFromCounts (counts: (GasProps.Species * float) list) =
        let nz = counts |> List.filter (fun (_, n) -> n > 1e-12)
        let s = nz |> List.sumBy snd
        if s <= 0.0 then []
        else nz |> List.map (fun (sp, n) -> sp, n / s)

    let private liquidEnthalpyPerAtom (tK: float) (vap: Speciation) =
        let vapAtoms = 2.0 * vap.NS2 + 6.0 * vap.NS6 + 8.0 * vap.NS8
        let hVapPerAtom =
            if vapAtoms > 1e-12 then vapourEnthalpy tK vap / vapAtoms
            else GasProps.hMolarAbs GasProps.S8 tK / 8.0
        hVapPerAtom - latentHeatPerAtom tK

    let processStateAt (shiftMode: Shift.Mode) (real: bool) (pPa: float)
                       (composition: GasProps.Composition) (tK: float) : ProcessState =
        let comp0 = GasProps.normalize composition
        let carrierCounts =
            comp0 |> List.filter (fun (sp, _) -> not (isElementalSulphur sp))
        let carrierMoles = carrierCounts |> List.sumBy snd
        let carrierComp =
            if carrierMoles > 1e-12 then
                carrierCounts |> List.map (fun (sp, n) -> sp, n / carrierMoles)
            else []
        let totalSAtoms =
            comp0 |> List.sumBy (fun (sp, y) -> y * elementalSulphurAtoms sp)
        let cond =
            if totalSAtoms > 1e-12 && carrierMoles > 1e-12 then
                condenserState tK pPa totalSAtoms carrierMoles
            elif totalSAtoms > 1e-12 then
                { Vapour = speciate tK pPa totalSAtoms 0.0
                  PSulphur = totalSAtoms * 1.0e5
                  NCondensed = 0.0
                  NVapour = totalSAtoms
                  CondensedFraction = 0.0
                  Condensing = false }
            else
                { Vapour = speciate tK pPa 0.0 carrierMoles
                  PSulphur = 0.0
                  NCondensed = 0.0
                  NVapour = 0.0
                  CondensedFraction = 0.0
                  Condensing = false }
        let carrierEq =
            if carrierMoles > 1e-12 then Shift.equilibrate shiftMode carrierComp tK
            else []
        let vapourCounts =
            [ yield! carrierEq |> List.map (fun (sp, y) -> sp, y * carrierMoles)
              if cond.Vapour.NS2 > 0.0 then yield GasProps.S2, cond.Vapour.NS2
              if cond.Vapour.NS6 > 0.0 then yield GasProps.S6, cond.Vapour.NS6
              if cond.Vapour.NS8 > 0.0 then yield GasProps.S8, cond.Vapour.NS8 ]
        let vapourComp = vapourCompositionFromCounts vapourCounts
        let vapourMass =
            vapourCounts |> List.sumBy (fun (sp, n) -> n * GasProps.molarMass sp)
        let totalMass =
            comp0 |> List.sumBy (fun (sp, y) -> y * GasProps.molarMass sp)
        let vapourH =
            if vapourMass > 1e-12 && not vapourComp.IsEmpty then
                GasProps.enthalpyAbsReal real vapourComp tK pPa * vapourMass
            else 0.0
        let liqMass = cond.NCondensed * sulphurAtomMolarMass
        let liqH =
            if cond.NCondensed > 1e-12 then
                cond.NCondensed * liquidEnthalpyPerAtom tK cond.Vapour
            else 0.0
        let vapourCp =
            if vapourMass > 1e-12 && not vapourComp.IsEmpty then
                let props = GasProps.mixReal GasProps.Wilke real vapourComp tK pPa 1.0
                props.Cp * vapourMass
            else 0.0
        let liquidCp = liqMass * cpLiquid tK
        let ySulphurVap =
            vapourComp
            |> List.sumBy (fun (sp, y) ->
                if isElementalSulphur sp then y else 0.0)
        { T = tK
          VapourComposition = (if vapourComp.IsEmpty then comp0 else vapourComp)
          TotalSpecificEnthalpy = (vapourH + liqH) / max 1e-12 totalMass
          CpApprox = (vapourCp + liquidCp) / max 1e-12 totalMass
          PSulphur = cond.PSulphur
          YElementalSulphurVapour = ySulphurVap
          SulphurDewPoint = (if totalSAtoms > 1e-12 then Some(dewPoint (max 0.0 cond.PSulphur)) else None)
          Condensing = cond.Condensing
          CondensedAtoms = cond.NCondensed
          CondensedFraction = cond.CondensedFraction }

    let processEnthalpyAt (shiftMode: Shift.Mode) (real: bool) (pPa: float)
                          (composition: GasProps.Composition) (tK: float) =
        (processStateAt shiftMode real pPa composition tK).TotalSpecificEnthalpy

    let processStateFromEnthalpyAt (shiftMode: Shift.Mode) (real: bool) (pPa: float)
                                   (composition: GasProps.Composition) (h: float) : ProcessState =
        if not (hasElementalSulphur composition) then
            let (t, comp) = Shift.stateFromEnthalpyAt shiftMode real pPa composition h
            let ySulphur =
                comp
                |> List.sumBy (fun (sp, y) -> if isElementalSulphur sp then y else 0.0)
            { T = t
              VapourComposition = comp
              TotalSpecificEnthalpy = h
              CpApprox = (GasProps.mixReal GasProps.Wilke real comp t pPa 1.0).Cp
              PSulphur = ySulphur * pPa
              YElementalSulphurVapour = ySulphur
              SulphurDewPoint = (if ySulphur > 1e-12 then Some(dewPoint (ySulphur * pPa)) else None)
              Condensing = false
              CondensedAtoms = 0.0
              CondensedFraction = 0.0 }
        else
            let guess = fst (Shift.stateFromEnthalpyAt shiftMode real pPa composition h)
            let residualAt t =
                let st = processStateAt shiftMode real pPa composition t
                struct (st.TotalSpecificEnthalpy - h, max 1e-3 st.CpApprox)
            let t = newtonIncreasing residualAt (cToK 120.0) 2500.0 guess 1e-9 80
            processStateAt shiftMode real pPa composition t

    let kGasFromHtc (hGas: float) (cpMolarGas: float) (lewis: float) (pPa: float) =
        hGas / (max 1.0 cpMolarGas * Math.Pow(max 0.1 lewis, 2.0 / 3.0) * max 1.0 pPa)

    let hFilmHorizontal (d: float) (dTFilm: float) (tFilmK: float) (latentPerKg: float) =
        if dTFilm <= 0.0 || d <= 0.0 then 0.0
        else
            let rho = rhoLiquid tFilmK
            let mu = muLiquid tFilmK
            let k = kLiquid tFilmK
            0.729 * Math.Pow(rho * rho * g * latentPerKg * k * k * k / (mu * d * dTFilm), 0.25)

    type CondensationResult =
        { TInterface: float
          PInterface: float
          MolarFlux: float
          QLatent: float
          QSensible: float
          QTotal: float
          LatentFraction: float
          DiffusionControlled: bool }

    let condenseColburnHougen
        (tGasK: float) (pSulphurPa: float) (pTotalPa: float)
        (hGas: float) (kG: float) (hToCoolant: float) (tCoolantK: float) =
        let latentAtom = latentHeatPerAtom tGasK
        let f (tI: float) =
            let pI = min (0.999 * pSulphurPa + 1e-9) (pSatTotal tI)
            let dp = pSulphurPa - pI
            let pnB = pTotalPa - pSulphurPa
            let pnI = pTotalPa - pI
            let pbm =
                if abs (pnI - pnB) < 1e-9 then max 1.0 pnB
                else (pnI - pnB) / log (max 1e-9 (pnI / pnB))
            let flux = kG * pTotalPa / max 1.0 pbm * max 0.0 dp
            let qGas = hGas * (tGasK - tI)
            let qLat = flux * latentAtom
            (qGas + qLat) - hToCoolant * (tI - tCoolantK)
        let tI = bisect f (tCoolantK + 1e-4) (tGasK - 1e-4) 1e-4 200
        let pI = min pSulphurPa (pSatTotal tI)
        let dp = max 0.0 (pSulphurPa - pI)
        let pnB = pTotalPa - pSulphurPa
        let pnI = pTotalPa - pI
        let pbm =
            if abs (pnI - pnB) < 1e-9 then max 1.0 pnB
            else (pnI - pnB) / log (max 1e-9 (pnI / pnB))
        let flux = kG * pTotalPa / max 1.0 pbm * dp
        let qLat = flux * latentAtom
        let qGas = hGas * (tGasK - tI)
        let qTot = qLat + qGas
        { TInterface = tI
          PInterface = pI
          MolarFlux = flux
          QLatent = qLat
          QSensible = qGas
          QTotal = qTot
          LatentFraction = (if qTot > 0.0 then qLat / qTot else 0.0)
          DiffusionControlled = (qTot > 0.0 && qLat / qTot > 0.5) }

    let silverBellGhaly (hCondensing: float) (hGas: float) (zRatio: float) =
        let inv = 1.0 / max 1.0 hCondensing + max 0.0 zRatio / max 1.0 hGas
        1.0 / inv

    type FogAssessment =
        { Supersaturation: float
          SlopeRatio: float
          Lewis: float
          FogLikely: bool
          Margin: float }

    let assessFog (tGasK: float) (pSulphurPa: float) (lewis: float)
                  (dTGas: float) (dPSulphur: float) =
        let ss = supersaturation pSulphurPa tGasK
        let dTdew =
            if abs dPSulphur < 1e-9 then 0.0
            else dewPoint (pSulphurPa + dPSulphur) - dewPoint pSulphurPa
        let slopeRatio =
            if abs dTdew < 1e-9 then (if abs dTGas > 1e-9 then 10.0 else 0.0)
            else abs dTGas / abs dTdew
        let fog = ss > 1.05 && slopeRatio > 1.0 && lewis > 1.0
        { Supersaturation = ss
          SlopeRatio = slopeRatio
          Lewis = lewis
          FogLikely = fog
          Margin = (if fog then 0.0 else max 0.0 (1.0 - ss)) }

    type Severity =
        | Ok
        | Watch
        | Alarm

    type Check =
        { Severity: Severity
          Title: string
          Value: string
          Limit: string
          Detail: string }

    let private chk s t v l d = { Severity = s; Title = t; Value = v; Limit = l; Detail = d }

    let checkWallWindow (tWallK: float) =
        let tC = kToC tWallK
        if tC < 125.0 then
            chk Alarm "Parete sotto la finestra dello zolfo"
                (sprintf "T parete = %.1f C" tC) "125-155 C (fusione a 115.2 C)"
                "Sotto i 125 C lo zolfo solidifica sulla parete: incrostazione, perdita di scambio e ostruzione dei drenaggi. Alzare la pressione del vapore LP."
        elif tC > kToC TLambda then
            chk Alarm "Parete oltre la transizione lambda"
                (sprintf "T parete = %.1f C" tC) (sprintf "< %.0f C" (kToC TLambda))
                "Sopra 159 C lo zolfo liquido polimerizza e la viscosita' sale di ordini di grandezza: il condensato non drena piu' e il fascio si intasa. Ridurre la pressione del vapore LP."
        elif tC > 155.0 then
            chk Watch "Parete vicina alla transizione lambda"
                (sprintf "T parete = %.1f C" tC) "155 C con riserva di 4 K su 159 C"
                "Il margine sulla transizione lambda e' sotto i 4 K: verificare la banda di regolazione del vapore LP e i transitori di carico."
        else
            chk Ok "Finestra di parete rispettata"
                (sprintf "T parete = %.1f C" tC) "125-155 C" ""

    let steamPressureForWall (tWallK: float) = Steam.psat_MPa tWallK * 1.0e6

    let checkSulphidation (tWallK: float) (yH2S: float) =
        let tC = kToC tWallK
        if yH2S <= 1e-4 then chk Ok "H2S trascurabile" (sprintf "y(H2S) = %.2e" yH2S) "-" ""
        elif tC > 340.0 then
            chk Alarm "Sulfidation: parete oltre il limite pratico"
                (sprintf "T parete = %.0f C, y(H2S) = %.3f" tC yH2S) "< 340 C per acciaio al carbonio"
                "Il tasso di sulfidation cresce in modo esponenziale: verificare integrita' ferrule e valutare 1.25Cr-0.5Mo o rivestimento."
        elif tC > 260.0 then
            chk Watch "Sulfidation: parete in campo attivo"
                (sprintf "T parete = %.0f C, y(H2S) = %.3f" tC yH2S) "260-340 C campo di attacco"
                "Sopra 260 C l'attacco e' misurabile. Con ferrule integre la parete resta vicina a Tsat; ogni bypass locale accelera il fenomeno."
        else chk Ok "Sulfidation entro i limiti" (sprintf "T parete = %.0f C" tC) "< 260 C" ""

    let checkWetH2S (tMetalK: float) (tWaterDewK: float) (yH2S: float) =
        if yH2S > 1e-4 && tMetalK <= tWaterDewK then
            chk Alarm "Rischio wet H2S (HIC/SOHIC/SSC)"
                (sprintf "T metallo = %.0f C <= dew point acqua %.0f C" (kToC tMetalK) (kToC tWaterDewK))
                "T metallo > dew point acqua in presenza di H2S"
                "Condensa acida in presenza di H2S: richiede acciaio HIC-resistant e verifica delle condizioni di fermata."
        else chk Ok "Nessuna condensa acida con H2S" "-" "-" ""

    let condenserChecks (tWallK: float) (tGasK: float) (pSulphurPa: float)
                        (fog: FogAssessment) =
        [ checkWallWindow tWallK
          (let tdew = dewPoint pSulphurPa
           if tGasK <= tdew then
               chk Ok "Condensazione attiva"
                   (sprintf "T gas = %.0f C, dew point = %.0f C" (kToC tGasK) (kToC tdew))
                   "T gas < dew point" ""
           else
               chk Watch "Gas sopra il dew point dello zolfo"
                   (sprintf "T gas = %.0f C, dew point = %.0f C" (kToC tGasK) (kToC tdew))
                   "T gas < dew point per condensare"
                   "Nessuna condensazione a questo punto: il tratto lavora in solo raffreddamento sensibile.")
          (if fog.FogLikely then
               chk Alarm "Rischio di nebbia (fog) di zolfo"
                   (sprintf "supersaturazione = %.2f, rapporto pendenze = %.2f" fog.Supersaturation fog.SlopeRatio)
                   "supersaturazione < 1 oppure raffreddamento piu' lento della curva di rugiada"
                   "Il gas si raffredda piu' in fretta di quanto scenda il suo dew point: lo zolfo nuclea in seno al gas invece che sulla parete."
           else
               chk Ok "Nessun rischio di nebbia"
                   (sprintf "supersaturazione = %.2f" fog.Supersaturation) "< 1" "") ]
