namespace Whb.Equipment

/// <summary>
/// Perforated tubesheet geometry with an associated profile family.
/// </summary>
type TubesheetGeometry() =
    /// <summary>
    /// Outside diameter of the tubesheet blank [m].
    /// </summary>
    member val Diameter = 0.0 with get, set

    /// <summary>
    /// Diameter of each perforation or tube hole [m].
    /// </summary>
    member val HoleDiameter = 0.0 with get, set

    /// <summary>
    /// Total number of perforations in the tubesheet.
    /// </summary>
    member val HoleCount = 0 with get, set

    /// <summary>
    /// Mechanical profile variant used to evaluate added reinforcement or flares.
    /// </summary>
    member val Profile = Unchecked.defaultof<TubesheetProfile> with get, set
