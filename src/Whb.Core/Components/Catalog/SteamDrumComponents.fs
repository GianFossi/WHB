namespace Whb.Core.Components

open System
open Whb.Core.Components.Geometry
module SteamDrumComponents =

    [<CLIMutable>]
    type Head =
        { Tag: string; HeadType: string; Di: float; Thickness: float; CrownDepth: float; Material: MaterialRef }
        member x.Metrics =
            let meanD = x.Di + x.Thickness
            let area = Math.PI * meanD * max x.CrownDepth (0.25 * meanD)
            let vm = area * x.Thickness
            { MetalVolume = vm; InternalVolume = Math.PI * x.Di * x.Di / 4.0 * x.CrownDepth / 3.0; ExternalVolume = 0.0; InternalArea = area; ExternalArea = area; Weight = vm * x.Material.Density }

    [<CLIMutable>]
    type ExpansionBox =
        { Tag: string; Width: float; Height: float; Length: float; Thickness: float; Count: int; Material: MaterialRef }
        member x.Metrics =
            let outer = x.Width * x.Height * x.Length
            let inner = max 0.0 (x.Width - 2.0 * x.Thickness) * max 0.0 (x.Height - 2.0 * x.Thickness) * x.Length
            let vm = max 0.0 (outer - inner)
            { MetalVolume = vm * float x.Count; InternalVolume = inner * float x.Count; ExternalVolume = outer * float x.Count; InternalArea = 2.0 * (x.Width * x.Length + x.Height * x.Length) * float x.Count; ExternalArea = 2.0 * (x.Width * x.Length + x.Height * x.Length) * float x.Count; Weight = vm * float x.Count * x.Material.Density }

    [<CLIMutable>]
    type Demister =
        { Tag: string; Area: float; Thickness: float; Density: float; Material: MaterialRef }
        member x.Metrics =
            let v = max 0.0 (x.Area * x.Thickness)
            { Geometry.emptyMetrics with MetalVolume = v; ExternalVolume = v; ExternalArea = x.Area; Weight = v * x.Density }

    [<CLIMutable>]
    type LevelDefinition =
        { LowLow: float; Low: float; Normal: float; High: float; HighHigh: float }

    [<CLIMutable>]
    type SteamDrum =
        { Tag: string
          ShellBarrels: WhbComponents.ShellBarrel list
          Heads: Head list
          Nozzles: WhbComponents.Nozzle list
          RiserExpansionBoxes: ExpansionBox list
          Demister: Demister option
          Levels: LevelDefinition }
        member x.Metrics =
            Geometry.combine
                [ yield! x.ShellBarrels |> Seq.map (fun b -> b.Metrics)
                  yield! x.Heads |> Seq.map (fun h -> h.Metrics)
                  yield! x.Nozzles |> Seq.map (fun n -> n.Metrics)
                  yield! x.RiserExpansionBoxes |> Seq.map (fun b -> b.Metrics)
                  match x.Demister with Some d -> yield d.Metrics | None -> () ]


