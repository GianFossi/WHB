namespace XSulfur

open System
open System.Globalization
open System.IO
open Ganfoss.ROP
open WhbThermo.Domain
open WhbThermo.Data

/// Condensation curves imported from a process simulator.
///
/// This is the industrial practice for Claus condensers and it is deliberate,
/// not a shortcut. The heat release of a condensing sulfur stream depends on the
/// full reactive vapour-liquid equilibrium of the allotrope mixture together
/// with the water, H2S, SO2, CO2 and nitrogen around it. Reproducing that inside
/// a rating engine means reimplementing a flash package, and doing it slightly
/// differently from the simulator that produced the process guarantee is worse
/// than not doing it at all: the rating and the heat balance would then disagree
/// and nobody could say which was right.
///
/// So the curve is an INPUT. It comes point by point from ProMax, HYSYS with
/// Sulsim, Symmetry or equivalent, and this module's job is to validate it,
/// interpolate it, and derive from it the quantities the heat transfer
/// correlations need - above all dT/dh, which feeds the Silver / Bell & Ghaly Z
/// factor.
///
/// `Speciation` remains useful alongside this: it gives an independent check on
/// the imported curve, and it works when no simulator run is available.
module CondensationCurve =

    /// One point on the condensation path, as exported by a simulator.
    type CurvePoint =
        { Temperature   : float<K>
          /// Cumulative heat removed from the stream [J/kg of total stream],
          /// increasing from the hot end.
          HeatRemoved   : float
          /// Vapour mass fraction of the total stream.
          VapourFraction : float
          /// Mass fraction of the stream that is condensed sulfur.
          CondensedSulfur : float
          /// Vapour specific heat at this point [J/(kg*K)], if exported.
          VapourHeatCapacity : float option }

    type Curve =
        { Points      : CurvePoint list
          Description : string
          Source      : string }

        member this.HotEnd = List.head this.Points
        member this.ColdEnd = List.last this.Points
        member this.TotalDuty = this.ColdEnd.HeatRemoved - this.HotEnd.HeatRemoved

    // ---------- validation ----------

    /// Validate an imported curve.
    ///
    /// The checks exist because a mis-exported curve is common and silent:
    /// columns swapped, the file ordered cold-to-hot, or duty exported as a
    /// rate rather than a specific quantity. Each of those produces a curve that
    /// interpolates cleanly and rates to nonsense.
    let validate (points: CurvePoint list) (description: string) (source: string)
                 : Thermo<Curve> =
        match points with
        | [] -> fail (InvalidMixture "the condensation curve has no points")
        | [ _ ] -> fail (InvalidMixture "a condensation curve needs at least two points")
        | _ ->
            let temperatures = points |> List.map (fun p -> float p.Temperature)
            let duties = points |> List.map (fun p -> p.HeatRemoved)
            let vapourFractions = points |> List.map (fun p -> p.VapourFraction)

            let monotonicallyCooling =
                List.pairwise temperatures |> List.forall (fun (a, b) -> b < a)
            let monotonicDuty =
                List.pairwise duties |> List.forall (fun (a, b) -> b > a)
            let fractionsValid =
                vapourFractions |> List.forall (fun v -> v >= -1e-9 && v <= 1.0 + 1e-9)
            let vapourFalling =
                List.pairwise vapourFractions |> List.forall (fun (a, b) -> b <= a + 1e-9)

            if not monotonicallyCooling then
                fail (InvalidMixture
                        ("the curve is not ordered hot to cold; a file exported cold-to-hot "
                         + "interpolates cleanly and rates to nonsense"))
            elif not monotonicDuty then
                fail (InvalidMixture
                        ("cumulative heat removed is not increasing along the curve; check "
                         + "whether duty was exported as a rate rather than a specific quantity"))
            elif not fractionsValid then
                fail (InvalidMixture "a vapour fraction lies outside [0,1]")
            else
                ok { Points = points; Description = description; Source = source }
                |> warnIf (not vapourFalling)
                          (CorrelationExtrapolated
                            ("condensation curve",
                             "the vapour fraction is not monotonically falling; this is "
                             + "possible with reheat but unusual in a condenser"))
                |> warnIf (points.Length < 10)
                          (CorrelationExtrapolated
                            ("condensation curve",
                             $"only {points.Length} points; a sulfur condensation curve is "
                             + "strongly non-linear and 20 or more is normal"))

    // ---------- loading ----------

    /// Parse a CSV export. Expected columns, in any order:
    ///   T_K, Q_J_kg, vapourFraction, condensedSulfur, [cp_vapour_J_kgK]
    let parseCsv (payload: string) : Thermo<Curve> =
        let lines =
            payload.Split('\n')
            |> Array.map (fun l -> l.Trim())
            |> Array.filter (fun l -> l <> "" && not (l.StartsWith "#"))

        if lines.Length < 2 then
            fail (DatabaseParseError "the curve file has no data rows")
        else
            let header = lines.[0].Split(',') |> Array.map (fun h -> h.Trim())
            let index name = Array.tryFindIndex (fun h -> h = name) header

            match index "T_K", index "Q_J_kg", index "vapourFraction" with
            | Some ti, Some qi, Some vi ->
                let si = index "condensedSulfur"
                let ci = index "cp_vapour_J_kgK"
                let number (cells: string[]) i =
                    Double.Parse(cells.[i].Trim(), CultureInfo.InvariantCulture)
                try
                    let points =
                        lines
                        |> Array.skip 1
                        |> Array.map (fun line ->
                            let cells = line.Split(',')
                            { Temperature = number cells ti * 1.0<K>
                              HeatRemoved = number cells qi
                              VapourFraction = number cells vi
                              CondensedSulfur =
                                match si with Some i -> number cells i | None -> 0.0
                              VapourHeatCapacity =
                                match ci with Some i -> Some (number cells i) | None -> None })
                        |> List.ofArray
                    validate points "imported condensation curve" "simulator export (CSV)"
                with ex ->
                    fail (DatabaseParseError $"malformed curve row: {ex.Message}")
            | _ ->
                fail (DatabaseParseError
                        "the curve file must contain T_K, Q_J_kg and vapourFraction columns")

    let loadFile (path: string) : Thermo<Curve> = DataStore.load path parseCsv

    // ---------- interrogation ----------

    let private interpolate (x0, y0) (x1, y1) x =
        if abs (x1 - x0) < 1e-12 then y0
        else y0 + (y1 - y0) * (x - x0) / (x1 - x0)

    /// Bracketing pair of points for a temperature, if it lies on the curve.
    let private bracketByTemperature (curve: Curve) (t: float<K>) =
        curve.Points
        |> List.pairwise
        |> List.tryFind (fun (a, b) -> t <= a.Temperature && t >= b.Temperature)

    /// Cumulative heat removed at a given temperature [J/kg].
    let heatRemovedAt (curve: Curve) (t: float<K>) : Thermo<float> =
        match bracketByTemperature curve t with
        | Some (a, b) ->
            ok (interpolate (float a.Temperature, a.HeatRemoved)
                            (float b.Temperature, b.HeatRemoved) (float t))
        | None ->
            fail (OutsideFitRange
                    ("condensation curve", "temperature", float t,
                     float curve.ColdEnd.Temperature, float curve.HotEnd.Temperature))

    /// Vapour fraction at a given temperature.
    let vapourFractionAt (curve: Curve) (t: float<K>) : Thermo<float> =
        match bracketByTemperature curve t with
        | Some (a, b) ->
            ok (interpolate (float a.Temperature, a.VapourFraction)
                            (float b.Temperature, b.VapourFraction) (float t))
        | None ->
            fail (OutsideFitRange
                    ("condensation curve", "temperature", float t,
                     float curve.ColdEnd.Temperature, float curve.HotEnd.Temperature))

    /// Local slope dT/dh [K/(J/kg)] of the condensation curve.
    ///
    /// This is the quantity the Silver / Bell & Ghaly method needs, and it is
    /// the reason the curve must come from a flash rather than a correlation.
    /// It is computed as a local finite difference on the imported points, so
    /// its resolution is the resolution of the export - another reason a coarse
    /// curve is flagged.
    let temperatureSlopeAt (curve: Curve) (t: float<K>) : Thermo<float> =
        match bracketByTemperature curve t with
        | Some (a, b) ->
            let deltaQ = b.HeatRemoved - a.HeatRemoved
            if abs deltaQ < 1e-9 then
                fail (CorrelationExtrapolated
                        ("condensation curve",
                         "two adjacent points have the same cumulative duty, so the local "
                         + "slope is undefined"))
            else
                // Cooling, so dT is negative and dh positive; the magnitude is
                // what the Z factor uses.
                ok (abs (float b.Temperature - float a.Temperature) / abs deltaQ)
        | None ->
            fail (OutsideFitRange
                    ("condensation curve", "temperature", float t,
                     float curve.ColdEnd.Temperature, float curve.HotEnd.Temperature))

    /// Silver / Bell & Ghaly Z at a point on the curve:
    ///   Z = x cp_vapour (dT/dh)
    ///
    /// Requires a vapour heat capacity, either exported with the curve or
    /// supplied. It is not defaulted: a wrong cp here propagates straight into
    /// the effective coefficient.
    let zFactorAt (curve: Curve) (t: float<K>) (vapourHeatCapacity: float option)
                  : Thermo<float> =
        vapourFractionAt curve t
        >>= fun x ->
            temperatureSlopeAt curve t
            >>= fun slope ->
                let exported =
                    bracketByTemperature curve t
                    |> Option.bind (fun (a, _) -> a.VapourHeatCapacity)
                match vapourHeatCapacity |> Option.orElse exported with
                | Some cp when cp > 0.0 -> ok (x * cp * slope)
                | Some _ -> fail (InvalidMixture "the vapour heat capacity must be positive")
                | None ->
                    fail (InvalidMixture
                            ("no vapour heat capacity: export it with the curve or supply it, "
                             + "it is not safe to default"))

    /// Split the curve into equal-duty zones, which is how a condenser is rated:
    /// each zone gets its own coefficient and its own LMTD rather than one
    /// average across a strongly non-linear curve.
    let zones (curve: Curve) (count: int) : Thermo<(float<K> * float<K> * float) list> =
        if count < 1 then
            fail (InvalidMixture "the zone count must be positive")
        else
            let start = curve.HotEnd.HeatRemoved
            let total = curve.TotalDuty
            let step = total / float count

            let temperatureAtDuty q =
                curve.Points
                |> List.pairwise
                |> List.tryFind (fun (a, b) -> q >= a.HeatRemoved && q <= b.HeatRemoved)
                |> Option.map (fun (a, b) ->
                    interpolate (a.HeatRemoved, float a.Temperature)
                                (b.HeatRemoved, float b.Temperature) q * 1.0<K>)

            let boundaries =
                [ 0 .. count ] |> List.map (fun i -> start + step * float i)

            let temperatures = boundaries |> List.map temperatureAtDuty

            if temperatures |> List.exists Option.isNone then
                fail (CorrelationExtrapolated
                        ("condensation curve", "a zone boundary fell outside the imported duty range"))
            else
                let values = temperatures |> List.map Option.get
                ok (List.pairwise values |> List.map (fun (a, b) -> a, b, step))
                |> warnIf (count > (curve.Points.Length - 1))
                          (CorrelationExtrapolated
                            ("condensation curve",
                             $"{count} zones requested from {curve.Points.Length} imported "
                             + "points; the zoning is finer than the data behind it"))
