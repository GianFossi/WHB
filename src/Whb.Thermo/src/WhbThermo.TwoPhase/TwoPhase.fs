namespace WhbThermo.TwoPhase

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Fluid state needed by every two-phase correlation here. Assembling it once
/// avoids twelve-argument functions and makes it obvious when a property is
/// missing rather than defaulted.
type PhaseProperties =
    { LiquidDensity      : float        // kg/m^3
      VapourDensity      : float
      LiquidViscosity    : float        // Pa*s
      VapourViscosity    : float
      LiquidConductivity : float        // W/(m*K)
      VapourConductivity : float
      LiquidHeatCapacity : float        // J/(kg*K)
      VapourHeatCapacity : float
      SurfaceTension     : float        // N/m
      LatentHeat         : float        // J/kg
      ReducedPressure    : float
      MolarMass          : float }      // kg/kmol

    member this.LiquidPrandtl =
        this.LiquidHeatCapacity * this.LiquidViscosity / this.LiquidConductivity

    member this.VapourPrandtl =
        this.VapourHeatCapacity * this.VapourViscosity / this.VapourConductivity

    member this.DensityRatio = this.LiquidDensity / this.VapourDensity


/// Shared dimensionless groups.
module Flow =

    /// Reynolds number of the liquid fraction only: Re_l = G(1-x)D/mu_l.
    let liquidReynolds (massVelocity: float) (quality: float) (diameter: float)
                       (p: PhaseProperties) =
        massVelocity * (1.0 - quality) * diameter / p.LiquidViscosity

    /// Reynolds number with the total flow taken as liquid: Re_lo = G D/mu_l.
    let liquidOnlyReynolds (massVelocity: float) (diameter: float) (p: PhaseProperties) =
        massVelocity * diameter / p.LiquidViscosity

    /// Vapour-fraction Reynolds number.
    let vapourReynolds (massVelocity: float) (quality: float) (diameter: float)
                       (p: PhaseProperties) =
        massVelocity * quality * diameter / p.VapourViscosity

    /// Lockhart-Martinelli parameter for turbulent-turbulent flow:
    ///   X_tt = [(1-x)/x]^0.9 (rho_v/rho_l)^0.5 (mu_l/mu_v)^0.1
    let martinelli (quality: float) (p: PhaseProperties) : Thermo<float> =
        if quality <= 0.0 || quality >= 1.0 then
            fail (InvalidMixture "Martinelli parameter needs a quality strictly between 0 and 1")
        else
            ok (((1.0 - quality) / quality) ** 0.9
                * (p.VapourDensity / p.LiquidDensity) ** 0.5
                * (p.LiquidViscosity / p.VapourViscosity) ** 0.1)

    /// Dittus-Boelter on a given Reynolds number, heating form (Pr^0.4).
    let dittusBoelterHeating (re: float) (pr: float) (k: float) (diameter: float) =
        0.023 * (re ** 0.8) * (pr ** 0.4) * k / diameter


/// Flow boiling inside tubes.
module FlowBoiling =

    /// Forster & Zuber nucleate boiling term used inside Chen's correlation.
    ///
    ///   h_FZ = 0.00122 [k_l^0.79 cp_l^0.45 rho_l^0.49
    ///                   / (sigma^0.5 mu_l^0.29 h_fg^0.24 rho_v^0.24)]
    ///          dT_sat^0.24 dp_sat^0.75
    ///
    /// `saturationPressureSlope` is dP/dT at saturation [Pa/K]; the term
    /// dp_sat is that slope times the wall superheat. Supplying it explicitly
    /// rather than differentiating an internal steam table keeps the function
    /// usable for any fluid.
    let forsterZuber (p: PhaseProperties) (superheat: float)
                     (saturationPressureSlope: float) : Thermo<float> =
        if superheat <= 0.0 then
            fail (InvalidMixture "wall superheat must be positive")
        elif saturationPressureSlope <= 0.0 then
            fail (InvalidMixture "dP/dT at saturation must be positive")
        else
            let group =
                (p.LiquidConductivity ** 0.79) * (p.LiquidHeatCapacity ** 0.45)
                * (p.LiquidDensity ** 0.49)
                / ((p.SurfaceTension ** 0.5) * (p.LiquidViscosity ** 0.29)
                   * (p.LatentHeat ** 0.24) * (p.VapourDensity ** 0.24))
            let deltaP = saturationPressureSlope * superheat
            ok (0.00122 * group * (superheat ** 0.24) * (deltaP ** 0.75))

    /// Chen's convective enhancement factor F, from the Martinelli parameter.
    ///   F = 1                            for 1/X_tt <= 0.1
    ///   F = 2.35 (1/X_tt + 0.213)^0.736  otherwise
    let chenEnhancement (martinelli: float) =
        let inverse = 1.0 / martinelli
        if inverse <= 0.1 then 1.0
        else 2.35 * (inverse + 0.213) ** 0.736

    /// Chen's nucleate suppression factor S. Rising velocity thins the boundary
    /// layer and suppresses bubble growth, so S falls towards zero at high
    /// two-phase Reynolds number.
    let chenSuppression (twoPhaseReynolds: float) =
        1.0 / (1.0 + 2.53e-6 * (twoPhaseReynolds ** 1.17))

    type ChenResult =
        { Martinelli   : float
          Enhancement  : float
          Suppression  : float
          Convective   : float
          Nucleate     : float
          Coefficient  : float<W/(m^2*K)> }

    /// Chen (1966) superposition: h_tp = F h_l + S h_nb.
    ///
    /// The two mechanisms are added, not combined asymptotically, which is what
    /// distinguishes Chen from Steiner-Taborek. At low quality the nucleate term
    /// dominates and at high quality the convective one does; in between both
    /// contribute and the correlation is at its least certain.
    let chen (massVelocity: float) (quality: float) (diameter: float)
             (p: PhaseProperties) (superheat: float) (saturationPressureSlope: float)
             : Thermo<ChenResult> =
        if quality <= 0.0 || quality >= 1.0 then
            fail (InvalidMixture "Chen needs a quality strictly between 0 and 1")
        else
            Flow.martinelli quality p
            >>= fun x ->
                let f = chenEnhancement x
                let reL = Flow.liquidReynolds massVelocity quality diameter p
                let hL =
                    Flow.dittusBoelterHeating reL p.LiquidPrandtl p.LiquidConductivity diameter
                let reTp = reL * (f ** 1.25)
                let s = chenSuppression reTp

                forsterZuber p superheat saturationPressureSlope
                >>= fun hNb ->
                    ok { Martinelli = x
                         Enhancement = f
                         Suppression = s
                         Convective = f * hL
                         Nucleate = s * hNb
                         Coefficient = (f * hL + s * hNb) * 1.0<W/(m^2*K)> }
                    |> warnIf (reL < 3000.0)
                              (CorrelationExtrapolated
                                ("Chen", $"liquid Reynolds %.0f{reL} is below 3000; the "
                                         + "Dittus-Boelter convective term is extrapolated"))
                    |> warnIf (quality > 0.7)
                              (CorrelationExtrapolated
                                ("Chen", $"quality %.2f{quality} is high; Chen does not model "
                                         + "dryout and will keep predicting a rising coefficient "
                                         + "past the point where the wall goes dry"))

    /// Steiner & Taborek two-phase multiplier for the convective part, vertical
    /// tube, no stratification:
    ///   F_tp = [(1-x)^1.5 + 1.9 x^0.6 (rho_l/rho_v)^0.35]^1.1
    let steinerTaborekConvective (quality: float) (p: PhaseProperties) =
        ((1.0 - quality) ** 1.5
         + 1.9 * (quality ** 0.6) * (p.DensityRatio ** 0.35)) ** 1.1

    /// Steiner & Taborek nucleate boiling multiplier.
    ///   F_pf = 2.816 pr^0.45 + [3.4 + 1.7/(1 - pr^7)] pr^3.7
    ///   n_f  = 0.8 - 0.1 exp(1.75 pr)
    ///   F_nb = F_pf (q/q_o)^n_f (d/d_o)^-0.4 (Rp/Rp_o)^0.133
    /// with the reference conditions q_o = 20 kW/m^2, d_o = 0.01 m, Rp_o = 1 um.
    let steinerTaborekNucleate (p: PhaseProperties) (heatFlux: float)
                               (diameter: float) (roughness: float) : Thermo<float> =
        let pr = p.ReducedPressure
        if pr <= 0.0 || pr >= 1.0 then
            fail (CorrelationExtrapolated ("Steiner-Taborek", $"reduced pressure %.4f{pr} must lie in (0,1)"))
        elif heatFlux <= 0.0 then
            fail (InvalidMixture "heat flux must be positive")
        else
            let fpf = 2.816 * (pr ** 0.45) + (3.4 + 1.7 / (1.0 - pr ** 7.0)) * (pr ** 3.7)
            let nf = 0.8 - 0.1 * exp (1.75 * pr)
            ok (fpf * ((heatFlux / 20000.0) ** nf)
                * ((diameter / 0.01) ** -0.4) * (roughness ** 0.133))
            |> warnIf (pr > 0.95)
                      (CorrelationExtrapolated
                        ("Steiner-Taborek", $"reduced pressure %.3f{pr} is near critical; the "
                                            + "1/(1 - pr^7) term is diverging"))

    type SteinerTaborekResult =
        { ConvectiveMultiplier : float
          NucleateMultiplier   : float
          Convective           : float
          Nucleate             : float
          Coefficient          : float<W/(m^2*K)> }

    /// Steiner & Taborek (1992) asymptotic model:
    ///   h_tp = [(h_nb,o F_nb)^3 + (h_lo F_tp)^3]^(1/3)
    ///
    /// `nucleateReference` is the fluid-specific h_nb,o measured at q = 20 kW/m^2
    /// and pr = 0.1. For water it is 25 580 W/(m^2*K). There is no way to derive
    /// it, so it must be supplied; passing a value borrowed from another fluid
    /// is the main way this correlation goes wrong.
    ///
    /// The cubic asymptotic form means the larger mechanism dominates smoothly
    /// instead of the two simply adding, which is why Steiner-Taborek and Chen
    /// disagree most in the middle of the quality range.
    let steinerTaborek (massVelocity: float) (quality: float) (diameter: float)
                       (p: PhaseProperties) (heatFlux: float) (roughness: float)
                       (nucleateReference: float) : Thermo<SteinerTaborekResult> =
        if quality < 0.0 || quality >= 1.0 then
            fail (InvalidMixture "quality must lie in [0,1)")
        elif nucleateReference <= 0.0 then
            fail (InvalidMixture "the nucleate reference coefficient must be positive")
        else
            let reLo = Flow.liquidOnlyReynolds massVelocity diameter p
            let hLo =
                Flow.dittusBoelterHeating reLo p.LiquidPrandtl p.LiquidConductivity diameter
            let ftp = steinerTaborekConvective quality p

            steinerTaborekNucleate p heatFlux diameter roughness
            >>= fun fnb ->
                let convective = hLo * ftp
                let nucleate = nucleateReference * fnb
                ok { ConvectiveMultiplier = ftp
                     NucleateMultiplier = fnb
                     Convective = convective
                     Nucleate = nucleate
                     Coefficient =
                        ((nucleate ** 3.0 + convective ** 3.0) ** (1.0 / 3.0)) * 1.0<W/(m^2*K)> }


/// In-tube condensation.
module Condensation =

    /// Shah (1979) correlation:
    ///   h/h_lo = (1-x)^0.8 + 3.8 x^0.76 (1-x)^0.04 / pr^0.38
    /// where h_lo is Dittus-Boelter with the total flow as liquid.
    ///
    /// Widely used and well validated for pure vapours at moderate reduced
    /// pressure. It reduces exactly to h_lo at x = 0, which is a useful check.
    let shah (massVelocity: float) (quality: float) (diameter: float)
             (p: PhaseProperties) : Thermo<float<W/(m^2*K)>> =
        if quality < 0.0 || quality > 1.0 then
            fail (InvalidMixture "quality must lie in [0,1]")
        elif p.ReducedPressure <= 0.0 || p.ReducedPressure >= 1.0 then
            fail (CorrelationExtrapolated ("Shah", "reduced pressure must lie in (0,1)"))
        else
            let reLo = Flow.liquidOnlyReynolds massVelocity diameter p
            // Condensation is cooling, so the 0.4 exponent of the heating form
            // is not appropriate; Shah was fitted with Pr^0.4 nonetheless.
            let hLo =
                Flow.dittusBoelterHeating reLo p.LiquidPrandtl p.LiquidConductivity diameter
            let factor =
                (1.0 - quality) ** 0.8
                + 3.8 * (quality ** 0.76) * ((1.0 - quality) ** 0.04)
                  / (p.ReducedPressure ** 0.38)
            ok (hLo * factor * 1.0<W/(m^2*K)>)
            |> warnIf (reLo < 350.0)
                      (CorrelationExtrapolated
                        ("Shah", $"liquid-only Reynolds %.0f{reLo} below the correlated 350"))
            |> warnIf (p.ReducedPressure > 0.44)
                      (CorrelationExtrapolated
                        ("Shah", $"reduced pressure %.3f{p.ReducedPressure} above the "
                                 + "correlated 0.44"))

    /// Boyko & Kruzhilin:
    ///   h = h_lo [1 + x(rho_l/rho_v - 1)]^0.5
    ///
    /// A homogeneous-model result: simple, and the one to reach for when only
    /// densities are known. It also reduces to h_lo at x = 0.
    let boykoKruzhilin (massVelocity: float) (quality: float) (diameter: float)
                       (p: PhaseProperties) : Thermo<float<W/(m^2*K)>> =
        if quality < 0.0 || quality > 1.0 then
            fail (InvalidMixture "quality must lie in [0,1]")
        else
            let reLo = Flow.liquidOnlyReynolds massVelocity diameter p
            let hLo =
                Flow.dittusBoelterHeating reLo p.LiquidPrandtl p.LiquidConductivity diameter
            ok (hLo * sqrt (1.0 + quality * (p.DensityRatio - 1.0)) * 1.0<W/(m^2*K)>)

    /// Cavallini & Zecchin:
    ///   h = 0.05 Re_eq^0.8 Pr_l^0.33 k_l/D
    ///   Re_eq = Re_v (mu_v/mu_l)(rho_l/rho_v)^0.5 + Re_l
    ///
    /// Note this does NOT reduce to Dittus-Boelter at x = 0: the leading
    /// constant is 0.05 rather than 0.023 and the Prandtl exponent is 0.33. It
    /// is an independently fitted correlation, not a two-phase multiplier on a
    /// single-phase result, so comparing its x = 0 limit to the others is
    /// meaningless.
    let cavalliniZecchin (massVelocity: float) (quality: float) (diameter: float)
                         (p: PhaseProperties) : Thermo<float<W/(m^2*K)>> =
        if quality < 0.0 || quality > 1.0 then
            fail (InvalidMixture "quality must lie in [0,1]")
        else
            let reV = Flow.vapourReynolds massVelocity quality diameter p
            let reL = Flow.liquidReynolds massVelocity quality diameter p
            let reEq =
                reV * (p.VapourViscosity / p.LiquidViscosity)
                    * (p.DensityRatio ** 0.5)
                + reL
            ok (0.05 * (reEq ** 0.8) * (p.LiquidPrandtl ** 0.33)
                * p.LiquidConductivity / diameter * 1.0<W/(m^2*K)>)

    // ---------- multicomponent condensation ----------

    /// Silver / Bell & Ghaly effective coefficient for a condensing mixture:
    ///   1/h_eff = 1/h_cond + Z/h_gas
    ///   Z = x cp_g (dT/dh)
    ///
    /// Z is the ratio of sensible vapour cooling to total heat release. For a
    /// pure vapour Z is zero and h_eff reduces to h_cond; for a wide-boiling
    /// mixture Z can approach one and the gas-phase resistance then dominates
    /// completely. This is the single largest effect in condensing a
    /// hydrocarbon cut, and ignoring it overpredicts the coefficient severalfold.
    let silverBellGhaly (condensingCoefficient: float<W/(m^2*K)>)
                        (gasCoefficient: float<W/(m^2*K)>)
                        (z: float) : Thermo<float<W/(m^2*K)>> =
        if float condensingCoefficient <= 0.0 || float gasCoefficient <= 0.0 then
            fail (InvalidMixture "both film coefficients must be positive")
        elif z < 0.0 then
            fail (InvalidMixture "Z cannot be negative")
        else
            ok (1.0 / (1.0 / float condensingCoefficient + z / float gasCoefficient)
                * 1.0<W/(m^2*K)>)
            |> warnIf (z > 0.5)
                      (CorrelationExtrapolated
                        ("Silver-Bell-Ghaly",
                         $"Z = %.2f{z}: sensible vapour cooling dominates the heat release, "
                         + "so the effective coefficient is governed by the gas-phase "
                         + "resistance and is highly sensitive to the vapour-side estimate"))

    /// Z factor from the condensation curve: Z = x cp_g (dT/dh).
    ///
    /// `temperatureSlope` is dT/dh along the condensation path [K/(J/kg)],
    /// which comes from a flash calculation, not from a correlation.
    let zFactor (quality: float) (vapourHeatCapacity: float)
                (temperatureSlope: float) : Thermo<float> =
        if quality < 0.0 || quality > 1.0 then
            fail (InvalidMixture "quality must lie in [0,1]")
        elif temperatureSlope < 0.0 then
            fail (InvalidMixture "the condensation curve slope must be non-negative")
        else
            ok (quality * vapourHeatCapacity * temperatureSlope)

    /// Colburn & Hougen interface energy balance for condensation in the
    /// presence of a non-condensable gas.
    ///
    /// At the interface, sensible cooling of the bulk gas plus the latent heat
    /// carried by diffusing vapour must equal what the condensate film and wall
    /// can remove:
    ///
    ///   h_g (T_g - T_i) + k_g M_v h_fg ln[(P - p_i)/(P - p_v)] = h_film (T_i - T_w)
    ///
    /// This returns the RESIDUAL of that balance at a trial interface
    /// temperature. It is deliberately not solved internally: the vapour partial
    /// pressure at the interface is the saturation pressure of the mixture at
    /// T_i, which requires a flash the caller owns.
    let colburnHougenResidual (gasCoefficient: float) (filmCoefficient: float)
                              (massTransferCoefficient: float)
                              (totalPressure: float) (bulkVapourPressure: float)
                              (interfaceVapourPressure: float)
                              (molarMassVapour: float) (latentHeat: float)
                              (tGas: float<K>) (tInterface: float<K>) (tWall: float<K>)
                              : Thermo<float> =
        if totalPressure <= interfaceVapourPressure || totalPressure <= bulkVapourPressure then
            fail (InvalidMixture "a partial pressure exceeds the total pressure")
        elif interfaceVapourPressure >= bulkVapourPressure then
            fail (CorrelationExtrapolated
                    ("Colburn-Hougen",
                     "the interface vapour pressure is not below the bulk value, so no "
                     + "condensation is driven; check the trial interface temperature"))
        else
            let sensible = gasCoefficient * (float tGas - float tInterface)
            let latent =
                massTransferCoefficient * molarMassVapour * latentHeat
                * log ((totalPressure - interfaceVapourPressure)
                       / (totalPressure - bulkVapourPressure))
            let removed = filmCoefficient * (float tInterface - float tWall)
            ok (sensible + latent - removed)

    /// Solves the Colburn-Hougen balance by bisection on the interface
    /// temperature, given a function returning the vapour partial pressure at
    /// saturation for a trial interface temperature.
    ///
    /// Bisection rather than Newton because the residual is only piecewise
    /// smooth: the logarithm term stiffens sharply as the interface approaches
    /// the bulk composition, and a Newton step there overshoots out of the
    /// physical range.
    let solveColburnHougen (gasCoefficient: float) (filmCoefficient: float)
                           (massTransferCoefficient: float)
                           (totalPressure: float) (bulkVapourPressure: float)
                           (saturationPressure: float<K> -> float)
                           (molarMassVapour: float) (latentHeat: float)
                           (tGas: float<K>) (tWall: float<K>)
                           : Thermo<float<K>> =
        let residual (t: float<K>) =
            colburnHougenResidual gasCoefficient filmCoefficient massTransferCoefficient
                                  totalPressure bulkVapourPressure (saturationPressure t)
                                  molarMassVapour latentHeat tGas t tWall

        let evaluate (t: float<K>) =
            match residual t with
            | Success (v, _) -> Some v
            | Failure _ -> None

        let rec bisect (lo: float<K>) (hi: float<K>) iterations : Thermo<float<K>> =
            if iterations = 0 then
                fail (CorrelationExtrapolated
                        ("Colburn-Hougen", "the interface temperature did not converge in 100 "
                                           + "bisections; check that the bracket is valid"))
            else
                let mid = (lo + hi) / 2.0
                if abs (float hi - float lo) < 1e-6 then ok mid
                else
                    match evaluate mid, evaluate lo with
                    | Some rm, Some rl ->
                        if rm = 0.0 then ok mid
                        elif (rm > 0.0) = (rl > 0.0) then bisect mid hi (iterations - 1)
                        else bisect lo mid (iterations - 1)
                    | _ ->
                        fail (CorrelationExtrapolated
                                ("Colburn-Hougen", "the residual is undefined inside the bracket"))

        // Above the bulk dew point the interface drives no condensation and the
        // residual is undefined, so the physical bracket ends at the dew point:
        // the hottest interface temperature at which condensation is still driven.
        let upper =
            match evaluate tGas, evaluate tWall with
            | Some _, _ -> tGas
            | None, None -> tGas
            | None, Some _ ->
                let mutable lo = tWall
                let mutable hi = tGas
                for _ in 1 .. 100 do
                    let mid = (lo + hi) / 2.0
                    match evaluate mid with
                    | Some _ -> lo <- mid
                    | None -> hi <- mid
                lo

        match evaluate tWall, evaluate upper with
        | Some low, Some high when (low > 0.0) <> (high > 0.0) ->
            bisect tWall upper 100
        | Some low, Some high ->
            fail (CorrelationExtrapolated
                    ("Colburn-Hougen",
                     $"the residual does not change sign between the wall and the bulk gas "
                     + $"(%.3g{low} and %.3g{high}); the interface temperature is not bracketed"))
        | _ ->
            fail (CorrelationExtrapolated
                    ("Colburn-Hougen", "the residual is undefined at one end of the bracket"))
