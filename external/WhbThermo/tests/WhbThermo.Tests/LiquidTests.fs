module WhbThermo.Tests.LiquidTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Liquids

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private db =
    match Dippr.load () with
    | Success (d, _) -> d
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private species key =
    match Dippr.find db key with
    | Success (s, _) -> s
    | Failure _ -> failwith $"missing species {key}"

// ---------- loading and structure ----------

[<Fact>]
let ``the liquid database loads`` () =
    Assert.True(db.Species.Count >= 30)

[<Fact>]
let ``every species has a molar mass and a critical temperature`` () =
    for KeyValue(_, s) in db.Species do
        Assert.True(s.MolarMass > 0.0, s.Key)
        Assert.True(s.Tc.IsSome, $"{s.Key} has no critical temperature")

[<Fact>]
let ``every correlation carries the coefficient count its equation requires`` () =
    for KeyValue(_, s) in db.Species do
        for KeyValue(name, c) in s.Correlations do
            match Dippr.expectedCoefficients c.Equation with
            | Some expected ->
                Assert.True(c.C.Length = expected,
                            $"{s.Key}/{name}: DIPPR {c.Equation} has {c.C.Length}, needs {expected}")
            | None -> failwith $"{s.Key}/{name}: unsupported equation {c.Equation}"

[<Fact>]
let ``every correlation carries a validity range and a source`` () =
    for KeyValue(_, s) in db.Species do
        for KeyValue(name, c) in s.Correlations do
            Assert.True(c.TMin.IsSome && c.TMax.IsSome, $"{s.Key}/{name} has no range")
            Assert.False(String.IsNullOrWhiteSpace c.Source, $"{s.Key}/{name} has no source")
            match c.TMin, c.TMax with
            | Some lo, Some hi -> Assert.True(hi > lo, $"{s.Key}/{name}: tMax <= tMin")
            | _ -> ()

[<Fact>]
let ``a coefficient set of the wrong length is rejected at load`` () =
    let json = """
    { "schemaVersion": "1.0", "licence": "test", "species": [
      { "key": "BAD", "cas": "0", "name": "Bad", "molarMass_g_mol": 40.0, "tcK": 500.0,
        "correlations": { "liquidViscosity":
          { "equation": 101, "c": [1,2], "tMinK": 200, "tMaxK": 400,
            "unit": "Pa*s", "source": "t" } } } ] }
    """
    match Dippr.parse json with
    | Failure [ DatabaseParseError d ] -> Assert.Contains("needs 5", d)
    | _ -> failwith "expected a parse failure"

[<Fact>]
let ``an unsupported equation number is rejected`` () =
    let json = """
    { "schemaVersion": "1.0", "licence": "test", "species": [
      { "key": "BAD", "cas": "0", "name": "Bad", "molarMass_g_mol": 40.0, "tcK": 500.0,
        "correlations": { "liquidViscosity":
          { "equation": 999, "c": [1,2,3,4,5], "tMinK": 200, "tMaxK": 400,
            "unit": "Pa*s", "source": "t" } } } ] }
    """
    match Dippr.parse json with
    | Failure [ DatabaseParseError d ] -> Assert.Contains("999", d)
    | _ -> failwith "expected a parse failure"

// ---------- the two unit traps ----------

/// DIPPR 105 is tabulated in mol/m^3, not the kmol/m^3 the definition implies.
/// Benzene at 300 K must come out at 871 kg/m^3, not 871 000.
[<Fact>]
let ``liquid density is converted from the correct molar unit`` () =
    let rho = Dippr.liquidDensity (species "C6H6") 300.0<K> |> value
    Assert.InRange(float rho, 860.0, 880.0)

/// Table 2-150 puts the critical temperature ahead of the coefficients and is
/// tabulated in kJ/kmol. Benzene at 300 K is about 431 kJ/kg.
[<Fact>]
let ``heat of vaporisation is converted from the correct molar unit`` () =
    let hvap = Dippr.heatOfVaporisation (species "C6H6") 300.0<K> |> value
    Assert.InRange(hvap / 1000.0, 410.0, 450.0)

/// Liquid heat capacity is molar, in J/(kmol*K). Water at 300 K is 4.18 kJ/(kg*K).
[<Fact>]
let ``liquid heat capacity is converted from the correct molar unit`` () =
    let cp = Dippr.liquidHeatCapacity (species "H2O") 300.0<K> |> value
    Assert.InRange(float cp / 1000.0, 4.0, 4.35)

// ---------- physical behaviour ----------

/// Liquid viscosity falls with temperature. This is the opposite of a gas, and
/// getting the sign wrong is a classic way to build a solver that runs away.
[<Fact>]
let ``liquid viscosity falls with temperature`` () =
    let failures = ResizeArray<string>()
    for KeyValue(_, s) in db.Species do
        if s.Has "liquidViscosity" && s.Key <> "nC5H12" then
            let range =
                match s.Correlations.["liquidViscosity"].TMin,
                      s.Correlations.["liquidViscosity"].TMax with
                | Some lo, Some hi -> Some (float lo, float hi)
                | _ -> None
            match range with
            | Some (lo, hi) ->
                let points =
                    [ 0.2; 0.4; 0.6; 0.8 ]
                    |> List.map (fun f -> lo + f * (hi - lo))
                    |> List.choose (fun t ->
                        match Dippr.liquidViscosity s (t * 1.0<K>) with
                        | Success (v, _) -> Some (t, float v)
                        | Failure _ -> None)
                for (t1, a), (t2, b) in List.pairwise points do
                    if b >= a then
                        failures.Add $"{s.Key}: viscosity rises from {t1:F0} to {t2:F0} K"
            | None -> ()
    Assert.True(failures.Count = 0, String.Join("\n", failures))

/// Saturated liquid density falls with temperature, everywhere, for everything.
[<Fact>]
let ``liquid density falls with temperature`` () =
    let failures = ResizeArray<string>()
    for KeyValue(_, s) in db.Species do
        if s.Has "liquidMolarDensity" then
            let c = s.Correlations.["liquidMolarDensity"]
            match c.TMin, c.TMax with
            | Some lo, Some hi ->
                let points =
                    [ 0.1; 0.4; 0.7; 0.9 ]
                    |> List.map (fun f -> float lo + f * (float hi - float lo))
                    |> List.choose (fun t ->
                        match Dippr.liquidDensity s (t * 1.0<K>) with
                        | Success (v, _) -> Some (t, float v)
                        | Failure _ -> None)
                for (t1, a), (t2, b) in List.pairwise points do
                    if b >= a then
                        failures.Add $"{s.Key}: density rises from {t1:F0} to {t2:F0} K"
            | _ -> ()
    Assert.True(failures.Count = 0, String.Join("\n", failures))

/// Heat of vaporisation must go to zero at the critical point, by construction
/// of DIPPR 106. This exercises the reduced-temperature handling.
[<Fact>]
let ``heat of vaporisation vanishes at the critical point`` () =
    for key in [ "C6H6"; "C7H8"; "nC6H14"; "NH3" ] do
        let s = species key
        match s.Tc with
        | Some tc ->
            let nearCritical = Dippr.heatOfVaporisation s (tc * 0.999) |> value
            let midRange = Dippr.heatOfVaporisation s (tc * 0.6) |> value
            Assert.True(nearCritical < midRange * 0.15,
                        $"{key}: {nearCritical:F0} near Tc against {midRange:F0} at 0.6 Tc")
        | None -> failwith $"{key} has no critical temperature"

[<Fact>]
let ``evaluation at or above the critical temperature is refused for reduced forms`` () =
    let s = species "C6H6"
    match s.Tc with
    | Some tc ->
        match Dippr.heatOfVaporisation s (tc * 1.01) with
        | Failure _ -> ()
        | Success _ -> failwith "there is no heat of vaporisation above Tc"
    | None -> failwith "no Tc"

/// Liquid Prandtl numbers are large, unlike gases. Water at 300 K is about 5.8;
/// heavier hydrocarbons run into the tens.
[<Fact>]
let ``liquid Prandtl numbers are in the physical range`` () =
    let pr = Dippr.liquidPrandtl (species "H2O") 300.0<K> |> value
    Assert.InRange(pr, 5.0, 7.0)

    let toluene = Dippr.liquidPrandtl (species "C7H8") 300.0<K> |> value
    Assert.InRange(toluene, 4.0, 12.0)

// ---------- range handling ----------

[<Fact>]
let ``evaluation outside the fit range warns rather than failing silently`` () =
    let s = species "C6H6"
    let c = s.Correlations.["liquidViscosity"]
    match c.TMax with
    | Some hi ->
        match Dippr.liquidViscosity s (hi + 50.0<K>) with
        | Success (_, warnings) ->
            Assert.Contains(warnings, function OutsideFitRange _ -> true | _ -> false)
        | Failure _ -> ()   // acceptable if the form itself breaks down
    | None -> ()

[<Fact>]
let ``a missing correlation fails rather than returning zero`` () =
    // Water has no DIPPR 105 density record; IF97 covers it instead.
    match Dippr.liquidDensity (species "H2O") 300.0<K> with
    | Failure _ -> ()
    | Success _ -> failwith "water has no DIPPR density record and must say so"

/// The n-pentane liquid viscosity record is known bad and is flagged in the
/// data file. This test pins that the flag is present, so it cannot be lost
/// silently in a future re-import.
[<Fact>]
let ``the known bad n-pentane viscosity record stays flagged`` () =
    let path =
        System.IO.Path.Combine(AppContext.BaseDirectory, "data", "liquid-properties.json")
    if System.IO.File.Exists path then
        let text = System.IO.File.ReadAllText path
        Assert.Contains("suspectReason", text)
