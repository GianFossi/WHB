namespace Whb.Core

open System
open Constants

/// **Corpo cilindrico (steam drum): perdite di carico e verifiche di
/// separazione.**
///
/// Il corpo cilindrico entra nel calcolo di circolazione in TRE modi distinti,
/// che vanno tenuti separati perche' e' l'errore piu' comune:
///
///  1. **termine statico** — il livello dell'acqua fissa la testa della colonna
///     di discesa. Non e' una perdita.
///  2. **perdita sul PERCORSO DI CIRCOLAZIONE** — bocchello riser -> convogliatore
///     -> scarico -> massa d'acqua -> imbocco del downcomer. E' l'unica che
///     entra nel bilancio del battente motore.
///  3. **perdita sul PERCORSO VAPORE** — pelo libero -> demister -> camini ->
///     collettore -> uscita vapore. **NON** influenza la circolazione: si
///     manifesta come differenza fra pressione nel corpo cilindrico e pressione
///     al collettore di rete.
///
/// Nei corpi cilindrici a CICLONI la miscela attraversa il ciclone per intero,
/// quindi tutta la perdita del separatore sta sul percorso di circolazione.
/// Nei corpi cilindrici a CONVOGLIATORI + demister (come questo) la miscela
/// passa solo nel convogliatore, e il demister vede il solo vapore.
module Drum =

    /// Geometria e coefficienti delle interne
    type Internals =
        { Enabled: bool
          /// Diametro interno del corpo cilindrico [m]
          ShellId: float
          /// Lunghezza fra le linee di tangenza [m]
          Length: float
          /// Livello NORMALE misurato dal fondo [m]
          NormalLevel: float

          // ---------- percorso di circolazione: convogliatori ----------
          /// Numero di convogliatori serviti dai riser di QUESTO apparecchio
          ConveyorCount: int
          /// Area di passaggio del canale del convogliatore [m²] (per canale)
          ConvDuctArea: float
          /// Lunghezza sviluppata del canale [m]
          ConvLength: float
          /// Diametro idraulico del canale [m]
          ConvHydDia: float
          /// Angolo complessivo di curvatura del percorso interno [gradi]
          ConvBendAngle: float
          /// Raggio di curvatura / diametro idraulico
          ConvBendROverD: float
          /// Area della finestra di scarico [m²] (per canale)
          ConvOutletArea: float
          /// true = scarica nello spazio vapore (sopra il livello);
          /// false = scarica sommerso nella massa d'acqua
          ConvOutletAboveLevel: bool
          /// K localizzati aggiuntivi sul percorso di circolazione
          ConvExtraK: float

          // ---------- percorso vapore ----------
          /// Area frontale del demister [m²]
          DemisterArea: float
          /// K del demister riferito alla velocita' frontale del vapore
          DemisterK: float
          /// Numero di camini di uscita vapore
          ChimneyCount: int
          /// Diametro interno del camino [m]
          ChimneyId: float
          /// K di un camino (imbocco + attrito + sbocco)
          ChimneyK: float
          /// Diametro interno del collettore sul cielo [m]
          ManifoldId: float
          /// Diametro interno del bocchello di uscita vapore [m]
          OutletId: float

          /// Vapore prodotto da ALTRI apparecchi collegati allo stesso corpo
          /// cilindrico [kg/s]: entra nel percorso vapore e nelle verifiche di
          /// separazione, NON nel percorso di circolazione di questo WHB.
          ExternalSteam: float
          /// Se assegnato, sovrascrive il calcolo con il dato del costruttore [Pa]
          VendorDpCirculation: float option }

    /// Voce di perdita
    type Item =
        { Label: string
          K: float
          Area: float
          Velocity: float
          Rho: float
          Dp: float
          Note: string }

    type Result =
        { /// Perdita sul percorso di circolazione [Pa] - entra nel bilancio
          DpCirculation: float
          /// Somma algebrica prima del troncamento a zero [Pa]
          DpCirculationNet: float
          CircItems: Item list
          /// Perdita sul percorso vapore [Pa] - NON entra nel bilancio
          DpSteam: float
          SteamItems: Item list
          /// Battente statico dello scarico del convogliatore [Pa]
          /// (positivo = contropressione perche' scarica sotto il livello)
          DpSubmergence: float
          // --- verifiche di separazione ---
          /// Area del pelo libero [m²]
          SurfaceArea: float
          /// Velocita' superficiale del vapore al pelo libero [m/s]
          VSurface: float
          /// Limite di Souders-Brown / Wallis [m/s]
          VSurfaceMax: float
          SurfaceUtil: float
          /// Velocita' frontale sul demister [m/s]
          VDemister: float
          VDemisterMax: float
          /// Altezza del vapore sopra il livello [m]
          SteamSpaceHeight: float
          /// Sommergenza dei downcomer sotto il livello [m]
          Submergence: float
          Notes: string list }

    /// Coefficiente di Souders-Brown per separazione gravitazionale a pelo
    /// libero in un corpo cilindrico orizzontale, corretto per la pressione:
    ///   v_max = K_SB * sqrt( (rho_l - rho_v) / rho_v )
    /// K_SB [m/s]: 0.03-0.05 senza interne sopra il pelo libero,
    /// 0.07-0.11 con demister a rete o a lamelle.
    let soudersBrown (kSb: float) (rhoL: float) (rhoV: float) =
        kSb * sqrt ((rhoL - rhoV) / rhoV)

    /// Perdita localizzata bifase con modello OMOGENEO:
    ///   dp = K * G² * v_H / 2       ( = K * 0.5 * rho_H * v_H² )
    /// E' il modello raccomandato per le singolarita' (Collier &amp; Thome,
    /// Idelchik cap. 12): per accidentalita' brusche il vapore e il liquido
    /// non hanno tempo di scorrere l'uno sull'altro.
    let dpLocalTwoPhase (k: float) (g_: float) (rhoH: float) =
        k * g_ * g_ / (2.0 * rhoH)

    /// Moltiplicatore bifase di **Chisholm per singolarita'**, alternativa al
    /// modello omogeneo quando lo scorrimento non e' trascurabile:
    ///   phi² = 1 + (rho_l/rho_v - 1) * ( B x (1-x) + x² )
    /// con B ~ 0.5 per accidentalita' compatte.
    let chisholmSingularity (b: float) (x: float) (rhoL: float) (rhoV: float) =
        1.0 + (rhoL / rhoV - 1.0) * (b * x * (1.0 - x) + x * x)

    /// Area del pelo libero di un cilindro orizzontale con livello h dal fondo
    let surfaceArea (id_: float) (length: float) (level: float) =
        let r = 0.5 * id_
        let y = level - r                      // quota del pelo rispetto all'asse
        let halfChord = sqrt (max 0.0 (r * r - y * y))
        2.0 * halfChord * length

    /// Attrito distribuito nel canale del convogliatore
    let private ductFriction (re: float) (l: float) (dh: float) =
        let f = GasSide.darcyFriction (max 2000.0 re) (5e-5 / dh)
        f * l / dh

    /// Calcolo completo.
    ///   wCirc : portata di circolazione TOTALE del WHB [kg/s]
    ///   x     : titolo all'uscita dei riser
    ///   wSteam: portata di vapore prodotta [kg/s]
    ///   riserArea : area totale di passaggio dei riser [m²]
    ///   dcArea    : area totale di passaggio dei downcomer [m²]
    let solve (d: Internals) (sat: Steam.SatProps) (wCirc: float) (x: float)
              (wSteam: float) (riserArea: float) (dcArea: float) : Result =
        let rhoH = TwoPhase.homogeneousDensity x sat
        let items = ResizeArray<Item>()
        let add lab k a v rho dp note =
            items.Add { Label = lab; K = k; Area = a; Velocity = v; Rho = rho; Dp = dp; Note = note }

        // ---------- percorso di circolazione ----------
        // TUTTO riferito alla velocita' nel bocchello riser, cosi' i K sono
        // confrontabili fra loro e con la letteratura.
        let nConv = max 1 d.ConveyorCount
        let wPerConv = wCirc / float nConv
        let aNoz = riserArea / float nConv
        let gNoz = wPerConv / aNoz
        let vNoz = gNoz / rhoH
        let headNoz = 0.5 * rhoH * vNoz * vNoz
        let rDuct = aNoz / max 1e-6 d.ConvDuctArea      // rapporto di area
        let rWin = aNoz / max 1e-6 d.ConvOutletArea

        // 1) passaggio bocchello -> canale del convogliatore
        let kExp =
            if rDuct < 1.0 then (1.0 - rDuct) ** 2.0     // allargamento (Borda-Carnot)
            else 0.5 * (1.0 - 1.0 / rDuct)               // restringimento
        add "bocchello riser -> canale del convogliatore" kExp aNoz vNoz rhoH (kExp * headNoz)
            "variazione brusca di sezione (Borda-Carnot / Idelchik)"
        // 2) attrito e curvatura nel canale
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
        // 3) finestra di scarico: perdita dell'intera energia cinetica
        let kWin = 1.0 * rWin * rWin
        add "finestra di scarico del convogliatore" kWin d.ConvOutletArea (wPerConv / (rhoH * d.ConvOutletArea))
            rhoH (kWin * headNoz)
            (if d.ConvOutletAboveLevel then "scarico nello spazio vapore: l'energia cinetica e' persa per intero"
             else "scarico sommerso: si aggiunge il battente di sommergenza")
        // 4) sottrazione dello sbocco gia' contato nella linea del riser
        //    (Piping.totalK include uno sbocco K = 1.0 verso un grande volume:
        //     qui il riser NON sbocca in un grande volume ma nel convogliatore,
        //     quindi quel termine va tolto per non contarlo due volte)
        add "DEDUZIONE: sbocco gia' contato nella linea del riser" -1.0 aNoz vNoz rhoH (-1.0 * headNoz)
            "evita il doppio conteggio con Piping.totalK"

        // sommergenza: se il convogliatore scarica sotto il livello, il battente
        // di acqua sopra lo scarico e' una contropressione
        let level = d.NormalLevel
        let dpSub =
            if d.ConvOutletAboveLevel then 0.0
            else sat.RhoL * g * (0.5 * level)

        // Somma algebrica: puo' risultare NEGATIVA, e non e' un errore. Significa
        // che il convogliatore rilascia la miscela piu' lentamente di quanto
        // farebbe uno sbocco nudo, quindi "restituisce" battente rispetto al
        // caso senza interne. Nel bilancio si usa comunque un valore >= 0.
        let dpNet = items |> Seq.sumBy (fun i -> i.Dp)
        let dpCirc =
            match d.VendorDpCirculation with
            | Some v -> v
            | None -> max 0.0 dpNet

        // ---------- percorso vapore ----------
        // vapore TOTALE nel corpo cilindrico: questo WHB piu' gli altri
        // apparecchi collegati (qui 3-E-1801)
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

        // ---------- verifiche di separazione ----------
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
