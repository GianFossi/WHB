namespace Whb.Core

open WhbThermo.Steam

/// <summary>
/// Water and steam properties for Whb.Core: a facade over <c>WhbThermo.Steam.Water</c>.
/// </summary>
/// <remarks>
/// The IAPWS-IF97 formulation, the IAPWS 2008 viscosity, the IAPWS 2011 conductivity, the
/// surface tension and the auxiliary saturation fits live in <c>src/Whb.Thermo</c>, with their
/// coefficients in <c>data/iapws-if97.json</c> and <c>data/iapws-water-transport.json</c>. This
/// module keeps the names and units the solver has always used (MPa or Pa as named, K, kJ/kg from
/// the region functions, SI in <see cref="SatProps"/>); do not copy coefficients back here.
/// </remarks>
module Steam =
    /// <summary>Saturation pressure [MPa] at temperature [K].</summary>
    let psat_MPa (tK: float) = Water.psatMPa tK
    /// <summary>Saturation temperature [K] at pressure [MPa].</summary>
    let tsat_K (pMPa: float) = Water.tsatK pMPa
    /// <summary>Dynamic viscosity [Pa·s] of water or steam at temperature [K] and density [kg/m³].</summary>
    let viscosity (tK: float) (rho: float) = Water.viscosity tK rho
    /// <summary>Thermal conductivity [W/(m·K)] of water or steam at temperature [K] and density [kg/m³].</summary>
    let conductivity (tK: float) (rho: float) = Water.conductivity tK rho
    /// <summary>Surface tension [N/m] of saturated water at temperature [K].</summary>
    let surfaceTension (tK: float) = Water.surfaceTension tK
    /// <summary>
    /// Explicit saturation-curve fits (auxiliary equations, not the formulation).
    /// </summary>
    module Explicit =
        /// <summary>Saturated liquid density [kg/m³].</summary>
        let rhoLsat (tK: float) = Water.Auxiliary.rhoLsat tK
        /// <summary>Saturated vapour density [kg/m³].</summary>
        let rhoVsat (tK: float) = Water.Auxiliary.rhoVsat tK
        /// <summary>Saturated liquid viscosity [Pa·s], Vogel equation.</summary>
        let muLVogel (tK: float) = Water.Auxiliary.muLVogel tK
        /// <summary>Saturated liquid conductivity [W/(m·K)], Ramires fit.</summary>
        let kLRamires (tK: float) = Water.Auxiliary.kLRamires tK
        /// <summary>Latent heat [J/kg], Watson correlation.</summary>
        let hfgWatson (tK: float) = Water.Auxiliary.hfgWatson tK
    /// <summary>
    /// Saturated liquid and vapour properties at one point of the saturation line, SI units.
    /// </summary>
    type SatProps = Water.SatProps
    /// <summary>Saturation properties at a pressure [Pa].</summary>
    let sat (pPa: float) : SatProps = Water.sat pPa
    /// <summary>Saturation properties at a saturation temperature [K].</summary>
    let satT (tK: float) : SatProps = Water.satT tK
    /// <summary>Saturation table from tMinC to tMaxC [°C] in steps of stepC, clipped to 0.02–370 °C.</summary>
    let saturationTable (tMinC: float) (tMaxC: float) (stepC: float) : SatProps list =
        Water.saturationTable tMinC tMaxC stepC
    /// <summary>The standard 20–310 °C, 10 °C-step saturation table used for checks and comparisons.</summary>
    let saturationTable20to310 () = saturationTable 20.0 310.0 10.0
    /// <summary>Region 1 tuple (v [m³/kg], h [kJ/kg], cp [kJ/(kg·K)], s [kJ/(kg·K)]) at pressure [MPa] and temperature [K].</summary>
    let region1 (pMPa: float) (tK: float) = Water.region1 pMPa tK
    /// <summary>Region 2 tuple (v [m³/kg], h [kJ/kg], cp [kJ/(kg·K)], s [kJ/(kg·K)]) at pressure [MPa] and temperature [K].</summary>
    let region2 (pMPa: float) (tK: float) = Water.region2 pMPa tK
    /// <summary>Compressed-liquid enthalpy [J/kg] at pressure [Pa] and temperature [K].</summary>
    let hLiquid (pPa: float) (tK: float) = Water.hLiquid pPa tK
    /// <summary>Compressed-liquid density [kg/m³] at pressure [Pa] and temperature [K].</summary>
    let rhoLiquid (pPa: float) (tK: float) = Water.rhoLiquid pPa tK
