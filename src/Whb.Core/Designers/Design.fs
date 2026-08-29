namespace Whb.Core

open Types

/// <summary>
/// Coordinates WHB process, thermal, hydraulic, vibration, mechanical, and equipment calculations into a design result.
/// </summary>
/// <remarks>
/// The thermal/process verification engine and the mechanical screening stage are orchestrated
/// here as separate modules with a typed hand-off contract. This keeps the verification kernel
/// shared while allowing each discipline to evolve independently.
/// </remarks>
module Design =

    type RunSettings = DesignRuntime.RunSettings

    let defaultRunSettings : RunSettings = DesignRuntime.defaultRunSettings

    module ParallelBudget = DesignRuntime.ParallelBudget

    let private sev = function Critical -> "CRITICO" | Warning -> "ATTENZIONE" | Note -> "NOTA"
    let private announce (reportProgress: DesignRuntime.ProgressUpdate -> unit) (fraction: float) (message: string) value =
        reportProgress (ExecutionProgress.Reporting.step fraction message)
        value

    type private DesignRequest =
        { Settings: RunSettings
          Case: DesignCase }

    type private ThermalStage =
        { Request: DesignRequest
          Thermal: DesignContracts.ThermalProcessStageResult }

    type private VerifiedStages =
        { ThermalStage: ThermalStage
          Mechanical: DesignContracts.MechanicalStageResult }

    type private AssessedStages =
        { Verified: VerifiedStages
          Findings: Finding list
          Warnings: string list }

    let buildFindings (correlationValidityWarnings: bool) (case: DesignCase) (sat: Steam.SatProps) (cells: CellResult list)
                      (axial: AxialResult list) (circ: CirculationResult)
                      (ft: FixedTubesheetResult) (risers: RiserCheck list)
                      (expansions: ExpansionResult list) (bp: Bypass.Result option) (dpGas: float)
                      (stress: StressResult) (valve: ValveResult option)
                      (notConnected: Piping.Line list)
                      (vibration: Vibration.Result list)
                      (conv: ConvergenceReport)
                      (sulphur: Sulphur.CouplingSummary option)
                      (sulphurCondenser: SulphurCondenser.Result option) =
        Findings.build correlationValidityWarnings case sat cells axial circ ft risers expansions bp dpGas
            stress valve notConnected vibration conv sulphur sulphurCondenser

    let private buildWarnings findings =
        findings
        |> List.map (fun f ->
            sprintf "%s - %s: %s (criterio: %s) @ %s%s%s"
                (sev f.Severity) f.Title f.Value f.Limit f.Where
                (if f.Detail = "" then "" else " | " + f.Detail)
                (if f.Action = "" then "" else " | AZIONE: " + f.Action))

    let private createRequest (settings: RunSettings) (caseIn: DesignCase) : DesignRequest =
        { Settings = settings
          Case = caseIn }

    let private runThermalStage (reportProgress: DesignRuntime.ProgressUpdate -> unit) (request: DesignRequest) : ThermalStage =
        { Request = request
          Thermal = DesignThermalProcess.run request.Settings reportProgress request.Case }

    let private runMechanicalStage (reportProgress: DesignRuntime.ProgressUpdate -> unit) (thermalStage: ThermalStage) : VerifiedStages =
        { ThermalStage = thermalStage
          Mechanical = DesignMechanical.run reportProgress thermalStage.Thermal }

    let private assessStages (verified: VerifiedStages) : AssessedStages =
        let thermal = verified.ThermalStage.Thermal
        let mechanical = verified.Mechanical
        let settings = verified.ThermalStage.Request.Settings
        let findings =
            buildFindings settings.CorrelationValidityWarnings thermal.Case thermal.Sat thermal.Cells thermal.Axial
                thermal.Circulation mechanical.FixedTubesheet mechanical.RiserChecks mechanical.Expansions
                thermal.BypassResult thermal.DpGas mechanical.Stress thermal.Valve thermal.NotConnectedLines
                thermal.Vibration thermal.Convergence thermal.SulphurCoupling thermal.SulphurCondenserResult
        { Verified = verified
          Findings = findings
          Warnings = buildWarnings findings }

    let private toDesignResult (assessed: AssessedStages) : DesignResult =
        let thermal = assessed.Verified.ThermalStage.Thermal
        let mechanical = assessed.Verified.Mechanical
        { Case = thermal.Case
          Sat = thermal.Sat
          Bands = thermal.Bands
          Cells = thermal.Cells
          Axial = thermal.Axial
          Circulation = thermal.Circulation
          Nozzles = thermal.Nozzles
          FerruleClasses = thermal.FerruleClasses
          Expansions = mechanical.Expansions
          FixedTubesheet = mechanical.FixedTubesheet
          Stress = mechanical.Stress
          Valve = thermal.Valve
          Vibration = thermal.Vibration
          Maldistribution = thermal.Maldistribution
          Transient = thermal.Transient
          ChfModels = thermal.ChfModels
          Sensitivity = thermal.Sensitivity
          FoulingCases = thermal.FoulingCases
          DrumResult = thermal.DrumResult
          BypassResult = thermal.BypassResult
          SulphurCoupling = thermal.SulphurCoupling
          SulphurCondenserResult = thermal.SulphurCondenserResult
          Findings = assessed.Findings
          RiserChecks = mechanical.RiserChecks
          LineChecks = mechanical.LineChecks
          Duty = thermal.Duty
          SteamProduction = thermal.SteamProduction
          TGasOutMean = thermal.TGasOutMean
          TGasOutMin = thermal.TGasOutMin
          TGasOutMax = thermal.TGasOutMax
          DpGas = thermal.DpGas
          AreaOut = thermal.AreaOut
          AreaIn = thermal.AreaIn
          UMean = thermal.UMean
          LmtdMean = thermal.LmtdMean
          SteamProductionNet = thermal.SteamProductionNet
          FeedSubcooling = thermal.FeedSubcooling
          Convergence = thermal.Convergence
          Warnings = assessed.Warnings }

    let private runPipeline (settings: RunSettings) (reportProgress: DesignRuntime.ProgressUpdate -> unit) =
        createRequest settings
        >> announce reportProgress 0.0 "Starting shared verification pipeline"
        >> runThermalStage (ExecutionProgress.Reporting.scale 0.02 0.84 reportProgress)
        >> announce reportProgress 0.85 "Running shared mechanical verification stage"
        >> runMechanicalStage (ExecutionProgress.Reporting.scale 0.86 0.97 reportProgress)
        >> announce reportProgress 0.98 "Assembling shared findings and final design result"
        >> assessStages
        >> toDesignResult

    let runWithSettingsAndStructuredProgress (settings: RunSettings) (reportProgress: DesignRuntime.ProgressUpdate -> unit) (caseIn: DesignCase) : DesignResult =
        caseIn |> runPipeline settings reportProgress

    let runWithSettingsAndProgress (settings: RunSettings) (reportProgress: string -> unit) (caseIn: DesignCase) : DesignResult =
        runWithSettingsAndStructuredProgress settings (fun update -> reportProgress update.Description) caseIn

    /// <summary>
    /// Runs the complete WHB design calculation with progress callbacks and default runtime settings.
    /// </summary>
    let runWithProgress (reportProgress: string -> unit) (caseIn: DesignCase) : DesignResult =
        runWithSettingsAndProgress defaultRunSettings reportProgress caseIn

    /// <summary>
    /// Runs the complete WHB design calculation without progress callbacks.
    /// </summary>
    let run (caseIn: DesignCase) : DesignResult =
        runWithSettingsAndProgress defaultRunSettings ignore caseIn
