namespace XSulfur

open System
open Ganfoss.ROP
open WhbThermo.Domain

/// Process and integrity checks for a sulfur condenser.
///
/// These were previously scattered between a Whb.Core sulfur module and this
/// library. They live here now, so there is one authority on the numbers that
/// end up in a process alarm.
module Checks =

    type Severity =
        | Ok
        | Watch
        | Alarm

    type Check =
        { Severity  : Severity
          Title     : string
          Actual    : string
          Limit     : string
          Rationale : string }

    let private make severity title actual limit rationale =
        { Severity = severity; Title = title; Actual = actual
          Limit = limit; Rationale = rationale }

    let private toC (t: float<K>) = float t - 273.15

    // ---------- the wall window ----------

    /// Practical floor on the wall temperature.
    ///
    /// The physical limits are 115.2 degC (rhombic) and 119.6 degC (monoclinic);
    /// which form appears depends on thermal history, so the monoclinic value is
    /// the one a control system must respect. 125 degC carries about 5 K of
    /// margin on it, which is the usual design practice.
    [<Literal>]
    let FloorC = 125.0

    /// Watch level below the lambda transition: 4 K of reserve.
    [<Literal>]
    let WatchC = 155.0

    /// The dominant constructional constraint on a sulfur condenser.
    ///
    /// The wall must stay ABOVE the freezing range, or sulfur solidifies on it,
    /// encrusts and blocks the drains; and BELOW the lambda transition at
    /// 159.4 degC, past which the condensate polymerises, the viscosity rises by
    /// orders of magnitude and drainage stops. That narrow window is why sulfur
    /// condensers run on LP steam at 3.5 to 4.5 barg: the steam pressure is the
    /// design handle on the wall temperature.
    let wallWindow (tWall: float<K>) =
        let tC = toC tWall
        if tC < Chemistry.MeltingPointMonoclinicC then
            make Alarm "Parete sotto il punto di fusione dello zolfo"
                 (sprintf "T parete = %.1f C" tC)
                 (sprintf "> %.1f C (fusione monoclino %.1f C)" FloorC Chemistry.MeltingPointMonoclinicC)
                 "Lo zolfo solidifica sulla parete: incrostazione, perdita di scambio e \
                  ostruzione dei drenaggi. Alzare la pressione del vapore LP."
        elif tC < FloorC then
            make Watch "Parete sotto il pavimento pratico"
                 (sprintf "T parete = %.1f C" tC)
                 (sprintf "%.0f-%.0f C" FloorC WatchC)
                 "Sopra la fusione ma senza margine: un transitorio di carico porta la \
                  parete in zona di solidificazione."
        elif tC > Chemistry.LambdaTransitionC then
            make Alarm "Parete oltre la transizione lambda"
                 (sprintf "T parete = %.1f C" tC)
                 (sprintf "< %.1f C" Chemistry.LambdaTransitionC)
                 "Lo zolfo liquido polimerizza e la viscosita' sale di ordini di grandezza \
                  (7 mPa s a 155 C, 4 Pa s a 165 C, massimo 93 Pa s a 187 C): il condensato \
                  non drena piu' e il fascio si intasa. Ridurre la pressione del vapore LP."
        elif tC > WatchC then
            make Watch "Parete vicina alla transizione lambda"
                 (sprintf "T parete = %.1f C" tC)
                 (sprintf "%.0f C con riserva di %.0f K" WatchC (Chemistry.LambdaTransitionC - WatchC))
                 "Il margine sulla transizione lambda e' sotto i 4 K: verificare la banda di \
                  regolazione del vapore LP e i transitori di carico."
        else
            make Ok "Finestra di parete rispettata"
                 (sprintf "T parete = %.1f C" tC)
                 (sprintf "%.0f-%.0f C" FloorC WatchC) ""

    // ---------- condensation state ----------

    let condensationActive (model: Speciation.Model) (tGas: float<K>)
                           (sulfurPressure: float<bar>) : Thermo<Check> =
        Chemistry.dewPoint model sulfurPressure
        >>= fun dew ->
            if tGas <= dew then
                ok (make Ok "Condensazione attiva"
                         (sprintf "T gas = %.0f C, dew point = %.0f C" (toC tGas) (toC dew))
                         "T gas < dew point" "")
            else
                ok (make Watch "Gas sopra il dew point dello zolfo"
                         (sprintf "T gas = %.0f C, dew point = %.0f C" (toC tGas) (toC dew))
                         "T gas < dew point per condensare"
                         "Nessuna condensazione a questo punto: il tratto lavora in solo \
                          raffreddamento sensibile.")

    // ---------- fog ----------

    type FogAssessment =
        { Supersaturation : float
          SlopeRatio      : float
          LewisNumber     : float
          FogLikely       : bool }

    /// Fog risk: sulfur nucleating in the bulk gas instead of on the wall.
    ///
    /// Fogged sulfur escapes the condenser and reappears downstream, and no heat
    /// or mass transfer correlation predicts it - the transfer models all assume
    /// condensation happens at the wall. It needs a separate criterion.
    ///
    /// The three conditions must hold TOGETHER: the bulk is supersaturated, the
    /// cooling path is steeper than the dew point curve, and the Lewis number
    /// exceeds one so heat diffuses to the wall faster than mass does, pushing
    /// the bulk into the two-phase region. In a well-designed condenser the bulk
    /// tracks saturation, because condensation removes sulfur as fast as the gas
    /// cools, and none of the three is met.
    let assessFog (supersaturation: float) (coolingSlope: float) (dewPointSlope: float)
                  (lewisNumber: float) : Thermo<FogAssessment> =
        if dewPointSlope <= 0.0 then
            fail (InvalidMixture "the dew point slope must be positive")
        else
            let ratio = coolingSlope / dewPointSlope
            ok { Supersaturation = supersaturation
                 SlopeRatio = ratio
                 LewisNumber = lewisNumber
                 FogLikely = supersaturation > 1.05 && ratio > 1.0 && lewisNumber > 1.0 }

    let fogCheck (fog: FogAssessment) =
        if fog.FogLikely then
            make Alarm "Rischio di nebbia (fog) di zolfo"
                 (sprintf "supersaturazione = %.2f, rapporto pendenze = %.2f, Le = %.2f"
                          fog.Supersaturation fog.SlopeRatio fog.LewisNumber)
                 "supersaturazione < 1.05 oppure raffreddamento piu' lento della curva di rugiada"
                 "Il gas si raffredda piu' in fretta di quanto scenda il suo dew point: lo zolfo \
                  nuclea in seno al gas invece che sulla parete. La nebbia sfugge al recupero e si \
                  ritrova a valle. Ridurre il gradiente di raffreddamento o prevedere un \
                  demister/coalescer."
        else
            make Ok "Nessun rischio di nebbia"
                 (sprintf "supersaturazione = %.2f, rapporto pendenze = %.2f"
                          fog.Supersaturation fog.SlopeRatio) "-" ""

    // ---------- drainage ----------

    let drainageCheck (verdict: FilmKinetics.DrainageVerdict) =
        match verdict with
        | FilmKinetics.DrainsFreely ->
            make Ok "Drenaggio del condensato regolare" "-" "-" ""
        | FilmKinetics.DrainsMarginally reason ->
            make Watch "Drenaggio del condensato al limite" reason
                 "film sotto la transizione lambda con margine, hold-up < 15 %"
                 "Il condensato drena ma senza riserva: un transitorio puo' fermarlo."
        | FilmKinetics.DoesNotDrain reason ->
            make Alarm "Il condensato non drena" reason
                 "film sotto la transizione lambda, hold-up < 15 %"
                 "Il fascio si intasa: verificare pressione del vapore LP, pendenza dei tubi e \
                  tracciatura di weep line e seal leg."

    // ---------- metallurgy ----------

    /// Sulfidation (Couper-Gorman). Carbon steel in H2S is attacked above about
    /// 260 degC and the rate climbs steeply; on a WHB it is the ferrule that
    /// keeps the real wall near the saturation temperature.
    let sulfidation (tWall: float<K>) (yH2S: float) =
        let tC = toC tWall
        if yH2S <= 1e-4 then
            make Ok "H2S trascurabile" (sprintf "y(H2S) = %.2e" yH2S) "-" ""
        elif tC > 340.0 then
            make Alarm "Sulfidation: parete oltre il limite pratico"
                 (sprintf "T parete = %.0f C, y(H2S) = %.3f" tC yH2S)
                 "< 340 C per acciaio al carbonio"
                 "Il tasso di sulfidation (Couper-Gorman) cresce in modo esponenziale: \
                  verificare integrita' ferrule e valutare 1.25Cr-0.5Mo o rivestimento."
        elif tC > 260.0 then
            make Watch "Sulfidation: parete in campo attivo"
                 (sprintf "T parete = %.0f C, y(H2S) = %.3f" tC yH2S)
                 "260-340 C campo di attacco"
                 "Sopra 260 C l'attacco e' misurabile. Con ferrule integre la parete resta \
                  vicina a Tsat; ogni bypass locale accelera il fenomeno."
        else
            make Ok "Sulfidation entro i limiti" (sprintf "T parete = %.0f C" tC) "< 260 C" ""

    /// Wet H2S damage: the risk exists whenever the metal can fall below the
    /// water dew point while H2S is present, which includes shutdown.
    let wetH2S (tMetal: float<K>) (tWaterDew: float<K>) (yH2S: float) =
        if yH2S > 1e-4 && tMetal <= tWaterDew then
            make Alarm "Rischio wet H2S (HIC/SOHIC/SSC)"
                 (sprintf "T metallo = %.0f C <= dew point acqua %.0f C" (toC tMetal) (toC tWaterDew))
                 "T metallo > dew point acqua in presenza di H2S"
                 "Condensa acida in presenza di H2S: richiede acciaio HIC-resistant \
                  (NACE MR0175 / ISO 15156, prove TM0284), basso tenore di zolfo e durezza \
                  ZTA < 200 HB. Verificare anche le condizioni di fermata."
        else
            make Ok "Nessuna condensa acida con H2S" "-" "-" ""

    // ---------- assembled set ----------

    /// The full check set for one point on a condenser profile.
    let condenserPoint (model: Speciation.Model) (tWall: float<K>) (tGas: float<K>)
                       (sulfurPressure: float<bar>) (yH2S: float)
                       (fog: FogAssessment) (drainage: FilmKinetics.DrainageVerdict)
                       : Thermo<Check list> =
        condensationActive model tGas sulfurPressure
        >>= fun condensing ->
            ok [ wallWindow tWall
                 condensing
                 fogCheck fog
                 drainageCheck drainage
                 sulfidation tWall yH2S ]

    /// Worst severity in a set, for a single go/no-go.
    let worst (checks: Check list) =
        if checks |> List.exists (fun c -> c.Severity = Alarm) then Alarm
        elif checks |> List.exists (fun c -> c.Severity = Watch) then Watch
        else Ok
