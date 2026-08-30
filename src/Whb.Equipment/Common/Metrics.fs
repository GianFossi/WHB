namespace Whb.Equipment

module Metrics =

    /// <summary>
    /// Splits total weight into dry component metal and contained-fluid contributions [kg].
    /// </summary>
    [<CLIMutable>]
    type WeightBreakdown =
        { OfComponent: float
          OfInternalFluid: float }

    /// <summary>
    /// Splits total volume into component material and contained-fluid contributions [m3].
    /// </summary>
    [<CLIMutable>]
    type VolumeBreakdown =
        { OfComponent: float
          OfInternalFluid: float }

    /// <summary>
    /// Geometry-derived metrics used by components and assembled equipment.
    /// </summary>
    [<CLIMutable>]
    type ComponentMetrics =
        { Weight: WeightBreakdown
          Volume: VolumeBreakdown
          InternalArea: float
          ExternalArea: float }

    let empty =
        { Weight = { OfComponent = 0.0; OfInternalFluid = 0.0 }
          Volume = { OfComponent = 0.0; OfInternalFluid = 0.0 }
          InternalArea = 0.0
          ExternalArea = 0.0 }

    let combine (items: ComponentMetrics seq) =
        items
        |> Seq.fold
            (fun acc item ->
                { Weight =
                    { OfComponent = acc.Weight.OfComponent + item.Weight.OfComponent
                      OfInternalFluid = acc.Weight.OfInternalFluid + item.Weight.OfInternalFluid }
                  Volume =
                    { OfComponent = acc.Volume.OfComponent + item.Volume.OfComponent
                      OfInternalFluid = acc.Volume.OfInternalFluid + item.Volume.OfInternalFluid }
                  InternalArea = acc.InternalArea + item.InternalArea
                  ExternalArea = acc.ExternalArea + item.ExternalArea })
            empty
