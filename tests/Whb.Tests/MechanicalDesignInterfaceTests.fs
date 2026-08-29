module MechanicalDesignInterfaceTests

open Whb.Core
open Whb.Core.Types
open Xunit

let private deterministicSettings =
    { Design.defaultRunSettings with
        Parallelism = 1
        GasPropertyCache = false }

let private benchmarkCase (caseIn: DesignCase) =
    { caseIn with
        NZ = 6
        NY = 2 }

[<Fact>]
let ``mechanical design interface exposes the requested future calculation packages`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let thermal = DesignThermalProcess.runPure deterministicSettings baseCase
    let mechanical = DesignMechanical.runPure thermal
    let iface = mechanical.CalculationInterface

    Assert.Equal(MechanicalDesignContracts.ReadyForImplementation, iface.TubeThickness.Status)
    Assert.Equal(MechanicalDesignContracts.ReadyForImplementation, iface.CreviceFreeWeld.Status)
    Assert.True(iface.BypassCentralWallThickness.IsSome)
    Assert.Equal(MechanicalDesignContracts.ReadyForImplementation, iface.BypassCentralWallThickness.Value.Status)
    Assert.Equal(MechanicalDesignContracts.NeedsAdditionalGeometry, iface.ChannelWallThickness.Status)
    Assert.Contains("channel inner diameter", iface.ChannelWallThickness.MissingInputs)
    Assert.Equal(MechanicalDesignContracts.NeedsAdditionalGeometry, iface.ShellWallThickness.Status)
    Assert.Contains("shell external pressure", iface.ShellWallThickness.MissingInputs)
    Assert.Equal(MechanicalDesignContracts.NeedsAdditionalGeometry, iface.TubesheetThickness.Status)
    Assert.Contains("channel inner diameter", iface.TubesheetThickness.MissingInputs)

[<Fact>]
let ``mechanical design interface is deterministic for the same thermal and screening inputs`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let thermal = DesignThermalProcess.runPure deterministicSettings baseCase
    let mechanical1 = DesignMechanical.runPure thermal
    let mechanical2 = DesignMechanical.runPure thermal
    let missing1 = mechanical1.CalculationInterface.TubesheetThickness.MissingInputs
    let missing2 = mechanical2.CalculationInterface.TubesheetThickness.MissingInputs

    Assert.Equal(mechanical1.CalculationInterface.TubeThickness.Input.Loads.InternalPressure.Value,
                 mechanical2.CalculationInterface.TubeThickness.Input.Loads.InternalPressure.Value)
    Assert.Equal(mechanical1.CalculationInterface.CreviceFreeWeld.Input.AxialLoadPerTube.Value,
                 mechanical2.CalculationInterface.CreviceFreeWeld.Input.AxialLoadPerTube.Value)
    Assert.True((missing1 = missing2))

[<Fact>]
let ``mechanical design interface rebuilt from design result matches the staged interface`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let thermal = DesignThermalProcess.runPure deterministicSettings baseCase
    let staged = DesignMechanical.runPure thermal
    let design = Design.runWithSettingsAndProgress deterministicSettings ignore baseCase
    let rebuilt = MechanicalDesignInterface.fromDesignResult design

    Assert.Equal(staged.CalculationInterface.TubeThickness.Status, rebuilt.TubeThickness.Status)
    Assert.Equal(staged.CalculationInterface.TubeThickness.Input.Loads.InternalPressure.Value,
                 rebuilt.TubeThickness.Input.Loads.InternalPressure.Value)
    Assert.True(staged.CalculationInterface.ShellWallThickness.MissingInputs = rebuilt.ShellWallThickness.MissingInputs)
    Assert.True(staged.CalculationInterface.TubesheetThickness.MissingInputs = rebuilt.TubesheetThickness.MissingInputs)

[<Fact>]
let ``mechanical interface report is deterministic and exposes missing inputs`` () =
    let baseCase = benchmarkCase Defaults.referenceCase
    let design = Design.runWithSettingsAndProgress deterministicSettings ignore baseCase
    let report1 = Report.mechanicalInterfaceText design
    let report2 = Report.mechanicalInterfaceText design

    Assert.Equal(report1, report2)
    Assert.Contains("MECHANICAL CALCULATION INTERFACE", report1)
    Assert.Contains("Tube thickness", report1)
    Assert.Contains("Channel shell thickness", report1)
    Assert.Contains("INCOMPLETE", report1)
    Assert.Contains("channel inner diameter", report1)
