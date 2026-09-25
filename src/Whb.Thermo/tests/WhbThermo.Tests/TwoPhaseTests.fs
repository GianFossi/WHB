module WhbThermo.Tests.TwoPhaseTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.TwoPhase

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Saturated water at 10 bar, from IF97 and IAPWS R1-76.
let private water : PhaseProperties =
    { LiquidDensity = 887.1
      VapourDensity = 5.15
      LiquidViscosity = 1.505e-4
      VapourViscosity = 1.503e-5
      LiquidConductivity = 0.6773
      VapourConductivity = 0.0339
      LiquidHeatCapacity = 4405.0
      VapourHeatCapacity = 2710.0
      SurfaceTension = 0.04246
      LatentHeat = 2014.6e3
      ReducedPressure = 1.0 / 22.064
      MolarMass = 18.015 }

let private massVelocity = 300.0
let private diameter = 0.025
let private saturationSlope = 23012.0   // dP/dT at 10 bar, Pa/K

// ---------- dimensionless groups ----------

/// The Martinelli parameter falls with quality: more vapour means less liquid
/// to shear. A rising X_tt would invert every enhancement factor built on it.
[<Fact>]
let ``Martinelli parameter falls with quality`` () =
    let values =
        [ 0.05; 0.1; 0.3; 0.6; 0.9 ]
        |> List.map (fun x -> Flow.martinelli x water |> value)
    for a, b in List.pairwise values do
        Assert.True(b < a, $"X_tt not decreasing: {a} -> {b}")

[<Fact>]
let ``Martinelli parameter is refused at the single phase limits`` () =
    for x in [ 0.0; 1.0 ] do
        match Flow.martinelli x water with
        | Failure _ -> ()
        | Success _ -> failwith $"quality {x} is single phase"

// ---------- Chen ----------

/// The two mechanisms move in opposite directions with quality: convection is
/// enhanced, nucleate boiling suppressed. If both moved the same way, one of
/// the factors would be wired backwards.
[<Fact>]
let ``Chen enhances convection and suppresses nucleation as quality rises`` () =
    let results =
        [ 0.05; 0.1; 0.2; 0.4; 0.6 ]
        |> List.map (fun x ->
            FlowBoiling.chen massVelocity x diameter water 5.0 saturationSlope |> value)

    for a, b in List.pairwise results do
        Assert.True(b.Enhancement > a.Enhancement, "F must rise with quality")
        Assert.True(b.Suppression < a.Suppression, "S must fall with quality")
        Assert.True(b.Convective > a.Convective, "the convective term must rise")
        Assert.True(b.Nucleate < a.Nucleate, "the nucleate term must fall")

/// Magnitudes at 10 bar, G = 300 kg/(m²·s), 5 K superheat.
[<Fact>]
let ``Chen gives the expected magnitude for water at 10 bar`` () =
    let low = FlowBoiling.chen massVelocity 0.05 diameter water 5.0 saturationSlope |> value
    let high = FlowBoiling.chen massVelocity 0.6 diameter water 5.0 saturationSlope |> value
    Assert.InRange(float low.Coefficient, 9000.0, 14000.0)
    Assert.InRange(float high.Coefficient, 26000.0, 35000.0)

/// At low quality nucleate boiling carries a large share; by x = 0.6 it is
/// almost gone. That crossover is the physical content of the correlation.
[<Fact>]
let ``the nucleate share collapses across the quality range`` () =
    let share (x: float) =
        let r = FlowBoiling.chen massVelocity x diameter water 5.0 saturationSlope |> value
        r.Nucleate / float r.Coefficient
    Assert.True(share 0.05 > 0.25, "nucleate boiling should matter at low quality")
    Assert.True(share 0.6 < 0.05, "nucleate boiling should be nearly gone at high quality")

/// Chen has no dryout model and will keep predicting a rising coefficient past
/// the point where the wall goes dry. It must say so.
[<Fact>]
let ``Chen warns at high quality where dryout is not modelled`` () =
    match FlowBoiling.chen massVelocity 0.85 diameter water 5.0 saturationSlope with
    | Success (_, warnings) ->
        Assert.Contains(warnings, function CorrelationExtrapolated _ -> true | _ -> false)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``Forster-Zuber requires a positive superheat and pressure slope`` () =
    match FlowBoiling.forsterZuber water 0.0 saturationSlope with
    | Failure _ -> ()
    | Success _ -> failwith "zero superheat has no nucleate boiling"
    match FlowBoiling.forsterZuber water 5.0 0.0 with
    | Failure _ -> ()
    | Success _ -> failwith "a zero saturation slope is not physical"

// ---------- Steiner-Taborek ----------

[<Fact>]
let ``the Steiner-Taborek convective multiplier rises with quality`` () =
    let values =
        [ 0.0; 0.1; 0.3; 0.6; 0.9 ]
        |> List.map (fun x -> FlowBoiling.steinerTaborekConvective x water)
    Assert.Equal(1.0, values.Head, 6)   // at x = 0 the multiplier must be unity
    for a, b in List.pairwise values do
        Assert.True(b > a)

[<Fact>]
let ``Steiner-Taborek gives the expected magnitude for water at 10 bar`` () =
    let result =
        FlowBoiling.steinerTaborek massVelocity 0.2 diameter water 50000.0 1.0 25580.0
        |> value
    Assert.InRange(float result.Coefficient, 25000.0, 32000.0)

/// The asymptotic form must always exceed either mechanism alone, and never
/// their sum.
[<Fact>]
let ``the asymptotic combination is bounded by its parts`` () =
    for x in [ 0.05; 0.2; 0.5; 0.8 ] do
        let r =
            FlowBoiling.steinerTaborek massVelocity x diameter water 50000.0 1.0 25580.0
            |> value
        let total = float r.Coefficient
        Assert.True(total >= max r.Convective r.Nucleate)
        Assert.True(total <= r.Convective + r.Nucleate)

/// The two models disagree by about a factor of two at low quality and converge
/// as convection takes over. This is model uncertainty, not an implementation
/// error, and the test pins the size of it so a future change cannot quietly
/// alter the picture.
[<Fact>]
let ``Chen and Steiner-Taborek disagree most at low quality`` () =
    let ratio x =
        let chen =
            float (FlowBoiling.chen massVelocity x diameter water 5.0 saturationSlope
                   |> value).Coefficient
        let steiner =
            float (FlowBoiling.steinerTaborek massVelocity x diameter water 50000.0 1.0 25580.0
                   |> value).Coefficient
        steiner / chen

    Assert.InRange(ratio 0.05, 1.7, 2.6)
    Assert.InRange(ratio 0.6, 1.1, 1.7)
    Assert.True(ratio 0.05 > ratio 0.6, "the gap must narrow as quality rises")

[<Fact>]
let ``Steiner-Taborek warns near the critical pressure`` () =
    let nearCritical = { water with ReducedPressure = 0.97 }
    match FlowBoiling.steinerTaborekNucleate nearCritical 50000.0 diameter 1.0 with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> ()

// ---------- condensation ----------

/// Shah and Boyko-Kruzhilin are two-phase multipliers on the all-liquid
/// coefficient, so both must return exactly that at x = 0.
[<Fact>]
let ``Shah and Boyko-Kruzhilin reduce to the all-liquid coefficient at zero quality`` () =
    let shah = Condensation.shah massVelocity 0.0 diameter water |> value
    let boyko = Condensation.boykoKruzhilin massVelocity 0.0 diameter water |> value
    Assert.Equal(float shah, float boyko, 6)
    Assert.InRange(float shah, 3300.0, 3800.0)

/// Cavallini-Zecchin is an independently fitted correlation, not a multiplier,
/// so it does NOT reduce to the same limit. Asserting that it should would be
/// asserting a misunderstanding.
[<Fact>]
let ``Cavallini-Zecchin does not reduce to the all-liquid coefficient`` () =
    let shah = Condensation.shah massVelocity 0.0 diameter water |> value
    let cavallini = Condensation.cavalliniZecchin massVelocity 0.0 diameter water |> value
    Assert.True(float cavallini > float shah * 1.5,
                "Cavallini-Zecchin uses a different leading constant by construction")

[<Fact>]
let ``all condensation correlations rise with quality`` () =
    for name, correlation in
        [ "Shah", Condensation.shah
          "Boyko-Kruzhilin", Condensation.boykoKruzhilin
          "Cavallini-Zecchin", Condensation.cavalliniZecchin ] do
        let values =
            [ 0.1; 0.3; 0.5; 0.7; 0.9 ]
            |> List.map (fun x -> float (correlation massVelocity x diameter water |> value))
        for a, b in List.pairwise values do
            Assert.True(b > a, $"{name} is not increasing with quality")

/// The three correlations span roughly 50 % at mid quality. That spread is the
/// real uncertainty in a condenser rating, and it dwarfs any refinement of the
/// property data feeding it.
[<Fact>]
let ``the condensation correlations span the expected range`` () =
    let x = 0.5
    let values =
        [ Condensation.shah; Condensation.boykoKruzhilin; Condensation.cavalliniZecchin ]
        |> List.map (fun c -> float (c massVelocity x diameter water |> value))
    let spread = List.max values / List.min values
    Assert.InRange(spread, 1.2, 1.6)

// ---------- Silver / Bell & Ghaly ----------

/// For a pure vapour Z = 0 and the effective coefficient is the condensing one.
[<Fact>]
let ``Silver-Bell-Ghaly reduces to the condensing coefficient for a pure vapour`` () =
    let h = Condensation.silverBellGhaly 8000.0<W/(m^2*K)> 200.0<W/(m^2*K)> 0.0 |> value
    Assert.Equal(8000.0, float h, 6)

/// The effect that governs mixture condensers. A Z of only 0.2 cuts the
/// coefficient from 8000 to 889 W/(m²·K) - a factor of nine - because the
/// gas-phase resistance takes over. Ignoring this overpredicts the coefficient
/// severalfold, which is the classic way a hydrocarbon condenser comes up short.
[<Fact>]
let ``the gas phase resistance dominates once Z is appreciable`` () =
    let h z = float (Condensation.silverBellGhaly 8000.0<W/(m^2*K)> 200.0<W/(m^2*K)> z |> value)
    Assert.InRange(h 0.05, 2500.0, 2900.0)
    Assert.InRange(h 0.2, 850.0, 950.0)
    Assert.InRange(h 1.0, 180.0, 210.0)
    Assert.True(h 0.2 < h 0.0 / 8.0, "a Z of 0.2 must cost nearly an order of magnitude")

[<Fact>]
let ``a large Z raises a warning`` () =
    match Condensation.silverBellGhaly 8000.0<W/(m^2*K)> 200.0<W/(m^2*K)> 0.8 with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``the Z factor is proportional to quality and to the curve slope`` () =
    let z = Condensation.zFactor 0.5 2710.0 1.0e-4 |> value
    Assert.Equal(0.5 * 2710.0 * 1.0e-4, z, 9)
    Assert.Equal(0.0, Condensation.zFactor 0.0 2710.0 1.0e-4 |> value, 9)

// ---------- Colburn & Hougen ----------

let private saturationPressure (t: float<K>) =
    1.0e5 * exp (13.0 - 4900.0 / float t)

[<Fact>]
let ``the Colburn-Hougen residual changes sign across the bracket`` () =
    let residual ti =
        Condensation.colburnHougenResidual 60.0 5000.0 1.5e-5 1.0e5 4.0e4
                                           (saturationPressure ti) 18.015 2.26e6
                                           360.0<K> ti 320.0<K>
    let atWall = residual 320.0<K> |> value
    let higher = residual 330.0<K> |> value
    Assert.True(atWall > 0.0 && higher < 0.0, "the balance must bracket the interface temperature")

/// Bisection is used rather than Newton because the logarithm term stiffens as
/// the interface approaches the bulk composition. The solved interface must sit
/// between the wall and the bulk gas, and drive the residual to zero.
[<Fact>]
let ``Colburn-Hougen solves for an interface temperature inside the bracket`` () =
    match Condensation.solveColburnHougen 60.0 5000.0 1.5e-5 1.0e5 4.0e4
                                          saturationPressure 18.015 2.26e6
                                          360.0<K> 320.0<K> with
    | Success (ti, _) ->
        Assert.InRange(float ti, 320.0, 360.0)
        let residual =
            Condensation.colburnHougenResidual 60.0 5000.0 1.5e-5 1.0e5 4.0e4
                                               (saturationPressure ti) 18.015 2.26e6
                                               360.0<K> ti 320.0<K>
            |> value
        Assert.True(abs residual < 100.0, $"residual {residual} not driven to zero")
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// An unbracketed case must be refused rather than returning an endpoint.
/// With an almost insulating condensate film (1 W/m2K) the residual stays
/// positive from the wall up to the bulk dew point (about 352 K here), so no
/// interface temperature balances the fluxes.
[<Fact>]
let ``Colburn-Hougen refuses an unbracketed case`` () =
    match Condensation.solveColburnHougen 60.0 1.0 1.5e-5 1.0e5 4.0e4
                                          saturationPressure 18.015 2.26e6
                                          360.0<K> 320.0<K> with
    | Failure _ -> ()
    | Success _ -> failwith "an unbracketed case must be refused"

[<Fact>]
let ``the Colburn-Hougen residual is refused when no condensation is driven`` () =
    match Condensation.colburnHougenResidual 60.0 5000.0 1.5e-5 1.0e5 4.0e4
                                             5.0e4 18.015 2.26e6
                                             360.0<K> 340.0<K> 320.0<K> with
    | Failure _ -> ()
    | Success _ -> failwith "an interface pressure above the bulk drives no condensation"
