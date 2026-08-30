module OutputPathTests

open System.IO
open Whb.Cli
open Xunit

let private resultsRoot =
    Path.GetFullPath "results"

[<Fact>]
let ``blank report directory resolves to results root`` () =
    let resolved = OutputPaths.reportDirectory "results" ""

    Assert.Equal(resultsRoot, resolved)

[<Fact>]
let ``relative report directory is nested under results root`` () =
    let resolved = OutputPaths.reportDirectory "results" "design/run1"

    Assert.Equal(Path.Combine(resultsRoot, "design", "run1"), resolved)

[<Fact>]
let ``existing results-prefixed report directory is preserved`` () =
    let requested = Path.Combine("results", "design", "run1")
    let resolved = OutputPaths.reportDirectory "results" requested

    Assert.Equal(Path.GetFullPath requested, resolved)

[<Fact>]
let ``external absolute report directory is folded back under results root`` () =
    let externalPath = Path.Combine(Path.GetTempPath(), "whb-audit", "run1")
    let resolved = OutputPaths.reportDirectory "results" externalPath

    Assert.Equal(Path.Combine(resultsRoot, "run1"), resolved)

[<Fact>]
let ``report files are also folded under results root`` () =
    let resolved = OutputPaths.reportFile "results" "steam/steam.csv" "steam.csv"

    Assert.Equal(Path.Combine(resultsRoot, "steam", "steam.csv"), resolved)
