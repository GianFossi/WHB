module WhbThermo.Tests.XSulfurConsolidatedTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open XSulfur

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private model =
    match Speciation.load () with
    | Success (m, _) -> m
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private kelvin c = (c + 273.15) * 1.0<K>

// ---------- chemistry ----------

[<Fact>]
let ``the liquid reference phase is loaded`` () =
    Assert.True(model.Liquid.IsSome, "vapour pressure needs the liquid phase")

/// Derived from CEA Gibbs energies, not from a separate correlation, so the
/// vapour pressure is consistent with the speciation and the latent heats.
[<Fact>]
let ``vapour pressure matches the expected magnitudes`` () =
    let pa tC = float (Chemistry.vapourPressure model (kelvin tC) |> value) * 1.0e5
    Assert.InRange(pa 120.0, 5.0, 7.0)
    Assert.InRange(pa 150.0, 30.0, 40.0)
    Assert.InRange(pa 300.0, 6500.0, 7800.0)

[<Fact>]
let ``vapour pressure rises monotonically`` () =
    let values =
        [ 120.0; 150.0; 200.0; 250.0; 300.0; 350.0 ]
        |> List.map (fun tC -> float (Chemistry.vapourPressure model (kelvin tC) |> value))
    for a, b in List.pairwise values do
        Assert.True(b > a)

/// The correlation gives 0.68 bar at the normal boiling point against 1.013
/// expected, so it must refuse above 350 degC rather than extrapolate.
[<Fact>]
let ``vapour pressure is refused outside its honest range`` () =
    for tC in [ 100.0; 400.0; 444.6 ] do
        match Chemistry.vapourPressure model (kelvin tC) with
        | Failure _ -> ()
        | Success _ -> failwith $"{tC} degC is outside the validated window"

/// Dew point and vapour pressure are inverses, so round-tripping is a real
/// check on the bisection.
[<Fact>]
let ``dew point inverts the vapour pressure curve`` () =
    for tC in [ 150.0; 220.0; 280.0; 330.0 ] do
        let p = Chemistry.vapourPressure model (kelvin tC) |> value
        let back = Chemistry.dewPoint model p |> value
        Assert.True(abs (float back - float (kelvin tC)) < 0.01,
                    $"round trip at {tC} degC gave {float back - 273.15}")

/// At Claus condenser partial pressures the dew point lands where it should.
[<Fact>]
let ``dew point is in the Claus range for typical sulfur pressures`` () =
    let dew p = float (Chemistry.dewPoint model (p * 1.0<bar>) |> value) - 273.15
    Assert.InRange(dew 0.01, 220.0, 240.0)
    Assert.InRange(dew 0.05, 275.0, 295.0)

/// Marching a profile: the gas-phase sulfur must be capped at saturation once
/// liquid is present, and the rest reported as condensate.
[<Fact>]
let ``condenser state caps the vapour at saturation and reports condensate`` () =
    let inlet = 0.05<bar>
    let hot = Chemistry.condenserState model (kelvin 320.0) inlet |> value
    Assert.False(hot.IsSaturated)
    Assert.Equal(0.0, hot.CondensedFraction, 9)

    let cold = Chemistry.condenserState model (kelvin 170.0) inlet |> value
    Assert.True(cold.IsSaturated)
    Assert.True(cold.CondensedFraction > 0.95,
                $"at 170 degC most sulfur should be out: {cold.CondensedFraction:P1}")
    Assert.True(float cold.VapourPressure <= float cold.SaturationPressure + 1e-12)

[<Fact>]
let ``recovery increases monotonically as the gas cools`` () =
    let recovered tC =
        (Chemistry.condenserState model (kelvin tC) 0.05<bar> |> value).CondensedFraction
    let values = [ 280.0; 250.0; 220.0; 190.0; 160.0 ] |> List.map recovered
    for a, b in List.pairwise values do
        Assert.True(b >= a)

// ---------- liquid properties ----------

/// The published anchors, reproduced exactly at the anchor temperatures.
[<Fact>]
let ``liquid viscosity matches the published anchors`` () =
    let mu tC = LiquidProperties.viscosity (kelvin tC) |> value
    Assert.Equal(7.0e-3, mu 155.0, 6)
    Assert.Equal(4.0, mu 165.0, 6)
    Assert.Equal(93.0, mu 187.0, 6)

/// The whole reason the wall window exists. Ten degrees across the lambda
/// transition costs a factor of about 570 in viscosity.
[<Fact>]
let ``the lambda transition raises the viscosity by orders of magnitude`` () =
    let penalty tC = LiquidProperties.viscosityPenalty (kelvin tC) |> value
    Assert.InRange(penalty 155.0, 0.99, 1.01)
    Assert.InRange(penalty 160.0, 3.0, 4.5)
    Assert.InRange(penalty 165.0, 500.0, 650.0)
    Assert.InRange(penalty 187.0, 12000.0, 15000.0)

/// Interpolation is on log viscosity: a linear interpolation between the 159.4
/// and 165 degC anchors would understate the middle of the transition badly.
[<Fact>]
let ``viscosity interpolation is logarithmic through the transition`` () =
    let mu163 = LiquidProperties.viscosity (kelvin 163.0) |> value
    let linear = 9.0e-3 + (4.0 - 9.0e-3) * (163.0 - 159.4) / (165.0 - 159.4)
    Assert.True(mu163 < linear * 0.5,
                $"log interpolation gives {mu163:g3}, linear would give {linear:g3}")

[<Fact>]
let ``viscosity above the lambda transition always warns`` () =
    match LiquidProperties.viscosity (kelvin 165.0) with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``liquid density and surface tension fall with temperature`` () =
    let rho tC = LiquidProperties.density (kelvin tC) |> value
    let sigma tC = LiquidProperties.surfaceTension (kelvin tC) |> value
    Assert.True(rho 120.0 > rho 200.0)
    Assert.InRange(rho 120.0, 1780.0, 1830.0)
    Assert.True(sigma 120.0 > sigma 200.0)

/// Ring opening absorbs energy, so the heat capacity peaks at the transition
/// too. A constant cp across the window misses it.
[<Fact>]
let ``liquid heat capacity peaks at the lambda transition`` () =
    let cp tC = LiquidProperties.heatCapacity (kelvin tC) |> value
    Assert.True(cp 159.4 > cp 130.0 * 1.5)
    Assert.True(cp 159.4 > cp 200.0)

[<Fact>]
let ``the lambda margin has the right sign`` () =
    Assert.True(LiquidProperties.lambdaMargin (kelvin 150.0) > 0.0)
    Assert.True(LiquidProperties.lambdaMargin (kelvin 165.0) < 0.0)

// ---------- film kinetics ----------

[<Fact>]
let ``the Nusselt film gives a plausible coefficient and thickness`` () =
    let film =
        FilmKinetics.nusseltHorizontalFilm (kelvin 140.0) (kelvin 150.0) 0.03 300.0e3 0.025
        |> value
    Assert.True(float film.Coefficient > 200.0 && float film.Coefficient < 5000.0,
                $"film coefficient {float film.Coefficient}")
    Assert.True(film.Thickness > 0.0 && film.Thickness < 1.0e-3,
                $"film thickness {film.Thickness}")
    Assert.False(film.IsPolymerised)

/// The Nusselt analysis assumes a Newtonian film. Past the lambda transition
/// that assumption is void and the result must say so.
[<Fact>]
let ``a film past the lambda transition is flagged`` () =
    match FilmKinetics.nusseltHorizontalFilm (kelvin 165.0) (kelvin 175.0) 0.03 300.0e3 0.025 with
    | Success (film, warnings) ->
        Assert.True(film.IsPolymerised)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``a wall hotter than saturation is refused`` () =
    match FilmKinetics.nusseltHorizontalFilm (kelvin 160.0) (kelvin 150.0) 0.03 300.0e3 0.025 with
    | Failure _ -> ()
    | Success _ -> failwith "no condensation onto a wall above saturation"

/// Drainage depth goes as flow and viscosity. Crossing the lambda transition
/// deepens the bottom stream dramatically at the same condensate load.
[<Fact>]
let ``drainage depth grows sharply across the lambda transition`` () =
    let depth tC =
        (FilmKinetics.drainage 0.002 0.025 6.0 0.01 15.0 1.2 (kelvin tC) |> value).Depth
    let cold = depth 150.0
    let hot = depth 165.0
    Assert.True(hot > cold * 3.0,
                $"depth {cold:g3} m at 150 degC against {hot:g3} m at 165 degC")

[<Fact>]
let ``drainage hold-up and residence time are physical`` () =
    let state = FilmKinetics.drainage 0.002 0.025 6.0 0.01 15.0 1.2 (kelvin 150.0) |> value
    Assert.InRange(state.HoldUp, 0.0, 1.0)
    Assert.True(state.Velocity > 0.0)
    Assert.True(Double.IsFinite state.ResidenceTime)

[<Fact>]
let ``zero slope leaves the condensate undrained`` () =
    let state = FilmKinetics.drainage 0.002 0.025 6.0 0.0 15.0 1.2 (kelvin 150.0) |> value
    Assert.Equal(0.0, state.Depth, 9)

/// A polymerised film does not drain at any slope, and the verdict must say
/// that rather than report a hold-up figure suggesting a dimensional fix.
[<Fact>]
let ``the verdict puts the lambda transition ahead of the geometry`` () =
    let film =
        FilmKinetics.nusseltHorizontalFilm (kelvin 140.0) (kelvin 150.0) 0.03 300.0e3 0.025
        |> value
    let drainageState =
        FilmKinetics.drainage 0.002 0.025 6.0 0.02 15.0 1.2 (kelvin 150.0) |> value

    match FilmKinetics.assess film drainageState (kelvin 165.0) |> value with
    | FilmKinetics.DoesNotDrain reason -> Assert.Contains("lambda", reason)
    | other -> failwith $"expected a lambda verdict, got {other}"

    match FilmKinetics.assess film drainageState (kelvin 110.0) |> value with
    | FilmKinetics.DoesNotDrain reason -> Assert.Contains("melting point", reason)
    | other -> failwith $"expected a freezing verdict, got {other}"

[<Fact>]
let ``a film with under four kelvin of margin drains only marginally`` () =
    let film =
        FilmKinetics.nusseltHorizontalFilm (kelvin 140.0) (kelvin 150.0) 0.03 300.0e3 0.025
        |> value
    let drainageState =
        FilmKinetics.drainage 0.002 0.025 6.0 0.02 15.0 1.2 (kelvin 150.0) |> value
    match FilmKinetics.assess film drainageState (kelvin 157.0) |> value with
    | FilmKinetics.DrainsMarginally _ -> ()
    | other -> failwith $"expected a marginal verdict, got {other}"

/// High viscosity resists entrainment, so polymerised sulfur is harder to carry
/// out of the tube - which is not good news, because it means it stays there.
[<Fact>]
let ``entrainment becomes harder above the lambda transition`` () =
    let cold = FilmKinetics.entrainment 20.0 1.2 2.0e-4 (kelvin 150.0) |> value
    let hot = FilmKinetics.entrainment 20.0 1.2 2.0e-4 (kelvin 170.0) |> value
    Assert.True(hot.ViscosityNumber > cold.ViscosityNumber)
    Assert.True(hot.Threshold >= cold.Threshold)

// ---------- checks ----------

[<Fact>]
let ``the wall window alarms below the melting point and above lambda`` () =
    Assert.Equal(Checks.Alarm, (Checks.wallWindow (kelvin 110.0)).Severity)
    Assert.Equal(Checks.Alarm, (Checks.wallWindow (kelvin 165.0)).Severity)

[<Fact>]
let ``the wall window accepts the design band and watches the edges`` () =
    Assert.Equal(Checks.Ok, (Checks.wallWindow (kelvin 140.0)).Severity)
    Assert.Equal(Checks.Watch, (Checks.wallWindow (kelvin 157.0)).Severity)
    Assert.Equal(Checks.Watch, (Checks.wallWindow (kelvin 122.0)).Severity)

/// The published limits, pinned so they cannot drift: the monoclinic melting
/// point is the freezing bound a control system must respect, and 159.4 degC is
/// the lambda transition.
[<Fact>]
let ``the wall window constants are the published values`` () =
    Assert.Equal(115.21, Chemistry.MeltingPointRhombicC, 2)
    Assert.Equal(119.6, Chemistry.MeltingPointMonoclinicC, 2)
    Assert.Equal(159.4, Chemistry.LambdaTransitionC, 2)

[<Fact>]
let ``fog needs all three conditions together`` () =
    let assess ss slope le = Checks.assessFog ss slope 1.0 le |> value
    Assert.True((assess 1.2 1.5 1.3).FogLikely)
    Assert.False((assess 1.0 1.5 1.3).FogLikely)    // not supersaturated
    Assert.False((assess 1.2 0.8 1.3).FogLikely)    // cooling slower than the dew curve
    Assert.False((assess 1.2 1.5 0.8).FogLikely)    // Lewis below one

[<Fact>]
let ``sulfidation and wet H2S grade as expected`` () =
    Assert.Equal(Checks.Ok, (Checks.sulfidation (kelvin 240.0) 0.03).Severity)
    Assert.Equal(Checks.Watch, (Checks.sulfidation (kelvin 300.0) 0.03).Severity)
    Assert.Equal(Checks.Alarm, (Checks.sulfidation (kelvin 360.0) 0.03).Severity)
    Assert.Equal(Checks.Ok, (Checks.sulfidation (kelvin 360.0) 1.0e-6).Severity)
    Assert.Equal(Checks.Alarm, (Checks.wetH2S (kelvin 40.0) (kelvin 60.0) 0.03).Severity)
    Assert.Equal(Checks.Ok, (Checks.wetH2S (kelvin 80.0) (kelvin 60.0) 0.03).Severity)

[<Fact>]
let ``the worst severity governs the point`` () =
    let checks =
        [ Checks.wallWindow (kelvin 140.0)
          Checks.sulfidation (kelvin 300.0) 0.03 ]
    Assert.Equal(Checks.Watch, Checks.worst checks)
    Assert.Equal(Checks.Alarm, Checks.worst (Checks.wallWindow (kelvin 165.0) :: checks))

// ---------- condensation bridge ----------

/// Heat of condensation must include the polymerisation contribution, so it
/// exceeds a bare S8 latent heat and falls as the vapour gets lighter.
[<Fact>]
let ``heat of condensation is in the right range and falls with temperature`` () =
    let h tC =
        Condensation.heatOfCondensation model (kelvin tC)
            (Chemistry.vapourPressure model (kelvin tC) |> value)
        |> value
    Assert.InRange(h 150.0 / 1000.0, 300.0, 380.0)
    Assert.InRange(h 300.0 / 1000.0, 250.0, 310.0)
    Assert.True(h 150.0 > h 300.0)

[<Fact>]
let ``the mass transfer coefficient follows Chilton-Colburn`` () =
    let k = Condensation.gasMassTransferFromHtc 60.0<W/(m^2*K)> 1.2 1100.0 1.0 |> value
    Assert.Equal(60.0 / (1.2 * 1100.0), k, 9)
    let higherLewis = Condensation.gasMassTransferFromHtc 60.0<W/(m^2*K)> 1.2 1100.0 1.5 |> value
    Assert.True(higherLewis < k, "a larger Lewis number reduces k_G")

/// The generic Silver / Bell & Ghaly method, reached through the sulfur
/// wrapper: there is one implementation, not two.
[<Fact>]
let ``the effective coefficient uses the shared Silver-Bell-Ghaly method`` () =
    let direct =
        WhbThermo.TwoPhase.Condensation.silverBellGhaly 1500.0<W/(m^2*K)> 60.0<W/(m^2*K)>
            (0.5 * 1100.0 * 2.0e-4)
        |> value
    let viaSulfur =
        Condensation.effectiveCoefficient 1500.0<W/(m^2*K)> 60.0<W/(m^2*K)> 0.5 1100.0 2.0e-4
        |> value
    Assert.Equal(float direct, float viaSulfur, 9)

/// The gas-phase resistance dominates in a Claus condenser, because N2, CO2 and
/// H2O are most of the gas. The film is nearly free by comparison.
[<Fact>]
let ``the gas phase governs the effective coefficient`` () =
    let effective =
        Condensation.effectiveCoefficient 1500.0<W/(m^2*K)> 60.0<W/(m^2*K)> 0.5 1100.0 2.0e-4
        |> value
    Assert.True(float effective < 450.0,
                $"effective {float effective} should be far below the film coefficient")
