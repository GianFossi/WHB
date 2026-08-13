namespace Whb.Core.Components

open System
open Whb.Core.Components.Geometry

module PipingComponents =

    [<CLIMutable>]
    type Pipe =
        { Tag: string; Di: float; Do: float; Length: float; Material: MaterialRef }
        member x.Metrics = Geometry.cylinderShell x.Material x.Di x.Do x.Length

    [<CLIMutable>]
    type Elbow =
        { Tag: string; Di: float; Do: float; AngleDeg: float; ROverD: float; Count: int; Material: MaterialRef }
        member x.ArcLength = Math.PI * x.AngleDeg / 180.0 * x.ROverD * x.Di
        member x.Metrics =
            let m = Geometry.cylinderShell x.Material x.Di x.Do x.ArcLength
            { m with Weight = m.Weight * float x.Count; MetalVolume = m.MetalVolume * float x.Count; InternalVolume = m.InternalVolume * float x.Count; ExternalVolume = m.ExternalVolume * float x.Count; InternalArea = m.InternalArea * float x.Count; ExternalArea = m.ExternalArea * float x.Count }

    [<CLIMutable>]
    type Reducer =
        { Tag: string; Di1: float; Do1: float; Di2: float; Do2: float; Length: float; Material: MaterialRef }
        member x.Metrics =
            let avgDi = 0.5 * (x.Di1 + x.Di2)
            let avgDo = 0.5 * (x.Do1 + x.Do2)
            Geometry.cylinderShell x.Material avgDi avgDo x.Length

    type PipeSegment =
        | Straight of Pipe
        | Bend of Elbow
        | Transition of Reducer

    [<CLIMutable>]
    type PipeRouting =
        { Tag: string; Service: string; Segments: PipeSegment list; Notes: string }
        member x.DevelopedLength =
            x.Segments
            |> List.sumBy (function Straight p -> p.Length | Bend e -> e.ArcLength * float e.Count | Transition r -> r.Length)
        member x.Metrics =
            x.Segments
            |> Seq.map (function Straight p -> p.Metrics | Bend e -> e.Metrics | Transition r -> r.Metrics)
            |> Geometry.combine
