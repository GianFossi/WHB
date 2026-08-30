namespace Whb.Core

open System
open System.Globalization
open Types
open Whb.Equipment

module EqBom = Whb.Equipment.Bom
module EqMaterials = Whb.Equipment.Materials
module EqGeometry = Whb.Equipment.Geometry
module EqPressureParts = Whb.Equipment.PressureParts
module EqAssemblies = Whb.Equipment.EquipmentAssemblies

module Package =

    type Package = EquipmentPackage

    let totalMetrics (p: Package) =
        p.Metrics

    let fromWhbCore (source: Interop.IWhbCoreEquipmentSnapshot) =
        EquipmentPackage.ofWhbCore source

    let private bom id description quantity unit : EqBom.BomItem =
        { Id = id
          Description = description
          Quantity = quantity
          Unit = unit }

    let private densityOf (material: Materials.Material) =
        let name = material.Name.ToUpperInvariant()
        if name.Contains("ALLOY") then 8050.0
        elif name.Contains("AUSTENITICO") || name.Contains("321") then 8000.0
        else 7850.0

    let private toEquipmentMaterial (material: Materials.Material) : EqMaterials.MaterialProperties =
        let tryBuiltIn =
            EqMaterials.builtInMaterials
            |> List.tryFind (fun item ->
                item.Id.Equals(material.Name, StringComparison.OrdinalIgnoreCase)
                || item.Name.Equals(material.Name, StringComparison.OrdinalIgnoreCase)
                || material.Name.Contains(item.Name, StringComparison.OrdinalIgnoreCase)
                || item.Name.Contains(material.Name, StringComparison.OrdinalIgnoreCase))

        match tryBuiltIn with
        | Some item -> item
        | None ->
            { Id = material.Name.Replace(" ", "-").Replace("/", "-").ToUpperInvariant()
              Name = material.Name
              Density = densityOf material
              Conductivity20C = material.K 20.0
              YoungModulus20C = material.E 20.0
              Yield20C = material.Sy 20.0
              Notes = material.Note }

    let private gasFluid (coreCase: DesignCase) : EqMaterials.FluidProperties =
        let gas =
            GasProps.mixReal
                coreCase.Gas.MixingRule
                coreCase.Gas.RealGas
                coreCase.Gas.Composition
                coreCase.Gas.TIn
                coreCase.Gas.PIn
                coreCase.Gas.Z

        { Name = "Process gas"
          Density = gas.Rho }

    let private saturatedLiquid (sat: Steam.SatProps) : EqMaterials.FluidProperties =
        { Name = "Saturated water"
          Density = sat.RhoL }

    let private twoPhaseFluid name density : EqMaterials.FluidProperties =
        { Name = name
          Density = max 0.0 density }

    let private inferPipeOdFromNps (nps: string) (id: float) =
        let normalized = nps.Replace("\"", "").Trim()
        let first =
            normalized.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.tryHead

        let nominalSize =
            match first with
            | Some value ->
                match Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture) with
                | true, parsed -> Some parsed
                | _ -> None
            | None -> None

        let odInches =
            match nominalSize with
            | Some 4.0 -> Some 4.5
            | Some 6.0 -> Some 6.625
            | Some 8.0 -> Some 8.625
            | Some 10.0 -> Some 10.75
            | Some 12.0 -> Some 12.75
            | Some 14.0 -> Some 14.0
            | Some 16.0 -> Some 16.0
            | Some 18.0 -> Some 18.0
            | Some 20.0 -> Some 20.0
            | Some 24.0 -> Some 24.0
            | _ -> None

        match odInches with
        | Some od -> max (id * 1.02) (od * 0.0254)
        | None -> id * 1.08

    let private defaultTubesheetThickness (coreCase: DesignCase) =
        max 0.050 (0.045 * coreCase.Tube.ShellId)

    let private defaultValveFaceToFace (bore: float) =
        max 0.300 (3.0 * bore)

    let private defaultValveBodyOuterDiameter (pipeOd: float) =
        max (pipeOd * 1.20) (pipeOd + 0.080)

    let private defaultDrumShellThickness (drum: Drum.Internals) =
        max 0.016 (0.025 * drum.ShellId)

    let private defaultDrumLevels (drum: Drum.Internals) =
        let band = 0.10 * drum.ShellId
        { LowLow = max 0.0 (drum.NormalLevel - 2.0 * band)
          Low = max 0.0 (drum.NormalLevel - band)
          Normal = drum.NormalLevel
          High = min drum.ShellId (drum.NormalLevel + band)
          HighHigh = min drum.ShellId (drum.NormalLevel + 2.0 * band) }

    let private mapComponentTree (mapper: Component -> Component) =
        let rec loop (part: Component) : Component =
            let updated =
                { part with
                    Components = part.Components |> List.map loop }

            mapper updated

        loop

    let private setComponentFluid componentId fluid =
        mapComponentTree (fun part ->
            if part.Id = componentId then
                { part with InternalFluid = fluid }
            else
                part)

    let private setComponentFluidWhere predicate fluid =
        mapComponentTree (fun part ->
            if predicate part then
                { part with InternalFluid = fluid }
            else
                part)

    let private appendChildren componentId extraChildren =
        mapComponentTree (fun part ->
            if part.Id = componentId then
                { part with Components = part.Components @ extraChildren }
            else
                part)

    let private equivalentFluidRegion
        (id: string)
        (name: string)
        (description: string)
        (diameter: float)
        (volume: float)
        (fluid: EqMaterials.FluidProperties)
        =
        let area = Math.PI * diameter * diameter / 4.0
        let safeLength = if area > 0.0 then volume / area else 0.0

        Component.createFluidRegion
            id
            name
            (bom (sprintf "BOM-%s" id) description 1.0 "calc")
            (EqGeometry.Cylinder
                { InnerDiameter = diameter
                  WallThickness = 0.0
                  Length = safeLength })
            fluid

    let private averageOrElse fallback values =
        match values with
        | [] -> fallback
        | _ -> values |> List.average

    let private tubeSideGasFluidFromResult (design: DesignResult) =
        let fallback = gasFluid design.Case
        let densities =
            design.Cells
            |> List.map (fun cell ->
                GasProps.mixReal
                    design.Case.Gas.MixingRule
                    design.Case.Gas.RealGas
                    design.Case.Gas.Composition
                    cell.TGas
                    cell.PGas
                    design.Case.Gas.Z)
            |> List.map (fun gas -> gas.Rho)

        twoPhaseFluid "Process gas (resolved tube side)" (averageOrElse fallback.Density densities)

    let private bypassGasFluidFromResult (design: DesignResult) =
        let fallback = gasFluid design.Case

        match design.BypassResult with
        | Some bypass when bypass.MassFlow > 0.0 && not bypass.Nodes.IsEmpty ->
            let area = Math.PI * design.Case.Bypass.LinerId * design.Case.Bypass.LinerId / 4.0
            let densities =
                bypass.Nodes
                |> List.choose (fun node ->
                    let denominator = area * node.Vel
                    if denominator > 0.0 then Some (bypass.MassFlow / denominator) else None)

            twoPhaseFluid "Process gas (resolved bypass side)" (averageOrElse fallback.Density densities)
        | _ -> fallback

    let private riserFluidFromResult (design: DesignResult) =
        twoPhaseFluid
            "Water/steam mixture (resolved)"
            (TwoPhase.homogeneousDensity design.Circulation.XOutRiser design.Sat)

    let private whbShellLiquidFromResult (design: DesignResult) =
        let volume = max 0.0 design.Transient.WaterInventory / max 1e-9 design.Sat.RhoL
        equivalentFluidRegion
            "WHB-SHELL-LIQUID-INVENTORY"
            "WHB shell-side liquid inventory"
            "WHB shell-side liquid inventory"
            design.Case.Tube.ShellId
            volume
            (saturatedLiquid design.Sat)

    let private drumLiquidFromResult (design: DesignResult) =
        let volume = max 0.0 design.Transient.DrumInventory / max 1e-9 design.Sat.RhoL
        equivalentFluidRegion
            "STEAM-DRUM-LIQUID-INVENTORY"
            "Steam drum liquid inventory"
            "Steam drum liquid inventory"
            design.Case.Loop.Drum.ShellId
            volume
            (saturatedLiquid design.Sat)

    let private ferruleComponents (coreCase: DesignCase) gas material =
        if not coreCase.Ferrule.Enabled then
            []
        else
            coreCase.Ferrule.Lengths
            |> List.mapi (fun index (fraction, length) ->
                let count =
                    max 1 (int (Math.Round(max 0.0 fraction * float coreCase.Tube.NTubes, MidpointRounding.AwayFromZero)))

                EqPressureParts.ferrule
                    (sprintf "WHB-TB-FERRULE-%02d" (index + 1))
                    (sprintf "Ferrules class %d" (index + 1))
                    (bom
                        (sprintf "BOM-WHB-TB-FERRULE-%02d" (index + 1))
                        (sprintf "Ferrules class %d" (index + 1))
                        (float count)
                        "ea")
                    coreCase.Ferrule.Bore
                    coreCase.Ferrule.SleeveOd
                    length
                    count
                    material
                    (Some gas))

    let private diaphragmComponent (coreCase: DesignCase) material =
        let count = max 0 coreCase.BaffleSpans.Length

        if count = 0 then
            []
        else
            [ EqPressureParts.diaphragm
                "WHB-TB-DIAPHRAGMS"
                "Support diaphragms"
                (bom "BOM-WHB-TB-DIAPHRAGMS" "Support diaphragms" (float count) "ea")
                coreCase.Tube.BaffleOd
                coreCase.BaffleThickness
                count
                material ]

    let private tubeBundleComponent (coreCase: DesignCase) =
        let tubeMaterial = toEquipmentMaterial coreCase.Material
        let ferruleMaterial = toEquipmentMaterial coreCase.FerruleMaterial
        let gas = gasFluid coreCase
        let tubeBank =
            EqPressureParts.tubeBank
                "WHB-TB-TUBES"
                "Tube bank"
                (bom "BOM-WHB-TB-TUBES" "Tube bank" (float coreCase.Tube.NTubes) "ea")
                coreCase.Tube.Di
                coreCase.Tube.Do
                coreCase.Tube.Length
                coreCase.Tube.NTubes
                tubeMaterial
                (Some gas)

        EqAssemblies.tubeBundle
            "WHB-TUBE-BUNDLE"
            "Tube bundle"
            (bom "BOM-WHB-TUBE-BUNDLE" "Tube bundle" 1.0 "set")
            tubeBank
            ([ yield! ferruleComponents coreCase gas ferruleMaterial
               yield! diaphragmComponent coreCase tubeMaterial ])

    let private centralBypassComponent (coreCase: DesignCase) =
        if not coreCase.Bypass.Enabled then
            []
        else
            let gas = gasFluid coreCase
            let linerMaterial = toEquipmentMaterial coreCase.Bypass.LinerMaterial
            let shellMaterial = toEquipmentMaterial coreCase.Bypass.PipeMaterial
            let valveBore = coreCase.Bypass.LinerId
            let valveFaceToFace = defaultValveFaceToFace valveBore
            let valveOuterDiameter = defaultValveBodyOuterDiameter coreCase.Bypass.PipeOd

            [ EqAssemblies.centralBypass
                "WHB-CENTRAL-BYPASS"
                "Central bypass"
                (bom "BOM-WHB-CENTRAL-BYPASS" "Central bypass" 1.0 "set")
                [ EqPressureParts.liner
                    "WHB-BP-LINER"
                    "Central bypass liner"
                    (bom "BOM-WHB-BP-LINER" "Central bypass liner" 1.0 "ea")
                    coreCase.Bypass.LinerId
                    coreCase.Bypass.LinerOd
                    coreCase.Tube.Length
                    linerMaterial
                    (Some gas)
                  EqPressureParts.shellBarrel
                    "WHB-BP-CONTAINMENT"
                    "Central bypass containment pipe"
                    (bom "BOM-WHB-BP-CONTAINMENT" "Central bypass containment pipe" 1.0 "ea")
                    coreCase.Bypass.InsulOd
                    coreCase.Bypass.PipeOd
                    coreCase.Tube.Length
                    shellMaterial
                    None
                  EqPressureParts.valveBody
                    "WHB-BP-VALVE"
                    "Central bypass valve"
                    (bom "BOM-WHB-BP-VALVE" "Central bypass valve" 1.0 "ea")
                    valveBore
                    valveFaceToFace
                    valveOuterDiameter
                    shellMaterial
                    (Some gas) ] ]

    let private whbEquipment (coreCase: DesignCase) =
        let shellMaterial = toEquipmentMaterial coreCase.ShellMaterial
        let tubeMaterial = toEquipmentMaterial coreCase.Material
        let tubesheetThickness = defaultTubesheetThickness coreCase

        { Id = "WHB-001"
          Name = coreCase.Name
          Bom = bom "BOM-WHB-001" coreCase.Name 1.0 "ea"
          Components =
            [ tubeBundleComponent coreCase
              EqPressureParts.shellBarrel
                "WHB-SHELL"
                "WHB shell"
                (bom "BOM-WHB-SHELL" "WHB shell" 1.0 "ea")
                coreCase.Tube.ShellId
                (coreCase.Tube.ShellId + 2.0 * coreCase.ShellThickness)
                coreCase.Tube.Length
                shellMaterial
                None
              EqPressureParts.tubesheet
                "WHB-TUBESHEETS"
                "WHB tubesheets"
                (bom "BOM-WHB-TUBESHEETS" "WHB tubesheets" 2.0 "ea")
                coreCase.Tube.ShellId
                tubesheetThickness
                (coreCase.Tube.Do + 0.0004)
                coreCase.Tube.NTubes
                2
                tubeMaterial ]
            @ centralBypassComponent coreCase }

    let private conveyorComponent (coreCase: DesignCase) (material: EqMaterials.MaterialProperties) =
        let drum = coreCase.Loop.Drum
        let width = max drum.ConvHydDia (sqrt (max 0.0 drum.ConvDuctArea))
        let height = max 0.0 (drum.ConvDuctArea / max 1e-9 width)
        let thickness = max 0.006 (0.003 * drum.ConvHydDia)

        if drum.ConveyorCount <= 0 || drum.ConvDuctArea <= 0.0 || drum.ConvLength <= 0.0 then
            []
        else
            [ EqPressureParts.expansionBox
                "DRUM-CONVEYORS"
                "Calm-box conveyor ducts"
                (bom "BOM-DRUM-CONVEYORS" "Calm-box conveyor ducts" (float drum.ConveyorCount) "ea")
                width
                height
                drum.ConvLength
                thickness
                drum.ConveyorCount
                material
                None ]

    let private steamDrumEquipment (coreCase: DesignCase) =
        let drum = coreCase.Loop.Drum
        let shellMaterial = toEquipmentMaterial coreCase.ShellMaterial
        let thickness = defaultDrumShellThickness drum

        { Id = "STEAM-DRUM-001"
          Name = "Steam drum"
          Bom = bom "BOM-STEAM-DRUM-001" "Steam drum" 1.0 "ea"
          Components =
            [ EqPressureParts.shellBarrel
                "DRUM-SHELL"
                "Steam drum shell"
                (bom "BOM-DRUM-SHELL" "Steam drum shell" 1.0 "ea")
                drum.ShellId
                (drum.ShellId + 2.0 * thickness)
                drum.Length
                shellMaterial
                None
              EqPressureParts.dishedHead
                "DRUM-HEADS"
                "Steam drum dished heads"
                (bom "BOM-DRUM-HEADS" "Steam drum dished heads" 2.0 "ea")
                drum.ShellId
                thickness
                (0.25 * drum.ShellId)
                2
                shellMaterial
                None
              EqPressureParts.demister
                "DRUM-DEMISTER"
                "Steam drum demister"
                (bom "BOM-DRUM-DEMISTER" "Steam drum demister" 1.0 "ea")
                drum.DemisterArea
                0.150
                160.0
                shellMaterial
              EqPressureParts.nozzle
                "DRUM-CHIMNEYS"
                "Steam chimneys"
                (bom "BOM-DRUM-CHIMNEYS" "Steam chimneys" (float drum.ChimneyCount) "ea")
                "Steam chimney"
                drum.ChimneyId
                (inferPipeOdFromNps "8\"" drum.ChimneyId)
                (max 0.250 (drum.ShellId - drum.NormalLevel))
                drum.ChimneyCount
                shellMaterial
                None
              EqPressureParts.shellBarrel
                "DRUM-MANIFOLD"
                "Steam drum top manifold"
                (bom "BOM-DRUM-MANIFOLD" "Steam drum top manifold" 1.0 "ea")
                drum.ManifoldId
                (inferPipeOdFromNps "20\"" drum.ManifoldId)
                drum.Length
                shellMaterial
                None
              EqPressureParts.nozzle
                "DRUM-OUTLET"
                "Steam outlet nozzle"
                (bom "BOM-DRUM-OUTLET" "Steam outlet nozzle" 1.0 "ea")
                "Steam outlet"
                drum.OutletId
                (inferPipeOdFromNps "18\"" drum.OutletId)
                (0.25 * drum.ShellId)
                1
                shellMaterial
                None ]
            @ conveyorComponent coreCase shellMaterial
          Levels = defaultDrumLevels drum }

    let private lineSegments lineId (line: Piping.Line) material fluid =
        let outerDiameter = inferPipeOdFromNps line.Nps line.Id

        let straights =
            line.Straights
            |> List.mapi (fun index length ->
                EqPressureParts.create
                    (sprintf "%s-SPOOL-%02d" lineId (index + 1))
                    (sprintf "%s straight spool %d" line.Tag (index + 1))
                    (bom
                        (sprintf "BOM-%s-SPOOL-%02d" lineId (index + 1))
                        (sprintf "%s straight spool %d" line.Tag (index + 1))
                        (float line.Count)
                        "ea")
                    (EqGeometry.Repeated
                        (line.Count,
                         EqGeometry.Pipe
                             { OuterDiameter = outerDiameter
                               WallThickness = max 0.0 (outerDiameter - line.Id) / 2.0
                               Length = length }))
                    material
                    fluid
                |> StraightPipeSpool)

        let elbows =
            line.Elbows
            |> List.mapi (fun index elbow ->
                EqPressureParts.create
                    (sprintf "%s-ELBOW-%02d" lineId (index + 1))
                    (sprintf "%s elbow %d" line.Tag (index + 1))
                    (bom
                        (sprintf "BOM-%s-ELBOW-%02d" lineId (index + 1))
                        (sprintf "%s elbow %.0f deg" line.Tag elbow.AngleDeg)
                        (float (line.Count * elbow.Count))
                        "ea")
                    (EqGeometry.Repeated
                        (line.Count * elbow.Count,
                         EqGeometry.PipeElbow
                             { OuterDiameter = outerDiameter
                               WallThickness = max 0.0 (outerDiameter - line.Id) / 2.0
                               AngleDeg = elbow.AngleDeg
                               CenterlineRadiusOverDiameter = elbow.ROverD
                               CoverageFraction = 1.0 }))
                    material
                    fluid
                |> Elbow)

        straights @ elbows

    let private pipelineEquipment
        (fromEquipment: string)
        (toEquipment: string)
        (fluidName: string)
        (fluidDensity: float option)
        (line: Piping.Line)
        : PipelineEquipment =
        let material = toEquipmentMaterial Materials.carbonSteel
        let toFluid (density: float) : EqMaterials.FluidProperties =
            { Name = fluidName
              Density = density }

        let fluid : EqMaterials.FluidProperties option =
            fluidDensity
            |> Option.map toFluid

        { Id = line.Tag
          Name = sprintf "%s %s" line.Tag line.Nps
          Bom = bom (sprintf "BOM-%s" line.Tag) (sprintf "%s %s" line.Tag line.Nps) 1.0 "line"
          Service = line.Nps
          FromNozzle =
            { EquipmentId = fromEquipment
              NozzleId = sprintf "%s-FROM" line.Tag }
          ToNozzle =
            { EquipmentId = toEquipment
              NozzleId = sprintf "%s-TO" line.Tag }
          Segments = lineSegments line.Tag line material fluid
          Notes = line.Note }

    let snapshotOfDesignCase (coreCase: DesignCase) : Interop.IWhbCoreEquipmentSnapshot =
        let sat = Steam.sat coreCase.Water.DrumPressure
        let downcomerFluid = saturatedLiquid sat
        let notes =
            [ "Derived from DesignCase without running the thermal or hydraulic solver."
              "Tube bundle and central bypass are exposed as composite WHB components."
              "Tube-side gas density comes from inlet process conditions; downcomer fluid density comes from drum-pressure saturation."
              "Riser internal-fluid hold-up is omitted because a DesignCase alone does not determine two-phase density."
              "WHB shell-side and steam-drum retained-fluid inventory are omitted until the equipment model gains partial-fill geometry."
              "Tubesheet thickness, steam-drum shell thickness, conveyor duct plate thickness, and bypass-valve envelope use local geometric proxies." ]
            |> String.concat " "

        { new Interop.IWhbCoreEquipmentSnapshot with
            member _.PackageName = coreCase.Name
            member _.Whbs = [ whbEquipment coreCase ]
            member _.Risers =
                coreCase.Loop.Risers
                |> List.map (pipelineEquipment "WHB-001" "STEAM-DRUM-001" "Water/steam mixture" None)
            member _.Downcomers =
                coreCase.Loop.Downcomers
                |> List.map (pipelineEquipment "STEAM-DRUM-001" "WHB-001" downcomerFluid.Name (Some downcomerFluid.Density))
            member _.SteamDrum = steamDrumEquipment coreCase
            member _.Notes = notes }

    let ofDesignCase (coreCase: DesignCase) =
        snapshotOfDesignCase coreCase |> fromWhbCore

    let snapshotOfDesignResult (design: DesignResult) : Interop.IWhbCoreEquipmentSnapshot =
        let baseSnapshot = snapshotOfDesignCase design.Case
        let tubeGas = tubeSideGasFluidFromResult design
        let bypassGas = bypassGasFluidFromResult design
        let riserFluid = riserFluidFromResult design
        let downcomerFluid = saturatedLiquid design.Sat
        let whb =
            baseSnapshot.Whbs
            |> List.map (fun equipment ->
                let components =
                    equipment.Components
                    |> List.map (
                        setComponentFluid "WHB-TB-TUBES" (Some tubeGas)
                        >> setComponentFluidWhere (fun part -> part.Id.StartsWith("WHB-TB-FERRULE-", StringComparison.Ordinal)) (Some tubeGas)
                        >> setComponentFluid "WHB-BP-LINER" (Some bypassGas)
                        >> setComponentFluid "WHB-BP-VALVE" (Some bypassGas)
                        >> appendChildren "WHB-TUBE-BUNDLE" [ equivalentFluidRegion
                                                                "WHB-TB-GAS-HOLDUP"
                                                                "Tube-side gas hold-up"
                                                                "Tube-side gas hold-up"
                                                                design.Case.Tube.Di
                                                                (Math.PI * design.Case.Tube.Di * design.Case.Tube.Di / 4.0
                                                                 * design.Case.Tube.Length
                                                                 * float design.Case.Tube.NTubes)
                                                                tubeGas ]
                        >> appendChildren "WHB-CENTRAL-BYPASS" (
                            match design.BypassResult with
                            | Some _ ->
                                [ equivalentFluidRegion
                                    "WHB-BP-GAS-HOLDUP"
                                    "Central bypass gas hold-up"
                                    "Central bypass gas hold-up"
                                    design.Case.Bypass.LinerId
                                    (Math.PI * design.Case.Bypass.LinerId * design.Case.Bypass.LinerId / 4.0
                                     * design.Case.Tube.Length)
                                    bypassGas ]
                            | None -> []))

                { equipment with
                    Components = components @ [ whbShellLiquidFromResult design ] })

        let drum =
            let baseDrum = baseSnapshot.SteamDrum
            { baseDrum with
                Components = baseDrum.Components @ [ drumLiquidFromResult design ] }

        let notes =
            [ baseSnapshot.Notes
              "Derived design-result enrichments:"
              "tube-side gas density from resolved WHB cell temperatures/pressures;"
              "bypass gas density from resolved bypass mass flow and node velocities;"
              "riser mixture density from resolved circulation quality;"
              "shell-side and steam-drum liquid inventories from resolved transient hold-up." ]
            |> String.concat " "

        { new Interop.IWhbCoreEquipmentSnapshot with
            member _.PackageName = baseSnapshot.PackageName
            member _.Whbs = whb
            member _.Risers =
                design.Case.Loop.Risers
                |> List.map (pipelineEquipment "WHB-001" "STEAM-DRUM-001" riserFluid.Name (Some riserFluid.Density))
            member _.Downcomers =
                design.Case.Loop.Downcomers
                |> List.map (pipelineEquipment "STEAM-DRUM-001" "WHB-001" downcomerFluid.Name (Some downcomerFluid.Density))
            member _.SteamDrum = drum
            member _.Notes = notes }

    let ofDesignResult (design: DesignResult) =
        snapshotOfDesignResult design |> fromWhbCore


