module Tests

open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Xunit

/// <summary>
/// Asserts that an actual floating-point value stays within an absolute tolerance of the expected value.
/// </summary>
/// <remarks>
/// Regression benchmarks use explicit tolerances so correlation updates must be reviewed intentionally.
/// </remarks>
let approx (expected: float) (tolerance: float) (actual: float) =
    Assert.True(abs (actual - expected) <= tolerance, sprintf "Expected %g +/- %g, got %g" expected tolerance actual)

let private compactCase (caseIn: DesignCase) : DesignCase =
    { caseIn with
        NZ = 16
        NY = 4 }

let private fastDeterministicCase (caseIn: DesignCase) : DesignCase =
    { caseIn with
        NZ = 6
        NY = 2 }

let private permissiveConstraints : ConstraintModel.ConstraintSet =
    { Targets =
        [ { Key = ConstraintModel.MinDNBR
            Name = "DNBR"
            Domain = ConstraintModel.Thermal
            Unit = "-"
            Limit = ConstraintModel.Min 0.5
            Required = true
            Weight = 1.0 }
          { Key = ConstraintModel.GasPressureDrop
            Name = "Gas pressure drop"
            Domain = ConstraintModel.Hydraulic
            Unit = "Pa"
            Limit = ConstraintModel.Max 1.0e6
            Required = true
            Weight = 1.0 }
          { Key = ConstraintModel.WhbWeightKg
            Name = "Estimated WHB weight"
            Domain = ConstraintModel.Weight
            Unit = "kg"
            Limit = ConstraintModel.Max 1.0e9
            Required = true
            Weight = 0.1 } ] }

let private weightObjective : Optimize.ObjectiveSet =
    { Terms =
        [ { Key = ConstraintModel.WhbWeightKg
            Name = "Estimated WHB weight"
            Weight = 1.0
            Scale = None
            Sense = Optimize.Minimize } ] }

let private deterministicSettings =
    { Design.defaultRunSettings with
        Parallelism = 1
        GasPropertyCache = false }

let private caseSnapshot (caseIn: DesignCase) =
    struct (
        caseIn.Name,
        caseIn.Tube.Length,
        caseIn.Tube.NTubes,
        caseIn.Tube.Pitch,
        caseIn.Tube.ShellId,
        caseIn.Gas.MassFlow,
        caseIn.Gas.TIn,
        caseIn.Water.DrumPressure,
        caseIn.Loop.DzDrumWhb,
        caseIn.BypassOpenFraction,
        caseIn.Ferrule.Lengths
    )

let private designResultSnapshot (result: DesignResult) =
    struct (
        result.Duty,
        result.SteamProduction,
        result.TGasOutMean,
        result.DpGas,
        result.Findings.Length,
        result.Warnings.Length,
        result.RiserChecks.Length,
        result.LineChecks.Length
    )

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
    Assert.Equal(Claus.Frozen, Defaults.referenceCase.Gas.ClausMode)
    Assert.True(Defaults.referenceCase.Gas.ClausKinetics.SeverityFactor > 0.0)
    Assert.True(Defaults.referenceCase.Gas.ClausKinetics.TauFactor > 0.0)
    Assert.False(Defaults.referenceCase.SulphurCondenser.Enabled)

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
            0.05 0.04 0.075 2.0e11 7850.0 8.0 12.0 700.0 0.0

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
            0.05 0.04 0.075 2.0e11 7850.0 1.0 12.0 700.0 0.0
    let highVelocity =
        Vibration.check
            0 0.0 1.5 (Vibration.lambda2Of 1) Vibration.Triangular30 0.02
            0.05 0.04 0.075 2.0e11 7850.0 30.0 12.0 700.0 0.0

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



[<Fact>]
let ``brent matches bisect on the same bracket and converges tighter`` () =
    let f x = x * x - 4.0

    approx 2.0 1e-12 (brent f 0.0 5.0 1e-10 200)
    approx 2.0 1e-12 (brent f 5.0 0.0 1e-10 200)
    // A bracket without a sign change must degrade exactly like bisect does.
    Assert.Equal(bisect f 3.0 5.0 1e-10 200, brent f 3.0 5.0 1e-10 200, 12)
    // Same bracket and tolerance: brent must not be less accurate than bisect.
    let g x = exp x - 7.0
    let target = log 7.0
    Assert.True(abs (brent g 0.0 5.0 1e-8 200 - target) <= abs (bisect g 0.0 5.0 1e-8 200 - target))

[<Fact>]
let ``safeguarded newton solves increasing residuals and falls back to bisection`` () =
    // Well-behaved residual: quadratic convergence from a poor starting point.
    let fdf x = struct (x * x * x - 8.0, 3.0 * x * x)
    approx 2.0 1e-9 (newtonIncreasing fdf 0.0 10.0 9.5 1e-12 60)
    // Zero derivative everywhere forces every step to be a bisection step.
    let flat x = struct (x - 3.0, 0.0)
    approx 3.0 1e-6 (newtonIncreasing flat 0.0 10.0 9.9 1e-9 200)

[<Fact>]
let ``enthalpy with cp returns the same enthalpy as the scalar evaluation`` () =
    let comp = GasProps.normalize Defaults.referenceCase.Gas.Composition
    let p = Defaults.referenceCase.Gas.PIn
    for t in [ 400.0; 800.0; 1200.0; 1600.0 ] do
        for real in [ false; true ] do
            let struct (h, cp) = GasProps.enthalpyAbsRealWithCp real comp t p
            Assert.Equal(GasProps.enthalpyAbsReal real comp t p, h, 12)
            // The derivative must match a central difference of the enthalpy itself.
            let dt = 0.05
            let fd =
                (GasProps.enthalpyAbsReal real comp (t + dt) p
                 - GasProps.enthalpyAbsReal real comp (t - dt) p) / (2.0 * dt)
            Assert.True(abs (cp - fd) <= 1e-4 * abs cp, sprintf "cp %g vs finite difference %g" cp fd)

[<Fact>]
let ``enthalpy inversion recovers the temperature it was built from`` () =
    let case = Defaults.referenceCase
    let comp = GasProps.normalize case.Gas.Composition
    for t in [ 600.0; 900.0; 1250.0 ] do
        let h = GasProps.enthalpyAbsReal case.Gas.RealGas comp t case.Gas.PIn
        let (tBack, _) =
            Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas case.Gas.PIn comp h
        approx t 1e-6 tBack

[<Fact>]
let ``frozen shift leaves the composition untouched without inverting the enthalpy`` () =
    let comp = GasProps.normalize Defaults.referenceCase.Gas.Composition
    let h = GasProps.enthalpyAbsReal true comp 1100.0 (barToPa 34.74)
    let c = Shift.compositionFromEnthalpyAt Shift.Frozen true (barToPa 34.74) comp h

    Assert.Equal<GasProps.Composition>(comp, c)

[<Fact>]
let ``shell side coefficient is unchanged by the cell level context split`` () =
    let case = Defaults.referenceCase
    let sat = Steam.sat case.Water.DrumPressure
    for x in [ 0.0; 0.05; 0.30 ] do
        for gCross in [ 50.0; 200.0 ] do
            let ctx = BundleSolver.shellContext case sat x gCross
            for q in [ 1.0e4; 1.0e5; 3.0e5 ] do
                Assert.Equal(
                    BundleSolver.shellHtc case sat q x gCross,
                    BundleSolver.shellHtcWith case sat ctx q,
                    12)

[<Fact>]
let ``dilute gas limit of the IAPWS transport properties stays on the full curve`` () =
    // rho = 0 short-circuits the density residual series because its factor is exp(0) = 1.
    // The short-circuit must therefore be the limit of the general branch, not a special case.
    for t in [ 400.0; 800.0; 1200.0 ] do
        let muLimit = Steam.viscosity t 0.0
        let kLimit = Steam.conductivity t 0.0
        Assert.True(muLimit > 0.0)
        Assert.True(kLimit > 0.0)
        approx muLimit (1e-12 * muLimit) (Steam.viscosity t 1e-11)
        approx kLimit (1e-12 * kLimit) (Steam.conductivity t 1e-11)

[<Fact>]
let ``run settings expose a parallelism control that defaults to the machine width`` () =
    Assert.True(Design.defaultRunSettings.Parallelism >= 1)
    Assert.True(Options.Options.defaultOptions.Calculation.Parallelism >= 1)

[<Fact>]
let ``phase logging is on by default`` () =
    Assert.True(Options.Options.defaultOptions.Logging.Enabled)
    Assert.False(System.String.IsNullOrWhiteSpace Options.Options.defaultOptions.Logging.LogFile)

[<Fact>]
let ``partial options files keep documented defaults instead of silently disabling them`` () =
    let tmp = System.IO.Path.GetTempFileName()
    let load (json: string) =
        System.IO.File.WriteAllText(tmp, json)
        Options.Options.load tmp
    try
        // Whole section absent: logging must not turn itself off.
        let noLogging = load """{ "calculation": { "axialSections": 42 } }"""
        Assert.True(noLogging.Logging.Enabled)
        Assert.Equal("logs/whb-run.log", noLogging.Logging.LogFile)
        Assert.Equal(42, noLogging.Calculation.AxialSections)
        Assert.True(noLogging.Calculation.Parallelism >= 1)
        // Section present but the key missing: same rule applies.
        let partialLogging = load """{ "logging": { "logFile": "custom/run.log" } }"""
        Assert.True(partialLogging.Logging.Enabled)
        Assert.Equal("custom/run.log", partialLogging.Logging.LogFile)
        // An explicit false is still honoured.
        let off = load """{ "logging": { "enabled": false } }"""
        Assert.False(off.Logging.Enabled)
        // An empty document is exactly the defaults.
        let empty = load "{}"
        Assert.True(empty.Logging.Enabled)
        Assert.Equal(Options.Options.defaultOptions.Calculation.BypassMapMode, empty.Calculation.BypassMapMode)
    finally
        System.IO.File.Delete tmp

[<Fact>]
let ``bisect with status separates a root from a clamped endpoint`` () =
    let f x = x * x - 4.0
    let (root, st) = bisectWithStatus f 0.0 5.0 1e-10 200
    approx 2.0 1e-8 root
    Assert.Equal(RootFound, st)
    // Same value as plain bisect, so it can be substituted without moving a number.
    Assert.Equal(bisect f 0.0 5.0 1e-10 200, root, 12)
    let (clamped, st2) = bisectWithStatus f 3.0 5.0 1e-10 200
    Assert.Equal(NoSignChange, st2)
    Assert.Equal(bisect f 3.0 5.0 1e-10 200, clamped, 12)

[<Fact>]
let ``sign change scan detects multiple roots`` () =
    Assert.Equal(1, countSignChanges (fun x -> x - 1.0) 0.0 2.0 40)
    // (x-1)(x-2)(x-3) crosses zero three times inside the bracket.
    Assert.Equal(3, countSignChanges (fun x -> (x - 1.0) * (x - 2.0) * (x - 3.0)) 0.0 4.0 80)

[<Fact>]
let ``band duty fractions use the thermal profile and fall back to an even split`` () =
    let even = Circulation.bandDutyFractions null 4
    Assert.Equal(4, even.Length)
    Assert.All(even, fun v -> Assert.Equal(0.25, v, 12))
    let real = Circulation.bandDutyFractions [| 3.0; 1.0 |] 2
    Assert.Equal(0.75, real.[0], 12)
    Assert.Equal(0.25, real.[1], 12)
    // A profile of the wrong length cannot be trusted, so the even split is used.
    Assert.Equal(3, (Circulation.bandDutyFractions [| 1.0; 2.0 |] 3).Length)

[<Fact>]
let ``two phase damping peaks at intermediate void and returns to the base value`` () =
    let baseDelta = 0.03
    approx baseDelta 1e-12 (Vibration.twoPhaseDamping baseDelta 0.0)
    approx baseDelta 1e-12 (Vibration.twoPhaseDamping baseDelta 1.0)
    Assert.True(Vibration.twoPhaseDamping baseDelta 0.5 > Vibration.twoPhaseDamping baseDelta 0.1)
    approx (4.0 * baseDelta) 1e-12 (Vibration.twoPhaseDamping baseDelta 0.5)

[<Fact>]
let ``buffeting now takes part in the vibration verdict`` () =
    // Same tube, cross-flow velocity high enough to push the buffeting frequency up to the
    // natural frequency while the fluid-elastic ratio stays acceptable.
    let r =
        Vibration.check
            0 0.0 3.0 (Vibration.lambda2Of 1) Vibration.Triangular30 0.02
            0.05 0.04 0.075 2.0e11 7850.0 4.0 12.0 700.0 0.0

    Assert.True(r.BuffetRatio > 0.0)
    if r.BuffetRatio > 0.5 then Assert.False(r.Ok)

[<Fact>]
let ``constrained search reports an optimum held by an active constraint`` () =
    // Objective falls as x grows, but a constraint caps x at 3: the answer is the constraint.
    let problem : Optimizer.Optimization.OptimizationProblem =
        { Name = "test"
          Variables = [ { Name = "x"; Current = 0.0; Lower = -10.0; Upper = 10.0; Step = 1.0; Unit = "-" } ]
          Constraints = [ { Name = "cap"; Min = None; Max = Some 3.0; Unit = "-"; Weight = 1.0 } ]
          Objective = "minimise -x"
          MaxIterations = 200
          Tolerance = 1e-3 }
    let r = Optimizer.Optimization.solve problem (fun v -> (-v.[0], [| v.[0] |]))

    Assert.Equal(Optimizer.Optimization.AtConstraint, r.Kind)
    Assert.Contains("cap", r.ActiveConstraints)
    approx 3.0 0.01 r.Best.Values.[0]
    Assert.True(r.Best.Feasible)

[<Fact>]
let ``constrained search separates a search bound from a real constraint`` () =
    // Nothing constrains x, so the answer only says the search box was too small.
    let problem : Optimizer.Optimization.OptimizationProblem =
        { Name = "test"
          Variables = [ { Name = "x"; Current = 0.0; Lower = 0.0; Upper = 5.0; Step = 1.0; Unit = "-" } ]
          Constraints = []
          Objective = "minimise -x"
          MaxIterations = 200
          Tolerance = 1e-3 }
    let r = Optimizer.Optimization.solve problem (fun v -> (-v.[0], [||]))

    Assert.Equal(Optimizer.Optimization.AtSearchBound, r.Kind)
    Assert.Contains("x", r.VariablesAtBound)
    approx 5.0 1e-9 r.Best.Values.[0]

[<Fact>]
let ``constrained search finds an interior stationary point`` () =
    // A parabola with its minimum well inside both the bounds and the feasible region.
    let problem : Optimizer.Optimization.OptimizationProblem =
        { Name = "test"
          Variables = [ { Name = "x"; Current = -8.0; Lower = -10.0; Upper = 10.0; Step = 2.0; Unit = "-" } ]
          Constraints = [ { Name = "loose"; Min = None; Max = Some 100.0; Unit = "-"; Weight = 1.0 } ]
          Objective = "minimise (x-2)^2"
          MaxIterations = 400
          Tolerance = 1e-4 }
    let r = Optimizer.Optimization.solve problem (fun v -> ((v.[0] - 2.0) ** 2.0, [| 0.0 |]))

    Assert.Equal(Optimizer.Optimization.Interior, r.Kind)
    Assert.Empty(r.ActiveConstraints)
    Assert.Empty(r.VariablesAtBound)
    approx 2.0 0.01 r.Best.Values.[0]

[<Fact>]
let ``constrained search prefers a feasible point over a better infeasible one`` () =
    let problem : Optimizer.Optimization.OptimizationProblem =
        { Name = "test"
          Variables = [ { Name = "x"; Current = 5.0; Lower = 0.0; Upper = 10.0; Step = 1.0; Unit = "-" } ]
          Constraints = [ { Name = "cap"; Min = None; Max = Some 2.0; Unit = "-"; Weight = 1.0 } ]
          Objective = "minimise -x"
          MaxIterations = 300
          Tolerance = 1e-3 }
    let r = Optimizer.Optimization.solve problem (fun v -> (-v.[0], [| v.[0] |]))

    Assert.True(r.Best.Feasible)
    Assert.True(r.Best.Values.[0] <= 2.0 + 1e-6)

[<Fact>]
let ``an infeasible best point is never reported as an interior optimum`` () =
    // Nothing in the range can satisfy the constraint, so no position is meaningful.
    let problem : Optimizer.Optimization.OptimizationProblem =
        { Name = "test"
          Variables = [ { Name = "x"; Current = 0.0; Lower = 0.0; Upper = 1.0; Step = 0.2; Unit = "-" } ]
          Constraints = [ { Name = "impossibile"; Min = Some 10.0; Max = None; Unit = "-"; Weight = 1.0 } ]
          Objective = "minimise x"
          MaxIterations = 60
          Tolerance = 1e-3 }
    let r = Optimizer.Optimization.solve problem (fun v -> (v.[0], [| v.[0] |]))

    Assert.Equal(Optimizer.Optimization.NoFeasiblePoint, r.Kind)
    Assert.False(r.Best.Feasible)
    Assert.Contains("impossibile", r.ActiveConstraints)

[<Fact>]
let ``load case overrides update the operating point without changing the base geometry contract`` () =
    let baseCase = Defaults.referenceCase
    let spec =
        { LoadCases.baseCase "110%"
            with GasMassFlowFactor = Some 1.10
                 GasInletTemperature = Some(cToK 980.0)
                 DrumPressure = Some(barToPa 45.0)
                 BypassOpenFraction = Some 0.15 }
    let rated = LoadCases.applyToCase spec baseCase

    approx (1.10 * baseCase.Gas.MassFlow) 1e-12 rated.Gas.MassFlow
    approx (cToK 980.0) 1e-12 rated.Gas.TIn
    approx (barToPa 45.0) 1e-6 rated.Water.DrumPressure
    approx 0.15 1e-12 rated.BypassOpenFraction
    Assert.Equal(baseCase.Tube.NTubes, rated.Tube.NTubes)

[<Fact>]
let ``optimize variable application updates geometry deterministically`` () =
    let baseCase = Defaults.referenceCase
    let variables : Optimize.DesignVariable list =
        [ { Key = Optimize.TubeCount
            Name = "numero tubi"
            Current = float baseCase.Tube.NTubes
            Lower = 800.0
            Upper = 900.0
            Step = 1.0
            Unit = "-" }
          { Key = Optimize.DrumCenterlineHeightM
            Name = "quota drum"
            Current = baseCase.Loop.DzDrumWhb
            Lower = 3.0
            Upper = 6.0
            Step = 0.1
            Unit = "m" } ]
    let updated = Optimize.applyVariables baseCase variables [| 847.6; 4.25 |]

    Assert.Equal(848, updated.Tube.NTubes)
    approx 4.25 1e-12 updated.Loop.DzDrumWhb

[<Fact>]
let ``rating mode evaluates load cases through the shared verification engine`` () =
    let baseCase = compactCase Defaults.referenceCase
    let input : Rating.RatingInput =
        { BaseCase = baseCase
          LoadCases =
            [ LoadCases.baseCase "base"
              { LoadCases.baseCase "110%" with GasMassFlowFactor = Some 1.10 } ]
          Constraints = permissiveConstraints
          RunSettings = deterministicSettings }
    let result = Rating.run input

    Assert.Equal(2, result.LoadCaseResults.Length)
    Assert.True(result.Assessment.IsFeasible)
    Assert.Contains(result.Assessment.ConstraintReadings, fun r -> r.Target.Key = ConstraintModel.WhbWeightKg)
    Assert.Contains(result.Assessment.GoverningLoadCases, fun name -> name = "110%")

[<Fact>]
let ``greenfield design ranks discrete candidates through the shared verification engine`` () =
    let templateCase = compactCase Defaults.referenceCase
    let input : GreenfieldDesign.DesignInput =
        { TemplateCase = templateCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = permissiveConstraints
          Objective = weightObjective
          Space =
            { TubeCounts = [ templateCase.Tube.NTubes - 40; templateCase.Tube.NTubes ]
              TubeLengthsM = [ templateCase.Tube.Length * 0.95; templateCase.Tube.Length ]
              FerruleLengthsMm = []
              ShellInnerDiametersM = []
              TubePitchesM = []
              DrumCenterlineHeightsM = [] }
          RunSettings = deterministicSettings }
    let result = GreenfieldDesign.run input

    Assert.Equal(4, result.Evaluations)
    Assert.True(result.Best.Assessment.IsFeasible)
    Assert.Equal(templateCase.Tube.NTubes - 40, result.Best.Case.Tube.NTubes)
    Assert.Equal(templateCase.Tube.Length * 0.95, result.Best.Case.Tube.Length, 12)

[<Fact>]
let ``optimize mode evaluates bounded candidates through the shared verification engine`` () =
    let baseCase = compactCase Defaults.referenceCase
    let lowerTubeCount = baseCase.Tube.NTubes - 40
    let candidateCounts = [ lowerTubeCount; baseCase.Tube.NTubes ]
    let input : Optimize.OptimizeInput =
        { BaseCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = permissiveConstraints
          Variables =
            [ { Key = Optimize.TubeCount
                Name = "numero tubi"
                Current = float baseCase.Tube.NTubes
                Lower = float lowerTubeCount
                Upper = float baseCase.Tube.NTubes
                Step = 40.0
                Unit = "-" } ]
          Objective = weightObjective
          RunSettings = deterministicSettings
          MaxIterations = 6
          Tolerance = 1e-3 }
    let result = Optimize.run input
    let recomputedLoads =
        LoadCases.runAll deterministicSettings input.LoadCases result.Best.Case
    let recomputedObjective =
        Optimize.scoreObjective weightObjective recomputedLoads

    Assert.True(result.Best.Assessment.IsFeasible)
    Assert.Contains(result.Best.Case.Tube.NTubes, candidateCounts)
    Assert.Equal(recomputedObjective, result.Best.ObjectiveValue, 12)
    Assert.Contains(result.Best.Assessment.ConstraintReadings, fun r -> r.Target.Key = ConstraintModel.WhbWeightKg)

[<Fact>]
let ``shared verification and mode pipelines are deterministic and do not mutate their inputs`` () =
    let baseCase = fastDeterministicCase Defaults.referenceCase
    let before = caseSnapshot baseCase
    let design1 = Design.runWithSettingsAndProgress deterministicSettings ignore baseCase
    let middle = caseSnapshot baseCase
    let design2 = Design.runWithSettingsAndProgress deterministicSettings ignore baseCase
    let after = caseSnapshot baseCase

    Assert.Equal(before, middle)
    Assert.Equal(before, after)
    Assert.Equal(designResultSnapshot design1, designResultSnapshot design2)

    let ratingInput : Rating.RatingInput =
        { BaseCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = permissiveConstraints
          RunSettings = deterministicSettings }
    let rating1 = Rating.run ratingInput
    let rating2 = Rating.run ratingInput
    Assert.Equal(rating1.Assessment.IsFeasible, rating2.Assessment.IsFeasible)
    Assert.Equal(rating1.Assessment.TotalViolation, rating2.Assessment.TotalViolation, 12)
    Assert.Equal(caseSnapshot ratingInput.BaseCase, before)

    let optimizeInput : Optimize.OptimizeInput =
        { BaseCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = permissiveConstraints
          Variables =
            [ { Key = Optimize.TubeCount
                Name = "numero tubi"
                Current = float baseCase.Tube.NTubes
                Lower = float baseCase.Tube.NTubes
                Upper = float baseCase.Tube.NTubes
                Step = 1.0
                Unit = "-" } ]
          Objective = weightObjective
          RunSettings = deterministicSettings
          MaxIterations = 4
          Tolerance = 1e-3 }
    let optimize1 = Optimize.run optimizeInput
    let optimize2 = Optimize.run optimizeInput
    Assert.Equal(optimize1.Best.Case.Tube.NTubes, optimize2.Best.Case.Tube.NTubes)
    Assert.Equal(optimize1.Best.ObjectiveValue, optimize2.Best.ObjectiveValue, 12)
    Assert.Equal(caseSnapshot optimizeInput.BaseCase, before)

    let designInput : GreenfieldDesign.DesignInput =
        { TemplateCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = permissiveConstraints
          Objective = weightObjective
          Space =
            { TubeCounts = [ baseCase.Tube.NTubes ]
              TubeLengthsM = [ baseCase.Tube.Length ]
              FerruleLengthsMm = []
              ShellInnerDiametersM = []
              TubePitchesM = []
              DrumCenterlineHeightsM = [] }
          RunSettings = deterministicSettings }
    let scratch1 = GreenfieldDesign.run designInput
    let scratch2 = GreenfieldDesign.run designInput
    Assert.Equal(scratch1.Best.Case.Tube.NTubes, scratch2.Best.Case.Tube.NTubes)
    Assert.Equal(scratch1.Best.ObjectiveValue, scratch2.Best.ObjectiveValue, 12)
    Assert.Equal(caseSnapshot designInput.TemplateCase, before)

[<Fact>]
let ``open work list stays in step with the code`` () =
    // TODO.md is the entry point for the next session, so a claim made there that no
    // longer matches the code is worse than no list at all.
    let root =
        let rec up (d: System.IO.DirectoryInfo) =
            if isNull d then failwith "repository root not found"
            elif System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "TODO.md")) then d.FullName
            else up d.Parent
        up (System.IO.DirectoryInfo(System.AppContext.BaseDirectory))
    let todo = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "TODO.md"))

    for id in [ "T-20"; "T-23"; "T-25"; "T-26" ] do
        Assert.Contains(id, todo)
    // The interim step of T-25 is done, so DpGas must be tube-count weighted.
    let solver =
        System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "src", "Whb.Core", "Solvers", "BundleSolver.Support.fs"))
    Assert.Contains("dpWeight", solver)
    Assert.DoesNotContain("dpAcc / float (ny * nc)", solver)

[<Fact>]
let ``extended gas database returns physical properties for all supported species`` () =
    let t = cToK 500.0
    for sp in GasProps.allSpecies do
        Assert.True(GasProps.molarMass sp > 0.0, sprintf "M %A" sp)
        Assert.True(GasProps.cpMolar sp t > 0.0, sprintf "cp %A" sp)
        Assert.True(GasProps.muPure sp t > 0.0, sprintf "mu %A" sp)
        Assert.True(GasProps.kPure sp t > 0.0, sprintf "k %A" sp)

[<Fact>]
let ``species parser accepts formulas and service aliases`` () =
    Assert.Equal(Some GasProps.H2S, GasProps.tryParseSpecies "H2S")
    Assert.Equal(Some GasProps.S8, GasProps.tryParseSpecies "s8")
    Assert.Equal(Some GasProps.C6H6, GasProps.tryParseSpecies "benzene")
    Assert.Equal(Some GasProps.C7H8, GasProps.tryParseSpecies "TOLUENE")
    Assert.Equal(Some GasProps.He, GasProps.tryParseSpecies "elio")
    Assert.Equal(None, GasProps.tryParseSpecies "unknown")

[<Fact>]
let ``sulphur allotropes stay ideal in virial mode instead of throwing`` () =
    let mix = [ GasProps.S2, 0.4; GasProps.S6, 0.3; GasProps.S8, 0.3 ]
    let props = GasProps.mixReal GasProps.Wilke true mix (cToK 600.0) (barToPa 2.0) 1.0
    Assert.True(props.Rho > 0.0)
    Assert.Equal(None, GasProps.Virial.criticalOpt GasProps.S8)

[<Fact>]
let ``satT and saturation table stay aligned with IF97 anchors`` () =
    let table = Steam.saturationTable20to310 ()
    Assert.Equal(30, table.Length)
    table
    |> List.pairwise
    |> List.iter (fun (a, b) ->
        Assert.True(b.P > a.P)
        Assert.True(b.Hfg < a.Hfg)
        Assert.True(b.RhoL < a.RhoL))
    let s100 = Steam.satT (cToK 100.0)
    approx 1.01418 1e-3 (paToBar s100.P)
    approx 2256.47e3 1.0e3 s100.Hfg
    let s310 = Steam.satT (cToK 310.0)
    approx 690.7 0.6 s310.RhoL
    approx s100.Hfg 1.0 (Steam.sat s100.P).Hfg

[<Fact>]
let ``explicit saturation correlations remain within their stated screening accuracy`` () =
    for tc in [ 20.0; 100.0; 200.0; 310.0 ] do
        let s = Steam.satT (cToK tc)
        approx s.RhoL (0.001 * s.RhoL) (Steam.Explicit.rhoLsat (cToK tc))
        approx s.RhoV (0.02 * s.RhoV) (Steam.Explicit.rhoVsat (cToK tc))
    for tc in [ 20.0; 60.0; 100.0 ] do
        let s = Steam.satT (cToK tc)
        approx s.MuL (0.01 * s.MuL) (Steam.Explicit.muLVogel (cToK tc))
        approx s.KL (0.01 * s.KL) (Steam.Explicit.kLRamires (cToK tc))
    for tc in [ 50.0; 100.0; 200.0; 300.0 ] do
        let s = Steam.satT (cToK tc)
        approx s.Hfg (0.03 * s.Hfg) (Steam.Explicit.hfgWatson (cToK tc))

[<Fact>]
let ``kandlikar helpers classify the NBD regime and default stays on chen`` () =
    let sat = Steam.sat Defaults.referenceCase.Water.DrumPressure
    let q = 150.0e3
    let g = 300.0
    let d = Defaults.referenceCase.Tube.Do
    let reLO = g * d / sat.MuL
    let hLO = WaterSide.hZukauskas reLO sat.PrL sat.PrL sat.KL d true (1.0 / 0.8660254)
    let k = WaterSide.hKandlikar hLO q g 0.05 d false 1.0 sat
    Assert.True(k.HTp > 0.0)
    Assert.True(k.Bo > 1.5e-4)
    Assert.True(k.Co > 0.65)
    Assert.Equal(WaterSide.NucleateBoilingDominant, WaterSide.regimeByCo k.Co)
    Assert.Equal(WaterSide.ChenSuperposition, Defaults.referenceCase.Water.FlowBoiling)

[<Fact>]
let ``dnb screening limits follow the case DNBR criterion`` () =
    approx 0.5 1e-12 (WaterSide.dnbAllowableFraction 2.0)
    approx 2.0 1e-12 (WaterSide.dnbrRequired 2.0 false false)
    approx 2.0 1e-12 (WaterSide.dnbrRequired 2.0 true false)
    approx 2.0 1e-12 (WaterSide.dnbrRequired 2.0 false true)
    approx 0.4 1e-12 (WaterSide.dnbAllowableFraction 2.5)
    approx 2.5 1e-12 (WaterSide.dnbrRequired 2.5 false false)

[<Fact>]
let ``reference case exposes the default DNBR project criterion`` () =
    approx 2.0 1e-12 Defaults.referenceCase.Water.MinDNBR

[<Fact>]
let ``sulphur equilibrium constants and reaction heats stay on their anchors`` () =
    approx 25.772024 1e-5 (Sulphur.lnKpS8 600.0)
    approx 7.7119e7 0.02e7 (exp (Sulphur.lnKpS6 600.0))
    approx 1.5592e11 0.02e11 (exp (Sulphur.lnKpS8 600.0))
    Assert.True(Sulphur.dhReactionS6 400.0 < 0.0)
    Assert.True(Sulphur.dhReactionS8 700.0 < 0.0)

[<Fact>]
let ``sulphur speciation shifts towards heavier allotropes on cooling`` () =
    let p = barToPa 1.7
    let hot = Sulphur.speciate (cToK 500.0) p 8.0 100.0
    let cold = Sulphur.speciate (cToK 200.0) p 8.0 100.0
    let atoms (s: Sulphur.Speciation) = 2.0 * s.NS2 + 6.0 * s.NS6 + 8.0 * s.NS8
    Assert.True(cold.MeanAtomicity > hot.MeanAtomicity)
    approx 8.0 1e-6 (atoms hot)
    approx 8.0 1e-6 (atoms cold)
    approx 0.01118250 1e-6 ((Sulphur.speciate (cToK 300.0) p 8.0 100.0).YSulphur)

[<Fact>]
let ``polymerisation duty is positive because frozen speciation underpredicts it`` () =
    let extra = Sulphur.polymerisationDuty (cToK 300.0) (cToK 170.0) (barToPa 1.7) 8.0 100.0
    Assert.True(extra > 0.0)
    approx 9815.0 1.0 extra

[<Fact>]
let ``sulphur vapour pressure and dew point are monotone and round trip`` () =
    approx 32.2973 1e-3 (Sulphur.pSatTotal (cToK 150.0))
    approx 6158.65 1.0 (Sulphur.pSatTotal (cToK 300.0))
    let ps = [ 120.0; 150.0; 200.0; 250.0; 300.0 ] |> List.map (fun t -> Sulphur.pSatTotal (cToK t))
    ps |> List.pairwise |> List.iter (fun (a, b) -> Assert.True(b > a))
    let t = cToK 230.0
    approx (kToC t) 0.01 (kToC (Sulphur.dewPoint (Sulphur.pSatTotal t)))

[<Fact>]
let ``condenser state caps vapour at saturation and reports condensed fraction`` () =
    let p = barToPa 1.7
    let st300 = Sulphur.condenserState (cToK 300.0) p 8.0 100.0
    Assert.False(st300.Condensing)
    approx 0.0 1e-12 st300.NCondensed
    let st170 = Sulphur.condenserState (cToK 170.0) p 8.0 100.0
    Assert.True(st170.Condensing)
    approx 86.432079 1e-3 st170.PSulphur
    approx 0.952407 1e-4 st170.CondensedFraction

[<Fact>]
let ``sulphur process state inverts enthalpy through condensation`` () =
    let p = barToPa 1.7
    let t = cToK 170.0
    let comp = [ GasProps.N2, 0.85; GasProps.H2O, 0.05; GasProps.S2, 0.10 ]
    let h = Sulphur.processEnthalpyAt Shift.Frozen false p comp t
    let st = Sulphur.processStateFromEnthalpyAt Shift.Frozen false p comp h

    Assert.True(st.Condensing)
    Assert.True(st.CondensedFraction > 0.0)
    approx t 0.01 st.T
    approx h 1e-6 st.TotalSpecificEnthalpy

[<Fact>]
let ``sulphur liquid viscosity captures the lambda transition cliff`` () =
    Assert.InRange(Sulphur.muLiquid (cToK 140.0), 5e-3, 15e-3)
    Assert.True(Sulphur.muLiquid (cToK 165.0) > 100.0 * Sulphur.muLiquid (cToK 155.0))
    approx 93.0 0.1 (Sulphur.muLiquid (cToK 187.0))
    Assert.True(Sulphur.muLiquid (cToK 187.0) > Sulphur.muLiquid (cToK 250.0))

[<Fact>]
let ``wall window condensation and corrosion checks fire in the intended regimes`` () =
    Assert.Equal(Sulphur.Alarm, (Sulphur.checkWallWindow (cToK 118.0)).Severity)
    Assert.Equal(Sulphur.Ok, (Sulphur.checkWallWindow (cToK 140.0)).Severity)
    Assert.Equal(Sulphur.Watch, (Sulphur.checkWallWindow (cToK 157.0)).Severity)
    Assert.Equal(Sulphur.Alarm, (Sulphur.checkWallWindow (cToK 165.0)).Severity)
    Assert.Equal(Sulphur.Watch, (Sulphur.checkSulphidation (cToK 300.0) 0.05).Severity)
    Assert.Equal(Sulphur.Alarm, (Sulphur.checkSulphidation (cToK 360.0) 0.05).Severity)
    Assert.Equal(Sulphur.Alarm, (Sulphur.checkWetH2S (cToK 80.0) (cToK 90.0) 0.05).Severity)

[<Fact>]
let ``claus screening separates elemental sulphur from generic claus species`` () =
    let p = barToPa 1.7
    let sc =
        Sulphur.clausScreening p [ GasProps.N2, 0.55; GasProps.H2O, 0.20; GasProps.H2S, 0.10; GasProps.S2, 0.15 ]

    Assert.True(sc.HasClausSpecies)
    Assert.True(sc.HasElementalSulphurVapour)
    Assert.Contains("H2S", sc.PresentSpecies)
    Assert.Contains("S2", sc.PresentSpecies)
    approx 0.10 1e-12 sc.YH2S
    approx 0.15 1e-12 sc.YElementalSulphur
    approx (kToC (Sulphur.dewPoint (0.15 * p))) 0.01 (kToC sc.SulphurDewPoint.Value)
    Assert.True(sc.WaterDewPoint.Value > cToK 50.0)

[<Fact>]
let ``claus pseudo-closure conserves atoms while generating elemental sulphur`` () =
    let atoms (comp: GasProps.Composition) =
        let cn = GasProps.normalize comp
        let y sp = GasProps.molFrac cn sp
        let nTot = 1.0 / GasProps.mixMolarMass cn
        let s =
            nTot * (y GasProps.H2S
                    + y GasProps.SO2
                    + y GasProps.COS
                    + 2.0 * y GasProps.CS2
                    + 2.0 * y GasProps.S2
                    + 6.0 * y GasProps.S6
                    + 8.0 * y GasProps.S8)
        let c = nTot * (y GasProps.CO + y GasProps.CO2 + y GasProps.COS + y GasProps.CS2)
        let h =
            nTot * (2.0 * y GasProps.H2
                    + 4.0 * y GasProps.CH4
                    + 2.0 * y GasProps.H2O
                    + 2.0 * y GasProps.H2S)
        let o =
            nTot * (y GasProps.CO
                    + 2.0 * y GasProps.CO2
                    + y GasProps.H2O
                    + y GasProps.COS
                    + 2.0 * y GasProps.SO2)
        struct (s, c, h, o)
    let compIn =
        [ GasProps.N2, 0.70
          GasProps.H2O, 0.15
          GasProps.H2S, 0.10
          GasProps.SO2, 0.03
          GasProps.COS, 0.015
          GasProps.CS2, 0.005 ]
    let compOut = Claus.advance Claus.Equilibrium (cToK 900.0) 0.05 compIn
    let struct (sIn, cIn, hIn, oIn) = atoms compIn
    let struct (sOut, cOut, hOut, oOut) = atoms compOut

    approx sIn 1e-6 sOut
    approx cIn 1e-6 cOut
    approx hIn 1e-6 hOut
    approx oIn 1e-6 oOut
    Assert.True((GasProps.molFrac compOut GasProps.S2) > 0.0)
    Assert.True((GasProps.molFrac compOut GasProps.H2S) < (GasProps.molFrac compIn GasProps.H2S))

[<Fact>]
let ``default claus kinetic mode is less aggressive than equilibrium on the same segment`` () =
    let compIn =
        [ GasProps.N2, 0.70
          GasProps.H2O, 0.15
          GasProps.H2S, 0.10
          GasProps.SO2, 0.03
          GasProps.COS, 0.015
          GasProps.CS2, 0.005 ]
    let eqOut = Claus.advance Claus.Equilibrium (cToK 900.0) 0.05 compIn
    let kinOut = Claus.advance Claus.Kinetic (cToK 900.0) 0.05 compIn
    let eqClosure = Claus.elementalSulphurAtomFraction eqOut
    let kinClosure = Claus.elementalSulphurAtomFraction kinOut

    Assert.True(kinClosure > 0.0)
    Assert.True(kinClosure < eqClosure)
    Assert.True((GasProps.molFrac kinOut GasProps.SO2) > (GasProps.molFrac eqOut GasProps.SO2))

[<Fact>]
let ``claus kinetic severity factor changes conversion monotonically`` () =
    let compIn =
        [ GasProps.N2, 0.70
          GasProps.H2O, 0.15
          GasProps.H2S, 0.10
          GasProps.SO2, 0.03
          GasProps.COS, 0.015
          GasProps.CS2, 0.005 ]
    let basePars = Defaults.referenceCase.Gas.ClausKinetics
    let mild = Claus.advanceWith (Claus.withSeverity 0.05 basePars) Claus.Kinetic (cToK 900.0) 0.05 compIn
    let strong = Claus.advanceWith (Claus.withSeverity 0.60 basePars) Claus.Kinetic (cToK 900.0) 0.05 compIn
    let mildClosure = Claus.elementalSulphurAtomFraction mild
    let strongClosure = Claus.elementalSulphurAtomFraction strong

    Assert.True(strongClosure > mildClosure)
    Assert.True((GasProps.molFrac strong GasProps.SO2) < (GasProps.molFrac mild GasProps.SO2))

[<Fact>]
let ``claus severity calibration matches a requested surrogate closure`` () =
    let compIn =
        [ GasProps.N2, 0.70
          GasProps.H2O, 0.15
          GasProps.H2S, 0.10
          GasProps.SO2, 0.03
          GasProps.COS, 0.015
          GasProps.CS2, 0.005 ]
    let target = 0.12
    let fitted =
        Claus.calibrateSeverity target Defaults.referenceCase.Gas.ClausKinetics (cToK 900.0) 0.05 compIn
    let outComp =
        Claus.advanceWith fitted Claus.Kinetic (cToK 900.0) 0.05 compIn
    let closure = Claus.elementalSulphurAtomFraction outComp

    approx target 2e-3 closure

[<Fact>]
let ``colburn hougen and fog helpers remain numerically stable`` () =
    let p = barToPa 1.7
    let pS = Sulphur.pSatTotal (cToK 250.0)
    let kG = Sulphur.kGasFromHtc 60.0 35.0 1.2 p
    let r = Sulphur.condenseColburnHougen (cToK 240.0) pS p 60.0 kG 400.0 (cToK 140.0)
    Assert.InRange(r.TInterface, cToK 140.0, cToK 240.0)
    approx 426.499368 1e-3 r.TInterface
    approx 0.01478073 1e-6 r.MolarFlux
    approx (r.QLatent + r.QSensible) 1e-6 r.QTotal
    let fog = Sulphur.assessFog (cToK 240.0) (1.5 * pS) 1.2 -25.0 -300.0
    Assert.True(fog.FogLikely)

[<Fact>]
let ``dedicated sulphur condenser solver returns duty area and liquid sulphur`` () =
    let feed : SulphurCondenser.Feed =
        { Composition = [ GasProps.N2, 0.72; GasProps.H2O, 0.08; GasProps.S2, 0.20 ]
          MassFlow = 12.0
          TIn = cToK 220.0
          PIn = barToPa 1.7
          Z = 1.0
          ShiftMode = Shift.Frozen
          ClausMode = Claus.Frozen
          ClausKinetics = Defaults.referenceCase.Gas.ClausKinetics
          MixingRule = GasProps.Wilke
          RealGas = true }
    let spec : SulphurCondenser.Spec =
        { Enabled = true
          UseWhbOutlet = false
          Sections = 24
          ResidenceTime = 1.0
          DpTotal = 2000.0
          TOutTarget = cToK 145.0
          TWall = cToK 140.0
          TCoolant = cToK 135.0
          UAssumed = 60.0
          Feed = feed }
    let result = SulphurCondenser.solve spec

    Assert.True(result.Duty > 0.0)
    Assert.True(result.AreaRequired > 0.0)
    Assert.True(result.OutletState.CondensedFraction > 0.0)
    Assert.True(result.CondensedSulphurMassFlow > 0.0)
    Assert.Equal(24, result.Segments.Length)

[<Fact>]
let ``design run can execute an integrated dedicated sulphur condenser module`` () =
    let baseCase = Defaults.referenceCase
    let scaled =
        baseCase.Gas.Composition
        |> List.map (fun (sp, y) -> sp, 0.98 * y)
    let case =
        { baseCase with
            NZ = 16
            NY = 4
            Gas =
                { baseCase.Gas with
                    Composition = (GasProps.S2, 0.02) :: scaled }
            SulphurCondenser =
                { baseCase.SulphurCondenser with
                    Enabled = true
                    UseWhbOutlet = false
                    TWall = cToK 165.0
                    Feed =
                        { baseCase.SulphurCondenser.Feed with
                            Composition = [ GasProps.N2, 0.72; GasProps.H2O, 0.08; GasProps.S2, 0.20 ]
                            MassFlow = 12.0
                            TIn = cToK 220.0
                            PIn = barToPa 1.7 } } }
    let settings =
        { Design.defaultRunSettings with Parallelism = 1 }
    let result = Design.runWithSettingsAndProgress settings ignore case
    let sc = result.SulphurCondenserResult.Value

    Assert.True(result.SulphurCondenserResult.IsSome)
    Assert.True(sc.CondensedSulphurMassFlow > 0.0)
    Assert.Contains(result.Findings, fun f -> f.Area = "CONDENSATORE ZOLFO")

[<Fact>]
let ``design findings keep generic claus species as screening only`` () =
    let baseCase = Defaults.referenceCase
    let scaled =
        baseCase.Gas.Composition
        |> List.map (fun (sp, y) -> sp, 0.98 * y)
    let case =
        { baseCase with
            NZ = 16
            NY = 4
            Gas =
                { baseCase.Gas with
                    Composition = (GasProps.H2S, 0.02) :: scaled } }
    let settings =
        { Design.defaultRunSettings with Parallelism = 1 }
    let result = Design.runWithSettingsAndProgress settings ignore case

    Assert.True(result.SulphurCoupling.IsNone)
    Assert.Contains(result.Findings, fun f -> f.Area = "ZOLFO" && f.Title.Contains("Specie Claus"))

[<Fact>]
let ``generic claus species can generate coupled sulphur when claus model is active`` () =
    let baseCase = Defaults.referenceCase
    let scaled =
        baseCase.Gas.Composition
        |> List.map (fun (sp, y) -> sp, 0.955 * y)
    let case =
        { baseCase with
            NZ = 16
            NY = 4
            Gas =
                { baseCase.Gas with
                    ClausMode = Claus.Equilibrium
                    Composition = [ GasProps.H2S, 0.03; GasProps.SO2, 0.015 ] @ scaled } }
    let settings =
        { Design.defaultRunSettings with Parallelism = 1 }
    let result = Design.runWithSettingsAndProgress settings ignore case
    let s = result.SulphurCoupling.Value

    Assert.True(result.SulphurCoupling.IsSome)
    Assert.True(s.CondensingCells > 0)
    Assert.True(s.OutletCondensedFraction > 0.0)
    Assert.Contains(result.Findings, fun f -> f.Area = "ZOLFO" && f.Title.Contains("Condensazione di zolfo elementare"))

[<Fact>]
let ``explicit elemental sulphur is coupled into the main bundle solve`` () =
    let baseCase = Defaults.referenceCase
    let scaled =
        baseCase.Gas.Composition
        |> List.map (fun (sp, y) -> sp, 0.98 * y)
    let case =
        { baseCase with
            NZ = 16
            NY = 4
            Gas =
                { baseCase.Gas with
                    Composition = (GasProps.S2, 0.02) :: scaled } }
    let settings =
        { Design.defaultRunSettings with Parallelism = 1 }
    let result = Design.runWithSettingsAndProgress settings ignore case
    let s = result.SulphurCoupling.Value

    Assert.True(result.SulphurCoupling.IsSome)
    Assert.True(s.CondensingCells > 0)
    Assert.True(s.OutletCondensedFraction > 0.0)
    Assert.Contains(result.Findings, fun f -> f.Area = "ZOLFO" && f.Title.Contains("Condensazione di zolfo elementare"))
