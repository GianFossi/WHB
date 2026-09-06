namespace Whb.Equipment

/// <summary>
/// Common physical-component model shared by standalone components, piping segments and
/// assembled equipment definitions.
/// </summary>
type Component() =
    /// <summary>
    /// Stable component identifier used across assemblies, reports and tree rewrites.
    /// </summary>
    member val Id = "" with get, set

    /// <summary>
    /// Human-readable component name for package listings and reports.
    /// </summary>
    member val Name = "" with get, set

    /// <summary>
    /// Bill-of-material item associated with this component.
    /// </summary>
    member val Bom: Bom.BomItem = Unchecked.defaultof<_> with get, set

    /// <summary>
    /// Physical shape used to derive volume, area and reference-length metrics.
    /// </summary>
    member val Geometry: Geometry.Shape option = None with get, set

    /// <summary>
    /// Structural material carried by the component, if the component has solid mass.
    /// </summary>
    member val Material: Materials.MaterialProperties option = None with get, set

    /// <summary>
    /// Internal retained fluid associated with the component geometry, if any.
    /// </summary>
    member val InternalFluid: Materials.FluidProperties option = None with get, set

    /// <summary>
    /// Child components nested under this component when it acts as an assembly node.
    /// </summary>
    member val Components: Component list = [] with get, set

    new
        (
            id: string,
            name: string,
            bom: Bom.BomItem,
            geometry: Geometry.Shape option,
            material: Materials.MaterialProperties option,
            internalFluid: Materials.FluidProperties option,
            components: Component list
        ) as this =
        Component() then
            this.Id <- id
            this.Name <- name
            this.Bom <- bom
            this.Geometry <- geometry
            this.Material <- material
            this.InternalFluid <- internalFluid
            this.Components <- components

    /// <summary>
    /// Aggregated derived metrics of this component and all nested child components.
    /// </summary>
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

    /// <summary>
    /// Creates a component from its full property set, including optional geometry, material,
    /// internal fluid and child components.
    /// </summary>
    let create id name bom geometry material internalFluid components =
        Component(id, name, bom, geometry, material, internalFluid, components)

    /// <summary>
    /// Creates a solid leaf component with geometry and material, plus optional retained fluid.
    /// </summary>
    let createLeaf id name bom geometry material internalFluid =
        create id name bom (Some geometry) (Some material) internalFluid []

    /// <summary>
    /// Creates an assembly node that carries child components but no direct geometry or material.
    /// </summary>
    let createAssembly id name bom components =
        create id name bom None None None components

    /// <summary>
    /// Creates a geometry-only fluid region used to represent retained-process inventory without
    /// artificial metal mass.
    /// </summary>
    let createFluidRegion id name bom geometry internalFluid =
        create id name bom (Some geometry) None (Some internalFluid) []

    /// <summary>
    /// Returns a copy of the component with the supplied child-component list.
    /// </summary>
    let withComponents components (part: Component) =
        create part.Id part.Name part.Bom part.Geometry part.Material part.InternalFluid components

    /// <summary>
    /// Returns a copy of the component with the supplied internal-fluid definition.
    /// </summary>
    let withInternalFluid internalFluid (part: Component) =
        create part.Id part.Name part.Bom part.Geometry part.Material internalFluid part.Components

    /// <summary>
    /// Combines the derived metrics of multiple top-level components into one package total.
    /// </summary>
    let totalMetrics (components: Component seq) =
        components |> Seq.map (fun part -> part.Metrics) |> Metrics.combine

    /// <summary>
    /// Enumerates a component together with all its descendants in depth-first order.
    /// </summary>
    let rec descendantsAndSelf (part: Component) =
        seq {
            yield part
            for child in part.Components do
                yield! descendantsAndSelf child
        }
