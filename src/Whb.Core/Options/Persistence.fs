namespace Whb.Core

open System.IO
open System.Text.Json
module Persistence =
    let private jsonOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    let save<'T> path (value: 'T) =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions))
    let load<'T> path =
        JsonSerializer.Deserialize<'T>(File.ReadAllText path, jsonOptions)
    let tryLoad<'T> path =
        if File.Exists path then Some(load<'T> path) else None


