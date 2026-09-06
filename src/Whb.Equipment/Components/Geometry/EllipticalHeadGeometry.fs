namespace Whb.Equipment

/// <summary>
/// Two-to-one elliptical head defined by internal diameter, wall thickness and an optional cylindrical skirt [m].
/// </summary>
type EllipticalHeadGeometry() =
    /// <summary>
    /// Internal tangent-line diameter of the elliptical head [m].
    /// </summary>
    member val InnerDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the head shell [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Optional positive cylindrical skirt length added below the formed head [m].
    /// </summary>
    member val CylindricalSkirtLength: float option = None with get, set
