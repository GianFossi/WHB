namespace WhbThermo.Steam

open System
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// IAPWS-IF97: Revised Release on the IAPWS Industrial Formulation 1997 for the
/// Thermodynamic Properties of Water and Steam (August 2007).
///
/// Regions implemented:
///   1  compressed liquid   273.15-623.15 K, up to 100 MPa
///   2  superheated steam   273.15-1073.15 K, up to 100 MPa
///   4  saturation line     273.15-647.096 K
///
/// Region 3 (near-critical, above 623.15 K / 16.53 MPa) and region 5 (above
/// 1073 K) are NOT implemented: a WHB steam drum at 40-120 bar never enters
/// them. Requests that fall there fail explicitly rather than extrapolating.
///
/// Why IF97 rather than the empirical fits in most vendor manuals: it is the
/// international industrial standard, it is thermodynamically consistent across
/// the saturation line (the property fits are not), and it ships with published
/// verification values, so an implementation is either provably right or wrong.
/// tools/verify_if97.py reproduces those values to 1e-9.
module If97 =

    [<Literal>]
    let DefaultFile = "iapws-if97.json"

    // ---------- data model ----------

    [<CLIMutable>]
    type private RangeDto = { tMinK: float; tMaxK: float; pMaxMPa: float option }

    [<CLIMutable>]
    type private ConstantsDto =
        { r_kJ_kgK: float; tc_K: float; pc_MPa: float; rhoc_kg_m3: float
          tt_K: float; pt_MPa: float }

    [<CLIMutable>]
    type private Region1Dto =
        { piStar_MPa: float; tauStar_K: float; range: RangeDto
          i: int[]; j: int[]; n: float[] }

    [<CLIMutable>]
    type private IdealDto = { j: int[]; n: float[] }

    [<CLIMutable>]
    type private ResidualDto = { i: int[]; j: int[]; n: float[] }

    [<CLIMutable>]
    type private Region2Dto =
        { piStar_MPa: float; tauStar_K: float; range: RangeDto
          ideal: IdealDto; residual: ResidualDto }

    [<CLIMutable>]
    type private Region4Dto = { range: RangeDto; n: float[] }

    [<CLIMutable>]
    type private Region3Dto = { n1: float; i: int[]; j: int[]; n: float[] }

    [<CLIMutable>]
    type private Region5Dto =
        { piStar_MPa: float; tauStar_K: float; range: RangeDto
          ideal: IdealDto; residual: ResidualDto }

    [<CLIMutable>]
    type private RootDto =
        { standard: string; source: string; constants: ConstantsDto
          region1: Region1Dto; region2: Region2Dto; region3: Region3Dto
          region4: Region4Dto; region5: Region5Dto }

    type Model =
        internal
            { R        : float
              Tc       : float<K>
              Pc       : float<bar>
              R1       : {| PiStar: float; TauStar: float
                            TMin: float; TMax: float; PMax: float
                            I: int[]; J: int[]; N: float[]
                            IMin: int; IMax: int; JMin: int; JMax: int |}
              RhoC     : float
              R2       : {| PiStar: float; TauStar: float
                            TMin: float; TMax: float; PMax: float
                            IdealJ: int[]; IdealN: float[]
                            I: int[]; J: int[]; N: float[]
                            /// Exponent bounds, fixed at load so the kernel can
                            /// build each distinct power once per call.
                            IdealJMin: int; IdealJMax: int; IMax: int; JMax: int |}
              R3       : {| N1: float; I: int[]; J: int[]; N: float[] |}
              R4       : {| TMin: float; TMax: float; N: float[] |}
              R5       : {| PiStar: float; TauStar: float
                            TMin: float; TMax: float; PMax: float
                            IdealJ: int[]; IdealN: float[]
                            I: int[]; J: int[]; N: float[] |}
              Standard : string
              Source   : string }

    /// State of water or steam. Pressures in MPa internally, per the standard.
    type SteamState =
        { Region       : int
          Temperature  : float<K>
          Pressure     : float<bar>
          SpecificVolume : float      // m^3/kg
          Density      : float<kg/m^3>
          Enthalpy     : float        // kJ/kg
          Entropy      : float        // kJ/(kg*K)
          Cp           : float        // kJ/(kg*K)
          Cv           : float }      // kJ/(kg*K)

    // ---------- loading ----------

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        // Optional fields are absent or null in parts of the data file; both read as None.
        o.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions.Default()
                    .WithSkippableOptionFields(SkippableOptionFields.Always,
                                               deserializeNullAsNone = true)))
        o

    let parse (json: string) : Thermo<Model> =
        try
            let r = JsonSerializer.Deserialize<RootDto>(json, options)
            let check name (a: int[]) (b: int[]) (n: float[]) =
                if a.Length <> n.Length || b.Length <> n.Length then
                    Some (DatabaseParseError
                            ($"IF97 {name}: coefficient arrays of unequal length "
                             + $"(I={a.Length}, J={b.Length}, n={n.Length})"))
                else None

            match check "region1" r.region1.i r.region1.j r.region1.n,
                  check "region2 residual" r.region2.residual.i r.region2.residual.j r.region2.residual.n with
            | Some e, _ | _, Some e -> fail e
            | None, None ->
                // Term counts are fixed by the standard; a wrong count means the
                // file is not IF97 whatever it claims.
                if r.region1.n.Length <> 34 then
                    fail (DatabaseParseError $"IF97 region 1 has {r.region1.n.Length} terms, standard has 34")
                elif r.region2.residual.n.Length <> 43 then
                    fail (DatabaseParseError $"IF97 region 2 residual has {r.region2.residual.n.Length} terms, standard has 43")
                elif r.region2.ideal.n.Length <> 9 then
                    fail (DatabaseParseError $"IF97 region 2 ideal has {r.region2.ideal.n.Length} terms, standard has 9")
                elif r.region4.n.Length <> 10 then
                    fail (DatabaseParseError $"IF97 region 4 has {r.region4.n.Length} coefficients, standard has 10")
                elif r.constants.tc_K <> PhysicalConstants.Water.CriticalTemperature
                     || r.constants.rhoc_kg_m3 <> PhysicalConstants.Water.CriticalDensity
                     || abs (r.constants.pc_MPa * 1.0e6 / PhysicalConstants.Water.CriticalPressure - 1.0) > 1e-12
                     || abs (r.constants.r_kJ_kgK * 1000.0 / PhysicalConstants.Water.SpecificGasConstant - 1.0) > 1e-12 then
                    fail (DatabaseParseError
                            "IF97 constants differ from PhysicalConstants.Water; the two must be one set")
                elif r.region1.range.pMaxMPa.IsNone || r.region2.range.pMaxMPa.IsNone then
                    fail (DatabaseParseError "IF97 regions 1 and 2 need range.pMaxMPa")
                elif r.region3.n.Length <> 39 || r.region3.i.Length <> 39 || r.region3.j.Length <> 39 then
                    fail (DatabaseParseError $"IF97 region 3 has {r.region3.n.Length} terms besides n1, standard has 39")
                elif r.region5.ideal.n.Length <> 6 || r.region5.residual.n.Length <> 6 then
                    fail (DatabaseParseError "IF97 region 5 has 6 ideal and 6 residual terms")
                elif r.region5.range.pMaxMPa.IsNone then
                    fail (DatabaseParseError "IF97 region 5 needs range.pMaxMPa")
                elif (r.region2.residual.i |> Array.min) < 1 || (r.region2.residual.j |> Array.min) < 0 then
                    fail (DatabaseParseError "IF97 region 2 residual exponents must be I >= 1, J >= 0")
                else
                    ok { R = r.constants.r_kJ_kgK
                         Tc = r.constants.tc_K * 1.0<K>
                         Pc = r.constants.pc_MPa * 10.0<bar>
                         RhoC = r.constants.rhoc_kg_m3
                         R1 = {| PiStar = r.region1.piStar_MPa; TauStar = r.region1.tauStar_K
                                 TMin = r.region1.range.tMinK; TMax = r.region1.range.tMaxK
                                 PMax = r.region1.range.pMaxMPa.Value
                                 I = r.region1.i; J = r.region1.j; N = r.region1.n
                                 IMin = Array.min r.region1.i; IMax = Array.max r.region1.i
                                 JMin = Array.min r.region1.j; JMax = Array.max r.region1.j |}
                         R2 = {| PiStar = r.region2.piStar_MPa; TauStar = r.region2.tauStar_K
                                 TMin = r.region2.range.tMinK; TMax = r.region2.range.tMaxK
                                 PMax = r.region2.range.pMaxMPa.Value
                                 IdealJ = r.region2.ideal.j; IdealN = r.region2.ideal.n
                                 I = r.region2.residual.i; J = r.region2.residual.j
                                 N = r.region2.residual.n
                                 IdealJMin = Array.min r.region2.ideal.j
                                 IdealJMax = Array.max r.region2.ideal.j
                                 IMax = Array.max r.region2.residual.i
                                 JMax = Array.max r.region2.residual.j |}
                         R3 = {| N1 = r.region3.n1; I = r.region3.i; J = r.region3.j; N = r.region3.n |}
                         R4 = {| TMin = r.region4.range.tMinK; TMax = r.region4.range.tMaxK
                                 N = r.region4.n |}
                         R5 = {| PiStar = r.region5.piStar_MPa; TauStar = r.region5.tauStar_K
                                 TMin = r.region5.range.tMinK; TMax = r.region5.range.tMaxK
                                 PMax = r.region5.range.pMaxMPa.Value
                                 IdealJ = r.region5.ideal.j; IdealN = r.region5.ideal.n
                                 I = r.region5.residual.i; J = r.region5.residual.j
                                 N = r.region5.residual.n |}
                         Standard = r.standard
                         Source = r.source }
        with ex ->
            fail (DatabaseParseError ex.Message)

    let load () : Thermo<Model> = DataStore.load DefaultFile parse
    let loadFile (path: string) : Thermo<Model> = DataStore.load path parse

    // ---------- region 4: saturation line ----------

    /// Saturation pressure [MPa] at temperature T [K]. IF97 equation 30.
    let internal psatMPa (m: Model) (tK: float) =
        let n = m.R4.N
        let theta = tK + n.[8] / (tK - n.[9])
        let a = theta * theta + n.[0] * theta + n.[1]
        let b = n.[2] * theta * theta + n.[3] * theta + n.[4]
        let c = n.[5] * theta * theta + n.[6] * theta + n.[7]
        let x = 2.0 * c / (-b + sqrt (b * b - 4.0 * a * c))
        x ** 4.0

    /// Saturation temperature [K] at pressure P [MPa]. IF97 equation 31.
    let internal tsatK (m: Model) (pMPa: float) =
        let n = m.R4.N
        let beta = pMPa ** 0.25
        let e = beta * beta + n.[2] * beta + n.[5]
        let f = n.[0] * beta * beta + n.[3] * beta + n.[6]
        let g = n.[1] * beta * beta + n.[4] * beta + n.[7]
        let d = 2.0 * g / (-f - sqrt (f * f - 4.0 * e * g))
        (n.[9] + d - sqrt ((n.[9] + d) * (n.[9] + d) - 4.0 * (n.[8] + n.[9] * d))) / 2.0

    /// Saturation pressure at a given temperature.
    let saturationPressure (m: Model) (t: float<K>) : Thermo<float<bar>> =
        let tK = float t
        if tK < m.R4.TMin || tK > m.R4.TMax then
            fail (OutsideFitRange ("IF97 region 4", "Psat", tK, m.R4.TMin, m.R4.TMax))
        else
            ok (psatMPa m tK * 10.0<bar>)

    /// Saturation temperature at a given pressure. This is the shell-side
    /// boiling temperature of a WHB, so it is the single most used call here.
    let saturationTemperature (m: Model) (p: float<bar>) : Thermo<float<K>> =
        let pMPa = float p / 10.0
        let pMin = psatMPa m m.R4.TMin
        let pMax = float m.Pc / 10.0
        if pMPa < pMin || pMPa > pMax then
            fail (CorrelationExtrapolated
                    ("IF97 region 4",
                     $"P = %.4f{pMPa} MPa outside %.6f{pMin}-%.3f{pMax} MPa"))
        else
            ok (tsatK m pMPa * 1.0<K>)

    // ---------- region 1: compressed liquid ----------

    /// Region 1 kernel. Terms are summed in table order, each distinct power built
    /// once with Math.Pow, and v, h, cp, s are assembled in the order Whb.Core used
    /// before the migration, so the two agree to the last bit. The pi-pi and
    /// pi-tau sums, for cv, cost only multiplications.
    let internal region1 (m: Model) (tK: float) (pMPa: float) =
        let pi = pMPa / m.R1.PiStar
        let tau = m.R1.TauStar / tK
        let a = 7.1 - pi
        let b = tau - 1.222
        let mutable g = 0.0
        let mutable gp = 0.0
        let mutable gt = 0.0
        let mutable gtt = 0.0
        let mutable gpp = 0.0
        let mutable gpt = 0.0
        // Every distinct power a^e (e = I-2 .. I) and b^e (e = J-2 .. J) once:
        // the same Math.Pow values as per-term evaluation, about half the calls,
        // and the cv sums then cost only multiplications.
        let aLo = m.R1.IMin - 2
        let bLo = m.R1.JMin - 2
        let aPow = Array.init (m.R1.IMax - aLo + 1) (fun e -> Math.Pow(a, float (e + aLo)))
        let bPow = Array.init (m.R1.JMax - bLo + 1) (fun e -> Math.Pow(b, float (e + bLo)))
        for k in 0 .. m.R1.N.Length - 1 do
            let i = m.R1.I.[k]
            let j = m.R1.J.[k]
            let fi = float i
            let fj = float j
            let n = m.R1.N.[k]
            let ai = aPow.[i - aLo]
            let bj = bPow.[j - bLo]
            let ai1 = aPow.[i - 1 - aLo]
            let bj1 = bPow.[j - 1 - bLo]
            g <- g + n * ai * bj
            gp <- gp - n * fi * ai1 * bj
            gt <- gt + n * ai * fj * bj1
            gtt <- gtt + n * ai * fj * (fj - 1.0) * bPow.[j - 2 - bLo]
            gpp <- gpp + n * fi * (fi - 1.0) * aPow.[i - 2 - aLo] * bj
            gpt <- gpt - n * fi * ai1 * fj * bj1
        let r = m.R
        {| V = pi * gp * r * tK / (pMPa * 1000.0)
           H = tau * gt * r * tK
           S = (tau * gt - g) * r
           Cp = -tau * tau * gtt * r
           Cv = r * (-tau * tau * gtt + (gp - tau * gpt) ** 2.0 / gpp) |}

    // ---------- region 2: superheated steam ----------

    /// Region 2 kernel. Each distinct power of tau, pi and (tau - 0.5) is built
    /// once per call with Math.Pow (the same values as per-term evaluation, at a
    /// fraction of the cost: this function sits inside the virial residual of
    /// the WHB solver). Assembly order as in Whb.Core, for bit-identity.
    let internal region2 (m: Model) (tK: float) (pMPa: float) =
        let r2 = m.R2
        let pi = pMPa / r2.PiStar
        let tau = r2.TauStar / tK
        let jLo = r2.IdealJMin - 2
        let tauPow = Array.init (r2.IdealJMax - jLo + 1) (fun k -> Math.Pow(tau, float (k + jLo)))
        let mutable go = log pi
        let mutable got = 0.0
        let mutable gott = 0.0
        for k in 0 .. r2.IdealN.Length - 1 do
            let jj = r2.IdealJ.[k]
            let j = float jj
            let n = r2.IdealN.[k]
            go <- go + n * tauPow.[jj - jLo]
            got <- got + n * j * tauPow.[jj - 1 - jLo]
            gott <- gott + n * j * (j - 1.0) * tauPow.[jj - 2 - jLo]
        let gop = 1.0 / pi
        let b = tau - 0.5
        // pi^-1 .. pi^IMax: the -1 entry serves the cv sum of the I = 1 terms.
        let piPow = Array.init (r2.IMax + 2) (fun k -> Math.Pow(pi, float (k - 1)))
        let bPow = Array.init (r2.JMax + 3) (fun k -> Math.Pow(b, float k - 2.0))   // b^-2 .. b^JMax
        let mutable gr = 0.0
        let mutable grp = 0.0
        let mutable grt = 0.0
        let mutable grtt = 0.0
        let mutable grpp = 0.0
        let mutable grpt = 0.0
        for k in 0 .. r2.N.Length - 1 do
            let i = r2.I.[k]
            let j = r2.J.[k]
            let fi = float i
            let fj = float j
            let n = r2.N.[k]
            let pii = piPow.[i + 1]
            let bj = bPow.[j + 2]
            gr <- gr + n * pii * bj
            grp <- grp + n * fi * piPow.[i] * bj
            grt <- grt + n * pii * fj * bPow.[j + 1]
            grtt <- grtt + n * pii * fj * (fj - 1.0) * bPow.[j]
            grpp <- grpp + n * fi * (fi - 1.0) * piPow.[i - 1] * bj
            grpt <- grpt + n * fi * piPow.[i] * fj * bPow.[j + 1]
        let r = m.R
        {| V = pi * (gop + grp) * r * tK / (pMPa * 1000.0)
           H = tau * (got + grt) * r * tK
           S = (tau * (got + grt) - (go + gr)) * r
           Cp = -tau * tau * (gott + grtt) * r
           Cv = r * (-tau * tau * (gott + grtt)
                     - (1.0 + pi * grp - tau * pi * grpt) ** 2.0
                       / (1.0 - pi * pi * grpp)) |}

    // ---------- region 3: near-critical ----------

    /// Region 3 kernel at density [kg/m^3] and temperature [K] (IF97 equation 28):
    /// pressure [MPa] and specific properties in kJ units.
    let internal region3 (m: Model) (rho: float) (tK: float) =
        let d = rho / m.RhoC
        let tau = float m.Tc / tK
        let mutable phi = m.R3.N1 * log d
        let mutable pd = m.R3.N1 / d
        let mutable pdd = -m.R3.N1 / (d * d)
        let mutable pt = 0.0
        let mutable ptt = 0.0
        let mutable pdt = 0.0
        for k in 0 .. m.R3.N.Length - 1 do
            let i = float m.R3.I.[k]
            let j = float m.R3.J.[k]
            let n = m.R3.N.[k]
            let di = Math.Pow(d, i)
            let tj = Math.Pow(tau, j)
            let di1 = Math.Pow(d, i - 1.0)
            let tj1 = Math.Pow(tau, j - 1.0)
            phi <- phi + n * di * tj
            pd <- pd + n * i * di1 * tj
            pdd <- pdd + n * i * (i - 1.0) * Math.Pow(d, i - 2.0) * tj
            pt <- pt + n * j * di * tj1
            ptt <- ptt + n * j * (j - 1.0) * di * Math.Pow(tau, j - 2.0)
            pdt <- pdt + n * i * j * di1 * tj1
        let r = m.R
        {| P = d * pd * r * tK * rho / 1000.0
           V = 1.0 / rho
           H = r * tK * (tau * pt + d * pd)
           S = r * (tau * pt - phi)
           Cp = r * (-tau * tau * ptt + (d * pd - d * tau * pdt) ** 2.0 / (2.0 * d * pd + d * d * pdd))
           Cv = -r * tau * tau * ptt |}

    /// Saturated liquid density [kg/m^3], IAPWS SR1-86(1992) auxiliary equation.
    /// Accurate to about 0.1 % away from the critical point: used here as the
    /// starting point of the region 3 density solve, and by Water.Auxiliary.
    let internal auxLiquidDensity (tc: float) (rhoc: float) (tK: float) =
        let th = 1.0 - tK / tc
        if th <= 0.0 then rhoc
        else
            rhoc *
            (1.0
             + 1.99274064 * Math.Pow(th, 1.0 / 3.0)
             + 1.09965342 * Math.Pow(th, 2.0 / 3.0)
             - 0.510839303 * Math.Pow(th, 5.0 / 3.0)
             - 1.75493479 * Math.Pow(th, 16.0 / 3.0)
             - 45.5170352 * Math.Pow(th, 43.0 / 3.0)
             - 6.74694450e5 * Math.Pow(th, 110.0 / 3.0))

    /// Saturated vapour density [kg/m^3], IAPWS SR1-86(1992) auxiliary equation.
    let internal auxVapourDensity (tc: float) (rhoc: float) (tK: float) =
        let th = 1.0 - tK / tc
        if th <= 0.0 then rhoc
        else
            rhoc *
            exp (-2.03150240 * Math.Pow(th, 2.0 / 6.0)
                 - 2.68302940 * Math.Pow(th, 4.0 / 6.0)
                 - 5.38626492 * Math.Pow(th, 8.0 / 6.0)
                 - 17.2991605 * Math.Pow(th, 18.0 / 6.0)
                 - 44.7586581 * Math.Pow(th, 37.0 / 6.0)
                 - 63.9201063 * Math.Pow(th, 71.0 / 6.0))

    /// Which root of p(rho, T) = P a region 3 state is on.
    type internal Branch =
        | Liquid
        | Vapour
        | Supercritical

    /// Density [kg/m^3] with p(rho, T) = P [MPa] on the requested branch of the
    /// region 3 equation. Below the critical temperature p(rho) has a van der
    /// Waals loop, so the root is bracketed from the SR1-86 saturation density of
    /// the branch, stepping away from it until the pressure changes sign, which
    /// keeps the search on the stable side of the loop; then bisected to machine
    /// precision. Above Tc p(rho) is monotone and the bracket is simply wide.
    let internal region3Density (m: Model) (branch: Branch) (pMPa: float) (tK: float) : Thermo<float> =
        let f rho = (region3 m rho tK).P - pMPa
        let bisect lo hi =
            let mutable a = lo
            let mutable b = hi
            let mutable fa = f a
            let mutable i = 0
            while i < 200 && (b - a) > 1e-13 * b do
                let mid = 0.5 * (a + b)
                let fm = f mid
                if (fm > 0.0) = (fa > 0.0) then
                    a <- mid
                    fa <- fm
                else b <- mid
                i <- i + 1
            0.5 * (a + b)
        let tc = float m.Tc
        let bracketFrom (guess: float) =
            // f rises with rho on both stable branches.
            let step = 1.002
            let mutable lo = guess
            let mutable hi = guess
            let mutable n = 0
            if f guess < 0.0 then
                while f hi < 0.0 && n < 3000 do
                    hi <- hi * step
                    n <- n + 1
                lo <- hi / step
            else
                while f lo > 0.0 && n < 3000 do
                    lo <- lo / step
                    n <- n + 1
                hi <- lo * step
            if n >= 3000 then None else Some (lo, hi)
        let solved =
            match branch with
            | Supercritical ->
                // Above Tc p(rho) is monotone over the region; start from the
                // critical density rather than a wide bracket, because the
                // equation extrapolated far outside region 3 is not physical.
                bracketFrom m.RhoC |> Option.map (fun (lo, hi) -> bisect lo hi)
            | Liquid ->
                bracketFrom (auxLiquidDensity tc m.RhoC tK) |> Option.map (fun (lo, hi) -> bisect lo hi)
            | Vapour ->
                bracketFrom (auxVapourDensity tc m.RhoC tK) |> Option.map (fun (lo, hi) -> bisect lo hi)
        match solved with
        | Some rho when branch = Liquid && rho < m.RhoC ->
            fail (CorrelationExtrapolated ("IF97 region 3", $"T = %.3f{tK} K is too close to the critical point to separate the liquid root"))
        | Some rho when branch = Vapour && rho > m.RhoC ->
            fail (CorrelationExtrapolated ("IF97 region 3", $"T = %.3f{tK} K is too close to the critical point to separate the vapour root"))
        | Some rho -> ok rho
        | None ->
            fail (CorrelationExtrapolated ("IF97 region 3", $"no density found for P = %.4f{pMPa} MPa, T = %.3f{tK} K"))

    // ---------- region 5: high-temperature steam ----------

    /// Region 5 kernel (IF97 equation 32), same outputs as region 2.
    let internal region5 (m: Model) (tK: float) (pMPa: float) =
        let r5 = m.R5
        let pi = pMPa / r5.PiStar
        let tau = r5.TauStar / tK
        let mutable go = log pi
        let mutable got = 0.0
        let mutable gott = 0.0
        for k in 0 .. r5.IdealN.Length - 1 do
            let j = float r5.IdealJ.[k]
            let n = r5.IdealN.[k]
            go <- go + n * Math.Pow(tau, j)
            got <- got + n * j * Math.Pow(tau, j - 1.0)
            gott <- gott + n * j * (j - 1.0) * Math.Pow(tau, j - 2.0)
        let gop = 1.0 / pi
        let gopp = -1.0 / (pi * pi)
        let mutable gr = 0.0
        let mutable grp = 0.0
        let mutable grt = 0.0
        let mutable grpp = 0.0
        let mutable grtt = 0.0
        let mutable grpt = 0.0
        for k in 0 .. r5.N.Length - 1 do
            let i = float r5.I.[k]
            let j = float r5.J.[k]
            let n = r5.N.[k]
            let pii = Math.Pow(pi, i)
            let pii1 = Math.Pow(pi, i - 1.0)
            let tj = Math.Pow(tau, j)
            let tj1 = Math.Pow(tau, j - 1.0)
            gr <- gr + n * pii * tj
            grp <- grp + n * i * pii1 * tj
            grpp <- grpp + n * i * (i - 1.0) * Math.Pow(pi, i - 2.0) * tj
            grt <- grt + n * pii * j * tj1
            grtt <- grtt + n * pii * j * (j - 1.0) * Math.Pow(tau, j - 2.0)
            grpt <- grpt + n * i * pii1 * j * tj1
        let r = m.R
        {| V = pi * (gop + grp) * r * tK / (pMPa * 1000.0)
           H = tau * (got + grt) * r * tK
           S = (tau * (got + grt) - (go + gr)) * r
           Cp = -tau * tau * (gott + grtt) * r
           Cv = r * (-tau * tau * (gott + grtt)
                     + ((gop + grp) - tau * grpt) ** 2.0 / (gopp + grpp)) |}

    // ---------- region selection ----------

    /// Boundary between regions 2 and 3 (IF97 equation 5), pressure [MPa] at T [K].
    let internal b23Pressure (tK: float) =
        348.05185628969 - 1.1671859879975 * tK + 1.0192970039326e-3 * tK * tK

    let private state (region: int) (t: float<K>) (p: float<bar>) (v: float) (h: float) (sEnt: float)
                      (cp: float) (cv: float) =
        { Region = region; Temperature = t; Pressure = p
          SpecificVolume = v; Density = (1.0 / v) * 1.0<kg/m^3>
          Enthalpy = h; Entropy = sEnt; Cp = cp; Cv = cv }

    /// Full property evaluation at (T, P) over all five IF97 regions:
    /// 1 (compressed liquid), 2 (steam), 3 (near-critical, by solving the
    /// Helmholtz equation for density), 4 (the saturation line, which separates
    /// 1/3-liquid from 2/3-vapour) and 5 (1073.15-2273.15 K, P <= 50 MPa).
    let properties (m: Model) (t: float<K>) (p: float<bar>) : Thermo<SteamState> =
        let tK = float t
        let pMPa = float p / 10.0

        if pMPa <= 0.0 then
            fail (InvalidMixture "pressure must be positive")
        elif tK < 273.15 then
            fail (OutsideFitRange ("IF97", "temperature", tK, 273.15, m.R5.TMax))
        elif tK > m.R5.TMax then
            fail (OutsideFitRange ("IF97", "temperature", tK, 273.15, m.R5.TMax))
        elif tK > 1073.15 then
            if pMPa > m.R5.PMax then
                fail (CorrelationExtrapolated ("IF97 region 5", $"P = %.1f{pMPa} MPa exceeds its 50 MPa limit"))
            else
                let r = region5 m tK pMPa
                ok (state 5 t p r.V r.H r.S r.Cp r.Cv)
        elif pMPa > 100.0 then
            fail (CorrelationExtrapolated ("IF97", $"P = %.1f{pMPa} MPa exceeds the 100 MPa limit"))
        elif tK > 623.15 && tK <= 863.15 && pMPa > b23Pressure tK then
            let tc = float m.Tc
            let branch =
                if tK >= tc then Supercritical
                elif pMPa > psatMPa m tK then Liquid
                else Vapour
            region3Density m branch pMPa tK
            >>= fun rho ->
                let r = region3 m rho tK
                ok (state 3 t p r.V r.H r.S r.Cp r.Cv)
        else
            let pSat = if tK <= m.R4.TMax then psatMPa m tK else Double.MaxValue
            if tK <= 623.15 && pMPa > pSat then
                let r = region1 m tK pMPa
                ok (state 1 t p r.V r.H r.S r.Cp r.Cv)
            else
                let r = region2 m tK pMPa
                ok (state 2 t p r.V r.H r.S r.Cp r.Cv)

    // ---------- saturated states ----------

    /// Saturated liquid and vapour at a given pressure, plus latent heat.
    /// This is what the shell side of a WHB actually needs.
    let saturatedAt (m: Model) (p: float<bar>) : Thermo<{| Liquid: SteamState
                                                           Vapour: SteamState
                                                           LatentHeat: float
                                                           SaturationTemperature: float<K> |}> =
        saturationTemperature m p
        >>= fun tSat ->
            let pMPa = float p / 10.0
            let tK = float tSat
            let result (liquid: SteamState) (vapour: SteamState) =
                ok {| Liquid = liquid
                      Vapour = vapour
                      LatentHeat = vapour.Enthalpy - liquid.Enthalpy
                      SaturationTemperature = tSat |}
            if tK <= 623.15 then
                // Approach the saturation line from each side to stay inside the
                // correct region: the basic equations are valid up to it, not across.
                let liquid = region1 m tK pMPa
                let vapour = region2 m tK pMPa
                result (state 1 tSat p liquid.V liquid.H liquid.S liquid.Cp liquid.Cv)
                       (state 2 tSat p vapour.V vapour.H vapour.S vapour.Cp vapour.Cv)
            else
                // Above 623.15 K both saturated phases lie in region 3.
                region3Density m Liquid pMPa tK
                >>= fun rhoL ->
                    region3Density m Vapour pMPa tK
                    >>= fun rhoV ->
                        let liquid = region3 m rhoL tK
                        let vapour = region3 m rhoV tK
                        result (state 3 tSat p liquid.V liquid.H liquid.S liquid.Cp liquid.Cv)
                               (state 3 tSat p vapour.V vapour.H vapour.S vapour.Cp vapour.Cv)
