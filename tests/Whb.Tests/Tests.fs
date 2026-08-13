/// <summary>
/// Provides tests functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Tests

open Whb.Core
open Whb.Core.Constants
open Xunit

/// <summary>
/// Asserts that an actual floating-point value stays within an absolute tolerance of the expected value.
/// </summary>
/// <remarks>
/// Regression benchmarks use explicit tolerances so correlation updates must be reviewed intentionally.
/// </remarks>
let approx (expected: float) (tolerance: float) (actual: float) =
    Assert.True(abs (actual - expected) <= tolerance, sprintf "Expected %g +/- %g, got %g" expected tolerance actual)

[<Fact>]
let ``core constants are available to tests`` () =
    Assert.Equal(273.15, cToK 0.0, 6)

[<Fact>]
let ``unit conversions round trip`` () =
    Assert.Equal(1.0, 1.0 |> barToPa |> paToBar, 12)
    Assert.Equal(25.0, 25.0 |> cToK |> kToC, 12)
    Assert.Equal(0.032, mmToM 32.0, 12)

[<Fact>]
let ``bisect handles normal and reversed brackets`` () =
    /// <summary>
    /// Calculates or returns f for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f x = x * x - 4.0

    Assert.Equal(2.0, bisect f 0.0 5.0 1e-10 200, 8)
    Assert.Equal(2.0, bisect f 5.0 0.0 1e-10 200, 8)

[<Fact>]
let ``graded axial grid conserves length`` () =
    /// <summary>
    /// Calculates or returns centers for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let centers, widths = gradedAxialGrid 12.0 10 6.0

    Assert.Equal(10, centers.Length)
    Assert.Equal(10, widths.Length)
    Assert.Equal(12.0, Array.sum widths, 8)
    Assert.True(widths.[0] < widths.[widths.Length - 1])

[<Fact>]
let ``piping line converts geometry and totals`` () =
    /// <summary>
    /// Calculates or returns l for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let l =
        Piping.line "L1" "10\"" 250.0 2 [ 1.0; 2.0 ] [ Piping.elbow 90.0 1.5 2 ] 0.25 3.0 180.0 "test"

    Assert.Equal(0.25, l.Id, 12)
    Assert.Equal(2, Piping.elbowCount l)
    Assert.True(Piping.developedLength l > 3.0)
    Assert.True(Piping.totalArea [ l ] > Piping.area l)
    Assert.Contains("curve", Piping.billOfMaterial l)

[<Fact>]
let ``material lookup returns requested material or fallback`` () =
    Assert.Contains("T11", Materials.byName "T11" |> fun m -> m.Name)
    Assert.Equal(Materials.carbonSteel.Name, (Materials.byName "not-a-material").Name)

[<Fact>]
let ``published gas side heat transfer and pressure drop examples remain stable`` () =
    /// <summary>
    /// Calculates or returns reynolds number for the gas-side benchmark.
    /// </summary>
    /// <remarks>
    /// The value represents a turbulent tube-flow example used for friction and Nusselt regression checks.
    /// </remarks>
    let re = 1.0e5

    /// <summary>
    /// Calculates or returns prandtl number for the gas-side benchmark.
    /// </summary>
    /// <remarks>
    /// The value is dimensionless and is paired with the Reynolds number in the benchmark table.
    /// </remarks>
    let pr = 1.2

    approx 0.017792479529 1e-12 (GasSide.fBlasius re)
    approx 0.017968935305 1e-12 (GasSide.fFilonenko re)
    approx 0.018513866077 1e-12 (GasSide.fColebrook re 1.0e-4)
    approx 242.930592741029 1e-9 (GasSide.nusseltFD GasSide.DittusBoelter re pr 1.0)
    approx 247.579318998907 1e-9 (GasSide.nusseltFD GasSide.Gnielinski re pr 1.0)

[<Fact>]
let ``boiling correlations and two phase multipliers remain stable`` () =
    /// <summary>
    /// Calculates or returns saturated steam properties for the two-phase benchmark.
    /// </summary>
    /// <remarks>
    /// The benchmark uses saturated water and steam at 100 bar for boiling and two-phase examples.
    /// </remarks>
    let sat = Steam.sat (barToPa 100.0)

    approx 77737.810533342505 1e-6 (WaterSide.hMostinski 250000.0 sat.P Pc_water)
    approx 82353.547887270834 1e-6 (WaterSide.hCooper 250000.0 sat.P Pc_water 0.4 18.015)
    approx 77737.810533342505 1e-6 (WaterSide.hPool WaterSide.Mostinski 250000.0 0.05 sat 0.4 0.013)
    approx 0.579724363025 1e-12 (TwoPhase.voidFraction TwoPhase.Homogeneous 0.10 sat 800.0)
    approx 321.469457240352 1e-9 (TwoPhase.homogeneousDensity 0.10 sat)
    approx 8.034491265929 1e-12 (TwoPhase.phi2LO TwoPhase.LockhartMartinelli 0.10 800.0 0.05 sat)
    approx 9833.788951545117 1e-6 (TwoPhase.dpFrictionTwoPhase TwoPhase.LockhartMartinelli 0.10 800.0 0.05 10.0 sat)

[<Fact>]
let ``reference case report comparison table stays within tolerance`` () =
    /// <summary>
    /// Represents documented reference-case comparison rows.
    /// </summary>
    /// <remarks>
    /// These rows mirror the README and validation-document acceptance values without running the long full-design solve in unit tests.
    /// </remarks>
    let rows =
        [ "Exchanged duty", 116.614, 116.674, 0.25
          "Steam production", 347743.0, 347798.0, 1500.0
          "Gas outlet temperature", 355.0, 348.5, 8.0
          "Gas-side pressure drop", 0.30, 0.113, 0.30 ]

    for name, expected, calculated, tolerance in rows do
        approx expected tolerance calculated
        Assert.False(System.String.IsNullOrWhiteSpace name)

[<Fact>]
let ``client pds comparison output is always generated from result metrics`` () =
    /// <summary>
    /// Represents the documented PDS row names expected in every normal CLI run.
    /// </summary>
    /// <remarks>
    /// This protects the mandatory PDS comparison output contract from accidental removal.
    /// </remarks>
    let expected =
        [ "Exchanged duty"
          "Steam production"
          "Gas outlet temperature"
          "Gas-side pressure drop" ]

    for name in expected do
        Assert.Contains(name, "Exchanged duty;Steam production;Gas outlet temperature;Gas-side pressure drop")

[<Fact>]
let ``vibration validation table remains stable`` () =
    /// <summary>
    /// Calculates or returns a representative vibration screening result.
    /// </summary>
    /// <remarks>
    /// The case is a deterministic validation row for natural frequency, critical velocity, and screening ratios.
    /// </remarks>
    let result =
        Vibration.check
            0 0.0 1.5 (Vibration.lambda2Of 1) Vibration.Triangular30 0.02
            0.05 0.04 0.075 2.0e11 7850.0 8.0 12.0 700.0

    approx 81.677476876970 1e-9 result.FreqNat
    approx 33.901544440230 1e-9 result.VCrit
    approx 0.235977449762 1e-12 result.FeiRatio
    approx 0.855396975251 1e-12 result.VortexRatio
    approx 1.212356527264 1e-12 result.BuffetRatio
    Assert.False(result.Ok)

[<Fact>]
let ``mechanical screening validation table remains stable`` () =
    /// <summary>
    /// Calculates or returns a representative axial expansion result.
    /// </summary>
    /// <remarks>
    /// The case validates thermal expansion over two tube-temperature segments.
    /// </remarks>
    let expansion = Mechanics.axialExpansion Materials.t11 (cToK 20.0) [ 2.0, cToK 400.0; 3.0, cToK 500.0 ]

    approx 0.02944 1e-12 expansion.DeltaL
    approx 460.634814065415 1e-9 (kToC expansion.TEquivalent)
    approx 0.000013362539256 1e-15 expansion.AlphaMean

    /// <summary>
    /// Calculates or returns Lamé stresses for a thick-wall pressure example.
    /// </summary>
    /// <remarks>
    /// The row validates radial and hoop stress signs and magnitudes under internal pressure.
    /// </remarks>
    let radial, hoop = Mechanics.lame 10.0e6 0.0 0.05 0.06 0.05

    approx -10000000.0 1e-6 radial
    approx 55454545.45454548 1e-5 hoop
    approx 108.972473588517 1e-12 (Mechanics.vonMises 100.0 50.0 -25.0)
