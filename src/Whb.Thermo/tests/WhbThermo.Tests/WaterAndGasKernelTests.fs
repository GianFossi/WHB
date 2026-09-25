module WhbThermo.Tests.WaterAndGasKernelTests

open System
open Xunit
open WhbThermo.Domain
open WhbThermo.Steam
open WhbThermo.Properties
open WhbThermo.Radiation

let private near (expected: float) (relTol: float) (actual: float) (label: string) =
    Assert.True(abs (actual - expected) <= relTol * abs expected,
                $"{label}: expected {expected}, got {actual}")

// ---------- IAPWS transport, against the releases' own verification tables ----------

/// IAPWS R12-08, Table 4 (mu_bar_2 = 1, i.e. without critical enhancement, which
/// is exact at these states).
[<Fact>]
let ``water viscosity reproduces the IAPWS 2008 check values`` () =
    for (t, rho, expected) in [ 298.15, 998.0, 889.735100
                                873.15, 1.0, 32.619287
                                1173.15, 400.0, 64.154608 ] do
        near expected 1e-6 (Water.viscosity t rho * 1e6) $"mu({t} K, {rho} kg/m3) [uPa s]"

/// IAPWS R15-11, Table 4: the background conductivity lambda_0 lambda_1,
/// without the critical enhancement.
[<Fact>]
let ``water conductivity reproduces the IAPWS 2011 check values`` () =
    for (t, rho, expected) in [ 298.15, 0.0, 18.4341883
                                298.15, 998.0, 607.712868
                                298.15, 1200.0, 799.038144
                                873.15, 0.0, 79.1034659 ] do
        near expected 1e-6 (Water.conductivity t rho * 1e3) $"k({t} K, {rho} kg/m3) [mW/(m K)]"

[<Fact>]
let ``surface tension follows IAPWS R1-76`` () =
    near 71.686e-3 1e-3 (Water.surfaceTension 300.0) "sigma(300 K)"
    near 58.91e-3 1e-3 (Water.surfaceTension 373.15) "sigma(373.15 K)"
    Assert.Equal(0.0, Water.surfaceTension 700.0)

/// The saturation record at one atmosphere against IF97 reference numbers.
[<Fact>]
let ``saturation at one atmosphere is physical`` () =
    let s = Water.sat 101325.0
    near 373.124 1e-5 s.Tsat "Tsat"
    near 2256.4e3 2e-3 s.Hfg "hfg"
    near 958.35 1e-3 s.RhoL "rhoL"
    Assert.True(s.PrL > 1.0 && s.PrV > 0.8 && s.PrV < 1.2)
    // The two entry points agree on the same saturation state.
    near s.HL 1e-9 (Water.satT s.Tsat).HL "hL via satT"

/// The fast float API and the checked If97 API evaluate the same kernel.
[<Fact>]
let ``float and checked steam APIs agree`` () =
    let model =
        match If97.load () with
        | Ganfoss.ROP.Success (m, _) -> m
        | Ganfoss.ROP.Failure e -> failwith (string e)
    for (tK, pMPa) in [ 500.0, 1.0; 700.0, 3.0; 1000.0, 0.1 ] do
        let (v, h, cp, _) = Water.region2 pMPa tK
        match If97.properties model (tK * 1.0<K>) (pMPa * 10.0<bar>) with
        | Ganfoss.ROP.Success (st, _) ->
            Assert.Equal(st.SpecificVolume, v)
            Assert.Equal(st.Enthalpy, h)
            Assert.Equal(st.Cp, cp)
        | Ganfoss.ROP.Failure e -> failwith (string e)

// ---------- gas kernels ----------

[<Fact>]
let ``water second virial coefficient is negative and weakens with temperature`` () =
    let b500 = SecondVirial.waterB 500.0
    let b900 = SecondVirial.waterB 900.0
    Assert.True(b500 < 0.0 && b900 < 0.0 && b900 > b500)
    // Pure-component mixture B is the component's own B.
    let n2 : SecondVirial.Component =
        { Id = "N2"; Critical = Some (126.2, 33.98e5, 0.037, 89.2e-6); If97SecondVirial = false }
    let terms = SecondVirial.buildPairTerms [| n2 |] (fun _ _ -> 0.0)
    Assert.Equal(SecondVirial.pitzer 126.2 33.98e5 0.037 600.0, SecondVirial.bMix terms [| 1.0 |] 600.0)

[<Fact>]
let ``virial k_ij lowers the cross-term critical temperature`` () =
    let a : SecondVirial.Component = { Id = "A"; Critical = Some (300.0, 50e5, 0.1, 100e-6); If97SecondVirial = false }
    let b : SecondVirial.Component = { Id = "B"; Critical = Some (200.0, 40e5, 0.0, 90e-6); If97SecondVirial = false }
    let cross kij = (SecondVirial.buildPairTerms [| a; b |] (fun _ _ -> kij)) |> Array.find (fun t -> t.I <> t.J)
    Assert.Equal(sqrt (300.0 * 200.0) * 0.9, (cross 0.1).Tc, 12)

/// The array kernel and the component-level combination give the same numbers.
[<Fact>]
let ``Wilke kernel matches the combine functions`` () =
    let y = [| 0.4; 0.35; 0.25 |]
    let m = [| 2.016; 28.013; 18.015 |]
    let mu = [| 1.4e-5; 3.0e-5; 2.4e-5 |]
    let k = [| 0.30; 0.045; 0.046 |]
    let struct (muMix, kMix) = Mixing.wilkeWassiljewa y m mu k
    near (Mixing.combine (Array.init 3 (fun i -> y.[i], m.[i], mu.[i]))) 1e-14 muMix "mu"
    near (Mixing.combineConductivity (Array.init 3 (fun i -> y.[i], m.[i], mu.[i], k.[i]))) 1e-14 kMix "k"

[<Fact>]
let ``grey-gas emissivity is bounded and the cavity coefficient is zero at equal temperatures`` () =
    Assert.Equal(0.0, GreyGas.emissivity 0.0 0.0 1e5 1.0 1200.0)
    let e = GreyGas.emissivity 0.3 0.1 35e5 0.05 1300.0
    Assert.InRange(e, 0.0, 0.95)
    Assert.Equal(0.95, GreyGas.emissivity 0.5 0.5 100e5 5.0 800.0)
    Assert.Equal(0.0, GreyGas.cavityCoefficient 0.2 0.85 900.0 900.0)
    Assert.True(GreyGas.cavityCoefficient 0.2 0.85 1300.0 600.0 > 0.0)

// ---------- water second virial coefficient: region 2 -> region 5 ----------

let private bFrom (region: float -> float -> float * float * float * float) (t: float) =
    let p = 1000.0
    let (v, _, _, _) = region (p / 1.0e6) t
    (p * v / (Water.gasConstant () * 1000.0 * t) - 1.0) * 8.31446261815324 * t / p

/// Below the blend window the value is region 2, above it region 5.
[<Fact>]
let ``water B is region 2 below 1023 K and region 5 above 1123 K`` () =
    Assert.Equal(bFrom Water.region2 900.0, SecondVirial.waterB 900.0)
    Assert.Equal(bFrom Water.region5 1300.0, SecondVirial.waterB 1300.0)
    // Above its Boyle temperature water has a positive B.
    Assert.True(SecondVirial.waterB 1673.0 > 0.0)

/// The residual cp of the mixture is a 2 K second difference of B: across the
/// blend window that second difference must stay smooth, with no spike where
/// the two equations meet (a hard switch gives one 100 times larger).
[<Fact>]
let ``water B is smooth across the region 2 to region 5 blend`` () =
    let d2 t = (SecondVirial.waterB (t + 2.0) - 2.0 * SecondVirial.waterB t + SecondVirial.waterB (t - 2.0)) / 4.0
    let inside = [ 1000.0 .. 5.0 .. 1150.0 ] |> List.map (fun t -> abs (d2 t))
    let reference = abs (d2 1000.0)
    for v in inside do
        Assert.True(v < 3.0 * reference, $"second difference {v:e3} vs {reference:e3} outside the blend")
