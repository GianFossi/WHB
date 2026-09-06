namespace Whb.Equipment

/// <summary>
/// Rectangular thin-walled shell defined by width, height, length and thickness [m].
/// </summary>
type RectangularShellGeometry() =
    /// <summary>
    /// External shell width [m].
    /// </summary>
    member val Width = 0.0 with get, set

    /// <summary>
    /// External shell height [m].
    /// </summary>
    member val Height = 0.0 with get, set

    /// <summary>
    /// Axial shell length [m].
    /// </summary>
    member val Length = 0.0 with get, set

    /// <summary>
    /// Wall thickness applied on all sides [m].
    /// </summary>
    member val Thickness = 0.0 with get, set
