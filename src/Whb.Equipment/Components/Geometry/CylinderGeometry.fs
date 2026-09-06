namespace Whb.Equipment

/// <summary>
/// Cylindrical shell defined by internal diameter, wall thickness and straight length [m].
/// </summary>
type CylinderGeometry() =
    /// <summary>
    /// Internal hydraulic diameter of the cylinder [m].
    /// </summary>
    member val InnerDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the cylinder [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Straight cylindrical length used for area and volume metrics [m].
    /// </summary>
    member val Length = 0.0 with get, set
