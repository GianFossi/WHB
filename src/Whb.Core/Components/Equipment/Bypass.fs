namespace Whb.Core

open System
open Constants

/// <summary>
/// Calculates bypass duct thermal performance, wall temperatures, heat loss, and pressure drop.
/// </summary>
/// <remarks>
/// Calculates bypass duct thermal and hydraulic behavior using gas-side convection, radiation, wall resistance, insulation, and pressure-drop estimates. Validate empirical coefficients, material conductivity, fouling, geometry, and operating range before final sizing.
/// </remarks>
module Bypass =
    type Spec =
        { Enabled: bool
          Fraction: float option
          TargetMixOut: float
          LinerId: float
          LinerOd: float
          LinerMaterial: Materials.Material
          InsulOd: float
          InsulK: float -> float
          PipeOd: float
          PipeMaterial: Materials.Material
          FoulingIn: float
          ExtraK: float
          ValveAtOutlet: bool
          ValveOpenDeg: float option
          MinOpenDeg: float
          MaxOpenDeg: float
          TMixMin: float
          TMixMax: float
          MinPurgeVel: float
          MaxRhoV2Valve: float }
    type Node =
        { Z: float
          TGas: float          // K
          Vel: float           // m/s
          Re: float
          HGas: float          // W/(m²·K)
          QLin: float          // W/m transferred to water
          TLinerIn: float      // K, liner inner face
          TLinerOut: float     // K
          TPipeIn: float       // K, containment pipe inner face
          TPipeOut: float      // K
          DTInsul: float }     // K, insulation temperature drop
    type Result =
        { Fraction: float
          MassFlow: float          // kg/s diverted
          TOutBypass: float        // K, bypass outlet
          TOutTubes: float         // K, tube outlet
          TOutMixed: float         // K, after mixing
          OutletComposition: GasProps.Composition
          HeatLoss: float          // W transferred from bypass to water
          SteamFromBypass: float   // kg/s
          Nodes: Node list
          TLinerMax: float         // K
          TPipeMax: float          // K
          DpBypass: float          // Pa
          Converged: bool }

    type ProgressReporter = ExecutionProgress.ProgressUpdate -> unit

    let private reportStep (reportProgress: ProgressReporter) fraction description =
        reportProgress (ExecutionProgress.Reporting.step fraction description)

    let private wallResistance (s: Spec) (tLinerC: float) (tPipeC: float) =
        let rLiner = log (s.LinerOd / s.LinerId) / (2.0 * Math.PI * s.LinerMaterial.K tLinerC)
        let rIns = log (s.InsulOd / s.LinerOd) / (2.0 * Math.PI * s.InsulK (0.5 * (tLinerC + tPipeC)))
        let rPipe = log (s.PipeOd / s.InsulOd) / (2.0 * Math.PI * s.PipeMaterial.K tPipeC)
        (rLiner, rIns, rPipe)

    type private PreparedMarch =
        { Spec: Spec
          Composition0: GasProps.Composition
          ProcessActive: bool
          Area: float
          InitialEnthalpy: float
          ProcessStateFromEnthalpyAt: float -> GasProps.Composition -> float -> Sulphur.ProcessState
          GasPropsAt: GasProps.Composition -> float -> float -> GasProps.MixProps
          WallResistanceAt: float -> float -> float * float * float }

    [<Struct>]
    type private MarchState =
        { Enthalpy: float
          Pressure: float
          Composition: GasProps.Composition
          TotalHeat: float
          AllConverged: bool }

    [<Struct>]
    type private SolvedNode =
        { Node: Node
          HeatPerLength: float
          PressureDrop: float
          NextEnthalpy: float
          NextComposition: GasProps.Composition
          Converged: bool }

    let private compKey (comp: GasProps.Composition) =
        comp
        |> GasProps.normalize
        |> List.map (fun (sp, y) -> sprintf "%s=%.6f" (GasProps.speciesName sp) y)
        |> String.concat ";"

    let private buildProcessStateFromEnthalpyAt (shiftMode: Shift.Mode) (real: bool) (processActive: bool) =
        fun pPa compNow hNow ->
            if processActive then
                Sulphur.processStateFromEnthalpyAt shiftMode real pPa compNow hNow
            else
                let (t0, compGas) = Shift.stateFromEnthalpyAt shiftMode real pPa compNow hNow
                let st : Sulphur.ProcessState =
                    { T = t0
                      VapourComposition = compGas
                      TotalSpecificEnthalpy = hNow
                      CpApprox = (GasProps.mixReal GasProps.Wilke real compGas t0 pPa 1.0).Cp
                      PSulphur = 0.0
                      YElementalSulphurVapour = 0.0
                      SulphurDewPoint = None
                      Condensing = false
                      CondensedAtoms = 0.0
                      CondensedFraction = 0.0 }
                st

    let private buildGasPropsResolver (mixRule: GasProps.MixingRule) (real: bool)
                                      (shiftMode: Shift.Mode) (processActive: bool) =
        let gasCache = Collections.Generic.Dictionary<string, GasProps.MixProps>()

        fun compProc tK pPa ->
            let compUse =
                if processActive then
                    (Sulphur.processStateAt shiftMode real pPa compProc (Math.Round(tK * 2.0) / 2.0)).VapourComposition
                else compProc
            let key =
                sprintf "%s|%.1f|%.0f"
                    (compKey compUse)
                    (Math.Round(tK * 2.0) / 2.0)
                    (Math.Round(pPa / 100.0) * 100.0)
            match gasCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let tUse = Math.Round(tK * 2.0) / 2.0
                let v = GasProps.mixReal mixRule real compUse tUse pPa 1.0
                gasCache.[key] <- v
                v

    let private buildWallResistanceResolver (s: Spec) =
        let wallCache = Collections.Generic.Dictionary<string, float * float * float>()

        fun linerC pipeC ->
            let key =
                sprintf "%.1f|%.1f"
                    (Math.Round(linerC * 2.0) / 2.0)
                    (Math.Round(pipeC * 2.0) / 2.0)
            match wallCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v = wallResistance s linerC pipeC
                wallCache.[key] <- v
                v

    let private prepareMarch (s: Spec) (comp: GasProps.Composition) (pIn: float) (tIn: float)
                             (mixRule: GasProps.MixingRule) (real: bool) (shiftMode: Shift.Mode)
                             (clausMode: Claus.Mode) =
        let comp0 = GasProps.normalize comp
        let processActive =
            Sulphur.hasElementalSulphur comp0
            || (clausMode <> Claus.Frozen && Claus.hasReactiveSpecies comp0)
        let processStateFromEnthalpyAt = buildProcessStateFromEnthalpyAt shiftMode real processActive
        { Spec = s
          Composition0 = comp0
          ProcessActive = processActive
          Area = Math.PI * s.LinerId * s.LinerId / 4.0
          InitialEnthalpy =
            if processActive then Sulphur.processEnthalpyAt shiftMode real pIn comp0 tIn
            else GasProps.enthalpyAbsReal real comp0 tIn pIn
          ProcessStateFromEnthalpyAt = processStateFromEnthalpyAt
          GasPropsAt = buildGasPropsResolver mixRule real shiftMode processActive
          WallResistanceAt = buildWallResistanceResolver s }

    let private initialState (prepared: PreparedMarch) (pIn: float) =
        { Enthalpy = prepared.InitialEnthalpy
          Pressure = pIn
          Composition = prepared.Composition0
          TotalHeat = 0.0
          AllConverged = true }

    let private nextComposition (clausMode: Claus.Mode) (clausKinetics: Claus.KineticParameters)
                                (tRef: float) (tau: float) (composition: GasProps.Composition) =
        if clausMode = Claus.Frozen then composition
        else Claus.advanceWith clausKinetics clausMode tRef tau composition

    let private solveNode (prepared: PreparedMarch) (sat: Steam.SatProps)
                          (clausMode: Claus.Mode) (clausKinetics: Claus.KineticParameters)
                          (wBp: float) (z: float) (dzi: float) (state: MarchState) =
        let sulphurState =
            prepared.ProcessStateFromEnthalpyAt state.Pressure state.Composition state.Enthalpy
        let tg = sulphurState.T
        let props = prepared.GasPropsAt state.Composition tg state.Pressure
        let rH2OUse = GasProps.molFrac sulphurState.VapourComposition GasProps.H2O
        let rCO2Use = GasProps.molFrac sulphurState.VapourComposition GasProps.CO2
        let g_ = wBp / prepared.Area
        let vel = g_ / props.Rho
        let re = g_ * prepared.Spec.LinerId / props.Mu
        let mutable tli = tg - 50.0
        let mutable tlo = tli
        let mutable tpi = sat.Tsat + 5.0
        let mutable tpo = sat.Tsat + 2.0
        let mutable q = 0.0
        let mutable hg = 0.0
        let mutable qPrev = 0.0
        let mutable lastRel = 1.0

        for _ in 1 .. 12 do
            let nu = GasSide.nusseltFD GasSide.Gnielinski re props.Pr 1.0
            let fProp = GasSide.gasPropertyCorrection tli props.T
            let hConv = nu * fProp * props.K / prepared.Spec.LinerId
            let eps = GasProps.gasEmissivity rH2OUse rCO2Use state.Pressure (0.9 * prepared.Spec.LinerId) props.T
            let hRad = GasProps.hRadiation eps 0.85 props.T tli
            hg <- hConv + hRad
            let rGas = 1.0 / (hg * Math.PI * prepared.Spec.LinerId)
            let rFoul = prepared.Spec.FoulingIn / (Math.PI * prepared.Spec.LinerId)
            let (rL, rI, rP) =
                prepared.WallResistanceAt (kToC (0.5 * (tli + tlo))) (kToC (0.5 * (tpi + tpo)))
            let hb =
                WaterSide.hMostinski (max 1000.0 (q / (Math.PI * prepared.Spec.PipeOd))) sat.P Pc_water * 1.2
                + 250.0
            let rB = 1.0 / (hb * Math.PI * prepared.Spec.PipeOd)
            let rTot = rGas + rFoul + rL + rI + rP + rB
            qPrev <- q
            q <- (props.T - sat.Tsat) / rTot
            lastRel <- abs (q - qPrev) / (abs q + 1e-12)
            tpo <- sat.Tsat + q * rB
            tpi <- tpo + q * rP
            tlo <- tpi + q * rI
            tli <- tlo + q * rL

        let node =
            { Z = z
              TGas = tg
              Vel = vel
              Re = re
              HGas = hg
              QLin = q
              TLinerIn = tli
              TLinerOut = tlo
              TPipeIn = tpi
              TPipeOut = tpo
              DTInsul = tlo - tpi }
        let hOut = state.Enthalpy - q * dzi / max 1e-9 wBp
        let f = GasSide.darcyFriction re (4.5e-5 / prepared.Spec.LinerId)
        let pressureDrop =
            GasSide.dpFrictionPerM f prepared.Spec.LinerId props.Rho vel * dzi
        let tau = dzi / max 1e-9 vel
        let tRef = 0.5 * (sulphurState.T + tli)
        { Node = node
          HeatPerLength = q
          PressureDrop = pressureDrop
          NextEnthalpy = hOut
          NextComposition = nextComposition clausMode clausKinetics tRef tau state.Composition
          Converged = lastRel <= 1e-6 }

    let private marchSegments (prepared: PreparedMarch) (sat: Steam.SatProps)
                              (clausMode: Claus.Mode) (clausKinetics: Claus.KineticParameters)
                              (wBp: float) (zc: float[]) (dz: float[]) (pIn: float)
                              (reportProgress: ProgressReporter) =
        let totalSegments = zc.Length
        let shouldReportSegment i =
            if totalSegments <= 1 then true
            elif i = 0 || i = totalSegments - 1 then true
            else
                let previousBucket = (i * 8) / totalSegments
                let nextBucket = ((i + 1) * 8) / totalSegments
                nextBucket <> previousBucket
        let folder (nodesRev, state) i =
            let dzi = dz.[i]
            let step = solveNode prepared sat clausMode clausKinetics wBp zc.[i] dzi state
            let nextState =
                { Enthalpy = step.NextEnthalpy
                  Pressure = state.Pressure - step.PressureDrop
                  Composition = step.NextComposition
                  TotalHeat = state.TotalHeat + step.HeatPerLength * dzi
                  AllConverged = state.AllConverged && step.Converged }
            if shouldReportSegment i then
                let fraction = float (i + 1) / float totalSegments
                reportStep reportProgress fraction (sprintf "Marching bypass axial profile (%d/%d)" (i + 1) totalSegments)
            (step.Node :: nodesRev, nextState)

        [ 0 .. zc.Length - 1 ]
        |> List.fold folder ([], initialState prepared pIn)
        |> fun (nodesRev, state) -> (List.rev nodesRev, state)

    let private outletTemperature (prepared: PreparedMarch) (state: MarchState) =
        (prepared.ProcessStateFromEnthalpyAt state.Pressure state.Composition state.Enthalpy).T

    let marchWithProgress (reportProgress: ProgressReporter)
                          (s: Spec) (comp: GasProps.Composition) (pIn: float) (z: float) (tIn: float)
                          (mixRule: GasProps.MixingRule) (real: bool) (shiftMode: Shift.Mode)
                          (clausMode: Claus.Mode) (clausKinetics: Claus.KineticParameters)
                          (sat: Steam.SatProps) (wBp: float) (zc: float[]) (dz: float[]) =
        let prepared = prepareMarch s comp pIn tIn mixRule real shiftMode clausMode
        let _ = z
        let (nodes, finalState) = marchSegments prepared sat clausMode clausKinetics wBp zc dz pIn reportProgress
        let tOut = outletTemperature prepared finalState
        (nodes, tOut, finalState.Composition, finalState.TotalHeat, pIn - finalState.Pressure, finalState.AllConverged)

    let march (s: Spec) (comp: GasProps.Composition) (pIn: float) (z: float) (tIn: float)
              (mixRule: GasProps.MixingRule) (real: bool) (shiftMode: Shift.Mode)
              (clausMode: Claus.Mode) (clausKinetics: Claus.KineticParameters)
              (sat: Steam.SatProps) (wBp: float) (zc: float[]) (dz: float[]) =
        marchWithProgress ignore s comp pIn z tIn mixRule real shiftMode clausMode clausKinetics sat wBp zc dz





