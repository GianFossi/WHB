namespace Whb.Core

open System
module Types =
    type TubeGeometry =
        { /// Tube inside diameter [m]
          Di: float
          Do: float
          Length: float
          NTubes: int
          Pitch: float
          Staggered: bool
          ShellId: float
          Otl: float
          Itl: float
          BaffleOd: float
          Roughness: float }
    type Ferrule =
        { Enabled: bool
          Lengths: (float * float) list
          Bore: float
          SleeveOd: float
          SleeveK: float -> float
          InsulK: float -> float }
    type GasStream =
        { Composition: GasProps.Composition
          MassFlow: float
          TIn: float
          PIn: float
          Z: float
          FoulingIn: float
          EpsWall: float
          Radiation: bool
          EntranceC: float
          Correlation: GasSide.Correlation
          ShiftMode: Shift.Mode
          MixingRule: GasProps.MixingRule
          RealGas: bool }
    type WaterSideSpec =
        { /// Steam-drum pressure [Pa]
          DrumPressure: float
          FoulingOut: float
          RoughnessUm: float
          BundleFactor: float
          Correlation: WaterSide.PoolBoilingCorrelation
          Csf: float
          TFeed: float }
    type LoopGeometry =
        { /// Steam-drum axis elevation minus WHB axis elevation [m]
          DzDrumWhb: float
          DrumLevelOffset: float
          Downcomers: Piping.Line list
          Risers: Piping.Line list
          DrumInternalsDp: float
          Drum: Drum.Internals
          VoidModel: TwoPhase.VoidModel
          FrictionModel: TwoPhase.FrictionModel }
    type Material = Materials.Material
    type DesignCase =
        { Name: string
          Tube: TubeGeometry
          Ferrule: Ferrule
          Gas: GasStream
          Water: WaterSideSpec
          Loop: LoopGeometry
          Material: Material
          FerruleMaterial: Material
          NZ: int
          NY: int
          AxialRefine: float
          RiserNozzleCount: int
          DowncomerNozzleCount: int
          TargetDowncomerVelocity: float
          MaxRhoV2Riser: float
          MaxRhoV2Downcomer: float
          ShellThickness: float
          ShellMaterial: Material
          UnsupportedSpan: float
          BaffleSpans: float list
          BaffleThickness: float
          TubeLayout: Vibration.Layout
          VibrationDamping: float
          TubesheetJoint: Vibration.JointType
          AssemblyTemperature: float
          ShellInsulationU: float
          Bypass: Bypass.Spec
          AllowInternalRecirculation: bool
          BypassOpenFraction: float }
    type CellResult =
        { I: int
          J: int
          C: int
          Frac: float
          FerruleLen: float
          Z: float                 // m
          Y: float                 // m from shell centerline
          NTubes: float
          TGas: float              // K
          PGas: float              // Pa
          VelGas: float            // m/s
          ReGas: float
          HConvGas: float
          HRadGas: float
          EpsGas: float
          XIn: float               // quality at band inlet
          XOut: float              // quality at band outlet
          Alpha: float             // mean void fraction in the band
          GCross: float            // kg/(m²·s) across the tube field
          VelCross: float          // m/s mixture velocity
          HBoil: float
          U_o: float
          QLin: float              // W/m per tube
          QFluxIn: float           // W/m²
          QFluxOut: float          // W/m²
          TMetalIn: float          // K
          TMetalMid: float         // K
          TMetalOut: float         // K
          TMetalWallAvg: float
          TWallBoil: float         // K
          DTsatWall: float
          DTDeposit: float
          DTMetalSat: float
          QCritLocal: float        // W/m² (bundle CHF corrected for quality)
          DNBR: float
          InFerrule: bool }
    type AxialResult =
        { Z: float
          TGasMean: float          // K, flow-weighted mean
          TGasMin: float
          TGasMax: float
          QFluxMean: float         // W/m²
          QFluxMax: float
          TMetalInMax: float       // K
          TMetalOutMax: float
          SteamLin: float          // kg/(s·m)
          DutyLin: float           // W/m
          WFieldLin: float         // kg/(s·m) across the tube field
          WBypassLin: float        // kg/(s·m) in free channels (negative = downward flow)
          XTop: float              // quality at bundle outlet
          AlphaTop: float
          GCross: float
          VelLiqIn: float
          VelMixOut: float
          VelVapOut: float
          VelAxialBottom: float
          VelAxialTop: float
          DNBRMin: float
          PGas: float
          SteamCum: float
          DutyCum: float }
    type CirculationResult =
        { CirculationRatio: float
          CircFlow: float
          SteamFlow: float
          DrivingHead: float
          DpDowncomer: float
          DpBundle: float
          DpRiser: float
          DpNozzles: float
          DpTotal: float
          XOutBundle: float
          XOutRiser: float
          AlphaOutBundle: float
          AlphaOutRiser: float
          VelDowncomer: float
          VelRiserMix: float
          HDowncomer: float
          HShell: float
          HRiser: float
          BypassFraction: float
          EffectiveCR: float
          BypassAlpha: float
          XCarryUnder: float
          OpenAnnulus: float
          StarvedSlices: int
          Converged: bool }
    type FlowRegime =
        | Bubbly
        | DispersedBubble
        | Slug
        | Churn
        | Annular
    type LineCheck =
        { Tag: string
          Nps: string
          Id: float
          Count: int
          ZNozzle: float
          AngleDeg: float
          DevelopedLength: float
          NElbows: int
          KTotal: float
          Flow: float          // kg/s in one line
          Velocity: float      // m/s
          RhoV2: float
          Regime: FlowRegime option
          Connected: bool
          Bom: string
          Note: string }
    type NozzleSpec =
        { Service: string
          Count: int
          Id: float
          Nps: string
          Positions: float list
          Velocity: float
          RhoV2: float
          RhoUsed: float
          Note: string }
    type Severity = Critical | Warning | Note
    type Finding =
        { Severity: Severity
          Area: string
          Title: string
          Value: string
          Limit: string
          Where: string
          Action: string
          Detail: string }
    type FixedTubesheetResult =
        { TTubeMeanEq: float       // K, equivalent bundle mean temperature
          TTubeHotEq: float        // K, hottest tube
          TShellEq: float          // K, shell
          AlphaTube: float
          AlphaShell: float
          AreaTube: float          // m² total tube metal area
          AreaShell: float         // m² shell metal area
          ETube: float             // Pa
          EShell: float            // Pa
          DeltaFree: float         // m, free differential expansion
          Force: float             // N (positive = tubes in compression)
          SigmaTube: float         // Pa
          SigmaShell: float        // Pa
          ForcePerTube: float      // N
          UnsupportedSpan: float   // m
          RadiusGyration: float    // m
          Slenderness: float
          SigmaBucklingAllow: float // Pa
          BucklingUtilisation: float
          ShellMaterial: string }
    type StressPoint =
        { /// "inner" | "middle" | "outer"
          Position: string
          R: float                 // m
          SigmaR: float            // Pa (negative compression)
          SigmaTheta: float        // Pa
          SigmaZ: float            // Pa (membrane plus thermal-gradient stress)
          SigmaVM: float           // Pa, von Mises
          SigmaTresca: float }     // Pa
    type StressMember =
        { Label: string
          MaterialName: string
          Count: float
          Area: float
          E: float
          TEq: float
          FreeElongation: float
          Force: float
          SigmaZ: float
          SigmaZThermal: float
          SigmaZPressure: float }
    type BucklingCheck =
        { Label: string
          MaterialName: string
          SigmaCompression: float
          Span: float
          RadiusGyration: float
          Slenderness: float
          E: float
          Sy: float
          SigmaAllow: float
          Utilisation: float
          PExtNet: float
          PCrElastic: float
          PCrYield: float
          PCollapse: float
          CollapseUtil: float
          Note: string }
    type StressCell =
        { Component: string        // "TUBI" | "BY-PASS"
          I: int
          J: int
          C: int
          Z: float                 // m
          Y: float                 // m dal centro mantello
          TMetalIn: float          // K
          TMetalOut: float         // K
          TMetalAvg: float         // K
          DTWall: float            // K, through-wall temperature drop
          PInt: float              // Pa absolute
          PExt: float              // Pa absolute
          SigmaZMembrane: float
          SigmaZThermal: float
          SigmaZPressure: float
          Points: StressPoint list
          SigmaVMMax: float        // Pa
          WorstAt: string
          Sy: float                // Pa at local temperature
          Utilisation: float }
    type LinerCheck =
        { /// Reference pressure drop equals tube-side pressure drop [Pa]
          DpTubes: float
          DpDesign: float
          Factor: float
          Od: float
          Id: float
          Thickness: float
          TEq: float               // K
          E: float
          Sy: float
          PCrElastic: float
          PCrYield: float
          PCollapse: float
          Utilisation: float
          UtilisationCode: float
          HoopStress: float
          Notes: string list }
    type StressResult =
        { /// Common axial displacement imposed by the tubesheets [m]
          CommonDelta: float
          PressureEndLoad: float
          AreaFluidShell: float
          AreaFluidTube: float
          PShell: float
          PTubeMean: float
          Members: StressMember list
          Cells: StressCell list
          Bucklings: BucklingCheck list
          LinerRestrainedForce: float
          LinerTEq: float
          LinerFreeElongation: float
          Liner: LinerCheck
          Notes: string list }
    type ValvePoint =
        { /// Opening angle [degrees] (0 = closed, 90 = fully open)
          OpenDeg: float
          ClosureDeg: float
          Zeta: float
          Fraction: float
          MassFlowBypass: float    // kg/s
          RhoValve: float          // kg/m³
          VelPipe: float           // m/s in liner
          VelThroat: float         // m/s in contracted jet
          Mach: float
          RhoV2Throat: float       // Pa
          DpValve: float           // Pa
          ZetaTheory: float
          Cv: float
          Kv: float
          KvRequired: float
          XRatio: float
          DpBypassTot: float       // Pa
          DpTubes: float           // Pa
          TOutTubes: float         // K
          TOutBypass: float        // K
          TMixed: float            // K
          Duty: float              // W
          Steam: float             // kg/s
          TLinerMax: float         // K
          Note: string }
    type ValveResult =
        { Normal: ValvePoint
          MinOpen: ValvePoint
          MaxOpen: ValvePoint
          Sweep: ValvePoint list
          MinDrivers: (string * float * string) list
          MaxDrivers: (string * float * string) list
          Diameter: float
          AtOutlet: bool }
    type ChfComparison =
        { Model: string
          QCrit: float             // W/m2
          DNBR: float
          Note: string }
    type SensitivityItem =
        { Group: string            // "gas" | "boiling" | "mixing" | ...
          Name: string
          HGas: float
          HBoil: float
          U: float
          QFlux: float             // W/m2 with U recalculated at the same local LMTD
          TMetalIn: float          // K
          Delta: float }           // percent deviation from the base selection
    type FoulingCase =
        { Label: string
          RfIn: float
          RfOut: float
          U: float
          QFlux: float
          TMetalIn: float
          TMetalOut: float
          DTDeposit: float
          DNBR: float }
    type MaldistributionPoint =
        { /// Excess flow relative to the average tube
          Excess: float
          FlowPerTube: float       // kg/s
          ReIn: float
          HGasPeak: float          // W/(m2 K) at the peak point
          QFluxMax: float          // W/m2
          ZQMax: float             // m
          TMetalInMax: float       // K
          TGasOut: float           // K
          DNBRMin: float
          DutyTube: float }        // W per tube
    type TransientResult =
        { /// Tube-metal thermal time constant [s]
          TauMetal: float
          WaterInventory: float
          DrumInventory: float
          TimeToDryoutIsolated: float
          ShellFreeVolume: float
          AlphaMean: float
          TimeToDryout: float
          TMetalDryout: float
          TimeToOverheat: float
          MakeupRate: float
          Notes: string list }
    type LoadPoint =
        { LoadFraction: float
          GasFlow: float
          Duty: float
          Steam: float
          TOutMixed: float
          TOutTubes: float
          ValveOpenDeg: float
          BypassFraction: float
          CircRatio: float
          QFluxMax: float
          TMetalMax: float
          DNBRMin: float
          DpGas: float
          AlphaMax: float
          Note: string }
    type ExpansionResult =
        { Label: string
          TEquivalent: float
          AlphaMean: float
          Length: float
          DeltaL: float }
    type RiserCheck =
        { Label: string
          Id: float
          Count: int
          VelSuperficialLiq: float
          VelSuperficialVap: float
          VelMix: float
          Alpha: float
          Regime: FlowRegime
          DMinBubbly: float
          RhoV2: float
          Ok: bool
          Note: string }
    type FerruleClassResult =
        { Index: int
          Frac: float
          Length: float
          QFluxMax: float
          ZQMax: float
          TMetalInMax: float
          DNBRMin: float
          TGasOut: float
          Duty: float }
    type DesignResult =
        { Case: DesignCase
          Sat: Steam.SatProps
          Bands: Bundle.Band list
          Cells: CellResult list
          Axial: AxialResult list
          Circulation: CirculationResult
          Nozzles: NozzleSpec list
          FerruleClasses: FerruleClassResult list
          Expansions: ExpansionResult list
          FixedTubesheet: FixedTubesheetResult
          Stress: StressResult
          Valve: ValveResult option
          Vibration: Vibration.Result list
          Maldistribution: MaldistributionPoint list
          Transient: TransientResult
          ChfModels: ChfComparison list
          Sensitivity: SensitivityItem list
          FoulingCases: FoulingCase list
          DrumResult: Drum.Result option
          BypassResult: Bypass.Result option
          Findings: Finding list
          RiserChecks: RiserCheck list
          LineChecks: LineCheck list
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
          Warnings: string list }



