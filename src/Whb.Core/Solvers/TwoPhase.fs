namespace Whb.Core

open System
open Constants

/// Idraulica bifase: frazione di vuoto, moltiplicatori bifase,
/// perdite di carico in tubo e in attraversamento di fascio tubiero.
module TwoPhase =

    type VoidModel =
        | Homogeneous
        | ChisholmSlip
        | ZuberFindlay
        | Smith

    let voidModelName =
        function
        | Homogeneous -> "Omogeneo (S = 1)"
        | ChisholmSlip -> "Chisholm (slip ratio)"
        | ZuberFindlay -> "Zuber-Findlay (drift-flux)"
        | Smith -> "Smith (1969)"

    /// Frazione di vuoto α.
    ///   x  : titolo massico
    ///   g_ : flusso di massa [kg/(m²·s)] (serve solo al drift-flux)
    let voidFraction (model: VoidModel) (x: float) (s: Steam.SatProps) (g_: float) =
        let x = min 0.9999 (max 0.0 x)
        if x <= 0.0 then 0.0
        else
            match model with
            | Homogeneous -> 1.0 / (1.0 + (1.0 - x) / x * s.RhoV / s.RhoL)
            | ChisholmSlip ->
                let sl = sqrt (1.0 - x * (1.0 - s.RhoL / s.RhoV))
                1.0 / (1.0 + sl * (1.0 - x) / x * s.RhoV / s.RhoL)
            | Smith ->
                let k = 0.4
                let r = s.RhoV / s.RhoL
                let sl =
                    k + (1.0 - k)
                        * sqrt ((1.0 / r + k * (1.0 - x) / x) / (1.0 + k * (1.0 - x) / x))
                1.0 / (1.0 + sl * (1.0 - x) / x * r)
            | ZuberFindlay ->
                let jv = g_ * x / s.RhoV
                let jl = g_ * (1.0 - x) / s.RhoL
                let j = jv + jl
                let c0 = 1.13
                let vgj = 1.53 * Math.Pow(s.Sigma * g * (s.RhoL - s.RhoV) / (s.RhoL * s.RhoL), 0.25)
                let a = jv / (c0 * j + vgj)
                min 0.999 (max 0.0 a)

    /// Densità della miscela bifase (per il carico statico) [kg/m³]
    let mixtureDensity (alpha: float) (s: Steam.SatProps) =
        alpha * s.RhoV + (1.0 - alpha) * s.RhoL

    /// Densità omogenea (per accelerazione e velocità media) [kg/m³]
    let homogeneousDensity (x: float) (s: Steam.SatProps) =
        1.0 / (x / s.RhoV + (1.0 - x) / s.RhoL)

    // ------------------------------------------------------------------
    // Moltiplicatori bifase per attrito
    // ------------------------------------------------------------------
    type FrictionModel =
        | HomogeneousFriction
        | LockhartMartinelli
        | Friedel
        | ChisholmB

    let frictionModelName =
        function
        | HomogeneousFriction -> "Omogeneo (McAdams)"
        | LockhartMartinelli -> "Lockhart-Martinelli / Chisholm"
        | Friedel -> "Friedel (1979)"
        | ChisholmB -> "Chisholm B (1973)"

    /// Parametro di Martinelli turbolento-turbolento
    let martinelliXtt (x: float) (s: Steam.SatProps) =
        let x = min 0.999 (max 1e-6 x)
        Math.Pow((1.0 - x) / x, 0.9)
        * Math.Pow(s.RhoV / s.RhoL, 0.5)
        * Math.Pow(s.MuL / s.MuV, 0.1)

    /// Moltiplicatore bifase φ²_lo (riferito al flusso totale come liquido).
    let phi2LO (model: FrictionModel) (x: float) (g_: float) (d: float) (s: Steam.SatProps) =
        let x = min 0.999 (max 0.0 x)
        if x <= 0.0 then 1.0
        else
            match model with
            | HomogeneousFriction ->
                // viscosità omogenea di McAdams
                let muH = 1.0 / (x / s.MuV + (1.0 - x) / s.MuL)
                (1.0 + x * (s.RhoL / s.RhoV - 1.0)) * Math.Pow(muH / s.MuL, -0.25)
                |> fun v -> max 1.0 v
            | LockhartMartinelli ->
                let xtt = martinelliXtt x s
                let c = 20.0
                let phi2l = 1.0 + c / xtt + 1.0 / (xtt * xtt)
                // conversione da φ²_l a φ²_lo
                phi2l * Math.Pow(1.0 - x, 1.75)
            | ChisholmB ->
                let y2 =
                    (s.RhoL / s.RhoV) * Math.Pow(s.MuV / s.MuL, 0.25)
                let y = sqrt y2
                let b =
                    if y < 9.5 then 55.0 / sqrt (max g_ 1.0)
                    elif y < 28.0 then 520.0 / (y * sqrt (max g_ 1.0))
                    else 15000.0 / (y * y * sqrt (max g_ 1.0))
                1.0 + (y2 - 1.0) * (b * Math.Pow(x, 0.875) * Math.Pow(1.0 - x, 0.875)
                                    + Math.Pow(x, 1.75))
            | Friedel ->
                let rhoH = homogeneousDensity x s
                let fLO = 0.079 * Math.Pow(max 1.0 (g_ * d / s.MuL), -0.25) * 4.0
                let fVO = 0.079 * Math.Pow(max 1.0 (g_ * d / s.MuV), -0.25) * 4.0
                let e = (1.0 - x) ** 2.0 + x * x * (s.RhoL * fVO) / (s.RhoV * fLO)
                let f = Math.Pow(x, 0.78) * Math.Pow(1.0 - x, 0.224)
                let h =
                    Math.Pow(s.RhoL / s.RhoV, 0.91)
                    * Math.Pow(s.MuV / s.MuL, 0.19)
                    * Math.Pow(1.0 - s.MuV / s.MuL, 0.7)
                let vel = g_ / rhoH
                let fr = vel * vel / (g * d)
                let we = g_ * g_ * d / (rhoH * s.Sigma)
                e + 3.24 * f * h / (Math.Pow(fr, 0.045) * Math.Pow(we, 0.035))

    /// Perdita di carico per attrito in tubo bifase [Pa]
    let dpFrictionTwoPhase
        (model: FrictionModel) (x: float) (g_: float) (d: float) (l: float) (s: Steam.SatProps) =
        let reLO = max 1.0 (g_ * d / s.MuL)
        let fLO = GasSide.darcyFriction reLO 0.0
        let dpLO = fLO * l / d * g_ * g_ / (2.0 * s.RhoL)
        dpLO * phi2LO model x g_ d s

    /// Perdita di carico per accelerazione [Pa] fra titolo xIn e xOut
    let dpAcceleration (model: VoidModel) (xIn: float) (xOut: float) (g_: float) (s: Steam.SatProps) =
        let term x =
            let a = voidFraction model x s g_
            if a <= 0.0 then 1.0 / s.RhoL
            elif a >= 1.0 then 1.0 / s.RhoV
            else x * x / (s.RhoV * a) + (1.0 - x) ** 2.0 / (s.RhoL * (1.0 - a))
        g_ * g_ * (term xOut - term xIn)

    /// Perdita di carico statica in un tratto verticale bifase [Pa]
    let dpStatic (model: VoidModel) (x: float) (g_: float) (h: float) (s: Steam.SatProps) =
        let a = voidFraction model x s g_
        mixtureDensity a s * g * h

    // ------------------------------------------------------------------
    // Attraversamento di fascio tubiero (crossflow)
    // ------------------------------------------------------------------
    /// Fattore d'attrito di Jakob per banchi di tubi.
    ///   a = ST/d (passo trasversale), b = SL/d (passo longitudinale)
    let bundleFrictionFactor (re: float) (a: float) (b: float) (staggered: bool) =
        let re = max 10.0 re
        if staggered then
            (0.25 + 0.1175 / Math.Pow(max 0.05 (a - 1.0), 1.08)) * Math.Pow(re, -0.16)
        else
            (0.044 + 0.08 * b / Math.Pow(max 0.05 (a - 1.0), 0.43 + 1.13 / b))
            * Math.Pow(re, -0.15)

    /// ΔP monofase di attraversamento fascio [Pa]:  Δp = f·Nr·G_max²/(2ρ)
    let dpCrossflow (re: float) (nRows: float) (gMax: float) (rho: float) (a: float) (b: float) (staggered: bool) =
        let f = bundleFrictionFactor re a b staggered
        f * nRows * gMax * gMax / (2.0 * rho)

    /// Moltiplicatore bifase per crossflow su fascio (Ishihara et al., C = 8)
    let phi2CrossflowLiquid (x: float) (s: Steam.SatProps) =
        let xtt = martinelliXtt x s
        let c = 8.0
        1.0 + c / xtt + 1.0 / (xtt * xtt)

    /// ΔP bifase di attraversamento fascio [Pa]
    let dpCrossflowTwoPhase
        (x: float) (nRows: float) (gMax: float) (s: Steam.SatProps)
        (d: float) (a: float) (b: float) (staggered: bool) =
        let gl = gMax * (1.0 - x)
        let reL = max 10.0 (gl * d / s.MuL)
        let dpL = dpCrossflow reL nRows gl s.RhoL a b staggered
        dpL * phi2CrossflowLiquid x s
