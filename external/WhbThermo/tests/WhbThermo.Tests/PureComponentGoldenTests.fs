module WhbThermo.Tests.PureComponentGoldenTests

open System
open System.Globalization
open System.IO
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

/// Pure-species campaign against CoolProp reference equations of state.
///
/// Tolerances differ by property because the underlying data differ in quality,
/// not because the looser ones matter less. Observed worst deviations over the
/// 259 comparisons in the golden file are cp 2.3 %, mu 2.9 %, k 11.6 %; the
/// gates sit just above those.
///
///   cp   3 %  - both sides are ideal-gas polynomial fits of the same measurements
///   mu   4 %  - NASA CEA correlations vs CoolProp transport models
///   k   13 %  - conductivity data are the scarcest and least consistent; the
///               worst cases are ethane and hydrogen above 1000 K
///
/// A tolerance wide enough to hide a real defect would be worthless, so the
/// negative-control test below checks that these are tight enough to catch a
/// species being swapped for another.
let private tolerances = dict [ "cp", 0.03; "mu", 0.04; "k", 0.13 ]

let private db =
    match SpeciesDatabase.load () with
    | Success (d, _) -> d
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private rows =
    let file = Path.Combine(AppContext.BaseDirectory, "reference", "coolprop-pure.csv")
    if not (File.Exists file) then [||]
    else
        let lines = File.ReadAllLines file
        let header = lines.[0].Split(',')
        lines |> Array.skip 1
              |> Array.map (fun l -> Array.zip header (l.Split(',')) |> Map.ofArray)

let private number (row: Map<string, string>) key =
    Double.Parse(row.[key], CultureInfo.InvariantCulture)

[<Fact>]
let ``pure species properties track CoolProp`` () =
    Assert.True(rows.Length > 0, "run tools/generate_golden.py first")

    let failures = ResizeArray<string>()
    let mutable compared = 0

    for row in rows do
        let key = row.["species"]
        match SpeciesDatabase.find db key with
        | Failure _ -> ()
        | Success (sp, _) ->
            let t = number row "T_K" * 1.0<K>

            let check name (result: Thermo<float>) (reference: float) =
                match result with
                | Failure _ -> ()
                | Success (ours, _) ->
                    compared <- compared + 1
                    let deviation = abs (ours - reference) / abs reference
                    if deviation > tolerances.[name] then
                        failures.Add ($"{key} @ {t}: {name} {deviation:P1} "
                                      + $"(ours {ours:g4}, CoolProp {reference:g4})")

            check "cp" (PureComponent.specificHeatMass sp t >>= fun v -> ok (float v))
                       (number row "cp_J_kgK")
            check "mu" (PureComponent.viscosity sp t >>= fun v -> ok (float v))
                       (number row "mu_Pas")
            check "k" (PureComponent.conductivity sp t >>= fun v -> ok (float v))
                      (number row "k_W_mK")

    Assert.True(compared > 200, $"only {compared} comparisons made")
    Assert.True(failures.Count = 0, String.Join("\n", failures))

/// Negative control. If the golden comparison passes when species are
/// deliberately swapped, the tolerances are too loose to be worth anything.
[<Fact>]
let ``the comparison is tight enough to detect a swapped species`` () =
    let nitrogen =
        match SpeciesDatabase.find db "N2" with
        | Success (s, _) -> s
        | Failure _ -> failwith "N2 missing"

    let mismatches =
        rows
        |> Array.filter (fun r -> r.["species"] = "CO2")
        |> Array.sumBy (fun row ->
            let t = number row "T_K" * 1.0<K>
            match PureComponent.specificHeatMass nitrogen t with
            | Success (ours, _) ->
                let deviation = abs (float ours - number row "cp_J_kgK") / number row "cp_J_kgK"
                if deviation > tolerances.["cp"] then 1 else 0
            | Failure _ -> 0)

    Assert.True(mismatches > 0,
                "N2 evaluated against CO2 reference data passed the cp tolerance; "
                + "the tolerance is too loose to be meaningful")

/// Every species must evaluate everywhere in the WHB envelope without failing.
[<Fact>]
let ``every species evaluates across the full envelope`` () =
    let failures = ResizeArray<string>()
    for KeyValue(_, sp) in db do
        for tC in [ 100.0; 300.0; 500.0; 800.0; 1100.0; 1400.0 ] do
            let t = (tC + 273.15) * 1.0<K>
            for name, result in
                [ "cp", (PureComponent.specificHeatMass sp t >>= fun v -> ok (float v))
                  "mu", (PureComponent.viscosity sp t >>= fun v -> ok (float v))
                  "k", (PureComponent.conductivity sp t >>= fun v -> ok (float v)) ] do
                // Species recorded with NoTransportData must REFUSE viscosity and
                // conductivity with their reason: the database deliberately does
                // not fabricate a fit. Everything else must evaluate.
                let refusesByDesign =
                    name <> "cp" && (match sp.Transport with NoTransportData _ -> true | _ -> false)
                match result with
                | Failure _ when refusesByDesign -> ()
                | Success _ when refusesByDesign ->
                    failures.Add $"{sp.Key} {name} @ {tC} degC: has no transport data but returned a value"
                | Failure msgs ->
                    failures.Add ($"{sp.Key} {name} @ {tC} degC: "
                                  + (msgs |> List.map string |> String.concat "; "))
                | Success (v, _) ->
                    if Double.IsNaN v || Double.IsInfinity v || v <= 0.0 then
                        failures.Add $"{sp.Key} {name} @ {tC} degC: non-physical value {v}"

    Assert.True(failures.Count = 0, String.Join("\n", failures))

/// Physical monotonicity: gas viscosity rises with temperature for every species.
[<Fact>]
let ``viscosity increases with temperature for every species`` () =
    let failures = ResizeArray<string>()
    for KeyValue(_, sp) in db do
        let values =
            [ 400.0; 600.0; 800.0; 1000.0; 1200.0; 1500.0 ]
            |> List.choose (fun tK ->
                match PureComponent.viscosity sp (tK * 1.0<K>) with
                | Success (v, _) -> Some (tK, float v)
                | Failure _ -> None)
        for (t1, a), (t2, b) in List.pairwise values do
            if b <= a then
                failures.Add $"{sp.Key}: viscosity falls from {t1} to {t2} K ({a:e3} -> {b:e3})"

    Assert.True(failures.Count = 0, String.Join("\n", failures))
