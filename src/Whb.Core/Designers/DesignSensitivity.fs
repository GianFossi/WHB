namespace Whb.Core

open System
open Types

module DesignSensitivity =

    type Input =
        { Case: DesignCase
          Sat: Steam.SatProps
          Tube: TubeGeometry
          TotalGasFlow: float
          BypassFractionUsed: float
          BaseCells: CellResult array
          DZ: float array
          InletEnthalpy: float
          StateFromEnthalpyAt: float -> float -> Sulphur.ProcessState
          GasPropsAt: GasProps.Composition -> float -> float -> GasProps.MixProps }

    let run (input: Input) =
        let wTube0 = input.TotalGasFlow * (1.0 - input.BypassFractionUsed) / float input.Tube.NTubes
        [ for ex in [ 0.0; 0.05; 0.10; 0.15; 0.20; 0.30 ] ->
            let w = wTube0 * (1.0 + ex)
            let mutable h = input.InletEnthalpy
            let mutable qMax = 0.0
            let mutable zMax = 0.0
            let mutable tmiMax = 0.0
            let mutable dnbMin = infinity
            let mutable duty = 0.0
            let mutable reIn = 0.0
            let mutable hPeak = 0.0
            for i in 0 .. input.BaseCells.Length - 1 do
                let bc = input.BaseCells.[i]
                let p = bc.PGas
                let state = input.StateFromEnthalpyAt p h
                let tG = state.T
                let pr = input.GasPropsAt state.VapourComposition tG p
                let bore = if bc.InFerrule then input.Case.Ferrule.Bore else input.Tube.Di
                let re = 4.0 * w / (Math.PI * bore * pr.Mu)
                if i = 0 then reIn <- re
                let nu = GasSide.nusseltFD input.Case.Gas.Correlation re pr.Pr 1.0
                let fProp = GasSide.gasPropertyCorrection bc.TMetalIn pr.T
                let ent = GasSide.entranceCorrection bc.Z bore input.Case.Gas.EntranceC
                let hg = nu * fProp * ent * pr.K / bore + bc.HRadGas
                let hgBase = bc.HConvGas + bc.HRadGas
                let rTotBase = (bc.TGas - input.Sat.Tsat) / max 1.0 bc.QLin
                let rGasBase = 1.0 / (max 1.0 hgBase * Math.PI * bore)
                let rGasNew = 1.0 / (max 1.0 hg * Math.PI * bore)
                let rFoulIn = input.Case.Gas.FoulingIn / (Math.PI * bore)
                let rTot = rTotBase - rGasBase + rGasNew
                let qlin = (tG - input.Sat.Tsat) / max 1e-9 rTot
                let qOut = qlin / (Math.PI * input.Tube.Do)
                let tmi = tG - qlin * (rGasNew + rFoulIn)
                if not bc.InFerrule then
                    if qOut > qMax then
                        qMax <- qOut
                        zMax <- bc.Z
                        hPeak <- hg
                    let dnb = bc.QCritLocal / max 1.0 qOut
                    if dnb < dnbMin then dnbMin <- dnb
                    if tmi > tmiMax then tmiMax <- tmi
                duty <- duty + qlin * input.DZ.[i]
                h <- h - qlin * input.DZ.[i] / w
            let tOut = (input.StateFromEnthalpyAt input.Case.Gas.PIn h).T
            { Excess = ex; FlowPerTube = w; ReIn = reIn; HGasPeak = hPeak
              QFluxMax = qMax; ZQMax = zMax; TMetalInMax = tmiMax
              TGasOut = tOut; DNBRMin = dnbMin; DutyTube = duty } ]
