namespace WhbThermo.Properties

open System
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// The reaction database, data/reactions.json.
///
/// Reactions are not species properties: a reaction belongs to several species
/// at once, so it lives in its own file, keyed by an immutable id, with its
/// stoichiometry written by species id. Equilibrium constants are never stored;
/// they are computed from the species' NASA-9 data, which keeps them consistent
/// with every enthalpy the library returns.
module ReactionDatabase =

    /// How the equilibrium constant of a reaction is obtained.
    [<RequireQualifiedAccess>]
    type EquilibriumModel =
        /// K = exp(-dG/RT) from the NASA-9 Gibbs energies of the species.
        | Nasa9Gibbs

    type ReactionRecord =
        { Id               : string
          /// The reaction as the equilibrium functions take it; Name is the equation.
          Reaction         : Equilibrium.Reaction
          EquilibriumModel : EquilibriumModel
          /// Name of a kinetic model, where one exists. None means equilibrium only.
          KineticModel     : string option
          TMin             : float<K>
          TMax             : float<K>
          Source           : string
          Note             : string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private ValidityDto = { tMinK: float; tMaxK: float }

    [<CLIMutable; NoComparison; NoEquality>]
    type private ReactionDto =
        { id: string; equation: string; stoichiometry: Map<string, float>
          equilibriumModel: string; kineticModel: string option
          validity: ValidityDto; source: string; note: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private RootDto =
        { schemaVersion: string; description: string option; reactions: ReactionDto[] option }

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions.Default()
                    .WithSkippableOptionFields(SkippableOptionFields.Always,
                                               deserializeNullAsNone = true)))
        o

    /// Atom balance of a reaction, element by element; empty when conserved.
    let imbalance (species: Map<string, SpeciesData>) (terms: (string * float) list) =
        terms
        |> List.collect (fun (key, nu) ->
            match Map.tryFind key species with
            | Some sp -> sp.Elements |> List.map (fun (e, n) -> e, nu * float n)
            | None -> [])
        |> List.groupBy fst
        |> List.map (fun (e, xs) -> e, xs |> List.sumBy snd)
        |> List.filter (fun (_, v) -> abs v > 1e-9)

    let private toRecord (species: Map<string, SpeciesData>) (d: ReactionDto) : Thermo<ReactionRecord> =
        let terms = d.stoichiometry |> Map.toList
        let unknown = terms |> List.map fst |> List.filter (fun k -> not (species.ContainsKey k))
        if String.IsNullOrWhiteSpace d.id then
            fail (DatabaseParseError "reaction with an empty id")
        elif terms.IsEmpty then
            fail (DatabaseParseError $"reaction '{d.id}': no stoichiometry")
        elif not unknown.IsEmpty then
            fail (UnknownSpecies $"""{String.Join(", ", unknown)} (in reaction '{d.id}')""")
        elif d.validity.tMaxK <= d.validity.tMinK then
            fail (DatabaseParseError $"reaction '{d.id}': validity tMax <= tMin")
        else
            match imbalance species terms with
            | (_ :: _) as off ->
                let text = off |> List.map (fun (e, v) -> sprintf "%s %+g" e v) |> String.concat ", "
                fail (DatabaseParseError $"reaction '{d.id}': atoms not conserved ({text})")
            | [] ->
                match d.equilibriumModel with
                | "nasa9Gibbs" ->
                    ok { Id = d.id
                         Reaction = { Name = d.equation; Terms = terms }
                         EquilibriumModel = EquilibriumModel.Nasa9Gibbs
                         KineticModel = d.kineticModel |> Option.filter (String.IsNullOrWhiteSpace >> not)
                         TMin = d.validity.tMinK * 1.0<K>
                         TMax = d.validity.tMaxK * 1.0<K>
                         Source = d.source
                         Note = d.note }
                | other -> fail (DatabaseParseError $"reaction '{d.id}': unknown equilibrium model '{other}'")

    /// Parses a reaction file against a species database.
    let parse (species: Map<string, SpeciesData>) (json: string) : Thermo<Map<string, ReactionRecord>> =
        let root =
            try ok (JsonSerializer.Deserialize<RootDto>(json, options))
            with ex -> fail (DatabaseParseError ex.Message)
        root
        >>= fun r ->
            r.reactions
            |> Option.defaultValue [||]
            |> List.ofArray
            |> traverseList (toRecord species)
            >>= fun records ->
                match records |> List.countBy (fun x -> x.Id) |> List.filter (fun (_, n) -> n > 1) with
                | [] -> ok (records |> List.map (fun x -> x.Id, x) |> Map.ofList)
                | dup -> fail (DatabaseParseError $"""duplicate reaction ids: {dup |> List.map fst |> String.concat ", "}""")

    [<Literal>]
    let DefaultFile = "reactions.json"

    /// Loads the default reaction file against the default species database.
    /// The raw text is cached by DataStore; the records are rebuilt on each call
    /// so an edited species database is never stale here.
    let load () : Thermo<Map<string, ReactionRecord>> =
        SpeciesDatabase.load ()
        >>= fun species -> DataStore.loadText DefaultFile >>= parse species

    let find (reactions: Map<string, ReactionRecord>) (id: string) : Thermo<ReactionRecord> =
        match Map.tryFind id reactions with
        | Some r -> ok r
        | None -> fail (DatabaseParseError $"unknown reaction id '{id}'")

    /// Equilibrium constant of a database reaction, with a warning outside the
    /// range the reaction is valid over.
    let equilibriumConstant (species: Map<string, SpeciesData>) (record: ReactionRecord) (t: float<K>)
                            : Thermo<Equilibrium.EquilibriumConstant> =
        match record.EquilibriumModel with
        | EquilibriumModel.Nasa9Gibbs ->
            Equilibrium.equilibriumConstant species record.Reaction t
            |> warnIf (t < record.TMin || t > record.TMax)
                      (OutsideFitRange (record.Id, "equilibrium constant", float t,
                                        float record.TMin, float record.TMax))
