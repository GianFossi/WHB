namespace Whb.Core.Reports

module ReportCatalog =

    type ReportFormat = Text | Html | Csv | Pdf

    [<CLIMutable>]
    type ReportRequest =
        { Title: string
          OutputFolder: string
          Formats: ReportFormat list
          IncludeCells: bool
          IncludeStress: bool
          IncludeVibration: bool }

    let defaultRequest folder =
        { Title = "WHB report"
          OutputFolder = folder
          Formats = [ Text; Html; Csv ]
          IncludeCells = true
          IncludeStress = true
          IncludeVibration = true }
