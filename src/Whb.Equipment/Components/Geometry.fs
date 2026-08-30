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
    /// Primitive and composite shapes used to derive component metrics.
    /// </summary>
    type Shape =
        | CylinderShell of innerDiameter: float * outerDiameter: float * length: float
        | SolidCylinder of diameter: float * length: float
        | PipeElbow of innerDiameter: float * outerDiameter: float * angleDeg: float * centerlineRadiusOverDiameter: float
        | ConicalReducer of innerDiameterIn: float * outerDiameterIn: float * innerDiameterOut: float * outerDiameterOut: float * length: float
        | DishedHead of innerDiameter: float * thickness: float * crownDepth: float
        | RectangularShell of width: float * height: float * length: float * thickness: float
        | PorousPad of area: float * thickness: float
        | PerforatedDisc of diameter: float * thickness: float * holeDiameter: float * holeCount: int
        | Composite of Shape list
        | Repeated of count: int * shape: Shape

    let empty =
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

    let private scale factor metrics =
        { ComponentVolume = metrics.ComponentVolume * factor
          InternalFluidVolume = metrics.InternalFluidVolume * factor
          InternalArea = metrics.InternalArea * factor
          ExternalArea = metrics.ExternalArea * factor }

    let rec evaluate shape =
        match shape with
        | CylinderShell (innerDiameter, outerDiameter, length) ->
            let di = max 0.0 innerDiameter
            let do_ = max di outerDiameter
            let length = max 0.0 length
            let inner = circleArea di * length
            let outer = circleArea do_ * length
            { ComponentVolume = max 0.0 (outer - inner)
              InternalFluidVolume = inner
              InternalArea = lateralArea di length
              ExternalArea = lateralArea do_ length }
        | SolidCylinder (diameter, length) ->
            let diameter = max 0.0 diameter
            let length = max 0.0 length
            { ComponentVolume = circleArea diameter * length
              InternalFluidVolume = 0.0
              InternalArea = 0.0
              ExternalArea = lateralArea diameter length }
        | PipeElbow (innerDiameter, outerDiameter, angleDeg, centerlineRadiusOverDiameter) ->
            let arcLength = Math.PI * max 0.0 angleDeg / 180.0 * max 0.0 centerlineRadiusOverDiameter * max 0.0 innerDiameter
            evaluate (CylinderShell (innerDiameter, outerDiameter, arcLength))
        | ConicalReducer (innerDiameterIn, outerDiameterIn, innerDiameterOut, outerDiameterOut, length) ->
            let di1 = max 0.0 innerDiameterIn
            let di2 = max 0.0 innerDiameterOut
            let do1 = max di1 outerDiameterIn
            let do2 = max di2 outerDiameterOut
            let length = max 0.0 length
            let inner = frustumVolume di1 di2 length
            let outer = frustumVolume do1 do2 length
            { ComponentVolume = max 0.0 (outer - inner)
              InternalFluidVolume = inner
              InternalArea = frustumLateralArea di1 di2 length
              ExternalArea = frustumLateralArea do1 do2 length }
        | DishedHead (innerDiameter, thickness, crownDepth) ->
            let di = max 0.0 innerDiameter
            let thickness = max 0.0 thickness
            let crownDepth = max 0.0 crownDepth
            let meanDiameter = di + thickness
            let area = Math.PI * meanDiameter * max crownDepth (0.25 * meanDiameter)
            let metalVolume = area * thickness
            { ComponentVolume = metalVolume
              InternalFluidVolume = Math.PI * di * di / 4.0 * crownDepth / 3.0
              InternalArea = area
              ExternalArea = area }
        | RectangularShell (width, height, length, thickness) ->
            let width = max 0.0 width
            let height = max 0.0 height
            let length = max 0.0 length
            let thickness = max 0.0 thickness
            let outer = width * height * length
            let inner =
                max 0.0 (width - 2.0 * thickness)
                * max 0.0 (height - 2.0 * thickness)
                * length
            { ComponentVolume = max 0.0 (outer - inner)
              InternalFluidVolume = inner
              InternalArea = 2.0 * ((max 0.0 (width - 2.0 * thickness)) * length + (max 0.0 (height - 2.0 * thickness)) * length)
              ExternalArea = 2.0 * (width * length + height * length) }
        | PorousPad (area, thickness) ->
            let area = max 0.0 area
            let thickness = max 0.0 thickness
            { ComponentVolume = area * thickness
              InternalFluidVolume = 0.0
              InternalArea = 0.0
              ExternalArea = area }
        | PerforatedDisc (diameter, thickness, holeDiameter, holeCount) ->
            let baseDisc = evaluate (SolidCylinder (diameter, thickness))
            let holes =
                circleArea (max 0.0 holeDiameter) * max 0.0 thickness * float (max 0 holeCount)
            { baseDisc with ComponentVolume = max 0.0 (baseDisc.ComponentVolume - holes) }
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
        | CylinderShell (_, _, length)
        | SolidCylinder (_, length)
        | ConicalReducer (_, _, _, _, length)
        | RectangularShell (_, _, length, _) -> max 0.0 length
        | PipeElbow (innerDiameter, _, angleDeg, centerlineRadiusOverDiameter) ->
            Math.PI * max 0.0 angleDeg / 180.0 * max 0.0 centerlineRadiusOverDiameter * max 0.0 innerDiameter
        | DishedHead _
        | PorousPad _
        | PerforatedDisc _ -> 0.0
        | Composite items -> items |> List.sumBy referenceLength
        | Repeated (count, item) -> float (max 0 count) * referenceLength item
