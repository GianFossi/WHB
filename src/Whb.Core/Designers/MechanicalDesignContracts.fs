namespace Whb.Core

/// <summary>
/// Typed contracts for the future code-level mechanical sizing modules.
/// </summary>
module MechanicalDesignContracts =

    type MechanicalCalculationStatus =
        | ReadyForImplementation
        | NeedsAdditionalGeometry

    /// <summary>
    /// One scalar mechanical design input with engineering units and provenance.
    /// </summary>
    type MechanicalQuantity =
        { Name: string
          Value: float option
          Unit: string
          Source: string }

    /// <summary>
    /// Pressure and axial loads that will drive a future thickness calculation.
    /// </summary>
    type PressureAxialEnvelope =
        { InternalPressure: MechanicalQuantity
          ExternalPressure: MechanicalQuantity
          AxialLoad: MechanicalQuantity option }

    type TubeThicknessInput =
        { MaterialName: string
          OuterDiameter: MechanicalQuantity
          InnerDiameter: MechanicalQuantity
          Length: MechanicalQuantity
          DesignMetalTemperature: MechanicalQuantity
          Loads: PressureAxialEnvelope
          Notes: string list }

    type CylindricalWallThicknessInput =
        { ComponentTag: string
          MaterialName: string option
          InnerDiameter: MechanicalQuantity
          Length: MechanicalQuantity
          CurrentThickness: MechanicalQuantity option
          DesignMetalTemperature: MechanicalQuantity option
          Loads: PressureAxialEnvelope
          Notes: string list }

    type CreviceFreeWeldInput =
        { JointType: string
          TubeOuterDiameter: MechanicalQuantity
          TubeCount: int
          TubeMaterialName: string
          AxialLoadPerTube: MechanicalQuantity
          PressureDifferential: MechanicalQuantity
          Notes: string list }

    type TubesheetThicknessInput =
        { TubesheetTag: string
          ShellInnerDiameter: MechanicalQuantity
          ChannelInnerDiameter: MechanicalQuantity
          TubeOuterDiameter: MechanicalQuantity
          TubePitch: MechanicalQuantity
          TubeCount: int
          TubeJointType: string
          TubeMaterialName: string
          ShellMaterialName: string
          ShellSidePressure: MechanicalQuantity
          TubeSidePressure: MechanicalQuantity
          AxialLoadPerTube: MechanicalQuantity
          Notes: string list }

    /// <summary>
    /// One prepared future calculation: typed inputs plus a completeness verdict.
    /// </summary>
    type PreparedCalculation<'TInput> =
        { Name: string
          Status: MechanicalCalculationStatus
          Input: 'TInput
          MissingInputs: string list
          Notes: string list }

    /// <summary>
    /// Prepared interfaces for the future code-level mechanical calculations requested by the project.
    /// </summary>
    type MechanicalCalculationInterface =
        { TubeThickness: PreparedCalculation<TubeThicknessInput>
          ChannelWallThickness: PreparedCalculation<CylindricalWallThicknessInput>
          ShellWallThickness: PreparedCalculation<CylindricalWallThicknessInput>
          BypassCentralWallThickness: PreparedCalculation<CylindricalWallThicknessInput> option
          CreviceFreeWeld: PreparedCalculation<CreviceFreeWeldInput>
          TubesheetThickness: PreparedCalculation<TubesheetThicknessInput> }
