namespace WhbThermo.Steam

open System
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// Water and steam properties with plain SI floats in and out, for solver hot
/// paths that cannot afford a result wrapper per call.
///
/// Everything here evaluates the same data as If97.properties: the IF97
/// coefficients come from iapws-if97.json through the shared If97 kernels, the
/// transport and surface-tension coefficients from iapws-water-transport.json.
/// Units follow the IF97 working set of Whb.Core: pressure in MPa or Pa as
/// named, temperature in K, specific properties in kJ/kg from the region
/// functions and in J/kg in SatProps.
///
/// Failure to load the data files is not recoverable at this level and throws;
/// out-of-range states are NOT checked, because the callers work inside the WHB
/// envelope and check it themselves. Use If97.properties where a checked,
/// warning-carrying result is wanted.
module Water =

    let private valueOf (what: string) (result: Thermo<'T>) : 'T =
        match result with
        | Success (value, _) -> value
        | Failure errors ->
            failwithf "WhbThermo.Steam %s: %s" what (errors |> List.map string |> String.concat "; ")

    let private if97 = lazy (If97.load () |> valueOf "IAPWS-IF97 data")

    // ---------- transport and surface tension data ----------

    [<CLIMutable; NoComparison; NoEquality>]
    type private ViscosityDto = { standard: string; h0: float[]; h1: float[][] }

    [<CLIMutable; NoComparison; NoEquality>]
    type private ConductivityDto = { standard: string; l0: float[]; l1: float[][] }

    [<CLIMutable; NoComparison; NoEquality>]
    type private SurfaceTensionDto = { standard: string; bigB: float; mu: float; smallB: float }

    [<CLIMutable; NoComparison; NoEquality>]
    type private TransportDto =
        { tcK: float; rhocKgM3: float
          viscosity: ViscosityDto; conductivity: ConductivityDto
          surfaceTension: SurfaceTensionDto }

    /// IAPWS transport and surface-tension coefficients.
    [<NoComparison; NoEquality>]
    type TransportData =
        { Tc     : float
          RhoC   : float
          H0     : float[]
          /// (i, j, H_ij): H_ij (1/Tb - 1)^i (rhob - 1)^j
          H1     : (int * int * float)[]
          L0     : float[]
          L1     : float[,]
          SigmaB : float
          SigmaMu: float
          Sigmab : float
          Standards : string list }

    [<Literal>]
    let TransportFile = "iapws-water-transport.json"

    let parseTransport (json: string) : Thermo<TransportData> =
        try
            let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
            o.Converters.Add(JsonFSharpConverter())
            let d = JsonSerializer.Deserialize<TransportDto>(json, o)
            if d.tcK <> PhysicalConstants.Water.CriticalTemperature
               || d.rhocKgM3 <> PhysicalConstants.Water.CriticalDensity then
                fail (DatabaseParseError
                        "transport file critical constants differ from PhysicalConstants.Water")
            elif d.viscosity.h0.Length <> 4 || d.viscosity.h1.Length <> 21 then
                fail (DatabaseParseError "IAPWS 2008 viscosity needs 4 dilute and 21 residual coefficients")
            elif d.conductivity.l0.Length <> 5 || d.conductivity.l1.Length <> 5
                 || d.conductivity.l1 |> Array.exists (fun row -> row.Length <> 6) then
                fail (DatabaseParseError "IAPWS 2011 conductivity needs 5 dilute and 5x6 residual coefficients")
            elif d.viscosity.h1 |> Array.exists (fun t -> t.Length <> 3) then
                fail (DatabaseParseError "viscosity residual terms are [i, j, H]")
            else
                ok { Tc = d.tcK
                     RhoC = d.rhocKgM3
                     H0 = d.viscosity.h0
                     H1 = d.viscosity.h1 |> Array.map (fun t -> int t.[0], int t.[1], t.[2])
                     L0 = d.conductivity.l0
                     L1 = array2D d.conductivity.l1
                     SigmaB = d.surfaceTension.bigB
                     SigmaMu = d.surfaceTension.mu
                     Sigmab = d.surfaceTension.smallB
                     Standards = [ d.viscosity.standard; d.conductivity.standard
                                   d.surfaceTension.standard ] }
        with ex -> fail (DatabaseParseError ex.Message)

    let loadTransport () : Thermo<TransportData> = DataStore.load TransportFile parseTransport

    let private transport = lazy (loadTransport () |> valueOf "IAPWS transport data")

    // ---------- IF97 ----------

    /// Specific gas constant of water [kJ/(kg K)] of the loaded IF97 data.
    let gasConstant () = (if97.Force()).R

    /// Critical temperature [K] and density [kg/m^3] of the loaded data.
    let criticalTemperature () = float (if97.Force()).Tc
    let criticalDensity () = (if97.Force()).RhoC

    /// Saturation pressure [MPa] at T [K] (IF97 equation 30).
    let psatMPa (tK: float) = If97.psatMPa (if97.Force()) tK

    /// Saturation temperature [K] at P [MPa] (IF97 equation 31).
    let tsatK (pMPa: float) = If97.tsatK (if97.Force()) pMPa

    /// Region 1 (compressed liquid): v [m^3/kg], h [kJ/kg], cp [kJ/(kg K)], s [kJ/(kg K)].
    let region1 (pMPa: float) (tK: float) =
        let r = If97.region1 (if97.Force()) tK pMPa
        (r.V, r.H, r.Cp, r.S)

    /// Region 2 (steam): v [m^3/kg], h [kJ/kg], cp [kJ/(kg K)], s [kJ/(kg K)].
    let region2 (pMPa: float) (tK: float) =
        let r = If97.region2 (if97.Force()) tK pMPa
        (r.V, r.H, r.Cp, r.S)

    /// Region 3 (near-critical) at density [kg/m^3] and T [K]: P [MPa], h [kJ/kg],
    /// cp [kJ/(kg K)], s [kJ/(kg K)].
    let region3 (rho: float) (tK: float) =
        let r = If97.region3 (if97.Force()) rho tK
        (r.P, r.H, r.Cp, r.S)

    /// Region 5 (1073.15-2273.15 K, P <= 50 MPa): v [m^3/kg], h [kJ/kg],
    /// cp [kJ/(kg K)], s [kJ/(kg K)].
    let region5 (pMPa: float) (tK: float) =
        let r = If97.region5 (if97.Force()) tK pMPa
        (r.V, r.H, r.Cp, r.S)

    // ---------- transport ----------

    /// Dynamic viscosity [Pa s] at T [K] and density [kg/m^3], IAPWS 2008
    /// without the critical enhancement.
    let viscosity (tK: float) (rho: float) =
        let d = transport.Force()
        let tb = tK / d.Tc
        let rb = rho / d.RhoC
        let mutable den = 0.0
        for i in 0 .. 3 do
            den <- den + d.H0.[i] / Math.Pow(tb, float i)
        let mu0 = 100.0 * sqrt tb / den                       // uPa s
        if rb = 0.0 then mu0 * 1e-6      // dilute-gas limit: the residual factor is exp(0) = 1
        else
            let mutable s = 0.0
            for (i, j, h) in d.H1 do
                s <- s + Math.Pow(1.0 / tb - 1.0, float i) * h * Math.Pow(rb - 1.0, float j)
            let mu1 = exp (rb * s)
            mu0 * mu1 * 1e-6

    /// Thermal conductivity [W/(m K)] at T [K] and density [kg/m^3], IAPWS 2011
    /// without the critical enhancement.
    let conductivity (tK: float) (rho: float) =
        let d = transport.Force()
        let tb = tK / d.Tc
        let rb = rho / d.RhoC
        let mutable den = 0.0
        for k in 0 .. 4 do
            den <- den + d.L0.[k] / Math.Pow(tb, float k)
        let l0 = sqrt tb / den
        if rb = 0.0 then l0 * 1e-3       // dilute-gas limit: the residual factor is exp(0) = 1
        else
            let mutable s = 0.0
            for i in 0 .. 4 do
                let mutable inner = 0.0
                for j in 0 .. 5 do
                    inner <- inner + d.L1.[i, j] * Math.Pow(rb - 1.0, float j)
                s <- s + Math.Pow(1.0 / tb - 1.0, float i) * inner
            let l1 = exp (rb * s)
            l0 * l1 * 1e-3

    /// Surface tension of saturated water [N/m] at T [K], IAPWS R1-76(2014).
    let surfaceTension (tK: float) =
        let d = transport.Force()
        let tau = 1.0 - tK / d.Tc
        if tau <= 0.0 then 0.0
        else d.SigmaB * Math.Pow(tau, d.SigmaMu) * (1.0 + d.Sigmab * tau)

    // ---------- explicit saturation-curve fits ----------

    /// Explicit fits along the saturation line, for places that need a value
    /// without a pressure iteration. Auxiliary equations, not the formulation:
    /// use IF97 where accuracy matters.
    module Auxiliary =

        /// Saturated liquid density [kg/m^3]: IAPWS SR1-86(1992) auxiliary equation.
        let rhoLsat (tK: float) =
            If97.auxLiquidDensity (criticalTemperature ()) (criticalDensity ()) tK

        /// Saturated vapour density [kg/m^3]: IAPWS SR1-86(1992) auxiliary equation.
        let rhoVsat (tK: float) =
            If97.auxVapourDensity (criticalTemperature ()) (criticalDensity ()) tK

        /// Saturated liquid viscosity [Pa s], Vogel equation.
        let muLVogel (tK: float) =
            exp (-3.7188 + 578.919 / (tK - 137.546)) * 1e-3

        /// Saturated liquid conductivity [W/(m K)], Ramires et al. (1995) fit.
        let kLRamires (tK: float) =
            let tr = tK / 298.15
            0.6065 * (-1.48445 + 4.12292 * tr - 1.63866 * tr * tr)

        /// Latent heat [J/kg], Watson scaling from 2256.5 kJ/kg at 373.124 K.
        let hfgWatson (tK: float) =
            let tc = criticalTemperature ()
            let tr = 1.0 - tK / tc
            let tr0 = 1.0 - 373.124 / tc
            if tr <= 0.0 then 0.0
            else 2256.5e3 * Math.Pow(tr / tr0, 0.38)

    // ---------- saturation states ----------

    /// Saturated liquid and vapour at one point of the saturation line, SI units.
    type SatProps =
        { P: float          // Pa
          Tsat: float       // K
          RhoL: float       // kg/m^3
          RhoV: float       // kg/m^3
          HL: float         // J/kg
          HV: float         // J/kg
          Hfg: float        // J/kg
          CpL: float        // J/(kg K)
          CpV: float        // J/(kg K)
          MuL: float        // Pa s
          MuV: float        // Pa s
          KL: float         // W/(m K)
          KV: float         // W/(m K)
          Sigma: float      // N/m
          PrL: float
          PrV: float }

    let private satCore (pPa: float) (tK: float) : SatProps =
        let pMPa = pPa / 1.0e6
        // Up to 623.15 K the saturated phases are the edges of regions 1 and 2;
        // above it (16.53 MPa and up) both lie in region 3.
        let (vl, hl, cpl, vv, hv, cpv) =
            if tK <= 623.15 then
                let (vl, hl, cpl, _) = region1 pMPa tK
                let (vv, hv, cpv, _) = region2 pMPa tK
                (vl, hl, cpl, vv, hv, cpv)
            else
                let m = if97.Force()
                let rhoL = If97.region3Density m If97.Liquid pMPa tK |> valueOf "region 3 saturated liquid"
                let rhoV = If97.region3Density m If97.Vapour pMPa tK |> valueOf "region 3 saturated vapour"
                let l = If97.region3 m rhoL tK
                let v = If97.region3 m rhoV tK
                (l.V, l.H, l.Cp, v.V, v.H, v.Cp)
        let rhol = 1.0 / vl
        let rhov = 1.0 / vv
        let mul = viscosity tK rhol
        let muv = viscosity tK rhov
        let kl = conductivity tK rhol
        let kv = conductivity tK rhov
        { P = pPa
          Tsat = tK
          RhoL = rhol
          RhoV = rhov
          HL = hl * 1000.0
          HV = hv * 1000.0
          Hfg = (hv - hl) * 1000.0
          CpL = cpl * 1000.0
          CpV = cpv * 1000.0
          MuL = mul
          MuV = muv
          KL = kl
          KV = kv
          Sigma = surfaceTension tK
          PrL = cpl * 1000.0 * mul / kl
          PrV = cpv * 1000.0 * muv / kv }

    /// Saturation state at a pressure [Pa].
    let sat (pPa: float) : SatProps =
        satCore pPa (tsatK (pPa / 1.0e6))

    /// Saturation state at a saturation temperature [K].
    let satT (tK: float) : SatProps =
        satCore (psatMPa tK * 1.0e6) tK

    /// Saturation states from tMinC to tMaxC [degC] in steps of stepC, clipped to
    /// 0.02-370 degC.
    let saturationTable (tMinC: float) (tMaxC: float) (stepC: float) : SatProps list =
        let step = max 0.1 stepC
        let lo = max 0.02 tMinC
        let hi = min 370.0 tMaxC
        let n = max 0 (int (round ((hi - lo) / step)))
        [ for i in 0 .. n -> satT (lo + float i * step + 273.15) ]

    /// Compressed-liquid enthalpy [J/kg] at P [Pa] and T [K].
    let hLiquid (pPa: float) (tK: float) =
        let (_, h, _, _) = region1 (pPa / 1.0e6) tK
        h * 1000.0

    /// Compressed-liquid density [kg/m^3] at P [Pa] and T [K].
    let rhoLiquid (pPa: float) (tK: float) =
        let (v, _, _, _) = region1 (pPa / 1.0e6) tK
        1.0 / v
