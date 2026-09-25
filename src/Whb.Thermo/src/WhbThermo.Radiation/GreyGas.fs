namespace WhbThermo.Radiation

open System

/// Single-grey-gas radiation for H2O + CO2 flue and process gases, as Whb.Core
/// has used it for its rating: an absorption coefficient of the form of the
/// boiler normative method,
///
///     k_g = ((0.78 + 1.6 r_H2O) / sqrt(p_n s) - 0.1) (1 - 0.37 T / 1000)   [1/(MPa m)]
///     eps = 1 - exp(-k_g p_n s),  clipped to [0, 0.95]
///
/// with p_n the partial pressure of H2O + CO2 [MPa] and s the mean beam length
/// [m]. It is cruder than the WSGG model (Wsgg.fs) and is kept because the WHB
/// reference results are built on it; switching a rating to WSGG is a model
/// change, not a refactor.
module GreyGas =

    /// Grey-gas emissivity of an H2O + CO2 mixture.
    let emissivity (rH2O: float) (rCO2: float) (pPa: float) (sBeam: float) (tK: float) =
        let rn = rH2O + rCO2
        if rn <= 1e-6 || sBeam <= 0.0 then 0.0
        else
            let pnMPa = pPa * rn / 1.0e6
            let ps = max 1e-6 (pnMPa * sBeam)
            let kg =
                ((0.78 + 1.6 * rH2O) / sqrt ps - 0.1) * (1.0 - 0.37 * tK / 1000.0)
            let kg = max 0.0 kg
            let e = 1.0 - exp (-kg * ps)
            min 0.95 (max 0.0 e)

    /// Linearised radiative coefficient [W/(m^2 K)] between the gas and a grey
    /// tube wall seen from inside a cavity: effective wall factor (eps_w + 1)/2.
    /// Returns 0 when the two temperatures coincide.
    let cavityCoefficient (epsGas: float) (epsWall: float) (tGasK: float) (tWallK: float) =
        if abs (tGasK - tWallK) < 1e-6 then 0.0
        else
            let effWall = 0.5 * (epsWall + 1.0)      // grey wall in a cavity
            let e = epsGas * effWall
            e * float Emissivity.sigma * (tGasK ** 4.0 - tWallK ** 4.0) / (tGasK - tWallK)
