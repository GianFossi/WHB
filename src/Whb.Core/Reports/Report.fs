namespace Whb.Core

/// <summary>
/// Public facade for WHB report generation.
/// </summary>
module Report =
    let f0 = ReportCommon.f0
    let f1 = ReportCommon.f1
    let f2 = ReportCommon.f2
    let f3 = ReportCommon.f3
    let f4 = ReportCommon.f4
    let f5 = ReportCommon.f5
    let inventoryText = ReportCommon.inventoryText
    let inventoryCsv = ReportCommon.inventoryCsv
    let text = ReportText.text
    let mechanicalInterfaceText = ReportMechanicalInterface.text
    let mechanicalInterfaceTextMany = ReportMechanicalInterface.textMany
    let synthesis = ReportSynthesis.synthesis
    let sizingText = ReportSizing.text
    let csvCells = ReportCsv.csvCells
    let maldistributionText = ReportCsv.maldistributionText
    let vibrationText = ReportCsv.vibrationText
    let csvStress = ReportCsv.csvStress
    let csvValve = ReportCsv.csvValve
    let csvAxial = ReportCsv.csvAxial
    let sulphurCondenserText = ReportSulphurCondenser.text
    let sulphurCondenserCsv = ReportSulphurCondenser.csvProfile


