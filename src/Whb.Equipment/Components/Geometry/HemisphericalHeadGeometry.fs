namespace Whb.Equipment

/// <summary>
/// Hemispherical head defined by inner radius, wall thickness and an optional negative cut [m].
/// </summary>
type HemisphericalHeadGeometry() =
    /// <summary>
    /// Internal radius of the full hemisphere before any negative cut [m].
    /// </summary>
    member val InnerRadius = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the head shell [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Optional removed depth measured from the pole toward the tangent line [m].
    /// </summary>
    member val NegativeCutDepth: float option = None with get, set
