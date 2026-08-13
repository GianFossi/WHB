namespace Whb.Core

open System
open Constants

/// Proprietà termofisiche di miscele di gas di processo (syngas da reforming,
/// gas di sintesi ammoniaca, fumi di combustione).
///
/// - cp da polinomi cp/R = A + B·T + C·T² + D/T² (Smith-Van Ness-Abbott)
/// - µ da correlazione di Sutherland (H2O da IAPWS gas diluito)
/// - k da Eucken modificato: k = (µ/M)(1.32·cv + 1.77·R)
/// - miscelazione: Wilke (µ) e Wassiljewa/Mason-Saxena (k)
/// - emissività dei gas triatomici: metodo CKTI/Blokh (gas non luminoso)
module GasProps =

    type Species =
        | H2 | N2 | O2 | CO | CO2 | CH4 | H2O | Ar | NH3

    /// Massa molare [kg/mol]
    let molarMass =
        function
        | H2 -> 0.00201588 | N2 -> 0.0280134 | O2 -> 0.0319988
        | CO -> 0.0280101  | CO2 -> 0.0440095 | CH4 -> 0.01604246
        | H2O -> 0.01801528 | Ar -> 0.039948 | NH3 -> 0.01703052

    /// Coefficienti cp/R = A + B·T + C·T² + D/T²
    let private cpCoef =
        function
        | H2  -> (3.249, 0.422e-3, 0.0,       0.083e5)
        | N2  -> (3.280, 0.593e-3, 0.0,       0.040e5)
        | O2  -> (3.639, 0.506e-3, 0.0,      -0.227e5)
        | CO  -> (3.376, 0.557e-3, 0.0,      -0.031e5)
        | CO2 -> (5.457, 1.045e-3, 0.0,      -1.157e5)
        | H2O -> (3.470, 1.450e-3, 0.0,       0.121e5)
        | CH4 -> (1.702, 9.081e-3, -2.164e-6, 0.0)
        | Ar  -> (2.500, 0.0,      0.0,       0.0)
        | NH3 -> (3.578, 3.020e-3, 0.0,      -0.186e5)

    /// Sutherland: (µ0 [Pa·s] a T0, T0 [K], S [K]). H2O trattato a parte.
    let private sutherland =
        function
        | H2  -> (8.411e-6, 273.15, 97.0)
        | N2  -> (1.663e-5, 273.15, 107.0)
        | O2  -> (1.919e-5, 273.15, 139.0)
        | CO  -> (1.657e-5, 273.15, 136.0)
        | CO2 -> (1.370e-5, 273.15, 222.0)
        | CH4 -> (1.024e-5, 273.15, 164.0)
        | Ar  -> (2.125e-5, 273.15, 144.0)
        | NH3 -> (0.918e-5, 273.15, 370.0)
        | H2O -> (1.120e-5, 350.0,  1064.0)

    /// cp molare [J/(mol·K)]
    let cpMolar (sp: Species) (tK: float) =
        let (a, b, c, d) = cpCoef sp
        R * (a + b * tK + c * tK * tK + d / (tK * tK))

    /// Entalpia di formazione standard a 298.15 K [J/mol]
    let hForm =
        function
        | H2 -> 0.0 | N2 -> 0.0 | O2 -> 0.0 | Ar -> 0.0
        | CO -> -110530.0 | CO2 -> -393510.0 | H2O -> -241826.0
        | CH4 -> -74850.0 | NH3 -> -45900.0

    /// Entalpia sensibile molare [J/mol] riferita a 298.15 K
    let hMolar (sp: Species) (tK: float) =
        let (a, b, c, d) = cpCoef sp
        let t0 = 298.15
        R * (a * (tK - t0)
             + b / 2.0 * (tK * tK - t0 * t0)
             + c / 3.0 * (tK ** 3.0 - t0 ** 3.0)
             - d * (1.0 / tK - 1.0 / t0))

    /// Entalpia molare assoluta (formazione + sensibile) [J/mol]
    let hMolarAbs (sp: Species) (tK: float) = hForm sp + hMolar sp tK

    /// Viscosità del componente puro [Pa·s]
    let muPure (sp: Species) (tK: float) =
        match sp with
        | H2O -> Steam.viscosity tK 0.0      // limite di gas diluito IAPWS
        | _ ->
            let (mu0, t0, s) = sutherland sp
            mu0 * Math.Pow(tK / t0, 1.5) * (t0 + s) / (tK + s)

    /// Conducibilità termica del componente puro [W/(m·K)]
    let kPure (sp: Species) (tK: float) =
        match sp with
        | H2O -> Steam.conductivity tK 0.0
        | _ ->
            let mu = muPure sp tK
            let m = molarMass sp
            let cv = cpMolar sp tK - R
            mu / m * (1.32 * cv + 1.77 * R)

    /// Composizione molare: lista (specie, frazione molare). Viene normalizzata.
    type Composition = (Species * float) list

    let normalize (c: Composition) : Composition =
        let s = c |> List.sumBy snd
        if s <= 0.0 then failwith "Composizione nulla"
        // se e' gia' normalizzata si restituisce LO STESSO oggetto: conserva
        // l'identita' di riferimento e fa scattare il memo della tabella del
        // viriale invece di ricostruire la chiave a ogni chiamata
        elif abs (s - 1.0) < 1e-12 then c
        else c |> List.map (fun (k, v) -> (k, v / s))

    /// Massa molare della miscela [kg/mol]
    let mixMolarMass (c: Composition) =
        c |> List.sumBy (fun (sp, y) -> y * molarMass sp)

    // ==================================================================
    //  GAS REALE: secondo coefficiente del viriale
    // ==================================================================
    /// A 35 bar e 600-1250 K la miscela non e' perfettamente ideale, e il
    /// contributo e' quasi tutto dell'ACQUA, che qui vale il 32.6 % molare
    /// (36.8 % in massa) ed e' l'unica specie con temperatura ridotta
    /// dell'ordine dell'unita'.
    ///
    /// Si usa il troncamento al SECONDO VIRIALE, che a queste densita'
    /// (rho_r << 1) e' piu' che sufficiente:
    ///
    ///     Z          = 1 + B_mix p / (R T)
    ///     h - h_ideale = p ( B_mix - T dB_mix/dT )
    ///     cp - cp_ideale = - p T d²B_mix/dT²
    ///     B_mix      = somma_i somma_j y_i y_j B_ij       (regola esatta)
    ///
    /// **Attenzione al peso dell'acqua**: nella regola esatta il termine di
    /// auto-interazione dell'acqua pesa y_H2O² = 0.106, non y_H2O = 0.326.
    /// Trattare ogni componente come puro alla pressione totale
    /// (Lewis-Randall) o alla pressione parziale (Amagat) triplicherebbe il
    /// termine dominante e sovrastimerebbe la correzione di circa tre volte.
    ///
    /// B_ij dalla correlazione di Pitzer-Curl/Tsonopoulos, tranne
    /// **B(H2O-H2O) che si ricava direttamente da IAPWS-IF97** come limite di
    /// bassa densita', piu' accurato di qualunque correlazione generalizzata
    /// per una molecola polare con legame a idrogeno.
    module Virial =

        /// (Tc [K], Pc [Pa], omega, Vc [m³/mol])
        let critical =
            function
            | H2  -> (33.19, 13.13e5, -0.216, 64.1e-6)
            | N2  -> (126.20, 33.98e5, 0.037, 89.2e-6)
            | O2  -> (154.58, 50.43e5, 0.022, 73.4e-6)
            | CO  -> (132.85, 34.94e5, 0.045, 93.1e-6)
            | CO2 -> (304.12, 73.74e5, 0.225, 94.07e-6)
            | CH4 -> (190.56, 45.99e5, 0.011, 98.6e-6)
            | H2O -> (647.10, 220.64e5, 0.345, 55.95e-6)
            | Ar  -> (150.86, 48.98e5, 0.000, 74.57e-6)
            | NH3 -> (405.50, 113.59e5, 0.253, 72.47e-6)

        /// Pitzer-Curl:  B Pc /(R Tc) = B0 + omega B1
        ///   B0 = 0.083 - 0.422 / Tr^1.6
        ///   B1 = 0.139 - 0.172 / Tr^4.2
        let pitzer (tc: float) (pc: float) (om: float) (tK: float) =
            let tr = max 0.30 (tK / tc)
            let b0 = 0.083 - 0.422 / Math.Pow(tr, 1.6)
            let b1 = 0.139 - 0.172 / Math.Pow(tr, 4.2)
            (b0 + om * b1) * R * tc / pc

        /// B(H2O-H2O) [m³/mol] dal limite di bassa densita' di IF97 regione 2.
        /// Sopra 1073.15 K (limite della regione 2) si prolunga con la
        /// dipendenza tipo viriale B ~ T^-1.6, dove comunque |B| e' piccolo.
        let bWater (tK: float) =
            let p = 1000.0                       // Pa: gas praticamente ideale
            let t = min tK 1073.15
            let (v, _, _, _) = Steam.region2 (p / 1.0e6) t   // v [m³/kg]
            let z = p * v / (Rw * 1000.0 * t)
            let b = (z - 1.0) * R * t / p
            if tK <= 1073.15 then b else b * Math.Pow(1073.15 / tK, 1.6)

        let private bPair (a: Species) (b: Species) (tK: float) =
            if a = b then
                if a = H2O then bWater tK
                else
                    let (tc, pc, om, _) = critical a
                    pitzer tc pc om tK
            else
                // regole di combinazione classiche (Prausnitz)
                let (tca, pca, oma, vca) = critical a
                let (tcb, pcb, omb, vcb) = critical b
                let tcij = sqrt (tca * tcb)
                let omij = 0.5 * (oma + omb)
                let zca = pca * vca / (R * tca)
                let zcb = pcb * vcb / (R * tcb)
                let zcij = 0.5 * (zca + zcb)
                let vcij = (0.5 * (Math.Cbrt vca + Math.Cbrt vcb)) ** 3.0
                let pcij = zcij * R * tcij / vcij
                pitzer tcij pcij omij tK

        /// B della miscela [m³/mol]
        let bMix (c: Composition) (tK: float) =
            let a = c |> List.toArray
            let mutable s = 0.0
            for (si, yi) in a do
                for (sj, yj) in a do
                    s <- s + yi * yj * bPair si sj tK
            s

        /// (Z, h_residua [J/mol], cp_residuo [J/(mol·K)]) a (T, p)
        let residual (c: Composition) (tK: float) (pPa: float) =
            let dt = 2.0
            let bm = bMix c tK
            let bp = bMix c (tK + dt)
            let bmn = bMix c (tK - dt)
            let db = (bp - bmn) / (2.0 * dt)
            let d2b = (bp - 2.0 * bm + bmn) / (dt * dt)
            let z = 1.0 + bm * pPa / (R * tK)
            let hRes = pPa * (bm - tK * db)
            let cpRes = -pPa * tK * d2b
            (z, hRes, cpRes)

    /// Entalpia molare residua (gas reale - gas ideale) [J/mol]
    let departure (real: bool) (c: Composition) (tK: float) (pPa: float) =
        if not real then 0.0
        else let (_, h, _) = Virial.residual c tK pPa in h

    /// Coefficienti di Wilke φij
    let private phiWilke (mi: float) (mj: float) (mui: float) (muj: float) =
        let a = 1.0 + sqrt (mui / muj) * Math.Pow(mj / mi, 0.25)
        a * a / sqrt (8.0 * (1.0 + mi / mj))

    type MixProps =
        { T: float          // K
          P: float          // Pa
          M: float          // kg/mol
          Rho: float        // kg/m³
          Cp: float         // J/(kg·K)
          Mu: float         // Pa·s
          K: float          // W/(m·K)
          Pr: float
          H: float }        // J/kg (sensibile, rif. 298.15 K)

    /// Regola di miscelazione per µ e k.
    /// Wilke/Mason-Saxena è la scelta fisicamente corretta (per miscele
    /// H2 + gas pesanti la viscosità reale supera la media molare, effetto che
    /// Wilke riproduce). `MolarAverage` è offerta solo per riprodurre i
    /// datasheet dei fornitori che usano la media molare.
    type MixingRule =
        | Wilke
        | MolarAverage

    let mixingRuleName = function
        | Wilke -> "Wilke (µ) / Wassiljewa-Mason-Saxena (k)"
        | MolarAverage -> "media molare (per confronto con datasheet)"

    /// Proprietà della miscela con regola di miscelazione selezionabile e
    /// correzione di gas reale opzionale (secondo viriale).
    /// Se `real` è vero, Z e cp sono corretti e il valore di `z` passato
    /// dal chiamante viene ignorato.
    let mixReal (rule: MixingRule) (real: bool) (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        let cn = normalize c
        let m = mixMolarMass cn
        let cpm = cn |> List.sumBy (fun (sp, y) -> y * cpMolar sp tK)
        let hm = cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)
        let mus = cn |> List.map (fun (sp, y) -> (sp, y, muPure sp tK, kPure sp tK, molarMass sp))
        let muMix, kMix =
            match rule with
            | MolarAverage ->
                // mu: media molare semplice (riproduce il datasheet entro 0.3 %)
                // k : media pesata su sqrt(M) tipo Herning-Zipperer - e' quella
                //     che i datasheet chiamano "molare"; la media semplice
                //     sovrastima del ~45 % perche' l'H2 (k altissimo, M minima)
                //     pesa quanto le specie pesanti
                let sw = mus |> List.sumBy (fun (_, y, _, _, m) -> y * sqrt m)
                (mus |> List.sumBy (fun (_, y, mu, _, _) -> y * mu),
                 (mus |> List.sumBy (fun (_, y, _, k, m) -> y * sqrt m * k)) / sw)
            | Wilke ->
                let den (mi: float) (mui: float) =
                    mus |> List.sumBy (fun (_, yj, muj, _, mj) -> yj * phiWilke mi mj mui muj)
                (mus |> List.sumBy (fun (_, yi, mui, _, mi) ->
                    let d = den mi mui in if d <= 0.0 then 0.0 else yi * mui / d),
                 mus |> List.sumBy (fun (_, yi, mui, ki, mi) ->
                    let d = den mi mui in if d <= 0.0 then 0.0 else yi * ki / d))
        let (zEff, hRes, cpRes) =
            if real then Virial.residual cn tK pPa else (z, 0.0, 0.0)
        let rho = pPa * m / (zEff * R * tK)
        let cpMass = (cpm + cpRes) / m
        { T = tK; P = pPa; M = m; Rho = rho
          Cp = cpMass; Mu = muMix; K = kMix
          Pr = cpMass * muMix / kMix
          H = (hm + hRes) / m }

    /// Versione a gas ideale (compatibilità)
    let mixWith (rule: MixingRule) (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        mixReal rule false c tK pPa z

    /// Proprietà della miscela a (T [K], p [Pa]) con fattore di comprimibilità Z.
    let mix (c: Composition) (tK: float) (pPa: float) (z: float) : MixProps =
        let cn = normalize c
        let m = mixMolarMass cn
        let cpm = cn |> List.sumBy (fun (sp, y) -> y * cpMolar sp tK)
        let hm = cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)
        let mus = cn |> List.map (fun (sp, y) -> (sp, y, muPure sp tK, kPure sp tK, molarMass sp))
        let muMix =
            mus
            |> List.sumBy (fun (_, yi, mui, _, mi) ->
                let den =
                    mus |> List.sumBy (fun (_, yj, muj, _, mj) -> yj * phiWilke mi mj mui muj)
                if den <= 0.0 then 0.0 else yi * mui / den)
        let kMix =
            mus
            |> List.sumBy (fun (_, yi, mui, ki, mi) ->
                let den =
                    mus |> List.sumBy (fun (_, yj, muj, _, mj) -> yj * phiWilke mi mj mui muj)
                if den <= 0.0 then 0.0 else yi * ki / den)
        let rho = pPa * m / (z * R * tK)
        let cpMass = cpm / m
        { T = tK; P = pPa; M = m; Rho = rho
          Cp = cpMass; Mu = muMix; K = kMix
          Pr = cpMass * muMix / kMix
          H = hm / m }

    /// Entalpia massica sensibile [J/kg] della miscela a T [K]
    let enthalpy (c: Composition) (tK: float) =
        let cn = normalize c
        (cn |> List.sumBy (fun (sp, y) -> y * hMolar sp tK)) / mixMolarMass cn

    /// Temperatura [K] corrispondente a un'entalpia massica assegnata [J/kg]
    let temperatureFromEnthalpy (c: Composition) (h: float) =
        bisect (fun t -> enthalpy c t - h) 250.0 2500.0 1e-4 200

    /// Entalpia massica ASSOLUTA [J/kg] (include le entalpie di formazione):
    /// necessaria quando la composizione varia per reazione lungo il tubo.
    let enthalpyAbs (c: Composition) (tK: float) =
        let cn = normalize c
        (cn |> List.sumBy (fun (sp, y) -> y * hMolarAbs sp tK)) / mixMolarMass cn

    /// Entalpia massica ASSOLUTA con correzione di gas reale [J/kg]
    let enthalpyAbsReal (real: bool) (c: Composition) (tK: float) (pPa: float) =
        let cn = normalize c
        ((cn |> List.sumBy (fun (sp, y) -> y * hMolarAbs sp tK)) + departure real cn tK pPa)
        / mixMolarMass cn

    /// Frazione molare di una specie
    let molFrac (c: Composition) (sp: Species) =
        c |> List.tryFind (fun (s, _) -> s = sp) |> Option.map snd |> Option.defaultValue 0.0

    // ------------------------------------------------------------------
    // Emissività del gas non luminoso (CO2 + H2O) - metodo CKTI / Blokh
    // ------------------------------------------------------------------
    /// Emissività del gas.
    ///   rH2O, rCO2 : frazioni molari
    ///   pPa        : pressione totale [Pa]
    ///   sBeam      : lunghezza media del raggio [m] (0.9·di per un tubo)
    ///   tK         : temperatura del gas [K]
    let gasEmissivity (rH2O: float) (rCO2: float) (pPa: float) (sBeam: float) (tK: float) =
        let rn = rH2O + rCO2
        if rn <= 1e-6 || sBeam <= 0.0 then 0.0
        else
            let pnMPa = pPa * rn / 1.0e6
            let ps = max 1e-6 (pnMPa * sBeam)
            let kg =
                ((0.78 + 1.6 * rH2O) / sqrt ps - 0.1) * (1.0 - 0.37 * tK / 1000.0)
            let kg = max 0.0 kg
            let e = 1.0 - exp (-kg * ps)
            min 0.95 (max 0.0 e)

    /// Coefficiente di scambio radiativo equivalente [W/(m²·K)] riferito
    /// alla superficie interna del tubo.
    let hRadiation (epsGas: float) (epsWall: float) (tGasK: float) (tWallK: float) =
        if abs (tGasK - tWallK) < 1e-6 then 0.0
        else
            let effWall = 0.5 * (epsWall + 1.0)      // parete grigia in cavità
            let e = epsGas * effWall
            e * sigmaSB * (tGasK ** 4.0 - tWallK ** 4.0) / (tGasK - tWallK)
