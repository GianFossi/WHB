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

module PdsComparison =

    /// <summary>
    /// Represents one client PDS comparison row.
    /// </summary>
    /// <remarks>
    /// Values are stored in report units so text and CSV output use the same numbers.
    /// </remarks>
    type Row =
        { Quantity: string
          Unit: string
          ClientPds: float
          Calculated: float
          Difference: float
          DifferencePercent: float option
          Limit: string
          Status: string }

    /// <summary>
    /// Formats a floating-point value for PDS comparison output.
    /// </summary>
    /// <remarks>
    /// Invariant formatting keeps CSV files stable across local Windows language settings.
    /// </remarks>
    let private f3 (value: float) =
        value.ToString("F3", CultureInfo.InvariantCulture)

    /// <summary>
    /// Builds a comparison row from client PDS and calculated values.
    /// </summary>
    /// <remarks>
    /// The status uses the provided absolute acceptance band when one is available for the PDS quantity.
    /// </remarks>
    let private row quantity unit pds calculated tolerance =
        let diff = calculated - pds
        let pct = if abs pds > 1e-12 then Some(100.0 * diff / pds) else None
        let ok = abs diff <= tolerance
        { Quantity = quantity
          Unit = unit
          ClientPds = pds
          Calculated = calculated
          Difference = diff
          DifferencePercent = pct
          Limit = sprintf "+/- %s %s" (f3 tolerance) unit
          Status = if ok then "OK" else "CHECK" }

    /// <summary>
    /// Builds all available client PDS comparison rows for a completed WHB result.
    /// </summary>
    /// <remarks>
    /// Current client PDS values match the reference datasheet values documented in the README and validation table.
    /// </remarks>
    let rows (result: DesignResult) =
        [ row "Exchanged duty" "MW" 116.614 (result.Duty / 1e6) 0.25
          row "Steam production" "kg/h" 347743.0 (result.SteamProduction * 3600.0) 1500.0
          row "Gas outlet temperature" "degC" 355.0 (kToC result.TGasOutMean) 8.0
          row "Gas-side pressure drop" "bar" 0.300 (paToBar result.DpGas) 0.300 ]

    /// <summary>
    /// Builds the text report for the mandatory client PDS comparison.
    /// </summary>
    /// <remarks>
    /// The text report is intended for direct engineering review next to the main calculation report.
    /// </remarks>
    let text (result: DesignResult) =
        let rows = rows result
        let diffTexts =
            rows
            |> List.map (fun r ->
                match r.DifferencePercent with
                | Some pct -> sprintf "%s (%+.2f%%)" (f3 r.Difference) pct
                | None -> f3 r.Difference)
        let quantityWidth =
            ("Quantity" :: (rows |> List.map (fun r -> r.Quantity)))
            |> List.maxBy String.length
            |> String.length
        let unitWidth =
            ("Unit" :: (rows |> List.map (fun r -> r.Unit)))
            |> List.maxBy String.length
            |> String.length
        let clientWidth =
            ("Client PDS" :: (rows |> List.map (fun r -> f3 r.ClientPds)))
            |> List.maxBy String.length
            |> String.length
        let calculatedWidth =
            ("Calculated" :: (rows |> List.map (fun r -> f3 r.Calculated)))
            |> List.maxBy String.length
            |> String.length
        let differenceWidth =
            ("Difference" :: diffTexts)
            |> List.maxBy String.length
            |> String.length
        let limitWidth =
            ("Limit" :: (rows |> List.map (fun r -> r.Limit)))
            |> List.maxBy String.length
            |> String.length
        let statusWidth =
            ("Status" :: (rows |> List.map (fun r -> r.Status)))
            |> List.maxBy String.length
            |> String.length
        let formatRow
            (quantity: string)
            (unit: string)
            (client: string)
            (calculated: string)
            (difference: string)
            (limit: string)
            (status: string) =
            String.concat "  "
                [ quantity.PadRight(quantityWidth)
                  unit.PadRight(unitWidth)
                  client.PadLeft(clientWidth)
                  calculated.PadLeft(calculatedWidth)
                  difference.PadLeft(differenceWidth)
                  limit.PadLeft(limitWidth)
                  status.PadLeft(statusWidth) ]
        let separatorLength =
            formatRow
                (String.replicate quantityWidth "-")
                (String.replicate unitWidth "-")
                (String.replicate clientWidth "-")
                (String.replicate calculatedWidth "-")
                (String.replicate differenceWidth "-")
                (String.replicate limitWidth "-")
                (String.replicate statusWidth "-")
            |> String.length
        let sb = Text.StringBuilder()
        sb.AppendLine("CLIENT PDS COMPARISON CHECK") |> ignore
        sb.AppendLine("This check is generated for every WHB run using the available client PDS reference data.") |> ignore
        sb.AppendLine("Review every CHECK row before accepting the calculation output.") |> ignore
        sb.AppendLine(String('-', separatorLength)) |> ignore
        sb.AppendLine(formatRow "Quantity" "Unit" "Client PDS" "Calculated" "Difference" "Limit" "Status") |> ignore
        sb.AppendLine(String('-', separatorLength)) |> ignore
        for r, diffText in List.zip rows diffTexts do
            sb.AppendLine(
                formatRow
                    r.Quantity
                    r.Unit
                    (f3 r.ClientPds)
                    (f3 r.Calculated)
                    diffText
                    r.Limit
                    r.Status) |> ignore
        sb.AppendLine(String('-', separatorLength)) |> ignore
        sb.ToString()

    /// <summary>
    /// Builds the CSV report for the mandatory client PDS comparison.
    /// </summary>
    /// <remarks>
    /// The CSV output supports audit trails and spreadsheet review of calculated versus PDS values.
    /// </remarks>
    let csv (result: DesignResult) =
        let sb = Text.StringBuilder()
        sb.AppendLine("quantity,unit,client_pds,calculated,difference,difference_percent,limit,status") |> ignore
        for r in rows result do
            let pct =
                match r.DifferencePercent with
                | Some value -> f3 value
                | None -> ""
            sb.AppendLine(String.Join(",",
                [| r.Quantity
                   r.Unit
                   f3 r.ClientPds
                   f3 r.Calculated
                   f3 r.Difference
                   pct
                   r.Limit
                   r.Status |])) |> ignore
        sb.ToString()


