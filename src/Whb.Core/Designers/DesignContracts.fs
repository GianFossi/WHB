namespace Whb.Core

open Whb.Core.Types

/// <summary>
/// Contracts shared by the thermal/process and mechanical design stages.
/// </summary>
module DesignContracts =

    type FerruleClassLayout =
        { Index: int
          Fraction: float
          Length: float }

    /// <summary>
    /// Output of the thermal/process verification stage, which is the shared input contract for
    /// downstream mechanical screening and final reporting.
    /// </summary>
    type ThermalProcessStageResult =
        { Case: DesignCase
          NotConnectedLines: Piping.Line list
          Sat: Steam.SatProps
          Bands: Bundle.Band list
          DZ: float[]
          CellField: CellResult[,,]
          ClassLayouts: FerruleClassLayout list
          Cells: CellResult list
          Axial: AxialResult list
          Circulation: CirculationResult
          Nozzles: NozzleSpec list
          FerruleClasses: FerruleClassResult list
          Valve: ValveResult option
          Vibration: Vibration.Result list
          Maldistribution: MaldistributionPoint list
          Transient: TransientResult
          ChfModels: ChfComparison list
          Sensitivity: SensitivityItem list
          FoulingCases: FoulingCase list
          DrumResult: Drum.Result option
          BypassResult: Bypass.Result option
          SulphurCoupling: Sulphur.CouplingSummary option
          SulphurCondenserResult: SulphurCondenser.Result option
          Duty: float
          SteamProduction: float
          TGasOutMean: float
          TGasOutMin: float
          TGasOutMax: float
          DpGas: float
          AreaOut: float
          AreaIn: float
          UMean: float
          LmtdMean: float
          SteamProductionNet: float
          FeedSubcooling: float
          Convergence: ConvergenceReport }

    /// <summary>
    /// Mechanical screening results produced from the shared thermal/process contract.
    /// </summary>
    type MechanicalStageResult =
        { Expansions: ExpansionResult list
          FixedTubesheet: FixedTubesheetResult
          Stress: StressResult
          RiserChecks: RiserCheck list
          LineChecks: LineCheck list
          CalculationInterface: MechanicalDesignContracts.MechanicalCalculationInterface }
