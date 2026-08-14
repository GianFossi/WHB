namespace Whb.Core

open Whb.Core.Components
module Package =

    [<CLIMutable>]
    type Package =
        { Name: string
          Whbs: WhbComponents.Whb list
          Risers: PipingComponents.PipeRouting list
          Downcomers: PipingComponents.PipeRouting list
          SteamDrum: SteamDrumComponents.SteamDrum
          Notes: string }
    let totalMetrics (p: Package) =
        Components.Geometry.combine
            [ yield! p.Whbs |> Seq.map (fun w -> w.Metrics)
              yield! p.Risers |> Seq.map (fun r -> r.Metrics)
              yield! p.Downcomers |> Seq.map (fun d -> d.Metrics)
              yield p.SteamDrum.Metrics ]


