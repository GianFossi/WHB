namespace WhbThermo.Convection

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// In-tube forced convection for the process gas side.
///
/// Gnielinski is the default. Dittus-Boelter is kept because it appears in
/// every specification written before about 1980 and a rating sometimes has to
/// be reproduced in the client's terms, but it should not be the basis of new
/// work: it carries roughly +/- 25 % scatter against +/- 10 % for Gnielinski,
/// and it degrades badly at the large wall-to-bulk temperature ratios a waste
/// heat boiler runs at.
///
/// The wall-to-bulk temperature ratio is the point worth understanding here. At
/// an SRU inlet the gas is near 1400 degC and the wall near 350 degC, a ratio of
/// 2.7, and every correlation below is fitted on near-isothermal data.
///
/// The correction for that is smaller and more subtle than it first appears.
/// A naive (T_bulk/T_wall)^0.45 gives a factor of 1.56 at SRU conditions - a
/// 56 % ENHANCEMENT, in the non-conservative direction, which is not credible.
/// Kays (*Convective Heat and Mass Transfer*) gives Nu/Nu_isothermal =
/// (T_wall/T_bulk)^n with n = -0.5 for gas HEATING and n approximately 0 for
/// gas COOLING. A waste heat boiler cools the gas, so the temperature-ratio
/// correction is close to unity and the aggressive exponents quoted for heating
/// must not be carried over.
///
/// The defensible choices for gas cooling are therefore: the Sieder-Tate
/// viscosity ratio, which is mild (about +10 % here) and well established, or
/// simply evaluating the properties at the film temperature (T_bulk + T_wall)/2
/// and applying no ratio at all. `filmTemperature` is provided for the latter.
module InTube =

    // ---------- friction ----------

    /// Petukhov smooth-tube friction factor:
    ///   f = (0.790 ln(Re) - 1.64)^-2      3000 <= Re <= 5e6
    let petukhovFriction (re: float) : Thermo<float> =
        if re <= 0.0 then
            fail (InvalidMixture "Reynolds number must be positive")
        else
            let f = (0.790 * log re - 1.64) ** -2.0
            ok f
            |> warnIf (re < 3000.0 || re > 5.0e6)
                      (CorrelationExtrapolated
                        ("Petukhov friction", $"Re = %.0f{re} outside 3000-5e6"))

    /// Darcy friction factor including the laminar branch, so a solver marching
    /// into a low-flow region gets a continuous answer instead of nonsense.
    let frictionFactor (re: float) : Thermo<float> =
        if re <= 0.0 then
            fail (InvalidMixture "Reynolds number must be positive")
        elif re < 2300.0 then
            ok (64.0 / re)
        else
            petukhovFriction re

    // ---------- property variation ----------

    /// Whether the gas is being cooled or heated. It matters: the property
    /// correction is near unity for cooling and significant for heating, and
    /// applying a heating exponent to a cooling duty inflates the film
    /// coefficient in the non-conservative direction.
    type ThermalDirection =
        | GasCooled     // a waste heat boiler
        | GasHeated     // a fired heater or reformer tube

    /// Film temperature, the arithmetic mean of bulk and wall. Evaluating the
    /// gas properties here and applying no ratio correction is the cleanest
    /// treatment of strong property variation.
    let filmTemperature (tBulk: float<K>) (tWall: float<K>) : float<K> =
        (tBulk + tWall) / 2.0

    /// How the correlation is corrected for the difference between bulk and
    /// wall properties.
    type PropertyCorrection =
        /// No correction. Correct when the properties were already evaluated at
        /// the film temperature, and honest when wall and bulk are within ~50 K.
        | None
        /// Sieder-Tate viscosity ratio, (mu_bulk/mu_wall)^0.14. Mild, well
        /// established, and the safe default for a gas cooler.
        | ViscosityRatio of muBulk: float * muWall: float
        /// Kays temperature ratio: (T_wall/T_bulk)^n, n = -0.5 for gas heating
        /// and 0 for gas cooling. Selecting `GasCooled` deliberately yields 1.0.
        | KaysTemperatureRatio of tBulk: float<K> * tWall: float<K> * direction: ThermalDirection
        /// An explicit exponent on (T_wall/T_bulk), for a house correlation with
        /// its own documented basis.
        | CustomTemperatureRatio of tBulk: float<K> * tWall: float<K> * exponent: float

        member this.Factor =
            match this with
            | None -> 1.0
            | ViscosityRatio (bulk, wall) when wall > 0.0 -> (bulk / wall) ** 0.14
            | ViscosityRatio _ -> 1.0
            | KaysTemperatureRatio (_, _, GasCooled) -> 1.0
            | KaysTemperatureRatio (bulk, wall, GasHeated) when float bulk > 0.0 ->
                (float wall / float bulk) ** -0.5
            | KaysTemperatureRatio _ -> 1.0
            | CustomTemperatureRatio (bulk, wall, n) when float bulk > 0.0 ->
                (float wall / float bulk) ** n
            | CustomTemperatureRatio _ -> 1.0

        /// True when the correction increases the film coefficient. Worth
        /// surfacing: an enhancement is by definition not conservative on duty.
        member this.IsEnhancement = this.Factor > 1.0

    // ---------- Nusselt correlations ----------

    /// Fully developed laminar flow. 3.66 for a uniform wall temperature,
    /// 4.36 for a uniform heat flux. A WHB tube is closer to uniform wall
    /// temperature because the shell side is boiling at constant saturation.
    let laminarNusselt (uniformWallTemperature: bool) =
        if uniformWallTemperature then 3.66 else 4.36

    /// Gnielinski correlation:
    ///   Nu = (f/8)(Re - 1000) Pr / [1 + 12.7 (f/8)^0.5 (Pr^(2/3) - 1)]
    ///
    /// `lengthOverDiameter` applies Gnielinski's entrance-length term
    /// [1 + (D/L)^(2/3)]. Pass None for a fully developed assumption. In a WHB
    /// with inlet ferrules the thermal boundary layer restarts at every ferrule
    /// exit, so the entrance term is usually the physically right choice.
    let gnielinski (re: float) (pr: float) (lengthOverDiameter: float option)
                   (correction: PropertyCorrection) : Thermo<float> =
        if re <= 0.0 then fail (InvalidMixture "Reynolds number must be positive")
        elif pr <= 0.0 then fail (InvalidMixture "Prandtl number must be positive")
        else
            frictionFactor re
            >>= fun f ->
                let fOver8 = f / 8.0
                let numerator = fOver8 * (re - 1000.0) * pr
                let denominator = 1.0 + 12.7 * sqrt fOver8 * (pr ** (2.0 / 3.0) - 1.0)
                let entrance =
                    match lengthOverDiameter with
                    | Some ratio when ratio > 0.0 -> 1.0 + (1.0 / ratio) ** (2.0 / 3.0)
                    | _ -> 1.0
                let nu = numerator / denominator * entrance * correction.Factor

                ok (max nu 0.0)
                |> warnIf (re < 3000.0)
                          (CorrelationExtrapolated
                            ("Gnielinski", $"Re = %.0f{re} is below 3000; the correlation is "
                                           + "being used in the transition region"))
                |> warnIf (re > 5.0e6)
                          (CorrelationExtrapolated ("Gnielinski", $"Re = %.0f{re} above 5e6"))
                |> warnIf (pr < 0.5 || pr > 2000.0)
                          (CorrelationExtrapolated
                            ("Gnielinski", $"Pr = %.3f{pr} outside the correlated 0.5-2000"))
                |> warnIf (correction.Factor > 1.15)
                          (CorrelationExtrapolated
                            ("Gnielinski",
                             $"property correction is %.2f{correction.Factor}, a large "
                             + "ENHANCEMENT and therefore non-conservative on duty; "
                             + "check the exponent suits gas cooling"))

    /// Dittus-Boelter, cooling form: Nu = 0.023 Re^0.8 Pr^0.3.
    /// Retained for reproducing legacy specifications, not for new work.
    let dittusBoelter (re: float) (pr: float) : Thermo<float> =
        if re <= 0.0 || pr <= 0.0 then
            fail (InvalidMixture "Reynolds and Prandtl numbers must be positive")
        else
            ok (0.023 * (re ** 0.8) * (pr ** 0.3))
            |> warnIf (re < 10000.0)
                      (CorrelationExtrapolated
                        ("Dittus-Boelter", $"Re = %.0f{re} below the correlated 10000"))
            |> warn (CorrelationExtrapolated
                        ("Dittus-Boelter",
                         "scatter is about +/- 25 %; prefer Gnielinski unless a "
                         + "specification requires this correlation"))

    /// Sieder-Tate: Nu = 0.027 Re^0.8 Pr^(1/3) (mu_bulk/mu_wall)^0.14.
    let siederTate (re: float) (pr: float) (muBulk: float) (muWall: float) : Thermo<float> =
        if re <= 0.0 || pr <= 0.0 then
            fail (InvalidMixture "Reynolds and Prandtl numbers must be positive")
        elif muWall <= 0.0 then
            fail (InvalidMixture "wall viscosity must be positive")
        else
            ok (0.027 * (re ** 0.8) * (pr ** (1.0 / 3.0)) * ((muBulk / muWall) ** 0.14))
            |> warnIf (re < 10000.0)
                      (CorrelationExtrapolated
                        ("Sieder-Tate", $"Re = %.0f{re} below the correlated 10000"))

    // ---------- regime selection ----------

    type FlowRegime =
        | Laminar
        | Transitional
        | Turbulent

        static member OfReynolds re =
            if re < 2300.0 then Laminar
            elif re < 3000.0 then Transitional
            else Turbulent

    type ConvectionResult =
        { Reynolds     : float
          Prandtl      : float
          Nusselt      : float
          Regime       : FlowRegime
          Friction     : float
          Coefficient  : float<W/(m^2*K)> }

    /// Reynolds number from mass velocity, which is what a WHB rating actually
    /// carries: G = m_dot / A_flow, so Re = G D / mu and the density cancels.
    /// Working from G rather than velocity avoids a needless density round trip
    /// and the error that comes with it.
    let reynoldsFromMassVelocity (massVelocity: float<kg/(m^2*s)>) (dInt: float<m>)
                                 (viscosity: float<Pa*s>) : Thermo<float> =
        if float viscosity <= 0.0 then
            fail (InvalidMixture "viscosity must be positive")
        elif float dInt <= 0.0 then
            fail (InvalidMixture "tube diameter must be positive")
        else
            ok (float massVelocity * float dInt / float viscosity)

    /// Full in-tube convection evaluation with automatic regime handling.
    ///
    /// In the transition band (2300 < Re < 3000) the result is interpolated
    /// between the laminar value and Gnielinski at Re = 3000, and warned about.
    /// Interpolating is not accurate -- nothing is, in that band -- but it keeps
    /// a marching solver continuous, which a hard switch does not.
    let evaluate (re: float) (pr: float) (conductivity: float<W/(m*K)>) (dInt: float<m>)
                 (lengthOverDiameter: float option) (correction: PropertyCorrection)
                 : Thermo<ConvectionResult> =
        if float dInt <= 0.0 then fail (InvalidMixture "tube diameter must be positive")
        elif float conductivity <= 0.0 then fail (InvalidMixture "conductivity must be positive")
        else
            let regime = FlowRegime.OfReynolds re

            let nusselt =
                match regime with
                | Laminar ->
                    ok (laminarNusselt true * correction.Factor)
                    |> warn (CorrelationExtrapolated
                                ("in-tube convection",
                                 $"Re = %.0f{re} is laminar; check for flow maldistribution, "
                                 + "a WHB tube in laminar flow is usually a symptom"))
                | Transitional ->
                    gnielinski 3000.0 pr lengthOverDiameter correction
                    >>= fun turbulent ->
                        let laminar = laminarNusselt true * correction.Factor
                        let blend = (re - 2300.0) / (3000.0 - 2300.0)
                        ok (laminar + blend * (turbulent - laminar))
                        |> warn (CorrelationExtrapolated
                                    ("in-tube convection",
                                     $"Re = %.0f{re} is in the transition band; the result is "
                                     + "interpolated and no correlation is reliable here"))
                | Turbulent ->
                    gnielinski re pr lengthOverDiameter correction

            nusselt
            >>= fun nu ->
                frictionFactor re
                >>= fun f ->
                    ok { Reynolds = re
                         Prandtl = pr
                         Nusselt = nu
                         Regime = regime
                         Friction = f
                         Coefficient = nu * float conductivity / float dInt * 1.0<W/(m^2*K)> }

    // ---------- pressure drop ----------

    /// Straight-tube frictional pressure drop, Darcy-Weisbach on a mass basis:
    ///   dP = f (L/D) G^2 / (2 rho)
    let frictionalPressureDrop (friction: float) (length: float<m>) (dInt: float<m>)
                               (massVelocity: float<kg/(m^2*s)>) (density: float<kg/m^3>)
                               : Thermo<float<Pa>> =
        if float density <= 0.0 then
            fail (InvalidMixture "density must be positive")
        elif float dInt <= 0.0 then
            fail (InvalidMixture "tube diameter must be positive")
        else
            let g = float massVelocity
            ok (friction * (float length / float dInt) * g * g / (2.0 * float density) * 1.0<Pa>)
