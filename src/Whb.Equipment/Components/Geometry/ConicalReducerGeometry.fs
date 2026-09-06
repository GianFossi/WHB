namespace Whb.Equipment

/// <summary>
/// Conical reducer defined by outer diameters, local wall thicknesses and axial length [m].
/// </summary>
type ConicalReducerGeometry() =
    /// <summary>
    /// Outside diameter at the inlet or larger end [m].
    /// </summary>
    member val OuterDiameterIn = 0.0 with get, set

    /// <summary>
    /// Outside diameter at the outlet or smaller end [m].
    /// </summary>
    member val OuterDiameterOut = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness at the inlet end [m].
    /// </summary>
    member val WallThicknessIn = 0.0 with get, set

    /// <summary>
    /// Radial wall thickness at the outlet end [m].
    /// </summary>
    member val WallThicknessOut = 0.0 with get, set

    /// <summary>
    /// Axial reducer length [m].
    /// </summary>
    member val Length = 0.0 with get, set
