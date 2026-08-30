module Whb.Cli.Program

open System
open System.IO
open System.Text
open System.Text.Json
open System.Globalization
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Options
open Whb.Cli

[<EntryPoint>]
let main argv =
    let args = List.ofArray argv
    let resultsRoot = OutputPaths.reportDirectory "results" ""
    let optionsPath = CommandSupport.getOpt "--options" "whb.options.json" args
    let projectOptions = Options.load optionsPath
    let existingOptionsPath =
        if File.Exists optionsPath then Some(Path.GetFullPath optionsPath) else None
    let reportRoot =
        OutputPaths.reportDirectory resultsRoot projectOptions.Folders.ResultsFolder
    let outDir = OutputPaths.reportDirectory reportRoot (CommandSupport.getOpt "--out" "" args)

    let writeTemplateFile path =
        let dir = Path.GetDirectoryName(Path.GetFullPath path)
        if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
        File.WriteAllText(path, CommandSupport.template)
        printfn "Template written to %s" (Path.GetFullPath path)
        0

    let writeSteamTable path tMin tMax step =
        let table = Steam.saturationTable tMin tMax step
        let sb = StringBuilder()
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
        let dir = Path.GetDirectoryName(Path.GetFullPath path)
        if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
        File.WriteAllText(path, sb.ToString())
        printfn "Tabella di saturazione %g-%g C (passo %g C) scritta in %s" tMin tMax step (Path.GetFullPath path)
        0

    let num name def =
        match CommandSupport.getOpt name "" args with
        | "" -> def
        | s ->
            match CommandSupport.tryParseFloat s with
            | Some v -> v
            | _ -> def

    try
        match args with
        | [] | "--out" :: _ | "--options" :: _ ->
            printfn "No case file provided: running the reference case.\n"
            CommandSupport.rememberRecentFiles existingOptionsPath None
            CommandRunners.runCase projectOptions None Defaults.referenceCase outDir
        | "--help" :: _ | "-h" :: _ ->
            CommandSupport.printHelp ()
            0
        | "--template" :: rest ->
            let path = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "case.json"
            writeTemplateFile path
        | "--selftest" :: _ -> CommandSupport.selfTest ()
        | "--steamtable" :: rest ->
            let requested = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "steam_saturation_table.csv"
            let path = OutputPaths.reportFile reportRoot requested "steam_saturation_table.csv"
            writeSteamTable path (num "--tmin" 20.0) (num "--tmax" 310.0) (num "--step" 10.0)
        | "--sulphur" :: rest ->
            let requested = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "sulphur_table.csv"
            let path = OutputPaths.reportFile reportRoot requested "sulphur_table.csv"
            CommandSupport.writeSulphurTable path (num "--pressure-bara" 1.7) (num "--s-atoms-mols" 8.0) (num "--inert-mols" 100.0) (num "--tmin" 120.0) (num "--tmax" 350.0) (num "--step" 10.0)
        | "--sulphur-condenser" :: rest ->
            match CommandSupport.filteredArgs rest with
            | f :: _ when File.Exists f ->
                let fullCasePath = Path.GetFullPath f
                CommandSupport.rememberRecentFiles existingOptionsPath (Some fullCasePath)
                CommandRunners.runSulphurCondenserCase projectOptions (Some fullCasePath) (CaseLoader.loadCase fullCasePath) outDir
            | f :: _ when not (f.StartsWith("--")) ->
                eprintfn "Case file not found: %s" f
                raise (FileNotFoundException("Case file not found", f))
            | _ ->
                CommandSupport.rememberRecentFiles existingOptionsPath None
                CommandRunners.runSulphurCondenserCase projectOptions None Defaults.referenceCase outDir
        | "--options-template" :: rest ->
            let path = match rest with | x :: _ when not (x.StartsWith("--")) -> x | _ -> "whb.options.json"
            CommandSupport.writeDefaultOptions path
        | "--github-plan" :: rest ->
            let path = match rest with | x :: _ when File.Exists x -> x | _ -> "whb.options.json"
            if File.Exists path then CommandSupport.rememberRecentFiles (Some(Path.GetFullPath path)) None
            CommandSupport.githubPlan path
        | "--github-push" :: rest ->
            let path = match rest with | x :: _ when File.Exists x -> x | _ -> "whb.options.json"
            if File.Exists path then CommandSupport.rememberRecentFiles (Some(Path.GetFullPath path)) None
            CommandSupport.githubPush path
        | "--rating" :: rest ->
            let casePath, caseIn = CommandSupport.resolveCaseArg rest
            CommandSupport.rememberRecentFiles existingOptionsPath casePath
            CommandRunners.runRatingMode projectOptions casePath caseIn outDir
        | "--optimize" :: rest ->
            let casePath, caseIn = CommandSupport.resolveCaseArg rest
            CommandSupport.rememberRecentFiles existingOptionsPath casePath
            CommandRunners.optimizeCase projectOptions casePath caseIn outDir
        | "--design" :: rest ->
            let casePath, caseIn = CommandSupport.resolveCaseArg rest
            CommandSupport.rememberRecentFiles existingOptionsPath casePath
            CommandRunners.runDesignMode projectOptions casePath caseIn outDir
        | "--optimize-legacy" :: rest ->
            let casePath, caseIn = CommandSupport.resolveCaseArg rest
            CommandSupport.rememberRecentFiles existingOptionsPath casePath
            CommandRunners.optimizeCaseLegacy projectOptions caseIn outDir
        | "--sizing" :: rest ->
            let casePath, caseIn = CommandSupport.resolveCaseArg rest
            CommandSupport.rememberRecentFiles existingOptionsPath casePath
            CommandRunners.sizingOnly projectOptions caseIn outDir
        | "--loads" :: rest ->
            let casePath, caseIn = CommandSupport.resolveCaseArg rest
            CommandSupport.rememberRecentFiles existingOptionsPath casePath
            CommandRunners.loadCurves projectOptions caseIn outDir
        | opt :: _ when opt.StartsWith("--") ->
            eprintfn "Unknown option: %s" opt
            CommandSupport.printUsage ()
            2
        | file :: _ when File.Exists file ->
            let fullCasePath = Path.GetFullPath file
            CommandSupport.rememberRecentFiles existingOptionsPath (Some fullCasePath)
            CommandRunners.runCase projectOptions (Some fullCasePath) (CaseLoader.loadCase fullCasePath) outDir
        | x :: _ ->
            eprintfn "File not found: %s" x
            CommandSupport.printUsage ()
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
