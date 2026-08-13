namespace Whb.Core.Reports

/// <summary>
/// Provides reportcatalog functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module ReportCatalog =

    /// <summary>
    /// Represents reportformat data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ReportFormat = Text | Html | Csv | Pdf

    [<CLIMutable>]
    /// <summary>
    /// Represents reportrequest data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type ReportRequest =
        { Title: string
          OutputFolder: string
          Formats: ReportFormat list
          IncludeCells: bool
          IncludeStress: bool
          IncludeVibration: bool }

    /// <summary>
    /// Calculates or returns defaultrequest for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let defaultRequest folder =
        { Title = "WHB report"
          OutputFolder = folder
          Formats = [ Text; Html; Csv ]
          IncludeCells = true
          IncludeStress = true
          IncludeVibration = true }
