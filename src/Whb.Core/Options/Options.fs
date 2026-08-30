namespace Whb.Core.Options

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
module Options =

    [<CLIMutable>]
    type GithubOptions =
        { Enabled: bool
          RepositoryUrl: string
          Branch: string
          CommitMessage: string
          PushOnSave: bool
          CreatePullRequest: bool }

    [<CLIMutable>]
    type FolderOptions =
        { CasesFolder: string
          ResultsFolder: string
          TempFolder: string
          DatabasesFolder: string
          ReportsFolder: string
          PackagesFolder: string }

    [<CLIMutable>]
    /// <summary>
    /// Represents phase logging options for WHB command-line runs.
    /// </summary>
    /// <remarks>
    /// Phase logging is intended for operational diagnostics before and during long calculations.
    /// </remarks>
    type LoggingOptions =
        { Enabled: bool
          LogFile: string }

    [<CLIMutable>]
    /// <summary>
    /// Represents report generation options for WHB command-line runs.
    /// </summary>
    /// <remarks>
    /// Summary and criticality outputs are always written; these options control additional full engineering reports.
    /// </remarks>
    type ReportingOptions =
        { GenerateFullReport: bool
          GenerateHtmlReport: bool }

    [<CLIMutable>]
    type CalculationOptions =
        { UseRealGas: bool
          AxialSections: int
          VerticalBands: int
          Parallelism: int
          StrictValidation: bool
          BypassMapMode: string
          BypassTargetToleranceK: float
          DutyToleranceFraction: float
          GasPropertyCache: bool
          CorrelationValidityWarnings: bool }

    [<CLIMutable>]
    type ProjectOptions =
        { Folders: FolderOptions
          Logging: LoggingOptions
          Reporting: ReportingOptions
          Calculation: CalculationOptions
          Github: GithubOptions }

    [<CLIMutable>]
    type PublicProjectOptionsTemplate =
        { Folders: FolderOptions
          Logging: LoggingOptions
          Reporting: ReportingOptions
          Calculation: CalculationOptions }
    let private serializerOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let defaultOptions =
        { Folders =
            { CasesFolder = "cases"
              ResultsFolder = "results"
              TempFolder = "tmp"
              DatabasesFolder = "databases"
              ReportsFolder = "reports"
              PackagesFolder = "packages" }
          Logging =
            { Enabled = true
              LogFile = "logs/whb-run.log" }
          Reporting =
            { GenerateFullReport = true
              GenerateHtmlReport = true }
          Calculation =
            { UseRealGas = true
              AxialSections = 90
              VerticalBands = 12
              Parallelism = max 1 Environment.ProcessorCount
              StrictValidation = true
              BypassMapMode = "adaptive"
              BypassTargetToleranceK = 0.5
              DutyToleranceFraction = 0.002
              GasPropertyCache = true
              CorrelationValidityWarnings = true }
          Github =
            { Enabled = false
              RepositoryUrl = ""
              Branch = "main"
              CommitMessage = "Update WHB project"
              PushOnSave = false
              CreatePullRequest = false } }

    let private toPublicTemplate (options: ProjectOptions) : PublicProjectOptionsTemplate =
        { Folders = options.Folders
          Logging = options.Logging
          Reporting = options.Reporting
          Calculation = options.Calculation }

    let saveTemplate path options =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, JsonSerializer.Serialize(toPublicTemplate options, serializerOptions))
    /// <summary>
    /// Overlays the values present in <paramref name="over"/> onto <paramref name="baseObj"/>,
    /// descending into nested objects.
    /// </summary>
    /// <remarks>
    /// Keys absent from the file keep the value already in the base document.
    /// </remarks>
    let rec private overlay (baseObj: JsonObject) (over: JsonObject) =
        for entry in over do
            match baseObj.[entry.Key], entry.Value with
            | (:? JsonObject as b), (:? JsonObject as o) -> overlay b o
            | _ -> baseObj.[entry.Key] <- (if isNull entry.Value then null else entry.Value.DeepClone())

    /// <summary>
    /// Loads project options, filling in anything the file does not mention from
    /// <see cref="defaultOptions"/>.
    /// </summary>
    /// <remarks>
    /// Options files are hand-edited and files written by earlier versions do not carry
    /// every section. Merging onto the defaults keeps documented defaults such as phase
    /// logging active instead of letting an absent key deserialize to false, null or zero.
    /// </remarks>
    let load path =
        if not (File.Exists path) then defaultOptions
        else
            match JsonNode.Parse(File.ReadAllText path) with
            | :? JsonObject as fileObj ->
                let merged =
                    JsonNode.Parse(JsonSerializer.Serialize(defaultOptions, serializerOptions)) :?> JsonObject
                overlay merged fileObj
                JsonSerializer.Deserialize<ProjectOptions>(merged.ToJsonString(), serializerOptions)
            | _ -> defaultOptions

