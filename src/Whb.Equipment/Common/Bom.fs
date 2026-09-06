namespace Whb.Equipment

module Bom =

    /// <summary>
    /// Identifies one bill-of-material row for a component or assembled equipment item.
    /// </summary>
    /// <param name="Id">A unique identifier for the bill-of-material item.</param>
    /// <param name="Description">A textual description of the bill-of-material item.</param>
    /// <param name="Quantity">The quantity of the bill-of-material item.</param>
    /// <param name="Unit">The unit of measurement for the quantity of the bill-of
    /// material item.</param>
    /// <returns>A <c>BomItem</c> instance representing a single row in a bill of materials.</returns>
    /// <remarks>
    /// This type is useful for representing the components and materials that make up a
    /// piece of equipment or assembly, along with their respective quantities and units.
    /// </remarks>
    [<CLIMutable>]
    type BomItem =
        { Id: string
          Description: string
          Quantity: float
          Unit: string }
