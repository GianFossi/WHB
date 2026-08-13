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

    /// <summary>
    /// Represents mode data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
    type Mode =
        | Frozen
        | EquilibriumAbove of tFreezeK: float
        | FractionalApproach of frac: float * tFreezeK: float

    /// <summary>
    /// Calculates or returns modename for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
    let modeName =
        function
        | Frozen -> "congelata (nessuna reazione)"
        | EquilibriumAbove t -> sprintf "equilibrio sopra %.0f °C, poi congelata" (kToC t)
        | FractionalApproach(f, t) ->
            sprintf "approccio %.0f%% all'equilibrio sopra %.0f °C" (100.0 * f) (kToC t)

    /// <summary>
    /// Calculates or returns kp for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
    let kp (tK: float) = exp (4577.8 / tK - 4.33)

    /// <summary>
    /// Calculates or returns extent for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
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

    /// <summary>
    /// Calculates or returns applyExtent for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
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

    /// <summary>
    /// Calculates or returns equilibrate for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
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

    /// <summary>
    /// Calculates or returns statefromenthalpyat for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
    let stateFromEnthalpyAt (mode: Mode) (real: bool) (pPa: float) (cIn: Composition) (h: float) =
        let hOf (c: Composition) (t: float) =
            if real then enthalpyAbsReal true c t pPa else enthalpyAbs c t
        match mode with
        | Frozen ->
            let t = bisect (fun t -> hOf cIn t - h) 250.0 2500.0 1e-4 200
            (t, cIn)
        | _ ->
            let f (t: float) =
                let c = equilibrate mode cIn t
                hOf c t - h
            let t = bisect f 250.0 2500.0 1e-4 200
            (t, equilibrate mode cIn t)

    /// <summary>
    /// Calculates or returns statefromenthalpy for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Models water-gas shift equilibrium or frozen-composition behavior for process calculations. The selected mode affects gas composition, enthalpy inversion, and thermal balance; confirm equilibrium assumptions against process chemistry requirements.
    /// </remarks>
    let stateFromEnthalpy (mode: Mode) (cIn: Composition) (h: float) =
        stateFromEnthalpyAt mode false 0.0 cIn h


