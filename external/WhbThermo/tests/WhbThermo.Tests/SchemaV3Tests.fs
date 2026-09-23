module WhbThermo.Tests.SchemaV3Tests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Liquids
open XSulfur

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private db = SpeciesDatabase.load () |> value

// ---------- identity ----------

[<Fact>]
let ``every species has an immutable id equal to its key and a family`` () =
    for KeyValue(key, sp) in db do
        Assert.Equal(key, sp.Id)
        Assert.NotEqual(SpeciesFamily.Other, sp.Family)
    Assert.Equal(SpeciesFamily.AcidGas, db.["CO2"].Family)
    Assert.Equal(SpeciesFamily.Radical, db.["OH"].Family)
    Assert.Equal(SpeciesFamily.SulfurCompound, db.["S8"].Family)
    Assert.Contains("Methanol", db.["CH3OH"].Synonyms)

// ---------- molecular ----------

/// IUPAC standard atomic weights, for the consistency check only.
let private atomicWeight =
    dict [ "H", 1.008; "C", 12.011; "N", 14.007; "O", 15.999; "S", 32.06
           "Ar", 39.948; "He", 4.0026 ]

/// The element counts must reproduce the stored molar mass: a wrong formula,
/// a wrong count or a wrong molar mass all show up here.
[<Fact>]
let ``element counts reproduce the molar mass`` () =
    for KeyValue(key, sp) in db do
        let fromElements = sp.Elements |> List.sumBy (fun (e, n) -> atomicWeight.[e] * float n)
        let stored = float sp.MolarMass
        Assert.True(abs (fromElements - stored) / stored < 2e-3,
                    $"{key}: elements give {fromElements:F4}, stored {stored:F4}")
    Assert.Equal<(string * int) list>([ "C", 1; "O", 2 ], db.["CO2"].Elements)
    Assert.Equal(8, db.["C2H6"].AtomCount)

[<Fact>]
let ``formula parser merges repeated symbols and rejects what it cannot read`` () =
    Assert.Equal(Ok [ "C", 1; "H", 4; "O", 1 ], Formula.elements "CH3OH")
    Assert.Equal(Ok [ "S", 1 ], Formula.elements "S")
    Assert.True(Result.isError (Formula.elements "Ca(OH)2"))
    Assert.True(Result.isError (Formula.elements ""))

// ---------- reference state ----------

/// NASA-9 data is on a 1 bar standard state. A 1 atm claim here would shift
/// every entropy by R ln(1.01325).
[<Fact>]
let ``reference state is stated and matches the NASA-9 convention`` () =
    match SpeciesDatabase.referenceState () with
    | Success (rs, warnings) ->
        Assert.Empty(warnings)
        Assert.Equal(298.15, float rs.Temperature, 9)
        Assert.Equal(1.0, float rs.Pressure, 12)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``a file without a reference state gets the NASA-9 one with a warning`` () =
    match SpeciesDatabase.parseReferenceState """{ "schemaVersion": "2.1", "species": [] }""" with
    | Success (rs, warnings) ->
        Assert.Equal(1.0, float rs.Pressure, 12)
        Assert.NotEmpty(warnings)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- data quality ----------

[<Fact>]
let ``quality levels follow the data that is actually present`` () =
    for KeyValue(key, sp) in db do
        match sp.Transport, sp.Quality.Transport with
        | NoTransportData _, Some q -> failwith $"{key}: transport graded {q} but there is no transport data"
        | NoTransportData _, None -> ()
        | _, None -> failwith $"{key}: transport data without a grade"
        | _ -> ()
        Assert.Equal(sp.Critical.IsSome, sp.Quality.Critical.IsSome)
        Assert.Equal(sp.VapourPressure.IsSome, sp.Quality.VapourPressure.IsSome)
    Assert.Equal(QualityLevel.A, db.["CO2"].Quality.Thermo)
    Assert.Equal(Some QualityLevel.C, db.["C3H8"].Quality.Transport)
    Assert.Equal(Fitted, QualityLevel.B.Provenance)

/// The CoolProp comparison found the legacy Sutherland transport of these four
/// hydrocarbons far off; the database must say so rather than stay silent.
[<Fact>]
let ``known transport deviations are declared in the data`` () =
    for key in [ "C3H8"; "C3H6"; "C6H6"; "C7H8" ] do
        match db.[key].Quality.Validation with
        | KnownDeviation note -> Assert.Contains("CoolProp", note)
        | other -> failwith $"{key}: expected a known deviation, got {other}"
    match db.["N2"].Quality.Validation with
    | Verified reference -> Assert.Contains("CoolProp", reference)
    | other -> failwith $"N2: expected verified, got {other}"

[<Fact>]
let ``a schema 2 record without the new fields still loads with implied values`` () =
    let json = """
    { "schemaVersion": "2.1", "description": "legacy", "species": [
      { "key": "N2", "name": "Nitrogen", "molarMass_kg_kmol": 28.0134, "formula": "N2",
        "cpModel": { "kind": "anchor", "anchorCp500C": 1.1, "source": "t" },
        "transport": { "kind": "none", "reason": "legacy" } } ] }
    """
    let legacy = SpeciesDatabase.parse json |> value
    let n2 = legacy.["N2"]
    Assert.Equal("N2", n2.Id)
    Assert.Equal(SpeciesFamily.Other, n2.Family)
    Assert.Equal<(string * int) list>([ "N", 2 ], n2.Elements)
    Assert.Equal(QualityLevel.D, n2.Quality.Thermo)
    Assert.Equal(None, n2.Quality.Transport)

[<Fact>]
let ``an id that differs from the key is rejected`` () =
    let json = """
    { "schemaVersion": "3.0", "description": "t", "species": [
      { "key": "N2", "id": "NITROGEN", "name": "Nitrogen", "molarMass_kg_kmol": 28.0134,
        "cpModel": { "kind": "anchor", "anchorCp500C": 1.1, "source": "t" } } ] }
    """
    match SpeciesDatabase.parse json with
    | Failure _ -> ()
    | Success _ -> failwith "an id that differs from the key must be refused"

// ---------- one id per substance across files ----------

/// A substance in both the gas and the liquid database must carry the same id
/// in both. Matched by CAS number, which is the one identifier neither file
/// invents. Methanol used to be "CH3OH" in one and "CH4O" in the other.
[<Fact>]
let ``liquid and gas databases use the same id for the same substance`` () =
    let liquids = Dippr.load () |> value
    let gasByCas =
        db |> Map.toList
           |> List.choose (fun (_, sp) -> sp.Cas |> Option.map (fun cas -> cas, sp.Id))
           |> Map.ofList
    for KeyValue(key, liquid) in liquids.Species do
        match Map.tryFind liquid.Cas gasByCas with
        | Some gasId -> Assert.True((gasId = key), $"CAS {liquid.Cas}: gas id '{gasId}', liquid id '{key}'")
        | None -> ()

// ---------- sulfur allotropes come from the species database ----------

/// sulfur-species.json no longer copies the gas-phase coefficients: each
/// allotrope must resolve to exactly the species-database record.
[<Fact>]
let ``sulfur allotropes are resolved from the species database`` () =
    let model = Speciation.load () |> value
    Assert.Equal(8, model.Allotropes.Length)
    for allotrope in model.Allotropes do
        let id = if allotrope.Key = "S" then "S1" else allotrope.Key
        let sp = db.[id]
        Assert.Equal(float sp.MolarMass, allotrope.MolarMass, 12)
        Assert.Equal<(string * int) list>([ "S", allotrope.Atoms ], sp.Elements)
        match sp.Cp with
        | Nasa9 segments ->
            Assert.Equal(segments.Length, allotrope.Segments.Length)
            for (seg, (_, _, a, b)) in List.zip segments allotrope.Segments do
                Assert.Equal<float[]>(seg.A, a)
                Assert.Equal<float[]>(seg.B, b)
        | _ -> failwith $"{id}: expected NASA-9"
    Assert.True(model.Liquid.IsSome, "the liquid reference phase stays in the sulfur file")

// ---------- radiation role is data ----------

[<Fact>]
let ``radiation role comes from the database`` () =
    match db.["CO2"].Radiation with
    | ParticipatingCovered models ->
        Assert.Contains(models, fun m -> m.Contains "WSGG")
        // The Leckner coefficients here were fitted to WSGG; the data must say so.
        Assert.Contains(models, fun m -> m.Contains "Leckner" && m.Contains "not independent")
    | other -> failwith $"CO2: expected covered, got {other}"
    Assert.Equal(Transparent, db.["N2"].Radiation)
    match db.["CH4"].Radiation with
    | ParticipatingUncovered note -> Assert.Contains("LOW", note)
    | other -> failwith $"CH4: expected uncovered, got {other}"

[<Fact>]
let ``a record without a radiation block falls back to the molecular rule`` () =
    let json = """
    { "schemaVersion": "2.1", "species": [
      { "key": "N2", "name": "Nitrogen", "molarMass_kg_kmol": 28.0134,
        "cpModel": { "kind": "anchor", "anchorCp500C": 1.1 } },
      { "key": "CO", "name": "Carbon monoxide", "molarMass_kg_kmol": 28.0101,
        "cpModel": { "kind": "anchor", "anchorCp500C": 1.1 } } ] }
    """
    let legacy = SpeciesDatabase.parse json |> value
    Assert.Equal(Transparent, legacy.["N2"].Radiation)
    Assert.True(legacy.["CO"].Radiation.IsParticipating)

// ---------- GasState: species, mixture and state kept apart ----------

let private mixture (composition: (string * float) list) =
    WhbThermo.Properties.Mixing.fromKeys db composition |> value

let private state composition tK pBar =
    WhbThermo.Properties.GasState.evaluate (mixture composition) (tK * 1.0<K>) (pBar * 1.0<bar>)
    |> value

/// N2 is an element in its reference state: its enthalpy at 298.15 K is zero.
[<Fact>]
let ``nitrogen enthalpy is zero at the reference state`` () =
    let s = state [ "N2", 1.0 ] 298.15 1.0
    Assert.True(abs s.Enthalpy < 50.0, $"h = {s.Enthalpy} J/kg")

/// cp and h come from the same NASA-9 coefficients, so cp = dh/dT; and at
/// constant pressure ds/dT = cp/T. Both hold for a mixture as for a species.
[<Fact>]
let ``state enthalpy and entropy are consistent with cp`` () =
    let composition = [ "N2", 0.70; "H2O", 0.15; "CO2", 0.10; "O2", 0.05 ]
    let dt = 0.5
    let at t = state composition t 1.5
    let mid, lo, hi = at 800.0, at (800.0 - dt), at (800.0 + dt)
    let dhdt = (hi.Enthalpy - lo.Enthalpy) / (2.0 * dt)
    let dsdt = (hi.Entropy - lo.Entropy) / (2.0 * dt)
    Assert.True(abs (dhdt - float mid.Cp) / float mid.Cp < 1e-4, $"dh/dT {dhdt} vs cp {mid.Cp}")
    Assert.True(abs (dsdt - float mid.Cp / 800.0) / (float mid.Cp / 800.0) < 1e-4,
                $"ds/dT {dsdt} vs cp/T {float mid.Cp / 800.0}")
    Assert.Equal(1.0, mid.Z)

/// The mixing term: an equimolar binary has R ln 2 per mole more entropy than
/// its separated components at the same T and P.
[<Fact>]
let ``state entropy includes ideal mixing`` () =
    let mixed = state [ "N2", 0.5; "O2", 0.5 ] 600.0 1.0
    let n2 = state [ "N2", 1.0 ] 600.0 1.0
    let o2 = state [ "O2", 1.0 ] 600.0 1.0
    let m = float mixed.MolarMass / 1000.0
    let separated =
        (0.5 * n2.Entropy * float n2.MolarMass / 1000.0
         + 0.5 * o2.Entropy * float o2.MolarMass / 1000.0) / m
    let expected = 8.31446261815324 * log 2.0 / m
    Assert.Equal(expected, mixed.Entropy - separated, 6)

[<Fact>]
let ``state gives the physical heat capacity ratio and speed of sound`` () =
    let cold = state [ "N2", 1.0 ] 300.0 1.0
    Assert.InRange(cold.Gamma, 1.39, 1.41)
    let hot = state [ "N2", 1.0 ] 800.0 1.0
    // a = sqrt(gamma R T / M): about 565 m/s for N2 at 800 K.
    Assert.InRange(float hot.SpeedOfSound, 555.0, 575.0)
    Assert.Equal(float hot.Cp - 8.31446261815324 / 0.0280134, float hot.Cv, 6)

[<Fact>]
let ``mass fractions follow from mole fractions and molar masses`` () =
    let fractions = Mixture.massFractions (mixture [ "H2", 0.5; "CO2", 0.5 ])
    Assert.Equal(1.0, fractions |> List.sumBy snd, 12)
    let co2 = fractions |> List.find (fun (sp, _) -> sp.Key = "CO2") |> snd
    Assert.Equal(44.0095 / (44.0095 + 2.01588), co2, 3)

// ---------- equation-of-state parameters and k_ij ----------

/// Water's second virial coefficient comes from IF97, not corresponding
/// states; the database says so, and nothing else needs EOS extras.
[<Fact>]
let ``water carries the IF97 second virial parameter set`` () =
    match db.["H2O"].EosParametersFor EquationOfState.Virial with
    | Some p -> Assert.Contains("IF97", p.SecondVirial.Value)
    | None -> failwith "H2O: expected a Virial parameter set"
    Assert.Equal(None, db.["N2"].EosParametersFor EquationOfState.Virial)

[<Fact>]
let ``the shipped k_ij table loads and is empty until sourced values are added`` () =
    let table = BinaryInteraction.load () |> value
    Assert.True(table.IsEmpty)
    // An absent pair is the k_ij = 0 default, without a warning.
    match BinaryInteraction.kij table "CO2" "N2" EquationOfState.PengRobinson 500.0<K> with
    | Success (k, warnings) ->
        Assert.Equal(0.0, k)
        Assert.Empty(warnings)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``k_ij lookup is symmetric and warns outside its fitted range`` () =
    let json = """
    { "schemaVersion": "1.0", "pairs": [
      { "species1": "N2", "species2": "CO2", "eos": "PengRobinson", "kij": -0.02,
        "tMinK": 200.0, "tMaxK": 600.0, "source": "synthetic" } ] }
    """
    let table = BinaryInteraction.parse json |> value
    Assert.Equal(-0.02, (BinaryInteraction.tryFind table "CO2" "N2" EquationOfState.PengRobinson).Value.Kij)
    Assert.Equal(-0.02, (BinaryInteraction.tryFind table "N2" "CO2" EquationOfState.PengRobinson).Value.Kij)
    Assert.Equal(None, BinaryInteraction.tryFind table "N2" "CO2" EquationOfState.Srk)
    match BinaryInteraction.kij table "CO2" "N2" EquationOfState.PengRobinson 900.0<K> with
    | Success (k, warnings) ->
        Assert.Equal(-0.02, k)
        Assert.Contains(warnings, function OutsideFitRange _ -> true | _ -> false)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``k_ij table rejects duplicates and self pairs`` () =
    let duplicate = """
    { "schemaVersion": "1.0", "pairs": [
      { "species1": "N2", "species2": "CO2", "eos": "Virial", "kij": 0.01, "tMinK": 200, "tMaxK": 600, "source": "a" },
      { "species1": "CO2", "species2": "N2", "eos": "Virial", "kij": 0.02, "tMinK": 200, "tMaxK": 600, "source": "b" } ] }
    """
    let self = """
    { "schemaVersion": "1.0", "pairs": [
      { "species1": "N2", "species2": "N2", "eos": "Virial", "kij": 0.01, "tMinK": 200, "tMaxK": 600, "source": "a" } ] }
    """
    for json in [ duplicate; self ] do
        match BinaryInteraction.parse json with
        | Failure _ -> ()
        | Success _ -> failwith "expected the table to be refused"

// ---------- reaction database ----------

let private reactions = WhbThermo.Properties.ReactionDatabase.load () |> value

[<Fact>]
let ``the reaction database loads and every reaction conserves atoms`` () =
    Assert.Equal(7, reactions.Count)
    for KeyValue(id, r) in reactions do
        Assert.Empty(WhbThermo.Properties.ReactionDatabase.imbalance db r.Reaction.Terms)
        Assert.True(r.TMax > r.TMin, id)
    let wgs = reactions.["waterGasShift"]
    Assert.Equal("CO + H2O -> CO2 + H2", wgs.Reaction.Name)
    Assert.Equal(None, wgs.KineticModel)

/// The validity range is the intersection of the species' NASA-9 ranges; H2S
/// and S2 start at 300 K, so H2S cracking cannot claim 200 K.
[<Fact>]
let ``reaction validity follows the species data`` () =
    Assert.Equal(300.0, float reactions.["h2sCracking"].TMin, 9)
    Assert.Equal(200.0, float reactions.["waterGasShift"].TMin, 9)

[<Fact>]
let ``a database reaction gives the same constant as the equilibrium module and warns outside its range`` () =
    let wgs = reactions.["waterGasShift"]
    let direct = WhbThermo.Properties.Equilibrium.equilibriumConstant db wgs.Reaction 1000.0<K> |> value
    match WhbThermo.Properties.ReactionDatabase.equilibriumConstant db wgs 1000.0<K> with
    | Success (k, warnings) ->
        Assert.Equal(direct.LogK, k.LogK, 12)
        Assert.Empty(warnings)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")
    match WhbThermo.Properties.ReactionDatabase.equilibriumConstant db reactions.["h2sCracking"] 250.0<K> with
    | Success (_, warnings) -> Assert.Contains(warnings, function OutsideFitRange _ -> true | _ -> false)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``unbalanced, unknown and duplicate reactions are refused`` () =
    let entry id stoich =
        $"""{{ "id": "{id}", "equation": "x", "stoichiometry": {stoich}, "equilibriumModel": "nasa9Gibbs",
             "validity": {{ "tMinK": 300, "tMaxK": 3000 }}, "source": "t" }}"""
    let file entries = $"""{{ "schemaVersion": "1.0", "reactions": [ {String.concat ", " entries} ] }}"""
    let cases =
        [ "unbalanced", file [ entry "bad" """{ "CO": -1, "H2O": -1, "CO2": 1 }""" ]
          "unknown species", file [ entry "bad" """{ "UNOBTAINIUM": -1, "H2": 1 }""" ]
          "duplicate id", file [ entry "wgs" """{ "CO": -1, "H2O": -1, "CO2": 1, "H2": 1 }"""
                                 entry "wgs" """{ "CO": -1, "H2O": -1, "CO2": 1, "H2": 1 }""" ] ]
    for name, json in cases do
        match WhbThermo.Properties.ReactionDatabase.parse db json with
        | Failure _ -> ()
        | Success _ -> failwith $"{name}: expected the reaction file to be refused"

// ---------- step 7: transport, phase, material and safety groups ----------

/// Every species is classified for material interaction and safety; the flags
/// are screening, and the record says so.
[<Fact>]
let ``material interaction and safety are classified for every species`` () =
    for KeyValue(key, sp) in db do
        Assert.True(sp.MaterialInteraction.IsSome, $"{key}: not classified for materials")
        Assert.True(sp.Safety.IsSome, $"{key}: not classified for safety")
    let m key = db.[key].MaterialInteraction.Value
    Assert.True((m "NH3").Nitriding)
    Assert.True((m "CO").Carburizing && (m "CO").MetalDusting)
    Assert.True((m "H2S").Sulfidation && (m "H2S").HydrogenService)
    Assert.True((m "H2").HydrogenService)
    Assert.False((m "N2").Nitriding || (m "N2").Oxidation)
    let s key = db.[key].Safety.Value
    Assert.True((s "H2").Flammable)
    Assert.True((s "H2S").Toxic && (s "H2S").Flammable)
    Assert.False((s "N2").Flammable || (s "N2").Toxic)
    Assert.Contains("SDS", (s "H2").Source)

/// Numeric limits are not in the database until a checked source is
/// transcribed; a fabricated LFL would be indistinguishable from data.
[<Fact>]
let ``unsourced numbers are absent with a stated reason`` () =
    for KeyValue(key, sp) in db do
        Assert.True(sp.LennardJones.IsNone || sp.LennardJonesUnavailable.IsNone, key)
        if sp.LennardJones.IsNone then
            Assert.True(sp.LennardJonesUnavailable.IsSome, $"{key}: no LJ parameters and no reason")
        Assert.True(sp.Phase.Unavailable.IsSome || sp.Phase.NormalBoilingPoint.IsSome, key)
        match sp.Safety with
        | Some safety ->
            Assert.Equal(None, safety.LowerFlammabilityLimit)
            Assert.Equal(None, safety.AutoIgnitionTemperature)
        | None -> ()

[<Fact>]
let ``a legacy record without the step 7 groups is unclassified, not safe`` () =
    let json = """
    { "schemaVersion": "2.1", "species": [
      { "key": "H2", "name": "Hydrogen", "molarMass_kg_kmol": 2.01588,
        "cpModel": { "kind": "anchor", "anchorCp500C": 14.5 } } ] }
    """
    let h2 = (SpeciesDatabase.parse json |> value).["H2"]
    Assert.Equal(None, h2.MaterialInteraction)
    Assert.Equal(None, h2.Safety)
    Assert.True(h2.Phase.Unavailable.IsSome)
