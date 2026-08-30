namespace Whb.Core

open System
open Types

/// <summary>
/// Greenfield design mode over a discrete candidate space, evaluated by the shared verification engine.
/// </summary>
/// <remarks>
/// This first step keeps the candidate generator explicit and deterministic: the template case
/// carries the non-varied details, while the discrete space defines the geometry choices that may
/// change. The selected candidate is still verified by the same engine used for rating.
/// </remarks>
module GreenfieldDesign =

    type TubeSizeOption =
        { OuterDiameterM: float
          PitchM: float }

    type DesignSpace =
        { TubeCounts: int list
          TubeLengthsM: float list
          FerruleLengthsMm: float list
          ShellInnerDiametersM: float list
          TubeSizeOptions: TubeSizeOption list
          TubePitchesM: float list
          DrumCenterlineHeightsM: float list }

    type DesignInput =
        { TemplateCase: DesignCase
          LoadCases: LoadCases.LoadCaseSpec list
          Constraints: ConstraintModel.ConstraintSet
          Objective: Optimize.ObjectiveSet
          Space: DesignSpace
          RunSettings: DesignRuntime.RunSettings }

    type DesignedCandidate =
        { Case: DesignCase
          LoadCaseResults: LoadCases.LoadCaseResult list
          Assessment: PerformanceAssessment.Assessment
          ObjectiveValue: float }

    type DesignResultFromScratch =
        { Input: DesignInput
          Best: DesignedCandidate
          Shortlist: DesignedCandidate list
          Evaluations: int }

    type private SeededCandidates =
        { Input: DesignInput
          Candidates: DesignCase list }

    type private EvaluatedCandidates =
        { Input: DesignInput
          Candidates: DesignedCandidate list }

    let private orCurrent current xs =
        if List.isEmpty xs then [ current ] else xs

    let private tubeSizes (tube: TubeGeometry) (space: DesignSpace) =
        if List.isEmpty space.TubeSizeOptions then
            orCurrent tube.Pitch space.TubePitchesM
            |> List.map (fun pitch ->
                { OuterDiameterM = tube.Do
                  PitchM = pitch })
        else
            space.TubeSizeOptions

    let private seedCases (input: DesignInput) =
        let c = input.TemplateCase
        let ferruleCurrent = (c.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)) * 1000.0
        let explicitShellIds = input.Space.ShellInnerDiametersM
        [ for nT in orCurrent c.Tube.NTubes input.Space.TubeCounts do
            for len in orCurrent c.Tube.Length input.Space.TubeLengthsM do
                for ferrule in orCurrent ferruleCurrent input.Space.FerruleLengthsMm do
                    for shellId in orCurrent c.Tube.ShellId explicitShellIds do
                        for tubeSize in tubeSizes c.Tube input.Space do
                            for dz in orCurrent c.Loop.DzDrumWhb input.Space.DrumCenterlineHeightsM do
                                let baseVars : Optimize.DesignVariable list =
                                    [ { Optimize.Key = Optimize.TubeCount
                                        Name = "numero tubi"
                                        Current = float nT
                                        Lower = float nT
                                        Upper = float nT
                                        Step = 1.0
                                        Unit = "-" }
                                      { Key = Optimize.TubeLengthM
                                        Name = "lunghezza tubi"
                                        Current = len
                                        Lower = len
                                        Upper = len
                                        Step = 1.0
                                        Unit = "m" }
                                      { Key = Optimize.FerruleLengthMm
                                        Name = "lunghezza ferrula"
                                        Current = ferrule
                                        Lower = ferrule
                                        Upper = ferrule
                                        Step = 1.0
                                        Unit = "mm" }
                                      { Key = Optimize.TubeOuterDiameterM
                                        Name = "diametro esterno tubi"
                                        Current = tubeSize.OuterDiameterM
                                        Lower = tubeSize.OuterDiameterM
                                        Upper = tubeSize.OuterDiameterM
                                        Step = 1.0
                                        Unit = "m" }
                                      { Key = Optimize.TubePitchM
                                        Name = "passo tubi"
                                        Current = tubeSize.PitchM
                                        Lower = tubeSize.PitchM
                                        Upper = tubeSize.PitchM
                                        Step = 1.0
                                        Unit = "m" }
                                      { Key = Optimize.DrumCenterlineHeightM
                                        Name = "quota drum"
                                        Current = dz
                                        Lower = dz
                                        Upper = dz
                                        Step = 1.0
                                        Unit = "m" } ]
                                let shellOverride : Optimize.DesignVariable list =
                                    if List.isEmpty explicitShellIds then []
                                    else
                                        [ { Key = Optimize.ShellInnerDiameterM
                                            Name = "diametro interno mantello"
                                            Current = shellId
                                            Lower = shellId
                                            Upper = shellId
                                            Step = 1.0
                                            Unit = "m" } ]
                                let vars = baseVars @ shellOverride
                                yield Optimize.applyVariables c vars [||] ]

    let private buildSeedSet (input: DesignInput) : SeededCandidates =
        { Input = input
          Candidates = seedCases input }

    let private evaluateCandidates (seeded: SeededCandidates) : EvaluatedCandidates =
        { Input = seeded.Input
          Candidates =
            seeded.Candidates
            |> List.map (fun candidate ->
                let loadResults = LoadCases.runAll seeded.Input.RunSettings seeded.Input.LoadCases candidate
                let assessment = PerformanceAssessment.assess seeded.Input.Constraints loadResults
                let objective = Optimize.scoreObjective seeded.Input.Objective loadResults
                { Case = candidate
                  LoadCaseResults = loadResults
                  Assessment = assessment
                  ObjectiveValue = objective }) }

    let private evaluateCandidatesWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (seeded: SeededCandidates) : EvaluatedCandidates =
        let total = max 1 seeded.Candidates.Length
        { Input = seeded.Input
          Candidates =
            seeded.Candidates
            |> List.mapi (fun index candidate ->
                let startFraction = float index / float total
                let endFraction = float (index + 1) / float total
                let spanReporter = ExecutionProgress.Reporting.scale startFraction endFraction reportProgress
                spanReporter
                    (ExecutionProgress.Reporting.step 0.0
                        (sprintf "Design candidate %d/%d" (index + 1) total))
                let loadResults =
                    LoadCases.runAllWithProgress spanReporter seeded.Input.RunSettings seeded.Input.LoadCases candidate
                let assessment = PerformanceAssessment.assess seeded.Input.Constraints loadResults
                let objective = Optimize.scoreObjective seeded.Input.Objective loadResults
                { Case = candidate
                  LoadCaseResults = loadResults
                  Assessment = assessment
                  ObjectiveValue = objective }) }

    let private rankCandidates (evaluated: EvaluatedCandidates) =
        evaluated.Candidates
        |> List.sortBy (fun c -> ((if c.Assessment.IsFeasible then 0 else 1), c.Assessment.TotalViolation, c.ObjectiveValue))
        |> fun ranked -> evaluated.Input, ranked

    let private buildResult ((input, ranked): DesignInput * DesignedCandidate list) : DesignResultFromScratch =
        let best =
            match ranked with
            | x :: _ -> x
            | [] -> failwith "Greenfield design space produced no candidate."
        { Input = input
          Best = best
          Shortlist = ranked |> List.truncate 5
          Evaluations = List.length ranked }

    let run (input: DesignInput) : DesignResultFromScratch =
        input
        |> buildSeedSet
        |> evaluateCandidates
        |> rankCandidates
        |> buildResult

    let runWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (input: DesignInput) : DesignResultFromScratch =
        input
        |> buildSeedSet
        |> evaluateCandidatesWithProgress reportProgress
        |> rankCandidates
        |> buildResult
