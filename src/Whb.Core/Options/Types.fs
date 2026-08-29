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
          ClausMode: Claus.Mode
          ClausKinetics: Claus.KineticParameters
          MixingRule: GasProps.MixingRule
          RealGas: bool }
    type WaterSideSpec =
        { /// Steam-drum pressure [Pa]
          DrumPressure: float
          FoulingOut: float
          RoughnessUm: float
          BundleFactor: float
          Correlation: WaterSide.PoolBoilingCorrelation
          /// Flow-boiling model used on the shell side. `ChenSuperposition` preserves the
          /// historical behaviour; `KandlikarMax` is available for NBD screening.
          FlowBoiling: WaterSide.FlowBoilingModel
          Csf: float
          /// Critical-heat-flux model used for the cell-by-cell DNBR field. Palen's bundle
          /// factor is the conservative default; the alternatives exist so the same case can
          /// be re-run against a different limit and the DNBR map compared.
          ChfModel: WaterSide.ChfModel
          /// Minimum project DNBR criterion used by the local boiling-crisis screening and as
          /// the default DNBR constraint when a mode does not declare one explicitly.
          MinDNBR: float
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
          SulphurCondenser: SulphurCondenser.Spec
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
          /// Peak momentum-force swing at one bend of this line [N], between a liquid slug and
          /// a vapour plug passing through it. Zero outside intermittent regimes.
          SlugForce: float
          /// Rate at which those slugs arrive [Hz]: the frequency the pipe supports see.
          SlugFrequency: float
          /// Bends on the line that the swing acts on.
          Bends: int
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
    /// <summary>
    /// Numerical health of a design run: what converged, what was clamped, and what was
    /// solved outside the domain the method intends.
    /// </summary>
    /// <remarks>
    /// These are diagnostics, not results. They exist so that a run that did not fully
    /// converge cannot be mistaken for one that did.
    /// </remarks>
    type ConvergenceReport =
        { /// Iterations spent in the coupled thermal/circulation loop.
          CoupledIterations: int
          /// True when the coupled loop met its tolerance instead of hitting the iteration cap.
          CoupledConverged: bool
          /// Largest relative change left on the field flow and inlet quality at exit.
          CoupledResidual: float
          /// Cells whose outlet quality hit the 0.95 barrier.
          QualityClampedCells: int
          /// Axial position of the first clamped cell, or NaN when none was clamped.
          QualityClampFirstZ: float
          /// Cells whose wall-temperature fixed point was still moving at the iteration cap.
          NonConvergedCells: int
          /// Sign changes found scanning the circulation balance over its bracket. More than
          /// one means the operating point is not unique (Ledinegg-type multiplicity).
          CirculationRoots: int
          /// True when the circulation bracket contained a sign change at all.
          CirculationBracketOk: bool
          /// Slope of the loop balance at the operating point. Negative is a stable crossing;
          /// positive is a flow-excursion point in the Ledinegg sense.
          CirculationSlope: float
          /// Feedwater subcooling available at the downcomer inlet [K], and the subcooling the
          /// local pressure drop there demands to avoid flashing.
          DowncomerSubcooling: float
          DowncomerSubcoolingRequired: float
          /// True when the bypass map brackets the target mixed temperature, so the reported
          /// valve limits are real limits and not the edge of the computed map.
          BypassMapBracketsTarget: bool }

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
          SulphurCoupling: Sulphur.CouplingSummary option
          SulphurCondenserResult: SulphurCondenser.Result option
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
          /// Net steam leaving the drum, i.e. the duty divided by the rise from feedwater
          /// enthalpy to saturated vapour. `SteamProduction` is the evaporation rate inside
          /// the bundle, which assumes the water entering the tubes is already saturated.
          SteamProductionNet: float
          /// Feedwater subcooling at drum pressure [K]. This is the margin that keeps the
          /// downcomer inlet from flashing.
          FeedSubcooling: float
          Convergence: ConvergenceReport
          Warnings: string list }



