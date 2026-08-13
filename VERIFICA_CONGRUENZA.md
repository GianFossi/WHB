# Verifica di congruenza — WHB HTS effluent, 848 tubi

Confronto fra il calcolo e i tre documenti forniti: datasheet, tabella bocchelli /
disegno d'assieme, disegno tubazioni LTI 7523‑00‑100‑01 rev.3.
Tutte le portate sono maggiorate del 10 % come da datasheet.

---

## 1. Bilancio termico — torna

| grandezza | datasheet (×1.1) | calcolato | scarto |
|---|---|---|---|
| Potenza scambiata | 116.614 MW | **116.54 MW** | **−0.06 %** |
| Vapore prodotto | 347'743 kg/h | **347'000 kg/h** | **−0.21 %** |
| Temperatura gas uscita miscelata | 355.0 °C | **355.0 °C** | esatta (col by-pass) |
| ΔP lato gas | ≤ 0.30 bar | **0.115 bar** | entro limite |
| T_sat a 117.84 bar | 323.3 °C | **323.29 °C** | esatto |
| MW miscela | 15.99 | **15.98** | −0.06 % |
| ρ gas IN / OUT | 5.36 / 10.48 kg/m³ | 5.38 / 10.54 | +0.4 / +0.6 % |
| c_p gas IN / OUT | 2.353 / 2.119 kJ/kgK | 2.342 / 2.075 | −0.5 / −2.1 % |

I −7 K sull'uscita gas sono l'unico scarto non trascurabile, e vanno nella direzione
prevedibile: il calcolo scambia leggermente più calore del datasheet a parità di superficie
(+0.13 % di potenza), quindi raffredda un po' di più. Le cause candidate, in ordine di peso:

- **µ e k della miscela.** Il datasheet li ricava con la media molare; il codice usa Wilke, che
  per miscele H₂ + gas pesanti dà valori più alti (per H₂/N₂ 50/50 a 300 K: Wilke 1.70·10⁻⁵ Pa·s,
  sperimentale ~1.68·10⁻⁵, media molare 1.34·10⁻⁵). Con `"miscelazione": "molare"` si riproduce
  la base del fornitore.
- **Irraggiamento del gas triatomico**, che il datasheet probabilmente non include.
- **Effetto d'imbocco** sul Nusselt, che vale il 40 % nei primi diametri.

Nessuna di queste è un errore: sono scelte di modello dichiarate. Il fatto che potenza e vapore
tornino entro lo 0.13 % dice che il bilancio complessivo è chiuso.

---

## 2. Geometria — torna

| verifica | esito |
|---|---|
| Ricostruzione del numero di tubi dalle bande | 38+67+82+92+77+68+68+77+92+82+67+38 = **848.0** ✓ |
| Superficie di scambio esterna | 1319.3 m² (π·38.1·12.998·848) ✓ |
| Campo tubi: OTL 1711.11 con anima ITL 571, passo 50.8 triangolare | area intubata = 848·p²·sin60° ✓ |
| Diaframmi OD 2015 in mantello ID 2025 | **5 mm di gioco per lato**: la corona periferica è di fatto chiusa, nessun by‑pass verticale ✓ |
| Ferrula: bore 26.7 → manicotto 30.0 → tubo ID 32.0 | la carta Saffil da 2×1 mm compressa riempie esattamente l'intercapedine ✓ |

La coerenza fra diaframma 2015 e mantello 2025 conferma che **il caso base è quello a corona
chiusa**: lo studio con diaframmi da 1700 mm resta un'ipotesi accademica (e peggiorativa, come
mostrato: DNBR da 0.71 a 0.65, valori della revisione con gas ideale; con il modello corrente il DNBR di riferimento è 0.73).

---

## 3. Tubazioni e bocchelli — tornano

| verifica | esito |
|---|---|
| Curve 90° Ø16" in distinta (pos. 109) | 10 = 2 (DC3‑DC4, una ciascuna) + 8 (DC5÷DC8, due ciascuna) ✓ |
| Curve 30° Ø16" in distinta (pos. 111) | 14 = 2 (DC3‑DC4) + 12 (DC5÷DC8, tre ciascuna) ✓ |
| ID da OD e schedule | 24" Sch120 → 610−2·46 = **518**; 18" → 457−2·35 = **387**; 16" → 406−2·31 = **344** — coincidono con le quote del disegno ✓ |
| Velocità nei downcomer alla portata calcolata | **1.70 m/s** (limite pratico 2–3 m/s) ✓ |
| Velocità nei riser | **3.07 m/s**, regime **anulare**, nessuno in slug ✓ |
| ρv² riser / downcomer | 3207 / 2088 kg/(m·s²), entro i limiti assunti ✓ |

Le sezioni sono coerenti fra loro: i 9 downcomer danno 0.885 m² e i 5 riser 0.858 m², che alla
densità della miscela producono proprio le velocità sopra. **Il dimensionamento reale è
equilibrato**, e infatti il calcolo non trova nulla da correggere sui bocchelli.

---

## 4. Le due assunzioni ancora aperte

### 4.1 Perdita nelle interne del corpo cilindrico — **decide il CR**

È l'unico numero che manca e vale il 31 % del battente. Sensibilità calcolata:

| interne drum [mbar] | CR | titolo uscita | DNBR min |
|---|---|---|---|
| 10 | **10.8** | 0.093 | 0.72 |
| 20 | **10.4** | 0.096 | 0.71 |
| **30** | **10.0** | **0.100** | 0.71 |
| 40 | 9.7 | 0.104 | 0.71 |
| **50 (assunto)** | **9.3** | 0.108 | 0.71 |
| 70 | 8.5 | 0.117 | 0.70 |
| 100 | 7.4 | 0.135 | 0.68 |

**Il criterio CR ≥ 10 è soddisfatto se e solo se le interne del drum costano meno di 30 mbar.**
Per un corpo cilindrico con cicloni a queste portate il campo tipico è 15–70 mbar, quindi la
risposta è genuinamente indeterminata finché non arriva il dato del costruttore. È l'unica
richiesta che farei prima di chiudere il calcolo di circolazione.

Nota: il DNBR è quasi insensibile (0.68–0.72 su tutto il campo). Il margine su DNB **non si
recupera con la circolazione**: va recuperato sul flusso termico di picco, cioè sulla ferrula.

### 4.2 Campata fra diaframmi — non è critica

| campata [m] | snellezza | ammissibile [MPa] | utilizzo |
|---|---|---|---|
| 1.0 | 40 | 113 | 31 % |
| 1.5 | 60 | 104 | 34 % |
| 2.0 | 80 | 92 | 38 % |
| 2.5 | 100 | 80 | 44 % |
| 3.0 | 121 | 65 | 54 % |

Anche con 3 m di campata l'utilizzo resta al 54 %: **l'instabilità dei tubi non è un problema di
questo apparecchio**, perché la compressione da dilatazione impedita è modesta (34.8 MPa) e i
tubi Ø38.1×3.05 sono tozzi (raggio d'inerzia 12.4 mm). Il dato esatto della campata si può
prendere dal GA con comodo.

---

## 5. Cosa resta da chiudere

| dato | serve per | stato |
|---|---|---|
| **Perdita interne corpo cilindrico** | rapporto di circolazione | **da costruttore drum — è il dato dirimente** |
| Posizioni assiali di R1÷R4 e DC1÷DC9 | distribuzione assiale del lavaggio, velocità nei plenum | da GA; ora di primo tentativo |
| Quota reale asse drum − asse WHB | battente motore | assunta 6 m su indicazione |
| Campata fra diaframmi | buckling | assunta 1.2 m, poco sensibile |
| Materiale reale dei tubi | limiti metallurgici, α, E | assunto SA‑213 T11 |
| Rugosità/Rf di progetto lato acqua | temperatura metallo | assunto 1.5·10⁻⁴ da datasheet ✓ |

---

## 6. Le criticità che restano, e non dipendono da assunzioni

1. **DNBR minimo 0.71** nella banda superiore a z = 0.21 m — insensibile a tutte le assunzioni
   aperte. Dipende dal flusso di picco (397 kW/m²) e dal titolo locale. La leva è la ferrula:
   da 200 a 500 mm il picco cala del 9 % e il DNBR sale a 0.75. Va detto che il criterio di
   Palen usato per il CHF di fascio è tarato su kettle ed è conservativo per un fascio con
   crossflow forzato: il DNBR va letto insieme al criterio pratico sul flusso massimo.
2. **Deposito lato acqua: 60 K di salto** contro 13 K di surriscaldamento reale. La temperatura
   metallica di 432 °C è governata dallo sporco, non dal regime di ebollizione. È un tema di
   condizionamento chimico dell'acqua, non di progetto termico.
3. **Surriscaldamento di parete 13.4 K contro un ΔT critico di 6.1 K** nella zona di picco.

---

## 7. Aggiornamenti dopo le ultime informazioni ricevute

### 7.1 Bocchelli non collegati — R5, DC9, R0A, R0B

| sigla | DN | posizione | stato |
|---|---|---|---|
| R5 | 6" | estremità fredda, cielo | presente, **non collegato** |
| DC9 | 4" | estremità fredda, fondo | presente, **non collegato** |
| R0A, R0B | da confermare | estremità calda, cielo | **non implementati** |

Il calcolo idraulico gira ora **senza** queste linee. L'effetto sul rapporto di circolazione è
piccolo (R5 e DC9 valgono insieme ~2 % della sezione: CR resta **9.3**), ma l'effetto sulla
**distribuzione** non lo è: le linee mancanti servivano proprio le due estremità, cioè le zone
meno lavate dalla circolazione trasversale. R0A/R0B stavano all'estremità **calda**, dove
cadono il picco di flusso e il DNBR minimo.

### 7.2 Valvola a farfalla del by-pass

Il by-pass nudo dissipa 0.4 mbar contro i 111 del fascio: senza strozzamento prenderebbe il
**13.8 %** della portata e l'uscita salirebbe a **430 °C**. La farfalla deve quindi lavorare a
**zeta ≈ 280**, cioè **24.6° di apertura** (65.4° di chiusura). Finestra ammessa **16.8°–30.7°**,
con vincolanti il **lavaggio minimo del liner** in basso e la **temperatura massima di processo**
in alto. Sensibilità **0.69 K/grado**.

Il punto di lavoro sta nella parte bassa della corsa: è il segno che il by-pass è
**sovradimensionato in diametro** rispetto alla portata che deve deviare. Non è un errore, ma un
diaframma fisso in serie sposterebbe il punto di lavoro verso il centro della corsa e renderebbe
la regolazione più stabile. Da valutare con il fornitore della valvola.

### 7.3 Stato di sollecitazione: il carico di pressione ribalta lo screening

Aggiungendo il **carico di estremità di pressione** (28.3 MN di trazione) al bilancio a piastre
fisse, i tubi passano da 35.8 MPa di **compressione** a **7 MPa di trazione**. Il buckling non è
quindi governato dall'esercizio ma dalla condizione **caldo/non in pressione** (avviamento,
depressurizzazione a caldo), dove l'utilizzo resta al **33 %**.

Tensione equivalente massima nei tubi: **107 MPa**, cioè il **55 % di Sy**, sulla faccia interna
nella zona di picco. Dominata dalla **compressione circonferenziale** (−127 MPa) dovuta alla
pressione esterna netta di 83 bar.

### 7.4 Il punto aperto nuovo: spessore del tubo di contenimento del by-pass

Con lo spessore assunto dal disegno (Ø300 su Ø284, cioè **8 mm**, in acciaio al carbonio a
328 °C) la verifica a **pressione esterna** dà **82 bar di collasso contro 83 richiesti**:
utilizzo **101 %**, e tensione equivalente al **107 % di Sy**. La verifica **non passa**.

Tre possibilità, in ordine di probabilità:

1. lo **spessore reale** è maggiore di quello dedotto dal disegno (probabile: basterebbero 10 mm);
2. i **diaframmi** irrigidiscono il tubo meglio di quanto assunto (il calcolo già li considera
   come anelli ogni 1.20 m; con gioco radiale nullo il margine migliora ancora);
3. il tubo ha **anelli di irrigidimento dedicati** non visibili sul disegno fornito.

È la prima domanda da fare al costruttore, insieme a quella sulle interne del corpo cilindrico.

---

## 8. Stato finale dopo l'introduzione del gas reale

Con il troncamento al secondo viriale (B(H2O-H2O) da IAPWS-IF97, B_ij da Pitzer con regole di
Prausnitz, regola esatta B_mix = ΣΣ y_i y_j B_ij) il bilancio si chiude:

| grandezza | datasheet (×1.1) | calcolato | scarto |
|---|---|---|---|
| Potenza | 116.614 MW | **116.54 MW** | **−0.06 %** |
| Vapore | 347'743 kg/h | **347'000 kg/h** | **−0.21 %** |
| T uscita miscelata | 355.0 °C | **355.0 °C** | esatta |
| ΔP lato gas | ≤ 300 mbar | 112 mbar | entro limite |

Il residuo di −0.93 % che restava era interamente dovuto al trattamento dell'acqua come gas
ideale: alla pressione parziale e alla temperatura di uscita il vapore d'acqua ha uno scostamento
del 3 % sul cp, che pesato sul 36.8 % in massa e integrato sul salto entalpico vale +0.87 %.

Questo chiude anche la questione dello **shift omogeneo**: se una parte della reazione avvenisse
nei tubi il bilancio si sposterebbe di qualche decimo di punto, e l'accordo allo 0.06 % con shift
congelata è la prova che nei tubi non avanza.
