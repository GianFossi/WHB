namespace Whb.Core

open System
open System.Text
open Types
open MechanicalDesignContracts

module ReportMechanicalInterface =

    open ReportCommon

    let private statusLabel =
        function
        | ReadyForImplementation -> "READY"
        | NeedsAdditionalGeometry -> "INCOMPLETE"

    let private quantityValueText (quantity: MechanicalQuantity) =
        match quantity.Value with
        | Some value when Double.IsNaN value -> "n/a"
        | Some value -> sprintf "%s %s" (f4 value) quantity.Unit
        | None -> "missing"

    let private quantityLine (quantity: MechanicalQuantity) =
        sprintf "    %-30s %-22s source: %s"
            quantity.Name
            (quantityValueText quantity)
            quantity.Source

    let private appendQuantities (sb: StringBuilder) (quantities: MechanicalQuantity list) =
        quantities
        |> List.iter (fun quantity -> sb.AppendLine(quantityLine quantity) |> ignore)

    let private appendOptionalQuantity (sb: StringBuilder) (label: string) (quantity: MechanicalQuantity option) =
        match quantity with
        | Some value -> sb.AppendLine(quantityLine { value with Name = label }) |> ignore
        | None -> sb.AppendLine(sprintf "    %-30s %-22s source: not modeled" label "missing") |> ignore

    let private appendMissingInputs (sb: StringBuilder) (missingInputs: string list) =
        if List.isEmpty missingInputs then
            sb.AppendLine("    Missing inputs                 none") |> ignore
        else
            sb.AppendLine(sprintf "    Missing inputs                 %s" (String.concat ", " missingInputs)) |> ignore

    let private appendNotes (sb: StringBuilder) (notes: string list) =
        notes |> List.iter (fun note -> para sb "    note: " note)

    let private appendPreparedHeader (sb: StringBuilder) (prepared: PreparedCalculation<'TInput>) =
        sb.AppendLine(sprintf "  %s" prepared.Name) |> ignore
        sb.AppendLine(sprintf "    Status                         %s" (statusLabel prepared.Status)) |> ignore
        appendMissingInputs sb prepared.MissingInputs

    let private appendPressureEnvelope (sb: StringBuilder) (loads: PressureAxialEnvelope) =
        appendQuantities sb [ loads.InternalPressure; loads.ExternalPressure ]
        appendOptionalQuantity sb "axial load" loads.AxialLoad

    let private appendTubeThickness (sb: StringBuilder) (prepared: PreparedCalculation<TubeThicknessInput>) =
        appendPreparedHeader sb prepared
        appendQuantities sb
            [ prepared.Input.OuterDiameter
              prepared.Input.InnerDiameter
              prepared.Input.Length
              prepared.Input.DesignMetalTemperature ]
        appendPressureEnvelope sb prepared.Input.Loads
        sb.AppendLine(sprintf "    Material                       %s" prepared.Input.MaterialName) |> ignore
        appendNotes sb prepared.Notes

    let private appendCylinderThickness (sb: StringBuilder) (prepared: PreparedCalculation<CylindricalWallThicknessInput>) =
        appendPreparedHeader sb prepared
        appendQuantities sb [ prepared.Input.InnerDiameter; prepared.Input.Length ]
        appendOptionalQuantity sb "current thickness" prepared.Input.CurrentThickness
        appendOptionalQuantity sb "design metal temperature" prepared.Input.DesignMetalTemperature
        appendPressureEnvelope sb prepared.Input.Loads
        sb.AppendLine(sprintf "    Component tag                  %s" prepared.Input.ComponentTag) |> ignore
        sb.AppendLine(sprintf "    Material                       %s" (prepared.Input.MaterialName |> Option.defaultValue "missing")) |> ignore
        appendNotes sb prepared.Notes

    let private appendWeldSizing (sb: StringBuilder) (prepared: PreparedCalculation<CreviceFreeWeldInput>) =
        appendPreparedHeader sb prepared
        appendQuantities sb
            [ prepared.Input.TubeOuterDiameter
              prepared.Input.AxialLoadPerTube
              prepared.Input.PressureDifferential ]
        sb.AppendLine(sprintf "    Joint type                     %s" prepared.Input.JointType) |> ignore
        sb.AppendLine(sprintf "    Tube count                     %d" prepared.Input.TubeCount) |> ignore
        sb.AppendLine(sprintf "    Tube material                  %s" prepared.Input.TubeMaterialName) |> ignore
        appendNotes sb prepared.Notes

    let private appendTubesheetThickness (sb: StringBuilder) (prepared: PreparedCalculation<TubesheetThicknessInput>) =
        appendPreparedHeader sb prepared
        appendQuantities sb
            [ prepared.Input.ShellInnerDiameter
              prepared.Input.ChannelInnerDiameter
              prepared.Input.TubeOuterDiameter
              prepared.Input.TubePitch
              prepared.Input.ShellSidePressure
              prepared.Input.TubeSidePressure
              prepared.Input.AxialLoadPerTube ]
        sb.AppendLine(sprintf "    Tubesheet tag                  %s" prepared.Input.TubesheetTag) |> ignore
        sb.AppendLine(sprintf "    Tube count                     %d" prepared.Input.TubeCount) |> ignore
        sb.AppendLine(sprintf "    Tube joint                     %s" prepared.Input.TubeJointType) |> ignore
        sb.AppendLine(sprintf "    Tube material                  %s" prepared.Input.TubeMaterialName) |> ignore
        sb.AppendLine(sprintf "    Shell material                 %s" prepared.Input.ShellMaterialName) |> ignore
        appendNotes sb prepared.Notes

    let private appendBypassThickness (sb: StringBuilder) (prepared: PreparedCalculation<CylindricalWallThicknessInput> option) =
        match prepared with
        | Some value -> appendCylinderThickness sb value
        | None ->
            sb.AppendLine("  Central bypass wall thickness") |> ignore
            sb.AppendLine("    Status                         not applicable") |> ignore

    let private appendInterfaceSection (sb: StringBuilder) (title: string) (iface: MechanicalCalculationInterface) =
        hdr sb title
        para sb "  " "Prepared inputs for future detailed mechanical code calculations. This report is generated from the same shared verification path used by rating, optimize, and design."
        sb.AppendLine() |> ignore
        appendTubeThickness sb iface.TubeThickness
        sb.AppendLine() |> ignore
        appendCylinderThickness sb iface.ChannelWallThickness
        sb.AppendLine() |> ignore
        appendCylinderThickness sb iface.ShellWallThickness
        sb.AppendLine() |> ignore
        appendBypassThickness sb iface.BypassCentralWallThickness
        sb.AppendLine() |> ignore
        appendWeldSizing sb iface.CreviceFreeWeld
        sb.AppendLine() |> ignore
        appendTubesheetThickness sb iface.TubesheetThickness

    let private renderSections (title: string) (namedInterfaces: (string * MechanicalCalculationInterface) list) =
        let sb = StringBuilder()
        sb.AppendLine(dline) |> ignore
        sb.AppendLine(title) |> ignore
        sb.AppendLine(dline) |> ignore
        namedInterfaces
        |> List.iter (fun (name, iface) -> appendInterfaceSection sb name iface)
        sb.ToString()

    let text (design: DesignResult) =
        [ sprintf "Caso %s" design.Case.Name, MechanicalDesignInterface.fromDesignResult design ]
        |> renderSections (sprintf "MECHANICAL CALCULATION INTERFACE - %s" design.Case.Name)

    let textMany (title: string) (results: (string * DesignResult) list) =
        results
        |> List.map (fun (name, design) -> name, MechanicalDesignInterface.fromDesignResult design)
        |> renderSections title
