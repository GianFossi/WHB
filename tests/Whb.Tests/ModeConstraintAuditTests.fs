module ModeConstraintAuditTests

open Whb.Core
open Whb.Core.Types
open Xunit

let private benchmarkCase (caseIn: DesignCase) : DesignCase =
    { caseIn with
        NZ = 6
        NY = 2 }

let private deterministicSettings =
    { Design.defaultRunSettings with
        Parallelism = 1
        GasPropertyCache = false }

let private weightObjective : Optimize.ObjectiveSet =
    { Terms =
        [ { Key = ConstraintModel.WhbWeightKg
            Name = "Estimated WHB weight"
            Weight = 1.0
            Scale = None
            Sense = Optimize.Minimize } ] }

let private dnbrConstraint limit : ConstraintModel.ConstraintTarget =
    { Key = ConstraintModel.MinDNBR
      Name = "DNBR"
      Domain = ConstraintModel.Thermal
      Unit = "-"
      Limit = ConstraintModel.Min limit
      Required = true
      Weight = 1.0 }

let private weightCap : ConstraintModel.ConstraintTarget =
    { Key = ConstraintModel.WhbWeightKg
      Name = "Estimated WHB weight"
      Domain = ConstraintModel.Weight
      Unit = "kg"
      Limit = ConstraintModel.Max 1.0e9
      Required = true
      Weight = 0.1 }

let private constraintSet dnbrMin : ConstraintModel.ConstraintSet =
    { Targets = [ dnbrConstraint dnbrMin; weightCap ] }

let private bestReading key (assessment: PerformanceAssessment.Assessment) =
    assessment.ConstraintReadings
    |> List.find (fun reading -> reading.Target.Key = key)

let private objectiveFor (caseIn: DesignCase) =
    caseIn
    |> LoadCases.runAll deterministicSettings [ LoadCases.baseCase "base" ]
    |> Optimize.scoreObjective weightObjective

[<Fact>]
let ``optimize mode objective still distinguishes lighter candidates when scale is omitted`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let permissiveConstraints = constraintSet 0.5
    let baseObjective = objectiveFor baseCase
    let input : Optimize.OptimizeInput =
        { BaseCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = permissiveConstraints
          Variables =
            [ { Key = Optimize.TubeCount
                Name = "numero tubi"
                Current = float baseCase.Tube.NTubes
                Lower = float (baseCase.Tube.NTubes - 80)
                Upper = float baseCase.Tube.NTubes
                Step = 40.0
                Unit = "-" } ]
          Objective = weightObjective
          RunSettings = deterministicSettings
          MaxIterations = 5
          Tolerance = 25.0 }

    let result = Optimize.run input
    let dnbr = bestReading ConstraintModel.MinDNBR result.Best.Assessment

    Assert.True(result.Best.Assessment.IsFeasible)
    Assert.Equal(baseCase.Tube.NTubes - 80, result.Best.Case.Tube.NTubes)
    Assert.True(result.Best.ObjectiveValue < baseObjective)
    Assert.True(dnbr.Value >= 0.5)

[<Fact>]
let ``design mode tight required constraints displace the unconstrained lightest candidate`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let space : GreenfieldDesign.DesignSpace =
        { TubeCounts = [ baseCase.Tube.NTubes - 80; baseCase.Tube.NTubes - 40; baseCase.Tube.NTubes ]
          TubeLengthsM = []
          FerruleLengthsMm = []
          ShellInnerDiametersM = []
          TubePitchesM = []
          DrumCenterlineHeightsM = [] }
    let baseInput constraints : GreenfieldDesign.DesignInput =
        { TemplateCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = constraints
          Objective = weightObjective
          Space = space
          RunSettings = deterministicSettings }

    let permissive = GreenfieldDesign.run (baseInput (constraintSet 0.5))
    let tight = GreenfieldDesign.run (baseInput (constraintSet 0.74))
    let permissiveDnbr = bestReading ConstraintModel.MinDNBR permissive.Best.Assessment
    let tightDnbr = bestReading ConstraintModel.MinDNBR tight.Best.Assessment

    Assert.Equal(baseCase.Tube.NTubes - 80, permissive.Best.Case.Tube.NTubes)
    Assert.Equal(baseCase.Tube.NTubes, tight.Best.Case.Tube.NTubes)
    Assert.True(permissive.Best.Assessment.IsFeasible)
    Assert.True(tight.Best.Assessment.IsFeasible)
    Assert.True(permissiveDnbr.Value < 0.74)
    Assert.True(tightDnbr.Value >= 0.74)
    Assert.True(permissive.Best.ObjectiveValue < tight.Best.ObjectiveValue)

[<Fact>]
let ``optimize mode tight required constraints move the solution to the feasible bound`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let buildInput constraints : Optimize.OptimizeInput =
        { BaseCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints = constraints
          Variables =
            [ { Key = Optimize.TubeCount
                Name = "numero tubi"
                Current = float baseCase.Tube.NTubes
                Lower = float (baseCase.Tube.NTubes - 80)
                Upper = float baseCase.Tube.NTubes
                Step = 40.0
                Unit = "-" } ]
          Objective = weightObjective
          RunSettings = deterministicSettings
          MaxIterations = 5
          Tolerance = 25.0 }

    let permissive = Optimize.run (buildInput (constraintSet 0.5))
    let tight = Optimize.run (buildInput (constraintSet 0.74))
    let permissiveDnbr = bestReading ConstraintModel.MinDNBR permissive.Best.Assessment
    let tightDnbr = bestReading ConstraintModel.MinDNBR tight.Best.Assessment

    Assert.Equal(baseCase.Tube.NTubes - 80, permissive.Best.Case.Tube.NTubes)
    Assert.Equal(baseCase.Tube.NTubes, tight.Best.Case.Tube.NTubes)
    Assert.True(permissive.Best.Assessment.IsFeasible)
    Assert.True(tight.Best.Assessment.IsFeasible)
    Assert.True(permissiveDnbr.Value < 0.74)
    Assert.True(tightDnbr.Value >= 0.74)
    Assert.True(permissive.Best.ObjectiveValue < tight.Best.ObjectiveValue)
    Assert.Equal(Optimizer.Optimization.AtSearchBound, permissive.Solver.Kind)
    Assert.Equal(Optimizer.Optimization.AtSearchBound, tight.Solver.Kind)
