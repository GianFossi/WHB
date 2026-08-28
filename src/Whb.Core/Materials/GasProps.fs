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
    type Species =
        | H2 | N2 | O2 | CO | CO2 | CH4 | H2O | Ar | NH3
        | H2S | SO2 | COS | CS2 | S2 | S6 | S8
        | C2H4 | C2H6 | C3H6 | C3H8 | C2H2 | C6H6 | C7H8
        | NO | NO2 | N2O | SO3 | HCN | He
    let molarMass =
        function
        | H2 -> 0.00201588 | N2 -> 0.0280134 | O2 -> 0.0319988
        | CO -> 0.0280101  | CO2 -> 0.0440095 | CH4 -> 0.01604246
        | H2O -> 0.01801528 | Ar -> 0.039948 | NH3 -> 0.01703052
        | H2S -> 0.0340809  | SO2 -> 0.0640638 | COS -> 0.0600751
        | CS2 -> 0.0761407  | S2 -> 0.0641300  | S6 -> 0.1923900
        | S8 -> 0.2565200
        | C2H4 -> 0.0280532 | C2H6 -> 0.0300690 | C3H6 -> 0.0420797
        | C3H8 -> 0.0440956 | C2H2 -> 0.0260373 | C6H6 -> 0.0781118
        | C7H8 -> 0.0921384
        | NO -> 0.0300061   | NO2 -> 0.0460055  | N2O -> 0.0440128
        | SO3 -> 0.0800632  | HCN -> 0.0270253  | He -> 0.0040026
    let private cpCoef =
        function
        | H2  -> (3.249, 0.422e-3, 0.0,       0.083e5)
        | N2  -> (3.280, 0.593e-3, 0.0,       0.040e5)
        | O2  -> (3.639, 0.506e-3, 0.0,      -0.227e5)
        | CO  -> (3.376, 0.557e-3, 0.0,      -0.031e5)
        | CO2 -> (5.457, 1.045e-3, 0.0,      -1.157e5)
        | H2O -> (3.470, 1.450e-3, 0.0,       0.121e5)
        | CH4 -> (1.702, 9.081e-3, -2.164e-6, 0.0)
        | Ar  -> (2.500, 0.0,      0.0,       0.0)
        | NH3 -> (3.578, 3.020e-3, 0.0,      -0.186e5)
        | H2S -> (2.8292,  3.4632e-3, -7.9790e-7,  0.3097e5)
        | SO2 -> (4.9720,  2.3231e-3, -6.9826e-7, -0.8281e5)
        | COS -> (5.2290,  2.3095e-3, -6.4143e-7, -0.8217e5)
        | CS2 -> (5.8945,  1.7962e-3, -5.3079e-7, -0.8790e5)
        | S2  -> (4.2230,  0.2990e-3,  1.3711e-8, -0.3757e5)
        | S6  -> (15.6648, 0.0976e-3,  3.0526e-7, -1.8938e5)
        | S8  -> (17.0260, 9.8204e-3, -3.5717e-6, -0.5874e5)
        | C2H4 -> (2.1491, 13.208e-3, -3.9794e-6, -0.5990e5)
        | C2H6 -> (1.3824, 19.067e-3, -5.6588e-6, -0.3041e5)
        | C3H6 -> (2.9907, 20.848e-3, -6.3411e-6, -0.9125e5)
        | C3H8 -> (2.9504, 26.119e-3, -7.9093e-6, -1.2056e5)
        | C2H2 -> (5.2215,  3.8191e-3, -7.8326e-7, -0.9041e5)
        | C6H6 -> (5.3626, 30.687e-3, -1.0222e-5, -3.6521e5)
        | C7H8 -> (5.7614, 38.863e-3, -1.2779e-5, -3.7211e5)
        | NO  -> (2.9165,  1.5763e-3, -4.3377e-7,  0.2067e5)
        | NO2 -> (4.2347,  3.0756e-3, -8.9391e-7, -0.6420e5)
        | N2O -> (4.5227,  2.9955e-3, -8.2205e-7, -0.6832e5)
        | SO3 -> (6.7777,  3.6138e-3, -1.1137e-6, -1.6553e5)
        | HCN -> (3.9467,  2.7421e-3, -6.1536e-7, -0.3608e5)
        | He  -> (2.500,   0.0,        0.0,        0.0)
    let private sutherland =
        function
        | H2  -> (8.411e-6, 273.15, 97.0)
        | N2  -> (1.663e-5, 273.15, 107.0)
        | O2  -> (1.919e-5, 273.15, 139.0)
        | CO  -> (1.657e-5, 273.15, 136.0)
        | CO2 -> (1.370e-5, 273.15, 222.0)
        | CH4 -> (1.024e-5, 273.15, 164.0)
        | Ar  -> (2.125e-5, 273.15, 144.0)
        | NH3 -> (0.918e-5, 273.15, 370.0)
        | H2O -> (1.120e-5, 350.0,  1064.0)
        | H2S -> (12.40e-6, 293.15, 331.0)
        | SO2 -> (12.55e-6, 293.15, 416.0)
        | COS -> (12.00e-6, 293.15, 380.0)
        | CS2 -> ( 9.90e-6, 293.15, 450.0)
        | S2  -> (11.50e-6, 293.15, 500.0)
        | S6  -> (10.20e-6, 293.15, 600.0)
        | S8  -> ( 9.50e-6, 293.15, 650.0)
        | C2H4 -> (10.08e-6, 293.15, 226.0)
        | C2H6 -> ( 9.10e-6, 293.15, 252.0)
        | C3H6 -> ( 8.35e-6, 293.15, 290.0)
        | C3H8 -> ( 8.00e-6, 293.15, 310.0)
        | C2H2 -> (10.20e-6, 293.15, 210.0)
        | C6H6 -> ( 7.50e-6, 293.15, 380.0)
        | C7H8 -> ( 6.90e-6, 293.15, 410.0)
        | NO  -> (18.80e-6, 293.15, 128.0)
        | NO2 -> (14.10e-6, 293.15, 270.0)
        | N2O -> (14.60e-6, 293.15, 260.0)
        | SO3 -> (13.50e-6, 293.15, 450.0)
        | HCN -> (11.20e-6, 293.15, 280.0)
        | He  -> (19.60e-6, 293.15,  79.0)
    let cpMolar (sp: Species) (tK: float) =
        let (a, b, c, d) = cpCoef sp
        R * (a + b * tK + c * tK * tK + d / (tK * tK))
    let hForm =
        function
        | H2 -> 0.0 | N2 -> 0.0 | O2 -> 0.0 | Ar -> 0.0 | He -> 0.0
        | CO -> -110530.0 | CO2 -> -393510.0 | H2O -> -241826.0
        | CH4 -> -74850.0 | NH3 -> -45900.0
        | H2S -> -20600.0   | SO2 -> -296810.0 | COS -> -141700.0
        | CS2 -> 116700.0   | S2 -> 128600.0   | S6 -> 101315.0
        | S8 -> 101277.0
        | C2H4 -> 52500.0   | C2H6 -> -83852.0 | C3H6 -> 20000.0
        | C3H8 -> -104680.0 | C2H2 -> 228200.0 | C6H6 -> 82880.0
        | C7H8 -> 50170.0
        | NO -> 91271.0     | NO2 -> 34193.0   | N2O -> 81600.0
        | SO3 -> -395900.0  | HCN -> 133082.0
    let hMolar (sp: Species) (tK: float) =
        let (a, b, c, d) = cpCoef sp
        let t0 = 298.15
        R * (a * (tK - t0)
             + b / 2.0 * (tK * tK - t0 * t0)
             + c / 3.0 * (tK ** 3.0 - t0 ** 3.0)
             - d * (1.0 / tK - 1.0 / t0))
    let hMolarAbs (sp: Species) (tK: float) = hForm sp + hMolar sp tK
    let muPure (sp: Species) (tK: float) =
        match sp with
        | H2O -> Steam.viscosity tK 0.0      // limite di gas diluito IAPWS
        | _ ->
            let (mu0, t0, s) = sutherland sp
            mu0 * Math.Pow(tK / t0, 1.5) * (t0 + s) / (tK + s)
    let private sutherlandKOpt =
        function
        | H2S -> Some (13.0e-3, 450.0)
        | SO2 -> Some (8.6e-3, 480.0)
        | COS -> Some (10.5e-3, 420.0)
        | CS2 -> Some (7.5e-3, 510.0)
        | S2 -> Some (9.0e-3, 550.0)
        | S6 -> Some (6.8e-3, 650.0)
        | S8 -> Some (5.5e-3, 700.0)
        | C2H4 -> Some (17.5e-3, 350.0)
        | C2H6 -> Some (18.0e-3, 380.0)
        | C3H6 -> Some (15.2e-3, 400.0)
        | C3H8 -> Some (15.0e-3, 420.0)
        | C2H2 -> Some (19.5e-3, 320.0)
        | C6H6 -> Some (9.5e-3, 450.0)
        | C7H8 -> Some (9.0e-3, 470.0)
        | NO -> Some (23.8e-3, 160.0)
        | NO2 -> Some (13.0e-3, 350.0)
        | N2O -> Some (15.1e-3, 340.0)
        | SO3 -> Some (10.0e-3, 500.0)
        | HCN -> Some (16.5e-3, 360.0)
        | He -> Some (150.0e-3, 100.0)
        | _ -> None
    let kPure (sp: Species) (tK: float) =
        match sp with
        | H2O -> Steam.conductivity tK 0.0
        | _ ->
            match sutherlandKOpt sp with
            | Some (k0, sk) ->
                k0 * Math.Pow(tK / 273.15, 1.5) * (273.15 + sk) / (tK + sk)
            | None ->
                let mu = muPure sp tK
                let m = molarMass sp
                let cv = cpMolar sp tK - R
                mu / m * (1.32 * cv + 1.77 * R)
    type Composition = (Species * float) list
    let allSpecies =
        [ H2; N2; O2; CO; CO2; CH4; H2O; Ar; NH3
          H2S; SO2; COS; CS2; S2; S6; S8
          C2H4; C2H6; C3H6; C3H8; C2H2; C6H6; C7H8
          NO; NO2; N2O; SO3; HCN; He ]
    let speciesName (sp: Species) = sprintf "%A" sp
    let private speciesByUpperName =
        [ yield! allSpecies |> List.map (fun sp -> ((speciesName sp).ToUpperInvariant(), sp))
          "ARGON", Ar
          "HELIUM", He
          "ELIO", He
          "BENZENE", C6H6
          "BENZENE C6H6", C6H6
          "TOLUENE", C7H8 ] |> Map.ofList
    let tryParseSpecies (name: string) : Species option =
        if String.IsNullOrWhiteSpace name then None
        else speciesByUpperName.TryFind(name.Trim().ToUpperInvariant())
    let normalize (c: Composition) : Composition =
        let s = c |> List.sumBy snd
        if s <= 0.0 then failwith "Composizione nulla"
        elif abs (s - 1.0) < 1e-12 then c
        else c |> List.map (fun (k, v) -> (k, v / s))
    let mixMolarMass (c: Composition) =
        c |> List.sumBy (fun (sp, y) -> y * molarMass sp)
    module Virial =

        let criticalOpt =
            function
            | H2  -> Some (33.19, 13.13e5, -0.216, 64.1e-6)
            | N2  -> Some (126.20, 33.98e5, 0.037, 89.2e-6)
            | O2  -> Some (154.58, 50.43e5, 0.022, 73.4e-6)
            | CO  -> Some (132.85, 34.94e5, 0.045, 93.1e-6)
            | CO2 -> Some (304.12, 73.74e5, 0.225, 94.07e-6)
            | CH4 -> Some (190.56, 45.99e5, 0.011, 98.6e-6)
            | H2O -> Some (647.10, 220.64e5, 0.345, 55.95e-6)
            | Ar  -> Some (150.86, 48.98e5, 0.000, 74.57e-6)
            | NH3 -> Some (405.50, 113.59e5, 0.253, 72.47e-6)
            | H2S -> Some (373.40, 89.63e5, 0.090, 98.5e-6)
            | SO2 -> Some (430.80, 78.84e5, 0.245, 122.0e-6)
            | COS -> Some (378.80, 63.49e5, 0.111, 137.0e-6)
            | CS2 -> Some (552.00, 79.00e5, 0.111, 173.0e-6)
            | C2H4 -> Some (282.34, 50.41e5, 0.087, 131.1e-6)
            | C2H6 -> Some (305.32, 48.72e5, 0.099, 145.5e-6)
            | C3H6 -> Some (364.90, 46.00e5, 0.142, 184.6e-6)
            | C3H8 -> Some (369.83, 42.48e5, 0.152, 200.0e-6)
            | C2H2 -> Some (308.30, 61.14e5, 0.187, 112.2e-6)
            | C6H6 -> Some (562.05, 48.95e5, 0.210, 256.0e-6)
            | C7H8 -> Some (591.75, 41.08e5, 0.264, 316.0e-6)
            | NO  -> Some (180.15, 64.80e5, 0.582, 58.0e-6)
            | NO2 -> Some (431.35, 101.32e5, 0.849, 167.8e-6)
            | N2O -> Some (309.57, 72.45e5, 0.141, 97.4e-6)
            | SO3 -> Some (490.85, 82.10e5, 0.424, 127.3e-6)
            | HCN -> Some (456.65, 53.90e5, 0.410, 139.0e-6)
            | He  -> Some (5.19, 2.27e5, -0.390, 57.3e-6)
            | S2 | S6 | S8 -> None
        let critical sp =
            match criticalOpt sp with
            | Some c -> c
            | None -> failwithf "Nessun dato critico per %A" sp

        let pitzer (tc: float) (pc: float) (om: float) (tK: float) =
            let tr = max 0.30 (tK / tc)
            let b0 = 0.083 - 0.422 / Math.Pow(tr, 1.6)
            let b1 = 0.139 - 0.172 / Math.Pow(tr, 4.2)
            (b0 + om * b1) * R * tc / pc

        let bWater (tK: float) =
            let p = 1000.0                       // Pa: gas praticamente ideale
            let t = min tK 1073.15
            let (v, _, _, _) = Steam.region2 (p / 1.0e6) t   // v [m³/kg]
            let z = p * v / (Rw * 1000.0 * t)
            let b = (z - 1.0) * R * t / p
            if tK <= 1073.15 then b else b * Math.Pow(1073.15 / tK, 1.6)
        /// Pseudo-critical constants of one (i, j) interaction pair. They depend only
        /// on the two species, never on temperature or composition, so they are built
        /// once per species set and reused for every bMix evaluation.
        [<Struct>]
        type private PairTerm =
            { I: int
              J: int
              Mult: float          // 1 on the diagonal, 2 off-diagonal (bPair is symmetric)
              Tc: float
              Pc: float
              Om: float
              IsWater: bool }
        let private speciesIndex =
            function
            | H2 -> 0 | N2 -> 1 | O2 -> 2 | CO -> 3 | CO2 -> 4
            | CH4 -> 5 | H2O -> 6 | Ar -> 7 | NH3 -> 8
            | H2S -> 9 | SO2 -> 10 | COS -> 11 | CS2 -> 12 | S2 -> 13 | S6 -> 14 | S8 -> 15
            | C2H4 -> 16 | C2H6 -> 17 | C3H6 -> 18 | C3H8 -> 19 | C2H2 -> 20 | C6H6 -> 21 | C7H8 -> 22
            | NO -> 23 | NO2 -> 24 | N2O -> 25 | SO3 -> 26 | HCN -> 27 | He -> 28
        let private buildPairTerms (species: Species[]) =
            let n = species.Length
            let acc = ResizeArray<PairTerm>(n * (n + 1) / 2)
            for i in 0 .. n - 1 do
                for j in i .. n - 1 do
                    let a = species.[i]
                    let b = species.[j]
                    let mult = if i = j then 1.0 else 2.0
                    if a = b then
                        if a = H2O then
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
                            let tcij = sqrt (tca * tcb)
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
        let private pairTermCache =
            Collections.Concurrent.ConcurrentDictionary<int64, PairTerm[]>()
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
    let departure (real: bool) (c: Composition) (tK: float) (pPa: float) =
        if not real then 0.0
        else let (_, h, _) = Virial.residual c tK pPa in h
    let private phiWilke (mi: float) (mj: float) (mui: float) (muj: float) =
        let a = 1.0 + sqrt (mui / muj) * Math.Pow(mj / mi, 0.25)
        a * a / sqrt (8.0 * (1.0 + mi / mj))
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
    type MixingRule =
        | Wilke
        | MolarAverage
    let mixingRuleName = function
        | Wilke -> "Wilke (µ) / Wassiljewa-Mason-Saxena (k)"
        | MolarAverage -> "media molare (per confronto con datasheet)"
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
    let mixWith (rule: MixingRule) (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        mixReal rule false c tK pPa z
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
    let enthalpy (c: Composition) (tK: float) =
        let cn = normalize c
        (cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)) / mixMolarMass cn
    let temperatureFromEnthalpy (c: Composition) (h: float) =
        bisect (fun t -> enthalpy c t - h) 250.0 2500.0 1e-4 200
    let enthalpyAbs (c: Composition) (tK: float) =
        let cn = normalize c
        (cn |> List.sumBy (fun (sp, y) -> y * hMolarAbs sp tK)) / mixMolarMass cn
    let enthalpyAbsReal (real: bool) (c: Composition) (tK: float) (pPa: float) =
        let cn = normalize c
        ((cn |> List.sumBy (fun (sp, y) -> y * hMolarAbs sp tK)) + departure real cn tK pPa)
        / mixMolarMass cn
    /// <summary>
    /// Absolute mixture enthalpy and its temperature derivative (the mixture cp) in a
    /// single pass, returned as J/kg and J/(kg·K).
    /// </summary>
    /// <remarks>
    /// The enthalpy is identical to <see cref="enthalpyAbsReal"/>. The virial residual
    /// already produces the pressure correction on cp, so the derivative is obtained at
    /// no extra cost, which lets the enthalpy inversion use a Newton iteration instead
    /// of a bisection.
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
    let molFrac (c: Composition) (sp: Species) =
        c |> List.tryFind (fun (s, _) -> s = sp) |> Option.map snd |> Option.defaultValue 0.0
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
    let hRadiation (epsGas: float) (epsWall: float) (tGasK: float) (tWallK: float) =
        if abs (tGasK - tWallK) < 1e-6 then 0.0
        else
            let effWall = 0.5 * (epsWall + 1.0)      // gray wall in a cavity
            let e = epsGas * effWall
            e * sigmaSB * (tGasK ** 4.0 - tWallK ** 4.0) / (tGasK - tWallK)




