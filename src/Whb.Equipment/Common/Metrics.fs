namespace Whb.Equipment

/// <summary>
/// Common metrics used by components and assembled equipment.
/// </summary>
/// <remarks>
/// This module is intended to be used by both the Whb.Equipment and Whb.
/// </remarks>
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

    /// <summary>
    /// Provides a zeroed-out instance of <c>ComponentMetrics</c>.
    /// </summary>
    /// <returns>A <c>ComponentMetrics</c> instance with all fields set to zero.</returns>
    /// <remarks>
    /// This function is useful for initializing accumulators or default values in calculations.
    /// </remarks>
    let empty =
        { Weight = { OfComponent = 0.0; OfInternalFluid = 0.0 }
          Volume = { OfComponent = 0.0; OfInternalFluid = 0.0 }
          InternalArea = 0.0
          ExternalArea = 0.0 }

    /// <summary>
    /// Combines a sequence of <c>ComponentMetrics</c> instances into a single
    /// <c>ComponentMetrics</c> instance by summing their respective fields.
    /// </summary>
    /// <param name="items">A sequence of <c>ComponentMetrics</c>
    /// instances to be combined.</param>
    /// <returns>A single <c>ComponentMetrics</c> instance representing the combined metrics
    /// of all input items.</returns>   
    /// <remarks>
    /// This function is useful for aggregating metrics from multiple components or equipment items.
    /// </remarks>
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
