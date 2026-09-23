namespace Whb.Core

open System
open Constants

/// <summary>
/// Provides gas-mixture thermodynamic, transport, radiation, and real-gas property calculations for WHB process models.
/// </summary>
/// <remarks>
/// Provides gas-mixture process properties using ideal and real-gas correlations, mixture rules, radiation factors, and enthalpy calculations. Validate composition normalization, pressure range, temperature range, and species data before using results for final design.
/// </remarks>
module GasProps =
    /// <summary>
    /// Identifies the gas species supported by the WHB gas-property model.
    /// </summary>
    type Species =
        | H2 | N2 | O2 | CO | CO2 | CH4 | H2O | Ar | NH3
        | H2S | SO2 | COS | CS2 | S2 | S6 | S8
        | C2H4 | C2H6 | C3H6 | C3H8 | C2H2 | C6H6 | C7H8
        | NO | NO2 | N2O | SO3 | HCN | He
    /// <summary>
    /// Returns the compact index used to address per-species tables and caches.
    /// </summary>
    /// <param name="sp">The species.</param>
    /// <returns>A stable integer ID for the species.</returns>
    let private speciesIndex =
        function
        | H2 -> 0 | N2 -> 1 | O2 -> 2 | CO -> 3 | CO2 -> 4
        | CH4 -> 5 | H2O -> 6 | Ar -> 7 | NH3 -> 8
        | H2S -> 9 | SO2 -> 10 | COS -> 11 | CS2 -> 12 | S2 -> 13 | S6 -> 14 | S8 -> 15
        | C2H4 -> 16 | C2H6 -> 17 | C3H6 -> 18 | C3H8 -> 19 | C2H2 -> 20 | C6H6 -> 21 | C7H8 -> 22
        | NO -> 23 | NO2 -> 24 | N2O -> 25 | SO3 -> 26 | HCN -> 27 | He -> 28
    /// <summary>
    /// Lists all gas species supported by the internal property library.
    /// </summary>
    /// <returns>A list of known gas species in the default order used for parsing and normalization.</returns>
    let allSpecies =
        [ H2; N2; O2; CO; CO2; CH4; H2O; Ar; NH3
          H2S; SO2; COS; CS2; S2; S6; S8
          C2H4; C2H6; C3H6; C3H8; C2H2; C6H6; C7H8
          NO; NO2; N2O; SO3; HCN; He ]
    /// <summary>
    /// Formats the species identifier as the canonical symbolic name.
    /// </summary>
    /// <remarks>
    /// The name is also the species key in the WhbThermo database.
    /// </remarks>
    /// <param name="sp">The species to format.</param>
    /// <returns>A string representation of the species.</returns>
    let speciesName (sp: Species) = sprintf "%A" sp
    /// <summary>
    /// Species data resolved from the WhbThermo database on first use, indexed by <c>speciesIndex</c>.
    /// </summary>
    let private records =
        lazy (
            let table = Array.zeroCreate (List.length allSpecies)
            for sp in allSpecies do
                table.[speciesIndex sp] <- GasThermoAdapter.record (speciesName sp)
            table)
    let private recordOf (sp: Species) : GasThermoAdapter.SpeciesRecord =
        records.Force().[speciesIndex sp]
    /// <summary>
    /// Returns the molecular weight of a species in kg/mol.
    /// </summary>
    /// <param name="sp">The species whose molar mass is requested.</param>
    /// <returns>The molar mass of the species in kg/mol, from the WhbThermo database.</returns>
    let molarMass (sp: Species) = (recordOf sp).MolarMass
    /// <summary>
    /// Computes the ideal-gas molar heat capacity at constant pressure for a single species.
    /// </summary>
    /// <param name="sp">The species to evaluate.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The molar heat capacity in J/mol·K, from the WhbThermo NASA-9 fit.</returns>
    let cpMolar (sp: Species) (tK: float) = GasThermoAdapter.cpMolar (recordOf sp) tK
    /// <summary>
    /// Returns the standard formation enthalpy of a species in J/mol.
    /// </summary>
    /// <param name="sp">The species whose formation enthalpy is requested.</param>
    /// <returns>The reference formation enthalpy in J/mol at 298.15 K, from the WhbThermo NASA-9 fit.</returns>
    let hForm (sp: Species) = (recordOf sp).FormationEnthalpy
    /// <summary>
    /// Returns the absolute molar enthalpy of a species including the formation term.
    /// </summary>
    /// <param name="sp">The species to evaluate.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The absolute molar enthalpy in J/mol.</returns>
    let hMolarAbs (sp: Species) (tK: float) = GasThermoAdapter.hMolarAbs (recordOf sp) tK
    /// <summary>
    /// Calculates the sensible molar enthalpy increment of a species relative to 298.15 K.
    /// </summary>
    /// <param name="sp">The gas species.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The sensible molar enthalpy in J/mol.</returns>
    let hMolar (sp: Species) (tK: float) =
        let r = recordOf sp
        GasThermoAdapter.hMolarAbs r tK - r.FormationEnthalpy
    /// <summary>
    /// Evaluates the pure-species low-pressure dynamic viscosity.
    /// </summary>
    /// <param name="sp">The gas species.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The dynamic viscosity in Pa·s, from the WhbThermo NASA CEA or Sutherland fit.</returns>
    let muPure (sp: Species) (tK: float) = GasThermoAdapter.viscosity (recordOf sp) tK
    /// <summary>
    /// Evaluates the pure-species low-pressure thermal conductivity in W/(m·K).
    /// </summary>
    /// <param name="sp">The species to assess.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The species thermal conductivity, from the WhbThermo NASA CEA or Sutherland fit.</returns>
    let kPure (sp: Species) (tK: float) = GasThermoAdapter.conductivity (recordOf sp) tK
    /// <summary>
    /// Represents a gas composition as a list of species/mol fractions.
    /// </summary>
    type Composition = (Species * float) list
    /// <summary>
    /// Normalizes case-insensitive species names to the internal enum values.
    /// </summary>
    /// <remarks>
    /// The lookup also accepts common aliases such as argon, helium, benzene, and toluene.
    /// </remarks>
    /// <param name="name">The input name to parse.</param>
    /// <returns>The matched species, when available.</returns>
    let private speciesByUpperName =
        [ yield! allSpecies |> List.map (fun sp -> ((speciesName sp).ToUpperInvariant(), sp))
          "ARGON", Ar
          "HELIUM", He
          "ELIO", He
          "BENZENE", C6H6
          "BENZENE C6H6", C6H6
          "TOLUENE", C7H8 ] |> Map.ofList
    /// <summary>
    /// Attempts to parse a gas species name into the internal species enum.
    /// </summary>
    /// <param name="name">The user provided name.</param>
    /// <returns>A species option or None if the input is empty or unknown.</returns>
    let tryParseSpecies (name: string) : Species option =
        if String.IsNullOrWhiteSpace name then None
        else speciesByUpperName.TryFind(name.Trim().ToUpperInvariant())
    /// <summary>
    /// Normalizes a composition so that the mole fractions sum to one.
    /// </summary>
    /// <param name="c">The composition to normalize.</param>
    /// <returns>A normalized composition with the same species order.</returns>
    let normalize (c: Composition) : Composition =
        let s = c |> List.sumBy snd
        if s <= 0.0 then failwith "Composizione nulla"
        elif abs (s - 1.0) < 1e-12 then c
        else c |> List.map (fun (k, v) -> (k, v / s))
    /// <summary>
    /// Computes the mixture molar mass from a normalized composition.
    /// </summary>
    /// <param name="c">The composition.</param>
    /// <returns>The average molar mass in kg/mol.</returns>
    let mixMolarMass (c: Composition) =
        c |> List.sumBy (fun (sp, y) -> y * molarMass sp)
    module Virial =

        /// <summary>
        /// Returns the critical parameters for a species needed by the virial mixture model.
        /// </summary>
        /// <param name="sp">The species to inspect.</param>
        /// <returns>
        /// The critical temperature [K], pressure [Pa], acentric factor, and critical volume [m³/mol] from the
        /// WhbThermo database, or None when the database lacks any of them (the sulfur allotropes).
        /// </returns>
        let criticalOpt (sp: Species) = (recordOf sp).Critical
        /// <summary>
        /// Gets the critical parameters for a species or throws if they are unavailable.
        /// </summary>
        /// <param name="sp">The species.</param>
        /// <returns>The critical values for a species.</returns>
        let critical sp =
            match criticalOpt sp with
            | Some c -> c
            | None -> failwithf "Nessun dato critico per %A" sp

        /// <summary>
        /// Computes the second virial coefficient term using the Pitzer correlation.
        /// </summary>
        /// <param name="tc">Critical temperature in kelvin.</param>
        /// <param name="pc">Critical pressure in pascal.</param>
        /// <param name="om">Acentric factor.</param>
        /// <param name="tK">Temperature in kelvin.</param>
        /// <returns>The pseudocritical virial contribution.</returns>
        let pitzer (tc: float) (pc: float) (om: float) (tK: float) =
            let tr = max 0.30 (tK / tc)
            let b0 = 0.083 - 0.422 / Math.Pow(tr, 1.6)
            let b1 = 0.139 - 0.172 / Math.Pow(tr, 4.2)
            (b0 + om * b1) * R * tc / pc

        /// <summary>
        /// Estimates the virial coefficient correction for water vapor.
        /// </summary>
        /// <param name="tK">Temperature in kelvin.</param>
        /// <returns>The water-specific virial pressure term.</returns>
        let bWater (tK: float) =
            let p = 1000.0                       // Pa: gas praticamente ideale
            let t = min tK 1073.15
            let (v, _, _, _) = Steam.region2 (p / 1.0e6) t   // v [m³/kg]
            let z = p * v / (Rw * 1000.0 * t)
            let b = (z - 1.0) * R * t / p
            if tK <= 1073.15 then b else b * Math.Pow(1073.15 / tK, 1.6)
        /// <summary>
        /// Stores the precomputed pseudo-critical interaction data for a species pair.
        /// </summary>
        /// <remarks>
        /// The pair coefficients are built once and reused across mixture evaluations to avoid recomputing the same values.
        /// </remarks>
        [<Struct>]
        type private PairTerm =
            { I: int
              J: int
              Mult: float          // 1 on the diagonal, 2 off-diagonal (bPair is symmetric)
              Tc: float
              Pc: float
              Om: float
              IsWater: bool }
        /// <summary>
        /// Builds the virial interaction coefficients for a species set.
        /// </summary>
        /// <param name="species">The species that define the mixture.</param>
        /// <returns>The cached pair coefficients used for mixture B calculations.</returns>
        let private buildPairTerms (species: Species[]) =
            let n = species.Length
            let acc = ResizeArray<PairTerm>(n * (n + 1) / 2)
            for i in 0 .. n - 1 do
                for j in i .. n - 1 do
                    let a = species.[i]
                    let b = species.[j]
                    let mult = if i = j then 1.0 else 2.0
                    if a = b then
                        if (recordOf a).If97SecondVirial then
                            acc.Add { I = i; J = j; Mult = mult
                                      Tc = 0.0; Pc = 0.0; Om = 0.0; IsWater = true }
                        else
                            match criticalOpt a with
                            | Some (tc, pc, om, _) ->
                                acc.Add { I = i; J = j; Mult = mult
                                          Tc = tc; Pc = pc; Om = om; IsWater = false }
                            | None -> ()
                    else
                        match criticalOpt a, criticalOpt b with
                        | Some (tca, pca, oma, vca), Some (tcb, pcb, omb, vcb) ->
                            // k_ij from the WhbThermo binary table; 0 when the pair has none.
                            let kij = GasThermoAdapter.virialKij (speciesName a) (speciesName b)
                            let tcij = sqrt (tca * tcb) * (1.0 - kij)
                            let omij = 0.5 * (oma + omb)
                            let zca = pca * vca / (R * tca)
                            let zcb = pcb * vcb / (R * tcb)
                            let zcij = 0.5 * (zca + zcb)
                            let vcij = (0.5 * (Math.Cbrt vca + Math.Cbrt vcb)) ** 3.0
                            let pcij = zcij * R * tcij / vcij
                            acc.Add { I = i; J = j; Mult = mult
                                      Tc = tcij; Pc = pcij; Om = omij; IsWater = false }
                        | _ -> ()
            acc.ToArray()
        /// <summary>
        /// Stores the per-species-set virial pair coefficient cache.
        /// </summary>
        let private pairTermCache =
            Collections.Concurrent.ConcurrentDictionary<int64, PairTerm[]>()
        /// <summary>
        /// Gets the pair-term cache entry for a species array.
        /// </summary>
        /// <param name="species">The species list for the current mixture.</param>
        /// <returns>The pair coefficient array for the mixture.</returns>
        let private pairTermsFor (species: Species[]) =
            if species.Length > 12 then buildPairTerms species
            else
                let mutable key = 1L
                for sp in species do
                    key <- (key <<< 5) ||| int64 (speciesIndex sp)
                match pairTermCache.TryGetValue key with
                | true, v -> v
                | _ ->
                    let v = buildPairTerms species
                    pairTermCache.[key] <- v
                    v

        /// <summary>
        /// Computes the mixture second virial coefficient approaching the ideal-gas limit for a composition.
        /// </summary>
        /// <param name="c">The gas composition.</param>
        /// <param name="tK">Temperature in kelvin.</param>
        /// <returns>The mixture virial coefficient.</returns>
        let bMix (c: Composition) (tK: float) =
            let n = List.length c
            let species = Array.zeroCreate n
            let ys = Array.zeroCreate n
            let mutable k = 0
            for (sp, y) in c do
                species.[k] <- sp
                ys.[k] <- y
                k <- k + 1
            let terms = pairTermsFor species
            let mutable s = 0.0
            for t in terms do
                let b = if t.IsWater then bWater tK else pitzer t.Tc t.Pc t.Om tK
                s <- s + t.Mult * ys.[t.I] * ys.[t.J] * b
            s

        /// <summary>
        /// Evaluates the virial residual terms for compression factor, enthalpy departure, and heat-capacity departure.
        /// </summary>
        /// <param name="c">The composition.</param>
        /// <param name="tK">Temperature in kelvin.</param>
        /// <param name="pPa">Pressure in pascal.</param>
        /// <returns>A tuple containing the compressibility factor, enthalpy residual, and heat-capacity residual.</returns>
        let residual (c: Composition) (tK: float) (pPa: float) =
            let dt = 2.0
            let bm = bMix c tK
            let bp = bMix c (tK + dt)
            let bmn = bMix c (tK - dt)
            let db = (bp - bmn) / (2.0 * dt)
            let d2b = (bp - 2.0 * bm + bmn) / (dt * dt)
            let z = 1.0 + bm * pPa / (R * tK)
            let hRes = pPa * (bm - tK * db)
            let cpRes = -pPa * tK * d2b
            (z, hRes, cpRes)
    /// <summary>
    /// Returns the enthalpy departure term when the real-gas correction is active.
    /// </summary>
    /// <param name="real">Whether the real-gas correction should be applied.</param>
    /// <param name="c">The gas composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <param name="pPa">Pressure in pascal.</param>
    /// <returns>The real-gas enthalpy departure term.</returns>
    let departure (real: bool) (c: Composition) (tK: float) (pPa: float) =
        if not real then 0.0
        else let (_, h, _) = Virial.residual c tK pPa in h
    /// <summary>
    /// Computes the Wilke diffusion factor used in the mixture transport-property averaging.
    /// </summary>
    /// <param name="mi">Molar mass of species i.</param>
    /// <param name="mj">Molar mass of species j.</param>
    /// <param name="mui">Viscosity of species i.</param>
    /// <param name="muj">Viscosity of species j.</param>
    /// <returns>The Wilke interaction coefficient.</returns>
    let private phiWilke (mi: float) (mj: float) (mui: float) (muj: float) =
        let a = 1.0 + sqrt (mui / muj) * Math.Pow(mj / mi, 0.25)
        a * a / sqrt (8.0 * (1.0 + mi / mj))
    /// <summary>
    /// Stores the equilibrium thermodynamic properties of a gas mixture at a given state.
    /// </summary>
    type MixProps =
        { T: float          // K
          P: float          // Pa
          M: float          // kg/mol
          Rho: float        // kg/m³
          Cp: float         // J/(kg·K)
          Mu: float         // Pa·s
          K: float          // W/(m·K)
          Pr: float
          H: float }        // J/kg (sensibile, rif. 298.15 K)
    /// <summary>
    /// Represents the available mixing rules for the transport properties of a gas mixture.
    /// </summary>
    type MixingRule =
        | Wilke
        | MolarAverage
    /// <summary>
    /// Returns a human-readable description of the selected mixture model.
    /// </summary>
    /// <param name="rule">The mixing rule.</param>
    /// <returns>The name of the mixing rule.</returns>
    let mixingRuleName = function
        | Wilke -> "Wilke (µ) / Wassiljewa-Mason-Saxena (k)"
        | MolarAverage -> "media molare (per confronto con datasheet)"
    /// <summary>
    /// Computes the mixture transport and thermodynamic properties using the selected mixing rule.
    /// </summary>
    /// <param name="rule">The mixing rule used for viscosity and conductivity.</param>
    /// <param name="real">Whether to include the real-gas residual enthalpy and cp correction.</param>
    /// <param name="c">The gas composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <param name="pPa">Pressure in pascal.</param>
    /// <param name="z">The compressibility factor used for density evaluation.</param>
    /// <returns>The evaluated mixture state data.</returns>
    let mixReal (rule: MixingRule) (real: bool) (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        let cn = normalize c
        let m = mixMolarMass cn
        let cpm = cn |> List.sumBy (fun (sp, y) -> y * cpMolar sp tK)
        let hm = cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)
        let mus = cn |> List.map (fun (sp, y) -> (sp, y, muPure sp tK, kPure sp tK, molarMass sp))
        let muMix, kMix =
            match rule with
            | MolarAverage ->
                let sw = mus |> List.sumBy (fun (_, y, _, _, m) -> y * sqrt m)
                (mus |> List.sumBy (fun (_, y, mu, _, _) -> y * mu),
                 (mus |> List.sumBy (fun (_, y, _, k, m) -> y * sqrt m * k)) / sw)
            | Wilke ->
                // The Wilke denominator depends on species i only, so it is built once and
                // reused for viscosity and conductivity instead of being summed twice. Same
                // expression in the same accumulation order, so the result is unchanged.
                let musArr = List.toArray mus
                let dens =
                    musArr
                    |> Array.map (fun (_, _, mui, _, mi) ->
                        mus |> List.sumBy (fun (_, yj, muj, _, mj) -> yj * phiWilke mi mj mui muj))
                let mutable muAcc = 0.0
                let mutable kAcc = 0.0
                for i in 0 .. musArr.Length - 1 do
                    let (_, yi, mui, ki, _) = musArr.[i]
                    let d = dens.[i]
                    muAcc <- muAcc + (if d <= 0.0 then 0.0 else yi * mui / d)
                    kAcc <- kAcc + (if d <= 0.0 then 0.0 else yi * ki / d)
                (muAcc, kAcc)
        let (zEff, hRes, cpRes) =
            if real then Virial.residual cn tK pPa else (z, 0.0, 0.0)
        let rho = pPa * m / (zEff * R * tK)
        let cpMass = (cpm + cpRes) / m
        { T = tK; P = pPa; M = m; Rho = rho
          Cp = cpMass; Mu = muMix; K = kMix
          Pr = cpMass * muMix / kMix
          H = (hm + hRes) / m }
    /// <summary>
    /// Computes a mixture state using the default ideal-gas approximation and the provided compressibility factor.
    /// </summary>
    /// <param name="rule">The mixing rule to use.</param>
    /// <param name="c">The composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <param name="pPa">Pressure in pascal.</param>
    /// <param name="z">Compressibility factor.</param>
    /// <returns>The evaluated mixture state.</returns>
    let mixWith (rule: MixingRule) (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        mixReal rule false c tK pPa z
    /// <summary>
    /// Evaluates the mixture state using the default Wilke mixture model.
    /// </summary>
    /// <param name="c">The gas composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <param name="pPa">Pressure in pascal.</param>
    /// <param name="z">Compressibility factor.</param>
    /// <returns>The mixture state at the provided conditions.</returns>
    let mix (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        let cn = normalize c
        let m = mixMolarMass cn
        let cpm = cn |> List.sumBy (fun (sp, y) -> y * cpMolar sp tK)
        let hm = cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)
        let mus = cn |> List.map (fun (sp, y) -> (sp, y, muPure sp tK, kPure sp tK, molarMass sp))
        // One denominator per species, shared by viscosity and conductivity (see mixReal).
        let musArr = List.toArray mus
        let dens =
            musArr
            |> Array.map (fun (_, _, mui, _, mi) ->
                mus |> List.sumBy (fun (_, yj, muj, _, mj) -> yj * phiWilke mi mj mui muj))
        let mutable muMix = 0.0
        let mutable kMix = 0.0
        for i in 0 .. musArr.Length - 1 do
            let (_, yi, mui, ki, _) = musArr.[i]
            let den = dens.[i]
            muMix <- muMix + (if den <= 0.0 then 0.0 else yi * mui / den)
            kMix <- kMix + (if den <= 0.0 then 0.0 else yi * ki / den)
        let rho = pPa * m / (z * R * tK)
        let cpMass = cpm / m
        { T = tK; P = pPa; M = m; Rho = rho
          Cp = cpMass; Mu = muMix; K = kMix
          Pr = cpMass * muMix / kMix
          H = hm / m }
    /// <summary>
    /// Computes the sensible mixture enthalpy at the given temperature.
    /// </summary>
    /// <param name="c">The gas composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The sensible enthalpy in J/kg.</returns>
    let enthalpy (c: Composition) (tK: float) =
        let cn = normalize c
        (cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)) / mixMolarMass cn
    /// <summary>
    /// Inverts the enthalpy equation to find the temperature corresponding to a target enthalpy.
    /// </summary>
    /// <param name="c">The gas composition.</param>
    /// <param name="h">The target enthalpy in J/kg.</param>
    /// <returns>The temperature in kelvin that matches the target enthalpy.</returns>
    let temperatureFromEnthalpy (c: Composition) (h: float) =
        bisect (fun t -> enthalpy c t - h) 250.0 2500.0 1e-4 200
    /// <summary>
    /// Computes the absolute mixture enthalpy including the formation term.
    /// </summary>
    /// <param name="c">The gas composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <returns>The absolute enthalpy in J/kg.</returns>
    let enthalpyAbs (c: Composition) (tK: float) =
        let cn = normalize c
        (cn |> List.sumBy (fun (sp, y) -> y * hMolarAbs sp tK)) / mixMolarMass cn
    /// <summary>
    /// Computes the absolute mixture enthalpy and optionally adds the real-gas departure correction.
    /// </summary>
    /// <param name="real">Whether to include the real-gas pressure correction.</param>
    /// <param name="c">The composition.</param>
    /// <param name="tK">Temperature in kelvin.</param>
    /// <param name="pPa">Pressure in pascal.</param>
    /// <returns>The absolute enthalpy in J/kg including the optional departure term.</returns>
    let enthalpyAbsReal (real: bool) (c: Composition) (tK: float) (pPa: float) =
        let cn = normalize c
        ((cn |> List.sumBy (fun (sp, y) -> y * hMolarAbs sp tK)) + departure real cn tK pPa)
        / mixMolarMass cn
    /// <summary>
    /// Absolute mixture enthalpy and its temperature derivative (the mixture cp) in a single pass, returned as J/kg and J/(kg·K).
    /// </summary>
    /// <remarks>
    /// The enthalpy is identical to <see cref="enthalpyAbsReal"/>. The virial residual already produces the pressure correction on cp, so the derivative is obtained at no extra cost, which lets the enthalpy inversion use a Newton iteration instead of a bisection.
    /// </remarks>
    let enthalpyAbsRealWithCp (real: bool) (c: Composition) (tK: float) (pPa: float) =
        let cn = normalize c
        let mutable hm = 0.0
        let mutable cpm = 0.0
        for (sp, y) in cn do
            hm <- hm + y * hMolarAbs sp tK
            cpm <- cpm + y * cpMolar sp tK
        let struct (hRes, cpRes) =
            if real then
                let (_, h, cp) = Virial.residual cn tK pPa
                struct (h, cp)
            else struct (0.0, 0.0)
        let m = mixMolarMass cn
        struct ((hm + hRes) / m, (cpm + cpRes) / m)
    /// <summary>
    /// Returns the mole fraction of a species within a composition.
    /// </summary>
    /// <param name="c">The composition.</param>
    /// <param name="sp">The species to look up.</param>
    /// <returns>The mole fraction, or zero if the species is not present.</returns>
    let molFrac (c: Composition) (sp: Species) =
        c |> List.tryFind (fun (s, _) -> s = sp) |> Option.map snd |> Option.defaultValue 0.0
    /// <summary>
    /// Estimates the gas emissivity contribution from water vapor and carbon dioxide.
    /// </summary>
    /// <param name="rH2O">Water-vapor mole fraction.</param>
    /// <param name="rCO2">Carbon-dioxide mole fraction.</param>
    /// <param name="pPa">Gas pressure in pascal.</param>
    /// <param name="sBeam">Beam length in meters.</param>
    /// <param name="tK">Gas temperature in kelvin.</param>
    /// <returns>The gas emissivity factor clipped to the physically meaningful range.</returns>
    let gasEmissivity (rH2O: float) (rCO2: float) (pPa: float) (sBeam: float) (tK: float) =
        let rn = rH2O + rCO2
        if rn <= 1e-6 || sBeam <= 0.0 then 0.0
        else
            let pnMPa = pPa * rn / 1.0e6
            let ps = max 1e-6 (pnMPa * sBeam)
            let kg =
                ((0.78 + 1.6 * rH2O) / sqrt ps - 0.1) * (1.0 - 0.37 * tK / 1000.0)
            let kg = max 0.0 kg
            let e = 1.0 - exp (-kg * ps)
            min 0.95 (max 0.0 e)
    /// <summary>
    /// Calculates the net radiative heat flux between a gas and a wall.
    /// </summary>
    /// <param name="epsGas">Gas emissivity.</param>
    /// <param name="epsWall">Wall emissivity.</param>
    /// <param name="tGasK">Gas temperature in kelvin.</param>
    /// <param name="tWallK">Wall temperature in kelvin.</param>
    /// <returns>The radiative heat-transfer rate in W/m².</returns>
    let hRadiation (epsGas: float) (epsWall: float) (tGasK: float) (tWallK: float) =
        if abs (tGasK - tWallK) < 1e-6 then 0.0
        else
            let effWall = 0.5 * (epsWall + 1.0)      // gray wall in a cavity
            let e = epsGas * effWall
            e * sigmaSB * (tGasK ** 4.0 - tWallK ** 4.0) / (tGasK - tWallK)




