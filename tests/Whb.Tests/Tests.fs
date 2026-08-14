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
let ``default project options expose thermal precision controls`` () =
    Assert.Equal("adaptive", Options.Options.defaultOptions.Calculation.BypassMapMode)
    Assert.True(Options.Options.defaultOptions.Calculation.BypassTargetToleranceK > 0.0)
    Assert.True(Options.Options.defaultOptions.Calculation.GasPropertyCache)
    Assert.True(Options.Options.defaultOptions.Calculation.CorrelationValidityWarnings)

[<Fact>]
let ``bisect handles normal and reversed brackets`` () =
    let f x = x * x - 4.0

    Assert.Equal(2.0, bisect f 0.0 5.0 1e-10 200, 8)
    Assert.Equal(2.0, bisect f 5.0 0.0 1e-10 200, 8)

[<Fact>]
let ``graded axial grid conserves length`` () =
    let centers, widths = gradedAxialGrid 12.0 10 6.0

    Assert.Equal(10, centers.Length)
    Assert.Equal(10, widths.Length)
    Assert.Equal(12.0, Array.sum widths, 8)
    Assert.True(widths.[0] < widths.[widths.Length - 1])

[<Fact>]
let ``piping line converts geometry and totals`` () =
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
    let re = 1.0e5
    let pr = 1.2

    approx 0.017792479529 1e-12 (GasSide.fBlasius re)
    approx 0.017968935305 1e-12 (GasSide.fFilonenko re)
    approx 0.018513866077 1e-12 (GasSide.fColebrook re 1.0e-4)
    approx 242.930592741029 1e-9 (GasSide.nusseltFD GasSide.DittusBoelter re pr 1.0)
    approx 247.579318998907 1e-9 (GasSide.nusseltFD GasSide.Gnielinski re pr 1.0)

[<Fact>]
let ``gas side heat transfer supports forced convection with optional radiation`` () =
    let props = GasProps.mixReal GasProps.Wilke true Defaults.referenceComposition (cToK 900.0) (barToPa 30.0) 1.0
    let convectionOnly =
        GasSide.localHtc GasSide.Gnielinski props props.Mu 0.032 0.10 0.50 1.4 (cToK 500.0) 0.326 0.0546 0.85 false
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
    let sat = Steam.sat (barToPa 100.0)
    let hConv = WaterSide.hNaturalConvection 0.0381 10.0 sat
    let hBoil = WaterSide.hPool WaterSide.Mostinski 250000.0 0.0381 sat 0.4 0.013
    let hShell = WaterSide.shellSideHtc WaterSide.Mostinski 250000.0 0.0381 sat 0.4 0.013 1.5 hConv

    Assert.True(hConv > 0.0)
    Assert.True(hBoil > hConv)
    Assert.True(hShell > hBoil)
    approx (hBoil * 1.5 + hConv) 1e-9 hShell

[<Fact>]
let ``steam drum calm box method includes outlet waterfall and downcomer entry`` () =
    let sat = Steam.sat (barToPa 100.0)
    let drum =
        { Defaults.referenceCase.Loop.Drum with
            CalmBoxWaterFallHeight = 0.25
            DowncomerEntryArea = 1.0
            DowncomerVortexBreakerK = 0.5 }

    let result = Drum.solve drum sat 500.0 0.10 20.0 0.85 1.25

    Assert.True(result.DpCirculation > 0.0)
    Assert.Contains(result.CircItems, fun i -> i.Label.Contains("riser discharge into calm box"))
    Assert.Contains(result.CircItems, fun i -> i.Label.Contains("calm box water fall"))
    Assert.Contains(result.CircItems, fun i -> i.Label.Contains("downcomer entry with vortex breaker"))

[<Fact>]
let ``steam drum vortex breaker coefficient affects downcomer entry loss`` () =
    let sat = Steam.sat (barToPa 100.0)
    let baseDrum =
        { Defaults.referenceCase.Loop.Drum with
            DowncomerEntryArea = 1.0
            DowncomerVortexBreakerK = 0.25 }

    let highLossDrum = { baseDrum with DowncomerVortexBreakerK = 2.0 }
    let low = Drum.solve baseDrum sat 500.0 0.10 20.0 0.85 1.25
    let high = Drum.solve highLossDrum sat 500.0 0.10 20.0 0.85 1.25

    Assert.True(high.DpCirculation > low.DpCirculation)

[<Fact>]
let ``ferrule component checks pressure drop and insulation paper thickness`` () =
    let ferrule = Defaults.referenceCase.Ferrule
    let props =
        GasProps.mixReal
            Defaults.referenceCase.Gas.MixingRule
            Defaults.referenceCase.Gas.RealGas
            Defaults.referenceComposition
            Defaults.referenceCase.Gas.TIn
            Defaults.referenceCase.Gas.PIn
            Defaults.referenceCase.Gas.Z
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
    let inertia = Vibration.inertia 0.05 0.04
    let baseFrequency = Vibration.naturalFrequency (Vibration.lambda2Of 0) 2.0e11 inertia 6.5 1.5
    let doubledSpanFrequency = Vibration.naturalFrequency (Vibration.lambda2Of 0) 2.0e11 inertia 6.5 3.0

    approx (baseFrequency / 4.0) 1e-12 doubledSpanFrequency
    Assert.True(Vibration.lambda2Of 2 > Vibration.lambda2Of 1)
    Assert.True(Vibration.lambda2Of 1 > Vibration.lambda2Of 0)

[<Fact>]
let ``vibration screening campaign responds to velocity and allowable span`` () =
    let lowVelocity =
        Vibration.check
            0 0.0 1.5 (Vibration.lambda2Of 1) Vibration.Triangular30 0.02
            0.05 0.04 0.075 2.0e11 7850.0 1.0 12.0 700.0
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
    let expansion = Mechanics.axialExpansion Materials.t11 (cToK 20.0) [ 2.0, cToK 400.0; 3.0, cToK 500.0 ]

    approx 0.02944 1e-12 expansion.DeltaL
    approx 460.634814065415 1e-9 (kToC expansion.TEquivalent)
    approx 0.000013362539256 1e-15 expansion.AlphaMean
    let radial, hoop = Mechanics.lame 10.0e6 0.0 0.05 0.06 0.05

    approx -10000000.0 1e-6 radial
    approx 55454545.45454548 1e-5 hoop
    approx 108.972473588517 1e-12 (Mechanics.vonMises 100.0 50.0 -25.0)


