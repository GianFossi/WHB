namespace Whb.Core

open System
open Constants

/// **By-pass interno**: tubo centrale che attraversa il fascio nell'anima non
/// intubata e porta una frazione del gas di processo dall'ingresso all'uscita
/// senza raffreddarla. Serve a regolare la temperatura di uscita miscelata:
/// il WHB e' dimensionato per sovra-raffreddare in condizioni pulite, e il
/// by-pass rialza la temperatura fino al valore richiesto.
///
/// Costruzione (da disegno 3-E-1401 / 7523-01-300-01):
///   gas -> liner in Alloy 601/602 CA -> carta Saffil -> tubo di contenimento
///   -> acqua in ebollizione
/// Il liner regge la temperatura, la carta isola, il tubo di contenimento
/// resta vicino a Tsat e porta la pressione.
module Bypass =

    type Spec =
        { Enabled: bool
          /// Frazione di portata deviata; se None viene calcolata per centrare
          /// la temperatura di uscita miscelata
          Fraction: float option
          /// Temperatura di uscita miscelata richiesta [K]
          TargetMixOut: float
          /// Diametro interno del liner [m]
          LinerId: float
          /// Diametro esterno del liner [m]
          LinerOd: float
          /// Materiale del liner
          LinerMaterial: Materials.Material
          /// Diametro esterno dell'isolante (= ID del tubo di contenimento) [m]
          InsulOd: float
          /// k(T[°C]) dell'isolante
          InsulK: float -> float
          /// Diametro esterno del tubo di contenimento [m]
          PipeOd: float
          /// Materiale del tubo di contenimento
          PipeMaterial: Materials.Material
          /// Resistenza di sporcamento interna [m²·K/W]
          FoulingIn: float
          // ---- organo di regolazione (valvola a farfalla) ----
          /// K localizzati del ramo di by-pass ESCLUSA la valvola
          /// (imbocco + sbocco + eventuale diffusore)
          ExtraK: float
          /// La valvola e' sull'estremita' FREDDA (uscita) del by-pass
          ValveAtOutlet: bool
          /// Se assegnato, la ripartizione e' imposta dall'angolo di APERTURA
          /// della farfalla [gradi, 0 = chiusa, 90 = tutta aperta] invece che
          /// dalla temperatura di uscita richiesta
          ValveOpenDeg: float option
          /// Finestra di apertura entro cui la farfalla e' realmente
          /// regolante [gradi]
          MinOpenDeg: float
          MaxOpenDeg: float
          /// Finestra di temperatura miscelata ammessa dal processo [K]
          TMixMin: float
          TMixMax: float
          /// Velocita' minima nel liner per evitare il ramo morto [m/s]
          MinPurgeVel: float
          /// rho*v² massimo ammesso nella vena contratta della valvola [Pa]
          MaxRhoV2Valve: float }

    /// Risultato per una sezione assiale del by-pass
    type Node =
        { Z: float
          TGas: float          // K
          Vel: float           // m/s
          Re: float
          HGas: float          // W/(m²·K)
          QLin: float          // W/m ceduti all'acqua
          TLinerIn: float      // K, faccia interna del liner
          TLinerOut: float     // K
          TPipeIn: float       // K, faccia interna del tubo di contenimento
          TPipeOut: float      // K
          DTInsul: float }     // K, salto sull'isolante

    type Result =
        { Fraction: float
          MassFlow: float          // kg/s deviati
          TOutBypass: float        // K, uscita dal by-pass
          TOutTubes: float         // K, uscita dai tubi
          TOutMixed: float         // K, dopo miscelazione
          HeatLoss: float          // W ceduti dal by-pass all'acqua
          SteamFromBypass: float   // kg/s
          Nodes: Node list
          TLinerMax: float         // K
          TPipeMax: float          // K
          DpBypass: float          // Pa
          Converged: bool }

    /// Resistenza per unita' di lunghezza dell'insieme liner+isolante+tubo
    let private wallResistance (s: Spec) (tLinerC: float) (tPipeC: float) =
        let rLiner = log (s.LinerOd / s.LinerId) / (2.0 * Math.PI * s.LinerMaterial.K tLinerC)
        let rIns = log (s.InsulOd / s.LinerOd) / (2.0 * Math.PI * s.InsulK (0.5 * (tLinerC + tPipeC)))
        let rPipe = log (s.PipeOd / s.InsulOd) / (2.0 * Math.PI * s.PipeMaterial.K tPipeC)
        (rLiner, rIns, rPipe)

    /// Marcia lungo il by-pass con la stessa griglia assiale del fascio.
    ///   wBp : portata deviata [kg/s]
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
        for i in 0 .. zc.Length - 1 do
            let tg = fst (Shift.stateFromEnthalpyAt shiftMode real p comp0 h)
            let props = GasProps.mixReal mixRule real comp0 tg p 1.0
            let g_ = wBp / a
            let vel = g_ / props.Rho
            let re = g_ * s.LinerId / props.Mu
            // iterazione sulle temperature di parete
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
                let (rL, rI, rP) = wallResistance s (kToC (0.5 * (tli + tlo))) (kToC (0.5 * (tpi + tpo)))
                // ebollizione esterna sul tubo di contenimento
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
