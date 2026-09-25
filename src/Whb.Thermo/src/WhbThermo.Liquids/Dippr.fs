namespace WhbThermo.Liquids

open System
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// Temperature-dependent liquid and vapour properties from DIPPR-form
/// correlations.
///
/// The equation number is carried as data rather than baked into a function per
/// property, because the same property uses different forms for different
/// compounds: liquid heat capacity is form 100 for most species and form 114
/// for the six where the 100 fit does not exist.
///
/// Two unit traps are handled explicitly rather than left to the caller. Form
/// 105 returns a MOLAR density in kmol/m^3 and forms 100/114 return a MOLAR
/// heat capacity in J/(kmol*K). Both need the molar mass to reach mass units,
/// and getting that wrong produces a number that looks plausible and is out by
/// three orders of magnitude. The unit is recorded alongside every coefficient
/// set and the conversion happens here.
module Dippr =

    [<Literal>]
    let DefaultFile = "liquid-properties.json"

    // ---------- data model ----------

    type Correlation =
        { Equation : int
          C        : float[]
          /// Critical temperature the correlation was FITTED with, where the
          /// source table supplies its own. Preferred over a handbook value:
          /// forms 106 and 114 are written in reduced temperature, so using a
          /// slightly different Tc shifts the whole curve.
          Tc       : float<K> option
          TMin     : float<K> option
          TMax     : float<K> option
          Unit     : string
          Source   : string
          /// A single measured value, not a correlation: TMin = TMax, and every
          /// evaluation away from that point is an extrapolation and warns.
          SinglePoint : bool }

    type LiquidSpecies =
        { Key          : string
          Cas          : string
          Name         : string
          MolarMass    : float          // g/mol
          Tc           : float<K> option
          Correlations : Map<string, Correlation> }

        member this.Has name = this.Correlations.ContainsKey name

    type Database =
        { Species : Map<string, LiquidSpecies>
          Licence : string }

    // ---------- loading ----------

    [<CLIMutable; NoComparison; NoEquality>]
    type private CorrelationDto =
        { equation: int; c: float[]; tcK: float option
          tMinK: float option; tMaxK: float option
          unit: string; source: string; singlePoint: bool option }

    [<CLIMutable; NoComparison; NoEquality>]
    type private SpeciesDto =
        { key: string; cas: string; name: string; molarMass_g_mol: float
          tcK: float option
          correlations: System.Collections.Generic.Dictionary<string, CorrelationDto> }

    [<CLIMutable; NoComparison; NoEquality>]
    type private RootDto =
        { schemaVersion: string; licence: string; species: SpeciesDto[] }

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        // Optional fields are absent or null in parts of the data file; both read as None.
        o.Converters.Add(
            JsonFSharpConverter(
                JsonFSharpOptions.Default()
                    .WithSkippableOptionFields(SkippableOptionFields.Always,
                                               deserializeNullAsNone = true)))
        o

    /// Number of coefficients each form requires. A set of the wrong length is a
    /// corrupt record, not something to evaluate and hope about.
    let expectedCoefficients equation =
        match equation with
        | 100 -> Some 5
        | 101 -> Some 5
        | 102 -> Some 4
        | 105 -> Some 4
        | 106 -> Some 4
        | 114 -> Some 5
        | _ -> None

    let private toCorrelation (key: string) (name: string) (d: CorrelationDto)
                              : Thermo<Correlation> =
        match expectedCoefficients d.equation with
        | Option.None ->
            fail (DatabaseParseError $"{key}/{name}: unsupported DIPPR equation {d.equation}")
        | Some expected when isNull d.c || d.c.Length <> expected ->
            let actual = if isNull d.c then 0 else d.c.Length
            fail (DatabaseParseError
                    $"{key}/{name}: DIPPR {d.equation} needs {expected} coefficients, got {actual}")
        | Some _ ->
            ok { Equation = d.equation
                 C = Array.copy d.c
                 Tc = d.tcK |> Option.map (fun v -> v * 1.0<K>)
                 TMin = d.tMinK |> Option.map (fun v -> v * 1.0<K>)
                 TMax = d.tMaxK |> Option.map (fun v -> v * 1.0<K>)
                 Unit = d.unit
                 Source = d.source
                 SinglePoint = defaultArg d.singlePoint false }

    let parse (json: string) : Thermo<Database> =
        try
            let root = JsonSerializer.Deserialize<RootDto>(json, options)
            root.species
            |> List.ofArray
            |> traverseList (fun s ->
                s.correlations
                |> Seq.map (fun kv -> kv.Key, kv.Value)
                |> List.ofSeq
                |> traverseList (fun (name, dto) ->
                    toCorrelation s.key name dto >>= fun c -> ok (name, c))
                >>= fun correlations ->
                    ok { Key = s.key; Cas = s.cas; Name = s.name
                         MolarMass = s.molarMass_g_mol
                         Tc = s.tcK |> Option.map (fun v -> v * 1.0<K>)
                         Correlations = Map.ofList correlations })
            >>= fun species ->
                ok { Species = species |> List.map (fun s -> s.Key, s) |> Map.ofList
                     Licence = root.licence }
        with ex ->
            fail (DatabaseParseError ex.Message)

    let load () : Thermo<Database> = DataStore.load DefaultFile parse
    let loadFile (path: string) : Thermo<Database> = DataStore.load path parse

    let find (db: Database) (key: string) : Thermo<LiquidSpecies> =
        match Map.tryFind key db.Species with
        | Some s -> ok s
        | Option.None -> fail (UnknownSpecies key)

    // ---------- evaluation ----------

    /// Raw evaluation in the correlation's own units.
    let private raw (equation: int) (c: float[]) (t: float<K>) (tc: float<K> option) =
        let tK = float t
        match equation with
        | 100 ->
            Ok (c.[0] + c.[1] * tK + c.[2] * tK ** 2.0 + c.[3] * tK ** 3.0 + c.[4] * tK ** 4.0)
        | 101 ->
            Ok (exp (c.[0] + c.[1] / tK + c.[2] * log tK + c.[3] * tK ** c.[4]))
        | 102 ->
            Ok (c.[0] * tK ** c.[1] / (1.0 + c.[2] / tK + c.[3] / (tK * tK)))
        | 105 ->
            Ok (c.[0] / (c.[1] ** (1.0 + (1.0 - tK / c.[2]) ** c.[3])))
        | 106 ->
            match tc with
            | Option.None -> Error "DIPPR 106 needs a critical temperature"
            | Some tcrit when tK >= float tcrit -> Error "at or above the critical temperature"
            | Some tcrit ->
                let tr = tK / float tcrit
                Ok (c.[0] * (1.0 - tr) ** (c.[1] + c.[2] * tr + c.[3] * tr ** 2.0))
        | 114 ->
            match tc with
            | Option.None -> Error "DIPPR 114 needs a critical temperature"
            | Some tcrit when tK >= float tcrit -> Error "at or above the critical temperature"
            | Some tcrit ->
                let tau = 1.0 - tK / float tcrit
                let a, b, cc, d = c.[0], c.[1], c.[2], c.[3]
                Ok (a * a / tau + b - 2.0 * a * cc * tau - a * d * tau ** 2.0
                    - cc * cc * tau ** 3.0 / 3.0 - cc * d * tau ** 4.0 / 2.0
                    - d * d * tau ** 5.0 / 5.0)
        | other ->
            Error $"unsupported DIPPR equation {other}"

    /// Evaluate a named correlation in its tabulated units, with a range check.
    let evaluateRaw (species: LiquidSpecies) (name: string) (t: float<K>) : Thermo<float> =
        match Map.tryFind name species.Correlations with
        | Option.None ->
            fail (NoCpDataAvailable $"{species.Key}: no {name} correlation")
        | Some correlation ->
            // A correlation's own fitted Tc wins over the species-level value.
            let tc = match correlation.Tc with Some v -> Some v | Option.None -> species.Tc
            match raw correlation.Equation correlation.C t tc with
            | Error message ->
                fail (CorrelationExtrapolated ($"{species.Key}/{name}", message))
            | Ok value ->
                if Double.IsNaN value || Double.IsInfinity value then
                    fail (CorrelationExtrapolated
                            ($"{species.Key}/{name}",
                             $"DIPPR {correlation.Equation} gave a non-finite value at %.1f{float t} K"))
                else
                    let below = match correlation.TMin with Some lo -> t < lo | Option.None -> false
                    let above = match correlation.TMax with Some hi -> t > hi | Option.None -> false
                    let lo = match correlation.TMin with Some v -> float v | Option.None -> nan
                    let hi = match correlation.TMax with Some v -> float v | Option.None -> nan
                    ok value
                    |> warnIf (below || above)
                              (OutsideFitRange (species.Key, name, float t, lo, hi))

    // ---------- named properties, in SI mass units ----------

    /// Liquid dynamic viscosity [Pa*s].
    let liquidViscosity (s: LiquidSpecies) (t: float<K>) : Thermo<float<Pa*s>> =
        evaluateRaw s "liquidViscosity" t >>= fun v -> ok (v * 1.0<Pa*s>)

    /// Liquid thermal conductivity [W/(m*K)].
    let liquidConductivity (s: LiquidSpecies) (t: float<K>) : Thermo<float<W/(m*K)>> =
        evaluateRaw s "liquidConductivity" t >>= fun v -> ok (v * 1.0<W/(m*K)>)

    /// Saturated liquid density [kg/m^3].
    ///
    /// The source table is in mol/m^3, NOT the kmol/m^3 the DIPPR definition
    /// implies - a factor of 1000 that produces an entirely plausible-looking
    /// wrong answer. Verified against CoolProp: benzene at 300 K gives
    /// 871.1 kg/m^3 against a reference 871.5.
    let liquidDensity (s: LiquidSpecies) (t: float<K>) : Thermo<float<kg/m^3>> =
        evaluateRaw s "liquidMolarDensity" t
        >>= fun molar -> ok (molar * s.MolarMass / 1000.0 * 1.0<kg/m^3>)

    /// Liquid specific heat capacity [J/(kg*K)]. The correlation is molar, in
    /// J/(kmol*K).
    let liquidHeatCapacity (s: LiquidSpecies) (t: float<K>) : Thermo<float<J/(kg*K)>> =
        evaluateRaw s "liquidMolarHeatCapacity" t
        >>= fun molar -> ok (molar / s.MolarMass * 1.0<J/(kg*K)>)  // J/(kmol*K) / (g/mol) = J/(kg*K)

    /// Heat of vaporisation [J/kg]. The correlation is in kJ/kmol.
    let heatOfVaporisation (s: LiquidSpecies) (t: float<K>) : Thermo<float> =
        evaluateRaw s "heatOfVaporisation" t
        >>= fun molar -> ok (molar * 1000.0 / s.MolarMass)

    /// Low-pressure vapour viscosity [Pa*s].
    let gasViscosity (s: LiquidSpecies) (t: float<K>) : Thermo<float<Pa*s>> =
        evaluateRaw s "gasViscosity" t >>= fun v -> ok (v * 1.0<Pa*s>)

    /// Low-pressure vapour thermal conductivity [W/(m*K)].
    let gasConductivity (s: LiquidSpecies) (t: float<K>) : Thermo<float<W/(m*K)>> =
        evaluateRaw s "gasConductivity" t >>= fun v -> ok (v * 1.0<W/(m*K)>)

    /// Liquid Prandtl number, assembled from three separate correlations. Each
    /// carries its own validity range, so the warnings accumulate.
    let liquidPrandtl (s: LiquidSpecies) (t: float<K>) : Thermo<float> =
        liquidViscosity s t
        >>= fun mu ->
            liquidHeatCapacity s t
            >>= fun cp ->
                liquidConductivity s t
                >>= fun k -> ok (float cp * float mu / float k)
