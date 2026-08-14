namespace Whb.Core

open System
open System.Text
open Constants
open Types

module ReportSizing =

    open ReportCommon

    let section (r: DesignResult) =
        let s = Sizing.evaluate (Sizing.defaultTargets r) r
        let sb = StringBuilder()
        hdr sb "12. Dimensionamento automatico"
        para sb "  " "Questa sezione usa il risultato del calcolo completo per proporre il dimensionamento automatico. L'ordine e' intenzionale: prima vincoli PDS, poi margini termici/idraulici/vibrazionali, poi minimizzazione di peso WHB e lunghezza tubi."
        sb.AppendLine() |> ignore

        sb.AppendLine("  VINCOLI E VERIFICHE") |> ignore
        sb.AppendLine(line) |> ignore
        sb.AppendLine(sprintf "  %-34s %-18s %-18s %-8s %s" "voce" "attuale" "target" "esito" "nota") |> ignore
        sb.AppendLine(line) |> ignore
        for c in s.Checks do
            sb.AppendLine(
                sprintf "  %-34s %-18s %-18s %-8s %s"
                    c.Name c.Current c.Target (if c.Ok then "OK" else "NO") c.Note) |> ignore
        sb.AppendLine(line) |> ignore
        sb.AppendLine() |> ignore

        sb.AppendLine("  OBIETTIVO DI OTTIMIZZAZIONE") |> ignore
        kv sb "Peso WHB stimato" (sprintf "%s t" (f1 (s.WeightEstimateKg / 1000.0)))
        kv sb "Lunghezza tubi" (sprintf "%s m" (f2 s.TubeLength))
        kv sb "Altezza centerline drum-WHB" (sprintf "%s m (min screened %s m)"
                                                (f2 s.Geometry.DrumCenterlineHeight)
                                                (f2 s.Geometry.MinimumDrumCenterlineHeight))
        kv sb "Nozzle / spool raiser minimo" (sprintf "%s / %s mm"
                                                (f0 (1000.0 * s.Geometry.NozzleHeight))
                                                (f0 (1000.0 * s.Geometry.MinimumRiserSpool)))
        kv sb "Peso riser + downcomer" (sprintf "%s t (%s + %s m sviluppati)"
                                            (f2 ((s.Geometry.RiserWeightKg + s.Geometry.DowncomerWeightKg) / 1000.0))
                                            (f1 s.Geometry.RiserDevelopedLength)
                                            (f1 s.Geometry.DowncomerDevelopedLength))
        kv sb "WHB ID x L / drum ID x L" (sprintf "%s / %s m2"
                                            (f2 s.Geometry.WhbIdLength)
                                            (f2 s.Geometry.DrumIdLength))
        para sb "  " s.ObjectiveNote
        para sb "  " s.Geometry.Note
        sb.AppendLine() |> ignore

        sb.AppendLine("  AZIONI DI DIMENSIONAMENTO PROPOSTE") |> ignore
        sb.AppendLine(line) |> ignore
        for a in s.Actions do
            sb.AppendLine(sprintf "  %s" a.Area) |> ignore
            kv sb "  Stato attuale" a.Current
            kv sb "  Richiesto" a.Required
            kv sb "  Beneficio" a.Benefit
            kv sb "  Impatto peso/lunghezza" a.WeightLengthImpact
            kv sb "  Applicabile all'esistente" (if a.FeasibleOnExisting then "SI" else "NO / solo nuovo fascio")
            para sb "    " a.Note
            sb.AppendLine() |> ignore
        sb.AppendLine(line) |> ignore
        sb.AppendLine() |> ignore

        sb.AppendLine("  PARAMETRI DEFAULT") |> ignore
        kv sb "Flusso critico massimo" (sprintf "%s kW/m2" (f0 (s.Targets.MaxQFlux / 1000.0)))
        kv sb "DNBR minimo" (f2 s.Targets.MinDNBR)
        kv sb "Rapporto di circolazione minimo" (f1 s.Targets.MinCirculationRatio)
        kv sb "V/Vcrit massimo" (f2 s.Targets.MaxFeiRatio)
        kv sb "dP gas PDS" (sprintf "%s mbar" (f0 (s.Targets.MaxDpGas / 100.0)))
        kv sb "Altezza nozzle" (sprintf "%s mm" (f0 (1000.0 * s.Targets.NozzleHeight)))
        kv sb "WT steam drum usato nello screening" (sprintf "%s mm" (f1 (1000.0 * s.Targets.DrumWallThickness)))
        kv sb "WT piping riser/downcomer usato nello screening" (sprintf "%s mm" (f1 (1000.0 * s.Targets.PipingWallThickness)))
        sb.AppendLine() |> ignore
        para sb "  " "I valori sono preliminari: ogni modifica geometrica va verificata rilanciando il calcolo completo, perche' potenza, dP gas, circolazione, DNBR e vibrazioni sono accoppiati."
        sb.ToString()

    let text (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine(dline) |> ignore
        sb.AppendLine("WHB / PGC - DIMENSIONAMENTO AUTOMATICO") |> ignore
        sb.AppendLine(sprintf "Caso: %s" r.Case.Name) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine(dline) |> ignore
        sb.Append(section r) |> ignore
        sb.ToString()
