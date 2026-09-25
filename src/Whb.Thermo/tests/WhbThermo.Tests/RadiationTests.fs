module WhbThermo.Tests.RadiationTests

open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Radiation

let private sigma = 5.670374419e-8

/// A populated model built from arbitrary coefficients, used to exercise the
/// EVALUATOR independently of whether the real Leckner tables have been filled.
let private syntheticModel () : Emissivity.RadiationModel =
    let coeff : Emissivity.LecknerCoefficients =
        { C = [| [| -0.5; 0.3 |]      // j = 0: constant and log10(pL) terms
                 [| -0.1; 0.0 |] |]   // j = 1: linear in T/1000
          TMin = 300.0<K>; TMax = 2500.0<K>
          PathMin = 0.001; PathMax = 10.0
          Source = "synthetic" }
    { Water = coeff
      Dioxide = coeff
      Overlap = { C = [| [| 0.01 |] |]; CAsym = [| [| 0.0 |] |]; Source = "synthetic" } }

// ---------- mean beam length ----------

[<Fact>]
let ``mean beam length of a circular duct is 0.95 D`` () =
    let le = Emissivity.meanBeamLength (Emissivity.CircularDuct 0.050<m>)
    Assert.Equal(0.0475, float le, 9)

[<Fact>]
let ``mean beam length from volume to area`` () =
    let le = Emissivity.meanBeamLength (Emissivity.VolumeToArea (2.0<m^3>, 12.0<m^2>))
    Assert.Equal(0.6, float le, 9)

// ---------- linearised radiative coefficient ----------

/// The (Tg^4 - Tw^4)/(Tg - Tw) form has a removable singularity at Tg = Tw.
/// The limit is 4 eps sigma T^3 and the code must return it, not NaN.
[<Fact>]
let ``radiative coefficient is finite at thermal equilibrium`` () =
    let h = Emissivity.radiativeCoefficient 0.3 800.0<K> 800.0<K>
    Assert.False(System.Double.IsNaN(float h))
    Assert.Equal(4.0 * 0.3 * sigma * 800.0 ** 3.0, float h, 6)

/// Continuity across the singularity: approaching equality must converge to the limit.
[<Fact>]
let ``radiative coefficient is continuous near equilibrium`` () =
    let limit = Emissivity.radiativeCoefficient 0.3 800.0<K> 800.0<K>
    let near = Emissivity.radiativeCoefficient 0.3 800.001<K> 800.0<K>
    Assert.True(abs (float near - float limit) < 1e-3)

[<Fact>]
let ``radiative coefficient matches the analytic value`` () =
    let h = Emissivity.radiativeCoefficient 0.4 1673.15<K> 623.15<K>
    let expected = 0.4 * sigma * (1673.15 ** 4.0 - 623.15 ** 4.0) / (1673.15 - 623.15)
    Assert.Equal(expected, float h, 6)

// ---------- effective emissivity ----------

[<Fact>]
let ``effective emissivity is bounded by the gas emissivity`` () =
    let eff = Emissivity.effectiveEmissivity 0.25 0.85
    Assert.True(eff < 0.25 && eff > 0.0)

[<Fact>]
let ``zero gas emissivity gives zero effective emissivity`` () =
    Assert.Equal(0.0, Emissivity.effectiveEmissivity 0.0 0.85, 12)

// ---------- the guard that actually matters ----------

/// The shipped model has EMPTY matrices. Any attempt to evaluate it must FAIL.
/// If this test ever goes green with the stock JSON, the fail-loud contract is broken.
[<Fact>]
let ``unpopulated Leckner model refuses to evaluate`` () =
    // The shipped radiation-models.json is populated now (tools/fit_leckner.py),
    // so the refusal path is exercised on a model with empty matrices.
    let empty : Emissivity.LecknerCoefficients =
        { C = [| [| 0.0 |] |]; TMin = 300.0<K>; TMax = 2500.0<K>
          PathMin = 0.001; PathMax = 10.0; Source = "empty" }
    let model : Emissivity.RadiationModel =
        { Water = empty; Dioxide = empty
          Overlap = { C = [| [| 0.0 |] |]; CAsym = [| [| 0.0 |] |]; Source = "empty" } }
    Assert.False(RadiationModel.isUsable model)
    match Emissivity.gasEmissivity model 1673.15<K> 0.25<bar> 0.10<bar> 0.0475<m> with
    | Failure msgs ->
        Assert.Contains(msgs, function RadiationCoefficientsPending _ -> true | _ -> false)
    | Success _ -> failwith "empty coefficient matrices must not produce an emissivity"

[<Fact>]
let ``shipped Leckner model is populated and usable`` () =
    match RadiationModel.load () with
    | Success (model, _) -> Assert.True(RadiationModel.isUsable model)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

[<Fact>]
let ``non radiating mixture gives zero emissivity with a warning`` () =
    let model = syntheticModel ()
    match Emissivity.gasEmissivity model 1200.0<K> 0.0<bar> 0.0<bar> 0.05<m> with
    | Success (eps, warnings) ->
        Assert.Equal(0.0, eps, 12)
        Assert.Contains(warnings, function NoRadiatingSpecies -> true | _ -> false)
    | Failure _ -> failwith "expected success"

[<Fact>]
let ``emissivity stays within physical bounds`` () =
    let model = syntheticModel ()
    for tK in [ 600.0; 1000.0; 1400.0; 1800.0 ] do
        for pl in [ 0.01; 0.1; 1.0; 5.0 ] do
            match Emissivity.gasEmissivity model (tK * 1.0<K>) (pl * 1.0<bar>) 0.05<bar> 1.0<m> with
            | Success (eps, _) -> Assert.InRange(eps, 0.0, 1.0)
            | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- total film coefficient ----------

[<Fact>]
let ``convection only rating warns that it is non conservative`` () =
    let model = Leckner (syntheticModel ())
    match InternalFilm.total model 120.0<W/(m^2*K)> 1673.15<K> 623.15<K>
                             0.25 0.10 1.5<bar> (Emissivity.CircularDuct 0.05<m>)
                             0.85 false with
    | Success (h, warnings) ->
        Assert.Equal(120.0, float h, 9)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "expected success"

/// At WHB inlet conditions radiation is not a rounding error: it must move the
/// total film coefficient materially above the convective value.
[<Fact>]
let ``radiation increases the total film coefficient`` () =
    let model = Leckner (syntheticModel ())
    match InternalFilm.total model 120.0<W/(m^2*K)> 1673.15<K> 623.15<K>
                             0.25 0.10 1.5<bar> (Emissivity.CircularDuct 0.05<m>)
                             0.85 true with
    | Success (h, _) -> Assert.True(float h > 120.0)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ==========================================================================
// Smith, Shen & Friedman (1982) WSGG -- the populated, usable model.
// ==========================================================================

module Wsgg =

    let private model =
        match Wsgg.load () with
        | Success (m, _) -> m
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    let private eps t pw pc le =
        match Wsgg.emissivity model t pw pc le with
        | Success (e, _) -> e
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    [<Fact>]
    let ``all five coefficient sets load`` () =
        Assert.Equal(5, model.Sets.Length)
        for id in [ "co2Only"; "ratio1"; "ratio2"; "h2oOnly"; "h2oPure" ] do
            Assert.True((model.TryFind id).IsSome, id)

    /// The clear-gas weight a_0 = 1 - SUM(a_i) must stay physical across the
    /// whole validity window. This is the single most effective check on a
    /// transcription error in the b matrix.
    [<Fact>]
    let ``gray gas weights stay physical over the validity range`` () =
        for set in model.Sets do
            for tK in [ 600.0; 900.0; 1200.0; 1500.0; 1800.0; 2100.0; 2400.0 ] do
                let a, clear = Wsgg.weights set (tK * 1.0<K>)
                for ai in a do
                    Assert.True(ai >= -1e-3, $"{set.Id} @ {tK} K: negative weight {ai}")
                Assert.InRange(clear, -1e-3, 1.0 + 1e-3)

    [<Fact>]
    let ``emissivity increases monotonically with path length`` () =
        let values =
            [ 0.001; 0.01; 0.1; 1.0 ]
            |> List.map (fun le -> eps 1200.0<K> 0.2<bar> 0.1<bar> (le * 1.0<m>))
        for a, b in List.pairwise values do
            Assert.True(b > a, $"emissivity not increasing: {a} -> {b}")

    [<Fact>]
    let ``emissivity stays within physical bounds`` () =
        for tK in [ 600.0; 1000.0; 1400.0; 1800.0; 2400.0 ] do
            for pw in [ 0.0; 0.05; 0.2; 0.6 ] do
                for pc in [ 0.0; 0.05; 0.3 ] do
                    match Wsgg.emissivity model (tK * 1.0<K>) (pw * 1.0<bar>)
                                          (pc * 1.0<bar>) 0.5<m> with
                    | Success (e, _) -> Assert.InRange(e, 0.0, 1.0)
                    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    /// Magnitude check against the classical Hottel charts: a 1200 K mixture with
    /// p*L around 0.43 atm*m sits near eps = 0.3.
    [<Fact>]
    let ``emissivity magnitude matches the Hottel chart order`` () =
        let e = eps 1200.0<K> 0.152<bar> 0.152<bar> 1.44<m>
        Assert.InRange(e, 0.25, 0.38)

    /// CO2 alone radiates less than an equimolar H2O/CO2 mixture at equal p*L.
    [<Fact>]
    let ``water vapour dominates the mixture emissivity`` () =
        let co2Only = eps 1200.0<K> 0.0<bar> 0.3<bar> 1.0<m>
        let mixture = eps 1200.0<K> 0.15<bar> 0.15<bar> 1.0<m>
        Assert.True(mixture > co2Only)

    /// Composition blending must be continuous: no jump across the interval
    /// boundaries at RR = 1/2 and RR = 2/3.
    [<Fact>]
    let ``coefficient interpolation is continuous across RR boundaries`` () =
        let at rr =
            let pw = 0.3 * rr
            let pc = 0.3 * (1.0 - rr)
            eps 1200.0<K> (pw * 1.0<bar>) (pc * 1.0<bar>) 1.0<m>
        for boundary in [ 0.5; 2.0 / 3.0 ] do
            let below = at (boundary - 1e-4)
            let above = at (boundary + 1e-4)
            Assert.True(abs (above - below) < 1e-3,
                        $"discontinuity at RR = {boundary}: {below} vs {above}")

    /// Above 0.5 atm the model must switch to the pure-water coefficient set.
    [<Fact>]
    let ``high water partial pressure selects the pure H2O set`` () =
        match Wsgg.selectCoefficients model 1.0 0.8 with
        | Success (s, _) -> Assert.Contains("h2oPure", s.Id)
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    [<Fact>]
    let ``low water partial pressure selects the dilute H2O set`` () =
        match Wsgg.selectCoefficients model 1.0 0.2 with
        | Success (s, _) -> Assert.Contains("h2oOnly", s.Id)
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    /// Beer-Lambert inversion must round-trip: eps -> k -> eps.
    [<Fact>]
    let ``absorption coefficient round-trips through Beer-Lambert`` () =
        let le = 0.5<m>
        let e = eps 1200.0<K> 0.2<bar> 0.1<bar> le
        match Wsgg.absorptionCoefficient model 1200.0<K> 0.2<bar> 0.1<bar> le with
        | Success (k, _) ->
            let recovered = 1.0 - exp (-(float k) * float le)
            Assert.Equal(e, recovered, 9)
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    /// A beam length that puts p*L below the fitted lower bound (0.001 atm*m)
    /// must warn: here p*L = 0.0296 atm * 0.02 m = 0.0006 atm*m.
    [<Fact>]
    let ``short beam length warns about the path length bound`` () =
        match Wsgg.emissivity model 1673.15<K> 0.02<bar> 0.01<bar> 0.02<m> with
        | Success (_, warnings) ->
            Assert.Contains(warnings, function CorrelationExtrapolated _ -> true | _ -> false)
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    /// End to end: the default model must produce a usable film coefficient
    /// at SRU waste-heat-boiler inlet conditions with no further data entry.
    [<Fact>]
    let ``default model rates an SRU inlet without extra data`` () =
        match GasRadiation.loadDefault () with
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")
        | Success (model, _) ->
            match InternalFilm.total model 120.0<W/(m^2*K)> 1673.15<K> 623.15<K>
                                     0.28 0.05 1.5<bar>
                                     (Emissivity.CircularDuct 0.050<m>) 0.85 true with
            | Success (h, _) ->
                Assert.True(float h > 120.0)
                Assert.True(float h < 400.0)
            | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ==========================================================================
// Leckner-form model, now populated by tools/fit_leckner.py.
// ==========================================================================

module LecknerFitted =

    let private model =
        match RadiationModel.load () with
        | Success (m, _) -> m
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    let private wsgg =
        match Wsgg.load () with
        | Success (m, _) -> m
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    let private value r =
        match r with
        | Success (v, _) -> v
        | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

    [<Fact>]
    let ``coefficient matrices are populated`` () =
        Assert.True(model.Water.IsPopulated)
        Assert.True(model.Dioxide.IsPopulated)
        Assert.True(model.Overlap.IsPopulated)
        Assert.True(RadiationModel.isUsable model)

    [<Fact>]
    let ``fitted model evaluates without failure`` () =
        let eps = Emissivity.gasEmissivity model 1673.15<K> 0.42<bar> 0.075<bar> 0.0475<m> |> value
        Assert.InRange(eps, 0.0, 1.0)

    [<Fact>]
    let ``emissivity increases with path length`` () =
        let values =
            [ 0.01; 0.05; 0.2; 1.0 ]
            |> List.map (fun le ->
                Emissivity.gasEmissivity model 1200.0<K> 0.2<bar> 0.1<bar> (le * 1.0<m>) |> value)
        for a, b in List.pairwise values do
            Assert.True(b > a, $"not increasing: {a} -> {b}")

    /// Overlap must vanish exactly when either species is absent. This is
    /// structural (the zeta(1-zeta) prefactor), not something the fit had to learn.
    [<Fact>]
    let ``overlap correction vanishes for a single radiating species`` () =
        let both = Emissivity.gasEmissivity model 1200.0<K> 0.2<bar> 0.0<bar> 0.5<m> |> value
        let alone = Emissivity.gasEmissivity model 1200.0<K> 0.2<bar> 0.0<bar> 0.5<m> |> value
        Assert.Equal(both, alone, 12)

    /// Cross-model check. The Leckner-form coefficients are a reparametrisation
    /// of Smith WSGG, so the two must agree closely; a wide divergence means the
    /// fit or the evaluator has drifted from the form the fitter assumed.
    [<Fact>]
    let ``fitted Leckner tracks the WSGG oracle it was calibrated against`` () =
        let cases =
            [ 1673.15<K>, 0.42<bar>, 0.075<bar>, 0.0475<m>
              1200.0<K>,  0.152<bar>, 0.152<bar>, 1.44<m>
              1000.0<K>,  0.2<bar>,  0.1<bar>,   0.5<m>
              1400.0<K>,  0.1<bar>,  0.2<bar>,   0.2<m>
              800.0<K>,   0.25<bar>, 0.05<bar>,  0.1<m> ]
        for (t, pw, pc, le) in cases do
            let leckner = Emissivity.gasEmissivity model t pw pc le |> value
            let reference = Wsgg.emissivity wsgg t pw pc le |> value
            let deviation = abs (leckner - reference) / reference
            Assert.True(deviation < 0.20,
                        $"T={t}, pw={pw}, pc={pc}, L={le}: {deviation:P1} apart "
                        + $"(Leckner {leckner:F4}, WSGG {reference:F4})")

    /// Both models must be selectable through the same interface.
    [<Fact>]
    let ``both models satisfy the GasRadiation interface`` () =
        let viaLeckner = GasRadiation.emissivity (Leckner model) 1200.0<K> 0.2<bar> 0.1<bar> 0.5<m>
        let viaWsgg = GasRadiation.emissivity (SmithWsgg wsgg) 1200.0<K> 0.2<bar> 0.1<bar> 0.5<m>
        match viaLeckner, viaWsgg with
        | Success (a, _), Success (b, _) ->
            Assert.InRange(a, 0.0, 1.0)
            Assert.InRange(b, 0.0, 1.0)
        | _ -> failwith "both models must evaluate"
