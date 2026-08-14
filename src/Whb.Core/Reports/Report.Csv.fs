namespace Whb.Core

open System
open System.Text
open System.Globalization
open Constants
open Types

module ReportCsv =

    open ReportCommon

    let csvCells (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("i;j;z_m;y_m;n_tubi;T_gas_C;p_gas_bar;v_gas_ms;Re_gas;h_conv_Wm2K;h_rad_Wm2K;eps_gas;x_in;x_out;alpha;G_cross_kgm2s;v_cross_ms;h_boil_Wm2K;U_o_Wm2K;q_lin_Wm;q_int_kWm2;q_est_kWm2;T_met_int_C;T_met_mid_C;T_met_est_C;T_met_media_spessore_C;T_parete_bollente_C;dT_superheat_K;dT_deposito_K;dT_met_sat_K;q_CHF_loc_kWm2;DNBR;ferrula") |> ignore
        for x in r.Cells do
            sb.AppendLine(String.Join(";",
                [ string x.I; string x.J; f3 x.Z; f4 x.Y; f1 x.NTubes
                  f2 (kToC x.TGas); f4 (paToBar x.PGas); f3 x.VelGas; f0 x.ReGas
                  f1 x.HConvGas; f2 x.HRadGas; f4 x.EpsGas
                  f5 x.XIn; f5 x.XOut; f4 x.Alpha; f2 x.GCross; f3 x.VelCross
                  f1 x.HBoil; f1 x.U_o; f1 x.QLin; f2 (x.QFluxIn / 1000.0); f2 (x.QFluxOut / 1000.0)
                  f2 (kToC x.TMetalIn); f2 (kToC x.TMetalMid); f2 (kToC x.TMetalOut); f2 (kToC x.TMetalWallAvg)
                  f2 (kToC x.TWallBoil); f3 x.DTsatWall; f2 x.DTDeposit; f2 x.DTMetalSat
                  f0 (x.QCritLocal / 1000.0); f2 x.DNBR; (if x.InFerrule then "1" else "0") ])) |> ignore
        sb.ToString()
    let extract (r: DesignResult) (titolo: string) (marker: string) (fine: string) =
        let full = ReportText.text r
        let i = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase)
        let j = full.IndexOf(fine, StringComparison.OrdinalIgnoreCase)
        let body =
            if i < 0 then "(sezione non disponibile)"
            elif j > i then full.Substring(i, j - i)
            else full.Substring(i)
        let sb = StringBuilder()
        sb.AppendLine(dline) |> ignore
        sb.AppendLine(titolo) |> ignore
        sb.AppendLine(sprintf "Caso: %s" r.Case.Name) |> ignore
        sb.AppendLine(sprintf "Data: %s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci))) |> ignore
        sb.AppendLine("Estratto dal report esteso: stessi dati, stesso calcolo, nessuna rielaborazione.") |> ignore
        sb.AppendLine(dline) |> ignore
        definizioni sb
        sb.Append(body) |> ignore
        sb.ToString()
    let maldistributionText (r: DesignResult) =
        extract r "WHB / PGC - MALDISTRIBUZIONE DELLA PORTATA DI GAS FRA I TUBI"
            "6F. MALDISTRIBUZIONE" "6E. TRANSITORI"
    let vibrationText (r: DesignResult) =
        extract r "WHB / PGC - VIBRAZIONI INDOTTE DAL FLUSSO (FIV)"
            "6D. VIBRAZIONI" "6F. MALDISTRIBUZIONE"
    let csvStress (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("componente;i;j;classe;z_m;y_m;T_met_int_C;T_met_est_C;T_met_media_C;dT_spessore_K;p_int_bar;p_est_bar;sZ_membr_MPa;sZ_termico_MPa;sZ_pressione_MPa;punto;r_mm;sigma_R_MPa;sigma_theta_MPa;sigma_Z_MPa;sigma_VM_MPa;sigma_Tresca_MPa;Sy_MPa;utilizzo_pc") |> ignore
        for c in r.Stress.Cells do
            for p in c.Points do
                sb.AppendLine(String.Join(";",
                    [ c.Component; string c.I; string c.J; string c.C; f3 c.Z; f4 c.Y
                      f2 (kToC c.TMetalIn); f2 (kToC c.TMetalOut); f2 (kToC c.TMetalAvg); f2 c.DTWall
                      f3 (paToBar c.PInt); f3 (paToBar c.PExt)
                      f3 (c.SigmaZMembrane / 1e6); f3 (c.SigmaZThermal / 1e6); f3 (c.SigmaZPressure / 1e6)
                      p.Position; f2 (p.R * 1000.0)
                      f3 (p.SigmaR / 1e6); f3 (p.SigmaTheta / 1e6); f3 (p.SigmaZ / 1e6)
                      f3 (p.SigmaVM / 1e6); f3 (p.SigmaTresca / 1e6)
                      f1 (c.Sy / 1e6); f2 (100.0 * p.SigmaVM / c.Sy) ])) |> ignore
        sb.ToString()
    let csvValve (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("apertura_gradi;chiusura_gradi;zeta;frazione_bypass_pc;w_bypass_kgs;rho_kgm3;v_liner_ms;v_vena_ms;Mach;rhov2_vena_Pa;dp_valvola_mbar;dp_bypass_tot_mbar;dp_fascio_mbar;T_out_tubi_C;T_out_bypass_C;T_miscelata_C;potenza_MW;vapore_th;T_liner_max_C;nota") |> ignore
        match r.Valve with
        | None -> ()
        | Some v ->
            for p in v.Sweep do
                let note = valvePositionLabel v.Normal.OpenDeg v.MinOpen.OpenDeg v.MaxOpen.OpenDeg p.OpenDeg
                sb.AppendLine(String.Join(";",
                    [ f2 p.OpenDeg; f2 p.ClosureDeg; f3 p.Zeta; f4 (100.0 * p.Fraction)
                      f4 p.MassFlowBypass; f3 p.RhoValve; f3 p.VelPipe; f2 p.VelThroat; f4 p.Mach
                      f0 p.RhoV2Throat; f2 (p.DpValve / 100.0); f2 (p.DpBypassTot / 100.0)
                      f2 (p.DpTubes / 100.0)
                      f2 (kToC p.TOutTubes); f2 (kToC p.TOutBypass); f2 (kToC p.TMixed)
                      f3 (p.Duty / 1e6); f1 (p.Steam * 3.6); f1 (kToC p.TLinerMax); note ])) |> ignore
        sb.ToString()
    let csvAxial (r: DesignResult) =
        let sb = StringBuilder()
        sb.AppendLine("z_m;T_gas_med_C;T_gas_min_C;T_gas_max_C;q_med_kWm2;q_max_kWm2;T_met_int_max_C;T_met_est_max_C;vapore_lin_kgsm;duty_lin_kWm;w_field_kgsm;w_bypass_kgsm;x_top;alpha_top;G_cross;v_liq_in_ms;v_mix_out_ms;v_vap_out_ms;v_ax_bottom_ms;v_ax_top_ms;DNBR_min;vapore_cum_kgh;duty_cum_MW") |> ignore
        for a in r.Axial do
            sb.AppendLine(String.Join(";",
                [ f3 a.Z; f2 (kToC a.TGasMean); f2 (kToC a.TGasMin); f2 (kToC a.TGasMax)
                  f2 (a.QFluxMean / 1000.0); f2 (a.QFluxMax / 1000.0)
                  f2 (kToC a.TMetalInMax); f2 (kToC a.TMetalOutMax)
                  f4 a.SteamLin; f2 (a.DutyLin / 1000.0); f2 a.WFieldLin; f2 a.WBypassLin
                  f5 a.XTop; f4 a.AlphaTop; f2 a.GCross
                  f4 a.VelLiqIn; f4 a.VelMixOut; f3 a.VelVapOut
                  f4 a.VelAxialBottom; f4 a.VelAxialTop; f2 a.DNBRMin
                  f1 (a.SteamCum * 3600.0); f3 (a.DutyCum / 1e6) ])) |> ignore
        sb.ToString()



