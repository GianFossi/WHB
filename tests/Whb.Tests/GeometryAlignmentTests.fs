module GeometryAlignmentTests

open Whb.Core
open Whb.Core.Types
open Xunit

let private shellGap (tube: TubeGeometry) =
    tube.ShellId - tube.BaffleOd

[<Fact>]
let ``bundle geometry realignment preserves the reference geometry when nothing changes`` () =
    let tube = Defaults.referenceCase.Tube
    let aligned = BundleGeometry.realignTubeEnvelope tube

    Assert.Equal(tube.Otl, aligned.Otl, 12)
    Assert.Equal(tube.ShellId, aligned.ShellId, 12)
    Assert.Equal(tube.BaffleOd, aligned.BaffleOd, 12)

[<Fact>]
let ``tube pitch changes realign OTL shell ID and baffle gap through the shared geometry pipeline`` () =
    let baseCase = Defaults.referenceCase
    let calibration = BundleGeometry.calibrate baseCase.Tube
    let targetPitch = 0.07449
    let targetCount = 1184.0
    let result =
        Optimize.applyVariables
            baseCase
            [ { Key = Optimize.TubeCount
                Name = "numero tubi"
                Current = float baseCase.Tube.NTubes
                Lower = targetCount
                Upper = targetCount
                Step = 1.0
                Unit = "-" }
              { Key = Optimize.TubePitchM
                Name = "passo tubi"
                Current = baseCase.Tube.Pitch
                Lower = targetPitch
                Upper = targetPitch
                Step = 1.0
                Unit = "m" } ]
            [| targetCount; targetPitch |]
    let expectedOtl = BundleGeometry.deriveOtl calibration (int targetCount) targetPitch
    let expectedShellId = BundleGeometry.deriveShellId calibration.ShellEnvelope expectedOtl

    Assert.Equal(expectedOtl, result.Tube.Otl, 12)
    Assert.Equal(expectedShellId, result.Tube.ShellId, 12)
    Assert.Equal(shellGap baseCase.Tube, shellGap result.Tube, 12)

[<Fact>]
let ``explicit shell overrides still win after dependent geometry realignment`` () =
    let baseCase = Defaults.referenceCase
    let manualShellId = 2.300
    let targetPitch = 0.07449
    let result =
        Optimize.applyVariables
            baseCase
            [ { Key = Optimize.ShellInnerDiameterM
                Name = "diametro interno mantello"
                Current = baseCase.Tube.ShellId
                Lower = manualShellId
                Upper = manualShellId
                Step = 1.0
                Unit = "m" }
              { Key = Optimize.TubePitchM
                Name = "passo tubi"
                Current = baseCase.Tube.Pitch
                Lower = targetPitch
                Upper = targetPitch
                Step = 1.0
                Unit = "m" } ]
            [| manualShellId; targetPitch |]

    Assert.Equal(manualShellId, result.Tube.ShellId, 12)
    Assert.Equal(shellGap baseCase.Tube, shellGap result.Tube, 12)

[<Fact>]
let ``greenfield design without explicit shell IDs keeps the derived shell envelope`` () =
    let baseCase = Defaults.referenceCase
    let targetPitch = 0.08719
    let targetCount = 1408
    let space : GreenfieldDesign.DesignSpace =
        { TubeCounts = [ targetCount ]
          TubeLengthsM = [ 16.0 ]
          FerruleLengthsMm = [ 500.0 ]
          ShellInnerDiametersM = []
          TubeSizeOptions =
            [ ({ OuterDiameterM = 0.0508
                 PitchM = targetPitch } : GreenfieldDesign.TubeSizeOption) ]
          TubePitchesM = []
          DrumCenterlineHeightsM = [ 6.0 ] }
    let input : GreenfieldDesign.DesignInput =
        { TemplateCase = baseCase
          LoadCases = [ LoadCases.baseCase "base" ]
          Constraints =
            { Targets =
                [ { Key = ConstraintModel.MinDNBR
                    Name = "DNBR"
                    Domain = ConstraintModel.Thermal
                    Unit = "-"
                    Limit = ConstraintModel.Min 0.0
                    Required = false
                    Weight = 1.0 } ] }
          Objective =
            { Terms =
                [ { Key = ConstraintModel.WhbWeightKg
                    Name = "Estimated WHB weight"
                    Weight = 1.0
                    Scale = None
                    Sense = Optimize.Minimize } ] }
          Space = space
          RunSettings =
            { Design.defaultRunSettings with
                Parallelism = 1
                GasPropertyCache = false } }
    let expectedShellId =
        Optimize.applyVariables
            baseCase
            [ { Key = Optimize.TubeCount
                Name = "numero tubi"
                Current = float targetCount
                Lower = float targetCount
                Upper = float targetCount
                Step = 1.0
                Unit = "-" }
              { Key = Optimize.TubeOuterDiameterM
                Name = "diametro esterno tubi"
                Current = 0.0508
                Lower = 0.0508
                Upper = 0.0508
                Step = 1.0
                Unit = "m" }
              { Key = Optimize.TubePitchM
                Name = "passo tubi"
                Current = targetPitch
                Lower = targetPitch
                Upper = targetPitch
                Step = 1.0
                Unit = "m" } ]
            [||]
        |> fun c -> c.Tube.ShellId
    let result = GreenfieldDesign.run input

    Assert.Equal(expectedShellId, result.Best.Case.Tube.ShellId, 12)
    Assert.NotEqual(baseCase.Tube.ShellId, result.Best.Case.Tube.ShellId, 12)
