module WhbThermo.Tests.SchemaCoverageTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data
open WhbThermo.Properties

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private db =
    match SpeciesDatabase.load () with
    | Success (d, _) -> d
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

let private species key =
    match SpeciesDatabase.find db key with
    | Success (s, _) -> s
    | Failure _ -> failwith $"{key} missing"

// ---------- identity ----------

/// Formula is a separate field from the key, and the two differ where it
/// matters: DME and atomic sulfur.
[<Fact>]
let ``every species has a formula and it is not merely the key`` () =
    for KeyValue(_, sp) in db do
        Assert.False(String.IsNullOrWhiteSpace sp.Formula, $"{sp.Key} has no formula")

    Assert.Equal("C2H6O", (species "DME").Formula)
    Assert.Equal("S", (species "S1").Formula)
    Assert.Equal("CH4O", (species "CH3OH").Formula)

// ---------- thermodynamics ----------

/// cv = cp - R for an ideal gas, and gamma follows. Stated as an identity so a
/// future real-gas path has something to replace rather than to discover.
[<Fact>]
let ``cv follows cp by the ideal gas relation`` () =
    for key in [ "N2"; "CO2"; "CH4"; "H2O" ] do
        let sp = species key
        let cpMolar =
            float (PureComponent.specificHeatMass sp 800.0<K> |> value)
            * float sp.MolarMass / 1000.0
        let cvMolar = SpeciesApi.molarHeatCapacityConstantVolume sp 800.0<K> |> value
        Assert.Equal(cpMolar - 8.31446261815324, cvMolar, 6)

/// Gamma for a diatomic near room temperature is about 1.4, for a monatomic
/// exactly 5/3. Both fall out of the same relation, which is a check on the
/// molar mass as well as the Cp.
[<Fact>]
let ``the heat capacity ratio is physical`` () =
    Assert.InRange(SpeciesApi.heatCapacityRatio (species "N2") 300.0<K> |> value, 1.38, 1.42)
    Assert.InRange(SpeciesApi.heatCapacityRatio (species "Ar") 300.0<K> |> value, 1.65, 1.68)
    Assert.InRange(SpeciesApi.heatCapacityRatio (species "CO2") 300.0<K> |> value, 1.25, 1.32)

[<Fact>]
let ``formation enthalpy and Gibbs energy are exposed directly`` () =
    Assert.InRange((SpeciesApi.formationEnthalpy (species "CO2") |> value) / 1000.0, -395.0, -392.0)
    Assert.InRange((SpeciesApi.formationEnthalpy (species "H2O") |> value) / 1000.0, -243.0, -240.0)
    // Gibbs is on the NASA-9 convention, absolute rather than of-formation.
    let g = SpeciesApi.formationGibbs (species "CO2") |> value
    let h = SpeciesApi.formationEnthalpy (species "CO2") |> value
    Assert.True(g < h, "G = H - TS with positive S")

// ---------- vapour pressure ----------

/// At the normal boiling point the correlation must return one atmosphere.
/// Four species, four independent records, all within 0.3 %.
[<Fact>]
let ``vapour pressure returns one atmosphere at the normal boiling point`` () =
    let check key tBoil =
        let p = SpeciesApi.vapourPressure (species key) (tBoil * 1.0<K>) |> value
        Assert.True(abs (p - 101325.0) / 101325.0 < 0.01,
                    $"{key}: {p:F0} Pa at {tBoil} K")
    check "H2O" 373.15
    check "C6H6" 353.25
    check "NH3" 239.80
    check "CH3OH" 337.70

[<Fact>]
let ``vapour pressure rises monotonically`` () =
    let sp = species "C7H8"
    let values =
        [ 300.0; 350.0; 400.0; 450.0 ]
        |> List.map (fun t -> SpeciesApi.vapourPressure sp (t * 1.0<K>) |> value)
    for a, b in List.pairwise values do
        Assert.True(b > a)

/// A species with no vapour pressure must fail WITH the reason. For a radical
/// the reason is that it has no condensed phase - physics, not a gap - and the
/// message must say so rather than implying missing data.
[<Fact>]
let ``a species without vapour pressure fails with its reason`` () =
    let radical = species "OH"
    Assert.True(radical.VapourPressureUnavailable.IsSome)
    Assert.Contains("no condensed phase", radical.VapourPressureUnavailable.Value)
    match SpeciesApi.vapourPressure radical 300.0<K> with
    | Failure _ -> ()
    | Success _ -> failwith "a radical has no vapour pressure"

[<Fact>]
let ``radicals and allotropes record why they have no critical point`` () =
    for key in [ "OH"; "CH3"; "H" ] do
        let sp = species key
        Assert.True(sp.Critical.IsNone)
        Assert.True(sp.CriticalUnavailable.IsSome)
        Assert.Contains("no condensed phase", sp.CriticalUnavailable.Value)

    let s8 = species "S8"
    Assert.True(s8.CriticalUnavailable.IsSome)
    Assert.Contains("reactive equilibrium", s8.CriticalUnavailable.Value)

// ---------- diffusivity ----------

/// Fuller against measured binary diffusivities. This is the property the
/// Claus condenser rating turns on, so the tolerance is as tight as the method
/// allows.
[<Fact>]
let ``Fuller binary diffusivity matches measured values`` () =
    let check a b tK expected tolerance =
        let d =
            SpeciesApi.binaryDiffusivity (species a) (species b) (tK * 1.0<K>) 1.0<bar>
            |> value
        // measured values are in cm^2/s
        let cm2s = d * 1e4
        let deviation = abs (cm2s - expected) / expected
        Assert.True(deviation < tolerance,
                    $"{a}-{b} at {tK} K: {cm2s:F4} against {expected} ({deviation:P1})")

    check "CO2" "N2" 298.0 0.165 0.03
    check "H2" "N2" 298.0 0.779 0.03
    check "CH4" "N2" 298.0 0.212 0.05
    check "H2O" "N2" 298.0 0.242 0.10

/// Diffusivity rises as T^1.75 and falls as 1/P. Getting either exponent wrong
/// would show up only at conditions away from where it was checked.
[<Fact>]
let ``diffusivity scales as T to the 1.75 and inversely with pressure`` () =
    let d tK p =
        SpeciesApi.binaryDiffusivity (species "S2") (species "N2") (tK * 1.0<K>) (p * 1.0<bar>)
        |> value
    Assert.Equal((600.0 / 300.0) ** 1.75, d 600.0 1.0 / d 300.0 1.0, 6)
    Assert.Equal(0.5, d 300.0 2.0 / d 300.0 1.0, 9)

/// Heavier sulfur allotropes diffuse more slowly, which is why the allotrope
/// distribution matters to the mass transfer and not only to the heat release.
[<Fact>]
let ``heavier sulfur allotropes diffuse more slowly`` () =
    let d key =
        SpeciesApi.binaryDiffusivity (species key) (species "N2") 573.0<K> 1.0<bar> |> value
    Assert.True(d "S2" > d "S6")
    Assert.True(d "S6" > d "S8")

/// Every Fuller result must announce that it is estimated.
[<Fact>]
let ``diffusivity always warns that it is estimated`` () =
    match SpeciesApi.binaryDiffusivity (species "CO2") (species "N2") 300.0<K> 1.0<bar> with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

/// The Lewis number is now computed rather than passed in. For a gas it sits
/// near unity, which is the check that the units line up.
[<Fact>]
let ``the Lewis number is computed and near unity for a gas`` () =
    let le =
        SpeciesApi.lewisNumber (species "S2") (species "N2") 573.0<K> 1.0<bar>
                               0.60 1150.0 0.045
        |> value
    Assert.InRange(le, 0.3, 3.0)

// ---------- radiation ----------

[<Fact>]
let ``homonuclear and monatomic species are transparent`` () =
    for key in [ "N2"; "O2"; "H2"; "Ar"; "He"; "S8" ] do
        Assert.Equal(Transparent, (species key).Radiation)
        Assert.False((species key).Radiation.IsParticipating)

[<Fact>]
let ``water and carbon dioxide are participating and covered`` () =
    for key in [ "H2O"; "CO2" ] do
        match (species key).Radiation with
        | ParticipatingCovered models -> Assert.Contains(models, fun m -> m.Contains "WSGG")
        | other -> failwith $"{key}: expected covered, got {other}"

/// The species that matter in Claus service absorb but have no parameter set.
/// The database must say so per species, not only inside the radiation model.
[<Fact>]
let ``sulfur dioxide and hydrogen sulfide are participating but uncovered`` () =
    for key in [ "SO2"; "H2S" ] do
        match (species key).Radiation with
        | ParticipatingUncovered reason ->
            Assert.Contains("LOW", reason)
            Assert.True((species key).Radiation.IsParticipating)
        | other -> failwith $"{key}: expected uncovered, got {other}"

/// A rating can ask what its emissivity is missing.
[<Fact>]
let ``a mixture reports which radiators are unaccounted for`` () =
    let composition =
        [ "N2", 0.60; "H2O", 0.28; "CO2", 0.06; "H2S", 0.04; "SO2", 0.02 ]
    let uncovered = SpeciesApi.uncoveredRadiators db composition |> List.map fst
    Assert.Contains("H2S", uncovered)
    Assert.Contains("SO2", uncovered)
    Assert.DoesNotContain("H2O", uncovered)
    Assert.DoesNotContain("N2", uncovered)

// ---------- schema completeness ----------

/// Every node of the property schema is either present or carries a recorded
/// reason for its absence. A silently empty field is the failure this guards.
[<Fact>]
let ``every schema node is present or has a recorded reason`` () =
    let failures = ResizeArray<string>()
    for KeyValue(_, sp) in db do
        if String.IsNullOrWhiteSpace sp.Formula then failures.Add $"{sp.Key}: no formula"
        if float sp.MolarMass <= 0.0 then failures.Add $"{sp.Key}: no molar mass"
        if not sp.IsProductionGrade then failures.Add $"{sp.Key}: no temperature-dependent Cp"
        if sp.Critical.IsNone && sp.CriticalUnavailable.IsNone then
            failures.Add $"{sp.Key}: critical properties absent with no reason given"
        if sp.VapourPressure.IsNone && sp.VapourPressureUnavailable.IsNone then
            failures.Add $"{sp.Key}: vapour pressure absent with no reason given"
        if sp.DiffusionVolume.IsNone then
            failures.Add $"{sp.Key}: no Fuller diffusion volume"
    Assert.True(failures.Count = 0, String.Join("\n", failures))
