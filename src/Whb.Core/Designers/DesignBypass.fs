namespace Whb.Core

open System
open Constants
open Types

module DesignBypass =

    type MapPoint =
        { X: float
          TMix: float
          TTubes: float
          TBp: float
          DpTubes: float
          DpBpFric: float
          Duty: float
          Steam: float
          TLinerMax: float
          RhoValve: float
          TValve: float }

    /// <summary>
    /// How a reported limit came about.
    /// </summary>
    /// <remarks>
    /// A value produced by a genuine crossing and one produced by running out of computed map
    /// are both single numbers, and printing them identically makes the edge of a calculation
    /// look like a design limit. This is what keeps them apart.
    /// </remarks>
    type LimitKind =
        | FromCrossing
        | FromMapEdge

    type Input =
        { Case: DesignCase
          Mode: string
          TargetToleranceK: float
          Parallelism: int
          TotalGasFlow: float
          MixtureMolarMass: float
          LinerArea: float
          Phase: string -> unit
          AcquireWorker: unit -> unit
          ReleaseWorker: unit -> unit
          MapPointAt: float -> MapPoint }

    type Result =
        { PMap: MapPoint list
          XUsed: float
          Valve: ValveResult option
          BypassMapBracketsTarget: bool }

    let private interpMap (pts: MapPoint list) (sel: MapPoint -> float) (x: float) =
        let a = pts |> List.toArray
        if x <= a.[0].X then sel a.[0]
        elif x >= a.[a.Length - 1].X then sel a.[a.Length - 1]
        else
            let mutable i = 0
            while i < a.Length - 2 && a.[i + 1].X < x do i <- i + 1
            let f = (x - a.[i].X) / (a.[i + 1].X - a.[i].X)
            sel a.[i] + f * (sel a.[i + 1] - sel a.[i])

    let private limitTag =
        function
        | FromCrossing -> "VINCOLANTE"
        | FromMapEdge -> "BORDO MAPPA - non e' un limite"

    let private invertMapWithKind (pts: MapPoint list) (sel: MapPoint -> float) (target: float) =
        let f (x: float) = interpMap pts sel x - target
        let x0 = (List.head pts).X
        let x1 = (List.last pts).X
        if f x0 >= 0.0 then (x0, FromCrossing)
        elif f x1 <= 0.0 then (x1, FromMapEdge)
        else (bisect f x0 x1 1e-7 60, FromCrossing)

    let private invertMap (pts: MapPoint list) (sel: MapPoint -> float) (target: float) =
        fst (invertMapWithKind pts sel target)

    let run (input: Input) =
        let case = input.Case
        let bpSpec = case.Bypass
        let mode =
            (input.Mode |> Option.ofObj |> Option.defaultValue "adaptive").Trim().ToLowerInvariant()
        let xGridBase =
            if not case.Bypass.Enabled then [ 0.0 ]
            elif mode = "full" || case.Bypass.ValveOpenDeg.IsSome then
                [ 0.0; 0.003; 0.006; 0.010; 0.015; 0.021; 0.030; 0.045; 0.065; 0.090; 0.125; 0.170 ]
            elif mode = "fixed" then
                match case.Bypass.Fraction with
                | Some f -> [ 0.0; max 0.0 (min 0.170 f) ]
                | None -> [ 0.0 ]
            elif mode = "fast" then
                [ 0.0; 0.006; 0.010 ]
            else
                match case.Bypass.Fraction with
                | Some f ->
                    [ 0.0; 0.5 * f; f; min 0.170 (1.5 * f); min 0.170 (f + 0.015) ]
                    |> List.map (fun x -> Math.Round(max 0.0 (min 0.170 x), 6))
                    |> List.distinct
                    |> List.sort
                | None ->
                    [ 0.0; 0.006; 0.010 ]

        input.Phase "Evaluating bypass map and coupled thermal/circulation points"
        let mapPointWithPhase x =
            input.Phase (sprintf "Evaluating bypass map point x = %.3f" x)
            let sw = Diagnostics.Stopwatch.StartNew()
            try
                let p = input.MapPointAt x
                input.Phase (sprintf "Bypass map point x = %.3f solved in %.2f s" x sw.Elapsed.TotalSeconds)
                p
            with ex ->
                raise (
                    InvalidOperationException(
                        sprintf "Bypass map point x = %.4f failed after %.2f s: %s"
                            x sw.Elapsed.TotalSeconds ex.Message, ex))

        let evaluateGrid (xs: float list) =
            let arr = List.toArray xs
            let dop = min (max 1 input.Parallelism) arr.Length
            if dop <= 1 then arr |> Array.map (fun x -> (x, mapPointWithPhase x)) |> List.ofArray
            else
                let res = Array.zeroCreate arr.Length
                let opts = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = dop)
                try
                    Threading.Tasks.Parallel.For(
                        0, arr.Length, opts,
                        Action<int>(fun i ->
                            input.AcquireWorker ()
                            try res.[i] <- (arr.[i], mapPointWithPhase arr.[i])
                            finally input.ReleaseWorker ())) |> ignore
                with :? AggregateException as agg ->
                    raise (agg.Flatten().InnerExceptions |> Seq.head)
                List.ofArray res

        let pmap =
            let initial = evaluateGrid xGridBase
            if not case.Bypass.Enabled || mode <> "adaptive" || case.Bypass.Fraction.IsSome || case.Bypass.ValveOpenDeg.IsSome then
                initial
            else
                let points = ResizeArray(initial)
                let mutable candidates = [ 0.015; 0.021; 0.030; 0.045; 0.065; 0.090; 0.125; 0.170 ]
                let closeEnough (p: MapPoint) = abs (p.TMix - case.Bypass.TargetMixOut) <= max 0.05 input.TargetToleranceK
                let bracketsTarget () =
                    (snd points.[0]).TMix >= case.Bypass.TargetMixOut
                    || (snd points.[points.Count - 1]).TMix >= case.Bypass.TargetMixOut
                let mutable done_ = bracketsTarget () || (points |> Seq.exists (snd >> closeEnough))
                while not done_ && not candidates.IsEmpty do
                    let x = List.head candidates
                    candidates <- List.tail candidates
                    points.Add(x, mapPointWithPhase x)
                    done_ <- bracketsTarget () || (points |> Seq.exists (snd >> closeEnough))
                points |> Seq.toList
            |> List.map snd

        let qDyn (x: float) =
            let rho = max 1e-3 (interpMap pmap (fun p -> p.RhoValve) x)
            let vel = input.TotalGasFlow * x / (rho * input.LinerArea)
            0.5 * rho * vel * vel
        let zetaRequired (x: float) =
            let q = qDyn x
            if q < 1e-9 then 1.0e7
            else
                let dpT = interpMap pmap (fun p -> p.DpTubes) x
                let dpF = interpMap pmap (fun p -> p.DpBpFric) x
                max 0.0 ((dpT - dpF) / q - bpSpec.ExtraK)
        let fractionForAngle (thetaDeg: float) =
            let z = Valve.zetaOpening thetaDeg
            let res (x: float) =
                let q = qDyn x
                interpMap pmap (fun p -> p.DpBpFric) x + (bpSpec.ExtraK + z) * q
                - interpMap pmap (fun p -> p.DpTubes) x
            let xMax = (List.last pmap).X
            if res xMax < 0.0 then xMax
            else bisect res 1e-6 xMax 1e-8 70
        let angleForFraction (x: float) = Valve.openingForZeta (zetaRequired x)

        let xUsed =
            if not case.Bypass.Enabled then 0.0
            else
                match bpSpec.ValveOpenDeg with
                | Some th -> fractionForAngle th
                | None ->
                    match case.Bypass.Fraction with
                    | Some f -> max 0.0 (min 0.5 f)
                    | None ->
                        if (List.head pmap).TMix >= case.Bypass.TargetMixOut then 0.0
                        else invertMap pmap (fun p -> p.TMix) case.Bypass.TargetMixOut

        let bypassMapBracketsTarget =
            not case.Bypass.Enabled
            || (List.head pmap).TMix >= case.Bypass.TargetMixOut
            || (List.last pmap).TMix >= case.Bypass.TargetMixOut

        let valveRes =
            if not case.Bypass.Enabled then None
            else
                let xTop = (List.last pmap).X
                let velAt (x: float) =
                    let rho = max 1e-3 (interpMap pmap (fun p -> p.RhoValve) x)
                    (rho, input.TotalGasFlow * x / (rho * input.LinerArea))
                let mkPoint (th: float) (note: string) =
                    let x = fractionForAngle th
                    let z = Valve.zetaOpening th
                    let (rho, vel) = velAt x
                    let q = 0.5 * rho * vel * vel
                    let dpV = z * q
                    let vth = Valve.throatVelocity dpV rho
                    let tv = interpMap pmap (fun p -> p.TValve) x
                    { OpenDeg = th
                      ClosureDeg = 90.0 - th
                      Zeta = z
                      Fraction = x
                      MassFlowBypass = input.TotalGasFlow * x
                      RhoValve = rho
                      VelPipe = vel
                      VelThroat = vth
                      Mach = vth / Valve.sonic 1.35 input.MixtureMolarMass tv
                      RhoV2Throat = rho * vth * vth
                      DpValve = dpV
                      ZetaTheory = Valve.zetaFlatDisc 0.03 (90.0 - th)
                      Cv = Valve.cvFromZeta bpSpec.LinerId z
                      Kv = Valve.kvFromZeta bpSpec.LinerId z
                      KvRequired = Valve.kvRequired (input.TotalGasFlow * x) rho dpV
                      XRatio = Valve.pressureDropRatio dpV case.Gas.PIn
                      DpBypassTot = interpMap pmap (fun p -> p.DpBpFric) x + (bpSpec.ExtraK + z) * q
                      DpTubes = interpMap pmap (fun p -> p.DpTubes) x
                      TOutTubes = interpMap pmap (fun p -> p.TTubes) x
                      TOutBypass = interpMap pmap (fun p -> p.TBp) x
                      TMixed = interpMap pmap (fun p -> p.TMix) x
                      Duty = interpMap pmap (fun p -> p.Duty) x
                      Steam = interpMap pmap (fun p -> p.Steam) x
                      TLinerMax = interpMap pmap (fun p -> p.TLinerMax) x
                      Note = note }
                let angFromX (x: float) = max 0.0 (min 90.0 (angleForFraction x))
                let xPurge =
                    let f (x: float) = snd (velAt x) - bpSpec.MinPurgeVel
                    if f 1e-6 >= 0.0 then 1e-6
                    elif f xTop <= 0.0 then xTop
                    else bisect f 1e-6 xTop 1e-9 60
                let xEros =
                    let f (x: float) =
                        let (rho, vel) = velAt x
                        let q = 0.5 * rho * vel * vel
                        2.0 * max 0.0 (interpMap pmap (fun p -> p.DpTubes) x
                                       - interpMap pmap (fun p -> p.DpBpFric) x
                                       - bpSpec.ExtraK * q) - bpSpec.MaxRhoV2Valve
                    if f 1e-6 <= 0.0 then 1e-6
                    elif f xTop >= 0.0 then xTop
                    else bisect f 1e-6 xTop 1e-9 60
                let (xLiner, kLiner) =
                    let lim = cToK bpSpec.LinerMaterial.TmaxDesign
                    if (List.last pmap).TLinerMax <= lim then (xTop, FromMapEdge)
                    else invertMapWithKind pmap (fun p -> p.TLinerMax) lim
                let (xTMixMin, kTMixMin) = invertMapWithKind pmap (fun p -> p.TMix) bpSpec.TMixMin
                let (xTMixMax, kTMixMax) = invertMapWithKind pmap (fun p -> p.TMix) bpSpec.TMixMax
                let why (k: LimitKind) (text: string) =
                    match k with
                    | FromCrossing -> text
                    | FromMapEdge ->
                        text + sprintf " -- ATTENZIONE: %s: la mappa del by-pass si ferma prima di raggiungere questo limite, quindi l'angolo mostrato e' il bordo del calcolo, non il vincolo. Rilanciare con calculation.bypassMapMode = full." (limitTag k)
                let minDrivers =
                    [ "controllabilita' meccanica", bpSpec.MinOpenDeg,
                      sprintf "sotto %.0f° di apertura il guadagno d(ln zeta)/d(theta) esplode: la farfalla diventa di fatto on-off" bpSpec.MinOpenDeg
                      "T miscelata minima di processo", angFromX xTMixMin,
                      why kTMixMin (sprintf "sotto questo angolo la miscelata scende sotto %.0f °C" (kToC bpSpec.TMixMin))
                      "lavaggio minimo del liner", angFromX xPurge,
                      sprintf "serve almeno %.1f m/s nel liner per non avere un ramo morto (stratificazione, deposito, corrosione sotto deposito)" bpSpec.MinPurgeVel
                      "erosione/rumore in vena contratta", angFromX xEros,
                      sprintf "rho v² nella vena contratta = 2 dp_valvola: chiudendo oltre si supera %.0f Pa" bpSpec.MaxRhoV2Valve ]
                let maxDrivers =
                    [ "autorita' della valvola", bpSpec.MaxOpenDeg,
                      sprintf "oltre %.0f° zeta e' quasi costante: aprendo di piu' non cambia nulla" bpSpec.MaxOpenDeg
                      "T miscelata massima di processo", angFromX xTMixMax,
                      why kTMixMax (sprintf "oltre questo angolo la miscelata supera %.0f °C" (kToC bpSpec.TMixMax))
                      "limite metallurgico del liner", angFromX xLiner,
                      why kLiner (sprintf "%s: %.0f °C" bpSpec.LinerMaterial.Name bpSpec.LinerMaterial.TmaxDesign) ]
                let thMin = minDrivers |> List.map (fun (_, a, _) -> a) |> List.max
                let thMax = maxDrivers |> List.map (fun (_, a, _) -> a) |> List.min
                let thNorm = angFromX xUsed
                let sweepAngles =
                    ([ 5.0; 10.0; 15.0; 20.0; 25.0; 30.0; 35.0; 40.0; 50.0; 60.0; 70.0; 90.0 ]
                     @ [ thMin; thNorm; thMax ])
                    |> List.map (fun a -> Math.Round(a, 2))
                    |> List.distinct
                    |> List.sort
                Some
                    { Normal = { mkPoint thNorm "ESERCIZIO NORMALE (centra la temperatura di uscita richiesta)" with Fraction = xUsed }
                      MinOpen = mkPoint thMin "APERTURA MINIMA ammessa"
                      MaxOpen = mkPoint thMax "APERTURA MASSIMA ammessa"
                      Sweep = sweepAngles |> List.map (fun a -> mkPoint a "")
                      MinDrivers = minDrivers
                      MaxDrivers = maxDrivers
                      Diameter = bpSpec.LinerId
                      AtOutlet = bpSpec.ValveAtOutlet }

        { PMap = pmap
          XUsed = xUsed
          Valve = valveRes
          BypassMapBracketsTarget = bypassMapBracketsTarget }
