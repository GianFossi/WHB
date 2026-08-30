namespace Whb.Cli

open System
open System.IO
open System.Text.Json

module RecentFilesStore =

    [<CLIMutable>]
    type Store =
        { CaseFiles: string list
          OptionFiles: string list }

    let defaultPath = Path.Combine(".user", "recent-files.json")

    let empty =
        { CaseFiles = []
          OptionFiles = [] }

    let private serializerOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    let private sanitize (store: Store) =
        { CaseFiles = defaultArg (Option.ofObj store.CaseFiles) []
          OptionFiles = defaultArg (Option.ofObj store.OptionFiles) [] }

    let private normalizePath (path: string) =
        Path.GetFullPath path

    let private rememberPath (path: string) (items: string list) =
        let full = normalizePath path
        full :: (items |> List.filter (fun item -> not (String.Equals(normalizePath item, full, StringComparison.OrdinalIgnoreCase))))
        |> List.truncate 20

    let rememberCaseFile (path: string) (store: Store) =
        { store with CaseFiles = rememberPath path store.CaseFiles }

    let rememberOptionFile (path: string) (store: Store) =
        { store with OptionFiles = rememberPath path store.OptionFiles }

    let load (path: string) =
        if not (File.Exists path) then empty
        else
            try
                JsonSerializer.Deserialize<Store>(File.ReadAllText path, serializerOptions)
                |> sanitize
            with
            | _ -> empty

    let save (path: string) (store: Store) =
        let dir = Path.GetDirectoryName(Path.GetFullPath path)
        if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
        File.WriteAllText(path, JsonSerializer.Serialize(sanitize store, serializerOptions))

    let private rememberIfExists remember path store =
        if String.IsNullOrWhiteSpace path || not (File.Exists path) then store
        else remember path store

    let updateStore (optionsPath: string option) (casePath: string option) (store: Store) =
        store
        |> rememberIfExists rememberOptionFile (optionsPath |> Option.defaultValue "")
        |> rememberIfExists rememberCaseFile (casePath |> Option.defaultValue "")

    let persistUpdate (storePath: string) (optionsPath: string option) (casePath: string option) =
        let updated =
            load storePath
            |> updateStore optionsPath casePath
        save storePath updated
