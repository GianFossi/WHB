module WhbThermo.Tests.EquilibriumTests

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

let private kelvin c = (c + 273.15) * 1.0<K>

// ---------- absolute thermochemistry ----------

/// Formation enthalpies at 298.15 K, against published values. This is the
/// sharpest check available on the NASA-9 integration constants: the Cp
/// polynomial can be right while b1 is wrong, and only the absolute enthalpy
/// exposes it.
[<Fact>]
let ``formation enthalpies match published values`` () =
    let check key expected tolerance =
        let h = Equilibrium.molarEnthalpy (species key) 298.15<K> |> value / 1000.0
        Assert.True(abs (h - expected) < tolerance,
                    $"{key}: {h:F1} kJ/mol against {expected}")

    check "H" 218.0 1.0
    check "O" 249.2 1.0
    check "N" 472.7 1.0
    check "OH" 37.3 1.0
    check "S1" 277.2 1.0
    check "CH3" 146.7 1.0
    check "CO" -110.5 1.0
    check "CO2" -393.5 1.0
    check "H2O" -241.8 1.0
    check "NO" 91.3 1.0

/// Absolute entropies at 298.15 K.
[<Fact>]
let ``standard entropies match published values`` () =
    let check key expected =
        let s = Equilibrium.molarEntropy (species key) 298.15<K> |> value
        Assert.True(abs (s - expected) < 1.5, $"{key}: {s:F1} J/(mol*K) against {expected}")

    check "N2" 191.6
    check "O2" 205.2
    check "H2O" 188.8
    check "CO2" 213.8
    check "H2S" 205.8

/// The absolute enthalpy must NOT be the difference form: the two differ by the
/// formation enthalpy, and confusing them makes every equilibrium wrong.
[<Fact>]
let ``absolute and relative enthalpies differ by the formation enthalpy`` () =
    let absolute = Equilibrium.molarEnthalpy (species "CO2") 800.0<K> |> value / 1000.0
    let relative = PureComponent.enthalpy (species "CO2") 800.0<K> |> value
    Assert.True(abs (absolute - relative) > 300.0,
                "the absolute enthalpy must carry the formation term")
    Assert.True(abs ((absolute - relative) - (-393.5)) < 3.0)

// ---------- equilibrium constants ----------

/// The water gas shift crosses K = 1 near 820 degC. Reproducing that crossing
/// from the database alone exercises four species at once.
[<Fact>]
let ``water gas shift crosses unity near 820 degC`` () =
    let logK tC =
        (Equilibrium.equilibriumConstant db Equilibrium.Reactions.waterGasShift (kelvin tC)
         |> value).LogK
    Assert.True(logK 700.0 > 0.0, "shift favours products below the crossing")
    Assert.True(logK 900.0 < 0.0, "and reactants above it")

/// H2S cracking is the reaction that decides how much sulfur leaves the thermal
/// stage as S2. Published extents are roughly 20 % at 1000 degC and 50-60 % at
/// 1400 degC; the database reproduces 19.6 % and 54.8 %.
[<Fact>]
let ``H2S cracking extent matches published Claus behaviour`` () =
    let extent tC =
        Equilibrium.dissociationFraction db Equilibrium.Reactions.h2sCracking
                                         (kelvin tC) 1.0<bar>
        |> value
    Assert.InRange(extent 1000.0, 0.15, 0.25)
    Assert.InRange(extent 1400.0, 0.45, 0.65)
    Assert.True(extent 1400.0 > extent 1000.0)

/// Endothermic dissociations must all become more favourable with temperature.
[<Fact>]
let ``endothermic dissociations become more favourable with temperature`` () =
    for reaction in [ Equilibrium.Reactions.h2sCracking
                      Equilibrium.Reactions.sulfurDepolymerisation
                      Equilibrium.Reactions.ammoniaCracking ] do
        let logK tC =
            (Equilibrium.equilibriumConstant db reaction (kelvin tC) |> value).LogK
        let values = [ 600.0; 900.0; 1200.0; 1500.0 ] |> List.map logK
        for a, b in List.pairwise values do
            Assert.True(b > a, $"{reaction.Name}: ln K fell from {a} to {b}")

/// Sulfur depolymerisation is strongly favoured at flame temperature - which is
/// why the thermal stage makes S2 and the catalytic stage makes S8.
[<Fact>]
let ``sulfur depolymerisation is strongly favoured at flame temperature`` () =
    let k = Equilibrium.equilibriumConstant db Equilibrium.Reactions.sulfurDepolymerisation
                                            (kelvin 1200.0) |> value
    Assert.True(k.LogK > 15.0, $"ln K = {k.LogK}")
    Assert.True(k.DeltaH > 0.0, "depolymerisation is endothermic")

/// The Claus reaction itself is exothermic and favoured, which is the whole
/// basis of the process.
[<Fact>]
let ``the Claus reaction is favoured across the furnace range`` () =
    for tC in [ 900.0; 1100.0; 1300.0 ] do
        let k = Equilibrium.equilibriumConstant db Equilibrium.Reactions.clausReaction
                                                (kelvin tC) |> value
        Assert.True(k.LogK > 0.0, $"at {tC} degC ln K = {k.LogK}")

/// An unknown species must fail by name, not silently contribute zero.
[<Fact>]
let ``a reaction naming an unknown species fails`` () =
    let bogus = { Equilibrium.Name = "test"; Terms = [ "UNOBTAINIUM", -1.0; "H2", 1.0 ] }
    match Equilibrium.equilibriumConstant db bogus 1000.0<K> with
    | Failure msgs -> Assert.Contains(msgs, function UnknownSpecies _ -> true | _ -> false)
    | Success _ -> failwith "an unknown species must fail"

/// Overflowing constants must be reported as a logarithm with a warning, not
/// as an infinity that propagates silently.
[<Fact>]
let ``an effectively complete reaction warns rather than overflowing quietly`` () =
    let strong =
        { Equilibrium.Name = "2 H2 + O2 -> 2 H2O"
          Terms = [ "H2", -2.0; "O2", -1.0; "H2O", 2.0 ] }
    match Equilibrium.equilibriumConstant db strong 600.0<K> with
    | Success (k, warnings) ->
        Assert.True(k.LogK > 100.0)
        if k.LogK > 700.0 then Assert.NotEmpty(warnings)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- Chung estimation ----------

let private chungInput tc vc m w dip assoc : Chung.ChungInput =
    { Tc = tc * 1.0<K>; Vc = vc; MolarMass = m
      Acentric = w; DipoleMoment = dip; Association = assoc }

/// Chung reproduces measured viscosities of non-polar gases to a few percent.
[<Fact>]
let ``Chung viscosity matches measured values for non-polar gases`` () =
    let check input tK expected tolerance =
        let mu = Chung.viscosity input (tK * 1.0<K>) |> value
        let deviation = abs (float mu - expected) / expected
        Assert.True(deviation < tolerance, $"{deviation:P1} at {tK} K")

    check (chungInput 126.2 89.8 28.014 0.037 0.0 0.0) 300.0 1.78e-5 0.03
    check (chungInput 126.2 89.8 28.014 0.037 0.0 0.0) 1000.0 4.15e-5 0.05
    check (chungInput 304.1 94.07 44.01 0.224 0.0 0.0) 300.0 1.50e-5 0.03

/// Polar and associating fluids are worse, as documented. Water at 500 K is
/// about 8 % out - within the stated band and outside what can be quoted.
[<Fact>]
let ``Chung is less accurate for polar fluids`` () =
    let mu =
        Chung.viscosity (chungInput 647.1 55.9 18.015 0.345 1.85 0.076) 500.0<K> |> value
    let deviation = abs (float mu - 1.73e-5) / 1.73e-5
    Assert.InRange(deviation, 0.0, 0.15)

/// Every Chung result must announce that it is an estimate. A number that
/// cannot be distinguished from measured data is worse than no number.
[<Fact>]
let ``every Chung result warns that it is estimated`` () =
    match Chung.viscosity (chungInput 126.2 89.8 28.014 0.037 0.0 0.0) 500.0<K> with
    | Success (_, warnings) ->
        Assert.Contains(warnings, function CorrelationExtrapolated _ -> true | _ -> false)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``Chung conductivity needs a real heat capacity`` () =
    let input = chungInput 126.2 89.8 28.014 0.037 0.0 0.0
    match Chung.conductivity input 500.0<K> 0.0 with
    | Failure _ -> ()
    | Success _ -> failwith "a zero heat capacity is not usable"

    let k = Chung.conductivity input 500.0<K> 22.0 |> value
    Assert.InRange(float k, 0.02, 0.06)

// ---------- provenance ----------

[<Fact>]
let ``provenance takes the worse quality when values combine`` () =
    let measured = Qualified.measured "NIST" 10.0
    let estimated = Qualified.estimated "Chung" 2.0
    let combined = Qualified.combine (*) measured estimated
    Assert.Equal(20.0, combined.Value, 9)
    Assert.Equal(Estimated, combined.Quality)
    Assert.False(combined.IsQuotable)

[<Fact>]
let ``a set reports its weakest input and its caveats`` () =
    let items =
        [ Qualified.measured "NIST" 1.0
          Qualified.fitted "Perry" 2.0
          Qualified.estimated "Chung" 3.0 ]
    let quality, source = Qualified.weakest items
    Assert.Equal(Estimated, quality)
    Assert.Equal("Chung", source)
    Assert.Single(Qualified.caveats items) |> ignore

[<Fact>]
let ``only measured and fitted values are quotable`` () =
    Assert.True(Measured.IsQuotable)
    Assert.True(Fitted.IsQuotable)
    Assert.False(Estimated.IsQuotable)
    Assert.False(Extrapolated.IsQuotable)
    Assert.False(Unavailable.IsQuotable)
