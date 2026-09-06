namespace Whb.Equipment

/// <summary>
/// Nozzle neck defined by bore, wall thickness and straight projection [m].
/// </summary>
type NozzleGeometry() =
    /// <summary>
    /// Internal bore diameter [m].
    /// </summary>
    member val InnerDiameter = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness of the nozzle neck [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Straight neck projection used for metric derivation [m].
    /// </summary>
    member val Projection = 0.0 with get, set
