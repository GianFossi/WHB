namespace XSulfur

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Kinetics of the condensed sulfur film inside a horizontal tube.
///
/// Once the wall drops below the sulfur dew point a liquid film forms on it,
/// runs down the circumference under gravity, collects in the bottom of the tube
/// and is dragged along by the gas. Thermally the film is almost irrelevant - it
/// is thin and its resistance is small next to the gas-phase diffusion
/// resistance. Mechanically it decides whether the bundle stays open.
///
/// The governing quantity is the viscosity, and the viscosity is governed by the
/// lambda transition. At 155 degC the condensate is 7 mPa*s and drains like a
/// light oil. At 165 degC it is 4 Pa*s, and the same film is 570 times more
/// resistant to flow. Nothing else in the calculation moves that fast, which is
/// why the wall window is a hard constraint and not a target.
///
/// The models below are deliberately simple - a Nusselt film for the condensing
/// section, a rivulet balance for the bottom stream, and standard entrainment
/// criteria. They are here to answer "does it drain", not to resolve the
/// interface. Anything more detailed would need a two-fluid solver and would
/// still be governed by the same viscosity.
module FilmKinetics =

    let g = 9.80665

    // ---------- film on the tube wall ----------

    type FilmState =
        { Thickness        : float        // m
          Reynolds         : float
          MeanVelocity     : float        // m/s
          Viscosity        : float        // Pa*s
          Coefficient      : float<W/(m^2*K)>
          IsPolymerised    : bool }

    /// Nusselt condensate film on a horizontal tube, in the falling-film sense:
    ///   h = 0.729 [rho_l (rho_l - rho_v) g k^3 h_fg / (mu_l dT D)]^0.25
    ///
    /// `latentHeat` should be the sulfur heat of condensation INCLUDING the
    /// polymerisation contribution; `Speciation.effectiveHeatCapacity` and the
    /// condensation curve both carry it, and using a bare S8 latent heat here
    /// understates the film loading.
    let nusseltHorizontalFilm (tWall: float<K>) (tSaturation: float<K>)
                              (vapourDensity: float) (latentHeat: float)
                              (diameter: float) : Thermo<FilmState> =
        let deltaT = float tSaturation - float tWall
        if deltaT <= 0.0 then
            fail (InvalidMixture "the wall must be colder than the saturation temperature")
        elif diameter <= 0.0 then
            fail (InvalidMixture "the tube diameter must be positive")
        else
            // Film properties at the mean film temperature.
            let tFilm = (tWall + tSaturation) / 2.0
            LiquidProperties.viscosity tFilm
            >>= fun mu ->
                LiquidProperties.density tFilm
                >>= fun rho ->
                    LiquidProperties.conductivity tFilm
                    >>= fun k ->
                        let h =
                            0.729 * ((rho * (rho - vapourDensity) * g * (k ** 3.0)
                                      * latentHeat) / (mu * deltaT * diameter)) ** 0.25
                        // Film loading from the heat removed, per unit length.
                        let gamma = h * deltaT * Math.PI * diameter / latentHeat / 2.0
                        let reynolds = 4.0 * gamma / mu
                        let thickness = ((3.0 * mu * gamma) / (rho * rho * g)) ** (1.0 / 3.0)
                        ok { Thickness = thickness
                             Reynolds = reynolds
                             MeanVelocity = if thickness > 0.0 then gamma / (rho * thickness) else 0.0
                             Viscosity = mu
                             Coefficient = h * 1.0<W/(m^2*K)>
                             IsPolymerised =
                                float tFilm - 273.15 > Chemistry.LambdaTransitionC }
                        |> warnIf (float tFilm - 273.15 > Chemistry.LambdaTransitionC)
                                  (CorrelationExtrapolated
                                    ("sulfur film",
                                     $"the mean film temperature is %.1f{float tFilm - 273.15} degC, "
                                     + "above the lambda transition: the Nusselt analysis assumes a "
                                     + "Newtonian film and polymerised sulfur is not"))
                        |> warnIf (reynolds > 30.0)
                                  (CorrelationExtrapolated
                                    ("sulfur film",
                                     $"film Reynolds %.0f{reynolds} is above the laminar Nusselt "
                                     + "range; waves increase the coefficient"))

    // ---------- rivulet in the bottom of the tube ----------

    type DrainageState =
        { /// Depth of the bottom stream [m].
          Depth            : float
          /// Fraction of the tube cross-section occupied by liquid.
          HoldUp           : float
          /// Mean liquid velocity along the tube [m/s].
          Velocity         : float
          /// Time for the condensate to traverse the tube [s].
          ResidenceTime    : float
          /// Viscous drainage against gas drag: below one, gravity governs.
          GravityDominance : float
          Drains           : bool }

    /// Drainage of the condensate collected in the bottom of a horizontal tube.
    ///
    /// The stream is treated as a shallow layer driven by the tube slope and
    /// sheared by the gas above it. For a laminar layer of depth d on a slope s
    /// the gravity-driven mean velocity is
    ///
    ///     u_gravity = rho g sin(s) d^2 / (3 mu)
    ///
    /// which goes as d^2 / mu. Both dependencies matter: doubling the viscosity
    /// halves the velocity, so the layer deepens until it can carry the same
    /// flow - and past the lambda transition the required depth grows faster
    /// than the tube can accommodate.
    let drainage (condensateFlow: float) (diameter: float) (length: float)
                 (slope: float) (gasVelocity: float) (gasDensity: float)
                 (t: float<K>) : Thermo<DrainageState> =
        if condensateFlow < 0.0 then
            fail (InvalidMixture "the condensate flow cannot be negative")
        elif diameter <= 0.0 || length <= 0.0 then
            fail (InvalidMixture "the tube geometry must be positive")
        elif slope < 0.0 then
            fail (InvalidMixture "the slope cannot be negative")
        else
            LiquidProperties.viscosity t
            >>= fun mu ->
                LiquidProperties.density t
                >>= fun rho ->
                    // Solve for the depth that carries the given flow. The width
                    // of a shallow chord is approximated as 2 sqrt(D d), which
                    // holds while d is small against the diameter - the regime a
                    // working condenser must stay in.
                    let flowAtDepth d =
                        let width = 2.0 * sqrt (max (diameter * d) 1e-12)
                        let area = 2.0 / 3.0 * width * d
                        let velocity = rho * g * (sin slope) * d * d / (3.0 * mu)
                        rho * area * velocity

                    let rec bisect lo hi n =
                        if n = 0 then (lo + hi) / 2.0
                        else
                            let mid = (lo + hi) / 2.0
                            if flowAtDepth mid < condensateFlow then bisect mid hi (n - 1)
                            else bisect lo mid (n - 1)

                    let depth =
                        if condensateFlow <= 0.0 || slope <= 0.0 then 0.0
                        else bisect 1e-9 (diameter * 0.5) 200

                    let width = 2.0 * sqrt (max (diameter * depth) 1e-12)
                    let area = 2.0 / 3.0 * width * depth
                    let tubeArea = Math.PI * diameter * diameter / 4.0
                    let holdUp = area / tubeArea
                    let velocity =
                        if area > 0.0 then condensateFlow / (rho * area) else 0.0

                    // Gas shear on the interface against the gravity driver.
                    let shear = 0.005 * gasDensity * gasVelocity * gasVelocity
                    let gravityDriver = rho * g * (sin slope) * max depth 1e-9
                    let dominance = if gravityDriver > 0.0 then shear / gravityDriver else infinity

                    ok { Depth = depth
                         HoldUp = holdUp
                         Velocity = velocity
                         ResidenceTime = if velocity > 0.0 then length / velocity else infinity
                         GravityDominance = dominance
                         Drains = depth < diameter * 0.25 && holdUp < 0.15 }
                    |> warnIf (holdUp > 0.15)
                              (CorrelationExtrapolated
                                ("sulfur drainage",
                                 $"liquid hold-up is %.1f{holdUp * 100.0} %% of the tube "
                                 + "cross-section: the bottom stream is restricting the gas "
                                 + "passage and the shallow-layer treatment no longer holds"))
                    |> warnIf (dominance > 1.0)
                              (CorrelationExtrapolated
                                ("sulfur drainage",
                                 $"gas shear exceeds the gravity driver (ratio %.2f{dominance}): "
                                 + "the condensate is being carried by the gas rather than "
                                 + "draining, so it will leave with the gas or pool at the outlet"))

    // ---------- entrainment ----------

    /// Criterion for re-entrainment of the bottom stream by the gas.
    ///
    /// Uses the standard inverse-viscosity group: entrainment begins when the
    /// gas Weber number based on the film exceeds a threshold that itself
    /// depends on the liquid viscosity number. High-viscosity liquids resist
    /// entrainment, so paradoxically polymerised sulfur is HARDER to entrain -
    /// which is not good news, because it means it stays in the tube.
    type EntrainmentState =
        { WeberNumber     : float
          ViscosityNumber : float
          Threshold       : float
          IsEntraining    : bool }

    let entrainment (gasVelocity: float) (gasDensity: float) (filmThickness: float)
                    (t: float<K>) : Thermo<EntrainmentState> =
        if gasVelocity < 0.0 || gasDensity <= 0.0 then
            fail (InvalidMixture "the gas state must be physical")
        else
            LiquidProperties.viscosity t
            >>= fun mu ->
                LiquidProperties.density t
                >>= fun rho ->
                    LiquidProperties.surfaceTension t
                    >>= fun sigma ->
                        let weber =
                            gasDensity * gasVelocity * gasVelocity * filmThickness / sigma
                        let viscosityNumber =
                            mu / sqrt (rho * sigma * sqrt (sigma / (g * (rho - gasDensity))))
                        // Ishii & Grolmes: the threshold rises with the viscosity number.
                        let threshold =
                            if viscosityNumber <= 1.0 / 15.0 then 11.78 * (viscosityNumber ** 0.8)
                            else 1.35
                        ok { WeberNumber = weber
                             ViscosityNumber = viscosityNumber
                             Threshold = threshold
                             IsEntraining = weber > threshold }

    // ---------- combined assessment ----------

    type DrainageVerdict =
        | DrainsFreely
        | DrainsMarginally of reason: string
        | DoesNotDrain of reason: string

    /// Single verdict on whether the condensate leaves the tube.
    ///
    /// The order of the checks is deliberate: the lambda transition is tested
    /// first because it dominates everything downstream of it. A film past the
    /// transition does not drain regardless of slope, flow or geometry, and
    /// reporting a hold-up figure for it would suggest the problem is
    /// dimensional when it is not.
    let assess (film: FilmState) (drainageState: DrainageState) (tFilm: float<K>)
               : Thermo<DrainageVerdict> =
        let tC = float tFilm - 273.15
        // A frozen film is decided before any liquid property is needed: the
        // liquid viscosity has no meaning below the melting point.
        if tC < Chemistry.MeltingPointRhombicC then
            ok (DoesNotDrain
                    (sprintf
                        "film at %.1f degC is below the melting point: the sulfur is solid and \
                         will encrust the wall" tC))
        else
        LiquidProperties.viscosityPenalty tFilm
        >>= fun penalty ->
            if tC > Chemistry.LambdaTransitionC then
                ok (DoesNotDrain
                        (sprintf
                            "film at %.1f degC is past the lambda transition: viscosity is %.0f times \
                             its value at 155 degC, the condensate will not drain at any slope"
                            tC penalty))
            elif tC > Chemistry.LambdaTransitionC - 4.0 then
                ok (DrainsMarginally
                        (sprintf
                            "film at %.1f degC leaves under 4 K of margin on the lambda transition; \
                             a load transient or a control excursion crosses it" tC))
            elif not drainageState.Drains then
                ok (DoesNotDrain
                        (sprintf "hold-up is %.1f %% of the tube section and the stream is not clearing"
                                 (drainageState.HoldUp * 100.0)))
            elif drainageState.GravityDominance > 1.0 then
                ok (DrainsMarginally
                        (sprintf "gas shear exceeds gravity (ratio %.2f): the condensate is carried \
                                  rather than drained" drainageState.GravityDominance))
            else
                ok DrainsFreely
