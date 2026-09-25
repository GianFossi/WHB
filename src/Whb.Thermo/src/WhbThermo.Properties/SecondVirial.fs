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

    /// Second virial coefficient of water [m^3/mol] from IAPWS-IF97 region 2 in
    /// the dilute limit (1 kPa). Above 1073.15 K, the region-2 limit, it is
    /// scaled as (1073.15/T)^1.6.
    let waterB (tK: float) =
        let p = 1000.0                       // Pa: practically an ideal gas
        let t = min tK 1073.15
        let (v, _, _, _) = Water.region2 (p / 1.0e6) t   // v [m^3/kg]
        let z = p * v / (Water.gasConstant () * 1000.0 * t)
        let b = (z - 1.0) * r * t / p
        if tK <= 1073.15 then b else b * Math.Pow(1073.15 / tK, 1.6)

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
