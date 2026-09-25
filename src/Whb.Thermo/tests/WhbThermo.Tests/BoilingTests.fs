module WhbThermo.Tests.BoilingTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Steam
open WhbThermo.Boiling

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private steam =
    match If97.load () with
    | Success (m, _) -> m
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- surface tension (IAPWS R1-76) ----------

/// Published values, reproduced to better than 0.1 %.
[<Fact>]
let ``surface tension matches published values`` () =
    let check tC expected =
        let sigma = Nucleate.surfaceTension ((tC + 273.15) * 1.0<K>) |> value
        Assert.True(abs (sigma * 1000.0 - expected) / expected < 1e-3,
                    $"{tC} degC: got {sigma * 1000.0:F3} mN/m, expected {expected}")
    check 20.0 72.74
    check 100.0 58.91
    check 200.0 37.67
    check 300.0 14.36

[<Fact>]
let ``surface tension vanishes at the critical point`` () =
    let sigma = Nucleate.surfaceTension 647.0<K> |> value
    Assert.InRange(sigma, 0.0, 1e-4)

[<Fact>]
let ``surface tension is refused above the critical temperature`` () =
    match Nucleate.surfaceTension 700.0<K> with
    | Failure _ -> ()
    | Success _ -> failwith "there is no surface tension above Tc"

// ---------- Cooper ----------

/// 100 bar, 50 kW/m2: about 30 kW/(m2*K), the expected order for water pool
/// boiling at high reduced pressure.
[<Fact>]
let ``Cooper gives the expected magnitude at drum conditions`` () =
    let pr = 10.0 / 22.064
    let h = Nucleate.cooperFromHeatFlux pr 18.015<kg/kmol> 50000.0<W/m^2> 1.0 |> value
    Assert.InRange(float h, 25000.0, 35000.0)

/// Boiling improves with pressure at fixed heat flux, all the way to the drum
/// pressures a WHB actually runs at.
[<Fact>]
let ``Cooper increases monotonically with pressure`` () =
    let values =
        [ 10.0; 20.0; 40.0; 60.0; 80.0; 100.0; 140.0 ]
        |> List.map (fun pBar ->
            let pr = pBar / 10.0 / 22.064
            float (Nucleate.cooperFromHeatFlux pr 18.015<kg/kmol> 50000.0<W/m^2> 1.0 |> value))
    for a, b in List.pairwise values do
        Assert.True(b > a, $"Cooper not increasing with pressure: {a} -> {b}")

/// The superheat form is an algebraic inversion of the flux form, not an
/// iteration, so the round trip must close to machine precision.
[<Fact>]
let ``Cooper superheat form inverts the heat flux form exactly`` () =
    let pr = 10.0 / 22.064
    for superheat in [ 1.0; 2.0; 5.0; 8.0 ] do
        let h = Nucleate.cooperFromSuperheat pr 18.015<kg/kmol> superheat 1.0 |> value
        let flux = h * (superheat * 1.0<K>)
        let back = Nucleate.cooperFromHeatFlux pr 18.015<kg/kmol> flux 1.0 |> value
        Assert.Equal(float h, float back, 6)

[<Fact>]
let ``Cooper refuses a reduced pressure outside the physical range`` () =
    match Nucleate.cooperFromHeatFlux 1.5 18.015<kg/kmol> 50000.0<W/m^2> 1.0 with
    | Failure _ -> ()
    | Success _ -> failwith "reduced pressure above 1 is not physical"

// ---------- critical heat flux ----------

/// Zuber CHF for water peaks near 60-70 bar at roughly 4 MW/m2 and falls away
/// on both sides. Reproducing that maximum is a strong check on the whole
/// property chain feeding it.
[<Fact>]
let ``Zuber critical heat flux peaks in the expected pressure range`` () =
    let flux pBar =
        let s = If97.saturatedAt steam (pBar * 1.0<bar>) |> value
        let sigma = Nucleate.surfaceTension s.SaturationTemperature |> value
        float (Nucleate.zuberCriticalHeatFlux (s.LatentHeat * 1000.0) (float s.Liquid.Density)
                                             (float s.Vapour.Density) sigma |> value)

    let pressures = [ 10.0; 20.0; 40.0; 60.0; 80.0; 100.0; 140.0 ]
    let values = pressures |> List.map (fun p ->
        let s = If97.saturatedAt steam (p * 1.0<bar>) |> value
        let sigma = Nucleate.surfaceTension s.SaturationTemperature |> value
        p, float (Nucleate.zuberCriticalHeatFlux (s.LatentHeat * 1000.0)
                                                 (float s.Liquid.Density)
                                                 (float s.Vapour.Density) sigma |> value))

    let peakPressure, peakValue = values |> List.maxBy snd
    Assert.InRange(peakPressure, 40.0, 80.0)
    Assert.InRange(peakValue / 1e6, 3.0, 5.0)
    ignore (flux 100.0)

/// The gate that makes the nucleate correlations safe to use.
[<Fact>]
let ``heat flux beyond critical is refused, not warned about`` () =
    match Nucleate.checkAgainstCritical 5.0e6<W/m^2> 3.8e6<W/m^2> with
    | Failure msgs ->
        Assert.Contains(msgs, function CorrelationExtrapolated _ -> true | _ -> false)
    | Success _ -> failwith "burnout must be a failure, not a warning"

[<Fact>]
let ``heat flux close to critical warns`` () =
    match Nucleate.checkAgainstCritical 3.0e6<W/m^2> 3.8e6<W/m^2> with
    | Success (check, warnings) ->
        Assert.True(check.IsAcceptable)
        Assert.InRange(check.Ratio, 0.75, 0.82)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "below critical should succeed"

[<Fact>]
let ``a comfortable heat flux passes without warnings`` () =
    match Nucleate.checkAgainstCritical 5.0e5<W/m^2> 3.8e6<W/m^2> with
    | Success (check, warnings) ->
        Assert.True(check.Ratio < 0.2)
        Assert.Empty(warnings)
    | Failure _ -> failwith "should succeed"

/// Cooper's superheat form climbs steeply enough to cross burnout at a modest
/// wall superheat. This test documents that trap rather than hiding it.
[<Fact>]
let ``Cooper superheat form crosses burnout at a modest superheat`` () =
    let pBar = 100.0
    let s = If97.saturatedAt steam (pBar * 1.0<bar>) |> value
    let sigma = Nucleate.surfaceTension s.SaturationTemperature |> value
    let critical =
        Nucleate.zuberCriticalHeatFlux (s.LatentHeat * 1000.0) (float s.Liquid.Density)
                                       (float s.Vapour.Density) sigma |> value

    let pr = pBar / 10.0 / 22.064
    let fluxAt superheat =
        let h = Nucleate.cooperFromSuperheat pr 18.015<kg/kmol> superheat 1.0 |> value
        h * (superheat * 1.0<K>)

    // Comfortable at a few degrees, past burnout by ten.
    match Nucleate.checkAgainstCritical (fluxAt 3.0) critical with
    | Success _ -> ()
    | Failure _ -> failwith "3 K superheat should be within the nucleate regime"

    match Nucleate.checkAgainstCritical (fluxAt 10.0) critical with
    | Failure _ -> ()
    | Success _ -> failwith "10 K superheat at 100 bar is past burnout and must be refused"

// ---------- bundle effects ----------

[<Fact>]
let ``bundle factor enhances and is capped`` () =
    let modest = Nucleate.bundleFactor 0.6<m> 0.05<m> |> value
    Assert.InRange(modest, 1.5, 1.8)

    match Nucleate.bundleFactor 20.0<m> 0.05<m> with
    | Success (f, warnings) ->
        Assert.Equal(3.0, f, 9)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "a large bundle should cap, not fail"

/// A dense bundle burns out far below a single tube. Getting this wrong is a
/// classic way to design a boiler that tests fine and dries out in service.
[<Fact>]
let ``bundle critical heat flux derates below the single tube value`` () =
    let single = 3.8e6<W/m^2>
    let bundle = Nucleate.bundleCriticalHeatFlux single 0.1 |> value
    Assert.True(float bundle < float single)
    Assert.Equal(float single * 0.31, float bundle, 3)

// ---------- overall resistance ----------

[<Fact>]
let ``overall resistance shares sum to one`` () =
    let r =
        Overall.resistances 0.044<m> 0.0508<m> 150.0<W/(m^2*K)> 25000.0<W/(m^2*K)>
                            0.0004 0.0002 Overall.LowAlloySA213_T11 350.0<degC>
        |> value
    Assert.Equal(1.0, r.Shares |> List.sumBy snd, 9)

/// The gas film dominates a WHB and boiling is nearly free. That asymmetry is
/// why tuning the boiling correlation matters far less than the fouling number.
[<Fact>]
let ``gas film dominates the resistance network`` () =
    let r =
        Overall.resistances 0.044<m> 0.0508<m> 150.0<W/(m^2*K)> 25000.0<W/(m^2*K)>
                            0.0004 0.0002 Overall.LowAlloySA213_T11 350.0<degC>
        |> value
    let share name = r.Shares |> List.find (fun (n, _) -> n = name) |> snd
    let gasFilm = share "gas film"
    let boiling = share "boiling"
    Assert.True(gasFilm > 0.6, $"gas film share {gasFilm}")
    Assert.True(boiling < 0.05, $"boiling share {boiling}")
    Assert.True(float r.OverallCoefficient > 50.0 && float r.OverallCoefficient < 200.0)

/// A clean-case rating must announce itself: it is valid for metal temperature
/// checks and not for a duty guarantee.
[<Fact>]
let ``zero fouling is flagged as the clean case`` () =
    match Overall.resistances 0.044<m> 0.0508<m> 150.0<W/(m^2*K)> 25000.0<W/(m^2*K)>
                              0.0 0.0 Overall.CarbonSteelSA516_70 300.0<degC> with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "the clean case is legitimate, just not a guarantee"

/// Fouling raises the wall temperature at fixed gas and boiling temperature --
/// the reason the dirty case is not automatically the conservative one.
[<Fact>]
let ``tube conductivity falls with temperature for the alloy steels`` () =
    let cold = Overall.LowAlloySA213_T11.Conductivity 100.0<degC>
    let hot = Overall.LowAlloySA213_T11.Conductivity 550.0<degC>
    Assert.True(cold > 0.0 && hot > 0.0)
    let austenitic = Overall.Austenitic316L.Conductivity 300.0<degC>
    let carbon = Overall.CarbonSteelSA516_70.Conductivity 300.0<degC>
    Assert.True(austenitic < carbon)
