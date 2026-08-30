namespace Whb.Equipment

module PressureParts =

    let create id name bom geometry material internalFluid =
        Component.createLeaf id name bom geometry material internalFluid

    let tubeBank id name bom innerDiameter outerDiameter length count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.CylinderShell (innerDiameter, outerDiameter, length)))
            material
            internalFluid

    let bafflePlate id name bom diameter thickness count material =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.SolidCylinder (diameter, thickness)))
            material
            None

    let shellBarrel id name bom innerDiameter outerDiameter length material internalFluid =
        create id name bom (Geometry.CylinderShell (innerDiameter, outerDiameter, length)) material internalFluid

    let tubesheet id name bom diameter thickness holeDiameter holeCount count material =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.PerforatedDisc (diameter, thickness, holeDiameter, holeCount)))
            material
            None

    let nozzle id name bom service innerDiameter outerDiameter projection count material internalFluid =
        let geometry =
            Geometry.Repeated (count, Geometry.CylinderShell (innerDiameter, outerDiameter, projection))

        create id $"{name} ({service})" bom geometry material internalFluid

    let valveBody id name bom bore faceToFace bodyOuterDiameter material internalFluid =
        create id name bom (Geometry.CylinderShell (bore, bodyOuterDiameter, faceToFace)) material internalFluid

    let ferrule id name bom innerDiameter outerDiameter length count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.CylinderShell (innerDiameter, outerDiameter, length)))
            material
            internalFluid

    let liner id name bom innerDiameter outerDiameter length material internalFluid =
        create id name bom (Geometry.CylinderShell (innerDiameter, outerDiameter, length)) material internalFluid

    let diaphragm id name bom diameter thickness count material =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.SolidCylinder (diameter, thickness)))
            material
            None

    let dishedHead id name bom innerDiameter thickness crownDepth count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.DishedHead (innerDiameter, thickness, crownDepth)))
            material
            internalFluid

    let expansionBox id name bom width height length thickness count material internalFluid =
        create
            id
            name
            bom
            (Geometry.Repeated (count, Geometry.RectangularShell (width, height, length, thickness)))
            material
            internalFluid

    let demister id name bom area thickness bulkDensity material =
        let porousMaterial : Materials.MaterialProperties = { material with Density = bulkDensity }
        create id name bom (Geometry.PorousPad (area, thickness)) porousMaterial None
