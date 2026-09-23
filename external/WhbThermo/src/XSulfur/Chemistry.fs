namespace XSulfur

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Vapour-liquid chemistry of elemental sulfur.
///
/// Everything here derives from the same CEA Gibbs energies used by
/// `Speciation`, through the equilibrium
///
///     n S(L)  ->  S_n(g)      ln p_n = -(g_n - n g_L) / RT
///
/// so the vapour pressure, the speciation and the latent heats are consistent
/// with one another by construction rather than by three separate correlations
/// that happen to be tabulated together.
///
/// HONEST LIMIT. At the normal boiling point (444.6 degC) this gives 0.68 bar
/// against the 1.013 expected, so it must not be used above about 350 degC.
/// Inside the condenser window it behaves: 5.6 Pa at 120 degC, 32 Pa at
/// 150 degC, 6.07 kPa at 300 degC. Calls outside 120-350 degC fail rather than
/// extrapolating quietly.
module Chemistry =

    [<Literal>]
    let MinimumTemperatureC = 120.0

    [<Literal>]
    let MaximumTemperatureC = 350.0

    /// Melting point of rhombic sulfur. The monoclinic form melts at 119.6 degC;
    /// which one is relevant depends on thermal history, so the lower value is
    /// the conservative floor for a freezing check.
    [<Literal>]
    let MeltingPointRhombicC = 115.21

    /// Melting point of monoclinic sulfur.
    [<Literal>]
    let MeltingPointMonoclinicC = 119.6

    /// Lambda transition: above it the liquid polymerises and the viscosity
    /// rises by orders of magnitude.
    [<Literal>]
    let LambdaTransitionC = 159.4

    /// Partial pressure of one allotrope over liquid sulfur [bar].
    let private allotropePressure (model: Speciation.Model) (liquid: Speciation.Allotrope)
                                  (a: Speciation.Allotrope) (t: float<K>) =
        let gLiquid = Speciation.gibbs liquid t
        let deltaG = Speciation.gibbs a t - float a.Atoms * gLiquid
        exp (-deltaG / (float Ru * float t))

    let private liquidPhase (model: Speciation.Model) : Thermo<Speciation.Allotrope> =
        match model.Liquid with
        | Some l -> ok l
        | None ->
            fail (DatabaseParseError
                    ("the sulfur database has no liquid reference phase, so no vapour "
                     + "pressure can be derived; re-run tools/import_sulfur.py"))

    let private inRange (t: float<K>) =
        let c = float t - 273.15
        c >= MinimumTemperatureC && c <= MaximumTemperatureC

    /// Saturation pressure of sulfur vapour over its own liquid [bar], summed
    /// over the allotropes.
    let vapourPressure (model: Speciation.Model) (t: float<K>) : Thermo<float<bar>> =
        if not (inRange t) then
            fail (OutsideFitRange
                    ("sulfur vapour pressure", "temperature", float t - 273.15,
                     MinimumTemperatureC, MaximumTemperatureC))
        else
            liquidPhase model
            >>= fun liquid ->
                let total =
                    model.Allotropes
                    |> List.sumBy (fun a -> allotropePressure model liquid a t)
                if Double.IsNaN total || total <= 0.0 then
                    fail (CorrelationExtrapolated
                            ("sulfur vapour pressure",
                             $"non-physical result at %.1f{float t - 273.15} degC"))
                else ok (total * 1.0<bar>)

    /// Dew point of sulfur at a given sulfur partial pressure, by bisection on
    /// the vapour pressure curve.
    ///
    /// Bisection rather than an inverted correlation because the vapour pressure
    /// is assembled from several allotropes and has no closed-form inverse.
    let dewPoint (model: Speciation.Model) (sulfurPartialPressure: float<bar>)
                 : Thermo<float<K>> =
        if float sulfurPartialPressure <= 0.0 then
            fail (InvalidMixture "the sulfur partial pressure must be positive")
        else
            let evaluate tC =
                match vapourPressure model ((tC + 273.15) * 1.0<K>) with
                | Success (p, _) -> Some (float p)
                | Failure _ -> None

            match evaluate MinimumTemperatureC, evaluate MaximumTemperatureC with
            | Some low, Some high ->
                let target = float sulfurPartialPressure
                if target < low then
                    fail (OutsideFitRange
                            ("sulfur dew point", "partial pressure", target, low, high))
                elif target > high then
                    fail (OutsideFitRange
                            ("sulfur dew point", "partial pressure", target, low, high))
                else
                    let rec bisect lo hi n =
                        if n = 0 || hi - lo < 1e-6 then (lo + hi) / 2.0
                        else
                            let mid = (lo + hi) / 2.0
                            match evaluate mid with
                            | Some p when p < target -> bisect mid hi (n - 1)
                            | Some _ -> bisect lo mid (n - 1)
                            | None -> mid
                    ok ((bisect MinimumTemperatureC MaximumTemperatureC 200 + 273.15) * 1.0<K>)
            | _ ->
                fail (CorrelationExtrapolated
                        ("sulfur dew point", "the vapour pressure curve could not be evaluated"))

    /// State of the sulfur at one point on a condenser profile.
    type CondenserState =
        { Temperature        : float<K>
          /// Sulfur actually present in the gas phase [bar], capped at saturation.
          VapourPressure     : float<bar>
          /// Saturation pressure at this temperature [bar].
          SaturationPressure : float<bar>
          /// Fraction of the incoming sulfur that has condensed.
          CondensedFraction  : float
          /// Average molar mass of the sulfur still in the vapour [g/mol].
          VapourMolarMass    : float
          IsSaturated        : bool }

    /// Sulfur state at a temperature, given the sulfur partial pressure the
    /// stream would have with nothing condensed.
    ///
    /// This is the function to march a profile with. A pure vapour-phase
    /// equilibrium would return a sulfur pressure well above saturation, which
    /// is impossible once liquid is present; here the gas-phase sulfur is capped
    /// at p_sat(T) and the remainder is reported as condensate.
    let condenserState (model: Speciation.Model) (t: float<K>)
                       (inletSulfurPressure: float<bar>) : Thermo<CondenserState> =
        if float inletSulfurPressure <= 0.0 then
            fail (InvalidMixture "the inlet sulfur partial pressure must be positive")
        else
            vapourPressure model t
            >>= fun pSat ->
                let saturated = inletSulfurPressure > pSat
                let inVapour = if saturated then pSat else inletSulfurPressure
                Speciation.distribution model t inVapour
                >>= fun d ->
                    ok { Temperature = t
                         VapourPressure = inVapour
                         SaturationPressure = pSat
                         CondensedFraction =
                            if saturated then
                                1.0 - float inVapour / float inletSulfurPressure
                            else 0.0
                         VapourMolarMass = d.AverageMolarMass
                         IsSaturated = saturated }

    /// Supersaturation ratio of the bulk gas: p_S / p_sat(T_bulk).
    ///
    /// Above one the bulk itself is supersaturated, which is the precondition
    /// for fog. In a well-behaved condenser it stays at or just below one,
    /// because condensation removes sulfur as fast as the gas cools.
    let supersaturation (model: Speciation.Model) (tBulk: float<K>)
                        (sulfurPartialPressure: float<bar>) : Thermo<float> =
        vapourPressure model tBulk
        >>= fun pSat -> ok (float sulfurPartialPressure / float pSat)
