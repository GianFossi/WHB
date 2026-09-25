namespace WhbThermo.Domain

/// The physical constants of the library, and of Whb.Core, in one place.
///
/// Plain floats in SI units; Units.fs re-exports the ones that carry units of
/// measure. Whb.Core's Constants module aliases these, so a constant has exactly
/// one definition across the solution.
module PhysicalConstants =

    /// Molar gas constant [J/(mol K)], CODATA 2018 (exact).
    [<Literal>]
    let MolarGasConstant = 8.31446261815324

    /// Stefan-Boltzmann constant [W/(m^2 K^4)], CODATA 2018.
    [<Literal>]
    let StefanBoltzmann = 5.670374419e-8

    /// Standard acceleration of gravity [m/s^2], CGPM 1901 (exact).
    [<Literal>]
    let StandardGravity = 9.80665

    /// Critical point and gas constant of water, as fixed by IAPWS (R2-83 and
    /// IF97). iapws-if97.json and iapws-water-transport.json must carry the same
    /// values; their loaders refuse a file that does not.
    module Water =
        /// Critical temperature [K].
        [<Literal>]
        let CriticalTemperature = 647.096
        /// Critical pressure [Pa].
        [<Literal>]
        let CriticalPressure = 22.064e6
        /// Critical density [kg/m^3].
        [<Literal>]
        let CriticalDensity = 322.0
        /// Specific gas constant of IAPWS-IF97 [J/(kg K)].
        [<Literal>]
        let SpecificGasConstant = 461.526
