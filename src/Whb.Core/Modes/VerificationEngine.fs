namespace Whb.Core

open Types

/// <summary>
/// Single entry point for geometry verification across rating, optimization, and design modes.
/// </summary>
/// <remarks>
/// This wrapper keeps the verification engine explicit at the architectural boundary: every
/// higher-level mode calls the same thermal/process and mechanical pipeline through this module.
/// </remarks>
module VerificationEngine =

    type VerificationRequest =
        { Case: DesignCase
          RunSettings: DesignRuntime.RunSettings
          ReportProgress: DesignRuntime.ProgressUpdate -> unit }

    type VerificationResult =
        { Request: VerificationRequest
          Result: DesignResult }

    let private executeSharedVerification (request: VerificationRequest) : VerificationResult =
        { Request = request
          Result = Design.runWithSettingsAndStructuredProgress request.RunSettings request.ReportProgress request.Case }

    let evaluate (request: VerificationRequest) : VerificationResult =
        request |> executeSharedVerification

    let evaluateSilent (settings: DesignRuntime.RunSettings) (caseIn: DesignCase) : VerificationResult =
        { Case = caseIn
          RunSettings = settings
          ReportProgress = ignore }
        |> evaluate
