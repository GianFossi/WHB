# WhbThermo — lacune, limiti e sviluppi futuri

Documento di riferimento sullo stato reale della libreria. Serve a rispondere a
una domanda sola: **di questo numero mi posso fidare, e fino a che punto?**

Aggiornato allo stato: 10 progetti, 48 specie, 275 test.

---

## 0. Il limite che viene prima di tutti gli altri

**Il codice non è mai stato compilato.** L'ambiente in cui è stato scritto non ha
accesso a NuGet, quindi `dotnet build` non è mai stato eseguito. La fisica è
stata validata numericamente in Python, riga per riga, contro oracoli
indipendenti — ma la prima compilazione troverà errori di sintassi, di tipo e di
ordine di compilazione, e vanno messi in conto.

Tutto ciò che segue presuppone quel passaggio fatto.

---

## 1. Lacune di dati

### 1.1 Trasporto — 22 specie su 48 senza dati misurati

| Stato | Specie |
|---|---|
| NASA CEA (misurato, fino a 5000–15000 K) | 26 |
| Sutherland dal manuale (degrada sopra ~1300 K) | 8: C₃H₆, C₃H₈, C₆H₆, C₇H₈, S₂, S₆, S₈, SO₃ |
| Nessun dato | 14: CH₃, CN, CS, DME, HCHO, HO₂, NH₂, S₁, S₃, S₄, S₅, S₇, SH, SO |

Le otto su Sutherland sono il problema pratico: **S₂, S₆ e S₈ servono proprio a
1400 °C**, dove un fit a due parametri non è difendibile. Chung-Lee-Starling le
copre come stima dichiarata, ma richiede Tc, Pc e ω — che per gli allotropi non
esistono (vedi 1.3), quindi lì Chung non è applicabile.

**Questa è la lacuna che tocca il rating Claus in modo diretto.** Non c'è una via
d'uscita pulita: servono dati misurati di viscosità e conducibilità per i vapori
di zolfo, e la letteratura aperta ne ha pochissimi.

### 1.2 Radiazione — 30 specie partecipanti scoperte

Il WSGG di Smith copre **solo H₂O e CO₂**. In servizio Claus mancano SO₂ (1–3 %)
e H₂S (2–5 %), entrambi assorbitori infrarossi sempre presenti.

**Cercato e non trovato**: nessun set di coefficienti WSGG o Leckner aperto
esiste per i composti dello zolfo. La letteratura recente estende il WSGG a CH₄,
CO, fuliggine e vapori di idrocarburi, non a SO₂ o H₂S.

Conseguenza pratica: **ogni emissività calcolata è bassa di una quantità non
quantificabile**. `Wsgg.emissivityWithCoverage` e `SpeciesApi.uncoveredRadiators`
lo dichiarano per specie, ma dichiararlo non lo risolve.

Vie possibili, in ordine di costo: acquisire dati SLW o line-by-line da HITEMP
per SO₂/H₂S; oppure accettare il risultato come limite inferiore e dimensionare
con un margine esplicito sul termine radiativo.

### 1.3 Proprietà critiche — 19 specie su 48

Assenti per radicali/atomi (H, O, N, OH, SH, SO, CH₃, HO₂, CS, NH₂, CN, S₁) e
per gli allotropi dello zolfo (S₂–S₈).

**Non è una lacuna di dati.** Un radicale non ha fase condensata e quindi non ha
punto critico; gli allotropi dello zolfo sono in equilibrio reattivo continuo e
non hanno punti critici individuali. La ragione è registrata per specie in
`criticalUnavailableReason` e un test verifica che ci sia.

Le 29 presenti provengono da raccolte manualistiche (CRC/IUPAC/Poling via
`chemicals`) e **non sono state verificate** una per una; solo 13 sono state
incrociate con CoolProp. Il campo `verified` lo registra.

### 1.4 Tensione di vapore — COS e NO₂

27 specie su 48 hanno la correlazione DIPPR 101. Le assenti sono radicali e
allotropi (che non ne hanno, per gli stessi motivi di sopra) più **due lacune
reali: COS e NO₂**, che semplicemente non sono nella Tabella 2-8 di Perry.

### 1.5 Record difettoso noto: viscosità liquida del n-pentano

Devia dal 19 al 49 % contro CoolProp su **tutto** il suo range tabulato, mentre
n-butano e n-esano della stessa tabella stanno entro il 2 %. La struttura dei
coefficienti differisce dagli omologhi (C₃ positivo, C₁ un ordine di grandezza
più grande). È marcato `suspect` con la motivazione, escluso dal golden test, e
un test blocca il flag perché un re-import non lo perda.

### 1.6 Coefficienti Leckner: fittati, non trascritti

Le matrici Leckner non sono quelle pubblicate nel 1972 — sono un **fit della
stessa forma funzionale** a Smith WSGG, perché le originali sono dietro paywall e
non riproducibili in nessuna fonte aperta verificabile. Portano quindi **zero
accuratezza indipendente da WSGG**: l'accordo del 10–15 % tra i due non è una
validazione.

Inoltre Alberti, Weber e Mancini riportano che i polinomi originali di Leckner
possono discostarsi dal line-by-line HITEMP-2010 tra −90 % e +180 %. WSGG resta
il default.

---

## 2. Limiti di modello

### 2.1 Gas ideale ovunque

Non c'è equazione di stato. `Z = 1` sempre, densità da `PM/RT`.

| Servizio | Pressione | Errore su densità |
|---|---|---|
| Claus SRU, TLE etilene | 1.5–2 bar | < 0.1 % |
| PGC metanolo/SMR | 20–40 bar | 0.5–1.5 % |
| **WHB synloop ammoniaca** | **130–250 bar** | **5–15 %** |

Sbagliare la densità sbaglia flusso massico, Reynolds e ΔP a cascata. Le
proprietà critiche ora presenti per 29 specie sono l'input che servirebbe a una
cubica (SRK/PR) o a PC-SAFT; il modulo non esiste.

### 2.2 Conducibilità di miscela: 14 % di spread di modello

Wassiljewa contro Mathur-Tondon-Saxena divergono fino al **14 %** su miscele
ricche di H₂. Non è un errore di implementazione: la regola di Wilke per la
viscosità riproduce Cantera a **0.0000 %**. È incertezza di modello reale.

Il fattore Mason-Saxena ε = 1.065, che il manuale prescrive, **non è applicato**:
sopra il φ basato sui rapporti di viscosità spinge k_mix sotto il limite
inferiore rigoroso in 22 casi su 45. La forma vera usa il rapporto delle
conducibilità traslazionali monoatomiche.

### 2.3 Correzione per variazione di proprietà: 25 % di spread

All'ingresso SRU (gas 1400 °C, parete 350 °C) i due trattamenti difendibili
danno **160 contro 124 W/(m²·K)**: Sieder-Tate o proprietà a temperatura di film.
Questa è l'incertezza vera del film lato gas, e domina qualunque raffinamento
delle proprietà.

Kays dà n ≈ 0 per il **raffreddamento** del gas: un esponente preso dal caso di
riscaldamento darebbe +56 %, in direzione non conservativa, sul tubo più caldo.

### 2.4 Ebollizione: fattore 2 tra i modelli

Chen e Steiner-Taborek differiscono di **2.12×** a basso titolo. Chen *somma* i
meccanismi con soppressione, Steiner-Taborek li combina **asintoticamente**.
Nessuno dei due è giusto: la scelta del modello pesa più di qualunque proprietà.

Rohsenow è peggio: i valori pubblicati di C_sf per l'acqua coprono h da 1108 a
104 kW/(m²·K), fattore **10.6**, perché C_sf entra al cubo.

### 2.5 Regioni IF97 non implementate

Regione 3 (quasi-critica, sopra 623 K / 16.5 MPa) e regione 5 (sopra 1073 K).
Uno stato che ci cade **fallisce esplicitamente**. Un corpo cilindrico sopra i
165 bar entrerebbe in regione 3.

### 2.6 Modelli monodimensionali

Nessun modello di distribuzione idraulica con ferrule, nessuna maldistribuzione,
nessun profilo radiale. Il tubo più caldo può scostarsi del 10–20 % dalla media e
la libreria non lo sa.

### 2.7 Limiti specifici zolfo

- La tensione di vapore da Gibbs CEA vale **120–350 °C**. Al punto di ebollizione
  normale dà 0.68 bar contro 1.013 attesi.
- La viscosità del liquido è un'**interpolazione logaritmica su ancoraggi
  pubblicati**, non una equazione pubblicata: nessuna forma chiusa riproduce bene
  la transizione λ.
- Il modello di drenaggio tratta il rivolo come strato sottile laminare. Oltre
  hold-up del 15 % l'ipotesi cade, e il modulo lo segnala.
- La curva di condensazione è un **input**, per scelta: riprodurre la VLE
  reattiva completa significherebbe reimplementare un pacchetto di flash e
  divergere dal simulatore che ha prodotto la garanzia.

---

## 3. Limiti di validazione

### 3.1 Gli oracoli non sono verità

CoolProp ha restituito **k = 0.0017 W/(m·K) per NH₃ a 1000 K** — due ordini di
grandezza in meno — senza sollevare errori. Il generatore golden ora filtra i
punti di riferimento con un margine sulla saturazione e un controllo di
monotonicità sulla serie stessa. 53 punti scartati.

### 3.2 Cosa non è stato validato contro la realtà

Nessun confronto con **dati di impianto**. Nessun confronto con **HTRI o Aspen
EDR**. La validazione è interamente contro altri calcoli: CoolProp, Cantera,
iapws, le tabelle ufficiali IF97, i dati NASA/NIST/Burcat.

Questo copre la correttezza delle proprietà e delle correlazioni. **Non copre se
il rating completo predice il duty di uno scambiatore reale.**

### 3.3 Dove sta davvero l'incertezza di una garanzia

Per un tubo WHB tipico la ripartizione delle resistenze è:

| Termine | Quota |
|---|---|
| film gas | **90.6 %** |
| fouling interno | 5.4 % |
| fouling esterno | 2.4 % |
| parete | 1.1 % |
| ebollizione | **0.5 %** |

Dimezzare il coefficiente di ebollizione sposta U dello 0.25 %. Il contributo
delle proprietà fisiche al duty è dell'ordine dell'**1–3 %**; fouling e film gas
valgono decine di punti. **Raffinare le proprietà non sposta una garanzia.**

---

## 4. Protezione del database

Quattro strumenti scrivono `data/species-database.json`, e nel corso dello
sviluppo **tre di loro hanno distrutto il lavoro di un altro**: un rebuild che ha
riportato 48 specie a 28, un importer che ha perso un flag `suspect` indagato a
mano, un cambio di percorso che ha lasciato un duplicato fantasma. Tutti e tre
scoperti per caso.

`tools/db_guard.py` esiste perché il prossimo sia scoperto di proposito. Ogni
scrittura passa da `save`, che **rifiuta** una scrittura che perde informazione:

- specie rimosse
- una specie che perde una proprietà che aveva
- un modello Cp degradato (nasa9 → nasa7 → shomate → anchor)
- un trasporto degradato (nasaCea → sutherland/none)
- un'annotazione manuale persa (`suspect`, `verified`, i campi `*Reason`)

Il file porta un hash SHA-256 del contenuto: una modifica fatta a mano è
**legittima e viene segnalata**, così la scrittura successiva stampa il diff
invece di sovrascrivere in silenzio. Un backup timestampato precede ogni
salvataggio in `data/.backups/` (ultimi 20).

L'override esiste — `--force` — ma è una decisione, e viene **registrata nella
storia del file** così resta visibile dopo.

```bash
python tools/db_guard.py          # stato, hash, ultime scritture
```

---

## 4bis. Prestazioni, robustezza, stabilità

### Corretto

**Math.Pow sui percorsi caldi.** `**` in F# chiama sempre `Math.Pow`, che costa
20–40 volte una moltiplicazione anche con esponente 2. Una valutazione NASA-9 ne
usava tre; un profilo condensatore zolfo da venti punti arrivava a circa
**72 000 chiamate**, quasi tutte evitabili. Forma di Horner in
`Domain/Numerics.fs`: zero `Math.Pow` in Cp, H, S, miscelazione Wilke ed
equilibri. I test verificano l'identità con la forma diretta a 9–12 cifre, perché
una riscrittura che sposta l'ultimo bit muoverebbe ogni ancoraggio di regressione
della suite.

**Bisezioni a conteggio fisso.** Tutte le ricerche di radice giravano un numero
fisso di iterazioni e restituivano il punto medio, convergenza o no. Due difetti
in uno: lavoro sprecato quando converge presto, e **risultato silenziosamente
privo di significato quando non converge**. `Numerics.bisect` si ferma sulla
tolleranza, **rifiuta** una radice non racchiusa invece di cercarla dentro, e
segnala la mancata convergenza. `bisectLog` per grandezze su molte decadi — le
pressioni parziali dello zolfo coprono trenta decadi, e bisecare la variabile
lineare sprecava quasi ogni iterazione nella decade sbagliata.

**`Mixing.evaluate`.** Allocava una matrice n², poi la percorreva due volte — una
per proprietà — allocando un array di somme parziali per ogni riga di ogni
percorso. Ora: array paralleli piatti, una passata sola che accumula entrambi i
denominatori, nessuna tupla boxata nel ciclo interno. Aggiunta anche la guardia
sul denominatore di Wilke non positivo, che prima produceva un infinito.

**Cast non verificato in `DataStore`.** La cache è non tipizzata e faceva
`entry.Value :?> 'T`. Due chiamanti che caricano lo stesso percorso con parser
diversi producevano una `InvalidCastException` che sfuggiva da una funzione il
cui contratto è restituire `Failure` invece di lanciare. Ora è un match tipizzato
con messaggio esplicito.

**Divisione non guardata** in `Mixing.bounds`: una conducibilità non positiva
avrebbe prodotto un infinito indistinguibile da un limite legittimo.

### Non corretto, e perché

**32 messaggi costruiti eagerly.** `warnIf condizione (Messaggio $"...")` valuta
l'argomento **sempre**, anche quando la condizione è falsa. Su percorsi chiamati
migliaia di volte è allocazione di stringhe pura. Non l'ho corretto perché
richiede una `warnIfLazy` nella libreria ROP, che è tua e fuori da questo
repository. È la modifica a maggior rapporto valore/rischio rimasta.

**`List.find` per chiave in `Equilibrium` e `Speciation`.** Ricerca lineare su
liste di 9–48 elementi dentro cicli. Irrilevante alle dimensioni attuali,
diventerebbe O(n²) con un database di migliaia di specie.

**Nessun benchmark.** Tutte le affermazioni qui sopra sono analisi statica e
conteggio di operazioni, non misure: il codice non è mai stato compilato. I numeri
sono ordini di grandezza, non misurazioni.

---

## 5. Sviluppi futuri, in ordine di valore

### 5.1 Compilare e far girare i test

Prima di tutto il resto. 275 test mai eseguiti.

### 5.2 Validare contro un caso consuntivato

Prendere **un caso Brembana & Rolle già a consuntivo** — preferibilmente un SRU,
dove le proprietà sono buone e la bassa pressione elimina le incognite di gas
reale — e confrontare U_ext calcolato contro quello dedotto dai dati di
esercizio. Quel singolo confronto dice più di qualunque modulo aggiuntivo.

### 5.3 Distribuzione idraulica con ferrule

L'ultimo anello mancante della catena di rating. Decide di quanto il tubo più
caldo si scosta dalla media, che è il numero che determina la vita del fascio.

### 5.4 Tarare i fouling sui dati storici

`Rf = 1/U_sporco − 1/U_pulito` da impianti in esercizio con una baseline pulita.
Vale più di qualunque correlazione e nessun software lo regala: sta nei rapporti
di collaudo.

### 5.5 Equazione di stato

Solo se il synloop ammoniaca è in scope. SRK/PR con le proprietà critiche già
presenti, oppure PC-SAFT per le miscele polari.

### 5.6 Dati radiativi SO₂ e H₂S

Da HITEMP via SLW, oppure acquisiti. Chiude l'unica lacuna radiativa che tocca
il servizio Claus.

### 5.7 Trasporto misurato per i vapori di zolfo

La lacuna più difficile da chiudere e quella che tocca il rating a 1400 °C.

### 5.8 `warnIfLazy` nella libreria ROP

Una variante che prenda `unit -> 'TMessage` invece del messaggio già costruito.
Elimina 32 costruzioni di stringa per chiamata sui percorsi caldi e non cambia
nessuna semantica.

### 5.9 Benchmark reale

`BenchmarkDotNet` su `Mixing.evaluate`, `Speciation.distribution` e un profilo
condensatore completo. Finché non esiste, ogni affermazione sulle prestazioni in
questo documento è una stima.

### 5.10 Un test che esegue i rigeneratori

La regola "ogni script che riscrive un file dati deve preservare le annotazioni
umane" è scritta nel README dopo il terzo incidente, e non è bastata: il quarto è
avvenuto comunque, perché era una regola e non un test. Un test che esegua
davvero gli importer e verifichi che il database non cambi è l'unica forma che
regge nel tempo.

---

## 6. Sintesi: di cosa fidarsi

| Dominio | Fiducia |
|---|---|
| Acqua/vapore IF97 | **Alta** — standard internazionale, verificato a 1e-9 |
| Cp gas puri | **Alta** — tre compilazioni indipendenti, <1 % su 22 specie |
| Termochimica (H, S, Gibbs, equilibri) | **Alta** — entalpie di formazione esatte |
| Trasporto gas puri (NASA CEA) | **Buona** — ~3 % viscosità, ~12 % conducibilità |
| Regola di miscelazione viscosità | **Alta** — Wilke esatto contro Cantera |
| Conducibilità di miscela | **Media** — 14 % di spread di modello |
| Liquidi DIPPR | **Buona** — ρ 0.8 %, h_vap 3.8 %, cp 6 % in range |
| Speciazione zolfo | **Buona** — riproduce il comportamento Claus senza parametri fittati |
| Convezione forzata | **Buona** in sé, **media** con la correzione di proprietà (25 %) |
| Ebollizione | **Media** — fattore 2 tra modelli |
| Radiazione gas | **Media** e **sistematicamente bassa** in servizio zolfo |
| Densità ad alta pressione | **Bassa** — gas ideale, 5–15 % oltre i 100 bar |
| Rating completo contro impianto | **Non validato** |
