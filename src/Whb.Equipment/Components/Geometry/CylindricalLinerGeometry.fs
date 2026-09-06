namespace Whb.Equipment

/// <summary>
/// Cylindrical liner defined by internal diameter, wall thickness and length [m].
/// </summary>
type CylindricalLinerGeometry() =
    /// <summary>
    /// Internal diameter of the liner passage [m].
    /// </summary>
    member val InnerDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the liner [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Straight liner length [m].
    /// </summary>
    member val Length = 0.0 with get, set
