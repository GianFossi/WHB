namespace WhbThermo.Properties

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Pure-component property evaluation. Every function is total: out-of-range
/// input produces a value plus a warning, never a silent lie and never an exception.
module PureComponent =

    /// Sutherland two-parameter law:
    ///   phi(T) = phi0 * (Tref + S)/(T + S) * (T/Tref)^1.5
    let private sutherland (fit: SutherlandFit) (t: float<K>) =
        let tK = float t
        let tRef = float fit.TRef
        let s = float fit.SutherlandK
        fit.Coeff0 * ((tRef + s) / (tK + s)) * ((tK / tRef) ** 1.5)

    let private rangeCheck (species: string) (property: string) (lo: float<K>) (hi: float<K>) (t: float<K>) v =
        ok v
        |> warnIf (t < lo || t > hi)
                  (OutsideFitRange (species, property, float t, float lo, float hi))

    /// NASA CEA transport law: ln(phi) = A ln T + B/T + C/T^2 + D.
    /// phi is in micropoise for viscosity and microwatt/(cm*K) for conductivity.
    let private nasaTransport (iv: NasaTransportInterval) (t: float<K>) =
        let tK = float t
        exp (iv.A * log tK + iv.B / tK + iv.C / (tK * tK) + iv.D)

    let private selectTransport (species: string) (property: string)
                                (intervals: NasaTransportInterval list) (t: float<K>) =
        match intervals |> List.tryFind (fun iv -> t >= iv.TMin && t <= iv.TMax) with
        | Some iv -> ok iv
        | None ->
            match intervals with
            | [] -> fail (DatabaseParseError $"{species}: no {property} intervals")
            | _ ->
                let nearest =
                    intervals
                    |> List.minBy (fun iv ->
                        if t < iv.TMin then float (iv.TMin - t) else float (t - iv.TMax))
                let lo = intervals |> List.map (fun iv -> float iv.TMin) |> List.min
                let hi = intervals |> List.map (fun iv -> float iv.TMax) |> List.max
                ok nearest
                |> warn (OutsideFitRange (species, property, float t, lo, hi))

    /// Dynamic viscosity of a pure species [Pa*s].
    let viscosity (sp: SpeciesData) (t: float<K>) : Thermo<float<Pa*s>> =
        match sp.Transport with
        | NasaCea (mu, _) ->
            selectTransport sp.Key "viscosity (NASA CEA)" mu t
            >>= fun iv -> ok (nasaTransport iv t * 1e-7<Pa*s>)   // micropoise -> Pa*s
        | SutherlandPair (fit, _) ->
            sutherland fit t * 1.0<Pa*s>
            |> rangeCheck sp.Key "viscosity" fit.TMin fit.TMax t
        | NoTransportData reason ->
            fail (DatabaseParseError $"{sp.Key} viscosity: {reason}")

    /// Thermal conductivity of a pure species [W/(m*K)].
    let conductivity (sp: SpeciesData) (t: float<K>) : Thermo<float<W/(m*K)>> =
        match sp.Transport with
        | NasaCea (_, k) when not k.IsEmpty ->
            selectTransport sp.Key "conductivity (NASA CEA)" k t
            >>= fun iv -> ok (nasaTransport iv t * 1e-4<W/(m*K)>) // uW/(cm*K) -> W/(m*K)
        | NasaCea _ ->
            match sp.Conductivity with
            | Some fit ->
                sutherland fit t * 1.0<W/(m*K)>
                |> rangeCheck sp.Key "conductivity" fit.TMin fit.TMax t
            | Option.None ->
                fail (DatabaseParseError
                        ($"{sp.Key} conductivity: NASA transport has no conductivity block and "
                         + "there is no Sutherland fallback"))
        | SutherlandPair (_, fit) ->
            sutherland fit t * 1.0<W/(m*K)>
            |> rangeCheck sp.Key "conductivity" fit.TMin fit.TMax t
        | NoTransportData reason ->
            fail (DatabaseParseError $"{sp.Key} conductivity: {reason}")

    /// Molar heat capacity from a Shomate segment [J/(mol*K)].
    let private shomateCp (seg: ShomateSegment) (t: float<K>) =
        let x = float t / 1000.0
        seg.A + seg.B * x + seg.C * x * x + seg.D * x * x * x + seg.E / (x * x)

    /// Picks the segment covering T, or the nearest one (with a warning) if T falls outside.
    let private selectSegment (species: string) (segments: ShomateSegment list) (t: float<K>) =
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
                |> warn (OutsideFitRange (species, "Cp", float t, lo, hi))

    /// Cp/R from a NASA-7 segment [dimensionless].
    let private nasa7CpOverR (seg: Nasa7Segment) (t: float<K>) =
        let x = float t
        let a = seg.A
        // Cp/R uses a1..a5 only; a6 and a7 are the enthalpy and entropy integration constants.
        a.[0] + x * (a.[1] + x * (a.[2] + x * (a.[3] + x * a.[4])))

    /// H/(R*T) from a NASA-7 segment [dimensionless].
    let private nasa7HOverRT (seg: Nasa7Segment) (t: float<K>) =
        let x = float t
        let a = seg.A
        a.[0] + x * (a.[1] * 0.5 + x * (a.[2] / 3.0 + x * (a.[3] * 0.25 + x * a.[4] * 0.2)))
        + a.[5] / x

    /// Same nearest-segment fallback policy as Shomate: extrapolate, but warn.
    let private selectNasa7 (species: string) (segments: Nasa7Segment list) (t: float<K>) =
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
                |> warn (OutsideFitRange (species, "Cp (NASA-7)", float t, lo, hi))

    /// Cp/R from a NASA-9 segment. Horner form: no Math.Pow on the hot path.
    let private nasa9CpOverR (seg: Nasa9Segment) (t: float<K>) =
        Numerics.nasa9CpOverR seg.A (float t)

    /// H/(R T) from a NASA-9 segment.
    let private nasa9HOverRT (seg: Nasa9Segment) (t: float<K>) =
        Numerics.nasa9HOverRT seg.A seg.B.[0] (float t)

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
                |> warn (OutsideFitRange (species, "Cp (NASA-9)", float t, lo, hi))

    /// Mass-basis specific heat of a pure species [J/(kg*K)].
    let specificHeatMass (sp: SpeciesData) (t: float<K>) : Thermo<float<J/(kg*K)>> =
        match sp.Cp with
        | Nasa9 segments ->
            selectNasa9 sp.Key segments t
            >>= fun seg ->
                let cpMolar = nasa9CpOverR seg t * float Ru
                ok (cpMolar / (float sp.MolarMass / 1000.0) * 1.0<J/(kg*K)>)
        | Nasa7 segments ->
            selectNasa7 sp.Key segments t
            >>= fun seg ->
                let cpMolar = nasa7CpOverR seg t * float Ru   // J/(mol*K)
                let m = float sp.MolarMass / 1000.0           // kg/mol
                ok (cpMolar / m * 1.0<J/(kg*K)>)
        | Shomate segments ->
            selectSegment sp.Key segments t
            >>= fun seg ->
                let cpMolar = shomateCp seg t                    // J/(mol*K)
                let m = float sp.MolarMass / 1000.0              // kg/mol
                ok (cpMolar / m * 1.0<J/(kg*K)>)
        | AnchorOnly (cp500, _) ->
            // kJ/(kg*K) -> J/(kg*K); constant, no temperature dependence available.
            ok (cp500 * 1000.0 * 1.0<J/(kg*K)>)
            |> warn (CpIsAnchorOnly sp.Key)

    /// Molar enthalpy relative to 298.15 K [kJ/mol], from the Shomate integral.
    /// Available only for species with a published fit.
    let enthalpy (sp: SpeciesData) (t: float<K>) : Thermo<float> =
        let reference = 298.15<K>
        match sp.Cp with
        | Nasa9 segments ->
            selectNasa9 sp.Key segments t
            >>= fun segT ->
                selectNasa9 sp.Key segments reference
                >>= fun segRef ->
                    let hT = nasa9HOverRT segT t * float Ru * float t
                    let hRef = nasa9HOverRT segRef reference * float Ru * float reference
                    ok ((hT - hRef) / 1000.0)
        | Nasa7 segments ->
            // H(T) - H(298.15), evaluated on the segment covering each endpoint.
            selectNasa7 sp.Key segments t
            >>= fun segT ->
                selectNasa7 sp.Key segments reference
                >>= fun segRef ->
                    let hT = nasa7HOverRT segT t * float Ru * float t
                    let hRef = nasa7HOverRT segRef reference * float Ru * float reference
                    ok ((hT - hRef) / 1000.0)   // kJ/mol
        | Shomate segments ->
            selectSegment sp.Key segments t
            >>= fun seg ->
                let x = float t / 1000.0
                ok (x * (seg.A + x * (seg.B * 0.5 + x * (seg.C / 3.0 + x * seg.D * 0.25)))
                    - seg.E / x + seg.F - seg.H)
        | AnchorOnly _ ->
            fail (NoCpDataAvailable sp.Key)
