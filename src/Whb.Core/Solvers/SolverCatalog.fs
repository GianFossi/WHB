namespace Whb.Core.Solvers

/// Catalogo leggero dei motori di calcolo. I solver storici restano nei moduli
/// esistenti; questo layer rende esplicita la classificazione richiesta.
module SolverCatalog =

    type SolverDomain =
        | Thermodynamic
        | Mechanical
        | Vibrational
        | Hydraulic
        | Optimization
        | Design

    [<CLIMutable>]
    type SolverInfo =
        { Name: string
          Domain: SolverDomain
          ModuleName: string
          Notes: string }

    let all =
        [ { Name = "Bundle 2D"; Domain = Thermodynamic; ModuleName = "Whb.Core.BundleSolver"; Notes = "Scambio gas/acqua con ferrule e boiling." }
          { Name = "Natural circulation"; Domain = Hydraulic; ModuleName = "Whb.Core.Circulation"; Notes = "Bilancio battente e perdite circuito." }
          { Name = "Fixed tubesheet mechanics"; Domain = Mechanical; ModuleName = "Whb.Core.Mechanics"; Notes = "Dilatazione impedita, stress e buckling." }
          { Name = "Flow induced vibration"; Domain = Vibrational; ModuleName = "Whb.Core.Vibration"; Notes = "Verifica FIV per campata e banda." } ]
