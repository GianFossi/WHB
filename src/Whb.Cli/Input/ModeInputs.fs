module Whb.Cli.ModeInputs

open System
open System.IO
open System.Text.Json
open System.Globalization
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Whb.Core.Options

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

let createRunSettings (options: Options.ProjectOptions) (correlationValidityWarnings: bool) : Design.RunSettings =
    { BypassMapMode = options.Calculation.BypassMapMode
      BypassTargetToleranceK = options.Calculation.BypassTargetToleranceK
      GasPropertyCache = options.Calculation.GasPropertyCache
      CorrelationValidityWarnings = correlationValidityWarnings
      Parallelism = max 1 options.Calculation.Parallelism }

let readCaseRoot (casePath: string option) (fallback: 'T) (reader: JsonElement -> 'T) =
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

let readLoadCases (root: JsonElement) (paths: string list) =
    match tryArrayAtPath root paths with
    | Some items when not (List.isEmpty items) -> items |> List.map loadCaseSpecAt
    | _ -> []

let defaultConstraintSet (caseIn: DesignCase) =
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

let readConstraintSet (caseIn: DesignCase) (root: JsonElement) : ConstraintModel.ConstraintSet =
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

let readObjectiveSet (root: JsonElement) (path: string) (fallback: Optimize.ObjectiveSet) : Optimize.ObjectiveSet =
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
        match variableInfos |> List.tryFind (fun candidate -> candidate.Aliases |> List.exists (fun alias -> lowerText alias = lowerText keyText)) with
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

let readOptimizeVariables (root: JsonElement) (caseIn: DesignCase) =
    let defaults =
        Optimize.defaultVariables caseIn
        |> List.map (fun item -> item.Key, item)
        |> Map.ofList
    match Json.tryArrayElements root "optimize.variabili" with
    | Some items when not (List.isEmpty items) -> items |> List.map (parseVariable caseIn defaults)
    | _ -> Optimize.defaultVariables caseIn

let readDesignSpace (root: JsonElement) : GreenfieldDesign.DesignSpace =
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

let formatNumber (value: float) =
    if Double.IsNaN value then "n/a"
    else value.ToString("F3", CultureInfo.InvariantCulture)

let formatMetricValue (key: ConstraintModel.ConstraintValueKey) (value: float) =
    let info = metricInfo key
    if Double.IsNaN value then "n/a"
    else sprintf "%s %s" (formatNumber (info.ToCli value)) info.Unit

let formatConstraintLimit (target: ConstraintModel.ConstraintTarget) =
    let info = metricInfo target.Key
    let f x = formatNumber (info.ToCli x)
    match target.Limit with
    | ConstraintModel.Min x -> sprintf ">= %s %s" (f x) info.Unit
    | ConstraintModel.Max x -> sprintf "<= %s %s" (f x) info.Unit
    | ConstraintModel.Range(lo, hi) -> sprintf "%s .. %s %s" (f lo) (f hi) info.Unit

let summaryValue key (result: DesignResult) =
    ConstraintReaders.tryFindValue key result
    |> Option.map (fun x -> x.Value)
    |> Option.defaultValue nan

let variableCurrentValue (key: Optimize.VariableKey) (caseIn: DesignCase) =
    variableInfo key |> fun info -> info.Current caseIn
