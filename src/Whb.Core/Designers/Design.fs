namespace Whb.Core

open System
open Constants
open Types

/// <summary>
/// Coordinates WHB process, thermal, hydraulic, vibration, mechanical, and equipment calculations into a design result.
/// </summary>
/// <remarks>
/// Coordinates process, thermal, hydraulic, vibration, mechanical, and equipment checks into the WHB design result. Correlation choices, assumptions, limits, and warnings should be reviewed together before using the output for engineering decisions.
/// </remarks>
module Design =

    /// <summary>
    /// Represents runtime settings that influence numerical strategy without changing case geometry or process data.
    /// </summary>
    /// <remarks>
    /// These settings are intended for precision/performance trade-offs such as bypass-map resolution and repeated gas-property evaluation.
    /// </remarks>
    type RunSettings =
        { BypassMapMode: string
          BypassTargetToleranceK: float
          GasPropertyCache: bool
          CorrelationValidityWarnings: bool
          /// Maximum number of bypass-map points evaluated concurrently. Each point is an
          /// independent solve of the same immutable case, so this changes run time only,
          /// never results. Use 1 to force a strictly sequential run.
          Parallelism: int }

    /// <summary>
    /// Provides conservative runtime settings for API callers that do not pass project options.
    /// </summary>
    /// <remarks>
    /// The adaptive bypass map keeps normal runs responsive while refining around the target when needed.
    /// </remarks>
    let defaultRunSettings =
        { BypassMapMode = "adaptive"
          BypassTargetToleranceK = 0.5
          GasPropertyCache = true
          CorrelationValidityWarnings = true
          Parallelism = max 1 Environment.ProcessorCount }
    /// <summary>
    /// Process-wide budget of concurrent solves.
    /// </summary>
    /// <remarks>
    /// `Parallelism` is a per-design setting, so nesting a parallel caller above a design run
    /// would multiply rather than share the machine. The budget is a single gate for the whole
    /// process: whoever wants a worker takes a slot from it, so total concurrency stays bounded
    /// no matter how many levels start running at once.
    /// </remarks>
    module ParallelBudget =
        let private gate =
            new Threading.SemaphoreSlim(max 1 Environment.ProcessorCount, max 1 Environment.ProcessorCount)
        let acquire () = gate.Wait()
        let release () = gate.Release() |> ignore
        let available () = gate.CurrentCount
    let private w fmt = Printf.kprintf id fmt
    let private sev = function Critical -> "CRITICO" | Warning -> "ATTENZIONE" | Note -> "NOTA"
    let private maxRelativeDelta (a: float[]) (b: float[]) =
        let n = min a.Length b.Length
        let mutable d = 0.0
        for i in 0 .. n - 1 do
            let den = max 1e-9 (max (abs a.[i]) (abs b.[i]))
            d <- max d (abs (a.[i] - b.[i]) / den)
        d
    let buildFindings (correlationValidityWarnings: bool) (case: DesignCase) (sat: Steam.SatProps) (cells: CellResult list)
                      (axial: AxialResult list) (circ: CirculationResult)
                      (ft: FixedTubesheetResult) (risers: RiserCheck list)
                      (expansions: ExpansionResult list) (bp: Bypass.Result option) (dpGas: float)
                      (stress: StressResult) (valve: ValveResult option)
                      (notConnected: Piping.Line list)
                      (vibration: Vibration.Result list)
                      (conv: ConvergenceReport)
                      (sulphur: Sulphur.CouplingSummary option)
                      (sulphurCondenser: SulphurCondenser.Result option) =
        Findings.build correlationValidityWarnings case sat cells axial circ ft risers expansions bp dpGas
            stress valve notConnected vibration conv sulphur sulphurCondenser
    let runWithSettingsAndProgress (settings: RunSettings) (reportProgress: string -> unit) (caseIn: DesignCase) : DesignResult =
        let phase text = reportProgress text
        phase "Preparing connected risers, downcomers, steam properties, and tube bands"
        let allRisers = caseIn.Loop.Risers
        let allDowncomers = caseIn.Loop.Downcomers
        let case =
            { caseIn with
                Loop =
                    { caseIn.Loop with
                        Risers = allRisers |> List.filter (fun l -> l.Connected)
                        Downcomers = allDowncomers |> List.filter (fun l -> l.Connected) } }
        let notConnected =
            (allRisers @ allDowncomers) |> List.filter (fun l -> not l.Connected)
        let sat = Steam.sat case.Water.DrumPressure
        let t = case.Tube
        let bands =
            Bundle.build t.ShellId t.Otl t.Itl t.Pitch t.Do t.NTubes case.NY
                (if case.Bypass.Enabled then case.Bypass.PipeOd else 0.0)
        let nz = max 6 case.NZ
        let comp0 = GasProps.normalize case.Gas.Composition
        let processActive =
            Sulphur.hasElementalSulphur comp0
            || (case.Gas.ClausMode <> Claus.Frozen && Claus.hasReactiveSpecies comp0)
        let processStateAt pPa comp tK =
            if processActive then
                Sulphur.processStateAt case.Gas.ShiftMode case.Gas.RealGas pPa comp tK
            else
                let compGas = Shift.equilibrate case.Gas.ShiftMode comp tK
                let st : Sulphur.ProcessState =
                    { T = tK
                      VapourComposition = compGas
                      TotalSpecificEnthalpy = GasProps.enthalpyAbsReal case.Gas.RealGas compGas tK pPa
                      CpApprox = (GasProps.mixReal GasProps.Wilke case.Gas.RealGas compGas tK pPa case.Gas.Z).Cp
                      PSulphur = 0.0
                      YElementalSulphurVapour = 0.0
                      SulphurDewPoint = None
                      Condensing = false
                      CondensedAtoms = 0.0
                      CondensedFraction = 0.0 }
                st
        let processStateFromEnthalpyAt pPa comp h =
            if processActive then
                Sulphur.processStateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pPa comp h
            else
                let (t0, compGas) = Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pPa comp h
                let st : Sulphur.ProcessState =
                    { T = t0
                      VapourComposition = compGas
                      TotalSpecificEnthalpy = h
                      CpApprox = (GasProps.mixReal GasProps.Wilke case.Gas.RealGas compGas t0 pPa case.Gas.Z).Cp
                      PSulphur = 0.0
                      YElementalSulphurVapour = 0.0
                      SulphurDewPoint = None
                      Condensing = false
                      CondensedAtoms = 0.0
                      CondensedFraction = 0.0 }
                st
        let processEnthalpyAt pPa comp tK =
            if processActive then
                Sulphur.processEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas pPa comp tK
            else
                GasProps.enthalpyAbsReal case.Gas.RealGas comp tK pPa
        let mixCompositionsByMass (m1: float) (c1: GasProps.Composition) (m2: float) (c2: GasProps.Composition) =
            let c1n = GasProps.normalize c1
            let c2n = GasProps.normalize c2
            let n1 = if m1 > 0.0 then m1 / GasProps.mixMolarMass c1n else 0.0
            let n2 = if m2 > 0.0 then m2 / GasProps.mixMolarMass c2n else 0.0
            [ yield! c1n |> List.map (fun (sp, y) -> sp, n1 * y)
              yield! c2n |> List.map (fun (sp, y) -> sp, n2 * y) ]
            |> List.groupBy fst
            |> List.map (fun (sp, items) -> sp, items |> List.sumBy snd)
            |> GasProps.normalize
        let wGasTot = case.Gas.MassFlow
        let gasCache =
            System.Collections.Concurrent.ConcurrentDictionary<struct (string * bool * string * float * float * float), GasProps.MixProps>()
        let compKey (comp: GasProps.Composition) =
            comp
            |> GasProps.normalize
            |> List.map (fun (sp, y) -> sprintf "%s=%.6f" (GasProps.speciesName sp) y)
            |> String.concat ";"
        let mixRealCached rule real comp tK pPa z =
            if not settings.GasPropertyCache then GasProps.mixReal rule real comp tK pPa z
            else
                let key =
                    struct (GasProps.mixingRuleName rule, real, compKey comp,
                            Math.Round(tK, 1), Math.Round(pPa, 0), Math.Round(z, 4))
                match gasCache.TryGetValue key with
                | true, value -> value
                | _ ->
                    let value = GasProps.mixReal rule real comp tK pPa z
                    gasCache.[key] <- value
                    value
        let dutyGuess = wGasTot * 2.2e3 * (case.Gas.TIn - sat.Tsat - 30.0)
        let steamGuess = dutyGuess / sat.Hfg

        let coupled (cx: DesignCase) =
            let mutable wField = Array.create nz (15.0 * steamGuess / t.Length)
            let mutable xIn = Array.create nz 0.0
            let mutable o = BundleSolver.solve cx bands wField xIn
            let mutable d = Circulation.solve cx sat bands o.BandDuty o.SteamLin o.Dz
            let mutable iter = 1
            let mutable converged = false
            let mutable residual = infinity
            while iter <= 5 && not converged do
                let wPrev = wField
                let xPrev = xIn
                wField <- d.WFieldLin
                xIn <- d.XInField
                o <- BundleSolver.solve cx bands wField xIn
                d <- Circulation.solve cx sat bands o.BandDuty o.SteamLin o.Dz
                let dw = maxRelativeDelta wPrev wField
                let dx = maxRelativeDelta xPrev xIn
                residual <- max dw dx
                converged <- dw < 1e-5 && dx < 1e-5
                iter <- iter + 1
            // The loop can exit either converged or capped, and the two must not look alike
            // downstream: the caller records which one happened.
            (o, d, iter - 1, converged, residual)

        let caseWith (x: float) =
            { case with Gas = { case.Gas with MassFlow = wGasTot * (1.0 - x) } }

        let tubeOutOf (o: BundleSolver.SolveOutput) =
            let ntb = o.NTubesBand
            let cls = o.Classes |> List.toArray
            let mutable wq = 0.0
            let mutable ta = 0.0
            for j in 0 .. ntb.Length - 1 do
                for c in 0 .. cls.Length - 1 do
                    let wgt = ntb.[j] * fst cls.[c]
                    wq <- wq + wgt
                    ta <- ta + wgt * o.TGasOutBandClass.[j, c]
            ta / wq
        let tubeOutletCompositionOf (o: BundleSolver.SolveOutput) =
            let ntb = o.NTubesBand
            let cls = o.Classes |> List.toArray
            [ for j in 0 .. ntb.Length - 1 do
                for c in 0 .. cls.Length - 1 do
                    let wgt = ntb.[j] * fst cls.[c]
                    for (sp, y) in o.OutletCompositionBandClass.[j, c] do
                        yield sp, wgt * y ]
            |> List.groupBy fst
            |> List.map (fun (sp, items) -> sp, items |> List.sumBy snd)
            |> GasProps.normalize

        // Convergence data travels back with the result rather than through shared state,
        // because map points are evaluated concurrently.
        let evaluate (x: float) =
            let (o, d, iters, conv, residual) = coupled (caseWith x)
            let health = struct (iters, conv, residual)
            let tTubes = tubeOutOf o
            let compTubes = tubeOutletCompositionOf o
            if not case.Bypass.Enabled || x <= 1e-6 then
                (tTubes, o, d, None, health)
            else
                let (nodes, tBp, compBp, qBp, dpBp, bpConverged) =
                    Bypass.march case.Bypass comp0 case.Gas.PIn 0.0 case.Gas.TIn
                        case.Gas.MixingRule case.Gas.RealGas case.Gas.ShiftMode
                        case.Gas.ClausMode case.Gas.ClausKinetics sat (wGasTot * x) o.ZC o.Dz
                let compMix =
                    mixCompositionsByMass (wGasTot * x) compBp (wGasTot * (1.0 - x)) compTubes
                let hMix =
                    x * processEnthalpyAt case.Gas.PIn compBp tBp
                    + (1.0 - x) * processEnthalpyAt case.Gas.PIn compTubes tTubes
                let tMix =
                    (processStateFromEnthalpyAt case.Gas.PIn compMix hMix).T
                let res : Bypass.Result =
                    { Fraction = x
                      MassFlow = wGasTot * x
                      TOutBypass = tBp
                      TOutTubes = tTubes
                      TOutMixed = tMix
                      OutletComposition = compBp
                      HeatLoss = qBp
                      SteamFromBypass = qBp / sat.Hfg
                      Nodes = nodes
                      TLinerMax = nodes |> List.map (fun n -> n.TLinerIn) |> List.max
                      TPipeMax = nodes |> List.map (fun n -> n.TPipeIn) |> List.max
                      DpBypass = dpBp
                      Converged = bpConverged }
                (tMix, o, d, Some res, health)

        let bpSpec = case.Bypass
        let aLiner = Math.PI * bpSpec.LinerId * bpSpec.LinerId / 4.0
        let mapPoint (x: float) : DesignBypass.MapPoint =
            let (tm, o, d, bp, _) = evaluate x
            let (tBp, tLin, rhoV, tV) =
                match bp with
                | Some b ->
                    let n = if bpSpec.ValveAtOutlet then List.last b.Nodes else List.head b.Nodes
                    let compValve =
                        (processStateAt case.Gas.PIn b.OutletComposition n.TGas).VapourComposition
                    let pr = mixRealCached case.Gas.MixingRule case.Gas.RealGas compValve n.TGas case.Gas.PIn 1.0
                    (b.TOutBypass, b.TLinerMax, pr.Rho, n.TGas)
                | None ->
                    let compValve =
                        (processStateAt case.Gas.PIn comp0 sat.Tsat).VapourComposition
                    let pr = mixRealCached case.Gas.MixingRule case.Gas.RealGas compValve sat.Tsat case.Gas.PIn 1.0
                    (sat.Tsat, sat.Tsat, pr.Rho, sat.Tsat)
            { X = x
              TMix = tm
              TTubes = (match bp with Some b -> b.TOutTubes | None -> tm)
              TBp = tBp
              DpTubes = o.DpGas
              DpBpFric = (match bp with Some b -> b.DpBypass | None -> 0.0)
              Duty = o.Duty + (match bp with Some b -> b.HeatLoss | None -> 0.0)
              Steam = o.Steam + (match bp with Some b -> b.SteamFromBypass | None -> 0.0)
              TLinerMax = tLin
              RhoValve = rhoV
              TValve = tV }
        let bypass =
            DesignBypass.run
                { Case = case
                  Mode = settings.BypassMapMode
                  TargetToleranceK = settings.BypassTargetToleranceK
                  Parallelism = settings.Parallelism
                  TotalGasFlow = wGasTot
                  MixtureMolarMass = GasProps.mixMolarMass comp0
                  LinerArea = aLiner
                  Phase = phase
                  AcquireWorker = ParallelBudget.acquire
                  ReleaseWorker = ParallelBudget.release
                  MapPointAt = mapPoint }
        let pmap = bypass.PMap
        let xUsed = bypass.XUsed

        phase "Solving final coupled thermal and natural-circulation case"
        let (tMixed, out, dist, bpRes, struct (coupledIters, coupledOk, coupledResidual)) =
            evaluate xUsed
        let bypassMapBracketsTarget = bypass.BypassMapBracketsTarget
        // Two different steam figures, both legitimate, and the report has to name which is
        // which. `out.Steam` is the evaporation rate inside the bundle, computed from the duty
        // and the latent heat with the entering water already at saturation - the basis the
        // reference datasheet uses. The net figure is what actually leaves the drum once the
        // feedwater has been heated from TFeed up to saturation.
        let totalDuty = out.Duty + (match bpRes with Some b -> b.HeatLoss | None -> 0.0)
        let hFeed = Steam.hLiquid case.Water.DrumPressure (min case.Water.TFeed (sat.Tsat - 0.01))
        let feedSubcooling = max 0.0 (sat.Tsat - case.Water.TFeed)
        let steamNet =
            let rise = sat.HV - hFeed
            if rise > 1.0 then totalDuty / rise else nan
        // Downcomer inlet flashing margin. Water leaves the drum essentially saturated; the
        // entry loss and the velocity head drop the local pressure BEFORE the column has any
        // height to recover it. If the subcooling from mixing with the feedwater is not enough
        // to cover that drop, bubbles form exactly where only liquid is wanted, and the
        // driving head that the design counts on is not there.
        let dcSubcooling =
            let wCirc = max 1e-9 dist.Global.CircFlow
            let dh = (sat.HL - hFeed) * min 1.0 (steamNet / wCirc)
            if Double.IsNaN dh then 0.0 else max 0.0 (dh / max 1.0 sat.CpL)
        let dcSubcoolingRequired =
            // Clausius-Clapeyron gives the saturation pressure slope, which converts the local
            // pressure dip into the temperature margin it demands.
            let dpdT = sat.Hfg * sat.RhoV * sat.RhoL / (sat.Tsat * max 1e-9 (sat.RhoL - sat.RhoV))
            let v = dist.Global.VelDowncomer
            let kEntry = 0.5 + max 0.0 case.Loop.Drum.DowncomerVortexBreakerK
            let dpLocal = kEntry * sat.RhoL * v * v / 2.0
            if dpdT <= 0.0 then 0.0 else dpLocal / dpdT
        let convergence =
            { CoupledIterations = coupledIters
              CoupledConverged = coupledOk
              CoupledResidual = coupledResidual
              QualityClampedCells = out.QualityClamped
              QualityClampFirstZ = out.QualityClampFirstZ
              NonConvergedCells = out.NonConvergedCells
              CirculationRoots = dist.RootCount
              CirculationBracketOk = dist.BracketOk
              CirculationSlope = dist.BalanceSlope
              DowncomerSubcooling = dcSubcooling
              DowncomerSubcoolingRequired = dcSubcoolingRequired
              BypassMapBracketsTarget = bypassMapBracketsTarget }
        let circ = dist.Global
        let axial0 =
            List.mapi (fun i (a: AxialResult) ->
                { a with
                    WFieldLin = dist.WFieldLin.[i]
                    WBypassLin = dist.WBypLin.[i] }) out.Axial
        let nozzles = Nozzles.design case sat axial0 circ
        let ny = List.length bands
        let ncls = List.length out.Classes
        let cells =
            [ for i in 0 .. nz - 1 do
                for j in 0 .. ny - 1 do
                    for c in 0 .. ncls - 1 -> out.Cells.[i, j, c] ]
        let dcPos = case.Loop.Downcomers |> List.map (fun l -> l.ZNozzle) |> List.sort
        let rsPos = case.Loop.Risers |> List.map (fun l -> l.ZNozzle) |> List.sort
        let axial = Circulation.axialVelocities case sat axial0 dist.WExtLin out.Dz dcPos rsPos

        let nT = float t.NTubes
        let areaOut = Math.PI * t.Do * t.Length * nT
        let areaIn = Math.PI * t.Di * t.Length * nT
        let ntb = out.NTubesBand
        let clsArr = out.Classes |> List.toArray
        let mutable tMin = infinity
        let mutable tMax = -infinity
        for j in 0 .. ny - 1 do
            for c in 0 .. ncls - 1 do
                let tv = out.TGasOutBandClass.[j, c]
                tMin <- min tMin tv
                tMax <- max tMax tv
        let tOutMean = tMixed
        let tubeOutletComposition = tubeOutletCompositionOf out
        let mixedOutletComposition =
            match bpRes with
            | Some b ->
                mixCompositionsByMass b.MassFlow b.OutletComposition (wGasTot - b.MassFlow) tubeOutletComposition
            | None -> tubeOutletComposition
        let sulphurCondenserResult =
            if case.SulphurCondenser.Enabled then
                let feedUsed =
                    if case.SulphurCondenser.UseWhbOutlet then
                        { case.SulphurCondenser.Feed with
                            Composition = mixedOutletComposition
                            MassFlow = wGasTot
                            TIn = tOutMean
                            PIn = max 1.0e4 (case.Gas.PIn - out.DpGas)
                            Z = case.Gas.Z
                            ShiftMode = case.Gas.ShiftMode
                            ClausMode = case.Gas.ClausMode
                            ClausKinetics = case.Gas.ClausKinetics
                            MixingRule = case.Gas.MixingRule
                            RealGas = case.Gas.RealGas }
                    else case.SulphurCondenser.Feed
                Some(
                    SulphurCondenser.solveWithFeed
                        (if case.SulphurCondenser.UseWhbOutlet then "WHB mixed outlet" else "Dedicated case feed")
                        case.SulphurCondenser
                        feedUsed)
            else None
        let dt1 = case.Gas.TIn - sat.Tsat
        let dt2 = tOutMean - sat.Tsat
        let lm = lmtd dt1 dt2
        let tRoom = case.AssemblyTemperature
        let segsFor (ci: int) (j: int) =
            [ for i in 0 .. nz - 1 -> (out.Dz.[i], out.Cells.[i, j, ci].TMetalWallAvg) ]
        phase "Calculating expansion, tubesheet loads, and mechanical stresses"
        let allExp =
            [ for ci in 0 .. ncls - 1 do
                for j in 0 .. ny - 1 ->
                    let e = Mechanics.axialExpansion case.Material tRoom (segsFor ci j)
                    (ci, j, e) ]
        let perClass =
            out.Classes
            |> List.mapi (fun ci (_, fl) ->
                let (_, jj, e) = allExp |> List.filter (fun (c2, _, _) -> c2 = ci) |> List.maxBy (fun (_, _, e) -> e.DeltaL)
                { e with Label = sprintf "Tubi - ferrula %.0f mm, banda %d (dL max)" (fl * 1000.0) jj })
        let coldest =
            let (cc2, jj, e) = allExp |> List.minBy (fun (_, _, e) -> e.DeltaL)
            { e with Label = sprintf "Tubi - banda %d, ferrula %.0f mm (dL MINIMO)" jj (snd (List.item cc2 out.Classes) * 1000.0) }
        let meanTube =
            let segs =
                [ for i in 0 .. nz - 1 ->
                    let num =
                        [ for j in 0 .. ny - 1 do
                            for c in 0 .. ncls - 1 ->
                              out.Cells.[i, j, c].TMetalWallAvg * out.Cells.[i, j, c].NTubes ] |> List.sum
                    let den =
                        [ for j in 0 .. ny - 1 do
                            for c in 0 .. ncls - 1 -> out.Cells.[i, j, c].NTubes ] |> List.sum
                    (out.Dz.[i], num / den) ]
            let e = Mechanics.axialExpansion case.Material tRoom segs
            { e with Label = sprintf "Tubi - MEDIA PESATA su tutti i %d tubi" t.NTubes }
        let (tShellMetal, qLoss) =
            Mechanics.shellMetalTemperature sat (cToK 25.0) case.ShellInsulationU
                case.ShellThickness (case.ShellMaterial.K (kToC sat.Tsat)) 20000.0
        let eShell =
            let e = Mechanics.axialExpansion case.ShellMaterial tRoom [ (t.Length, tShellMetal) ]
            { e with Label = "MANTELLO (metallo a contatto con acqua satura)" }
        let dHot = (perClass @ [ coldest ]) |> List.map (fun e -> e.DeltaL) |> List.max
        let dCold = (perClass @ [ coldest ]) |> List.map (fun e -> e.DeltaL) |> List.min
        let expansions =
            perClass @ [ coldest; meanTube; eShell ]
            @ [ { Label = "DIFFERENZIALE tubo medio - mantello"
                  TEquivalent = nan; AlphaMean = nan; Length = t.Length
                  DeltaL = meanTube.DeltaL - eShell.DeltaL }
                { Label = "DIFFERENZIALE tubo piu' caldo - mantello"
                  TEquivalent = nan; AlphaMean = nan; Length = t.Length
                  DeltaL = dHot - eShell.DeltaL }
                { Label = "DIFFERENZIALE fra tubi (piu' caldo - piu' freddo)"
                  TEquivalent = nan; AlphaMean = nan; Length = t.Length
                  DeltaL = dHot - dCold } ]
        let ftRes =
            Mechanics.fixedTubesheet case.Material case.ShellMaterial tRoom t.Length t.NTubes
                t.Do t.Di t.ShellId case.ShellThickness case.UnsupportedSpan
                meanTube.TEquivalent (perClass |> List.maxBy (fun e -> e.DeltaL)).TEquivalent eShell.TEquivalent
        let valveRes = bypass.Valve

        let pShell = case.Water.DrumPressure
        let pGasMean =
            let s = cells |> List.sumBy (fun c -> c.PGas * c.NTubes)
            let n = cells |> List.sumBy (fun c -> c.NTubes)
            s / n
        let aTubeMetal1 = Math.PI / 4.0 * (t.Do * t.Do - t.Di * t.Di)
        let aFluidTube =
            float t.NTubes * Math.PI / 4.0 * t.Di * t.Di
            + (if case.Bypass.Enabled then Math.PI / 4.0 * bpSpec.InsulOd * bpSpec.InsulOd else 0.0)
        let aFluidShell =
            Math.PI / 4.0 * t.ShellId * t.ShellId
            - float t.NTubes * Math.PI / 4.0 * t.Do * t.Do
            - (if case.Bypass.Enabled then Math.PI / 4.0 * bpSpec.PipeOd * bpSpec.PipeOd else 0.0)
        let pEnd = pShell * aFluidShell + pGasMean * aFluidTube
        let bpPipeExp =
            bpRes
            |> Option.map (fun b ->
                let segs =
                    b.Nodes |> List.mapi (fun i n -> (out.Dz.[i], 0.5 * (n.TPipeIn + n.TPipeOut)))
                Mechanics.axialExpansion bpSpec.PipeMaterial tRoom segs)
        let bpLinerExp =
            bpRes
            |> Option.map (fun b ->
                let segs =
                    b.Nodes |> List.mapi (fun i n -> (out.Dz.[i], 0.5 * (n.TLinerIn + n.TLinerOut)))
                Mechanics.axialExpansion bpSpec.LinerMaterial tRoom segs)
        let memberSpecs =
            [ for (ci, j, e) in allExp ->
                let n = out.Cells.[0, j, ci].NTubes
                (sprintf "Tubi banda %d (y = %+.2f m), ferrula %.0f mm" j (List.item j bands).Y
                     (snd (List.item ci out.Classes) * 1000.0),
                 case.Material, n, n * aTubeMetal1, e.TEquivalent, e.DeltaL) ]
            @ [ ("MANTELLO", case.ShellMaterial, 1.0,
                 Math.PI * (t.ShellId + case.ShellThickness) * case.ShellThickness,
                 eShell.TEquivalent, eShell.DeltaL) ]
            @ (match bpPipeExp with
               | Some e ->
                   [ ("BY-PASS - tubo di contenimento", bpSpec.PipeMaterial, 1.0,
                      Math.PI / 4.0 * (bpSpec.PipeOd * bpSpec.PipeOd - bpSpec.InsulOd * bpSpec.InsulOd),
                      e.TEquivalent, e.DeltaL) ]
               | None -> [])
        let (commonDelta, members) =
            Mechanics.restrainedSystem tRoom t.Length pEnd memberSpecs
        let sigmaZof =
            members
            |> List.mapi (fun k m -> (k, m.SigmaZ))
            |> dict
        let nGroups = List.length allExp
        let sigmaTubeGroup = Array2D.create ncls ny 0.0
        allExp
        |> List.iteri (fun k (ci, j, _) -> sigmaTubeGroup.[ci, j] <- sigmaZof.[k])
        let sigmaShellZ = (List.item nGroups members).SigmaZ
        let sigmaBpZ =
            if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZ else 0.0
        let stressTubes =
            [ for ci in 0 .. ncls - 1 do
                for j in 0 .. ny - 1 do
                    for i in 0 .. nz - 1 ->
                        let c = out.Cells.[i, j, ci]
                        let tAvg = kToC c.TMetalWallAvg
                        let dT = c.TMetalIn - c.TMetalOut
                        let pts =
                            Mechanics.stressPoints c.PGas pShell (t.Di / 2.0) (t.Do / 2.0)
                                sigmaTubeGroup.[ci, j] (case.Material.Alpha tAvg)
                                (case.Material.E tAvg) dT
                        let worst = pts |> List.maxBy (fun p -> p.SigmaVM)
                        let sy = case.Material.Sy tAvg
                        { Component = "TUBI"
                          I = i; J = j; C = ci
                          Z = c.Z; Y = c.Y
                          TMetalIn = c.TMetalIn; TMetalOut = c.TMetalOut
                          TMetalAvg = c.TMetalWallAvg; DTWall = dT
                          PInt = c.PGas; PExt = pShell
                          SigmaZMembrane = sigmaTubeGroup.[ci, j]
                          SigmaZThermal = (List.item (ci * ny + j) members).SigmaZThermal
                          SigmaZPressure = (List.item (ci * ny + j) members).SigmaZPressure
                          Points = pts
                          SigmaVMMax = worst.SigmaVM
                          WorstAt = worst.Position
                          Sy = sy
                          Utilisation = worst.SigmaVM / sy } ]
        let stressBypass =
            match bpRes with
            | None -> []
            | Some b ->
                b.Nodes
                |> List.mapi (fun i n ->
                    let tAvg = kToC (0.5 * (n.TPipeIn + n.TPipeOut))
                    let dT = n.TPipeIn - n.TPipeOut
                    let pInt = case.Gas.PIn
                    let pts =
                        Mechanics.stressPoints pInt pShell (bpSpec.InsulOd / 2.0) (bpSpec.PipeOd / 2.0)
                            sigmaBpZ (bpSpec.PipeMaterial.Alpha tAvg) (bpSpec.PipeMaterial.E tAvg) dT
                    let worst = pts |> List.maxBy (fun p -> p.SigmaVM)
                    let sy = bpSpec.PipeMaterial.Sy tAvg
                    { Component = "BY-PASS"
                      I = i; J = -1; C = -1
                      Z = n.Z; Y = 0.0
                      TMetalIn = n.TPipeIn; TMetalOut = n.TPipeOut
                      TMetalAvg = cToK tAvg; DTWall = dT
                      PInt = pInt; PExt = pShell
                      SigmaZMembrane = sigmaBpZ
                      SigmaZThermal = (if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZThermal else 0.0)
                      SigmaZPressure = (if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZPressure else 0.0)
                      Points = pts
                      SigmaVMMax = worst.SigmaVM
                      WorstAt = worst.Position
                      Sy = sy
                      Utilisation = worst.SigmaVM / sy })
        let pExtNetTube = pShell - pGasMean
        let mTubeWorstOp = [ 0 .. nGroups - 1 ] |> List.minBy (fun k -> (List.item k members).SigmaZ) |> fun k -> List.item k members
        let mTubeWorstTh = [ 0 .. nGroups - 1 ] |> List.minBy (fun k -> (List.item k members).SigmaZThermal) |> fun k -> List.item k members
        let bucklings =
            [ Mechanics.bucklingCheck
                  (sprintf "TUBI - LC1 esercizio (termico + pressione): %s" mTubeWorstOp.Label)
                  case.Material mTubeWorstOp.TEq t.Do t.Di case.UnsupportedSpan
                  mTubeWorstOp.SigmaZ pExtNetTube
              Mechanics.bucklingCheck
                  (sprintf "TUBI - LC2 caldo NON in pressione: %s" mTubeWorstTh.Label)
                  case.Material mTubeWorstTh.TEq t.Do t.Di case.UnsupportedSpan
                  mTubeWorstTh.SigmaZThermal 0.0 ]
            @ (match bpPipeExp with
               | Some e ->
                   let mb = List.item (nGroups + 1) members
                   [ Mechanics.bucklingCheck "BY-PASS tubo di contenimento - LC1 esercizio"
                         bpSpec.PipeMaterial e.TEquivalent bpSpec.PipeOd bpSpec.InsulOd
                         case.UnsupportedSpan mb.SigmaZ (pShell - case.Gas.PIn)
                     Mechanics.bucklingCheck "BY-PASS tubo di contenimento - LC2 caldo non in pressione"
                         bpSpec.PipeMaterial e.TEquivalent bpSpec.PipeOd bpSpec.InsulOd
                         case.UnsupportedSpan mb.SigmaZThermal 0.0 ]
               | None -> [])
        let stressRes =
            { CommonDelta = commonDelta
              PressureEndLoad = pEnd
              AreaFluidShell = aFluidShell
              AreaFluidTube = aFluidTube
              PShell = pShell
              PTubeMean = pGasMean
              Members = members
              Cells = stressTubes @ stressBypass
              Bucklings = bucklings
              LinerRestrainedForce =
                (match bpLinerExp with
                 | Some e ->
                     let a = Math.PI / 4.0 * (bpSpec.LinerOd * bpSpec.LinerOd - bpSpec.LinerId * bpSpec.LinerId)
                     a * bpSpec.LinerMaterial.E (kToC e.TEquivalent) * e.DeltaL / t.Length
                 | None -> 0.0)
              LinerTEq = (match bpLinerExp with Some e -> e.TEquivalent | None -> nan)
              LinerFreeElongation = (match bpLinerExp with Some e -> e.DeltaL | None -> nan)
              Liner =
                (let tEq = match bpLinerExp with Some e -> e.TEquivalent | None -> cToK 700.0
                 let tC = kToC tEq
                 let ee = bpSpec.LinerMaterial.E tC
                 let sy = bpSpec.LinerMaterial.Sy tC
                 let od = bpSpec.LinerOd
                 let idl = bpSpec.LinerId
                 let thk = 0.5 * (od - idl)
                 let dm = 0.5 * (od + idl)
                 let factor = 2.0
                 let dpDes = factor * out.DpGas
                 let pE = 2.0 * ee / (1.0 - Mechanics.nu * Mechanics.nu) * (thk / dm) ** 3.0
                 let pY = 2.0 * sy * thk / od
                 let pC = 1.0 / sqrt (1.0 / (pE * pE) + 1.0 / (pY * pY))
                 { DpTubes = out.DpGas; DpDesign = dpDes; Factor = factor
                   Od = od; Id = idl; Thickness = thk; TEq = tEq; E = ee; Sy = sy
                   PCrElastic = pE; PCrYield = pY; PCollapse = pC
                   Utilisation = dpDes / pC
                   UtilisationCode = 3.0 * dpDes / pC
                   HoopStress = dpDes * dm / (2.0 * thk)
                   Notes =
                     [ "Il liner e' LIBERO di dilatare e NON e' un componente in pressione: separa due volumi di gas che stanno quasi alla stessa pressione."
                       "Il salto lo genera solo la perdita di carico del fascio, perche' l'intercapedine esterna e' in comunicazione con il lato a valle dei tubi."
                       "Il verso puo' invertirsi secondo la posizione della valvola e i transitori, quindi si verifica il caso PIU' severo, cioe' la pressione esterna."
                       "Cilindro lungo non irrigidito: la carta refrattaria riempie l'intercapedine ma e' cedevole e non viene conteggiata come supporto." ] })
              Notes =
                [ "Trazione positiva in tutte le tensioni."
                  "Le tensioni di Lame' sono PRIMARIE (equilibrio con la pressione); quelle da gradiente termico radiale e da dilatazione impedita sono SECONDARIE (autoequilibrate)."
                  "Verifica di screening: il calcolo di codice (ASME VIII-1 UHX-13 / TEMA RCB-7) aggiunge la flessibilita' della piastra tubiera e i limiti differenziati Pm / Pm+Pb / Pm+Pb+Q."
                  "Il LINER del by-pass e' libero di dilatare (dato costruttivo confermato): non sviluppa carico assiale e non figura fra i membri del sistema a piastre fisse. La riga di instabilita' che lo riguarda e' una verifica IPOTETICA, riportata solo per documentare l'ordine di grandezza della forza che il giunto scorrevole evita."
                  "Pressione esterna: i diaframmi di supporto lavorano come anelli di irrigidimento. Il gioco foro/tubo e' di 0.40 mm sul diametro (0.20 mm radiali), cioe' lo 0.5 % del raggio: il vincolo radiale e' effettivo e l'ipotesi e' confermata."
                  "Il carico di estremita' di pressione presuppone l'apparecchio CHIUSO alle due estremita' e privo di giunto di dilatazione sul mantello." ] }

        phase "Calculating CHF, fouling, and correlation sensitivity cases"
        let propsAt (c: CellResult) =
            mixRealCached case.Gas.MixingRule case.Gas.RealGas comp0 c.TGas c.PGas case.Gas.Z
        let mixRulePropsAt rule (c: CellResult) =
            mixRealCached rule case.Gas.RealGas comp0 c.TGas c.PGas case.Gas.Z
        let thermalPost =
            DesignThermalPost.run
                { Case = case
                  Sat = sat
                  Tube = t
                  AreaOut = areaOut
                  Comp0 = comp0
                  Cells = cells
                  GasPropsAt = propsAt
                  MixRulePropsAt = mixRulePropsAt }
        let hotCells = thermalPost.HotCells
        let cellDnb = thermalPost.CellDnb
        let cellQmax = thermalPost.CellQmax
        let chfModels = thermalPost.ChfModels
        let sensitivity = thermalPost.Sensitivity
        let foulingCases = thermalPost.FoulingCases

        phase "Running vibration screening over tube bands and support spans"
        let vibration =
            let gasDensityAt (c: CellResult) =
                (mixRealCached case.Gas.MixingRule case.Gas.RealGas comp0 c.TGas c.PGas case.Gas.Z).Rho
            DesignVibration.run case sat t ny cells gasDensityAt
        phase "Running maldistribution sensitivity campaign"
        let maldist =
            let jb = cellDnb.J
            let stateFromEnthalpyAt p h = processStateFromEnthalpyAt p comp0 h
            let gasPropsAt comp tG p =
                mixRealCached case.Gas.MixingRule case.Gas.RealGas comp tG p case.Gas.Z
            DesignSensitivity.run
                { Case = case
                  Sat = sat
                  Tube = t
                  TotalGasFlow = wGasTot
                  BypassFractionUsed = xUsed
                  BaseCells = Array.init nz (fun i -> out.Cells.[i, jb, 0])
                  DZ = out.Dz
                  InletEnthalpy = processEnthalpyAt case.Gas.PIn comp0 case.Gas.TIn
                  StateFromEnthalpyAt = stateFromEnthalpyAt
                  GasPropsAt = gasPropsAt }

        phase "Calculating transient dry-out screening and water inventory basis"
        let transient =
            let duty = out.Duty + (match bpRes with Some b -> b.HeatLoss | None -> 0.0)
            DesignTransient.run case sat t bpSpec.PipeOd cells duty

        phase "Checking risers, downcomers, findings, and final result tables"
        let riserFlows = Circulation.lineFlows case sat case.Loop.Risers true circ.XOutRiser circ.CircFlow
        let dcFlows = Circulation.lineFlows case sat case.Loop.Downcomers false 0.0 circ.CircFlow
        let riserChecks =
            Mechanics.checkRisers sat riserFlows circ.XOutRiser case.MaxRhoV2Riser case.Loop.VoidModel
        let mkCheck (twoPhase: bool) (x: float) ((ln: Piping.Line), (w: float)) =
            let rho = if twoPhase then TwoPhase.homogeneousDensity x sat else sat.RhoL
            let v = w / (rho * Piping.area ln)
            let re = max 100.0 (rho * v * ln.Id / sat.MuL)
            let f = GasSide.darcyFriction re (4.5e-5 / ln.Id)
            { Tag = ln.Tag; Nps = ln.Nps; Id = ln.Id; Count = ln.Count
              ZNozzle = ln.ZNozzle; AngleDeg = ln.AngleDeg
              DevelopedLength = Piping.developedLength ln
              NElbows = Piping.elbowCount ln
              KTotal = Piping.totalK f ln
              Flow = w; Velocity = v; RhoV2 = rho * v * v
              Regime = (if twoPhase then Some(Mechanics.flowRegime sat ln.Id (w * (1.0 - x) / (sat.RhoL * Piping.area ln)) (w * x / (sat.RhoV * Piping.area ln))) else None)
              Connected = true
              Bom = Piping.billOfMaterial ln
              Note = ln.Note }
        let lineChecks =
            (riserFlows |> List.map (mkCheck true circ.XOutRiser))
            @ (dcFlows |> List.map (mkCheck false 0.0))
            @ (notConnected
               |> List.map (fun ln ->
                    { Tag = ln.Tag; Nps = ln.Nps; Id = ln.Id; Count = ln.Count
                      ZNozzle = ln.ZNozzle; AngleDeg = ln.AngleDeg
                      DevelopedLength = 0.0; NElbows = 0; KTotal = 0.0
                      Flow = 0.0; Velocity = 0.0; RhoV2 = 0.0; Regime = None
                      Connected = false
                      Bom = "linea non realizzata"
                      Note = ln.Note }))
        let findings =
            buildFindings settings.CorrelationValidityWarnings case sat cells axial circ ftRes riserChecks expansions bpRes out.DpGas
                stressRes valveRes notConnected vibration convergence out.SulphurCoupling sulphurCondenserResult
        let warnings =
            findings
            |> List.map (fun f ->
                sprintf "%s - %s: %s (criterio: %s) @ %s%s%s"
                    (sev f.Severity) f.Title f.Value f.Limit f.Where
                    (if f.Detail = "" then "" else " | " + f.Detail)
                    (if f.Action = "" then "" else " | AZIONE: " + f.Action))

        { Case = case
          Sat = sat
          Bands = bands
          Cells = cells
          Axial = axial
          Circulation = circ
          Nozzles = nozzles
          Expansions = expansions
          FixedTubesheet = ftRes
          Stress = stressRes
          Valve = valveRes
          Vibration = vibration
          Maldistribution = maldist
          Transient = transient
          ChfModels = chfModels
          Sensitivity = sensitivity
          FoulingCases = foulingCases
          DrumResult =
            (if case.Loop.Drum.Enabled then
                Some(Drum.solve case.Loop.Drum sat circ.CircFlow circ.XOutRiser
                         circ.SteamFlow (Circulation.branchArea case.Loop.Risers)
                         (Circulation.branchArea case.Loop.Downcomers))
             else None)
          BypassResult = bpRes
          SulphurCoupling = out.SulphurCoupling
          SulphurCondenserResult = sulphurCondenserResult
          Findings = findings
          RiserChecks = riserChecks
          LineChecks = lineChecks
          FerruleClasses =
            out.Classes
            |> List.mapi (fun ci (frac, fl) ->
                let mutable qFluxMax = Double.NegativeInfinity
                let mutable zQMax = 0.0
                let mutable tMetalInMax = Double.NegativeInfinity
                let mutable dnbrMin = Double.PositiveInfinity
                let mutable duty = 0.0
                for cell in cells do
                    if cell.C = ci then
                        if cell.TMetalIn > tMetalInMax then tMetalInMax <- cell.TMetalIn
                        duty <- duty + cell.QLin * out.Dz.[cell.I] * cell.NTubes
                        if not cell.InFerrule then
                            if cell.QFluxOut > qFluxMax then
                                qFluxMax <- cell.QFluxOut
                                zQMax <- cell.Z
                            if cell.DNBR < dnbrMin then dnbrMin <- cell.DNBR
                { Index = ci
                  Frac = frac
                  Length = fl
                  QFluxMax = qFluxMax
                  ZQMax = zQMax
                  TMetalInMax = tMetalInMax
                  DNBRMin = dnbrMin
                  TGasOut =
                    (let mutable a = 0.0
                     let mutable wq = 0.0
                     for j in 0 .. ny - 1 do
                        a <- a + ntb.[j] * out.TGasOutBandClass.[j, ci]
                        wq <- wq + ntb.[j]
                     a / wq)
                  Duty = duty })
          Duty = out.Duty + (match bpRes with Some b -> b.HeatLoss | None -> 0.0)
          SteamProduction = out.Steam + (match bpRes with Some b -> b.SteamFromBypass | None -> 0.0)
          TGasOutMean = tOutMean
          TGasOutMin = tMin
          TGasOutMax = tMax
          DpGas = out.DpGas
          AreaOut = areaOut
          AreaIn = areaIn
          UMean = (if lm > 0.0 then out.Duty / (areaOut * lm) else 0.0)
          LmtdMean = lm
          SteamProductionNet = steamNet
          FeedSubcooling = feedSubcooling
          Convergence = convergence
          Warnings = warnings }

    /// <summary>
    /// Runs the complete WHB design calculation with progress callbacks and default runtime settings.
    /// </summary>
    /// <remarks>
    /// This overload preserves the earlier API while allowing callers to observe phase-level progress.
    /// </remarks>
    let runWithProgress (reportProgress: string -> unit) (caseIn: DesignCase) : DesignResult =
        runWithSettingsAndProgress defaultRunSettings reportProgress caseIn

    /// <summary>
    /// Runs the complete WHB design calculation without progress callbacks.
    /// </summary>
    /// <remarks>
    /// Coordinates process, thermal, hydraulic, vibration, mechanical, and equipment checks into the WHB design result.
    /// </remarks>
    let run (caseIn: DesignCase) : DesignResult =
        runWithSettingsAndProgress defaultRunSettings ignore caseIn
