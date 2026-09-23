module WhbThermo.Tests.If97Tests

open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Steam

let private model =
    match If97.load () with
    | Success (m, _) -> m
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Relative agreement with the published verification values. IF97 is one of the
/// few correlations in this repository that can be checked against printed
/// digits rather than judgement, so the tolerance is tight on purpose.
let private near (expected: float) (actual: float) =
    Assert.True(abs (actual - expected) / abs expected < 1e-8,
                $"expected {expected:e9}, got {actual:e9}")

// ---------- region 1: compressed liquid (Table 5 of the standard) ----------

[<Fact>]
let ``region 1 at 300 K and 3 MPa`` () =
    let s = If97.properties model 300.0<K> 30.0<bar> |> value
    Assert.Equal(1, s.Region)
    near 0.100215168e-2 s.SpecificVolume
    near 0.115331273e3 s.Enthalpy
    near 0.392294792 s.Entropy
    near 0.417301218e1 s.Cp

[<Fact>]
let ``region 1 at 300 K and 80 MPa`` () =
    let s = If97.properties model 300.0<K> 800.0<bar> |> value
    near 0.971180894e-3 s.SpecificVolume
    near 0.184142828e3 s.Enthalpy
    near 0.368563852 s.Entropy

[<Fact>]
let ``region 1 at 500 K and 3 MPa`` () =
    let s = If97.properties model 500.0<K> 30.0<bar> |> value
    near 0.120241800e-2 s.SpecificVolume
    near 0.975542239e3 s.Enthalpy
    near 0.465580682e1 s.Cp

// ---------- region 2: superheated steam (Table 15) ----------

[<Fact>]
let ``region 2 at 300 K and 0.0035 MPa`` () =
    let s = If97.properties model 300.0<K> 0.035<bar> |> value
    Assert.Equal(2, s.Region)
    near 0.394913866e2 s.SpecificVolume
    near 0.254991145e4 s.Enthalpy
    near 0.852238967e1 s.Entropy

[<Fact>]
let ``region 2 at 700 K and 30 MPa`` () =
    let s = If97.properties model 700.0<K> 300.0<bar> |> value
    near 0.542946619e-2 s.SpecificVolume
    near 0.263149474e4 s.Enthalpy
    near 0.103505092e2 s.Cp

// ---------- region 4: saturation line (Tables 35, 36) ----------

[<Fact>]
let ``saturation pressure matches the standard`` () =
    near 0.353658941e-2 (float (If97.saturationPressure model 300.0<K> |> value) / 10.0)
    near 2.63889776 (float (If97.saturationPressure model 500.0<K> |> value) / 10.0)
    near 0.123443146e2 (float (If97.saturationPressure model 600.0<K> |> value) / 10.0)

[<Fact>]
let ``saturation temperature matches the standard`` () =
    near 0.372755919e3 (float (If97.saturationTemperature model 1.0<bar> |> value))
    near 0.453035632e3 (float (If97.saturationTemperature model 10.0<bar> |> value))
    near 0.584149488e3 (float (If97.saturationTemperature model 100.0<bar> |> value))

/// Psat and Tsat are separate correlations, not inverses of one another, so
/// their consistency is a real check rather than a tautology.
[<Fact>]
let ``saturation correlations are mutually consistent`` () =
    for tK in [ 300.0; 400.0; 500.0; 600.0; 640.0 ] do
        let p = If97.saturationPressure model (tK * 1.0<K>) |> value
        let back = If97.saturationTemperature model p |> value
        Assert.True(abs (float back - tK) < 1e-6, $"round trip at {tK} K gave {back}")

// ---------- WHB service conditions ----------

/// A 100 bar steam drum: saturation temperature about 311 degC, latent heat
/// about 1317 kJ/kg. These are the numbers the shell-side boiling correlations
/// consume, so a gross error here would propagate into every rating.
[<Fact>]
let ``saturated state at 100 bar is physically right`` () =
    let s = If97.saturatedAt model 100.0<bar> |> value
    Assert.InRange(float s.SaturationTemperature - 273.15, 310.0, 312.0)
    Assert.InRange(s.LatentHeat, 1300.0, 1330.0)
    Assert.True(s.Liquid.Density > s.Vapour.Density)
    Assert.InRange(float s.Liquid.Density, 680.0, 730.0)
    Assert.InRange(float s.Vapour.Density, 50.0, 60.0)

/// Latent heat must fall monotonically towards zero as the critical point nears.
[<Fact>]
let ``latent heat decreases with pressure`` () =
    let values =
        [ 10.0; 40.0; 80.0; 120.0; 160.0 ]
        |> List.map (fun p -> (If97.saturatedAt model (p * 1.0<bar>) |> value).LatentHeat)
    for a, b in List.pairwise values do
        Assert.True(b < a, $"latent heat not decreasing: {a} -> {b}")

// ---------- explicit refusal outside the implemented regions ----------

/// Region 3 is not implemented. A near-critical state must FAIL, not silently
/// extrapolate a region 1 or 2 equation into a range where it is meaningless.
[<Fact>]
let ``near critical state is refused rather than extrapolated`` () =
    match If97.properties model 650.0<K> 250.0<bar> with
    | Failure msgs ->
        Assert.Contains(msgs, function CorrelationExtrapolated _ -> true | _ -> false)
    | Success _ -> failwith "region 3 must be refused"

[<Fact>]
let ``region 5 is refused`` () =
    match If97.properties model 1200.0<K> 10.0<bar> with
    | Failure _ -> ()
    | Success _ -> failwith "region 5 must be refused"

/// The term counts are fixed by the standard: a file with the wrong count is
/// not IF97, whatever its header claims.
[<Fact>]
let ``a file with the wrong term count is rejected`` () =
    let json = """
    { "standard": "fake", "source": "test",
      "constants": { "r_kJ_kgK": 0.461526, "tc_K": 647.096, "pc_MPa": 22.064,
                     "rhoc_kg_m3": 322.0, "tt_K": 273.16, "pt_MPa": 0.000611657 },
      "region1": { "piStar_MPa": 16.53, "tauStar_K": 1386.0,
                   "range": { "tMinK": 273.15, "tMaxK": 623.15, "pMaxMPa": 100.0 },
                   "i": [0], "j": [0], "n": [1.0] },
      "region2": { "piStar_MPa": 1.0, "tauStar_K": 540.0,
                   "range": { "tMinK": 273.15, "tMaxK": 1073.15, "pMaxMPa": 100.0 },
                   "ideal": { "j": [0], "n": [1.0] },
                   "residual": { "i": [0], "j": [0], "n": [1.0] } },
      "region4": { "range": { "tMinK": 273.15, "tMaxK": 647.096 }, "n": [1.0] } }
    """
    match If97.parse json with
    | Failure [ DatabaseParseError d ] -> Assert.Contains("34", d)
    | _ -> failwith "expected a parse failure on term count"
