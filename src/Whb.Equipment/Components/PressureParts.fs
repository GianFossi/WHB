namespace Whb.Equipment

module PressureParts =

    let private wallThickness innerDiameter outerDiameter =
        max 0.0 (outerDiameter - innerDiameter) / 2.0

    let create id name bom geometry material internalFluid =
        Component.createLeaf id name bom geometry material internalFluid

    let tubeBank id name bom innerDiameter outerDiameter length count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.Cylinder
                     { InnerDiameter = innerDiameter
                       WallThickness = wallThickness innerDiameter outerDiameter
                       Length = length }))
            material
            internalFluid

    let bafflePlate id name bom diameter thickness count material =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.Baffle
                     { Diameter = diameter
                       Thickness = thickness
                       CutFraction = 0.0 }))
            material
            None

    let shellBarrel id name bom innerDiameter outerDiameter length material internalFluid =
        create
            id
            name
            bom
            (Geometry.Cylinder
                { InnerDiameter = innerDiameter
                  WallThickness = wallThickness innerDiameter outerDiameter
                  Length = length })
            material
            internalFluid

    let tubesheet id name bom diameter thickness holeDiameter holeCount count material =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.Tubesheet
                     { Diameter = diameter
                       HoleDiameter = holeDiameter
                       HoleCount = holeCount
                       Profile = Geometry.Flat thickness }))
            material
            None

    let nozzle id name bom service innerDiameter outerDiameter projection count material internalFluid =
        let geometry =
            Geometry.Repeated
                (count,
                 Geometry.Nozzle
                     { InnerDiameter = innerDiameter
                       WallThickness = wallThickness innerDiameter outerDiameter
                       Projection = projection })

        create id $"{name} ({service})" bom geometry material internalFluid

    let valveBody id name bom bore faceToFace bodyOuterDiameter material internalFluid =
        create
            id
            name
            bom
            (Geometry.Cylinder
                { InnerDiameter = bore
                  WallThickness = wallThickness bore bodyOuterDiameter
                  Length = faceToFace })
            material
            internalFluid

    let ferrule id name bom innerDiameter outerDiameter length count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.Cylinder
                     { InnerDiameter = innerDiameter
                       WallThickness = wallThickness innerDiameter outerDiameter
                       Length = length }))
            material
            internalFluid

    let liner id name bom innerDiameter outerDiameter length material internalFluid =
        create
            id
            name
            bom
            (Geometry.CylindricalLiner
                { InnerDiameter = innerDiameter
                  WallThickness = wallThickness innerDiameter outerDiameter
                  Length = length })
            material
            internalFluid

    let diaphragm id name bom diameter thickness count material =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.Baffle
                     { Diameter = diameter
                       Thickness = thickness
                       CutFraction = 0.0 }))
            material
            None

    let dishedHead id name bom innerDiameter thickness crownDepth count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.DishedHead
                     { InnerDiameter = innerDiameter
                       WallThickness = thickness
                       Profile = Geometry.Elliptical crownDepth
                       CylindricalSkirtLength = 0.0 }))
            material
            internalFluid

    let expansionBox id name bom width height length thickness count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.RectangularShell
                     { Width = width
                       Height = height
                       Length = length
                       Thickness = thickness }))
            material
            internalFluid

    let demister id name bom area thickness bulkDensity material =
        let porousMaterial : Materials.MaterialProperties = { material with Density = bulkDensity }
        create
            id
            name
            bom
            (Geometry.PorousPad
                { Area = area
                  Thickness = thickness })
            porousMaterial
            None
