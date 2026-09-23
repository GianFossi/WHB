module WhbThermo.Tests.BuoyancyTests

open System
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Convection

let private value r =
    match r with
    | Success (v, _) -> v
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

// ---------- Churchill & Chu ----------

/// Textbook values (Incropera). The vertical all-range form at Ra = 1e9,
/// Pr = 0.7 gives Nu = 122.6; the horizontal cylinder at Ra = 1e6 gives 14.5.
[<Fact>]
let ``Churchill-Chu vertical matches the textbook value`` () =
    let nu = Buoyancy.churchillChuVertical 1.0e9 0.7 |> value
    Assert.Equal(122.6, nu, 1)

[<Fact>]
let ``Churchill-Chu horizontal cylinder matches the textbook value`` () =
    let nu = Buoyancy.churchillChuHorizontalCylinder 1.0e6 0.7 |> value
    Assert.Equal(14.51, nu, 2)

/// The laminar and all-range forms must agree closely below Ra = 1e9, which is
/// where both are valid. They differ by a few percent, not a factor.
[<Fact>]
let ``the laminar and all-range forms agree in the laminar region`` () =
    for ra in [ 1.0e4; 1.0e6; 1.0e7 ] do
        let allRange = Buoyancy.churchillChuVertical ra 0.7 |> value
        let laminar = Buoyancy.churchillChuVerticalLaminar ra 0.7 |> value
        let deviation = abs (allRange - laminar) / laminar
        Assert.True(deviation < 0.10, $"Ra = {ra}: {deviation:P1} apart")

[<Fact>]
let ``the laminar form warns above its range`` () =
    match Buoyancy.churchillChuVerticalLaminar 1.0e11 0.7 with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

[<Fact>]
let ``natural convection Nusselt rises monotonically with Rayleigh`` () =
    let values =
        [ 1.0e4; 1.0e6; 1.0e8; 1.0e10; 1.0e12 ]
        |> List.map (fun ra -> Buoyancy.churchillChuVertical ra 0.7 |> value)
    for a, b in List.pairwise values do
        Assert.True(b > a)

// ---------- dimensionless groups ----------

[<Fact>]
let ``ideal gas expansion coefficient is one over temperature`` () =
    Assert.Equal(1.0 / 1673.15, Buoyancy.idealGasExpansion 1673.15<K>, 12)

/// Grashof scales with the cube of the characteristic length and the square of
/// density. That is why buoyancy is negligible in a small low-pressure tube and
/// significant in a large or high-pressure one.
[<Fact>]
let ``Grashof scales as length cubed and density squared`` () =
    let gr length density =
        Buoyancy.grashof (1.0 / 1673.15) 623.15<K> 1673.15<K> (length * 1.0<m>)
                         (density * 1.0<kg/m^3>) 59.3e-6<Pa*s>
        |> value

    let baseline = gr 0.044 0.29
    Assert.Equal(8.0, gr 0.088 0.29 / baseline, 6)
    Assert.Equal(4.0, gr 0.044 0.58 / baseline, 6)

[<Fact>]
let ``a cooled wall gives a negative Grashof`` () =
    let gr =
        Buoyancy.grashof (1.0 / 1673.15) 623.15<K> 1673.15<K> 0.044<m>
                         0.29<kg/m^3> 59.3e-6<Pa*s>
        |> value
    Assert.True(gr < 0.0, "the wall is colder than the bulk, so buoyancy is downward")

[<Fact>]
let ``regimes are classified at the conventional thresholds`` () =
    Assert.Equal(Buoyancy.ForcedDominated, Buoyancy.Regime.OfRichardson 0.01)
    Assert.Equal(Buoyancy.Mixed, Buoyancy.Regime.OfRichardson 1.0)
    Assert.Equal(Buoyancy.NaturalDominated, Buoyancy.Regime.OfRichardson 50.0)
    // The sign carries direction, not magnitude: classification uses |Ri|.
    Assert.Equal(Buoyancy.Mixed, Buoyancy.Regime.OfRichardson -1.0)

// ---------- combination ----------

[<Fact>]
let ``assisting buoyancy raises the combined Nusselt above both parts`` () =
    let combined = Buoyancy.combine 30.0 20.0 Buoyancy.Assisting |> value
    Assert.True(combined > 30.0)
    Assert.Equal((30.0 ** 3.0 + 20.0 ** 3.0) ** (1.0 / 3.0), combined, 9)

/// The case that matters. Opposed buoyancy REDUCES the coefficient below the
/// forced-convection value -- combining the two as if buoyancy always helps is
/// optimistic exactly where a WHB is most likely to be.
[<Fact>]
let ``opposing buoyancy reduces the combined Nusselt below the forced value`` () =
    let combined = Buoyancy.combine 30.0 20.0 Buoyancy.Opposing |> value
    Assert.True(combined < 30.0, $"opposed buoyancy must reduce, got {combined}")
    Assert.Equal((30.0 ** 3.0 - 20.0 ** 3.0) ** (1.0 / 3.0), combined, 9)

[<Fact>]
let ``strong opposing buoyancy warns`` () =
    match Buoyancy.combine 30.0 20.0 Buoyancy.Opposing with
    | Success (_, warnings) -> Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed with a warning"

/// When opposed buoyancy exceeds forced convection the near-wall flow reverses
/// and no correlation applies. Refusing is the only honest answer.
[<Fact>]
let ``opposing buoyancy exceeding forced convection is refused`` () =
    match Buoyancy.combine 15.0 20.0 Buoyancy.Opposing with
    | Failure msgs ->
        Assert.Contains(msgs, function CorrelationExtrapolated _ -> true | _ -> false)
    | Success _ -> failwith "flow reversal must be refused, not reported"

[<Fact>]
let ``transverse buoyancy is combined additively`` () =
    let transverse = Buoyancy.combine 30.0 20.0 Buoyancy.Transverse |> value
    let assisting = Buoyancy.combine 30.0 20.0 Buoyancy.Assisting |> value
    Assert.Equal(assisting, transverse, 9)

// ---------- service cases ----------

/// A small low-pressure tube: buoyancy is irrelevant at any credible load.
/// Gr is only 1.3e4 because the gas density is 0.29 kg/m3, and Gr/Re^2 stays
/// below 0.005 even at 10 % of design flow.
[<Fact>]
let ``buoyancy is negligible in the SRU tube even at deep turndown`` () =
    let gr =
        Buoyancy.grashof (1.0 / 1673.15) 623.15<K> 1673.15<K> 0.044<m>
                         0.2926<kg/m^3> 59.3e-6<Pa*s>
        |> value
    Assert.InRange(abs gr, 1.0e4, 1.5e4)

    for loadFraction in [ 1.0; 0.5; 0.25; 0.10 ] do
        let re = 18552.0 * loadFraction
        let ri = Buoyancy.richardson gr re |> value
        Assert.Equal(Buoyancy.ForcedDominated, Buoyancy.Regime.OfRichardson ri)

/// A high-pressure tube is a completely different matter: at 200 bar the gas
/// density is 35 kg/m3, Gr rises to 3.7e8, and the mixed regime is reached at
/// about half load -- well inside normal operation.
[<Fact>]
let ``buoyancy governs the high pressure tube at half load`` () =
    let gr =
        Buoyancy.grashof (1.0 / 723.15) 603.15<K> 723.15<K> 0.050<m>
                         34.93<kg/m^3> 26.0e-6<Pa*s>
        |> value
    Assert.InRange(abs gr, 3.0e8, 4.5e8)

    let designRe = 115385.0
    Assert.Equal(Buoyancy.ForcedDominated,
                 Buoyancy.Regime.OfRichardson (Buoyancy.richardson gr designRe |> value))
    Assert.Equal(Buoyancy.Mixed,
                 Buoyancy.Regime.OfRichardson (Buoyancy.richardson gr (designRe * 0.5) |> value))

/// The full evaluation must switch behaviour with the regime and warn when it
/// leaves pure forced convection.
[<Fact>]
let ``evaluation warns once buoyancy becomes significant`` () =
    let gr = -3.67e8
    match Buoyancy.evaluate 200.0 57000.0 0.75 gr Buoyancy.Opposing
                            0.06<W/(m*K)> 0.050<m> with
    | Success (result, warnings) ->
        Assert.Equal(Buoyancy.Mixed, result.Regime)
        Assert.True(result.NusseltNatural > 0.0)
        Assert.True(result.Nusselt < result.NusseltForced, "opposed buoyancy must reduce")
        Assert.NotEmpty(warnings)
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")

/// Approaching the threshold from the forced side must give early warning, so a
/// rating that is about to become invalid at turndown says so.
[<Fact>]
let ``approaching the mixed regime warns while still forced dominated`` () =
    match Buoyancy.evaluate 200.0 80000.0 0.75 -4.8e8 Buoyancy.Opposing
                            0.06<W/(m*K)> 0.050<m> with
    | Success (result, warnings) ->
        Assert.Equal(Buoyancy.ForcedDominated, result.Regime)
        Assert.NotEmpty(warnings)
    | Failure _ -> failwith "should succeed"

[<Fact>]
let ``the vertical plate approximation is rejected for a slender tube`` () =
    Assert.False(Buoyancy.verticalPlateApproximationValid 0.044<m> 6.0<m> 1.3e4)
    Assert.True(Buoyancy.verticalPlateApproximationValid 0.9<m> 2.0<m> 1.7e8)
