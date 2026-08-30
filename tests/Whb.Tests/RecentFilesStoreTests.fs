module RecentFilesStoreTests

open System
open System.IO
open System.Text.Json
open Whb.Cli
open Whb.Core.Options
open Xunit

[<Fact>]
let ``options template excludes github and recent files sections`` () =
    let path = Path.Combine(Path.GetTempPath(), sprintf "whb-options-%s.json" (Guid.NewGuid().ToString("N")))

    try
        Options.saveTemplate path Options.defaultOptions
        use doc = JsonDocument.Parse(File.ReadAllText path)
        let names =
            doc.RootElement.EnumerateObject()
            |> Seq.map (fun property -> property.Name)
            |> Set.ofSeq

        Assert.Contains("folders", names)
        Assert.Contains("logging", names)
        Assert.Contains("reporting", names)
        Assert.Contains("calculation", names)
        Assert.DoesNotContain("github", names)
        Assert.DoesNotContain("recentFiles", names)
    finally
        if File.Exists path then File.Delete path

[<Fact>]
let ``options loader stays compatible with legacy recent files section`` () =
    let path = Path.Combine(Path.GetTempPath(), sprintf "whb-options-legacy-%s.json" (Guid.NewGuid().ToString("N")))
    let json =
        """{
  "folders": { "resultsFolder": "results-custom" },
  "recentFiles": [ "old-case.json" ]
}"""

    try
        File.WriteAllText(path, json)
        let loaded = Options.load path

        Assert.Equal("results-custom", loaded.Folders.ResultsFolder)
        Assert.False(String.IsNullOrWhiteSpace loaded.Github.Branch)
    finally
        if File.Exists path then File.Delete path

[<Fact>]
let ``recent files store keeps separate deduplicated case and option lists`` () =
    let store =
        RecentFilesStore.empty
        |> RecentFilesStore.rememberCaseFile "case-a.json"
        |> RecentFilesStore.rememberOptionFile "whb.options.json"
        |> RecentFilesStore.rememberCaseFile "case-b.json"
        |> RecentFilesStore.rememberCaseFile "case-a.json"
    let expectedCaseFiles =
        [ Path.GetFullPath "case-a.json"
          Path.GetFullPath "case-b.json" ]
    let expectedOptionFiles =
        [ Path.GetFullPath "whb.options.json" ]

    Assert.True(store.CaseFiles = expectedCaseFiles)
    Assert.True(store.OptionFiles = expectedOptionFiles)

[<Fact>]
let ``recent files store persists under local user path`` () =
    let root = Path.Combine(Path.GetTempPath(), sprintf "whb-user-%s" (Guid.NewGuid().ToString("N")))
    let storePath = Path.Combine(root, ".user", "recent-files.json")
    let casePath = Path.Combine(root, "demo-case.json")
    let optionsPath = Path.Combine(root, "whb.options.json")

    Directory.CreateDirectory root |> ignore
    File.WriteAllText(casePath, "{}")
    File.WriteAllText(optionsPath, "{}")

    try
        RecentFilesStore.persistUpdate storePath (Some optionsPath) (Some casePath)
        let stored = RecentFilesStore.load storePath
        let expectedCaseFiles = [ Path.GetFullPath casePath ]
        let expectedOptionFiles = [ Path.GetFullPath optionsPath ]

        Assert.True(stored.CaseFiles = expectedCaseFiles)
        Assert.True(stored.OptionFiles = expectedOptionFiles)
    finally
        if Directory.Exists root then Directory.Delete(root, true)
