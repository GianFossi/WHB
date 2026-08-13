namespace Whb.Core

open System
open Constants
open Types

/// <summary>
/// Provides mechanics functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Mechanics =

    /// <summary>
    /// Calculates or returns axialexpansion for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let axialExpansion (mat: Materials.Material) (tRoomK: float) (segments: (float * float) list) =
        let tRoom = kToC tRoomK
        let l = segments |> List.sumBy fst
        let dL =
            segments
            |> List.sumBy (fun (dz, tK) ->
                let t = kToC tK
                mat.Alpha t * (t - tRoom) * dz)
        let f (t: float) = mat.Alpha t * (t - tRoom) * l - dL
        let tEq = bisect f (tRoom - 50.0) 1200.0 1e-6 200
        { Label = ""
          TEquivalent = cToK tEq
          AlphaMean = mat.Alpha tEq
          Length = l
          DeltaL = dL }

    /// <summary>
    /// Calculates or returns shellmetaltemperature for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let shellMetalTemperature (sat: Steam.SatProps) (tAmbK: float) (uAmb: float)
                              (thk: float) (kShell: float) (hBoil: float) =
        let rTot = 1.0 / max 1.0 hBoil + thk / kShell + 1.0 / max 1e-3 uAmb
        let q = (sat.Tsat - tAmbK) / rTot
        let tIn = sat.Tsat - q / max 1.0 hBoil
        let tOut = tIn - q * thk / kShell
        (0.5 * (tIn + tOut), q)

    /// <summary>
    /// Calculates or returns fixedtubesheet for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let fixedTubesheet
        (tubeMat: Materials.Material) (shellMat: Materials.Material)
        (tRoomK: float) (l: float) (nTubes: int) (dOut: float) (dIn: float)
        (shellId: float) (shellThk: float) (span: float)
        (tTubeEqK: float) (tTubeHotK: float) (tShellEqK: float) : FixedTubesheetResult =

        let tRoom = kToC tRoomK
        let tT = kToC tTubeEqK
        let tS = kToC tShellEqK
        let aT = float nTubes * Math.PI / 4.0 * (dOut * dOut - dIn * dIn)
        let aS = Math.PI * (shellId + shellThk) * shellThk
        let eT = tubeMat.E tT
        let eS = shellMat.E tS
        let dFree = l * (tubeMat.Alpha tT * (tT - tRoom) - shellMat.Alpha tS * (tS - tRoom))
        let f = dFree / (l / (aT * eT) + l / (aS * eS))
        let rGyr = sqrt (dOut * dOut + dIn * dIn) / 4.0
        let slend = 0.5 * span / rGyr
        let sy = tubeMat.Sy tT
        let cc = sqrt (2.0 * Math.PI * Math.PI * eT / sy)
        let sAllow =
            if slend < cc then
                let r = slend / cc
                let fs = 5.0 / 3.0 + 3.0 / 8.0 * r - r * r * r / 8.0
                sy * (1.0 - r * r / 2.0) / fs
            else
                Math.PI * Math.PI * eT / (slend * slend) / (23.0 / 12.0)
        let sigT = f / aT
        { TTubeMeanEq = tTubeEqK
          TTubeHotEq = tTubeHotK
          TShellEq = tShellEqK
          AlphaTube = tubeMat.Alpha tT
          AlphaShell = shellMat.Alpha tS
          AreaTube = aT
          AreaShell = aS
          ETube = eT
          EShell = eS
          DeltaFree = dFree
          Force = f
          SigmaTube = sigT
          SigmaShell = f / aS
          ForcePerTube = f / float nTubes
          UnsupportedSpan = span
          RadiusGyration = rGyr
          Slenderness = slend
          SigmaBucklingAllow = sAllow
          BucklingUtilisation = (if sigT > 0.0 then sigT / sAllow else 0.0)
          ShellMaterial = shellMat.Name }

    /// <summary>
    /// Calculates or returns nu for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let nu = 0.3

    /// <summary>
    /// Calculates or returns lame for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let lame (pi_: float) (pe: float) (ri: float) (ro: float) (r: float) =
        let d = ro * ro - ri * ri
        let a = (pi_ * ri * ri - pe * ro * ro) / d
        let b = (pi_ - pe) * ri * ri * ro * ro / d
        let sr = a - b / (r * r)
        let st = a + b / (r * r)
        (sr, st)

    /// <summary>
    /// Calculates or returns thermalgradient for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let thermalGradient (alpha: float) (e: float) (dT: float)
                        (ri: float) (ro: float) (r: float) =
        if abs dT < 1e-9 then (0.0, 0.0, 0.0)
        else
            let lba = log (ro / ri)
            let m = alpha * e * dT / (2.0 * (1.0 - nu) * lba)
            let k = ri * ri / (ro * ro - ri * ri)
            let lbr = log (ro / r)
            let sr = m * (-lbr - k * (1.0 - ro * ro / (r * r)) * lba)
            let st = m * (1.0 - lbr - k * (1.0 + ro * ro / (r * r)) * lba)
            let sz = m * (1.0 - 2.0 * lbr - 2.0 * k * lba)
            (sr, st, sz)

    /// <summary>
    /// Calculates or returns vonmises for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let vonMises (s1: float) (s2: float) (s3: float) =
        sqrt (0.5 * ((s1 - s2) ** 2.0 + (s2 - s3) ** 2.0 + (s3 - s1) ** 2.0))

    /// <summary>
    /// Calculates or returns tresca for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let tresca (s1: float) (s2: float) (s3: float) =
        let mx = max s1 (max s2 s3)
        let mn = min s1 (min s2 s3)
        mx - mn

    /// <summary>
    /// Calculates or returns stresspoints for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let stressPoints (pi_: float) (pe: float) (ri: float) (ro: float)
                     (sigmaZmem: float) (alpha: float) (e: float) (dT: float) =
        [ "interna", ri; "media", 0.5 * (ri + ro); "esterna", ro ]
        |> List.map (fun (nm, r) ->
            let (srP, stP) = lame pi_ pe ri ro r
            let (srT, stT, szT) = thermalGradient alpha e dT ri ro r
            let sr = srP + srT
            let st = stP + stT
            let sz = sigmaZmem + szT
            { Position = nm
              R = r
              SigmaR = sr
              SigmaTheta = st
              SigmaZ = sz
              SigmaVM = vonMises sr st sz
              SigmaTresca = tresca sr st sz })

    /// <summary>
    /// Calculates or returns restrainedsystem for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let restrainedSystem (tRoomK: float) (l: float) (pEnd: float)
                         (members: (string * Materials.Material * float * float * float * float) list) =
        let ks =
            members
            |> List.map (fun (_, m, _, a, tEq, _) -> a * m.E (kToC tEq) / l)
        let kTot = List.sum ks
        let sumKd =
            List.zip ks members |> List.sumBy (fun (k, (_, _, _, _, _, d)) -> k * d)
        let delta = (pEnd + sumKd) / kTot
        let res =
            List.zip ks members
            |> List.map (fun (k, (lab, m, n, a, tEq, d)) ->
                let f = k * (delta - d)
                let fPress = k / kTot * pEnd
                { Label = lab
                  MaterialName = m.Name
                  Count = n
                  Area = a
                  E = m.E (kToC tEq)
                  TEq = tEq
                  FreeElongation = d
                  Force = f
                  SigmaZ = f / a
                  SigmaZPressure = fPress / a
                  SigmaZThermal = (f - fPress) / a })
        (delta, res)

    /// <summary>
    /// Calculates or returns bucklingcheck for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let bucklingCheck (label: string) (mat: Materials.Material) (tEqK: float)
                      (dOut: float) (dIn: float) (span: float)
                      (sigmaZ: float) (pExtNet: float) : BucklingCheck =
        let t = kToC tEqK
        let e = mat.E t
        let sy = mat.Sy t
        let rGyr = sqrt (dOut * dOut + dIn * dIn) / 4.0
        let slend = 0.5 * span / rGyr
        let cc = sqrt (2.0 * Math.PI * Math.PI * e / sy)
        let sAllow =
            if slend < cc then
                let r = slend / cc
                let fs = 5.0 / 3.0 + 3.0 / 8.0 * r - r * r * r / 8.0
                sy * (1.0 - r * r / 2.0) / fs
            else Math.PI * Math.PI * e / (slend * slend) / (23.0 / 12.0)
        let thk = 0.5 * (dOut - dIn)
        let dm = 0.5 * (dOut + dIn)
        let pLong = 2.0 * e / (1.0 - nu * nu) * (thk / dm) ** 3.0
        let tOverD = thk / dOut
        let denom = (1.0 - nu * nu) ** 0.75 * (span / dOut - 0.45 * sqrt tOverD)
        let pShort =
            if denom > 1e-6 then 2.42 * e * tOverD ** 2.5 / denom else infinity
        let pElastic = max pLong (min pShort 1e12)
        let pYield = 2.0 * sy * thk / dOut
        let pColl = 1.0 / sqrt (1.0 / (pElastic * pElastic) + 1.0 / (pYield * pYield))
        let sc = max 0.0 (-sigmaZ)
        { Label = label
          MaterialName = mat.Name
          SigmaCompression = sc
          Span = span
          RadiusGyration = rGyr
          Slenderness = slend
          E = e
          Sy = sy
          SigmaAllow = sAllow
          Utilisation = sc / sAllow
          PExtNet = pExtNet
          PCrElastic = pElastic
          PCrYield = pYield
          PCollapse = pColl
          CollapseUtil = max 0.0 pExtNet / pColl
          Note =
            (if pElastic < pYield then "collasso governato dall'instabilita' ELASTICA (tubo sottile)"
             else "collasso governato dallo SNERVAMENTO circonferenziale (tubo tozzo)")
            + (if pShort < pLong * 0.999 then ""
               elif pShort < 1e11 then sprintf "; irrigidito dai diaframmi ogni %.2f m (senza, il collasso scenderebbe a %.0f bar)" span (pLong / 1e5)
               else "") }

    /// <summary>
    /// Calculates or returns regimename for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let regimeName =
        function
        | Bubbly -> "a bolle (bubbly)"
        | DispersedBubble -> "a bolle disperse"
        | Slug -> "A TAPPI (slug) - da evitare"
        | Churn -> "agitato (churn)"
        | Annular -> "anulare"

    /// <summary>
    /// Calculates or returns dminforslug for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dMinForSlug (sat: Steam.SatProps) =
        19.0 * sqrt ((sat.RhoL - sat.RhoV) * sat.Sigma / (sat.RhoL * sat.RhoL * g))

    /// <summary>
    /// Calculates or returns flowregime for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let flowRegime (sat: Steam.SatProps) (d: float) (jl: float) (jv: float) =
        let j = jl + jv
        let c1 = Math.Pow(g * sat.Sigma * (sat.RhoL - sat.RhoV) / (sat.RhoL * sat.RhoL), 0.25)
        let c2 = Math.Pow(sat.Sigma * g * (sat.RhoL - sat.RhoV) / (sat.RhoV * sat.RhoV), 0.25)
        let nuL = sat.MuL / sat.RhoL
        let jDisp =
            4.0 * (Math.Pow(d, 0.429) * Math.Pow(sat.Sigma / sat.RhoL, 0.089) / Math.Pow(nuL, 0.072))
                * Math.Pow(g * (sat.RhoL - sat.RhoV) / sat.RhoL, 0.446)
        let alphaH = if j > 0.0 then jv / j else 0.0
        if d < dMinForSlug sat then Bubbly
        elif jl >= 3.0 * jv - 1.15 * c1 then Bubbly
        elif j >= jDisp && alphaH < 0.52 then DispersedBubble
        elif jv >= 3.1 * c2 then Annular
        else Slug

    /// <summary>
    /// Calculates or returns checkrisers for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let checkRisers (sat: Steam.SatProps) (lines: (Piping.Line * float) list) (x: float)
                    (rhoV2Max: float) (voidModel: TwoPhase.VoidModel) =
        lines
        |> List.map (fun (b, wLine) ->
            let a = Math.PI * b.Id * b.Id / 4.0
            let gm = wLine / a
            let jl = gm * (1.0 - x) / sat.RhoL
            let jv = gm * x / sat.RhoV
            let alpha = TwoPhase.voidFraction voidModel x sat gm
            let rhoH = TwoPhase.homogeneousDensity x sat
            let vMix = gm / rhoH
            let reg = flowRegime sat b.Id jl jv
            let rv2 = rhoH * vMix * vMix
            let ok = (reg <> Slug) && rv2 <= rhoV2Max
            { Label = sprintf "%s %s" b.Tag b.Nps
              Id = b.Id
              Count = b.Count
              VelSuperficialLiq = jl
              VelSuperficialVap = jv
              VelMix = vMix
              Alpha = alpha
              Regime = reg
              DMinBubbly = dMinForSlug sat
              RhoV2 = rv2
              Ok = ok
              Note =
                match reg with
                | Slug ->
                    "moto a tappi: pulsazioni di portata e di pressione, forzanti a bassa frequenza sui supporti. Aumentare la velocita' (diametro minore) per portarsi in churn/anulare, oppure ridurre il titolo (piu' circolazione)."
                | Annular | Churn ->
                    "regime churn/anulare: flusso continuo, nessuna pulsazione da tappi. Verificare comunque erosione ai gomiti."
                | Bubbly | DispersedBubble ->
                    "regime a bolle: il piu' quieto, tipico di titoli bassi. Nessun rischio di slug." })

    /// <summary>
    /// Calculates or returns minsubmergence for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let minSubmergence (d: float) (v: float) =
        let fr = v / sqrt (g * d)
        d * (0.5 + 2.3 * fr)
