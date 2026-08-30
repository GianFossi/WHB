module Whb.Cli.Program

open System
open System.IO
open System.Text.Json
open System.Globalization
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Whb.Core.Options
open Whb.Cli

let private tryParseFloat (text: string) =
    match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, value -> Some value
    | _ -> None

let private floatGrid (a: float) (b: float) (step: float) =
    let dx = abs step
    if dx <= 0.0 then invalidArg "step" "Step must be positive."
    let lo = min a b
    let hi = max a b
    [ let mutable x = lo
      while x <= hi + 1e-9 * dx do
          yield x
          x <- x + dx ]

let private gasCorrelation (name: string) =
    match name.ToLowerInvariant() with
    | "dittus-boelter" | "dittusboelter" | "db" -> GasSide.DittusBoelter
    | "sieder-tate" | "siedertate" | "st" -> GasSide.SiederTate
    | "colburn" -> GasSide.Colburn
    | "petukhov" | "petukhov-kirillov" -> GasSide.PetukhovKirillov
    | "hausen" -> GasSide.Hausen
    | _ -> GasSide.Gnielinski
let private boilCorrelation (name: string) =
    match name.ToLowerInvariant() with
    | "cooper" -> WaterSide.Cooper
    | "rohsenow" -> WaterSide.Rohsenow
    | "gorenflo" -> WaterSide.Gorenflo
    | "cornwell" | "cornwell-houston" -> WaterSide.CornwellHouston
    | _ -> WaterSide.Mostinski
let private flowBoilingModel (name: string) =
    match name.Trim().ToLowerInvariant() with
    | "kandlikar" -> WaterSide.KandlikarMax
    | _ -> WaterSide.ChenSuperposition
/// CHF model used for the cell-by-cell DNBR field. A bare number is read as a practical
/// design limit in kW/m2, which is how the criterion is usually written on a datasheet.
let private chfModel (name: string) (fallback: WaterSide.ChfModel) =
    match name.Trim().ToLowerInvariant() with
    | "" -> fallback
    | "palen" | "palen-bundle" | "fascio" -> WaterSide.PalenBundle
    | "lienhard" | "lienhard-eichhorn" | "crossflow" -> WaterSide.LienhardEichhornCrossflow
    | "zuber" | "zuber-titolo" -> WaterSide.ZuberQuality
    | other ->
        match Double.TryParse(other, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, kw when kw > 0.0 -> WaterSide.PracticalLimit(kw * 1000.0)
        | _ -> fallback
let private voidModel (name: string) =
    match name.ToLowerInvariant() with
    | "omogeneo" | "homogeneous" -> TwoPhase.Homogeneous
    | "chisholm" -> TwoPhase.ChisholmSlip
    | "smith" -> TwoPhase.Smith
    | _ -> TwoPhase.ZuberFindlay
let private frictionModel (name: string) =
    match name.ToLowerInvariant() with
    | "omogeneo" | "homogeneous" -> TwoPhase.HomogeneousFriction
    | "lockhart" | "lockhart-martinelli" -> TwoPhase.LockhartMartinelli
    | "chisholm" -> TwoPhase.ChisholmB
    | _ -> TwoPhase.Friedel
let private gasModelUsesRealGas (name: string) (fallback: bool) =
    match name.Trim().ToLowerInvariant() with
    | "" -> fallback
    | "ideale" | "ideal" | "ideal-gas" | "ideal gas" -> false
    | "viriale" | "virial" | "reale" | "real" | "real-gas" | "real gas" | "realistico" | "realistic" -> true
    | _ -> fallback
let private clausMode (name: string) =
    match name.Trim().ToLowerInvariant() with
    | "equilibrium" | "equilibrio" -> Claus.Equilibrium
    | "kinetic" | "cinetico" | "cinetica" -> Claus.Kinetic
    | _ -> Claus.Frozen
let private clausKineticsAt (r: JsonElement) (prefix: string) (fallback: Claus.KineticParameters) =
    let d = Claus.sanitizeKineticParameters fallback
    let path key = prefix + "." + key
    Claus.sanitizeKineticParameters
        { SeverityFactor = Json.f r (path "fattore_severita") d.SeverityFactor
          TauFactor = Json.f r (path "fattore_tau") d.TauFactor
          SubSteps = Json.i r (path "sottopassi") d.SubSteps
          Claus =
            { PreExponential = Json.f r (path "claus_a_1s") d.Claus.PreExponential
              ActivationEnergy = Json.f r (path "claus_ea_kjmol") (d.Claus.ActivationEnergy / 1000.0) * 1000.0 }
          CosHydrolysis =
            { PreExponential = Json.f r (path "cos_a_1s") d.CosHydrolysis.PreExponential
              ActivationEnergy = Json.f r (path "cos_ea_kjmol") (d.CosHydrolysis.ActivationEnergy / 1000.0) * 1000.0 }
          Cs2Hydrolysis =
            { PreExponential = Json.f r (path "cs2_a_1s") d.Cs2Hydrolysis.PreExponential
              ActivationEnergy = Json.f r (path "cs2_ea_kjmol") (d.Cs2Hydrolysis.ActivationEnergy / 1000.0) * 1000.0 } }
let private sulphurFeedOfGas (g: GasStream) : SulphurCondenser.Feed =
    { Composition = g.Composition
      MassFlow = g.MassFlow
      TIn = g.TIn
      PIn = g.PIn
      Z = g.Z
      ShiftMode = g.ShiftMode
      ClausMode = g.ClausMode
      ClausKinetics = g.ClausKinetics
      MixingRule = g.MixingRule
      RealGas = g.RealGas }
let private loadGasStream (r: JsonElement) (prefix: string) (fallback: GasStream) =
    let path key = prefix + "." + key
    { Composition = Json.compositionAt r (path "composizione") fallback.Composition
      MassFlow = Json.f r (path "portata_kgs") fallback.MassFlow * Json.f r (path "maggiorazione") 1.0
      TIn = cToK (Json.f r (path "t_ingresso_C") (kToC fallback.TIn))
      PIn = barToPa (Json.f r (path "p_ingresso_bara") (paToBar fallback.PIn))
      Z = Json.f r (path "z") fallback.Z
      FoulingIn = Json.f r (path "fouling_m2KW") fallback.FoulingIn
      EpsWall = Json.f r (path "emissivita_parete") fallback.EpsWall
      Radiation = Json.b r (path "irraggiamento") fallback.Radiation
      EntranceC = Json.f r (path "coeff_imbocco") fallback.EntranceC
      Correlation = gasCorrelation (Json.s r (path "correlazione") "gnielinski")
      ShiftMode =
        match (Json.s r (path "shift") "congelata").ToLowerInvariant() with
        | "equilibrio" -> Shift.EquilibriumAbove(cToK (Json.f r (path "shift_t_freeze_C") 700.0))
        | "parziale" ->
            Shift.FractionalApproach(Json.f r (path "shift_frazione") 0.3,
                                     cToK (Json.f r (path "shift_t_freeze_C") 700.0))
        | _ -> Shift.Frozen
      ClausMode = clausMode (Json.s r (path "modello_claus") "frozen")
      ClausKinetics = clausKineticsAt r (path "claus_cinetica") fallback.ClausKinetics
      MixingRule =
        match (Json.s r (path "miscelazione") "wilke").ToLowerInvariant() with
        | "molare" | "molar" -> GasProps.MolarAverage
        | _ -> GasProps.Wilke
      RealGas =
        gasModelUsesRealGas
            (Json.s r (path "modello_gas") "")
            (Json.b r (path "gas_reale") fallback.RealGas) }
let private insulK (name: string) =
    match name.ToLowerInvariant() with
    | "fibra" | "ceramic" -> Materials.Refractory.ceramicFibre
    | "denso" | "dense" -> Materials.Refractory.castableDense
    | "leggero" | "castable" -> Materials.Refractory.castableLight
    | _ -> Materials.Refractory.saffilPaper
let loadCase (path: string) : DesignCase =
    let d = Defaults.referenceCase
    use fs = File.OpenRead path
    use doc = JsonDocument.Parse(fs, JsonDocumentOptions(AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip))
    let r = doc.RootElement
    let t = d.Tube
    let tube =
        { Di = Json.f r "tubi.di_mm" (t.Di * 1000.0) / 1000.0
          Do = Json.f r "tubi.do_mm" (t.Do * 1000.0) / 1000.0
          Length = Json.f r "tubi.lunghezza_m" t.Length
          NTubes = Json.i r "tubi.numero" t.NTubes
          Pitch = Json.f r "tubi.passo_mm" (t.Pitch * 1000.0) / 1000.0
          Staggered = Json.b r "tubi.sfalsato" t.Staggered
          ShellId = Json.f r "tubi.mantello_id_mm" (t.ShellId * 1000.0) / 1000.0
          Otl = Json.f r "tubi.otl_mm" (t.Otl * 1000.0) / 1000.0
          Itl = Json.f r "tubi.itl_mm" (t.Itl * 1000.0) / 1000.0
          BaffleOd = Json.f r "tubi.diaframma_od_mm" (t.BaffleOd * 1000.0) / 1000.0
          Roughness = Json.f r "tubi.rugosita_mm" (t.Roughness * 1000.0) / 1000.0 }
    let fr = d.Ferrule
    let ferruleLengths =
        match Json.lengths r "ferrula.lunghezze" with
        | Some l -> l
        | None -> [ (1.0, Json.f r "ferrula.lunghezza_mm" 200.0 / 1000.0) ]
    let ferrule =
        { Enabled = Json.b r "ferrula.presente" fr.Enabled
          Lengths = ferruleLengths
          Bore = Json.f r "ferrula.bore_mm" (fr.Bore * 1000.0) / 1000.0
          SleeveOd = Json.f r "ferrula.manicotto_od_mm" (fr.SleeveOd * 1000.0) / 1000.0
          SleeveK = (Materials.byName (Json.s r "ferrula.manicotto_materiale" "800")).K
          InsulK = insulK (Json.s r "ferrula.isolante" "saffil") }
    let g = d.Gas
    let gas = loadGasStream r "gas" g
    let wt = d.Water
    let water =
        { DrumPressure = barToPa (Json.f r "vapore.pressione_bara" (paToBar wt.DrumPressure))
          FoulingOut = Json.f r "vapore.fouling_m2KW" wt.FoulingOut
          RoughnessUm = Json.f r "vapore.rugosita_um" wt.RoughnessUm
          BundleFactor = Json.f r "vapore.fattore_fascio" wt.BundleFactor
          Correlation = boilCorrelation (Json.s r "vapore.correlazione" "mostinski")
          FlowBoiling = flowBoilingModel (Json.s r "vapore.ebollizione_flusso" "chen")
          Csf = Json.f r "vapore.csf" wt.Csf
          ChfModel = chfModel (Json.s r "vapore.modello_chf" "") wt.ChfModel
          MinDNBR = Json.f r "vapore.dnbr_min" wt.MinDNBR
          TFeed = cToK (Json.f r "vapore.t_alimento_C" (kToC wt.TFeed)) }
    let l = d.Loop
    let loop =
        { DzDrumWhb = Json.f r "circuito.dz_drum_whb_m" l.DzDrumWhb
          DrumLevelOffset = Json.f r "circuito.offset_livello_m" l.DrumLevelOffset
          Downcomers = Json.lines r "circuito.downcomer" l.Downcomers
          Risers = Json.lines r "circuito.riser" l.Risers
          DrumInternalsDp = Json.f r "circuito.interne_drum_mbar" (l.DrumInternalsDp / 100.0) * 100.0
          Drum =
            { l.Drum with
                Enabled = Json.b r "drum.modello_attivo" l.Drum.Enabled
                ShellId = Json.f r "drum.id_mm" (l.Drum.ShellId * 1000.0) / 1000.0
                Length = Json.f r "drum.lunghezza_tt_mm" (l.Drum.Length * 1000.0) / 1000.0
                NormalLevel = Json.f r "drum.livello_normale_mm" (l.Drum.NormalLevel * 1000.0) / 1000.0
                ConveyorCount = Json.i r "drum.calm_box_n" (Json.i r "drum.convogliatori" l.Drum.ConveyorCount)
                CalmBoxRisersPerBox = Json.i r "drum.calm_box_risers_per_box" l.Drum.CalmBoxRisersPerBox
                ConvDuctArea = Json.f r "drum.calm_box_area_m2" (Json.f r "drum.canale_area_m2" l.Drum.ConvDuctArea)
                ConvLength = Json.f r "drum.calm_box_lunghezza_m" (Json.f r "drum.canale_lunghezza_m" l.Drum.ConvLength)
                ConvHydDia = Json.f r "drum.calm_box_dh_m" (Json.f r "drum.canale_dh_m" l.Drum.ConvHydDia)
                ConvBendAngle = Json.f r "drum.canale_curvatura_gradi" l.Drum.ConvBendAngle
                ConvOutletArea = Json.f r "drum.calm_box_top_opening_m2" (Json.f r "drum.finestra_area_m2" l.Drum.ConvOutletArea)
                ConvOutletAboveLevel = Json.b r "drum.calm_box_opening_above_level" (Json.b r "drum.scarico_sopra_livello" l.Drum.ConvOutletAboveLevel)
                ConvExtraK = Json.f r "drum.calm_box_k_extra" (Json.f r "drum.canale_k_extra" l.Drum.ConvExtraK)
                CalmBoxWaterFallHeight = Json.f r "drum.calm_box_waterfall_m" l.Drum.CalmBoxWaterFallHeight
                DowncomerEntryArea = Json.f r "drum.downcomer_entry_area_m2" l.Drum.DowncomerEntryArea
                DowncomerVortexBreakerK = Json.f r "drum.downcomer_vortex_breaker_k" l.Drum.DowncomerVortexBreakerK
                DemisterArea = Json.f r "drum.demister_area_m2" l.Drum.DemisterArea
                DemisterK = Json.f r "drum.demister_k" l.Drum.DemisterK
                ChimneyCount = Json.i r "drum.camini_numero" l.Drum.ChimneyCount
                ChimneyId = Json.f r "drum.camini_id_mm" (l.Drum.ChimneyId * 1000.0) / 1000.0
                ExternalSteam = Json.f r "drum.vapore_esterno_kgs" l.Drum.ExternalSteam
                VendorDpCirculation =
                  (let v = Json.f r "drum.dp_costruttore_mbar" -1.0
                   if v >= 0.0 then Some(v * 100.0) else None) }
          VoidModel = voidModel (Json.s r "circuito.modello_vuoto" "zuber")
          FrictionModel = frictionModel (Json.s r "circuito.modello_attrito" "friedel") }
    let sc0 : SulphurCondenser.Spec = d.SulphurCondenser
    let scGas0 =
        { gas with
            Composition = sc0.Feed.Composition
            MassFlow = sc0.Feed.MassFlow
            TIn = sc0.Feed.TIn
            PIn = sc0.Feed.PIn
            Z = sc0.Feed.Z
            ShiftMode = sc0.Feed.ShiftMode
            ClausMode = sc0.Feed.ClausMode
            ClausKinetics = sc0.Feed.ClausKinetics
            MixingRule = sc0.Feed.MixingRule
            RealGas = sc0.Feed.RealGas }
    let scGas = loadGasStream r "condensatore_zolfo.gas_ingresso" scGas0
    let sulphurCondenser : SulphurCondenser.Spec =
        { Enabled = Json.b r "condensatore_zolfo.presente" sc0.Enabled
          UseWhbOutlet = Json.b r "condensatore_zolfo.usa_uscita_whb" sc0.UseWhbOutlet
          Sections = Json.i r "condensatore_zolfo.sezioni" sc0.Sections
          ResidenceTime = Json.f r "condensatore_zolfo.tempo_residenza_s" sc0.ResidenceTime
          DpTotal = Json.f r "condensatore_zolfo.dp_mbar" (sc0.DpTotal / 100.0) * 100.0
          TOutTarget = cToK (Json.f r "condensatore_zolfo.t_uscita_target_C" (kToC sc0.TOutTarget))
          TWall = cToK (Json.f r "condensatore_zolfo.t_parete_C" (kToC sc0.TWall))
          TCoolant = cToK (Json.f r "condensatore_zolfo.t_refrigerante_C" (kToC sc0.TCoolant))
          UAssumed = Json.f r "condensatore_zolfo.u_assunto_Wm2K" sc0.UAssumed
          Feed = sulphurFeedOfGas scGas }
    { Name = Json.s r "nome" d.Name
      Tube = tube
      Ferrule = ferrule
      Gas = gas
      Water = water
      Loop = loop
      Material = Materials.byName (Json.s r "materiale" "T11")
      FerruleMaterial = Materials.byName (Json.s r "ferrula.manicotto_materiale" "800")
      NZ = Json.i r "sezioni_assiali" d.NZ
      NY = Json.i r "bande_verticali" d.NY
      AxialRefine = Json.f r "infittimento_imbocco" d.AxialRefine
      RiserNozzleCount = Json.i r "bocchelli.n_riser" d.RiserNozzleCount
      DowncomerNozzleCount = Json.i r "bocchelli.n_downcomer" d.DowncomerNozzleCount
      TargetDowncomerVelocity = Json.f r "bocchelli.v_downcomer_ms" d.TargetDowncomerVelocity
      MaxRhoV2Riser = Json.f r "bocchelli.rhov2_max_riser" d.MaxRhoV2Riser
      MaxRhoV2Downcomer = Json.f r "bocchelli.rhov2_max_downcomer" d.MaxRhoV2Downcomer
      ShellThickness = Json.f r "tubi.mantello_wt_mm" (d.ShellThickness * 1000.0) / 1000.0
      ShellMaterial = Materials.byName (Json.s r "materiale_mantello" "SA-516")
      UnsupportedSpan = Json.f r "campata_non_supportata_m" d.UnsupportedSpan
      BaffleSpans =
        (match Json.tryArray r "campate_diaframmi_mm" with
         | Some xs when not xs.IsEmpty -> xs |> List.map (fun v -> v / 1000.0)
         | _ -> d.BaffleSpans)
      BaffleThickness = Json.f r "diaframmi_thk_mm" (d.BaffleThickness * 1000.0) / 1000.0
      TubeLayout =
        (match (Json.s r "reticolo" "60").ToLowerInvariant() with
         | "30" | "triangolare" -> Vibration.Triangular30
         | "90" | "quadrato" -> Vibration.Square90
         | "45" | "quadrato_ruotato" -> Vibration.RotatedSquare45
         | _ -> Vibration.RotatedTriangular60)
      VibrationDamping = Json.f r "smorzamento_log" d.VibrationDamping
      TubesheetJoint =
        (match (Json.s r "giunto_tubo_piastra" "piena_penetrazione").ToLowerInvariant() with
         | "crevice_free" | "crevice-free" | "appoggio" -> Vibration.CreviceFreeWeld
         | _ -> Vibration.FullPenetrationWeld)
      AssemblyTemperature = cToK (Json.f r "t_montaggio_C" (kToC d.AssemblyTemperature))
      ShellInsulationU = Json.f r "coibentazione_u" d.ShellInsulationU
      Bypass =
        { Enabled = Json.b r "bypass.presente" d.Bypass.Enabled
          Fraction =
            (let f = Json.f r "bypass.frazione" -1.0
             if f >= 0.0 then Some f else None)
          TargetMixOut = cToK (Json.f r "bypass.t_uscita_target_C" (kToC d.Bypass.TargetMixOut))
          LinerId = Json.f r "bypass.liner_id_mm" (d.Bypass.LinerId * 1000.0) / 1000.0
          LinerOd = Json.f r "bypass.liner_od_mm" (d.Bypass.LinerOd * 1000.0) / 1000.0
          LinerMaterial = Materials.byName (Json.s r "bypass.liner_materiale" "601")
          InsulOd = Json.f r "bypass.isolante_od_mm" (d.Bypass.InsulOd * 1000.0) / 1000.0
          InsulK = insulK (Json.s r "bypass.isolante" "saffil")
          PipeOd = Json.f r "bypass.tubo_od_mm" (d.Bypass.PipeOd * 1000.0) / 1000.0
          PipeMaterial = Materials.byName (Json.s r "bypass.tubo_materiale" "SA-192")
          FoulingIn = Json.f r "bypass.fouling_m2KW" d.Bypass.FoulingIn
          ExtraK = Json.f r "bypass.k_localizzati" d.Bypass.ExtraK
          ValveAtOutlet = Json.b r "bypass.valvola_a_valle" d.Bypass.ValveAtOutlet
          ValveOpenDeg =
            (let a = Json.f r "bypass.valvola_apertura_gradi" -1.0
             if a >= 0.0 then Some a else None)
          MinOpenDeg = Json.f r "bypass.valvola_apertura_min_gradi" d.Bypass.MinOpenDeg
          MaxOpenDeg = Json.f r "bypass.valvola_apertura_max_gradi" d.Bypass.MaxOpenDeg
          TMixMin = cToK (Json.f r "bypass.t_miscelata_min_C" (kToC d.Bypass.TMixMin))
          TMixMax = cToK (Json.f r "bypass.t_miscelata_max_C" (kToC d.Bypass.TMixMax))
          MinPurgeVel = Json.f r "bypass.v_lavaggio_min_ms" d.Bypass.MinPurgeVel
          MaxRhoV2Valve = Json.f r "bypass.rhov2_max_valvola" d.Bypass.MaxRhoV2Valve }
      SulphurCondenser = sulphurCondenser
      AllowInternalRecirculation = Json.b r "ricircolo_interno" d.AllowInternalRecirculation
      BypassOpenFraction = Json.f r "bypass_frazione_aperta" d.BypassOpenFraction }

type private MetricInfo =
    { Key: ConstraintModel.ConstraintValueKey
      Aliases: string list
      Name: string
      Domain: ConstraintModel.ConstraintDomain
      Unit: string
      FromCli: float -> float
      ToCli: float -> float }

type private VariableInfo =
    { Key: Optimize.VariableKey
      Aliases: string list
      Name: string
      Unit: string
      Current: DesignCase -> float
      FromCli: float -> float
      ToCli: float -> float }

let private idFloat x = x
let private lowerText (text: string) = text.Trim().ToLowerInvariant()
let private metricInfos =
    [ { Key = ConstraintModel.Duty
        Aliases = [ "Duty"; "duty"; "potenza"; "potenza_mw"; "duty_mw" ]
        Name = "Duty"
        Domain = ConstraintModel.Process
        Unit = "MW"
        FromCli = fun x -> x * 1e6
        ToCli = fun x -> x / 1e6 }
      { Key = ConstraintModel.SteamProduction
        Aliases = [ "SteamProduction"; "steamproduction"; "vapore"; "vapore_th"; "steam_tph" ]
        Name = "Steam production"
        Domain = ConstraintModel.Process
        Unit = "t/h"
        FromCli = fun x -> x / 3.6
        ToCli = fun x -> x * 3.6 }
      { Key = ConstraintModel.GasOutletTemperature
        Aliases = [ "GasOutletTemperature"; "gasoutlettemperature"; "t_gas_uscita"; "t_gas_uscita_c"; "t_gas_out_c" ]
        Name = "Gas outlet temperature"
        Domain = ConstraintModel.Process
        Unit = "degC"
        FromCli = cToK
        ToCli = kToC }
      { Key = ConstraintModel.GasPressureDrop
        Aliases = [ "GasPressureDrop"; "gaspressuredrop"; "dp_gas"; "dp_gas_mbar" ]
        Name = "Gas pressure drop"
        Domain = ConstraintModel.Hydraulic
        Unit = "mbar"
        FromCli = fun x -> x * 100.0
        ToCli = fun x -> x / 100.0 }
      { Key = ConstraintModel.MaxHeatFlux
        Aliases = [ "MaxHeatFlux"; "maxheatflux"; "q_max"; "q_max_kwm2" ]
        Name = "Maximum heat flux"
        Domain = ConstraintModel.Thermal
        Unit = "kW/m2"
        FromCli = fun x -> x * 1000.0
        ToCli = fun x -> x / 1000.0 }
      { Key = ConstraintModel.MinDNBR
        Aliases = [ "MinDNBR"; "mindnbr"; "dnbr"; "dnbr_min" ]
        Name = "Minimum DNBR"
        Domain = ConstraintModel.Thermal
        Unit = "-"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.MinCirculationRatio
        Aliases = [ "MinCirculationRatio"; "mincirculationratio"; "cr"; "circulation_ratio"; "rapporto_circolazione" ]
        Name = "Minimum circulation ratio"
        Domain = ConstraintModel.Hydraulic
        Unit = "-"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.MaxFeiRatio
        Aliases = [ "MaxFeiRatio"; "maxfeiratio"; "fei"; "fei_max"; "v_vcrit" ]
        Name = "Maximum FIV ratio"
        Domain = ConstraintModel.Vibration
        Unit = "-"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.MaxTubeMetalTemperature
        Aliases = [ "MaxTubeMetalTemperature"; "maxtubemetaltemperature"; "t_metallo_tubi"; "t_metallo_tubi_max_c" ]
        Name = "Maximum tube metal temperature"
        Domain = ConstraintModel.Thermal
        Unit = "degC"
        FromCli = cToK
        ToCli = kToC }
      { Key = ConstraintModel.MaxBypassLinerTemperature
        Aliases = [ "MaxBypassLinerTemperature"; "maxbypasslinertemperature"; "t_liner_bypass"; "t_liner_bypass_max_c" ]
        Name = "Maximum bypass liner temperature"
        Domain = ConstraintModel.Mechanical
        Unit = "degC"
        FromCli = cToK
        ToCli = kToC }
      { Key = ConstraintModel.MaxBypassPipeTemperature
        Aliases = [ "MaxBypassPipeTemperature"; "maxbypasspipetemperature"; "t_tubo_bypass"; "t_tubo_bypass_max_c" ]
        Name = "Maximum bypass pipe temperature"
        Domain = ConstraintModel.Mechanical
        Unit = "degC"
        FromCli = cToK
        ToCli = kToC }
      { Key = ConstraintModel.DowncomerSubcoolingMargin
        Aliases = [ "DowncomerSubcoolingMargin"; "downcomersubcoolingmargin"; "margine_sottoraffreddamento"; "margine_sottoraffreddamento_k" ]
        Name = "Downcomer subcooling margin"
        Domain = ConstraintModel.Hydraulic
        Unit = "K"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.CoupledResidual
        Aliases = [ "CoupledResidual"; "coupledresidual"; "residuo_accoppiato" ]
        Name = "Coupled residual"
        Domain = ConstraintModel.Numerical
        Unit = "-"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.NonConvergedCells
        Aliases = [ "NonConvergedCells"; "nonconvergedcells"; "celle_non_convergenti" ]
        Name = "Non-converged cells"
        Domain = ConstraintModel.Numerical
        Unit = "-"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.WhbWeightKg
        Aliases = [ "WhbWeightKg"; "whbweightkg"; "peso_whb"; "peso_whb_kg" ]
        Name = "Estimated WHB weight"
        Domain = ConstraintModel.Weight
        Unit = "kg"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.ExternalPipingWeightKg
        Aliases = [ "ExternalPipingWeightKg"; "externalpipingweightkg"; "peso_piping"; "peso_piping_kg" ]
        Name = "Estimated external piping weight"
        Domain = ConstraintModel.Weight
        Unit = "kg"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.WhbOuterDiameter
        Aliases = [ "WhbOuterDiameter"; "whbouterdiameter"; "diametro_esterno_whb"; "diametro_esterno_whb_mm" ]
        Name = "WHB outer diameter"
        Domain = ConstraintModel.Envelope
        Unit = "mm"
        FromCli = fun x -> x / 1000.0
        ToCli = fun x -> x * 1000.0 }
      { Key = ConstraintModel.DrumOuterDiameter
        Aliases = [ "DrumOuterDiameter"; "drumouterdiameter"; "diametro_esterno_drum"; "diametro_esterno_drum_mm" ]
        Name = "Steam drum outer diameter"
        Domain = ConstraintModel.Envelope
        Unit = "mm"
        FromCli = fun x -> x / 1000.0
        ToCli = fun x -> x * 1000.0 }
      { Key = ConstraintModel.WhbIdTimesLength
        Aliases = [ "WhbIdTimesLength"; "whbidtimeslength"; "ingombro_whb"; "ingombro_whb_m2"; "whb_id_x_l" ]
        Name = "WHB ID x L"
        Domain = ConstraintModel.Envelope
        Unit = "m2"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.DrumIdTimesLength
        Aliases = [ "DrumIdTimesLength"; "drumidtimeslength"; "ingombro_drum"; "ingombro_drum_m2"; "drum_id_x_l" ]
        Name = "Steam drum ID x L"
        Domain = ConstraintModel.Envelope
        Unit = "m2"
        FromCli = idFloat
        ToCli = idFloat }
      { Key = ConstraintModel.DrumCenterlineHeight
        Aliases = [ "DrumCenterlineHeight"; "drumcenterlineheight"; "quota_drum"; "quota_drum_m" ]
        Name = "Steam drum centerline elevation"
        Domain = ConstraintModel.Geometry
        Unit = "m"
        FromCli = idFloat
        ToCli = idFloat } ]

let private variableInfos =
    [ { Key = Optimize.FerruleLengthMm
        Aliases = [ "FerruleLengthMm"; "ferrulelengthmm"; "lunghezza_ferrula"; "lunghezza_ferrula_mm" ]
        Name = "lunghezza ferrula"
        Unit = "mm"
        Current = fun c -> (c.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)) * 1000.0
        FromCli = idFloat
        ToCli = idFloat }
      { Key = Optimize.TubeLengthM
        Aliases = [ "TubeLengthM"; "tubelengthm"; "lunghezza_tubi"; "lunghezza_tubi_m" ]
        Name = "lunghezza tubi"
        Unit = "m"
        Current = fun c -> c.Tube.Length
        FromCli = idFloat
        ToCli = idFloat }
      { Key = Optimize.TubeCount
        Aliases = [ "TubeCount"; "tubecount"; "numero_tubi" ]
        Name = "numero tubi"
        Unit = "-"
        Current = fun c -> float c.Tube.NTubes
        FromCli = idFloat
        ToCli = idFloat }
      { Key = Optimize.TubeOuterDiameterM
        Aliases = [ "TubeOuterDiameterM"; "tubeouterdiameterm"; "diametro_esterno_tubi"; "do_tubi_mm"; "do_mm" ]
        Name = "diametro esterno tubi"
        Unit = "mm"
        Current = fun c -> c.Tube.Do * 1000.0
        FromCli = fun x -> x / 1000.0
        ToCli = fun x -> x * 1000.0 }
      { Key = Optimize.TubePitchM
        Aliases = [ "TubePitchM"; "tubepitchm"; "passo_tubi"; "passo_tubi_mm" ]
        Name = "passo tubi"
        Unit = "mm"
        Current = fun c -> c.Tube.Pitch * 1000.0
        FromCli = fun x -> x / 1000.0
        ToCli = fun x -> x * 1000.0 }
      { Key = Optimize.ShellInnerDiameterM
        Aliases = [ "ShellInnerDiameterM"; "shellinnerdiameterm"; "diametro_interno_mantello"; "mantello_id_mm" ]
        Name = "diametro interno mantello"
        Unit = "mm"
        Current = fun c -> c.Tube.ShellId * 1000.0
        FromCli = fun x -> x / 1000.0
        ToCli = fun x -> x * 1000.0 }
      { Key = Optimize.DrumCenterlineHeightM
        Aliases = [ "DrumCenterlineHeightM"; "drumcenterlineheightm"; "quota_drum"; "quota_drum_m" ]
        Name = "quota drum"
        Unit = "m"
        Current = fun c -> c.Loop.DzDrumWhb
        FromCli = idFloat
        ToCli = idFloat } ]

let private metricInfo key = metricInfos |> List.find (fun info -> info.Key = key)
let private tryMetricInfo (name: string) =
    let target = lowerText name
    metricInfos
    |> List.tryFind (fun info -> info.Aliases |> List.exists (fun alias -> lowerText alias = target))

let private variableInfo key = variableInfos |> List.find (fun info -> info.Key = key)
let private tryVariableInfo (name: string) =
    let target = lowerText name
    variableInfos
    |> List.tryFind (fun info -> info.Aliases |> List.exists (fun alias -> lowerText alias = target))

let private createRunSettings (options: Options.ProjectOptions) (correlationValidityWarnings: bool) : Design.RunSettings =
    { BypassMapMode = options.Calculation.BypassMapMode
      BypassTargetToleranceK = options.Calculation.BypassTargetToleranceK
      GasPropertyCache = options.Calculation.GasPropertyCache
      CorrelationValidityWarnings = correlationValidityWarnings
      Parallelism = max 1 options.Calculation.Parallelism }

let private readCaseRoot (casePath: string option) (fallback: 'T) (reader: JsonElement -> 'T) =
    match casePath with
    | Some path ->
        use fs = File.OpenRead path
        use doc =
            JsonDocument.Parse(
                fs,
                JsonDocumentOptions(AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip))
        reader doc.RootElement
    | None -> fallback

let private tryArrayAtPath (root: JsonElement) (paths: string list) =
    paths |> List.tryPick (fun path -> Json.tryArrayElements root path)

let private tryNumberArrayAtPath (root: JsonElement) (paths: string list) =
    paths |> List.tryPick (fun path -> Json.tryArray root path)

let private loadCaseSpecAt (item: JsonElement) =
    let notes =
        match Json.tryStringArrayAt item "note" with
        | Some xs -> xs
        | None ->
            match Json.trySAt item "nota" with
            | Some note -> [ note ]
            | None -> []
    { LoadCases.baseCase (Json.sAt item "nome" "base")
        with GasMassFlow = Json.tryFAt item "portata_gas_kgs"
             GasMassFlowFactor =
                match Json.tryFAt item "fattore_portata_gas", Json.tryFAt item "carico" with
                | Some value, _ -> Some value
                | None, Some value -> Some value
                | _ -> None
             GasInletTemperature =
                match Json.tryFAt item "t_ingresso_gas_C", Json.tryFAt item "t_gas_ingresso_C" with
                | Some value, _ -> Some(cToK value)
                | None, Some value -> Some(cToK value)
                | _ -> None
             DrumPressure =
                match Json.tryFAt item "pressione_vapore_bara", Json.tryFAt item "pressione_drum_bara" with
                | Some value, _ -> Some(barToPa value)
                | None, Some value -> Some(barToPa value)
                | _ -> None
             BypassTargetMixOut =
                match Json.tryFAt item "t_miscelata_target_C", Json.tryFAt item "t_uscita_bypass_C" with
                | Some value, _ -> Some(cToK value)
                | None, Some value -> Some(cToK value)
                | _ -> None
             BypassOpenFraction = Json.tryFAt item "bypass_frazione_aperta"
             Notes = notes }

let private readLoadCases (root: JsonElement) (paths: string list) =
    match tryArrayAtPath root paths with
    | Some items when not (List.isEmpty items) -> items |> List.map loadCaseSpecAt
    | _ -> []

let private defaultConstraintSet (caseIn: DesignCase) =
    ConstraintModel.defaultRatingConstraints caseIn

let private parseConstraintTarget (item: JsonElement) : ConstraintModel.ConstraintTarget =
    let keyText = Json.sAt item "chiave" ""
    let info =
        match tryMetricInfo keyText with
        | Some found -> found
        | None -> failwithf "Vincolo non riconosciuto: %s" keyText
    let minValue = Json.tryFAt item "min" |> Option.map info.FromCli
    let maxValue = Json.tryFAt item "max" |> Option.map info.FromCli
    let limit =
        match minValue, maxValue with
        | Some lo, Some hi -> ConstraintModel.Range(lo, hi)
        | Some lo, None -> ConstraintModel.Min lo
        | None, Some hi -> ConstraintModel.Max hi
        | None, None -> failwithf "Il vincolo '%s' richiede almeno min o max." keyText
    { Key = info.Key
      Name = Json.sAt item "nome" info.Name
      Domain = info.Domain
      Unit = info.Unit
      Limit = limit
      Required = Json.bAt item "richiesto" true
      Weight = Json.fAt item "peso" 1.0 }

let private readConstraintSet (caseIn: DesignCase) (root: JsonElement) : ConstraintModel.ConstraintSet =
    match Json.tryArrayElements root "vincoli" with
    | Some items when not (List.isEmpty items) ->
        { Targets = items |> List.map parseConstraintTarget }
    | _ -> defaultConstraintSet caseIn

let private objectiveSense (text: string) =
    match lowerText text with
    | "max" | "maximize" | "massimizza" | "massimo" -> Optimize.Maximize
    | _ -> Optimize.Minimize

let private parseObjectiveTerm (item: JsonElement) : Optimize.ObjectiveTerm =
    let keyText = Json.sAt item "chiave" ""
    let info =
        match tryMetricInfo keyText with
        | Some found -> found
        | None -> failwithf "Obiettivo non riconosciuto: %s" keyText
    { Key = info.Key
      Name = Json.sAt item "nome" info.Name
      Weight = Json.fAt item "peso" 1.0
      Scale = Json.tryFAt item "scala" |> Option.map info.FromCli
      Sense = objectiveSense (Json.sAt item "senso" "min") }

let private readObjectiveSet (root: JsonElement) (path: string) (fallback: Optimize.ObjectiveSet) : Optimize.ObjectiveSet =
    match Json.tryArrayElements root path with
    | Some items when not (List.isEmpty items) ->
        { Terms = items |> List.map parseObjectiveTerm }
    | _ -> fallback

let private defaultVariableFor (caseIn: DesignCase) (key: Optimize.VariableKey) : Optimize.DesignVariable =
    let info = variableInfo key
    let currentCli = info.Current caseIn
    match key with
    | Optimize.FerruleLengthMm ->
        { Key = key
          Name = info.Name
          Current = currentCli
          Lower = max 50.0 (currentCli * 0.5)
          Upper = max (currentCli + 50.0) (currentCli * 1.5)
          Step = max 10.0 (currentCli * 0.1)
          Unit = info.Unit }
    | Optimize.TubeLengthM ->
        { Key = key
          Name = info.Name
          Current = currentCli
          Lower = max 1.0 (currentCli * 0.8)
          Upper = max (currentCli + 0.5) (currentCli * 1.2)
          Step = max 0.1 (currentCli * 0.05)
          Unit = info.Unit }
    | Optimize.TubeCount ->
        { Key = key
          Name = info.Name
          Current = currentCli
          Lower = max 1.0 (floor (currentCli * 0.85))
          Upper = max (currentCli + 1.0) (ceil (currentCli * 1.15))
          Step = max 1.0 (Math.Round(currentCli * 0.02))
          Unit = info.Unit }
    | Optimize.TubeOuterDiameterM ->
        let current = info.FromCli currentCli
        { Key = key
          Name = info.Name
          Current = current
          Lower = max 0.010 (current * 0.75)
          Upper = max (current + 0.005) (current * 1.50)
          Step = 0.001
          Unit = info.Unit }
    | Optimize.TubePitchM ->
        let current = info.FromCli currentCli
        { Key = key
          Name = info.Name
          Current = current
          Lower = max 0.005 (current * 0.85)
          Upper = max (current + 0.002) (current * 1.15)
          Step = max 0.001 (current * 0.025)
          Unit = info.Unit }
    | Optimize.ShellInnerDiameterM ->
        let current = info.FromCli currentCli
        { Key = key
          Name = info.Name
          Current = current
          Lower = max 0.5 (current * 0.9)
          Upper = max (current + 0.05) (current * 1.1)
          Step = max 0.01 (current * 0.025)
          Unit = info.Unit }
    | Optimize.DrumCenterlineHeightM ->
        { Key = key
          Name = info.Name
          Current = currentCli
          Lower = max 0.5 (currentCli * 0.75)
          Upper = max (currentCli + 0.5) (currentCli * 1.25)
          Step = max 0.1 (currentCli * 0.05)
          Unit = info.Unit }

let private parseVariable (caseIn: DesignCase) (defaults: Map<Optimize.VariableKey, Optimize.DesignVariable>) (item: JsonElement) =
    let keyText = Json.sAt item "chiave" ""
    let info =
        match tryVariableInfo keyText with
        | Some found -> found
        | None -> failwithf "Variabile di progetto non riconosciuta: %s" keyText
    let baseVar =
        defaults
        |> Map.tryFind info.Key
        |> Option.defaultWith (fun () -> defaultVariableFor caseIn info.Key)
    let currentValue =
        Json.tryFAt item "corrente"
        |> Option.map info.FromCli
        |> Option.defaultValue baseVar.Current
    let lowerValue =
        Json.tryFAt item "min"
        |> Option.map info.FromCli
        |> Option.defaultValue baseVar.Lower
    let upperValue =
        Json.tryFAt item "max"
        |> Option.map info.FromCli
        |> Option.defaultValue baseVar.Upper
    { baseVar with
        Name = Json.sAt item "nome" baseVar.Name
        Current = currentValue
        Lower = min lowerValue upperValue
        Upper = max lowerValue upperValue
        Step =
            Json.tryFAt item "passo"
            |> Option.map (info.FromCli >> abs)
            |> Option.defaultValue baseVar.Step
        Unit = info.Unit }

let private readOptimizeVariables (root: JsonElement) (caseIn: DesignCase) =
    let defaults =
        Optimize.defaultVariables caseIn
        |> List.map (fun item -> item.Key, item)
        |> Map.ofList
    match Json.tryArrayElements root "optimize.variabili" with
    | Some items when not (List.isEmpty items) -> items |> List.map (parseVariable caseIn defaults)
    | _ -> Optimize.defaultVariables caseIn

let private readDesignSpace (root: JsonElement) : GreenfieldDesign.DesignSpace =
    let tubeSizes =
        match Json.tryArrayElements root "design.spazio.taglie_tubo" with
        | Some items ->
            items
            |> List.choose (fun item ->
                match Json.tryFAt item "do_mm", Json.tryFAt item "passo_mm" with
                | Some doMm, Some pitchMm ->
                    Some ({ OuterDiameterM = doMm / 1000.0
                            PitchM = pitchMm / 1000.0 } : GreenfieldDesign.TubeSizeOption)
                | _ ->
                    match Json.tryFAt item "od_mm", Json.tryFAt item "pitch_mm" with
                    | Some doMm, Some pitchMm ->
                        Some ({ OuterDiameterM = doMm / 1000.0
                                PitchM = pitchMm / 1000.0 } : GreenfieldDesign.TubeSizeOption)
                    | _ -> None)
        | None -> []
    let tubeCounts =
        tryNumberArrayAtPath root [ "design.spazio.numero_tubi"; "design.spazio.tube_count" ]
        |> Option.defaultValue []
        |> List.map (fun (x: float) -> max 1 (int (Math.Round x)))
    let tubeLengths =
        tryNumberArrayAtPath root [ "design.spazio.lunghezza_tubi_m"; "design.spazio.tube_length_m" ]
        |> Option.defaultValue []
    let ferruleLengths =
        tryNumberArrayAtPath root [ "design.spazio.lunghezza_ferrula_mm"; "design.spazio.ferrule_length_mm" ]
        |> Option.defaultValue []
    let shellIds =
        tryNumberArrayAtPath root [ "design.spazio.mantello_id_mm"; "design.spazio.shell_id_mm" ]
        |> Option.defaultValue []
        |> List.map (fun x -> x / 1000.0)
    let pitches =
        tryNumberArrayAtPath root [ "design.spazio.passo_tubi_mm"; "design.spazio.tube_pitch_mm" ]
        |> Option.defaultValue []
        |> List.map (fun x -> x / 1000.0)
    let drumHeights =
        tryNumberArrayAtPath root [ "design.spazio.quota_drum_m"; "design.spazio.drum_centerline_m" ]
        |> Option.defaultValue []
    { TubeCounts = tubeCounts
      TubeLengthsM = tubeLengths
      FerruleLengthsMm = ferruleLengths
      ShellInnerDiametersM = shellIds
      TubeSizeOptions = tubeSizes
      TubePitchesM = pitches
      DrumCenterlineHeightsM = drumHeights }

let private formatNumber (value: float) =
    if Double.IsNaN value then "n/a"
    else value.ToString("F3", CultureInfo.InvariantCulture)

let private formatMetricValue (key: ConstraintModel.ConstraintValueKey) (value: float) =
    let info = metricInfo key
    if Double.IsNaN value then "n/a"
    else sprintf "%s %s" (formatNumber (info.ToCli value)) info.Unit

let private formatConstraintLimit (target: ConstraintModel.ConstraintTarget) =
    let info = metricInfo target.Key
    let f x = formatNumber (info.ToCli x)
    match target.Limit with
    | ConstraintModel.Min x -> sprintf ">= %s %s" (f x) info.Unit
    | ConstraintModel.Max x -> sprintf "<= %s %s" (f x) info.Unit
    | ConstraintModel.Range(lo, hi) -> sprintf "%s .. %s %s" (f lo) (f hi) info.Unit

let private summaryValue key (result: DesignResult) =
    ConstraintReaders.tryFindValue key result
    |> Option.map (fun x -> x.Value)
    |> Option.defaultValue nan

let private writeTextFile (outDir: string) (name: string) (content: string) =
    File.WriteAllText(Path.Combine(outDir, name), content)

let private writeMechanicalInterfaceFile (outDir: string) (title: string) (results: (string * DesignResult) list) =
    Report.mechanicalInterfaceTextMany title results
    |> writeTextFile outDir "interfaccia_meccanica.txt"

let private progressState description =
    ref (Progress.snapshot description None)

let private setProgress (state: Progress.StatusSnapshot ref) (description: string) (fraction: float option) =
    state.Value <- { Description = description; Fraction = fraction }

let private reportStructuredProgress (logger: PhaseLogger.Logger) (state: Progress.StatusSnapshot ref) (update: DesignRuntime.ProgressUpdate) =
    state.Value <- Progress.mergeStatus state.Value update
    logger update.Description

let private filteredArgs (rest: string list) (outDir: string) (optionsPath: string) =
    rest |> List.filter (fun x -> x <> "--out" && x <> outDir && x <> "--options" && x <> optionsPath)

let private resolveCaseArg (rest: string list) (outDir: string) (optionsPath: string) =
    match filteredArgs rest outDir optionsPath with
    | f :: _ when File.Exists f -> Some f, loadCase f
    | f :: _ when not (f.StartsWith("--")) ->
        eprintfn "Case file not found: %s" f
        raise (FileNotFoundException("Case file not found", f))
    | _ -> None, Defaults.referenceCase
let template = """{
  "nome": "WHB reformer secondario - caso base",
  "materiale": "T11",
  "materiale_mantello": "SA-516",
  "campata_non_supportata_m": 1.20,
  "reticolo": "60",
  "smorzamento_log": 0.03,
  "t_montaggio_C": 20.0,
  "sezioni_assiali": 90,
  "bande_verticali": 12,
  "infittimento_imbocco": 10.0,
  "ricircolo_interno": false,

  "bypass": {
    "presente": true,
    "frazione": -1,
    "t_uscita_target_C": 355.0,
    "liner_id_mm": 275.0,
    "liner_od_mm": 281.0,
    "liner_materiale": "601",
    "isolante_od_mm": 284.0,
    "isolante": "saffil",
    "tubo_od_mm": 300.0,
    "tubo_materiale": "SA-192",
    "fouling_m2KW": 0.00050,
    "k_localizzati": 1.5,
    "valvola_a_valle": true,
    "valvola_apertura_gradi": -1,
    "valvola_apertura_min_gradi": 15.0,
    "valvola_apertura_max_gradi": 70.0,
    "t_miscelata_min_C": 350.0,
    "t_miscelata_max_C": 360.0,
    "v_lavaggio_min_ms": 1.5,
    "rhov2_max_valvola": 40000.0
  },

  "bypass_frazione_aperta": 0.10,

  "tubi": {
    "numero": 848,
    "di_mm": 32.0,
    "do_mm": 38.1,
    "lunghezza_m": 12.998,
    "passo_mm": 50.8,
    "sfalsato": true,
    "mantello_id_mm": 2025.0,
    "otl_mm": 1711.11,
    "itl_mm": 571.0,
    "diaframma_od_mm": 2015.0,
    "mantello_wt_mm": 58.0,
    "rugosita_mm": 0.045
  },

  "ferrula": {
    "presente": true,
    "lunghezza_mm": 200.0,
    "lunghezze": [ { "frazione": 1.0, "lunghezza_mm": 200.0 } ],
    "bore_mm": 26.7,
    "manicotto_od_mm": 30.0,
    "manicotto_materiale": "800",
    "isolante": "saffil"
  },

  "gas": {
    "composizione": { "H2": 0.3707, "N2": 0.1577, "CO": 0.0863, "CO2": 0.0546, "CH4": 0.0027, "AR": 0.0020, "H2O": 0.3260 },
    "portata_kgs": 85.42,
    "maggiorazione": 1.0,
    "t_ingresso_C": 967.5,
    "p_ingresso_bara": 34.74,
    "z": 1.0,
    "modello_gas": "realistico",
    "modello_claus": "frozen",
    "claus_cinetica": {
      "fattore_severita": 0.15,
      "fattore_tau": 0.35,
      "sottopassi": 8,
      "claus_a_1s": 500000.0,
      "claus_ea_kjmol": 60.0,
      "cos_a_1s": 200000.0,
      "cos_ea_kjmol": 70.0,
      "cs2_a_1s": 300000.0,
      "cs2_ea_kjmol": 90.0
    },
    "fouling_m2KW": 0.00050,
    "emissivita_parete": 0.85,
    "irraggiamento": true,
    "coeff_imbocco": 1.4,
    "correlazione": "gnielinski",
    "miscelazione": "wilke",
    "gas_reale": true,
    "shift": "congelata",
    "shift_t_freeze_C": 700.0,
    "shift_frazione": 0.3
  },

  "vapore": {
    "pressione_bara": 117.84,
    "dnbr_min": 2.0,
    "fouling_m2KW": 0.00015,
    "rugosita_um": 1.0,
    "fattore_fascio": 1.5,
    "correlazione": "mostinski",
    "ebollizione_flusso": "chen",
    "modello_chf": "palen",
    "csf": 0.013,
    "t_alimento_C": 250.0
  },

  "condensatore_zolfo": {
    "presente": false,
    "usa_uscita_whb": true,
    "sezioni": 24,
    "tempo_residenza_s": 1.0,
    "dp_mbar": 20.0,
    "t_uscita_target_C": 145.0,
    "t_parete_C": 140.0,
    "t_refrigerante_C": 135.0,
    "u_assunto_Wm2K": 60.0,
    "gas_ingresso": {
      "composizione": { "N2": 0.70, "H2O": 0.10, "H2S": 0.12, "SO2": 0.04, "S2": 0.04 },
      "portata_kgs": 10.0,
      "maggiorazione": 1.0,
      "t_ingresso_C": 220.0,
      "p_ingresso_bara": 1.7,
      "z": 1.0,
      "modello_gas": "realistico",
      "modello_claus": "kinetic",
      "claus_cinetica": {
        "fattore_severita": 0.15,
        "fattore_tau": 0.35,
        "sottopassi": 8,
        "claus_a_1s": 500000.0,
        "claus_ea_kjmol": 60.0,
        "cos_a_1s": 200000.0,
        "cos_ea_kjmol": 70.0,
        "cs2_a_1s": 300000.0,
        "cs2_ea_kjmol": 90.0
      },
      "miscelazione": "wilke",
      "gas_reale": true,
      "shift": "congelata",
      "shift_t_freeze_C": 700.0,
      "shift_frazione": 0.3
    }
  },

  "circuito": {
    "dz_drum_whb_m": 6.0,
    "offset_livello_m": 0.0,
    "interne_drum_mbar": 50.0,
    "modello_vuoto": "zuber",
    "modello_attrito": "friedel",

    "riser": [
      { "tag": "R1", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 0.90, "angolo_gradi": 0 },
      { "tag": "R2", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 2.60, "angolo_gradi": 0 },
      { "tag": "R3", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 5.50, "angolo_gradi": 0 },
      { "tag": "R4", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 9.30, "angolo_gradi": 0 },
      { "tag": "R5", "nps": "6\" Sch.120",  "id_mm": 139.7, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 12.70, "angolo_gradi": 0, "nota": "estremita' fredda" }
    ],

    "downcomer": [
      { "tag": "DC1", "nps": "18\" Sch.120", "id_mm": 387.2, "n": 1, "diritti_mm": [250,2623,2376],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":2} ], "z_m": 0.80, "angolo_gradi": 150 },
      { "tag": "DC2", "nps": "18\" Sch.120", "id_mm": 387.2, "n": 1, "diritti_mm": [250,2623,2376],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":2} ], "z_m": 0.80, "angolo_gradi": 210 },
      { "tag": "DC3", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [250,3040,2621],
        "curve": [ {"gradi":60,"r_su_d":1.5,"n":1}, {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":1} ], "z_m": 2.50, "angolo_gradi": 150 },
      { "tag": "DC4", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [250,3040,2621],
        "curve": [ {"gradi":60,"r_su_d":1.5,"n":1}, {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":1} ], "z_m": 2.50, "angolo_gradi": 210 },
      { "tag": "DC5", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 4.50, "angolo_gradi": 180 },
      { "tag": "DC6", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 6.80, "angolo_gradi": 180 },
      { "tag": "DC7", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 9.00, "angolo_gradi": 180 },
      { "tag": "DC8", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 11.00, "angolo_gradi": 180 },
      { "tag": "DC9", "nps": "4\" Sch.120", "id_mm": 92.1, "n": 1, "diritti_mm": [500,3000,1500],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2} ], "z_m": 12.70, "angolo_gradi": 180, "nota": "estremita' fredda" }
    ]
  },

  "drum": {
    "modello_attivo": true,
    "id_mm": 3000.0,
    "lunghezza_tt_mm": 13000.0,
    "livello_normale_mm": 1650.0,
    "calm_box_n": 4,
    "calm_box_risers_per_box": 1,
    "calm_box_area_m2": 0.22799999999999998,
    "calm_box_lunghezza_m": 2.30,
    "calm_box_dh_m": 0.47010309278350513,
    "canale_curvatura_gradi": 150.0,
    "calm_box_top_opening_m2": 0.35,
    "calm_box_opening_above_level": true,
    "calm_box_k_extra": 1.0,
    "calm_box_waterfall_m": 0.30,
    "downcomer_entry_area_m2": 0.0,
    "downcomer_vortex_breaker_k": 0.5,
    "demister_area_m2": 20.8,
    "demister_k": 2.0,
    "camini_numero": 8,
    "camini_id_mm": 202.7,
    "vapore_esterno_kgs": 14.12,
    "dp_costruttore_mbar": -1.0
  },

  "bocchelli": {
    "n_riser": 0,
    "n_downcomer": 0,
    "v_downcomer_ms": 2.0,
    "rhov2_max_riser": 6000.0,
    "rhov2_max_downcomer": 3000.0
  },

  "vincoli": [
    { "chiave": "dnbr_min", "min": 2.0, "peso": 1.0, "richiesto": true },
    { "chiave": "t_metallo_tubi_max_c", "max": 450.0, "peso": 1.0, "richiesto": true },
    { "chiave": "dp_gas_mbar", "max": 300.0, "peso": 0.5, "richiesto": true },
    { "chiave": "v_vcrit", "max": 0.80, "peso": 1.0, "richiesto": true },
    { "chiave": "peso_whb_kg", "max": 999999.0, "peso": 0.2, "richiesto": false },
    { "chiave": "ingombro_whb_m2", "max": 999999.0, "peso": 0.2, "richiesto": false }
  ],

  "rating": {
    "carichi": [
      { "nome": "base" },
      { "nome": "110%", "fattore_portata_gas": 1.10 }
    ]
  },

  "optimize": {
    "carichi": [
      { "nome": "base" },
      { "nome": "110%", "fattore_portata_gas": 1.10 }
    ],
    "variabili": [
      { "chiave": "lunghezza_ferrula_mm", "min": 100.0, "max": 350.0, "passo": 25.0 },
      { "chiave": "lunghezza_tubi_m", "min": 11.0, "max": 14.0, "passo": 0.25 },
      { "chiave": "numero_tubi", "min": 780.0, "max": 900.0, "passo": 4.0 }
    ],
    "obiettivo": [
      { "chiave": "peso_whb_kg", "peso": 1.0, "senso": "min" },
      { "chiave": "ingombro_whb_m2", "peso": 0.25, "senso": "min" },
      { "chiave": "ingombro_drum_m2", "peso": 0.10, "senso": "min" }
    ],
    "max_iterazioni": 80,
    "tolleranza": 0.001
  },

  "design": {
    "carichi": [
      { "nome": "base" },
      { "nome": "110%", "fattore_portata_gas": 1.10 }
    ],
    "obiettivo": [
      { "chiave": "peso_whb_kg", "peso": 1.0, "senso": "min" },
      { "chiave": "ingombro_whb_m2", "peso": 0.25, "senso": "min" }
    ],
    "spazio": {
      "numero_tubi": [800, 848, 896],
      "lunghezza_tubi_m": [12.0, 13.0, 14.0],
      "lunghezza_ferrula_mm": [150.0, 200.0, 250.0],
      "mantello_id_mm": [1950.0, 2025.0, 2100.0],
      "passo_tubi_mm": [48.0, 50.8, 53.0],
      "quota_drum_m": [5.5, 6.0, 6.5]
    }
  }
}
"""
let selfTest () =
    let ci = CultureInfo.InvariantCulture
    let mutable fails = 0
    let check name (got: float) (exp: float) (tol: float) =
        let err = abs (got - exp) / abs exp
        if err > tol then fails <- fails + 1
        printfn "  %-50s %14s  (rif. %-14s) %s"
            name (got.ToString("G8", ci)) (exp.ToString("G8", ci)) (if err <= tol then "OK" else "FALLITO")

    printfn "IAPWS-IF97 - punti di riferimento ufficiali"
    check "psat(300 K) [MPa]" (Steam.psat_MPa 300.0) 0.353658941e-2 1e-7
    check "psat(500 K) [MPa]" (Steam.psat_MPa 500.0) 0.263889776e1 1e-7
    check "Tsat(0.1 MPa) [K]" (Steam.tsat_K 0.1) 372.755919 1e-7
    check "Tsat(10 MPa) [K]" (Steam.tsat_K 10.0) 584.149488 1e-7
    let (v1, h1, cp1, s1) = Steam.region1 3.0 300.0
    check "R1 (3 MPa,300 K) v" v1 0.100215168e-2 1e-8
    check "R1 (3 MPa,300 K) h" h1 0.115331273e3 1e-8
    check "R1 (3 MPa,300 K) cp" cp1 0.417301218e1 1e-8
    check "R1 (3 MPa,300 K) s" s1 0.392294792 1e-8
    let (v1b, h1b, _, _) = Steam.region1 80.0 300.0
    check "R1 (80 MPa,300 K) v" v1b 0.971180894e-3 1e-8
    check "R1 (80 MPa,300 K) h" h1b 0.184142828e3 1e-8
    let (v1c, h1c, _, _) = Steam.region1 3.0 500.0
    check "R1 (3 MPa,500 K) v" v1c 0.120241800e-2 1e-8
    check "R1 (3 MPa,500 K) h" h1c 0.975542239e3 1e-8
    let (v2, h2, cp2, _) = Steam.region2 0.0035 300.0
    check "R2 (0.0035 MPa,300 K) v" v2 0.394913866e2 1e-8
    check "R2 (0.0035 MPa,300 K) h" h2 0.254991145e4 1e-8
    check "R2 (0.0035 MPa,300 K) cp" cp2 0.191300162e1 1e-8
    let (v2b, h2b, _, _) = Steam.region2 0.0035 700.0
    check "R2 (0.0035 MPa,700 K) v" v2b 0.923015898e2 1e-8
    check "R2 (0.0035 MPa,700 K) h" h2b 0.333568375e4 1e-8
    let (v2c, h2c, _, _) = Steam.region2 30.0 700.0
    check "R2 (30 MPa,700 K) v" v2c 0.542946619e-2 1e-8
    check "R2 (30 MPa,700 K) h" h2c 0.263149474e4 1e-8
    check "mu(298.15 K,998) [uPa s]" (Steam.viscosity 298.15 998.0 * 1e6) 889.735100 1e-5
    check "k(298.15 K,998) [mW/m/K]" (Steam.conductivity 298.15 998.0 * 1e3) 607.712868 1e-4
    check "sigma(300 K) [mN/m]" (Steam.surfaceTension 300.0 * 1e3) 71.6893 1e-4

    printfn ""
    printfn "Proprieta' dei gas"
    let air = [ GasProps.N2, 0.79; GasProps.O2, 0.21 ]
    let p300 = GasProps.mix air 300.0 101325.0 1.0
    check "aria 300 K rho [kg/m3]" p300.Rho 1.177 0.01
    check "aria 300 K cp [J/kg/K]" p300.Cp 1005.0 0.02
    check "aria 300 K mu [uPa s]" (p300.Mu * 1e6) 18.5 0.03
    check "aria 300 K k [W/m/K]" p300.K 0.0263 0.05

    printfn ""
    printfn "Confronto con il datasheet (miscela di riferimento)"
    let comp = Defaults.referenceComposition
    let pin = GasProps.mix comp (cToK 967.5) (barToPa 34.74) 1.0
    let pout = GasProps.mix comp (cToK 355.0) (barToPa 34.44) 1.0
    check "MW miscela [kg/kmol]" (GasProps.mixMolarMass comp * 1000.0) 15.99 2e-3
    check "rho ingresso [kg/m3]" pin.Rho 5.36 0.02
    check "rho uscita [kg/m3]" pout.Rho 10.48 0.02
    check "cp ingresso [kJ/kg/K]" (pin.Cp / 1000.0) 2.353 0.05
    check "cp uscita [kJ/kg/K]" (pout.Cp / 1000.0) 2.119 0.05
    let pinM = GasProps.mixWith GasProps.MolarAverage comp (cToK 967.5) (barToPa 34.74) 1.0
    let poutM = GasProps.mixWith GasProps.MolarAverage comp (cToK 355.0) (barToPa 34.44) 1.0
    printfn "  --- mu e k: il datasheet usa la media molare, il codice per default Wilke"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "k ingresso [W/m/K]" (pin.K.ToString("G6", ci)) (pinM.K.ToString("G6", ci)) "0.1722"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "k uscita [W/m/K]" (pout.K.ToString("G6", ci)) (poutM.K.ToString("G6", ci)) "0.1011"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "mu ingresso [cP]" ((pin.Mu * 1000.0).ToString("G6", ci)) ((pinM.Mu * 1000.0).ToString("G6", ci)) "0.0376"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "mu uscita [cP]" ((pout.Mu * 1000.0).ToString("G6", ci)) ((poutM.Mu * 1000.0).ToString("G6", ci)) "0.0223"
    check "media molare: mu ingresso [cP]" (pinM.Mu * 1000.0) 0.0376 0.05
    check "media molare: k ingresso [W/m/K]" pinM.K 0.1722 0.10
    let sat = Steam.sat (barToPa 117.84)
    check "Tsat a 117.84 bar [C]" (kToC sat.Tsat) 323.3 5e-4

    printfn ""
    printfn "Gas reale: secondo coefficiente del viriale (p = 34.74 bar)"
    printfn "  %-9s %14s %10s %14s %14s" "T [K]" "B_H2O [m3/mol]" "Z mix" "h_res [kJ/kg]" "cp_res [J/kgK]"
    let mwx = GasProps.mixMolarMass (GasProps.normalize comp)
    for t in [ 628.0; 700.0; 850.0; 1000.0; 1240.0 ] do
        let bw = GasProps.Virial.bWater t
        let (z, hr, cpr) = GasProps.Virial.residual (GasProps.normalize comp) t (barToPa 34.74)
        printfn "  %-9.0f %14.4e %10.5f %14.2f %14.2f" t bw z (hr / mwx / 1000.0) (cpr / mwx)
    let dh_id =
        GasProps.enthalpyAbs comp (cToK 967.5) - GasProps.enthalpyAbs comp (cToK 355.0)
    let dh_re =
        GasProps.enthalpyAbsReal true comp (cToK 967.5) (barToPa 34.74)
        - GasProps.enthalpyAbsReal true comp (cToK 355.0) (barToPa 34.44)
    printfn "  salto entalpico 967.5 -> 355 C: ideale %.1f kJ/kg, reale %.1f kJ/kg  (%+.2f %%)"
        (dh_id / 1000.0) (dh_re / 1000.0) (100.0 * (dh_re / dh_id - 1.0))

    printfn ""
    printfn "Shift: K_p(700 K) = %.3f ; K_p(1000 K) = %.3f" (Shift.kp 700.0) (Shift.kp 1000.0)
    printfn ""
    if fails = 0 then printfn "TUTTI I CONTROLLI SUPERATI" else printfn "%d CONTROLLI FALLITI" fails
    fails
let loadCurves (options: Options.ProjectOptions) (case0: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    let logger = PhaseLogger.create options
    let currentTask = ref "Starting partial-load campaign"
    let swRun = Diagnostics.Stopwatch.StartNew()
    logger "Partial-load campaign started"
    let sb = Text.StringBuilder()
    let ci = CultureInfo.InvariantCulture
    let f1 (x: float) = x.ToString("F1", ci)
    let f2 (x: float) = x.ToString("F2", ci)
    let f3 (x: float) = x.ToString("F3", ci)
    let f4 (x: float) = x.ToString("F4", ci)
    let f0 (x: float) = x.ToString("F0", ci)
    let coarse = { case0 with NZ = 40; NY = 8; AxialRefine = 6.0 }
    let loads = [ 0.50; 0.60; 0.70; 0.80; 0.90; 1.00; 1.10 ]
    sb.AppendLine(String('=', 110)) |> ignore
    sb.AppendLine("WHB / PGC - CURVE DI CARICO PARZIALE") |> ignore
    sb.AppendLine(sprintf "Caso: %s" case0.Name) |> ignore
    sb.AppendLine(String('=', 110)) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  Maglia ridotta (40 x 8) per il confronto fra carichi: la convergenza di griglia e' gia' dimostrata.") |> ignore
    sb.AppendLine("  Si mantengono composizione, temperatura d'ingresso del gas e pressione del corpo cilindrico.") |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  carico  w gas   potenza  vapore   T tubi  T MISC  farfalla  by-pass    CR   q''max  T met  DNBR  alpha  dP gas") |> ignore
    sb.AppendLine("     [%] [kg/s]      [MW]   [t/h]     [°C]    [°C]       [°]      [%]         [kW/m2]  [°C]         max  [mbar]") |> ignore
    sb.AppendLine(String('-', 110)) |> ignore
    let pts = ResizeArray<LoadPoint>()
    for l in loads do
        let c = { coarse with Gas = { coarse.Gas with MassFlow = case0.Gas.MassFlow * l } }
        logger (sprintf "Partial-load point %.0f %% started" (100.0 * l))
        let swPoint = Diagnostics.Stopwatch.StartNew()
        let r =
            Progress.runWithStatus
                (sprintf "Partial-load calculation %.0f %%: thermal, hydraulic, bypass, vibration, and mechanical checks" (100.0 * l))
                12.0
                (fun () -> Design.runWithProgress (PhaseLogger.phase logger currentTask) c)
        logger (sprintf "Partial-load point %.0f %% completed in %.1f s" (100.0 * l) swPoint.Elapsed.TotalSeconds)
        let hot = r.Cells |> List.filter (fun x -> not x.InFerrule)
        let p =
            { LoadFraction = l
              GasFlow = c.Gas.MassFlow
              Duty = r.Duty
              Steam = r.SteamProduction
              TOutMixed = r.TGasOutMean
              TOutTubes = (match r.BypassResult with Some b -> b.TOutTubes | None -> r.TGasOutMean)
              ValveOpenDeg = (match r.Valve with Some v -> v.Normal.OpenDeg | None -> nan)
              BypassFraction = (match r.BypassResult with Some b -> b.Fraction | None -> 0.0)
              CircRatio = r.Circulation.CirculationRatio
              QFluxMax = (hot |> List.map (fun x -> x.QFluxOut) |> List.max)
              TMetalMax = (r.Cells |> List.map (fun x -> x.TMetalIn) |> List.max)
              DNBRMin = (hot |> List.map (fun x -> x.DNBR) |> List.min)
              DpGas = r.DpGas
              AlphaMax = (r.Cells |> List.map (fun x -> x.Alpha) |> List.max)
              Note = "" }
        pts.Add p
        sb.AppendLine(
            sprintf "  %6s %6s %9s %7s %8s %7s %9s %8s %5s %7s %6s %5s %6s %7s"
                (f0 (100.0 * l)) (f1 p.GasFlow) (f2 (p.Duty / 1e6)) (f0 (p.Steam * 3.6))
                (f1 (kToC p.TOutTubes)) (f1 (kToC p.TOutMixed)) (f1 p.ValveOpenDeg)
                (f2 (100.0 * p.BypassFraction)) (f1 p.CircRatio) (f0 (p.QFluxMax / 1000.0))
                (f0 (kToC p.TMetalMax)) (f2 p.DNBRMin) (f3 p.AlphaMax) (f0 (p.DpGas / 100.0))) |> ignore
        printfn "  carico %3.0f %% completato" (100.0 * l)
    sb.AppendLine(String('-', 110)) |> ignore
    sb.AppendLine() |> ignore
    let bp = case0.Bypass
    let outOfWindow =
        pts |> Seq.filter (fun p -> p.ValveOpenDeg < bp.MinOpenDeg || p.ValveOpenDeg > bp.MaxOpenDeg) |> List.ofSeq
    sb.AppendLine("  LETTURA") |> ignore
    let para (t: string) =
        let words = t.Split(' ')
        let mutable cur = ""
        for w in words do
            if cur.Length + w.Length + 1 > 100 then
                sb.AppendLine("  " + cur) |> ignore
                cur <- w
            else cur <- (if cur = "" then w else cur + " " + w)
        if cur <> "" then sb.AppendLine("  " + cur) |> ignore
        sb.AppendLine() |> ignore
    para (sprintf "REGOLAZIONE. La farfalla deve muoversi da %.1f gradi al %.0f %% di carico a %.1f gradi al %.0f %%. La finestra di controllabilita' ammessa e' %.1f - %.1f gradi."
              (pts.[0].ValveOpenDeg) (100.0 * loads.[0])
              (pts.[pts.Count - 1].ValveOpenDeg) (100.0 * loads.[loads.Length - 1])
              bp.MinOpenDeg bp.MaxOpenDeg)
    if outOfWindow.IsEmpty then
        para "Tutti i carichi cadono dentro la finestra di controllabilita': la valvola e' della taglia giusta su tutto il campo."
    else
        para (sprintf "ATTENZIONE: a %s la posizione richiesta cade FUORI dalla finestra di controllabilita'. In quelle condizioni la regolazione diventa instabile o priva di autorita'."
                  (outOfWindow |> List.map (fun p -> sprintf "%.0f %%" (100.0 * p.LoadFraction)) |> String.concat ", "))
    para "CIRCOLAZIONE. Il rapporto di circolazione MIGLIORA a carico ridotto: il battente motore cala meno delle perdite, che vanno con il quadrato della portata. Il carico ridotto non e' quindi una condizione critica per la circolazione."
    para "CRISI DI EBOLLIZIONE. Il DNBR migliora anch'esso al calare del carico, perche' il flusso termico scende piu' in fretta del flusso critico. La condizione critica resta il carico pieno, e in particolare il carico pieno con apparecchio pulito."
    para "TEMPERATURA DEL METALLO. Cala con il carico, ma meno di quanto ci si aspetti: il coefficiente di scambio lato gas scende come la portata alla 0.8, quindi la resistenza dominante peggiora relativamente e una parte del guadagno si perde."
    para "AVVERTENZA. A carico ridotto la temperatura d'ingresso del gas e' stata mantenuta costante. Nella marcia reale un carico ridotto del reformer di solito comporta anche una temperatura d'ingresso diversa: per una curva d'esercizio realistica serve la coppia (portata, temperatura) del bilancio d'impianto a ogni carico."
    let txt = sb.ToString()
    printfn "%s" txt
    File.WriteAllText(Path.Combine(outDir, "carichi.txt"), txt)
    let csv = Text.StringBuilder()
    csv.AppendLine("carico;w_gas_kgs;potenza_MW;vapore_th;T_tubi_C;T_miscelata_C;farfalla_gradi;bypass_pc;CR;q_max_kWm2;T_met_max_C;DNBR_min;alpha_max;dp_gas_mbar") |> ignore
    for p in pts do
        csv.AppendLine(String.Join(";",
            [ f2 p.LoadFraction; f2 p.GasFlow; f3 (p.Duty / 1e6); f1 (p.Steam * 3.6)
              f2 (kToC p.TOutTubes); f2 (kToC p.TOutMixed); f2 p.ValveOpenDeg
              f3 (100.0 * p.BypassFraction); f2 p.CircRatio; f1 (p.QFluxMax / 1000.0)
              f1 (kToC p.TMetalMax); f3 p.DNBRMin; f4 p.AlphaMax; f1 (p.DpGas / 100.0) ])) |> ignore
    File.WriteAllText(Path.Combine(outDir, "carichi.csv"), csv.ToString())
    logger (sprintf "Partial-load campaign completed in %.1f s; output folder: %s"
                swRun.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0
let runCase (options: Options.ProjectOptions) (casePath: string option) (case: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    Directory.CreateDirectory options.Folders.TempFolder |> ignore
    let logger = PhaseLogger.create options
    Preflight.run options casePath outDir logger
    let sw = Diagnostics.Stopwatch.StartNew()
    let currentStatus = progressState "Starting design run"
    logger "Run started"
    let r =
        let runSettings = createRunSettings options options.Calculation.CorrelationValidityWarnings
        Progress.runWithStatusSnapshot
            (fun () -> currentStatus.Value)
            25.0
            (fun () -> Design.runWithSettingsAndStructuredProgress runSettings (reportStructuredProgress logger currentStatus) case)
    logger "Design calculation completed; writing reports"
    sw.Stop()
    if options.Reporting.GenerateFullReport then
        let rep = Report.text r
        File.WriteAllText(Path.Combine(outDir, "report.txt"), rep)
        logger "Full text report written"
    else
        logger "Full text report skipped by option"
    let syn = Report.synthesis r
    File.WriteAllText(Path.Combine(outDir, "criticita.txt"), syn)
    let pdsText = PdsComparison.text r
    File.WriteAllText(Path.Combine(outDir, "pds_comparison.txt"), pdsText)
    File.WriteAllText(Path.Combine(outDir, "pds_comparison.csv"), PdsComparison.csv r)
    let inventoryText = Report.inventoryText r
    File.WriteAllText(Path.Combine(outDir, "inventory_summary.txt"), inventoryText)
    File.WriteAllText(Path.Combine(outDir, "inventory_summary.csv"), Report.inventoryCsv r)
    writeMechanicalInterfaceFile outDir (sprintf "MECHANICAL CALCULATION INTERFACE - %s" r.Case.Name) [ sprintf "Caso %s" r.Case.Name, r ]
    File.WriteAllText(Path.Combine(outDir, "celle.csv"), Report.csvCells r)
    File.WriteAllText(Path.Combine(outDir, "profilo_assiale.csv"), Report.csvAxial r)
    File.WriteAllText(Path.Combine(outDir, "tensioni.csv"), Report.csvStress r)
    File.WriteAllText(Path.Combine(outDir, "valvola_bypass.csv"), Report.csvValve r)
    File.WriteAllText(Path.Combine(outDir, "maldistribuzione.txt"), Report.maldistributionText r)
    File.WriteAllText(Path.Combine(outDir, "vibrazioni.txt"), Report.vibrationText r)
    File.WriteAllText(Path.Combine(outDir, "dimensionamento.txt"), Report.sizingText r)
    match r.SulphurCondenserResult with
    | Some sc ->
        File.WriteAllText(Path.Combine(outDir, "sulphur_condenser.txt"), Report.sulphurCondenserText sc)
        File.WriteAllText(Path.Combine(outDir, "sulphur_condenser_profile.csv"), Report.sulphurCondenserCsv sc)
        logger "Sulphur-condenser integration reports written"
    | None -> ()
    if options.Reporting.GenerateHtmlReport then
        File.WriteAllText(Path.Combine(outDir, "report.html"), HtmlReport.build r)
        logger "Full HTML report written"
    else
        logger "Full HTML report skipped by option"
    logger "Report files written"
    printfn "%s" syn
    printfn "%s" pdsText
    printfn "Calcolo completato in %.1f s. File scritti in: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Run completed in %.1f s; output folder: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0

let runSulphurCondenserCase (options: Options.ProjectOptions) (casePath: string option) (case: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    Directory.CreateDirectory options.Folders.TempFolder |> ignore
    let logger = PhaseLogger.create options
    Preflight.run options casePath outDir logger
    let sw = Diagnostics.Stopwatch.StartNew()
    let currentStatus = progressState "Starting sulphur-condenser run"
    logger "Sulphur-condenser run started"
    let scSpec = { case.SulphurCondenser with Enabled = true }
    let caseWithSc = { case with SulphurCondenser = scSpec }
    let result =
        if scSpec.UseWhbOutlet then
            let runSettings = createRunSettings options options.Calculation.CorrelationValidityWarnings
            setProgress currentStatus "Running base WHB calculation for sulphur-condenser inlet" (Some 0.0)
            let design =
                Progress.runWithStatusSnapshot
                    (fun () -> currentStatus.Value)
                    25.0
                    (fun () -> Design.runWithSettingsAndStructuredProgress runSettings (reportStructuredProgress logger currentStatus) caseWithSc)
            match design.SulphurCondenserResult with
            | Some sc -> sc
            | None -> failwith "Sulphur-condenser integration did not produce a result."
        else
            setProgress currentStatus "Running dedicated sulphur-condenser calculation" None
            Progress.runWithStatusSnapshot
                (fun () -> currentStatus.Value)
                10.0
                (fun () -> SulphurCondenser.solve scSpec)
    File.WriteAllText(Path.Combine(outDir, "sulphur_condenser.txt"), Report.sulphurCondenserText result)
    File.WriteAllText(Path.Combine(outDir, "sulphur_condenser_profile.csv"), Report.sulphurCondenserCsv result)
    printfn "%s" (Report.sulphurCondenserText result)
    printfn "Sulphur-condenser calculation completed in %.1f s. Files written to: %s"
        sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Sulphur-condenser run completed in %.1f s; output folder: %s"
                sw.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0

let sizingOnly (options: Options.ProjectOptions) (case: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    let logger = PhaseLogger.create options
    let currentStatus = progressState "Starting sizing run"
    let sw = Diagnostics.Stopwatch.StartNew()
    logger "Sizing run started"
    let runSettings = createRunSettings options options.Calculation.CorrelationValidityWarnings
    let r =
        Progress.runWithStatusSnapshot
            (fun () -> currentStatus.Value)
            25.0
            (fun () -> Design.runWithSettingsAndStructuredProgress runSettings (reportStructuredProgress logger currentStatus) case)
    let txt = Report.sizingText r
    File.WriteAllText(Path.Combine(outDir, "dimensionamento.txt"), txt)
    printfn "%s" txt
    printfn "Sizing completed in %.1f s. File written to: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Sizing run completed in %.1f s; output folder: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0

let optimizeCaseLegacy (options: Options.ProjectOptions) (case: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    let logger = PhaseLogger.create options
    let currentStatus = progressState "Starting constrained search"
    let sw = Diagnostics.Stopwatch.StartNew()
    logger "Constrained design search started"
    let runSettings = createRunSettings options false
    let problem = Designers.Designer.defaultProblem case
    let mutable n = 0
    let runOne (c: DesignCase) =
        n <- n + 1
        let label =
            sprintf "Design evaluation %d (ferrula %.0f mm, tubi %.2f m)"
                n (1000.0 * (c.Ferrule.Lengths |> List.sumBy (fun (f, l) -> f * l))) c.Tube.Length
        let startFraction = float (n - 1) / float (max 1 problem.MaxIterations)
        let endFraction = float n / float (max 1 problem.MaxIterations)
        let spanReporter =
            ExecutionProgress.Reporting.scale startFraction endFraction (reportStructuredProgress logger currentStatus)
        spanReporter (ExecutionProgress.Reporting.step 0.0 label)
        Design.runWithSettingsAndStructuredProgress runSettings spanReporter c
    let result =
        Progress.runWithStatusSnapshot
            (fun () -> currentStatus.Value)
            60.0
            (fun () -> Designers.Designer.optimize runOne case problem)
    let sb = Text.StringBuilder()
    let ci = CultureInfo.InvariantCulture
    let f2 (x: float) = x.ToString("F2", ci)
    let f3 (x: float) = x.ToString("F3", ci)
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "RICERCA VINCOLATA - %s" problem.Name) |> ignore
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine(sprintf "  Obiettivo   : %s" problem.Objective) |> ignore
    sb.AppendLine(sprintf "  Valutazioni : %d (%s)" result.Evaluations
                    (if result.Converged then "tolleranza sul passo raggiunta" else "tetto di valutazioni")) |> ignore
    sb.AppendLine(sprintf "  Potenza     : %s MW" (f3 (-result.Best.Objective / 1.0e6))) |> ignore
    sb.AppendLine(sprintf "  Ammissibile : %s" (if result.Best.Feasible then "SI" else "NO")) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  VARIABILI") |> ignore
    problem.Variables
    |> List.iteri (fun i v ->
        let value = if i < result.Best.Values.Length then result.Best.Values.[i] else nan
        let atBound = result.VariablesAtBound |> List.contains v.Name
        sb.AppendLine(
            sprintf "    %-24s %10s %-5s  (intervallo %s .. %s)%s"
                v.Name (f2 value) v.Unit (f2 v.Lower) (f2 v.Upper)
                (if atBound then "   <== AL BORDO DELLA RICERCA" else "")) |> ignore)
    sb.AppendLine() |> ignore
    sb.AppendLine("  VINCOLI") |> ignore
    problem.Constraints
    |> List.iteri (fun i c ->
        let value = if i < result.Best.ConstraintValues.Length then result.Best.ConstraintValues.[i] else nan
        let limit =
            match c.Min, c.Max with
            | Some m, _ -> sprintf ">= %s" (f2 m)
            | _, Some m -> sprintf "<= %s" (f2 m)
            | _ -> "-"
        let active = result.ActiveConstraints |> List.contains c.Name
        sb.AppendLine(
            sprintf "    %-24s %10s %-5s  %-10s%s"
                c.Name (f3 value) c.Unit limit
                (if active then "   <== ATTIVO: e' questo che ferma la soluzione" else "")) |> ignore)
    sb.AppendLine() |> ignore
    sb.AppendLine("  NATURA DELL'OTTIMO") |> ignore
    for note in result.Notes do
        sb.AppendLine(sprintf "    %s" note) |> ignore
    sb.AppendLine(String('=', 96)) |> ignore
    let txt = sb.ToString()
    File.WriteAllText(Path.Combine(outDir, "ottimizzazione_legacy.txt"), txt)
    printfn "%s" txt
    printfn "Search completed in %.1f s. File written to: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Constrained design search completed in %.1f s" sw.Elapsed.TotalSeconds)
    0

let runRatingMode (options: Options.ProjectOptions) (casePath: string option) (case0: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    let logger = PhaseLogger.create options
    let currentStatus = progressState "Starting rating run"
    let sw = Stopwatch.StartNew()
    logger "Rating mode started"
    let runSettings = createRunSettings options options.Calculation.CorrelationValidityWarnings
    let loadCases =
        readCaseRoot casePath [] (fun root ->
            readLoadCases root [ "rating.carichi"; "carichi" ])
    let constraints =
        readCaseRoot casePath (defaultConstraintSet case0) (readConstraintSet case0)
    let input : Rating.RatingInput =
        { BaseCase = case0
          LoadCases = loadCases
          Constraints = constraints
          RunSettings = runSettings }
    let result =
        Progress.runWithStatusSnapshot
            (fun () -> currentStatus.Value)
            20.0
            (fun () ->
                setProgress currentStatus (sprintf "Rating: %d load case(s) through the shared verification engine" (max 1 input.LoadCases.Length)) (Some 0.0)
                Rating.runWithProgress (reportStructuredProgress logger currentStatus) input)
    let sb = Text.StringBuilder()
    let csv = Text.StringBuilder()
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "RATING - %s" case0.Name) |> ignore
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "  Carichi valutati : %d" result.LoadCaseResults.Length) |> ignore
    sb.AppendLine(sprintf "  Ammissibile      : %s" (if result.Assessment.IsFeasible then "SI" else "NO")) |> ignore
    if not result.Assessment.GoverningLoadCases.IsEmpty then
        sb.AppendLine(sprintf "  Carichi governanti: %s" (String.concat ", " result.Assessment.GoverningLoadCases)) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  VINCOLI") |> ignore
    for reading in result.Assessment.ConstraintReadings do
        let verdict = if reading.Passed then "OK " else "NO "
        sb.AppendLine(
            sprintf "    [%s] %-32s %-18s limite %s  (carico %s)"
                verdict
                reading.Target.Name
                (formatMetricValue reading.Target.Key reading.Value)
                (formatConstraintLimit reading.Target)
                reading.GoverningLoadCase) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  RISULTATI PER CARICO") |> ignore
    sb.AppendLine("    nome                 duty [MW]  steam [t/h]  T gas out [C]  dP gas [mbar]  DNBR min  T met max [C]  FEI max") |> ignore
    csv.AppendLine("nome;duty_MW;steam_th;T_gas_out_C;dp_gas_mbar;dnbr_min;T_met_max_C;fei_max") |> ignore
    for load in result.LoadCaseResults do
        let design = load.Verification.Result
        let duty = summaryValue ConstraintModel.Duty design / 1e6
        let steam = summaryValue ConstraintModel.SteamProduction design * 3.6
        let tOut = kToC (summaryValue ConstraintModel.GasOutletTemperature design)
        let dpGas = summaryValue ConstraintModel.GasPressureDrop design / 100.0
        let dnbr = summaryValue ConstraintModel.MinDNBR design
        let tMetal = kToC (summaryValue ConstraintModel.MaxTubeMetalTemperature design)
        let fei = summaryValue ConstraintModel.MaxFeiRatio design
        sb.AppendLine(
            sprintf "    %-20s %10.3f %11.3f %14.3f %15.3f %9.3f %14.3f %8.3f"
                load.Spec.Name duty steam tOut dpGas dnbr tMetal fei) |> ignore
        csv.AppendLine(
            String.Join(";",
                [ load.Spec.Name
                  duty.ToString("F3", CultureInfo.InvariantCulture)
                  steam.ToString("F3", CultureInfo.InvariantCulture)
                  tOut.ToString("F3", CultureInfo.InvariantCulture)
                  dpGas.ToString("F3", CultureInfo.InvariantCulture)
                  dnbr.ToString("F3", CultureInfo.InvariantCulture)
                  tMetal.ToString("F3", CultureInfo.InvariantCulture)
                  fei.ToString("F3", CultureInfo.InvariantCulture) ])) |> ignore
    let txt = sb.ToString()
    writeTextFile outDir "rating.txt" txt
    writeTextFile outDir "rating.csv" (csv.ToString())
    result.LoadCaseResults
    |> List.map (fun load -> load.Spec.Name, load.Verification.Result)
    |> writeMechanicalInterfaceFile outDir (sprintf "MECHANICAL CALCULATION INTERFACE - RATING - %s" case0.Name)
    printfn "%s" txt
    printfn "Rating completed in %.1f s. Files written to: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Rating mode completed in %.1f s; output folder: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0

let optimizeCase (options: Options.ProjectOptions) (casePath: string option) (case0: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    let logger = PhaseLogger.create options
    let currentStatus = progressState "Starting optimize run"
    let sw = Stopwatch.StartNew()
    logger "Shared optimize mode started"
    let runSettings = createRunSettings options options.Calculation.CorrelationValidityWarnings
    let constraints =
        readCaseRoot casePath (defaultConstraintSet case0) (readConstraintSet case0)
    let loadCases =
        readCaseRoot casePath [] (fun root ->
            readLoadCases root [ "optimize.carichi"; "rating.carichi"; "carichi" ])
    let variables =
        readCaseRoot casePath (Optimize.defaultVariables case0) (fun root ->
            readOptimizeVariables root case0)
    let objective =
        readCaseRoot casePath Optimize.defaultObjective (fun root ->
            readObjectiveSet root "optimize.obiettivo" Optimize.defaultObjective)
    let maxIterations =
        readCaseRoot casePath 80 (fun root ->
            Json.tryI root "optimize.max_iterazioni" |> Option.defaultValue 80)
    let tolerance =
        readCaseRoot casePath 1e-3 (fun root ->
            Json.tryF root "optimize.tolleranza" |> Option.defaultValue 1e-3)
    let input : Optimize.OptimizeInput =
        { BaseCase = case0
          LoadCases = loadCases
          Constraints = constraints
          Variables = variables
          Objective = objective
          RunSettings = runSettings
          MaxIterations = maxIterations
          Tolerance = tolerance }
    let result =
        Progress.runWithStatusSnapshot
            (fun () -> currentStatus.Value)
            60.0
            (fun () ->
                setProgress currentStatus (sprintf "Optimize: %d variable(s), %d load case(s) through the shared verification engine" input.Variables.Length (max 1 input.LoadCases.Length)) (Some 0.0)
                Optimize.runWithProgress (reportStructuredProgress logger currentStatus) input)
    let sb = Text.StringBuilder()
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "OPTIMIZE - %s" case0.Name) |> ignore
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "  Valutazioni       : %d" result.Solver.Evaluations) |> ignore
    sb.AppendLine(sprintf "  Convergenza       : %s" (if result.Solver.Converged then "raggiunta" else "fermata al tetto")) |> ignore
    sb.AppendLine(sprintf "  Ammissibile       : %s" (if result.Best.Assessment.IsFeasible then "SI" else "NO")) |> ignore
    sb.AppendLine(sprintf "  Violazione totale : %.6f" result.Best.Assessment.TotalViolation) |> ignore
    sb.AppendLine(sprintf "  Obiettivo         : %.6f" result.Best.ObjectiveValue) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  GEOMETRIA OTTIMA") |> ignore
    for variable in input.Variables do
        let bestCurrent = variableInfo variable.Key |> fun info -> info.Current result.Best.Case
        sb.AppendLine(
            sprintf "    %-28s %12s %s"
                variable.Name
                (formatNumber bestCurrent)
                variable.Unit) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  VINCOLI") |> ignore
    for reading in result.Best.Assessment.ConstraintReadings do
        let verdict = if reading.Passed then "OK " else "NO "
        sb.AppendLine(
            sprintf "    [%s] %-32s %-18s limite %s  (carico %s)"
                verdict
                reading.Target.Name
                (formatMetricValue reading.Target.Key reading.Value)
                (formatConstraintLimit reading.Target)
                reading.GoverningLoadCase) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  CARICHI DELLA GEOMETRIA OTTIMA") |> ignore
    for load in result.Best.LoadCaseResults do
        let design = load.Verification.Result
        sb.AppendLine(
            sprintf "    %-18s duty %8.3f MW | steam %8.3f t/h | T gas out %8.3f C | dP gas %8.3f mbar"
                load.Spec.Name
                (summaryValue ConstraintModel.Duty design / 1e6)
                (summaryValue ConstraintModel.SteamProduction design * 3.6)
                (kToC (summaryValue ConstraintModel.GasOutletTemperature design))
                (summaryValue ConstraintModel.GasPressureDrop design / 100.0)) |> ignore
    if not result.Solver.ActiveConstraints.IsEmpty then
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "  Vincoli attivi del solver: %s" (String.concat ", " result.Solver.ActiveConstraints)) |> ignore
    if not result.Solver.Notes.IsEmpty then
        sb.AppendLine() |> ignore
        sb.AppendLine("  NOTE DEL SOLVER") |> ignore
        for note in result.Solver.Notes do
            sb.AppendLine(sprintf "    %s" note) |> ignore
    let txt = sb.ToString()
    writeTextFile outDir "ottimizzazione.txt" txt
    result.Best.LoadCaseResults
    |> List.map (fun load -> load.Spec.Name, load.Verification.Result)
    |> writeMechanicalInterfaceFile outDir (sprintf "MECHANICAL CALCULATION INTERFACE - OPTIMIZE - %s" case0.Name)
    printfn "%s" txt
    printfn "Optimize completed in %.1f s. Files written to: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Shared optimize mode completed in %.1f s; output folder: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0

let runDesignMode (options: Options.ProjectOptions) (casePath: string option) (case0: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    let logger = PhaseLogger.create options
    let currentStatus = progressState "Starting greenfield design run"
    let sw = Stopwatch.StartNew()
    logger "Greenfield design mode started"
    let runSettings = createRunSettings options options.Calculation.CorrelationValidityWarnings
    let constraints =
        readCaseRoot casePath (defaultConstraintSet case0) (readConstraintSet case0)
    let loadCases =
        readCaseRoot casePath [] (fun root ->
            readLoadCases root [ "design.carichi"; "rating.carichi"; "carichi" ])
    let objective =
        readCaseRoot casePath Optimize.defaultObjective (fun root ->
            readObjectiveSet root "design.obiettivo" Optimize.defaultObjective)
    let space =
        readCaseRoot casePath
            ({ TubeCounts = []
               TubeLengthsM = []
               FerruleLengthsMm = []
               ShellInnerDiametersM = []
               TubeSizeOptions = []
               TubePitchesM = []
               DrumCenterlineHeightsM = [] } : GreenfieldDesign.DesignSpace)
            readDesignSpace
    let input : GreenfieldDesign.DesignInput =
        { TemplateCase = case0
          LoadCases = loadCases
          Constraints = constraints
          Objective = objective
          Space = space
          RunSettings = runSettings }
    let result =
        Progress.runWithStatusSnapshot
            (fun () -> currentStatus.Value)
            60.0
            (fun () ->
                setProgress currentStatus "Design: exploring the configured candidate space through the shared verification engine" (Some 0.0)
                GreenfieldDesign.runWithProgress (reportStructuredProgress logger currentStatus) input)
    let sb = Text.StringBuilder()
    let best = result.Best
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "DESIGN - %s" case0.Name) |> ignore
    sb.AppendLine(String('=', 96)) |> ignore
    sb.AppendLine(sprintf "  Candidati valutati : %d" result.Evaluations) |> ignore
    sb.AppendLine(sprintf "  Ammissibile        : %s" (if best.Assessment.IsFeasible then "SI" else "NO")) |> ignore
    sb.AppendLine(sprintf "  Violazione totale  : %.6f" best.Assessment.TotalViolation) |> ignore
    sb.AppendLine(sprintf "  Obiettivo          : %.6f" best.ObjectiveValue) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  MIGLIOR GEOMETRIA") |> ignore
    sb.AppendLine(sprintf "    numero tubi                %d" best.Case.Tube.NTubes) |> ignore
    sb.AppendLine(sprintf "    diametro esterno tubi      %s mm" (formatNumber (best.Case.Tube.Do * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    lunghezza tubi             %s m" (formatNumber best.Case.Tube.Length)) |> ignore
    sb.AppendLine(sprintf "    lunghezza ferrula          %s mm" (formatNumber ((best.Case.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)) * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    diametro interno mantello  %s mm" (formatNumber (best.Case.Tube.ShellId * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    passo tubi                %s mm" (formatNumber (best.Case.Tube.Pitch * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    quota drum                %s m" (formatNumber best.Case.Loop.DzDrumWhb)) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  VINCOLI DEL MIGLIOR CANDIDATO") |> ignore
    for reading in best.Assessment.ConstraintReadings do
        let verdict = if reading.Passed then "OK " else "NO "
        sb.AppendLine(
            sprintf "    [%s] %-32s %-18s limite %s  (carico %s)"
                verdict
                reading.Target.Name
                (formatMetricValue reading.Target.Key reading.Value)
                (formatConstraintLimit reading.Target)
                reading.GoverningLoadCase) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  SHORTLIST") |> ignore
    for candidate in result.Shortlist do
        sb.AppendLine(
            sprintf "    %s | obj %.6f | viol %.6f | tubi %d | OD %.1f mm | L %.3f m | ferrula %.1f mm | mantello %.1f mm"
                (if candidate.Assessment.IsFeasible then "OK " else "NO ")
                candidate.ObjectiveValue
                candidate.Assessment.TotalViolation
                candidate.Case.Tube.NTubes
                (candidate.Case.Tube.Do * 1000.0)
                candidate.Case.Tube.Length
                ((candidate.Case.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)) * 1000.0)
                (candidate.Case.Tube.ShellId * 1000.0)) |> ignore
    let txt = sb.ToString()
    writeTextFile outDir "design.txt" txt
    result.Best.LoadCaseResults
    |> List.map (fun load -> load.Spec.Name, load.Verification.Result)
    |> writeMechanicalInterfaceFile outDir (sprintf "MECHANICAL CALCULATION INTERFACE - DESIGN - %s" case0.Name)
    printfn "%s" txt
    printfn "Design completed in %.1f s. Files written to: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    logger (sprintf "Greenfield design mode completed in %.1f s; output folder: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir))
    0

let writeDefaultOptions path =
    let dir = Path.GetDirectoryName(Path.GetFullPath path)
    if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
    Options.save path Options.defaultOptions
    printfn "Project options written to: %s" (Path.GetFullPath path)
    0
let githubPlan optionsPath =
    let opts = Options.load optionsPath
    let plan = GitHubTransfer.plan opts
    printfn "GitHub transfer plan"
    printfn "  repository: %s" (if String.IsNullOrWhiteSpace plan.RepositoryUrl then "(not set)" else plan.RepositoryUrl)
    printfn "  branch:     %s" plan.Branch
    printfn "  commit:     %s" plan.CommitMessage
    printfn ""
    for c in plan.Commands do
        printfn "  %s" c
    0
let githubPush optionsPath =
    let opts = Options.load optionsPath
    match GitHubTransfer.execute (Directory.GetCurrentDirectory()) opts with
    | Ok output ->
        printfn "GitHub transfer completed."
        if not (String.IsNullOrWhiteSpace output) then printfn "%s" output
        0
    | Error err ->
        eprintfn "GitHub transfer failed: %s" err
        3

let private writeSulphurTable (path: string) (pressureBara: float) (sAtoms: float) (inertMols: float)
                              (tMinC: float) (tMaxC: float) (stepC: float) =
    let pPa = barToPa pressureBara
    let sb = Text.StringBuilder()
    sb.AppendLine("T[C];p_sat_total[Pa];p_sulphur_dry[Pa];p_sulphur_eq[Pa];y_sulphur_eq[-];mean_atomicity_eq[-];nS2_eq[mol];nS6_eq[mol];nS8_eq[mol];condensing[-];condensed_atoms[mol];condensed_fraction[-];mu_liq[Pa.s]") |> ignore
    for tC in floatGrid tMinC tMaxC stepC do
        let tK = cToK tC
        let dry = Sulphur.speciate tK pPa sAtoms inertMols
        let cond = Sulphur.condenserState tK pPa sAtoms inertMols
        let vap = cond.Vapour
        let values =
            [ tC
              Sulphur.pSatTotal tK
              dry.PS2 + dry.PS6 + dry.PS8
              cond.PSulphur
              vap.YSulphur
              vap.MeanAtomicity
              vap.NS2
              vap.NS6
              vap.NS8
              (if cond.Condensing then 1.0 else 0.0)
              cond.NCondensed
              cond.CondensedFraction
              Sulphur.muLiquid tK ]
            |> List.map (fun v -> v.ToString("G6", CultureInfo.InvariantCulture))
        sb.AppendLine(String.Join(";", values)) |> ignore
    let dir = Path.GetDirectoryName(Path.GetFullPath path)
    if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
    File.WriteAllText(path, sb.ToString())
    let hotT = max tMinC tMaxC
    let hot = Sulphur.speciate (cToK hotT) pPa sAtoms inertMols
    let pSulphurHot = hot.PS2 + hot.PS6 + hot.PS8
    let dewC = kToC (Sulphur.dewPoint pSulphurHot)
    printfn "Tabella zolfo %g-%g C (passo %g C) scritta in %s" (min tMinC tMaxC) (max tMinC tMaxC) stepC (Path.GetFullPath path)
    printfn "  p = %.3f bar(a), S-atomi = %.6g mol, inerti = %.6g mol" pressureBara sAtoms inertMols
    printfn "  Hot-end dry sulphur partial pressure = %.3f Pa, dew point = %.1f C" pSulphurHot dewC
    0

[<EntryPoint>]
let main argv =
    let args = List.ofArray argv
    /// Short form printed on a usage error, where the user needs the shape of the
    /// command line and not the manual.
    let printUsage () =
        printfn "Usage:"
        printfn "  whb [case.json] [--out <folder>] [--options <whb.options.json>]"
        printfn "  whb --template [file.json]"
        printfn "  whb --options-template [file.json]"
        printfn "  whb --selftest"
        printfn "  whb --steamtable [file.csv] [--tmin <C>] [--tmax <C>] [--step <C>]"
        printfn "  whb --sulphur [file.csv] [--pressure-bara <bar>] [--s-atoms-mols <mol>] [--inert-mols <mol>]"
        printfn "                 [--tmin <C>] [--tmax <C>] [--step <C>]"
        printfn "  whb --sulphur-condenser [case.json] [--out <folder>]"
        printfn "  whb --rating [case.json] [--out <folder>]"
        printfn "  whb --design [case.json] [--out <folder>]"
        printfn "  whb --loads [case.json] [--out <folder>]"
        printfn "  whb --sizing [case.json] [--out <folder>]"
        printfn "  whb --optimize [case.json] [--out <folder>]"
        printfn "  whb --optimize-legacy [case.json] [--out <folder>]"
        printfn "  whb --github-plan [options.json]"
        printfn "  whb --github-push [options.json]"
        printfn ""
        printfn "If no case file is provided, the reference case is used."
        printfn "Run 'whb --help' for the full list of commands and options."

    /// Full manual: every command, every flag, every options-file key and every exit
    /// code the program can return.
    let printHelp () =
        let rule = String('-', 78)
        printfn "WHB / PGC - thermal, hydraulic and diagnostic calculations for fire-tube"
        printfn "waste heat boilers and process gas coolers."
        printfn ""
        printfn "Usage:  whb [command] [case.json] [options]"
        printfn ""
        printfn "With no command and no case file, the built-in reference case is run."
        printfn "A case file may be given to any calculation command; when omitted, the"
        printfn "reference case is used."
        printfn ""
        printfn "%s" rule
        printfn "COMMANDS"
        printfn "%s" rule
        printfn "  (none) [case.json]      Full design run: thermal, hydraulic, bypass,"
        printfn "                          vibration and mechanical checks, then all report"
        printfn "                          and CSV files. This is the normal command."
        printfn ""
        printfn "  --sizing [case.json]    Design run reported as a sizing sheet only."
        printfn "                          Writes dimensionamento.txt and nothing else."
        printfn ""
        printfn "  --rating [case.json]    Verifies one fixed geometry against one or more"
        printfn "                          configured load cases and explicit constraints."
        printfn "                          All checks go through the same shared thermal/"
        printfn "                          process plus mechanical verification engine used"
        printfn "                          by the other modes. Writes rating.txt and"
        printfn "                          rating.csv plus interfaccia_meccanica.txt."
        printfn ""
        printfn "  --optimize [case.json]  Modifies one existing geometry within explicit"
        printfn "                          variable bounds to minimize configured weight/"
        printfn "                          envelope objectives while keeping every required"
        printfn "                          load case inside the same shared verification"
        printfn "                          constraints. Writes ottimizzazione.txt plus"
        printfn "                          interfaccia_meccanica.txt."
        printfn ""
        printfn "  --design [case.json]    Explores a discrete greenfield geometry space,"
        printfn "                          starting from the non-varied details of the case"
        printfn "                          and selecting the best candidate under the same"
        printfn "                          shared verification engine and constraints."
        printfn "                          Writes design.txt plus interfaccia_meccanica.txt."
        printfn ""
        printfn "  --loads [case.json]     Partial-load campaign at 50, 60, 70, 80, 90, 100"
        printfn "                          and 110 %% of gas flow, on a reduced 40 x 8 grid."
        printfn "                          Writes carichi.txt and carichi.csv."
        printfn ""
        printfn "  --optimize-legacy       Legacy constrained search for the largest duty that still"
        printfn "                          satisfies DNBR, metal temperature, gas pressure"
        printfn "                          drop and flow-induced vibration, moving ferrule"
        printfn "                          length and tube length. Writes ottimizzazione_legacy.txt,"
        printfn "                          which reports not only where the optimum is but"
        printfn "                          WHAT HOLDS IT THERE: an active constraint, the edge"
        printfn "                          of the search range, a genuine interior stationary"
        printfn "                          point, or no feasible point at all."
        printfn "                          Every evaluation is a full coupled solve, so this"
        printfn "                          takes minutes, not seconds."
        printfn ""
        printfn "  --selftest              Check the installed correlations and property"
        printfn "                          functions against published reference values."
        printfn "                          Writes nothing; exits non-zero on a mismatch."
        printfn ""
        printfn "  --steamtable [file.csv] Write a saturation table from tmin to tmax."
        printfn "                          Default file name: steam_saturation_table.csv"
        printfn "                          Default range: 20 to 310 degC every 10 degC."
        printfn ""
        printfn "  --sulphur [file.csv]    Write a standalone sulphur-process sweep:"
        printfn "                          S2/S6/S8 equilibrium, total sulphur saturation"
        printfn "                          pressure, onset of condensation and condensed"
        printfn "                          fraction against temperature."
        printfn "                          Default file name: sulphur_table.csv"
        printfn "                          Defaults: 1.7 bar(a), 8 mol S-atoms, 100 mol"
        printfn "                          inerts, 120 to 350 degC every 10 degC."
        printfn ""
        printfn "  --sulphur-condenser     Dedicated Claus sulphur-condenser run."
        printfn "                          Reads the condensatore_zolfo section of the case"
        printfn "                          file. If usa_uscita_whb = true, it first solves"
        printfn "                          the base WHB and then feeds the solved mixed"
        printfn "                          outlet gas into the dedicated condenser module."
        printfn "                          Otherwise it runs on condensatore_zolfo.gas_ingresso"
        printfn "                          only. Writes sulphur_condenser.txt and"
        printfn "                          sulphur_condenser_profile.csv."
        printfn ""
        printfn "  --template [file.json]  Write a commented case template."
        printfn "                          Default file name: case.json"
        printfn ""
        printfn "  --options-template [f]  Write a project options file with the documented"
        printfn "                          defaults. Default file name: whb.options.json"
        printfn ""
        printfn "  --github-plan [opt]     Print the git commands the transfer would run,"
        printfn "                          without running any of them."
        printfn ""
        printfn "  --github-push [opt]     Execute that transfer."
        printfn ""
        printfn "  --help, -h              This text."
        printfn ""
        printfn "%s" rule
        printfn "OPTIONS"
        printfn "%s" rule
        printfn "  --out <folder>          Output folder. Overrides folders.resultsFolder"
        printfn "                          from the options file. Created if missing."
        printfn ""
        printfn "  --options <file>        Project options file to read."
        printfn "                          Default: whb.options.json in the current folder."
        printfn "                          Keys absent from the file keep their documented"
        printfn "                          default, so a partial file is safe."
        printfn ""
        printfn "Both options may be combined with any calculation command."
        printfn ""
        printfn "%s" rule
        printfn "PROJECT OPTIONS FILE (whb.options.json)"
        printfn "%s" rule
        printfn "  folders.resultsFolder           Default output folder."
        printfn "  folders.tempFolder              Temporary and preflight files."
        printfn "  folders.casesFolder             Convention for case files."
        printfn "  folders.databasesFolder         Convention for property databases."
        printfn "  folders.reportsFolder           Convention for report material."
        printfn "  folders.packagesFolder          Convention for package artifacts."
        printfn ""
        printfn "  logging.enabled                 Timestamped phase logging. Default true,"
        printfn "                                  and active on every calculation command."
        printfn "  logging.logFile                 Log file path. Default logs/whb-run.log"
        printfn ""
        printfn "  reporting.generateFullReport    Write report.txt. Default true."
        printfn "  reporting.generateHtmlReport    Write report.html. Default true."
        printfn ""
        printfn "  calculation.axialSections       Axial grid sections. Default 90."
        printfn "  calculation.verticalBands       Vertical bands. Default 12."
        printfn "  calculation.parallelism         Bypass-map points solved concurrently."
        printfn "                                  Changes run time only, never results."
        printfn "                                  Use 1 to force a sequential run."
        printfn "                                  Default: processor count."
        printfn "  calculation.bypassMapMode       adaptive | fast | full | fixed."
        printfn "                                  Because the map is solved concurrently,"
        printfn "                                  'full' costs little more than 'adaptive'."
        printfn "  calculation.bypassTargetToleranceK"
        printfn "                                  Tolerance on the target mixed outlet"
        printfn "                                  temperature. Default 0.5 K."
        printfn "  calculation.gasPropertyCache    Reuse repeated gas-property evaluations."
        printfn "  calculation.correlationValidityWarnings"
        printfn "                                  Raise findings when a correlation is used"
        printfn "                                  outside its usual validity range."
        printfn "  calculation.useRealGas          Legacy switch; prefer gas.modello_gas in"
        printfn "                                  the case file."
        printfn "  calculation.strictValidation    Stricter input consistency checks."
        printfn "  calculation.dutyToleranceFraction"
        printfn "                                  Duty tolerance for acceptance checks."
        printfn ""
        printfn "  github.*                        Repository, branch and commit settings"
        printfn "                                  used by --github-plan / --github-push."
        printfn ""
        printfn "%s" rule
        printfn "CASE FILE"
        printfn "%s" rule
        printfn "  The JSON case file uses engineering datasheet language, grouped in"
        printfn "  sections: gas, vapore, tubi, ferrula, circuito, drum, bypass, materiali."
        printfn "  Mode-specific optional sections are: vincoli, rating, optimize, design."
        printfn "  Start from 'whb --template case.json'; the full field list is in"
        printfn "  docs/INPUT_SCHEMA.md."
        printfn ""
        printfn "  Pressures ending in _bara are absolute; pressure drops are differential."
        printfn ""
        printfn "%s" rule
        printfn "OUTPUT FILES"
        printfn "%s" rule
        printfn "  report.txt / report.html   Full engineering report."
        printfn "  criticita.txt              Findings and warnings, most severe first."
        printfn "  pds_comparison.txt/.csv    Comparison against the client datasheet."
        printfn "  inventory_summary.txt/.csv Water volumes and estimated metal weights."
        printfn "  interfaccia_meccanica.txt  Prepared interface for future mechanical"
        printfn "                             code calculations."
        printfn "  celle.csv                  Cell-by-cell thermal field."
        printfn "  profilo_assiale.csv        Axial profiles."
        printfn "  tensioni.csv               Stress field."
        printfn "  valvola_bypass.csv         Bypass valve sweep."
        printfn "  vibrazioni.txt             Vibration screening per band."
        printfn "  maldistribuzione.txt       Maldistribution sensitivity."
        printfn "  dimensionamento.txt        Sizing sheet (--sizing, and normal runs)."
        printfn "  rating.txt / rating.csv    Shared-engine geometry rating."
        printfn "  carichi.txt / carichi.csv  Partial-load curves (--loads)."
        printfn "  ottimizzazione.txt         Shared-engine optimize result (--optimize)."
        printfn "  ottimizzazione_legacy.txt  Legacy maximize-duty search (--optimize-legacy)."
        printfn "  design.txt                 Shared-engine greenfield design result (--design)."
        printfn "  sulphur_table.csv          Standalone sulphur sweep (--sulphur)."
        printfn "  sulphur_condenser.txt      Dedicated Claus sulphur-condenser report."
        printfn "  sulphur_condenser_profile.csv"
        printfn "                             Axial profile for the dedicated sulphur condenser."
        printfn ""
        printfn "%s" rule
        printfn "EXIT CODES"
        printfn "%s" rule
        printfn "  0   Success."
        printfn "  1   Unhandled error; the message is printed on stderr."
        printfn "  2   Usage error: unknown option, or case file not found."
        printfn "  3   GitHub transfer failed."
        printfn "  4   Invalid JSON in the case or options file."
        printfn "  5   File or folder access error."
        printfn ""
        printfn "%s" rule
        printfn "EXAMPLES"
        printfn "%s" rule
        printfn "  whb                                    Run the reference case."
        printfn "  whb my-case.json --out results/run1     Run a case into a folder."
        printfn "  whb --template my-case.json            Start a new case file."
        printfn "  whb my-case.json --options prj.json    Use a specific options file."
        printfn "  whb --rating my-case.json              Rate one geometry on configured loads."
        printfn "  whb --optimize my-case.json            Optimize an existing geometry."
        printfn "  whb --design my-case.json              Explore a greenfield geometry space."
        printfn "  whb --loads my-case.json               Partial-load curves."
        printfn "  whb --optimize-legacy my-case.json     Legacy maximize-duty search."
        printfn "  whb --selftest                         Verify the installation."
        printfn "  whb --sulphur sulphur.csv --pressure-bara 1.7 --s-atoms-mols 8 --inert-mols 100"
        printfn "  whb --sulphur-condenser claus-case.json --out results/condenser"
        printfn ""
        printfn "This software is a design aid. It is not a certified pressure-vessel or"
        printfn "boiler-code tool and does not replace code calculations or vendor"
        printfn "verification."
    let getOpt name def =
        let rec go = function
            | a :: b :: _ when a = name -> b
            | _ :: rest -> go rest
            | [] -> def
        go args
    let optionsPath = getOpt "--options" "whb.options.json"
    let projectOptions = Options.load optionsPath
    let outDir = getOpt "--out" projectOptions.Folders.ResultsFolder
    try
        match args with
        | [] | "--out" :: _ | "--options" :: _ ->
            printfn "No case file provided: running the reference case.\n"
            runCase projectOptions None Defaults.referenceCase outDir
        | "--help" :: _ | "-h" :: _ ->
            printHelp ()
            0
        | "--template" :: rest ->
            let f = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "case.json"
            let dir = Path.GetDirectoryName(Path.GetFullPath f)
            if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(f, template)
            printfn "Template written to %s" (Path.GetFullPath f)
            0
        | "--selftest" :: _ -> selfTest ()
        | "--steamtable" :: rest ->
            let f = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "steam_saturation_table.csv"
            let num name def =
                match getOpt name "" with
                | "" -> def
                | s ->
                    match tryParseFloat s with
                    | Some v -> v
                    | _ -> def
            let tMin = num "--tmin" 20.0
            let tMax = num "--tmax" 310.0
            let step = num "--step" 10.0
            let table = Steam.saturationTable tMin tMax step
            let sb = Text.StringBuilder()
            sb.AppendLine("T[C];Psat[bara];rhoL[kg/m3];rhoV[kg/m3];hL[kJ/kg];hV[kJ/kg];hfg[kJ/kg];cpL[kJ/kgK];cpV[kJ/kgK];muL[uPa.s];muV[uPa.s];kL[W/mK];kV[W/mK];sigma[mN/m];PrL[-];PrV[-]") |> ignore
            for s in table do
                sb.AppendLine(
                    String.Join(";",
                        [ kToC s.Tsat; paToBar s.P; s.RhoL; s.RhoV
                          s.HL / 1e3; s.HV / 1e3; s.Hfg / 1e3
                          s.CpL / 1e3; s.CpV / 1e3
                          s.MuL * 1e6; s.MuV * 1e6; s.KL; s.KV
                          s.Sigma * 1e3; s.PrL; s.PrV ]
                        |> List.map (fun v -> v.ToString("G6", CultureInfo.InvariantCulture)))) |> ignore
            let dir = Path.GetDirectoryName(Path.GetFullPath f)
            if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
            File.WriteAllText(f, sb.ToString())
            printfn "Tabella di saturazione %g-%g C (passo %g C) scritta in %s" tMin tMax step (Path.GetFullPath f)
            0
        | "--sulphur" :: rest ->
            let f = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "sulphur_table.csv"
            let num name def =
                match getOpt name "" with
                | "" -> def
                | s ->
                    match tryParseFloat s with
                    | Some v -> v
                    | _ -> def
            let pressureBara = num "--pressure-bara" 1.7
            let sAtoms = num "--s-atoms-mols" 8.0
            let inertMols = num "--inert-mols" 100.0
            let tMin = num "--tmin" 120.0
            let tMax = num "--tmax" 350.0
            let step = num "--step" 10.0
            writeSulphurTable f pressureBara sAtoms inertMols tMin tMax step
        | "--sulphur-condenser" :: rest ->
            match rest |> List.filter (fun x -> x <> "--out" && x <> outDir && x <> "--options" && x <> optionsPath) with
            | f :: _ when File.Exists f -> runSulphurCondenserCase projectOptions (Some f) (loadCase f) outDir
            | f :: _ when not (f.StartsWith("--")) ->
                    eprintfn "Case file not found: %s" f
                    raise (FileNotFoundException("Case file not found", f))
            | _ -> runSulphurCondenserCase projectOptions None Defaults.referenceCase outDir
        | "--options-template" :: rest ->
            let f = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "whb.options.json"
            writeDefaultOptions f
        | "--github-plan" :: rest ->
            let f = match rest with | x :: _ when File.Exists x -> x | _ -> "whb.options.json"
            githubPlan f
        | "--github-push" :: rest ->
            let f = match rest with | x :: _ when File.Exists x -> x | _ -> "whb.options.json"
            githubPush f
        | "--rating" :: rest ->
            let casePath, c = resolveCaseArg rest outDir optionsPath
            runRatingMode projectOptions casePath c outDir
        | "--optimize" :: rest ->
            let casePath, c = resolveCaseArg rest outDir optionsPath
            optimizeCase projectOptions casePath c outDir
        | "--design" :: rest ->
            let casePath, c = resolveCaseArg rest outDir optionsPath
            runDesignMode projectOptions casePath c outDir
        | "--optimize-legacy" :: rest ->
            let _, c = resolveCaseArg rest outDir optionsPath
            optimizeCaseLegacy projectOptions c outDir
        | "--sizing" :: rest ->
            let _, c = resolveCaseArg rest outDir optionsPath
            sizingOnly projectOptions c outDir
        | "--loads" :: rest ->
            let _, c = resolveCaseArg rest outDir optionsPath
            loadCurves projectOptions c outDir
        | opt :: _ when opt.StartsWith("--") ->
            eprintfn "Unknown option: %s" opt
            printUsage ()
            2
        | file :: _ when File.Exists file -> runCase projectOptions (Some file) (loadCase file) outDir
        | x :: _ ->
            eprintfn "File not found: %s" x
            printUsage ()
            2
    with
    | :? JsonException as ex ->
        eprintfn "Invalid JSON: %s" ex.Message
        4
    | :? IOException as ex ->
        eprintfn "File error: %s" ex.Message
        5
    | ex ->
        eprintfn "Error: %s" ex.Message
        1


