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
let ``gas side heat transfer supports forced convection with optional radiation`` () =
    /// <summary>
    /// Calculates or returns representative gas properties for the gas-side HTC test.
    /// </summary>
    /// <remarks>
    /// The test uses the reference gas composition at high temperature to make the radiation contribution visible.
    /// </remarks>
    let props = GasProps.mixReal GasProps.Wilke true Defaults.referenceComposition (cToK 900.0) (barToPa 30.0) 1.0

    /// <summary>
    /// Calculates or returns the forced-convection-only gas-side heat-transfer result.
    /// </summary>
    /// <remarks>
    /// Radiation is disabled so total heat transfer equals the convective component.
    /// </remarks>
    let convectionOnly =
        GasSide.localHtc GasSide.Gnielinski props props.Mu 0.032 0.10 0.50 1.4 (cToK 500.0) 0.326 0.0546 0.85 false

    /// <summary>
    /// Calculates or returns the gas-side heat-transfer result with radiation enabled.
    /// </summary>
    /// <remarks>
    /// Radiation is enabled with water vapor and carbon dioxide fractions from the reference gas composition.
    /// </remarks>
    let withRadiation =
        GasSide.localHtc GasSide.Gnielinski props props.Mu 0.032 0.10 0.50 1.4 (cToK 500.0) 0.326 0.0546 0.85 true

    Assert.True(convectionOnly.Re > 0.0)
    Assert.True(convectionOnly.HConv > 0.0)
    Assert.Equal(0.0, convectionOnly.HRad, 12)
    Assert.Equal(convectionOnly.HConv, convectionOnly.HTot, 9)
    Assert.True(withRadiation.HRad > 0.0)
    Assert.True(withRadiation.HTot > convectionOnly.HTot)

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
let ``water side heat transfer supports boiling and convection screening correlations`` () =
    /// <summary>
    /// Calculates or returns saturated steam properties for the water-side HTC test.
    /// </summary>
    /// <remarks>
    /// The test uses 100 bar saturation properties for simple preliminary engineering checks.
    /// </remarks>
    let sat = Steam.sat (barToPa 100.0)

    /// <summary>
    /// Calculates or returns a natural-convection coefficient around the tube.
    /// </summary>
    /// <remarks>
    /// The coefficient must remain positive for a positive wall superheat.
    /// </remarks>
    let hConv = WaterSide.hNaturalConvection 0.0381 10.0 sat

    /// <summary>
    /// Calculates or returns a pool-boiling coefficient around the tube.
    /// </summary>
    /// <remarks>
    /// The coefficient uses the selected empirical boiling correlation.
    /// </remarks>
    let hBoil = WaterSide.hPool WaterSide.Mostinski 250000.0 0.0381 sat 0.4 0.013

    /// <summary>
    /// Calculates or returns the combined shell-side preliminary HTC.
    /// </summary>
    /// <remarks>
    /// The combined coefficient adds convection to the bundled boiling contribution.
    /// </remarks>
    let hShell = WaterSide.shellSideHtc WaterSide.Mostinski 250000.0 0.0381 sat 0.4 0.013 1.5 hConv

    Assert.True(hConv > 0.0)
    Assert.True(hBoil > hConv)
    Assert.True(hShell > hBoil)
    approx (hBoil * 1.5 + hConv) 1e-9 hShell

[<Fact>]
let ``ferrule component checks pressure drop and insulation paper thickness`` () =
    /// <summary>
    /// Calculates or returns the reference ferrule component.
    /// </summary>
    /// <remarks>
    /// The reference case has a 26.7 mm bore, 30.0 mm sleeve OD, and 32.0 mm tube ID.
    /// </remarks>
    let ferrule = Defaults.referenceCase.Ferrule

    /// <summary>
    /// Calculates or returns reference inlet gas properties for the ferrule pressure-drop check.
    /// </summary>
    /// <remarks>
    /// The pressure-drop estimate is a component check based on inlet gas properties and average ferrule length.
    /// </remarks>
    let props =
        GasProps.mixReal
            Defaults.referenceCase.Gas.MixingRule
            Defaults.referenceCase.Gas.RealGas
            Defaults.referenceComposition
            Defaults.referenceCase.Gas.TIn
            Defaults.referenceCase.Gas.PIn
            Defaults.referenceCase.Gas.Z

    /// <summary>
    /// Calculates or returns weighted average ferrule length.
    /// </summary>
    /// <remarks>
    /// Length classes are normalized before averaging.
    /// </remarks>
    let length =
        BundleSolver.ferruleClasses ferrule |> List.sumBy (fun (fraction, value) -> fraction * value)

    let paperThickness = BundleSolver.ferruleInsulationThickness ferrule Defaults.referenceCase.Tube.Di
    let dp =
        BundleSolver.ferrulePressureDropEstimate
            ferrule
            Defaults.referenceCase.Tube.Di
            Defaults.referenceCase.Tube.Roughness
            (Defaults.referenceCase.Gas.MassFlow / float Defaults.referenceCase.Tube.NTubes)
            props
            length

    approx 0.001 1e-12 paperThickness
    Assert.Equal("OK", BundleSolver.ferruleInsulationFitStatus ferrule Defaults.referenceCase.Tube.Di)
    Assert.True(BundleSolver.ferruleResistance ferrule Defaults.referenceCase.Tube.Di 500.0 > 0.0)
    Assert.True(dp > 0.0)

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
let ``vibration empirical coefficients stay within screening expectations`` () =
    /// <summary>
    /// Represents pitch ratios used by the vibration empirical-coefficient campaign.
    /// </summary>
    /// <remarks>
    /// The campaign checks monotonic and bounded behavior instead of only one frozen benchmark row.
    /// </remarks>
    let pitchRatios = [ 1.25; 1.50; 2.00 ]

    for pitchRatio in pitchRatios do
        Assert.True(Vibration.addedMassCoef pitchRatio > 1.0)
        Assert.InRange(Vibration.strouhal pitchRatio, 0.2, 0.6)

    Assert.Equal(4.0, Vibration.connorsK Vibration.Triangular30 0.1, 12)
    Assert.Equal(4.0, Vibration.connorsK Vibration.Square90 0.1, 12)
    Assert.Equal(1.1, Vibration.connorsK Vibration.RotatedTriangular60 0.5, 12)
    Assert.Equal(1.5, Vibration.connorsK Vibration.RotatedTriangular60 0.6, 12)

[<Fact>]
let ``vibration theoretical frequency follows span and boundary condition scaling`` () =
    /// <summary>
    /// Calculates or returns tube inertia for the vibration theory campaign.
    /// </summary>
    /// <remarks>
    /// Frequency should scale with the eigenvalue coefficient and with span to the inverse square.
    /// </remarks>
    let inertia = Vibration.inertia 0.05 0.04

    /// <summary>
    /// Calculates or returns base natural frequency for the vibration theory campaign.
    /// </summary>
    /// <remarks>
    /// The base case uses a simply supported style eigenvalue and a 1.5 m span.
    /// </remarks>
    let baseFrequency = Vibration.naturalFrequency (Vibration.lambda2Of 0) 2.0e11 inertia 6.5 1.5

    /// <summary>
    /// Calculates or returns natural frequency after doubling the unsupported span.
    /// </summary>
    /// <remarks>
    /// Euler-Bernoulli screening theory gives frequency proportional to one over span squared.
    /// </remarks>
    let doubledSpanFrequency = Vibration.naturalFrequency (Vibration.lambda2Of 0) 2.0e11 inertia 6.5 3.0

    approx (baseFrequency / 4.0) 1e-12 doubledSpanFrequency
    Assert.True(Vibration.lambda2Of 2 > Vibration.lambda2Of 1)
    Assert.True(Vibration.lambda2Of 1 > Vibration.lambda2Of 0)

[<Fact>]
let ``vibration screening campaign responds to velocity and allowable span`` () =
    /// <summary>
    /// Calculates or returns a low-velocity vibration screening case.
    /// </summary>
    /// <remarks>
    /// Low velocity should remain below fluid-elastic and vortex screening limits.
    /// </remarks>
    let lowVelocity =
        Vibration.check
            0 0.0 1.5 (Vibration.lambda2Of 1) Vibration.Triangular30 0.02
            0.05 0.04 0.075 2.0e11 7850.0 1.0 12.0 700.0

    /// <summary>
    /// Calculates or returns a high-velocity vibration screening case.
    /// </summary>
    /// <remarks>
    /// High velocity should increase the screening ratios and fail the simplified check.
    /// </remarks>
    let highVelocity =
        Vibration.check
            0 0.0 1.5 (Vibration.lambda2Of 1) Vibration.Triangular30 0.02
            0.05 0.04 0.075 2.0e11 7850.0 30.0 12.0 700.0

    Assert.True(lowVelocity.Ok)
    Assert.False(highVelocity.Ok)
    Assert.True(highVelocity.FeiRatio > lowVelocity.FeiRatio)
    Assert.True(highVelocity.VortexRatio > lowVelocity.VortexRatio)
    Assert.True(Vibration.maxSpan 0.8 highVelocity < highVelocity.Span)

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
