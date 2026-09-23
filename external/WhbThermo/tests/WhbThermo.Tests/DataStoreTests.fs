module WhbThermo.Tests.DataStoreTests

open System
open System.IO
open Xunit
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

let private temporaryRoot () =
    let dir = Path.Combine(Path.GetTempPath(), "whbthermo-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    dir

[<Fact>]
let ``missing file fails with the searched paths listed`` () =
    match DataStore.resolve "definitely-not-here.json" with
    | Failure [ DatabaseNotFound detail ] -> Assert.Contains("searched:", detail)
    | _ -> failwith "expected DatabaseNotFound"

[<Fact>]
let ``format is chosen by extension`` () =
    Assert.Equal(DataStore.Json, DataStore.Format.OfPath "a/b/species.json")
    Assert.Equal(DataStore.Xml, DataStore.Format.OfPath "a/b/model.XML")
    Assert.Equal(DataStore.Text, DataStore.Format.OfPath "a/b/burcat.thr")

/// A second load of an unchanged file must not re-parse. The parse counter makes
/// that observable rather than assumed.
[<Fact>]
let ``unchanged file is served from cache`` () =
    let dir = temporaryRoot ()
    let path = Path.Combine(dir, "cached.txt")
    File.WriteAllText(path, "alpha")

    let mutable parses = 0
    let parse (payload: string) = parses <- parses + 1; ok payload

    let first = DataStore.load path parse
    let second = DataStore.load path parse

    match first, second with
    | Success (a, _), Success (b, _) ->
        Assert.Equal("alpha", a)
        Assert.Equal("alpha", b)
        Assert.Equal(1, parses)
    | _ -> failwith "both loads should succeed"

    Directory.Delete(dir, true)

/// Editing a data file must take effect without restarting the process --
/// that is the whole point of loading from disk instead of embedding.
[<Fact>]
let ``edited file is reloaded`` () =
    let dir = temporaryRoot ()
    let path = Path.Combine(dir, "edited.txt")
    File.WriteAllText(path, "before")

    let mutable parses = 0
    let parse (payload: string) = parses <- parses + 1; ok payload

    DataStore.load path parse |> ignore

    // Last-write time has one-second resolution on some file systems.
    File.WriteAllText(path, "after")
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds 2.0)

    match DataStore.load path parse with
    | Success (v, _) ->
        Assert.Equal("after", v)
        Assert.Equal(2, parses)
    | Failure _ -> failwith "reload should succeed"

    Directory.Delete(dir, true)

/// A parse failure must not poison the cache with a half-built value.
[<Fact>]
let ``failed parse is not cached`` () =
    let dir = temporaryRoot ()
    let path = Path.Combine(dir, "bad.txt")
    File.WriteAllText(path, "payload")

    let failing (_: string) : Thermo<string> = fail (DatabaseParseError "deliberate")

    match DataStore.load path failing with
    | Failure _ -> ()
    | Success _ -> failwith "expected failure"

    let mutable parses = 0
    let good (payload: string) = parses <- parses + 1; ok payload
    match DataStore.load path good with
    | Success (v, _) ->
        Assert.Equal("payload", v)
        Assert.Equal(1, parses)
    | Failure _ -> failwith "second load should succeed"

    Directory.Delete(dir, true)

/// Provenance: a calculation report must be able to state which files it used.
[<Fact>]
let ``loaded files are reportable`` () =
    match SpeciesDatabase.load () with
    | Success _ ->
        let entries = DataStore.loaded ()
        Assert.NotEmpty(entries)
        Assert.Contains(entries, fun (p, _, _) -> p.EndsWith "species-database.json")
    | Failure msgs -> failwith (msgs |> List.map string |> String.concat "; ")
