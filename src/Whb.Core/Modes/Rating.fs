namespace Whb.Core

open Types

/// <summary>
/// Rating mode for verifying one fixed geometry across one or more load cases.
/// </summary>
module Rating =

    type RatingInput =
        { BaseCase: DesignCase
          LoadCases: LoadCases.LoadCaseSpec list
          Constraints: ConstraintModel.ConstraintSet
          RunSettings: DesignRuntime.RunSettings }

    type RatingResult =
        { Input: RatingInput
          LoadCaseResults: LoadCases.LoadCaseResult list
          Assessment: PerformanceAssessment.Assessment }

    type private EvaluatedRating =
        { Input: RatingInput
          LoadCaseResults: LoadCases.LoadCaseResult list }

    let private evaluateLoadCases (input: RatingInput) : EvaluatedRating =
        { Input = input
          LoadCaseResults = LoadCases.runAll input.RunSettings input.LoadCases input.BaseCase }

    let private assessPerformance (evaluated: EvaluatedRating) : RatingResult =
        { Input = evaluated.Input
          LoadCaseResults = evaluated.LoadCaseResults
          Assessment = PerformanceAssessment.assess evaluated.Input.Constraints evaluated.LoadCaseResults }

    let private evaluateLoadCasesWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (input: RatingInput) : EvaluatedRating =
        { Input = input
          LoadCaseResults = LoadCases.runAllWithProgress reportProgress input.RunSettings input.LoadCases input.BaseCase }

    let run (input: RatingInput) : RatingResult =
        input
        |> evaluateLoadCases
        |> assessPerformance

    let runWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (input: RatingInput) : RatingResult =
        input
        |> evaluateLoadCasesWithProgress reportProgress
        |> assessPerformance
