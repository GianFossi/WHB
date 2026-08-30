module Whb.Cli.CommandRunners

open System
open System.IO
open System.Globalization
open System.Diagnostics
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Types
open Whb.Core.Options
open Whb.Cli

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
        let runSettings = ModeInputs.createRunSettings options options.Calculation.CorrelationValidityWarnings
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
            let runSettings = ModeInputs.createRunSettings options options.Calculation.CorrelationValidityWarnings
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
    let runSettings = ModeInputs.createRunSettings options options.Calculation.CorrelationValidityWarnings
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
    let runSettings = ModeInputs.createRunSettings options false
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
    let runSettings = ModeInputs.createRunSettings options options.Calculation.CorrelationValidityWarnings
    let loadCases =
        ModeInputs.readCaseRoot casePath [] (fun root ->
            ModeInputs.readLoadCases root [ "rating.carichi"; "carichi" ])
    let constraints =
        ModeInputs.readCaseRoot casePath (ModeInputs.defaultConstraintSet case0) (ModeInputs.readConstraintSet case0)
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
                (ModeInputs.formatMetricValue reading.Target.Key reading.Value)
                (ModeInputs.formatConstraintLimit reading.Target)
                reading.GoverningLoadCase) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  RISULTATI PER CARICO") |> ignore
    sb.AppendLine("    nome                 duty [MW]  steam [t/h]  T gas out [C]  dP gas [mbar]  DNBR min  T met max [C]  FEI max") |> ignore
    csv.AppendLine("nome;duty_MW;steam_th;T_gas_out_C;dp_gas_mbar;dnbr_min;T_met_max_C;fei_max") |> ignore
    for load in result.LoadCaseResults do
        let design = load.Verification.Result
        let duty = ModeInputs.summaryValue ConstraintModel.Duty design / 1e6
        let steam = ModeInputs.summaryValue ConstraintModel.SteamProduction design * 3.6
        let tOut = kToC (ModeInputs.summaryValue ConstraintModel.GasOutletTemperature design)
        let dpGas = ModeInputs.summaryValue ConstraintModel.GasPressureDrop design / 100.0
        let dnbr = ModeInputs.summaryValue ConstraintModel.MinDNBR design
        let tMetal = kToC (ModeInputs.summaryValue ConstraintModel.MaxTubeMetalTemperature design)
        let fei = ModeInputs.summaryValue ConstraintModel.MaxFeiRatio design
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
    let runSettings = ModeInputs.createRunSettings options options.Calculation.CorrelationValidityWarnings
    let constraints =
        ModeInputs.readCaseRoot casePath (ModeInputs.defaultConstraintSet case0) (ModeInputs.readConstraintSet case0)
    let loadCases =
        ModeInputs.readCaseRoot casePath [] (fun root ->
            ModeInputs.readLoadCases root [ "optimize.carichi"; "rating.carichi"; "carichi" ])
    let variables =
        ModeInputs.readCaseRoot casePath (Optimize.defaultVariables case0) (fun root ->
            ModeInputs.readOptimizeVariables root case0)
    let objective =
        ModeInputs.readCaseRoot casePath Optimize.defaultObjective (fun root ->
            ModeInputs.readObjectiveSet root "optimize.obiettivo" Optimize.defaultObjective)
    let maxIterations =
        ModeInputs.readCaseRoot casePath 80 (fun root ->
            Json.tryI root "optimize.max_iterazioni" |> Option.defaultValue 80)
    let tolerance =
        ModeInputs.readCaseRoot casePath 1e-3 (fun root ->
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
        let bestCurrent = ModeInputs.variableCurrentValue variable.Key result.Best.Case
        sb.AppendLine(
            sprintf "    %-28s %12s %s"
                variable.Name
                (ModeInputs.formatNumber bestCurrent)
                variable.Unit) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  VINCOLI") |> ignore
    for reading in result.Best.Assessment.ConstraintReadings do
        let verdict = if reading.Passed then "OK " else "NO "
        sb.AppendLine(
            sprintf "    [%s] %-32s %-18s limite %s  (carico %s)"
                verdict
                reading.Target.Name
                (ModeInputs.formatMetricValue reading.Target.Key reading.Value)
                (ModeInputs.formatConstraintLimit reading.Target)
                reading.GoverningLoadCase) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  CARICHI DELLA GEOMETRIA OTTIMA") |> ignore
    for load in result.Best.LoadCaseResults do
        let design = load.Verification.Result
        sb.AppendLine(
            sprintf "    %-18s duty %8.3f MW | steam %8.3f t/h | T gas out %8.3f C | dP gas %8.3f mbar"
                load.Spec.Name
                (ModeInputs.summaryValue ConstraintModel.Duty design / 1e6)
                (ModeInputs.summaryValue ConstraintModel.SteamProduction design * 3.6)
                (kToC (ModeInputs.summaryValue ConstraintModel.GasOutletTemperature design))
                (ModeInputs.summaryValue ConstraintModel.GasPressureDrop design / 100.0)) |> ignore
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
    let runSettings = ModeInputs.createRunSettings options options.Calculation.CorrelationValidityWarnings
    let constraints =
        ModeInputs.readCaseRoot casePath (ModeInputs.defaultConstraintSet case0) (ModeInputs.readConstraintSet case0)
    let loadCases =
        ModeInputs.readCaseRoot casePath [] (fun root ->
            ModeInputs.readLoadCases root [ "design.carichi"; "rating.carichi"; "carichi" ])
    let objective =
        ModeInputs.readCaseRoot casePath Optimize.defaultObjective (fun root ->
            ModeInputs.readObjectiveSet root "design.obiettivo" Optimize.defaultObjective)
    let space =
        ModeInputs.readCaseRoot casePath
            ({ TubeCounts = []
               TubeLengthsM = []
               FerruleLengthsMm = []
               ShellInnerDiametersM = []
               TubeSizeOptions = []
               TubePitchesM = []
               DrumCenterlineHeightsM = [] } : GreenfieldDesign.DesignSpace)
            ModeInputs.readDesignSpace
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
    sb.AppendLine(sprintf "    diametro esterno tubi      %s mm" (ModeInputs.formatNumber (best.Case.Tube.Do * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    lunghezza tubi             %s m" (ModeInputs.formatNumber best.Case.Tube.Length)) |> ignore
    sb.AppendLine(sprintf "    lunghezza ferrula          %s mm" (ModeInputs.formatNumber ((best.Case.Ferrule.Lengths |> List.sumBy (fun (frac, l) -> frac * l)) * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    diametro interno mantello  %s mm" (ModeInputs.formatNumber (best.Case.Tube.ShellId * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    passo tubi                %s mm" (ModeInputs.formatNumber (best.Case.Tube.Pitch * 1000.0))) |> ignore
    sb.AppendLine(sprintf "    quota drum                %s m" (ModeInputs.formatNumber best.Case.Loop.DzDrumWhb)) |> ignore
    sb.AppendLine() |> ignore
    sb.AppendLine("  VINCOLI DEL MIGLIOR CANDIDATO") |> ignore
    for reading in best.Assessment.ConstraintReadings do
        let verdict = if reading.Passed then "OK " else "NO "
        sb.AppendLine(
            sprintf "    [%s] %-32s %-18s limite %s  (carico %s)"
                verdict
                reading.Target.Name
                (ModeInputs.formatMetricValue reading.Target.Key reading.Value)
                (ModeInputs.formatConstraintLimit reading.Target)
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
