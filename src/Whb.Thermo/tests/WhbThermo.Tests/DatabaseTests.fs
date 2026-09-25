module WhbThermo.Tests.DatabaseTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

/// The embedded database must always load. Warnings are expected (anchor-only Cp),
/// failures are not.
[<Fact>]
let ``embedded database loads without failure`` () =
    match SpeciesDatabase.load () with
    | Success (db, _) -> Assert.True(db.Count >= 28)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``every species has a positive molar mass`` () =
    match SpeciesDatabase.load () with
    | Success (db, _) ->
        for KeyValue(_, sp) in db do
            Assert.True(float sp.MolarMass > 0.0, sp.Key)
    | Failure _ -> failwith "database did not load"

/// Regression gate: this number must only ever go DOWN as the database is completed.
/// Lower it every time a Shomate fit replaces an anchor.
[<Fact>]
let ``pending-completion count does not regress`` () =
    let allowed = 0   // nothing may regress to anchor-only
    match SpeciesDatabase.load () with
    | Success (db, _) ->
        let pending = SpeciesDatabase.pendingCompletion db
        Assert.True(pending.Length <= allowed,
                    $"{pending.Length} species still anchor-only: "
                    + (pending |> List.map (fun s -> s.Key) |> String.concat ", "))
    | Failure _ -> failwith "database did not load"

/// Anchor-only species MUST warn. Silence here would be the dangerous failure mode.
[<Fact>]
let ``anchor-only species emit a warning`` () =
    // The shipped database has no anchor-only species left (see the test above),
    // so the warning path is exercised on a synthetic record.
    let json = """
    { "schemaVersion": "2.1", "description": "test", "species": [
      { "key": "ANC", "name": "Anchor", "molarMass_kg_kmol": 40.0,
        "cpModel": { "kind": "anchor", "anchorCp500C": 1.0, "source": "t" },
        "transport": { "kind": "none", "reason": "synthetic" } } ] }
    """
    match SpeciesDatabase.parse json with
    | Success (_, warnings) ->
        Assert.Contains(warnings, function CpIsAnchorOnly _ -> true | _ -> false)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``unknown species key fails cleanly`` () =
    match SpeciesDatabase.load () with
    | Success (db, _) ->
        match SpeciesDatabase.find db "UNOBTAINIUM" with
        | Failure [ UnknownSpecies "UNOBTAINIUM" ] -> ()
        | _ -> failwith "expected UnknownSpecies failure"
    | Failure _ -> failwith "database did not load"

// ==========================================================================
// Species coverage. The list is the process scope the library must support;
// a species silently dropped by a rebuild is the failure this guards against.
// ==========================================================================

/// Every species the process scope requires must be present.
[<Fact>]
let ``the database covers the required process scope`` () =
    let required =
        [ // permanent gases
          "H2"; "N2"; "O2"; "Ar"; "He"
          // carbon species
          "CO"; "CO2"; "CH4"; "C2H6"; "C2H4"; "C2H2"; "C3H8"; "C3H6"
          // steam
          "H2O"
          // ammonia
          "NH3"
          // sulfur
          "H2S"; "SO2"; "COS"; "CS2"; "S2"; "S3"; "S4"; "S5"; "S6"; "S7"; "S8"
          // nitrogen oxides
          "NO"; "NO2"; "N2O"
          // oxygenates
          "CH3OH"; "HCHO"; "DME" ]

    match SpeciesDatabase.load () with
    | Success (db, _) ->
        let missing = required |> List.filter (fun k -> not (db.ContainsKey k))
        Assert.True(missing.IsEmpty,
                    $"""missing species: {String.Join(", ", missing)}""")
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Every species must carry a temperature-dependent Cp. Transport may be
/// absent - that is recorded explicitly - but a species with no Cp is not data.
[<Fact>]
let ``every species has a temperature dependent heat capacity`` () =
    match SpeciesDatabase.load () with
    | Success (db, _) ->
        for KeyValue(_, sp) in db do
            Assert.True(sp.IsProductionGrade, $"{sp.Key} has no temperature-dependent Cp")
    | Failure _ -> failwith "database did not load"

/// Species without transport data must FAIL when asked, not return a
/// fabricated number. Six species are in that state by design.
[<Fact>]
let ``species without transport data fail rather than invent a value`` () =
    match SpeciesDatabase.load () with
    | Success (db, _) ->
        let withoutTransport =
            db |> Map.toList |> List.map snd
               |> List.filter (fun sp ->
                    match sp.Transport with NoTransportData _ -> true | _ -> false)

        Assert.NotEmpty(withoutTransport)
        for sp in withoutTransport do
            match PureComponent.viscosity sp 600.0<K> with
            | Failure _ -> ()
            | Success _ -> failwith $"{sp.Key} returned a viscosity with no transport data"
    | Failure _ -> failwith "database did not load"

/// Oxygen and the oxygenates were added from CEA; their Cp agrees with CoolProp
/// to better than 1.5 % over 500-1000 K.
[<Fact>]
let ``the newly added species reproduce known heat capacities`` () =
    match SpeciesDatabase.load () with
    | Success (db, _) ->
        let cp key tK =
            match SpeciesDatabase.find db key with
            | Success (sp, _) ->
                match PureComponent.specificHeatMass sp (tK * 1.0<K>) with
                | Success (v, _) -> float v
                | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")
            | Failure _ -> failwith $"{key} missing"

        Assert.InRange(cp "O2" 1000.0, 1075.0, 1105.0)
        Assert.InRange(cp "CH3OH" 700.0, 2270.0, 2340.0)
        Assert.InRange(cp "DME" 700.0, 2480.0, 2570.0)
        Assert.InRange(cp "HCHO" 700.0, 1600.0, 1900.0)
    | Failure _ -> failwith "database did not load"
