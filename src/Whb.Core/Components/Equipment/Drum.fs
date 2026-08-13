namespace Whb.Core

open System
open Constants

/// <summary>
/// Provides drum functionality for the WHB calculation model.
/// </summary>
/// <remarks>
/// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
/// </remarks>
module Drum =

    /// <summary>
    /// Represents internals data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Internals =
        { Enabled: bool
          ShellId: float
          Length: float
          NormalLevel: float

          ConveyorCount: int
          ConvDuctArea: float
          ConvLength: float
          ConvHydDia: float
          ConvBendAngle: float
          ConvBendROverD: float
          ConvOutletArea: float
          ConvOutletAboveLevel: bool
          ConvExtraK: float

          DemisterArea: float
          DemisterK: float
          ChimneyCount: int
          ChimneyId: float
          ChimneyK: float
          ManifoldId: float
          OutletId: float

          ExternalSteam: float
          VendorDpCirculation: float option }

    /// <summary>
    /// Represents item data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Item =
        { Label: string
          K: float
          Area: float
          Velocity: float
          Rho: float
          Dp: float
          Note: string }

    /// <summary>
    /// Represents result data used by the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    type Result =
        { /// Pressure loss along the circulation path [Pa]; included in the balance.
          DpCirculation: float
          DpCirculationNet: float
          CircItems: Item list
          DpSteam: float
          SteamItems: Item list
          DpSubmergence: float
          SurfaceArea: float
          VSurface: float
          VSurfaceMax: float
          SurfaceUtil: float
          VDemister: float
          VDemisterMax: float
          SteamSpaceHeight: float
          Submergence: float
          Notes: string list }

    /// <summary>
    /// Calculates or returns soudersbrown for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let soudersBrown (kSb: float) (rhoL: float) (rhoV: float) =
        kSb * sqrt ((rhoL - rhoV) / rhoV)

    /// <summary>
    /// Calculates or returns dplocaltwophase for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let dpLocalTwoPhase (k: float) (g_: float) (rhoH: float) =
        k * g_ * g_ / (2.0 * rhoH)

    /// <summary>
    /// Calculates or returns chisholmsingularity for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let chisholmSingularity (b: float) (x: float) (rhoL: float) (rhoV: float) =
        1.0 + (rhoL / rhoV - 1.0) * (b * x * (1.0 - x) + x * x)

    /// <summary>
    /// Calculates or returns surfacearea for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let surfaceArea (id_: float) (length: float) (level: float) =
        let r = 0.5 * id_
        let y = level - r                      // Free-surface elevation relative to the axis
        let halfChord = sqrt (max 0.0 (r * r - y * y))
        2.0 * halfChord * length

    /// <summary>
    /// Calculates or returns ductFriction for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let private ductFriction (re: float) (l: float) (dh: float) =
        let f = GasSide.darcyFriction (max 2000.0 re) (5e-5 / dh)
        f * l / dh

    /// <summary>
    /// Calculates or returns solve for the WHB calculation model.
    /// </summary>
    /// <remarks>
    /// Keep this documentation synchronized with the implemented WHB calculation behavior and engineering units.
    /// </remarks>
    let solve (d: Internals) (sat: Steam.SatProps) (wCirc: float) (x: float)
              (wSteam: float) (riserArea: float) (dcArea: float) : Result =
        let rhoH = TwoPhase.homogeneousDensity x sat
        let items = ResizeArray<Item>()
        let add lab k a v rho dp note =
            items.Add { Label = lab; K = k; Area = a; Velocity = v; Rho = rho; Dp = dp; Note = note }

        let nConv = max 1 d.ConveyorCount
        let wPerConv = wCirc / float nConv
        let aNoz = riserArea / float nConv
        let gNoz = wPerConv / aNoz
        let vNoz = gNoz / rhoH
        let headNoz = 0.5 * rhoH * vNoz * vNoz
        let rDuct = aNoz / max 1e-6 d.ConvDuctArea      // Area ratio
        let rWin = aNoz / max 1e-6 d.ConvOutletArea

        let kExp =
            if rDuct < 1.0 then (1.0 - rDuct) ** 2.0     // allargamento (Borda-Carnot)
            else 0.5 * (1.0 - 1.0 / rDuct)               // restringimento
        add "bocchello riser -> canale del convogliatore" kExp aNoz vNoz rhoH (kExp * headNoz)
            "variazione brusca di sezione (Borda-Carnot / Idelchik)"
        let gDuct = wPerConv / d.ConvDuctArea
        let reDuct = gDuct * d.ConvHydDia / sat.MuL
        let kFric = ductFriction reDuct d.ConvLength d.ConvHydDia
        let kBend =
            let th = d.ConvBendAngle
            let a1 =
                if th < 70.0 then 0.9 * sin (th * Math.PI / 180.0)
                elif th <= 100.0 then 1.0
                else 0.7 + 0.35 * th / 90.0
            let rd = max 0.5 d.ConvBendROverD
            a1 * (0.21 / sqrt rd)
        let kDuct = (kFric + kBend + d.ConvExtraK) * rDuct * rDuct
        add "canale del convogliatore (attrito + curvatura)" kDuct d.ConvDuctArea (gDuct / rhoH)
            rhoH (kDuct * headNoz)
            (sprintf "K sul canale %.2f (attrito %.2f + curva %.2f + extra %.2f), riportato alla velocita' del bocchello con (A_noz/A_canale)^2 = %.2f"
                 (kFric + kBend + d.ConvExtraK) kFric kBend d.ConvExtraK (rDuct * rDuct))
        let kWin = 1.0 * rWin * rWin
        add "finestra di scarico del convogliatore" kWin d.ConvOutletArea (wPerConv / (rhoH * d.ConvOutletArea))
            rhoH (kWin * headNoz)
            (if d.ConvOutletAboveLevel then "scarico nello spazio vapore: l'energia cinetica e' persa per intero"
             else "scarico sommerso: si aggiunge il battente di sommergenza")
        add "DEDUZIONE: sbocco gia' contato nella linea del riser" -1.0 aNoz vNoz rhoH (-1.0 * headNoz)
            "evita il doppio conteggio con Piping.totalK"

        let level = d.NormalLevel
        let dpSub =
            if d.ConvOutletAboveLevel then 0.0
            else sat.RhoL * g * (0.5 * level)

        let dpNet = items |> Seq.sumBy (fun i -> i.Dp)
        let dpCirc =
            match d.VendorDpCirculation with
            | Some v -> v
            | None -> max 0.0 dpNet

        let wSteam = wSteam + d.ExternalSteam
        let sItems = ResizeArray<Item>()
        let addS lab k a v rho dp note =
            sItems.Add { Label = lab; K = k; Area = a; Velocity = v; Rho = rho; Dp = dp; Note = note }
        let vDem = wSteam / (sat.RhoV * max 1e-6 d.DemisterArea)
        let dpDem = d.DemisterK * 0.5 * sat.RhoV * vDem * vDem
        addS "demister / separatore secondario" d.DemisterK d.DemisterArea vDem sat.RhoV dpDem
            "K tipico 1-3 per rete metallica pulita, 3-8 per pacco a lamelle"
        let aCh = float (max 1 d.ChimneyCount) * Math.PI * d.ChimneyId * d.ChimneyId / 4.0
        let vCh = wSteam / (sat.RhoV * aCh)
        let dpCh = d.ChimneyK * 0.5 * sat.RhoV * vCh * vCh
        addS (sprintf "camini di uscita (%d x DN %.0f mm)" d.ChimneyCount (d.ChimneyId * 1000.0))
            d.ChimneyK aCh vCh sat.RhoV dpCh "imbocco + attrito + sbocco nel collettore"
        let aMan = Math.PI * d.ManifoldId * d.ManifoldId / 4.0
        let vMan = wSteam / (sat.RhoV * aMan)
        let dpMan = 1.0 * 0.5 * sat.RhoV * vMan * vMan
        addS "collettore sul cielo" 1.0 aMan vMan sat.RhoV dpMan "confluenza dei camini"
        let aOut = Math.PI * d.OutletId * d.OutletId / 4.0
        let vOutS = wSteam / (sat.RhoV * aOut)
        let dpOutS = 0.5 * 0.5 * sat.RhoV * vOutS * vOutS
        addS "bocchello di uscita vapore" 0.5 aOut vOutS sat.RhoV dpOutS ""

        let aSurf = surfaceArea d.ShellId d.Length level
        let vSurf = wSteam / (sat.RhoV * aSurf)
        let vSurfMax = soudersBrown 0.045 sat.RhoL sat.RhoV
        let vDemMax = soudersBrown 0.10 sat.RhoL sat.RhoV
        let steamSpace = d.ShellId - level

        { DpCirculation = dpCirc
          DpCirculationNet = dpNet
          CircItems = List.ofSeq items
          DpSteam = sItems |> Seq.sumBy (fun i -> i.Dp)
          SteamItems = List.ofSeq sItems
          DpSubmergence = dpSub
          SurfaceArea = aSurf
          VSurface = vSurf
          VSurfaceMax = vSurfMax
          SurfaceUtil = vSurf / vSurfMax
          VDemister = vDem
          VDemisterMax = vDemMax
          SteamSpaceHeight = steamSpace
          Submergence = level
          Notes =
            [ "La perdita del percorso VAPORE non entra nel bilancio di circolazione: si scarica sulla pressione consegnata in rete."
              "Il modello per le singolarita' bifase e' OMOGENEO: dp = K G²/(2 rho_H). E' la pratica raccomandata per accidentalita' brusche."
              "Se il costruttore fornisce la curva dp-portata delle interne, usarla: e' l'unico dato veramente affidabile." ] }
