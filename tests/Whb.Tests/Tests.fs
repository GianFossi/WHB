/// <summary>
/// Provides tests functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Tests

open Whb.Core
open Whb.Core.Constants
open Xunit

[<Fact>]
let ``core constants are available to tests`` () =
    Assert.Equal(273.15, cToK 0.0, 6)

[<Fact>]
let ``unit conversions round trip`` () =
    Assert.Equal(1.0, 1.0 |> barToPa |> paToBar, 12)
    Assert.Equal(25.0, 25.0 |> cToK |> kToC, 12)
    Assert.Equal(0.032, mmToM 32.0, 12)

[<Fact>]
let ``bisect handles normal and reversed brackets`` () =
    /// <summary>
    /// Calculates or returns f for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let f x = x * x - 4.0

    Assert.Equal(2.0, bisect f 0.0 5.0 1e-10 200, 8)
    Assert.Equal(2.0, bisect f 5.0 0.0 1e-10 200, 8)

[<Fact>]
let ``graded axial grid conserves length`` () =
    /// <summary>
    /// Calculates or returns centers for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let centers, widths = gradedAxialGrid 12.0 10 6.0

    Assert.Equal(10, centers.Length)
    Assert.Equal(10, widths.Length)
    Assert.Equal(12.0, Array.sum widths, 8)
    Assert.True(widths.[0] < widths.[widths.Length - 1])

[<Fact>]
let ``piping line converts geometry and totals`` () =
    /// <summary>
    /// Calculates or returns l for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let l =
        Piping.line "L1" "10\"" 250.0 2 [ 1.0; 2.0 ] [ Piping.elbow 90.0 1.5 2 ] 0.25 3.0 180.0 "test"

    Assert.Equal(0.25, l.Id, 12)
    Assert.Equal(2, Piping.elbowCount l)
    Assert.True(Piping.developedLength l > 3.0)
    Assert.True(Piping.totalArea [ l ] > Piping.area l)
    Assert.Contains("curve", Piping.billOfMaterial l)

[<Fact>]
let ``material lookup returns requested material or fallback`` () =
    Assert.Contains("T11", Materials.byName "T11" |> fun m -> m.Name)
    Assert.Equal(Materials.carbonSteel.Name, (Materials.byName "not-a-material").Name)
