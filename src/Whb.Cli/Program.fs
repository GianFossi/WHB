/// <summary>
/// Provides whb.cli.program functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Whb.Cli.Program

open System
open System.IO
open System.Text.Json
open System.Globalization
open System.Threading
open System.Threading.Tasks
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Whb.Core.Options

/// <summary>
/// Provides json functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Json =

    /// <summary>
    /// Calculates or returns tryPath for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private tryPath (root: JsonElement) (path: string) =
        let parts = path.Split('.')
        let mutable cur = root
        let mutable ok = true
        for p in parts do
            if ok then
                if cur.ValueKind = JsonValueKind.Object then
                    match cur.TryGetProperty p with
                    | true, v -> cur <- v
                    | _ -> ok <- false
                else ok <- false
        if ok then Some cur else None

    /// <summary>
    /// Calculates or returns f for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f (root: JsonElement) (path: string) (def: float) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Number -> v.GetDouble()
        | _ -> def

    /// <summary>
    /// Calculates or returns i for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let i (root: JsonElement) (path: string) (def: int) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Number -> v.GetInt32()
        | _ -> def

    /// <summary>
    /// Calculates or returns b for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let b (root: JsonElement) (path: string) (def: bool) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.True -> true
        | Some v when v.ValueKind = JsonValueKind.False -> false
        | _ -> def

    /// <summary>
    /// Calculates or returns s for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let s (root: JsonElement) (path: string) (def: string) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.String -> v.GetString()
        | _ -> def

    /// <summary>
    /// Calculates or returns composition for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let composition (root: JsonElement) (def: GasProps.Composition) =
        match tryPath root "gas.composizione" with
        | Some v when v.ValueKind = JsonValueKind.Object ->
            let map =
                [ "H2", GasProps.H2; "N2", GasProps.N2; "O2", GasProps.O2
                  "CO", GasProps.CO; "CO2", GasProps.CO2; "CH4", GasProps.CH4
                  "H2O", GasProps.H2O; "AR", GasProps.Ar; "NH3", GasProps.NH3 ] |> Map.ofList
            let res =
                v.EnumerateObject()
                |> Seq.choose (fun p ->
                    match map.TryFind(p.Name.ToUpperInvariant()) with
                    | Some sp -> Some(sp, p.Value.GetDouble())
                    | None -> None)
                |> List.ofSeq
            if res.IsEmpty then def else res
        | _ -> def

    /// <summary>
    /// Calculates or returns tryArray for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let tryArray (root: JsonElement) (path: string) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Array ->
            Some(v.EnumerateArray() |> Seq.map (fun x -> x.GetDouble()) |> List.ofSeq)
        | _ -> None

    /// <summary>
    /// Calculates or returns lengths for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let lengths (root: JsonElement) (path: string) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Array ->
            let res =
                v.EnumerateArray()
                |> Seq.map (fun e ->
                    let g name d =
                        match e.TryGetProperty(name: string) with
                        | true, x when x.ValueKind = JsonValueKind.Number -> x.GetDouble()
                        | _ -> d
                    (g "frazione" 1.0, g "lunghezza_mm" 200.0 / 1000.0))
                |> List.ofSeq
            if res.IsEmpty then None else Some res
        | _ -> None

    /// <summary>
    /// Calculates or returns lines for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let lines (root: JsonElement) (path: string) (def: Piping.Line list) =
        match tryPath root path with
        | Some v when v.ValueKind = JsonValueKind.Array ->
            let res =
                v.EnumerateArray()
                |> Seq.map (fun e ->
                    let gd name d =
                        match e.TryGetProperty(name: string) with
                        | true, x when x.ValueKind = JsonValueKind.Number -> x.GetDouble()
                        | _ -> d
                    let gs name d =
                        match e.TryGetProperty(name: string) with
                        | true, x when x.ValueKind = JsonValueKind.String -> x.GetString()
                        | _ -> d
                    let straights =
                        match e.TryGetProperty "diritti_mm" with
                        | true, a when a.ValueKind = JsonValueKind.Array ->
                            a.EnumerateArray() |> Seq.map (fun x -> x.GetDouble() / 1000.0) |> List.ofSeq
                        | _ -> []
                    let elbows =
                        match e.TryGetProperty "curve" with
                        | true, a when a.ValueKind = JsonValueKind.Array ->
                            a.EnumerateArray()
                            |> Seq.map (fun c ->
                                let g2 n d =
                                    match c.TryGetProperty(n: string) with
                                    | true, x when x.ValueKind = JsonValueKind.Number -> x.GetDouble()
                                    | _ -> d
                                ({ AngleDeg = g2 "gradi" 90.0
                                   ROverD = g2 "r_su_d" 1.5
                                   Count = int (g2 "n" 1.0) } : Piping.Elbow))
                            |> List.ofSeq
                        | _ -> []
                    ({ Tag = gs "tag" "?"
                       Nps = gs "nps" "?"
                       Id = gd "id_mm" 400.0 / 1000.0
                       Count = int (gd "n" 1.0)
                       Straights = straights
                       Elbows = elbows
                       ExtraK = gd "k_extra" 0.0
                       ZNozzle = gd "z_m" 0.0
                       AngleDeg = gd "angolo_gradi" 0.0
                       Connected =
                         (match e.TryGetProperty "collegato" with
                          | true, x when x.ValueKind = JsonValueKind.False -> false
                          | _ -> true)
                       Note = gs "nota" "" } : Piping.Line))
                |> List.ofSeq
            if res.IsEmpty then def else res
        | _ -> def

/// <summary>
/// Provides console progress rendering for long-running WHB calculations.
/// </summary>
/// <remarks>
/// The helper shows a status bar, current task description, elapsed time, and estimated remaining time while the calculation runs.
/// </remarks>
module Progress =

    /// <summary>
    /// Formats a duration as a compact human-readable time value.
    /// </summary>
    /// <remarks>
    /// The formatted value is used by the CLI progress window and keeps output stable for redirected consoles.
    /// </remarks>
    let private formatDuration (span: TimeSpan) =
        if span.TotalHours >= 1.0 then sprintf "%02d:%02d:%02d" (int span.TotalHours) span.Minutes span.Seconds
        else sprintf "%02d:%02d" span.Minutes span.Seconds

    /// <summary>
    /// Builds a fixed-width progress bar line.
    /// </summary>
    /// <remarks>
    /// Progress is estimated from elapsed time and the configured expected duration when the operation does not report internal steps.
    /// </remarks>
    let private bar (fraction: float) =
        let width = 32
        let filled = int (Math.Round((max 0.0 (min 1.0 fraction)) * float width))
        let left = String('#', filled)
        let right = String('-', width - filled)
        sprintf "[%s%s] %3.0f%%" left right (100.0 * max 0.0 (min 1.0 fraction))

    /// <summary>
    /// Writes a progress update to the console.
    /// </summary>
    /// <remarks>
    /// Interactive consoles are updated in place; redirected output receives separate progress lines.
    /// </remarks>
    let private render (header: string) (description: string) (fraction: float) (elapsed: TimeSpan) (remaining: TimeSpan option) =
        let remainingText =
            match remaining with
            | Some value -> formatDuration value
            | None -> "estimating"
        let line1 = sprintf "%s %s" header (bar fraction)
        let line2 = sprintf "Running: %s" description
        let line3 = sprintf "Elapsed: %s | Estimated remaining: %s" (formatDuration elapsed) remainingText
        if Console.IsOutputRedirected then
            printfn "%s | %s | %s" line1 line2 line3
        else
            Console.Write("\r{0,-100}\n{1,-100}\n{2,-100}", line1, line2, line3)
            Console.SetCursorPosition(0, max 0 (Console.CursorTop - 2))

    /// <summary>
    /// Runs a calculation while showing an estimated console progress window.
    /// </summary>
    /// <remarks>
    /// The calculation itself remains unchanged; the progress estimate is time based and intended as user feedback, not a solver convergence metric.
    /// </remarks>
    let runWithStatus<'T> (description: string) (estimatedSeconds: float) (work: unit -> 'T) =
        let estimate = TimeSpan.FromSeconds(max 1.0 estimatedSeconds)
        use finished = new ManualResetEventSlim(false)
        let sw = Diagnostics.Stopwatch.StartNew()
        let task =
            Task.Run<'T>(Func<'T>(fun () ->
                try work ()
                finally finished.Set()))
        while not task.IsCompleted do
            let elapsed = sw.Elapsed
            let fraction = min 0.98 (elapsed.TotalSeconds / estimate.TotalSeconds)
            let remaining =
                if fraction > 0.0 then Some(TimeSpan.FromSeconds(max 0.0 (estimate.TotalSeconds - elapsed.TotalSeconds)))
                else None
            render "WHB status" description fraction elapsed remaining
            finished.Wait(TimeSpan.FromMilliseconds(500.0)) |> ignore
        sw.Stop()
        if not Console.IsOutputRedirected then
            Console.SetCursorPosition(0, Console.CursorTop + 2)
        render "WHB status" description 1.0 sw.Elapsed (Some TimeSpan.Zero)
        task.GetAwaiter().GetResult()


/// <summary>
/// Calculates or returns gasCorrelation for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this private helper synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let private gasCorrelation (name: string) =
    match name.ToLowerInvariant() with
    | "dittus-boelter" | "dittusboelter" | "db" -> GasSide.DittusBoelter
    | "sieder-tate" | "siedertate" | "st" -> GasSide.SiederTate
    | "colburn" -> GasSide.Colburn
    | "petukhov" | "petukhov-kirillov" -> GasSide.PetukhovKirillov
    | "hausen" -> GasSide.Hausen
    | _ -> GasSide.Gnielinski

/// <summary>
/// Calculates or returns boilCorrelation for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this private helper synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let private boilCorrelation (name: string) =
    match name.ToLowerInvariant() with
    | "cooper" -> WaterSide.Cooper
    | "rohsenow" -> WaterSide.Rohsenow
    | "gorenflo" -> WaterSide.Gorenflo
    | "cornwell" | "cornwell-houston" -> WaterSide.CornwellHouston
    | _ -> WaterSide.Mostinski

/// <summary>
/// Calculates or returns voidModel for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this private helper synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let private voidModel (name: string) =
    match name.ToLowerInvariant() with
    | "omogeneo" | "homogeneous" -> TwoPhase.Homogeneous
    | "chisholm" -> TwoPhase.ChisholmSlip
    | "smith" -> TwoPhase.Smith
    | _ -> TwoPhase.ZuberFindlay

/// <summary>
/// Calculates or returns frictionModel for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this private helper synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let private frictionModel (name: string) =
    match name.ToLowerInvariant() with
    | "omogeneo" | "homogeneous" -> TwoPhase.HomogeneousFriction
    | "lockhart" | "lockhart-martinelli" -> TwoPhase.LockhartMartinelli
    | "chisholm" -> TwoPhase.ChisholmB
    | _ -> TwoPhase.Friedel

/// <summary>
/// Calculates or returns insulK for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this private helper synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let private insulK (name: string) =
    match name.ToLowerInvariant() with
    | "fibra" | "ceramic" -> Materials.Refractory.ceramicFibre
    | "denso" | "dense" -> Materials.Refractory.castableDense
    | "leggero" | "castable" -> Materials.Refractory.castableLight
    | _ -> Materials.Refractory.saffilPaper

/// <summary>
/// Calculates or returns loadcase for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let loadCase (path: string) : DesignCase =
    /// <summary>
    /// Calculates or returns d for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let d = Defaults.referenceCase
    use fs = File.OpenRead path
    use doc = JsonDocument.Parse(fs, JsonDocumentOptions(AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip))
    /// <summary>
    /// Calculates or returns r for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let r = doc.RootElement
    /// <summary>
    /// Calculates or returns t for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let t = d.Tube
    /// <summary>
    /// Calculates or returns tube for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// <summary>
    /// Calculates or returns fr for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let fr = d.Ferrule
    /// <summary>
    /// Calculates or returns ferruleLengths for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let ferruleLengths =
        match Json.lengths r "ferrula.lunghezze" with
        | Some l -> l
        | None -> [ (1.0, Json.f r "ferrula.lunghezza_mm" 200.0 / 1000.0) ]
    /// <summary>
    /// Calculates or returns ferrule for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let ferrule =
        { Enabled = Json.b r "ferrula.presente" fr.Enabled
          Lengths = ferruleLengths
          Bore = Json.f r "ferrula.bore_mm" (fr.Bore * 1000.0) / 1000.0
          SleeveOd = Json.f r "ferrula.manicotto_od_mm" (fr.SleeveOd * 1000.0) / 1000.0
          SleeveK = (Materials.byName (Json.s r "ferrula.manicotto_materiale" "800")).K
          InsulK = insulK (Json.s r "ferrula.isolante" "saffil") }
    /// <summary>
    /// Calculates or returns g for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let g = d.Gas
    /// <summary>
    /// Calculates or returns margin for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let margin = Json.f r "gas.maggiorazione" 1.0
    /// <summary>
    /// Calculates or returns gas for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let gas =
        { Composition = Json.composition r g.Composition
          MassFlow = Json.f r "gas.portata_kgs" g.MassFlow * margin
          TIn = cToK (Json.f r "gas.t_ingresso_C" (kToC g.TIn))
          PIn = barToPa (Json.f r "gas.p_ingresso_bara" (paToBar g.PIn))
          Z = Json.f r "gas.z" g.Z
          FoulingIn = Json.f r "gas.fouling_m2KW" g.FoulingIn
          EpsWall = Json.f r "gas.emissivita_parete" g.EpsWall
          Radiation = Json.b r "gas.irraggiamento" g.Radiation
          EntranceC = Json.f r "gas.coeff_imbocco" g.EntranceC
          Correlation = gasCorrelation (Json.s r "gas.correlazione" "gnielinski")
          ShiftMode =
            match (Json.s r "gas.shift" "congelata").ToLowerInvariant() with
            | "equilibrio" -> Shift.EquilibriumAbove(cToK (Json.f r "gas.shift_t_freeze_C" 700.0))
            | "parziale" ->
                Shift.FractionalApproach(Json.f r "gas.shift_frazione" 0.3,
                                         cToK (Json.f r "gas.shift_t_freeze_C" 700.0))
            | _ -> Shift.Frozen
          MixingRule =
            match (Json.s r "gas.miscelazione" "wilke").ToLowerInvariant() with
            | "molare" | "molar" -> GasProps.MolarAverage
            | _ -> GasProps.Wilke
          RealGas = Json.b r "gas.gas_reale" g.RealGas }
    /// <summary>
    /// Calculates or returns wt for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let wt = d.Water
    /// <summary>
    /// Calculates or returns water for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let water =
        { DrumPressure = barToPa (Json.f r "vapore.pressione_bara" (paToBar wt.DrumPressure))
          FoulingOut = Json.f r "vapore.fouling_m2KW" wt.FoulingOut
          RoughnessUm = Json.f r "vapore.rugosita_um" wt.RoughnessUm
          BundleFactor = Json.f r "vapore.fattore_fascio" wt.BundleFactor
          Correlation = boilCorrelation (Json.s r "vapore.correlazione" "mostinski")
          Csf = Json.f r "vapore.csf" wt.Csf
          TFeed = cToK (Json.f r "vapore.t_alimento_C" (kToC wt.TFeed)) }
    /// <summary>
    /// Calculates or returns l for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let l = d.Loop
    /// <summary>
    /// Calculates or returns loop for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
                ConveyorCount = Json.i r "drum.convogliatori" l.Drum.ConveyorCount
                ConvDuctArea = Json.f r "drum.canale_area_m2" l.Drum.ConvDuctArea
                ConvLength = Json.f r "drum.canale_lunghezza_m" l.Drum.ConvLength
                ConvHydDia = Json.f r "drum.canale_dh_m" l.Drum.ConvHydDia
                ConvBendAngle = Json.f r "drum.canale_curvatura_gradi" l.Drum.ConvBendAngle
                ConvOutletArea = Json.f r "drum.finestra_area_m2" l.Drum.ConvOutletArea
                ConvOutletAboveLevel = Json.b r "drum.scarico_sopra_livello" l.Drum.ConvOutletAboveLevel
                ConvExtraK = Json.f r "drum.canale_k_extra" l.Drum.ConvExtraK
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
      AllowInternalRecirculation = Json.b r "ricircolo_interno" d.AllowInternalRecirculation
      BypassOpenFraction = Json.f r "bypass_frazione_aperta" d.BypassOpenFraction }

/// <summary>
/// Calculates or returns template for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
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
    "fouling_m2KW": 0.00015,
    "rugosita_um": 1.0,
    "fattore_fascio": 1.5,
    "correlazione": "mostinski",
    "csf": 0.013,
    "t_alimento_C": 250.0
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

  "bocchelli": {
    "n_riser": 0,
    "n_downcomer": 0,
    "v_downcomer_ms": 2.0,
    "rhov2_max_riser": 6000.0,
    "rhov2_max_downcomer": 3000.0
  }
}
"""

/// <summary>
/// Calculates or returns selftest for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let selfTest () =
    /// <summary>
    /// Calculates or returns ci for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let ci = CultureInfo.InvariantCulture
    /// <summary>
    /// Calculates or returns mutable for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let mutable fails = 0
    /// <summary>
    /// Calculates or returns check for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// <summary>
    /// Calculates or returns air for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let air = [ GasProps.N2, 0.79; GasProps.O2, 0.21 ]
    /// <summary>
    /// Calculates or returns p300 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let p300 = GasProps.mix air 300.0 101325.0 1.0
    check "aria 300 K rho [kg/m3]" p300.Rho 1.177 0.01
    check "aria 300 K cp [J/kg/K]" p300.Cp 1005.0 0.02
    check "aria 300 K mu [uPa s]" (p300.Mu * 1e6) 18.5 0.03
    check "aria 300 K k [W/m/K]" p300.K 0.0263 0.05

    printfn ""
    printfn "Confronto con il datasheet (miscela di riferimento)"
    /// <summary>
    /// Calculates or returns comp for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let comp = Defaults.referenceComposition
    /// <summary>
    /// Calculates or returns pin for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let pin = GasProps.mix comp (cToK 967.5) (barToPa 34.74) 1.0
    /// <summary>
    /// Calculates or returns pout for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let pout = GasProps.mix comp (cToK 355.0) (barToPa 34.44) 1.0
    check "MW miscela [kg/kmol]" (GasProps.mixMolarMass comp * 1000.0) 15.99 2e-3
    check "rho ingresso [kg/m3]" pin.Rho 5.36 0.02
    check "rho uscita [kg/m3]" pout.Rho 10.48 0.02
    check "cp ingresso [kJ/kg/K]" (pin.Cp / 1000.0) 2.353 0.05
    check "cp uscita [kJ/kg/K]" (pout.Cp / 1000.0) 2.119 0.05
    /// <summary>
    /// Calculates or returns pinM for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let pinM = GasProps.mixWith GasProps.MolarAverage comp (cToK 967.5) (barToPa 34.74) 1.0
    /// <summary>
    /// Calculates or returns poutM for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// <summary>
    /// Calculates or returns sat for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let sat = Steam.sat (barToPa 117.84)
    check "Tsat a 117.84 bar [C]" (kToC sat.Tsat) 323.3 5e-4

    printfn ""
    printfn "Gas reale: secondo coefficiente del viriale (p = 34.74 bar)"
    printfn "  %-9s %14s %10s %14s %14s" "T [K]" "B_H2O [m3/mol]" "Z mix" "h_res [kJ/kg]" "cp_res [J/kgK]"
    /// <summary>
    /// Calculates or returns mwx for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let mwx = GasProps.mixMolarMass (GasProps.normalize comp)
    for t in [ 628.0; 700.0; 850.0; 1000.0; 1240.0 ] do
        let bw = GasProps.Virial.bWater t
        let (z, hr, cpr) = GasProps.Virial.residual (GasProps.normalize comp) t (barToPa 34.74)
        printfn "  %-9.0f %14.4e %10.5f %14.2f %14.2f" t bw z (hr / mwx / 1000.0) (cpr / mwx)
    /// <summary>
    /// Calculates or returns dh_id for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dh_id =
        GasProps.enthalpyAbs comp (cToK 967.5) - GasProps.enthalpyAbs comp (cToK 355.0)
    /// <summary>
    /// Calculates or returns dh_re for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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

/// <summary>
/// Calculates or returns loadcurves for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let loadCurves (case0: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    /// <summary>
    /// Calculates or returns sb for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let sb = Text.StringBuilder()
    /// <summary>
    /// Calculates or returns ci for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let ci = CultureInfo.InvariantCulture
    /// <summary>
    /// Calculates or returns f1 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f1 (x: float) = x.ToString("F1", ci)
    /// <summary>
    /// Calculates or returns f2 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f2 (x: float) = x.ToString("F2", ci)
    /// <summary>
    /// Calculates or returns f3 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f3 (x: float) = x.ToString("F3", ci)
    /// <summary>
    /// Calculates or returns f4 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f4 (x: float) = x.ToString("F4", ci)
    /// <summary>
    /// Calculates or returns f0 for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f0 (x: float) = x.ToString("F0", ci)
    /// <summary>
    /// Calculates or returns coarse for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let coarse = { case0 with NZ = 40; NY = 8; AxialRefine = 6.0 }
    /// <summary>
    /// Calculates or returns loads for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// <summary>
    /// Calculates or returns pts for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let pts = ResizeArray<LoadPoint>()
    for l in loads do
        let c = { coarse with Gas = { coarse.Gas with MassFlow = case0.Gas.MassFlow * l } }
        let r =
            Progress.runWithStatus
                (sprintf "Partial-load calculation %.0f %%: thermal, hydraulic, bypass, vibration, and mechanical checks" (100.0 * l))
                12.0
                (fun () -> Design.run c)
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
    /// <summary>
    /// Calculates or returns bp for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let bp = case0.Bypass
    /// <summary>
    /// Calculates or returns outOfWindow for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let outOfWindow =
        pts |> Seq.filter (fun p -> p.ValveOpenDeg < bp.MinOpenDeg || p.ValveOpenDeg > bp.MaxOpenDeg) |> List.ofSeq
    sb.AppendLine("  LETTURA") |> ignore
    /// <summary>
    /// Calculates or returns para for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// <summary>
    /// Calculates or returns txt for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let txt = sb.ToString()
    printfn "%s" txt
    File.WriteAllText(Path.Combine(outDir, "carichi.txt"), txt)
    /// <summary>
    /// Calculates or returns csv for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let csv = Text.StringBuilder()
    csv.AppendLine("carico;w_gas_kgs;potenza_MW;vapore_th;T_tubi_C;T_miscelata_C;farfalla_gradi;bypass_pc;CR;q_max_kWm2;T_met_max_C;DNBR_min;alpha_max;dp_gas_mbar") |> ignore
    for p in pts do
        csv.AppendLine(String.Join(";",
            [ f2 p.LoadFraction; f2 p.GasFlow; f3 (p.Duty / 1e6); f1 (p.Steam * 3.6)
              f2 (kToC p.TOutTubes); f2 (kToC p.TOutMixed); f2 p.ValveOpenDeg
              f3 (100.0 * p.BypassFraction); f2 p.CircRatio; f1 (p.QFluxMax / 1000.0)
              f1 (kToC p.TMetalMax); f3 p.DNBRMin; f4 p.AlphaMax; f1 (p.DpGas / 100.0) ])) |> ignore
    File.WriteAllText(Path.Combine(outDir, "carichi.csv"), csv.ToString())
    0

/// <summary>
/// Calculates or returns runcase for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let runCase (case: DesignCase) (outDir: string) =
    Directory.CreateDirectory outDir |> ignore
    /// <summary>
    /// Calculates or returns sw for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let sw = Diagnostics.Stopwatch.StartNew()
    /// <summary>
    /// Calculates or returns r for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let r =
        Progress.runWithStatus
            "Design run: thermal, hydraulic, bypass, vibration, mechanical, report-preparation calculations"
            25.0
            (fun () -> Design.run case)
    sw.Stop()
    /// <summary>
    /// Calculates or returns rep for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let rep = Report.text r
    printfn "%s" rep
    File.WriteAllText(Path.Combine(outDir, "report.txt"), rep)
    /// <summary>
    /// Calculates or returns syn for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let syn = Report.synthesis r
    File.WriteAllText(Path.Combine(outDir, "criticita.txt"), syn)
    printfn "%s" syn
    File.WriteAllText(Path.Combine(outDir, "celle.csv"), Report.csvCells r)
    File.WriteAllText(Path.Combine(outDir, "profilo_assiale.csv"), Report.csvAxial r)
    File.WriteAllText(Path.Combine(outDir, "tensioni.csv"), Report.csvStress r)
    File.WriteAllText(Path.Combine(outDir, "valvola_bypass.csv"), Report.csvValve r)
    File.WriteAllText(Path.Combine(outDir, "maldistribuzione.txt"), Report.maldistributionText r)
    File.WriteAllText(Path.Combine(outDir, "vibrazioni.txt"), Report.vibrationText r)
    File.WriteAllText(Path.Combine(outDir, "report.html"), HtmlReport.build r)
    printfn "Calcolo completato in %.1f s. File scritti in: %s" sw.Elapsed.TotalSeconds (Path.GetFullPath outDir)
    0

/// <summary>
/// Calculates or returns writedefaultoptions for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let writeDefaultOptions path =
    Options.save path Options.defaultOptions
    printfn "Opzioni progetto scritte in: %s" (Path.GetFullPath path)
    0

/// <summary>
/// Calculates or returns githubplan for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let githubPlan optionsPath =
    /// <summary>
    /// Calculates or returns opts for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let opts = Options.load optionsPath
    /// <summary>
    /// Calculates or returns plan for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let plan = GitHubTransfer.plan opts
    printfn "Piano trasferimento GitHub"
    printfn "  repository: %s" (if String.IsNullOrWhiteSpace plan.RepositoryUrl then "(non impostato)" else plan.RepositoryUrl)
    printfn "  branch:     %s" plan.Branch
    printfn "  commit:     %s" plan.CommitMessage
    printfn ""
    for c in plan.Commands do
        printfn "  %s" c
    0

/// <summary>
/// Calculates or returns githubpush for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let githubPush optionsPath =
    /// <summary>
    /// Calculates or returns opts for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let opts = Options.load optionsPath
    match GitHubTransfer.execute (Directory.GetCurrentDirectory()) opts with
    | Ok output ->
        printfn "Trasferimento GitHub completato."
        if not (String.IsNullOrWhiteSpace output) then printfn "%s" output
        0
    | Error err ->
        eprintfn "Trasferimento GitHub non completato: %s" err
        3

[<EntryPoint>]
/// <summary>
/// Calculates or returns main for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
let main argv =
    /// <summary>
    /// Calculates or returns args for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let args = List.ofArray argv
    /// <summary>
    /// Calculates or returns printUsage for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let printUsage () =
        printfn "Uso:"
        printfn "  whb [caso.json] [--out <cartella>]"
        printfn "  whb --template [file.json]"
        printfn "  whb --options-template [file.json]"
        printfn "  whb --selftest"
        printfn "  whb --carichi [caso.json] [--out <cartella>]"
        printfn "  whb --github-plan [options.json]"
        printfn "  whb --github-push [options.json]"
        printfn ""
        printfn "Se il caso non viene indicato viene usato il caso di riferimento."
    /// <summary>
    /// Calculates or returns getOpt for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let getOpt name def =
        let rec go = function
            | a :: b :: _ when a = name -> b
            | _ :: rest -> go rest
            | [] -> def
        go args
    /// <summary>
    /// Calculates or returns outDir for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let outDir = getOpt "--out" "risultati"
    try
        match args with
        | [] | "--out" :: _ ->
            printfn "Nessun file di caso indicato: eseguo il caso di riferimento.\n"
            runCase Defaults.referenceCase outDir
        | "--help" :: _ | "-h" :: _ ->
            printUsage ()
            0
        | "--template" :: rest ->
            let f = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "caso.json"
            File.WriteAllText(f, template)
            printfn "Template scritto in %s" (Path.GetFullPath f)
            0
        | "--selftest" :: _ -> selfTest ()
        | "--options-template" :: rest ->
            let f = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "whb.options.json"
            writeDefaultOptions f
        | "--github-plan" :: rest ->
            let f = match rest with | x :: _ when File.Exists x -> x | _ -> "whb.options.json"
            githubPlan f
        | "--github-push" :: rest ->
            let f = match rest with | x :: _ when File.Exists x -> x | _ -> "whb.options.json"
            githubPush f
        | "--carichi" :: rest ->
            let c =
                match rest |> List.filter (fun x -> x <> "--out" && x <> outDir) with
                | f :: _ when File.Exists f -> loadCase f
                | f :: _ when not (f.StartsWith("--")) ->
                    eprintfn "File caso non trovato: %s" f
                    raise (FileNotFoundException("File caso non trovato", f))
                | _ -> Defaults.referenceCase
            loadCurves c outDir
        | opt :: _ when opt.StartsWith("--") ->
            eprintfn "Opzione non riconosciuta: %s" opt
            printUsage ()
            2
        | file :: _ when File.Exists file -> runCase (loadCase file) outDir
        | x :: _ ->
            eprintfn "File non trovato: %s" x
            printUsage ()
            2
    with
    | :? JsonException as ex ->
        eprintfn "JSON non valido: %s" ex.Message
        4
    | :? IOException as ex ->
        eprintfn "Errore file: %s" ex.Message
        5
    | ex ->
        eprintfn "Errore: %s" ex.Message
        1
