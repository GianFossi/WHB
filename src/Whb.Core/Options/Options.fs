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
          DatabasesFolder: string
          ReportsFolder: string
          PackagesFolder: string }

    [<CLIMutable>]
    type CalculationOptions =
        { UseRealGas: bool
          AxialSections: int
          VerticalBands: int
          Parallelism: int
          StrictValidation: bool }

    [<CLIMutable>]
    type ProjectOptions =
        { Folders: FolderOptions
          Calculation: CalculationOptions
          Github: GithubOptions
          RecentFiles: string list }

    let private serializerOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let defaultOptions =
        { Folders =
            { CasesFolder = "cases"
              ResultsFolder = "risultati"
              DatabasesFolder = "databases"
              ReportsFolder = "reports"
              PackagesFolder = "packages" }
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
