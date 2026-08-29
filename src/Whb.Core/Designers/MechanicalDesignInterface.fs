namespace Whb.Core

open System
open Types
open DesignContracts
open MechanicalDesignContracts

/// <summary>
/// Builds the typed hand-off interface for the future detailed mechanical sizing modules.
/// </summary>
/// <remarks>
/// This module does not perform the final code calculations yet. It only prepares immutable,
/// traceable inputs from the shared thermal/process stage and the existing mechanical screening
/// output, so the later implementation can plug into a stable contract.
/// </remarks>
module MechanicalDesignInterface =

    type private PreparationSource =
        { Case: DesignCase
          Cells: CellResult list
          BypassResult: Bypass.Result option
          FixedTubesheet: FixedTubesheetResult
          Stress: StressResult }

    type private PreparationContext =
        { Source: PreparationSource }

    type private PartialInterface =
        { TubeThickness: PreparedCalculation<TubeThicknessInput> option
          ChannelWallThickness: PreparedCalculation<CylindricalWallThicknessInput> option
          ShellWallThickness: PreparedCalculation<CylindricalWallThicknessInput> option
          BypassCentralWallThickness: PreparedCalculation<CylindricalWallThicknessInput> option option
          CreviceFreeWeld: PreparedCalculation<CreviceFreeWeldInput> option
          TubesheetThickness: PreparedCalculation<TubesheetThicknessInput> option }

    let private present (name: string) (unitText: string) (source: string) (value: float) : MechanicalQuantity =
        { Name = name
          Value = Some value
          Unit = unitText
          Source = source }

    let private missing (name: string) (unitText: string) (source: string) : MechanicalQuantity =
        { Name = name
          Value = None
          Unit = unitText
          Source = source }

    let private quantityMissingLabel (quantity: MechanicalQuantity) =
        match quantity.Value with
        | Some _ -> None
        | None -> Some quantity.Name

    let private axialMissingLabel =
        function
        | Some quantity -> quantityMissingLabel quantity
        | None -> Some "axial load"

    let private loadsMissing (loads: PressureAxialEnvelope) =
        [ quantityMissingLabel loads.InternalPressure
          quantityMissingLabel loads.ExternalPressure
          axialMissingLabel loads.AxialLoad ]
        |> List.choose id

    let private missingForCylinder (input: CylindricalWallThicknessInput) =
        ([ quantityMissingLabel input.InnerDiameter
           quantityMissingLabel input.Length
           match input.CurrentThickness with
           | Some quantity -> quantityMissingLabel quantity
           | None -> Some "current thickness"
           match input.DesignMetalTemperature with
           | Some quantity -> quantityMissingLabel quantity
           | None -> Some "design metal temperature"
           if input.MaterialName.IsSome then None else Some "material name" ]
         @ (loadsMissing input.Loads |> List.map Some))
        |> List.choose id

    let private missingForTube (input: TubeThicknessInput) =
        ([ quantityMissingLabel input.OuterDiameter
           quantityMissingLabel input.InnerDiameter
           quantityMissingLabel input.Length
           quantityMissingLabel input.DesignMetalTemperature ]
         @ (loadsMissing input.Loads |> List.map Some))
        |> List.choose id

    let private missingForWeld (input: CreviceFreeWeldInput) =
        [ quantityMissingLabel input.TubeOuterDiameter
          quantityMissingLabel input.AxialLoadPerTube
          quantityMissingLabel input.PressureDifferential ]
        |> List.choose id

    let private missingForTubesheet (input: TubesheetThicknessInput) =
        [ quantityMissingLabel input.ShellInnerDiameter
          quantityMissingLabel input.ChannelInnerDiameter
          quantityMissingLabel input.TubeOuterDiameter
          quantityMissingLabel input.TubePitch
          quantityMissingLabel input.ShellSidePressure
          quantityMissingLabel input.TubeSidePressure
          quantityMissingLabel input.AxialLoadPerTube ]
        |> List.choose id

    let private statusFor missingInputs =
        if List.isEmpty missingInputs then ReadyForImplementation
        else NeedsAdditionalGeometry

    let private prepare name input notes missingInputs =
        { Name = name
          Status = statusFor missingInputs
          Input = input
          MissingInputs = missingInputs
          Notes = notes }

    let private maxTubePressure (ctx: PreparationContext) =
        ctx.Source.Cells
        |> List.maxBy (fun c -> c.PGas)
        |> fun c -> c.PGas

    let private maxTubeMetalTemperature (ctx: PreparationContext) =
        ctx.Source.Cells
        |> List.maxBy (fun c -> c.TMetalIn)
        |> fun c -> c.TMetalIn

    let private shellDesignTemperature (ctx: PreparationContext) =
        ctx.Source.FixedTubesheet.TShellEq

    let private maxShellPressure (ctx: PreparationContext) =
        ctx.Source.Case.Water.DrumPressure

    let private maxTubeToShellDifferential (ctx: PreparationContext) =
        abs (maxTubePressure ctx - maxShellPressure ctx)

    let private tryBypassMember (ctx: PreparationContext) =
        ctx.Source.Stress.Members
        |> List.tryFind (fun member_ -> member_.Label = "BY-PASS - tubo di contenimento")

    let private buildContext (thermal: ThermalProcessStageResult) (fixedTubesheet: FixedTubesheetResult) (stress: StressResult) : PreparationContext =
        { Source =
            { Case = thermal.Case
              Cells = thermal.Cells
              BypassResult = thermal.BypassResult
              FixedTubesheet = fixedTubesheet
              Stress = stress } }

    let private buildContextFromDesignResult (design: DesignResult) : PreparationContext =
        { Source =
            { Case = design.Case
              Cells = design.Cells
              BypassResult = design.BypassResult
              FixedTubesheet = design.FixedTubesheet
              Stress = design.Stress } }

    let private startAssembly (context: PreparationContext) =
        context,
        { TubeThickness = None
          ChannelWallThickness = None
          ShellWallThickness = None
          BypassCentralWallThickness = None
          CreviceFreeWeld = None
          TubesheetThickness = None }

    let private addTubeThickness ((ctx, partial): PreparationContext * PartialInterface) =
        let tube = ctx.Source.Case.Tube
        let input =
            { MaterialName = ctx.Source.Case.Material.Name
              OuterDiameter = present "tube outer diameter" "m" "DesignCase.Tube.Do" tube.Do
              InnerDiameter = present "tube inner diameter" "m" "DesignCase.Tube.Di" tube.Di
              Length = present "tube length" "m" "DesignCase.Tube.Length" tube.Length
              DesignMetalTemperature = present "tube design metal temperature" "K" "max thermal tube-metal temperature" (maxTubeMetalTemperature ctx)
              Loads =
                { InternalPressure = present "tube internal pressure" "Pa(a)" "max tube-side gas pressure from thermal cells" (maxTubePressure ctx)
                  ExternalPressure = present "tube external pressure" "Pa(a)" "shell-side drum pressure" (maxShellPressure ctx)
                  AxialLoad = Some (present "tube axial load per tube" "N" "fixed-tubesheet screening force per tube" ctx.Source.FixedTubesheet.ForcePerTube) }
              Notes =
                [ "Use for future tube wall-thickness checks under internal pressure, external pressure, and axial load."
                  "Tube-side pressure comes from the shared thermal/process verification path; shell-side pressure comes from the saturated water side." ] }
        let prepared = prepare "Tube thickness" input input.Notes (missingForTube input)
        ctx, { partial with TubeThickness = Some prepared }

    let private addChannelWallThickness ((ctx, partial): PreparationContext * PartialInterface) =
        let input =
            { ComponentTag = "CHANNEL"
              MaterialName = None
              InnerDiameter = missing "channel inner diameter" "m" "front/rear channel geometry is not modeled in DesignCase"
              Length = missing "channel shell length" "m" "front/rear channel geometry is not modeled in DesignCase"
              CurrentThickness = None
              DesignMetalTemperature = None
              Loads =
                { InternalPressure = present "channel internal pressure" "Pa(a)" "use tube-side process design pressure as current proxy" (maxTubePressure ctx)
                  ExternalPressure = missing "channel external pressure" "Pa(a)" "vacuum or external pressure design case is not modeled yet"
                  AxialLoad = Some (missing "channel axial load" "N" "channel longitudinal pressure load path is not modeled yet") }
              Notes =
                [ "Prepared as an explicit placeholder for future channel wall-thickness sizing."
                  "The current WHB model has no dedicated channel geometry or channel material object, so this interface stays intentionally incomplete." ] }
        let prepared = prepare "Channel shell thickness" input input.Notes (missingForCylinder input)
        ctx, { partial with ChannelWallThickness = Some prepared }

    let private addShellWallThickness ((ctx, partial): PreparationContext * PartialInterface) =
        let tube = ctx.Source.Case.Tube
        let case_ = ctx.Source.Case
        let input =
            { ComponentTag = "WHB SHELL"
              MaterialName = Some case_.ShellMaterial.Name
              InnerDiameter = present "shell inner diameter" "m" "DesignCase.Tube.ShellId" tube.ShellId
              Length = present "shell straight length" "m" "DesignCase.Tube.Length" tube.Length
              CurrentThickness = Some (present "shell current thickness" "m" "DesignCase.ShellThickness" case_.ShellThickness)
              DesignMetalTemperature = Some (present "shell design metal temperature" "K" "fixed-tubesheet shell equivalent temperature" (shellDesignTemperature ctx))
              Loads =
                { InternalPressure = present "shell internal pressure" "Pa(a)" "shell-side drum pressure" (maxShellPressure ctx)
                  ExternalPressure = missing "shell external pressure" "Pa(a)" "vacuum or external pressure design case is not modeled yet"
                  AxialLoad = Some (present "shell axial load" "N" "global pressure-end load from restrained-system screening" ctx.Source.Stress.PressureEndLoad) }
              Notes =
                [ "Prepared for future shell wall-thickness sizing under internal pressure, external pressure, and axial load."
                  "External pressure as a code design case still needs an explicit process/mechanical input." ] }
        let prepared = prepare "Shell wall thickness" input input.Notes (missingForCylinder input)
        ctx, { partial with ShellWallThickness = Some prepared }

    let private addBypassCentralWallThickness ((ctx, partial): PreparationContext * PartialInterface) =
        let case_ = ctx.Source.Case
        if not case_.Bypass.Enabled then
            ctx, { partial with BypassCentralWallThickness = Some None }
        else
            let bypassMember = tryBypassMember ctx
            let input =
                { ComponentTag = "CENTRAL BY-PASS PIPE"
                  MaterialName = Some case_.Bypass.PipeMaterial.Name
                  InnerDiameter = present "bypass pipe inner diameter" "m" "DesignCase.Bypass.InsulOd" case_.Bypass.InsulOd
                  Length = present "bypass pipe straight length" "m" "DesignCase.Tube.Length" case_.Tube.Length
                  CurrentThickness = Some (present "bypass pipe current thickness" "m" "0.5 * (PipeOd - InsulOd)" (0.5 * (case_.Bypass.PipeOd - case_.Bypass.InsulOd)))
                  DesignMetalTemperature =
                    Some (present "bypass pipe design metal temperature" "K" "max bypass pipe temperature from thermal bypass nodes"
                            (ctx.Source.BypassResult |> Option.map (fun b -> b.TPipeMax) |> Option.defaultValue Double.NaN))
                  Loads =
                    { InternalPressure = present "bypass pipe internal pressure" "Pa(a)" "tube-side gas pressure proxy" (maxTubePressure ctx)
                      ExternalPressure = present "bypass pipe external pressure" "Pa(a)" "shell-side drum pressure" (maxShellPressure ctx)
                      AxialLoad =
                        bypassMember
                        |> Option.map (fun m -> present "bypass pipe axial load" "N" "restrained-system member force" m.Force) }
                  Notes =
                    [ "Prepared for future central bypass pipe wall-thickness sizing."
                      "The current thermal model resolves bypass temperatures and pressures, so this interface is complete enough for implementation." ] }
            let prepared = prepare "Central bypass wall thickness" input input.Notes (missingForCylinder input)
            ctx, { partial with BypassCentralWallThickness = Some (Some prepared) }

    let private addCreviceFreeWeld ((ctx, partial): PreparationContext * PartialInterface) =
        let tube = ctx.Source.Case.Tube
        let input =
            { JointType = Vibration.jointName ctx.Source.Case.TubesheetJoint
              TubeOuterDiameter = present "tube outer diameter" "m" "DesignCase.Tube.Do" tube.Do
              TubeCount = tube.NTubes
              TubeMaterialName = ctx.Source.Case.Material.Name
              AxialLoadPerTube = present "tube axial load per tube" "N" "fixed-tubesheet screening force per tube" ctx.Source.FixedTubesheet.ForcePerTube
              PressureDifferential = present "tube-to-shell pressure differential" "Pa" "absolute difference between tube-side and shell-side pressure envelopes" (maxTubeToShellDifferential ctx)
              Notes =
                [ "Prepared for future crevice-free tube-to-tubesheet weld sizing."
                  "The existing screening already computes the restrained axial force per tube; future weld rules may add local pressure and fabrication factors." ] }
        let prepared = prepare "Crevice-free weld sizing" input input.Notes (missingForWeld input)
        ctx, { partial with CreviceFreeWeld = Some prepared }

    let private addTubesheetThickness ((ctx, partial): PreparationContext * PartialInterface) =
        let tube = ctx.Source.Case.Tube
        let case_ = ctx.Source.Case
        let input =
            { TubesheetTag = "HOT TUBESHEET"
              ShellInnerDiameter = present "shell inner diameter" "m" "DesignCase.Tube.ShellId" tube.ShellId
              ChannelInnerDiameter = missing "channel inner diameter" "m" "front/rear channel geometry is not modeled in DesignCase"
              TubeOuterDiameter = present "tube outer diameter" "m" "DesignCase.Tube.Do" tube.Do
              TubePitch = present "tube pitch" "m" "DesignCase.Tube.Pitch" tube.Pitch
              TubeCount = tube.NTubes
              TubeJointType = Vibration.jointName case_.TubesheetJoint
              TubeMaterialName = case_.Material.Name
              ShellMaterialName = case_.ShellMaterial.Name
              ShellSidePressure = present "shell-side pressure" "Pa(a)" "shell-side drum pressure" (maxShellPressure ctx)
              TubeSidePressure = present "tube-side pressure" "Pa(a)" "max tube-side gas pressure from thermal cells" (maxTubePressure ctx)
              AxialLoadPerTube = present "tube axial load per tube" "N" "fixed-tubesheet screening force per tube" ctx.Source.FixedTubesheet.ForcePerTube
              Notes =
                [ "Prepared for future tubesheet thickness sizing."
                  "The current model already exposes the tube pattern, shell diameter, pressure split, and axial load per tube."
                  "A detailed code implementation will still need explicit channel geometry, gasket lane rules, and tubesheet boundary assumptions." ] }
        let prepared = prepare "Tubesheet thickness" input input.Notes (missingForTubesheet input)
        ctx, { partial with TubesheetThickness = Some prepared }

    let private assemble ((_, partial): PreparationContext * PartialInterface) : MechanicalCalculationInterface =
        { TubeThickness = partial.TubeThickness |> Option.get
          ChannelWallThickness = partial.ChannelWallThickness |> Option.get
          ShellWallThickness = partial.ShellWallThickness |> Option.get
          BypassCentralWallThickness = partial.BypassCentralWallThickness |> Option.defaultValue None
          CreviceFreeWeld = partial.CreviceFreeWeld |> Option.get
          TubesheetThickness = partial.TubesheetThickness |> Option.get }

    let runPure (thermal: ThermalProcessStageResult) (fixedTubesheet: FixedTubesheetResult) (stress: StressResult) : MechanicalCalculationInterface =
        buildContext thermal fixedTubesheet stress
        |> startAssembly
        |> addTubeThickness
        |> addChannelWallThickness
        |> addShellWallThickness
        |> addBypassCentralWallThickness
        |> addCreviceFreeWeld
        |> addTubesheetThickness
        |> assemble

    let fromDesignResult (design: DesignResult) : MechanicalCalculationInterface =
        buildContextFromDesignResult design
        |> startAssembly
        |> addTubeThickness
        |> addChannelWallThickness
        |> addShellWallThickness
        |> addBypassCentralWallThickness
        |> addCreviceFreeWeld
        |> addTubesheetThickness
        |> assemble
