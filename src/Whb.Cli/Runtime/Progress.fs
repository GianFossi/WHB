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

module Progress =

    type StatusSnapshot =
        { Description: string
          Fraction: float option }

    type ProgressEstimate =
        { Fraction: float
          Remaining: TimeSpan option }

    let snapshot description fraction =
        { Description = description
          Fraction = fraction }

    let mergeStatus (current: StatusSnapshot) (update: DesignRuntime.ProgressUpdate) =
        { Description = update.Description
          Fraction =
            match update.Fraction with
            | Some value -> Some value
            | None -> current.Fraction }

    let estimateProgress (estimatedSeconds: float) (elapsed: TimeSpan) (status: StatusSnapshot) =
        let estimate = TimeSpan.FromSeconds(max 1.0 estimatedSeconds)
        let reportedFraction =
            status.Fraction
            |> Option.map (fun value -> max 0.0 (min 1.0 value))
        match reportedFraction with
        | Some fraction when fraction >= 1.0 ->
            { Fraction = 1.0
              Remaining = Some TimeSpan.Zero }
        | Some fraction when fraction > 0.0 ->
            { Fraction = fraction
              Remaining = Some(TimeSpan.FromSeconds(max 0.0 (elapsed.TotalSeconds * (1.0 - fraction) / fraction))) }
        | _ ->
            let fraction = min 0.98 (elapsed.TotalSeconds / estimate.TotalSeconds)
            let remaining =
                if elapsed <= estimate && fraction > 0.0 then
                    Some(TimeSpan.FromSeconds(max 0.0 (estimate.TotalSeconds - elapsed.TotalSeconds)))
                else None
            { Fraction = fraction
              Remaining = remaining }

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
            | None -> "running longer than expected"
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
    let runWithStatusSnapshot<'T> (status: unit -> StatusSnapshot) (estimatedSeconds: float) (work: unit -> 'T) =
        use finished = new ManualResetEventSlim(false)
        let sw = Diagnostics.Stopwatch.StartNew()
        let task =
            Task.Run<'T>(Func<'T>(fun () ->
                try work ()
                finally finished.Set()))
        while not task.IsCompleted do
            let elapsed = sw.Elapsed
            let current = status ()
            let estimate = estimateProgress estimatedSeconds elapsed current
            render "WHB status" current.Description estimate.Fraction elapsed estimate.Remaining
            finished.Wait(TimeSpan.FromMilliseconds(500.0)) |> ignore
        sw.Stop()
        if not Console.IsOutputRedirected then
            Console.SetCursorPosition(0, Console.CursorTop + 2)
        let final = status ()
        render "WHB status" final.Description 1.0 sw.Elapsed (Some TimeSpan.Zero)
        task.GetAwaiter().GetResult()

    let runWithStatusDynamic<'T> (description: unit -> string) (estimatedSeconds: float) (work: unit -> 'T) =
        runWithStatusSnapshot
            (fun () -> snapshot (description ()) None)
            estimatedSeconds
            work

    /// <summary>
    /// Runs a calculation while showing a fixed task description.
    /// </summary>
    /// <remarks>
    /// Use <c>runWithStatusDynamic</c> when the operation can report phase-level diagnostics.
    /// </remarks>
    let runWithStatus<'T> (description: string) (estimatedSeconds: float) (work: unit -> 'T) =
        runWithStatusDynamic (fun () -> description) estimatedSeconds work



