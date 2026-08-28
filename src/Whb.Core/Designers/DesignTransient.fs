namespace Whb.Core

open System
open Types

module DesignTransient =

    let run (case: DesignCase) (sat: Steam.SatProps) (t: TubeGeometry)
            (bpPipeOd: float) (cells: CellResult list) (duty: float) =
        let aMetal = Math.PI / 4.0 * (t.Do * t.Do - t.Di * t.Di)
        let mMetal = 7850.0 * aMetal
        let cMetal = 500.0
        let hEff =
            let c = cells |> List.filter (fun x -> not x.InFerrule) |> List.maxBy (fun x -> x.QFluxOut)
            1.0 / (1.0 / max 1.0 c.HBoil + case.Water.FoulingOut)
        let tau = mMetal * cMetal / (hEff * Math.PI * t.Do)
        let vShell =
            Math.PI / 4.0 * t.ShellId * t.ShellId * t.Length
            - float t.NTubes * Math.PI / 4.0 * t.Do * t.Do * t.Length
            - (if case.Bypass.Enabled then Math.PI / 4.0 * bpPipeOd * bpPipeOd * t.Length else 0.0)
        let alphaMean = cells |> List.averageBy (fun c -> c.Alpha)
        let mWater = vShell * (1.0 - alphaMean) * sat.RhoL
        let tDry = mWater * sat.Hfg / max 1.0 duty
        let mDrum =
            if case.Loop.Drum.Enabled then
                let d0 = case.Loop.Drum
                let rr = 0.5 * d0.ShellId
                let hh = min d0.NormalLevel d0.ShellId
                let th = acos (max -1.0 (min 1.0 ((rr - hh) / rr)))
                let aSeg = rr * rr * (th - sin th * cos th)
                aSeg * d0.Length * sat.RhoL
            else 0.0
        let tDryTot = (mWater + mDrum) * sat.Hfg / max 1.0 duty
        let hSteam = 800.0
        let cHot = cells |> List.maxBy (fun c -> c.TGas)
        let hg = cHot.HConvGas + cHot.HRadGas
        let bore = if cHot.InFerrule then case.Ferrule.Bore else t.Di
        let tEq =
            let rg = 1.0 / (hg * Math.PI * bore) + case.Gas.FoulingIn / (Math.PI * bore)
            let rs = 1.0 / (hSteam * Math.PI * t.Do)
            sat.Tsat + (cHot.TGas - sat.Tsat) * rs / (rg + rs)
        let tauDry = mMetal * cMetal / (hSteam * Math.PI * t.Do)
        { TauMetal = tau
          WaterInventory = mWater
          ShellFreeVolume = vShell
          AlphaMean = alphaMean
          DrumInventory = mDrum
          TimeToDryoutIsolated = tDry
          TimeToDryout = tDryTot
          TMetalDryout = tEq
          TimeToOverheat = 3.0 * tauDry
          MakeupRate = duty / sat.Hfg
          Notes =
            [ "La costante di tempo del metallo e' l'inerzia termica del tubo verso l'acqua: dice in quanto tempo il metallo segue una variazione del gas."
              "Si distinguono DUE scenari. (1) PERDITA DI ACQUA ALIMENTO con circolazione attiva: e' disponibile tutto l'inventario, mantello piu' corpo cilindrico, perche' i downcomer continuano a scendere per gravita'. (2) BLOCCO DELLA CIRCOLAZIONE con downcomer ostruiti: resta il solo inventario del mantello, ed e' il caso severo."
              "La temperatura di equilibrio dopo il dry-out assume raffreddamento per solo vapore con h = 800 W/(m2 K): valore indicativo, il transitorio reale dipende dalla portata residua." ] }
