namespace Whb.Core

open System
open Constants
open Types

/// <summary>
/// Provides nozzles functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Nozzles =

    /// <summary>
    /// Calculates or returns pipetable for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let pipeTable =
        [ "2\"",   52.50,  49.25,  42.85
          "2.5\"", 62.71,  59.00,  53.98
          "3\"",   77.93,  73.66,  66.65
          "4\"",  102.26,  97.18,  87.32
          "5\"",  128.19, 122.25, 109.54
          "6\"",  154.05, 146.33, 131.75
          "8\"",  202.72, 193.68, 173.05
          "10\"", 254.51, 242.93, 215.90
          "12\"", 303.23, 288.90, 257.20
          "14\"", 333.34, 317.50, 284.20
          "16\"", 381.00, 363.52, 325.40
          "18\"", 428.66, 409.58, 366.70
          "20\"", 477.82, 455.62, 407.98
          "24\"", 574.65, 547.72, 490.54 ]

    /// <summary>
    /// Represents schedule data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Schedule = Sch40 | Sch80 | Sch160

    let private idOf (sch: Schedule) (nps: string, s40, s80, s160) =
        match sch with
        | Sch40 -> (nps, s40 / 1000.0)
        | Sch80 -> (nps, s80 / 1000.0)
        | Sch160 -> (nps, s160 / 1000.0)

    /// <summary>
    /// Calculates or returns selectpipe for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let selectPipe (sch: Schedule) (idReq: float) =
        pipeTable
        |> List.map (idOf sch)
        |> List.tryFind (fun (_, d) -> d >= idReq)
        |> Option.defaultValue (idOf sch (List.last pipeTable))

    /// <summary>
    /// Calculates or returns equaldutypositions for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let equalDutyPositions (n: int) (zs: float[]) (rate: float[]) =
        let m = zs.Length
        if m < 2 || n < 1 then [ zs.[m / 2] ]
        else
            let dz = (zs.[m - 1] - zs.[0]) / float (m - 1)
            let tot = rate |> Array.sumBy (fun r -> r * dz)
            let target = tot / float n
            let res = ResizeArray<float>()
            let mutable acc = 0.0
            let mutable num = 0.0
            let mutable den = 0.0
            for i in 0 .. m - 1 do
                let w = rate.[i] * dz
                acc <- acc + w
                num <- num + zs.[i] * w
                den <- den + w
                if acc >= target * float (res.Count + 1) - 1e-12 && res.Count < n then
                    res.Add(if den > 0.0 then num / den else zs.[i])
                    num <- 0.0
                    den <- 0.0
            while res.Count < n do
                res.Add(zs.[m - 1] * (float res.Count + 0.5) / float n)
            res |> List.ofSeq |> List.truncate n

    /// <summary>
    /// Calculates or returns sizeset for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let sizeSet
        (service: string) (w: float) (rho: float) (vTarget: float)
        (rhoV2Max: float) (sch: Schedule) (nFixed: int) (nMin: int) (nMax: int)
        (positions: int -> float list) (note: string) =

        let vAllow = min vTarget (sqrt (rhoV2Max / rho))
        let tryCount (n: int) =
            let aReq = w / (rho * vAllow * float n)
            let dReq = sqrt (4.0 * aReq / Math.PI)
            let (nps, d) = selectPipe sch dReq
            let a = Math.PI * d * d / 4.0 * float n
            let v = w / (rho * a)
            let rv2 = rho * v * v
            (n, nps, d, v, rv2)

        let candidates =
            if nFixed > 0 then [ tryCount nFixed ]
            else [ for n in (max 1 nMin) .. (max (max 1 nMin) nMax) -> tryCount n ]

        let ok = candidates |> List.filter (fun (_, _, _, _, rv2) -> rv2 <= rhoV2Max)
        let (n, nps, d, v, rv2) =
            match ok with
            | [] -> candidates |> List.minBy (fun (_, _, _, _, rv2) -> rv2)
            | l -> List.head l

        { Service = service
          Count = n
          Id = d
          Nps = nps
          Positions = positions n
          Velocity = v
          RhoV2 = rv2
          RhoUsed = rho
          Note = note }

    /// <summary>
    /// Calculates or returns staggeredpositions for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let staggeredPositions (n: int) (riser: float list) (l: float) =
        let sorted = riser |> List.sort
        let mids =
            if List.length sorted < 2 then []
            else List.pairwise sorted |> List.map (fun (a, b) -> 0.5 * (a + b))
        let ends = [ 0.5 * (List.head sorted); 0.5 * (List.last sorted + l) ]
        let cand = (ends @ mids) |> List.sort
        if List.length cand >= n then
            let step = float (List.length cand - 1) / float (max 1 (n - 1))
            [ for k in 0 .. n - 1 -> cand.[int (round (float k * step))] ] |> List.distinct
        else cand

    /// <summary>
    /// Calculates or returns design for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let design (case: DesignCase) (sat: Steam.SatProps) (axial: AxialResult list) (circ: CirculationResult) =
        let group (svc: string) (lines: Piping.Line list) (rho: float) (note: string) =
            lines
            |> List.groupBy (fun l -> l.Nps)
            |> List.map (fun (nps, ls) ->
                let n = ls |> List.sumBy (fun l -> l.Count)
                let d = (List.head ls).Id
                let aTot = ls |> List.sumBy (fun l -> Piping.area l * float l.Count)
                let aAll = lines |> List.sumBy (fun l -> Piping.area l * float l.Count)
                let w = circ.CircFlow * aTot / aAll
                let v = w / (rho * aTot)
                { Service = sprintf "%s - %s" svc nps
                  Count = n
                  Id = d
                  Nps = nps
                  Positions = ls |> List.map (fun l -> l.ZNozzle) |> List.sort
                  Velocity = v
                  RhoV2 = rho * v * v
                  RhoUsed = rho
                  Note =
                    sprintf "%s  |  posizioni angolari: %s"
                        note
                        (ls |> List.map (fun l -> sprintf "%s a %.0f°" l.Tag l.AngleDeg) |> String.concat ", ") })
        let rhoMix = TwoPhase.homogeneousDensity circ.XOutRiser sat
        group "Riser (cielo mantello)" case.Loop.Risers rhoMix
            "Estrazione della miscela; cappello antitrascinamento sopra la bocca."
        @ group "Downcomer (fondo mantello)" case.Loop.Downcomers sat.RhoL
            "Rientro dell'acqua; deflettore per evitare impingement sul fascio."

