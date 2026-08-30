namespace Whb.Core

open System
open Types

/// <summary>
/// Optimization mode for improving one existing geometry within explicit bounds and constraints.
/// </summary>
module Optimize =

    module Opt = Whb.Core.Optimizer.Optimization

    type VariableKey =
        | FerruleLengthMm
        | TubeLengthM
        | TubeCount
        | TubeOuterDiameterM
        | TubePitchM
        | ShellInnerDiameterM
        | DrumCenterlineHeightM

    type DesignVariable =
        { Key: VariableKey
          Name: string
          Current: float
          Lower: float
          Upper: float
          Step: float
          Unit: string }

    type ObjectiveSense =
        | Minimize
        | Maximize

    type ObjectiveTerm =
        { Key: ConstraintModel.ConstraintValueKey
          Name: string
          Weight: float
          Scale: float option
          Sense: ObjectiveSense }

    type ObjectiveSet =
        { Terms: ObjectiveTerm list }

    type OptimizeInput =
        { BaseCase: DesignCase
          LoadCases: LoadCases.LoadCaseSpec list
          Constraints: ConstraintModel.ConstraintSet
          Variables: DesignVariable list
          Objective: ObjectiveSet
          RunSettings: DesignRuntime.RunSettings
          MaxIterations: int
          Tolerance: float }

    type OptimizeCandidate =
        { Case: DesignCase
          LoadCaseResults: LoadCases.LoadCaseResult list
          Assessment: PerformanceAssessment.Assessment
          ObjectiveValue: float }

    type OptimizeResult =
        { Input: OptimizeInput
          Best: OptimizeCandidate
          Solver: Opt.Result }

    type private OptimizationPlan =
        { Input: OptimizeInput
          Problem: Opt.OptimizationProblem }

    type private SolvedOptimization =
        { Plan: OptimizationPlan
          Solver: Opt.Result }

    type private EvaluatedBestCandidate =
        { Solved: SolvedOptimization
          BestCase: DesignCase
          LoadCaseResults: LoadCases.LoadCaseResult list
          Assessment: PerformanceAssessment.Assessment
          ObjectiveValue: float }

    let defaultVariables (caseIn: DesignCase) =
        let ferruleLength = caseIn.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)
        [ { Key = FerruleLengthMm
            Name = "lunghezza ferrula"
            Current = ferruleLength * 1000.0
            Lower = 100.0
            Upper = 800.0
            Step = 50.0
            Unit = "mm" }
          { Key = TubeLengthM
            Name = "lunghezza tubi"
            Current = caseIn.Tube.Length
            Lower = caseIn.Tube.Length * 0.8
            Upper = caseIn.Tube.Length * 1.2
            Step = caseIn.Tube.Length * 0.05
            Unit = "m" } ]

    let defaultObjective =
        { Terms =
            [ { Key = ConstraintModel.WhbWeightKg
                Name = "peso WHB"
                Weight = 1.0
                Scale = None
                Sense = Minimize }
              { Key = ConstraintModel.WhbIdTimesLength
                Name = "ingombro WHB"
                Weight = 0.25
                Scale = None
                Sense = Minimize }
              { Key = ConstraintModel.DrumIdTimesLength
                Name = "ingombro drum"
                Weight = 0.10
                Scale = None
                Sense = Minimize }
              { Key = ConstraintModel.ExternalPipingWeightKg
                Name = "peso piping esterno"
                Weight = 0.20
                Scale = None
                Sense = Minimize } ] }

    let applyVariable (caseIn: DesignCase) (variable: DesignVariable) (value: float) : DesignCase =
        match variable.Key with
        | FerruleLengthMm ->
            let total = caseIn.Ferrule.Lengths |> List.sumBy fst
            let scale = if total > 0.0 then 1.0 / total else 1.0
            { caseIn with
                Ferrule =
                    { caseIn.Ferrule with
                        Lengths = caseIn.Ferrule.Lengths |> List.map (fun (f, _) -> (f * scale, value / 1000.0)) } }
        | TubeLengthM ->
            { caseIn with Tube = { caseIn.Tube with Length = value } }
        | TubeCount ->
            let calibration = BundleGeometry.calibrate caseIn.Tube
            { caseIn with
                Tube =
                    { caseIn.Tube with
                        NTubes = max 1 (int (Math.Round value)) }
                    |> BundleGeometry.realignTubeEnvelopeWith calibration }
        | TubeOuterDiameterM ->
            let tube = caseIn.Tube
            let calibration = BundleGeometry.calibrate tube
            let wall = max 1e-4 (0.5 * (tube.Do - tube.Di))
            let do' = max 1e-3 value
            let di' = max 1e-4 (do' - 2.0 * wall)
            { caseIn with
                Tube =
                    { tube with
                        Do = do'
                        Di = di' }
                    |> BundleGeometry.realignTubeEnvelopeWith calibration }
        | TubePitchM ->
            let calibration = BundleGeometry.calibrate caseIn.Tube
            { caseIn with
                Tube =
                    { caseIn.Tube with Pitch = value }
                    |> BundleGeometry.realignTubeEnvelopeWith calibration }
        | ShellInnerDiameterM ->
            let tube = caseIn.Tube
            let gap = max 0.0 (tube.ShellId - tube.BaffleOd)
            { caseIn with
                Tube =
                    { tube with
                        ShellId = value
                        BaffleOd = max 1e-6 (value - gap) } }
        | DrumCenterlineHeightM ->
            { caseIn with Loop = { caseIn.Loop with DzDrumWhb = value } }

    let private variablePriority key =
        match key with
        | ShellInnerDiameterM -> 2
        | DrumCenterlineHeightM -> 3
        | _ -> 1

    let applyVariables (caseIn: DesignCase) (variables: DesignVariable list) (values: float[]) =
        let ordered =
            variables
            |> List.indexed
            |> List.sortBy (fun (i, variable) -> variablePriority variable.Key, i)
        (caseIn, ordered)
        ||> List.fold (fun acc (i, variable) ->
            let value =
                if i < values.Length then values.[i] else variable.Current
            applyVariable acc variable value)

    let private aggregateObjectiveValue (sense: ObjectiveSense) (values: float list) =
        match sense with
        | Minimize -> values |> List.max
        | Maximize -> values |> List.min

    let private effectiveObjectiveScale (term: ObjectiveTerm) =
        // An omitted scale means "use raw engineering units". Recomputing the scale from the
        // candidate itself would collapse every positive objective term to +/-1 and erase the trade-off.
        term.Scale
        |> Option.defaultValue 1.0
        |> max 1e-9

    let private normalizeObjectiveTerm (term: ObjectiveTerm) (aggregated: float) =
        let scale = effectiveObjectiveScale term
        match term.Sense with
        | Minimize -> aggregated / scale
        | Maximize -> -aggregated / scale

    let scoreObjective (terms: ObjectiveSet) (loadResults: LoadCases.LoadCaseResult list) =
        let valueFor key (r: LoadCases.LoadCaseResult) =
            ConstraintReaders.tryFindValue key r.Verification.Result
            |> Option.map (fun v -> v.Value)
            |> Option.defaultValue nan
        terms.Terms
        |> List.sumBy (fun term ->
            let values = loadResults |> List.map (valueFor term.Key)
            let aggregated = aggregateObjectiveValue term.Sense values
            term.Weight * normalizeObjectiveTerm term aggregated)

    let private buildProblem (input: OptimizeInput) : Opt.OptimizationProblem =
        { Name = sprintf "Optimize - %s" input.BaseCase.Name
          Variables =
            input.Variables
            |> List.map (fun v ->
                ({ Name = v.Name
                   Current = v.Current
                   Lower = v.Lower
                   Upper = v.Upper
                   Step = v.Step
                   Unit = v.Unit } : Opt.Variable))
          Constraints =
            input.Constraints.Targets
            |> List.choose (fun t ->
                if not t.Required then None
                else
                    let minV, maxV =
                        match t.Limit with
                        | ConstraintModel.Min x -> Some x, None
                        | ConstraintModel.Max x -> None, Some x
                        | ConstraintModel.Range(lo, hi) -> Some lo, Some hi
                    Some
                        (({ Name = t.Name
                            Min = minV
                            Max = maxV
                            Unit = t.Unit
                            Weight = t.Weight } : Opt.Constraint)))
          Objective = "minimize configured weighted objective under shared verification constraints"
          MaxIterations = input.MaxIterations
          Tolerance = input.Tolerance }

    let private evaluateCandidate (input: OptimizeInput) (values: float[]) =
        let candidateCase = applyVariables input.BaseCase input.Variables values
        let loadResults = LoadCases.runAll input.RunSettings input.LoadCases candidateCase
        let assessment = PerformanceAssessment.assess input.Constraints loadResults
        let objective = scoreObjective input.Objective loadResults
        (candidateCase, loadResults, assessment, objective)

    let private evaluateCandidateWithLoadRunner (runLoadCases: DesignRuntime.RunSettings -> LoadCases.LoadCaseSpec list -> DesignCase -> LoadCases.LoadCaseResult list)
                                               (input: OptimizeInput) (values: float[]) =
        let candidateCase = applyVariables input.BaseCase input.Variables values
        let loadResults = runLoadCases input.RunSettings input.LoadCases candidateCase
        let assessment = PerformanceAssessment.assess input.Constraints loadResults
        let objective = scoreObjective input.Objective loadResults
        (candidateCase, loadResults, assessment, objective)

    let private planOptimization (input: OptimizeInput) : OptimizationPlan =
        { Input = input
          Problem = buildProblem input }

    let private solveOptimization (plan: OptimizationPlan) : SolvedOptimization =
        let evaluate values =
            let (_, _, assessment, objective) = evaluateCandidate plan.Input values
            let readings =
                assessment.ConstraintReadings
                |> List.filter (fun r -> r.Target.Required)
                |> List.map (fun r -> r.Value)
                |> List.toArray
            (objective, readings)
        { Plan = plan
          Solver = Opt.solve plan.Problem evaluate }

    let private evaluateBestCandidate (solved: SolvedOptimization) : EvaluatedBestCandidate =
        let (caseBest, loadResults, assessment, objective) =
            evaluateCandidate solved.Plan.Input solved.Solver.Best.Values
        { Solved = solved
          BestCase = caseBest
          LoadCaseResults = loadResults
          Assessment = assessment
          ObjectiveValue = objective }

    let private buildResult (evaluated: EvaluatedBestCandidate) : OptimizeResult =
        { Input = evaluated.Solved.Plan.Input
          Best =
            { Case = evaluated.BestCase
              LoadCaseResults = evaluated.LoadCaseResults
              Assessment = evaluated.Assessment
              ObjectiveValue = evaluated.ObjectiveValue }
          Solver = evaluated.Solved.Solver }

    let run (input: OptimizeInput) : OptimizeResult =
        input
        |> planOptimization
        |> solveOptimization
        |> evaluateBestCandidate
        |> buildResult

    let runWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (input: OptimizeInput) : OptimizeResult =
        let maxEvaluations = max 1 input.MaxIterations
        let mutable evaluations = 0
        let evaluate values =
            evaluations <- evaluations + 1
            let startFraction = float (evaluations - 1) / float maxEvaluations
            let endFraction = float evaluations / float maxEvaluations
            let spanReporter = ExecutionProgress.Reporting.scale startFraction endFraction reportProgress
            spanReporter
                (ExecutionProgress.Reporting.step 0.0
                    (sprintf "Optimize evaluation %d/%d" evaluations maxEvaluations))
            let (_, _, assessment, objective) =
                evaluateCandidateWithLoadRunner
                    (LoadCases.runAllWithProgress spanReporter)
                    input
                    values
            let readings =
                assessment.ConstraintReadings
                |> List.filter (fun r -> r.Target.Required)
                |> List.map (fun r -> r.Value)
                |> List.toArray
            (objective, readings)
        let plan = planOptimization input
        let solved =
            { Plan = plan
              Solver = Opt.solve plan.Problem evaluate }
        solved
        |> evaluateBestCandidate
        |> buildResult
