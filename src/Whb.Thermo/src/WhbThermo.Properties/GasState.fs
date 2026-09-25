namespace WhbThermo.Properties

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// The operating point of a gas: one mixture at one temperature and pressure,
/// with every property the rating needs evaluated consistently from the same
/// species data.
///
/// Three things are kept apart on purpose: SpeciesData is the pure substance,
/// Mixture is the composition of a stream, and GasState is that composition at
/// (T, P). Mass flow is not here: a state is intensive, and the flow belongs to
/// the stream that carries it.
type GasState =
    { Temperature         : float<K>
      Pressure            : float<bar>
      MolarMass           : float<kg/kmol>
      /// Compressibility factor. Exactly 1 here: no equation of state is applied
      /// yet, so every property below is the ideal-gas value.
      Z                   : float
      Density             : float<kg/m^3>
      Cp                  : float<J/(kg*K)>
      Cv                  : float<J/(kg*K)>
      Gamma               : float
      /// Absolute specific enthalpy on the database reference state [J/kg]:
      /// formation enthalpies included, so differences across a reaction are right.
      Enthalpy            : float
      /// Absolute specific entropy [J/(kg*K)], including the ideal mixing term.
      Entropy             : float
      Viscosity           : float<Pa*s>
      ThermalConductivity : float<W/(m*K)>
      Prandtl             : float
      SpeedOfSound        : float<m/s> }

module GasState =

    /// Evaluates the ideal-gas state of a mixture at (T, P).
    ///
    /// Transport and cp come from Mixing.evaluate (Wilke / Wassiljewa); enthalpy
    /// and entropy from the NASA-9 fits through Equilibrium, so cp = dh/dT holds
    /// to the precision of the fits themselves. Warnings from every species
    /// evaluation are carried through.
    let evaluate (mixture: Mixture) (t: float<K>) (p: float<bar>) : Thermo<GasState> =
        if float p <= 0.0 then fail (InvalidMixture "pressure must be positive")
        else
        Mixing.evaluate mixture t p
        >>= fun props ->
            mixture.Components
            |> traverseList (fun (sp, y) ->
                Equilibrium.molarEnthalpy sp t
                >>= fun h -> Equilibrium.molarEntropy sp t >>= fun s -> ok (y, h, s))
            >>= fun terms ->
                let r = float Ru
                let mKgMol = float props.MolarMass / 1000.0
                let pRatio = float p / Equilibrium.StandardPressureBar
                let hMolar = terms |> List.sumBy (fun (y, h, _) -> y * h)
                // Ideal mixing: each component at its partial pressure y P.
                let sMolar =
                    terms
                    |> List.sumBy (fun (y, _, s) -> if y > 0.0 then y * (s - r * log (y * pRatio)) else 0.0)
                let cp = float props.SpecificHeat
                let cv = cp - r / mKgMol
                let gamma = cp / cv
                ok { Temperature = t
                     Pressure = p
                     MolarMass = props.MolarMass
                     Z = 1.0
                     Density = props.Density
                     Cp = props.SpecificHeat
                     Cv = cv * 1.0<J/(kg*K)>
                     Gamma = gamma
                     Enthalpy = hMolar / mKgMol
                     Entropy = sMolar / mKgMol
                     Viscosity = props.Viscosity
                     ThermalConductivity = props.ThermalConductivity
                     Prandtl = props.Prandtl
                     SpeedOfSound = sqrt (gamma * r * float t / mKgMol) * 1.0<m/s> }
