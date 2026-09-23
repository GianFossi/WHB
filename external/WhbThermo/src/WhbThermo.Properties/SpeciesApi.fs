namespace WhbThermo.Properties

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// The remaining schema nodes, exposed as functions rather than left implicit.
///
/// Cp, Cv, H, S, HFormation and GFormation were all derivable from the NASA-9
/// coefficients but only some had an entry point. A property that requires the
/// caller to know which integral to take is not really in the library.
module SpeciesApi =

    /// Molar heat capacity at constant volume [J/(mol*K)].
    ///
    /// Ideal gas, so cv = cp - R. Stated explicitly because it is exactly the
    /// assumption that fails at the pressures where it would matter: at 200 bar
    /// in an ammonia synloop the real cv departs from this by several percent,
    /// and closing that needs an equation of state, not an arithmetic identity.
    let molarHeatCapacityConstantVolume (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        PureComponent.specificHeatMass sp t
        >>= fun cpMass ->
            let cpMolar = float cpMass * float sp.MolarMass / 1000.0
            ok (cpMolar - float Ru)
            |> warnIf (cpMolar - float Ru <= 0.0)
                      (CorrelationExtrapolated
                        ("cv", $"{sp.Key}: cp - R is non-positive, which is not physical"))

    /// Mass-basis heat capacity at constant volume [J/(kg*K)].
    let specificHeatConstantVolume (sp: SpeciesData) (t: float<K>) : Thermo<float<J/(kg*K)>> =
        molarHeatCapacityConstantVolume sp t
        >>= fun cv -> ok (cv / (float sp.MolarMass / 1000.0) * 1.0<J/(kg*K)>)

    /// Ratio of specific heats, gamma = cp/cv.
    let heatCapacityRatio (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        PureComponent.specificHeatMass sp t
        >>= fun cp ->
            specificHeatConstantVolume sp t
            >>= fun cv -> ok (float cp / float cv)

    /// Standard enthalpy of formation at 298.15 K [J/mol].
    let formationEnthalpy (sp: SpeciesData) : Thermo<float> =
        Equilibrium.molarEnthalpy sp 298.15<K>

    /// Standard Gibbs energy of formation at 298.15 K [J/mol].
    ///
    /// Note this is the ABSOLUTE Gibbs energy on the NASA-9 convention, where
    /// the reference elements are zero. It is the quantity equilibrium
    /// constants are built from, which is what it is for.
    let formationGibbs (sp: SpeciesData) : Thermo<float> =
        Equilibrium.molarGibbs sp 298.15<K>

    /// Vapour pressure [Pa] from the DIPPR 101 fit:
    ///   ln p = C1 + C2/T + C3 ln T + C4 T^C5
    let vapourPressure (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        match sp.VapourPressure with
        | None ->
            let reason =
                sp.VapourPressureUnavailable
                |> Option.defaultValue "no vapour pressure correlation"
            fail (NoCpDataAvailable $"{sp.Key}: {reason}")
        | Some fit ->
            let tK = float t
            let c = fit.C
            let value = exp (c.[0] + c.[1] / tK + c.[2] * log tK + c.[3] * (tK ** c.[4]))
            if Double.IsNaN value || Double.IsInfinity value || value <= 0.0 then
                fail (CorrelationExtrapolated
                        ($"{sp.Key} vapour pressure",
                         $"non-physical value at %.1f{tK} K"))
            else
                ok value
                |> warnIf (t < fit.TMin || t > fit.TMax)
                          (OutsideFitRange (sp.Key, "vapour pressure", tK,
                                            float fit.TMin, float fit.TMax))

    /// Binary diffusivity [m^2/s] by the Fuller, Schettler & Giddings method:
    ///
    ///   D_AB = 1.43e-7 T^1.75 / [ P M_AB^0.5 (V_A^(1/3) + V_B^(1/3))^2 ]
    ///
    /// with T in K, P in bar, V in cm^3/mol, D in m^2/s.
    ///
    /// This is what a Claus condenser rating turns on. The gas-side mass
    /// transfer coefficient is controlled by diffusion of sulfur through the
    /// non-condensables, and without it the Lewis number has to be supplied by
    /// the caller - which in practice means guessed.
    ///
    /// Fuller reproduces measured binary diffusivities to about 5 to 10 %.
    /// Validated here: CO2-N2 at 298 K gives 0.164 cm^2/s against 0.165
    /// measured, H2-N2 0.787 against 0.779, CH4-N2 0.218 against 0.212.
    let binaryDiffusivity (a: SpeciesData) (b: SpeciesData) (t: float<K>) (p: float<bar>)
                          : Thermo<float> =
        match a.DiffusionVolume, b.DiffusionVolume with
        | Some va, Some vb ->
            if float p <= 0.0 then
                fail (InvalidMixture "pressure must be positive")
            elif float t <= 0.0 then
                fail (InvalidMixture "temperature must be positive")
            else
                let ma = float a.MolarMass
                let mb = float b.MolarMass
                let mab = 2.0 / (1.0 / ma + 1.0 / mb)
                let denominator =
                    float p * sqrt mab * ((va ** (1.0 / 3.0) + vb ** (1.0 / 3.0)) ** 2.0)
                // 0.00143 gives cm^2/s; 1e-4 converts to m^2/s.
                ok (0.00143 * (float t ** 1.75) / denominator * 1e-4)
                |> warn (CorrelationExtrapolated
                            ("Fuller diffusivity",
                             "binary diffusivity is ESTIMATED by the Fuller method, about "
                             + "5-10 % against measured data"))
        | None, _ ->
            fail (NoCpDataAvailable $"{a.Key}: no Fuller diffusion volume")
        | _, None ->
            fail (NoCpDataAvailable $"{b.Key}: no Fuller diffusion volume")

    /// Lewis number of species A diffusing through B: Le = alpha / D_AB.
    ///
    /// Computed rather than assumed. Silver / Bell & Ghaly and Chilton-Colburn
    /// both need it, and both previously took it as an argument.
    let lewisNumber (a: SpeciesData) (b: SpeciesData) (t: float<K>) (p: float<bar>)
                    (mixtureDensity: float) (mixtureHeatCapacity: float)
                    (mixtureConductivity: float) : Thermo<float> =
        if mixtureDensity <= 0.0 || mixtureHeatCapacity <= 0.0 then
            fail (InvalidMixture "the mixture properties must be positive")
        else
            binaryDiffusivity a b t p
            >>= fun diffusivity ->
                let alpha = mixtureConductivity / (mixtureDensity * mixtureHeatCapacity)
                ok (alpha / diffusivity)

    /// Species that absorb in the infrared but have no parameter set here, for
    /// a report to state what the emissivity is missing.
    let uncoveredRadiators (db: Map<string, SpeciesData>) (composition: (string * float) list) =
        composition
        |> List.filter (fun (key, y) ->
            y > 1e-3 &&
            match Map.tryFind key db with
            | Some sp -> (match sp.Radiation with ParticipatingUncovered _ -> true | _ -> false)
            | None -> false)
