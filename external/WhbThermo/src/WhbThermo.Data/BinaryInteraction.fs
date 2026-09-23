namespace WhbThermo.Data

open System
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain

/// Binary interaction parameters k_ij, kept apart from the species database
/// because they belong to a pair and to one equation of state.
///
/// An absent pair is NOT an error: k_ij = 0 is the standard default of every
/// mixing rule here, and most pairs in a WHB gas never had one fitted. What
/// the table must never do is hand back a value outside the temperature range
/// it was fitted over without saying so.
module BinaryInteraction =

    [<CLIMutable; NoComparison; NoEquality>]
    type private PairDto =
        { species1: string; species2: string; eos: string; kij: float
          tMinK: float; tMaxK: float; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private RootDto =
        { schemaVersion: string; description: string option; pairs: PairDto[] option }

    /// Pairs indexed by (id, id, eos) with the two ids in ordinal order.
    type Table = Map<string * string * EquationOfState, BinaryInteractionParameter>

    let private ordered (a: string) (b: string) =
        if String.CompareOrdinal(a, b) <= 0 then a, b else b, a

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions.Default()
                    .WithSkippableOptionFields(SkippableOptionFields.Always,
                                               deserializeNullAsNone = true)))
        o

    let parse (json: string) : Thermo<Table> =
        let root =
            try ok (JsonSerializer.Deserialize<RootDto>(json, options))
            with ex -> fail (DatabaseParseError ex.Message)
        root
        >>= fun r ->
            r.pairs
            |> Option.defaultValue [||]
            |> List.ofArray
            |> traverseList (fun d ->
                match SpeciesDatabase.toEquationOfState d.eos with
                | Error e -> fail (DatabaseParseError $"k_ij {d.species1}-{d.species2}: {e}")
                | Ok _ when d.species1 = d.species2 ->
                    fail (DatabaseParseError $"k_ij {d.species1}-{d.species2}: a pair needs two species")
                | Ok _ when d.tMaxK <= d.tMinK ->
                    fail (DatabaseParseError $"k_ij {d.species1}-{d.species2}: tMax <= tMin")
                | Ok eos ->
                    let a, b = ordered d.species1 d.species2
                    ok { Species1 = a; Species2 = b; Eos = eos; Kij = d.kij
                         TMin = d.tMinK * 1.0<K>; TMax = d.tMaxK * 1.0<K>; Source = d.source })
            >>= fun pairs ->
                let keyOf (p: BinaryInteractionParameter) = p.Species1, p.Species2, p.Eos
                match pairs |> List.countBy keyOf |> List.filter (fun (_, n) -> n > 1) with
                | [] -> ok (pairs |> List.map (fun p -> keyOf p, p) |> Map.ofList)
                | duplicates ->
                    let names =
                        duplicates |> List.map (fun ((a, b, e), _) -> $"{a}-{b} ({e})") |> String.concat ", "
                    fail (DatabaseParseError $"duplicate k_ij pairs: {names}")

    [<Literal>]
    let DefaultFile = "binary-interaction.json"

    let load () : Thermo<Table> = DataStore.load DefaultFile parse
    let loadFile (path: string) : Thermo<Table> = DataStore.load path parse

    /// The stored parameter of a pair, in either order, if there is one.
    let tryFind (table: Table) (a: string) (b: string) (eos: EquationOfState) =
        let x, y = ordered a b
        Map.tryFind (x, y, eos) table

    /// k_ij for a pair at a temperature: 0 when the pair has no entry, the
    /// stored value otherwise, with a warning when T is outside its fit range.
    let kij (table: Table) (a: string) (b: string) (eos: EquationOfState) (t: float<K>) : Thermo<float> =
        if a = b then ok 0.0
        else
            match tryFind table a b eos with
            | None -> ok 0.0
            | Some p ->
                ok p.Kij
                |> warnIf (t < p.TMin || t > p.TMax)
                          (OutsideFitRange ($"{p.Species1}-{p.Species2}", "k_ij", float t,
                                            float p.TMin, float p.TMax))
