namespace Whb.Equipment

/// <summary>
/// Porous insert represented by projected area and thickness [m].
/// </summary>
type PorousPadGeometry() =
    /// <summary>
    /// Projected face area of the pad [m2].
    /// </summary>
    member val Area = 0.0 with get, set

    /// <summary>
    /// Pad thickness through the flow direction [m].
    /// </summary>
    member val Thickness = 0.0 with get, set
