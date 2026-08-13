namespace Whb.Core.Components

open System
open Whb.Core.Components.Geometry

/// <summary>
/// Provides pipingcomponents functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module PipingComponents =

    [<CLIMutable>]
    /// <summary>
    /// Represents pipe data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Pipe =
        { Tag: string; Di: float; Do: float; Length: float; Material: MaterialRef }
        member x.Metrics = Geometry.cylinderShell x.Material x.Di x.Do x.Length

    [<CLIMutable>]
    /// <summary>
    /// Represents elbow data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Elbow =
        { Tag: string; Di: float; Do: float; AngleDeg: float; ROverD: float; Count: int; Material: MaterialRef }
        member x.ArcLength = Math.PI * x.AngleDeg / 180.0 * x.ROverD * x.Di
        member x.Metrics =
            let m = Geometry.cylinderShell x.Material x.Di x.Do x.ArcLength
            { m with Weight = m.Weight * float x.Count; MetalVolume = m.MetalVolume * float x.Count; InternalVolume = m.InternalVolume * float x.Count; ExternalVolume = m.ExternalVolume * float x.Count; InternalArea = m.InternalArea * float x.Count; ExternalArea = m.ExternalArea * float x.Count }

    [<CLIMutable>]
    /// <summary>
    /// Represents reducer data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Reducer =
        { Tag: string; Di1: float; Do1: float; Di2: float; Do2: float; Length: float; Material: MaterialRef }
        member x.Metrics =
            let avgDi = 0.5 * (x.Di1 + x.Di2)
            let avgDo = 0.5 * (x.Do1 + x.Do2)
            Geometry.cylinderShell x.Material avgDi avgDo x.Length

    /// <summary>
    /// Represents pipesegment data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type PipeSegment =
        | Straight of Pipe
        | Bend of Elbow
        | Transition of Reducer

    [<CLIMutable>]
    /// <summary>
    /// Represents piperouting data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type PipeRouting =
        { Tag: string; Service: string; Segments: PipeSegment list; Notes: string }
        member x.DevelopedLength =
            x.Segments
            |> List.sumBy (function Straight p -> p.Length | Bend e -> e.ArcLength * float e.Count | Transition r -> r.Length)
        member x.Metrics =
            x.Segments
            |> Seq.map (function Straight p -> p.Metrics | Bend e -> e.Metrics | Transition r -> r.Metrics)
            |> Geometry.combine
