namespace Whb.Core

open System
open Constants
open GasProps

/// <summary>
/// Provides water-gas shift chemistry models used by WHB gas-composition and enthalpy calculations.
/// </summary>
/// <remarks>
/// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
/// </remarks>
module Shift =
    type Mode =
        | Frozen
        | EquilibriumAbove of tFreezeK: float
        | FractionalApproach of frac: float * tFreezeK: float
    let modeName =
        function
        | Frozen -> "congelata (nessuna reazione)"
        | EquilibriumAbove t -> sprintf "equilibrio sopra %.0f °C, poi congelata" (kToC t)
        | FractionalApproach(f, t) ->
            sprintf "approccio %.0f%% all'equilibrio sopra %.0f °C" (100.0 * f) (kToC t)
    let kp (tK: float) = exp (4577.8 / tK - 4.33)
    let private extent (nCO: float) (nH2O: float) (nCO2: float) (nH2: float) (k: float) =
        let a = k - 1.0
        let b = -(k * (nCO + nH2O) + nCO2 + nH2)
        let c = k * nCO * nH2O - nCO2 * nH2
        let loLim = -(min nCO2 nH2) + 1e-12
        let hiLim = (min nCO nH2O) - 1e-12
        if hiLim <= loLim then 0.0
        else
            let roots =
                if abs a < 1e-12 then
                    if abs b < 1e-15 then [] else [ -c / b ]
                else
                    let disc = b * b - 4.0 * a * c
                    if disc < 0.0 then []
                    else
                        let sq = sqrt disc
                        [ (-b + sq) / (2.0 * a); (-b - sq) / (2.0 * a) ]
            match roots |> List.filter (fun x -> x > loLim && x < hiLim) with
            | [] -> 0.0
            | l -> l |> List.minBy abs
    let private applyExtent (c: Composition) (xi: float) : Composition =
        let get sp = molFrac c sp
        let upd sp v (l: Composition) =
            if l |> List.exists (fun (s, _) -> s = sp) then
                l |> List.map (fun (s, y) -> if s = sp then (s, max 0.0 v) else (s, y))
            else (sp, max 0.0 v) :: l
        c
        |> upd CO (get CO - xi)
        |> upd H2O (get H2O - xi)
        |> upd CO2 (get CO2 + xi)
        |> upd H2 (get H2 + xi)
    let equilibrate (mode: Mode) (c0: Composition) (tK: float) : Composition =
        match mode with
        | Frozen -> c0
        | _ ->
            let tFreeze, frac =
                match mode with
                | EquilibriumAbove t -> t, 1.0
                | FractionalApproach(f, t) -> t, max 0.0 (min 1.0 f)
                | Frozen -> 0.0, 0.0
            if tK < tFreeze then c0
            else
                let c = normalize c0
                let xiEq = extent (molFrac c CO) (molFrac c H2O) (molFrac c CO2) (molFrac c H2) (kp tK)
                normalize (applyExtent c (frac * xiEq))
    let stateFromEnthalpyAt (mode: Mode) (real: bool) (pPa: float) (cIn: Composition) (h: float) =
        // Enthalpy grows monotonically with temperature (cp > 0), so the inversion is
        // driven by a bracketed Newton iteration on the residual, using the mixture cp
        // that the property evaluation already returns as the derivative.
        let residualAt (c: Composition) (t: float) =
            let struct (hc, cp) = enthalpyAbsRealWithCp real c t pPa
            struct (hc - h, cp)
        match mode with
        | Frozen ->
            let t = newtonIncreasing (residualAt cIn) 250.0 2500.0 1000.0 1e-9 60
            (t, cIn)
        | _ ->
            let f (t: float) = residualAt (equilibrate mode cIn t) t
            let t = newtonIncreasing f 250.0 2500.0 1000.0 1e-9 60
            (t, equilibrate mode cIn t)
    /// <summary>
    /// Gas composition in equilibrium with the given enthalpy state.
    /// </summary>
    /// <remarks>
    /// In <c>Frozen</c> mode the composition is invariant, so the enthalpy inversion
    /// is skipped entirely; other modes fall back to the full state solve.
    /// </remarks>
    let compositionFromEnthalpyAt (mode: Mode) (real: bool) (pPa: float) (cIn: Composition) (h: float) =
        match mode with
        | Frozen -> cIn
        | _ -> snd (stateFromEnthalpyAt mode real pPa cIn h)
    let stateFromEnthalpy (mode: Mode) (cIn: Composition) (h: float) =
        stateFromEnthalpyAt mode false 0.0 cIn h




