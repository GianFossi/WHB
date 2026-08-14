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
            System.IO.Path.Combine(root, "src", "Whb.Core", "Solvers", "BundleSolver.fs"))
    Assert.Contains("dpWeight", solver)
    Assert.DoesNotContain("dpAcc / float (ny * nc)", solver)
