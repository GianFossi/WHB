# Verifica di allineamento fra i documenti

Controllo incrociato di tutti gli elaborati prodotti, alla ricerca di discrepanze e
incongruenze. Sono state trovate **tre famiglie di problemi**: numeri residui di revisioni
precedenti, incoerenze di metodo, e un errore vero di modello. Tutte risolte o dichiarate.

---

## 1. Perché nascono le discrepanze

Il calcolo è passato per tre revisioni successive, e la documentazione è cresciuta insieme:

| revisione | materiale tubi | gas | potenza |
|---|---|---|---|
| R1 | SA-213 T11 | ideale | 115.53 MW |
| R2 | SA-213 T22 | ideale | 115.53 MW |
| **R3 (corrente)** | **SA-213 T22** | **reale, secondo viriale** | **116.54 MW** |

Ogni revisione ha spostato numeri che erano già stati citati nei capitoli precedenti. È il
meccanismo con cui si generano le incongruenze in qualunque documentazione di progetto che
evolve, e l'unico rimedio strutturale è che **i numeri siano generati dal calcolo, non
ricopiati**. Da questa revisione i file `maldistribuzione.txt` e `vibrazioni.txt` non sono più
scritti a parte: sono **estratti dallo stesso report**, quindi allineati per costruzione.

---

## 2. Discrepanze numeriche trovate e risolte

| # | dove | valore obsoleto | valore corrente | causa |
|---|---|---|---|---|
| 1 | DOC §5, tabella resistenze | parete tubo (T11) | parete tubo (T22) | cambio materiale |
| 2 | DOC §8, dilatazioni | «materiale T11» | «materiale T22» + rimando a §15.5 | cambio materiale |
| 3 | DOC §11, tabella tensioni | σ tubi 34.8 MPa | **19.4 MPa** (solo termico) | α del T22 minore |
| 4 | DOC §11, carico giunzione | 11.7 kN/tubo | **6.5 kN/tubo** | come sopra |
| 5 | DOC §11, buckling | utilizzo 32 % | **18 %** | come sopra |
| 6 | DOC §14, tabella by-pass | 115.52 MW / 344.4 t/h | **116.54 / 347** | gas reale |
| 7 | DOC §16, valvola | 24.6°, finestra 16.8–30.7° | **23.5°, 16.7–29.8°** | gas reale |
| 8 | DOC §18.5 | CR 12.1, α 0.46, DNBR 0.71→0.73 | **CR 12.0**, α 0.399 | gas reale |
| 9 | DOC §14, by-pass | frazione 1.53 %, liner 767 °C | **1.36 %, 761 °C** | gas reale |
| 10 | DOC §18.5, percorso vapore | 61.5 mbar | **62.6 mbar** | gas reale |
| 11 | VERIFICA §2 | DNBR 0.71 | **0.73** (nota di revisione aggiunta) | gas reale |

**Rimedio adottato.** In testa al DOC è stata inserita una **tabella dei valori autorevoli**
con la dichiarazione esplicita che i capitoli anteriori conservano i numeri delle revisioni
precedenti perché documentano il ragionamento, e che la fonte autorevole è il report generato
dal software.

---

## 3. Incongruenze di metodo trovate

### 3.1 Il DNBR presentato in due modi diversi

Nel quadro sintetico compariva **«DNBR 0.73 su minimo 2.0»**, come se fosse un criterio
superato o meno. Nel capitolo dedicato (§19.3, report §5d) si dimostra invece che **nessuno dei
modelli di flusso critico disponibili è tarato su questa geometria a questa pressione**, che
divergono di un ordine di grandezza, e che il DNBR va usato solo come indicatore relativo.

Le due affermazioni non possono convivere senza una nota. **Risolto**: il quadro sintetico
rimanda esplicitamente alla sezione dei modelli di CHF, e la voce è etichettata come indicatore.

### 3.2 Tre valori diversi dello stesso numero, tutti corretti

| grandezza | maglia fine 90×12 | maglia ridotta 40×8 | verifica di griglia 180×20 |
|---|---|---|---|
| flusso di picco | 395 kW/m² | 393 | 398 |
| farfalla | 23.5° | 27.1° | — |
| DNBR | 0.73 | 0.73 | 0.72 |
| frazione by-pass | 1.36 % | 1.83 % | — |

Non è un errore: le **curve di carico** girano a maglia ridotta per costo di calcolo, e la
convergenza di griglia (§19.1) autorizza il confronto **fra carichi**, non l'uso dei valori
assoluti. **Risolto**: dichiarato in ogni tabella di carico. *Per i valori assoluti fa fede
sempre la maglia fine.*

### 3.3 Il liner del by-pass

Fino alla revisione precedente il liner era trattato come **da verificare** («deve essere libero
di dilatare, controllare sul disegno»), e compariva fra i membri del sistema a piastre fisse con
una forza di 1.9 GPa. Il committente ha confermato che **è libero di dilatare**.

**Risolto**: il liner non figura più fra i membri strutturali; la forza di 1.9 GPa resta nel
report ma etichettata come **ipotesi non applicabile**, riportata solo per documentare l'ordine
di grandezza che il giunto scorrevole evita (159 mm di allungamento libero a 700 °C).

---

## 4. Errore di modello trovato e corretto: la maldistribuzione

**Questo era un errore vero, non una disallineatura.**

La prima versione dell'analisi di maldistribuzione simulava il tubo più caricato **rifacendo il
calcolo dell'intero apparecchio con la portata maggiorata**. È sbagliato: così facendo si ottiene
un apparecchio più potente — la potenza saliva da 116 a 150 MW e la produzione di vapore con
essa — non un tubo sbilanciato dentro un apparecchio invariato. La colonna «potenza» di quella
tabella era priva di significato fisico, e anche il resto era contaminato, perché cambiavano
circolazione, titolo e flusso critico.

**Modello corretto**, ora implementato: gli 848 tubi sono canali **in parallelo che non si
scambiano calore fra loro**, quindi un tubo sbilanciato non altera né la circolazione né la
produzione di vapore. Si marcia **un solo tubo** con la portata maggiorata, tenendo congelato
tutto il lato mantello — temperatura di saturazione, coefficiente di ebollizione, sporcamento,
flusso critico locale. Cambia solo la resistenza lato gas, che è quella governata dalla portata.

Il risultato è ora nel report principale (sezione 6f) ed è estratto nel file
`maldistribuzione.txt`: stessi numeri, nessuna rielaborazione.

---

## 5. Cosa resta dichiaratamente non allineabile

Non sono incongruenze da correggere ma **incertezze da dichiarare**, e come tali compaiono nella
sezione «Ipotesi principali» del report:

| voce | stato | effetto se cambia |
|---|---|---|
| K del convogliatore del corpo cilindrico | **aperto** | CR fra 12 e 8 |
| spessore del tubo di contenimento del by-pass | **aperto** | è l'unica verifica non conforme |
| posizioni assiali dei bocchelli | **aperto** | distribuzione del lavaggio, non il bilancio |
| K di Connors e smorzamento per le vibrazioni | **assunto conservativo** | V/Vcrit fra 0.97 e 0.46 |
| passo reale dei diaframmi (variabile) | **assunto governante** | V/Vcrit va con il quadrato |
| modello di flusso critico | **nessuno applicabile** | DNBR fra 0.8 e 8 |

---

## 6. Regola adottata da questa revisione

1. **I file di dettaglio sono estratti dal report**, non riscritti: allineamento per costruzione.
2. **Il PDF è generato dagli stessi txt** dopo ogni analisi, quindi non può divergere.
3. **La documentazione di metodo (questo DOC) non ripete i risultati**, li richiama: la tabella
   dei valori autorevoli in testa è l'unico punto in cui compaiono, ed è aggiornata a ogni giro.

---

## 7. Check finale: tre correzioni ulteriori

Rilettura completa del codice, del selftest e dei datasheet a caccia di cose sfuggite.

### 7.1 Il selftest falliva su un controllo, e il colpevole non era il codice

Il confronto «media molare di k» contro il datasheet dava 0.254 contro 0.172 W/(m·K), cioè
**+47 %**, e il controllo risultava fallito da sempre. Non era un errore di calcolo: era
un'interpretazione sbagliata di che cosa il datasheet chiami «media molare».

Per la conducibilità la media molare **semplice** non ha senso fisico in una miscela ricca di
idrogeno: l'H₂ ha k altissimo e massa molare minima, quindi in una media pesata sulle frazioni
molari pesa quanto le specie pesanti e trascina il risultato verso l'alto. La regola che i
datasheet usano è quella di **Herning-Zipperer**, pesata su √M:

```
  k_mix = Σ y_i √M_i k_i  /  Σ y_i √M_i
```

Con questa: **0.161 contro 0.172 del datasheet**, entro il 6 %. **Il selftest ora passa
integralmente.** Non cambia nulla nei risultati (il calcolo usa Wassiljewa-Mason-Saxena), ma
elimina un falso allarme che rimaneva acceso a ogni verifica.

### 7.2 Il corpo cilindrico serve DUE apparecchi, e il percorso vapore lo ignorava

Dal datasheet 7523-03-DSS-01: il drum 3-D-4201 riceve **106.013 MW × 1.1 da questo WHB e
15.573 MW × 1.1 da 3-E-1801**. I bocchelli lo confermano: C1 = 4 riser da 24" da 3-E-1401,
C2 = 4 riser da 10" da 3-E-1801.

*(Nota di congruenza: 106.013 × 1.1 = 116.61 MW, che coincide esattamente con il datasheet del
WHB. Le due schede sono coerenti fra loro.)*

Il modello del corpo cilindrico faceva passare per demister, camini e bocchello di uscita **il
solo vapore di questo WHB**. Sbagliato: quel percorso vede il vapore di entrambi.

| | prima | dopo |
|---|---|---|
| vapore sul percorso vapore | 96.5 kg/s | **110.6 kg/s** |
| Δp dal pelo libero all'uscita | 62.6 mbar | **82.4 mbar** |
| velocità superficiale al pelo libero | 0.036 m/s | **0.0415 m/s** |
| utilizzo del limite di Souders-Brown | 27 % | **31 %** |

Il percorso di **circolazione** resta invariato — i convogliatori C1 vedono solo questo WHB — e
quindi **CR, DNBR e bilancio termico non cambiano**. Cambia il Δp consegnato in rete, che è
un'informazione di processo, e il margine di separazione, che resta comunque ampio.

### 7.3 Il tempo di dry-out considerava solo l'acqua del mantello

Il transitorio dava «155 s all'asciutto», presentato come *il* margine di protezione. È il
numero giusto per **un solo** scenario, e non è quello più probabile.

| scenario | inventario disponibile | tempo |
|---|---|---|
| **1 — perdita acqua alimento, circolazione attiva** | mantello + corpo cilindrico = **49.1 t** | **8.5 min** |
| **2 — blocco della circolazione, downcomer ostruiti** | solo mantello = **15.0 t** | **2.6 min** |

Se manca l'acqua alimento ma la circolazione funziona, i downcomer continuano a scendere per
gravità e si consuma tutto l'inventario, **34.1 t dei quali nel corpo cilindrico**: 8.5 minuti,
margine confortevole, e spiega perché il basso livello nel drum è una protezione efficace.

Lo scenario severo è il secondo, ed è **il più insidioso**: se si blocca la circolazione il
livello nel corpo cilindrico **non scende**, quindi la protezione di basso livello non
interviene. La grandezza che rivela il problema è la **differenza di temperatura fra mantello e
corpo cilindrico**, non il livello. È una raccomandazione di strumentazione, non di calcolo.

*Nota: l'inventario del drum qui calcolato (34.1 t = 51.8 m³) considera il solo cilindro. Il
datasheet, includendo i fondi, dà 60.2 m³ al livello normale. Il valore usato è quindi
conservativo del 14 %.*

### 7.4 Cosa resta fuori dal modello, verificato e giudicato trascurabile

- **Spurgo continuo** 7 200 kg/h × 1.1 (2 % dell'acqua alimento): 0.002 kg/s contro 890 kg/s di
  circolazione. Ininfluente sull'idraulica; già contabilizzato nel bilancio del corpo cilindrico
  dal costruttore.
- **Interazione fra i due circuiti** sullo stesso corpo cilindrico: i downcomer B1 e B2 pescano
  dalla stessa massa d'acqua. Al punto di progetto i due anelli sono indipendenti; in transitorio
  no. Fuori dallo scopo di un calcolo stazionario.
