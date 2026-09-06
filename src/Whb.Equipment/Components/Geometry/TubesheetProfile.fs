namespace Whb.Equipment

/// <summary>
/// Mechanical variants supported for a tubesheet.
/// </summary>
type TubesheetProfile =
    | Flat of thickness: float
    | FlatWithExternalReinforcement of thickness: float * reinforcementOuterDiameter: float * reinforcementThickness: float
    | WithKnucklesAndFlares of thickness: float * knuckleRadius: float * flareLength: float * flareWallThickness: float option
