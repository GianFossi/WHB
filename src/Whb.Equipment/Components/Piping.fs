namespace Whb.Equipment

/// <summary>
/// Identifies a nozzle endpoint used by an interconnecting pipeline.
/// </summary>
[<CLIMutable>]
type NozzleEndpoint =
    { EquipmentId: string
      NozzleId: string }

type PipelineSegment =
    | StraightPipeSpool of Component
    | Elbow of Component
    | Reducer of Component

[<RequireQualifiedAccess>]
module PipelineSegment =

    let unwrap segment =
        match segment with
        | StraightPipeSpool part
        | Elbow part
        | Reducer part -> part

/// <summary>
/// Physical routing between two equipment nozzles. Hydraulic calculations stay in `Whb.Core`.
/// </summary>
[<CLIMutable>]
type PipelineEquipment =
    { Id: string
      Name: string
      Bom: Bom.BomItem
      Service: string
      FromNozzle: NozzleEndpoint
      ToNozzle: NozzleEndpoint
      Segments: PipelineSegment list
      Notes: string }
    member x.Components = x.Segments |> List.map PipelineSegment.unwrap
    member x.Metrics = x.Components |> Component.totalMetrics
    member x.DevelopedLength =
        x.Components
        |> Seq.collect Component.descendantsAndSelf
        |> Seq.sumBy (fun part ->
            match part.Geometry with
            | Some shape -> Geometry.referenceLength shape
            | None -> 0.0)

[<RequireQualifiedAccess>]
module Piping =

    let straightPipeSpool id name bom innerDiameter outerDiameter length material internalFluid =
        PressureParts.shellBarrel id name bom innerDiameter outerDiameter length material internalFluid
        |> StraightPipeSpool

    let elbow id name bom innerDiameter outerDiameter angleDeg centerlineRadiusOverDiameter material internalFluid =
        PressureParts.create
            id
            name
            bom
            (Geometry.PipeElbow (innerDiameter, outerDiameter, angleDeg, centerlineRadiusOverDiameter))
            material
            internalFluid
        |> Elbow

    let reducer id name bom innerDiameterIn outerDiameterIn innerDiameterOut outerDiameterOut length material internalFluid =
        PressureParts.create
            id
            name
            bom
            (Geometry.ConicalReducer (innerDiameterIn, outerDiameterIn, innerDiameterOut, outerDiameterOut, length))
            material
            internalFluid
        |> Reducer
