namespace Whb.Core

open System
open Constants

/// **Vibrazioni indotte dal flusso lato mantello (FIV).**
///
/// E' la causa di rottura piu' comune nei fasci tubieri attraversati da
/// crossflow, e non ha nulla a che vedere con la termica: un tubo puo' essere
/// perfettamente verificato a temperatura, pressione e dilatazione e rompersi
/// per fatica in poche migliaia di ore perche' vibra.
///
/// Quattro meccanismi, da verificare tutti (TEMA sez. V; Pettigrew-Taylor;
/// Chen; Owen):
///
///  1. **instabilita' fluido-elastica** - il piu' pericoloso. Sopra una
///     velocita' critica il tubo estrae energia dal flusso a ogni ciclo e
///     l'ampiezza cresce senza limite fino all'urto contro i vicini o alla
///     rottura al diaframma. Non e' una risonanza: non c'e' frequenza da
///     evitare, c'e' una velocita' da non superare.
///  2. **distacco di vortici** (Strouhal) - risonanza se la frequenza di
///     distacco si avvicina a quella propria. In flusso bifase e' molto meno
///     rilevante, perche' le bolle distruggono la coerenza della scia.
///  3. **buffeting turbolento** - eccitazione casuale a banda larga: non
///     produce collasso ma fatica e usura ai supporti.
///  4. **risonanza acustica** - solo con gas comprimibile a mantello; qui il
///     mantello contiene acqua e vapore in ebollizione, quindi non si applica.
module Vibration =

    /// **Reticolo secondo TEMA RCB-2.4**, definito dall'angolo fra la
    /// direzione del CROSSFLOW e le file di tubi. E' il dato che seleziona la
    /// costante di Connors, e vale un fattore 4 sulla velocita' critica:
    /// va letto sul disegno, non assunto.
    ///   30 gradi - TRIANGOLARE: il vertice del triangolo punta nel flusso
    ///   60 gradi - TRIANGOLARE RUOTATO: il lato lungo e' trasversale al flusso
    ///   90 gradi - QUADRATO ;  45 gradi - QUADRATO RUOTATO
    type Layout =
        | Triangular30
        | RotatedTriangular60
        | Square90
        | RotatedSquare45

    /// **Vincolo del tubo alle estremita'.**
    /// Ai DIAFRAMMI il tubo e' un semplice NODO: il foro impedisce lo
    /// spostamento laterale ma non la rotazione. Trattarlo come incastro
    /// sovrastima la frequenza propria di oltre il doppio.
    /// Alla PIASTRA TUBIERA dipende dal giunto:
    ///   - saldatura crevice-free  -> nodo semplice (appoggio)
    ///   - saldatura a piena penetrazione -> incastro
    type JointType =
        | CreviceFreeWeld
        | FullPenetrationWeld

    let jointName =
        function
        | CreviceFreeWeld -> "saldatura crevice-free -> APPOGGIO alla piastra"
        | FullPenetrationWeld -> "saldatura a piena penetrazione -> INCASTRO alla piastra"

    /// Coefficiente lambda² del primo modo secondo le condizioni di vincolo:
    ///   appoggio-appoggio  9.87   (pi²)
    ///   incastro-appoggio 15.42
    ///   incastro-incastro 22.37
    let lambda2Of (clampedEnds: int) =
        match clampedEnds with
        | 0 -> 9.87
        | 1 -> 15.42
        | _ -> 22.37

    let layoutName =
        function
        | Triangular30 -> "30° TRIANGOLARE (vertice nel flusso)"
        | RotatedTriangular60 -> "60° TRIANGOLARE RUOTATO (lato lungo trasversale al flusso)"
        | Square90 -> "90° QUADRATO"
        | RotatedSquare45 -> "45° QUADRATO RUOTATO"

    /// **Costante di Connors raccomandata per il reticolo, in flusso BIFASE.**
    /// I reticoli RUOTATI sono molto meno stabili di quelli normali: il valore
    /// per il triangolare ruotato e' 3-4 volte piu' basso, e dipende anche dal
    /// parametro di massa-smorzamento.
    /// Fonte: "Design guidelines for fluid-elastic instability of tube bundles
    /// subjected to two-phase cross flow", J. Zhejiang Univ. SCIENCE A.
    /// Il valore 3.0 di Pettigrew-Taylor e' invece una guida GENERALE, valida
    /// come inviluppo su tutte le configurazioni ma non specifica del reticolo.
    let connorsK (lay: Layout) (massDamping: float) =
        match lay with
        | Triangular30 | Square90 -> 4.0
        | RotatedTriangular60 | RotatedSquare45 ->
            if massDamping <= 0.54 then 1.1 else 1.5

    type Result =
        { Band: int
          Y: float
          /// campata considerata [m]
          Span: float
          /// frequenza propria del primo modo [Hz]
          FreqNat: float
          /// massa per unita' di lunghezza: metallo + fluido interno + massa
          /// aggiunta idrodinamica [kg/m]
          MassLin: float
          MassAdded: float
          /// coefficiente di massa aggiunta
          Cm: float
          /// velocita' di crossflow nel varco [m/s]
          VGap: float
          /// densita' della miscela [kg/m³]
          Rho: float
          /// parametro di massa-smorzamento  m delta /(rho D²)
          MassDamping: float
          /// velocita' critica di instabilita' fluido-elastica [m/s]
          VCrit: float
          /// rapporto V/Vcrit  (criterio: < 0.8)
          FeiRatio: float
          /// frequenza di distacco dei vortici [Hz]
          FreqVortex: float
          /// rapporto f_vortici / f_propria (lock-in fra 0.5 e 2)
          VortexRatio: float
          /// frequenza caratteristica del buffeting turbolento [Hz]
          FreqBuffet: float
          BuffetRatio: float
          /// costante di Connors effettivamente usata
          KConnors: float
          /// decremento logaritmico usato
          Delta: float
          Ok: bool
          Note: string }

    /// **Coefficiente di massa aggiunta** per un tubo in un fascio
    /// (TEMA V-6 / Rogers): il tubo che oscilla deve muovere anche il fluido
    /// che ha intorno, e in un fascio fitto quel fluido e' molto piu' della
    /// scia di un cilindro isolato.
    ///     De/D = (0.96 + 0.5 P/D) P/D
    ///     Cm   = ((De/D)² + 1) / ((De/D)² - 1)
    let addedMassCoef (pitchRatio: float) =
        let de = (0.96 + 0.5 * pitchRatio) * pitchRatio
        let d2 = de * de
        if d2 <= 1.0 then 2.0 else (d2 + 1.0) / (d2 - 1.0)

    /// Frequenza propria del primo modo di una trave a sezione anulare:
    ///     f = (lambda²/2 pi) sqrt( E I / (m L^4) )
    /// lambda²: 22.37 incastro-incastro, 15.42 incastro-appoggio,
    ///          9.87 appoggio-appoggio.
    /// Con gioco foro/tubo di 0.4 mm sul diametro il vincolo al diaframma e'
    /// di fatto un incastro.
    let naturalFrequency (lambda2: float) (e: float) (i: float) (m: float) (l: float) =
        lambda2 / (2.0 * Math.PI) * sqrt (e * i / (m * Math.Pow(l, 4.0)))

    /// Momento d'inerzia della sezione anulare [m^4]
    let inertia (dOut: float) (dIn: float) =
        Math.PI / 64.0 * (Math.Pow(dOut, 4.0) - Math.Pow(dIn, 4.0))

    /// **Velocita' critica di instabilita' fluido-elastica** (Connors, nella
    /// forma raccomandata da Pettigrew-Taylor e ripresa da TEMA):
    ///
    ///     V_crit / (f_n D) = K [ m delta / (rho D²) ]^0.5
    ///
    /// K dipende dal reticolo e dalla base dati: 3.0 e' il limite inferiore
    /// raccomandato come criterio di progetto per fasci in flusso bifase,
    /// 3.3 e' il valore classico di Connors, i dati sperimentali arrivano a 10.
    /// **Usare 3.0 significa progettare sull'inviluppo inferiore dei dati.**
    ///
    /// delta = decremento logaritmico totale (strutturale + viscoso + bifase).
    /// In crossflow bifase Pettigrew misura 0.03-0.10; 0.03 e' conservativo.
    let criticalVelocity (k: float) (fn: float) (d: float) (m: float)
                         (delta: float) (rho: float) =
        k * fn * d * sqrt (m * delta / (rho * d * d))

    /// Numero di Strouhal per reticolo triangolare ruotato (Chen / Fitz-Hugh):
    /// dipende dal passo relativo; per P/D fra 1.25 e 1.50 vale ~0.5-0.3.
    let strouhal (pitchRatio: float) =
        max 0.2 (min 0.6 (0.85 / pitchRatio - 0.13))

    /// Frequenza caratteristica del **buffeting turbolento** (Owen):
    ///     f_tb = (V/D) [ 3.05 (1 - D/P)² + 0.28 ]
    let buffetFrequency (v: float) (d: float) (pitch: float) =
        let r = 1.0 - d / pitch
        v / d * (3.05 * r * r + 0.28)

    /// Verifica completa per una banda del fascio.
    ///   kConnors : costante di Connors (3.0 conservativo)
    ///   delta    : decremento logaritmico totale
    ///   lambda2  : condizione di vincolo agli estremi della campata
    let check (band: int) (y: float) (span: float) (lambda2: float)
              (layout: Layout) (delta: float)
              (dOut: float) (dIn: float) (pitch: float)
              (eMetal: float) (rhoMetal: float)
              (vGap: float) (rhoShell: float) (rhoInside: float) : Result =
        let aMetal = Math.PI / 4.0 * (dOut * dOut - dIn * dIn)
        let mMetal = rhoMetal * aMetal
        let mIn = rhoInside * Math.PI / 4.0 * dIn * dIn
        let cm = addedMassCoef (pitch / dOut)
        let mAdd = cm * rhoShell * Math.PI / 4.0 * dOut * dOut
        let m = mMetal + mIn + mAdd
        let i = inertia dOut dIn
        let fn = naturalFrequency lambda2 eMetal i m span
        let md = m * delta / (rhoShell * dOut * dOut)
        // la costante di Connors dipende dal reticolo E dal parametro di
        // massa-smorzamento, quindi si risolve qui e non a monte
        let kConnors = connorsK layout md
        let vc = criticalVelocity kConnors fn dOut m delta rhoShell
        let fs = strouhal (pitch / dOut) * vGap / dOut
        let ftb = buffetFrequency vGap dOut pitch
        let fei = vGap / vc
        let vr = fs / fn
        let br = ftb / fn
        { Band = band; Y = y; Span = span
          KConnors = kConnors; Delta = delta
          FreqNat = fn; MassLin = m; MassAdded = mAdd; Cm = cm
          VGap = vGap; Rho = rhoShell; MassDamping = md
          VCrit = vc; FeiRatio = fei
          FreqVortex = fs; VortexRatio = vr
          FreqBuffet = ftb; BuffetRatio = br
          Ok = fei < 0.8 && not (vr > 0.5 && vr < 2.0)
          Note =
            if fei >= 1.0 then
                "INSTABILITA' FLUIDO-ELASTICA: la velocita' supera la critica. Ampiezza divergente, urto fra tubi e rottura per fatica al diaframma. Ridurre la campata."
            elif fei >= 0.8 then
                "margine insufficiente sull'instabilita' fluido-elastica (criterio V/Vcrit < 0.8)"
            elif vr > 0.5 && vr < 2.0 then
                "possibile aggancio con il distacco dei vortici (attenuato in flusso bifase)"
            else "verificato su tutti i meccanismi" }

    /// Campata massima ammessa perche' V/Vcrit resti sotto il limite.
    /// Poiche' f_n ~ 1/L² e V_crit ~ f_n, il rapporto V/Vcrit cresce con L²:
    /// dimezzare la campata divide per quattro il rapporto.
    let maxSpan (limit: float) (r: Result) =
        r.Span * sqrt (limit / max 1e-9 r.FeiRatio)

    /// Campata massima ammessa con una coppia (K, delta) diversa da quella del
    /// calcolo: V_crit e' proporzionale a K e a sqrt(delta), e il rapporto
    /// V/Vcrit va con L^2.
    let maxSpanWith (limit: float) (kNew: float) (deltaNew: float) (r: Result) =
        let scale = (kNew / r.KConnors) * sqrt (deltaNew / r.Delta)
        r.Span * sqrt (limit * scale / max 1e-9 r.FeiRatio)

    /// Rapporto V/Vcrit con una coppia (K, delta) diversa
    let ratioWith (kNew: float) (deltaNew: float) (r: Result) =
        r.FeiRatio / ((kNew / r.KConnors) * sqrt (deltaNew / r.Delta))
