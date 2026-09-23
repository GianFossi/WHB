namespace XSulfur

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Properties of liquid sulfur.
///
/// The viscosity is the whole story and it is unlike any other process liquid.
/// Below the lambda transition at 159.4 degC molten sulfur is a thin S8 ring
/// liquid, about 7 mPa*s - roughly ten times water. Above it the rings open and
/// polymerise into long chains, and the viscosity rises by FOUR ORDERS OF
/// MAGNITUDE over about thirty degrees, peaking near 93 Pa*s at 187 degC before
/// depolymerisation brings it down again.
///
///     155 degC   0.007 Pa*s
///     165 degC   4      Pa*s        570 times higher, ten degrees away
///     187 degC  93      Pa*s        13 000 times higher
///
/// That is why a sulfur condenser is designed around a wall temperature window
/// rather than around a heat transfer target, and why the window's upper bound
/// is a hard constraint rather than a preference: a few degrees of overshoot
/// stops the condensate draining and plugs the bundle.
module LiquidProperties =

    /// Viscosity anchor points, degC -> Pa*s, from the classical measurements
    /// (Bacon & Fanelli 1943; Touro & Wiewiorowski 1966).
    ///
    /// Interpolation is on LOG viscosity against temperature, because the
    /// property spans four decades and a linear interpolation between 159 and
    /// 165 degC would understate the middle of the transition by more than an
    /// order of magnitude. Held as data, not as a fitted equation: no published
    /// closed form reproduces the lambda transition well, and pretending
    /// otherwise would hide where the numbers come from.
    let viscosityAnchors : (float * float) list =
        [ 120.0, 11.0e-3
          130.0, 9.5e-3
          140.0, 8.2e-3
          150.0, 7.4e-3
          155.0, 7.0e-3
          159.4, 9.0e-3
          160.0, 25.0e-3
          161.0, 0.12
          163.0, 0.80
          165.0, 4.0
          170.0, 15.0
          175.0, 33.0
          180.0, 60.0
          187.0, 93.0
          195.0, 90.0
          200.0, 85.0
          220.0, 55.0
          250.0, 25.0
          300.0, 8.0
          350.0, 3.0
          400.0, 1.5 ]

    /// Dynamic viscosity of liquid sulfur [Pa*s].
    let viscosity (t: float<K>) : Thermo<float> =
        let tC = float t - 273.15
        let first = List.head viscosityAnchors
        let last = List.last viscosityAnchors

        if tC < fst first || tC > fst last then
            fail (OutsideFitRange
                    ("liquid sulfur viscosity", "temperature", tC, fst first, fst last))
        else
            let bracket =
                viscosityAnchors
                |> List.pairwise
                |> List.tryFind (fun ((t0, _), (t1, _)) -> tC >= t0 && tC <= t1)

            match bracket with
            | Some ((t0, v0), (t1, v1)) ->
                let value =
                    if abs (t1 - t0) < 1e-12 then v0
                    else exp (log v0 + (log v1 - log v0) * (tC - t0) / (t1 - t0))
                ok value
                |> warnIf (tC > Chemistry.LambdaTransitionC)
                          (CorrelationExtrapolated
                            ("liquid sulfur viscosity",
                             $"%.1f{tC} degC is above the lambda transition: the condensate "
                             + $"is polymerised at %.3g{value} Pa*s and will not drain"))
            | None ->
                ok (snd last)

    /// Density of liquid sulfur [kg/m^3]. Nearly linear over the condenser
    /// window; the polymerisation barely affects it, unlike the viscosity.
    let density (t: float<K>) : Thermo<float> =
        let tC = float t - 273.15
        if tC < 115.0 || tC > 400.0 then
            fail (OutsideFitRange ("liquid sulfur density", "temperature", tC, 115.0, 400.0))
        else
            ok (1819.0 - 0.784 * (tC - 120.0))

    /// Surface tension of liquid sulfur [N/m].
    let surfaceTension (t: float<K>) : Thermo<float> =
        let tC = float t - 273.15
        if tC < 115.0 || tC > 400.0 then
            fail (OutsideFitRange ("liquid sulfur surface tension", "temperature", tC, 115.0, 400.0))
        else
            ok (0.0608 - 7.0e-5 * (tC - 120.0))

    /// Thermal conductivity of liquid sulfur [W/(m*K)]. Low, which is why the
    /// film would matter if it were thick - and it is not, which is why it
    /// usually does not.
    let conductivity (t: float<K>) : Thermo<float> =
        let tC = float t - 273.15
        if tC < 115.0 || tC > 400.0 then
            fail (OutsideFitRange ("liquid sulfur conductivity", "temperature", tC, 115.0, 400.0))
        else
            ok (0.1352 + 1.15e-4 * (tC - 120.0))

    /// Specific heat of liquid sulfur [J/(kg*K)]. Rises sharply through the
    /// lambda transition because ring opening absorbs energy: the transition
    /// shows up in cp as well as in viscosity, and a constant value across the
    /// window misses it.
    let heatCapacity (t: float<K>) : Thermo<float> =
        let tC = float t - 273.15
        if tC < 115.0 || tC > 400.0 then
            fail (OutsideFitRange ("liquid sulfur heat capacity", "temperature", tC, 115.0, 400.0))
        else
            let baseline = 1000.0 + 0.55 * (tC - 120.0)
            // Lambda anomaly, modelled as a peak centred on the transition.
            let width = 8.0
            let anomaly = 900.0 * exp (-((tC - Chemistry.LambdaTransitionC) ** 2.0)
                                       / (2.0 * width * width))
            ok (baseline + anomaly)

    /// How far the liquid is from the lambda transition, in kelvin. Negative
    /// means past it.
    let lambdaMargin (t: float<K>) =
        Chemistry.LambdaTransitionC - (float t - 273.15)

    /// Ratio of the viscosity at a temperature to its value at 155 degC, the
    /// practical design point below the transition. This is the number to quote
    /// when arguing about a few degrees of wall temperature.
    let viscosityPenalty (t: float<K>) : Thermo<float> =
        viscosity t
        >>= fun mu ->
            viscosity (428.15<K>)      // 155 degC
            >>= fun reference -> ok (mu / reference)
