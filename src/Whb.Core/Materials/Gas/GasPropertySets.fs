namespace Whb.Core.MaterialData.Gas

open Whb.Core

/// <summary>
/// Provides gaspropertysets functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module GasPropertySets =

    [<CLIMutable>]
    /// <summary>
    /// Represents gaspoint data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type GasPoint =
        { Temperature: float
          Pressure: float
          Density: float
          Cp: float
          Viscosity: float
          Conductivity: float
          Prandtl: float
          MolecularWeight: float }

    /// <summary>
    /// Calculates or returns mixture for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let mixture composition temperature pressure =
        let p = GasProps.mix composition temperature pressure 1.0
        { Temperature = temperature
          Pressure = pressure
          Density = p.Rho
          Cp = p.Cp
          Viscosity = p.Mu
          Conductivity = p.K
          Prandtl = p.Pr
          MolecularWeight = GasProps.mixMolarMass composition }
