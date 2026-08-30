namespace Whb.Tests

open System
open Whb.Equipment
open Whb.Core
open Xunit

module EquipmentModelTests =

    let private approxEqual tolerance expected actual =
        Assert.True(abs (expected - actual) <= tolerance, sprintf "Expected %.12f, got %.12f" expected actual)

    let private deterministicSettings =
        { Design.defaultRunSettings with
            Parallelism = 1
            GasPropertyCache = false }

    let private bom id description =
        let item : Bom.BomItem =
            { Id = id
              Description = description
              Quantity = 1.0
              Unit = "ea" }

        item

    [<Fact>]
    let ``component metrics are derived from geometry material and internal fluid`` () =
        let steel = Materials.getMaterialByName "SA-192"
        let water : Materials.FluidProperties = { Name = "BFW"; Density = 998.0 }
        let spool =
            PressureParts.shellBarrel
                "PIPE-001"
                "Feed spool"
                (bom "BOM-PIPE-001" "Feed spool")
                0.100
                0.110
                2.0
                steel
                (Some water)

        let metrics = spool.Metrics
        let expectedMetalVolume = Math.PI * (0.110 * 0.110 - 0.100 * 0.100) / 4.0 * 2.0
        let expectedFluidVolume = Math.PI * 0.100 * 0.100 / 4.0 * 2.0

        approxEqual 1e-12 expectedMetalVolume metrics.Volume.OfComponent
        approxEqual 1e-12 expectedFluidVolume metrics.Volume.OfInternalFluid
        approxEqual 1e-9 (expectedMetalVolume * steel.Density) metrics.Weight.OfComponent
        approxEqual 1e-9 (expectedFluidVolume * water.Density) metrics.Weight.OfInternalFluid

    [<Fact>]
    let ``fluid-only regions contribute derived internal-fluid metrics without fake metal weight`` () =
        let gas : Materials.FluidProperties = { Name = "Gas hold-up"; Density = 4.5 }
        let region =
            Component.createFluidRegion
                "INV-001"
                "Resolved gas hold-up"
                (bom "BOM-INV-001" "Resolved gas hold-up")
                (Geometry.Cylinder
                    { InnerDiameter = 0.250
                      WallThickness = 0.0
                      Length = 3.0 })
                gas

        let expectedVolume = Math.PI * 0.250 * 0.250 / 4.0 * 3.0

        approxEqual 1e-12 0.0 region.Metrics.Volume.OfComponent
        approxEqual 1e-12 expectedVolume region.Metrics.Volume.OfInternalFluid
        approxEqual 1e-12 0.0 region.Metrics.Weight.OfComponent
        approxEqual 1e-9 (expectedVolume * gas.Density) region.Metrics.Weight.OfInternalFluid

    [<Fact>]
    let ``cylinder and pipe geometries stay equivalent when describing the same spool`` () =
        let cylinder =
            Geometry.Cylinder
                { InnerDiameter = 0.100
                  WallThickness = 0.005
                  Length = 2.0 }

        let pipe =
            Geometry.Pipe
                { OuterDiameter = 0.110
                  WallThickness = 0.005
                  Length = 2.0 }

        let cylinderMetrics = Geometry.evaluate cylinder
        let pipeMetrics = Geometry.evaluate pipe

        approxEqual 1e-12 cylinderMetrics.ComponentVolume pipeMetrics.ComponentVolume
        approxEqual 1e-12 cylinderMetrics.InternalFluidVolume pipeMetrics.InternalFluidVolume
        approxEqual 1e-12 (Geometry.referenceLength cylinder) (Geometry.referenceLength pipe)

    [<Fact>]
    let ``whb metrics include central bypass components`` () =
        let steel = Materials.getMaterialByName "SA-192"
        let hotAlloy = Materials.getMaterialByName "602 CA"
        let gas : Materials.FluidProperties = { Name = "Syngas"; Density = 3.0 }

        let tubeBank =
            PressureParts.tubeBank
                "TB-TUBES"
                "Tube bank"
                (bom "BOM-TUBE-BANK" "Tube bank")
                0.020
                0.025
                6.0
                120
                steel
                (Some gas)

        let diaphragm =
            PressureParts.diaphragm
                "TB-DIAPHRAGMS"
                "Diaphragms"
                (bom "BOM-DIAPHRAGMS" "Diaphragms")
                1.400
                0.012
                8
                steel

        let shell =
            PressureParts.shellBarrel
                "WHB-SHELL"
                "WHB shell"
                (bom "BOM-WHB-SHELL" "WHB shell")
                1.600
                1.640
                6.0
                steel
                None

        let tubesheet =
            PressureParts.tubesheet
                "TS-001"
                "Tubesheets"
                (bom "BOM-TS" "Tubesheets")
                1.550
                0.100
                0.026
                120
                2
                steel

        let nozzle =
            PressureParts.nozzle
                "NZ-001"
                "Steam outlet"
                (bom "BOM-NZ-001" "Steam outlet nozzle")
                "Steam"
                0.250
                0.280
                0.180
                1
                steel
                None

        let ferrules =
            PressureParts.ferrule
                "FERR-001"
                "Ferrules"
                (bom "BOM-FERRULES" "Ferrules")
                0.022
                0.028
                0.450
                120
                hotAlloy
                (Some gas)

        let centralBypass =
            EquipmentAssemblies.centralBypass
                "CBP-001"
                "Central bypass"
                (bom "BOM-CBP" "Central bypass")
                [ PressureParts.liner
                    "CBP-LINER"
                    "Central bypass liner"
                    (bom "BOM-CBP-LINER" "Central bypass liner")
                    0.350
                    0.370
                    6.0
                    hotAlloy
                    (Some gas)
                  PressureParts.shellBarrel
                    "CBP-SHELL"
                    "Central bypass shell"
                    (bom "BOM-CBP-SHELL" "Central bypass shell")
                    0.450
                    0.480
                    6.0
                    steel
                    None ]

        let bypassValve =
            PressureParts.valveBody
                "BV-001"
                "Bypass valve"
                (bom "BOM-BV-001" "Bypass valve")
                0.300
                0.500
                0.420
                steel
                (Some gas)

        let tubeBundle =
            EquipmentAssemblies.tubeBundle
                "TB-001"
                "Tube bundle"
                (bom "BOM-TB" "Tube bundle")
                tubeBank
                [ diaphragm; ferrules ]

        let whb =
            { Id = "WHB-001"
              Name = "Main WHB"
              Bom = bom "BOM-WHB" "Main WHB"
              Components = [ tubeBundle; shell; tubesheet; nozzle; centralBypass; bypassValve ] }

        let componentIds = whb.Components |> List.map (fun part -> part.Id)
        let allIds =
            whb.Components
            |> Seq.collect Component.descendantsAndSelf
            |> Seq.map (fun part -> part.Id)
            |> Set.ofSeq

        Assert.Contains("TB-001", componentIds)
        Assert.Contains("CBP-001", componentIds)
        Assert.Contains("CBP-LINER", allIds)
        Assert.Contains("CBP-SHELL", allIds)
        Assert.Contains("FERR-001", allIds)
        Assert.Contains("TB-DIAPHRAGMS", allIds)
        approxEqual 1e-9 (Component.totalMetrics whb.Components).Weight.OfComponent whb.Metrics.Weight.OfComponent

    [<Fact>]
    let ``equipment package can be created from the Whb Core port`` () =
        let steel = Materials.getMaterialByName "SA-192"
        let shell =
            PressureParts.shellBarrel
                "DRUM-SHELL"
                "Steam drum shell"
                (bom "BOM-DRUM-SHELL" "Steam drum shell")
                1.200
                1.230
                8.0
                steel
                None

        let drum =
            { Id = "DRUM-001"
              Name = "Steam drum"
              Bom = bom "BOM-DRUM" "Steam drum"
              Components = [ shell ]
              Levels = { LowLow = 0.2; Low = 0.4; Normal = 0.6; High = 0.8; HighHigh = 1.0 } }

        let source =
            { new Interop.IWhbCoreEquipmentSnapshot with
                member _.PackageName = "Reference package"
                member _.Whbs = []
                member _.Risers = []
                member _.Downcomers = []
                member _.SteamDrum = drum
                member _.Notes = "Bridge contract" }

        let package = EquipmentPackage.ofWhbCore source

        Assert.Equal("Reference package", package.Name)
        Assert.Equal("DRUM-001", package.SteamDrum.Id)

    [<Fact>]
    let ``equipment package can be derived from a design case`` () =
        let package = Package.ofDesignCase Defaults.referenceCase
        let whb = package.Whbs |> List.exactlyOne
        let topLevelIds = whb.Components |> List.map (fun part -> part.Id)
        let allIds =
            whb.Components
            |> Seq.collect Component.descendantsAndSelf
            |> Seq.map (fun part -> part.Id)
            |> Set.ofSeq

        Assert.Contains("WHB-TUBE-BUNDLE", topLevelIds)
        Assert.Contains("WHB-CENTRAL-BYPASS", topLevelIds)
        Assert.Contains("WHB-BP-LINER", allIds)
        Assert.Contains("WHB-BP-VALVE", allIds)
        Assert.Contains("WHB-TB-DIAPHRAGMS", allIds)
        Assert.Equal(Defaults.referenceCase.Loop.Risers.Length, package.Risers.Length)
        Assert.Equal(Defaults.referenceCase.Loop.Downcomers.Length, package.Downcomers.Length)
        Assert.Equal(Defaults.referenceCase.Loop.Drum.NormalLevel, package.SteamDrum.Levels.Normal, 12)

    [<Fact>]
    let ``equipment package can be enriched from a design result`` () =
        let resolvedCase =
            { Defaults.referenceCase with
                NZ = 6
                NY = 2 }

        let basePackage = Package.ofDesignCase resolvedCase
        let design = Design.runWithSettingsAndProgress deterministicSettings ignore resolvedCase
        let package = Package.ofDesignResult design
        let whb = package.Whbs |> List.exactlyOne
        let allWhbIds =
            whb.Components
            |> Seq.collect Component.descendantsAndSelf
            |> Seq.map (fun part -> part.Id)
            |> Set.ofSeq

        Assert.Contains("resolved", package.Notes)
        Assert.Contains("WHB-TB-GAS-HOLDUP", allWhbIds)
        Assert.Contains("WHB-BP-GAS-HOLDUP", allWhbIds)
        Assert.Contains("WHB-SHELL-LIQUID-INVENTORY", whb.Components |> List.map (fun part -> part.Id))
        Assert.Contains("STEAM-DRUM-LIQUID-INVENTORY", package.SteamDrum.Components |> List.map (fun part -> part.Id))
        Assert.True(package.Risers |> List.sumBy (fun line -> line.Metrics.Weight.OfInternalFluid) > 0.0)
        Assert.True(package.Metrics.Weight.OfInternalFluid > basePackage.Metrics.Weight.OfInternalFluid)
