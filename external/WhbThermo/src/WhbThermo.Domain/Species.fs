namespace WhbThermo.Domain

/// Core data model for the process-gas species database.
///
/// Design rules:
///   1. Every fitted correlation carries its own validity range and provenance.
///   2. Cp is modelled explicitly: either a real published fit, or an honest
///      single-point anchor that the loader flags as a WARNING.
///   3. No numeric constant is hard-coded in an F# `match` -- the database is data.
[<AutoOpen>]
module Species =

    /// Two-parameter Sutherland fit, used for both viscosity and conductivity.
    ///   phi(T) = phi0 * (Tref + S)/(T + S) * (T/Tref)^1.5
    type SutherlandFit =
        { Coeff0      : float          // Pa*s for viscosity, W/(m*K) for conductivity
          SutherlandK : float<K>
          TRef        : float<K>
          TMin        : float<K>
          TMax        : float<K>
          Source      : string }

    /// NIST Shomate segment. Cp [J/(mol*K)] = A + B*t + C*t^2 + D*t^3 + E/t^2, t = T[K]/1000.
    type ShomateSegment =
        { TMin   : float<K>
          TMax   : float<K>
          A: float; B: float; C: float; D: float
          E: float; F: float; G: float; H: float
          Source : string }

    /// NASA-7 polynomial segment (CHEMKIN / Burcat format).
    ///   Cp/R      = a1 + a2*T + a3*T^2 + a4*T^3 + a5*T^4
    ///   H/(R*T)   = a1 + a2*T/2 + a3*T^2/3 + a4*T^3/4 + a5*T^4/5 + a6/T
    ///   S/R       = a1*ln(T) + a2*T + a3*T^2/2 + a4*T^3/3 + a5*T^4/4 + a7
    type Nasa7Segment =
        { TMin : float<K>
          TMax : float<K>
          /// Exactly seven coefficients a1..a7, in order.
          A      : float[]
          Source : string }

    /// NASA-9 polynomial segment (CEA / PAC format), exponents -2..4.
    ///   Cp/R    = a1 T^-2 + a2 T^-1 + a3 + a4 T + a5 T^2 + a6 T^3 + a7 T^4
    ///   H/(R T) = -a1 T^-2 + a2 ln(T)/T + a3 + a4 T/2 + a5 T^2/3 + a6 T^3/4
    ///             + a7 T^4/5 + b1/T
    /// Preferred over both Shomate and NASA-7: the T^-2 and T^-1 terms fit the
    /// low range properly and the CEA tables run to 6000 K.
    type Nasa9Segment =
        { TMin   : float<K>
          TMax   : float<K>
          A      : float[]   // exactly 7
          B      : float[]   // exactly 2
          Source : string }

    /// How Cp is known for a given species.
    type CpModel =
        /// Published temperature-dependent fit. Production quality.
        | Shomate of ShomateSegment list
        /// NASA-7 polynomial fit (Burcat / CHEMKIN). Production quality.
        | Nasa7 of Nasa7Segment list
        /// NASA-9 polynomial fit (NASA CEA thermo.inp). Highest quality available here.
        | Nasa9 of Nasa9Segment list
        /// Single measured point at 500 degC, no temperature dependence.
        /// Acceptable for feasibility studies only -- always emits a warning.
        | AnchorOnly of cpAt500C: float * source: string

    type CriticalProperties =
        { Tc       : float<K>
          Pc       : float<bar>
          Acentric : float
          Source   : string }

    /// NASA CEA transport interval:
    ///   ln(phi) = A ln T + B/T + C/T^2 + D
    /// with phi in micropoise (viscosity) or microwatt/(cm*K) (conductivity).
    type NasaTransportInterval =
        { TMin : float<K>
          TMax : float<K>
          A: float; B: float; C: float; D: float
          Source : string }

    /// Which transport correlation a species carries. NASA CEA is four-parameter
    /// and tabulated to 5000 K; the manual's Sutherland fits are two-parameter
    /// and degrade above roughly 1000 degC, so they are the fallback only.
    type TransportModel =
        | NasaCea of viscosity: NasaTransportInterval list
                   * conductivity: NasaTransportInterval list
        | SutherlandPair of viscosity: SutherlandFit * conductivity: SutherlandFit
        /// No transport data of any kind. Deliberate, not an oversight: for a
        /// species with no measured fit, fabricating one would produce numbers
        /// indistinguishable from data. Viscosity and conductivity calls fail
        /// with the recorded reason instead.
        | NoTransportData of reason: string

    /// DIPPR 101 vapour pressure: ln p[Pa] = C1 + C2/T + C3 ln T + C4 T^C5
    type VapourPressureFit =
        { C      : float[]
          TMin   : float<K>
          TMax   : float<K>
          Source : string }

    /// Whether a species participates in thermal radiation, and whether the
    /// library actually has parameters for it.
    type RadiationRole =
        /// Absorbs and emits, and the WSGG model covers it.
        | ParticipatingCovered of model: string
        /// Absorbs and emits, but NO open parameter set exists. Its emission is
        /// missing from any emissivity computed here, which is therefore low.
        | ParticipatingUncovered of reason: string
        /// Transparent in the infrared: homonuclear diatomics and monatomics.
        | Transparent

        member this.IsParticipating =
            match this with
            | Transparent -> false
            | _ -> true

    type SpeciesData =
        { Key          : string
          Name         : string
          /// Molecular formula. Distinct from Key: DME has key "DME" and
          /// formula "C2H6O", atomic sulfur has key "S1" and formula "S".
          Formula      : string
          Cas          : string option
          MolarMass    : float<kg/kmol>
          /// Legacy Sutherland fallback. Absent for species added from CEA,
          /// where the NASA correlations are the only transport source.
          Viscosity    : SutherlandFit option
          Conductivity : SutherlandFit option
          Transport    : TransportModel
          Cp           : CpModel
          Critical     : CriticalProperties option
          /// Reason the critical properties are absent, where they are. A
          /// radical has no critical point at all - that is physics, not a gap.
          CriticalUnavailable : string option
          VapourPressure      : VapourPressureFit option
          VapourPressureUnavailable : string option
          /// Fuller diffusion volume [cm^3/mol], for binary diffusivities.
          DiffusionVolume     : float option
          Radiation           : RadiationRole }

        /// True when the record is backed by a published temperature-dependent Cp fit.
        member this.IsProductionGrade =
            match this.Cp with
            | Shomate (_ :: _) | Nasa7 (_ :: _) | Nasa9 (_ :: _) -> true
            | _ -> false

    /// A validated gas mixture: mole fractions summing to 1.
    type Mixture =
        private { Fractions: (SpeciesData * float) list }
        member this.Components = this.Fractions

    /// Evaluated mixture properties at a given (T, P).
    type MixtureProperties =
        { MolarMass           : float<kg/kmol>
          Density             : float<kg/m^3>
          Viscosity           : float<Pa*s>
          ThermalConductivity : float<W/(m*K)>
          SpecificHeat        : float<J/(kg*K)>
          Prandtl             : float }

    module Mixture =
        /// Smart constructor. Normalises mole fractions and rejects degenerate input.
        /// Returns Error for structurally invalid mixtures; the caller lifts this
        /// into the ROP pipeline.
        let create (components: (SpeciesData * float) list) =
            match components with
            | [] -> Error "Mixture is empty"
            | _ when components |> List.exists (fun (_, y) -> y < 0.0) ->
                Error "Mixture contains a negative mole fraction"
            | _ ->
                let total = components |> List.sumBy snd
                if total <= 1e-12 then Error "Mixture mole fractions sum to zero"
                else Ok { Fractions = components |> List.map (fun (sp, y) -> sp, y / total) }
