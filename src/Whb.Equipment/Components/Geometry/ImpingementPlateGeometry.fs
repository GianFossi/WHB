namespace Whb.Equipment

/// <summary>
/// Flat impingement plate defined by width, height and thickness [m].
/// </summary>
type ImpingementPlateGeometry() =
    /// <summary>
    /// Plate width [m].
    /// </summary>
    member val Width = 0.0 with get, set

    /// <summary>
    /// Plate height [m].
    /// </summary>
    member val Height = 0.0 with get, set

    /// <summary>
    /// Plate thickness [m].
    /// </summary>
    member val Thickness = 0.0 with get, set
