namespace Whb.Core

open System
open Constants
open Types

module DesignThermalPost =

    type Input =
        { Case: DesignCase
          Sat: Steam.SatProps
          Tube: TubeGeometry
          AreaOut: float
          Comp0: GasProps.Composition
          Cells: CellResult list
          GasPropsAt: CellResult -> GasProps.MixProps
          MixRulePropsAt: GasProps.MixingRule -> CellResult -> GasProps.MixProps }

    type Result =
        { HotCells: CellResult list
          CellDnb: CellResult
          CellQmax: CellResult
          ChfModels: ChfComparison list
          Sensitivity: SensitivityItem list
          FoulingCases: FoulingCase list }

    let run (input: Input) =
        let case = input.Case
        let sat = input.Sat
        let t = input.Tube
        let hotCells = input.Cells |> List.filter (fun c -> not c.InFerrule)
        let cellDnb = hotCells |> List.minBy (fun c -> c.DNBR)
        let cellQmax = hotCells |> List.maxBy (fun c -> c.QFluxOut)
        let dBundle = t.Otl
        let qCritTube1 =
            min (WaterSide.chfHorizontalTube t.Do sat) (WaterSide.chfMostinski sat.P Pc_water)
        let phiB = WaterSide.palenPhiB dBundle t.Length input.AreaOut
        let chfModels =
            let q = cellDnb.QFluxOut
            let ratio = sat.RhoL / sat.RhoV
            let we = sat.RhoV * cellDnb.VelCross ** 2.0 * t.Do / sat.Sigma
            let psi = dBundle * t.Length / input.AreaOut
            let mk (nm: string) (qc: float) (note: string) =
                { Model = nm; QCrit = qc; DNBR = qc / max 1.0 q; Note = note }
            [ mk "Palen - fattore di fascio (BASE DEL CALCOLO)"
                  (phiB * qCritTube1)
                  (sprintf "psi = D_fascio L / A = %.4f darebbe phi_b = 3.1 psi = %.3f, TRONCATO a 0.10 per pratica HEDH. Il troncamento dice che il criterio e' usato FUORI dal suo campo: e' tarato su ribollitori kettle molto piu' piccoli, dove l'unica circolazione e' quella indotta dalle bolle. Qui l'acqua attraversa il fascio a %.1f m/s spinta dalla circolazione naturale. E' quindi un LIMITE INFERIORE, non una previsione." psi (3.1 * psi) cellDnb.VelCross)
              mk "Zuber (idrodinamico su piastra infinita) + derating sul titolo"
                  (WaterSide.chfZuber sat * WaterSide.chfQualityDerating cellDnb.XOut 1.0)
                  "Limite idrodinamico teorico della singola superficie: nessun effetto fascio, nessun effetto di velocita'. E' un LIMITE SUPERIORE di riferimento."
              mk "Lienhard-Dhir (tubo singolo orizzontale) + derating sul titolo"
                  (qCritTube1 * WaterSide.chfQualityDerating cellDnb.XOut 1.0)
                  "Zuber corretto per la curvatura del cilindro. Ancora senza effetto fascio: limite superiore piu' realistico."
              mk "Lienhard-Eichhorn (cilindro in crossflow)"
                  (WaterSide.chfLienhardEichhorn t.Do cellDnb.VelCross sat
                   * WaterSide.chfQualityDerating cellDnb.XOut 1.0)
                  (sprintf "FUORI CAMPO DI VALIDITA': il valore NON va usato. La correlazione e' tarata a bassa pressione, dove rho_l/rho_v vale centinaia; qui vale %.1f con We_D = %.0f. Il gruppo rho_v h_fg u su cui e' costruita esplode ad alta pressione e produce un flusso critico privo di significato fisico. E' riportata solo per documentare che e' stata verificata e scartata." ratio we) ]
        let sensCell = cellQmax
        let chain (hGas: float) (hBoil: float) (rfIn: float) (rfOut: float) (c: CellResult) =
            let bore = if c.InFerrule then case.Ferrule.Bore else t.Di
            let km = case.Material.K (kToC c.TMetalWallAvg)
            let rGas = 1.0 / (max 1.0 hGas * Math.PI * bore)
            let rFi = rfIn / (Math.PI * bore)
            let rM = log (t.Do / t.Di) / (2.0 * Math.PI * km)
            let rFo = rfOut / (Math.PI * t.Do)
            let rB = 1.0 / (max 1.0 hBoil * Math.PI * t.Do)
            let rTot = rGas + rFi + rM + rFo + rB
            let qlin = (c.TGas - sat.Tsat) / rTot
            let qOut = qlin / (Math.PI * t.Do)
            let tmo = sat.Tsat + qlin * (rB + rFo)
            let tmi = tmo + qlin * rM
            (qOut, tmi, tmo, qlin * rFo, qOut / (Math.PI * t.Do) * 0.0 + qOut)
        let hGas0 = sensCell.HConvGas + sensCell.HRadGas
        let hBoil0 = sensCell.HBoil
        let (q0, _, _, _, _) = chain hGas0 hBoil0 case.Gas.FoulingIn case.Water.FoulingOut sensCell
        let sensitivity =
            let pr = input.GasPropsAt sensCell
            let bore = if sensCell.InFerrule then case.Ferrule.Bore else t.Di
            let gasItems =
                [ GasSide.DittusBoelter; GasSide.Colburn; GasSide.SiederTate
                  GasSide.Gnielinski; GasSide.PetukhovKirillov; GasSide.Hausen ]
                |> List.map (fun corr ->
                    let nu = GasSide.nusseltFD corr sensCell.ReGas pr.Pr 1.0
                    let fProp = GasSide.gasPropertyCorrection sensCell.TMetalIn pr.T
                    let ent = GasSide.entranceCorrection sensCell.Z bore case.Gas.EntranceC
                    let h = nu * fProp * ent * pr.K / bore + sensCell.HRadGas
                    let (q, tmi, _, _, _) = chain h hBoil0 case.Gas.FoulingIn case.Water.FoulingOut sensCell
                    { Group = "correlazione lato gas"
                      Name = GasSide.correlationName corr
                      HGas = h; HBoil = hBoil0
                      U = q / (sensCell.TGas - sat.Tsat)
                      QFlux = q; TMetalIn = tmi
                      Delta = 100.0 * (q / q0 - 1.0) })
            let boilItems =
                [ WaterSide.Mostinski; WaterSide.Cooper; WaterSide.Rohsenow
                  WaterSide.Gorenflo; WaterSide.CornwellHouston ]
                |> List.map (fun corr ->
                    let h =
                        WaterSide.hPool corr sensCell.QFluxOut t.Do sat case.Water.RoughnessUm case.Water.Csf
                        * case.Water.BundleFactor
                    let (q, tmi, _, _, _) = chain hGas0 h case.Gas.FoulingIn case.Water.FoulingOut sensCell
                    { Group = "correlazione di ebollizione"
                      Name = WaterSide.poolBoilingName corr
                      HGas = hGas0; HBoil = h
                      U = q / (sensCell.TGas - sat.Tsat)
                      QFlux = q; TMetalIn = tmi
                      Delta = 100.0 * (q / q0 - 1.0) })
            let mixItems =
                [ GasProps.Wilke; GasProps.MolarAverage ]
                |> List.map (fun rule ->
                    let p2 = input.MixRulePropsAt rule sensCell
                    let nu = GasSide.nusseltFD case.Gas.Correlation sensCell.ReGas p2.Pr 1.0
                    let fProp = GasSide.gasPropertyCorrection sensCell.TMetalIn p2.T
                    let ent = GasSide.entranceCorrection sensCell.Z bore case.Gas.EntranceC
                    let h = nu * fProp * ent * p2.K / bore + sensCell.HRadGas
                    let (q, tmi, _, _, _) = chain h hBoil0 case.Gas.FoulingIn case.Water.FoulingOut sensCell
                    { Group = "regola di miscelazione"
                      Name = GasProps.mixingRuleName rule
                      HGas = h; HBoil = hBoil0
                      U = q / (sensCell.TGas - sat.Tsat)
                      QFlux = q; TMetalIn = tmi
                      Delta = 100.0 * (q / q0 - 1.0) })
            gasItems @ boilItems @ mixItems
        let foulingCases =
            [ ("PULITO su entrambi i lati", 0.0, 0.0)
              ("sporco solo lato GAS", case.Gas.FoulingIn, 0.0)
              ("sporco solo lato ACQUA", 0.0, case.Water.FoulingOut)
              ("SPORCO su entrambi i lati (progetto)", case.Gas.FoulingIn, case.Water.FoulingOut) ]
            |> List.map (fun (lab, rfi, rfo) ->
                let (q, tmi, tmo, dDep, _) = chain hGas0 hBoil0 rfi rfo sensCell
                { Label = lab; RfIn = rfi; RfOut = rfo
                  U = q / (sensCell.TGas - sat.Tsat)
                  QFlux = q; TMetalIn = tmi; TMetalOut = tmo
                  DTDeposit = dDep
                  DNBR = sensCell.QCritLocal / max 1.0 q })
        { HotCells = hotCells
          CellDnb = cellDnb
          CellQmax = cellQmax
          ChfModels = chfModels
          Sensitivity = sensitivity
          FoulingCases = foulingCases }
