namespace WhbThermo.Convection

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Natural and mixed convection.
///
/// Why a waste heat boiler needs this at all. At design flow the tube side is
/// firmly forced-convective and buoyancy is irrelevant. At turndown it is not:
/// the Richardson number Gr/Re^2 scales as 1/Re^2 at roughly fixed Gr, so
/// halving the throughput quadruples it. A tube that sits at Gr/Re^2 = 0.02 at
/// full load reaches 0.32 at 25 % load, which is squarely in the mixed regime
/// where a pure forced-convection correlation is no longer defensible.
///
/// The same applies to a tube that has partially blocked, to standby and
/// hold-warm conditions, and to casing and insulation heat loss, which is pure
/// natural convection.
///
/// Direction matters and is easy to get wrong. In a vertical tube with gas being
/// cooled, the gas near the wall is denser than the core, so buoyancy drives it
/// DOWNWARD. That opposes upward flow and assists downward flow, and opposed
/// mixed convection can give a lower coefficient than either mechanism alone --
/// combining them as if they always help is optimistic in exactly the case that
/// matters.
module Buoyancy =

    let g = PhysicalConstants.StandardGravity * 1.0<m/s^2>

    // ---------- dimensionless groups ----------

    /// Volumetric thermal expansion coefficient. For an ideal gas beta = 1/T,
    /// which is what the process gas side uses; a liquid needs a measured value.
    let idealGasExpansion (t: float<K>) : float =
        1.0 / float t

    /// Grashof number:
    ///   Gr = g beta (T_wall - T_bulk) L^3 rho^2 / mu^2
    ///
    /// The sign of the temperature difference is kept: a cooled wall gives a
    /// negative Grashof, and the magnitude is used for the correlations while
    /// the sign carries the buoyancy direction.
    let grashof (beta: float) (tWall: float<K>) (tBulk: float<K>)
                (characteristicLength: float<m>) (density: float<kg/m^3>)
                (viscosity: float<Pa*s>) : Thermo<float> =
        if float viscosity <= 0.0 then
            fail (InvalidMixture "viscosity must be positive")
        elif float density <= 0.0 then
            fail (InvalidMixture "density must be positive")
        elif float characteristicLength <= 0.0 then
            fail (InvalidMixture "characteristic length must be positive")
        else
            let dt = float tWall - float tBulk
            let l = float characteristicLength
            ok (float g * beta * dt * l * l * l
                * (float density ** 2.0) / (float viscosity ** 2.0))

    /// Rayleigh number, Ra = Gr Pr.
    let rayleigh (gr: float) (pr: float) = gr * pr

    /// Richardson number Gr/Re^2, the ratio of buoyancy to inertia forces.
    let richardson (gr: float) (re: float) : Thermo<float> =
        if re = 0.0 then fail (InvalidMixture "Reynolds number must be non-zero")
        else ok (gr / (re * re))

    /// Which mechanism governs.
    ///
    /// The conventional thresholds are Gr/Re^2 below 0.1 for forced convection
    /// and above 10 for natural, with the two decades between them mixed. Those
    /// boundaries are soft: they are where the neglected mechanism contributes
    /// roughly 10 % to a cubic combination, not a physical transition.
    type Regime =
        | ForcedDominated
        | Mixed
        | NaturalDominated

        static member OfRichardson(ri: float) =
            let magnitude = abs ri
            if magnitude < 0.1 then ForcedDominated
            elif magnitude > 10.0 then NaturalDominated
            else Mixed

    // ---------- Churchill & Chu ----------

    /// Churchill & Chu, vertical plate or vertical cylinder, valid over the
    /// whole Rayleigh range including transition:
    ///   Nu = { 0.825 + 0.387 Ra^(1/6) / [1 + (0.492/Pr)^(9/16)]^(8/27) }^2
    let churchillChuVertical (ra: float) (pr: float) : Thermo<float> =
        if pr <= 0.0 then fail (InvalidMixture "Prandtl number must be positive")
        elif ra < 0.0 then fail (InvalidMixture "Rayleigh number must be non-negative")
        elif ra = 0.0 then ok 0.0
        else
            let denominator = (1.0 + (0.492 / pr) ** (9.0 / 16.0)) ** (8.0 / 27.0)
            let nu = (0.825 + 0.387 * (ra ** (1.0 / 6.0)) / denominator) ** 2.0
            ok nu
            |> warnIf (ra > 1.0e12)
                      (CorrelationExtrapolated
                        ("Churchill-Chu vertical", $"Ra = %.2e{ra} above 1e12"))

    /// Churchill & Chu laminar form, more accurate below Ra = 1e9:
    ///   Nu = 0.68 + 0.670 Ra^(1/4) / [1 + (0.492/Pr)^(9/16)]^(4/9)
    let churchillChuVerticalLaminar (ra: float) (pr: float) : Thermo<float> =
        if pr <= 0.0 then fail (InvalidMixture "Prandtl number must be positive")
        elif ra < 0.0 then fail (InvalidMixture "Rayleigh number must be non-negative")
        else
            let denominator = (1.0 + (0.492 / pr) ** (9.0 / 16.0)) ** (4.0 / 9.0)
            ok (0.68 + 0.670 * (ra ** 0.25) / denominator)
            |> warnIf (ra > 1.0e9)
                      (CorrelationExtrapolated
                        ("Churchill-Chu laminar",
                         $"Ra = %.2e{ra} above 1e9; use the all-range form"))

    /// Churchill & Chu, horizontal cylinder, valid to Ra = 1e12:
    ///   Nu = { 0.60 + 0.387 Ra^(1/6) / [1 + (0.559/Pr)^(9/16)]^(8/27) }^2
    ///
    /// The characteristic length is the cylinder DIAMETER, not its length.
    let churchillChuHorizontalCylinder (ra: float) (pr: float) : Thermo<float> =
        if pr <= 0.0 then fail (InvalidMixture "Prandtl number must be positive")
        elif ra < 0.0 then fail (InvalidMixture "Rayleigh number must be non-negative")
        elif ra = 0.0 then ok 0.0
        else
            let denominator = (1.0 + (0.559 / pr) ** (9.0 / 16.0)) ** (8.0 / 27.0)
            ok ((0.60 + 0.387 * (ra ** (1.0 / 6.0)) / denominator) ** 2.0)
            |> warnIf (ra > 1.0e12)
                      (CorrelationExtrapolated
                        ("Churchill-Chu cylinder", $"Ra = %.2e{ra} above 1e12"))

    /// A vertical cylinder may be treated as a vertical plate only when it is
    /// thick enough that the boundary layer stays thin relative to its radius:
    ///   D/L >= 35 / Gr^(1/4)
    /// Otherwise curvature enhances the transfer and the plate result is
    /// conservative. This returns whether the plate treatment is admissible.
    let verticalPlateApproximationValid (diameter: float<m>) (length: float<m>)
                                        (gr: float) : bool =
        if gr <= 0.0 || float length <= 0.0 then false
        else float diameter / float length >= 35.0 / (gr ** 0.25)

    // ---------- mixed convection ----------

    /// Whether buoyancy helps or hinders the forced flow.
    ///
    /// In a VERTICAL tube: gas cooled at the wall becomes denser and sinks. With
    /// upward flow that is opposing; with downward flow, assisting. Heated gas
    /// reverses both.
    type BuoyancyDirection =
        | Assisting
        | Opposing
        /// Horizontal tube: buoyancy acts across the flow, producing secondary
        /// circulation rather than adding to or subtracting from it.
        | Transverse

    /// Chen / Churchill asymptotic combination of forced and natural convection:
    ///   Nu^n = Nu_forced^n +/- Nu_natural^n,   n = 3
    ///
    /// The exponent 3 is Churchill's recommendation for external and internal
    /// flows alike. The sign is what matters: for OPPOSING flow the natural
    /// contribution is subtracted, and the combined coefficient can fall BELOW
    /// the forced-convection value. That is real -- opposed buoyancy thickens
    /// the boundary layer and can even reverse the near-wall flow -- and it is
    /// the case a WHB at turndown is most likely to be in.
    ///
    /// Transverse buoyancy is combined additively, since secondary circulation
    /// enhances mixing regardless of sign.
    let combine (forced: float) (natural: float) (direction: BuoyancyDirection)
                : Thermo<float> =
        if forced < 0.0 || natural < 0.0 then
            fail (InvalidMixture "Nusselt numbers must be non-negative")
        else
            let n = 3.0
            match direction with
            | Assisting | Transverse ->
                ok ((forced ** n + natural ** n) ** (1.0 / n))
            | Opposing ->
                let cubed = forced ** n - natural ** n
                if cubed <= 0.0 then
                    fail (CorrelationExtrapolated
                            ("mixed convection",
                             $"opposing buoyancy (Nu_nat %.1f{natural}) exceeds forced "
                             + $"convection (Nu_for %.1f{forced}); the near-wall flow is "
                             + "reversing and no correlation here is valid"))
                else
                    ok (cubed ** (1.0 / n))
                    |> warnIf (natural / forced > 0.5)
                              (CorrelationExtrapolated
                                ("mixed convection",
                                 $"opposing buoyancy is %.0f{natural / forced * 100.0} %% of "
                                 + "the forced contribution; the combined coefficient is "
                                 + "being reduced and the result is uncertain"))

    type MixedResult =
        { Grashof     : float
          Rayleigh    : float
          Richardson  : float
          Regime      : Regime
          NusseltForced  : float
          NusseltNatural : float
          Nusselt     : float
          Coefficient : float<W/(m^2*K)> }

    /// Full mixed-convection evaluation for a tube.
    ///
    /// `forcedNusselt` comes from `InTube.evaluate`; this decides whether that
    /// is sufficient on its own, and combines it with the buoyancy contribution
    /// when it is not.
    let evaluate (forcedNusselt: float) (re: float) (pr: float) (gr: float)
                 (direction: BuoyancyDirection)
                 (conductivity: float<W/(m*K)>) (dInt: float<m>)
                 : Thermo<MixedResult> =
        if float dInt <= 0.0 then fail (InvalidMixture "tube diameter must be positive")
        elif float conductivity <= 0.0 then fail (InvalidMixture "conductivity must be positive")
        else
            richardson gr re
            >>= fun ri ->
                let regime = Regime.OfRichardson ri
                let ra = rayleigh (abs gr) pr

                let natural =
                    match regime with
                    | ForcedDominated -> ok 0.0
                    | _ -> churchillChuVertical ra pr

                natural
                >>= fun nuNatural ->
                    let combined =
                        match regime with
                        | ForcedDominated ->
                            ok forcedNusselt
                            |> warnIf (abs ri > 0.05)
                                      (CorrelationExtrapolated
                                        ("mixed convection",
                                         $"Gr/Re^2 = %.3f{ri} is approaching the mixed regime; "
                                         + "a further reduction in flow will invalidate the "
                                         + "forced-convection result"))
                        | _ ->
                            combine forcedNusselt nuNatural direction
                            |> warn (CorrelationExtrapolated
                                        ("mixed convection",
                                         $"Gr/Re^2 = %.2f{ri}: buoyancy is significant and the "
                                         + "forced-convection correlation alone is not valid"))

                    combined
                    >>= fun nu ->
                        ok { Grashof = gr
                             Rayleigh = ra
                             Richardson = ri
                             Regime = regime
                             NusseltForced = forcedNusselt
                             NusseltNatural = nuNatural
                             Nusselt = nu
                             Coefficient = nu * float conductivity / float dInt * 1.0<W/(m^2*K)> }
