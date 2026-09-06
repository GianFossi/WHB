namespace Whb.Equipment

/// <summary>
/// Torispherical head defined by tangent-line diameter, wall thickness, crown radius, knuckle radius and an optional cylindrical skirt [m].
/// </summary>
type TorisphericalHeadGeometry() =
    /// <summary>
    /// Internal tangent-line diameter of the torispherical head [m].
    /// </summary>
    member val InnerDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the formed head shell [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Internal spherical crown radius of the head [m].
    /// </summary>
    member val CrownRadius = 0.0 with get, set

    /// <summary>
    /// Internal knuckle radius of the head [m].
    /// </summary>
    member val KnuckleRadius = 0.0 with get, set

    /// <summary>
    /// Optional cylindrical skirt length appended to the head [m].
    /// </summary>
    member val CylindricalSkirtLength: float option = None with get, set

    /// <summary>
    /// Optional skirt inner diameter override [m]. When omitted, the head inner diameter is reused.
    /// </summary>
    member val CylindricalSkirtInnerDiameter: float option = None with get, set

    /// <summary>
    /// Optional skirt wall-thickness override [m]. When omitted, the head wall thickness is reused.
    /// </summary>
    member val CylindricalSkirtWallThickness: float option = None with get, set
