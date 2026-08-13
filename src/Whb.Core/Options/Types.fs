namespace Whb.Core

open System

/// Modello dati del caso di progetto di una caldaia a recupero (WHB/PGC)
/// a tubi da fumo: gas di processo nei tubi, acqua/vapore in ebollizione
/// a mantello, circolazione naturale con corpo cilindrico sopraelevato.
///
/// La discretizzazione è **bidimensionale**: Nz sezioni lungo l'asse
/// dell'apparecchio × Ny bande orizzontali del fascio tubiero.
module Types =

    type TubeGeometry =
        { /// Diametro interno tubo [m]
          Di: float
          /// Diametro esterno tubo [m]
          Do: float
          /// Lunghezza riscaldata (fra facce interne piastre tubiere) [m]
          Length: float
          /// Numero di tubi
          NTubes: int
          /// Passo [m]
          Pitch: float
          /// Layout sfalsato (triangolare) o in linea (quadrato)
          Staggered: bool
          /// Diametro interno mantello [m]
          ShellId: float
          /// OTL - diametro esterno del fascio [m]
          Otl: float
          /// ITL - diametro dell'anima centrale non intubata [m]
          Itl: float
          /// Diametro esterno dei diaframmi di supporto [m].
          /// Se < ShellId resta aperta una corona anulare che funziona da
          /// canale verticale di by-pass / discesa interna.
          BaffleOd: float
          /// Rugosità assoluta interna tubo [m]
          Roughness: float }

    /// Ferrula multistrato (manicotto metallico + isolante anulare).
    /// Geometria da disegno: bore gas -> manicotto -> carta isolante -> tubo.
    type Ferrule =
        { Enabled: bool
          /// Classi di lunghezza della ferrula: (frazione di tubi, lunghezza [m]).
          /// Consente di rappresentare una popolazione di tubi con ferrule di
          /// lunghezza diversa (tolleranze di montaggio, forniture miste).
          Lengths: (float * float) list
          /// Diametro interno del passaggio gas [m]
          Bore: float
          /// Diametro esterno del manicotto metallico [m]
          SleeveOd: float
          /// k(T[°C]) del manicotto metallico [W/(m·K)]
          SleeveK: float -> float
          /// k(T[°C]) dell'isolante anulare (carta/feltro ceramico) [W/(m·K)]
          InsulK: float -> float }

    type GasStream =
        { Composition: GasProps.Composition
          /// Portata massica totale lato gas [kg/s] (già maggiorata)
          MassFlow: float
          /// Temperatura ingresso [K]
          TIn: float
          /// Pressione ingresso [Pa]
          PIn: float
          /// Fattore di comprimibilità
          Z: float
          /// Resistenza di sporcamento interna [m²·K/W]
          FoulingIn: float
          /// Emissività della parete
          EpsWall: float
          /// Includere lo scambio radiativo del gas triatomico
          Radiation: bool
          /// Costante di correzione d'imbocco
          EntranceC: float
          /// Correlazione lato gas
          Correlation: GasSide.Correlation
          /// Trattamento della reazione di water-gas shift
          ShiftMode: Shift.Mode
          /// Regola di miscelazione per viscosità e conducibilità
          MixingRule: GasProps.MixingRule
          /// Correzione di GAS REALE (secondo viriale) su Z, entalpia e cp
          RealGas: bool }

    type WaterSideSpec =
        { /// Pressione nel corpo cilindrico [Pa]
          DrumPressure: float
          /// Resistenza di sporcamento esterna [m²·K/W]
          FoulingOut: float
          /// Rugosità superficiale per correlazioni di ebollizione [µm]
          RoughnessUm: float
          /// Fattore di fascio di Palen Fb
          BundleFactor: float
          /// Correlazione di ebollizione nucleata
          Correlation: WaterSide.PoolBoilingCorrelation
          /// Costante di Rohsenow Csf
          Csf: float
          /// Temperatura acqua alimento [K]
          TFeed: float }

    type LoopGeometry =
        { /// Quota asse corpo cilindrico - asse WHB [m]
          DzDrumWhb: float
          /// Livello acqua nel drum rispetto all'asse del drum [m]
          DrumLevelOffset: float
          /// Linee di discesa (downcomer), con distinta tratti/curve
          Downcomers: Piping.Line list
          /// Linee di salita (riser)
          Risers: Piping.Line list
          /// Perdita di carico nelle interne del corpo cilindrico [Pa]
          /// (cicloni, separatori, imbocco downcomer). E' un dato del
          /// costruttore del drum: pesa molto sul rapporto di circolazione.
          DrumInternalsDp: float
          /// Interne del corpo cilindrico: geometria e coefficienti
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
          /// Materiale del manicotto ferrula (per verifiche)
          FerruleMaterial: Material
          /// Sezioni assiali
          NZ: int
          /// Bande orizzontali del fascio
          NY: int
          /// Grado di infittimento della maglia assiale all'imbocco
          /// (1 = uniforme; 8 = prima cella 8 volte piu' fine della media)
          AxialRefine: float
          RiserNozzleCount: int
          DowncomerNozzleCount: int
          TargetDowncomerVelocity: float
          MaxRhoV2Riser: float
          MaxRhoV2Downcomer: float
          /// Spessore del mantello [m]
          ShellThickness: float
          /// Materiale del mantello
          ShellMaterial: Material
          /// Massima campata non supportata dei tubi (passo diaframmi) [m].
          /// E' un ripiego: se BaffleSpans e' popolato si usa quello.
          UnsupportedSpan: float
          /// CAMPATE LIBERE fra i supporti, in ordine dalla piastra tubiera
          /// lato gas caldo [m]. La prima va dalla faccia interna della piastra
          /// al primo diaframma, l'ultima dall'ultimo diaframma alla seconda
          /// piastra. Se vuota si usa UnsupportedSpan uniforme.
          BaffleSpans: float list
          /// Spessore dei diaframmi [m]
          BaffleThickness: float
          /// Reticolo secondo TEMA RCB-2.4, riferito alla direzione del
          /// crossflow lato mantello. Seleziona la costante di Connors.
          TubeLayout: Vibration.Layout
          /// Decremento logaritmico totale per la verifica FIV
          VibrationDamping: float
          /// Tipo di giunto tubo-piastra: decide il vincolo alla piastra
          /// tubiera nella verifica di vibrazione (ai diaframmi il tubo e'
          /// comunque un semplice nodo)
          TubesheetJoint: Vibration.JointType
          /// Temperatura di montaggio / riferimento per le dilatazioni [K]
          AssemblyTemperature: float
          /// Trasmittanza globale del mantello verso l'ambiente [W/(m²·K)]
          ShellInsulationU: float
          /// By-pass interno centrale
          Bypass: Bypass.Spec
          /// Consente ricircolo interno attraverso i canali non intubati
          AllowInternalRecirculation: bool
          /// Frazione dei canali liberi effettivamente aperta in verticale.
          /// I diaframmi di supporto (OD ~ ID mantello) bloccano quasi del tutto
          /// la corona periferica: valori tipici 0.05-0.20.
          BypassOpenFraction: float }

    /// Risultato di una singola cella (sezione assiale i, banda j, classe ferrula c)
    type CellResult =
        { I: int
          J: int
          C: int
          /// frazione di tubi della classe
          Frac: float
          /// lunghezza della ferrula della classe [m]
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
          /// Temperatura media dello spessore pesata sull'area (governa la
          /// dilatazione assiale del tubo) [K]
          TMetalWallAvg: float
          TWallBoil: float         // K
          DTsatWall: float
          DTDeposit: float
          DTMetalSat: float
          QCritLocal: float        // W/m² (CHF di fascio corretto per il titolo)
          DNBR: float
          InFerrule: bool }

    /// Aggregato per sezione assiale
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
          /// CR "efficace" visto dai tubi = portata nel campo tubi / vapore
          EffectiveCR: float
          /// frazione di vuoto del flusso discendente nel canale anulare
          BypassAlpha: float
          /// titolo con cui la miscela ricircolata rientra nel fascio
          XCarryUnder: float
          /// area del canale anulare aperto [m²/m]
          OpenAnnulus: float
          /// Numero di sezioni assiali che non ricevono portata sufficiente
          StarvedSlices: int
          Converged: bool }

    /// Regime di moto bifase in un riser verticale
    type FlowRegime =
        | Bubbly
        | DispersedBubble
        | Slug
        | Churn
        | Annular

    /// Verifica di una linea del circuito
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
          /// false = bocchello presente ma non collegato
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

    /// Criticita' rilevata dalla diagnostica, con la sua collocazione fisica.
    type Finding =
        { Severity: Severity
          /// area tematica: TERMICO / EBOLLIZIONE / MATERIALI / CIRCOLAZIONE / MECCANICA / GAS
          Area: string
          /// titolo breve
          Title: string
          /// valore calcolato, gia' formattato con l'unita'
          Value: string
          /// criterio o soglia di riferimento
          Limit: string
          /// collocazione fisica (z, y, banda, classe, componente)
          Where: string
          /// azione consigliata
          Action: string
          /// spiegazione estesa
          Detail: string }

    /// Analisi a piastre fisse: dilatazione impedita fra fascio e mantello
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

    // ==================================================================
    //  Stato di sollecitazione: Lame' + carico assiale da dilatazione
    //  impedita, per ogni zona assiale z e ogni altezza y
    // ==================================================================

    /// Stato tensionale in un punto radiale della parete
    type StressPoint =
        { /// "interna" | "media" | "esterna"
          Position: string
          R: float                 // m
          SigmaR: float            // Pa (compressione negativa)
          SigmaTheta: float        // Pa
          SigmaZ: float            // Pa (membranale + gradiente termico)
          SigmaVM: float           // Pa, von Mises
          SigmaTresca: float }     // Pa

    /// Membro strutturale del sistema a piastre fisse (un gruppo di tubi,
    /// il mantello, il tubo di contenimento del by-pass)
    type StressMember =
        { Label: string
          MaterialName: string
          /// numero di elementi rappresentati (tubi)
          Count: float
          /// area metallica totale del membro [m²]
          Area: float
          /// modulo elastico alla sua temperatura [Pa]
          E: float
          /// temperatura media equivalente [K]
          TEq: float
          /// dilatazione libera [m]
          FreeElongation: float
          /// forza assiale [N], POSITIVA = trazione
          Force: float
          /// tensione assiale membranale [Pa], positiva = trazione
          SigmaZ: float
          /// quota della forza dovuta al solo vincolo termico [Pa]
          SigmaZThermal: float
          /// quota dovuta al carico di estremita' di pressione [Pa]
          SigmaZPressure: float }

    /// Verifica di instabilita' (colonna + collasso per pressione esterna)
    type BucklingCheck =
        { Label: string
          MaterialName: string
          /// tensione assiale di compressione [Pa] (valore positivo)
          SigmaCompression: float
          Span: float
          RadiusGyration: float
          Slenderness: float
          E: float
          Sy: float
          SigmaAllow: float
          Utilisation: float
          /// pressione esterna netta [Pa]
          PExtNet: float
          /// collasso elastico (Bresse/Timoshenko) [Pa]
          PCrElastic: float
          /// collasso plastico (snervamento del cerchio) [Pa]
          PCrYield: float
          /// minimo dei due, con fattore di forma [Pa]
          PCollapse: float
          CollapseUtil: float
          Note: string }

    /// Stato tensionale completo di una cella (zona z, altezza y, classe)
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
          /// tensione assiale membranale (termica + pressione) [Pa]
          SigmaZMembrane: float
          SigmaZThermal: float
          SigmaZPressure: float
          Points: StressPoint list
          SigmaVMMax: float        // Pa
          /// posizione in cui si verifica il massimo
          WorstAt: string
          Sy: float                // Pa alla temperatura locale
          Utilisation: float }

    /// Verifica del LINER del by-pass a pressione differenziale.
    /// Il liner NON porta la pressione di processo: l'intercapedine esterna
    /// comunica con il lato a VALLE del fascio, quindi il salto che vede e'
    /// soltanto la perdita di carico fra ingresso e uscita dei tubi.
    type LinerCheck =
        { /// salto di riferimento = perdita di carico lato tubi [Pa]
          DpTubes: float
          /// salto di progetto adottato (fattore di maggiorazione applicato) [Pa]
          DpDesign: float
          Factor: float
          Od: float
          Id: float
          Thickness: float
          TEq: float               // K
          E: float
          Sy: float
          /// collasso elastico del cilindro lungo [Pa]
          PCrElastic: float
          /// collasso plastico circonferenziale [Pa]
          PCrYield: float
          PCollapse: float
          /// utilizzo diretto e con fattore di sicurezza 3 (ASME UG-28)
          Utilisation: float
          UtilisationCode: float
          /// tensione circonferenziale se il salto agisce dall'interno [Pa]
          HoopStress: float
          Notes: string list }

    type StressResult =
        { /// spostamento assiale comune imposto dalle piastre [m]
          CommonDelta: float
          /// carico di estremita' da pressione [N] (trazione)
          PressureEndLoad: float
          AreaFluidShell: float
          AreaFluidTube: float
          PShell: float
          PTubeMean: float
          Members: StressMember list
          Cells: StressCell list
          Bucklings: BucklingCheck list
          /// forza che il liner svilupperebbe se fosse vincolato [N]
          LinerRestrainedForce: float
          LinerTEq: float
          LinerFreeElongation: float
          /// verifica del liner a pressione differenziale
          Liner: LinerCheck
          Notes: string list }

    // ==================================================================
    //  Valvola a farfalla del by-pass
    // ==================================================================
    type ValvePoint =
        { /// angolo di APERTURA [gradi] (0 = chiusa, 90 = tutta aperta)
          OpenDeg: float
          /// angolo di CHIUSURA [gradi] usato da Idelchik
          ClosureDeg: float
          Zeta: float
          /// frazione di portata deviata
          Fraction: float
          MassFlowBypass: float    // kg/s
          RhoValve: float          // kg/m³
          VelPipe: float           // m/s nel liner
          VelThroat: float         // m/s nella vena contratta
          Mach: float
          RhoV2Throat: float       // Pa
          DpValve: float           // Pa
          /// zeta dalla teoria del disco piano (per confronto con la tabella)
          ZetaTheory: float
          /// Cv e Kv geometrici corrispondenti a zeta
          Cv: float
          Kv: float
          /// Kv richiesto dal servizio a quella portata e caduta
          KvRequired: float
          /// rapporto di caduta dp/p1 (choking se > ~0.7)
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
          /// vincoli che determinano gli estremi: (etichetta, angolo, motivo)
          MinDrivers: (string * float * string) list
          MaxDrivers: (string * float * string) list
          Diameter: float
          AtOutlet: bool }

    /// Confronto fra modelli di flusso termico critico nella cella peggiore
    type ChfComparison =
        { Model: string
          QCrit: float             // W/m2
          DNBR: float
          Note: string }

    /// Voce dello studio di incertezza sulle correlazioni
    type SensitivityItem =
        { Group: string            // "gas" | "ebollizione" | "miscelazione" | ...
          Name: string
          HGas: float
          HBoil: float
          U: float
          QFlux: float             // W/m2 con U ricalcolato a pari LMTD locale
          TMetalIn: float          // K
          Delta: float }           // scostamento % rispetto alla scelta di base

    /// Confronto pulito / sporco su entrambi i lati, nella cella di picco
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

    /// Punto della scansione di maldistribuzione: UN SOLO tubo riceve piu'
    /// portata, il lato mantello resta quello di progetto.
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

    /// Transitori e protezioni
    type TransientResult =
        { /// costante di tempo termica del metallo del tubo [s]
          TauMetal: float
          /// inventario d'acqua liquida a mantello [kg]
          WaterInventory: float
          /// inventario d'acqua liquida nel corpo cilindrico al livello normale [kg]
          DrumInventory: float
          /// tempo di evaporazione col solo inventario del mantello [s]
          TimeToDryoutIsolated: float
          /// volume libero a mantello [m³]
          ShellFreeVolume: float
          /// frazione di vuoto media usata
          AlphaMean: float
          /// tempo di evaporazione a secco con potenza piena [s]
          TimeToDryout: float
          /// temperatura di equilibrio del metallo dopo dry-out [K]
          TMetalDryout: float
          /// tempo per raggiungerla [s]
          TimeToOverheat: float
          /// portata di reintegro necessaria per compensare [kg/s]
          MakeupRate: float
          Notes: string list }

    /// Punto della curva di carico
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

    /// Dilatazione assiale di un elemento
    type ExpansionResult =
        { Label: string
          /// temperatura media equivalente [K]: alpha(Teq)*(Teq-Troom)*L = DeltaL
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
          /// diametro minimo perche' esista il regime a bolle (Taitel-Dukler)
          DMinBubbly: float
          RhoV2: float
          Ok: bool
          Note: string }

    /// Sintesi per classe di lunghezza della ferrula
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
