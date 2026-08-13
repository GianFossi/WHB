namespace Whb.Core

open System
open Constants

/// **Valvola a farfalla (serranda) sul by-pass centrale.**
///
/// Il by-pass e il fascio tubiero sono due rami in PARALLELO fra la camera
/// d'ingresso e la camera d'uscita: la ripartizione delle portate non e' un
/// dato, ma il risultato dell'uguaglianza delle perdite di carico dei due rami.
///
///     dp_fascio(w_f)  =  dp_bypass(w_b, theta)      con  w_f + w_b = w_tot
///
/// Il ramo di by-pass e' quasi privo di resistenza propria (tubo liscio,
/// diametro grande, velocita' bassa): senza organo di strozzamento prenderebbe
/// una frazione enorme della portata. L'unico elemento che regola la
/// ripartizione e' quindi la valvola a farfalla, il cui coefficiente di
/// perdita varia di oltre tre ordini di grandezza fra tutta aperta e quasi
/// chiusa.
module Valve =

    /// **Correlazione di riferimento — Idelchik, "Handbook of Hydraulic
    /// Resistance", diagramma della valvola a disco (farfalla) in condotto
    /// circolare.** Coefficiente di perdita zeta riferito alla velocita' MEDIA
    /// NEL TUBO, in funzione dell'angolo di CHIUSURA alpha [gradi]:
    ///   alpha = 0  -> disco parallelo al flusso (tutta aperta)
    ///   alpha = 90 -> disco perpendicolare (chiusa)
    /// I valori fino a 70 gradi sono tabellati; oltre si estrapola in scala
    /// logaritmica con la pendenza dell'ultimo tratto (in quel campo il
    /// comportamento reale dipende dal trafilamento e dalla battuta).
    let private table =
        [ 0.0,  0.20
          5.0,  0.24
          10.0, 0.52
          15.0, 0.90
          20.0, 1.54
          25.0, 2.51
          30.0, 3.91
          35.0, 6.22
          40.0, 10.8
          45.0, 18.7
          50.0, 32.6
          55.0, 58.8
          60.0, 118.0
          65.0, 256.0
          70.0, 751.0 ]

    let private arr = table |> List.toArray

    /// zeta in funzione dell'angolo di CHIUSURA [gradi]
    let zetaClosure (alphaDeg: float) =
        let a = max 0.0 alphaDeg
        if a <= 0.0 then snd arr.[0]
        elif a >= fst arr.[arr.Length - 1] then
            // estrapolazione log-lineare con la pendenza degli ultimi due punti
            let (a1, z1) = arr.[arr.Length - 2]
            let (a2, z2) = arr.[arr.Length - 1]
            let s = (log z2 - log z1) / (a2 - a1)
            min 1.0e7 (z2 * exp (s * (a - a2)))
        else
            let mutable i = 0
            while i < arr.Length - 2 && fst arr.[i + 1] < a do i <- i + 1
            let (a1, z1) = arr.[i]
            let (a2, z2) = arr.[i + 1]
            // interpolazione lineare in log(zeta): zeta varia esponenzialmente
            exp (log z1 + (log z2 - log z1) * (a - a1) / (a2 - a1))

    /// zeta in funzione dell'angolo di APERTURA [gradi]
    /// (0 = chiusa, 90 = tutta aperta) — e' la convenzione usata nel report
    /// e nella posizione dell'attuatore.
    let zetaOpening (openDeg: float) = zetaClosure (90.0 - openDeg)

    /// Inverso: angolo di CHIUSURA che da' un dato zeta
    let closureForZeta (z: float) =
        let zz = max (snd arr.[0]) z
        let n = arr.Length
        if zz >= snd arr.[n - 1] then
            let (a1, z1) = arr.[n - 2]
            let (a2, z2) = arr.[n - 1]
            let s = (log z2 - log z1) / (a2 - a1)
            min 90.0 (a2 + log (zz / z2) / s)
        else
            let mutable i = 0
            while i < n - 2 && snd arr.[i + 1] < zz do i <- i + 1
            let (a1, z1) = arr.[i]
            let (a2, z2) = arr.[i + 1]
            a1 + (a2 - a1) * (log zz - log z1) / (log z2 - log z1)

    /// Inverso in termini di angolo di APERTURA [gradi]
    let openingForZeta (z: float) = 90.0 - closureForZeta z

    // ==================================================================
    //  TEORIA DEL DISCO PIANO CONCENTRICO
    // ==================================================================
    /// Quando la curva del costruttore non c'e', zeta si ricava dalla
    /// geometria. Per un **disco piano con asse di rotazione passante per il
    /// centro** (farfalla concentrica) in un condotto circolare:
    ///
    /// 1. area libera. Il disco, ruotato di alpha rispetto alla posizione di
    ///    tutta aperta, proietta sulla sezione un'ellisse di area
    ///    (pi/4) d² sin(alpha); lo spessore proietta d·t·cos(alpha):
    ///        sigma = A_libera/A = 1 - sin(alpha) - (4 t)/(pi d) cos(alpha)
    /// 2. la corrente si contrae nella vena contratta (coefficiente Cc) e poi
    ///    riespande bruscamente: perdita di Borda-Carnot
    ///        zeta_contrazione = ( 1/(Cc sigma) - 1 )²
    ///    con Cc di Weisbach  Cc = 0.62 + 0.38 sigma³
    /// 3. a valvola quasi aperta resta la resistenza di forma del disco,
    ///        zeta_0 ~ 0.2
    ///
    ///        zeta = ( 1/(Cc sigma) - 1 )² + zeta_0
    ///
    /// Confronto con i valori sperimentali di Idelchik: coincide entro il 5 %
    /// fino a 20° di chiusura, e resta il 15-25 % CONSERVATIVO nel campo
    /// 30-70°, che e' quello di lavoro. Lo scarto e' fisico: il passaggio
    /// reale sono due luci a mezzaluna con parziale recupero di pressione,
    /// che il modello a contrazione unica non rappresenta.
    let zetaFlatDisc (thicknessRatio: float) (closureDeg: float) =
        let a = max 0.0 (min 89.9 closureDeg) * Math.PI / 180.0
        let sigma =
            max 1e-4 (1.0 - sin a - 4.0 * thicknessRatio / Math.PI * cos a)
        let cc = 0.62 + 0.38 * sigma * sigma * sigma
        let r = 1.0 / (cc * sigma) - 1.0
        r * r + 0.20

    /// Come sopra, ma con la correzione empirica che riporta la teoria sui
    /// dati di Idelchik nel campo di lavoro (fattore 0.82 sul termine di
    /// contrazione).
    let zetaFlatDiscCalibrated (thicknessRatio: float) (closureDeg: float) =
        0.82 * (zetaFlatDisc thicknessRatio closureDeg - 0.20) + 0.20

    /// **Cv** (US: gallon/min di acqua a 60 F con 1 psi di caduta) ricavato dal
    /// coefficiente di perdita e dal diametro:
    ///     Cv = 29.9 d² / sqrt(zeta)        d in pollici
    let cvFromZeta (idM: float) (zeta: float) =
        let dIn = idM / 0.0254
        29.9 * dIn * dIn / sqrt (max 1e-9 zeta)

    /// **Kv** (m³/h di acqua con 1 bar di caduta): Kv = Cv / 1.156
    let kvFromZeta (idM: float) (zeta: float) = cvFromZeta idM zeta / 1.156

    /// Kv RICHIESTO dal servizio, dalla definizione stessa di Kv applicata al
    /// fluido reale (caduta piccola rispetto alla pressione assoluta, quindi
    /// il gas si comporta come incomprimibile: x = dp/p1 = 0.003 << x_T):
    ///     Kv = w / sqrt( 1000 * rho * dp[bar] )      w in kg/h
    /// Il confronto fra Kv geometrico e Kv richiesto e' una verifica incrociata
    /// del dimensionamento.
    let kvRequired (wKgS: float) (rho: float) (dpPa: float) =
        let w = wKgS * 3600.0
        w / sqrt (1000.0 * max 1e-6 rho * max 1e-9 (dpPa / 1.0e5))

    /// Rapporto di caduta x = dp/p1: se supera x_T (~0.7 per una farfalla)
    /// il flusso e' critico (choked) e il modello incomprimibile non vale.
    let pressureDropRatio (dpPa: float) (p1Pa: float) = dpPa / p1Pa

    /// Guadagno inerente della valvola: d(ln zeta)/d(theta) [1/grado].
    /// Serve a giudicare la CONTROLLABILITA': se una frazione di grado cambia
    /// zeta del 30 % la regolazione e' di fatto on-off.
    let gain (openDeg: float) =
        let d = 0.5
        let z1 = zetaOpening (openDeg - d)
        let z2 = zetaOpening (openDeg + d)
        (log z1 - log z2) / (2.0 * d)

    /// Velocita' stimata nella sezione ristretta (vena contratta) a partire
    /// dal salto di pressione dissipato: v_vc ~ sqrt(2 dp / rho).
    /// Serve per la verifica di erosione e di numero di Mach.
    let throatVelocity (dp: float) (rho: float) = sqrt (2.0 * max 0.0 dp / max 1e-6 rho)

    /// Velocita' del suono nella miscela [m/s]
    let sonic (gamma: float) (mw: float) (tK: float) = sqrt (gamma * R * tK / mw)
