namespace Whb.Core.Components

open System
module Geometry =

    [<CLIMutable>]
    type MaterialRef =
        { Name: string
          Density: float }

    [<CLIMutable>]
    type ComponentMetrics =
        { MetalVolume: float
          InternalVolume: float
          ExternalVolume: float
          InternalArea: float
          ExternalArea: float
          Weight: float }
    let emptyMetrics =
        { MetalVolume = 0.0
          InternalVolume = 0.0
          ExternalVolume = 0.0
          InternalArea = 0.0
          ExternalArea = 0.0
          Weight = 0.0 }
    let private circleArea d = Math.PI * d * d / 4.0
    let private cylArea d l = Math.PI * d * l
    let cylinderShell (material: MaterialRef) (di: float) (do_: float) (length: float) =
        let di = max 0.0 di
        let do_ = max di do_
        let length = max 0.0 length
        let vi = circleArea di * length
        let ve = circleArea do_ * length
        let vm = max 0.0 (ve - vi)
        { MetalVolume = vm
          InternalVolume = vi
          ExternalVolume = ve
          InternalArea = cylArea di length
          ExternalArea = cylArea do_ length
          Weight = vm * material.Density }
    let solidCylinder (material: MaterialRef) (diameter: float) (length: float) =
        let v = circleArea (max 0.0 diameter) * max 0.0 length
        { emptyMetrics with MetalVolume = v; ExternalVolume = v; ExternalArea = cylArea diameter length; Weight = v * material.Density }
    let annulusArea di do_ = max 0.0 (circleArea do_ - circleArea di)
    let combine (items: ComponentMetrics seq) =
        items
        |> Seq.fold
            (fun a b ->
                { MetalVolume = a.MetalVolume + b.MetalVolume
                  InternalVolume = a.InternalVolume + b.InternalVolume
                  ExternalVolume = a.ExternalVolume + b.ExternalVolume
                  InternalArea = a.InternalArea + b.InternalArea
                  ExternalArea = a.ExternalArea + b.ExternalArea
                  Weight = a.Weight + b.Weight })
            emptyMetrics
    let validateTubeLike tag di do_ length =
        [ if di <= 0.0 then $"{tag}: diametro interno non positivo"
          if do_ <= 0.0 then $"{tag}: diametro esterno non positivo"
          if do_ <= di then $"{tag}: diametro esterno minore o uguale all'interno"
          if length <= 0.0 then $"{tag}: lunghezza non positiva" ]


