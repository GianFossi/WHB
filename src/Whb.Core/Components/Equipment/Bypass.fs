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
          HeatLoss: float          // W transferred from bypass to water
          SteamFromBypass: float   // kg/s
          Nodes: Node list
          TLinerMax: float         // K
          TPipeMax: float          // K
          DpBypass: float          // Pa
          Converged: bool }
    let private wallResistance (s: Spec) (tLinerC: float) (tPipeC: float) =
        let rLiner = log (s.LinerOd / s.LinerId) / (2.0 * Math.PI * s.LinerMaterial.K tLinerC)
        let rIns = log (s.InsulOd / s.LinerOd) / (2.0 * Math.PI * s.InsulK (0.5 * (tLinerC + tPipeC)))
        let rPipe = log (s.PipeOd / s.InsulOd) / (2.0 * Math.PI * s.PipeMaterial.K tPipeC)
        (rLiner, rIns, rPipe)
    let march (s: Spec) (comp: GasProps.Composition) (pIn: float) (z: float) (tIn: float)
              (mixRule: GasProps.MixingRule) (real: bool) (shiftMode: Shift.Mode)
              (sat: Steam.SatProps) (wBp: float) (zc: float[]) (dz: float[]) =
        let comp0 = GasProps.normalize comp
        let rH2O = GasProps.molFrac comp0 GasProps.H2O
        let rCO2 = GasProps.molFrac comp0 GasProps.CO2
        let a = Math.PI * s.LinerId * s.LinerId / 4.0
        let mutable h = GasProps.enthalpyAbsReal real comp0 tIn pIn
        let mutable p = pIn
        let nodes = ResizeArray<Node>()
        let mutable qTot = 0.0
        let gasCache = Collections.Generic.Dictionary<string, GasProps.MixProps>()
        let wallCache = Collections.Generic.Dictionary<string, float * float * float>()
        let gasProps tK pPa =
            let key = sprintf "%.1f|%.0f" (Math.Round(tK * 2.0) / 2.0) (Math.Round(pPa / 100.0) * 100.0)
            match gasCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v = GasProps.mixReal mixRule real comp0 (Math.Round(tK * 2.0) / 2.0) pPa 1.0
                gasCache.[key] <- v
                v
        let cachedWallResistance linerC pipeC =
            let key = sprintf "%.1f|%.1f" (Math.Round(linerC * 2.0) / 2.0) (Math.Round(pipeC * 2.0) / 2.0)
            match wallCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v = wallResistance s linerC pipeC
                wallCache.[key] <- v
                v
        for i in 0 .. zc.Length - 1 do
            let tg = fst (Shift.stateFromEnthalpyAt shiftMode real p comp0 h)
            let props = gasProps tg p
            let g_ = wBp / a
            let vel = g_ / props.Rho
            let re = g_ * s.LinerId / props.Mu
            let mutable tli = tg - 50.0
            let mutable tlo = tli
            let mutable tpi = sat.Tsat + 5.0
            let mutable tpo = sat.Tsat + 2.0
            let mutable q = 0.0
            let mutable hg = 0.0
            for _ in 1 .. 12 do
                let nu = GasSide.nusseltFD GasSide.Gnielinski re props.Pr 1.0
                let fProp = GasSide.gasPropertyCorrection tli props.T
                let hConv = nu * fProp * props.K / s.LinerId
                let eps = GasProps.gasEmissivity rH2O rCO2 p (0.9 * s.LinerId) props.T
                let hRad = GasProps.hRadiation eps 0.85 props.T tli
                hg <- hConv + hRad
                let rGas = 1.0 / (hg * Math.PI * s.LinerId)
                let rFoul = s.FoulingIn / (Math.PI * s.LinerId)
                let (rL, rI, rP) = cachedWallResistance (kToC (0.5 * (tli + tlo))) (kToC (0.5 * (tpi + tpo)))
                let hb =
                    WaterSide.hMostinski (max 1000.0 (q / (Math.PI * s.PipeOd))) sat.P Pc_water * 1.2
                    + 250.0
                let rB = 1.0 / (hb * Math.PI * s.PipeOd)
                let rTot = rGas + rFoul + rL + rI + rP + rB
                q <- (props.T - sat.Tsat) / rTot
                tpo <- sat.Tsat + q * rB
                tpi <- tpo + q * rP
                tlo <- tpi + q * rI
                tli <- tlo + q * rL
            let dzi = dz.[i]
            qTot <- qTot + q * dzi
            nodes.Add
                { Z = zc.[i]; TGas = tg; Vel = vel; Re = re; HGas = hg
                  QLin = q; TLinerIn = tli; TLinerOut = tlo
                  TPipeIn = tpi; TPipeOut = tpo; DTInsul = tlo - tpi }
            h <- h - q * dzi / max 1e-9 wBp
            let f = GasSide.darcyFriction re (4.5e-5 / s.LinerId)
            p <- p - GasSide.dpFrictionPerM f s.LinerId props.Rho vel * dzi
        let tOut = fst (Shift.stateFromEnthalpyAt shiftMode real p comp0 h)
        (List.ofSeq nodes, tOut, qTot, pIn - p)





