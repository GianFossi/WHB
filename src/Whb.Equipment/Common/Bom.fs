namespace Whb.Equipment

module Bom =

    /// <summary>
    /// Identifies one bill-of-material row for a component or assembled equipment item.
    /// </summary>
    [<CLIMutable>]
    type BomItem =
        { Id: string
          Description: string
          Quantity: float
          Unit: string }
