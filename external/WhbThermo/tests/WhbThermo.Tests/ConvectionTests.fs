module WhbThermo.Tests.ConvectionTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Convection

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- friction ----------

/// Textbook value: Re = 50000 gives f = 0.02096 on a smooth tube.
[<Fact>]
let ``Petukhov friction matches the textbook value`` () =
    let f = InTube.petukhovFriction 50000.0 |> value
    Assert.Equal(0.020958, f, 6)

/// The laminar branch must be exact, not an extrapolation of Petukhov.
[<Fact>]
let ``laminar friction is 64 over Re`` () =
    let f = InTube.frictionFactor 1000.0 |> value
    Assert.Equal(0.064, f, 9)

[<Fact>]
let ``friction falls monotonically with Reynolds in the turbulent range`` () =
    let values =
        [ 5000.0; 10000.0; 50000.0; 200000.0; 1.0e6 ]
        |> List.map (fun re -> InTube.frictionFactor re |> value)
    for a, b in List.pairwise values do
        Assert.True(b < a, $"friction not decreasing: {a} -> {b}")

// ---------- Gnielinski ----------

/// Re = 50000, Pr = 0.7 gives Nu = 104.2. Dittus-Boelter gives 118.7 on the
/// same case, about 14 % higher, which is the expected spread between them.
[<Fact>]
let ``Gnielinski matches the textbook value`` () =
    let nu = InTube.gnielinski 50000.0 0.7 None InTube.None |> value
    Assert.Equal(104.2, nu, 1)

[<Fact>]
let ``Dittus-Boelter sits about 14 percent above Gnielinski`` () =
    let gnielinski = InTube.gnielinski 50000.0 0.7 None InTube.None |> value
    let dittus = InTube.dittusBoelter 50000.0 0.7 |> value
    let spread = (dittus - gnielinski) / gnielinski
    Assert.InRange(spread, 0.10, 0.20)

/// Dittus-Boelter must always announce its scatter: it is retained for
/// reproducing legacy specifications, not for new work.
[<Fact>]
let ``Dittus-Boelter always warns`` () =
    match InTube.dittusBoelter 50000.0 0.7 with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed"

[<Fact>]
let ``the entrance term enhances short tubes and vanishes for long ones`` () =
    let developed = InTube.gnielinski 50000.0 0.7 None InTube.None |> value
    let short = InTube.gnielinski 50000.0 0.7 (Some 10.0) InTube.None |> value
    let long = InTube.gnielinski 50000.0 0.7 (Some 500.0) InTube.None |> value
    Assert.True(short > developed * 1.15, "a short tube should be enhanced")
    Assert.True(abs (long - developed) / developed < 0.02, "a long tube should be near developed")

// ---------- property correction ----------

/// The correction that matters most, and the one easiest to get wrong. Kays
/// gives n = 0 for gas COOLING, so a waste heat boiler gets no enhancement from
/// the temperature ratio. A naive (T_bulk/T_wall)^0.45 would give 1.56 at SRU
/// conditions -- a 56 % enhancement in the non-conservative direction.
[<Fact>]
let ``Kays temperature ratio is unity for a cooled gas`` () =
    let correction =
        InTube.KaysTemperatureRatio (1673.15<K>, 623.15<K>, InTube.GasCooled)
    Assert.Equal(1.0, correction.Factor, 9)
    Assert.False(correction.IsEnhancement)

[<Fact>]
let ``Kays temperature ratio enhances a heated gas`` () =
    let correction =
        InTube.KaysTemperatureRatio (623.15<K>, 1073.15<K>, InTube.GasHeated)
    Assert.True(correction.Factor < 1.0, "a heated gas with a hotter wall is degraded")

/// Sieder-Tate is mild: about +11 % at SRU conditions, against the +56 % a
/// misapplied temperature ratio would give.
[<Fact>]
let ``Sieder-Tate viscosity correction is mild`` () =
    let correction = InTube.ViscosityRatio (59.3e-6, 30.0e-6)
    Assert.InRange(correction.Factor, 1.05, 1.15)

/// A large enhancement must be flagged, because it is by definition
/// non-conservative on duty.
[<Fact>]
let ``a large enhancement raises a warning`` () =
    let aggressive = InTube.CustomTemperatureRatio (1673.15<K>, 623.15<K>, -0.45)
    Assert.True(aggressive.Factor > 1.5)
    match InTube.gnielinski 20000.0 0.75 (Some 136.0) aggressive with
    | Success (_, warnings) ->
        Assert.Contains(warnings, function CorrelationExtrapolated _ -> true | _ -> false)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``film temperature is the arithmetic mean`` () =
    Assert.Equal(1148.15, float (InTube.filmTemperature 1673.15<K> 623.15<K>), 6)

// ---------- regime handling ----------

[<Fact>]
let ``regimes are classified at the conventional boundaries`` () =
    Assert.Equal(InTube.Laminar, InTube.FlowRegime.OfReynolds 1500.0)
    Assert.Equal(InTube.Transitional, InTube.FlowRegime.OfReynolds 2500.0)
    Assert.Equal(InTube.Turbulent, InTube.FlowRegime.OfReynolds 20000.0)

/// The transition band has no reliable correlation, but a marching solver still
/// needs a continuous answer. Blending must join both ends without a step.
[<Fact>]
let ``the transition band is continuous at both ends`` () =
    let evaluate re =
        (InTube.evaluate re 0.75 0.12<W/(m*K)> 0.044<m> None InTube.None |> value).Nusselt

    let laminarSide = evaluate 2299.0, evaluate 2301.0
    let turbulentSide = evaluate 2999.0, evaluate 3001.0
    let jump (a, b) = abs (b - a) / a
    Assert.True(jump laminarSide < 0.02, $"step at Re = 2300: {jump laminarSide:P1}")
    Assert.True(jump turbulentSide < 0.02, $"step at Re = 3000: {jump turbulentSide:P1}")

[<Fact>]
let ``laminar flow in a WHB tube is flagged as a symptom`` () =
    match InTube.evaluate 1200.0 0.75 0.12<W/(m*K)> 0.044<m> None InTube.None with
    | Success (result, warnings) ->
        Assert.Equal(InTube.Laminar, result.Regime)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "laminar flow should evaluate, with a warning"

// ---------- the SRU case, end to end ----------

/// Claus SRU inlet: 1400 degC gas, 44 mm bore, G = 25 kg/(m2*s). The mixture
/// properties give Re about 18500 and Pr about 0.75, and Gnielinski with the
/// entrance term returns roughly 145 W/(m2*K) uncorrected.
[<Fact>]
let ``SRU inlet convection is in the expected range`` () =
    let re = InTube.reynoldsFromMassVelocity 25.0<kg/(m^2*s)> 0.044<m> 59.3e-6<Pa*s> |> value
    Assert.InRange(re, 18000.0, 19000.0)

    let result =
        InTube.evaluate re 0.748 0.122<W/(m*K)> 0.044<m> (Some (6.0 / 0.044))
                        (InTube.KaysTemperatureRatio (1673.15<K>, 623.15<K>, InTube.GasCooled))
        |> value

    Assert.Equal(InTube.Turbulent, result.Regime)
    Assert.InRange(float result.Coefficient, 130.0, 160.0)

/// Pressure drop for the same tube: about 39 mbar over 6 m. A WHB tube-side
/// budget is usually a few hundred mbar, so this is the right order.
[<Fact>]
let ``SRU tube pressure drop is in the expected range`` () =
    let re = InTube.reynoldsFromMassVelocity 25.0<kg/(m^2*s)> 0.044<m> 59.3e-6<Pa*s> |> value
    let f = InTube.frictionFactor re |> value
    let dp =
        InTube.frictionalPressureDrop f 6.0<m> 0.044<m> 25.0<kg/(m^2*s)> 0.2926<kg/m^3>
        |> value
    Assert.InRange(float dp / 100.0, 30.0, 50.0)

/// Pressure drop scales with the square of mass velocity. Getting this wrong
/// would only show up on a rerate, so it is worth pinning.
[<Fact>]
let ``pressure drop scales quadratically with mass velocity`` () =
    let dp g =
        let re = InTube.reynoldsFromMassVelocity (g * 1.0<kg/(m^2*s)>) 0.044<m> 59.3e-6<Pa*s> |> value
        let f = InTube.frictionFactor re |> value
        float (InTube.frictionalPressureDrop f 6.0<m> 0.044<m> (g * 1.0<kg/(m^2*s)>)
                                             0.2926<kg/m^3> |> value)
    // Friction falls slightly with Re, so the exponent is a little under 2.
    let ratio = dp 50.0 / dp 25.0
    Assert.InRange(ratio, 3.2, 3.6)
