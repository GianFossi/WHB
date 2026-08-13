namespace Whb.Core.Solvers

/// <summary>
/// Provides solvercatalog functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module SolverCatalog =

    /// <summary>
    /// Represents solverdomain data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type SolverDomain =
        | Thermodynamic
        | Mechanical
        | Vibrational
        | Hydraulic
        | Optimization
        | Design

    [<CLIMutable>]
    /// <summary>
    /// Represents solverinfo data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type SolverInfo =
        { Name: string
          Domain: SolverDomain
          ModuleName: string
          Notes: string }

    /// <summary>
    /// Calculates or returns all for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let all =
        [ { Name = "Bundle 2D"; Domain = Thermodynamic; ModuleName = "Whb.Core.BundleSolver"; Notes = "Scambio gas/acqua con ferrule e boiling." }
          { Name = "Natural circulation"; Domain = Hydraulic; ModuleName = "Whb.Core.Circulation"; Notes = "Bilancio battente e perdite circuito." }
          { Name = "Fixed tubesheet mechanics"; Domain = Mechanical; ModuleName = "Whb.Core.Mechanics"; Notes = "Dilatazione impedita, stress e buckling." }
          { Name = "Flow induced vibration"; Domain = Vibrational; ModuleName = "Whb.Core.Vibration"; Notes = "Verifica FIV per campata e banda." } ]
