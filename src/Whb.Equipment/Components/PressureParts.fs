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
                     (CylinderGeometry(
                        InnerDiameter = innerDiameter,
                        WallThickness = wallThickness innerDiameter outerDiameter,
                        Length = length
                     ))))
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
                     (BaffleGeometry(
                        Diameter = diameter,
                        Thickness = thickness,
                        CutFraction = 0.0
                     ))))
            material
            None

    let shellBarrel id name bom innerDiameter outerDiameter length material internalFluid =
        create
            id
            name
            bom
            (Geometry.Cylinder
                (CylinderGeometry(
                    InnerDiameter = innerDiameter,
                    WallThickness = wallThickness innerDiameter outerDiameter,
                    Length = length
                )))
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
                     (TubesheetGeometry(
                        Diameter = diameter,
                        HoleDiameter = holeDiameter,
                        HoleCount = holeCount,
                        Profile = Flat thickness
                     ))))
            material
            None

    let nozzle id name bom service innerDiameter outerDiameter projection count material internalFluid =
        let geometry =
            Geometry.Repeated
                (count,
                 Geometry.Nozzle
                     (NozzleGeometry(
                        InnerDiameter = innerDiameter,
                        WallThickness = wallThickness innerDiameter outerDiameter,
                        Projection = projection
                     )))

        create id $"{name} ({service})" bom geometry material internalFluid

    let valveBody id name bom bore faceToFace bodyOuterDiameter material internalFluid =
        create
            id
            name
            bom
            (Geometry.Cylinder
                (CylinderGeometry(
                    InnerDiameter = bore,
                    WallThickness = wallThickness bore bodyOuterDiameter,
                    Length = faceToFace
                )))
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
                     (CylinderGeometry(
                        InnerDiameter = innerDiameter,
                        WallThickness = wallThickness innerDiameter outerDiameter,
                        Length = length
                     ))))
            material
            internalFluid

    let liner id name bom innerDiameter outerDiameter length material internalFluid =
        create
            id
            name
            bom
            (Geometry.CylindricalLiner
                (CylindricalLinerGeometry(
                    InnerDiameter = innerDiameter,
                    WallThickness = wallThickness innerDiameter outerDiameter,
                    Length = length
                )))
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
                     (BaffleGeometry(
                        Diameter = diameter,
                        Thickness = thickness,
                        CutFraction = 0.0
                     ))))
            material
            None

    let ellipticalHead id name bom innerDiameter thickness cylindricalSkirtLength count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.EllipticalHead
                     (EllipticalHeadGeometry(
                        InnerDiameter = innerDiameter,
                        WallThickness = thickness,
                        CylindricalSkirtLength = cylindricalSkirtLength
                     ))))
            material
            internalFluid

    let dishedHead id name bom innerDiameter thickness _crownDepth count material internalFluid =
        ellipticalHead id name bom innerDiameter thickness None count material internalFluid

    let expansionBox id name bom width height length thickness count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated
                (count,
                 Geometry.RectangularShell
                     (RectangularShellGeometry(
                        Width = width,
                        Height = height,
                        Length = length,
                        Thickness = thickness
                     ))))
            material
            internalFluid

    let demister id name bom area thickness bulkDensity material =
        let porousMaterial : Materials.MaterialProperties = { material with Density = bulkDensity }
        create
            id
            name
            bom
            (Geometry.PorousPad
                (PorousPadGeometry(
                    Area = area,
                    Thickness = thickness
                )))
            porousMaterial
            None
