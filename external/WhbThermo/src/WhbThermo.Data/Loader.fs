namespace WhbThermo.Data

open System
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain

/// Loads and validates the embedded species database.
module SpeciesDatabase =

    // ---------- DTOs (mirror the JSON exactly, nothing more) ----------

    [<CLIMutable; NoComparison; NoEquality>]
    type private FitDto =
        { coeff0: float; sutherlandK: float; tRefK: float
          tMinK: float; tMaxK: float; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private SegmentDto =
        { tMinK: float; tMaxK: float
          a: float; b: float; c: float; d: float
          e: float; f: float; g: float; h: float
          source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private Nasa7Dto =
        { tMinK: float; tMaxK: float; a: float[]; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private Nasa9Dto =
        { tMinK: float; tMaxK: float; a: float[]; b: float[]; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private TransportIntervalDto =
        { tMinK: float; tMaxK: float; a: float; b: float; c: float; d: float
          source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private TransportDto =
        { kind: string
          viscosity: TransportIntervalDto[]
          conductivity: TransportIntervalDto[]
          reason: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private CpDto =
        { kind: string; segments: SegmentDto[]; nasa7Segments: Nasa7Dto[]
          nasa9Segments: Nasa9Dto[]; anchorCp500C: float; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private CriticalDto =
        { tcK: float; pcBar: float; acentric: float; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private VapourPressureDto =
        { equation: int; c: float[]; tMinK: float; tMaxK: float
          unit: string; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private DiffusionDto =
        { value_cm3_mol: float; source: string; computed: bool }

    [<CLIMutable; NoComparison; NoEquality>]
    type private SpeciesDto =
        { key: string; name: string; molarMass_kg_kmol: float
          viscosity: FitDto option; conductivity: FitDto option
          cpModel: CpDto; critical: CriticalDto option
          transport: TransportDto option
          formula: string; cas: string
          criticalUnavailableReason: string
          vapourPressure: VapourPressureDto option
          vapourPressureUnavailableReason: string
          diffusionVolume: DiffusionDto option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private RootDto = { schemaVersion: string; description: string; species: SpeciesDto[] }

    // ---------- mapping ----------

    let private toFit (d: FitDto) : SutherlandFit =
        { Coeff0 = d.coeff0
          SutherlandK = d.sutherlandK * 1.0<K>
          TRef = d.tRefK * 1.0<K>
          TMin = d.tMinK * 1.0<K>
          TMax = d.tMaxK * 1.0<K>
          Source = d.source }

    let private toSegment (d: SegmentDto) : ShomateSegment =
        { TMin = d.tMinK * 1.0<K>; TMax = d.tMaxK * 1.0<K>
          A = d.a; B = d.b; C = d.c; D = d.d
          E = d.e; F = d.f; G = d.g; H = d.h
          Source = d.source }

    let private toNasa7 (key: string) (d: Nasa7Dto) : Thermo<Nasa7Segment> =
        if isNull d.a || d.a.Length <> 7 then
            let n = if isNull d.a then 0 else d.a.Length
            fail (DatabaseParseError $"species '{key}': NASA-7 segment has {n} coefficients, expected 7")
        elif d.tMaxK <= d.tMinK then
            fail (DatabaseParseError $"species '{key}': NASA-7 segment has tMax <= tMin")
        else
            ok { TMin = d.tMinK * 1.0<K>; TMax = d.tMaxK * 1.0<K>
                 A = Array.copy d.a; Source = d.source }

    let private toNasa9 (key: string) (d: Nasa9Dto) : Thermo<Nasa9Segment> =
        let na = if isNull d.a then 0 else d.a.Length
        let nb = if isNull d.b then 0 else d.b.Length
        if na <> 7 then
            fail (DatabaseParseError $"species '{key}': NASA-9 segment has {na} a-coefficients, expected 7")
        elif nb <> 2 then
            fail (DatabaseParseError $"species '{key}': NASA-9 segment has {nb} b-coefficients, expected 2")
        elif d.tMaxK <= d.tMinK then
            fail (DatabaseParseError $"species '{key}': NASA-9 segment has tMax <= tMin")
        else
            ok { TMin = d.tMinK * 1.0<K>; TMax = d.tMaxK * 1.0<K>
                 A = Array.copy d.a; B = Array.copy d.b; Source = d.source }

    let private toTransportInterval (d: TransportIntervalDto) : NasaTransportInterval =
        { TMin = d.tMinK * 1.0<K>; TMax = d.tMaxK * 1.0<K>
          A = d.a; B = d.b; C = d.c; D = d.d; Source = d.source }

    let private toCp (key: string) (d: CpDto) : Thermo<CpModel> =
        match d.kind with
        | "nasa9" when not (isNull d.nasa9Segments) && d.nasa9Segments.Length > 0 ->
            d.nasa9Segments
            |> List.ofArray
            |> traverseList (toNasa9 key)
            >>= fun segs -> ok (Nasa9 segs)
        | "nasa9" ->
            fail (DatabaseParseError $"species '{key}': cpModel.kind = nasa9 but no nasa9Segments")
        | "nasa7" when not (isNull d.nasa7Segments) && d.nasa7Segments.Length > 0 ->
            d.nasa7Segments
            |> List.ofArray
            |> traverseList (toNasa7 key)
            >>= fun segs -> ok (Nasa7 segs)
        | "nasa7" ->
            fail (DatabaseParseError $"species '{key}': cpModel.kind = nasa7 but no nasa7Segments")
        | "shomate" when not (isNull d.segments) && d.segments.Length > 0 ->
            ok (Shomate (d.segments |> Array.map toSegment |> List.ofArray))
        | "shomate" ->
            fail (DatabaseParseError $"species '{key}': cpModel.kind = shomate but no segments")
        | "anchor" ->
            ok (AnchorOnly (d.anchorCp500C, d.source))
            |> warn (CpIsAnchorOnly key)
        | other ->
            fail (DatabaseParseError $"species '{key}': unknown cpModel.kind '{other}'")

    let private toSpecies (d: SpeciesDto) : Thermo<SpeciesData> =
        if String.IsNullOrWhiteSpace d.key then
            fail (DatabaseParseError "species record with empty key")
        elif d.molarMass_kg_kmol <= 0.0 then
            fail (DatabaseParseError $"species '{d.key}': non-positive molar mass")
        else
            toCp d.key d.cpModel
            >>= fun cp ->
                // Infrared activity is a property of the molecule, not of the
                // model: homonuclear diatomics and monatomics are transparent,
                // everything else absorbs. Whether the library can USE that is a
                // separate question, and the two are kept distinct.
                let transparent = set [ "H2"; "N2"; "O2"; "Ar"; "He"; "H"; "O"; "N"
                                        "S2"; "S3"; "S4"; "S5"; "S6"; "S7"; "S8"; "S1" ]
                let covered = set [ "H2O"; "CO2" ]
                let radiation =
                    if transparent.Contains d.key then Transparent
                    elif covered.Contains d.key then
                        ParticipatingCovered "Smith, Shen & Friedman (1982) WSGG"
                    else
                        ParticipatingUncovered
                            (sprintf "%s absorbs in the infrared but no open WSGG or Leckner                                       parameter set exists for it. Its emission is missing from                                       any emissivity computed here, which is therefore LOW."
                                     d.key)

                ok { Key = d.key
                     Name = d.name
                     Formula = (if String.IsNullOrWhiteSpace d.formula then d.key else d.formula)
                     Cas = (if String.IsNullOrWhiteSpace d.cas then Option.None else Some d.cas)
                     CriticalUnavailable =
                        (if String.IsNullOrWhiteSpace d.criticalUnavailableReason then Option.None
                         else Some d.criticalUnavailableReason)
                     VapourPressure =
                        (match d.vapourPressure with
                         | Some v ->
                            Some { C = Array.copy v.c
                                   TMin = v.tMinK * 1.0<K>
                                   TMax = v.tMaxK * 1.0<K>
                                   Source = v.source }
                         | None -> Option.None)
                     VapourPressureUnavailable =
                        (if String.IsNullOrWhiteSpace d.vapourPressureUnavailableReason then Option.None
                         else Some d.vapourPressureUnavailableReason)
                     DiffusionVolume =
                        (d.diffusionVolume |> Option.map (fun v -> v.value_cm3_mol))
                     Radiation = radiation
                     MolarMass = d.molarMass_kg_kmol * 1.0<kg/kmol>
                     Viscosity = (d.viscosity |> Option.map toFit)
                     Conductivity = (d.conductivity |> Option.map toFit)
                     Transport =
                        match d.transport with
                        | Some (t: TransportDto) when t.kind = "nasaCea" && not (isNull t.viscosity) && t.viscosity.Length > 0 ->
                            NasaCea (t.viscosity |> Array.map toTransportInterval |> List.ofArray,
                                     (if isNull t.conductivity then []
                                      else t.conductivity |> Array.map toTransportInterval |> List.ofArray))
                        | Some (t: TransportDto) when t.kind = "none" ->
                            NoTransportData (if String.IsNullOrWhiteSpace t.reason then
                                                $"no transport data for '{d.key}'"
                                             else t.reason)
                        | _ ->
                            match (d.viscosity |> Option.map toFit),
                                  (d.conductivity |> Option.map toFit) with
                            | Some mu, Some k -> SutherlandPair (mu, k)
                            | _ -> NoTransportData $"no transport data for '{d.key}'"
                     Cp = cp
                     Critical =
                        match d.critical with
                        | Some c ->
                            Some { Tc = c.tcK * 1.0<K>; Pc = c.pcBar * 1.0<bar>
                                   Acentric = c.acentric; Source = c.source }
                        | None -> None }
            |> warnIf (Option.isNone d.critical) (MissingCriticalProps d.key)

    // ---------- public API ----------

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.Converters.Add(JsonFSharpConverter())
        o

    /// Parse a JSON payload into a validated species map.
    /// Warnings (anchor-only Cp, missing critical properties) are accumulated,
    /// not swallowed.
    let parse (json: string) : Thermo<Map<string, SpeciesData>> =
        let root =
            try ok (JsonSerializer.Deserialize<RootDto>(json, options))
            with ex -> fail (DatabaseParseError ex.Message)

        root
        >>= fun r ->
            if isNull (box r.species) || r.species.Length = 0 then
                fail (DatabaseParseError "database contains no species")
            else
                r.species
                |> List.ofArray
                |> traverseList toSpecies
                >>= fun list ->
                    let duplicates =
                        list |> List.countBy (fun s -> s.Key)
                             |> List.filter (fun (_, n) -> n > 1)
                    match duplicates with
                    | [] -> ok (list |> List.map (fun s -> s.Key, s) |> Map.ofList)
                    | ds -> fail (DatabaseParseError $"""duplicate species keys: {ds |> List.map fst |> String.concat ", "}""")

    [<Literal>]
    let DefaultFile = "species-database.json"

    /// Load the species database from the data directory. Cached; re-reads
    /// automatically if the file changes on disk.
    let load () : Thermo<Map<string, SpeciesData>> =
        DataStore.load DefaultFile parse

    /// Load from an explicit path (site-specific overrides, client-supplied data).
    let loadFile (path: string) : Thermo<Map<string, SpeciesData>> =
        DataStore.load path parse

    /// Look up a species by key.
    let find (db: Map<string, SpeciesData>) (key: string) : Thermo<SpeciesData> =
        match Map.tryFind key db with
        | Some s -> ok s
        | None -> fail (UnknownSpecies key)

    /// Species still lacking a published temperature-dependent Cp fit.
    /// Use this in a build-time gate or a report appendix.
    let pendingCompletion (db: Map<string, SpeciesData>) =
        db |> Map.toList |> List.map snd |> List.filter (fun s -> not s.IsProductionGrade)
