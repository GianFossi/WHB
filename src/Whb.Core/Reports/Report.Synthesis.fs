namespace Whb.Core

open System
open System.Text
open System.Globalization
open Constants
open Types

module ReportSynthesis =

    open ReportCommon

    let synthesis (r: DesignResult) =
        let c = r.Case
        let sb = StringBuilder()
        let sevTag = function Critical -> "[CRITICO]  " | Warning -> "[ATTENZIONE]" | Note -> "[NOTA]     "
        let rank = function Critical -> 0 | Warning -> 1 | Note -> 2
        sb.AppendLine(dline) |> ignore
        sb.AppendLine("WHB / PGC - SINTESI DELLE CRITICITA'") |> ignore
        sb.AppendLine(sprintf "Caso: %s" c.Name) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine(dline) |> ignore
        sb.AppendLine() |> ignore

        let nC = r.Findings |> List.filter (fun f -> f.Severity = Critical) |> List.length
        let nW = r.Findings |> List.filter (fun f -> f.Severity = Warning) |> List.length
        let nN = r.Findings |> List.filter (fun f -> f.Severity = Note) |> List.length
        sb.AppendLine(sprintf "  ESITO:  %d criticita' | %d attenzioni | %d note" nC nW nN) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  NUMERI CHIAVE") |> ignore
        let hot = r.Cells |> List.filter (fun x -> not x.InFerrule)
        let kv2 k v = sb.AppendLine(sprintf "    %-42s %s" k v) |> ignore
        kv2 "Potenza / vapore" (sprintf "%s MW / %s t/h" (f2 (r.Duty / 1e6)) (f0 (r.SteamProduction * 3.6)))
        kv2 "T gas uscita (media)" (sprintf "%s °C" (f1 (kToC r.TGasOutMean)))
        kv2 "Flusso termico di picco" (sprintf "%s kW/m2" (f0 ((hot |> List.map (fun x -> x.QFluxOut) |> List.max) / 1000.0)))
        kv2 "T metallo massima (interna)" (sprintf "%s °C su limite %s °C" (f0 (kToC (r.Cells |> List.map (fun x -> x.TMetalIn) |> List.max))) (f0 c.Material.TmaxDesign))
        kv2 "DNBR locale minimo (INDICATORE, vedi 5d)"
            (sprintf "%s  - il criterio di riferimento e' 2.0, ma nessun modello di CHF disponibile e' tarato su questa geometria: il valore va usato per confronti relativi, non in assoluto"
                 (f2 (hot |> List.map (fun x -> x.DNBR) |> List.min)))
        kv2 "Rapporto di circolazione" (sprintf "%s  su minimo 10.0" (f1 r.Circulation.CirculationRatio))
        kv2 "Frazione di vuoto massima" (sprintf "%s  su limite 0.70" (f3 (r.Cells |> List.map (fun x -> x.Alpha) |> List.max)))
        kv2 "Dilatazione differenziale tubo-mantello"
            (sprintf "%s mm -> %s MPa compr. nei tubi (utilizzo %s%% a instabilita')"
                 (f2 (r.FixedTubesheet.DeltaFree * 1000.0)) (f1 (r.FixedTubesheet.SigmaTube / 1e6))
                 (f0 (100.0 * r.FixedTubesheet.BucklingUtilisation)))
        kv2 "Carico per tubo sulla giunzione tubo-piastra" (sprintf "%s kN" (f1 (r.FixedTubesheet.ForcePerTube / 1000.0)))
        kv2 "dP lato gas" (sprintf "%s mbar su ammesso 300 mbar" (f0 (r.DpGas / 100.0)))
        if c.Ferrule.Enabled then
            let ferruleLength =
                BundleSolver.ferruleClasses c.Ferrule
                |> List.sumBy (fun (fr, l) -> fr * l)
            let compIn = GasProps.normalize c.Gas.Composition
            let propsIn = GasProps.mixReal c.Gas.MixingRule c.Gas.RealGas compIn c.Gas.TIn c.Gas.PIn c.Gas.Z
            let dpFerrule =
                BundleSolver.ferrulePressureDropEstimate
                    c.Ferrule c.Tube.Di c.Tube.Roughness (c.Gas.MassFlow / float c.Tube.NTubes) propsIn ferruleLength
            let dpShare = 100.0 * dpFerrule / max 1.0 r.DpGas
            let paperThk = BundleSolver.ferruleInsulationThickness c.Ferrule c.Tube.Di
            kv2 "Ferrula: dP stimata / quota dP gas"
                (sprintf "%s mbar per tubo / %s%%" (f2 (dpFerrule / 100.0)) (f1 dpShare))
            kv2 "Ferrula: carta isolante radiale"
                (sprintf "%s mm - %s" (f2 (paperThk * 1000.0)) (BundleSolver.ferruleInsulationFitStatus c.Ferrule c.Tube.Di))
        kv2 "dP circuito acqua/vapore"
            (sprintf "DC %s | riser %s | fascio %s | drum/calm box %s mbar"
                 (f0 (r.Circulation.DpDowncomer / 100.0))
                 (f0 (r.Circulation.DpRiser / 100.0))
                 (f0 (r.Circulation.DpBundle / 100.0))
                 (f0 (r.Circulation.DpNozzles / 100.0)))
        let inventory = inventoryValues c
        kv2 "Inventory acqua / peso metallo"
            (sprintf "%s m3 / %s t"
                 (f1 inventory.TotalWater)
                 (f1 (inventory.TotalMetal / 1000.0)))
        (let vw = r.Vibration |> List.maxBy (fun v -> v.FeiRatio)
         kv2 "VIBRAZIONI - V/Vcrit (istab. fluido-elastica)"
             (sprintf "%s  su limite 0.8   [reticolo %s, K = %s]"
                  (f2 vw.FeiRatio) (Vibration.layoutName c.TubeLayout) (f1 vw.KConnors))
         kv2 "  campata massima ammessa / campata assunta"
             (sprintf "%s m  contro  %s m assunti" (f2 (Vibration.maxSpan 0.8 vw)) (f2 vw.Span)))
        let ws = r.Stress.Cells |> List.maxBy (fun x -> x.Utilisation)
        kv2 "Tensione equivalente massima (Lame' + assiale)"
            (sprintf "%s MPa = %s%% di Sy  (%s, z = %s m%s)"
                 (f0 (ws.SigmaVMMax / 1e6)) (f0 (100.0 * ws.Utilisation)) ws.Component (f2 ws.Z)
                 (if ws.J >= 0 then sprintf ", banda %d" ws.J else ""))
        kv2 "Carico di estremita' da pressione (trazione)"
            (sprintf "%s MN, che compensa la compressione termica" (f2 (r.Stress.PressureEndLoad / 1e6)))
        (match r.Stress.Bucklings |> List.filter (fun b -> b.CollapseUtil > 0.0) with
         | [] -> ()
         | bs ->
            let w = bs |> List.maxBy (fun b -> b.CollapseUtil)
            kv2 "Pressione esterna: caso peggiore"
                (sprintf "%s: %s bar su %s bar di collasso (utilizzo %s%%)"
                     (w.Label.Split(':').[0]) (f1 (w.PExtNet / 1e5)) (f0 (w.PCollapse / 1e5)) (f0 (100.0 * w.CollapseUtil))))
        (match r.Valve with
         | Some v ->
            kv2 "Farfalla del by-pass in esercizio normale"
                (sprintf "%s° di apertura (finestra ammessa %s° - %s°)"
                     (f1 v.Normal.OpenDeg) (f1 v.MinOpen.OpenDeg) (f1 v.MaxOpen.OpenDeg))
            kv2 "By-pass: frazione / dP libero / dP valvola"
                (sprintf "%s%% / %s mbar / %s mbar"
                     (f2 (100.0 * v.Normal.Fraction))
                     (f1 ((v.Normal.DpBypassTot - v.Normal.DpValve) / 100.0))
                     (f1 (v.Normal.DpValve / 100.0)))
            kv2 "  sensibilita' della regolazione"
                (sprintf "%s K di T miscelata per grado di stelo"
                     (f2 (abs (v.MaxOpen.TMixed - v.MinOpen.TMixed) / max 1.0 (v.MaxOpen.OpenDeg - v.MinOpen.OpenDeg))))
         | None -> ())
        let validityWarnings =
            r.Findings
            |> List.filter (fun f -> f.Area.Contains("VALIDITA"))
            |> List.length
        if validityWarnings > 0 then
            kv2 "Warning validita' correlazioni/proprieta'" (sprintf "%d da verificare" validityWarnings)
        (match r.SulphurCondenserResult with
         | Some sc ->
            kv2 "Condensatore zolfo integrato"
                (sprintf "duty %s MW | liquido %s kg/h | area %s m2"
                    (f2 (sc.Duty / 1e6)) (f0 (sc.CondensedSulphurMassFlow * 3600.0)) (f1 sc.AreaRequired))
         | None -> ())
        (match r.LineChecks |> List.filter (fun l -> not l.Connected) with
         | [] -> ()
         | nc -> kv2 "BOCCHELLI NON COLLEGATI" (nc |> List.map (fun l -> l.Tag) |> String.concat ", "))
        sb.AppendLine() |> ignore

        definizioni sb

        if r.Findings.IsEmpty then
            sb.AppendLine("  Nessuna criticita' rilevata dai criteri implementati.") |> ignore
        else
            for f in r.Findings |> List.sortBy (fun f -> (rank f.Severity, f.Area)) do
                sb.AppendLine(dline) |> ignore
                sb.AppendLine(sprintf "%s %s / %s" (sevTag f.Severity) f.Area f.Title) |> ignore
                sb.AppendLine(String('-', 96)) |> ignore
                sb.AppendLine(sprintf "  valore .... %s" f.Value) |> ignore
                sb.AppendLine(sprintf "  criterio .. %s" f.Limit) |> ignore
                sb.AppendLine(sprintf "  DOVE ...... %s" f.Where) |> ignore
                if f.Detail <> "" then
                    sb.AppendLine("  perche' ...") |> ignore
                    para sb "              " f.Detail
                if f.Action <> "" then
                    sb.AppendLine("  AZIONE ....") |> ignore
                    para sb "              " f.Action
                sb.AppendLine() |> ignore
            sb.AppendLine(dline) |> ignore

        sb.AppendLine() |> ignore
        sb.AppendLine("  MAPPA DELLE ZONE CRITICHE (dove guardare sull'apparecchio)") |> ignore
        sb.AppendLine(String('-', 96)) |> ignore
        let qm = hot |> List.maxBy (fun x -> x.QFluxOut)
        let dn = hot |> List.minBy (fun x -> x.DNBR)
        let al = r.Cells |> List.maxBy (fun x -> x.Alpha)
        para sb "  " (sprintf "1) IMBOCCO GAS, subito a valle della ferrula (z = %.2f m su %.2f m totali, cioe' i primi %.0f cm): e' dove cade il picco di flusso termico (%.0f kW/m2) e la temperatura metallica massima. Ispezione boroscopica dei primi 500 mm di tubo e verifica dell'integrita' delle ferrule." qm.Z c.Tube.Length (qm.Z * 100.0) (qm.QFluxOut / 1000.0))
        para sb "  " (sprintf "2) BANDA SUPERIORE DEL FASCIO (y = %+.2f m rispetto all'asse, cioe' i ranghi piu' alti): e' dove il titolo e la frazione di vuoto sono massimi (x = %.3f, alpha = %.2f) e dove cade il DNBR minimo (%.2f a z = %.2f m). E' la zona esposta allo steam blanketing." al.Y al.XOut al.Alpha dn.DNBR dn.Z)
        para sb "  " (sprintf "3) GIUNZIONE TUBO-PIASTRA: carico assiale di %.1f kN per tubo dalla dilatazione impedita, piu' i termini di pressione non inclusi in questo screening. Da verificare con TEMA RCB-7.16 / ASME UHX-13." (r.FixedTubesheet.ForcePerTube / 1000.0))
        para sb "  " (sprintf "4) CIRCUITO DI CIRCOLAZIONE: CR = %.1f contro il minimo di 10. Le perdite si ripartiscono in %.0f mbar sui riser, %.0f sui downcomer, %.0f sul fascio e %.0f sulle interne del corpo cilindrico (quest'ultimo dato e' un'assunzione da confermare col costruttore)." r.Circulation.CirculationRatio (r.Circulation.DpRiser / 100.0) (r.Circulation.DpDowncomer / 100.0) (r.Circulation.DpBundle / 100.0) (r.Circulation.DpNozzles / 100.0))
        sb.AppendLine(String('-', 96)) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("  Il dettaglio completo, con le spiegazioni di ogni grandezza, e' nel report esteso.") |> ignore
        sb.ToString()



