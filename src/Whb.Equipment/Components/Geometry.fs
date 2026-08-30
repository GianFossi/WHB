namespace Whb.Equipment

open System

module Geometry =

    /// <summary>
    /// Raw geometry output before material and fluid densities are applied.
    /// </summary>
    [<CLIMutable>]
    type ShapeMetrics =
        { ComponentVolume: float
          InternalFluidVolume: float
          InternalArea: float
          ExternalArea: float }

    /// <summary>
    /// Cylindrical shell defined by internal diameter and wall thickness.
    /// </summary>
    [<CLIMutable>]
    type CylinderGeometry =
        { InnerDiameter: float
          WallThickness: float
          Length: float }

    /// <summary>
    /// Pipe-like shell defined by outer diameter and wall thickness.
    /// </summary>
    [<CLIMutable>]
    type PipeGeometry =
        { OuterDiameter: float
          WallThickness: float
          Length: float }

    /// <summary>
    /// Conical transition defined by inlet/outlet inner diameters and local wall thickness.
    /// </summary>
    [<CLIMutable>]
    type TransitionConeGeometry =
        { InnerDiameterIn: float
          InnerDiameterOut: float
          WallThicknessIn: float
          WallThicknessOut: float
          Length: float }

    /// <summary>
    /// Head profile families supported by the physical equipment model.
    /// </summary>
    type DishedHeadProfile =
        | Hemispherical of capDepth: float option
        | Elliptical of crownDepth: float
        | Torispherical of crownRadius: float * knuckleRadius: float

    /// <summary>
    /// Dished head with optional cylindrical skirt.
    /// </summary>
    [<CLIMutable>]
    type DishedHeadGeometry =
        { InnerDiameter: float
          WallThickness: float
          Profile: DishedHeadProfile
          CylindricalSkirtLength: float }

    /// <summary>
    /// Mechanical variants supported for a tubesheet.
    /// </summary>
    type TubesheetProfile =
        | Flat of thickness: float
        | FlatWithExternalReinforcement of thickness: float * reinforcementOuterDiameter: float * reinforcementThickness: float
        | WithKnucklesAndFlares of thickness: float * knuckleRadius: float * flareLength: float * flareWallThickness: float option

    /// <summary>
    /// Perforated tubesheet geometry.
    /// </summary>
    [<CLIMutable>]
    type TubesheetGeometry =
        { Diameter: float
          HoleDiameter: float
          HoleCount: int
          Profile: TubesheetProfile }

    /// <summary>
    /// Flat baffle plate with an optional cut fraction for segmental openings.
    /// </summary>
    [<CLIMutable>]
    type BaffleGeometry =
        { Diameter: float
          Thickness: float
          CutFraction: float }

    /// <summary>
    /// Cylindrical liner controlled by internal diameter and wall thickness.
    /// </summary>
    [<CLIMutable>]
    type CylindricalLinerGeometry =
        { InnerDiameter: float
          WallThickness: float
          Length: float }

    /// <summary>
    /// Flat impingement plate.
    /// </summary>
    [<CLIMutable>]
    type ImpingementPlateGeometry =
        { Width: float
          Height: float
          Thickness: float }

    /// <summary>
    /// Nozzle neck defined by bore and wall thickness.
    /// </summary>
    [<CLIMutable>]
    type NozzleGeometry =
        { InnerDiameter: float
          WallThickness: float
          Projection: float }

    /// <summary>
    /// Piping elbow. `CoverageFraction = 1.0` means a full elbow, smaller values represent cut pieces.
    /// </summary>
    [<CLIMutable>]
    type PipeElbowGeometry =
        { OuterDiameter: float
          WallThickness: float
          AngleDeg: float
          CenterlineRadiusOverDiameter: float
          CoverageFraction: float }

    /// <summary>
    /// Piping conical reducer defined by outer diameters and wall thickness at both ends.
    /// </summary>
    [<CLIMutable>]
    type ConicalReducerGeometry =
        { OuterDiameterIn: float
          OuterDiameterOut: float
          WallThicknessIn: float
          WallThicknessOut: float
          Length: float }

    /// <summary>
    /// Supporting rectangular shell shape kept for existing equipment details such as conveyor boxes.
    /// </summary>
    [<CLIMutable>]
    type RectangularShellGeometry =
        { Width: float
          Height: float
          Length: float
          Thickness: float }

    /// <summary>
    /// Supporting porous insert shape kept for demister-style internals.
    /// </summary>
    [<CLIMutable>]
    type PorousPadGeometry =
        { Area: float
          Thickness: float }

    /// <summary>
    /// Primitive and composite shapes used to derive component metrics.
    /// </summary>
    type Shape =
        | Cylinder of CylinderGeometry
        | Pipe of PipeGeometry
        | TransitionCone of TransitionConeGeometry
        | DishedHead of DishedHeadGeometry
        | Tubesheet of TubesheetGeometry
        | Baffle of BaffleGeometry
        | CylindricalLiner of CylindricalLinerGeometry
        | ImpingementPlate of ImpingementPlateGeometry
        | Nozzle of NozzleGeometry
        | PipeElbow of PipeElbowGeometry
        | ConicalReducer of ConicalReducerGeometry
        | RectangularShell of RectangularShellGeometry
        | PorousPad of PorousPadGeometry
        | Composite of Shape list
        | Repeated of count: int * shape: Shape

    [<RequireQualifiedAccess>]
    module CylinderOps =

        let outerDiameter (x: CylinderGeometry) =
            max 0.0 x.InnerDiameter + 2.0 * max 0.0 x.WallThickness

    [<RequireQualifiedAccess>]
    module PipeOps =

        let innerDiameter (x: PipeGeometry) =
            max 0.0 (max 0.0 x.OuterDiameter - 2.0 * max 0.0 x.WallThickness)

    [<RequireQualifiedAccess>]
    module CylindricalLinerOps =

        let outerDiameter (x: CylindricalLinerGeometry) =
            max 0.0 x.InnerDiameter + 2.0 * max 0.0 x.WallThickness

    [<RequireQualifiedAccess>]
    module NozzleOps =

        let outerDiameter (x: NozzleGeometry) =
            max 0.0 x.InnerDiameter + 2.0 * max 0.0 x.WallThickness

    [<RequireQualifiedAccess>]
    module PipeElbowOps =

        let innerDiameter (x: PipeElbowGeometry) =
            max 0.0 (max 0.0 x.OuterDiameter - 2.0 * max 0.0 x.WallThickness)

    [<RequireQualifiedAccess>]
    module ConicalReducerOps =

        let innerDiameterIn (x: ConicalReducerGeometry) =
            max 0.0 (max 0.0 x.OuterDiameterIn - 2.0 * max 0.0 x.WallThicknessIn)

        let innerDiameterOut (x: ConicalReducerGeometry) =
            max 0.0 (max 0.0 x.OuterDiameterOut - 2.0 * max 0.0 x.WallThicknessOut)

    let empty : ShapeMetrics =
        { ComponentVolume = 0.0
          InternalFluidVolume = 0.0
          InternalArea = 0.0
          ExternalArea = 0.0 }

    let private circleArea d = Math.PI * d * d / 4.0
    let private lateralArea d l = Math.PI * d * l

    let private frustumVolume d1 d2 length =
        Math.PI * length * (d1 * d1 + d1 * d2 + d2 * d2) / 12.0

    let private frustumLateralArea d1 d2 length =
        let r1 = d1 / 2.0
        let r2 = d2 / 2.0
        let slant = sqrt ((r2 - r1) * (r2 - r1) + length * length)
        Math.PI * (r1 + r2) * slant

    let private scale factor (metrics: ShapeMetrics) : ShapeMetrics =
        { ComponentVolume = metrics.ComponentVolume * factor
          InternalFluidVolume = metrics.InternalFluidVolume * factor
          InternalArea = metrics.InternalArea * factor
          ExternalArea = metrics.ExternalArea * factor }

    let private clamp01 value =
        min 1.0 (max 0.0 value)

    let private fromCylinder innerDiameter outerDiameter length : ShapeMetrics =
        let di = max 0.0 innerDiameter
        let do_ = max di outerDiameter
        let length = max 0.0 length
        let inner = circleArea di * length
        let outer = circleArea do_ * length
        { ComponentVolume = max 0.0 (outer - inner)
          InternalFluidVolume = inner
          InternalArea = lateralArea di length
          ExternalArea = lateralArea do_ length }

    let private cylinderMetrics (shape: CylinderGeometry) =
        fromCylinder shape.InnerDiameter (CylinderOps.outerDiameter shape) shape.Length

    let private pipeMetrics (shape: PipeGeometry) =
        fromCylinder (PipeOps.innerDiameter shape) shape.OuterDiameter shape.Length

    let private transitionConeMetrics (shape: TransitionConeGeometry) =
        let di1 = max 0.0 shape.InnerDiameterIn
        let di2 = max 0.0 shape.InnerDiameterOut
        let do1 = max di1 (di1 + 2.0 * max 0.0 shape.WallThicknessIn)
        let do2 = max di2 (di2 + 2.0 * max 0.0 shape.WallThicknessOut)
        let length = max 0.0 shape.Length
        let inner = frustumVolume di1 di2 length
        let outer = frustumVolume do1 do2 length
        { ComponentVolume = max 0.0 (outer - inner)
          InternalFluidVolume = inner
          InternalArea = frustumLateralArea di1 di2 length
          ExternalArea = frustumLateralArea do1 do2 length }

    let private equivalentHeadMetrics innerDiameter wallThickness effectiveDepth : ShapeMetrics =
        let di = max 0.0 innerDiameter
        let thickness = max 0.0 wallThickness
        let depth = max 0.0 effectiveDepth
        let meanDiameter = di + thickness
        let area = Math.PI * meanDiameter * max depth (0.25 * meanDiameter)
        { ComponentVolume = area * thickness
          InternalFluidVolume = Math.PI * di * di / 4.0 * depth / 3.0
          InternalArea = area
          ExternalArea = area }

    let private sphericalCapMetrics innerDiameter wallThickness capDepth : ShapeMetrics =
        let di = max 0.0 innerDiameter
        let thickness = max 0.0 wallThickness
        let radius = di / 2.0
        let depth = min radius (max 0.0 capDepth)
        let meanRadius = radius + thickness / 2.0
        let meanDepth = min meanRadius (depth + thickness / 2.0)
        let shellArea = 2.0 * Math.PI * meanRadius * meanDepth
        { ComponentVolume = shellArea * thickness
          InternalFluidVolume = Math.PI * depth * depth * (3.0 * radius - depth) / 3.0
          InternalArea = 2.0 * Math.PI * radius * depth
          ExternalArea = 2.0 * Math.PI * (radius + thickness) * min (radius + thickness) (depth + thickness) }

    let private torisphericalDepth innerDiameter crownRadius knuckleRadius =
        let di = max 0.0 innerDiameter
        let crownRadius = max 0.0 crownRadius
        let knuckleRadius = max 0.0 knuckleRadius
        let span = max 0.0 (di / 2.0 - knuckleRadius)
        let crownRise =
            if crownRadius > 0.0 && span < crownRadius then
                crownRadius - sqrt (crownRadius * crownRadius - span * span)
            else
                0.0

        max crownRise knuckleRadius

    let private dishedHeadMetrics (shape: DishedHeadGeometry) =
        let headOnly =
            match shape.Profile with
            | Hemispherical capDepth ->
                let depth = defaultArg capDepth (shape.InnerDiameter / 2.0)
                sphericalCapMetrics shape.InnerDiameter shape.WallThickness depth
            | Elliptical crownDepth ->
                equivalentHeadMetrics shape.InnerDiameter shape.WallThickness crownDepth
            | Torispherical (crownRadius, knuckleRadius) ->
                equivalentHeadMetrics
                    shape.InnerDiameter
                    shape.WallThickness
                    (torisphericalDepth shape.InnerDiameter crownRadius knuckleRadius)

        if shape.CylindricalSkirtLength > 0.0 then
            let skirt =
                cylinderMetrics
                    { InnerDiameter = shape.InnerDiameter
                      WallThickness = shape.WallThickness
                      Length = shape.CylindricalSkirtLength }

            { ComponentVolume = headOnly.ComponentVolume + skirt.ComponentVolume
              InternalFluidVolume = headOnly.InternalFluidVolume + skirt.InternalFluidVolume
              InternalArea = headOnly.InternalArea + skirt.InternalArea
              ExternalArea = headOnly.ExternalArea + skirt.ExternalArea }
        else
            headOnly

    let private tubesheetBaseMetrics diameter thickness holeDiameter holeCount : ShapeMetrics =
        let disc = circleArea (max 0.0 diameter) * max 0.0 thickness
        let holes = circleArea (max 0.0 holeDiameter) * max 0.0 thickness * float (max 0 holeCount)
        { ComponentVolume = max 0.0 (disc - holes)
          InternalFluidVolume = 0.0
          InternalArea = 0.0
          ExternalArea = lateralArea (max 0.0 diameter) (max 0.0 thickness) }

    let private tubesheetMetrics (shape: TubesheetGeometry) =
        match shape.Profile with
        | Flat thickness ->
            tubesheetBaseMetrics shape.Diameter thickness shape.HoleDiameter shape.HoleCount
        | FlatWithExternalReinforcement (thickness, reinforcementOuterDiameter, reinforcementThickness) ->
            let baseDisc = tubesheetBaseMetrics shape.Diameter thickness shape.HoleDiameter shape.HoleCount
            let reinforcement =
                max 0.0 (circleArea (max shape.Diameter reinforcementOuterDiameter) - circleArea (max 0.0 shape.Diameter))
                * max 0.0 reinforcementThickness

            { ComponentVolume = baseDisc.ComponentVolume + reinforcement
              InternalFluidVolume = baseDisc.InternalFluidVolume
              InternalArea = baseDisc.InternalArea
              ExternalArea = baseDisc.ExternalArea + lateralArea (max shape.Diameter reinforcementOuterDiameter) (max 0.0 reinforcementThickness) }
        | WithKnucklesAndFlares (thickness, knuckleRadius, flareLength, flareWallThickness) ->
            let baseDisc = tubesheetBaseMetrics shape.Diameter thickness shape.HoleDiameter shape.HoleCount
            let flareWallThickness = defaultArg flareWallThickness thickness
            let flare =
                cylinderMetrics
                    { InnerDiameter = shape.Diameter
                      WallThickness = flareWallThickness
                      Length = flareLength }

            let knuckleArea =
                2.0
                * Math.PI
                * max 0.0 (shape.Diameter / 2.0 + knuckleRadius / 2.0)
                * max 0.0 knuckleRadius

            { ComponentVolume = baseDisc.ComponentVolume + flare.ComponentVolume + knuckleArea * max 0.0 thickness
              InternalFluidVolume = flare.InternalFluidVolume
              InternalArea = flare.InternalArea
              ExternalArea = baseDisc.ExternalArea + flare.ExternalArea + knuckleArea }

    let private baffleMetrics (shape: BaffleGeometry) =
        let cutFraction = clamp01 shape.CutFraction
        let full = circleArea (max 0.0 shape.Diameter) * max 0.0 shape.Thickness
        { ComponentVolume = full * (1.0 - cutFraction)
          InternalFluidVolume = 0.0
          InternalArea = 0.0
          ExternalArea = lateralArea (max 0.0 shape.Diameter) (max 0.0 shape.Thickness) }

    let private cylindricalLinerMetrics (shape: CylindricalLinerGeometry) =
        fromCylinder shape.InnerDiameter (CylindricalLinerOps.outerDiameter shape) shape.Length

    let private impingementPlateMetrics (shape: ImpingementPlateGeometry) =
        let width = max 0.0 shape.Width
        let height = max 0.0 shape.Height
        let thickness = max 0.0 shape.Thickness
        { ComponentVolume = width * height * thickness
          InternalFluidVolume = 0.0
          InternalArea = 0.0
          ExternalArea = width * height }

    let private nozzleMetrics (shape: NozzleGeometry) =
        fromCylinder shape.InnerDiameter (NozzleOps.outerDiameter shape) shape.Projection

    let private elbowMetrics (shape: PipeElbowGeometry) =
        let coverage = clamp01 shape.CoverageFraction
        let arcLength =
            Math.PI
            * max 0.0 shape.AngleDeg
            / 180.0
            * max 0.0 shape.CenterlineRadiusOverDiameter
            * PipeElbowOps.innerDiameter shape
            * coverage

        fromCylinder (PipeElbowOps.innerDiameter shape) shape.OuterDiameter arcLength

    let private reducerMetrics (shape: ConicalReducerGeometry) =
        let di1 = ConicalReducerOps.innerDiameterIn shape
        let di2 = ConicalReducerOps.innerDiameterOut shape
        let do1 = max di1 (max 0.0 shape.OuterDiameterIn)
        let do2 = max di2 (max 0.0 shape.OuterDiameterOut)
        let length = max 0.0 shape.Length
        let inner = frustumVolume di1 di2 length
        let outer = frustumVolume do1 do2 length
        { ComponentVolume = max 0.0 (outer - inner)
          InternalFluidVolume = inner
          InternalArea = frustumLateralArea di1 di2 length
          ExternalArea = frustumLateralArea do1 do2 length }

    let private rectangularShellMetrics (shape: RectangularShellGeometry) =
        let width = max 0.0 shape.Width
        let height = max 0.0 shape.Height
        let length = max 0.0 shape.Length
        let thickness = max 0.0 shape.Thickness
        let outer = width * height * length
        let inner = max 0.0 (width - 2.0 * thickness) * max 0.0 (height - 2.0 * thickness) * length
        { ComponentVolume = max 0.0 (outer - inner)
          InternalFluidVolume = inner
          InternalArea = 2.0 * (max 0.0 (width - 2.0 * thickness) * length + max 0.0 (height - 2.0 * thickness) * length)
          ExternalArea = 2.0 * (width * length + height * length) }

    let private porousPadMetrics (shape: PorousPadGeometry) =
        let area = max 0.0 shape.Area
        let thickness = max 0.0 shape.Thickness
        { ComponentVolume = area * thickness
          InternalFluidVolume = 0.0
          InternalArea = 0.0
          ExternalArea = area }

    let rec evaluate shape =
        match shape with
        | Cylinder x -> cylinderMetrics x
        | Pipe x -> pipeMetrics x
        | TransitionCone x -> transitionConeMetrics x
        | DishedHead x -> dishedHeadMetrics x
        | Tubesheet x -> tubesheetMetrics x
        | Baffle x -> baffleMetrics x
        | CylindricalLiner x -> cylindricalLinerMetrics x
        | ImpingementPlate x -> impingementPlateMetrics x
        | Nozzle x -> nozzleMetrics x
        | PipeElbow x -> elbowMetrics x
        | ConicalReducer x -> reducerMetrics x
        | RectangularShell x -> rectangularShellMetrics x
        | PorousPad x -> porousPadMetrics x
        | Composite items ->
            items
            |> List.map evaluate
            |> List.fold
                (fun acc item ->
                    { ComponentVolume = acc.ComponentVolume + item.ComponentVolume
                      InternalFluidVolume = acc.InternalFluidVolume + item.InternalFluidVolume
                      InternalArea = acc.InternalArea + item.InternalArea
                      ExternalArea = acc.ExternalArea + item.ExternalArea })
                empty
        | Repeated (count, item) ->
            let factor = float (max 0 count)
            evaluate item |> scale factor

    let rec referenceLength shape =
        match shape with
        | Cylinder x -> max 0.0 x.Length
        | Pipe x -> max 0.0 x.Length
        | TransitionCone x -> max 0.0 x.Length
        | DishedHead x -> max 0.0 x.CylindricalSkirtLength
        | Tubesheet _
        | Baffle _
        | ImpingementPlate _
        | Nozzle _
        | PorousPad _ -> 0.0
        | CylindricalLiner x -> max 0.0 x.Length
        | PipeElbow x ->
            Math.PI
            * max 0.0 x.AngleDeg
            / 180.0
            * max 0.0 x.CenterlineRadiusOverDiameter
            * PipeElbowOps.innerDiameter x
            * clamp01 x.CoverageFraction
        | ConicalReducer x -> max 0.0 x.Length
        | RectangularShell x -> max 0.0 x.Length
        | Composite items -> items |> List.sumBy referenceLength
        | Repeated (count, item) -> float (max 0 count) * referenceLength item
