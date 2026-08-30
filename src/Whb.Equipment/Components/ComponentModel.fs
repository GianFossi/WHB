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
            match x.Geometry with
            | Some shape ->
                let geometry = Geometry.evaluate shape
                let componentDensity =
                    x.Material
                    |> Option.map (fun material -> max 0.0 material.Density)
                    |> Option.defaultValue 0.0

                let internalFluidDensity =
                    x.InternalFluid
                    |> Option.map (fun fluid -> max 0.0 fluid.Density)
                    |> Option.defaultValue 0.0

                let componentVolume =
                    if x.Material.IsSome then geometry.ComponentVolume else 0.0

                let internalFluidVolume =
                    if x.InternalFluid.IsSome then geometry.InternalFluidVolume else 0.0

                let metrics : Metrics.ComponentMetrics =
                    { Weight =
                        { OfComponent = componentVolume * componentDensity
                          OfInternalFluid = internalFluidVolume * internalFluidDensity }
                      Volume =
                        { OfComponent = componentVolume
                          OfInternalFluid = internalFluidVolume }
                      InternalArea = geometry.InternalArea
                      ExternalArea = geometry.ExternalArea }

                metrics
            | None -> Metrics.empty

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

    let createFluidRegion id name bom geometry internalFluid =
        { Id = id
          Name = name
          Bom = bom
          Geometry = Some geometry
          Material = None
          InternalFluid = Some internalFluid
          Components = [] }

    let totalMetrics (components: Component seq) =
        components |> Seq.map (fun part -> part.Metrics) |> Metrics.combine

    let rec descendantsAndSelf (part: Component) =
        seq {
            yield part
            for child in part.Components do
                yield! descendantsAndSelf child
        }
