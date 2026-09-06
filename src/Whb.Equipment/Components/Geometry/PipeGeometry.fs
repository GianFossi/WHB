namespace Whb.Equipment

/// <summary>
/// Pipe-like shell defined by outer diameter, wall thickness and straight length [m].
/// </summary>
type PipeGeometry() =
    /// <summary>
    /// External diameter of the pipe envelope [m].
    /// </summary>
    member val OuterDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the pipe [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Straight pipe length used for area and volume metrics [m].
    /// </summary>
    member val Length = 0.0 with get, set
