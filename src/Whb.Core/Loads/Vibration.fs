namespace Whb.Core

open System
open Constants

/// <summary>
/// Provides tube-bundle vibration screening calculations using empirical fluid-elastic
/// stability, vortex-shedding, turbulent-buffeting and damping checks.
/// </summary>
/// <remarks>
/// Applies empirical and theoretical vibration checks for tube bundles: fluid-elastic
/// instability, vortex shedding, turbulent buffeting, added mass, damping and natural
/// frequency. Acoustic resonance is deliberately NOT checked: it concerns compressible gas
/// on the shell side, whereas a fire-tube WHB has boiling water there. Use as screening
/// analysis and validate against project criteria, vendor data, and applicable
/// heat-exchanger standards.
/// </remarks>
module Vibration =
    type Layout =
        | Triangular30
        | RotatedTriangular60
        | Square90
        | RotatedSquare45
    type JointType =
        | CreviceFreeWeld
        | FullPenetrationWeld
    let jointName =
        function
        | CreviceFreeWeld -> "saldatura crevice-free -> APPOGGIO alla piastra"
        | FullPenetrationWeld -> "saldatura a piena penetrazione -> INCASTRO alla piastra"
    let lambda2Of (clampedEnds: int) =
        match clampedEnds with
        | 0 -> 9.87
        | 1 -> 15.42
        | _ -> 22.37
    let layoutName =
        function
        | Triangular30 -> "30° TRIANGOLARE (vertice nel flusso)"
        | RotatedTriangular60 -> "60° TRIANGOLARE RUOTATO (lato lungo trasversale al flusso)"
        | Square90 -> "90° QUADRATO"
        | RotatedSquare45 -> "45° QUADRATO RUOTATO"
    let connorsK (lay: Layout) (massDamping: float) =
        match lay with
        | Triangular30 | Square90 -> 4.0
        | RotatedTriangular60 | RotatedSquare45 ->
            if massDamping <= 0.54 then 1.1 else 1.5
    type Result =
        { Band: int
          Y: float
          Span: float
          FreqNat: float
          MassLin: float
          MassAdded: float
          Cm: float
          VGap: float
          Rho: float
          MassDamping: float
          VCrit: float
          FeiRatio: float
          FreqVortex: float
          VortexRatio: float
          FreqBuffet: float
          BuffetRatio: float
          KConnors: float
          Delta: float
          /// Void fraction of the mixture washing this band.
          Alpha: float
          /// Damping the band would have if the void-dependent two-phase shape were used
          /// instead of the single case value.
          DeltaTwoPhase: float
          /// V/Vcrit recomputed with that damping. The verdict still uses the case value:
          /// this is the sensitivity, shown so the margin can be judged against it.
          FeiRatioTwoPhase: float
          Ok: bool
          Note: string }
    let addedMassCoef (pitchRatio: float) =
        let de = (0.96 + 0.5 * pitchRatio) * pitchRatio
        let d2 = de * de
        if d2 <= 1.0 then 2.0 else (d2 + 1.0) / (d2 - 1.0)
    let naturalFrequency (lambda2: float) (e: float) (i: float) (m: float) (l: float) =
        lambda2 / (2.0 * Math.PI) * sqrt (e * i / (m * Math.Pow(l, 4.0)))
    let inertia (dOut: float) (dIn: float) =
        Math.PI / 64.0 * (Math.Pow(dOut, 4.0) - Math.Pow(dIn, 4.0))
    let criticalVelocity (k: float) (fn: float) (d: float) (m: float)
                         (delta: float) (rho: float) =
        k * fn * d * sqrt (m * delta / (rho * d * d))
    let strouhal (pitchRatio: float) =
        max 0.2 (min 0.6 (0.85 / pitchRatio - 0.13))
    /// <summary>
    /// Two-phase damping ratio as a function of void fraction, relative to the single-phase
    /// value supplied for the case.
    /// </summary>
    /// <remarks>
    /// Damping in a bundle washed by a boiling mixture is strongly void dependent, with a
    /// broad maximum at intermediate void and a collapse back towards the single-phase value
    /// as the mixture approaches all-liquid or all-vapour. This is a screening shape, not a
    /// design correlation: it exists so the sensitivity of the critical velocity to damping
    /// can be reported per band, since Connors' velocity goes as its square root. The case
    /// input remains the basis of the verdict.
    /// </remarks>
    let twoPhaseDamping (deltaBase: float) (alpha: float) =
        let a = max 0.0 (min 1.0 alpha)
        // 4a(1-a) is one at half void and zero at either single-phase end.
        deltaBase * (1.0 + 3.0 * (4.0 * a * (1.0 - a)))
    let buffetFrequency (v: float) (d: float) (pitch: float) =
        let r = 1.0 - d / pitch
        v / d * (3.05 * r * r + 0.28)
    let check (band: int) (y: float) (span: float) (lambda2: float)
              (layout: Layout) (delta: float)
              (dOut: float) (dIn: float) (pitch: float)
              (eMetal: float) (rhoMetal: float)
              (vGap: float) (rhoShell: float) (rhoInside: float) (alpha: float) : Result =
        let aMetal = Math.PI / 4.0 * (dOut * dOut - dIn * dIn)
        let mMetal = rhoMetal * aMetal
        let mIn = rhoInside * Math.PI / 4.0 * dIn * dIn
        let cm = addedMassCoef (pitch / dOut)
        let mAdd = cm * rhoShell * Math.PI / 4.0 * dOut * dOut
        let m = mMetal + mIn + mAdd
        let i = inertia dOut dIn
        let fn = naturalFrequency lambda2 eMetal i m span
        let md = m * delta / (rhoShell * dOut * dOut)
        let kConnors = connorsK layout md
        let vc = criticalVelocity kConnors fn dOut m delta rhoShell
        let fs = strouhal (pitch / dOut) * vGap / dOut
        let ftb = buffetFrequency vGap dOut pitch
        let fei = vGap / vc
        let vr = fs / fn
        let br = ftb / fn
        let deltaTp = twoPhaseDamping delta alpha
        let vcTp = criticalVelocity kConnors fn dOut m deltaTp rhoShell
        { Band = band; Y = y; Span = span
          KConnors = kConnors; Delta = delta
          Alpha = alpha
          DeltaTwoPhase = deltaTp
          FeiRatioTwoPhase = vGap / vcTp
          FreqNat = fn; MassLin = m; MassAdded = mAdd; Cm = cm
          VGap = vGap; Rho = rhoShell; MassDamping = md
          VCrit = vc; FeiRatio = fei
          FreqVortex = fs; VortexRatio = vr
          FreqBuffet = ftb; BuffetRatio = br
          // Turbulent buffeting is the mechanism that dominates in two-phase cross flow, where
          // vortex shedding is broken up by the bubbles. It is screened the usual way: the
          // buffeting frequency must stay clear of the tube natural frequency, since random
          // broadband excitation at resonance is what wears the supports and opens fatigue
          // cracks over the life of the bundle.
          Ok = fei < 0.8 && not (vr > 0.5 && vr < 2.0) && br <= 0.5
          Note =
            if fei >= 1.0 then
                "INSTABILITA' FLUIDO-ELASTICA: la velocita' supera la critica. Ampiezza divergente, urto fra tubi e rottura per fatica al diaframma. Ridurre la campata."
            elif fei >= 0.8 then
                "margine insufficiente sull'instabilita' fluido-elastica (criterio V/Vcrit < 0.8)"
            elif vr > 0.5 && vr < 2.0 then
                "possibile aggancio con il distacco dei vortici (attenuato in flusso bifase)"
            elif br > 0.5 then
                "BUFFETING TURBOLENTO: la frequenza di eccitazione a banda larga si avvicina alla frequenza propria (criterio f_buffet / f_n <= 0.5). Non fa collassare nulla subito, ma consuma i supporti e apre cricche di fatica nel lungo periodo. E' il meccanismo dominante in flusso bifase."
            else "verificato su tutti i meccanismi" }
    let maxSpan (limit: float) (r: Result) =
        r.Span * sqrt (limit / max 1e-9 r.FeiRatio)
    let maxSpanWith (limit: float) (kNew: float) (deltaNew: float) (r: Result) =
        let scale = (kNew / r.KConnors) * sqrt (deltaNew / r.Delta)
        r.Span * sqrt (limit * scale / max 1e-9 r.FeiRatio)
    let ratioWith (kNew: float) (deltaNew: float) (r: Result) =
        r.FeiRatio / ((kNew / r.KConnors) * sqrt (deltaNew / r.Delta))




