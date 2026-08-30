namespace Whb.Equipment

/// <summary>
/// Common physical-component model shared by standalone components, piping segments and
/// assembled equipment definitions.
/// </summary>
[<CLIMutable>]
type Component =
    { Id: string
      Name: string
      Bom: Bom.BomItem
      Geometry: Geometry.Shape option
      Material: Materials.MaterialProperties option
      InternalFluid: Materials.FluidProperties option
      Components: Component list }
    member x.Metrics =
        let ownMetrics =
            match x.Geometry, x.Material with
            | Some shape, Some material ->
                let geometry = Geometry.evaluate shape
                let internalFluidDensity =
                    x.InternalFluid
                    |> Option.map (fun fluid -> max 0.0 fluid.Density)
                    |> Option.defaultValue 0.0

                let metrics : Metrics.ComponentMetrics =
                    { Weight =
                        { OfComponent = geometry.ComponentVolume * max 0.0 material.Density
                          OfInternalFluid = geometry.InternalFluidVolume * internalFluidDensity }
                      Volume =
                        { OfComponent = geometry.ComponentVolume
                          OfInternalFluid = geometry.InternalFluidVolume }
                      InternalArea = geometry.InternalArea
                      ExternalArea = geometry.ExternalArea }

                metrics
            | _ -> Metrics.empty

        Metrics.combine
            [ yield ownMetrics
              yield! x.Components |> Seq.map (fun child -> child.Metrics) ]

[<RequireQualifiedAccess>]
module Component =

    let createLeaf id name bom geometry material internalFluid =
        { Id = id
          Name = name
          Bom = bom
          Geometry = Some geometry
          Material = Some material
          InternalFluid = internalFluid
          Components = [] }

    let createAssembly id name bom components =
        { Id = id
          Name = name
          Bom = bom
          Geometry = None
          Material = None
          InternalFluid = None
          Components = components }

    let totalMetrics (components: Component seq) =
        components |> Seq.map (fun part -> part.Metrics) |> Metrics.combine

    let rec descendantsAndSelf (part: Component) =
        seq {
            yield part
            for child in part.Components do
                yield! descendantsAndSelf child
        }
