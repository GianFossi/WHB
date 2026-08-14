namespace Whb.Core.Options

open System
open System.IO
open System.Text.Json
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
          Github: GithubOptions
          RecentFiles: string list }
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
              CreatePullRequest = false }
          RecentFiles = [] }
    let rememberFile path options =
        let full = Path.GetFullPath path
        let recent =
            full :: (options.RecentFiles |> List.filter (fun x -> not (String.Equals(Path.GetFullPath x, full, StringComparison.OrdinalIgnoreCase))))
            |> List.truncate 20
        { options with RecentFiles = recent }
    let save path options =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, JsonSerializer.Serialize(options, serializerOptions))
    let load path =
        if File.Exists path then
            JsonSerializer.Deserialize<ProjectOptions>(File.ReadAllText path, serializerOptions)
        else defaultOptions


