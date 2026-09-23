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
          viscosity: TransportIntervalDto[] option
          conductivity: TransportIntervalDto[] option
          reason: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private CpDto =
        { kind: string; segments: SegmentDto[] option; nasa7Segments: Nasa7Dto[] option
          nasa9Segments: Nasa9Dto[] option; anchorCp500C: float option; source: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private CriticalDto =
        { tcK: float; pcBar: float; acentric: float; source: string
          vcCm3Mol: float option; vcSource: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private VapourPressureDto =
        { equation: int; c: float[]; tMinK: float; tMaxK: float
          unit: string; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private DiffusionDto =
        { value_cm3_mol: float; source: string; computed: bool }

    [<CLIMutable; NoComparison; NoEquality>]
    type private LennardJonesDto = { sigmaAngstrom: float; epsilonOverKK: float; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private PhaseDto =
        { normalBoilingPointK: float option; meltingPointK: float option
          triplePointK: float option; triplePointBar: float option
          source: string option; unavailableReason: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private MaterialDto =
        { hydrogenService: bool; nitriding: bool; carburizing: bool; metalDusting: bool
          sulfidation: bool; oxidation: bool; notes: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private SafetyDto =
        { flammable: bool; toxic: bool; corrosive: bool
          lflVolPct: float option; uflVolPct: float option; autoIgnitionK: float option
          source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private EosParameterDto =
        { eos: string; secondVirial: string option
          extra: Map<string, float> option; source: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private RadiationDto =
        { participating: bool; supportedModels: string[] option; note: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private QualityDto =
        { thermo: string; transport: string option; critical: string option
          vapourPressure: string option
          /// "verified" | "unverified" | "knownDeviation"
          validation: string; validationNote: string option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private ReferenceStateDto =
        { temperatureK: float; pressureBar: float
          enthalpyConvention: string; entropyConvention: string }

    [<CLIMutable; NoComparison; NoEquality>]
    type private SpeciesDto =
        { key: string; id: string option; name: string; molarMass_kg_kmol: float
          synonyms: string[] option; family: string option
          elements: Map<string, int> option; dataQuality: QualityDto option
          radiation: RadiationDto option
          eosParameters: EosParameterDto[] option
          lennardJones: LennardJonesDto option; lennardJonesUnavailableReason: string option
          phase: PhaseDto option; materialInteraction: MaterialDto option; safety: SafetyDto option
          viscosity: FitDto option; conductivity: FitDto option
          cpModel: CpDto; critical: CriticalDto option
          transport: TransportDto option
          formula: string option; cas: string option
          criticalUnavailableReason: string option
          vapourPressure: VapourPressureDto option
          vapourPressureUnavailableReason: string option
          diffusionVolume: DiffusionDto option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private RootDto =
        { schemaVersion: string; description: string option; species: SpeciesDto[]
          referenceState: ReferenceStateDto option }

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
        | "nasa9" when (d.nasa9Segments |> Option.exists (fun s -> s.Length > 0)) ->
            d.nasa9Segments.Value
            |> List.ofArray
            |> traverseList (toNasa9 key)
            >>= fun segs -> ok (Nasa9 segs)
        | "nasa9" ->
            fail (DatabaseParseError $"species '{key}': cpModel.kind = nasa9 but no nasa9Segments")
        | "nasa7" when (d.nasa7Segments |> Option.exists (fun s -> s.Length > 0)) ->
            d.nasa7Segments.Value
            |> List.ofArray
            |> traverseList (toNasa7 key)
            >>= fun segs -> ok (Nasa7 segs)
        | "nasa7" ->
            fail (DatabaseParseError $"species '{key}': cpModel.kind = nasa7 but no nasa7Segments")
        | "shomate" when (d.segments |> Option.exists (fun s -> s.Length > 0)) ->
            ok (Shomate (d.segments.Value |> Array.map toSegment |> List.ofArray))
        | "shomate" ->
            fail (DatabaseParseError $"species '{key}': cpModel.kind = shomate but no segments")
        | "anchor" ->
            match d.anchorCp500C with
            | Some cp500 ->
                ok (AnchorOnly (cp500, defaultArg d.source ""))
                |> warn (CpIsAnchorOnly key)
            | None ->
                fail (DatabaseParseError $"species '{key}': cpModel.kind = anchor but no anchorCp500C")
        | other ->
            fail (DatabaseParseError $"species '{key}': unknown cpModel.kind '{other}'")

    let private toFamily (key: string) (value: string option) : Thermo<SpeciesFamily> =
        match value with
        | None -> ok SpeciesFamily.Other
        | Some f ->
            match f with
            | "PermanentGas" -> ok SpeciesFamily.PermanentGas
            | "Hydrocarbon" -> ok SpeciesFamily.Hydrocarbon
            | "Oxygenate" -> ok SpeciesFamily.Oxygenate
            | "SulfurCompound" -> ok SpeciesFamily.SulfurCompound
            | "NitrogenCompound" -> ok SpeciesFamily.NitrogenCompound
            | "Water" -> ok SpeciesFamily.Water
            | "Inert" -> ok SpeciesFamily.Inert
            | "AcidGas" -> ok SpeciesFamily.AcidGas
            | "Radical" -> ok SpeciesFamily.Radical
            | "Other" -> ok SpeciesFamily.Other
            | other -> fail (DatabaseParseError $"species '{key}': unknown family '{other}'")

    /// Parses an equation-of-state name as written in the data files.
    let toEquationOfState (name: string) : Result<EquationOfState, string> =
        match name with
        | "IdealGas" -> Ok EquationOfState.IdealGas
        | "Virial" -> Ok EquationOfState.Virial
        | "PengRobinson" -> Ok EquationOfState.PengRobinson
        | "SRK" | "Srk" -> Ok EquationOfState.Srk
        | other -> Error $"unknown equation of state '{other}'"

    let private toEosParameters (key: string) (dtos: EosParameterDto[] option)
                                : Thermo<EosParameterSet list> =
        dtos
        |> Option.defaultValue [||]
        |> List.ofArray
        |> traverseList (fun d ->
            match toEquationOfState d.eos with
            | Ok eos ->
                ok { Eos = eos
                     SecondVirial = d.secondVirial |> Option.filter (String.IsNullOrWhiteSpace >> not)
                     Extra = defaultArg d.extra Map.empty
                     Source = d.source }
            | Error e -> fail (DatabaseParseError $"species '{key}': {e}"))

    let private toLevel (key: string) (group: string) (value: string) : Thermo<QualityLevel> =
        match value with
        | "A" -> ok QualityLevel.A
        | "B" -> ok QualityLevel.B
        | "C" -> ok QualityLevel.C
        | "D" -> ok QualityLevel.D
        | other -> fail (DatabaseParseError $"species '{key}': {group} quality '{other}' is not A-D")

    /// Quality implied by the kind of data, used when a record does not state it
    /// (schema 2.x files). Stated values always win.
    let private impliedQuality (cp: CpModel) (transport: TransportModel)
                               (hasCritical: bool) (hasVapourPressure: bool) : SpeciesDataQuality =
        { Thermo =
            match cp with
            | Nasa9 _ | Nasa7 _ -> QualityLevel.A
            | Shomate _ -> QualityLevel.B
            | AnchorOnly _ -> QualityLevel.D
          Transport =
            match transport with
            | NasaCea _ -> Some QualityLevel.B
            | SutherlandPair _ -> Some QualityLevel.C
            | NoTransportData _ -> Option.None
          Critical = if hasCritical then Some QualityLevel.A else Option.None
          VapourPressure = if hasVapourPressure then Some QualityLevel.B else Option.None
          Validation = Unverified }

    let private toQuality (key: string) (implied: SpeciesDataQuality) (dto: QualityDto option)
                          : Thermo<SpeciesDataQuality> =
        match dto with
        | None -> ok implied
        | Some q ->
            let optionalLevel group value =
                match value with
                | Some v -> toLevel key group v >>= fun l -> ok (Some l)
                | None -> ok Option.None
            toLevel key "thermo" q.thermo
            >>= fun thermo ->
            optionalLevel "transport" q.transport
            >>= fun transport ->
            optionalLevel "critical" q.critical
            >>= fun critical ->
            optionalLevel "vapourPressure" q.vapourPressure
            >>= fun vapour ->
                let note = q.validationNote |> Option.defaultValue ""
                let validation =
                    match q.validation with
                    | "verified" -> ok (Verified note)
                    | "unverified" -> ok Unverified
                    | "knownDeviation" -> ok (KnownDeviation note)
                    | other -> fail (DatabaseParseError $"species '{key}': unknown validation '{other}'")
                validation
                >>= fun v ->
                    ok { Thermo = thermo; Transport = transport; Critical = critical
                         VapourPressure = vapour; Validation = v }

    let private toSpecies (d: SpeciesDto) : Thermo<SpeciesData> =
        if String.IsNullOrWhiteSpace d.key then
            fail (DatabaseParseError "species record with empty key")
        elif d.id |> Option.exists (fun id -> id <> d.key) then
            fail (DatabaseParseError $"species '{d.key}': id '{d.id.Value}' differs from key")
        elif d.molarMass_kg_kmol <= 0.0 then
            fail (DatabaseParseError $"species '{d.key}': non-positive molar mass")
        else
            let formula =
                d.formula
                |> Option.filter (String.IsNullOrWhiteSpace >> not)
                |> Option.defaultValue d.key
            let elements =
                match d.elements with
                | Some e when not e.IsEmpty -> ok (Map.toList e)
                | _ ->
                    match Formula.elements formula with
                    | Ok e -> ok e
                    | Error e -> fail (DatabaseParseError $"species '{d.key}': {e}")
            elements
            >>= fun elements ->
            toFamily d.key d.family
            >>= fun family ->
            toCp d.key d.cpModel
            >>= fun cp ->
                // Infrared activity is a property of the molecule, not of the
                // model; whether the library can USE it is a separate question,
                // and the two are kept distinct. Schema 3.0 states it per species.
                let uncoveredNote =
                    $"{d.key} absorbs in the infrared but no open WSGG or Leckner parameter set "
                    + "exists for it. Its emission is missing from any emissivity computed here, "
                    + "which is therefore LOW."
                let radiation =
                    match d.radiation with
                    | Some r when not r.participating -> Transparent
                    | Some r ->
                        match r.supportedModels |> Option.map List.ofArray |> Option.defaultValue [] with
                        | [] ->
                            ParticipatingUncovered
                                (r.note
                                 |> Option.filter (String.IsNullOrWhiteSpace >> not)
                                 |> Option.defaultValue uncoveredNote)
                        | models -> ParticipatingCovered models
                    | None ->
                        // Schema 2.x fallback: homonuclear diatomics and monatomics
                        // are transparent, H2O and CO2 are covered, the rest absorb.
                        let transparent = set [ "H2"; "N2"; "O2"; "Ar"; "He"; "H"; "O"; "N"
                                                "S2"; "S3"; "S4"; "S5"; "S6"; "S7"; "S8"; "S1" ]
                        if transparent.Contains d.key then Transparent
                        elif d.key = "H2O" || d.key = "CO2" then
                            ParticipatingCovered [ "Smith, Shen & Friedman (1982) WSGG" ]
                        else ParticipatingUncovered uncoveredNote

                let transport =
                    match d.transport with
                    | Some (t: TransportDto) when t.kind = "nasaCea" && (t.viscosity |> Option.exists (fun v -> v.Length > 0)) ->
                        NasaCea (t.viscosity |> Option.defaultValue [||] |> Array.map toTransportInterval |> List.ofArray,
                                 t.conductivity |> Option.defaultValue [||]
                                 |> Array.map toTransportInterval |> List.ofArray)
                    | Some (t: TransportDto) when t.kind = "none" ->
                        NoTransportData (t.reason
                                         |> Option.filter (String.IsNullOrWhiteSpace >> not)
                                         |> Option.defaultValue $"no transport data for '{d.key}'")
                    | _ ->
                        match (d.viscosity |> Option.map toFit),
                              (d.conductivity |> Option.map toFit) with
                        | Some mu, Some k -> SutherlandPair (mu, k)
                        | _ -> NoTransportData $"no transport data for '{d.key}'"
                let implied =
                    impliedQuality cp transport (Option.isSome d.critical) (Option.isSome d.vapourPressure)

                toQuality d.key implied d.dataQuality
                >>= fun quality ->
                toEosParameters d.key d.eosParameters
                >>= fun eosParameters ->
                ok { Key = d.key
                     Name = d.name
                     Synonyms = d.synonyms |> Option.map List.ofArray |> Option.defaultValue []
                     Family = family
                     Elements = elements
                     Quality = quality
                     EosParameters = eosParameters
                     LennardJones =
                        d.lennardJones
                        |> Option.map (fun lj ->
                            { Sigma = lj.sigmaAngstrom
                              EpsilonOverK = lj.epsilonOverKK * 1.0<K>
                              Source = lj.source })
                     LennardJonesUnavailable =
                        d.lennardJonesUnavailableReason |> Option.filter (String.IsNullOrWhiteSpace >> not)
                     Phase =
                        match d.phase with
                        | Some ph ->
                            { NormalBoilingPoint = ph.normalBoilingPointK |> Option.map (fun v -> v * 1.0<K>)
                              MeltingPoint = ph.meltingPointK |> Option.map (fun v -> v * 1.0<K>)
                              TriplePoint =
                                match ph.triplePointK, ph.triplePointBar with
                                | Some t, Some p -> Some (t * 1.0<K>, p * 1.0<bar>)
                                | _ -> Option.None
                              Source = ph.source
                              Unavailable = ph.unavailableReason }
                        | None ->
                            { NormalBoilingPoint = Option.None; MeltingPoint = Option.None
                              TriplePoint = Option.None; Source = Option.None
                              Unavailable = Some "not stated in this database version" }
                     MaterialInteraction =
                        d.materialInteraction
                        |> Option.map (fun m ->
                            { HydrogenService = m.hydrogenService
                              Nitriding = m.nitriding
                              Carburizing = m.carburizing
                              MetalDusting = m.metalDusting
                              Sulfidation = m.sulfidation
                              Oxidation = m.oxidation
                              Notes = defaultArg m.notes "" })
                     Safety =
                        d.safety
                        |> Option.map (fun sf ->
                            { Flammable = sf.flammable
                              Toxic = sf.toxic
                              Corrosive = sf.corrosive
                              LowerFlammabilityLimit = sf.lflVolPct
                              UpperFlammabilityLimit = sf.uflVolPct
                              AutoIgnitionTemperature = sf.autoIgnitionK |> Option.map (fun v -> v * 1.0<K>)
                              Source = sf.source })
                     Formula = formula
                     Cas = (d.cas |> Option.filter (String.IsNullOrWhiteSpace >> not))
                     CriticalUnavailable =
                        (d.criticalUnavailableReason |> Option.filter (String.IsNullOrWhiteSpace >> not))
                     VapourPressure =
                        (match d.vapourPressure with
                         | Some v ->
                            Some { C = Array.copy v.c
                                   TMin = v.tMinK * 1.0<K>
                                   TMax = v.tMaxK * 1.0<K>
                                   Source = v.source }
                         | None -> Option.None)
                     VapourPressureUnavailable =
                        (d.vapourPressureUnavailableReason
                         |> Option.filter (String.IsNullOrWhiteSpace >> not))
                     DiffusionVolume =
                        (d.diffusionVolume |> Option.map (fun v -> v.value_cm3_mol))
                     Radiation = radiation
                     MolarMass = d.molarMass_kg_kmol * 1.0<kg/kmol>
                     Viscosity = (d.viscosity |> Option.map toFit)
                     Conductivity = (d.conductivity |> Option.map toFit)
                     Transport = transport
                     Cp = cp
                     Critical =
                        match d.critical with
                        | Some c ->
                            Some { Tc = c.tcK * 1.0<K>; Pc = c.pcBar * 1.0<bar>
                                   Acentric = c.acentric; Source = c.source
                                   Vc = c.vcCm3Mol |> Option.filter (fun v -> v > 0.0)
                                   VcSource = c.vcSource }
                        | None -> None }
            |> warnIf (Option.isNone d.critical) (MissingCriticalProps d.key)

    // ---------- public API ----------

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        // Optional blocks are absent from many records and null in others (CEA-only
        // species carry "anchorCp500C": null), so both must read as None rather than
        // fail the whole database.
        o.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions.Default()
                    .WithSkippableOptionFields(SkippableOptionFields.Always,
                                               deserializeNullAsNone = true)))
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

    /// Reference state of the database: the one stated in the file, or, for a
    /// schema 2.x file that does not state it, the NASA-9 convention the data is
    /// on (298.15 K, 1 bar), with a warning that it was assumed.
    let parseReferenceState (json: string) : Thermo<ReferenceState> =
        try
            let r = JsonSerializer.Deserialize<RootDto>(json, options)
            match r.referenceState with
            | Some rs ->
                ok { Temperature = rs.temperatureK * 1.0<K>
                     Pressure = rs.pressureBar * 1.0<bar>
                     EnthalpyConvention = rs.enthalpyConvention
                     EntropyConvention = rs.entropyConvention }
            | None ->
                ok { Temperature = 298.15<K>
                     Pressure = 1.0<bar>
                     EnthalpyConvention = "NASA-9 absolute: H(298.15 K) equals the formation enthalpy"
                     EntropyConvention = "NASA-9 absolute (third-law) entropy at 1 bar" }
                |> warn (CorrelationExtrapolated
                            ("reference state",
                             "the database does not state its reference state; the NASA-9 "
                             + "convention (298.15 K, 1 bar) was assumed"))
        with ex -> fail (DatabaseParseError ex.Message)

    /// Reference state of the default database. Read directly rather than
    /// through the DataStore cache, which already holds that file as a species map.
    let referenceState () : Thermo<ReferenceState> =
        DataStore.resolve DefaultFile
        >>= fun path ->
            let text =
                try ok (File.ReadAllText path)
                with ex -> fail (DatabaseParseError $"{path}: {ex.Message}")
            text >>= parseReferenceState

    /// Look up a species by key.
    let find (db: Map<string, SpeciesData>) (key: string) : Thermo<SpeciesData> =
        match Map.tryFind key db with
        | Some s -> ok s
        | None -> fail (UnknownSpecies key)

    /// Species still lacking a published temperature-dependent Cp fit.
    /// Use this in a build-time gate or a report appendix.
    let pendingCompletion (db: Map<string, SpeciesData>) =
        db |> Map.toList |> List.map snd |> List.filter (fun s -> not s.IsProductionGrade)
