namespace Whb.Core

open System
open Constants

/// <summary>
/// Provides water-side boiling, convection, wall-temperature, and hydraulic correlations for WHB thermal calculations.
/// </summary>
/// <remarks>
/// Uses empirical boiling, convection, pressure-drop, and wall-temperature correlations for water-side thermal calculations. Treat results as engineering estimates and verify pressure, quality, mass flux, and SI-unit assumptions against the applicable design code or vendor method.
/// </remarks>
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
    let hMostinski (q: float) (p: float) (pc: float) =
        let pr = p / pc
        let pcKPa = pc / 1000.0
        let fp = 1.8 * Math.Pow(pr, 0.17) + 4.0 * Math.Pow(pr, 1.2) + 10.0 * Math.Pow(pr, 10.0)
        0.00417 * Math.Pow(pcKPa, 0.69) * Math.Pow(max q 1.0, 0.7) * fp
    let hCooper (q: float) (p: float) (pc: float) (rp: float) (mMol: float) =
        let pr = max 1e-4 (p / pc)
        55.0
        * Math.Pow(pr, 0.12 - 0.2 * log10 rp)
        * Math.Pow(-log10 pr, -0.55)
        * Math.Pow(mMol, -0.5)
        * Math.Pow(max q 1.0, 0.67)
    let hRohsenow (dTe: float) (s: Steam.SatProps) (csf: float) (sExp: float) =
        if dTe <= 0.0 then 0.0
        else
            let a = s.MuL * s.Hfg * sqrt (g * (s.RhoL - s.RhoV) / s.Sigma)
            let b = s.CpL * dTe / (csf * s.Hfg * Math.Pow(s.PrL, sExp))
            let q = a * Math.Pow(b, 3.0)
            q / dTe
    let hGorenflo (q: float) (p: float) (pc: float) (rp: float) =
        let pr = max 1e-4 (min 0.95 (p / pc))
        let h0 = 5600.0
        let q0 = 20000.0
        let rp0 = 0.4
        let fp = 1.73 * Math.Pow(pr, 0.27) + (6.1 + 0.68 / (1.0 - pr)) * pr * pr
        let n = 0.9 - 0.3 * Math.Pow(pr, 0.15)
        h0 * fp * Math.Pow(max q 1.0 / q0, n) * Math.Pow(rp / rp0, 0.133)
    let hCornwellHouston (q: float) (d: float) (p: float) (pc: float) (s: Steam.SatProps) =
        let pr = p / pc
        let pcBar = pc / 1.0e5
        let fp = 1.8 * Math.Pow(pr, 0.17) + 4.0 * Math.Pow(pr, 1.2) + 10.0 * Math.Pow(pr, 10.0)
        let reb = max 1.0 (q * d / (s.MuL * s.Hfg))
        let nu = 9.7 * Math.Pow(pcBar, 0.5) * fp * Math.Pow(reb, 0.67) * Math.Pow(s.PrL, 0.4)
        nu * s.KL / d
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
            let f dTe =
                let a = s.MuL * s.Hfg * sqrt (g * (s.RhoL - s.RhoV) / s.Sigma)
                let b = s.CpL * dTe / (csf * s.Hfg * Math.Pow(s.PrL, 1.0))
                a * Math.Pow(b, 3.0) - q
            let dTe = bisect f 0.01 200.0 1e-6 200
            if dTe <= 0.0 then 0.0 else q / dTe
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
    let bundleFactor (fb: float) = max 1.0 fb
    let chenF (x: float) (s: Steam.SatProps) =
        let x = min 0.99 (max 1e-4 x)
        let xtt =
            Math.Pow((1.0 - x) / x, 0.9)
            * Math.Pow(s.RhoV / s.RhoL, 0.5)
            * Math.Pow(s.MuL / s.MuV, 0.1)
        let inv = 1.0 / xtt
        if inv <= 0.1 then 1.0 else 2.35 * Math.Pow(inv + 0.213, 0.736)
    let chenS (reL: float) (f: float) =
        let retp = reL * Math.Pow(f, 1.25)
        1.0 / (1.0 + 2.53e-6 * Math.Pow(retp, 1.17))
    let hForsterZuber (dTsat: float) (dPsat: float) (s: Steam.SatProps) =
        if dTsat <= 0.0 then 0.0
        else
            0.00122
            * (Math.Pow(s.KL, 0.79) * Math.Pow(s.CpL, 0.45) * Math.Pow(s.RhoL, 0.49))
            / (Math.Pow(s.Sigma, 0.5) * Math.Pow(s.MuL, 0.29) * Math.Pow(s.Hfg, 0.24)
               * Math.Pow(s.RhoV, 0.24))
            * Math.Pow(dTsat, 0.24)
            * Math.Pow(max dPsat 0.0, 0.75)
    let boilingNumber (q: float) (gMass: float) (hfg: float) =
        max 0.0 q / (max 1.0 gMass * max 1.0 hfg)
    let convectionNumber (x: float) (s: Steam.SatProps) =
        let xc = min 0.95 (max 1e-4 x)
        Math.Pow((1.0 - xc) / xc, 0.8) * Math.Pow(s.RhoV / s.RhoL, 0.5)
    let froudeLO (gMass: float) (d: float) (s: Steam.SatProps) =
        gMass * gMass / (s.RhoL * s.RhoL * g * max 1e-6 d)
    let kandlikarF2 (frLO: float) (horizontal: bool) =
        if horizontal && frLO < 0.04 then Math.Pow(25.0 * frLO, 0.3) else 1.0
    type FlowBoilingRegime =
        | NucleateBoilingDominant
        | ConvectiveBoilingDominant
    let flowBoilingRegimeName =
        function
        | NucleateBoilingDominant -> "NBD - ebollizione nucleata dominante"
        | ConvectiveBoilingDominant -> "CBD - ebollizione convettiva dominante"
    let regimeByCo (co: float) =
        if co > 0.65 then NucleateBoilingDominant else ConvectiveBoilingDominant
    type KandlikarResult =
        { HNbd: float
          HCbd: float
          HTp: float
          Bo: float
          Co: float
          FrLO: float
          F2: float
          Regime: FlowBoilingRegime }
    let hKandlikar
        (hLO: float) (q: float) (gMass: float) (x: float) (d: float)
        (horizontal: bool) (fFl: float) (s: Steam.SatProps) : KandlikarResult =
        let xc = min 0.95 (max 1e-4 x)
        let bo = boilingNumber q gMass s.Hfg
        let co = convectionNumber xc s
        let fr = froudeLO gMass d s
        let f2 = kandlikarF2 fr horizontal
        let oneMinusX = Math.Pow(1.0 - xc, 0.8)
        let boPow = Math.Pow(bo, 0.7)
        let hNbd = (0.6683 * Math.Pow(co, -0.2) * f2 + 1058.0 * boPow * fFl) * oneMinusX * hLO
        let hCbd = (1.136 * Math.Pow(co, -0.9) * f2 + 667.2 * boPow * fFl) * oneMinusX * hLO
        { HNbd = hNbd
          HCbd = hCbd
          HTp = max hNbd hCbd
          Bo = bo
          Co = co
          FrLO = fr
          F2 = f2
          Regime = if hNbd >= hCbd then NucleateBoilingDominant else ConvectiveBoilingDominant }
    type FlowBoilingModel =
        | ChenSuperposition
        | KandlikarMax
    let flowBoilingModelName =
        function
        | ChenSuperposition -> "Chen (superposizione con soppressione)"
        | KandlikarMax -> "Kandlikar (1990) - max(h_NBD, h_CBD)"
    let dnbAllowableFraction (requiredMinDNBR: float) =
        1.0 / max 1e-9 requiredMinDNBR
    let dnbrRequired (requiredMinDNBR: float) (_firstRow: bool) (_inFerrule: bool) =
        max 1e-9 requiredMinDNBR
    let hZukauskas (reMax: float) (pr: float) (prW: float) (k: float) (d: float) (staggered: bool) (stOverSl: float) =
        let c, m =
            if staggered then
                (if reMax < 2.0e5 then (0.35 * Math.Pow(stOverSl, 0.2), 0.60)
                 else (0.031 * Math.Pow(stOverSl, 0.2), 0.80))
            else
                (if reMax < 2.0e5 then (0.27, 0.63) else (0.021, 0.84))
        let nu = c * Math.Pow(reMax, m) * Math.Pow(pr, 0.36) * Math.Pow(pr / prW, 0.25)
        nu * k / d
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
    let chfZuber (s: Steam.SatProps) =
        0.131 * s.Hfg * Math.Pow(s.RhoV, 0.5)
        * Math.Pow(s.Sigma * g * (s.RhoL - s.RhoV), 0.25)
    let chfHorizontalTube (d: float) (s: Steam.SatProps) =
        let lc = sqrt (s.Sigma / (g * (s.RhoL - s.RhoV)))
        let rp = d / 2.0 / lc
        let corr =
            if rp > 1.2 then 0.89 + 2.27 * exp (-3.44 * sqrt rp)
            elif rp > 0.15 then 1.05 * Math.Pow(rp, -0.25)
            else 1.4 * Math.Pow(rp, -0.25)
        chfZuber s * corr
    let chfMostinski (p: float) (pc: float) =
        let pr = p / pc
        let pcKPa = pc / 1000.0
        0.368 * pcKPa * Math.Pow(pr, 0.35) * Math.Pow(1.0 - pr, 0.9) * 1000.0
    let palenPhiB (dBundle: float) (lTube: float) (areaOut: float) =
        if areaOut <= 0.0 then 1.0
        else
            let psi = dBundle * lTube / areaOut
            max 0.1 (min 1.0 (3.1 * psi))
    let chfBundle (dBundle: float) (lTube: float) (areaOut: float) (qCritTube: float) =
        palenPhiB dBundle lTube areaOut * qCritTube
    let chfLienhardEichhorn (d: float) (u: float) (s: Steam.SatProps) =
        let uu = max 0.01 u
        let we = s.RhoV * uu * uu * d / s.Sigma
        let baseq = s.RhoV * s.Hfg * uu
        let low = baseq / Math.PI * (1.0 + Math.Pow(4.0 / max 1e-9 we, 1.0 / 3.0))
        let high = baseq * (1.0 / (169.0 * Math.PI) + Math.Pow(we, -1.0 / 3.0) / 19.2)
        max low high
    let chfQualityDerating (x: float) (xCrit: float) =
        max 0.1 (1.0 - (max 0.0 x) / (max 1e-3 xCrit))
    type ChfModel =
        | PalenBundle
        | LienhardEichhornCrossflow
        | ZuberQuality
        | PracticalLimit of float
    let chfModelName =
        function
        | PalenBundle -> "Palen (fattore di fascio, base kettle)"
        | LienhardEichhornCrossflow -> "Lienhard-Eichhorn (cilindro in crossflow) + derating sul titolo"
        | ZuberQuality -> "Zuber + derating sul titolo"
        | PracticalLimit q -> sprintf "limite pratico di progetto %.0f kW/m2" (q / 1000.0)
    let chfLocal (model: ChfModel) (d: float) (u: float) (x: float) (xCrit: float)
                 (phiB: float) (qCritTube: float) (s: Steam.SatProps) =
        match model with
        | PalenBundle -> phiB * qCritTube
        | LienhardEichhornCrossflow ->
            chfLienhardEichhorn d u s * chfQualityDerating x xCrit
        | ZuberQuality -> chfZuber s * chfQualityDerating x xCrit
        | PracticalLimit q -> q
    let dTonb (q: float) (rc: float) (s: Steam.SatProps) =
        2.0 * s.Sigma * s.Tsat / (s.RhoV * s.Hfg * rc) + q * rc / s.KL
    let dTcrit (corr: PoolBoilingCorrelation) (qCrit: float) (d: float) (s: Steam.SatProps) (rp: float) (csf: float) =
        let h = hPool corr qCrit d s rp csf
        if h <= 0.0 then nan else qCrit / h




