namespace WhbThermo.Properties

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Entropy, Gibbs energy and chemical equilibrium for any species in the
/// database.
///
/// This was previously implemented only inside XSulfur, for the sulfur
/// allotropes. That is general physics trapped in a specific module - the same
/// duplication problem as before, in the opposite direction. It lives here now
/// and XSulfur calls it.
///
/// Above about 1000 degC dissociation stops being negligible: H2S cracks, NH3
/// decomposes, CO2 and H2O begin to dissociate, and the sulfur allotropes shift
/// continuously. None of that can be evaluated from Cp alone - it needs the
/// absolute entropy and the formation enthalpy, which is exactly what the `b`
/// coefficients of a NASA-9 fit carry. Every species in the database has them.
module Equilibrium =

    /// Standard-state pressure for equilibrium constants [bar].
    [<Literal>]
    let StandardPressureBar = 1.0

    let private selectNasa9 (species: string) (segments: Nasa9Segment list) (t: float<K>) =
        match segments |> List.tryFind (fun s -> t >= s.TMin && t <= s.TMax) with
        | Some s -> ok s
        | None ->
            match segments with
            | [] -> fail (NoCpDataAvailable species)
            | _ ->
                let nearest =
                    segments
                    |> List.minBy (fun s ->
                        if t < s.TMin then float (s.TMin - t) else float (t - s.TMax))
                let lo = segments |> List.map (fun s -> float s.TMin) |> List.min
                let hi = segments |> List.map (fun s -> float s.TMax) |> List.max
                ok nearest
                |> warn (OutsideFitRange (species, "thermochemistry", float t, lo, hi))

    /// Absolute molar entropy [J/(mol*K)] at the standard pressure.
    ///
    ///   S/R = -a1 T^-2/2 - a2 T^-1 + a3 ln T + a4 T + a5 T^2/2
    ///         + a6 T^3/3 + a7 T^4/4 + b2
    ///
    /// The b2 term is what makes this an ABSOLUTE entropy rather than a
    /// difference. A Shomate or NASA-7 fit without its integration constants
    /// cannot give it, which is why only the NASA-9 path is implemented.
    let molarEntropy (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        match sp.Cp with
        | Nasa9 segments ->
            selectNasa9 sp.Key segments t
            >>= fun seg ->
                ok (Numerics.nasa9SOverR seg.A seg.B.[1] (float t) * float Ru)
        | _ ->
            fail (NoCpDataAvailable
                    $"{sp.Key}: absolute entropy needs a NASA-9 fit with its integration \
                       constants; a Shomate or NASA-7 record cannot supply it")

    /// Absolute molar enthalpy [J/mol], including the formation enthalpy.
    ///
    /// Distinct from `PureComponent.enthalpy`, which returns an enthalpy
    /// DIFFERENCE against 298.15 K. Equilibrium needs the absolute value: the
    /// difference cancels the formation enthalpies and would make every
    /// equilibrium constant wrong.
    let molarEnthalpy (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        match sp.Cp with
        | Nasa9 segments ->
            selectNasa9 sp.Key segments t
            >>= fun seg ->
                let x = float t
                ok (Numerics.nasa9HOverRT seg.A seg.B.[0] x * float Ru * x)
        | _ ->
            fail (NoCpDataAvailable
                    $"{sp.Key}: absolute enthalpy needs a NASA-9 fit with its integration \
                       constants")

    /// Molar Gibbs energy [J/mol] at the standard pressure.
    let molarGibbs (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        molarEnthalpy sp t
        >>= fun h ->
            molarEntropy sp t
            >>= fun s -> ok (h - float t * s)

    /// A reaction as stoichiometric coefficients: negative for reactants,
    /// positive for products.
    type Reaction =
        { Name    : string
          /// (species key, stoichiometric coefficient)
          Terms   : (string * float) list }

        member this.MoleChange = this.Terms |> List.sumBy snd

    /// Standard Gibbs energy change of a reaction [J/mol].
    let reactionGibbs (db: Map<string, SpeciesData>) (reaction: Reaction) (t: float<K>)
                      : Thermo<float> =
        reaction.Terms
        |> traverseList (fun (key, nu) ->
            match Map.tryFind key db with
            | Some sp -> molarGibbs sp t >>= fun g -> ok (nu * g)
            | None -> fail (UnknownSpecies $"{key} (in reaction '{reaction.Name}')"))
        >>= fun terms -> ok (List.sum terms)

    /// Standard enthalpy change of a reaction [J/mol]. Negative is exothermic.
    let reactionEnthalpy (db: Map<string, SpeciesData>) (reaction: Reaction) (t: float<K>)
                         : Thermo<float> =
        reaction.Terms
        |> traverseList (fun (key, nu) ->
            match Map.tryFind key db with
            | Some sp -> molarEnthalpy sp t >>= fun h -> ok (nu * h)
            | None -> fail (UnknownSpecies $"{key} (in reaction '{reaction.Name}')"))
        >>= fun terms -> ok (List.sum terms)

    /// Equilibrium constant on a partial-pressure basis, referred to 1 bar:
    ///   K_p = exp(-dG / RT)
    ///
    /// Returned as the logarithm as well, because K_p spans hundreds of orders
    /// of magnitude over a WHB temperature range and the bare value overflows a
    /// double for strongly favoured reactions.
    type EquilibriumConstant =
        { Reaction    : string
          Temperature : float<K>
          LogK        : float
          /// exp(LogK), or infinity / zero where it overflows.
          K           : float
          DeltaG      : float
          DeltaH      : float }

    let equilibriumConstant (db: Map<string, SpeciesData>) (reaction: Reaction) (t: float<K>)
                            : Thermo<EquilibriumConstant> =
        reactionGibbs db reaction t
        >>= fun dG ->
            reactionEnthalpy db reaction t
            >>= fun dH ->
                let logK = -dG / (float Ru * float t)
                ok { Reaction = reaction.Name
                     Temperature = t
                     LogK = logK
                     K = (if logK > 700.0 then Double.PositiveInfinity
                          elif logK < -700.0 then 0.0
                          else exp logK)
                     DeltaG = dG
                     DeltaH = dH }
                |> warnIf (abs logK > 700.0)
                          (CorrelationExtrapolated
                            ("equilibrium constant",
                             $"ln K = %.0f{logK} for '{reaction.Name}': the reaction is "
                             + "effectively complete in one direction; use the logarithm, "
                             + "not the constant itself"))

    /// Degree of dissociation for a reaction of the form A -> n B at a given
    /// pressure, solved from K_p.
    ///
    /// Provided because "is dissociation negligible here?" is the question that
    /// actually gets asked, and answering it from a raw K_p requires care with
    /// the mole change.
    let dissociationFraction (db: Map<string, SpeciesData>) (reaction: Reaction)
                             (t: float<K>) (pressure: float<bar>) : Thermo<float> =
        if float pressure <= 0.0 then
            fail (InvalidMixture "pressure must be positive")
        else
            equilibriumConstant db reaction t
            >>= fun k ->
                let dn = reaction.MoleChange
                if abs dn < 1e-9 then
                    fail (CorrelationExtrapolated
                            ("dissociation fraction",
                             $"'{reaction.Name}' has no mole change, so the extent is not "
                             + "set by pressure; solve the full equilibrium instead"))
                else
                    // A -> n B: Kp = (n x)^n / (1-x) * (P/P0)^(n-1) / (1+(n-1)x)^(n-1)
                    // Solved by bisection on x, which is bounded in [0,1] and
                    // monotonic in Kp - no convergence question.
                    let n = dn + 1.0
                    let residual x =
                        if x <= 0.0 then -k.K
                        elif x >= 1.0 then Double.PositiveInfinity
                        else
                            let total = 1.0 + (n - 1.0) * x
                            let pA = (1.0 - x) / total * float pressure / StandardPressureBar
                            let pB = n * x / total * float pressure / StandardPressureBar
                            (pB ** n) / pA - k.K

                    if Double.IsPositiveInfinity k.K then ok 1.0
                    elif k.K <= 0.0 then ok 0.0
                    else
                        let rec bisect lo hi iterations =
                            if iterations = 0 || hi - lo < 1e-12 then (lo + hi) / 2.0
                            else
                                let mid = (lo + hi) / 2.0
                                if residual mid < 0.0 then bisect mid hi (iterations - 1)
                                else bisect lo mid (iterations - 1)
                        ok (bisect 1e-12 (1.0 - 1e-12) 200)

    /// Reactions that matter in WHB and PGC service above about 1000 degC.
    /// Named so a rating can ask "is this one significant here?" rather than
    /// assuming.
    module Reactions =

        let h2sCracking =
            { Name = "2 H2S -> 2 H2 + S2"
              Terms = [ "H2S", -2.0; "H2", 2.0; "S2", 1.0 ] }

        let sulfurDepolymerisation =
            { Name = "S8 -> 4 S2"
              Terms = [ "S8", -1.0; "S2", 4.0 ] }

        let ammoniaCracking =
            { Name = "2 NH3 -> N2 + 3 H2"
              Terms = [ "NH3", -2.0; "N2", 1.0; "H2", 3.0 ] }

        let waterGasShift =
            { Name = "CO + H2O -> CO2 + H2"
              Terms = [ "CO", -1.0; "H2O", -1.0; "CO2", 1.0; "H2", 1.0 ] }

        let clausReaction =
            { Name = "2 H2S + SO2 -> 3/2 S2 + 2 H2O"
              Terms = [ "H2S", -2.0; "SO2", -1.0; "S2", 1.5; "H2O", 2.0 ] }

        let cosHydrolysis =
            { Name = "COS + H2O -> CO2 + H2S"
              Terms = [ "COS", -1.0; "H2O", -1.0; "CO2", 1.0; "H2S", 1.0 ] }

        let sulfurDioxideDissociation =
            { Name = "2 SO2 -> S2 + 2 O2"
              Terms = [ "SO2", -2.0; "S2", 1.0; "O2", 2.0 ] }

        let all =
            [ h2sCracking; sulfurDepolymerisation; ammoniaCracking; waterGasShift
              clausReaction; cosHydrolysis; sulfurDioxideDissociation ]
