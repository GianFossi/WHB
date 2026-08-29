namespace Whb.Core

open System
open Constants
open Types

module Findings =

    let build (correlationValidityWarnings: bool) (case: DesignCase) (sat: Steam.SatProps) (cells: CellResult list)
              (axial: AxialResult list) (circ: CirculationResult)
              (ft: FixedTubesheetResult) (risers: RiserCheck list)
              (expansions: ExpansionResult list) (bp: Bypass.Result option) (dpGas: float)
              (stress: StressResult) (valve: ValveResult option)
              (notConnected: Piping.Line list)
              (vibration: Vibration.Result list)
              (conv: ConvergenceReport)
              (sulphur: Sulphur.CouplingSummary option)
              (sulphurCondenser: SulphurCondenser.Result option) =
        let fs = ResizeArray<Finding>()
        let hot = cells |> List.filter (fun c -> not c.InFerrule)
        let qmax = hot |> List.maxBy (fun c -> c.QFluxOut)
        let dnbReqOf (c: CellResult) = WaterSide.dnbrRequired case.Water.MinDNBR (c.I = 0) c.InFerrule
        let dnbReqLabel = sprintf "%.2f" case.Water.MinDNBR
        let dnb = cells |> List.minBy (fun c -> c.DNBR / dnbReqOf c)
        let dnbReq = dnbReqOf dnb
        let dnbZone =
            if dnb.InFerrule then sprintf "zona ferrula (DNBR richiesto %s)" dnbReqLabel
            elif dnb.I = 0 then sprintf "prima fila al gas d'ingresso (DNBR richiesto %s)" dnbReqLabel
            else sprintf "campo fascio (DNBR richiesto %s)" dnbReqLabel
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
        let locSimple (c: CellResult) =
            sprintf "z = %.2f m, y = %+.2f m" c.Z c.Y
        let add s area title value limit where action detail =
            fs.Add { Severity = s; Area = area; Title = title; Value = value
                     Limit = limit; Where = where; Action = action; Detail = detail }
        let addSulphurCheck where action (check: Sulphur.Check) =
            match check.Severity with
            | Sulphur.Ok -> ()
            | Sulphur.Watch ->
                add Warning "ZOLFO" check.Title check.Value check.Limit where action check.Detail
            | Sulphur.Alarm ->
                add Critical "ZOLFO" check.Title check.Value check.Limit where action check.Detail
        let addCondenserCheck (check: Sulphur.Check) =
            match check.Severity with
            | Sulphur.Ok -> ()
            | Sulphur.Watch ->
                add Warning "CONDENSATORE ZOLFO" check.Title check.Value check.Limit
                    "tratto dedicato di condensazione Claus"
                    "Verificare il report dedicato del condensatore zolfo e la finestra termica di esercizio."
                    check.Detail
            | Sulphur.Alarm ->
                add Critical "CONDENSATORE ZOLFO" check.Title check.Value check.Limit
                    "tratto dedicato di condensazione Claus"
                    "Rivedere subito parete, livello termico del refrigerante e gestione del condensato."
                    check.Detail

        if not conv.CoupledConverged then
            add Critical "CONVERGENZA" "Ciclo termico/circolazione non convergente"
                (sprintf "residuo relativo %.2e dopo %d iterazioni" conv.CoupledResidual conv.CoupledIterations)
                "residuo < 1e-5 su portata di campo e titolo in ingresso"
                "accoppiamento fra scambio termico e circolazione naturale"
                "Verificare il caso: geometria del circuito, battente disponibile e portata gas. I risultati sotto NON sono un punto fisso convergente."
                "Il ciclo alterna il solutore di fascio e quello di circolazione fino a che la ripartizione di portata non si stabilizza. Fermarsi al tetto di iterazioni significa che le due parti stanno ancora spostandosi a vicenda: duty, titolo e DNBR riportati sono quelli dell'ultima passata, non della soluzione."
        if not conv.CirculationBracketOk then
            add Critical "CIRCOLAZIONE" "Bilancio di circolazione senza soluzione nell'intervallo esplorato"
                "nessun cambio di segno nel bracket"
                "battente disponibile = domanda del fascio, con portata fra 1.5 e 200 volte il vapore prodotto"
                "circuito di circolazione naturale"
                "Il circuito non e' sostenibile con questa geometria: rivedere dislivello, numero e diametro di downcomer e riser."
                "In assenza di cambio di segno la portata riportata e' l'estremo dell'intervallo con residuo minore, NON una soluzione. Il rapporto di circolazione che ne deriva e' numericamente plausibile ma privo di significato fisico."
        elif conv.CirculationRoots > 1 then
            add Warning "CIRCOLAZIONE" "Punto di lavoro della circolazione non unico"
                (sprintf "%d attraversamenti dello zero nel bilancio" conv.CirculationRoots)
                "una sola soluzione (punto di lavoro stabile)"
                "circuito di circolazione naturale"
                "Verificare la stabilita' del circuito ai carichi parziali e valutare una perdita concentrata all'imbocco dei downcomer, che irrigidisce la curva di domanda."
                "La domanda di un canale bollente e' a S: a bassa portata cresce per il vuoto, ad alta portata per l'attrito. Se il battente disponibile la interseca piu' volte esistono piu' punti di lavoro e l'apparecchio puo' saltare dall'uno all'altro (instabilita' statica di Ledinegg). La soluzione riportata e' una delle possibili."
        if conv.QualityClampedCells > 0 then
            add Warning "EBOLLIZIONE" "Titolo troncato al limite del modello bifase"
                (sprintf "%d celle a x = 0.95" conv.QualityClampedCells)
                "x < 0.95 per restare nel campo delle correlazioni bifase"
                (sprintf "prima cella troncata a z = %.2f m" conv.QualityClampFirstZ)
                "Aumentare la circolazione o ridurre il flusso termico locale nelle bande alte."
                "Il titolo e' limitato a 0.95 per non uscire dal campo delle correlazioni di frazione di vuoto e di attrito bifase. La potenza scambiata non ne risente, perche' il vapore si ricava dalla potenza; ma frazione di vuoto, DNBR e perdite di carico di quelle celle usano il titolo troncato e vanno letti come stime conservative."
        if conv.NonConvergedCells > 0 then
            add Warning "CONVERGENZA" "Celle con temperatura di parete non convergente"
                (sprintf "%d celle oltre il tetto di iterazioni" conv.NonConvergedCells)
                "punto fisso stabile entro 1 mK"
                "solutore di cella, tipicamente vicino al flusso termico critico"
                "Verificare le celle a flusso piu' alto: e' li' che la curva di ebollizione si impenna e il punto fisso fatica a chiudere."
                "La temperatura di parete si ottiene iterando fra resistenza gas, metallo e ebollizione. Vicino al CHF il coefficiente di ebollizione cambia rapidamente con il flusso e l'iterazione puo' non chiudere: per quelle celle T metallo e DNBR sono indicativi."
        if conv.CirculationBracketOk && conv.CirculationSlope > 0.0 then
            add Critical "CIRCOLAZIONE" "Punto di lavoro instabile per escursione di portata"
                (sprintf "pendenza del bilancio %+.3e Pa/(kg/s) al punto di lavoro" conv.CirculationSlope)
                "pendenza negativa: la domanda del fascio deve crescere piu' in fretta del battente disponibile"
                "circuito di circolazione naturale"
                "Irrigidire la curva di domanda con una perdita concentrata all'imbocco dei downcomer, oppure aumentare il battente."
                "E' il criterio di Ledinegg. Con pendenza positiva una piccola riduzione di portata fa calare la domanda piu' del battente: la portata continua a scendere invece di tornare indietro. E' un'instabilita' statica, non un'oscillazione, e porta il fascio a un punto di lavoro completamente diverso."
        if conv.DowncomerSubcooling < conv.DowncomerSubcoolingRequired then
            add Warning "CIRCOLAZIONE" "Margine al flash all'imbocco dei downcomer insufficiente"
                (sprintf "sottoraffreddamento disponibile %.2f K" conv.DowncomerSubcooling)
                (sprintf "richiesti %.2f K dalla caduta locale di imbocco" conv.DowncomerSubcoolingRequired)
                "bocchelli di discesa sul corpo cilindrico"
                "Alzare il livello sul bocchello, ridurre la velocita' di imbocco (bocchelli piu' grandi o piu' numerosi) o aumentare il sottoraffreddamento dell'alimento."
                "All'imbocco la perdita di ingresso e la testa cinetica abbassano la pressione statica prima che la colonna la faccia risalire. Se il sottoraffreddamento non copre quel salto, si formano bolle nel downcomer: il battente motore cala, la portata oscilla e il fascio viene alimentato in modo intermittente. Il sottoraffreddamento disponibile viene dalla miscelazione con l'acqua alimento nel corpo cilindrico."
        if not conv.BypassMapBracketsTarget then
            add Warning "BY-PASS" "Mappa del by-pass piu' stretta della temperatura richiesta"
                "il bersaglio cade oltre l'ultimo punto calcolato"
                "la mappa deve contenere la temperatura miscelata di progetto"
                "mappa del by-pass"
                "Usare calculation.bypassMapMode = full, oppure estendere la griglia delle frazioni di by-pass."
                "Ogni limite della valvola si ottiene invertendo la mappa. Se il bersaglio cade fuori, l'inversione restituisce l'estremo della mappa e quel valore compare come se fosse un vincolo: la finestra operativa riportata e' quindi piu' stretta di quella reale."

        if correlationValidityWarnings then
            let reMin = cells |> List.map (fun c -> c.ReGas) |> List.min
            let reMax = cells |> List.map (fun c -> c.ReGas) |> List.max
            if reMin < 10000.0 then
                add Warning "VALIDITA' CORRELAZIONI" "Reynolds gas fuori dal campo pienamente turbolento"
                    (sprintf "Re min = %.0f, Re max = %.0f" reMin reMax)
                    "Re >= 10000 per uso robusto delle correlazioni forced-convection turbolente"
                    "lato gas, celle a bassa portata/temperatura"
                    "Verificare correlazione laminar/transitional o aumentare la confidenza con benchmark dedicato."
                    "Dittus-Boelter, Gnielinski e simili sono piu' affidabili in moto turbolento sviluppato."
            let compV = GasProps.normalize case.Gas.Composition
            let prValues =
                cells
                |> List.map (fun c -> (GasProps.mixReal case.Gas.MixingRule case.Gas.RealGas compV c.TGas c.PGas case.Gas.Z).Pr)
            let prMin = prValues |> List.min
            let prMax = prValues |> List.max
            if prMin < 0.5 || prMax > 2.0 then
                add Warning "VALIDITA' CORRELAZIONI" "Prandtl gas fuori dal range tipico"
                    (sprintf "Pr = %.3f .. %.3f" prMin prMax)
                    "range indicativo 0.5 .. 2.0 per screening gas-side"
                    "lato gas"
                    "Verificare proprieta' di trasporto e correlazione scelta."
                    "Il limite e' pratico: non blocca il calcolo ma richiede review."
            if case.Gas.PIn > barToPa 30.0 && not case.Gas.RealGas then
                add Warning "VALIDITA' PROPRIETA'" "Gas ideale usato ad alta pressione"
                    (sprintf "p ingresso = %.2f bar(a)" (paToBar case.Gas.PIn))
                    "usare modello realistico/viriale sopra circa 30 bar(a)"
                    "gas di processo"
                    "Impostare gas.modello_gas = realistico e validare con dati di trasporto."
                    "La densita' ideale puo' alterare velocita', Reynolds, dP e duty."

        let claus = Sulphur.clausScreening case.Gas.PIn case.Gas.Composition
        if claus.HasClausSpecies then
            let coldMetal = cells |> List.minBy (fun c -> c.TMetalIn)
            let speciesText = String.concat ", " claus.PresentSpecies
            let clausModeText = Claus.modeName case.Gas.ClausMode
            let sulphurModelText =
                match sulphur with
                | Some s when s.CondensingCells > 0 ->
                    sprintf "Modello Claus %s: lo solve accoppia la formazione/condensazione di zolfo elementare; condensa in %d celle, prima comparsa a z = %.2f m, frazione condensata in uscita %.1f %%."
                        clausModeText s.CondensingCells s.FirstCondensationZ (100.0 * s.OutletCondensedFraction)
                | Some _ ->
                    sprintf "Modello Claus %s: lo solve accoppia lo zolfo elementare nel bilancio principale, ma lungo questo profilo non raggiunge la saturazione." clausModeText
                | None ->
                    match case.Gas.ClausMode with
                    | Claus.Frozen ->
                        "Il solve WHB non converte H2S/SO2/COS/CS2 in zolfo elementare con modello Claus congelato: senza S2/S6/S8 espliciti questi finding restano uno screening di servizio Claus."
                    | _ ->
                        sprintf "Modello Claus %s attivo, ma questo caso non sviluppa zolfo elementare accoppiato in misura apprezzabile." clausModeText
            let dewBits =
                [ match sulphur with
                  | Some s ->
                      match s.InletSulphurDewPoint with
                      | Some t -> sprintf "dew point zolfo ingresso %.0f °C" (kToC t)
                      | None -> ()
                  | None ->
                      match claus.SulphurDewPoint with
                      | Some t -> sprintf "dew point zolfo elementare %.0f °C" (kToC t)
                      | None -> ()
                  match claus.WaterDewPoint with
                  | Some t -> sprintf "dew point acqua %.0f °C" (kToC t)
                  | None -> () ]
            add Note "ZOLFO" "Specie Claus rilevate nel gas"
                (sprintf "specie presenti: %s" speciesText)
                "screening dedicato richiesto quando il gas contiene specie Claus"
                "lato gas, intero apparecchio"
                "Usare il comando --sulphur per sweep dew point/condensa e confermare drenaggio e finestre di temperatura."
                (String.concat "; "
                    ([ sulphurModelText ]
                     @ dewBits))
            addSulphurCheck (loc tmax)
                "Limitare la temperatura di parete calda o valutare una metallurgia piu' resistente alla sulfidation."
                (Sulphur.checkSulphidation tmax.TMetalIn claus.YH2S)
            match claus.WaterDewPoint with
            | Some tDewWater ->
                addSulphurCheck (loc coldMetal)
                    "Proteggere avviamenti/fermate, evitare zone fredde persistenti e verificare materiali HIC/SOHIC/SSC."
                    (Sulphur.checkWetH2S coldMetal.TMetalIn tDewWater claus.YH2S)
            | None -> ()
            match sulphur with
            | Some s when s.CondensingCells > 0 ->
                add Warning "ZOLFO" "Condensazione di zolfo elementare nel fascio"
                    (sprintf "frazione condensata in uscita %.1f %%"
                        (100.0 * s.OutletCondensedFraction))
                    "parete drenante e temperatura di film nella finestra liquida"
                    (if Double.IsNaN s.FirstCondensationZ then locSimple coldMetal
                     else sprintf "prima condensazione a z = %.2f m" s.FirstCondensationZ)
                    "Verificare drenaggio del liquido, evitare ristagni e confermare il controllo della pressione LP."
                    "Il bilancio termico principale ora include l'equilibrio e la condensazione dello zolfo elementare esplicito; resta comunque uno screening 1D senza idraulica del film liquido."
                addSulphurCheck (loc coldMetal)
                    "Tenere la parete nella finestra liquida drenabile dello zolfo e controllare i transitori."
                    (Sulphur.checkWallWindow coldMetal.TMetalIn)
            | None ->
                match claus.SulphurDewPoint with
                | Some tDewSulphur when case.Gas.ClausMode = Claus.Frozen && coldMetal.TMetalIn <= tDewSulphur ->
                    add Warning "ZOLFO" "Parete in campo di condensazione dello zolfo non modellata"
                        (sprintf "T metallo minima = %.0f °C, dew point zolfo = %.0f °C"
                            (kToC coldMetal.TMetalIn) (kToC tDewSulphur))
                        "T parete gas > dew point zolfo se il tratto deve restare in solo raffreddamento"
                        (locSimple coldMetal)
                        "Rieseguire il caso con --sulphur e verificare quota latente, drenaggio liquido e margine alla transizione lambda."
                        "Questo e' uno screening: senza S2/S6/S8 espliciti il solve principale non sa quanta parte delle specie Claus diventi davvero zolfo elementare."
                | _ -> ()
            | _ -> ()

        match sulphurCondenser with
        | Some sc ->
            add Note "CONDENSATORE ZOLFO" "Integrazione condensatore zolfo attiva"
                (sprintf "sorgente %s, duty %.2f MW, area richiesta %.1f m2"
                    sc.SourceLabel (sc.Duty / 1e6) sc.AreaRequired)
                "modulo dedicato downstream per Claus"
                "servizio Claus / condensatore zolfo"
                "Controllare i file dedicati sulphur_condenser.txt e sulphur_condenser_profile.csv."
                (sprintf "Outlet %.1f C, frazione condensata %.1f %%, portata liquido zolfo %.1f kg/h."
                    (kToC sc.OutletState.T) (100.0 * sc.OutletState.CondensedFraction) (sc.CondensedSulphurMassFlow * 3600.0))
            for chk in sc.Checks do
                addCondenserCheck chk
        | None -> ()

        if dnb.DNBR < dnbReq then
            add Critical "EBOLLIZIONE" "Margine su DNB insufficiente"
                (sprintf "DNBR = %.2f (%s)" dnb.DNBR dnbZone)
                (sprintf "DNBR >= %.2f" dnbReq)
                (loc dnb)
                "Allungare la ferrula, aumentare la circolazione, o ridurre il titolo locale nella banda alta."
                (sprintf "Il flusso locale (%.0f kW/m2) supera il CHF di fascio corretto per il titolo locale x = %.3f. E' il punto in cui il film di vapore puo' staccare l'acqua dalla parete (steam blanketing)." (dnb.QFluxOut / 1000.0) dnb.XOut)
        elif dnb.DNBR < 1.25 * dnbReq then
            add Warning "EBOLLIZIONE" "Margine su DNB ridotto"
                (sprintf "DNBR = %.2f (%s)" dnb.DNBR dnbZone) (sprintf "DNBR >= %.2f" dnbReq) (loc dnb)
                "Verificare con criterio di flusso termico massimo; valutare ferrula piu' lunga."
                "Il criterio di Palen usato per il CHF di fascio e' conservativo, ma il margine resta sotto la pratica corrente."
        let boWorst = WaterSide.boilingNumber dnb.QFluxOut dnb.GCross sat.Hfg
        let coWorst = WaterSide.convectionNumber dnb.XOut sat
        if boWorst > 1.5e-4 || coWorst > 0.65 then
            add Note "EBOLLIZIONE" "Regime NBD al punto critico"
                (sprintf "Bo = %.2e, Co = %.2f" boWorst coWorst)
                "Bo > 1.5e-4 o Co > 0.65"
                (loc dnb)
                "Il vincolo di progetto e' il CHF locale, non l'HTC."
                "Nelle condizioni tipiche WHB a bassa portata di massa e basso titolo il rischio di steam blanketing e' governato dal margine su DNB. L'opzione vapore.ebollizione_flusso = kandlikar permette di rivalutare l'HTC locale senza il fattore di soppressione di Chen."

        if qmax.QFluxOut > 300000.0 then
            add Warning "TERMICO" "Flusso termico di picco elevato"
                (sprintf "%.0f kW/m2" (qmax.QFluxOut / 1000.0)) "250-350 kW/m2 (pratica per WHB a tubi da fumo)"
                (loc qmax)
                "Allungare la ferrula: da 200 a 500 mm il picco cala di circa il 9%."
                "Sopra 300 kW/m2 la sensibilita' a depositi, maldistribuzione e qualita' dell'acqua cresce rapidamente. E' il criterio pratico dominante, piu' del CHF teorico."

        if dTsup.DTsatWall > dTc then
            add Critical "EBOLLIZIONE" "Surriscaldamento di parete oltre il dT critico"
                (sprintf "%.1f K" dTsup.DTsatWall) (sprintf "dT critico = %.1f K" dTc)
                (loc dTsup)
                "Ridurre il flusso di picco (ferrula) e aumentare la circolazione."
                "Oltre il ginocchio della curva di ebollizione le bolle si fondono in un film continuo: h crolla e il metallo si scalda di centinaia di gradi in minuti."

        if dTdep.DTDeposit > 25.0 then
            add Warning "MATERIALI" "Deposito lato acqua determinante sulla T metallo"
                (sprintf "%.0f K di salto sul deposito" dTdep.DTDeposit)
                (sprintf "Rf assunto = %.1e m2K/W" case.Water.FoulingOut)
                (loc dTdep)
                "Controllo chimico dell'acqua (fosfati/AVT, silice, conducibilita') e pulizia chimica programmata."
                "Il metallo si scalda per il deposito, non per l'ebollizione. Il meccanismo e' autoacceleratore: piu' caldo -> piu' deposito -> piu' caldo."

        let tmC = kToC tmax.TMetalIn
        if tmC > case.Material.TmaxDesign then
            add Critical "MATERIALI" "Temperatura metallo oltre il limite del materiale"
                (sprintf "%.0f °C" tmC) (sprintf "%s: %.0f °C" case.Material.Name case.Material.TmaxDesign)
                (loc tmax) "Cambiare materiale o abbattere il flusso di picco." ""
        elif tmC > 0.92 * case.Material.TmaxDesign then
            add Warning "MATERIALI" "Temperatura metallo vicina al limite"
                (sprintf "%.0f °C" tmC) (sprintf "%s: %.0f °C" case.Material.Name case.Material.TmaxDesign)
                (loc tmax) "Verificare i margini a creep sulla vita di progetto." ""

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

        let velIn = (cells |> List.filter (fun c -> c.I = 0) |> List.maxBy (fun c -> c.VelGas)).VelGas
        if velIn > 60.0 then
            add Warning "GAS" "Velocita' del gas elevata all'imbocco"
                (sprintf "%.1f m/s" velIn) "<= 50-60 m/s" "imbocco tubi"
                "Verificare erosione e vibrazioni indotte." ""
        if dpGas > 0.9 * 0.3e5 then
            add Warning "GAS" "Perdita di carico lato gas vicina all'ammissibile"
                (sprintf "%.0f mbar" (dpGas / 100.0)) "0.30 bar (datasheet)" "intero percorso gas" "" ""

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

        if not (List.isEmpty notConnected) then
            let tags = notConnected |> List.map (fun l -> sprintf "%s (%s)" l.Tag l.Nps) |> String.concat ", "
            add Warning "CIRCOLAZIONE" "Bocchelli presenti ma NON collegati"
                tags "tutti i bocchelli previsti dovrebbero essere in servizio"
                "mantello, estremita' fredda e calda"
                "Verificare se sono riserve intenzionali. Se lo scopo era il lavaggio delle estremita', il collegamento va realizzato: sono le zone dove il campo tubi e' meno lavato."
                "Il calcolo idraulico e' stato eseguito SENZA queste linee: sezione di passaggio e battente motore sono quelli effettivamente disponibili, non quelli di disegno. R5 e DC9 servivano l'estremita' fredda; R0A/R0B l'estremita' calda, cioe' proprio la zona di picco di flusso termico e di DNBR minimo."

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
