namespace Whb.Core

open System
open Constants
open Types

/// Orchestratore: accoppia il solutore termico 2-D con la rete di
/// circolazione naturale e produce la diagnostica di progetto.
module Design =

    let private w fmt = Printf.kprintf id fmt

    let private sev = function Critical -> "CRITICO" | Warning -> "ATTENZIONE" | Note -> "NOTA"

    /// Punto della mappa parametrica: risposta dell'apparecchio a una data
    /// frazione di by-pass. Una valutazione completa costa qualche secondo,
    /// quindi se ne calcolano poche e si interpola linearmente.
    type private MapPt =
        { X: float
          TMix: float
          TTubes: float
          TBp: float
          DpTubes: float
          DpBpFric: float
          Duty: float
          Steam: float
          TLinerMax: float
          RhoValve: float
          TValve: float }

    let private interpMap (pts: MapPt list) (sel: MapPt -> float) (x: float) =
        let a = pts |> List.toArray
        if x <= a.[0].X then sel a.[0]
        elif x >= a.[a.Length - 1].X then sel a.[a.Length - 1]
        else
            let mutable i = 0
            while i < a.Length - 2 && a.[i + 1].X < x do i <- i + 1
            let f = (x - a.[i].X) / (a.[i + 1].X - a.[i].X)
            sel a.[i] + f * (sel a.[i + 1] - sel a.[i])

    /// inverso di una funzione monotona crescente costruita sulla mappa
    let private invertMap (pts: MapPt list) (sel: MapPt -> float) (target: float) =
        let f (x: float) = interpMap pts sel x - target
        let x0 = (List.head pts).X
        let x1 = (List.last pts).X
        if f x0 >= 0.0 then x0
        elif f x1 <= 0.0 then x1
        else bisect f x0 x1 1e-7 60

    /// Converte le stringhe della diagnostica classica in Finding strutturati e
    /// aggiunge le verifiche meccaniche.
    let buildFindings (case: DesignCase) (sat: Steam.SatProps) (cells: CellResult list)
                      (axial: AxialResult list) (circ: CirculationResult)
                      (ft: FixedTubesheetResult) (risers: RiserCheck list)
                      (expansions: ExpansionResult list) (bp: Bypass.Result option) (dpGas: float)
                      (stress: StressResult) (valve: ValveResult option)
                      (notConnected: Piping.Line list)
                      (vibration: Vibration.Result list) =
        let fs = ResizeArray<Finding>()
        let hot = cells |> List.filter (fun c -> not c.InFerrule)
        let qmax = hot |> List.maxBy (fun c -> c.QFluxOut)
        let dnb = hot |> List.minBy (fun c -> c.DNBR)
        let tmax = cells |> List.maxBy (fun c -> c.TMetalIn)
        let alphaC = cells |> List.maxBy (fun c -> c.Alpha)
        let dTdep = hot |> List.maxBy (fun c -> c.DTDeposit)
        let dTsup = hot |> List.maxBy (fun c -> c.DTsatWall)
        let qCritTube =
            min (WaterSide.chfHorizontalTube case.Tube.Do sat) (WaterSide.chfMostinski sat.P Pc_water)
        let dTc =
            WaterSide.dTcrit case.Water.Correlation qCritTube case.Tube.Do sat
                case.Water.RoughnessUm case.Water.Csf
        let loc (c: CellResult) =
            sprintf "z = %.2f m, y = %+.2f m (banda %d, ferrula %.0f mm)" c.Z c.Y c.J (c.FerruleLen * 1000.0)
        let add s area title value limit where action detail =
            fs.Add { Severity = s; Area = area; Title = title; Value = value
                     Limit = limit; Where = where; Action = action; Detail = detail }

        // --- DNB
        if dnb.DNBR < 1.0 then
            add Critical "EBOLLIZIONE" "Margine su DNB insufficiente"
                (sprintf "DNBR = %.2f" dnb.DNBR) "DNBR >= 2 (pratica di progetto)"
                (loc dnb)
                "Allungare la ferrula, aumentare la circolazione, o ridurre il titolo locale nella banda alta."
                (sprintf "Il flusso locale (%.0f kW/m2) supera il CHF di fascio corretto per il titolo locale x = %.3f. E' il punto in cui il film di vapore puo' staccare l'acqua dalla parete (steam blanketing)." (dnb.QFluxOut / 1000.0) dnb.XOut)
        elif dnb.DNBR < 2.0 then
            add Warning "EBOLLIZIONE" "Margine su DNB ridotto"
                (sprintf "DNBR = %.2f" dnb.DNBR) "DNBR >= 2" (loc dnb)
                "Verificare con criterio di flusso termico massimo; valutare ferrula piu' lunga."
                "Il criterio di Palen usato per il CHF di fascio e' conservativo, ma il margine resta sotto la pratica corrente."

        // --- flusso termico
        if qmax.QFluxOut > 300000.0 then
            add Warning "TERMICO" "Flusso termico di picco elevato"
                (sprintf "%.0f kW/m2" (qmax.QFluxOut / 1000.0)) "250-350 kW/m2 (pratica per WHB a tubi da fumo)"
                (loc qmax)
                "Allungare la ferrula: da 200 a 500 mm il picco cala di circa il 9%."
                "Sopra 300 kW/m2 la sensibilita' a depositi, maldistribuzione e qualita' dell'acqua cresce rapidamente. E' il criterio pratico dominante, piu' del CHF teorico."

        // --- surriscaldamento parete
        if dTsup.DTsatWall > dTc then
            add Critical "EBOLLIZIONE" "Surriscaldamento di parete oltre il dT critico"
                (sprintf "%.1f K" dTsup.DTsatWall) (sprintf "dT critico = %.1f K" dTc)
                (loc dTsup)
                "Ridurre il flusso di picco (ferrula) e aumentare la circolazione."
                "Oltre il ginocchio della curva di ebollizione le bolle si fondono in un film continuo: h crolla e il metallo si scalda di centinaia di gradi in minuti."

        // --- deposito
        if dTdep.DTDeposit > 25.0 then
            add Warning "MATERIALI" "Deposito lato acqua determinante sulla T metallo"
                (sprintf "%.0f K di salto sul deposito" dTdep.DTDeposit)
                (sprintf "Rf assunto = %.1e m2K/W" case.Water.FoulingOut)
                (loc dTdep)
                "Controllo chimico dell'acqua (fosfati/AVT, silice, conducibilita') e pulizia chimica programmata."
                "Il metallo si scalda per il deposito, non per l'ebollizione. Il meccanismo e' autoacceleratore: piu' caldo -> piu' deposito -> piu' caldo."

        // --- temperatura metallo
        let tmC = kToC tmax.TMetalIn
        if tmC > case.Material.TmaxDesign then
            add Critical "MATERIALI" "Temperatura metallo oltre il limite del materiale"
                (sprintf "%.0f °C" tmC) (sprintf "%s: %.0f °C" case.Material.Name case.Material.TmaxDesign)
                (loc tmax) "Cambiare materiale o abbattere il flusso di picco." ""
        elif tmC > 0.92 * case.Material.TmaxDesign then
            add Warning "MATERIALI" "Temperatura metallo vicina al limite"
                (sprintf "%.0f °C" tmC) (sprintf "%s: %.0f °C" case.Material.Name case.Material.TmaxDesign)
                (loc tmax) "Verificare i margini a creep sulla vita di progetto." ""

        // --- metal dusting
        match case.Material.MetalDusting with
        | Some(lo, hi) ->
            let inWin = cells |> List.filter (fun c -> let t = kToC c.TMetalIn in t >= lo && t <= hi)
            let hasCO =
                case.Gas.Composition
                |> List.exists (fun (sp, y) -> (sp = GasProps.CO || sp = GasProps.CH4) && y > 0.005)
            if not inWin.IsEmpty && hasCO then
                let z0 = inWin |> List.map (fun c -> c.Z) |> List.min
                let z1 = inWin |> List.map (fun c -> c.Z) |> List.max
                add Warning "MATERIALI" "Finestra di metal dusting"
                    (sprintf "%.0f%% delle celle" (100.0 * float (List.length inWin) / float (List.length cells)))
                    (sprintf "finestra %.0f-%.0f °C con CO/CH4" lo hi)
                    (sprintf "fra z = %.2f e z = %.2f m" z0 z1)
                    "Valutare alligazione, alluminizzazione o dosaggio di S; tenere la parete fuori finestra."
                    "La carburizzazione catastrofica attacca gli acciai in gas ad alta attivita' di carbonio."
        | None -> ()

        // --- circolazione
        if circ.CirculationRatio < 10.0 then
            add Critical "CIRCOLAZIONE" "Rapporto di circolazione sotto il minimo"
                (sprintf "CR = %.1f (x = %.3f)" circ.CirculationRatio (1.0 / circ.CirculationRatio))
                "CR >= 10, cioe' x <= 0.10"
                (sprintf "intero apparecchio; dislivello drum-WHB %.2f m" case.Loop.DzDrumWhb)
                (sprintf "Aumentare numero/diametro dei riser (pesano %.0f mbar su %.0f di battente) o alzare il drum." (circ.DpRiser / 100.0) (circ.DrivingHead / 100.0))
                "Con CR basso l'acqua che lava i tubi non basta a garantire il margine su DNB e la sommergenza dei ranghi alti."
        elif circ.CirculationRatio < 12.0 then
            add Warning "CIRCOLAZIONE" "Margine ridotto sul rapporto di circolazione"
                (sprintf "CR = %.1f" circ.CirculationRatio) "CR >= 10" "intero apparecchio"
                "Verificare il CR al 50% del carico." "A carico parziale il battente cala piu' delle perdite."

        if circ.VelDowncomer > 3.0 then
            add Warning "CIRCOLAZIONE" "Velocita' elevata nel downcomer"
                (sprintf "%.2f m/s" circ.VelDowncomer) "<= 2-3 m/s" "tubazioni di discesa"
                "Aumentare la sezione dei downcomer." "Rischio di carry-under (bolle trascinate in basso) e di cavitazione all'imbocco."

        if alphaC.Alpha > 0.80 then
            add Critical "EBOLLIZIONE" "Frazione di vuoto eccessiva nella banda alta"
                (sprintf "alpha = %.2f" alphaC.Alpha) "alpha <= 0.70" (loc alphaC)
                "Aumentare la circolazione o ridurre il titolo."
                "In un fascio orizzontale sopra 0.7-0.8 si innesca stratificazione e i ranghi superiori restano scoperti."
        elif alphaC.Alpha > 0.70 then
            add Warning "EBOLLIZIONE" "Frazione di vuoto alta nella banda superiore"
                (sprintf "alpha = %.2f" alphaC.Alpha) "alpha <= 0.70" (loc alphaC)
                "Verificare la sommergenza dei ranghi alti." ""

        let xTopMax = axial |> List.map (fun a -> a.XTop) |> List.max
        if xTopMax > 0.15 then
            let za = axial |> List.maxBy (fun a -> a.XTop)
            add Warning "CIRCOLAZIONE" "Titolo in uscita dal fascio elevato"
                (sprintf "x = %.3f" xTopMax) "x <= 0.10-0.12" (sprintf "z = %.2f m, cielo del fascio" za.Z)
                "Aumentare la circolazione." ""

        if circ.EffectiveCR > 1.25 * circ.CirculationRatio then
            add Note "CIRCOLAZIONE" "Ricircolo interno attraverso la corona anulare"
                (sprintf "CR efficace %.1f contro %.1f esterno" circ.EffectiveCR circ.CirculationRatio)
                (sprintf "corona aperta %.3f m2/m (diaframma OD %.0f mm)" circ.OpenAnnulus (case.Tube.BaffleOd * 1000.0))
                "mantello, corona periferica"
                "Verificare il CR LOCALE nella sezione di picco: il valore medio e' dominato dall'estremita' fredda."
                "La corona lasciata aperta dai diaframmi puo' fare da discesa interna nelle zone fredde e da salita preferenziale in quelle calde."

        // --- riser
        for rc in risers do
            if rc.Regime = Slug then
                add Critical "MECCANICA" "Riser in moto a tappi (slug)"
                    (sprintf "jl = %.2f, jv = %.2f m/s, alpha = %.2f" rc.VelSuperficialLiq rc.VelSuperficialVap rc.Alpha)
                    "regime churn/anulare o a bolle" (sprintf "riser %s" rc.Label)
                    "Piu' riser e piu' piccoli (alza la velocita' verso churn/anulare) oppure abbassare il titolo."
                    "Il moto a tappi pulsa a 0.5-5 Hz ed eccita supporti, bocchelli e piastra tubiera."
            elif rc.RhoV2 > case.MaxRhoV2Riser then
                add Warning "MECCANICA" "rho*v2 elevato nel riser"
                    (sprintf "%.0f kg/(m s2)" rc.RhoV2) (sprintf "<= %.0f" case.MaxRhoV2Riser)
                    (sprintf "riser %s" rc.Label) "Aumentare la sezione." "Erosione ai gomiti e vibrazione indotta."

        // --- meccanica: dilatazione impedita
        if ft.BucklingUtilisation > 1.0 then
            add Critical "MECCANICA" "Instabilita' dei tubi per dilatazione impedita"
                (sprintf "sigma = %.0f MPa (utilizzo %.0f%%)" (ft.SigmaTube / 1e6) (100.0 * ft.BucklingUtilisation))
                (sprintf "ammissibile %.0f MPa (campata %.2f m)" (ft.SigmaBucklingAllow / 1e6) ft.UnsupportedSpan)
                "tutti i tubi, fra due diaframmi"
                "Ridurre il passo dei diaframmi o prevedere un giunto di dilatazione sul mantello."
                "I tubi piu' caldi del mantello vanno in compressione: se la campata e' lunga possono instabilizzarsi."
        elif ft.BucklingUtilisation > 0.5 then
            add Warning "MECCANICA" "Compressione nei tubi da dilatazione impedita"
                (sprintf "sigma = %.0f MPa (utilizzo %.0f%%)" (ft.SigmaTube / 1e6) (100.0 * ft.BucklingUtilisation))
                (sprintf "ammissibile %.0f MPa" (ft.SigmaBucklingAllow / 1e6))
                "tutti i tubi, fra due diaframmi"
                "Verificare con TEMA RCB-7.16 / ASME UHX-13 includendo i termini di pressione." ""
        else
            add Note "MECCANICA" "Dilatazione impedita entro margine"
                (sprintf "sigma tubi %.0f MPa (compr.), mantello %.0f MPa (traz.)" (ft.SigmaTube / 1e6) (ft.SigmaShell / 1e6))
                (sprintf "ammissibile buckling %.0f MPa" (ft.SigmaBucklingAllow / 1e6))
                "fascio e mantello"
                "Confermare con il calcolo di codice, che aggiunge i termini di pressione."
                (sprintf "Dilatazione differenziale libera %.2f mm; forza assiale interna %.2f MN, pari a %.1f kN per tubo sulla giunzione tubo-piastra." (ft.DeltaFree * 1000.0) (ft.Force / 1e6) (ft.ForcePerTube / 1000.0))

        // --- gas
        let velIn = (cells |> List.filter (fun c -> c.I = 0) |> List.maxBy (fun c -> c.VelGas)).VelGas
        if velIn > 60.0 then
            add Warning "GAS" "Velocita' del gas elevata all'imbocco"
                (sprintf "%.1f m/s" velIn) "<= 50-60 m/s" "imbocco tubi"
                "Verificare erosione e vibrazioni indotte." ""
        if dpGas > 0.9 * 0.3e5 then
            add Warning "GAS" "Perdita di carico lato gas vicina all'ammissibile"
                (sprintf "%.0f mbar" (dpGas / 100.0)) "0.30 bar (datasheet)" "intero percorso gas" "" ""

        // --- by-pass interno
        match bp with
        | Some (b: Bypass.Result) ->
            let tl = kToC b.TLinerMax
            let tp = kToC b.TPipeMax
            if tl > case.Bypass.LinerMaterial.TmaxDesign then
                add Critical "MATERIALI" "Liner del by-pass oltre il limite"
                    (sprintf "%.0f °C" tl) (sprintf "%s: %.0f °C" case.Bypass.LinerMaterial.Name case.Bypass.LinerMaterial.TmaxDesign)
                    "tubo di by-pass centrale" "Aumentare lo spessore isolante o cambiare lega." ""
            match case.Bypass.LinerMaterial.MetalDusting with
            | Some(lo, hi) when tl >= lo && tl <= hi ->
                add Note "MATERIALI" "Liner del by-pass in finestra di metal dusting"
                    (sprintf "%.0f °C" tl) (sprintf "finestra %.0f-%.0f °C" lo hi)
                    "tubo di by-pass centrale, tutta la lunghezza"
                    "Confermare la scelta di lega alto Cr-Al (601/602 CA) e il controllo del rapporto S/CO."
                    "Il liner lavora per definizione in piena finestra: e' il motivo per cui si specifica una lega alto Cr-Al invece di un acciaio comune."
            | _ -> ()
            if tp > case.Bypass.PipeMaterial.TmaxDesign then
                add Critical "MATERIALI" "Tubo di contenimento del by-pass troppo caldo"
                    (sprintf "%.0f °C" tp) (sprintf "%s: %.0f °C" case.Bypass.PipeMaterial.Name case.Bypass.PipeMaterial.TmaxDesign)
                    "tubo di by-pass centrale" "Aumentare lo spessore dell'isolante." ""
            else
                add Note "TERMICO" "By-pass interno: isolamento verificato"
                    (sprintf "liner %.0f °C, tubo di contenimento %.0f °C (Tsat %.0f °C)" tl tp (kToC sat.Tsat))
                    (sprintf "salto sull'isolante %.0f K" (b.Nodes |> List.map (fun n -> n.DTInsul) |> List.max))
                    "tubo di by-pass centrale" ""
                    (sprintf "Il tubo di contenimento resta a %.0f K sopra la temperatura dell'acqua: la carta ceramica assorbe tutto il salto. Attraverso l'isolante passano %.0f kW, cioe' il %.2f%% della potenza." (tp - kToC sat.Tsat) (b.HeatLoss / 1000.0) (100.0 * b.HeatLoss / (b.HeatLoss + 1.0)))
            add Warning "GAS" "By-pass da strozzare per la regolazione"
                (sprintf "frazione richiesta %.2f%%" (100.0 * b.Fraction))
                (sprintf "dP del by-pass libero %.1f mbar contro %.1f mbar del fascio" (b.DpBypass / 100.0) (dpGas / 100.0))
                "organo di regolazione del by-pass"
                (sprintf "L'organo di regolazione deve dissipare %.0f mbar. A valvola completamente aperta il by-pass prenderebbe circa il %.0f%% della portata e la temperatura d'uscita salirebbe ben oltre il target." ((dpGas - b.DpBypass) / 100.0) (100.0 * b.Fraction * sqrt (max 1.0 (dpGas / max 1.0 b.DpBypass))))
                "By-pass e fascio sono in parallelo fra gli stessi due punti: senza strozzamento la resistenza del tubo centrale e' troppo bassa."
        | None -> ()

        // --- vibrazioni indotte dal flusso
        (let vw = vibration |> List.maxBy (fun v -> v.FeiRatio)
         let lay = Vibration.layoutName case.TubeLayout
         if vw.FeiRatio >= 1.0 then
            add Critical "VIBRAZIONI" "Instabilita' fluido-elastica: velocita' critica SUPERATA"
                (sprintf "V/Vcrit = %.2f nella banda %d (y = %+.2f m): %.2f m/s contro %.2f m/s critici"
                     vw.FeiRatio vw.Band vw.Y vw.VGap vw.VCrit)
                (sprintf "V/Vcrit <= 0.8 (criterio di progetto); K = %.1f, delta = %.3f" vw.KConnors vw.Delta)
                (sprintf "banda superiore del fascio, campata %.2f m" vw.Span)
                (sprintf "Ridurre la campata a %.2f m (per V/Vcrit = 0.8) oppure %.2f m (per 1.0). Prima di intervenire: procurarsi il calcolo di vibrazione del costruttore e il passo REALE dei diaframmi nella zona alta."
                     (Vibration.maxSpan 0.8 vw) (Vibration.maxSpan 1.0 vw))
                (sprintf "Reticolo %s. E' la configurazione MENO stabile: la costante di Connors in bifase vale %.1f contro 4.0 del triangolare normale, cioe' la velocita' critica e' quasi quattro volte piu' bassa a parita' di tutto il resto. L'ampiezza diverge senza preavviso: non c'e' un ginocchio graduale. La banda superiore e' la stessa in cui cade il DNBR minimo, perche' e' il collo di bottiglia del percorso lato mantello." lay vw.KConnors)
         elif vw.FeiRatio >= 0.8 then
            add Warning "VIBRAZIONI" "Instabilita' fluido-elastica: margine insufficiente"
                (sprintf "V/Vcrit = %.2f nella banda %d" vw.FeiRatio vw.Band)
                "V/Vcrit <= 0.8" (sprintf "campata %.2f m" vw.Span)
                (sprintf "Campata massima ammessa %.2f m." (Vibration.maxSpan 0.8 vw))
                (sprintf "Reticolo %s, K = %.1f, delta = %.3f." lay vw.KConnors vw.Delta)
         else
            add Note "VIBRAZIONI" "Instabilita' fluido-elastica verificata"
                (sprintf "V/Vcrit = %.2f" vw.FeiRatio) "V/Vcrit <= 0.8"
                (sprintf "banda %d, campata %.2f m" vw.Band vw.Span) ""
                (sprintf "Reticolo %s, K = %.1f." lay vw.KConnors))

        // --- bocchelli esistenti ma non collegati
        if not (List.isEmpty notConnected) then
            let tags = notConnected |> List.map (fun l -> sprintf "%s (%s)" l.Tag l.Nps) |> String.concat ", "
            add Warning "CIRCOLAZIONE" "Bocchelli presenti ma NON collegati"
                tags "tutti i bocchelli previsti dovrebbero essere in servizio"
                "mantello, estremita' fredda e calda"
                "Verificare se sono riserve intenzionali. Se lo scopo era il lavaggio delle estremita', il collegamento va realizzato: sono le zone dove il campo tubi e' meno lavato."
                "Il calcolo idraulico e' stato eseguito SENZA queste linee: sezione di passaggio e battente motore sono quelli effettivamente disponibili, non quelli di disegno. R5 e DC9 servivano l'estremita' fredda; R0A/R0B l'estremita' calda, cioe' proprio la zona di picco di flusso termico e di DNBR minimo."

        // --- stato di sollecitazione combinato (Lame' + assiale)
        let worstStress = stress.Cells |> List.maxBy (fun c -> c.Utilisation)
        let sLoc (c: StressCell) =
            if c.J < 0 then sprintf "%s, z = %.2f m" c.Component c.Z
            else sprintf "%s, z = %.2f m, y = %+.2f m (banda %d)" c.Component c.Z c.Y c.J
        if worstStress.Utilisation > 1.0 then
            add Critical "MECCANICO" "Tensione equivalente oltre lo snervamento"
                (sprintf "sigma_VM = %.0f MPa (%.0f %% di Sy)" (worstStress.SigmaVMMax / 1e6) (100.0 * worstStress.Utilisation))
                (sprintf "Sy(T) = %.0f MPa" (worstStress.Sy / 1e6)) (sLoc worstStress)
                "Rivedere spessore del tubo o temperatura di parete."
                "Combinazione di Lame' (pressione esterna prevalente), gradiente termico radiale e carico assiale da dilatazione impedita."
        elif worstStress.Utilisation > 0.66 then
            add Warning "MECCANICO" "Tensione equivalente elevata"
                (sprintf "sigma_VM = %.0f MPa (%.0f %% di Sy)" (worstStress.SigmaVMMax / 1e6) (100.0 * worstStress.Utilisation))
                "indicativo: Pm+Pb+Q entro 2 Sy o 3 Sm (ASME VIII-2)" (sLoc worstStress)
                "Verificare con il calcolo di codice (UHX-13)."
                "Gran parte del valore e' tensione SECONDARIA (gradiente termico + vincolo assiale): non causa collasso, ma governa la fatica termica ai transitori."
        else
            add Note "MECCANICO" "Stato di sollecitazione combinato verificato"
                (sprintf "sigma_VM max = %.0f MPa (%.0f %% di Sy) sulla faccia %s"
                     (worstStress.SigmaVMMax / 1e6) (100.0 * worstStress.Utilisation) worstStress.WorstAt)
                (sprintf "Sy(T) = %.0f MPa" (worstStress.Sy / 1e6)) (sLoc worstStress)
                ""
                (sprintf "Carico di estremita' da pressione %.1f MN (trazione), che compensa in parte la compressione da dilatazione impedita." (stress.PressureEndLoad / 1e6))
        for b in stress.Bucklings do
            if b.Utilisation > 1.0 then
                add Critical "MECCANICO" (sprintf "Instabilita' a compressione: %s" b.Label)
                    (sprintf "sigma = %.0f MPa" (b.SigmaCompression / 1e6))
                    (sprintf "ammissibile %.0f MPa (snellezza %.0f)" (b.SigmaAllow / 1e6) b.Slenderness)
                    b.Label "Ridurre la campata fra diaframmi." ""
            if b.CollapseUtil > 1.0 then
                add Critical "MECCANICO" (sprintf "Pressione esterna: collasso NON verificato su %s" b.Label)
                    (sprintf "p_ext netta = %.1f bar contro %.0f bar di collasso stimato" (b.PExtNet / 1e5) (b.PCollapse / 1e5))
                    "ASME VIII-1 UG-28 con fattore di sicurezza 3 sul collasso"
                    b.Label
                    "Aumentare lo spessore, ridurre la spaziatura degli irrigidimenti, oppure verificare sul disegno lo spessore reale (qui assunto)."
                    (b.Note + ". Il cilindro e' premuto dall'esterno dall'acqua a pressione di corpo cilindrico mentre dentro ha gas a pressione di processo: la differenza e' la pressione di collasso di progetto.")
            elif b.CollapseUtil > 0.5 then
                add Warning "MECCANICO" (sprintf "Pressione esterna: margine ridotto su %s" b.Label)
                    (sprintf "p_ext netta = %.1f bar" (b.PExtNet / 1e5))
                    (sprintf "collasso stimato %.0f bar" (b.PCollapse / 1e5))
                    b.Label "Verificare con ASME VIII-1 UG-28/UG-31." b.Note
        if abs stress.LinerRestrainedForce > 1.0 then
            add Note "MECCANICO" "Liner del by-pass: dilatazione LIBERA (confermato)"
                (sprintf "allungamento libero %.1f mm alla T media equivalente di %.0f °C"
                     (stress.LinerFreeElongation * 1000.0) (kToC stress.LinerTEq))
                "il liner non e' vincolato a entrambe le estremita'"
                "by-pass centrale"
                ""
                (sprintf "Costruttivamente il liner e' libero di dilatare, quindi NON sviluppa carico assiale e non entra nel bilancio a piastre fisse: nel sistema strutturale figura il solo tubo di contenimento. A titolo di documentazione, se fosse vincolato a entrambe le estremita' svilupperebbe %.2f MN, cioe' %.0f MPa: un ordine di grandezza oltre qualunque ammissibile. E' la ragione costruttiva del giunto scorrevole." (stress.LinerRestrainedForce / 1e6) (stress.LinerRestrainedForce / (Math.PI / 4.0 * (case.Bypass.LinerOd ** 2.0 - case.Bypass.LinerId ** 2.0)) / 1e6))

        // --- valvola a farfalla del by-pass
        match valve with
        | Some v ->
            let inWindow = v.Normal.OpenDeg >= v.MinOpen.OpenDeg && v.Normal.OpenDeg <= v.MaxOpen.OpenDeg
            if v.MinOpen.OpenDeg > v.MaxOpen.OpenDeg then
                add Critical "REGOLAZIONE" "Finestra di regolazione della farfalla vuota"
                    (sprintf "min %.1f° > max %.1f°" v.MinOpen.OpenDeg v.MaxOpen.OpenDeg)
                    "min <= normale <= max" "valvola del by-pass"
                    "Ridimensionare il by-pass: diametro del liner o resistenza fissa in serie."
                    "Nessun angolo soddisfa contemporaneamente i criteri di processo e di controllabilita'."
            elif not inWindow then
                add Critical "REGOLAZIONE" "Posizione normale della farfalla fuori finestra"
                    (sprintf "%.1f° di apertura" v.Normal.OpenDeg)
                    (sprintf "finestra ammessa %.1f° - %.1f°" v.MinOpen.OpenDeg v.MaxOpen.OpenDeg)
                    "valvola del by-pass"
                    "Ridurre il diametro del by-pass o inserire una resistenza fissa, per portare il punto di lavoro al centro della corsa."
                    "Con la valvola quasi chiusa la regolazione diventa instabile: piccoli spostamenti dello stelo danno grandi variazioni di portata."
            else
                add Note "REGOLAZIONE" "Farfalla del by-pass entro la finestra utile"
                    (sprintf "%.1f° di apertura in esercizio normale (zeta = %.0f)" v.Normal.OpenDeg v.Normal.Zeta)
                    (sprintf "finestra %.1f° - %.1f°" v.MinOpen.OpenDeg v.MaxOpen.OpenDeg)
                    "valvola del by-pass" ""
                    (sprintf "Guadagno inerente d(ln zeta)/d(theta) = %.2f 1/°: un grado di stelo sposta la temperatura miscelata di circa %.1f K."
                         (Valve.gain v.Normal.OpenDeg)
                         (abs (v.MaxOpen.TMixed - v.MinOpen.TMixed) / max 1.0 (v.MaxOpen.OpenDeg - v.MinOpen.OpenDeg)))
            if v.Normal.Mach > 0.3 then
                add Warning "REGOLAZIONE" "Velocita' elevata nella vena contratta della farfalla"
                    (sprintf "Mach %.2f, v = %.0f m/s" v.Normal.Mach v.Normal.VelThroat)
                    "Mach <= 0.3 per evitare rumore e vibrazione" "valvola del by-pass"
                    "Valutare una valvola multi-stadio o un diaframma in serie." ""
            add Note "REGOLAZIONE" "Posizione di sicurezza della farfalla"
                "CHIUSA in mancanza di aria/segnale" "fail-safe"
                "valvola del by-pass"
                ""
                (sprintf "Chiudendo, la portata va tutta al fascio: la temperatura miscelata scende a %.0f °C (piu' freddo = piu' sicuro per il fascio e per l'apparecchiatura a valle) e il vapore prodotto sale. Aprendo del tutto si arriverebbe a %.0f °C con il %.1f %% di portata deviata."
                     (kToC (v.Sweep |> List.minBy (fun p -> p.OpenDeg)).TMixed)
                     (kToC (v.Sweep |> List.maxBy (fun p -> p.OpenDeg)).TMixed)
                     (100.0 * (v.Sweep |> List.maxBy (fun p -> p.OpenDeg)).Fraction))
        | None -> ()

        if not case.Ferrule.Enabled then
            add Warning "TERMICO" "Nessuna ferrula d'imbocco"
                "assente" "obbligatoria nei WHB syngas" "piastra tubiera lato gas"
                "Prevedere ferrula refrattaria." "Protegge la giunzione tubo/piastra dal picco di flusso d'imbocco."

        List.ofSeq fs

    /// Esecuzione completa con accoppiamento termico <-> idraulico.
    let run (caseIn: DesignCase) : DesignResult =
        // I bocchelli esistenti ma NON collegati (flangia cieca / linea non
        // realizzata) restano in distinta ma non partecipano all'idraulica.
        let allRisers = caseIn.Loop.Risers
        let allDowncomers = caseIn.Loop.Downcomers
        let case =
            { caseIn with
                Loop =
                    { caseIn.Loop with
                        Risers = allRisers |> List.filter (fun l -> l.Connected)
                        Downcomers = allDowncomers |> List.filter (fun l -> l.Connected) } }
        let notConnected =
            (allRisers @ allDowncomers) |> List.filter (fun l -> not l.Connected)
        let sat = Steam.sat case.Water.DrumPressure
        let t = case.Tube
        let bands =
            Bundle.build t.ShellId t.Otl t.Itl t.Pitch t.Do t.NTubes case.NY
                (if case.Bypass.Enabled then case.Bypass.PipeOd else 0.0)
        let nz = max 6 case.NZ
        let comp0 = GasProps.normalize case.Gas.Composition
        let wGasTot = case.Gas.MassFlow
        let dutyGuess = wGasTot * 2.2e3 * (case.Gas.TIn - sat.Tsat - 30.0)
        let steamGuess = dutyGuess / sat.Hfg

        /// risolve il sistema accoppiato fascio + circolazione per un caso dato
        let coupled (cx: DesignCase) =
            let mutable wField = Array.create nz (15.0 * steamGuess / t.Length)
            let mutable xIn = Array.create nz 0.0
            let mutable o = BundleSolver.solve cx bands wField xIn
            let mutable d = Circulation.solve cx sat bands o.SteamLin o.Dz
            for _ in 1 .. 5 do
                wField <- d.WFieldLin
                xIn <- d.XInField
                o <- BundleSolver.solve cx bands wField xIn
                d <- Circulation.solve cx sat bands o.SteamLin o.Dz
            (o, d)

        let caseWith (x: float) =
            { case with Gas = { case.Gas with MassFlow = wGasTot * (1.0 - x) } }

        let tubeOutOf (o: BundleSolver.SolveOutput) =
            let ntb = o.NTubesBand
            let cls = o.Classes |> List.toArray
            let mutable wq = 0.0
            let mutable ta = 0.0
            for j in 0 .. ntb.Length - 1 do
                for c in 0 .. cls.Length - 1 do
                    let wgt = ntb.[j] * fst cls.[c]
                    wq <- wq + wgt
                    ta <- ta + wgt * o.TGasOutBandClass.[j, c]
            ta / wq

        /// dato x, restituisce (T miscelata, out, dist, risultato by-pass)
        let evaluate (x: float) =
            let (o, d) = coupled (caseWith x)
            let tTubes = tubeOutOf o
            if not case.Bypass.Enabled || x <= 1e-6 then
                (tTubes, o, d, None)
            else
                let (nodes, tBp, qBp, dpBp) =
                    Bypass.march case.Bypass comp0 case.Gas.PIn 0.0 case.Gas.TIn
                        case.Gas.MixingRule case.Gas.RealGas case.Gas.ShiftMode sat (wGasTot * x) o.ZC o.Dz
                let hMix =
                    x * GasProps.enthalpyAbsReal case.Gas.RealGas comp0 tBp case.Gas.PIn
                    + (1.0 - x) * GasProps.enthalpyAbsReal case.Gas.RealGas comp0 tTubes case.Gas.PIn
                let tMix =
                    fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas case.Gas.PIn comp0 hMix)
                let res : Bypass.Result =
                    { Fraction = x
                      MassFlow = wGasTot * x
                      TOutBypass = tBp
                      TOutTubes = tTubes
                      TOutMixed = tMix
                      HeatLoss = qBp
                      SteamFromBypass = qBp / sat.Hfg
                      Nodes = nodes
                      TLinerMax = nodes |> List.map (fun n -> n.TLinerIn) |> List.max
                      TPipeMax = nodes |> List.map (fun n -> n.TPipeIn) |> List.max
                      DpBypass = dpBp
                      Converged = true }
                (tMix, o, d, Some res)

        // ------------------------------------------------------------------
        //  MAPPA PARAMETRICA  x -> risposta dell'apparecchio
        //  Serve a tre cose contemporaneamente:
        //   1. centrare la temperatura di uscita miscelata richiesta
        //   2. costruire la caratteristica della valvola a farfalla
        //   3. dare la ripartizione dei flussi per ogni apertura
        // ------------------------------------------------------------------
        let bpSpec = case.Bypass
        let aLiner = Math.PI * bpSpec.LinerId * bpSpec.LinerId / 4.0
        let mapPoint (x: float) =
            let (tm, o, d, bp) = evaluate x
            let (tBp, tLin, rhoV, tV) =
                match bp with
                | Some b ->
                    let n = if bpSpec.ValveAtOutlet then List.last b.Nodes else List.head b.Nodes
                    let pr = GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0 n.TGas case.Gas.PIn 1.0
                    (b.TOutBypass, b.TLinerMax, pr.Rho, n.TGas)
                | None ->
                    // limite a portata nulla: il gas nel by-pass si raffredda
                    // completamente fino alla temperatura dell'acqua
                    let pr = GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0 sat.Tsat case.Gas.PIn 1.0
                    (sat.Tsat, sat.Tsat, pr.Rho, sat.Tsat)
            { X = x
              TMix = tm
              TTubes = (match bp with Some b -> b.TOutTubes | None -> tm)
              TBp = tBp
              DpTubes = o.DpGas
              DpBpFric = (match bp with Some b -> b.DpBypass | None -> 0.0)
              Duty = o.Duty + (match bp with Some b -> b.HeatLoss | None -> 0.0)
              Steam = o.Steam + (match bp with Some b -> b.SteamFromBypass | None -> 0.0)
              TLinerMax = tLin
              RhoValve = rhoV
              TValve = tV }
        let xGrid =
            if not case.Bypass.Enabled then [ 0.0 ]
            else [ 0.0; 0.003; 0.006; 0.010; 0.015; 0.021; 0.030; 0.045; 0.065; 0.090; 0.125; 0.170 ]
        let pmap = xGrid |> List.map mapPoint

        // frazione di by-pass: assegnata, imposta dall'angolo della farfalla,
        // oppure risolta per centrare la temperatura di uscita miscelata
        let qDyn (x: float) =
            // pressione dinamica nel liner alla sezione della valvola
            let rho = max 1e-3 (interpMap pmap (fun p -> p.RhoValve) x)
            let vel = wGasTot * x / (rho * aLiner)
            0.5 * rho * vel * vel
        /// zeta che la valvola deve dissipare perche' i due rami abbiano lo
        /// stesso salto di pressione
        let zetaRequired (x: float) =
            let q = qDyn x
            if q < 1e-9 then 1.0e7
            else
                let dpT = interpMap pmap (fun p -> p.DpTubes) x
                let dpF = interpMap pmap (fun p -> p.DpBpFric) x
                max 0.0 ((dpT - dpF) / q - bpSpec.ExtraK)
        /// frazione che si stabilisce con la farfalla a un dato angolo di
        /// APERTURA: si risolve dp_bypass(x) = dp_fascio(x)
        let fractionForAngle (thetaDeg: float) =
            let z = Valve.zetaOpening thetaDeg
            let res (x: float) =
                let q = qDyn x
                interpMap pmap (fun p -> p.DpBpFric) x + (bpSpec.ExtraK + z) * q
                - interpMap pmap (fun p -> p.DpTubes) x
            let xMax = (List.last pmap).X
            if res xMax < 0.0 then xMax
            else bisect res 1e-6 xMax 1e-8 70
        let angleForFraction (x: float) = Valve.openingForZeta (zetaRequired x)

        let xUsed =
            if not case.Bypass.Enabled then 0.0
            else
                match bpSpec.ValveOpenDeg with
                | Some th -> fractionForAngle th
                | None ->
                    match case.Bypass.Fraction with
                    | Some f -> max 0.0 (min 0.5 f)
                    | None ->
                        // i tubi da soli non raffreddano abbastanza?
                        if (List.head pmap).TMix >= case.Bypass.TargetMixOut then 0.0
                        else
                            // primo tentativo dalla mappa, poi affinamento sul
                            // modello completo
                            let x0 = invertMap pmap (fun p -> p.TMix) case.Bypass.TargetMixOut
                            let lo = max 0.0 (x0 - 0.004)
                            let hi = min 0.35 (x0 + 0.004)
                            let f (x: float) =
                                let (tm, _, _, _) = evaluate x
                                tm - case.Bypass.TargetMixOut
                            if f lo >= 0.0 then lo
                            elif f hi <= 0.0 then hi
                            else bisect f lo hi 1e-5 12

        let (tMixed, out, dist, bpRes) = evaluate xUsed
        let circ = dist.Global
        let axial0 =
            List.mapi (fun i (a: AxialResult) ->
                { a with
                    WFieldLin = dist.WFieldLin.[i]
                    WBypassLin = dist.WBypLin.[i] }) out.Axial
        let nozzles = Nozzles.design case sat axial0 circ
        let ny = List.length bands
        let ncls = List.length out.Classes
        let cells =
            [ for i in 0 .. nz - 1 do
                for j in 0 .. ny - 1 do
                    for c in 0 .. ncls - 1 -> out.Cells.[i, j, c] ]
        let dcPos = case.Loop.Downcomers |> List.map (fun l -> l.ZNozzle) |> List.sort
        let rsPos = case.Loop.Risers |> List.map (fun l -> l.ZNozzle) |> List.sort
        let axial = Circulation.axialVelocities case sat axial0 dist.WExtLin out.Dz dcPos rsPos

        let nT = float t.NTubes
        let areaOut = Math.PI * t.Do * t.Length * nT
        let areaIn = Math.PI * t.Di * t.Length * nT
        let ntb = out.NTubesBand
        let clsArr = out.Classes |> List.toArray
        let mutable tMin = infinity
        let mutable tMax = -infinity
        for j in 0 .. ny - 1 do
            for c in 0 .. ncls - 1 do
                let tv = out.TGasOutBandClass.[j, c]
                tMin <- min tMin tv
                tMax <- max tMax tv
        let tOutMean = tMixed
        let dt1 = case.Gas.TIn - sat.Tsat
        let dt2 = tOutMean - sat.Tsat
        let lm = lmtd dt1 dt2
        // ---------- dilatazioni termiche ----------
        let tRoom = case.AssemblyTemperature
        let segsFor (ci: int) (j: int) =
            [ for i in 0 .. nz - 1 -> (out.Dz.[i], out.Cells.[i, j, ci].TMetalWallAvg) ]
        // dilatazione di OGNI combinazione banda x classe, poi si estraggono
        // gli estremi in termini di ALLUNGAMENTO (non di temperatura di picco)
        let allExp =
            [ for ci in 0 .. ncls - 1 do
                for j in 0 .. ny - 1 ->
                    let e = Mechanics.axialExpansion case.Material tRoom (segsFor ci j)
                    (ci, j, e) ]
        let perClass =
            out.Classes
            |> List.mapi (fun ci (_, fl) ->
                let (_, jj, e) = allExp |> List.filter (fun (c2, _, _) -> c2 = ci) |> List.maxBy (fun (_, _, e) -> e.DeltaL)
                { e with Label = sprintf "Tubi - ferrula %.0f mm, banda %d (dL max)" (fl * 1000.0) jj })
        let coldest =
            let (cc2, jj, e) = allExp |> List.minBy (fun (_, _, e) -> e.DeltaL)
            { e with Label = sprintf "Tubi - banda %d, ferrula %.0f mm (dL MINIMO)" jj (snd (List.item cc2 out.Classes) * 1000.0) }
        // media pesata sul numero di tubi: e' la temperatura da usare per il
        // bilancio globale di dilatazione impedita fascio/mantello
        let meanTube =
            let segs =
                [ for i in 0 .. nz - 1 ->
                    let num =
                        [ for j in 0 .. ny - 1 do
                            for c in 0 .. ncls - 1 ->
                              out.Cells.[i, j, c].TMetalWallAvg * out.Cells.[i, j, c].NTubes ] |> List.sum
                    let den =
                        [ for j in 0 .. ny - 1 do
                            for c in 0 .. ncls - 1 -> out.Cells.[i, j, c].NTubes ] |> List.sum
                    (out.Dz.[i], num / den) ]
            let e = Mechanics.axialExpansion case.Material tRoom segs
            { e with Label = sprintf "Tubi - MEDIA PESATA su tutti i %d tubi" t.NTubes }
        let (tShellMetal, qLoss) =
            Mechanics.shellMetalTemperature sat (cToK 25.0) case.ShellInsulationU
                case.ShellThickness (case.ShellMaterial.K (kToC sat.Tsat)) 20000.0
        let eShell =
            let e = Mechanics.axialExpansion case.ShellMaterial tRoom [ (t.Length, tShellMetal) ]
            { e with Label = "MANTELLO (metallo a contatto con acqua satura)" }
        let dHot = (perClass @ [ coldest ]) |> List.map (fun e -> e.DeltaL) |> List.max
        let dCold = (perClass @ [ coldest ]) |> List.map (fun e -> e.DeltaL) |> List.min
        let expansions =
            perClass @ [ coldest; meanTube; eShell ]
            @ [ { Label = "DIFFERENZIALE tubo medio - mantello"
                  TEquivalent = nan; AlphaMean = nan; Length = t.Length
                  DeltaL = meanTube.DeltaL - eShell.DeltaL }
                { Label = "DIFFERENZIALE tubo piu' caldo - mantello"
                  TEquivalent = nan; AlphaMean = nan; Length = t.Length
                  DeltaL = dHot - eShell.DeltaL }
                { Label = "DIFFERENZIALE fra tubi (piu' caldo - piu' freddo)"
                  TEquivalent = nan; AlphaMean = nan; Length = t.Length
                  DeltaL = dHot - dCold } ]
        let ftRes =
            Mechanics.fixedTubesheet case.Material case.ShellMaterial tRoom t.Length t.NTubes
                t.Do t.Di t.ShellId case.ShellThickness case.UnsupportedSpan
                meanTube.TEquivalent (perClass |> List.maxBy (fun e -> e.DeltaL)).TEquivalent eShell.TEquivalent
        // ==================================================================
        //  VALVOLA A FARFALLA DEL BY-PASS: caratteristica e finestra operativa
        // ==================================================================
        let xTop = (List.last pmap).X
        let valveRes =
            if not case.Bypass.Enabled then None
            else
                let mwMix = GasProps.mixMolarMass comp0
                let velAt (x: float) =
                    let rho = max 1e-3 (interpMap pmap (fun p -> p.RhoValve) x)
                    (rho, wGasTot * x / (rho * aLiner))
                let mkPoint (th: float) (note: string) =
                    let x = fractionForAngle th
                    let z = Valve.zetaOpening th
                    let (rho, vel) = velAt x
                    let q = 0.5 * rho * vel * vel
                    let dpV = z * q
                    let vth = Valve.throatVelocity dpV rho
                    let tv = interpMap pmap (fun p -> p.TValve) x
                    { OpenDeg = th
                      ClosureDeg = 90.0 - th
                      Zeta = z
                      Fraction = x
                      MassFlowBypass = wGasTot * x
                      RhoValve = rho
                      VelPipe = vel
                      VelThroat = vth
                      Mach = vth / Valve.sonic 1.35 mwMix tv
                      RhoV2Throat = rho * vth * vth
                      DpValve = dpV
                      ZetaTheory = Valve.zetaFlatDisc 0.03 (90.0 - th)
                      Cv = Valve.cvFromZeta bpSpec.LinerId z
                      Kv = Valve.kvFromZeta bpSpec.LinerId z
                      KvRequired = Valve.kvRequired (wGasTot * x) rho dpV
                      XRatio = Valve.pressureDropRatio dpV case.Gas.PIn
                      DpBypassTot = interpMap pmap (fun p -> p.DpBpFric) x + (bpSpec.ExtraK + z) * q
                      DpTubes = interpMap pmap (fun p -> p.DpTubes) x
                      TOutTubes = interpMap pmap (fun p -> p.TTubes) x
                      TOutBypass = interpMap pmap (fun p -> p.TBp) x
                      TMixed = interpMap pmap (fun p -> p.TMix) x
                      Duty = interpMap pmap (fun p -> p.Duty) x
                      Steam = interpMap pmap (fun p -> p.Steam) x
                      TLinerMax = interpMap pmap (fun p -> p.TLinerMax) x
                      Note = note }
                // --- angoli imposti dai singoli criteri di progetto ---
                let angFromX (x: float) = max 0.0 (min 90.0 (angleForFraction x))
                let xPurge =
                    let f (x: float) = snd (velAt x) - bpSpec.MinPurgeVel
                    if f 1e-6 >= 0.0 then 1e-6
                    elif f xTop <= 0.0 then xTop
                    else bisect f 1e-6 xTop 1e-9 60
                let xEros =
                    // rho v² in vena contratta = 2 dp_valvola: cala aprendo
                    let f (x: float) =
                        let (rho, vel) = velAt x
                        let q = 0.5 * rho * vel * vel
                        2.0 * max 0.0 (interpMap pmap (fun p -> p.DpTubes) x
                                       - interpMap pmap (fun p -> p.DpBpFric) x
                                       - bpSpec.ExtraK * q) - bpSpec.MaxRhoV2Valve
                    if f 1e-6 <= 0.0 then 1e-6
                    elif f xTop >= 0.0 then xTop
                    else bisect f 1e-6 xTop 1e-9 60
                let xLiner =
                    let lim = cToK bpSpec.LinerMaterial.TmaxDesign
                    if (List.last pmap).TLinerMax <= lim then xTop
                    else invertMap pmap (fun p -> p.TLinerMax) lim
                let minDrivers =
                    [ "controllabilita' meccanica", bpSpec.MinOpenDeg,
                      sprintf "sotto %.0f° di apertura il guadagno d(ln zeta)/d(theta) esplode: la farfalla diventa di fatto on-off" bpSpec.MinOpenDeg
                      "T miscelata minima di processo", angFromX (invertMap pmap (fun p -> p.TMix) bpSpec.TMixMin),
                      sprintf "sotto questo angolo la miscelata scende sotto %.0f °C" (kToC bpSpec.TMixMin)
                      "lavaggio minimo del liner", angFromX xPurge,
                      sprintf "serve almeno %.1f m/s nel liner per non avere un ramo morto (stratificazione, deposito, corrosione sotto deposito)" bpSpec.MinPurgeVel
                      "erosione/rumore in vena contratta", angFromX xEros,
                      sprintf "rho v² nella vena contratta = 2 dp_valvola: chiudendo oltre si supera %.0f Pa" bpSpec.MaxRhoV2Valve ]
                let maxDrivers =
                    [ "autorita' della valvola", bpSpec.MaxOpenDeg,
                      sprintf "oltre %.0f° zeta e' quasi costante: aprendo di piu' non cambia nulla" bpSpec.MaxOpenDeg
                      "T miscelata massima di processo", angFromX (invertMap pmap (fun p -> p.TMix) bpSpec.TMixMax),
                      sprintf "oltre questo angolo la miscelata supera %.0f °C" (kToC bpSpec.TMixMax)
                      "limite metallurgico del liner", angFromX xLiner,
                      sprintf "%s: %.0f °C" bpSpec.LinerMaterial.Name bpSpec.LinerMaterial.TmaxDesign ]
                let thMin = minDrivers |> List.map (fun (_, a, _) -> a) |> List.max
                let thMax = maxDrivers |> List.map (fun (_, a, _) -> a) |> List.min
                let thNorm = angFromX xUsed
                let sweepAngles =
                    ([ 5.0; 10.0; 15.0; 20.0; 25.0; 30.0; 35.0; 40.0; 50.0; 60.0; 70.0; 90.0 ]
                     @ [ thMin; thNorm; thMax ])
                    |> List.map (fun a -> Math.Round(a, 2))
                    |> List.distinct
                    |> List.sort
                Some
                    { Normal = { mkPoint thNorm "ESERCIZIO NORMALE (centra la temperatura di uscita richiesta)" with Fraction = xUsed }
                      MinOpen = mkPoint thMin "APERTURA MINIMA ammessa"
                      MaxOpen = mkPoint thMax "APERTURA MASSIMA ammessa"
                      Sweep = sweepAngles |> List.map (fun a -> mkPoint a "")
                      MinDrivers = minDrivers
                      MaxDrivers = maxDrivers
                      Diameter = bpSpec.LinerId
                      AtOutlet = bpSpec.ValveAtOutlet }

        // ==================================================================
        //  STATO DI SOLLECITAZIONE: Lame' + carico assiale da dilatazione
        //  impedita, per ogni zona z e ogni altezza y
        // ==================================================================
        let pShell = case.Water.DrumPressure
        let pGasMean =
            let s = cells |> List.sumBy (fun c -> c.PGas * c.NTubes)
            let n = cells |> List.sumBy (fun c -> c.NTubes)
            s / n
        let aTubeMetal1 = Math.PI / 4.0 * (t.Do * t.Do - t.Di * t.Di)
        let aFluidTube =
            float t.NTubes * Math.PI / 4.0 * t.Di * t.Di
            + (if case.Bypass.Enabled then Math.PI / 4.0 * bpSpec.InsulOd * bpSpec.InsulOd else 0.0)
        let aFluidShell =
            Math.PI / 4.0 * t.ShellId * t.ShellId
            - float t.NTubes * Math.PI / 4.0 * t.Do * t.Do
            - (if case.Bypass.Enabled then Math.PI / 4.0 * bpSpec.PipeOd * bpSpec.PipeOd else 0.0)
        let pEnd = pShell * aFluidShell + pGasMean * aFluidTube
        // espansione libera del tubo di contenimento e del liner del by-pass
        let bpPipeExp =
            bpRes
            |> Option.map (fun b ->
                let segs =
                    b.Nodes |> List.mapi (fun i n -> (out.Dz.[i], 0.5 * (n.TPipeIn + n.TPipeOut)))
                Mechanics.axialExpansion bpSpec.PipeMaterial tRoom segs)
        let bpLinerExp =
            bpRes
            |> Option.map (fun b ->
                let segs =
                    b.Nodes |> List.mapi (fun i n -> (out.Dz.[i], 0.5 * (n.TLinerIn + n.TLinerOut)))
                Mechanics.axialExpansion bpSpec.LinerMaterial tRoom segs)
        let memberSpecs =
            [ for (ci, j, e) in allExp ->
                let n = out.Cells.[0, j, ci].NTubes
                (sprintf "Tubi banda %d (y = %+.2f m), ferrula %.0f mm" j (List.item j bands).Y
                     (snd (List.item ci out.Classes) * 1000.0),
                 case.Material, n, n * aTubeMetal1, e.TEquivalent, e.DeltaL) ]
            @ [ ("MANTELLO", case.ShellMaterial, 1.0,
                 Math.PI * (t.ShellId + case.ShellThickness) * case.ShellThickness,
                 eShell.TEquivalent, eShell.DeltaL) ]
            @ (match bpPipeExp with
               | Some e ->
                   [ ("BY-PASS - tubo di contenimento", bpSpec.PipeMaterial, 1.0,
                      Math.PI / 4.0 * (bpSpec.PipeOd * bpSpec.PipeOd - bpSpec.InsulOd * bpSpec.InsulOd),
                      e.TEquivalent, e.DeltaL) ]
               | None -> [])
        let (commonDelta, members) =
            Mechanics.restrainedSystem tRoom t.Length pEnd memberSpecs
        let sigmaZof =
            members
            |> List.mapi (fun k m -> (k, m.SigmaZ))
            |> dict
        let nGroups = List.length allExp
        let sigmaTubeGroup = Array2D.create ncls ny 0.0
        allExp
        |> List.iteri (fun k (ci, j, _) -> sigmaTubeGroup.[ci, j] <- sigmaZof.[k])
        let sigmaShellZ = (List.item nGroups members).SigmaZ
        let sigmaBpZ =
            if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZ else 0.0
        // --- celle di tensione: tubi ---
        let stressTubes =
            [ for ci in 0 .. ncls - 1 do
                for j in 0 .. ny - 1 do
                    for i in 0 .. nz - 1 ->
                        let c = out.Cells.[i, j, ci]
                        let tAvg = kToC c.TMetalWallAvg
                        let dT = c.TMetalIn - c.TMetalOut
                        let pts =
                            Mechanics.stressPoints c.PGas pShell (t.Di / 2.0) (t.Do / 2.0)
                                sigmaTubeGroup.[ci, j] (case.Material.Alpha tAvg)
                                (case.Material.E tAvg) dT
                        let worst = pts |> List.maxBy (fun p -> p.SigmaVM)
                        let sy = case.Material.Sy tAvg
                        { Component = "TUBI"
                          I = i; J = j; C = ci
                          Z = c.Z; Y = c.Y
                          TMetalIn = c.TMetalIn; TMetalOut = c.TMetalOut
                          TMetalAvg = c.TMetalWallAvg; DTWall = dT
                          PInt = c.PGas; PExt = pShell
                          SigmaZMembrane = sigmaTubeGroup.[ci, j]
                          SigmaZThermal = (List.item (ci * ny + j) members).SigmaZThermal
                          SigmaZPressure = (List.item (ci * ny + j) members).SigmaZPressure
                          Points = pts
                          SigmaVMMax = worst.SigmaVM
                          WorstAt = worst.Position
                          Sy = sy
                          Utilisation = worst.SigmaVM / sy } ]
        // --- celle di tensione: tubo di contenimento del by-pass ---
        let stressBypass =
            match bpRes with
            | None -> []
            | Some b ->
                b.Nodes
                |> List.mapi (fun i n ->
                    let tAvg = kToC (0.5 * (n.TPipeIn + n.TPipeOut))
                    let dT = n.TPipeIn - n.TPipeOut
                    let pInt = case.Gas.PIn
                    let pts =
                        Mechanics.stressPoints pInt pShell (bpSpec.InsulOd / 2.0) (bpSpec.PipeOd / 2.0)
                            sigmaBpZ (bpSpec.PipeMaterial.Alpha tAvg) (bpSpec.PipeMaterial.E tAvg) dT
                    let worst = pts |> List.maxBy (fun p -> p.SigmaVM)
                    let sy = bpSpec.PipeMaterial.Sy tAvg
                    { Component = "BY-PASS"
                      I = i; J = -1; C = -1
                      Z = n.Z; Y = 0.0
                      TMetalIn = n.TPipeIn; TMetalOut = n.TPipeOut
                      TMetalAvg = cToK tAvg; DTWall = dT
                      PInt = pInt; PExt = pShell
                      SigmaZMembrane = sigmaBpZ
                      SigmaZThermal = (if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZThermal else 0.0)
                      SigmaZPressure = (if List.length members > nGroups + 1 then (List.item (nGroups + 1) members).SigmaZPressure else 0.0)
                      Points = pts
                      SigmaVMMax = worst.SigmaVM
                      WorstAt = worst.Position
                      Sy = sy
                      Utilisation = worst.SigmaVM / sy })
        let pExtNetTube = pShell - pGasMean
        // Il sistema e' LINEARE: la soluzione senza carico di pressione e'
        // esattamente la quota "termica" gia' separata. Si verificano quindi
        // due condizioni di carico:
        //   LC1 esercizio        = termico + carico di estremita' di pressione
        //   LC2 termico puro     = apparecchio caldo ma non in pressione
        //                          (avviamento, depressurizzazione a caldo):
        //                          e' il caso severo per l'instabilita'
        let mTubeWorstOp = [ 0 .. nGroups - 1 ] |> List.minBy (fun k -> (List.item k members).SigmaZ) |> fun k -> List.item k members
        let mTubeWorstTh = [ 0 .. nGroups - 1 ] |> List.minBy (fun k -> (List.item k members).SigmaZThermal) |> fun k -> List.item k members
        let bucklings =
            [ Mechanics.bucklingCheck
                  (sprintf "TUBI - LC1 esercizio (termico + pressione): %s" mTubeWorstOp.Label)
                  case.Material mTubeWorstOp.TEq t.Do t.Di case.UnsupportedSpan
                  mTubeWorstOp.SigmaZ pExtNetTube
              Mechanics.bucklingCheck
                  (sprintf "TUBI - LC2 caldo NON in pressione: %s" mTubeWorstTh.Label)
                  case.Material mTubeWorstTh.TEq t.Do t.Di case.UnsupportedSpan
                  mTubeWorstTh.SigmaZThermal 0.0 ]
            @ (match bpPipeExp with
               | Some e ->
                   let mb = List.item (nGroups + 1) members
                   [ Mechanics.bucklingCheck "BY-PASS tubo di contenimento - LC1 esercizio"
                         bpSpec.PipeMaterial e.TEquivalent bpSpec.PipeOd bpSpec.InsulOd
                         case.UnsupportedSpan mb.SigmaZ (pShell - case.Gas.PIn)
                     Mechanics.bucklingCheck "BY-PASS tubo di contenimento - LC2 caldo non in pressione"
                         bpSpec.PipeMaterial e.TEquivalent bpSpec.PipeOd bpSpec.InsulOd
                         case.UnsupportedSpan mb.SigmaZThermal 0.0 ]
               | None -> [])
            @ (match bpLinerExp with
               | Some e ->
                   [ Mechanics.bucklingCheck "BY-PASS liner - IPOTESI NON APPLICABILE (il liner e' libero)"
                         bpSpec.LinerMaterial e.TEquivalent bpSpec.LinerOd bpSpec.LinerId
                         case.UnsupportedSpan
                         (-(bpSpec.LinerMaterial.E (kToC e.TEquivalent)) * e.DeltaL / t.Length) 0.0 ]
               | None -> [])
        let stressRes =
            { CommonDelta = commonDelta
              PressureEndLoad = pEnd
              AreaFluidShell = aFluidShell
              AreaFluidTube = aFluidTube
              PShell = pShell
              PTubeMean = pGasMean
              Members = members
              Cells = stressTubes @ stressBypass
              Bucklings = bucklings
              LinerRestrainedForce =
                (match bpLinerExp with
                 | Some e ->
                     let a = Math.PI / 4.0 * (bpSpec.LinerOd * bpSpec.LinerOd - bpSpec.LinerId * bpSpec.LinerId)
                     a * bpSpec.LinerMaterial.E (kToC e.TEquivalent) * e.DeltaL / t.Length
                 | None -> 0.0)
              LinerTEq = (match bpLinerExp with Some e -> e.TEquivalent | None -> nan)
              LinerFreeElongation = (match bpLinerExp with Some e -> e.DeltaL | None -> nan)
              Liner =
                (let tEq = match bpLinerExp with Some e -> e.TEquivalent | None -> cToK 700.0
                 let tC = kToC tEq
                 let ee = bpSpec.LinerMaterial.E tC
                 let sy = bpSpec.LinerMaterial.Sy tC
                 let od = bpSpec.LinerOd
                 let idl = bpSpec.LinerId
                 let thk = 0.5 * (od - idl)
                 let dm = 0.5 * (od + idl)
                 // Il liner NON porta la pressione di processo: l'intercapedine
                 // fra liner e tubo di contenimento comunica con il lato a
                 // VALLE del fascio. Il salto e' quindi la sola perdita di
                 // carico dei tubi, maggiorata per gli scostamenti d'esercizio.
                 let factor = 2.0
                 let dpDes = factor * out.DpGas
                 let pE = 2.0 * ee / (1.0 - Mechanics.nu * Mechanics.nu) * (thk / dm) ** 3.0
                 let pY = 2.0 * sy * thk / od
                 let pC = 1.0 / sqrt (1.0 / (pE * pE) + 1.0 / (pY * pY))
                 { DpTubes = out.DpGas; DpDesign = dpDes; Factor = factor
                   Od = od; Id = idl; Thickness = thk; TEq = tEq; E = ee; Sy = sy
                   PCrElastic = pE; PCrYield = pY; PCollapse = pC
                   Utilisation = dpDes / pC
                   UtilisationCode = 3.0 * dpDes / pC
                   HoopStress = dpDes * dm / (2.0 * thk)
                   Notes =
                     [ "Il liner e' LIBERO di dilatare e NON e' un componente in pressione: separa due volumi di gas che stanno quasi alla stessa pressione."
                       "Il salto lo genera solo la perdita di carico del fascio, perche' l'intercapedine esterna e' in comunicazione con il lato a valle dei tubi."
                       "Il verso puo' invertirsi secondo la posizione della valvola e i transitori, quindi si verifica il caso PIU' severo, cioe' la pressione esterna."
                       "Cilindro lungo non irrigidito: la carta refrattaria riempie l'intercapedine ma e' cedevole e non viene conteggiata come supporto." ] })
              Notes =
                [ "Trazione positiva in tutte le tensioni."
                  "Le tensioni di Lame' sono PRIMARIE (equilibrio con la pressione); quelle da gradiente termico radiale e da dilatazione impedita sono SECONDARIE (autoequilibrate)."
                  "Verifica di screening: il calcolo di codice (ASME VIII-1 UHX-13 / TEMA RCB-7) aggiunge la flessibilita' della piastra tubiera e i limiti differenziati Pm / Pm+Pb / Pm+Pb+Q."
                  "Il LINER del by-pass e' libero di dilatare (dato costruttivo confermato): non sviluppa carico assiale e non figura fra i membri del sistema a piastre fisse. La riga di instabilita' che lo riguarda e' una verifica IPOTETICA, riportata solo per documentare l'ordine di grandezza della forza che il giunto scorrevole evita."
                  "Pressione esterna: i diaframmi di supporto lavorano come anelli di irrigidimento. Il gioco foro/tubo e' di 0.40 mm sul diametro (0.20 mm radiali), cioe' lo 0.5 % del raggio: il vincolo radiale e' effettivo e l'ipotesi e' confermata."
                  "Il carico di estremita' di pressione presuppone l'apparecchio CHIUSO alle due estremita' e privo di giunto di dilatazione sul mantello." ] }

        // ==================================================================
        //  A) CONFRONTO FRA MODELLI DI FLUSSO CRITICO (CHF)
        //  B) STUDIO DI INCERTEZZA SULLE CORRELAZIONI
        //  C) CONFRONTO PULITO / SPORCO SUI DUE LATI
        //  Tutti valutati sulle celle governanti, a partire dai risultati gia'
        //  calcolati: non richiedono un nuovo giro del solutore.
        // ==================================================================
        let hotCells = cells |> List.filter (fun c -> not c.InFerrule)
        let cellDnb = hotCells |> List.minBy (fun c -> c.DNBR)
        let cellQmax = hotCells |> List.maxBy (fun c -> c.QFluxOut)
        let dBundle = t.Otl
        let qCritTube1 =
            min (WaterSide.chfHorizontalTube t.Do sat) (WaterSide.chfMostinski sat.P Pc_water)
        let phiB = WaterSide.palenPhiB dBundle t.Length areaOut
        let chfModels =
            let q = cellDnb.QFluxOut
            let ratio = sat.RhoL / sat.RhoV
            let we = sat.RhoV * cellDnb.VelCross ** 2.0 * t.Do / sat.Sigma
            let psi = dBundle * t.Length / areaOut
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
        // --- incertezza sulle correlazioni, valutata nella cella di picco ---
        let sensCell = cellQmax
        let propsAt (c: CellResult) =
            GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0 c.TGas c.PGas case.Gas.Z
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
        let (q0, tmi0, _, _, _) = chain hGas0 hBoil0 case.Gas.FoulingIn case.Water.FoulingOut sensCell
        let sensitivity =
            let pr = propsAt sensCell
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
                    let p2 = GasProps.mixReal rule case.Gas.RealGas comp0 sensCell.TGas sensCell.PGas case.Gas.Z
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
        // --- pulito / sporco sui due lati (locale, nella cella di picco) ---
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

        // ==================================================================
        //  VIBRAZIONI INDOTTE DAL FLUSSO (FIV) e TRANSITORI
        // ==================================================================
        // Campate reali: si costruiscono gli intervalli assiali [z0, z1] di
        // ciascuna campata libera, tenendo conto dello spessore dei diaframmi.
        let spanRanges =
            if List.isEmpty case.BaffleSpans then
                [ (0.0, t.Length, case.UnsupportedSpan) ]
            else
                let mutable z = 0.0
                [ for sp in case.BaffleSpans ->
                    let z0 = z
                    let z1 = z + sp
                    z <- z1 + case.BaffleThickness
                    (z0, z1, sp) ]
        // Per ogni CAMPATA e ogni BANDA: la velocita' di crossflow che conta e'
        // quella locale dentro quella campata, non il massimo su tutto l'asse.
        // La combinazione peggiore e' campata lunga + velocita' alta, e qui il
        // disegno la mette proprio all'estremita' calda.
        let nSpans = List.length spanRanges
        let vibrationAll =
            [ for (si, (z0, z1, sp)) in List.indexed spanRanges do
                // Ai DIAFRAMMI il tubo e' un semplice nodo (spostamento
                // laterale impedito, rotazione libera). Solo alle piastre
                // tubiere il vincolo puo' essere un incastro, e solo se la
                // saldatura e' a piena penetrazione.
                let clamped =
                    let atTubesheet = (si = 0) || (si = nSpans - 1)
                    if atTubesheet && case.TubesheetJoint = Vibration.FullPenetrationWeld then 1 else 0
                let lam = Vibration.lambda2Of clamped
                for j in 0 .. ny - 1 do
                    let inSpan =
                        cells |> List.filter (fun c -> c.J = j && c.Z >= z0 && c.Z <= z1)
                    if not inSpan.IsEmpty then
                        let w = inSpan |> List.maxBy (fun c -> c.VelCross)
                        let rhoH = TwoPhase.homogeneousDensity w.XOut sat
                        let rhoGas =
                            (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0 w.TGas w.PGas case.Gas.Z).Rho
                        yield
                            Vibration.check j w.Y sp lam case.TubeLayout case.VibrationDamping
                                t.Do t.Di t.Pitch (case.Material.E (kToC w.TMetalWallAvg)) 7850.0
                                w.VelCross rhoH rhoGas ]
        // per il report per banda si tiene la campata peggiore di ogni banda
        let vibration =
            [ for j in 0 .. ny - 1 ->
                vibrationAll |> List.filter (fun v -> v.Band = j) |> List.maxBy (fun v -> v.FeiRatio) ]
        // ==================================================================
        //  MALDISTRIBUZIONE DELLA PORTATA DI GAS FRA I TUBI
        //  Un SOLO tubo riceve piu' portata degli altri. Il lato mantello NON
        //  cambia: gli 848 tubi sono canali in parallelo che non si scambiano
        //  calore fra loro, e un singolo tubo sbilanciato non altera ne' la
        //  circolazione ne' la produzione di vapore dell'apparecchio. Quindi
        //  si marcia UN tubo con la portata maggiorata tenendo congelate le
        //  resistenze lato acqua e il flusso critico locale.
        // ==================================================================
        let maldist =
            let jb = cellDnb.J
            let wTube0 = wGasTot * (1.0 - xUsed) / float t.NTubes
            [ for ex in [ 0.0; 0.05; 0.10; 0.15; 0.20; 0.30 ] ->
                let w = wTube0 * (1.0 + ex)
                let mutable h = GasProps.enthalpyAbsReal case.Gas.RealGas comp0 case.Gas.TIn case.Gas.PIn
                let mutable qMax = 0.0
                let mutable zMax = 0.0
                let mutable tmiMax = 0.0
                let mutable dnbMin = infinity
                let mutable duty = 0.0
                let mutable reIn = 0.0
                let mutable hPeak = 0.0
                for i in 0 .. nz - 1 do
                    let bc = out.Cells.[i, jb, 0]
                    let p = bc.PGas
                    let tG = fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas p comp0 h)
                    let pr = GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas comp0 tG p case.Gas.Z
                    let bore = if bc.InFerrule then case.Ferrule.Bore else t.Di
                    let re = 4.0 * w / (Math.PI * bore * pr.Mu)
                    if i = 0 then reIn <- re
                    let nu = GasSide.nusseltFD case.Gas.Correlation re pr.Pr 1.0
                    let fProp = GasSide.gasPropertyCorrection bc.TMetalIn pr.T
                    let ent = GasSide.entranceCorrection bc.Z bore case.Gas.EntranceC
                    let hg = nu * fProp * ent * pr.K / bore + bc.HRadGas
                    // resistenza totale di riferimento, dalla soluzione di base:
                    //   R_tot,base = (T_gas,base - Tsat) / q'_base
                    // si sostituisce la sola quota lato gas, tutto il resto
                    // (sporcamento, metallo, ferrula, ebollizione) resta identico
                    let hgBase = bc.HConvGas + bc.HRadGas
                    let rTotBase = (bc.TGas - sat.Tsat) / max 1.0 bc.QLin
                    let rGasBase = 1.0 / (max 1.0 hgBase * Math.PI * bore)
                    let rGasNew = 1.0 / (max 1.0 hg * Math.PI * bore)
                    let rFoulIn = case.Gas.FoulingIn / (Math.PI * bore)
                    let rTot = rTotBase - rGasBase + rGasNew
                    let qlin = (tG - sat.Tsat) / max 1e-9 rTot
                    let qOut = qlin / (Math.PI * t.Do)
                    let tmi = tG - qlin * (rGasNew + rFoulIn)
                    // Le celle SOTTO FERRULA sono escluse dai massimi: li' la
                    // formula darebbe la temperatura del bore della ferrula,
                    // non quella del metallo del tubo, che sta dietro
                    // l'isolante. Il picco cade comunque subito a valle.
                    if not bc.InFerrule then
                        if qOut > qMax then
                            qMax <- qOut
                            zMax <- bc.Z
                            hPeak <- hg
                        let dnb = bc.QCritLocal / max 1.0 qOut
                        if dnb < dnbMin then dnbMin <- dnb
                        if tmi > tmiMax then tmiMax <- tmi
                    duty <- duty + qlin * out.Dz.[i]
                    h <- h - qlin * out.Dz.[i] / w
                let tOut = fst (Shift.stateFromEnthalpyAt case.Gas.ShiftMode case.Gas.RealGas case.Gas.PIn comp0 h)
                { Excess = ex; FlowPerTube = w; ReIn = reIn; HGasPeak = hPeak
                  QFluxMax = qMax; ZQMax = zMax; TMetalInMax = tmiMax
                  TGasOut = tOut; DNBRMin = dnbMin; DutyTube = duty } ]

        // --- transitori e protezione ---
        let transient =
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
                - (if case.Bypass.Enabled then Math.PI / 4.0 * bpSpec.PipeOd * bpSpec.PipeOd * t.Length else 0.0)
            let alphaMean = cells |> List.averageBy (fun c -> c.Alpha)
            let mWater = vShell * (1.0 - alphaMean) * sat.RhoL
            let duty = out.Duty + (match bpRes with Some b -> b.HeatLoss | None -> 0.0)
            let tDry = mWater * sat.Hfg / max 1.0 duty
            // Inventario del corpo cilindrico al livello normale: segmento
            // circolare per la lunghezza fra le linee di tangenza.
            let mDrum =
                if case.Loop.Drum.Enabled then
                    let d0 = case.Loop.Drum
                    let rr = 0.5 * d0.ShellId
                    let hh = min d0.NormalLevel d0.ShellId
                    let th = acos (max -1.0 (min 1.0 ((rr - hh) / rr)))
                    let aSeg = rr * rr * (th - sin th * cos th)
                    aSeg * d0.Length * sat.RhoL
                else 0.0
            // Se i downcomer restano aperti l'acqua del corpo cilindrico scende
            // per gravita' e si aggiunge all'inventario disponibile.
            let tDryTot = (mWater + mDrum) * sat.Hfg / max 1.0 duty
            // dopo il dry-out resta il solo vapore a raffreddare: il metallo
            // tende alla temperatura del gas ridotta dalla resistenza residua
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

        let riserFlows = Circulation.lineFlows case sat case.Loop.Risers true circ.XOutRiser circ.CircFlow
        let dcFlows = Circulation.lineFlows case sat case.Loop.Downcomers false 0.0 circ.CircFlow
        let riserChecks =
            Mechanics.checkRisers sat riserFlows circ.XOutRiser case.MaxRhoV2Riser case.Loop.VoidModel
        let mkCheck (twoPhase: bool) (x: float) ((ln: Piping.Line), (w: float)) =
            let rho = if twoPhase then TwoPhase.homogeneousDensity x sat else sat.RhoL
            let v = w / (rho * Piping.area ln)
            let re = max 100.0 (rho * v * ln.Id / sat.MuL)
            let f = GasSide.darcyFriction re (4.5e-5 / ln.Id)
            { Tag = ln.Tag; Nps = ln.Nps; Id = ln.Id; Count = ln.Count
              ZNozzle = ln.ZNozzle; AngleDeg = ln.AngleDeg
              DevelopedLength = Piping.developedLength ln
              NElbows = Piping.elbowCount ln
              KTotal = Piping.totalK f ln
              Flow = w; Velocity = v; RhoV2 = rho * v * v
              Regime = (if twoPhase then Some(Mechanics.flowRegime sat ln.Id (w * (1.0 - x) / (sat.RhoL * Piping.area ln)) (w * x / (sat.RhoV * Piping.area ln))) else None)
              Connected = true
              Bom = Piping.billOfMaterial ln
              Note = ln.Note }
        let lineChecks =
            (riserFlows |> List.map (mkCheck true circ.XOutRiser))
            @ (dcFlows |> List.map (mkCheck false 0.0))
            @ (notConnected
               |> List.map (fun ln ->
                    { Tag = ln.Tag; Nps = ln.Nps; Id = ln.Id; Count = ln.Count
                      ZNozzle = ln.ZNozzle; AngleDeg = ln.AngleDeg
                      DevelopedLength = 0.0; NElbows = 0; KTotal = 0.0
                      Flow = 0.0; Velocity = 0.0; RhoV2 = 0.0; Regime = None
                      Connected = false
                      Bom = "linea non realizzata"
                      Note = ln.Note }))
        let findings =
            buildFindings case sat cells axial circ ftRes riserChecks expansions bpRes out.DpGas
                stressRes valveRes notConnected vibration
        let warnings =
            findings
            |> List.map (fun f ->
                sprintf "%s - %s: %s (criterio: %s) @ %s%s%s"
                    (sev f.Severity) f.Title f.Value f.Limit f.Where
                    (if f.Detail = "" then "" else " | " + f.Detail)
                    (if f.Action = "" then "" else " | AZIONE: " + f.Action))

        { Case = case
          Sat = sat
          Bands = bands
          Cells = cells
          Axial = axial
          Circulation = circ
          Nozzles = nozzles
          Expansions = expansions
          FixedTubesheet = ftRes
          Stress = stressRes
          Valve = valveRes
          Vibration = vibration
          Maldistribution = maldist
          Transient = transient
          ChfModels = chfModels
          Sensitivity = sensitivity
          FoulingCases = foulingCases
          DrumResult =
            (if case.Loop.Drum.Enabled then
                Some(Drum.solve case.Loop.Drum sat circ.CircFlow circ.XOutRiser
                         circ.SteamFlow (Circulation.branchArea case.Loop.Risers)
                         (Circulation.branchArea case.Loop.Downcomers))
             else None)
          BypassResult = bpRes
          Findings = findings
          RiserChecks = riserChecks
          LineChecks = lineChecks
          FerruleClasses =
            out.Classes
            |> List.mapi (fun ci (frac, fl) ->
                let sub = cells |> List.filter (fun c -> c.C = ci)
                let hotSub = sub |> List.filter (fun c -> not c.InFerrule)
                let qm = hotSub |> List.maxBy (fun c -> c.QFluxOut)
                { Index = ci
                  Frac = frac
                  Length = fl
                  QFluxMax = qm.QFluxOut
                  ZQMax = qm.Z
                  TMetalInMax = sub |> List.map (fun c -> c.TMetalIn) |> List.max
                  DNBRMin = hotSub |> List.map (fun c -> c.DNBR) |> List.min
                  TGasOut =
                    (let mutable a = 0.0
                     let mutable wq = 0.0
                     for j in 0 .. ny - 1 do
                        a <- a + ntb.[j] * out.TGasOutBandClass.[j, ci]
                        wq <- wq + ntb.[j]
                     a / wq)
                  Duty = sub |> List.sumBy (fun c -> c.QLin * out.Dz.[c.I] * c.NTubes) })
          Duty = out.Duty + (match bpRes with Some b -> b.HeatLoss | None -> 0.0)
          SteamProduction = out.Steam + (match bpRes with Some b -> b.SteamFromBypass | None -> 0.0)
          TGasOutMean = tOutMean
          TGasOutMin = tMin
          TGasOutMax = tMax
          DpGas = out.DpGas
          AreaOut = areaOut
          AreaIn = areaIn
          UMean = (if lm > 0.0 then out.Duty / (areaOut * lm) else 0.0)
          LmtdMean = lm
          Warnings = warnings }
