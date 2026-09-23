namespace WhbThermo.Data

open System
open System.Collections.Concurrent
open System.IO
open System.Reflection
open Ganfoss.ROP
open WhbThermo.Domain

/// Loads reference data from EXTERNAL files rather than embedded resources.
///
/// Three reasons this matters:
///
///  1. Licensing. Several thermodynamic databases (Goos-Burcat-Ruscic among them)
///     forbid redistribution inside software. Keeping the data outside the
///     assembly means the tool never carries the data, only reads it.
///  2. Field maintenance. A coefficient can be corrected on a running
///     installation by editing a file; no rebuild, no redeployment.
///  3. Auditability. A calculation report can name the exact data file and its
///     timestamp, which an embedded blob cannot provide.
///
/// Entries are cached and invalidated on the file's last-write time, so editing
/// a JSON file takes effect on the next call without restarting the process.
module DataStore =

    /// Supported payload formats. Format is chosen by extension, not guessed
    /// from content.
    type Format =
        | Json
        | Xml
        | Text

        static member OfPath(path: string) =
            match Path.GetExtension(path).ToLowerInvariant() with
            | ".json" -> Json
            | ".xml" -> Xml
            | _ -> Text

    [<NoComparison; NoEquality>]
    type CacheEntry =
        { Path      : string
          LoadedUtc : DateTime
          StampUtc  : DateTime
          Value     : obj }

    let private cache = ConcurrentDictionary<string, CacheEntry>()
    let mutable private explicitRoot : string option = None

    // ---------- root resolution ----------

    /// Override the data directory explicitly. Highest priority.
    let setRoot (path: string) =
        explicitRoot <- Some path
        cache.Clear()

    /// Candidate directories, in priority order:
    ///   1. the path set by setRoot
    ///   2. the WHBTHERMO_DATA environment variable
    ///   3. a `data` folder next to the executing assembly
    ///   4. a `data` folder under the current working directory
    let candidateRoots () =
        [ explicitRoot
          (match Environment.GetEnvironmentVariable "WHBTHERMO_DATA" with
           | null | "" -> None
           | v -> Some v)
          (let asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
           if String.IsNullOrEmpty asmDir then None
           else Some(Path.Combine(asmDir, "data")))
          Some(Path.Combine(Directory.GetCurrentDirectory(), "data")) ]
        |> List.choose id

    /// Resolve a file name against the candidate roots.
    let resolve (fileName: string) : Thermo<string> =
        if Path.IsPathRooted fileName && File.Exists fileName then
            ok fileName
        else
            let attempts = candidateRoots () |> List.map (fun r -> Path.Combine(r, fileName))
            match attempts |> List.tryFind File.Exists with
            | Some found -> ok found
            | None ->
                fail (DatabaseNotFound
                        (sprintf "%s (searched: %s)" fileName (String.Join("; ", attempts))))

    // ---------- loading ----------

    let private stamp (path: string) =
        try File.GetLastWriteTimeUtc path with _ -> DateTime.MinValue

    /// Load and parse a data file, caching the parsed value.
    ///
    /// `parse` receives the raw payload. The cache key is the resolved path and
    /// the entry is discarded when the file's last-write time changes, so a
    /// corrected data file is picked up without a restart.
    let load<'T> (fileName: string) (parse: string -> Thermo<'T>) : Thermo<'T> =
        resolve fileName
        >>= fun path ->
            let current = stamp path
            match cache.TryGetValue path with
            | true, entry when entry.StampUtc = current ->
                // The cache is untyped, so two callers loading the same path
                // with different parsers would collide. An unchecked cast turns
                // that into an InvalidCastException escaping a function whose
                // whole contract is to return Failure instead of throwing.
                match entry.Value with
                | :? 'T as value -> ok value
                | other ->
                    fail (DatabaseParseError
                            (sprintf
                                "%s is cached as %s but was requested as %s. Two parsers are \
                                 sharing one path; give them separate files or separate caches."
                                path (other.GetType().Name) (typeof<'T>.Name)))
            | _ ->
                let payload =
                    try ok (File.ReadAllText path)
                    with ex -> fail (DatabaseParseError $"{path}: {ex.Message}")

                payload
                >>= parse
                >>= fun value ->
                    cache.[path] <-
                        { Path = path
                          LoadedUtc = DateTime.UtcNow
                          StampUtc = current
                          Value = box value }
                    ok value

    /// Raw text, cached.
    let loadText (fileName: string) : Thermo<string> =
        load fileName ok

    /// Format of a resolved data file, for callers that dispatch on it.
    let formatOf (fileName: string) : Thermo<Format> =
        resolve fileName >>= (Format.OfPath >> ok)

    // ---------- introspection ----------

    /// What is currently cached, for a calculation report's data provenance section.
    let loaded () =
        cache.Values
        |> Seq.map (fun e -> e.Path, e.StampUtc, e.LoadedUtc)
        |> Seq.sortBy (fun (p, _, _) -> p)
        |> List.ofSeq

    let clear () = cache.Clear()

    let invalidate (fileName: string) =
        match resolve fileName with
        | Success (path, _) -> cache.TryRemove path |> ignore
        | Failure _ -> ()
