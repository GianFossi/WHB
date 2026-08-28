namespace Whb.Core

open System
open System.Text
open System.Globalization
open Constants

module ReportSulphurCondenser =

    let private ci = CultureInfo.InvariantCulture
    let private f0 (x: float) = x.ToString("F0", ci)
    let private f1 (x: float) = x.ToString("F1", ci)
    let private f2 (x: float) = x.ToString("F2", ci)
    let private f3 (x: float) = x.ToString("F3", ci)
    let private f4 (x: float) = x.ToString("F4", ci)
    let private dline = String('=', 96)

    let text (r: SulphurCondenser.Result) =
        let sb = StringBuilder()
        sb.AppendLine(dline) |> ignore
        sb.AppendLine("CONDENSATORE ZOLFO - REPORT DEDICATO") |> ignore
        sb.AppendLine(sprintf "Sorgente gas: %s" r.SourceLabel) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine(dline) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("NUMERI CHIAVE") |> ignore
        let pOut = r.Segments |> List.tryLast |> Option.map (fun s -> s.POut) |> Option.defaultValue r.FeedUsed.PIn
        sb.AppendLine(sprintf "  Portata gas .............. %s kg/s" (f2 r.FeedUsed.MassFlow)) |> ignore
        sb.AppendLine(sprintf "  T ingresso / uscita ...... %s / %s C" (f1 (kToC r.InletState.T)) (f1 (kToC r.OutletState.T))) |> ignore
        sb.AppendLine(sprintf "  p ingresso / uscita ...... %s / %s bar(a)" (f3 (paToBar r.FeedUsed.PIn)) (f3 (paToBar pOut))) |> ignore
        sb.AppendLine(sprintf "  Duty totale .............. %s MW" (f3 (r.Duty / 1e6))) |> ignore
        sb.AppendLine(sprintf "  di cui latente ........... %s MW" (f3 (r.DutyLatent / 1e6))) |> ignore
        sb.AppendLine(sprintf "  di cui sensibile ......... %s MW" (f3 (r.DutySensible / 1e6))) |> ignore
        sb.AppendLine(sprintf "  Area richiesta ........... %s m2  (U assunto %s W/m2K)" (f1 r.AreaRequired) (f1 r.SpecUsed.UAssumed)) |> ignore
        sb.AppendLine(sprintf "  Frazione condensata out .. %s %% " (f2 (100.0 * r.OutletState.CondensedFraction))) |> ignore
        sb.AppendLine(sprintf "  Zolfo liquido ............ %s kg/h" (f1 (r.CondensedSulphurMassFlow * 3600.0))) |> ignore
        sb.AppendLine(sprintf "  p vapore LP per parete ... %s bar(a) per T parete %s C" (f3 (paToBar r.SteamPressureForWall)) (f1 (kToC r.SpecUsed.TWall))) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("CHECK DI SERVIZIO") |> ignore
        for chk in r.Checks do
            let tag =
                match chk.Severity with
                | Sulphur.Ok -> "OK"
                | Sulphur.Watch -> "ATTENZIONE"
                | Sulphur.Alarm -> "CRITICO"
            sb.AppendLine(sprintf "  [%-10s] %s" tag chk.Title) |> ignore
            sb.AppendLine(sprintf "      valore .. %s" chk.Value) |> ignore
            sb.AppendLine(sprintf "      criterio  %s" chk.Limit) |> ignore
            if chk.Detail <> "" then sb.AppendLine(sprintf "      nota .... %s" chk.Detail) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("PROFILO ASSIALE SEMPLIFICATO") |> ignore
        sb.AppendLine("  i   Tin[C]  Tout[C]  pin[bar] pout[bar] duty[kW] latent[kW] area[m2] cond_out[%] yS_out[%mol]") |> ignore
        for s in r.Segments do
            sb.AppendLine(
                sprintf "  %2d  %7s  %7s  %8s %8s %8s %10s %8s %10s %11s"
                    s.Index
                    (f1 (kToC s.TIn))
                    (f1 (kToC s.TOut))
                    (f3 (paToBar s.PIn))
                    (f3 (paToBar s.POut))
                    (f1 (s.Duty / 1000.0))
                    (f1 (s.DutyLatent / 1000.0))
                    (f2 s.AreaRequired)
                    (f2 (100.0 * s.CondensedFractionOut))
                    (f3 (100.0 * s.YElementalSulphurOut))) |> ignore
        sb.ToString()

    let csvProfile (r: SulphurCondenser.Result) =
        let sb = StringBuilder()
        sb.AppendLine("i;Tin_C;Tout_C;pin_bara;pout_bara;duty_kW;duty_latent_kW;duty_sensible_kW;area_m2;y_sulphur_in_pc;y_sulphur_out_pc;condensed_in_pc;condensed_out_pc;dew_in_C;dew_out_C") |> ignore
        for s in r.Segments do
            let dewIn =
                match s.SulphurDewPointIn with
                | Some t -> f2 (kToC t)
                | None -> ""
            let dewOut =
                match s.SulphurDewPointOut with
                | Some t -> f2 (kToC t)
                | None -> ""
            sb.AppendLine(String.Join(";",
                [ string s.Index
                  f2 (kToC s.TIn)
                  f2 (kToC s.TOut)
                  f4 (paToBar s.PIn)
                  f4 (paToBar s.POut)
                  f3 (s.Duty / 1000.0)
                  f3 (s.DutyLatent / 1000.0)
                  f3 (s.DutySensible / 1000.0)
                  f4 s.AreaRequired
                  f4 (100.0 * s.YElementalSulphurIn)
                  f4 (100.0 * s.YElementalSulphurOut)
                  f4 (100.0 * s.CondensedFractionIn)
                  f4 (100.0 * s.CondensedFractionOut)
                  dewIn
                  dewOut ])) |> ignore
        sb.ToString()
