namespace Whb.Equipment

/// <summary>
/// Conical transition defined by left/right internal diameters, uniform wall thickness and length [m].
/// </summary>
type TransitionConeGeometry() =
    /// <summary>
    /// Internal diameter at the left or inlet side [m].
    /// </summary>
    member val LeftInnerDiameter = 0.0 with get, set

    /// <summary>
    /// Internal diameter at the right or outlet side [m].
    /// </summary>
    member val RightInnerDiameter = 0.0 with get, set

    /// <summary>
    /// Uniform radial wall thickness applied along the conical shell [m].
    /// </summary>
    member val WallThickness = 0.0 with get, set

    /// <summary>
    /// Axial cone length between the two end sections [m].
    /// </summary>
    member val Length = 0.0 with get, set
