namespace Whb.Core.MaterialData.Gas

open Whb.Core
module GasPropertySets =

    [<CLIMutable>]
    type GasPoint =
        { Temperature: float
          Pressure: float
          Density: float
          Cp: float
          Viscosity: float
          Conductivity: float
          Prandtl: float
          MolecularWeight: float }
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


