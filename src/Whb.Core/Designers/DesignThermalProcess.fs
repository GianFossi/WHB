namespace Whb.Core

open System
open Constants
open Types
open DesignContracts

/// <summary>
/// Runs the coupled thermal, process, hydraulic, bypass, vibration, and auxiliary campaigns.
/// </summary>
/// <remarks>
/// This stage owns the single source of truth for geometry verification on the process side. It
/// prepares the shared contract consumed by the separate mechanical stage and by the final report
/// assembly, so downstream code never needs to re-solve the thermal model.
/// </remarks>
module DesignThermalProcess =

    let private maxRelativeDelta (a: float[]) (b: float[]) =
        let n = min a.Length b.Length
        let mutable d = 0.0
        for i in 0 .. n - 1 do
            let den = max 1e-9 (max (abs a.[i]) (abs b.[i]))
            d <- max d (abs (a.[i] - b.[i]) / den)
        d

    let run (settings: DesignRuntime.RunSettings) (reportProgress: DesignRuntime.ProgressUpdate -> unit) (caseIn: DesignCase) : ThermalProcessStageResult =
        let phase fraction text =
            reportProgress (ExecutionProgress.Reporting.step fraction text)
        let pointPhase (reportPointProgress: DesignRuntime.ProgressUpdate -> unit) fraction text =
            reportPointProgress (ExecutionProgress.Reporting.step fraction text)
        phase 0.03 "Preparing connected risers, downcomers, steam properties, and tube bands"
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
            Collections.Concurrent.ConcurrentDictionary<struct (string * bool * string * float * float * float), GasProps.MixProps>()

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

        let coupled (reportPointProgress: DesignRuntime.ProgressUpdate -> unit) (cx: DesignCase) =
            let maxIterations = 5
            pointPhase reportPointProgress 0.05 "Preparing coupled bundle/circulation point solve"
            let mutable wField = Array.create nz (15.0 * steamGuess / t.Length)
            let mutable xIn = Array.create nz 0.0
            pointPhase reportPointProgress 0.16 "Solving initial bundle field"
            let mutable o = BundleSolver.solve cx bands wField xIn
            pointPhase reportPointProgress 0.30 "Solving initial natural-circulation field"
            let mutable d = Circulation.solve cx sat bands o.BandDuty o.SteamLin o.Dz
            let mutable iter = 1
            let mutable converged = false
            let mutable residual = infinity
            while iter <= maxIterations && not converged do
                pointPhase reportPointProgress
                    (0.30 + 0.08 * float (iter - 1))
                    (sprintf "Correcting coupled thermal/circulation field (%d/%d)" iter maxIterations)
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
                pointPhase reportPointProgress
                    (0.38 + 0.08 * float (iter - 1))
                    (sprintf "Coupled correction iteration %d/%d solved (residual %.3g)" iter maxIterations residual)
                iter <- iter + 1
            pointPhase reportPointProgress
                0.72
                (if converged then
                    sprintf "Coupled bundle/circulation point converged in %d iteration(s)" (iter - 1)
                 else
                    sprintf "Coupled bundle/circulation point reached %d iteration(s)" maxIterations)
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

        let evaluate (x: float) (reportPointProgress: DesignRuntime.ProgressUpdate -> unit) =
            let (o, d, iters, conv, residual) = coupled reportPointProgress (caseWith x)
            let health = struct (iters, conv, residual)
            let tTubes = tubeOutOf o
            let compTubes = tubeOutletCompositionOf o
            if not case.Bypass.Enabled || x <= 1e-6 then
                pointPhase reportPointProgress 1.0 "Bundle-only point solve completed"
                (tTubes, o, d, None, health)
            else
                pointPhase reportPointProgress 0.76 "Marching bypass axial thermal/hydraulic profile"
                let (nodes, tBp, compBp, qBp, dpBp, bpConverged) =
                    Bypass.marchWithProgress
                        (ExecutionProgress.Reporting.scale 0.78 0.96 reportPointProgress)
                        case.Bypass comp0 case.Gas.PIn 0.0 case.Gas.TIn
                        case.Gas.MixingRule case.Gas.RealGas case.Gas.ShiftMode
                        case.Gas.ClausMode case.Gas.ClausKinetics sat (wGasTot * x) o.ZC o.Dz
                pointPhase reportPointProgress 0.98 "Mixing bypass and tube outlet streams"
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
                pointPhase reportPointProgress 1.0 "Bypass point solve completed"
                (tMix, o, d, Some res, health)

        let bpSpec = case.Bypass
        let aLiner = Math.PI * bpSpec.LinerId * bpSpec.LinerId / 4.0

        let mapPoint (x: float) (reportPointProgress: DesignRuntime.ProgressUpdate -> unit) : DesignBypass.MapPoint =
            let (tm, o, _, bp, _) = evaluate x reportPointProgress
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
                  Phase = ExecutionProgress.Reporting.scale 0.12 0.48 reportProgress
                  AcquireWorker = DesignRuntime.ParallelBudget.acquire
                  ReleaseWorker = DesignRuntime.ParallelBudget.release
                  MapPointAt = mapPoint }

        phase 0.58 "Solving final coupled thermal and natural-circulation case"
        let (tMixed, out, dist, bpRes, struct (coupledIters, coupledOk, coupledResidual)) =
            evaluate bypass.XUsed ignore

        let totalDuty = out.Duty + (match bpRes with Some b -> b.HeatLoss | None -> 0.0)
        let hFeed = Steam.hLiquid case.Water.DrumPressure (min case.Water.TFeed (sat.Tsat - 0.01))
        let feedSubcooling = max 0.0 (sat.Tsat - case.Water.TFeed)
        let steamNet =
            let rise = sat.HV - hFeed
            if rise > 1.0 then totalDuty / rise else nan
        let dcSubcooling =
            let wCirc = max 1e-9 dist.Global.CircFlow
            let dh = (sat.HL - hFeed) * min 1.0 (steamNet / wCirc)
            if Double.IsNaN dh then 0.0 else max 0.0 (dh / max 1.0 sat.CpL)
        let dcSubcoolingRequired =
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
              BypassMapBracketsTarget = bypass.BypassMapBracketsTarget }
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

        phase 0.72 "Calculating CHF, fouling, and correlation sensitivity cases"
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
        let chfModels = thermalPost.ChfModels
        let sensitivity = thermalPost.Sensitivity
        let foulingCases = thermalPost.FoulingCases

        phase 0.84 "Running vibration screening over tube bands and support spans"
        let vibration =
            let gasDensityAt (c: CellResult) =
                (mixRealCached case.Gas.MixingRule case.Gas.RealGas comp0 c.TGas c.PGas case.Gas.Z).Rho
            DesignVibration.run case sat t ny cells gasDensityAt

        phase 0.92 "Running maldistribution sensitivity campaign"
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
                  BypassFractionUsed = bypass.XUsed
                  BaseCells = Array.init nz (fun i -> out.Cells.[i, jb, 0])
                  DZ = out.Dz
                  InletEnthalpy = processEnthalpyAt case.Gas.PIn comp0 case.Gas.TIn
                  StateFromEnthalpyAt = stateFromEnthalpyAt
                  GasPropsAt = gasPropsAt }

        phase 0.97 "Calculating transient dry-out screening and water inventory basis"
        let transient =
            DesignTransient.run case sat t bpSpec.PipeOd cells totalDuty

        let classLayouts =
            out.Classes
            |> List.mapi (fun ci (frac, fl) ->
                { Index = ci
                  Fraction = frac
                  Length = fl })

        let ferruleClasses =
            classLayouts
            |> List.map (fun cls ->
                let mutable qFluxMax = Double.NegativeInfinity
                let mutable zQMax = 0.0
                let mutable tMetalInMax = Double.NegativeInfinity
                let mutable dnbrMin = Double.PositiveInfinity
                let mutable duty = 0.0
                for cell in cells do
                    if cell.C = cls.Index then
                        if cell.TMetalIn > tMetalInMax then tMetalInMax <- cell.TMetalIn
                        duty <- duty + cell.QLin * out.Dz.[cell.I] * cell.NTubes
                        if not cell.InFerrule then
                            if cell.QFluxOut > qFluxMax then
                                qFluxMax <- cell.QFluxOut
                                zQMax <- cell.Z
                            if cell.DNBR < dnbrMin then dnbrMin <- cell.DNBR
                { Index = cls.Index
                  Frac = cls.Fraction
                  Length = cls.Length
                  QFluxMax = qFluxMax
                  ZQMax = zQMax
                  TMetalInMax = tMetalInMax
                  DNBRMin = dnbrMin
                  TGasOut =
                    (let mutable a = 0.0
                     let mutable wq = 0.0
                     for j in 0 .. ny - 1 do
                        a <- a + ntb.[j] * out.TGasOutBandClass.[j, cls.Index]
                        wq <- wq + ntb.[j]
                     a / wq)
                  Duty = duty })

        { Case = case
          NotConnectedLines = notConnected
          Sat = sat
          Bands = bands
          DZ = out.Dz
          CellField = out.Cells
          ClassLayouts = classLayouts
          Cells = cells
          Axial = axial
          Circulation = circ
          Nozzles = nozzles
          FerruleClasses = ferruleClasses
          Valve = bypass.Valve
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
          Duty = totalDuty
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
          Convergence = convergence }

    /// <summary>
    /// Runs the thermal/process verification stage without emitting progress side effects.
    /// </summary>
    let runPure (settings: DesignRuntime.RunSettings) (caseIn: DesignCase) : ThermalProcessStageResult =
        run settings ignore caseIn
