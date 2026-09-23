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
    type private RangeDto = { tMinK: float; tMaxK: float; pMaxMPa: float }

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
    type private RootDto =
        { standard: string; source: string; constants: ConstantsDto
          region1: Region1Dto; region2: Region2Dto; region4: Region4Dto }

    type Model =
        internal
            { R        : float
              Tc       : float<K>
              Pc       : float<bar>
              R1       : {| PiStar: float; TauStar: float
                            TMin: float; TMax: float; PMax: float
                            I: int[]; J: int[]; N: float[] |}
              R2       : {| PiStar: float; TauStar: float
                            TMin: float; TMax: float; PMax: float
                            IdealJ: int[]; IdealN: float[]
                            I: int[]; J: int[]; N: float[] |}
              R4       : {| TMin: float; TMax: float; N: float[] |}
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
        o.Converters.Add(JsonFsharpConverter())
        o

    let parse (json: string) : Thermo<Model> =
        try
            let r = JsonSerializer.Deserialize<RootDto>(json, options)
            let check name (a: int[]) (b: int[]) (n: float[]) =
                if a.Length <> n.Length || b.Length <> n.Length then
                    Some (DatabaseParseError
                            $"IF97 {name}: coefficient arrays of unequal length "
                            + $"(I={a.Length}, J={b.Length}, n={n.Length})")
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
                else
                    ok { R = r.constants.r_kJ_kgK
                         Tc = r.constants.tc_K * 1.0<K>
                         Pc = r.constants.pc_MPa * 10.0<bar>
                         R1 = {| PiStar = r.region1.piStar_MPa; TauStar = r.region1.tauStar_K
                                 TMin = r.region1.range.tMinK; TMax = r.region1.range.tMaxK
                                 PMax = r.region1.range.pMaxMPa
                                 I = r.region1.i; J = r.region1.j; N = r.region1.n |}
                         R2 = {| PiStar = r.region2.piStar_MPa; TauStar = r.region2.tauStar_K
                                 TMin = r.region2.range.tMinK; TMax = r.region2.range.tMaxK
                                 PMax = r.region2.range.pMaxMPa
                                 IdealJ = r.region2.ideal.j; IdealN = r.region2.ideal.n
                                 I = r.region2.residual.i; J = r.region2.residual.j
                                 N = r.region2.residual.n |}
                         R4 = {| TMin = r.region4.range.tMinK; TMax = r.region4.range.tMaxK
                                 N = r.region4.n |}
                         Standard = r.standard
                         Source = r.source }
        with ex ->
            fail (DatabaseParseError ex.Message)

    let load () : Thermo<Model> = DataStore.load DefaultFile parse
    let loadFile (path: string) : Thermo<Model> = DataStore.load path parse

    // ---------- region 4: saturation line ----------

    /// Saturation pressure [MPa] at temperature T [K]. IF97 equation 30.
    let private psatMPa (m: Model) (tK: float) =
        let n = m.R4.N
        let theta = tK + n.[8] / (tK - n.[9])
        let a = theta * theta + n.[0] * theta + n.[1]
        let b = n.[2] * theta * theta + n.[3] * theta + n.[4]
        let c = n.[5] * theta * theta + n.[6] * theta + n.[7]
        let x = 2.0 * c / (-b + sqrt (b * b - 4.0 * a * c))
        x ** 4.0

    /// Saturation temperature [K] at pressure P [MPa]. IF97 equation 31.
    let private tsatK (m: Model) (pMPa: float) =
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

    let private region1 (m: Model) (tK: float) (pMPa: float) =
        let pi = pMPa / m.R1.PiStar
        let tau = m.R1.TauStar / tK
        let mutable g = 0.0
        let mutable gp = 0.0
        let mutable gt = 0.0
        let mutable gpp = 0.0
        let mutable gtt = 0.0
        let mutable gpt = 0.0

        for k in 0 .. m.R1.N.Length - 1 do
            let i = float m.R1.I.[k]
            let j = float m.R1.J.[k]
            let c = m.R1.N.[k]
            let a = 7.1 - pi
            let b = tau - 1.222
            g <- g + c * (a ** i) * (b ** j)
            gp <- gp - c * i * (a ** (i - 1.0)) * (b ** j)
            gt <- gt + c * (a ** i) * j * (b ** (j - 1.0))
            gpp <- gpp + c * i * (i - 1.0) * (a ** (i - 2.0)) * (b ** j)
            gtt <- gtt + c * (a ** i) * j * (j - 1.0) * (b ** (j - 2.0))
            gpt <- gpt - c * i * (a ** (i - 1.0)) * j * (b ** (j - 1.0))

        let v = pi * gp * m.R * tK / pMPa / 1000.0
        {| V = v
           H = tau * gt * m.R * tK
           S = m.R * (tau * gt - g)
           Cp = -m.R * tau * tau * gtt
           Cv = m.R * (-tau * tau * gtt + (gp - tau * gpt) ** 2.0 / gpp) |}

    // ---------- region 2: superheated steam ----------

    let private region2 (m: Model) (tK: float) (pMPa: float) =
        let pi = pMPa / m.R2.PiStar
        let tau = m.R2.TauStar / tK

        let mutable go = log pi
        let mutable got = 0.0
        let mutable gott = 0.0
        for k in 0 .. m.R2.IdealN.Length - 1 do
            let j = float m.R2.IdealJ.[k]
            let c = m.R2.IdealN.[k]
            go <- go + c * (tau ** j)
            got <- got + c * j * (tau ** (j - 1.0))
            gott <- gott + c * j * (j - 1.0) * (tau ** (j - 2.0))
        let gop = 1.0 / pi
        let gopp = -1.0 / (pi * pi)

        let mutable gr = 0.0
        let mutable grp = 0.0
        let mutable grt = 0.0
        let mutable grpp = 0.0
        let mutable grtt = 0.0
        let mutable grpt = 0.0
        for k in 0 .. m.R2.N.Length - 1 do
            let i = float m.R2.I.[k]
            let j = float m.R2.J.[k]
            let c = m.R2.N.[k]
            let b = tau - 0.5
            gr <- gr + c * (pi ** i) * (b ** j)
            grp <- grp + c * i * (pi ** (i - 1.0)) * (b ** j)
            grt <- grt + c * (pi ** i) * j * (b ** (j - 1.0))
            grpp <- grpp + c * i * (i - 1.0) * (pi ** (i - 2.0)) * (b ** j)
            grtt <- grtt + c * (pi ** i) * j * (j - 1.0) * (b ** (j - 2.0))
            grpt <- grpt + c * i * (pi ** (i - 1.0)) * j * (b ** (j - 1.0))

        let v = pi * (gop + grp) * m.R * tK / pMPa / 1000.0
        {| V = v
           H = tau * (got + grt) * m.R * tK
           S = m.R * (tau * (got + grt) - (go + gr))
           Cp = -m.R * tau * tau * (gott + grtt)
           Cv = m.R * (-tau * tau * (gott + grtt)
                       - (1.0 + pi * grp - tau * pi * grpt) ** 2.0
                         / (1.0 - pi * pi * grpp)) |}

    // ---------- region selection ----------

    /// Boundary between regions 2 and 3 (IF97 equation 5), used only to detect
    /// that a state has left the implemented range.
    let private b23Pressure (tK: float) =
        348.05185628969 - 1.1671859879975 * tK + 1.0192970039326e-3 * tK * tK

    /// Full property evaluation at (T, P). Selects the region and refuses
    /// explicitly when the state falls in regions 3 or 5.
    let properties (m: Model) (t: float<K>) (p: float<bar>) : Thermo<SteamState> =
        let tK = float t
        let pMPa = float p / 10.0

        if pMPa <= 0.0 then
            fail (InvalidMixture "pressure must be positive")
        elif tK < 273.15 then
            fail (OutsideFitRange ("IF97", "temperature", tK, 273.15, 1073.15))
        elif pMPa > 100.0 then
            fail (CorrelationExtrapolated ("IF97", $"P = %.1f{pMPa} MPa exceeds the 100 MPa limit"))
        elif tK > 1073.15 then
            fail (CorrelationExtrapolated
                    ("IF97", $"T = %.1f{tK} K is in region 5, which is not implemented"))
        elif tK > 623.15 && pMPa > b23Pressure tK then
            fail (CorrelationExtrapolated
                    ("IF97", $"T = %.1f{tK} K, P = %.2f{pMPa} MPa is in region 3 "
                             + "(near-critical), which is not implemented"))
        else
            let pSat = if tK <= m.R4.TMax then psatMPa m tK else Double.MaxValue
            let region = if tK <= 623.15 && pMPa > pSat then 1 else 2
            let r = if region = 1 then region1 m tK pMPa else region2 m tK pMPa

            ok { Region = region
                 Temperature = t
                 Pressure = p
                 SpecificVolume = r.V
                 Density = (1.0 / r.V) * 1.0<kg/m^3>
                 Enthalpy = r.H
                 Entropy = r.S
                 Cp = r.Cp
                 Cv = r.Cv }

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
            // Approach the saturation line from each side to stay inside the
            // correct region: the basic equations are valid up to it, not across.
            let liquid = region1 m tK pMPa
            let vapour = region2 m tK pMPa
            ok {| Liquid = { Region = 1; Temperature = tSat; Pressure = p
                             SpecificVolume = liquid.V
                             Density = (1.0 / liquid.V) * 1.0<kg/m^3>
                             Enthalpy = liquid.H; Entropy = liquid.S
                             Cp = liquid.Cp; Cv = liquid.Cv }
                  Vapour = { Region = 2; Temperature = tSat; Pressure = p
                             SpecificVolume = vapour.V
                             Density = (1.0 / vapour.V) * 1.0<kg/m^3>
                             Enthalpy = vapour.H; Entropy = vapour.S
                             Cp = vapour.Cp; Cv = vapour.Cv }
                  LatentHeat = vapour.H - liquid.H
                  SaturationTemperature = tSat |}
