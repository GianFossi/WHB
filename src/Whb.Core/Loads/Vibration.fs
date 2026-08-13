namespace Whb.Core

open System
open Constants

/// <summary>
/// Provides vibration functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Vibration =

    /// <summary>
    /// Represents layout data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Layout =
        | Triangular30
        | RotatedTriangular60
        | Square90
        | RotatedSquare45

    /// <summary>
    /// Represents jointtype data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type JointType =
        | CreviceFreeWeld
        | FullPenetrationWeld

    /// <summary>
    /// Calculates or returns jointname for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let jointName =
        function
        | CreviceFreeWeld -> "saldatura crevice-free -> APPOGGIO alla piastra"
        | FullPenetrationWeld -> "saldatura a piena penetrazione -> INCASTRO alla piastra"

    /// <summary>
    /// Calculates or returns lambda2of for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let lambda2Of (clampedEnds: int) =
        match clampedEnds with
        | 0 -> 9.87
        | 1 -> 15.42
        | _ -> 22.37

    /// <summary>
    /// Calculates or returns layoutname for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let layoutName =
        function
        | Triangular30 -> "30° TRIANGOLARE (vertice nel flusso)"
        | RotatedTriangular60 -> "60° TRIANGOLARE RUOTATO (lato lungo trasversale al flusso)"
        | Square90 -> "90° QUADRATO"
        | RotatedSquare45 -> "45° QUADRATO RUOTATO"

    /// <summary>
    /// Calculates or returns connorsk for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let connorsK (lay: Layout) (massDamping: float) =
        match lay with
        | Triangular30 | Square90 -> 4.0
        | RotatedTriangular60 | RotatedSquare45 ->
            if massDamping <= 0.54 then 1.1 else 1.5

    /// <summary>
    /// Represents result data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
          Ok: bool
          Note: string }

    /// <summary>
    /// Calculates or returns addedmasscoef for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let addedMassCoef (pitchRatio: float) =
        let de = (0.96 + 0.5 * pitchRatio) * pitchRatio
        let d2 = de * de
        if d2 <= 1.0 then 2.0 else (d2 + 1.0) / (d2 - 1.0)

    /// <summary>
    /// Calculates or returns naturalfrequency for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let naturalFrequency (lambda2: float) (e: float) (i: float) (m: float) (l: float) =
        lambda2 / (2.0 * Math.PI) * sqrt (e * i / (m * Math.Pow(l, 4.0)))

    /// <summary>
    /// Calculates or returns inertia for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let inertia (dOut: float) (dIn: float) =
        Math.PI / 64.0 * (Math.Pow(dOut, 4.0) - Math.Pow(dIn, 4.0))

    /// <summary>
    /// Calculates or returns criticalvelocity for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let criticalVelocity (k: float) (fn: float) (d: float) (m: float)
                         (delta: float) (rho: float) =
        k * fn * d * sqrt (m * delta / (rho * d * d))

    /// <summary>
    /// Calculates or returns strouhal for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let strouhal (pitchRatio: float) =
        max 0.2 (min 0.6 (0.85 / pitchRatio - 0.13))

    /// <summary>
    /// Calculates or returns buffetfrequency for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let buffetFrequency (v: float) (d: float) (pitch: float) =
        let r = 1.0 - d / pitch
        v / d * (3.05 * r * r + 0.28)

    /// <summary>
    /// Calculates or returns check for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let check (band: int) (y: float) (span: float) (lambda2: float)
              (layout: Layout) (delta: float)
              (dOut: float) (dIn: float) (pitch: float)
              (eMetal: float) (rhoMetal: float)
              (vGap: float) (rhoShell: float) (rhoInside: float) : Result =
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
        { Band = band; Y = y; Span = span
          KConnors = kConnors; Delta = delta
          FreqNat = fn; MassLin = m; MassAdded = mAdd; Cm = cm
          VGap = vGap; Rho = rhoShell; MassDamping = md
          VCrit = vc; FeiRatio = fei
          FreqVortex = fs; VortexRatio = vr
          FreqBuffet = ftb; BuffetRatio = br
          Ok = fei < 0.8 && not (vr > 0.5 && vr < 2.0)
          Note =
            if fei >= 1.0 then
                "INSTABILITA' FLUIDO-ELASTICA: la velocita' supera la critica. Ampiezza divergente, urto fra tubi e rottura per fatica al diaframma. Ridurre la campata."
            elif fei >= 0.8 then
                "margine insufficiente sull'instabilita' fluido-elastica (criterio V/Vcrit < 0.8)"
            elif vr > 0.5 && vr < 2.0 then
                "possibile aggancio con il distacco dei vortici (attenuato in flusso bifase)"
            else "verificato su tutti i meccanismi" }

    /// <summary>
    /// Calculates or returns maxspan for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let maxSpan (limit: float) (r: Result) =
        r.Span * sqrt (limit / max 1e-9 r.FeiRatio)

    /// <summary>
    /// Calculates or returns maxspanwith for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let maxSpanWith (limit: float) (kNew: float) (deltaNew: float) (r: Result) =
        let scale = (kNew / r.KConnors) * sqrt (deltaNew / r.Delta)
        r.Span * sqrt (limit * scale / max 1e-9 r.FeiRatio)

    /// <summary>
    /// Calculates or returns ratiowith for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let ratioWith (kNew: float) (deltaNew: float) (r: Result) =
        r.FeiRatio / ((kNew / r.KConnors) * sqrt (deltaNew / r.Delta))
