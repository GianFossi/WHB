namespace Whb.Equipment

/// <summary>
/// Flat baffle plate with an optional segmental cut fraction.
/// </summary>
type BaffleGeometry() =
    /// <summary>
    /// Full circular plate diameter before any cut is applied [m].
    /// </summary>
    member val Diameter = 0.0 with get, set

    /// <summary>
    /// Plate thickness [m].
    /// </summary>
    member val Thickness = 0.0 with get, set

    /// <summary>
    /// Removed area fraction for segmental windows, clamped to the [0, 1] interval.
    /// </summary>
    member val CutFraction = 0.0 with get, set
