namespace Whb.Core

open System

/// <summary>
/// Aggregates multi-load-case verification results into governing constraints and feasibility.
/// </summary>
module PerformanceAssessment =

    type Assessment =
        { ConstraintReadings: ConstraintModel.ConstraintReading list
          IsFeasible: bool
          GoverningLoadCases: string list
          TotalViolation: float }

    type private AssessmentInput =
        { Constraints: ConstraintModel.ConstraintSet
          Results: LoadCases.LoadCaseResult list }

    type private ReadingSummary =
        { Input: AssessmentInput
          Readings: ConstraintModel.ConstraintReading list }

    let private scaleFor value a b =
        max 1e-9 (max (abs value) (max (abs a) (abs b)))

    let private score limit value =
        match limit with
        | ConstraintModel.Min x ->
            let s = scaleFor value x x
            (value - x) / s
        | ConstraintModel.Max x ->
            let s = scaleFor value x x
            (x - value) / s
        | ConstraintModel.Range(lo, hi) ->
            let s = scaleFor value lo hi
            min (value - lo) (hi - value) / s

    let private readingForTarget (target: ConstraintModel.ConstraintTarget) (results: LoadCases.LoadCaseResult list) =
        let available =
            results
            |> List.choose (fun r ->
                match ConstraintReaders.tryFindValue target.Key r.Verification.Result with
                | Some value ->
                    let limitScore = score target.Limit value.Value
                    Some
                        ({ Target = target
                           Value = value.Value
                           GoverningLoadCase = r.Spec.Name
                           Passed = limitScore >= 0.0
                           LimitScore = limitScore
                           NormalizedViolation = max 0.0 (-limitScore) } : ConstraintModel.ConstraintReading)
                | None -> None)
        match available with
        | [] ->
            ({ Target = target
               Value = nan
               GoverningLoadCase = "(missing)"
               Passed = not target.Required
               LimitScore = if target.Required then Double.NegativeInfinity else 0.0
               NormalizedViolation = if target.Required then 1.0 else 0.0 } : ConstraintModel.ConstraintReading)
        | xs -> xs |> List.minBy (fun r -> r.LimitScore)

    let private collectReadings (input: AssessmentInput) : ReadingSummary =
        { Input = input
          Readings = input.Constraints.Targets |> List.map (fun target -> readingForTarget target input.Results) }

    let private summarizeAssessment (summary: ReadingSummary) : Assessment =
        let readings = summary.Readings
        let feasible =
            readings
            |> List.forall (fun r -> (not r.Target.Required) || r.Passed)
        let governing =
            readings
            |> List.filter (fun r -> r.Target.Required)
            |> List.map (fun r -> r.GoverningLoadCase)
            |> List.distinct
        let violation =
            readings
            |> List.sumBy (fun r -> if r.Target.Required then r.Target.Weight * r.NormalizedViolation else 0.0)
        { ConstraintReadings = readings
          IsFeasible = feasible
          GoverningLoadCases = governing
          TotalViolation = violation }

    let assess (constraints: ConstraintModel.ConstraintSet) (results: LoadCases.LoadCaseResult list) : Assessment =
        { Constraints = constraints
          Results = results }
        |> collectReadings
        |> summarizeAssessment
