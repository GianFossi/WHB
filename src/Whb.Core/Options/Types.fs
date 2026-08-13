namespace Whb.Core

open System

/// <summary>
/// Provides types functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Types =

    /// <summary>
    /// Represents tubegeometry data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type TubeGeometry =
        { /// Diametro interno tubo [m]
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

    /// <summary>
    /// Represents ferrule data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Ferrule =
        { Enabled: bool
          Lengths: (float * float) list
          Bore: float
          SleeveOd: float
          SleeveK: float -> float
          InsulK: float -> float }

    /// <summary>
    /// Represents gasstream data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents watersidespec data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type WaterSideSpec =
        { /// Pressione nel corpo cilindrico [Pa]
          DrumPressure: float
          FoulingOut: float
          RoughnessUm: float
          BundleFactor: float
          Correlation: WaterSide.PoolBoilingCorrelation
          Csf: float
          TFeed: float }

    /// <summary>
    /// Represents loopgeometry data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type LoopGeometry =
        { /// Quota asse corpo cilindrico - asse WHB [m]
          DzDrumWhb: float
          DrumLevelOffset: float
          Downcomers: Piping.Line list
          Risers: Piping.Line list
          DrumInternalsDp: float
          Drum: Drum.Internals
          VoidModel: TwoPhase.VoidModel
          FrictionModel: TwoPhase.FrictionModel }

    /// <summary>
    /// Represents material data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Material = Materials.Material

    /// <summary>
    /// Represents designcase data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents cellresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type CellResult =
        { I: int
          J: int
          C: int
          Frac: float
          FerruleLen: float
          Z: float                 // m
          Y: float                 // m (dal centro mantello)
          NTubes: float
          TGas: float              // K
          PGas: float              // Pa
          VelGas: float            // m/s
          ReGas: float
          HConvGas: float
          HRadGas: float
          EpsGas: float
          XIn: float               // titolo all'ingresso della banda
          XOut: float              // titolo all'uscita della banda
          Alpha: float             // frazione di vuoto media nella banda
          GCross: float            // kg/(m²·s) nel campo tubi
          VelCross: float          // m/s velocità della miscela
          HBoil: float
          U_o: float
          QLin: float              // W/m per tubo
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
          QCritLocal: float        // W/m² (CHF di fascio corretto per il titolo)
          DNBR: float
          InFerrule: bool }

    /// <summary>
    /// Represents axialresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type AxialResult =
        { Z: float
          TGasMean: float          // K, media pesata sulla portata
          TGasMin: float
          TGasMax: float
          QFluxMean: float         // W/m²
          QFluxMax: float
          TMetalInMax: float       // K
          TMetalOutMax: float
          SteamLin: float          // kg/(s·m)
          DutyLin: float           // W/m
          WFieldLin: float         // kg/(s·m) attraverso il campo tubi
          WBypassLin: float        // kg/(s·m) nei canali liberi (negativo = discesa)
          XTop: float              // titolo in uscita dal fascio
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

    /// <summary>
    /// Represents circulationresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents flowregime data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type FlowRegime =
        | Bubbly
        | DispersedBubble
        | Slug
        | Churn
        | Annular

    /// <summary>
    /// Represents linecheck data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
          Flow: float          // kg/s nella singola linea
          Velocity: float      // m/s
          RhoV2: float
          Regime: FlowRegime option
          Connected: bool
          Bom: string
          Note: string }

    /// <summary>
    /// Represents nozzlespec data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents severity data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Severity = Critical | Warning | Note

    /// <summary>
    /// Represents finding data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Finding =
        { Severity: Severity
          Area: string
          Title: string
          Value: string
          Limit: string
          Where: string
          Action: string
          Detail: string }

    /// <summary>
    /// Represents fixedtubesheetresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type FixedTubesheetResult =
        { TTubeMeanEq: float       // K, temperatura media equivalente del fascio
          TTubeHotEq: float        // K, tubo piu' caldo
          TShellEq: float          // K, mantello
          AlphaTube: float
          AlphaShell: float
          AreaTube: float          // m² sezione metallica totale dei tubi
          AreaShell: float         // m² sezione metallica del mantello
          ETube: float             // Pa
          EShell: float            // Pa
          DeltaFree: float         // m, dilatazione differenziale libera
          Force: float             // N (positiva = tubi in COMPRESSIONE)
          SigmaTube: float         // Pa
          SigmaShell: float        // Pa
          ForcePerTube: float      // N
          UnsupportedSpan: float   // m
          RadiusGyration: float    // m
          Slenderness: float
          SigmaBucklingAllow: float // Pa
          BucklingUtilisation: float
          ShellMaterial: string }


    /// <summary>
    /// Represents stresspoint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type StressPoint =
        { /// "interna" | "media" | "esterna"
          Position: string
          R: float                 // m
          SigmaR: float            // Pa (compressione negativa)
          SigmaTheta: float        // Pa
          SigmaZ: float            // Pa (membranale + gradiente termico)
          SigmaVM: float           // Pa, von Mises
          SigmaTresca: float }     // Pa

    /// <summary>
    /// Represents stressmember data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents bucklingcheck data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents stresscell data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
          DTWall: float            // K, salto nello spessore
          PInt: float              // Pa assoluti
          PExt: float              // Pa assoluti
          SigmaZMembrane: float
          SigmaZThermal: float
          SigmaZPressure: float
          Points: StressPoint list
          SigmaVMMax: float        // Pa
          WorstAt: string
          Sy: float                // Pa alla temperatura locale
          Utilisation: float }

    /// <summary>
    /// Represents linercheck data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type LinerCheck =
        { /// salto di riferimento = perdita di carico lato tubi [Pa]
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

    /// <summary>
    /// Represents stressresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type StressResult =
        { /// spostamento assiale comune imposto dalle piastre [m]
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

    /// <summary>
    /// Represents valvepoint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ValvePoint =
        { /// angolo di APERTURA [gradi] (0 = chiusa, 90 = tutta aperta)
          OpenDeg: float
          ClosureDeg: float
          Zeta: float
          Fraction: float
          MassFlowBypass: float    // kg/s
          RhoValve: float          // kg/m³
          VelPipe: float           // m/s nel liner
          VelThroat: float         // m/s nella vena contratta
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

    /// <summary>
    /// Represents valveresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ValveResult =
        { Normal: ValvePoint
          MinOpen: ValvePoint
          MaxOpen: ValvePoint
          Sweep: ValvePoint list
          MinDrivers: (string * float * string) list
          MaxDrivers: (string * float * string) list
          Diameter: float
          AtOutlet: bool }

    /// <summary>
    /// Represents chfcomparison data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ChfComparison =
        { Model: string
          QCrit: float             // W/m2
          DNBR: float
          Note: string }

    /// <summary>
    /// Represents sensitivityitem data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type SensitivityItem =
        { Group: string            // "gas" | "ebollizione" | "miscelazione" | ...
          Name: string
          HGas: float
          HBoil: float
          U: float
          QFlux: float             // W/m2 con U ricalcolato a pari LMTD locale
          TMetalIn: float          // K
          Delta: float }           // scostamento % rispetto alla scelta di base

    /// <summary>
    /// Represents foulingcase data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents maldistributionpoint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type MaldistributionPoint =
        { /// eccesso di portata rispetto al tubo medio
          Excess: float
          FlowPerTube: float       // kg/s
          ReIn: float
          HGasPeak: float          // W/(m2 K) nel punto di picco
          QFluxMax: float          // W/m2
          ZQMax: float             // m
          TMetalInMax: float       // K
          TGasOut: float           // K
          DNBRMin: float
          DutyTube: float }        // W per tubo

    /// <summary>
    /// Represents transientresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type TransientResult =
        { /// costante di tempo termica del metallo del tubo [s]
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

    /// <summary>
    /// Represents loadpoint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents expansionresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ExpansionResult =
        { Label: string
          TEquivalent: float
          AlphaMean: float
          Length: float
          DeltaL: float }

    /// <summary>
    /// Represents risercheck data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

    /// <summary>
    /// Represents ferruleclassresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// Represents designresult data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
