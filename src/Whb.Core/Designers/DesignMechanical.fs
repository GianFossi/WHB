namespace Whb.Core

open System
open Constants
open Types
open DesignContracts

/// <summary>
/// Runs the mechanical and thermoelastic screening on top of the thermal/process stage output.
/// </summary>
/// <remarks>
/// The thermal/process stage remains the owner of the verification solve; this module only
/// consumes its published contract. That keeps future mechanical implementation work separate
/// while still allowing both parts to communicate through typed data instead of shared state.
/// </remarks>
module DesignMechanical =

    let run (reportProgress: DesignRuntime.ProgressUpdate -> unit) (thermal: ThermalProcessStageResult) : MechanicalStageResult =
        let phase fraction text =
            reportProgress (ExecutionProgress.Reporting.step fraction text)
        let case = thermal.Case
        let sat = thermal.Sat
        let t = case.Tube
        let bpSpec = case.Bypass
        let tRoom = case.AssemblyTemperature
        let dz = thermal.DZ
        let cellField = thermal.CellField
        let classLayouts = thermal.ClassLayouts
        let bands = thermal.Bands
        let bpRes = thermal.BypassResult
        let circ = thermal.Circulation
        let cells = thermal.Cells
        let nz = dz.Length
        let ny = List.length bands
        let ncls = List.length classLayouts

        let segsFor (ci: int) (j: int) =
            [ for i in 0 .. nz - 1 -> (dz.[i], cellField.[i, j, ci].TMetalWallAvg) ]

        phase 0.10 "Calculating expansion, tubesheet loads, and mechanical stresses"
        let allExp =
            [ for ci in 0 .. ncls - 1 do
                for j in 0 .. ny - 1 ->
                    let e = Mechanics.axialExpansion case.Material tRoom (segsFor ci j)
                    (ci, j, e) ]
        let perClass =
            classLayouts
            |> List.map (fun cls ->
                let (_, jj, e) =
                    allExp
                    |> List.filter (fun (c2, _, _) -> c2 = cls.Index)
                    |> List.maxBy (fun (_, _, e) -> e.DeltaL)
                { e with Label = sprintf "Tubi - ferrula %.0f mm, banda %d (dL max)" (cls.Length * 1000.0) jj })
        let coldest =
            let (cc2, jj, e) = allExp |> List.minBy (fun (_, _, e) -> e.DeltaL)
            { e with
                Label =
                    sprintf "Tubi - banda %d, ferrula %.0f mm (dL MINIMO)"
                        jj ((List.item cc2 classLayouts).Length * 1000.0) }
        let meanTube =
            let segs =
                [ for i in 0 .. nz - 1 ->
                    let num =
                        [ for j in 0 .. ny - 1 do
                            for c in 0 .. ncls - 1 ->
                                cellField.[i, j, c].TMetalWallAvg * cellField.[i, j, c].NTubes ] |> List.sum
                    let den =
                        [ for j in 0 .. ny - 1 do
                            for c in 0 .. ncls - 1 -> cellField.[i, j, c].NTubes ] |> List.sum
                    (dz.[i], num / den) ]
            let e = Mechanics.axialExpansion case.Material tRoom segs
            { e with Label = sprintf "Tubi - MEDIA PESATA su tutti i %d tubi" t.NTubes }
        let (tShellMetal, _) =
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
                  TEquivalent = nan
                  AlphaMean = nan
                  Length = t.Length
                  DeltaL = meanTube.DeltaL - eShell.DeltaL }
                { Label = "DIFFERENZIALE tubo piu' caldo - mantello"
                  TEquivalent = nan
                  AlphaMean = nan
                  Length = t.Length
                  DeltaL = dHot - eShell.DeltaL }
                { Label = "DIFFERENZIALE fra tubi (piu' caldo - piu' freddo)"
                  TEquivalent = nan
                  AlphaMean = nan
                  Length = t.Length
                  DeltaL = dHot - dCold } ]
        let ftRes =
            Mechanics.fixedTubesheet case.Material case.ShellMaterial tRoom t.Length t.NTubes
                t.Do t.Di t.ShellId case.ShellThickness case.UnsupportedSpan
                meanTube.TEquivalent (perClass |> List.maxBy (fun e -> e.DeltaL)).TEquivalent eShell.TEquivalent

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
                    b.Nodes |> List.mapi (fun i n -> (dz.[i], 0.5 * (n.TPipeIn + n.TPipeOut)))
                Mechanics.axialExpansion bpSpec.PipeMaterial tRoom segs)
        let bpLinerExp =
            bpRes
            |> Option.map (fun b ->
                let segs =
                    b.Nodes |> List.mapi (fun i n -> (dz.[i], 0.5 * (n.TLinerIn + n.TLinerOut)))
                Mechanics.axialExpansion bpSpec.LinerMaterial tRoom segs)
        let memberSpecs =
            [ for (ci, j, e) in allExp ->
                let n = cellField.[0, j, ci].NTubes
                (sprintf "Tubi banda %d (y = %+.2f m), ferrula %.0f mm" j (List.item j bands).Y
                     ((List.item ci classLayouts).Length * 1000.0),
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
        let sigmaBpZ =
            if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZ else 0.0
        let stressTubes =
            [ for ci in 0 .. ncls - 1 do
                for j in 0 .. ny - 1 do
                    for i in 0 .. nz - 1 ->
                        let c = cellField.[i, j, ci]
                        let tAvg = kToC c.TMetalWallAvg
                        let dT = c.TMetalIn - c.TMetalOut
                        let pts =
                            Mechanics.stressPoints c.PGas pShell (t.Di / 2.0) (t.Do / 2.0)
                                sigmaTubeGroup.[ci, j] (case.Material.Alpha tAvg)
                                (case.Material.E tAvg) dT
                        let worst = pts |> List.maxBy (fun p -> p.SigmaVM)
                        let sy = case.Material.Sy tAvg
                        { Component = "TUBI"
                          I = i
                          J = j
                          C = ci
                          Z = c.Z
                          Y = c.Y
                          TMetalIn = c.TMetalIn
                          TMetalOut = c.TMetalOut
                          TMetalAvg = c.TMetalWallAvg
                          DTWall = dT
                          PInt = c.PGas
                          PExt = pShell
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
                      I = i
                      J = -1
                      C = -1
                      Z = n.Z
                      Y = 0.0
                      TMetalIn = n.TPipeIn
                      TMetalOut = n.TPipeOut
                      TMetalAvg = cToK tAvg
                      DTWall = dT
                      PInt = pInt
                      PExt = pShell
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
                 let dpDes = factor * thermal.DpGas
                 let pE = 2.0 * ee / (1.0 - Mechanics.nu * Mechanics.nu) * (thk / dm) ** 3.0
                 let pY = 2.0 * sy * thk / od
                 let pC = 1.0 / sqrt (1.0 / (pE * pE) + 1.0 / (pY * pY))
                 { DpTubes = thermal.DpGas
                   DpDesign = dpDes
                   Factor = factor
                   Od = od
                   Id = idl
                   Thickness = thk
                   TEq = tEq
                   E = ee
                   Sy = sy
                   PCrElastic = pE
                   PCrYield = pY
                   PCollapse = pC
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

        phase 0.60 "Checking risers, downcomers, findings, and final result tables"
        let riserFlows = Circulation.lineFlows case sat case.Loop.Risers true circ.XOutRiser circ.CircFlow
        let dcFlows = Circulation.lineFlows case sat case.Loop.Downcomers false 0.0 circ.CircFlow
        let riserChecks =
            Mechanics.checkRisers sat riserFlows circ.XOutRiser case.MaxRhoV2Riser case.Loop.VoidModel
        let mkCheck (twoPhase: bool) (x: float) ((ln: Piping.Line), (w: float)) =
            let rho = if twoPhase then TwoPhase.homogeneousDensity x sat else sat.RhoL
            let v = w / (rho * Piping.area ln)
            let re = max 100.0 (rho * v * ln.Id / sat.MuL)
            let f = GasSide.darcyFriction re (4.5e-5 / ln.Id)
            { Tag = ln.Tag
              Nps = ln.Nps
              Id = ln.Id
              Count = ln.Count
              ZNozzle = ln.ZNozzle
              AngleDeg = ln.AngleDeg
              DevelopedLength = Piping.developedLength ln
              NElbows = Piping.elbowCount ln
              KTotal = Piping.totalK f ln
              Flow = w
              Velocity = v
              RhoV2 = rho * v * v
              Regime = (if twoPhase then Some(Mechanics.flowRegime sat ln.Id (w * (1.0 - x) / (sat.RhoL * Piping.area ln)) (w * x / (sat.RhoV * Piping.area ln))) else None)
              Connected = true
              Bom = Piping.billOfMaterial ln
              Note = ln.Note }
        let lineChecks =
            (riserFlows |> List.map (mkCheck true circ.XOutRiser))
            @ (dcFlows |> List.map (mkCheck false 0.0))
            @ (thermal.NotConnectedLines
               |> List.map (fun ln ->
                    { Tag = ln.Tag
                      Nps = ln.Nps
                      Id = ln.Id
                      Count = ln.Count
                      ZNozzle = ln.ZNozzle
                      AngleDeg = ln.AngleDeg
                      DevelopedLength = 0.0
                      NElbows = 0
                      KTotal = 0.0
                      Flow = 0.0
                      Velocity = 0.0
                      RhoV2 = 0.0
                      Regime = None
                      Connected = false
                      Bom = "linea non realizzata"
                      Note = ln.Note }))
        phase 0.85 "Preparing the detailed mechanical-calculation interface"
        let calculationInterface =
            MechanicalDesignInterface.runPure thermal ftRes stressRes

        phase 1.0 "Mechanical screening stage completed"
        { Expansions = expansions
          FixedTubesheet = ftRes
          Stress = stressRes
          RiserChecks = riserChecks
          LineChecks = lineChecks
          CalculationInterface = calculationInterface }

    /// <summary>
    /// Runs the mechanical screening stage without emitting progress side effects.
    /// </summary>
    let runPure (thermal: ThermalProcessStageResult) : MechanicalStageResult =
        run ignore thermal
