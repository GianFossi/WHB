namespace WhbThermo.Radiation

open System
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// Weighted-sum-of-gray-gases total emissivity, Smith, Shen & Friedman (1982).
///
///     eps    = SUM_i a_i(T) * (1 - exp(-k_i * p * L))
///     a_i(T) = SUM_j b[i][j] * T^(j-1)
///     a_0    = 1 - SUM_i a_i                       (clear gas)
///     k      = -ln(1 - eps) / L                    (Beer-Lambert)
///
/// Unlike the Leckner chart correlation this model is fully populated here, and
/// it yields the absorption coefficient directly - which is what a marching or
/// finite-volume solver actually needs.
///
/// Validity: T 600-2400 K, p*L 0.001-10 atm*m, total pressure 1 atm.
/// The WHB envelope (250-1400 degC) sits inside the temperature range; short
/// tube beam lengths sit near the lower p*L bound, which is warned about.
module Wsgg =

    [<Literal>]
    let AtmPerBar = 0.9869232667160128

    type CoefficientSet =
        { Id          : string
          /// H2O / (H2O + CO2) at which this set was tabulated.
          RR          : float
          Description : string
          /// Pressure absorption coefficients [1/(atm*m)], one per gray gas.
          K           : float[]
          /// b.[i].[j], cubic in T.
          B           : float[][] }

    type Model =
        { Sets      : CoefficientSet list
          TMin      : float<K>
          TMax      : float<K>
          PathMin   : float
          PathMax   : float
          Source    : string }

        member this.TryFind id = this.Sets |> List.tryFind (fun s -> s.Id = id)

    // ---------- loading ----------

    [<CLIMutable>]
    type private SetDto =
        { id: string; rr: float; description: string; k: float[]; b: float[][] }

    [<CLIMutable>]
    type private ValidityDto =
        { tMinK: float; tMaxK: float; pathMinAtmM: float; pathMaxAtmM: float
          totalPressureAtm: float }

    [<CLIMutable>]
    type private RootDto =
        { model: string; status: string; validity: ValidityDto
          source: string; sets: SetDto[] }

    let private options =
        let o = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
        o.Converters.Add(JsonFsharpConverter())
        o

    let private validateSet (d: SetDto) : Thermo<CoefficientSet> =
        if isNull d.k || d.k.Length = 0 then
            fail (DatabaseParseError $"WSGG set '{d.id}': no absorption coefficients")
        elif isNull d.b || d.b.Length <> d.k.Length then
            fail (DatabaseParseError
                    $"WSGG set '{d.id}': {d.k.Length} gray gases but "
                    + $"{(if isNull d.b then 0 else d.b.Length)} weight polynomials")
        elif d.b |> Array.exists (fun row -> isNull row || row.Length <> 4) then
            fail (DatabaseParseError $"WSGG set '{d.id}': weight polynomials must be cubic (4 coefficients)")
        else
            ok { Id = d.id; RR = d.rr; Description = d.description
                 K = Array.copy d.k; B = d.b |> Array.map Array.copy }

    let parse (json: string) : Thermo<Model> =
        try
            let r = JsonSerializer.Deserialize<RootDto>(json, options)
            r.sets
            |> List.ofArray
            |> traverseList validateSet
            >>= fun sets ->
                ok { Sets = sets
                     TMin = r.validity.tMinK * 1.0<K>
                     TMax = r.validity.tMaxK * 1.0<K>
                     PathMin = r.validity.pathMinAtmM
                     PathMax = r.validity.pathMaxAtmM
                     Source = r.source }
        with ex ->
            fail (DatabaseParseError ex.Message)

    [<Literal>]
    let DefaultFile = "wsgg-smith-1982.json"

    let load () : Thermo<Model> = DataStore.load DefaultFile parse

    let loadFile (path: string) : Thermo<Model> = DataStore.load path parse

    // ---------- coefficient selection ----------

    /// Piecewise-linear interpolation over RR (Marzouk & Huckaby, Appendix B),
    /// which the source shows to be materially more accurate than the
    /// piecewise-constant scheme, especially at low H2O fractions.
    let private interpolate (a: CoefficientSet) (b: CoefficientSet) (delta: float) =
        { a with
            Id = $"{a.Id}~{b.Id}"
            K = Array.map2 (fun ka kb -> ka + delta * (kb - ka)) a.K b.K
            B = Array.map2 (Array.map2 (fun ba bb -> ba + delta * (bb - ba))) a.B b.B }

    /// Selects (and blends) the coefficient set for a given composition.
    /// `rr` = H2O / (H2O + CO2); `pwAtm` = H2O partial pressure [atm].
    let selectCoefficients (model: Model) (rr: float) (pwAtm: float) : Thermo<CoefficientSet> =
        let get id =
            match model.TryFind id with
            | Some s -> ok s
            | None -> fail (DatabaseParseError $"WSGG coefficient set '{id}' missing")

        // Which tabulated set represents RR = 1 depends on the water partial pressure.
        let unityId = if pwAtm > 0.5 then "h2oPure" else "h2oOnly"

        if rr <= 0.5 then
            get "co2Only" >>= fun lo -> get "ratio1" >>= fun hi ->
                ok (interpolate lo hi ((rr - 0.0) / 0.5))
        elif rr <= 2.0 / 3.0 then
            get "ratio1" >>= fun lo -> get "ratio2" >>= fun hi ->
                ok (interpolate lo hi ((rr - 0.5) / (2.0 / 3.0 - 0.5)))
        else
            get "ratio2" >>= fun lo -> get unityId >>= fun hi ->
                ok (interpolate lo hi ((rr - 2.0 / 3.0) / (1.0 - 2.0 / 3.0)))

    /// Species this model accounts for. Everything else in the gas is treated
    /// as transparent.
    let radiatingSpecies = [ "H2O"; "CO2" ]

    /// Radiating species present in a mixture that the model CANNOT account for.
    ///
    /// SO2 and H2S are the ones that matter in Claus service: both absorb in the
    /// infrared and both are always present, SO2 at 1-3 % and H2S at 2-5 %. No
    /// open WSGG or Leckner coefficient set exists for either - searched and not
    /// found - so their contribution is simply missing from the emissivity, and
    /// the result is LOW by an amount nobody here can quantify.
    ///
    /// This is reported rather than silently ignored, because an emissivity that
    /// is quietly 10 or 20 % low looks exactly like one that is right.
    let unaccountedRadiators (composition: (string * float) list) =
        let known = set radiatingSpecies
        let absorbers = set [ "SO2"; "H2S"; "CH4"; "CO"; "NH3"; "N2O"; "NO"; "SO3"; "COS"; "CS2" ]
        composition
        |> List.filter (fun (key, y) ->
            y > 1e-3 && absorbers.Contains key && not (known.Contains key))

    /// Emissivity with an explicit statement of what was left out.
    ///
    /// Returns the value tagged with its provenance: Fitted when the mixture
    /// contains only species the model covers, Estimated when a radiating
    /// species had to be ignored.
    let emissivityWithCoverage (model: Model) (t: float<K>)
                               (pH2O: float<bar>) (pCO2: float<bar>) (le: float<m>)
                               (composition: (string * float) list)
                               : Thermo<Qualified<float>> =
        let missing = unaccountedRadiators composition
        emissivity model t pH2O pCO2 le
        >>= fun eps ->
            match missing with
            | [] ->
                ok (Qualified.fitted "Smith, Shen & Friedman (1982) WSGG, H2O + CO2" eps)
            | _ ->
                let names = missing |> List.map fst |> String.concat ", "
                ok (Qualified.estimated
                        (sprintf "Smith WSGG; %s present and NOT accounted for" names) eps)
                |> warn (CorrelationExtrapolated
                            ("WSGG coverage",
                             sprintf
                                "the mixture contains radiating species the model does not cover                                  (%s). Their emission is missing, so this emissivity is LOW by an                                  unquantified amount. No open coefficient set exists for them;                                  to close the gap, obtain SLW or line-by-line data, or accept                                  the result as a lower bound." names))

    // ---------- evaluation ----------

    /// Gray-gas weights a_i(T), plus the clear-gas weight a_0.
    let weights (set: CoefficientSet) (t: float<K>) =
        let tK = float t
        let a = set.B |> Array.map (fun b -> b.[0] + b.[1] * tK + b.[2] * tK ** 2.0 + b.[3] * tK ** 3.0)
        a, 1.0 - Array.sum a

    /// Total gas emissivity.
    ///
    /// `pH2O`, `pCO2` are partial pressures; `le` is the mean beam length.
    let emissivity (model: Model) (t: float<K>)
                   (pH2O: float<bar>) (pCO2: float<bar>) (le: float<m>) : Thermo<float> =
        let pwAtm = float pH2O * AtmPerBar
        let pcAtm = float pCO2 * AtmPerBar
        let pTotal = pwAtm + pcAtm
        let pathAtmM = pTotal * float le

        if pTotal <= 0.0 then
            ok 0.0 |> warn NoRadiatingSpecies
        else
            let rr = pwAtm / pTotal
            selectCoefficients model rr pwAtm
            >>= fun set ->
                let a, _ = weights set t
                let eps =
                    Array.map2 (fun ai ki -> ai * (1.0 - exp (-ki * pathAtmM))) a set.K
                    |> Array.sum
                ok (max 0.0 (min 1.0 eps))
                |> warnIf (t < model.TMin || t > model.TMax)
                          (OutsideFitRange ("WSGG Smith 1982", "emissivity", float t,
                                            float model.TMin, float model.TMax))
                |> warnIf (pathAtmM < model.PathMin || pathAtmM > model.PathMax)
                          (CorrelationExtrapolated ("WSGG Smith 1982",
                            $"p*L = %.5f{pathAtmM} atm*m outside %.3f{model.PathMin}-%.1f{model.PathMax}"))

    /// Beer-Lambert absorption coefficient [1/m], for use in an RTE solver.
    let absorptionCoefficient (model: Model) (t: float<K>)
                              (pH2O: float<bar>) (pCO2: float<bar>) (le: float<m>) : Thermo<float<1/m>> =
        emissivity model t pH2O pCO2 le
        >>= fun eps ->
            if eps >= 1.0 then
                fail (CorrelationExtrapolated ("WSGG", "emissivity reached unity, k is unbounded"))
            elif float le <= 0.0 then
                fail (InvalidMixture "mean beam length must be positive")
            else
                ok (-(log (1.0 - eps)) / float le * 1.0<1/m>)
