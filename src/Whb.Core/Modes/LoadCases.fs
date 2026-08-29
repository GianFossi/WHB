namespace Whb.Core

open Types

/// <summary>
/// Load-case definitions layered over one base geometry.
/// </summary>
module LoadCases =

    type LoadCaseSpec =
        { Name: string
          GasMassFlow: float option
          GasMassFlowFactor: float option
          GasInletTemperature: float option
          DrumPressure: float option
          BypassTargetMixOut: float option
          BypassOpenFraction: float option
          Notes: string list }

    type LoadCaseResult =
        { Spec: LoadCaseSpec
          Verification: VerificationEngine.VerificationResult }

    type private ExecutionContext =
        { Settings: DesignRuntime.RunSettings
          BaseCase: DesignCase
          RequestedLoadCases: LoadCaseSpec list }

    type private PlannedExecution =
        { Settings: DesignRuntime.RunSettings
          BaseCase: DesignCase
          EffectiveLoadCases: LoadCaseSpec list }

    let baseCase name =
        { Name = name
          GasMassFlow = None
          GasMassFlowFactor = None
          GasInletTemperature = None
          DrumPressure = None
          BypassTargetMixOut = None
          BypassOpenFraction = None
          Notes = [] }

    let applyToCase (spec: LoadCaseSpec) (caseIn: DesignCase) : DesignCase =
        let gasFlow =
            match spec.GasMassFlow, spec.GasMassFlowFactor with
            | Some value, _ -> value
            | None, Some factor -> caseIn.Gas.MassFlow * factor
            | None, None -> caseIn.Gas.MassFlow
        let gas =
            { caseIn.Gas with
                MassFlow = gasFlow
                TIn = defaultArg spec.GasInletTemperature caseIn.Gas.TIn }
        let water =
            { caseIn.Water with
                DrumPressure = defaultArg spec.DrumPressure caseIn.Water.DrumPressure }
        let bypass =
            { caseIn.Bypass with
                TargetMixOut = defaultArg spec.BypassTargetMixOut caseIn.Bypass.TargetMixOut }
        { caseIn with
            Gas = gas
            Water = water
            Bypass = bypass
            BypassOpenFraction = defaultArg spec.BypassOpenFraction caseIn.BypassOpenFraction }

    let private planExecution (context: ExecutionContext) : PlannedExecution =
        { Settings = context.Settings
          BaseCase = context.BaseCase
          EffectiveLoadCases =
            if List.isEmpty context.RequestedLoadCases then [ baseCase "base" ]
            else context.RequestedLoadCases }

    let private evaluateLoadCase (plan: PlannedExecution) (spec: LoadCaseSpec) : LoadCaseResult =
        let caseForLoad = applyToCase spec plan.BaseCase
        { Spec = spec
          Verification = VerificationEngine.evaluateSilent plan.Settings caseForLoad }

    let private evaluateLoadCaseWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (plan: PlannedExecution) (spec: LoadCaseSpec) : LoadCaseResult =
        let caseForLoad = applyToCase spec plan.BaseCase
        { Spec = spec
          Verification =
            VerificationEngine.evaluate
                { Case = caseForLoad
                  RunSettings = plan.Settings
                  ReportProgress = reportProgress } }

    let private runPlannedLoadCases (plan: PlannedExecution) : LoadCaseResult list =
        plan.EffectiveLoadCases |> List.map (evaluateLoadCase plan)

    let private runPlannedLoadCasesWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (plan: PlannedExecution) : LoadCaseResult list =
        let total = max 1 plan.EffectiveLoadCases.Length
        plan.EffectiveLoadCases
        |> List.mapi (fun index spec ->
            let startFraction = float index / float total
            let endFraction = float (index + 1) / float total
            let spanReporter = ExecutionProgress.Reporting.scale startFraction endFraction reportProgress
            spanReporter
                (ExecutionProgress.Reporting.step 0.0
                    (sprintf "Load case %d/%d: %s" (index + 1) total spec.Name))
            evaluateLoadCaseWithProgress spanReporter plan spec)

    let runAll (settings: DesignRuntime.RunSettings) (loadCases: LoadCaseSpec list) (baseDesignCase: DesignCase) : LoadCaseResult list =
        { Settings = settings
          BaseCase = baseDesignCase
          RequestedLoadCases = loadCases }
        |> planExecution
        |> runPlannedLoadCases

    let runAllWithProgress (reportProgress: DesignRuntime.ProgressUpdate -> unit) (settings: DesignRuntime.RunSettings) (loadCases: LoadCaseSpec list) (baseDesignCase: DesignCase) : LoadCaseResult list =
        { Settings = settings
          BaseCase = baseDesignCase
          RequestedLoadCases = loadCases }
        |> planExecution
        |> runPlannedLoadCasesWithProgress reportProgress
