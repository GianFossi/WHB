namespace WhbThermo.Properties

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Chung, Lee & Starling estimation of low-pressure gas transport properties.
///
/// This exists because 14 species in the database have no measured transport
/// data, and several of them - the sulfur allotropes above all - are needed at
/// 1400 degC where the two-parameter Sutherland fits are no longer defensible
/// anyway.
///
/// It is an ESTIMATE, and the distinction is load-bearing. Chung reproduces
/// measured viscosities of non-polar gases to roughly 5 %, polar ones to 10 to
/// 15 %, and conductivities rather worse. That is good enough to keep a rating
/// running and not good enough to quote. Every value returned therefore carries
/// a warning naming it as estimated, so the provenance travels with the number
/// into the calculation report instead of being lost at the call site.
///
///     mu = 40.785 * Fc * sqrt(M T) / (Vc^(2/3) Omega_v)       [micropoise]
///     Fc = 1 - 0.2756 omega + 0.059035 mu_r^4 + kappa
///
/// where Omega_v is the Neufeld collision integral in T* = 1.2593 T/Tc.
module Chung =

    /// What the estimate needs. Tc and Vc are the minimum; the acentric factor
    /// and dipole moment refine it and default to the non-polar case.
    type ChungInput =
        { /// Critical temperature [K].
          Tc            : float<K>
          /// Critical volume [cm^3/mol].
          Vc            : float
          /// Molar mass [g/mol].
          MolarMass     : float
          /// Acentric factor.
          Acentric      : float
          /// Dipole moment [debye].
          DipoleMoment  : float
          /// Association factor: 0 for most gases, 0.076 for water,
          /// 0.0682 + 4.704 (n_OH / M) for alcohols.
          Association   : float }

    /// Neufeld collision integral for viscosity.
    let collisionIntegral (tStar: float) =
        if tStar <= 0.0 then nan
        else
            1.16145 * (tStar ** -0.14874)
            + 0.52487 * exp (-0.7732 * tStar)
            + 2.16178 * exp (-2.43787 * tStar)

    /// Low-pressure dynamic viscosity [Pa*s].
    let viscosity (input: ChungInput) (t: float<K>) : Thermo<float<Pa*s>> =
        if float input.Tc <= 0.0 || input.Vc <= 0.0 || input.MolarMass <= 0.0 then
            fail (InvalidMixture "Chung needs a positive Tc, Vc and molar mass")
        elif float t <= 0.0 then
            fail (InvalidMixture "temperature must be positive")
        else
            let tStar = 1.2593 * float t / float input.Tc
            let omega = collisionIntegral tStar
            // Reduced dipole moment.
            let muR =
                131.3 * input.DipoleMoment / sqrt (input.Vc * float input.Tc)
            let fc =
                1.0 - 0.2756 * input.Acentric
                + 0.059035 * Numerics.sq (Numerics.sq muR) + input.Association
            let microPoise =
                40.785 * fc * sqrt (input.MolarMass * float t)
                / ((input.Vc ** (2.0 / 3.0)) * omega)
            ok (microPoise * 1e-7<Pa*s>)
            |> warn (CorrelationExtrapolated
                        ("Chung-Lee-Starling",
                         "viscosity is ESTIMATED, not measured: about 5 % for non-polar gases, "
                         + "10-15 % for polar ones. Do not quote it as data."))
            |> warnIf (tStar < 0.3 || tStar > 100.0)
                      (CorrelationExtrapolated
                        ("Chung-Lee-Starling",
                         $"reduced temperature T* = %.2f{tStar} outside the correlated 0.3-100"))

    /// Low-pressure thermal conductivity [W/(m*K)] by the Chung modification of
    /// the Eucken relation.
    ///
    ///     k = 3.75 Psi (mu R) / M      with Psi from the internal degrees of
    ///                                  freedom via alpha = cv/R - 3/2
    ///
    /// `cvMolar` is the molar heat capacity at constant volume [J/(mol*K)],
    /// which the caller already has from the NASA-9 fit: cv = cp - R for an
    /// ideal gas. Requiring it rather than estimating it keeps the one piece of
    /// real data in the calculation.
    let conductivity (input: ChungInput) (t: float<K>) (cvMolar: float)
                     : Thermo<float<W/(m*K)>> =
        if cvMolar <= 0.0 then
            fail (InvalidMixture "the molar heat capacity must be positive")
        else
            viscosity input t
            >>= fun mu ->
                let r = float Ru
                let alpha = cvMolar / r - 1.5
                let beta = 0.7862 - 0.7109 * input.Acentric
                            + 1.3168 * input.Acentric * input.Acentric
                let tReduced = float t / float input.Tc
                let z = 2.0 + 10.5 * tReduced * tReduced
                let psi =
                    1.0 + alpha * ((0.215 + 0.28288 * alpha - 1.061 * beta + 0.26665 * z)
                                   / (0.6366 + beta * z + 1.061 * alpha * beta))
                let k =
                    3.75 * psi * float mu * r / (input.MolarMass / 1000.0)
                ok (k * 1.0<W/(m*K)>)
                |> warn (CorrelationExtrapolated
                            ("Chung-Lee-Starling",
                             "thermal conductivity is ESTIMATED and carries more scatter than "
                             + "the viscosity it derives from - typically 10-20 %."))

    /// Critical volume estimated from Tc and Pc where it is not tabulated,
    /// using the Ihmels correlation. An estimate feeding an estimate: the
    /// warning says so, because two stacked estimates is a different quality of
    /// number from one.
    let criticalVolumeFrom (tc: float<K>) (pc: float<bar>) : Thermo<float> =
        if float tc <= 0.0 || float pc <= 0.0 then
            fail (InvalidMixture "Tc and Pc must be positive")
        else
            ok (0.2513 * 83.14 * float tc / float pc)
            |> warn (CorrelationExtrapolated
                        ("Chung-Lee-Starling",
                         "the critical volume is itself estimated from Tc and Pc, so the "
                         + "transport values built on it are a second-order estimate"))
