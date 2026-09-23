namespace WhbThermo.Boiling

open System
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Steam

/// Shell-side nucleate boiling and the overall heat transfer resistance.
///
/// A word on what these correlations are worth. Cooper and Rohsenow routinely
/// disagree by 30-50 % on the same duty, and Rohsenow's C_sf is a surface-and-
/// fluid constant that must be looked up, not derived: for water on a given
/// tube material it ranges from 0.006 to 0.020, and since it enters cubed, that
/// range alone moves the predicted heat flux by a factor of about 37. Cooper is
/// the safer default because it needs only reduced pressure and molar mass, and
/// its scatter against data is roughly +/- 30 % rather than the order of
/// magnitude an unverified C_sf can produce. Measured on this implementation at
/// 100 bar and 5 K superheat, the published water C_sf values from 0.0060 to
/// 0.0132 span h from 1108 down to 104 kW/(m^2*K) - a factor of 10.6, with
/// Cooper's 275 sitting in between.
///
/// Nothing here is a substitute for a critical heat flux check. The correlations
/// are all for the nucleate regime and say nothing about departure from it;
/// `criticalHeatFlux` is provided for exactly that reason and a rating that does
/// not call it is incomplete.
module Nucleate =

    /// Standard gravity.
    let g = 9.80665<m/s^2>

    /// Surface tension of water against its own vapour, IAPWS R1-76:
    ///   sigma = B tau^mu (1 - b tau),  tau = 1 - T/Tc
    /// Valid from the triple point to the critical point.
    let surfaceTension (t: float<K>) : Thermo<float> =
        let tc = 647.096
        let tK = float t
        if tK < 273.15 || tK > tc then
            fail (OutsideFitRange ("IAPWS R1-76", "surface tension", tK, 273.15, tc))
        else
            let tau = 1.0 - tK / tc
            let sigma = 0.2358 * (tau ** 1.256) * (1.0 - 0.625 * tau)   // N/m
            ok sigma

    // ---------- Cooper ----------

    /// Cooper pool boiling correlation for a single tube:
    ///   h = 55 * pr^0.12 * (-log10 pr)^-0.55 * M^-0.5 * q''^0.67
    ///
    /// The leading constant of 55 assumes a surface roughness Rp of 1 micron.
    /// `roughness` scales it as (Rp/1um)^0.2; pass 1.0 for a normal machined or
    /// drawn tube. The exponent is small, so this is a second-order knob, not a
    /// tuning parameter to close a heat balance with.
    let cooperFromHeatFlux (reducedPressure: float) (molarMass: float<kg/kmol>)
                           (heatFlux: float<W/m^2>) (roughness: float)
                           : Thermo<float<W/(m^2*K)>> =
        let pr = reducedPressure
        if pr <= 0.0 || pr >= 1.0 then
            fail (CorrelationExtrapolated ("Cooper", $"reduced pressure %.4f{pr} must lie in (0,1)"))
        elif float heatFlux <= 0.0 then
            fail (InvalidMixture "heat flux must be positive")
        else
            let h =
                55.0
                * (pr ** 0.12)
                * ((-(log10 pr)) ** -0.55)
                * (float molarMass ** -0.5)
                * (float heatFlux ** 0.67)
                * (roughness ** 0.2)
            ok (h * 1.0<W/(m^2*K)>)
            |> warnIf (pr < 0.001 || pr > 0.9)
                      (CorrelationExtrapolated ("Cooper",
                        $"reduced pressure %.4f{pr} outside the correlated 0.001-0.9"))
            |> warnIf (float heatFlux > 1.0e6)
                      (CorrelationExtrapolated ("Cooper",
                        $"heat flux %.0f{float heatFlux / 1000.0} kW/m2 is high; "
                        + "check against the critical heat flux"))

    /// Cooper expressed against wall superheat instead of heat flux.
    ///
    /// h depends on q'' and q'' = h dT, so the pair is implicit. Substituting
    /// gives a closed form rather than an iteration:
    ///   h = [C * dT^0.67]^(1/0.33),  C = 55 pr^0.12 (-log10 pr)^-0.55 M^-0.5 Rp^0.2
    /// which avoids any convergence question.
    let cooperFromSuperheat (reducedPressure: float) (molarMass: float<kg/kmol>)
                            (superheat: float) (roughness: float)
                            : Thermo<float<W/(m^2*K)>> =
        if superheat <= 0.0 then
            fail (InvalidMixture "wall superheat must be positive")
        else
            let pr = reducedPressure
            if pr <= 0.0 || pr >= 1.0 then
                fail (CorrelationExtrapolated ("Cooper", $"reduced pressure %.4f{pr} must lie in (0,1)"))
            else
                let c =
                    55.0 * (pr ** 0.12) * ((-(log10 pr)) ** -0.55)
                    * (float molarMass ** -0.5) * (roughness ** 0.2)
                let h = (c * (superheat ** 0.67)) ** (1.0 / 0.33)
                ok (h * 1.0<W/(m^2*K)>)

    // ---------- Rohsenow ----------

    /// Surface-fluid combinations for Rohsenow's C_sf. These are experimental
    /// constants, not properties: using one for a surface it was not measured on
    /// is the single largest error source in this correlation, because C_sf
    /// enters cubed.
    type SurfaceFluid =
        | WaterOnPolishedStainless      // C_sf 0.0080, n = 1.0
        | WaterOnGroundStainless        // C_sf 0.0080
        | WaterOnMechanicallyPolishedSS // C_sf 0.0132
        | WaterOnBrass                  // C_sf 0.0060
        | WaterOnCopper                 // C_sf 0.0130
        | WaterOnPlatinum               // C_sf 0.0130
        | CustomSurface of csf: float * n: float

        member this.Coefficients =
            match this with
            | WaterOnPolishedStainless -> 0.0080, 1.0
            | WaterOnGroundStainless -> 0.0080, 1.0
            | WaterOnMechanicallyPolishedSS -> 0.0132, 1.0
            | WaterOnBrass -> 0.0060, 1.0
            | WaterOnCopper -> 0.0130, 1.0
            | WaterOnPlatinum -> 0.0130, 1.0
            | CustomSurface (csf, n) -> csf, n

    /// Rohsenow mechanistic correlation, heat flux from wall superheat.
    ///
    /// Rohsenow relates flux to superheat CUBED, so it climbs violently: at
    /// 100 bar this implementation gives 11 MW/m^2 at a 10 K superheat, roughly
    /// three times the critical heat flux. The correlation has no knowledge of
    /// that limit. Always pass the result through `checkAgainstCritical`.
    ///
    ///   q'' = mu_l h_fg [g(rho_l - rho_v)/sigma]^0.5
    ///         * [cp_l dTsat / (C_sf h_fg Pr_l^n)]^3
    let rohsenowHeatFlux (surface: SurfaceFluid)
                         (muLiquid: float) (hfg: float) (cpLiquid: float)
                         (rhoLiquid: float) (rhoVapour: float)
                         (sigma: float) (prandtlLiquid: float)
                         (superheat: float) : Thermo<float<W/m^2>> =
        let csf, n = surface.Coefficients
        if superheat <= 0.0 then
            fail (InvalidMixture "wall superheat must be positive")
        elif rhoLiquid <= rhoVapour then
            fail (InvalidMixture "liquid density must exceed vapour density")
        elif sigma <= 0.0 then
            fail (InvalidMixture "surface tension must be positive")
        else
            let bubble = sqrt (float g * (rhoLiquid - rhoVapour) / sigma)
            let group = cpLiquid * superheat / (csf * hfg * (prandtlLiquid ** n))
            ok (muLiquid * hfg * bubble * (group ** 3.0) * 1.0<W/m^2>)
            |> warn (CorrelationExtrapolated
                        ("Rohsenow",
                         $"C_sf = %.4f{csf} is a surface-specific constant entering cubed; "
                         + "confirm it matches the actual tube surface"))

    // ---------- bundle effects ----------

    /// Palen & Small bundle correction. Rising vapour from lower tubes enhances
    /// convection around upper ones, so a bundle boils better than a single tube:
    ///   F_b = 1 + 0.1 (D_bundle / D_ext)^0.75
    ///
    /// This is an enhancement factor, so it is NOT conservative on the duty side
    /// and IS conservative on wall temperature. Palen recommends capping it,
    /// since the same vapour blanket that enhances low heat fluxes degrades high
    /// ones; the cap here is 3.0.
    let bundleFactor (bundleDiameter: float<m>) (tubeOuterDiameter: float<m>) : Thermo<float> =
        if float tubeOuterDiameter <= 0.0 || float bundleDiameter <= 0.0 then
            fail (InvalidMixture "diameters must be positive")
        elif bundleDiameter < tubeOuterDiameter then
            fail (InvalidMixture "bundle diameter is smaller than a single tube")
        else
            let raw = 1.0 + 0.1 * ((float bundleDiameter / float tubeOuterDiameter) ** 0.75)
            ok (min raw 3.0)
            |> warnIf (raw > 3.0)
                      (CorrelationExtrapolated
                        ("Palen bundle factor",
                         $"computed %.2f{raw}, capped at 3.0"))

    /// Verdict of a heat flux against the burnout limit.
    type CriticalCheck =
        { HeatFlux     : float<W/m^2>
          Critical     : float<W/m^2>
          Ratio        : float
          IsAcceptable : bool }

    /// Gate a nucleate boiling result against the critical heat flux.
    ///
    /// Both Cooper and Rohsenow are nucleate-regime correlations and neither
    /// knows where that regime ends: they will happily return a heat flux well
    /// past burnout. Beyond the critical flux the surface dries out, the film
    /// coefficient collapses by an order of magnitude and the tube wall runs
    /// away to gas temperature. A rating that does not perform this check is
    /// incomplete, so this returns Failure rather than a warning when the limit
    /// is exceeded.
    ///
    /// The 0.7 warning threshold reflects that the correlations themselves carry
    /// +/- 30 % scatter: at 70 % of nominal CHF the error bands already touch.
    let checkAgainstCritical (heatFlux: float<W/m^2>) (critical: float<W/m^2>)
                             : Thermo<CriticalCheck> =
        if float critical <= 0.0 then
            fail (InvalidMixture "critical heat flux must be positive")
        else
            let ratio = float heatFlux / float critical
            if ratio >= 1.0 then
                fail (CorrelationExtrapolated
                        ("critical heat flux",
                         $"q'' = %.0f{float heatFlux / 1000.0} kW/m2 is at or beyond the "
                         + $"critical %.0f{float critical / 1000.0} kW/m2 (ratio %.2f{ratio}); "
                         + "the nucleate correlations are not valid here and the tube would dry out"))
            else
                ok { HeatFlux = heatFlux; Critical = critical
                     Ratio = ratio; IsAcceptable = true }
                |> warnIf (ratio > 0.7)
                          (CorrelationExtrapolated
                            ("critical heat flux",
                             $"q'' is %.0f{ratio * 100.0} %% of critical; with the +/-30 %% "
                             + "scatter of the boiling correlations this is not a safe margin"))

    // ---------- critical heat flux ----------

    /// Zuber critical heat flux for pool boiling on a single horizontal surface:
    ///   q_crit = 0.131 h_fg rho_v^0.5 [sigma g (rho_l - rho_v)]^0.25
    let zuberCriticalHeatFlux (hfg: float) (rhoLiquid: float) (rhoVapour: float)
                              (sigma: float) : Thermo<float<W/m^2>> =
        if rhoLiquid <= rhoVapour then
            fail (InvalidMixture "liquid density must exceed vapour density")
        else
            let q =
                0.131 * hfg * sqrt rhoVapour
                * ((sigma * float g * (rhoLiquid - rhoVapour)) ** 0.25)
            ok (q * 1.0<W/m^2>)

    /// Palen & Small bundle derating of the critical heat flux. A tube bundle
    /// reaches burnout at a far lower flux than a single tube, because vapour
    /// generated below has to escape past the tubes above. `psi` is the bundle
    /// geometry parameter pi * D_bundle * L / (N_tubes * pi * D_ext * L),
    /// which reduces to D_bundle / (N_tubes * D_ext).
    ///
    /// Ignoring this is a classic way to design a boiler that tests fine on
    /// paper and dries out in service.
    let bundleCriticalHeatFlux (singleTube: float<W/m^2>) (psi: float) : Thermo<float<W/m^2>> =
        if psi <= 0.0 then
            fail (InvalidMixture "bundle geometry parameter must be positive")
        else
            let factor = 3.1 * psi
            ok (singleTube * min factor 1.0)
            |> warnIf (factor < 0.1)
                      (CorrelationExtrapolated
                        ("Palen bundle CHF",
                         $"derating factor %.3f{factor} is severe; a dense bundle "
                         + "may be vapour-blanketed at modest heat flux"))


/// Assembles the overall heat transfer resistance.
module Overall =

    /// Tube metal thermal conductivity [W/(m*K)] as a function of temperature.
    /// Fits taken from the design manual; valid over normal WHB metal
    /// temperatures, roughly 20-600 degC.
    type TubeMaterial =
        | CarbonSteelSA516_70
        | LowAlloySA213_T11
        | Austenitic316L
        | CustomConductivity of (float<degC> -> float)

        member this.Conductivity(t: float<degC>) =
            let c = float t
            match this with
            | CarbonSteelSA516_70 -> 52.0 - 0.028 * c
            | LowAlloySA213_T11 -> 36.5 + 0.012 * c - 1.1e-5 * c * c
            | Austenitic316L -> 14.6 + 0.015 * c
            | CustomConductivity f -> f t

    type Resistances =
        { GasFilm      : float
          FoulingInner : float
          Wall         : float
          FoulingOuter : float
          Boiling      : float
          Total        : float }

        member this.OverallCoefficient = 1.0 / this.Total

        /// Fraction of the total resistance contributed by each term. This is
        /// what tells you where the rating's uncertainty actually lives -- it is
        /// usually fouling and boiling, not the gas-side film everyone tunes.
        member this.Shares =
            [ "gas film", this.GasFilm / this.Total
              "inner fouling", this.FoulingInner / this.Total
              "wall", this.Wall / this.Total
              "outer fouling", this.FoulingOuter / this.Total
              "boiling", this.Boiling / this.Total ]

    /// Overall resistance referred to the OUTSIDE tube area:
    ///   1/U_ext = D_ext/(D_int h_int) + D_ext ln(D_ext/D_int)/(2 k_wall)
    ///             + R_f,int D_ext/D_int + R_f,ext + 1/h_ext
    let resistances (dInt: float<m>) (dExt: float<m>)
                    (hInternal: float<W/(m^2*K)>) (hExternal: float<W/(m^2*K)>)
                    (foulingInner: float) (foulingOuter: float)
                    (material: TubeMaterial) (meanMetalTemperature: float<degC>)
                    : Thermo<Resistances> =
        if dExt <= dInt then
            fail (InvalidMixture "outer diameter must exceed inner diameter")
        elif float hInternal <= 0.0 || float hExternal <= 0.0 then
            fail (InvalidMixture "film coefficients must be positive")
        elif foulingInner < 0.0 || foulingOuter < 0.0 then
            fail (InvalidMixture "fouling resistances cannot be negative")
        else
            let ratio = float dExt / float dInt
            let kWall = material.Conductivity meanMetalTemperature

            let gasFilm = ratio / float hInternal
            let innerFouling = foulingInner * ratio
            let wall = float dExt * log ratio / (2.0 * kWall)
            let outerFouling = foulingOuter
            let boiling = 1.0 / float hExternal
            let total = gasFilm + innerFouling + wall + outerFouling + boiling

            ok { GasFilm = gasFilm
                 FoulingInner = innerFouling
                 Wall = wall
                 FoulingOuter = outerFouling
                 Boiling = boiling
                 Total = total }
            |> warnIf (kWall <= 0.0)
                      (CorrelationExtrapolated
                        ("tube conductivity",
                         $"non-positive at %.0f{float meanMetalTemperature} degC"))
            |> warnIf (foulingInner = 0.0 && foulingOuter = 0.0)
                      (CorrelationExtrapolated
                        ("fouling",
                         "both fouling resistances are zero - this is the CLEAN case, "
                         + "valid for metal temperature checks but not for a duty guarantee"))

    /// Mean tube wall temperature from the resistance network, referred to the
    /// outside area. Needed for the metal temperature check and to close the
    /// Cooper superheat loop.
    let wallTemperature (r: Resistances) (tGas: float<degC>) (tBoiling: float<degC>)
                        : float<degC> =
        let driving = float tGas - float tBoiling
        let flux = driving / r.Total
        // Wall outer surface: start from the boiling side and add its resistance.
        (float tBoiling + flux * (r.Boiling + r.FoulingOuter)) * 1.0<degC>
