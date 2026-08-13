namespace Whb.Core

open Whb.Core.Components

/// <summary>
/// Provides package functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Package =

    [<CLIMutable>]
    /// <summary>
    /// Represents package data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Package =
        { Name: string
          Whbs: WhbComponents.Whb list
          Risers: PipingComponents.PipeRouting list
          Downcomers: PipingComponents.PipeRouting list
          SteamDrum: SteamDrumComponents.SteamDrum
          Notes: string }

    /// <summary>
    /// Calculates or returns totalmetrics for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let totalMetrics (p: Package) =
        Components.Geometry.combine
            [ yield! p.Whbs |> Seq.map (fun w -> w.Metrics)
              yield! p.Risers |> Seq.map (fun r -> r.Metrics)
              yield! p.Downcomers |> Seq.map (fun d -> d.Metrics)
              yield p.SteamDrum.Metrics ]
