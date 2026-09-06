namespace Whb.Equipment

/// <summary>
/// Pipe elbow defined by outer diameter, wall thickness, bend angle and centerline radius.
/// </summary>
type PipeElbowGeometry() =
    /// <summary>
    /// Outside diameter of the elbow body [m].
    /// </summary>
    member val OuterDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the elbow [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Bend angle of the elbow [deg].
    /// </summary>
    member val AngleDeg = 0.0 with get, set

    /// <summary>
    /// Centerline bend radius expressed as a multiple of the elbow diameter [-].
    /// </summary>
    member val CenterlineRadiusOverDiameter = 0.0 with get, set

    /// <summary>
    /// Fraction of a full elbow represented by this geometry, clamped to the [0, 1] interval.
    /// </summary>
    member val CoverageFraction = 0.0 with get, set
