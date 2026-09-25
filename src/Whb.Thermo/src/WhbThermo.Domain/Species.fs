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
          Source   : string
          /// Critical molar volume [cm^3/mol], where tabulated. The virial
          /// pair rules need it for the critical compressibility Zc.
          Vc       : float option
          VcSource : string option }

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
    /// library actually has parameters for it. Emissivity is deliberately NOT a
    /// species property: it depends on p_i L and on the rest of the mixture.
    type RadiationRole =
        /// Absorbs and emits, and at least one model here covers it. The list
        /// names every such model; they need not be independent of each other.
        | ParticipatingCovered of models: string list
        /// Absorbs and emits, but NO open parameter set exists. Its emission is
        /// missing from any emissivity computed here, which is therefore low.
        | ParticipatingUncovered of reason: string
        /// Transparent in the infrared: homonuclear diatomics and monatomics.
        | Transparent

        member this.IsParticipating =
            match this with
            | Transparent -> false
            | _ -> true

    /// Chemical family, one per species. Used to group species and to pick
    /// models automatically; it is a classification, not a property.
    /// AcidGas is reserved for the gas-treating acid gases CO2 and H2S; the other
    /// sulfur species are SulfurCompound.
    [<RequireQualifiedAccess>]
    type SpeciesFamily =
        | PermanentGas
        | Hydrocarbon
        | Oxygenate
        | SulfurCompound
        | NitrogenCompound
        | Water
        | Inert
        | AcidGas
        /// Free radicals and atoms (H, O, OH, CH3, CN, ...): equilibrium and
        /// dissociation species with no critical point and usually no transport fit.
        | Radical
        | Other

    /// Quality of a data set, most reliable first.
    [<RequireQualifiedAccess>]
    type QualityLevel =
        /// Experimental or reference-grade (NASA CEA thermochemistry, evaluated critical constants).
        | A
        /// Published correlation fitted to evaluated data (NASA CEA transport, DIPPR).
        | B
        /// Estimated or crude fit (Chung, legacy two-parameter Sutherland fits).
        | C
        /// Engineering fallback (single-point anchor values).
        | D

        /// The run-time provenance a value from this data carries: A is measured,
        /// B fitted, C and D are estimates and not quotable.
        member this.Provenance =
            match this with
            | QualityLevel.A -> Measured
            | QualityLevel.B -> Fitted
            | QualityLevel.C | QualityLevel.D -> Estimated

    /// Whether a data set has been checked against an independent reference.
    type ValidationStatus =
        | Verified of reference: string
        | Unverified
        /// Checked, and known to deviate; the note states by how much and where.
        | KnownDeviation of note: string

    /// Quality of each data group of a species. None where the group is absent.
    type SpeciesDataQuality =
        { Thermo         : QualityLevel
          Transport      : QualityLevel option
          Critical       : QualityLevel option
          VapourPressure : QualityLevel option
          Validation     : ValidationStatus }

    /// Equations of state a parameter set can belong to.
    [<RequireQualifiedAccess>]
    type EquationOfState =
        | IdealGas
        /// Truncated virial, second coefficient from corresponding states (Pitzer).
        | Virial
        | PengRobinson
        | Srk

    /// Species parameters for one equation of state. The critical constants
    /// stay in CriticalProperties; a set carries only what that equation needs
    /// beyond them, so changing EOS never means editing the species record.
    type EosParameterSet =
        { Eos          : EquationOfState
          /// Where the second virial coefficient comes from when it is not the
          /// corresponding-states correlation, e.g. "IAPWS-IF97 region 2" for water.
          SecondVirial : string option
          /// Named extra parameters (volume translation, alpha-function constants...).
          Extra        : Map<string, float>
          Source       : string }

    /// A binary interaction parameter k_ij. It belongs to a PAIR of species and
    /// one equation of state, never to a single species, so it lives in its own
    /// table (binary-interaction.json), symmetric in the two ids.
    type BinaryInteractionParameter =
        { Species1 : string
          Species2 : string
          Eos      : EquationOfState
          Kij      : float
          TMin     : float<K>
          TMax     : float<K>
          Source   : string }

    /// Lennard-Jones 12-6 potential parameters, for Chapman-Enskog viscosity,
    /// conductivity and diffusivity.
    type LennardJonesParameters =
        { /// Collision diameter [angstrom].
          Sigma        : float
          EpsilonOverK : float<K>
          Source       : string }

    /// Phase-change points. Each is optional on its own: a radical has none of
    /// them, and a gas like H2 has no normal boiling point worth a correlation.
    type PhaseData =
        { NormalBoilingPoint : float<K> option
          MeltingPoint       : float<K> option
          /// Triple point temperature and pressure.
          TriplePoint        : (float<K> * float<bar>) option
          Source             : string option
          /// Why the points are absent, where they are.
          Unavailable        : string option }

    /// Damage mechanisms a species can drive in a WHB / PGC. These are screening
    /// flags, not verdicts: whether a mechanism is active depends on partial
    /// pressure, temperature, carbon or sulfur activity, and the material.
    type MaterialInteraction =
        { /// High-temperature hydrogen attack or hydrogen embrittlement.
          HydrogenService : bool
          Nitriding       : bool
          Carburizing     : bool
          MetalDusting    : bool
          Sulfidation     : bool
          Oxidation       : bool
          Notes           : string }

    /// Process-safety classification. The flags are screening; the numeric
    /// limits are for 25 degC and 1 atm in air and do not hold at process
    /// temperature, and are absent unless taken from a checked source.
    type SafetyData =
        { Flammable               : bool
          Toxic                   : bool
          Corrosive               : bool
          /// Lower and upper flammability limits in air [vol %].
          LowerFlammabilityLimit  : float option
          UpperFlammabilityLimit  : float option
          AutoIgnitionTemperature : float<K> option
          Source                  : string }

    /// Reference state of the whole database. Thermochemistry from NASA-9 is on
    /// a 1 bar standard state, not 1 atm: mixing the two shifts every entropy by
    /// R ln(1.01325) and every equilibrium constant with it.
    type ReferenceState =
        { Temperature        : float<K>
          Pressure           : float<bar>
          EnthalpyConvention : string
          EntropyConvention  : string }

    type SpeciesData =
        { /// Immutable identifier. Never renamed once published: reports, input
          /// files and other databases refer to species by it.
          Key          : string
          Name         : string
          Synonyms     : string list
          Family       : SpeciesFamily
          /// Molecular formula. Distinct from Key: DME has key "DME" and
          /// formula "C2H6O", atomic sulfur has key "S1" and formula "S".
          Formula      : string
          /// Atom counts by element symbol, e.g. CO2 -> [ "C", 1; "O", 2 ].
          Elements     : (string * int) list
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
          Radiation           : RadiationRole
          Quality             : SpeciesDataQuality
          /// EOS-specific parameters beyond the critical constants. Empty for
          /// most species: the critical constants are all the cubic and virial
          /// equations need.
          EosParameters       : EosParameterSet list
          LennardJones        : LennardJonesParameters option
          /// Why the Lennard-Jones parameters are absent, where they are.
          LennardJonesUnavailable : string option
          Phase               : PhaseData
          /// None when the species has not been classified; false flags in a
          /// classified record mean "not a driver", absence means "unknown".
          MaterialInteraction : MaterialInteraction option
          Safety              : SafetyData option }

        /// The parameter set of one equation of state, if the species has one.
        member this.EosParametersFor (eos: EquationOfState) =
            this.EosParameters |> List.tryFind (fun p -> p.Eos = eos)

        /// The immutable identifier; the same value as Key.
        member this.Id = this.Key

        /// Total number of atoms in the molecule.
        member this.AtomCount = this.Elements |> List.sumBy snd

        /// True when the record is backed by a published temperature-dependent Cp fit.
        member this.IsProductionGrade =
            match this.Cp with
            | Shomate (_ :: _) | Nasa7 (_ :: _) | Nasa9 (_ :: _) -> true
            | _ -> false

    module Formula =
        /// Atom counts of a simple molecular formula ("CO2", "CH4O", "S").
        /// Element symbols are one capital plus optional lower-case letters;
        /// parentheses and charges are not supported and are rejected.
        let elements (formula: string) : Result<(string * int) list, string> =
            if System.String.IsNullOrWhiteSpace formula then Error "empty formula"
            else
                let s = formula.Trim()
                let rec go i (acc: (string * int) list) =
                    if i >= s.Length then Ok (List.rev acc)
                    elif not (System.Char.IsUpper s.[i]) then
                        Error $"'{formula}': unexpected '{s.[i]}' at position {i}"
                    else
                        let mutable j = i + 1
                        while j < s.Length && System.Char.IsLower s.[j] do j <- j + 1
                        let symbol = s.Substring(i, j - i)
                        let mutable k = j
                        while k < s.Length && System.Char.IsDigit s.[k] do k <- k + 1
                        let count = if k = j then 1 else int (s.Substring(j, k - j))
                        go k ((symbol, count) :: acc)
                go 0 []
                |> Result.map (fun pairs ->
                    // "CH3OH"-style formulas repeat a symbol; merge in first-seen order.
                    pairs
                    |> List.groupBy fst
                    |> List.map (fun (symbol, xs) -> symbol, xs |> List.sumBy snd))

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

        /// Mass fractions of the components, in the order of the mixture.
        let massFractions (mixture: Mixture) =
            let components = mixture.Components
            let total = components |> List.sumBy (fun (sp, y) -> y * float sp.MolarMass)
            components |> List.map (fun (sp, y) -> sp, y * float sp.MolarMass / total)
