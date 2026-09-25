namespace WhbThermo.Properties

open System
open WhbThermo.Domain
open WhbThermo.Steam

/// Truncated virial equation of state, Z = 1 + B P / (R T), for process-gas
/// mixtures at WHB conditions (up to a few tens of bar, well above the critical
/// temperatures of the components).
///
///   B_ii from the Pitzer corresponding-states correlation (Tsonopoulos form,
///        b0 = 0.083 - 0.422/Tr^1.6, b1 = 0.139 - 0.172/Tr^4.2), except for
///        species whose database record gives B from IAPWS-IF97 (water);
///   B_ij from pseudo-critical constants: Tc_ij = sqrt(Tci Tcj) (1 - k_ij),
///        omega_ij arithmetic mean, Zc_ij arithmetic mean, Vc_ij from the
///        cube-root rule, Pc_ij = Zc_ij R Tc_ij / Vc_ij;
///   B_mix = SUM_i SUM_j y_i y_j B_ij.
///
/// Residual enthalpy and cp follow from B(T) by central differences (2 K).
/// Moved here from Whb.Core; the arithmetic is kept term for term so the WHB
/// results do not move.
module SecondVirial =

    let private r = float Ru

    /// Pitzer second virial coefficient [m^3/mol] from Tc [K], Pc [Pa], omega.
    let pitzer (tc: float) (pc: float) (om: float) (tK: float) =
        let tr = max 0.30 (tK / tc)
        let b0 = 0.083 - 0.422 / Math.Pow(tr, 1.6)
        let b1 = 0.139 - 0.172 / Math.Pow(tr, 4.2)
        (b0 + om * b1) * r * tc / pc

    /// B [m^3/mol] from an IF97 specific volume [m^3/kg] at 1 kPa, where the
    /// gas is ideal to within B p / (R T), about 1e-6.
    let private bFromVolume (p: float) (v: float) (t: float) =
        let z = p * v / (Water.gasConstant () * 1000.0 * t)
        (z - 1.0) * r * t / p

    /// Lower and upper end of the region 2 to region 5 blend [K].
    let private blendLow = 1023.15
    let private blendHigh = 1123.15

    /// Second virial coefficient of water [m^3/mol] from IAPWS-IF97 in the
    /// dilute limit (1 kPa): region 2 up to 1023.15 K, region 5 from 1123.15 K,
    /// and between the two a quintic smoothstep blend.
    ///
    /// The blend is not cosmetic. The two equations differ by 0.7 % in B at
    /// their common boundary (1073.15 K), and the mixture residual cp is a
    /// second difference of B over 2 K: a hard switch would put a spike of about
    /// 100 J/(mol K) into cp_res at 35 bar, three times the gas cp itself. The
    /// quintic weight keeps B, dB/dT and d2B/dT2 continuous. Region 5 matters
    /// above 800 degC - WHB inlets, Claus furnaces - where B of water heads to
    /// zero and turns positive near 1600 K; the earlier (1073.15/T)^1.6
    /// extrapolation of region 2 kept it negative and was 61 % off at 1240 K.
    let waterB (tK: float) =
        let p = 1000.0                       // Pa: practically an ideal gas
        let b2 () =
            let (v, _, _, _) = Water.region2 (p / 1.0e6) tK
            bFromVolume p v tK
        let b5 () =
            let (v, _, _, _) = Water.region5 (p / 1.0e6) tK
            bFromVolume p v tK
        if tK <= blendLow then b2 ()
        elif tK >= blendHigh then b5 ()
        else
            let x = (tK - blendLow) / (blendHigh - blendLow)
            let w = x * x * x * (10.0 + x * (-15.0 + 6.0 * x))
            (1.0 - w) * b2 () + w * b5 ()

    /// A mixture component as the virial model needs it.
    type Component =
        { Id: string
          /// Tc [K], Pc [Pa], omega, Vc [m^3/mol]; None: the species stays ideal.
          Critical: (float * float * float * float) option
          /// B_ii from IAPWS-IF97 instead of the Pitzer correlation.
          If97SecondVirial: bool }

    /// Precomputed pseudo-critical data of one (i, j) pair, j >= i.
    [<Struct>]
    type PairTerm =
        { I: int
          J: int
          /// 1 on the diagonal, 2 off it (B_ij is symmetric).
          Mult: float
          Tc: float
          Pc: float
          Om: float
          IsWater: bool }

    /// Builds the pair terms of a component set. Temperature independent, so a
    /// caller evaluating the same set many times builds them once. `kij i j`
    /// gives the binary parameter of components i and j (0 for none).
    let buildPairTerms (components: Component[]) (kij: int -> int -> float) : PairTerm[] =
        let n = components.Length
        let acc = ResizeArray<PairTerm>(n * (n + 1) / 2)
        for i in 0 .. n - 1 do
            for j in i .. n - 1 do
                let a = components.[i]
                let b = components.[j]
                let mult = if i = j then 1.0 else 2.0
                if a.Id = b.Id then
                    if a.If97SecondVirial then
                        acc.Add { I = i; J = j; Mult = mult
                                  Tc = 0.0; Pc = 0.0; Om = 0.0; IsWater = true }
                    else
                        match a.Critical with
                        | Some (tc, pc, om, _) ->
                            acc.Add { I = i; J = j; Mult = mult
                                      Tc = tc; Pc = pc; Om = om; IsWater = false }
                        | None -> ()
                else
                    match a.Critical, b.Critical with
                    | Some (tca, pca, oma, vca), Some (tcb, pcb, omb, vcb) ->
                        let tcij = sqrt (tca * tcb) * (1.0 - kij i j)
                        let omij = 0.5 * (oma + omb)
                        let zca = pca * vca / (r * tca)
                        let zcb = pcb * vcb / (r * tcb)
                        let zcij = 0.5 * (zca + zcb)
                        let vcij = (0.5 * (Math.Cbrt vca + Math.Cbrt vcb)) ** 3.0
                        let pcij = zcij * r * tcij / vcij
                        acc.Add { I = i; J = j; Mult = mult
                                  Tc = tcij; Pc = pcij; Om = omij; IsWater = false }
                    | _ -> ()
        acc.ToArray()

    /// Mixture second virial coefficient [m^3/mol] at T [K] for mole fractions
    /// ys in the order the pair terms were built.
    let bMix (terms: PairTerm[]) (ys: float[]) (tK: float) =
        let mutable s = 0.0
        for t in terms do
            let b = if t.IsWater then waterB tK else pitzer t.Tc t.Pc t.Om tK
            s <- s + t.Mult * ys.[t.I] * ys.[t.J] * b
        s

    /// Compressibility factor, residual molar enthalpy [J/mol] and residual
    /// molar cp [J/(mol K)] at T [K] and P [Pa].
    let residual (terms: PairTerm[]) (ys: float[]) (tK: float) (pPa: float) =
        let dt = 2.0
        let bm = bMix terms ys tK
        let bp = bMix terms ys (tK + dt)
        let bmn = bMix terms ys (tK - dt)
        let db = (bp - bmn) / (2.0 * dt)
        let d2b = (bp - 2.0 * bm + bmn) / (dt * dt)
        let z = 1.0 + bm * pPa / (r * tK)
        let hRes = pPa * (bm - tK * db)
        let cpRes = -pPa * tK * d2b
        (z, hRes, cpRes)
