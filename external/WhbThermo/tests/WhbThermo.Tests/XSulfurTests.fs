module WhbThermo.Tests.XSulfurTests

open System
open System.IO
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

let private fraction (d: Speciation.Distribution) key =
    d.Fractions |> List.find (fun (k, _) -> k = key) |> snd

// ---------- speciation ----------

[<Fact>]
let ``all eight allotropes load`` () =
    Assert.Equal(8, model.Allotropes.Length)
    for key in [ "S"; "S2"; "S3"; "S4"; "S5"; "S6"; "S7"; "S8" ] do
        Assert.Contains(model.Allotropes, fun a -> a.Key = key)

[<Fact>]
let ``mole fractions sum to one`` () =
    for tK in [ 400.0; 600.0; 800.0; 1000.0 ] do
        let d = Speciation.distribution model (tK * 1.0<K>) 0.05<bar> |> value
        Assert.Equal(1.0, d.Fractions |> List.sumBy snd, 9)
        for (_, y) in d.Fractions do
            Assert.InRange(y, 0.0, 1.0)

/// The published Claus behaviour: below about 700 degF (644 K) the vapour is
/// dominated by S6 and S8; above about 1000 degF (811 K) it is nearly all S2.
/// Reproducing this from NASA CEA Gibbs energies alone is a strong check on the
/// whole equilibrium solve.
[<Fact>]
let ``speciation reproduces the published Claus behaviour`` () =
    let cold = Speciation.distribution model 644.0<K> 0.0507<bar> |> value
    Assert.True(fraction cold "S6" + fraction cold "S8" > 0.65,
                "below 700 degF the vapour should be dominated by S6 and S8")
    Assert.True(fraction cold "S2" < 0.10)

    let hot = Speciation.distribution model 811.0<K> 0.0507<bar> |> value
    Assert.True(fraction hot "S2" > 0.70,
                "above 1000 degF the vapour should be predominantly S2")
    Assert.True(fraction hot "S8" < 0.05)

/// The average molar mass runs from about 250 g/mol at the cold end of a
/// condenser to about 65 g/mol in the hot gas. A rating that assumes a fixed
/// molecular weight is wrong by a factor of nearly four across that span.
[<Fact>]
let ``average molar mass falls by nearly a factor of four across the range`` () =
    let cold = Speciation.distribution model 450.0<K> 0.05<bar> |> value
    let hot = Speciation.distribution model 900.0<K> 0.05<bar> |> value
    Assert.InRange(cold.AverageMolarMass, 240.0, 255.0)
    Assert.InRange(hot.AverageMolarMass, 62.0, 72.0)
    Assert.True(cold.AverageMolarMass / hot.AverageMolarMass > 3.4)

[<Fact>]
let ``average molar mass falls monotonically with temperature`` () =
    let values =
        [ 400.0; 500.0; 600.0; 700.0; 800.0; 900.0 ]
        |> List.map (fun tK ->
            (Speciation.distribution model (tK * 1.0<K>) 0.05<bar> |> value).AverageMolarMass)
    for a, b in List.pairwise values do
        Assert.True(b < a, $"molar mass not falling: {a} -> {b}")

/// Raising the sulfur partial pressure shifts the equilibrium towards the
/// heavier allotropes, so at a fixed temperature the average molar mass rises.
[<Fact>]
let ``higher sulfur partial pressure favours the heavier allotropes`` () =
    let low = Speciation.distribution model 700.0<K> 0.01<bar> |> value
    let high = Speciation.distribution model 700.0<K> 0.20<bar> |> value
    Assert.True(high.AverageMolarMass > low.AverageMolarMass)
    Assert.True(fraction high "S8" > fraction low "S8")

/// The polymerisation enthalpy is the whole point. Comparing the equilibrium
/// heat capacity with the frozen-composition one shows how much a
/// fixed-molecular-weight model throws away: a factor of 18 at 800 K.
[<Fact>]
let ``the polymerisation enthalpy dominates the effective heat capacity`` () =
    let ratio tK =
        let effective = Speciation.effectiveHeatCapacity model (tK * 1.0<K>) 0.05<bar> |> value
        let frozen = Speciation.frozenHeatCapacity model (tK * 1.0<K>) 0.05<bar> |> value
        effective / frozen

    Assert.InRange(ratio 450.0, 1.2, 1.5)     // condenser outlet: modest
    Assert.InRange(ratio 650.0, 2.5, 3.5)     // condenser inlet: significant
    Assert.InRange(ratio 800.0, 14.0, 23.0)   // reheat range: dominant

[<Fact>]
let ``the effective heat capacity always exceeds the frozen value`` () =
    for tK in [ 400.0; 500.0; 600.0; 700.0; 800.0; 900.0 ] do
        let effective = Speciation.effectiveHeatCapacity model (tK * 1.0<K>) 0.05<bar> |> value
        let frozen = Speciation.frozenHeatCapacity model (tK * 1.0<K>) 0.05<bar> |> value
        Assert.True(effective > frozen,
                    $"at {tK} K the reaction term must add, not subtract")

[<Fact>]
let ``speciation is refused outside its temperature range`` () =
    for tK in [ 200.0; 2500.0 ] do
        match Speciation.distribution model (tK * 1.0<K>) 0.05<bar> with
        | Failure _ -> ()
        | Success _ -> failwith $"{tK} K is outside the modelled range"

[<Fact>]
let ``a non-positive sulfur pressure is refused`` () =
    match Speciation.distribution model 600.0<K> 0.0<bar> with
    | Failure _ -> ()
    | Success _ -> failwith "zero sulfur pressure has no speciation"

// ---------- condensation curve ----------

let private samplePath =
    Path.Combine(AppContext.BaseDirectory, "reference", "sample-condensation-curve.csv")

let private sampleCurve () =
    Assert.True(File.Exists samplePath, "the sample curve fixture is missing")
    CondensationCurve.parseCsv (File.ReadAllText samplePath) |> value

[<Fact>]
let ``a simulator curve export is parsed`` () =
    let curve = sampleCurve ()
    Assert.Equal(20, curve.Points.Length)
    Assert.InRange(float curve.HotEnd.Temperature, 573.0, 574.0)
    Assert.InRange(curve.TotalDuty, 500000.0, 520000.0)

/// A curve exported cold-to-hot interpolates cleanly and rates to nonsense, so
/// it must be rejected outright rather than warned about.
[<Fact>]
let ``a curve ordered cold to hot is rejected`` () =
    let curve = sampleCurve ()
    match CondensationCurve.validate (List.rev curve.Points) "reversed" "test" with
    | Failure _ -> ()
    | Success _ -> failwith "a reversed curve must be rejected"

[<Fact>]
let ``a curve with non-increasing duty is rejected`` () =
    let points =
        [ { CondensationCurve.Temperature = 500.0<K>; HeatRemoved = 0.0
            VapourFraction = 1.0; CondensedSulfur = 0.0; VapourHeatCapacity = Some 1100.0 }
          { CondensationCurve.Temperature = 480.0<K>; HeatRemoved = 0.0
            VapourFraction = 0.8; CondensedSulfur = 0.2; VapourHeatCapacity = Some 1100.0 } ]
    match CondensationCurve.validate points "flat duty" "test" with
    | Failure _ -> ()
    | Success _ -> failwith "a curve with no duty change must be rejected"

[<Fact>]
let ``a single point curve is rejected`` () =
    let point =
        { CondensationCurve.Temperature = 500.0<K>; HeatRemoved = 0.0
          VapourFraction = 1.0; CondensedSulfur = 0.0; VapourHeatCapacity = None }
    match CondensationCurve.validate [ point ] "single" "test" with
    | Failure _ -> ()
    | Success _ -> failwith "one point is not a curve"

[<Fact>]
let ``a coarse curve is warned about`` () =
    let curve = sampleCurve ()
    let coarse = curve.Points |> List.take 5
    match CondensationCurve.validate coarse "coarse" "test" with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "a coarse curve is usable, just worth flagging"

[<Fact>]
let ``interpolation is monotonic and lands on the imported points`` () =
    let curve = sampleCurve ()
    // Exact hit on an imported point.
    let q = CondensationCurve.heatRemovedAt curve 473.15<K> |> value
    Assert.Equal(198200.0, q, 0)

    let duties =
        [ 560.0; 520.0; 480.0; 440.0; 400.0 ]
        |> List.map (fun t -> CondensationCurve.heatRemovedAt curve (t * 1.0<K>) |> value)
    for a, b in List.pairwise duties do
        Assert.True(b > a)

[<Fact>]
let ``a temperature outside the curve is refused`` () =
    let curve = sampleCurve ()
    for t in [ 600.0; 350.0 ] do
        match CondensationCurve.heatRemovedAt curve (t * 1.0<K>) with
        | Failure _ -> ()
        | Success _ -> failwith $"{t} K lies outside the imported curve"

/// dT/dh is what Silver / Bell & Ghaly needs, and it is the reason the curve
/// must come from a flash rather than a correlation.
[<Fact>]
let ``the temperature slope is positive and finite everywhere on the curve`` () =
    let curve = sampleCurve ()
    for t in [ 560.0; 520.0; 480.0; 440.0; 400.0 ] do
        let slope = CondensationCurve.temperatureSlopeAt curve (t * 1.0<K>) |> value
        Assert.True(slope > 0.0 && Double.IsFinite slope)

/// dT/dh falls as condensation proceeds: at the hot end little sulfur is
/// condensing so a joule of duty buys many degrees, while further down the
/// latent heat dominates and the same joule buys far fewer. That is exactly why
/// a condenser cannot be rated on one average LMTD, and why the Z factor - which
/// is proportional to this slope - varies along the curve.
[<Fact>]
let ``the temperature slope falls as condensation proceeds`` () =
    let curve = sampleCurve ()
    let hot = CondensationCurve.temperatureSlopeAt curve 560.0<K> |> value
    let cold = CondensationCurve.temperatureSlopeAt curve 400.0<K> |> value
    Assert.True(hot > cold * 2.0,
                $"dT/dh should fall sharply along the curve: {hot:e3} at the hot end "
                + $"against {cold:e3} at the cold end")

[<Fact>]
let ``the Z factor uses the exported vapour heat capacity`` () =
    let curve = sampleCurve ()
    let z = CondensationCurve.zFactorAt curve 500.0<K> None |> value
    Assert.True(z > 0.0)
    let x = CondensationCurve.vapourFractionAt curve 500.0<K> |> value
    let slope = CondensationCurve.temperatureSlopeAt curve 500.0<K> |> value
    Assert.Equal(x * 1136.0 * slope, z, 3)

/// Refusing to default the vapour heat capacity is deliberate: a wrong value
/// propagates straight into the effective coefficient.
[<Fact>]
let ``the Z factor is refused when no vapour heat capacity is available`` () =
    let points =
        [ { CondensationCurve.Temperature = 500.0<K>; HeatRemoved = 0.0
            VapourFraction = 1.0; CondensedSulfur = 0.0; VapourHeatCapacity = None }
          { CondensationCurve.Temperature = 480.0<K>; HeatRemoved = 50000.0
            VapourFraction = 0.8; CondensedSulfur = 0.2; VapourHeatCapacity = None } ]
    let curve = CondensationCurve.validate points "no cp" "test" |> value
    match CondensationCurve.zFactorAt curve 490.0<K> None with
    | Failure _ -> ()
    | Success _ -> failwith "the vapour heat capacity must not be defaulted"

/// Zoning: a condenser is rated zone by zone because the curve is strongly
/// non-linear, and one average across it is meaningless.
[<Fact>]
let ``equal duty zones span the whole curve`` () =
    let curve = sampleCurve ()
    let zones = CondensationCurve.zones curve 5 |> value
    Assert.Equal(5, zones.Length)

    let (firstStart, _, _) = List.head zones
    let (_, lastEnd, _) = List.last zones
    Assert.Equal(float curve.HotEnd.Temperature, float firstStart, 3)
    Assert.Equal(float curve.ColdEnd.Temperature, float lastEnd, 3)

    let totalDuty = zones |> List.sumBy (fun (_, _, q) -> q)
    Assert.Equal(curve.TotalDuty, totalDuty, 3)

    // Equal duty does not mean equal temperature span, which is the point.
    let spans = zones |> List.map (fun (a, b, _) -> float a - float b)
    Assert.True(List.max spans / List.min spans > 1.3,
                "equal-duty zones must have unequal temperature spans on a non-linear curve")

[<Fact>]
let ``zoning finer than the imported data is warned about`` () =
    let curve = sampleCurve ()
    match CondensationCurve.zones curve 40 with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "over-zoning is usable, just worth flagging"
