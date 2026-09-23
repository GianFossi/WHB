module WhbThermo.Tests.PureComponentTests

open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

let private db =
    match SpeciesDatabase.load () with
    | Success (d, _) -> d
    | Failure _ -> failwith "database did not load"

let private species key =
    match SpeciesDatabase.find db key with
    | Success (s, _) -> s
    | Failure _ -> failwith $"missing species {key}"

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// N2 viscosity at 300 K is ~17.9e-6 Pa*s (Poling, Table).
[<Fact>]
let ``nitrogen viscosity at 300 K`` () =
    let mu = PureComponent.viscosity (species "N2") 300.0<K> |> value
    Assert.InRange(float mu * 1e6, 17.0, 19.0)

/// CO2 Cp at 500 K is ~1.014 kJ/(kg*K) (NIST).
[<Fact>]
let ``carbon dioxide Cp at 500 K`` () =
    let cp = PureComponent.specificHeatMass (species "CO2") 500.0<K> |> value
    Assert.InRange(float cp / 1000.0, 0.98, 1.06)

/// Evaluating outside the fit range must succeed WITH a warning, never silently.
[<Fact>]
let ``out of range evaluation warns`` () =
    // NASA CEA transport for N2 starts at 200 K; 100 K is below every interval.
    match PureComponent.viscosity (species "N2") 100.0<K> with
    | Success (_, warnings) ->
        Assert.Contains(warnings, function OutsideFitRange _ -> true | _ -> false)
    | Failure _ -> failwith "expected success with warning"

/// Every species now carries a temperature-dependent fit, so enthalpy is
/// available across the board. The anchor-only refusal path is exercised in
/// Nasa7Tests against a synthetic species instead.
[<Fact>]
let ``enthalpy is available for every species`` () =
    for KeyValue(_, sp) in db do
        match PureComponent.enthalpy sp 800.0<K> with
        | Success _ -> ()
        | Failure msgs ->
            failwith ($"{sp.Key}: " + (msgs |> List.map string |> String.concat "; "))

/// NASA CEA transport must be preferred wherever it exists: the Sutherland
/// fallback is only reliable below about 1000 degC.
[<Fact>]
let ``most species use NASA CEA transport`` () =
    let nasa =
        db |> Map.toList |> List.map snd
           |> List.filter (fun sp -> match sp.Transport with NasaCea _ -> true | _ -> false)
    Assert.True(nasa.Length >= 20, $"only {nasa.Length} species on NASA transport")

/// Viscosity at 1400 degC, the SRU inlet. The Sutherland fits were never valid
/// there; the NASA correlations are tabulated to 5000 K.
[<Fact>]
let ``viscosity is available at WHB inlet temperature`` () =
    let mu = PureComponent.viscosity (species "N2") 1673.15<K> |> value
    Assert.InRange(float mu * 1e6, 45.0, 65.0)
