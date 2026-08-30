namespace Whb.Cli

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

module Preflight =

    /// <summary>
    /// Represents one preflight check result.
    /// </summary>
    /// <remarks>
    /// Failed checks stop the run; warnings are logged and printed but allow the calculation to continue.
    /// </remarks>
    type Check =
        { Name: string
          Ok: bool
          WarningOnly: bool
          Message: string }

    /// <summary>
    /// Creates a preflight result row.
    /// </summary>
    /// <remarks>
    /// The row is used for both console messages and phase logging.
    /// </remarks>
    let private row name ok warningOnly message =
        { Name = name; Ok = ok; WarningOnly = warningOnly; Message = message }

    /// <summary>
    /// Verifies that a directory can be created and written.
    /// </summary>
    /// <remarks>
    /// A temporary probe file is created and deleted inside the directory.
    /// </remarks>
    let private canWriteDirectory name path =
        try
            Directory.CreateDirectory path |> ignore
            let probe = Path.Combine(path, sprintf ".whb_write_probe_%s.tmp" (Guid.NewGuid().ToString("N")))
            File.WriteAllText(probe, "probe")
            File.Delete probe
            row name true false (sprintf "Writable: %s" (Path.GetFullPath path))
        with ex ->
            row name false false (sprintf "Cannot write %s: %s" (Path.GetFullPath path) ex.Message)

    /// <summary>
    /// Verifies that an input file is readable.
    /// </summary>
    /// <remarks>
    /// The check opens and closes the file before the calculation starts.
    /// </remarks>
    let private canReadFile name path =
        try
            use _stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            row name true false (sprintf "Readable: %s" (Path.GetFullPath path))
        with ex ->
            row name false false (sprintf "Cannot read %s: %s" (Path.GetFullPath path) ex.Message)

    /// <summary>
    /// Checks available disk space on the output drive.
    /// </summary>
    /// <remarks>
    /// The default threshold is intentionally modest because normal report output is small, but low space is still reported early.
    /// </remarks>
    let private diskSpace path =
        try
            let root = Path.GetPathRoot(Path.GetFullPath path)
            let drive = DriveInfo root
            let minBytes = 200L * 1024L * 1024L
            let ok = drive.AvailableFreeSpace >= minBytes
            row "Disk space" ok false (sprintf "Free space on %s: %.1f MB" root (float drive.AvailableFreeSpace / 1024.0 / 1024.0))
        with ex ->
            row "Disk space" false true (sprintf "Could not check disk space: %s" ex.Message)

    /// <summary>
    /// Checks whether another WHB executable is still active.
    /// </summary>
    /// <remarks>
    /// Existing WHB processes can lock build outputs and make multitasking unreliable.
    /// </remarks>
    let private activeWhbProcesses () =
        try
            let currentId = Process.GetCurrentProcess().Id
            let procs =
                Process.GetProcessesByName("whb")
                |> Array.filter (fun p -> p.Id <> currentId)
                |> Array.toList
            if List.isEmpty procs then
                row "Active WHB processes" true true "No other whb.exe process detected"
            else
                let ids = procs |> List.map (fun p -> string p.Id) |> String.concat ", "
                row "Active WHB processes" false false (sprintf "Other whb.exe process(es) detected: %s. Close them before starting a new run." ids)
        with ex ->
            row "Active WHB processes" false true (sprintf "Could not inspect WHB processes: %s" ex.Message)

    /// <summary>
    /// Runs preflight checks and raises an error when blocking checks fail.
    /// </summary>
    /// <remarks>
    /// All results are logged so the user can diagnose what happened before the calculation started.
    /// </remarks>
    let run (options: Options.ProjectOptions) (casePath: string option) (outDir: string) (logger: PhaseLogger.Logger) =
        logger "Preflight checks started"
        let logFolder =
            let folder = Path.GetDirectoryName(Path.GetFullPath options.Logging.LogFile)
            if String.IsNullOrWhiteSpace folder then "." else folder
        let checks =
            [ activeWhbProcesses()
              canWriteDirectory "Output folder" outDir
              canWriteDirectory "Temp folder" options.Folders.TempFolder
              canWriteDirectory "Log folder" logFolder
              diskSpace outDir ]
            @ (match casePath with Some p -> [ canReadFile "Case file" p ] | None -> [])
        for c in checks do
            let level = if c.Ok then "OK" elif c.WarningOnly then "WARNING" else "ERROR"
            let msg = sprintf "Preflight %s | %s | %s" level c.Name c.Message
            logger msg
            if not c.Ok then
                let printer = if c.WarningOnly then eprintfn else eprintfn
                printer "%s" msg
        let blocking = checks |> List.filter (fun c -> not c.Ok && not c.WarningOnly)
        if not blocking.IsEmpty then
            let details = blocking |> List.map (fun c -> sprintf "%s: %s" c.Name c.Message) |> String.concat "; "
            failwithf "Preflight failed: %s" details
        logger "Preflight checks completed"


