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

module PhaseLogger =

    /// <summary>
    /// Represents one lightweight logging function.
    /// </summary>
    /// <remarks>
    /// The function accepts a message and writes it with local date/time when logging is enabled.
    /// </remarks>
    type Logger = string -> unit

    /// <summary>
    /// Creates a phase logger from project options.
    /// </summary>
    /// <remarks>
    /// Parent folders are created automatically; write errors are reported to stderr but do not stop the calculation.
    /// </remarks>
    let create (options: Options.ProjectOptions) : Logger =
        if not options.Logging.Enabled then ignore
        else
            let logPath = Path.GetFullPath options.Logging.LogFile
            let dir = Path.GetDirectoryName logPath
            if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
            // Calculation phases are reported from several threads at once (the bypass map is
            // solved concurrently). The lock lives here so that thread safety is a property of
            // the logger itself and no caller has to know about it.
            let gate = obj ()
            fun message ->
                try
                    let stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    lock gate (fun () ->
                        File.AppendAllText(logPath, sprintf "%s | %s%s" stamp message Environment.NewLine))
                with ex ->
                    eprintfn "Logging disabled for this message: %s" ex.Message

    /// <summary>
    /// Writes a log message and updates a mutable current-task label.
    /// </summary>
    /// <remarks>
    /// This keeps console progress and the diagnostic log synchronized.
    /// </remarks>
    let phase (logger: Logger) (currentTask: string ref) (message: string) =
        currentTask.Value <- message
        logger message


