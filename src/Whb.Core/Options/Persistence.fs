namespace Whb.Core

open System.IO
open System.Text.Json

/// <summary>
/// Provides persistence functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Persistence =

    /// <summary>
    /// Calculates or returns jsonOptions for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private jsonOptions =
        JsonSerializerOptions(WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase)

    /// <summary>
    /// Calculates or returns save for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let save<'T> path (value: 'T) =
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
        File.WriteAllText(path, JsonSerializer.Serialize(value, jsonOptions))

    /// <summary>
    /// Calculates or returns load for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let load<'T> path =
        JsonSerializer.Deserialize<'T>(File.ReadAllText path, jsonOptions)

    /// <summary>
    /// Calculates or returns tryload for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let tryLoad<'T> path =
        if File.Exists path then Some(load<'T> path) else None
