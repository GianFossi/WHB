module Whb.Cli.CaseLoader

open System
open System.IO
open System.Text.Json
open System.Globalization
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Whb.Core.Options
open Whb.Cli

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
