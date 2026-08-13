# WHB / PGC a tubi da fumo — correlazioni, problematiche di esercizio e circolazione naturale

Nota tecnica di supporto al software `whb` (F#/.NET).
Configurazione di riferimento: **gas di processo nei tubi, acqua/vapore in ebollizione a mantello,
circolazione naturale con corpo cilindrico (steam drum) sopraelevato**.
Servizio: syngas da reforming (impianti ammoniaca / metanolo / idrogeno), 25–40 bar, 1000 → 350 °C.

---

> **NOTA DI REVISIONE E ALLINEAMENTO**
>
> Questo documento è cresciuto insieme al calcolo: i capitoli sono stati scritti man mano che il
> modello evolveva, e alcuni riportano valori di revisioni intermedie. Le tre revisioni sono:
> **R1** materiale T11 e gas ideale, **R2** materiale T22 e gas ideale, **R3** (corrente) materiale
> **SA-213 T22** e **gas reale** con secondo viriale.
>
> **I valori autorevoli sono solo quelli della tabella qui sotto e dei capitoli 15, 18.5, 19, 20,
> 21 e 22.** Dove un capitolo precedente cita un numero di revisione anteriore, il numero è
> conservato perché documenta il ragionamento con cui si è arrivati al risultato, e il rimando al
> valore corrente è indicato nel testo. Il report generato dal software è sempre la fonte
> autorevole: questo documento spiega il metodo, non sostituisce i risultati.
>
> | grandezza | valore corrente (R3) |
> |---|---|
> | potenza / vapore | **116.54 MW / 347 t/h** (datasheet 116.61 / 347.7 → −0.06 %) |
> | temperatura di uscita miscelata | **355.0 °C** |
> | frazione deviata nel by-pass | **1.36 %** |
> | flusso termico di picco | **395 kW/m²** a z = 0.21 m |
> | temperatura metallica massima | **434 °C** (limite T22: 580 °C) |
> | DNBR minimo (indicatore, vedi §19.3) | **0.73** |
> | rapporto di circolazione | **12.0** |
> | frazione di vuoto massima | **0.399** |
> | perdita interne del corpo cilindrico | **9.95 mbar** (circolazione) / 62.6 mbar (percorso vapore) |
> | dilatazione differenziale tubo-mantello | **2.34 mm** → 19.4 MPa, 6.5 kN/tubo |
> | tensione equivalente massima | **193 MPa = 103 % di Sy** (tubo di contenimento by-pass) |
> | farfalla in esercizio normale | **23.5°** (finestra 16.7°–29.8°) |
> | V/Vcrit vibrazioni, banda 11 | **0.974** (campata max 1.09 m) |


## 1. Lato gas (interno tubo)

### 1.1 Convezione forzata monofase

Tutte nella forma `Nu = h·d_i/k`, proprietà valutate alla temperatura di bulk salvo diverso avviso.

| Correlazione | Espressione | Campo | Note per WHB |
|---|---|---|---|
| **Dittus–Boelter** (1930) | `Nu = 0.023 Re^0.8 Pr^n`, n = 0.3 in raffreddamento | Re > 10⁴, 0.6 < Pr < 160, L/d > 10 | Semplice, ±25 %. Sovrastima con forti ΔT parete-gas |
| **Colburn** (1933) | `Nu = 0.023 Re^0.8 Pr^(1/3)` | idem | Base storica del fattore j |
| **Sieder–Tate** (1936) | `Nu = 0.027 Re^0.8 Pr^(1/3) (µ_b/µ_w)^0.14` | proprietà molto variabili | Correzione di viscosità già inclusa |
| **Petukhov–Kirillov** (1958) | `Nu = (f/8)·Re·Pr / [1.07 + 12.7√(f/8)(Pr^(2/3) − 1)]` | 10⁴ < Re < 5·10⁶ | Base teorica solida |
| **Gnielinski** (1976) | `Nu = (f/8)(Re − 1000)Pr / [1 + 12.7√(f/8)(Pr^(2/3) − 1)]` | 2300 < Re < 5·10⁶ | **Raccomandata**: ±10 %, copre la transizione |
| **Hausen** (1959) | `Nu = 0.116 (Re^(2/3) − 125) Pr^(1/3) (µ_b/µ_w)^0.14` | 2300 < Re < 10⁴ | Utile a carico ridotto |

Fattore d'attrito (Darcy) associato:

- **Filonenko / Petukhov**: `f = (1.82 log₁₀Re − 1.64)^(−2)` (liscio)
- **Blasius**: `f = 0.3164 Re^(−0.25)` (3·10³ < Re < 10⁵)
- **Colebrook–White** per tubo rugoso (implicito, risolto iterativamente nel codice)

### 1.2 Correzioni indispensabili nei WHB

**(a) Proprietà variabili (gas fortemente raffreddato).** Kays & London:

```
Nu / Nu_prop.cost. = (T_w / T_b)^n     con n = −0.5 per gas in raffreddamento
```

Con T_gas ≈ 1270 K e T_parete ≈ 630 K il fattore vale ≈ 1.42: ignorarlo significa sottostimare
h del 40 % nella zona più critica.

**(b) Regione d'ingresso.** Il picco di flusso termico all'imbocco dei tubi è la causa n.1 dei
cedimenti in prossimità della piastra tubiera:

```
Nu(x) / Nu_fd = 1 + C (d/x)^0.7        C ≈ 1.4 (imbocco a spigolo vivo), 0.7 (raccordato)
```

Valida per x/d ≳ 2 (nel codice il rapporto è congelato al valore in x = 2d).

**(c) Irraggiamento del gas triatomico.** A 30–40 bar la pressione parziale di H₂O + CO₂ è alta
e, benché il cammino ottico in un tubo sia corto (s ≈ 0.9·d_i), il prodotto p_n·s è confrontabile
con quello di una camera di combustione a pressione atmosferica. Metodo CKTI/Blokh implementato:

```
k_g = [ (0.78 + 1.6 r_H2O) / √(p_n s) − 0.1 ] · (1 − 0.37 T/1000)     [1/(m·MPa)]
ε_g = 1 − exp(−k_g · p_n · s)
h_rad = ε_g · (ε_w+1)/2 · σ · (T_g⁴ − T_w⁴)/(T_g − T_w)
```

Nel caso di riferimento ε_g ≈ 0.12 → h_rad ≈ 50 W/m²K contro h_conv ≈ 1900 W/m²K: circa il 2–3 %,
non trascurabile nel bilancio delle temperature metalliche del tratto caldo.

**(d) Ferrula.** Il tratto iniziale (300–500 mm) è protetto da una ferrula refrattaria:
la resistenza conduttiva del refrattario (k ≈ 0.35 W/m·K) è ~15 volte quella del film gas e
abbatte il flusso termico locale di un ordine di grandezza. Il picco di flusso si sposta
**appena a valle dell'uscita della ferrula**, che è il punto realmente critico da verificare.

### 1.3 Proprietà del gas

- `c_p` da polinomi `c_p/R = A + B·T + C·T² + D/T²` (Smith–Van Ness–Abbott) per specie
- `µ` da Sutherland; per H₂O il limite di gas diluito IAPWS (µ₀(T))
- `k` da Eucken modificato `k = (µ/M)(1.32 c_v + 1.77 R)`
- miscelazione: **Wilke** per µ, **Wassiljewa/Mason–Saxena** per k

Attenzione: syngas ricco di H₂ ha `k` 5–8 volte quello dei fumi → `h` molto più alto a parità
di velocità, e quindi flussi termici molto più alti.

### 1.4 Sporcamento lato gas

R_f tipici: syngas pulito da reforming 1.5–3·10⁻⁴ m²K/W; gas con carbon black o catalyst fines
fino a 8·10⁻⁴. Il fouling lato gas **abbassa** la temperatura del metallo (è a monte della parete):
progettare "pulito" per le temperature metalliche e "sporco" per la superficie è la prassi corretta.

---

## 2. Lato acqua (mantello, ebollizione)

### 2.1 Ebollizione nucleata a bagno

| Correlazione | Espressione | Note |
|---|---|---|
| **Mostinski** (1963) | `h = 0.00417 p_c^0.69 q^0.7 F_p`, `F_p = 1.8 p_r^0.17 + 4 p_r^1.2 + 10 p_r^10` (p_c in kPa) | Stati corrispondenti, robusta, default |
| **Cooper** (1984) | `h = 55 p_r^(0.12−0.2 log₁₀R_p) (−log₁₀p_r)^(−0.55) M^(−0.5) q^0.67` | Include la rugosità R_p [µm] |
| **Gorenflo** (VDI) | `h/h₀ = F_p (q/q₀)^n (R_p/R_p₀)^0.133`, h₀ = 5600 W/m²K, q₀ = 20 kW/m² per l'acqua | Riferimento VDI Heat Atlas |
| **Rohsenow** (1952) | `q = µ_l h_fg √(g Δρ/σ) [c_pl ΔT_e /(C_sf h_fg Pr_l^s)]³`, C_sf = 0.013 (acqua/acciaio), s = 1 | Esplicita in ΔT_e; sensibile a C_sf |
| **Cornwell–Houston** (1994) | `Nu = 9.7 p_c^0.5 F_p Re_b^0.67 Pr_l^0.4`, `Re_b = q d/(µ_l h_fg)` | Tarata su tubi e fasci |

A 105 bar e q = 200 kW/m² le prime quattro danno 56 000–84 000 W/m²K: la resistenza lato acqua
è **trascurabile** (2–5 % del totale). Ciò che conta non è il valore di h, ma **fino a che flusso
termico l'ebollizione nucleata resta stabile**.

### 2.2 Modello di fascio (Palen)

```
h_fascio = h_nb · F_b · F_c + h_conv
```

- `F_b` fattore di fascio: 1.0 (tubo singolo) → 3 (fascio fitto); **valore conservativo 1.5**
- `F_c` correzione di miscela: 1.0 per fluido puro (acqua)
- `h_conv`: il maggiore fra convezione naturale (Churchill–Chu) e convezione bifase

### 2.3 Ebollizione convettiva (crossflow con titolo)

- **Chen** (1966): `h = S·h_nb(Forster–Zuber) + F·h_cb`, con `F` da parametro di Martinelli
  `X_tt` e `S = [1 + 2.53·10⁻⁶ Re_tp^1.17]^(−1)`, `Re_tp = Re_l F^1.25`
- **Zukauskas** per la parte convettiva monofase in attraversamento fascio:
  `Nu = C Re_max^m Pr^0.36 (Pr/Pr_w)^0.25`

### 2.4 Flusso termico critico (CHF)

| Metodo | Espressione | Uso |
|---|---|---|
| **Zuber** | `q_max = 0.131 h_fg ρ_v^0.5 [σ g (ρ_l−ρ_v)]^0.25` | Piastra piana |
| **Lienhard–Dhir** | Zuber × correzione geometrica su R' = R/L_c, `L_c = √(σ/gΔρ)` | Cilindro orizzontale |
| **Mostinski** | `q_max = 0.368 p_c p_r^0.35 (1−p_r)^0.9` [kW/m², p_c in kPa] | Stati corrispondenti |
| **Palen (fascio)** | `q_max,fascio = φ_b · q_max,tubo`, `φ_b = 3.1 ψ`, `ψ = D_b L / A_o`, limitato a [0.1, 1] | Kettle |

**Avvertenza fondamentale**: il criterio di Palen è tarato su ribollitori kettle, dove il moto
è solo per galleggiamento all'interno del fascio. In un WHB a circolazione naturale c'è un
crossflow forzato di 0.5–3 m/s e il criterio è fortemente conservativo. Nel caso di riferimento
dà φ_b = 0.10 → 330 kW/m² contro 3300 kW/m² del tubo singolo.

Nella pratica industriale il criterio dominante non è il CHF teorico ma il **limite di flusso
termico**: 250–350 kW/m² per ebollizione a mantello in circolazione naturale con acqua di buona
qualità (Kern: U_max ≈ 5800 W/m²K per l'acqua; heater di greggio limitati a 25 kW/m² per fouling).
Il software riporta entrambi i criteri.

### 2.5 Sporcamento lato acqua — il vero killer

R_f = 1·10⁻⁴ m²K/W (deposito di magnetite di poche decine di µm) a q = 450 kW/m² introduce un
salto termico di **45 K** fra parete metallica e interfaccia bollente. Nel caso di riferimento:

- surriscaldamento *reale* di parete (q/h_eb): **2.4 K** → ebollizione nucleata perfettamente stabile
- salto attraverso il deposito: **44.7 K**
- ΔT totale metallo-Tsat: **47.2 K**

Il metallo si scalda per il deposito, non per il DNB. È il meccanismo più frequente di cedimento
reale, e cresce in modo autoacceleratore (più caldo → più deposito → più caldo).

---

## 3. Steam blanketing e altre problematiche tipiche

### 3.1 Steam blanketing (API 571 §4.2.11)

**Meccanismo.** Quando il flusso termico locale supera il CHF, o quando il flusso d'acqua sul
tubo si interrompe, si forma uno strato continuo di vapore che isola la parete
(*film boiling* / DNB). Il coefficiente di scambio crolla di 1–2 ordini di grandezza e la
temperatura del metallo sale rapidamente verso quella del gas.

**Conseguenze.** Due modalità distinte:

- **short-term overheating**: ΔT di centinaia di gradi in minuti → rottura "a bocca di pesce"
  (*fish-mouth*), bordi assottigliati e affilati, deformazione plastica evidente, grani deformati;
- **long-term overheating / creep**: temperatura sopra il limite di progetto per mesi/anni →
  ingrossamento del grano, sferoidizzazione dei carburi, cricche intergranulari, rottura a
  bordi spessi e senza deformazione apprezzabile.

**Cause tipiche in un WHB a tubi da fumo orizzontale:**

1. picco di flusso termico all'uscita della ferrula;
2. circolazione insufficiente (CR troppo basso, downcomer sottodimensionati, dislivello drum
   insufficiente);
3. **stratificazione nel mantello orizzontale**: se la frazione di vuoto in uscita dal fascio
   supera ~0.7–0.8, i ranghi superiori restano scoperti;
4. maldistribuzione assiale: bocchelli riser/downcomer troppo distanti fra loro rispetto alla
   lunghezza del mantello;
5. basso livello nel drum, o transitori di livello durante variazioni di carico;
6. deposito lato acqua (vedi §2.5), che riduce il CHF locale e alza la parete.

**Prevenzione (implementata come diagnostica nel software):**

- limitare il flusso termico di picco (ferrula più lunga e/o più spessa, ferrula a doppio stadio);
- verificare DNBR di fascio ≥ 2–3 e ΔT_parete « ΔT_critico;
- CR = 10–30 (WHB a tubi da fumo, 40–100 bar); vedi §4;
- α in uscita dal fascio < 0.7, titolo in uscita < 0.10–0.12;
- bocchelli distribuiti sui **baricentri di bande a pari produzione di vapore** (più fitti
  all'estremità calda), downcomer allineati verticalmente ai riser;
- controllo chimico rigoroso dell'acqua (fosfati/AVT, conducibilità, silice) e pulizia chimica.

### 3.2 Altre problematiche tipiche

**Metal dusting (carburizzazione catastrofica).** Gas con CO/CH₄ ad alta attività di carbonio
in finestra 400–800 °C (fino a 900 °C per leghe austenitiche). Il caso di riferimento con
CO 8 % ha la parete interna fra 320 e 410 °C: al limite inferiore della finestra, ma il tratto
dentro/sotto la ferrula e la zona d'ingresso vanno verificati. Mitigazioni: leghe alto-Cr/Ni,
alluminizzazione, rivestimenti, dosaggio di S, e soprattutto tenere la parete fuori finestra.

**Ferrule e giunzione tubo/piastra tubiera.** La piastra tubiera lato ingresso è il componente
più sollecitato: gradiente termico radiale, tensioni di espansione differenziale fra tubi caldi
e freddi, crevice corrosion nell'interstizio tubo/foro. Soluzioni industriali: mandrinatura
idraulica per tutto lo spessore, saldatura interna, layout HOT/COLD alternato (BORSIG synloop
WHB, che mantiene la piastra sotto 380 °C, sotto la temperatura di nitrurazione), rivestimento
refrattario della camera d'ingresso.

**Cedimento della ferrula.** Se il refrattario si sfalda o la fibra si compatta, il tubo si trova
esposto al gas a piena temperatura con un flusso locale 5–10 volte superiore a quello di progetto:
cedimento rapido. Ispezione boroscopica periodica.

**Vibrazioni indotte dal flusso.** Sul lato mantello, un crossflow bifase con velocità della
miscela > 4–5 m/s può portare a instabilità fluidoelastica dei tubi nelle campate lunghe:
verificare le luci fra diaframmi/supporti.

**Erosione lato gas.** Velocità gas > 50–60 m/s con particolato (carbon black, catalyst fines)
erode l'imbocco dei tubi. Nel caso di riferimento 28 m/s all'ingresso: accettabile.

**Corrosione sotto tensione (SCC).** Acciai austenitici a contatto con acqua di caldaia contenente
cloruri: evitare AISI 3xx sui tubi; il layout BORSIG usa tubi ferritici proprio per questo.

**Instabilità di circolazione.** A carico ridotto il battente motore cala più rapidamente delle
perdite: verificare il CR anche al 50 % del carico. Circuiti paralleli con lunghezze diverse
possono innescare oscillazioni di densità (density-wave).

**Accumulo di fanghi e sali.** Velocità nel downcomer < 0.5 m/s e nelle zone morte del mantello
favorisce depositi; il blowdown continuo va posizionato nel punto più basso all'estremità fredda.

---

## 4. Rapporto di circolazione (CR) — metodo di calcolo

### 4.1 Definizione

```
CR = W_circolante / W_vapore_generato          x_uscita = 1 / CR
```

Valori di riferimento (Ganapathy): caldaie HP a tubi d'acqua 100–145 bar → CR 4–8;
**caldaie a recupero 14–70 bar → CR 15–50**; circolazione forzata → CR 2–6.
Per WHB syngas a tubi da fumo a 100–125 bar il campo di progetto è tipicamente **CR = 10–30**.

### 4.2 Geometria del circuito e altezze

Assumendo come riferimento l'**asse del WHB** e detto ΔZ il dislivello **asse corpo cilindrico –
asse WHB** (dato di input chiave):

```
z_livello_acqua = ΔZ + offset_livello          (offset = 0 se NWL sull'asse del drum)

H_dc    = z_livello_acqua + D_mantello/2       colonna discendente (liquido saturo)
H_sh    = D_fascio                             tratto riscaldato attraversato (verticale)
H_riser = z_livello_acqua − D_mantello/2       colonna montante bifase
```

### 4.3 Bilancio

**Battente motore disponibile:**

```
Δp_motore = g · [ ρ_l · H_dc − ρ̄_mantello · H_sh − ρ̄_riser · H_riser ]
```

con `ρ̄ = α ρ_v + (1−α) ρ_l` e α da modello di frazione di vuoto
(omogeneo, Chisholm slip, **Zuber–Findlay drift-flux** `α = j_v /(C₀ j + V_gj)`, C₀ = 1.13,
`V_gj = 1.53 [σ g Δρ/ρ_l²]^0.25`, oppure Smith). La densità media nel mantello è integrata
sul titolo da 0 a x_uscita.

**Perdite di carico da bilanciare:**

1. downcomer: `(f L/D + ΣK) ρ_l V²/2`
2. bocchello d'ingresso al mantello (espansione brusca, K ≈ 1)
3. **attraversamento del fascio** (crossflow bifase su banco di tubi orizzontali):
   `Δp = f_b · N_ranghi · G_max²/(2ρ)` con f_b di Jakob
   (sfalsato: `f_b = [0.25 + 0.1175/(a−1)^1.08] Re^(−0.16)`, a = passo/d_e)
   moltiplicato per il moltiplicatore bifase di **Ishihara** `φ²_l = 1 + C/X_tt + 1/X_tt²`, C = 8
4. raccolta e bocchello riser (K ≈ 0.7)
5. riser: attrito bifase (**Friedel** / Lockhart–Martinelli / Chisholm-B / omogeneo),
   accelerazione `Δp_a = G²[ (x²/ρ_vα + (1−x)²/ρ_l(1−α)) ]_in^out`, perdite localizzate
6. interne del corpo cilindrico (separatori, ciclone): 1.5–7 kPa

**Soluzione:** si itera su CR fino a `Δp_motore(CR) = Σ Δp_perdite(CR)`
(bisezione su CR ∈ [1.2, 400] nel codice). Il residuo è monotòno decrescente in CR: la
soluzione è unica.

### 4.4 Distribuzione assiale (circuiti in parallelo)

Poiché il flusso termico decade lungo il tubo (il gas si raffredda), **la generazione di vapore
per metro non è uniforme**: nel caso di riferimento passa da 428 kW/m² a z = 0.4 m a 31 kW/m² a
z = 6 m, un fattore 14.

Il mantello viene quindi discretizzato in fette assiali trattate come **circuiti in parallelo con
lo stesso battente disponibile a valle del downcomer comune**:

```
Δp_disponibile,i = ρ_l g H_dc − Δp_downcomer(W_tot) − Δp_drum
                   − ρ̄_mantello,i g H_sh − ρ̄_riser,i g H_riser
Δp_disponibile,i = Δp_fascio,i(W_i) + Δp_riser,i(W_i)
```

Da cui W_i per ciascuna fetta, il **CR locale** `CR_i = W_i / W_vapore,i`, e i profili di:

- flusso di massa in attraversamento del fascio `G_cross,i`
- velocità del liquido in ingresso e della miscela in uscita dal fascio
- velocità della fase vapore `u_v = G x /(ρ_v α)`
- frazione di vuoto in uscita

Le **velocità assiali** nei canali periferici (semicorone fra mantello e fascio, sopra e sotto)
si ottengono integrando la domanda di ciascuna fetta a partire dal bocchello che la serve.

### 4.5 Sensibilità a ΔZ (caso di riferimento)

Il battente motore cresce quasi linearmente con ΔZ, mentre le perdite crescono con W²:
`CR ∝ √(ΔZ)` in prima approssimazione. Nel caso di riferimento (600 tubi Ø38.1×6 m, 105 bar,
157 t/h di vapore):

| ΔZ [m] | CR | x_uscita |
|---|---|---|
| 7.0 | ~8 (4 riser Ø317) | 0.126 |
| 8.0 | 11.6 (6 riser Ø317) | 0.086 |

Il termine dominante nelle perdite è il **riser** (112 mbar su 224 mbar): agire sul numero/diametro
dei riser è più efficace che alzare il drum.

---

## 5. Dimensionamento e posizionamento dei bocchelli

### 5.1 Criteri di velocità e ρv²

| Servizio | Velocità tipica | ρv² limite |
|---|---|---|
| Downcomer (liquido saturo) | 1.5–2.5 m/s | 2200–3000 kg/(m·s²) — TEMA RCB-4.61: protezione anti-impingement obbligatoria oltre 744 kg/(m·s²) per liquidi in ebollizione |
| Riser (miscela bifase) | 3–6 m/s | 4000–6000 kg/(m·s²); protezione anti-impingement sempre richiesta per bifase |
| Blowdown | 1–2 m/s | — |

Nel software la velocità ammessa è `min(v_obiettivo, √(ρv²_max/ρ))`, il diametro viene scelto
sulla tabella ASME B36.10 (Sch 40/80/160) e il numero di bocchelli viene aumentato finché il
criterio è rispettato.

### 5.2 Posizionamento assiale

Regola implementata: **baricentri di bande a pari produzione di vapore**.
La lunghezza del mantello viene divisa in N zone che generano ciascuna 1/N del vapore totale,
e il bocchello è posto nel baricentro (pesato sul flusso termico) di ogni zona.
Poiché il flusso termico decade lungo z, i bocchelli risultano **più fitti verso l'estremità
calda**, dove serve più portata: è la disposizione che minimizza le velocità assiali nel
mantello e quindi la maldistribuzione.

Nel caso di riferimento (L = 6 m): 3 riser da 18" a z = 0.60 / 1.60 / 3.72 m, downcomer allineati
verticalmente sugli stessi assi, blowdown a z = 5.70 m (estremità fredda, punto più basso).

Numero minimo consigliato: ~1 bocchello ogni 2 m di mantello, mai meno di 2.

---

---

## 6. Modello 2-D e verifica sull'apparecchio reale

### 6.1 Discretizzazione

Il calcolo è discretizzato su una griglia **Nz × Ny**:

- **Nz sezioni lungo l'asse dell'apparecchio** (default 60 su 12.998 m ≈ 217 mm per sezione).
  Il gas marcia lungo z con bilancio entalpico e di quantità di moto.
- **Ny bande orizzontali del fascio** (default 12). Ogni banda contiene un numero di tubi
  ricavato dalla geometria anulare del fascio (OTL 1711.11 mm, anima non intubata ITL 571 mm,
  passo 50.8 mm triangolare 60°, area per tubo = p²·sin60° = 2234.9 mm²):

  ```
  38 / 67 / 82 / 92 / 77 / 68 / 68 / 77 / 92 / 82 / 67 / 38   (somma 848 ✓)
  ```

**Perché serve la seconda dimensione.** L'acqua attraversa il fascio dal basso verso l'alto:
il titolo entra a zero nella banda inferiore e cresce banda dopo banda. Nella stessa sezione
assiale i tubi bassi lavorano in acqua praticamente satura, quelli alti in una miscela già
ricca di vapore. Nel caso reale, alla sezione più calda:

| banda | y [m] | n. tubi | x uscita | alpha | DNBR |
|---|---|---|---|---|---|
| 0 (bassa) | −0.784 | 38 | 0.006 | 0.02 | 0.82 |
| 5 | −0.071 | 68 | 0.067 | 0.32 | 0.77 |
| 11 (alta) | +0.784 | 38 | 0.133 | 0.51 | 0.71 |

La **temperatura d'uscita del gas** varia invece pochissimo fra le bande (349.6 → 350.2 °C,
spread ~1 K): la resistenza lato acqua è talmente piccola rispetto a quella lato gas che
anche un dimezzamento di h_ebollizione sposta poco il bilancio. **L'effetto fascio non si vede
sul gas, si vede sul margine di DNB**: è la frazione di vuoto locale, non la temperatura,
la variabile che discrimina fra la banda bassa e quella alta.

### 6.2 Ferrula multistrato (da disegno DTL. FERULE)

La ferrula non è un semplice manicotto refrattario ma una struttura a tre strati:

```
gas  →  bore Ø26.7  →  manicotto metallico thk 1.65 (OD 30.0)
     →  2 strati di carta Saffil da 1 mm compressi nell'intercapedine Ø30 → Ø32
     →  tubo Ø32 ID / Ø38.1 OD (thk 3.05)
```

Sporgenza 200 mm dentro il tubo riscaldato, oltre a 30 mm nello spessore della piastra e
90 mm nel refrattario della camera d'ingresso. Resistenze per unità di lunghezza a 500 °C:

| strato | R [m·K/W] | quota |
|---|---|---|
| manicotto metallico (Alloy 800, k ≈ 20) | 0.0009 | 1 % |
| carta Saffil compressa (k ≈ 0.145) | 0.0709 | 99 % |
| **totale ferrula** | **0.0718** | |
| film gas nel bore Ø26.7 (h ≈ 1900) | 0.0063 | — |
| parete tubo (T22) | 0.0008 | — |

L'isolante anulare **domina di due ordini di grandezza** su tutto il resto: è lui a fare la
ferrula. Il flusso termico nel tratto protetto scende a ~68 kW/m² contro i ~381 kW/m² del
tratto nudo immediatamente a valle. **Il picco di flusso termico e la temperatura metallica
massima cadono a z ≈ 0.32 m, cioè circa 120 mm a valle dell'estremità della ferrula**: è lì
che va concentrata la verifica, non alla piastra tubiera.

### 6.3 Water-gas shift

`CO + H₂O ⇌ CO₂ + H₂`, ΔH°(298) = −41.16 kJ/mol, K_p da Moe (1962):

```
K_p(T) = exp(4577.8/T − 4.33)        K_p(700 K) = 9.11 ; K_p(1000 K) = 1.28
```

Tre modalità: **congelata** (default), **equilibrio sopra una temperatura di congelamento**,
**approccio frazionario**. Il bilancio energetico usa entalpie **assolute** (formazione +
sensibile), quindi il calore di reazione entra automaticamente nel bilancio senza termini
aggiuntivi.

Nel caso reale la modalità corretta è **congelata**: il datasheet riporta MW 15.99 kg/kmol
sia in ingresso sia in uscita, cioè composizione invariata. Senza catalizzatore e con tempi
di residenza di frazioni di secondo la shift omogenea è effettivamente spenta. L'opzione
serve per valutare il caso limite (se la reazione procedesse, libererebbe calore aggiuntivo
e alzerebbe i flussi termici del tratto caldo).

### 6.4 Circolazione: distribuzione assiale

Il mantello è **un unico volume d'acqua**: i plenum sopra e sotto il fascio sono continui per
tutti i 13 m, quindi le pressioni di plenum sono praticamente uniformi lungo z. La portata
locale segue allora la produzione locale di vapore (**rapporto di circolazione locale
uniforme**), e la variazione assiale della velocità di attraversamento nasce interamente dal
decadimento del flusso termico lungo il tubo — nel caso reale un fattore ~30 fra z = 0.5 m
(214 kg/(s·m)) e z = 13 m (7 kg/(s·m)).

È stato valutato anche un modello a **circuiti in parallelo per sezione** con ricircolo
interno attraverso i canali non intubati (corona periferica + anima centrale). Va usato con
cautela: la corona periferica è di fatto **sigillata dai diaframmi di supporto** (nel disegno
il diaframma ha OD ≈ ID mantello − 10 mm), quindi non è un canale verticale aperto. Con la
corona aperta il modello produce ricircoli interni di ampiezza non fisica. Default:
ricircolo interno disattivato, con parametro `bypass_frazione_aperta` per lo studio di
sensibilità.

### 6.5 Verifica contro il datasheet

Caso: 848 tubi OD 38.1 × 3.05, L 12.998 m, passo 50.8 triangolare, mantello ID 2025,
OTL 1711.11, ITL 571; gas 279'566 × 1.1 kg/h da 967.5 °C a 34.74 bar a; vapore 117.84 bar a;
fouling 0.00050 (gas) / 0.00015 (acqua) m²K/W.

| grandezza | datasheet (×1.1) | calcolato | scarto |
|---|---|---|---|
| Potenza scambiata | 116.614 MW | **116.674 MW** | +0.05 % |
| Vapore prodotto | 347'743 kg/h | **347'798 kg/h** | +0.02 % |
| T gas uscita | 355.0 °C | **348.5 °C** | −6.5 K |
| ΔP lato gas | ≤ 0.30 bar | **0.113 bar** | entro limite |
| Tsat a 117.84 bar | 323.3 °C | **323.29 °C** | esatto |
| MW miscela | 15.99 | **15.98** | −0.06 % |
| ρ gas IN / OUT | 5.36 / 10.48 kg/m³ | **5.38 / 10.54** | +0.4 / +0.6 % |
| c_p gas IN / OUT | 2.353 / 2.119 kJ/kgK | **2.342 / 2.075** | −0.5 / −2.1 % |

**Nota su µ e k.** Il datasheet usa la **media molare** per viscosità e conducibilità della
miscela (µ = 0.0376 cP in ingresso). Il codice usa per default **Wilke** (µ e
Wassiljewa/Mason-Saxena per k), che per miscele H₂ + gas pesanti dà valori superiori alla
media molare — comportamento sperimentalmente corretto (per H₂/N₂ 50/50 a 300 K Wilke dà
1.70·10⁻⁵ Pa·s contro un dato sperimentale di ~1.68·10⁻⁵ e una media molare di 1.34·10⁻⁵).
Il codice offre entrambe le regole (`"miscelazione": "wilke" | "molare"`) proprio per poter
riprodurre la base del fornitore quando serve.

### 6.6 Risultati notevoli del caso reale

1. **Il picco di flusso termico (381–383 kW/m²) cade a z ≈ 0.32 m**, subito a valle della
   ferrula da 200 mm. Sotto la ferrula il flusso è 68 kW/m². Il rapporto è 5.6.
2. **Il deposito lato acqua fa più danni del DNB.** Con Rf = 1.5·10⁻⁴ m²K/W il salto termico
   attraverso il deposito è 57 K, contro un surriscaldamento *reale* di parete di 13 K.
   La temperatura metallica di 428 °C è dovuta al deposito, non all'ebollizione.
3. **Il DNBR minimo (0.71) è nella banda superiore**, non in quella con il flusso più alto:
   è la combinazione di flusso elevato e titolo locale 0.133 a determinarlo.
4. **CR = 7.5 con ΔZ = 6 m**, sotto il campo di progetto 10–30. Le perdite si ripartiscono in
   28 mbar (downcomer) + 67 mbar (riser) + 17 mbar (fascio) + 50 mbar (interne drum) contro
   un battente netto di 175 mbar. Il termine dominante è il riser; le interne del drum, che
   sono un'assunzione (5 kPa), pesano per il 28 %: vanno confermate con il costruttore prima
   di trarre conclusioni sul CR.

---

---

## 7. Definizioni operative (le grandezze che compaiono nel report)

### 7.1 K oppure °C?

Una **differenza** di temperatura vale lo stesso numero in kelvin e in gradi Celsius: le due
scale hanno la stessa ampiezza di grado e differiscono solo per l'origine (0 K = −273.15 °C).
Quindi *13.1 K di surriscaldamento = 13.1 °C di surriscaldamento*, è la stessa cosa.
Per convenzione tecnica le **differenze** si scrivono in K e i **livelli** in °C, proprio per non
lasciare ambiguità. Nel report tutti i valori etichettati [K] sono differenze, tutti quelli in
[°C] sono livelli.

### 7.2 Surriscaldamento di parete

```
ΔT_sup = T_parete_bagnata − T_sat = q″ / h_ebollizione
```

È la variabile che governa l'ebollizione nucleata, con tre zone:

- sotto ~1 K non ci sono ancora bolle (non si è superato il ΔT di **ONB**, *onset of nucleate
  boiling*: le cavità della superficie devono attivarsi);
- da qualche K fino al ΔT critico si è in **ebollizione nucleata pienamente sviluppata**: è la
  zona di lavoro, h molto alto e stabile;
- oltre il **ΔT critico** (il ginocchio della curva di Nukiyama, ~6 K a 118 bar) le bolle si
  fondono in un film continuo: h crolla di 1–2 ordini di grandezza e la parete si scalda di
  centinaia di gradi in minuti.

**Attenzione**: il surriscaldamento di parete *non* è la differenza fra metallo e T_sat. Fra il
metallo e l'acqua c'è il deposito. Nel caso reale: ΔT_sup = 13 K, salto attraverso il deposito
= 57 K, ΔT totale metallo−T_sat = 70 K. Il metallo è caldo per il deposito, non per l'ebollizione.

### 7.3 DNBR locale

```
DNBR = q″_critico,locale / q″_effettivo,locale        [adimensionale]
```

Rapporto fra il flusso termico che farebbe passare l'ebollizione da nucleata a film (il **CHF**)
e il flusso che il tubo sta realmente scambiando in quel punto. È un **margine**: DNBR = 3
significa lavorare a un terzo del flusso che innescherebbe il DNB; DNBR = 1 è il limite esatto.

Perché **locale**: il CHF non è un numero unico dell'apparecchio. Dipende dalla pressione
(Mostinski/Zuber), dalla geometria del fascio (φ_b di Palen) e soprattutto dal **titolo locale**:
più vapore c'è già nella miscela che lava il tubo, meno flusso serve per scoprire la parete.
Per questo il DNBR minimo del caso reale **non cade dove il flusso è massimo**, ma nella banda
superiore del fascio, dove il titolo è massimo perché l'acqua ha già attraversato tutte le bande
sottostanti.

### 7.4 Rapporto di circolazione — definizione e calcolo

```
CR = portata d'acqua circolante / portata di vapore prodotta
x_uscita = 1 / CR
```

CR = 10 significa che di ogni 10 kg di miscela che escono dal mantello 1 kg è vapore.
**Regola di progetto: CR ≥ 10, cioè x ≤ 0.10.**

Il CR non è un dato di input: è il risultato di un bilancio idraulico implicito.

**Forza motrice** — differenza di peso fra la colonna di acqua satura che scende e le colonne
bifase, più leggere, che salgono:

```
Δp_motore = g · [ ρ_liq·H_dc − ρ̄_fascio·H_fascio − ρ̄_riser·H_riser ]
```

con H_dc dal livello acqua nel drum al fondo del fascio, H_fascio l'altezza attraversata nel
fascio, H_riser dal cielo del mantello al livello nel drum. Le densità delle colonne bifase
dipendono dalla frazione di vuoto → dal titolo → da CR: il problema è implicito.

**Perdite** che si oppongono, tutte ∝ portata² cioè ∝ CR²:

```
Δp_perdite = downcomer + bocchello ingresso + attraversamento fascio
           + bocchello uscita + riser + interne del corpo cilindrico
```

**Soluzione**: il CR di esercizio annulla `Δp_motore(CR) − Δp_perdite(CR)`; il codice lo trova
per bisezione. Per alzarlo si agisce sulle perdite (riser e downcomer più grandi) o sul
dislivello asse drum − asse WHB.

Nel caso reale: battente netto 177 mbar, di cui riser 67, downcomer 28, fascio 17, interne drum
50. **CR = 7.5, sotto il minimo di 10.** Il termine su cui conviene agire è il riser.

### 7.5 Titolo e frazione di vuoto

Titolo `x` = rapporto di **masse** (vapore / totale). Frazione di vuoto `α` = rapporto di **aree**.
A 118 bar il vapore è 10 volte meno denso del liquido, quindi x = 0.10 corrisponde già a
α ≈ 0.45: metà sezione è vapore. **È α, non x, a dire se i ranghi alti del fascio restano bagnati.**

### 7.6 Legenda delle tabelle del report

Ogni tabella del report testuale e del report HTML è ora seguita da una **legenda delle
colonne** che spiega, per ciascuna voce, che cosa è la grandezza, a quale superficie è
riferita e come va letta. Le legende coprono:

- §4 effetto fascio (banda, y, n. tubi, T gas out, q″ max, T met. int. max, x uscita, alpha, DNBR min);
- §5b classi di lunghezza della ferrula;
- §6b verifica dei riser (velocità superficiali j_l e j_v, V_mix, alpha, ρv², regime);
- §8b dilatazioni termiche (T_eq, alpha medio, ΔL);
- §10 profilo assiale (spread, x_top, w campo, w by-pass, V_mix).

Tre precisazioni ricorrenti che vale la pena isolare:

- **q″ è sempre riferito alla superficie ESTERNA** del tubo (quella bagnata dall'acqua), salvo
  dove è scritto esplicitamente "int.". Il rapporto fra i due vale D_est/D_int = 38.1/32.0 = 1.19.
- **Il tratto sotto ferrula è escluso dai massimi** di flusso e dai minimi di DNBR: lì il flusso
  è artificialmente basso (68 contro 397 kW/m²) e includerlo mascherebbe il punto critico.
- **Le velocità j_l e j_v dei riser sono superficiali**, cioè riferite all'intera sezione come
  se la fase la occupasse da sola. Non sono le velocità reali delle fasi: servono perché sono
  le coordinate della mappa dei regimi di moto.

---

## 8. Dilatazioni termiche assiali

Il criterio richiesto:

```
ΔL = α_medio(T_eq) · (T_eq − T_ambiente) · L
```

dove `T_eq` è la **temperatura uniforme equivalente**, cioè quella che produrrebbe la stessa
dilatazione del profilo reale. Si ottiene per inversione da:

```
ΔL_reale = Σ_i  α(T_i) · (T_i − T_room) · Δz_i          (integrale sul profilo assiale)
α(T_eq) · (T_eq − T_room) · L = ΔL_reale                 (definizione di T_eq)
```

**Quale temperatura in ogni sezione.** Non quella di superficie: la dilatazione assiale di un
tubo è governata dalla **media sullo spessore pesata sull'area**. Con profilo logaritmico
`T(r) = T_est + q′/(2πk)·ln(r_est/r)` l'integrale si chiude in forma chiusa:

```
T_media,spessore = T_est + q′/(2πk) · [ 1/2 − r_int² ln(r_est/r_int) / (r_est² − r_int²) ]
```

**Mantello**: la lamiera è bagnata all'interno dall'acqua in ebollizione (h ≈ 20 000 W/m²K) e
coibentata all'esterno; il flusso disperso è dell'ordine di 0.6 W/m²K·ΔT, quindi il mantello si
porta praticamente a T_sat.

**Risultati del caso reale** (T_montaggio 20 °C, L = 12.998 m, materiale T22). *I valori numerici di questo paragrafo sono stati ricalcolati con il T22 e con il gas reale: vedi §15.5 e §19.7 per i valori correnti.*

| elemento | T_eq [°C] | α [10⁻⁶/°C] | ΔL [mm] |
|---|---|---|---|
| tubi, banda più calda | 344.3 | 12.66 | **53.4** |
| tubi, banda più fredda | 343.3 | 12.65 | 53.2 |
| mantello | 323.1 | 12.56 | **49.5** |
| **differenziale tubo − mantello** | | | **3.9** |
| differenziale fra tubi | | | 0.2 |

I **3.9 mm** di differenziale su 13 m sono il carico che si scarica sulla piastra tubiera in una
costruzione a piastre fisse: è la grandezza che decide se serve un giunto di dilatazione sul
mantello. Il differenziale *fra tubi* è invece piccolo (0.2 mm) perché i tubi si portano tutti
vicino a T_sat: la dispersione fra bande e fra classi di ferrula è irrilevante per la meccanica.

---

## 9. Riser: regime di moto e vibrazioni

### 9.1 Perché evitare il moto a tappi

In un tubo verticale con miscela acqua-vapore le bolle possono coalescere in **bolle di Taylor**
che occupano quasi tutta la sezione, separate da tappi di liquido. Il flusso diventa
intermittente: portata e pressione pulsano a 0.5–5 Hz e la forzante si scarica sui supporti,
sui bocchelli e sulla piastra tubiera. È il meccanismo di vibrazione più insidioso dei circuiti
di caldaia perché la frequenza è bassa e vicina alle frequenze proprie delle tubazioni.

### 9.2 Mappa di Taitel-Dukler (moto verticale ascendente)

Implementata nel codice, con `j_l` e `j_v` velocità superficiali:

```
diametro minimo perché esista la bolla di Taylor:
    D_min = 19 · [ (ρ_l − ρ_v)·σ / (ρ_l²·g) ]^0.5           (21.4 mm a 118 bar)
frontiera bolle → tappi:
    j_l ≥ 3.0·j_v − 1.15·[g·σ·(ρ_l−ρ_v)/ρ_l²]^0.25
bolle disperse (α < 0.52):
    j ≥ 4.0·[ D^0.429 (σ/ρ_l)^0.089 / ν_l^0.072 ]·[g(ρ_l−ρ_v)/ρ_l]^0.446
frontiera → anulare:
    j_v ≥ 3.1·[ σ·g·(ρ_l−ρ_v)/ρ_v² ]^0.25                    (1.00 m/s a 118 bar)
```

Alta pressione = vapore denso = soglia anulare bassa. Con 4 riser da 24" nel caso reale:
`j_l = 1.01 m/s`, `j_v = 1.50 m/s`, α = 0.50 → **regime anulare**, non slug. Il flusso è continuo,
nessuna pulsazione da tappi. Se invece si mettessero riser molto più grandi (velocità più basse)
si scenderebbe nel campo slug: **per i riser di caldaia la direzione sicura è più riser e più
piccoli, non pochi e grandi.**

### 9.3 Downcomer: vibrazioni e vortice

Due criteri:

- **velocità ≤ 2–3 m/s**: sopra si entra in zona di rumore e di vibrazione indotta, e cresce il
  rischio di carry-under (trascinamento di bolle verso il basso, che riduce il battente motore);
- **sommergenza dello stacco dal drum** per evitare il vortice a superficie libera:
  `h_min = D·(0.5 + 2.3·Fr)` con `Fr = v/√(gD)`. Nel caso reale, con v = 1.4 m/s e stacchi da 18",
  servono **0.87 m** di battente d'acqua sopra la bocca di aspirazione.

### 9.4 Bocchelli riser e downcomer: posizioni e diametri diversi

Non c'è ragione perché siano uguali, e di norma non lo sono:

- i **riser** devono estrarre il vapore dove viene prodotto → si posizionano sui **baricentri di
  bande a pari produzione di vapore**, quindi più fitti verso l'estremità calda; sono i più
  grandi perché devono passare una miscela con densità 3–5 volte inferiore a quella dell'acqua;
- i **downcomer** vanno messi **sfalsati**, a metà fra due riser consecutivi: l'acqua entra dove i
  riser non stanno già estraendo, il percorso assiale nel plenum inferiore si dimezza e cala la
  maldistribuzione. Portano liquido denso, quindi a pari portata sono più piccoli.

Il codice ora dimensiona e posiziona i due set in modo indipendente.

---

## 10. Studio: diaframmi OD 1700 mm e ferrule di lunghezza variabile 200–500 mm

Quattro varianti calcolate sullo stesso apparecchio (portate +10%, maglia assiale graduata da
20 mm all'imbocco):

| | A (base) | B | C | D |
|---|---|---|---|---|
| diaframmi OD [mm] | 2015 | **1700** | **1700** | 2015 |
| ferrule [mm] | 200 | 200 | **200/300/400/500** | **200/300/400/500** |
| Potenza [MW] | 116.78 | 116.69 | 116.54 | 116.64 |
| Vapore [t/h] | 348.1 | 347.8 | 347.4 | 347.7 |
| CR (esterno) | 7.5 | 7.3 | 7.3 | 7.6 |
| CR efficace ai tubi | 7.5 | 14.8 | 15.0 | 7.6 |
| q″ max [kW/m²] | 397 | 397 | 398 | 400 |
| T metallo max [°C] | 432 | 432 | 432 | 432 |
| **x max uscita fascio** | 0.133 | **0.181** | 0.161 | 0.132 |
| **α max locale** | 0.508 | **0.578** | 0.549 | 0.504 |
| **DNBR minimo** | 0.69 | **0.65** | 0.72 | 0.69 |

### 10.1 Diaframmi da 1700 mm: peggiorano il punto critico

Con OD 1700 in un mantello da 2025 resta una corona anulare di **162 mm per lato**, cioè
0.419 m²/m di canale verticale libero — confrontabile con l'area libera di attraversamento del
fascio (0.388 m²/m in mezzeria). Il modello (con carry-under delle bolle secondo la drift
velocity di Zuber) mostra due comportamenti opposti lungo l'asse:

- **estremità fredda** (poca produzione di vapore): la corona è piena di liquido, pesante, e
  scende; l'acqua rientra nel fascio dal basso. Il CR locale passa da 10 a **73**, la frazione di
  vuoto da 0.24 a 0.09. Ottimo — ma è la zona dove non serviva.
- **estremità calda** (z ≈ 0.2–0.6 m, dove sta il picco di flusso): qui la colonna nel fascio è
  già molto leggera (α ≈ 0.5) e offre poca resistenza, ma il fascio è anche stretto nelle bande
  alta e bassa, quindi l'attrito di attraversamento è alto. Il risultato è che la corona diventa
  **una via di salita preferenziale**: parte dell'acqua sale nell'anello invece di attraversare
  i tubi. Il CR locale nella sezione critica **scende da 7.2 a 5.7**, il titolo sale da 0.139 a
  0.175 e il DNBR minimo peggiora da 0.69 a **0.65**.

Il "CR efficace 14.8" è un numero mediato dominato dall'estremità fredda: **non va letto come un
miglioramento**. La grandezza da guardare è il CR locale nella sezione di picco.

**Conclusione**: la corona aperta è un by-pass, non un aiuto. È la ragione tecnica per cui si
montano i diaframmi a filo del mantello (o si aggiungono *sealing strips* lungo la periferia).
Se i 1700 mm sono un vincolo costruttivo, vanno previste lamiere di tenuta almeno nel primo
terzo caldo dell'apparecchio.

### 10.2 Ferrule di lunghezza variabile 200–500 mm

Con la maglia assiale infittita a 20 mm all'imbocco le quattro classi si separano nettamente:

| ferrula [mm] | q″ max [kW/m²] | z del picco [m] | T metallo int. max [°C] | DNBR min |
|---|---|---|---|---|
| 200 | **399.6** | 0.21 | **432.1** | **0.69** |
| 300 | 383.9 | 0.31 | 428.0 | 0.72 |
| 400 | 372.1 | 0.42 | 425.5 | 0.73 |
| 500 | 365.1 | 0.52 | 423.7 | 0.75 |

Il meccanismo è semplice: il picco cade sempre **subito a valle dell'estremità della ferrula**;
allungando la ferrula il picco si sposta dove il gas si è già raffreddato. Da 200 a 500 mm il
gas al punto di picco passa da ~955 °C a ~905 °C, e il flusso cala del **9 %**, la temperatura
metallica di **8.4 K**.

**Ma la popolazione mista non aiuta.** Il DNBR complessivo, la temperatura metallica massima e il
flusso di picco restano quelli della classe **200 mm**: il progetto è dettato dal tubo peggiore,
non dalla media. Effetti collaterali della dispersione:

- potenza scambiata: −0.12 % (la superficie isolata cresce) — trascurabile;
- temperatura del gas in uscita: si apre una dispersione da 347.9 a 349.5 °C fra i tubi;
- dilatazione assiale differenziale fra tubi: **0.18 mm su 13 m** — irrilevante, perché tutti i
  tubi restano comunque vicini a T_sat.

**Conclusione**: la tolleranza sulla lunghezza della ferrula va trattata come una **specifica di
progetto, non di montaggio**. Se il criterio di verifica è il DNBR nella zona d'imbocco, tanto
vale specificare per tutti i tubi la lunghezza maggiore: costa poco e sposta il picco di flusso
in una zona più fredda. Una popolazione mista 200–500 mm dà lo stesso risultato di progetto di
una popolazione tutta a 200 mm, cioè il caso peggiore, e in più introduce una variabilità che
rende difficile interpretare le ispezioni.

---

---

## 11. Dilatazione impedita: quali temperature usare e che tensioni ne escono

### 11.1 Le due temperature medie

Servono due numeri distinti, entrambi **temperature medie equivalenti** nel senso della §8
(la temperatura uniforme che darebbe la stessa dilatazione del profilo reale):

**Fascio tubiero.** Si parte dalla temperatura **media sullo spessore pesata sull'area** di ogni
cella (formula chiusa dal profilo logaritmico), si integra lungo la lunghezza per ottenere ΔL di
ogni combinazione banda × classe di ferrula, e si fa la **media pesata sul numero di tubi**.
È quella la temperatura da usare per il bilancio globale, perché tutti i tubi condividono le
stesse due piastre e quindi contribuiscono in parallelo alla rigidezza. Il tubo con il ΔL massimo
serve invece per la verifica **locale** del singolo giunto.

**Mantello.** La lamiera è bagnata all'interno dall'acqua in ebollizione (h ≈ 20 000 W/m²K) e
coibentata all'esterno: la resistenza è tutta nel coibente, quindi il metallo si porta
praticamente a T_sat. Il codice lo calcola come serie ebollizione + lamiera + coibente e
restituisce la media dello spessore.

Caso reale: **fascio 343.8 °C**, **mantello 323.2 °C**, differenza 20.6 K.

| elemento | T_eq [°C] | α [10⁻⁶/°C] | ΔL [mm] |
|---|---|---|---|
| tubi, ΔL massimo (banda 0) | 344.3 | 12.90 | 54.36 |
| tubi, ΔL minimo (banda 3) | 343.3 | 12.89 | 54.18 |
| **tubi, media pesata su 848** | **343.8** | **12.90** | **54.28** |
| **mantello (SA-516)** | **323.2** | **12.71** | **50.09** |
| **differenziale tubo medio − mantello** | | | **4.18** |
| differenziale fra tubi | | | 0.18 |

### 11.2 Da ΔL a tensione: il modello

A piastre fisse tubi e mantello sono vincolati alla **stessa** variazione di lunghezza, quindi
lavorano come due molle in parallelo e la differenza libera si scarica in una forza interna:

```
δ_libera = L · [ α_t(T_t)(T_t − T_a) − α_s(T_s)(T_s − T_a) ]
F        = δ_libera / [ L/(A_t E_t) + L/(A_s E_s) ]
σ_tubi     = F / A_t     (COMPRESSIONE se i tubi sono più caldi)
σ_mantello = F / A_s     (trazione)
F_tubo     = F / N_tubi  (carico sulla giunzione tubo-piastra)
```

con `A_t = N·π/4·(d_e² − d_i²)` e `A_s = π·(D_s + t_s)·t_s`.

Caso reale: A_t = 0.2848 m², A_s = 0.3795 m², E_t = 191 GPa, E_s = 186 GPa.

| grandezza | valore |
|---|---|
| dilatazione differenziale libera | **4.18 mm** |
| forza assiale interna | **9.90 MN** |
| σ tubi (compressione, SOLO TERMICO) | **19.4 MPa** |
| σ mantello (trazione) | **26.1 MPa** |
| **carico per tubo sulla giunzione tubo-piastra** | **6.5 kN** |

### 11.3 Verifica di instabilità (buckling) dei tubi

I tubi in compressione possono incurvarsi fra due diaframmi come una colonna snella. Con il
metodo tipo AISC/TEMA, estremi incastrati (k = 0.5):

```
r_giro = √(d_e² + d_i²)/4 = 12.44 mm
snellezza  λ = k·L_campata/r_giro = 0.5·1200/12.44 = 48.2
C_c = √(2π²E/S_y) = 143
λ < C_c  ->  σ_cr = S_y (1 − λ²/2C_c²) / FS,  FS = 5/3 + 3λ/8C_c − λ³/8C_c³
```

Risultato: **σ_ammissibile = 109 MPa** contro **19.4 MPa** → **utilizzo 18 %**,
margine ampio. Il parametro decisivo è la **campata fra diaframmi**: qui assunta 1.20 m. A 2.4 m
la snellezza raddoppia e l'ammissibile scende di circa il 40 %, portando l'utilizzo oltre il 50 %.
È il numero da confermare sul disegno.

### 11.4 Limiti dichiarati

È un calcolo di **screening**. Assume piastre infinitamente rigide, **trascura i termini di
pressione** (lato mantello 118 bar e lato tubi 35 bar, che caricano anch'essi la piastra e i
tubi) e non considera un eventuale giunto di dilatazione sul mantello. Il calcolo formale è
**TEMA RCB-7.16** oppure **ASME VIII-1 UHX-13**, che introducono il coefficiente J (presenza del
giunto), il rapporto di rigidezza K e il fattore F_q di flessione della piastra. I valori di
E, S_y e α usati qui sono indicativi: per il calcolo di codice vanno presi da **ASME II-D**.

Va inoltre verificata a parte la **tenuta della giunzione tubo-piastra** (mandrinatura e/o
saldatura) contro i 6.5 kN per tubo, secondo ASME VIII-1 UW-20.

---

## 12. Cos'è il cappello antitrascinamento

È una piccola lamiera piegata a cappello, saldata **dentro il mantello sopra la bocca del
bocchello di uscita** (riser), con l'apertura rivolta di lato o verso il basso. Fa due cose:

1. **Impedisce il prelievo preferenziale.** Senza cappello il bocchello aspira direttamente il
   getto più ricco di vapore che sale dal fascio proprio sotto di lui: si crea un cammino
   privilegiato e quel tratto di fascio viene lavato meno degli altri. Il cappello costringe la
   miscela a una piccola deviazione, uniformando il prelievo lungo il mantello.
2. **Rompe il vortice** che si formerebbe sopra la bocca e che trascinerebbe vapore in modo
   intermittente, facendo pulsare il riser.

L'equivalente sul lato discesa è il **rompivortice (vortex breaker)** sullo stacco del downcomer
dal corpo cilindrico: stesso scopo, ma per evitare che il liquido si porti dietro vapore verso il
basso (*carry-under*), che ridurrebbe il battente motore della circolazione. Per quest'ultimo il
codice calcola anche la **sommergenza minima** richiesta con il criterio di Froude
`h_min = D(0.5 + 2.3·Fr)`: nel caso reale **0.87 m** sopra la bocca di aspirazione.

---

---

## 13. Bocchelli e tubazioni del circuito (configurazione reale)

### 13.1 Bocchelli lato mantello

Dalla tabella bocchelli del disegno d'assieme e dalle indicazioni di dettaglio:

| tag | q.tà | DN | posizione angolare | posizione assiale |
|---|---|---|---|---|
| **R1÷R4** | 4 | 24" | 0° (cielo) | quattro distanze diverse |
| **R5** | 1 | 6" | 0° (cielo) | estremità fredda |
| **DC1, DC2** | 2 | 18" | 150° e 210° | stessa distanza, vicino alla piastra tubiera calda |
| **DC3, DC4** | 2 | 16" | 150° e 210° | stessa distanza fra loro |
| **DC5÷DC8** | 4 | 16" | 180° (fondo) | distribuiti lungo l'asse |
| **DC9** | 1 | 4" | 180° (fondo) | estremità fredda |
| CBD / IBD1-IBD2 / HPS | 1 / 2 / 1 | 2" | — | blow-down continuo, intermittente, heating system |

I riser sono **al cielo e più grandi** (la miscela ha densità 3–5 volte inferiore all'acqua e
occupa molto più volume); i downcomer sono **sui fianchi bassi e al fondo**, e sono più piccoli.
DC1/DC2 da 18" sono i più grandi del gruppo di discesa proprio perché servono la zona calda,
dove serve più portata. Le due posizioni a 150° e 210° (invece che a 180°) evitano che i getti
dei due bocchelli si scontrino sul fondo e distribuiscono meglio il rientro d'acqua.

### 13.2 Distinta delle tubazioni

Dal disegno tubazioni LTI 7523‑00‑100‑01 rev.3 ogni linea è definita come nella distinta di
fabbricazione: **spezzoni diritti di lunghezza nota + curve di angolo e raggio noti**.

| linea | DN / ID | tratti diritti [mm] | curve |
|---|---|---|---|
| R1÷R4 | 24" Sch.120 / 518.0 | 2700 | nessuna |
| DC1, DC2 | 18" Sch.120 / 387.2 | 250 + 2623 + 2376 | 1×90°, 2×30° (R/D 1.5) |
| DC3, DC4 | 16" Sch.120 / 344.6 | 250 + 3040 + 2621 | 1×60°, 1×90°, 1×30° |
| DC5÷DC8 | 16" Sch.120 / 344.6 | 500 + 2873 + 1159 + 1377 | 2×90°, 3×30° |
| DC9 | 4" Sch.120 / 92.1 | (da confermare) | 2×90° |

### 13.3 Come si calcolano le perdite

La lunghezza che conta non è la distanza in linea d'aria ma la **lunghezza sviluppata**:

```
L_sviluppata = Σ tratti diritti + Σ archi delle curve
arco di una curva = (θ/180)·π·(R/D)·D
```

Le curve pesano poi anche come perdita localizzata. Metodo di **Idelchik** per curve lisce:

```
ζ_curva = A1 · B1 + ζ_attrito
A1 = 0.9 sin θ            (θ < 70°)
   = 1.0                  (70° ≤ θ ≤ 100°)
   = 0.7 + 0.35 θ/90      (θ > 100°)
B1 = 0.21 / (R/D)^0.5     (R/D ≥ 1)
ζ_attrito = f · (π θ/180) · (R/D)
```

Verifica: curva 90° LR (R/D = 1.5) con f = 0.015 → ζ = 0.171 + 0.035 = **0.207**, contro
il valore Crane K = 14·f_T ≈ 0.18 per 18". Coerente.

Coefficiente totale della linea, riferito alla velocità nella linea:

```
K_tot = f·L_svil/D + Σ ζ_curve + K_extra + K_imbocco (0.5) + K_sbocco (1.0)
```

**La portata non si divide in parti uguali.** Le linee sono in parallelo fra gli stessi due punti
(mantello e corpo cilindrico), quindi si ripartisce in modo che ciascuna dissipi la **stessa Δp**:
il codice risolve per bisezione la Δp comune e poi ricava la portata di ogni singola linea.

### 13.4 Risultato con la configurazione reale

| linea | L svil [m] | curve | K tot | W [kg/s] | v [m/s] | regime |
|---|---|---|---|---|---|---|
| R1÷R4 (24") | 2.70 | 0 | 1.56 | 220.5 | 3.07 | anulare |
| R5 (6") | 2.70 | 0 | 1.80 | 15.5 | 2.96 | anulare |
| DC1, DC2 (18") | 6.77 | 3 | 2.10 | 138.1 | 1.78 | liquido |
| DC3, DC4 (16") | 7.53 | 3 | 2.23 | 106.2 | 1.73 | liquido |
| DC5÷DC8 (16") | 8.34 | 5 | 2.48 | 100.6 | 1.64 | liquido |
| DC9 (4") | 5.43 | 2 | 2.93 | 6.6 | 1.51 | liquido |

**Effetto sul rapporto di circolazione: CR passa da 7.5 (tubazioni assunte) a 9.3 (tubazioni
reali).** I riser reali sono spool diritti da soli 2.7 m senza curve, quindi le perdite di salita
crollano da 67 a 45 mbar. Il circuito resta comunque appena sotto il minimo di 10, e a questo
punto **il termine dominante è l'assunzione sulle interne del corpo cilindrico** (50 mbar su 161
di battente, cioè il 31 %): è quel dato, non le tubazioni, a decidere se il CR è dentro o fuori
specifica. Va chiesto al costruttore del drum.

Tutti i riser risultano in **regime anulare**, non slug: nessun problema di pulsazioni.

---

---

## 14. By-pass interno centrale

### 14.1 Che cos'è e perché c'è

Nell'anima non intubata del fascio (ITL 571 mm) corre un **tubo che porta una frazione del gas
di processo dall'ingresso all'uscita senza raffreddarla**. Serve a **regolare la temperatura di
uscita**: un WHB si dimensiona in condizioni sporche, quindi da pulito sovra-raffredda; il
by-pass rialza la temperatura miscelando gas caldo. È anche la leva per compensare
l'invecchiamento della superficie nel corso della campagna.

Costruzione (disegno 3‑E‑1401 / 7523‑01‑300‑01), tre strati per la stessa logica della ferrula:

```
gas 967 °C → liner Alloy 601/602 CA  ID 275, spessore 3  (OD 281)
           → 2 giri di carta Saffil da 1 mm compressi     (OD 284)
           → tubo di contenimento  ID 284, spessore 8     (OD 300)
           → acqua in ebollizione a 323 °C
```

Il liner regge la temperatura, la carta assorbe il salto termico, il tubo di contenimento porta
la pressione e resta freddo.

### 14.2 Come si modella

Il by-pass è un **canale in parallelo** al fascio fra le stesse due camere. Il codice:

1. divide la portata: `W_tubi = (1−x)·W_tot`, `W_bypass = x·W_tot`;
2. marcia il gas nel by-pass sulla **stessa griglia assiale** del fascio, con la catena di
   resistenze film gas → liner → carta → tubo → ebollizione, restituendo il calore ceduto per
   metro e le temperature di liner e tubo;
3. **miscela le due correnti** all'uscita su base entalpica;
4. **risolve x per centrare la temperatura di uscita richiesta** (oppure la usa come dato);
5. aggiunge il calore ceduto dall'isolante alla produzione di vapore;
6. sottrae la proiezione del tubo dall'area libera verticale dell'anima nel calcolo idraulico
   del mantello.

### 14.3 Il by-pass spiega lo scarto sulla temperatura di uscita

Senza by-pass il calcolo dava **348.0 °C** contro i **355.0** del datasheet: 7 K di scarto che
avevo attribuito a differenze di modello. Non era così: **i 355 °C del datasheet sono la
temperatura MISCELATA**. Con il by-pass il calcolo centra esattamente il valore:

| | senza by-pass | con by-pass |
|---|---|---|
| frazione deviata | — | **1.53 %** (1.30 kg/s) |
| T uscita dai tubi | 348.0 °C | 347.4 °C |
| T uscita dal by-pass | — | 825.1 °C |
| **T uscita miscelata** | 348.0 °C | **355.0 °C** ✓ |
| potenza | 116.77 MW | 115.52 MW |
| vapore | 348.1 t/h | 344.4 t/h |

Resta uno scarto di **−0.93 %** sulla potenza rispetto al datasheet (115.52 contro 116.61 MW),
tutto attribuibile al c_p: il c_p medio integrato del codice vale 2208 J/kgK contro i 2230
impliciti nel datasheet, cioè **−1.0 %**. È la precisione dei polinomi di c_p per questa
miscela, non un errore di bilancio.

### 14.4 Effetto sulle grandezze di progetto

Sensibilità alla frazione deviata (le altre condizioni invariate):

| frazione | T tubi [°C] | **T miscelata** | potenza [MW] | vapore [t/h] | **CR** | **q″ max** | **DNBR** | T liner | T tubo cont. |
|---|---|---|---|---|---|---|---|---|---|
| 0 % | 348.0 | 348.0 | 116.77 | 348.1 | 9.3 | 397.5 | 0.71 | — | — |
| **1.53 % (progetto)** | 347.4 | **355.0** | 115.52 | 344.4 | **9.4** | **395.3** | **0.71** | 767 | 333 |
| 5 % | 346.0 | 375.8 | 111.83 | 333.4 | 9.6 | 390.4 | 0.72 | 831 | 334 |
| 10 % | 344.0 | 406.5 | 106.35 | 317.0 | 10.0 | 382.8 | 0.74 | 863 | 335 |
| 15 % | 342.0 | 437.4 | 100.80 | 300.5 | 10.3 | 374.9 | 0.76 | 879 | 335 |

**Rapporto di circolazione: migliora leggermente.** Meno gas nei tubi → meno vapore prodotto →
a parità di battente motore il CR sale, da 9.3 a 9.4 al punto di progetto e fino a 10.0 con il
10 % di by-pass. Il by-pass quindi *aiuta* la circolazione, ma di poco: alla frazione di progetto
il guadagno è dell'1 %.

**Flussi termici: calano poco.** Meno portata per tubo → coefficiente di scambio più basso
(h ∝ G^0.8) → picco da 397.5 a 395.3 kW/m², cioè −0.6 %. Al 10 % di by-pass si arriva a
−3.7 % e il DNBR sale da 0.71 a 0.74. **Non è una leva utile per il margine su DNB**: per
guadagnare qualcosa servirebbe un by-pass così aperto da mandare l'uscita a 400 °C.

**Il tubo di contenimento resta sempre a 333–335 °C**, cioè circa 10 K sopra la temperatura
dell'acqua, in tutto il campo. È la verifica che l'isolamento funziona: la carta assorbe 429 K
di salto e il componente in pressione non vede mai il gas caldo.

**Il liner lavora fra 767 e 879 °C**, ben dentro il limite dell'Alloy 601/602 CA (1100 °C) ma
anche **in piena finestra di metal dusting** (450–900 °C) con CO all'8.6 %. Non è un difetto: è
esattamente la ragione per cui si specifica una lega alto Cr‑Al invece di un acciaio comune.

### 14.5 Il punto critico: la regolazione

By-pass e fascio sono in parallelo fra le stesse due camere, quindi **la ripartizione la decide
la resistenza idraulica**, non il desiderio:

| | Δp |
|---|---|
| fascio tubiero (848 tubi Ø32, 13 m) | **111.6 mbar** |
| by-pass a tubo nudo (Ø275, 13 m) alla portata di progetto | **0.4 mbar** |

A valvola completamente aperta il tubo centrale prenderebbe circa il **26 %** della portata (la
frazione cresce con la radice del rapporto delle Δp) e la temperatura d'uscita salirebbe oltre
i 500 °C. L'organo di regolazione deve quindi dissipare **111 mbar**, cioè praticamente tutta la
perdita di carico del fascio.

Due conseguenze pratiche:

- **l'autorità di regolazione è enorme**: pochi gradi di apertura spostano molto la temperatura,
  quindi la valvola va scelta con caratteristica adatta e la taratura è delicata;
- **una valvola che si apre per guasto porta il gas a valle a 400–500 °C**: va verificato che
  l'apparecchiatura a valle lo sopporti, e che il sistema di controllo abbia il fail-safe nella
  direzione giusta (chiusura).

---

## 15. Stato di sollecitazione: formule di Lamé + carico assiale di compressione

### 15.1 Perché serve sommare tre cose

Un tubo scambiatore di questo apparecchio è contemporaneamente:

1. **schiacciato dall'esterno** — l'acqua a mantello sta a 117.84 bar, il gas dentro il tubo a
   ~34.7 bar. La pressione netta esterna è **83 bar**;
2. **caricato assialmente** — le due piastre tubiere fisse gli impediscono di allungarsi come
   vorrebbe, e allo stesso tempo la pressione tira l'apparecchio per le estremità;
3. **attraversato da un salto di temperatura nello spessore** — ~36 K nella zona di picco, che
   mette la faccia calda in compressione e quella fredda in trazione.

Solo la somma dei tre dice se il tubo è verificato, e i tre termini hanno **statuti diversi**
davanti al codice: le tensioni di pressione sono **primarie** (equilibrio con un carico esterno,
il loro superamento porta al collasso), quelle da gradiente termico e da dilatazione impedita
sono **secondarie** (autoequilibrate, si rilassano, contano per la fatica).

### 15.2 Lamé

Per un cilindro di parete spessa con pressione interna `pi` sul raggio `ri` ed esterna `pe` su `ro`:

```
                 pi ri² − pe ro²        (pi − pe) ri² ro²
  sigma_r(r)  =  ───────────────  −  ─────────────────────
                    ro² − ri²            r² (ro² − ri²)

                 pi ri² − pe ro²        (pi − pe) ri² ro²
  sigma_th(r) =  ───────────────  +  ─────────────────────
                    ro² − ri²            r² (ro² − ri²)
```

Verifica immediata: a `r = ri` la radiale vale `−pi`, a `r = ro` vale `−pe`. Nel caso classico
della caldaia a tubi d'acqua (pressione dentro) la circonferenziale è di **trazione**. Qui è
il contrario: **la pressione grande è fuori, quindi il cerchio è compresso**. Cambia anche il
modo di rottura: il tubo non si apre, si schiaccia — ed è per questo che accanto alla verifica
di tensione ne serve una di **collasso per pressione esterna**.

### 15.3 Gradiente termico radiale (Timoshenko)

Con profilo logaritmico stazionario e `dT = T(ri) − T(ro)`:

```
  m = alpha E dT / [ 2 (1 − nu) ln(ro/ri) ]

  sigma_r  = m [ −ln(ro/r) − k (1 − ro²/r²) ln(ro/ri) ]
  sigma_th = m [ 1 − ln(ro/r) − k (1 + ro²/r²) ln(ro/ri) ]
  sigma_z  = m [ 1 − 2 ln(ro/r) − 2 k ln(ro/ri) ]          k = ri²/(ro² − ri²)
```

Per parete sottile il valore limite è `± alpha E dT / (2(1−nu))`: con dT = 36 K, alpha = 13·10⁻⁶,
E = 191 GPa e nu = 0.3 fa **±64 MPa**, tutt'altro che trascurabile.

### 15.4 Il sistema a piastre fisse generalizzato

Ogni gruppo di tubi (banda × classe di ferrula), il mantello e il tubo di contenimento del
by-pass sono **molle assiali in parallelo** fra le due piastre, `k_i = A_i E_i / L`. Le piastre
impongono a tutti lo stesso allungamento `delta`:

```
  somma_i k_i (delta − delta_i,libero) = P_end
  delta = ( P_end + somma_i k_i delta_i ) / somma_i k_i
  F_i   = k_i (delta − delta_i)                     (+ = trazione)
```

`P_end` è il **carico di estremità di pressione**. Dall'equilibrio del corpo tagliato in
mezzeria (testa + piastra + fluido a sinistra):

```
  P_end = p_mantello · A_fluido,mantello + p_tubi · A_fluido,tubi
```

ed è di **trazione**: la pressione tende ad allungare l'apparecchio. Per questo WHB vale
**28.3 MN**, e si ripartisce fra tubi e mantello in proporzione alle rigidezze.

### 15.5 Il risultato che ribalta lo screening precedente

| condizione | sigma_z nei tubi |
|---|---|
| solo dilatazione impedita (screening §11) | **−35.8 MPa** (compressione) |
| + carico di estremità di pressione | **+42.8 MPa** (trazione) |
| **totale in esercizio** | **+7 MPa (trazione)** |

**In esercizio i tubi non sono compressi**: la pressione più che compensa la dilatazione
impedita. Il buckling non è quindi governato dall'esercizio ma dalla condizione
**"caldo e non in pressione"** (avviamento, depressurizzazione a caldo, prova a freddo dopo
riscaldamento), dove la compressione da dilatazione resta senza il contrasto della pressione.
Il calcolo riporta perciò **due condizioni di carico**:

- **LC1 esercizio** = termico + pressione → tubi in leggera trazione, utilizzo a instabilità 0 %;
- **LC2 caldo non in pressione** = solo termico → 35.8 MPa di compressione, utilizzo **33 %**.

### 15.6 Collasso per pressione esterna

Due meccanismi, di cui si prende il minore con interazione dolce (`1/p² = 1/pE² + 1/pY²`):

- **elastico**: il maggiore fra il cilindro infinitamente lungo `2E/(1−nu²)(t/Dm)³` e il
  cilindro corto fra due irrigidimenti (**Windenburg-Trilling**)
  `2.42 E (t/Do)^2.5 / [ (1−nu²)^0.75 (L/Do − 0.45 sqrt(t/Do)) ]`;
- **plastico**: snervamento del cerchio, `2 Sy t / Do`.

I **diaframmi di supporto lavorano come anelli di irrigidimento**: è la loro spaziatura a
fissare `L`. Per i tubi Ø38.1×3.05 il collasso stimato è **334 bar** contro 83 richiesti
(utilizzo 25 %). Per il **tubo di contenimento del by-pass** Ø300 con 8 mm di parete il collasso
scende a **82 bar contro 83 richiesti**: la verifica **non passa**, ed è uno dei risultati da
portare al costruttore — o lo spessore reale è maggiore di quello assunto dal disegno, o servono
irrigidimenti. Senza i diaframmi il collasso scenderebbe a 86 bar sul cilindro lungo: il
risultato dipende quindi anche dal **gioco radiale del foro nel diaframma**, che va verificato.

### 15.7 Il liner del by-pass — libero di dilatare (confermato)

Lavora a T_eq ≈ 700 °C, non porta pressione (stesso gas dentro e fuori) e vorrebbe allungarsi di
decine di millimetri in più del tubo di contenimento. **Costruttivamente è libero di dilatare**:
non sviluppa quindi alcun carico assiale e **non compare fra i membri del sistema a piastre
fisse**, dove figura il solo tubo di contenimento.

Il valore di **1.9 GPa** che il calcolo riporta è l'**ipotesi contraria** — liner vincolato a
entrambe le estremità — ed è documentata solo per dare l'ordine di grandezza di ciò che il giunto
scorrevole evita: un ordine di grandezza oltre qualunque ammissibile. È la ragione costruttiva per
cui quel giunto esiste.

### 15.8 Criteri di equivalenza

Von Mises `sqrt(0.5[(s1−s2)²+(s2−s3)²+(s3−s1)²])` e Tresca `s_max − s_min` sono riportati
entrambi. Tresca è sempre più severo (fino al 15 %) ed è quello adottato da ASME. Il punto
peggiore risulta la **faccia interna nella zona di picco di flusso**, con sigma_VM ≈ 107 MPa
pari al **55 % dello snervamento** alla temperatura locale.

---

## 16. Valvola a farfalla del by-pass: ripartizione dei flussi e finestra operativa

### 16.1 Il principio: due rami in parallelo

Fascio e by-pass collegano gli stessi due punti, quindi **dissipano la stessa caduta di
pressione**. La frazione deviata non è un dato di progetto: è il risultato di

```
  dp_fascio(w_f)  =  dp_bypass(w_b, theta)        w_f + w_b = w_tot
```

risolta per bisezione. L'unico elemento che si può scegliere è la resistenza della valvola.

### 16.2 Correlazione per la valvola

**Idelchik**, valvola a disco (farfalla) in condotto circolare: `zeta` in funzione dell'angolo
di **chiusura** `alpha` (0 = tutta aperta, 90 = chiusa), riferito alla velocità media nel tubo.

| alpha [°] | 0 | 5 | 10 | 15 | 20 | 25 | 30 | 35 | 40 | 45 | 50 | 55 | 60 | 65 | 70 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| zeta | 0.20 | 0.24 | 0.52 | 0.90 | 1.54 | 2.51 | 3.91 | 6.22 | 10.8 | 18.7 | 32.6 | 58.8 | 118 | 256 | 751 |

Interpolazione **lineare in log(zeta)**, perché zeta varia in modo esponenziale con l'angolo;
oltre 70° si estrapola con la pendenza dell'ultimo tratto (in quel campo conta il trafilamento
reale della battuta, quindi il valore è indicativo).

Le altre perdite del ramo: Darcy-Weisbach con attrito di Colebrook/Filonenko nel liner,
imbocco 0.5 e sbocco 1.0.

### 16.3 Risultato per questo apparecchio

Il ramo di by-pass, nudo, dissipa **0.4 mbar** contro i **111 mbar** del fascio: senza
strozzamento prenderebbe il 13-14 % della portata e la temperatura di uscita salirebbe a
**430 °C**. La farfalla deve quindi bruciare ~111 mbar, cioè lavorare a **zeta ≈ 280**:

| | apertura | chiusura | zeta | by-pass | T miscelata |
|---|---|---|---|---|---|
| **minimo ammesso** | 16.8° | 73.2° | 1508 | 0.70 % | 350.5 °C |
| **ESERCIZIO NORMALE** | **24.6°** | **65.4°** | **280** | **1.52 %** | **355.0 °C** |
| **massimo ammesso** | 30.7° | 59.3° | 108 | 2.38 % | 360.0 °C |

Sensibilità: **≈ 0.7 K di temperatura miscelata per grado di stelo**.

### 16.4 Da dove vengono il minimo e il massimo

**Apertura minima** (vince il vincolo più alto):

| criterio | angolo | vincolante |
|---|---|---|
| controllabilità meccanica (sotto ~15° la farfalla è on-off) | 15.0° | |
| T miscelata ≥ 350 °C | 15.4° | |
| **lavaggio minimo del liner, ≥ 1.5 m/s** | **16.8°** | **sì** |
| erosione in vena contratta (rho·v² ≤ 40 kPa) | non vincolante | |

**Apertura massima** (vince il più basso):

| criterio | angolo | vincolante |
|---|---|---|
| autorità della valvola (sopra ~70° zeta è piatto) | 70.0° | |
| **T miscelata ≤ 360 °C** | **30.7°** | **sì** |
| limite metallurgico del liner (Alloy 601, 1100 °C) | 76.1° | |

Il vincolo di **lavaggio** merita una nota: sotto ~1.5 m/s il liner diventa un ramo morto, dove
il gas stratifica, rallenta e deposita — con un syngas ricco di CO nella finestra di metal
dusting è la condizione peggiore possibile. Non è un criterio di temperatura, è di integrità.

### 16.5 Verifiche fluidodinamiche sulla valvola

- velocità in vena contratta `v = sqrt(2 dp/rho)` ≈ **60 m/s**, Mach **0.07**: nessun problema
  di rumore o forzanti acustiche;
- `rho·v²` in vena ≈ 2·dp_valvola ≈ 22 kPa, entro il limite assunto di 40 kPa per gas pulito
  (API RP 14E con C = 200 darebbe ~100 m/s di velocità erosiva);
- il punto di lavoro a 24.6° è **nella parte bassa della corsa**: la valvola è quasi chiusa.
  È il comportamento tipico di un by-pass sovradimensionato in diametro, e va accettato o
  corretto con un diaframma fisso in serie, che sposterebbe il punto di lavoro verso il centro
  della corsa migliorando la stabilità della regolazione.

### 16.6 Posizione di sicurezza

**Fail-closed.** In mancanza di aria strumenti tutta la portata va al fascio: la temperatura
scende a 348 °C e il vapore aumenta — condizione sicura sia per l'apparecchio sia per il
processo a valle. Il guasto da evitare è l'opposto: una valvola che si apre manderebbe gas
a 940 °C direttamente all'uscita.

---

## 17. Bocchelli non collegati (R5, DC9, R0A, R0B)

Quattro bocchelli previsti sul disegno **non sono in servizio**:

| sigla | DN | posizione | stato |
|---|---|---|---|
| R5 | 6" | estremità fredda, 0° (cielo) | presente, **non collegato** |
| DC9 | 4" | estremità fredda, 180° (fondo) | presente, **non collegato** |
| R0A, R0B | da confermare | estremità calda, 0° | **non implementati** |

Il calcolo idraulico è eseguito **senza** queste linee, quindi sezione di passaggio e battente
motore sono quelli realmente disponibili e non quelli che si leggerebbero dal disegno.

La conseguenza non è tanto sul rapporto di circolazione — R5 e DC9 sono piccoli e pesano poco
sulla sezione totale — quanto sulla **distribuzione**: le linee mancanti servivano le due
estremità dell'apparecchio, cioè proprio le zone dove il campo tubi è meno lavato dalla
circolazione trasversale. R0A/R0B in particolare stavano all'**estremità calda**, dove cadono
il picco di flusso termico e il DNBR minimo. Vale la pena chiedersi se la loro assenza sia una
scelta o una dimenticanza costruttiva.

---

## 18. Perdite di carico nel corpo cilindrico: metodo e stato dell'arte

### 18.1 Tre cose diverse che si chiamano tutte "perdita nel drum"

È l'errore più comune di questo calcolo. Il corpo cilindrico entra nel bilancio in **tre modi
distinti**, e solo uno è una perdita che toglie battente alla circolazione:

| | cos'è | dove va |
|---|---|---|
| **1. livello** | la quota del pelo libero fissa la testa della colonna di discesa | **termine statico**, già nel battente |
| **2. percorso di circolazione** | perdita che la **miscela** subisce dal bocchello del riser fino a quando l'acqua separata rientra nella massa | **l'unica** che entra nel bilancio |
| **3. percorso vapore** | perdita che il **vapore** subisce dal pelo libero a demister, camini, collettore, uscita | **non** c'entra con la circolazione: si scarica sulla pressione consegnata in rete |

### 18.2 Perché il numero pesa così tanto

La miscela arriva ai riser a ~3.6 m/s con densità omogenea ~390 kg/m³: **una sola altezza
cinetica vale già 25.6 mbar**. Su un battente motore di ~150 mbar, ogni unità di K nelle interne
si mangia il 17 % della circolazione. Per questo il valore va **calcolato**, non assunto.

### 18.3 Il metodo

Si scompone il percorso in singolarità elementari e si sommano i K, **tutti riportati alla stessa
velocità di riferimento** (qui quella nel bocchello del riser), così che siano confrontabili fra
loro e con la letteratura:

```
  K_tot = K_transizione                                (Borda-Carnot, Idelchik)
        + (K_attrito + K_curva + K_extra) (A_noz/A_canale)²
        + 1.0 (A_noz/A_finestra)²                      (energia cinetica persa allo scarico)
        − 1.0                                          (sbocco già contato nella linea del riser)

  dp = K_tot · ½ ρ_H v_noz²
```

L'ultima riga è essenziale: la linea del riser include già uno **sbocco K = 1.0 verso un grande
volume**. Ma il riser non sbocca in un grande volume, sbocca nel convogliatore. Senza la
deduzione si conta due volte.

**Bifase.** Per le singolarità si usa il modello **omogeneo**, `dp = K G²/(2 ρ_H)`: in
un'accidentalità brusca vapore e liquido non hanno tempo di scorrere l'uno rispetto all'altro.
È la pratica raccomandata (Collier & Thome; Idelchik cap. 12). L'alternativa è il
**moltiplicatore di Chisholm per singolarità**,
`φ² = 1 + (ρ_l/ρ_v − 1)(B x(1−x) + x²)` con B ≈ 0.5, che dà valori più alti a titolo basso.

### 18.4 Convogliatori o cicloni: non è lo stesso

- **Cicloni**: tutta la miscela attraversa il ciclone, quindi **tutta** la perdita del separatore
  sta sul percorso di circolazione. È la costruzione costosa in termini di battente
  (indicativamente 30–150 mbar, secondo carico e tipo).
- **Convogliatori + demister** (questo caso): la miscela passa solo nel canale del convogliatore,
  progettato per **non** strozzare; il demister vede solo il vapore. Costa molto meno battente
  (indicativamente 0–50 mbar).

Il corpo cilindrico 3-D-4201 è del secondo tipo: quattro convogliatori sui bocchelli R1÷R4, che
prendono la miscela dai bocchelli inferiori, la portano lungo la parete e la scaricano da una
finestra **sopra il livello**, nello spazio vapore. Demister longitudinale, otto camini da 8"
verso il collettore da 20" e uscita da 18".

### 18.5 Risultato per questo apparecchio

**A) percorso di circolazione** — K netto **0.41**, cioè **9.9 mbar** (7 % del battente):

| voce | K (rif. bocchello) | dp [mbar] |
|---|---|---|
| bocchello → canale | 0.006 | 0.2 |
| canale (attrito + curva + K extra) | 1.02 | 26.1 |
| finestra di scarico | 0.363 | 9.3 |
| **deduzione sbocco già contato** | **−1.000** | **−25.6** |
| **totale** | **0.41** | **9.9** |

**Sensibilità al K del convogliatore**, che è il parametro dominante:

| K extra | K netto | dp [mbar] | quota del battente |
|---|---|---|---|
| 0.0 | −0.45 | 0 | 0 % |
| 0.5 | −0.02 | 0 | 0 % |
| **1.0** | **0.41** | **9.9** | **6.9 %** |
| 1.5 | 0.83 | 20.2 | 14.2 % |
| 2.0 | 1.26 | 30.6 | 21.4 % |
| 3.0 | 2.12 | 51.3 | 35.9 % |
| 4.0 | 2.97 | 72.0 | 50.4 % |

A K = 0 il convogliatore costa **meno** di uno sbocco nudo: rilascia la miscela più lentamente
(finestra 0.35 m² contro bocchello 0.211 m²). **È esattamente il motivo per cui i convogliatori
esistono**, e spiega perché su questo drum non serviva ricorrere ai cicloni.

**Effetto sul risultato**: sostituendo i 50 mbar assunti con i 9.9 mbar calcolati, il rapporto di
circolazione passa da **9.3 a 12.1**, cioè **il criterio CR ≥ 10 è soddisfatto**. La frazione di
vuoto massima scende da 0.46 a 0.40 e il DNBR minimo sale da 0.71 a 0.73.

**B) percorso vapore** — **61.5 mbar** dal pelo libero all'uscita, di cui 25 sui camini, 21 sul
collettore, 16 sul bocchello. Il demister è trascurabile (0.07 m/s di velocità frontale). Questi
61.5 mbar **non** toccano la circolazione: sono la differenza fra pressione nel corpo cilindrico
e pressione consegnata.

**C) verifiche di separazione**

| grandezza | valore | limite | esito |
|---|---|---|---|
| area del pelo libero | 38.8 m² | — | — |
| velocità superficiale del vapore | 0.036 m/s | 0.132 m/s (Souders-Brown K = 0.045) | **27 %** |
| velocità frontale sul demister | 0.067 m/s | 0.294 m/s (K = 0.10) | 23 % |
| spazio vapore | 1350 mm | — | ampio |
| sommergenza degli stacchi | 1650 mm | — | nessun rischio di vortice |

`v_max = K_SB · sqrt((ρ_l − ρ_v)/ρ_v)` — Souders-Brown, con K_SB 0.03–0.05 m/s per separazione
gravitazionale a pelo libero e 0.07–0.11 con demister. Il 27 % di utilizzo è il margine che rende
sufficiente la separazione gravitazionale.

### 18.6 Come ottenere il numero vero

In ordine di affidabilità:

1. **Curva sperimentale del costruttore** (Alfa Laval / OLMI): dp in funzione della portata di
   miscela, misurata sulle interne reali. È l'unico dato veramente affidabile. La domanda da fare
   è precisa: *«qual è la perdita di carico fra la flangia del bocchello riser e lo spazio d'acqua
   del corpo cilindrico, alla portata di circolazione di progetto?»* — non «qual è la perdita del
   drum», che è ambigua.
2. **Ricostruzione geometrica** come qui, con i K di Idelchik e la sensibilità dichiarata.
3. **Verifica in campo**: a carico stazionario, la ΔP misurata fra una presa sul mantello del WHB
   e una nello spazio d'acqua del corpo cilindrico, corretta per le colonne statiche, dà la
   perdita dell'intero anello. Alternativa più semplice: confrontare il livello in un indicatore
   collegato al mantello del WHB con quello del corpo cilindrico — la differenza è direttamente
   la perdita dell'anello in colonna d'acqua.

### 18.7 Riferimenti specifici

- **I. E. Idelchik**, *Handbook of Hydraulic Resistance* — K di allargamenti, restringimenti,
  curve, canali rettangolari, imbocchi e sbocchi.
- **J. G. Collier, J. R. Thome**, *Convective Boiling and Condensation* — perdite localizzate
  bifase, modello omogeneo e moltiplicatori.
- **D. Chisholm**, *Two-Phase Flow in Pipelines and Heat Exchangers* — moltiplicatore per
  singolarità.
- **V. Ganapathy**, *Boiler circulation calculations* — impostazione del bilancio dell'anello e
  ordini di grandezza tipici delle interne.
- **Babcock & Wilcox**, *Steam: its Generation and Use*, capitolo sulla circolazione — curve di
  perdita dei separatori a ciclone in funzione del carico.
- **Souders & Brown (1934)** per il limite di velocità al pelo libero; **API 12J / GPSA** per i
  valori pratici di K_SB e dei demister.

---

## 19. Miglioramento della precisione: cosa è stato fatto e cosa resta

### 19.1 Convergenza: chiusa

Raddoppiando la maglia (NZ 90→180, NY 12→20, infittimento 10→20):

| grandezza | 90 × 12 | 180 × 20 | scarto |
|---|---|---|---|
| potenza | 116.54 MW | 116.54 MW | < 0.01 % |
| flusso di picco | 396 kW/m² | 398 kW/m² | +0.5 % |
| DNBR minimo | 0.73 | 0.72 | −1.4 % |
| rapporto di circolazione | 12.1 | 12.0 | −0.8 % |

**La discretizzazione non è più una fonte di errore.** Verificata anche la
**conduzione assiale** nel tubo alla fine della ferrula, dove il flusso ha uno scalino: la
lunghezza di smorzamento √(k·t/h_eff) = √(40 × 0.00305 / 5460) = **4.7 mm**, contro celle di
20 mm. Il picco non viene spalmato e il modello 1-D radiale per cella è corretto.

### 19.2 Gas reale: il residuo di −0.93 % era tutto lì

Il calcolo usava polinomi di **gas ideale** per tutti i componenti, acqua compresa. Ma l'H₂O è
il 32.6 % molare (36.8 % in massa) ed è l'unica specie con temperatura ridotta dell'ordine
dell'unità: alla sua pressione parziale, all'estremità fredda, non è affatto ideale.

Si è introdotto il troncamento al **secondo viriale**:

```
  Z             = 1 + B_mix p / (R T)
  h − h_ideale  = p ( B_mix − T dB_mix/dT )
  cp − cp_ideale = − p T d²B_mix/dT²
  B_mix         = Σ_i Σ_j y_i y_j B_ij
```

Due scelte che contano:

- **il peso dell'acqua**. Nella regola esatta il termine di auto-interazione dell'acqua pesa
  **y² = 0.106**, non y = 0.326. Trattare ogni componente come puro alla pressione totale
  (Lewis-Randall) o alla parziale (Amagat) triplicherebbe il termine dominante e
  sovrastimerebbe la correzione di circa tre volte — un errore facile da fare.
- **B(H₂O–H₂O) da IAPWS-IF97**, ricavato come limite di bassa densità della regione 2, invece
  che da una correlazione generalizzata: per una molecola polare con legame a idrogeno nessuna
  correlazione a stati corrispondenti è affidabile. Gli altri B_ij da Pitzer-Curl con le
  regole di combinazione di Prausnitz.

Risultati:

| T [K] | B(H₂O) [m³/mol] | Z miscela | h residua [kJ/kg] |
|---|---|---|---|
| 628 | −8.85·10⁻⁵ | 0.9984 | −10.77 |
| 700 | −6.50·10⁻⁵ | 1.0013 | −7.59 |
| 850 | −3.65·10⁻⁵ | 1.0040 | −3.59 |
| 1000 | −2.13·10⁻⁵ | 1.0048 | −1.36 |
| 1240 | −1.30·10⁻⁵ | 1.0048 | +1.11 |

**Salto entalpico 967.5 → 355 °C: ideale 1352.5 kJ/kg, reale 1364.3 kJ/kg, +0.87 %.**

E sul risultato completo:

| | prima (gas ideale) | dopo (gas reale) | datasheet |
|---|---|---|---|
| potenza | 115.53 MW | **116.54 MW** | 116.61 |
| vapore | 344 t/h | **347 t/h** | 347.7 |
| scarto | −0.93 % | **−0.06 %** | — |

**Lo scarto residuo è sceso da −0.93 % a −0.06 %.** Nota metodologica: questo chiude anche la
questione dello **shift omogeneo**. Se una frazione della reazione avvenisse nei tubi, il
bilancio si sposterebbe di qualche decimo di punto; il fatto che con shift congelata l'accordo
sia ora dello 0.06 % è di per sé la prova che nei tubi la reazione **non avanza**.

Costo: il tempo di calcolo passa da ~100 a ~400 s, perché B_mix(T) entra nell'inversione
entalpia → temperatura. Mitigato tabellando B_mix una volta per composizione e interpolando.

### 19.3 Flusso critico: la conclusione è che non esiste un numero affidabile

| modello | q critico [kW/m²] | DNBR |
|---|---|---|
| **Palen, fattore di fascio (base del calcolo)** | **314** | **0.80** |
| Zuber (piastra infinita) + derating sul titolo | 3235 | 8.20 |
| Lienhard-Dhir (tubo singolo) + derating | 2880 | 7.30 |
| Lienhard-Eichhorn (cilindro in crossflow) | 162958 | *fuori campo* |

I modelli non divergono di poco: divergono di **un ordine di grandezza**, e non perché uno sia
sbagliato, ma perché **nessuno è tarato su questa geometria a questa pressione**:

- **Palen**: ψ = D_fascio·L/A = 0.0169 darebbe φ_b = 3.1ψ = **0.052**, troncato a 0.10 per
  pratica HEDH. Il troncamento stesso dice che il criterio è usato fuori campo. È tarato su
  ribollitori kettle molto più piccoli, dove l'unica circolazione è quella indotta dalle bolle;
  qui l'acqua attraversa il fascio a **6.3 m/s**. È un **limite inferiore**, non una previsione.
- **Zuber e Lienhard-Dhir**: ignorano del tutto l'effetto fascio. Limiti superiori.
- **Lienhard-Eichhorn**: geometricamente sarebbe quello giusto (cilindro investito da corrente),
  ma è tarato a bassa pressione, dove ρ_l/ρ_v vale centinaia; qui vale **9.6**. Il gruppo
  ρ_v·h_fg·u su cui è costruito esplode ad alta pressione. **Verificato e scartato.**

**Quello che resta affidabile**: il **flusso di picco** (395 kW/m²), che il calcolo determina
senza alcuna correlazione di crisi e si confronta con l'esperienza di apparecchi analoghi; e la
**posizione** del punto critico (subito a valle della ferrula, banda superiore), robusta rispetto
a ogni assunzione. Per una risposta quantitativa sul margine servono dati sperimentali su fascio,
oppure la **look-up table di Groeneveld** con i fattori correttivi per fascio.

### 19.4 Incertezza dovuta alle correlazioni

Valutata nella cella di picco, ricalcolando la catena di resistenze:

| gruppo | banda sul flusso | banda su T metallo |
|---|---|---|
| correlazione lato gas | −3.4 % … +18.0 % | 426 … 449 °C |
| correlazione di ebollizione | +0.9 % … +1.2 % | 424 … 425 °C |
| regola di miscelazione | −3.2 % … 0.0 % | 426 … 430 °C |

Il messaggio è netto: **cambiare la correlazione di ebollizione non sposta nulla** (la resistenza
lato acqua è una frazione minima del totale), mentre la correlazione **lato gas vale 23 K sulla
temperatura del metallo**. L'incertezza del risultato è quasi tutta l'incertezza della
correlazione lato gas — ed è il motivo per cui si usa Gnielinski, la più accurata nel campo di
transizione e turbolento.

### 19.5 Pulito / sporco sui due lati

Progetto in condizione **sporca** come da datasheet. Confronto locale nella cella di picco:

| condizione | U [W/m²K] | q″ [kW/m²] | T met. int [°C] | ΔT deposito [K] | DNBR |
|---|---|---|---|---|---|
| pulito entrambi i lati | 1149 | 732 | 411 | 0 | 0.39 |
| sporco solo lato gas | 682 | 435 | 375 | 0 | 0.66 |
| sporco solo lato acqua | 980 | 625 | **492** | 93.7 | 0.46 |
| **sporco entrambi (progetto)** | **619** | **395** | **430** | **59.2** | **0.73** |

Tre conclusioni non ovvie:

1. **La condizione critica per la crisi di ebollizione è l'apparecchio PULITO**, cioè appena
   messo in servizio: U più alto, flusso di picco quasi doppio, DNBR peggiore (0.39 contro 0.73).
   Istintivamente si associa il rischio allo sporco: è il contrario.
2. **I due lati non sono equivalenti.** Lo sporco lato **gas** sta a monte del metallo: riduce
   il flusso e **raffredda** il tubo (375 °C), quindi è quasi benefico dal punto di vista
   metallurgico. Lo sporco lato **acqua** sta a valle: riduce il flusso ma **scalda** il tubo
   (492 °C). È per questo che il condizionamento chimico dell'acqua conta più della pulizia
   lato gas.
3. Il caso "sporco solo lato acqua" porta il metallo a **492 °C**: ancora sotto il limite del
   T22 (580 °C) ma con margine ridotto. È lo scenario da tenere d'occhio se l'acqua di caldaia
   peggiora senza che lo faccia il gas.

### 19.6 Valvola a farfalla: Cv dalla teoria

Curva del costruttore non disponibile, disco piano con asse di rotazione passante per il centro.
Si è costruito ζ dalla sola geometria:

```
  sigma = A_libera/A = 1 − sin(alpha) − (4 t)/(pi d) cos(alpha)
  Cc    = 0.62 + 0.38 sigma³                      (Weisbach)
  zeta  = ( 1/(Cc sigma) − 1 )²  +  0.20          (contrazione + riespansione + forma)
```

e da ζ il coefficiente di efflusso:

```
  Cv = 29.9 d² / sqrt(zeta)     d in pollici
  Kv = Cv / 1.156               m³/h con 1 bar
```

Verifica incrociata con il Kv **richiesto dal servizio**, Kv = w/√(1000·ρ·Δp[bar]):

| apertura | ζ tabella | ζ teoria | Cv | Kv geometrico | Kv richiesto |
|---|---|---|---|---|---|
| 16.7° (min) | 1521 | 2555 | 90 | 78 | 78 |
| **23.5° (normale)** | **357** | **525** | **186** | **161** | **160** |
| 29.8° (max) | 123 | 176 | 317 | 274 | 273 |
| 90° (aperta) | 0.2 | 0.2 | 7837 | 6779 | 6762 |

**Kv geometrico e Kv richiesto coincidono a ogni apertura**: è la verifica che l'intera catena
idraulica è consistente. Il rapporto di caduta x = Δp/p₁ vale 0.003, quindi il flusso è
lontanissimo dal critico e il modello incomprimibile è abbondantemente valido.

La teoria è conservativa del **35-50 %** nel campo di lavoro. Lo scarto è fisico: il passaggio
reale sono **due luci a mezzaluna** con getti che si ricongiungono e recuperano parte della
pressione, mentre il modello tratta il passaggio come una contrazione unica seguita da
riespansione brusca. Sotto i 10-15° di apertura la teoria diverge del tutto, perché l'area libera
geometrica tende a zero mentre nella valvola reale governa il trafilamento sulla battuta. Per il
dimensionamento si usa la tabella; la teoria conferma che la tabella è coerente con la geometria
reale e dà un limite superiore di ζ.

### 19.7 Dati confermati e loro effetto

| dato | valore | effetto |
|---|---|---|
| quota drum − WHB | **6.0 m confermata** | l'assunzione regge: nessuna correzione |
| materiale tubi | **SA-213 T22** (era T11) | limite metallurgico 550 → 580 °C; α minore, quindi la dilatazione differenziale scende da 4.19 a **2.34 mm**, la compressione da 34.8 a **19.4 MPa** e il carico sulla giunzione da 11.7 a **6.5 kN/tubo** |
| gioco foro diaframma / OD tubo | **0.40 mm sul diametro** | 0.20 mm radiali = 0.5 % del raggio: il vincolo radiale è effettivo, quindi i diaframmi valgono come **anelli di irrigidimento** contro la pressione esterna. L'ipotesi del calcolo è confermata |
| passo diaframmi | **variabile** | si assume il passo governante (il più lungo); il buckling è poco sensibile |
| R0A / R0B | **mai realizzati** | confermato: l'estremità calda resta senza riser dedicato |
| sporcamento | **condizione sporca** | confermata la base di progetto; aggiunto il confronto pulito/sporco |

### 19.8 Cosa resta aperto

1. **Curva Δp delle interne del corpo cilindrico** — l'unica assunzione ancora aperta sulla
   circolazione (K del convogliatore: 0 → 4 dà 0 → 72 mbar, CR da 12.1 a ~8).
2. **Verifica a pressione esterna con carico di punta** — sarà svolta con modulo dedicato;
   quanto c'è qui resta uno screening.
3. **Posizioni assiali reali dei bocchelli** — quelle attuali sono di primo tentativo.
4. **Il «20 % vaporization in BFW @ 80 % load»** del datasheet del corpo cilindrico resta da
   chiarire. L'interpretazione più naturale è che, all'80 % del carico, il 20 % dell'acqua
   alimento **flasha** entrando nel corpo cilindrico — il che sposta il bilancio termico del
   drum e la temperatura effettiva dell'acqua nei downcomer. L'interpretazione alternativa
   (all'80 % della portata di gas si ottiene il 20 % del vapore nominale) non è coerente con il
   bilancio: a 80 % di gas il vapore scenderebbe a circa il 78 %, non al 20 %. Da confermare con
   il costruttore, perché se il flash è reale i downcomer non portano liquido saturo puro e il
   battente motore ne risente.

---

## 20. Vibrazioni indotte dal flusso: la verifica che mancava

### 20.1 Perché è a sé

Un tubo può essere perfettamente verificato a temperatura, pressione e dilatazione e rompersi lo
stesso in poche migliaia di ore **perché vibra**. Nei fasci attraversati da crossflow è la causa
di rottura più comune, e non dipende dalla termica: solo da velocità, densità, geometria e
campata. Quattro meccanismi:

1. **instabilità fluido-elastica** — il pericoloso;
2. **distacco di vortici** (Strouhal) — risonanza, molto attenuata in bifase;
3. **buffeting turbolento** — fatica e usura ai supporti, non collasso;
4. **risonanza acustica** — solo con gas comprimibile a mantello: **non si applica**.

### 20.2 L'instabilità fluido-elastica non è una risonanza

Non c'è una frequenza da evitare: c'è una **velocità da non superare**. Sotto quella velocità il
fluido smorza le oscillazioni; sopra, il tubo estrae energia dal flusso a ogni ciclo e l'ampiezza
cresce senza limite, fino all'urto contro i vicini o alla rottura per fatica in corrispondenza del
diaframma. Non c'è ginocchio graduale: è un interruttore.

```
  f_n     = (lambda²/2 pi) sqrt( E I / (m L⁴) )          lambda² = 22.37 (incastro-incastro)
  m       = m_metallo + m_gas interno + Cm rho pi D²/4   (massa aggiunta idrodinamica)
  Cm      = ((De/D)²+1)/((De/D)²-1),  De/D = (0.96 + 0.5 P/D) P/D
  V_crit  = K f_n D sqrt( m delta / (rho D²) )           (Connors)
```

Il gioco foro diaframma / OD tubo di **0.40 mm sul diametro** (0.20 radiali) giustifica il
vincolo a incastro.

### 20.3 Il risultato: la banda superiore è al limite

| banda | y [m] | f propria [Hz] | V varco [m/s] | V critica [m/s] | **V/Vcrit** | esito |
|---|---|---|---|---|---|---|
| 0 | −0.78 | 125.9 | 3.77 | 5.02 | 0.750 | ok |
| 3 | −0.36 | 129.1 | 1.85 | 5.49 | 0.336 | ok |
| 6 | +0.07 | 131.3 | 2.88 | 5.88 | 0.489 | ok |
| 10 | +0.64 | 133.9 | 3.53 | 6.42 | 0.551 | ok |
| **11** | **+0.78** | **134.2** | **6.31** | **6.48** | **0.974** | **ATTENZIONE** |

**La banda superiore è al 97 % della velocità critica**, contro un limite di progetto di 0.8. Ed è
la stessa banda dove cade il DNBR minimo: la zona alta del fascio è critica su **due** fronti
indipendenti, per la stessa ragione fisica — è dove passa tutta la portata di circolazione già
carica del vapore prodotto dalle bande sottostanti.

Distacco di vortici: f_s/f_n = 0.63 nella banda 11, dentro la finestra di aggancio 0.5–2, ma in
flusso bifase il meccanismo è fortemente attenuato perché le bolle distruggono la coerenza della
scia. Buffeting: f/f_n = 0.58, nessuna coincidenza modale.

### 20.4 La campata decide, con il quadrato

f_n va come 1/L² e V_crit è proporzionale a f_n: **il rapporto V/Vcrit cresce come L²**.

| campata [m] | f propria [Hz] | V critica [m/s] | V/Vcrit | esito |
|---|---|---|---|---|
| 0.60 | 536 | 25.9 | 0.243 | ok |
| 0.80 | 302 | 14.6 | 0.433 | ok |
| 1.00 | 193 | 9.34 | 0.676 | ok |
| **1.20** | **134** | **6.48** | **0.974** | **attenzione** |
| 1.50 | 86 | 4.15 | 1.522 | **critico** |
| 1.80 | 60 | 2.88 | 2.191 | **critico** |

- **campata massima per V/Vcrit ≤ 0.8: 1.09 m**
- **campata massima per V/Vcrit ≤ 1.0: 1.22 m**

Il passo dei diaframmi è **variabile** lungo l'apparecchio: è la **campata più lunga** a decidere,
e va confrontata con questi valori. Se da qualche parte supera 1.2 m, in quel tratto e nella banda
alta l'instabilità è prevista dal criterio conservativo.

### 20.5 Le costanti contano quanto la geometria

Si è usato **K = 3.0** (inviluppo inferiore dei dati sperimentali) e **δ = 0.03** (estremo basso
del campo misurato da Pettigrew in crossflow bifase, 0.03–0.10): la combinazione più conservativa
possibile. Con K = 4.5 e δ = 0.06 la velocità critica sale di un fattore 2.1 e il rapporto scende
a 0.46, cioè ampiamente verificato.

**Questa è la prima cosa da fare se il risultato preoccupa: non cambiare il progetto, ma
procurarsi K e δ giusti per questo reticolo** (triangolare 60°, P/D = 1.333) in flusso bifase ad
alta pressione. La differenza fra "critico" e "ampiamente verificato" sta interamente lì.

---

## 21. Transitori e protezione

| grandezza | valore |
|---|---|
| costante di tempo termica del metallo | **1.9 s** |
| volume libero a mantello | 28.4 m³ (α media 0.198) |
| inventario d'acqua liquida | **14 990 kg** |
| **tempo di evaporazione a secco a potenza piena** | **155 s (2.6 minuti)** |
| portata di reintegro per compensare la potenza | 96.5 kg/s (347 t/h) |
| T metallo di equilibrio dopo dry-out | **636 °C** (limite T22: 580 °C) |
| tempo per avvicinarla (3 τ) | 41 s |

Tre conseguenze operative:

1. **Il metallo non ha inerzia.** τ = 1.9 s: il tubo segue la temperatura del gas praticamente in
   tempo reale. Non esiste nessun cuscinetto termico, e non ha senso sperare che «una punta breve
   non faccia in tempo a scaldare il metallo». Fa in tempo.
2. **Quello che protegge è l'acqua, e dura 2.6 minuti.** Se la circolazione si ferma di colpo a
   potenza piena, i 15 tonnellate di acqua a mantello evaporano in 155 s. È il tempo che la
   protezione ha per intercettare il gas o ripristinare la circolazione, e va confrontato con il
   tempo di chiusura reale della valvola di intercettazione a monte. Non è un margine comodo.
3. **Dopo il dry-out il metallo va a 636 °C in ~40 s**, oltre il limite del T22. Non è
   danneggiamento progressivo: è rottura in tempi brevi. È la ragione per cui la protezione di
   basso livello nel corpo cilindrico deve essere un **blocco**, non un allarme.

Per l'**avviamento** il caso severo per la meccanica è l'apparecchio **caldo e non in pressione**
(condizione LC2 del §15.5), perché manca il carico di estremità che in esercizio annulla la
compressione da dilatazione impedita. In pratica: pressurizzare prima di scaldare, e non lasciare
l'apparecchio caldo depressurizzato più del necessario.

Sono bilanci di primo ordine, non una simulazione dinamica: servono a dimensionare i tempi di
intervento e a decidere quali protezioni devono essere blocchi.

---

## 22. Curve di carico parziale 50–110 %

Portata di gas scalata, composizione / temperatura d'ingresso / pressione del corpo cilindrico
invariate, farfalla libera di riposizionarsi per centrare i 355 °C. Maglia ridotta 40 × 8 (la
convergenza di griglia è già dimostrata, quindi il confronto fra carichi resta valido; l'angolo
assoluto differisce di ~3° da quello a maglia fine).

| carico | w gas [kg/s] | potenza [MW] | vapore [t/h] | T tubi [°C] | **farfalla [°]** | by-pass [%] | **CR** | q″max [kW/m²] | T met [°C] | **DNBR** | α max | ΔP gas [mbar] |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 50 % | 42.7 | 58.27 | 174 | 328.8 | **42.9** | 4.72 | 20.0 | 295 | 407 | **1.01** | 0.281 | 26 |
| 60 % | 51.3 | 69.93 | 208 | 331.4 | 40.7 | 4.23 | 17.7 | 320 | 413 | 0.93 | 0.307 | 37 |
| 70 % | 59.8 | 81.58 | 243 | 334.4 | 38.1 | 3.70 | 15.8 | 342 | 419 | 0.86 | 0.331 | 52 |
| 80 % | 68.3 | 93.24 | 278 | 337.7 | 35.0 | 3.13 | 14.3 | 361 | 425 | 0.81 | 0.352 | 68 |
| 90 % | 76.9 | 104.89 | 313 | 341.5 | 31.5 | 2.51 | 13.1 | 378 | 429 | 0.77 | 0.372 | 88 |
| **100 %** | **85.4** | **116.54** | **347** | **345.5** | **27.1** | **1.83** | **12.0** | **393** | **434** | **0.73** | **0.391** | **111** |
| 110 % | 94.0 | 128.20 | 382 | 349.9 | **21.4** | 1.09 | 11.2 | 407 | 437 | 0.70 | 0.409 | 137 |

### 22.1 Regolazione: la valvola è della taglia giusta su tutto il campo

La farfalla deve muoversi da **42.9° al 50 %** a **21.4° al 110 %**, contro una finestra di
controllabilità 15–70°. **Tutti i carichi cadono dentro.** La corsa utile è di soli 21 gradi su
tutto il campo di funzionamento: è poca, e conferma che il by-pass è sovradimensionato in
diametro. Un diaframma fisso in serie allargherebbe la corsa e renderebbe la regolazione più fine.

Il verso è quello che ci si aspetta ma vale la pena esplicitarlo: **a carico ridotto serve PIÙ
by-pass**, non meno (4.72 % al 50 % contro 1.09 % al 110 %). A portata bassa il fascio ha più
superficie per unità di portata, quindi sovra-raffredda di più e il by-pass deve compensare.

Attenzione al vincolo di **lavaggio del liner** all'estremo alto: al 110 % la portata deviata è
1.02 kg/s contro 1.56 al nominale, quindi la velocità nel liner scende a ~2.3 m/s. È ancora sopra
il minimo di 1.5 m/s, ma il margine si assottiglia proprio dove il carico è massimo. Sopra il
115–120 % il vincolo diventerebbe attivo.

### 22.2 Nessuna sorpresa sulla circolazione

Il rapporto di circolazione **migliora** al calare del carico, da 12.0 a 20.0: il battente motore
cala meno delle perdite, che vanno con il quadrato della portata. **Il carico ridotto non è una
condizione critica per la circolazione**, e la frazione di vuoto massima scende da 0.391 a 0.281.

### 22.3 Il carico pieno resta la condizione critica

Il DNBR degrada monotonicamente con il carico: **1.01 al 50 %, 0.73 al 100 %, 0.70 al 110 %**. Il
flusso termico cresce più in fretta del flusso critico. Combinato con il risultato del §19.5 — la
condizione peggiore è l'apparecchio **pulito** — la condizione di progetto più severa è
**carico pieno con apparecchio pulito**, cioè le prime ore di marcia dopo una fermata di pulizia.

### 22.4 La temperatura del metallo cala meno di quanto ci si aspetti

Da 437 a 407 °C fra 110 % e 50 %, cioè 30 K per un dimezzamento della portata. Il coefficiente
lato gas scende come la portata alla 0.8, quindi la resistenza dominante peggiora relativamente e
gran parte del guadagno si perde. **Non si può contare sul carico ridotto per proteggere il
metallo**: se il problema è la temperatura di parete, la leva è lo sporcamento lato acqua, non il
carico.

### 22.5 Avvertenza

La temperatura d'ingresso del gas è stata mantenuta costante a tutti i carichi. Nella marcia reale
un carico ridotto del reformer comporta di solito anche una temperatura d'ingresso diversa: per
una curva d'esercizio realistica serve la coppia **(portata, temperatura)** del bilancio d'impianto
a ogni carico. Con quei dati il calcolo si rifà in una manciata di minuti.

### 22.6 Maldistribuzione della portata fra i tubi

Il calcolo di base assume portata identica in tutti gli 848 tubi. Sensibilità allo scarto del tubo
più caricato:

| scarto | w per tubo | q″ picco [kW/m²] | T met. int [°C] | DNBR min |
|---|---|---|---|---|
| 0 % | 101 g/s | 393 | 433.5 | 0.73 |
| 5 % | 106 g/s | 400 | 435.5 | 0.72 |
| 10 % | 111 g/s | 407 | 437.4 | 0.70 |
| 15 % | 116 g/s | 414 | 439.2 | 0.69 |
| 20 % | 121 g/s | 420 | 440.9 | 0.68 |
| 30 % | 131 g/s | 431 | 444.0 | 0.65 |

Il tubo più caricato scambia di più **ma si scalda di più**: il coefficiente lato gas cresce come
la portata alla 0.8, quindi il flusso di picco cresce quasi in proporzione, mentre il tempo di
residenza cala e il gas esce più caldo da quel tubo. Le due cose si sommano — **è
contemporaneamente il tubo con il DNBR peggiore e quello che consegna gas più caldo**.

La buona notizia è la pendenza: **+10 % di portata costa +4 K sul metallo e −0.03 di DNBR**. Non è
un effetto amplificato, è quasi lineare e modesto. Anche un 30 % di maldistribuzione — valore
elevato — sposta il metallo di 10 K e il DNBR di 0.08. **La maldistribuzione non è la spiegazione
di un eventuale problema, e non è una leva di progetto.**

Il valore realistico dipende dalla camera d'ingresso: con bocchello assiale centrato e camera
profonda si resta entro il 5–10 %; con ingresso laterale o camera corta si arriva al 20–30 % sui
tubi affacciati al getto. Serve la geometria della camera per dire quale riga leggere.

---

## Appendice A. Definizioni: CR, DNB, DNBR

Tre sigle che ricorrono in tutti i documenti e che è facile confondere, perché due riguardano
lo stesso fenomeno ma una è il fatto fisico e l'altra il numero che lo misura.

### CR — rapporto di circolazione

```
  CR = portata d'acqua circolante nel mantello / portata di vapore prodotta      [adimensionale]
```

Equivale al reciproco del titolo in uscita dal fascio: **CR = 10 significa che di ogni 10 kg di
miscela che escono dal mantello 1 kg è vapore e 9 kg sono acqua** che torna al corpo cilindrico.

**Non è un dato di progetto**: è il risultato di un equilibrio. La forza motrice è la differenza
di peso fra la colonna di acqua satura che scende nei downcomer e le colonne di miscela bifase,
più leggere, che salgono nel fascio e nei riser:

```
  dp_motore = g [ rho_liq H_dc − rho_fascio H_fascio − rho_riser H_riser ]
```

A questa si oppongono le perdite di carico del giro, che crescono con il quadrato della portata.
Il CR di esercizio è il valore che le pareggia. Le densità delle colonne bifase dipendono dal
titolo, quindi da CR: è un problema implicito, risolto per bisezione.

**Perché serve che sia alto**: l'acqua in eccesso è quella che *lava* i tubi. Se ce n'è poca, i
ranghi alti si scoprono. Criterio pratico **CR ≥ 10**, cioè titolo ≤ 0.10.

### DNB — Departure from Nucleate Boiling (crisi di ebollizione)

È **il fenomeno**. In ebollizione nucleata le bolle nascono su singoli siti della parete, si
staccano e l'acqua le rimpiazza subito: è il regime di scambio più efficiente che esista, e il
metallo resta a pochi gradi sopra l'acqua anche con flussi enormi.

Oltre un certo flusso le bolle si generano più in fretta di quanto l'acqua riesca a
rimpiazzarle: si toccano, si saldano, e formano un **film di vapore continuo** che avvolge la
parete. Il vapore conduce una ventina di volte peggio dell'acqua, quindi lo scambio crolla. Il
calore continua ad arrivare dal gas ma non passa più, e **la temperatura del metallo sale di
centinaia di gradi in pochi secondi**.

Nei fasci si chiama anche **steam blanketing**, perché il film si forma di preferenza sui ranghi
alti, dove la miscela arriva già carica del vapore prodotto sotto.

**Non è un peggioramento progressivo: è un salto.** Sotto la soglia non succede niente, sopra il
tubo cede per sfondamento a caldo in tempi brevissimi. Per questo non si progetta al limite.

### CHF — Critical Heat Flux

Il valore di flusso termico al quale si innesca il DNB. Dipende dalla pressione, dalla geometria
del fascio e soprattutto dal **titolo locale**: più vapore c'è già nella miscela che lava il
tubo, meno flusso serve per scoprire la parete.

### DNBR — Departure from Nucleate Boiling Ratio

```
  DNBR = flusso termico critico locale / flusso termico effettivo locale        [adimensionale]
```

È **un margine**: DNBR = 3 vuol dire che si lavora a un terzo del limite, 1 esattamente al
limite, sotto 1 oltre il limite. La pratica di progetto chiede almeno 2.

Si calcola **cella per cella**, perché sia il flusso sia il valore critico cambiano lungo il tubo
e da banda a banda. Ne consegue una cosa poco intuitiva ma importante: **il DNBR minimo non cade
dove il flusso termico è massimo**, ma dove il rapporto fra i due è peggiore — cioè nella banda
superiore, dove il titolo è alto perché l'acqua ha già attraversato tutte le bande sottostanti.

**Avvertenza, ripetuta perché conta**: nessuno dei modelli di CHF disponibili è tarato su questa
geometria a questa pressione, e fra loro divergono di un ordine di grandezza (§19.3). Il DNBR va
usato per confronti **relativi** — fra zone dello stesso apparecchio o fra varianti dello stesso
progetto — non come numero assoluto.

### In una riga

| sigla | che cos'è | tipo |
|---|---|---|
| **CR** | quanta acqua gira per ogni chilo di vapore | rapporto di portate |
| **DNB** | il film di vapore che isola la parete | fenomeno fisico |
| **CHF** | il flusso al quale il DNB si innesca | grandezza fisica [W/m²] |
| **DNBR** | quanto si è lontani dal CHF | margine adimensionale |

---

## Riferimenti

- V. Ganapathy, *Boiler circulation calculations*, Hydrocarbon Processing — <http://v_ganapathy.tripod.com/circulation.pdf>
- J. W. Palen, *Shell-and-tube reboilers*, in **Heat Exchanger Design Handbook** (HEDH)
- Thermopedia, *Tubes and tube banks, boiling heat transfer on* — <https://www.thermopedia.com/content/1213/>
- G. Hagan, *Understand Heat Flux Limitations on Reboiler Design* — <https://ureaknowhow.com/wp-content/uploads/2025/06/2010-Hagan-Understand-heat-Flux-Limitations-on-Reboiler-Design.pdf>
- BORSIG Process Heat Exchanger, *Synloop Waste Heat Boilers in Ammonia Plants* — <https://www.borsig.de/fileadmin/mediamanager/Downloads/BPHE_Synloop_Waste_Heat_Boilers_E.pdf>
- Altex Industries, *Steam drums and boiler circulation in fire-tube waste heat boilers* — <https://www.altexinc.com/case-studies/shell-tube-heat-exchangers/steam-drums-and-boiler-circulation-in-fire-tube-waste-heat-boilers/>
- Ecolab/Nalco, *Failures diagnosis and prediction in heat recovery boilers in the chemical industry* — <https://en-br.ecolab.com/nalco-water/news/2021/07/heat-recovery-boilers>
- *Failure analysis of waste heat boiler tubing caused by a high local heat flux*, Eng. Failure Analysis — <https://www.sciencedirect.com/science/article/abs/pii/S1350630722001212>
- *Metal dusting (catastrophic carburization) of a waste heat boiler tube* — <https://www.sciencedirect.com/science/article/abs/pii/1350630794900043>
- National Board, *Short-term high temperature failures* — <https://www.nationalboard.org/index.aspx?pageID=164&ID=186>
- Numerical investigation of shell-side flow behavior and vapor distribution in waste heat boilers — <https://pmc.ncbi.nlm.nih.gov/articles/PMC11209691/>
- IAPWS-IF97 (proprietà acqua/vapore), IAPWS 2008 (viscosità), IAPWS 2011 (conducibilità), IAPWS 1994 (tensione superficiale)
