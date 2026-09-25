namespace XSulfur

open System
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// Reactive equilibrium of gas-phase sulfur allotropes.
///
/// Elemental sulfur vapour is not a component, it is a reacting mixture. The
/// allotropes S1 to S8 sit in continuous equilibrium
///
///     n/2 S2  <->  S_n
///
/// and the distribution shifts strongly with temperature and with sulfur partial
/// pressure. Below roughly 700 degF the vapour is dominated by S6 and S8; above
/// roughly 1000 degF it is nearly all S2. The average molar mass therefore runs
/// from about 250 g/mol at the cold end of a Claus condenser to about 65 g/mol
/// at the hot end.
///
/// The engineering consequence is that a sulfur condenser cannot be rated with a
/// fixed molar mass, a fixed cp or a single latent heat. Cooling the vapour
/// drives polymerisation, and that reaction enthalpy is released alongside the
/// sensible and latent terms. Treating sulfur as a pseudo-component with a
/// constant molecular weight understates the heat release in the polymerising
/// range, which is exactly the range a Claus condenser operates in.
module Speciation =

    [<Literal>]
    let DefaultFile = "sulfur-species.json"

    // ---------- data ----------

    [<CLIMutable>]
    type private SegmentDto =
        { tMinK: float; tMaxK: float; a: float[]; b: float[]; source: string }

    /// An allotrope is either a reference to species-database.json by `id`
    /// (the normal case: the gas-phase coefficients live there only) or carries
    /// its own NASA-9 segments (the liquid reference phase, and test fixtures).
    [<CLIMutable>]
    type private SpeciesDto =
        { key: string; id: string option; atoms: int option
          molarMass_g_mol: float option; nasa9Segments: SegmentDto[] option }

    [<CLIMutable>]
    type private RootDto =
        { atomicMass_g_mol: float; source: string; species: SpeciesDto[]
          liquid: SpeciesDto }

    type Allotrope =
        { Key       : string
          Atoms     : int
          MolarMass : float          // g/mol
          Segments  : (float<K> * float<K> * float[] * float[]) list }

    type Model =
        { Allotropes : Allotrope list
          /// Liquid sulfur, the reference phase for vapour pressure. Optional:
          /// speciation works without it, vapour pressure does not.
          Liquid     : Allotrope option
          AtomicMass : float
          Source     : string }

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions.Default()
                    .WithSkippableOptionFields(SkippableOptionFields.Always,
                                               deserializeNullAsNone = true)))
        o

    let private inlineAllotrope (s: SpeciesDto) (segments: SegmentDto[]) =
        match s.atoms, s.molarMass_g_mol with
        | Some atoms, _ when atoms < 1 || atoms > 8 ->
            fail (DatabaseParseError $"sulfur allotrope '{s.key}': implausible atom count {atoms}")
        | Some atoms, Some molarMass ->
            if segments |> Array.exists (fun g ->
                   isNull g.a || g.a.Length <> 7 || isNull g.b || g.b.Length <> 2) then
                fail (DatabaseParseError $"sulfur allotrope '{s.key}': malformed NASA-9 segment")
            else
                ok { Key = s.key
                     Atoms = atoms
                     MolarMass = molarMass
                     Segments =
                        segments
                        |> Array.map (fun g ->
                            g.tMinK * 1.0<K>, g.tMaxK * 1.0<K>, Array.copy g.a, Array.copy g.b)
                        |> List.ofArray }
        | _ ->
            fail (DatabaseParseError
                    $"sulfur allotrope '{s.key}': inline NASA-9 data needs atoms and molarMass_g_mol")

    /// Resolves an allotrope from the species database by id. The atom count is
    /// taken from the database elements and, where the file also states it,
    /// checked against it.
    let private referencedAllotrope (db: Map<string, SpeciesData>) (s: SpeciesDto) (id: string) =
        match Map.tryFind id db with
        | None -> fail (UnknownSpecies $"{id} (sulfur allotrope '{s.key}')")
        | Some sp ->
            let sulfurAtoms =
                match sp.Elements with
                | [ ("S", n) ] -> Some n
                | _ -> Option.None
            match sp.Cp, sulfurAtoms with
            | _, Option.None ->
                fail (DatabaseParseError $"sulfur allotrope '{s.key}': species '{id}' is not pure sulfur")
            | _, Some n when s.atoms |> Option.exists (fun a -> a <> n) ->
                fail (DatabaseParseError
                        $"sulfur allotrope '{s.key}': states {s.atoms.Value} atoms, species '{id}' has {n}")
            | Nasa9 segments, Some n ->
                ok { Key = s.key
                     Atoms = n
                     MolarMass = float sp.MolarMass
                     Segments = segments |> List.map (fun g -> g.TMin, g.TMax, Array.copy g.A, Array.copy g.B) }
            | _ ->
                fail (DatabaseParseError $"sulfur allotrope '{s.key}': species '{id}' has no NASA-9 fit")

    /// Parses the sulfur data file. Allotropes given by `id` are resolved against
    /// `db`; without a database only fully inline allotropes can be read.
    let parseWith (db: Map<string, SpeciesData> option) (json: string) : Thermo<Model> =
        try
            let root = JsonSerializer.Deserialize<RootDto>(json, options)
            let toAllotrope (s: SpeciesDto) =
                match s.nasa9Segments, s.id, db with
                | Some segments, _, _ when segments.Length > 0 -> inlineAllotrope s segments
                | _, Some id, Some db -> referencedAllotrope db s id
                | _, Some id, Option.None ->
                    fail (DatabaseParseError
                            $"sulfur allotrope '{s.key}' refers to '{id}' but no species database was given")
                | _ -> fail (DatabaseParseError $"sulfur allotrope '{s.key}': no NASA-9 segments and no id")

            root.species
            |> List.ofArray
            |> traverseList toAllotrope
            >>= fun allotropes ->
                if allotropes |> List.exists (fun a -> a.Key = "S2") then
                    let liquid =
                        if isNull (box root.liquid) || isNull root.liquid.key then ok Option.None
                        else toAllotrope root.liquid >>= fun l -> ok (Some l)
                    liquid
                    >>= fun l ->
                        ok { Allotropes = allotropes
                             Liquid = l
                             AtomicMass = root.atomicMass_g_mol
                             Source = root.source }
                else
                    fail (DatabaseParseError "sulfur database has no S2, which is the reference species")
        with ex ->
            fail (DatabaseParseError ex.Message)

    /// Parses a sulfur file whose allotropes are all inline.
    let parse (json: string) : Thermo<Model> = parseWith Option.None json

    /// Loads the default sulfur file, resolving the gas-phase allotropes against
    /// the species database. The raw text is cached by DataStore; the model is
    /// rebuilt on each call so an edited species database is never stale here.
    let load () : Thermo<Model> =
        SpeciesDatabase.load ()
        >>= fun db -> DataStore.loadText DefaultFile >>= parseWith (Some db)

    let loadFile (path: string) : Thermo<Model> =
        SpeciesDatabase.load ()
        >>= fun db -> DataStore.loadText path >>= parseWith (Some db)

    // ---------- thermodynamic functions ----------

    let private segmentFor (a: Allotrope) (t: float<K>) =
        a.Segments
        |> List.tryFind (fun (lo, hi, _, _) -> t >= lo && t <= hi)
        |> Option.defaultValue (
            a.Segments
            |> List.minBy (fun (lo, hi, _, _) ->
                if t < lo then float (lo - t) else float (t - hi)))

    /// Molar enthalpy [J/mol] from the NASA-9 fit.
    let enthalpy (a: Allotrope) (t: float<K>) =
        let (_, _, c, b) = segmentFor a t
        let x = float t
        Numerics.nasa9HOverRT c b.[0] x * float Ru * x

    /// Molar entropy [J/(mol*K)] from the NASA-9 fit.
    let entropy (a: Allotrope) (t: float<K>) =
        let (_, _, c, b) = segmentFor a t
        Numerics.nasa9SOverR c b.[1] (float t) * float Ru

    /// Molar Gibbs energy [J/mol].
    let gibbs (a: Allotrope) (t: float<K>) =
        enthalpy a t - float t * entropy a t

    // ---------- equilibrium ----------

    type Distribution =
        { Temperature       : float<K>
          SulfurPartialPressure : float<bar>
          /// Mole fraction of each allotrope within the sulfur vapour.
          Fractions         : (string * float) list
          /// Average molar mass of the sulfur vapour [g/mol].
          AverageMolarMass  : float
          /// Molar enthalpy per MOLE OF SULFUR ATOMS [J/mol-atom]. Referring
          /// enthalpy to atoms rather than to molecules is what makes the
          /// polymerisation enthalpy appear naturally: the atom count is
          /// conserved along the condensation path while the molecule count is
          /// not.
          EnthalpyPerAtom   : float }

    /// Solve the allotrope distribution at a given temperature and sulfur
    /// partial pressure.
    ///
    /// Every allotrope is referred to S2:
    ///     p_n = exp[-(g_n - (n/2) g_S2) / RT] * p_S2^(n/2)
    /// and p_S2 is found by bisection on log p so that the partial pressures sum
    /// to the specified total. Bisection on the logarithm because the partial
    /// pressures span many orders of magnitude and the sum is steeply monotonic
    /// in p_S2 - a Newton iteration on the linear variable overshoots into
    /// negative pressures at the cold end.
    let distribution (model: Model) (t: float<K>) (sulfurPressure: float<bar>)
                     : Thermo<Distribution> =
        if float sulfurPressure <= 0.0 then
            fail (InvalidMixture "sulfur partial pressure must be positive")
        elif float t < 250.0 || float t > 2000.0 then
            fail (OutsideFitRange ("sulfur speciation", "temperature", float t, 250.0, 2000.0))
        else
            let rt = float Ru * float t
            let reference =
                model.Allotropes |> List.find (fun a -> a.Key = "S2")
            let gReference = gibbs reference t

            // Coefficient of p_S2^(n/2) for each allotrope.
            let coefficients =
                model.Allotropes
                |> List.map (fun a ->
                    let power = float a.Atoms / 2.0
                    let deltaG = gibbs a t - power * gReference
                    a, power, exp (-deltaG / rt))

            let total pS2 =
                coefficients |> List.sumBy (fun (_, power, k) -> k * (pS2 ** power))

            let target = float sulfurPressure

            // Logarithmic bisection with a real convergence test. The partial
            // pressures span thirty decades, so bisecting the linear variable
            // would spend most of its iterations in the wrong one; and a fixed
            // iteration count returns a midpoint whether or not it means
            // anything, which is how an impossible input reaches a report.
            let solved =
                Numerics.bisectLog (fun p -> total p - target)
                                   1e-30 (target * 10.0) 1e-12 200
                                   $"sulfur speciation at %.1f{float t} K"

            solved
            >>= fun solution ->
            let pS2 = solution.Root
            let partials =
                coefficients |> List.map (fun (a, power, k) -> a, k * (pS2 ** power))
            let sum = partials |> List.sumBy snd

            if sum <= 0.0 || Double.IsNaN sum then
                fail (CorrelationExtrapolated
                        ("sulfur speciation",
                         $"the equilibrium solve gave a non-physical total at %.1f{float t} K"))
            else
                let fractions = partials |> List.map (fun (a, p) -> a, p / sum)
                let averageMolarMass = fractions |> List.sumBy (fun (a, y) -> y * a.MolarMass)
                // Enthalpy per mole of atoms: sum(y_n H_n) / sum(y_n n)
                let atomsPerMole = fractions |> List.sumBy (fun (a, y) -> y * float a.Atoms)
                let enthalpyPerMole = fractions |> List.sumBy (fun (a, y) -> y * enthalpy a t)

                ok { Temperature = t
                     SulfurPartialPressure = sulfurPressure
                     Fractions = fractions |> List.map (fun (a, y) -> a.Key, y)
                     AverageMolarMass = averageMolarMass
                     EnthalpyPerAtom = enthalpyPerMole / atomsPerMole }
                |> warnIf (not solution.Converged)
                          (CorrelationExtrapolated
                            ("sulfur speciation",
                             $"the pressure balance did not converge in {solution.Iterations} iterations"))

    /// Effective heat capacity per mole of sulfur atoms [J/(mol-atom*K)],
    /// INCLUDING the polymerisation enthalpy.
    ///
    /// Evaluated as a numerical derivative of the equilibrium enthalpy rather
    /// than as a mole-weighted average of the pure-species cp values, because
    /// the latter omits the reaction term entirely. In the polymerising range
    /// the two differ by a large factor, and it is the reaction term that a
    /// fixed-molecular-weight model throws away.
    let effectiveHeatCapacity (model: Model) (t: float<K>) (sulfurPressure: float<bar>)
                              : Thermo<float> =
        let step = 0.5<K>
        distribution model (t + step) sulfurPressure
        >>= fun hot ->
            distribution model (t - step) sulfurPressure
            >>= fun cold ->
                ok ((hot.EnthalpyPerAtom - cold.EnthalpyPerAtom) / (2.0 * float step))

    /// Frozen heat capacity per mole of atoms: the mole-weighted average with
    /// the composition held fixed. Provided for comparison, so the size of the
    /// reaction contribution can be seen rather than assumed.
    let frozenHeatCapacity (model: Model) (t: float<K>) (sulfurPressure: float<bar>)
                           : Thermo<float> =
        distribution model t sulfurPressure
        >>= fun d ->
            let lookup key = model.Allotropes |> List.find (fun a -> a.Key = key)
            let step = 0.5<K>
            let atomsPerMole =
                d.Fractions |> List.sumBy (fun (k, y) -> y * float (lookup k).Atoms)
            let derivative =
                d.Fractions
                |> List.sumBy (fun (k, y) ->
                    let a = lookup k
                    y * (enthalpy a (t + step) - enthalpy a (t - step)) / (2.0 * float step))
            ok (derivative / atomsPerMole)
