namespace Whb.Core

open System
open Types

/// <summary>
/// Derives coupled bundle-envelope geometry from the tube field definition.
/// </summary>
/// <remarks>
/// The shared optimize/design path varies a subset of the geometric inputs. This module keeps
/// the dependent envelope dimensions aligned by preserving the current case's packing and
/// shell-build assumptions while recomputing OTL, shell ID, and baffle OD from the updated
/// tube count and pitch.
/// </remarks>
module BundleGeometry =

    [<Struct>]
    type ShellEnvelopeRule =
        { TubesheetThickness: float
          KnuckleRadius: float }

    [<Struct>]
    type LayoutCalibration =
        { Itl: float
          TubeFieldAreaFactor: float
          BaffleShellGap: float
          ShellEnvelope: ShellEnvelopeRule }

    [<Struct>]
    type DerivedEnvelope =
        { Otl: float
          ShellId: float
          BaffleOd: float }

    let private triangularPitchArea (pitch: float) =
        pitch * pitch * 0.8660254

    let private annulusArea (itl: float) (otl: float) =
        Math.PI / 4.0 * max 0.0 (otl * otl - itl * itl)

    let private inferShellEnvelopeRule (tube: TubeGeometry) =
        let knuckleRadius = 0.120
        let radialBuild = max 0.0 (0.5 * (tube.ShellId - tube.Otl))
        let tubesheetThickness = max 0.0 ((radialBuild - knuckleRadius) / 3.0)
        { TubesheetThickness = tubesheetThickness
          KnuckleRadius = knuckleRadius }

    let calibrate (tube: TubeGeometry) : LayoutCalibration =
        let pitchArea = triangularPitchArea tube.Pitch
        let actualFieldArea = annulusArea tube.Itl tube.Otl
        let tubeFieldAreaFactor =
            if tube.NTubes <= 0 || pitchArea <= 0.0 then 1.0
            else actualFieldArea / (float tube.NTubes * pitchArea)
        { Itl = tube.Itl
          TubeFieldAreaFactor = max 1e-9 tubeFieldAreaFactor
          BaffleShellGap = max 0.0 (tube.ShellId - tube.BaffleOd)
          ShellEnvelope = inferShellEnvelopeRule tube }

    let deriveOtl (calibration: LayoutCalibration) (tubeCount: int) (pitch: float) =
        let requiredFieldArea =
            float (max 1 tubeCount) * triangularPitchArea pitch * calibration.TubeFieldAreaFactor
        sqrt (max (calibration.Itl * calibration.Itl) (calibration.Itl * calibration.Itl + 4.0 * requiredFieldArea / Math.PI))

    let deriveShellId (shellRule: ShellEnvelopeRule) (otl: float) =
        otl + 2.0 * (3.0 * shellRule.TubesheetThickness + shellRule.KnuckleRadius)

    let deriveBaffleOd (calibration: LayoutCalibration) (shellId: float) =
        max 1e-6 (shellId - calibration.BaffleShellGap)

    let deriveEnvelope (calibration: LayoutCalibration) (tubeCount: int) (pitch: float) : DerivedEnvelope =
        let otl = deriveOtl calibration tubeCount pitch
        let shellId = deriveShellId calibration.ShellEnvelope otl
        { Otl = otl
          ShellId = shellId
          BaffleOd = deriveBaffleOd calibration shellId }

    let realignTubeEnvelopeWith (calibration: LayoutCalibration) (tube: TubeGeometry) : TubeGeometry =
        let derived = deriveEnvelope calibration tube.NTubes tube.Pitch
        { tube with
            Otl = derived.Otl
            ShellId = derived.ShellId
            BaffleOd = derived.BaffleOd }

    let realignTubeEnvelope (tube: TubeGeometry) : TubeGeometry =
        realignTubeEnvelopeWith (calibrate tube) tube
