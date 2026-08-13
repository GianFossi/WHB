namespace Whb.Core

open System
open Constants

/// Correlazioni di scambio termico LATO ACQUA/VAPORE (mantello):
/// ebollizione nucleata a bagno, ebollizione convettiva, flusso termico critico,
/// convezione naturale e forzata su fasci di tubi orizzontali.
module WaterSide =

    type PoolBoilingCorrelation =
        | Mostinski
        | Cooper
        | Rohsenow
        | Gorenflo
        | CornwellHouston

    let poolBoilingName =
        function
        | Mostinski -> "Mostinski (1963) - stati corrispondenti"
        | Cooper -> "Cooper (1984) - stati corrispondenti + rugosità"
        | Rohsenow -> "Rohsenow (1952) - Csf acqua/acciaio"
        | Gorenflo -> "Gorenflo (VDI Heat Atlas)"
        | CornwellHouston -> "Cornwell-Houston (1994) - tubo singolo/fascio"

    // ------------------------------------------------------------------
    // Ebollizione nucleata a bagno (pool boiling)
    // ------------------------------------------------------------------

    /// Mostinski: h [W/m²K], q [W/m²], p in Pa
    let hMostinski (q: float) (p: float) (pc: float) =
        let pr = p / pc
        let pcKPa = pc / 1000.0
        let fp = 1.8 * Math.Pow(pr, 0.17) + 4.0 * Math.Pow(pr, 1.2) + 10.0 * Math.Pow(pr, 10.0)
        0.00417 * Math.Pow(pcKPa, 0.69) * Math.Pow(max q 1.0, 0.7) * fp

    /// Cooper: h [W/m²K]; rp rugosità [µm]; mMol massa molare [g/mol]
    let hCooper (q: float) (p: float) (pc: float) (rp: float) (mMol: float) =
        let pr = max 1e-4 (p / pc)
        55.0
        * Math.Pow(pr, 0.12 - 0.2 * log10 rp)
        * Math.Pow(-log10 pr, -0.55)
        * Math.Pow(mMol, -0.5)
        * Math.Pow(max q 1.0, 0.67)

    /// Rohsenow: restituisce h [W/m²K] a partire dal surriscaldamento parete ΔTe
    let hRohsenow (dTe: float) (s: Steam.SatProps) (csf: float) (sExp: float) =
        if dTe <= 0.0 then 0.0
        else
            let a = s.MuL * s.Hfg * sqrt (g * (s.RhoL - s.RhoV) / s.Sigma)
            let b = s.CpL * dTe / (csf * s.Hfg * Math.Pow(s.PrL, sExp))
            let q = a * Math.Pow(b, 3.0)
            q / dTe

    /// Gorenflo (riferimento acqua): h [W/m²K]
    let hGorenflo (q: float) (p: float) (pc: float) (rp: float) =
        let pr = max 1e-4 (min 0.95 (p / pc))
        let h0 = 5600.0
        let q0 = 20000.0
        let rp0 = 0.4
        let fp = 1.73 * Math.Pow(pr, 0.27) + (6.1 + 0.68 / (1.0 - pr)) * pr * pr
        let n = 0.9 - 0.3 * Math.Pow(pr, 0.15)
        h0 * fp * Math.Pow(max q 1.0 / q0, n) * Math.Pow(rp / rp0, 0.133)

    /// Cornwell-Houston: h [W/m²K] su tubo orizzontale di diametro d [m]
    let hCornwellHouston (q: float) (d: float) (p: float) (pc: float) (s: Steam.SatProps) =
        let pr = p / pc
        let pcBar = pc / 1.0e5
        let fp = 1.8 * Math.Pow(pr, 0.17) + 4.0 * Math.Pow(pr, 1.2) + 10.0 * Math.Pow(pr, 10.0)
        let reb = max 1.0 (q * d / (s.MuL * s.Hfg))
        let nu = 9.7 * Math.Pow(pcBar, 0.5) * fp * Math.Pow(reb, 0.67) * Math.Pow(s.PrL, 0.4)
        nu * s.KL / d

    /// Dispatcher pool boiling. Per Rohsenow si itera internamente su ΔTe.
    let hPool
        (corr: PoolBoilingCorrelation)
        (q: float)
        (d: float)
        (s: Steam.SatProps)
        (rp: float)
        (csf: float)
        =
        match corr with
        | Mostinski -> hMostinski q s.P Pc_water
        | Cooper -> hCooper q s.P Pc_water rp 18.015
        | Gorenflo -> hGorenflo q s.P Pc_water rp
        | CornwellHouston -> hCornwellHouston q d s.P Pc_water s
        | Rohsenow ->
            // risolve q = h(ΔTe)·ΔTe per ΔTe, poi h = q/ΔTe
            let f dTe =
                let a = s.MuL * s.Hfg * sqrt (g * (s.RhoL - s.RhoV) / s.Sigma)
                let b = s.CpL * dTe / (csf * s.Hfg * Math.Pow(s.PrL, 1.0))
                a * Math.Pow(b, 3.0) - q
            let dTe = bisect f 0.01 200.0 1e-6 200
            if dTe <= 0.0 then 0.0 else q / dTe

    // ------------------------------------------------------------------
    // Convezione naturale su cilindro orizzontale (Churchill-Chu)
    // ------------------------------------------------------------------
    let hNaturalConvection (d: float) (dT: float) (s: Steam.SatProps) =
        if dT <= 0.0 then 0.0
        else
            let beta = 1.0 / s.Tsat * 3.0        // stima β per acqua satura (ordine 1e-3 1/K)
            let nu_ = s.MuL / s.RhoL
            let alpha = s.KL / (s.RhoL * s.CpL)
            let ra = g * beta * dT * Math.Pow(d, 3.0) / (nu_ * alpha)
            let num = 0.60 + 0.387 * Math.Pow(ra, 1.0 / 6.0)
                            / Math.Pow(1.0 + Math.Pow(0.559 / s.PrL, 9.0 / 16.0), 8.0 / 27.0)
            let nu = num * num
            nu * s.KL / d

    // ------------------------------------------------------------------
    // Fattore di fascio (Palen) ed ebollizione convettiva
    // ------------------------------------------------------------------
    /// Fattore di fascio Fb (Palen): 1.0 (tubo singolo) .. ~3 (fascio fitto).
    /// Valore conservativo raccomandato: 1.5
    let bundleFactor (fb: float) = max 1.0 fb

    /// Fattore di Chisholm/Chen F per convezione bifase da parametro di
    /// Martinelli turbolento-turbolento.
    let chenF (x: float) (s: Steam.SatProps) =
        let x = min 0.99 (max 1e-4 x)
        let xtt =
            Math.Pow((1.0 - x) / x, 0.9)
            * Math.Pow(s.RhoV / s.RhoL, 0.5)
            * Math.Pow(s.MuL / s.MuV, 0.1)
        let inv = 1.0 / xtt
        if inv <= 0.1 then 1.0 else 2.35 * Math.Pow(inv + 0.213, 0.736)

    /// Fattore di soppressione S di Chen
    let chenS (reL: float) (f: float) =
        let retp = reL * Math.Pow(f, 1.25)
        1.0 / (1.0 + 2.53e-6 * Math.Pow(retp, 1.17))

    /// Forster-Zuber per la parte nucleata di Chen
    let hForsterZuber (dTsat: float) (dPsat: float) (s: Steam.SatProps) =
        if dTsat <= 0.0 then 0.0
        else
            0.00122
            * (Math.Pow(s.KL, 0.79) * Math.Pow(s.CpL, 0.45) * Math.Pow(s.RhoL, 0.49))
            / (Math.Pow(s.Sigma, 0.5) * Math.Pow(s.MuL, 0.29) * Math.Pow(s.Hfg, 0.24)
               * Math.Pow(s.RhoV, 0.24))
            * Math.Pow(dTsat, 0.24)
            * Math.Pow(max dPsat 0.0, 0.75)

    /// Zukauskas per convezione forzata monofase su fascio di tubi (crossflow).
    let hZukauskas (reMax: float) (pr: float) (prW: float) (k: float) (d: float) (staggered: bool) (stOverSl: float) =
        let c, m =
            if staggered then
                (if reMax < 2.0e5 then (0.35 * Math.Pow(stOverSl, 0.2), 0.60)
                 else (0.031 * Math.Pow(stOverSl, 0.2), 0.80))
            else
                (if reMax < 2.0e5 then (0.27, 0.63) else (0.021, 0.84))
        let nu = c * Math.Pow(reMax, m) * Math.Pow(pr, 0.36) * Math.Pow(pr / prW, 0.25)
        nu * k / d

    /// Coefficiente lato mantello complessivo, modello Palen:
    ///   h = h_nb · Fb · Fc + h_conv
    /// dove h_conv è la maggiore fra convezione naturale e convezione bifase.
    let shellSideHtc
        (corr: PoolBoilingCorrelation)
        (q: float)
        (dOut: float)
        (s: Steam.SatProps)
        (rp: float)
        (csf: float)
        (fb: float)
        (hConv: float)
        =
        let hnb = hPool corr q dOut s rp csf
        hnb * bundleFactor fb + hConv

    // ------------------------------------------------------------------
    // Flusso termico critico
    // ------------------------------------------------------------------
    /// Zuber (piastra piana infinita) [W/m²]
    let chfZuber (s: Steam.SatProps) =
        0.131 * s.Hfg * Math.Pow(s.RhoV, 0.5)
        * Math.Pow(s.Sigma * g * (s.RhoL - s.RhoV), 0.25)

    /// Zuber corretto per cilindro orizzontale (Lienhard-Dhir), d [m]
    let chfHorizontalTube (d: float) (s: Steam.SatProps) =
        let lc = sqrt (s.Sigma / (g * (s.RhoL - s.RhoV)))
        let rp = d / 2.0 / lc
        let corr =
            if rp > 1.2 then 0.89 + 2.27 * exp (-3.44 * sqrt rp)
            elif rp > 0.15 then 1.05 * Math.Pow(rp, -0.25)
            else 1.4 * Math.Pow(rp, -0.25)
        chfZuber s * corr

    /// Mostinski (stati corrispondenti) [W/m²]
    let chfMostinski (p: float) (pc: float) =
        let pr = p / pc
        let pcKPa = pc / 1000.0
        0.368 * pcKPa * Math.Pow(pr, 0.35) * Math.Pow(1.0 - pr, 0.9) * 1000.0

    /// Fattore di fascio di Palen: φb = 3.1·ψ, ψ = Db·L/A, limitato a [0.1, 1.0].
    /// Il limite inferiore 0.1 e' la pratica raccomandata (HEDH): sotto tale
    /// valore la correlazione, tarata su ribollitori kettle, perde significato.
    /// Per fasci attraversati da crossflow forzato (WHB a circolazione naturale)
    /// il criterio resta comunque conservativo.
    let palenPhiB (dBundle: float) (lTube: float) (areaOut: float) =
        if areaOut <= 0.0 then 1.0
        else
            let psi = dBundle * lTube / areaOut
            max 0.1 (min 1.0 (3.1 * psi))

    /// CHF di fascio (Palen): q_crit,bundle = φb · q_crit,tubo
    let chfBundle (dBundle: float) (lTube: float) (areaOut: float) (qCritTube: float) =
        palenPhiB dBundle lTube areaOut * qCritTube

    // ==================================================================
    //  CHF con CROSSFLOW FORZATO
    // ==================================================================
    /// **Lienhard-Eichhorn (1976)**: flusso critico su un cilindro investito
    /// da una corrente. E' la geometria di questo apparecchio - tubi lambiti
    /// dalla miscela che risale il fascio - e a differenza dei criteri di tipo
    /// kettle tiene conto esplicitamente della velocita'.
    ///
    ///   We_D = rho_v U² D / sigma
    ///   regime a bassa velocita':
    ///     q/(rho_v hfg U) = (1/pi) [ 1 + (4/We_D)^(1/3) ]
    ///   regime ad alta velocita':
    ///     q/(rho_v hfg U) = 1/(169 pi) + (1/19.2) We_D^(-1/3)
    /// Si prende il MAGGIORE dei due (criterio di transizione degli autori).
    let chfLienhardEichhorn (d: float) (u: float) (s: Steam.SatProps) =
        let uu = max 0.01 u
        let we = s.RhoV * uu * uu * d / s.Sigma
        let baseq = s.RhoV * s.Hfg * uu
        let low = baseq / Math.PI * (1.0 + Math.Pow(4.0 / max 1e-9 we, 1.0 / 3.0))
        let high = baseq * (1.0 / (169.0 * Math.PI) + Math.Pow(we, -1.0 / 3.0) / 19.2)
        max low high

    /// Derating del CHF con il titolo locale. Forma usata nella pratica dei
    /// fasci evaporanti (Katto; HEDH): il flusso critico decade in modo
    /// pressoche' lineare con il titolo fino a un titolo di crisi.
    ///   phi_x = max( 0.1 , 1 - x / x_crit )
    let chfQualityDerating (x: float) (xCrit: float) =
        max 0.1 (1.0 - (max 0.0 x) / (max 1e-3 xCrit))

    /// Modelli di CHF di fascio disponibili
    type ChfModel =
        /// Palen: phi_b = 3.1 psi applicato al CHF di tubo singolo (kettle)
        | PalenBundle
        /// Lienhard-Eichhorn su cilindro in crossflow, derating sul titolo
        | LienhardEichhornCrossflow
        /// Zuber puro (piastra infinita) con derating sul titolo
        | ZuberQuality
        /// Limite pratico di progetto sul flusso massimo [W/m²]
        | PracticalLimit of float

    let chfModelName =
        function
        | PalenBundle -> "Palen (fattore di fascio, base kettle)"
        | LienhardEichhornCrossflow -> "Lienhard-Eichhorn (cilindro in crossflow) + derating sul titolo"
        | ZuberQuality -> "Zuber + derating sul titolo"
        | PracticalLimit q -> sprintf "limite pratico di progetto %.0f kW/m2" (q / 1000.0)

    /// CHF locale secondo il modello scelto.
    ///   d  : diametro esterno del tubo [m]
    ///   u  : velocita' della miscela nel fascio [m/s]
    ///   x  : titolo locale
    ///   phiB : fattore di fascio di Palen gia' calcolato
    let chfLocal (model: ChfModel) (d: float) (u: float) (x: float) (xCrit: float)
                 (phiB: float) (qCritTube: float) (s: Steam.SatProps) =
        match model with
        | PalenBundle -> phiB * qCritTube
        | LienhardEichhornCrossflow ->
            chfLienhardEichhorn d u s * chfQualityDerating x xCrit
        | ZuberQuality -> chfZuber s * chfQualityDerating x xCrit
        | PracticalLimit q -> q

    /// Surriscaldamento parete corrispondente all'inizio dell'ebollizione
    /// nucleata (ONB), criterio di Davis-Anderson con raggio cavità rc [m].
    let dTonb (q: float) (rc: float) (s: Steam.SatProps) =
        // ΔT_ONB = 2 σ Tsat / (ρv hfg rc)  +  q rc / k_l
        2.0 * s.Sigma * s.Tsat / (s.RhoV * s.Hfg * rc) + q * rc / s.KL

    /// Surriscaldamento parete critico (transizione a film boiling) stimato da
    /// ΔT_crit = q_crit / h_nb(q_crit)
    let dTcrit (corr: PoolBoilingCorrelation) (qCrit: float) (d: float) (s: Steam.SatProps) (rp: float) (csf: float) =
        let h = hPool corr qCrit d s rp csf
        if h <= 0.0 then nan else qCrit / h
