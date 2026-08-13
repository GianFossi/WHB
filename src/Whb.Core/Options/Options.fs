namespace Whb.Core.Options

open System
open System.IO
open System.Text.Json

/// <summary>
/// Provides options functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Options =

    [<CLIMutable>]
    /// <summary>
    /// Represents githuboptions data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type GithubOptions =
        { Enabled: bool
          RepositoryUrl: string
          Branch: string
          CommitMessage: string
          PushOnSave: bool
          CreatePullRequest: bool }

    [<CLIMutable>]
    /// <summary>
    /// Represents folderoptions data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
    /// <summary>
    /// Represents calculationoptions data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type CalculationOptions =
        { UseRealGas: bool
          AxialSections: int
          VerticalBands: int
          Parallelism: int
          StrictValidation: bool }

    [<CLIMutable>]
    /// <summary>
    /// Represents projectoptions data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ProjectOptions =
        { Folders: FolderOptions
          Logging: LoggingOptions
          Reporting: ReportingOptions
          Calculation: CalculationOptions
          Github: GithubOptions
          RecentFiles: string list }

    /// <summary>
    /// Calculates or returns serializerOptions for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private serializerOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    /// <summary>
    /// Calculates or returns defaultoptions for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
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
              StrictValidation = true }
          Github =
            { Enabled = false
              RepositoryUrl = ""
              Branch = "main"
              CommitMessage = "Update WHB project"
              PushOnSave = false
              CreatePullRequest = false }
          RecentFiles = [] }

    /// <summary>
    /// Calculates or returns rememberfile for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let rememberFile path options =
        let full = Path.GetFullPath path
        let recent =
            full :: (options.RecentFiles |> List.filter (fun x -> not (String.Equals(Path.GetFullPath x, full, StringComparison.OrdinalIgnoreCase))))
            |> List.truncate 20
        { options with RecentFiles = recent }

    /// <summary>
    /// Calculates or returns save for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let save path options =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, JsonSerializer.Serialize(options, serializerOptions))

    /// <summary>
    /// Calculates or returns load for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let load path =
        if File.Exists path then
            JsonSerializer.Deserialize<ProjectOptions>(File.ReadAllText path, serializerOptions)
        else defaultOptions
