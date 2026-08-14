namespace Whb.Core.Components

open System
open Whb.Core.Components.Geometry
module WhbComponents =

    [<CLIMutable>]
    type Tube =
        { Tag: string; Di: float; Do: float; Length: float; Count: int; Material: MaterialRef }
        member x.Metrics = Geometry.cylinderShell x.Material x.Di x.Do x.Length |> fun m -> { m with Weight = m.Weight * float x.Count; MetalVolume = m.MetalVolume * float x.Count; InternalVolume = m.InternalVolume * float x.Count; ExternalVolume = m.ExternalVolume * float x.Count; InternalArea = m.InternalArea * float x.Count; ExternalArea = m.ExternalArea * float x.Count }

    [<CLIMutable>]
    type TubeBundle =
        { Tag: string
          Tubes: Tube
          Pitch: float
          Layout: string
          Otl: float
          Itl: float
          BaffleOd: float
          BaffleThickness: float
          BaffleCount: int
          BaffleMaterial: MaterialRef }
        member x.Metrics =
            let baffle = Geometry.solidCylinder x.BaffleMaterial x.BaffleOd x.BaffleThickness
            Geometry.combine [ x.Tubes.Metrics; { baffle with Weight = baffle.Weight * float x.BaffleCount; MetalVolume = baffle.MetalVolume * float x.BaffleCount } ]

    [<CLIMutable>]
    type PipeBypass =
        { Tag: string
          LinerDi: float; LinerDo: float
          InsulationDo: float
          ContainmentDo: float
          Length: float
          LinerMaterial: MaterialRef
          ContainmentMaterial: MaterialRef }
        member x.Metrics =
            Geometry.combine
                [ Geometry.cylinderShell x.LinerMaterial x.LinerDi x.LinerDo x.Length
                  Geometry.cylinderShell x.ContainmentMaterial x.InsulationDo x.ContainmentDo x.Length ]

    [<CLIMutable>]
    type Tubesheet =
        { Tag: string; Diameter: float; Thickness: float; TubeHoleDiameter: float; TubeHoleCount: int; Material: MaterialRef }
        member x.Metrics =
            let gross = Geometry.solidCylinder x.Material x.Diameter x.Thickness
            let holes = Math.PI * x.TubeHoleDiameter * x.TubeHoleDiameter / 4.0 * x.Thickness * float x.TubeHoleCount
            { gross with MetalVolume = max 0.0 (gross.MetalVolume - holes); Weight = max 0.0 (gross.MetalVolume - holes) * x.Material.Density }

    [<CLIMutable>]
    type ShellBarrel =
        { Tag: string; Di: float; Do: float; Length: float; Material: MaterialRef }
        member x.Metrics = Geometry.cylinderShell x.Material x.Di x.Do x.Length

    [<CLIMutable>]
    type Nozzle =
        { Tag: string; Service: string; Di: float; Do: float; Projection: float; Count: int; Material: MaterialRef }
        member x.Metrics =
            let m = Geometry.cylinderShell x.Material x.Di x.Do x.Projection
            { m with Weight = m.Weight * float x.Count; MetalVolume = m.MetalVolume * float x.Count; InternalVolume = m.InternalVolume * float x.Count; ExternalVolume = m.ExternalVolume * float x.Count; InternalArea = m.InternalArea * float x.Count; ExternalArea = m.ExternalArea * float x.Count }

    [<CLIMutable>]
    type BypassValve =
        { Tag: string; ValveType: string; Bore: float; FaceToFace: float; BodyDo: float; Material: MaterialRef }
        member x.Metrics = Geometry.cylinderShell x.Material x.Bore x.BodyDo x.FaceToFace

    [<CLIMutable>]
    type Whb =
        { Tag: string
          TubeBundle: TubeBundle
          Shell: ShellBarrel
          Tubesheets: Tubesheet list
          Nozzles: Nozzle list
          Bypass: PipeBypass option
          BypassValve: BypassValve option }
        member x.Metrics =
            Geometry.combine
                [ yield x.TubeBundle.Metrics
                  yield x.Shell.Metrics
                  yield! x.Tubesheets |> Seq.map (fun p -> p.Metrics)
                  yield! x.Nozzles |> Seq.map (fun n -> n.Metrics)
                  match x.Bypass with Some b -> yield b.Metrics | None -> ()
                  match x.BypassValve with Some v -> yield v.Metrics | None -> () ]


