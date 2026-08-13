namespace Whb.Core

open System
open Constants

/// Proprietà termodinamiche e di trasporto dell'acqua/vapore.
/// Formulazione IAPWS-IF97 (regioni 1, 2, 4) + IAPWS 2008 (viscosità),
/// IAPWS 2011 (conducibilità termica), IAPWS 1994 (tensione superficiale).
///
/// Unità interne IF97: p in MPa, T in K, v in m³/kg, h e s in kJ/kg(·K).
/// Le funzioni pubbliche `sat*` usano unità SI (Pa, K, J/kg, kg/m³, W/m/K, Pa·s).
module Steam =

    // ------------------------------------------------------------------
    // Regione 4 : linea di saturazione
    // ------------------------------------------------------------------
    let private n4 =
        [| 0.11670521452767e4; -0.72421316703206e6; -0.17073846940092e2;
           0.12020824702470e5; -0.32325550322333e7;  0.14915108613530e2;
           -0.48232657361591e4;  0.40511340542057e6; -0.23855557567849;
           0.65017534844798e3 |]

    /// Pressione di saturazione [MPa] da T [K]
    let psat_MPa (tK: float) =
        let th = tK + n4.[8] / (tK - n4.[9])
        let a = th * th + n4.[0] * th + n4.[1]
        let b = n4.[2] * th * th + n4.[3] * th + n4.[4]
        let c = n4.[5] * th * th + n4.[6] * th + n4.[7]
        let x = 2.0 * c / (-b + sqrt (b * b - 4.0 * a * c))
        x ** 4.0

    /// Temperatura di saturazione [K] da p [MPa]
    let tsat_K (pMPa: float) =
        let beta = pMPa ** 0.25
        let e = beta * beta + n4.[2] * beta + n4.[5]
        let f = n4.[0] * beta * beta + n4.[3] * beta + n4.[6]
        let gg = n4.[1] * beta * beta + n4.[4] * beta + n4.[7]
        let d = 2.0 * gg / (-f - sqrt (f * f - 4.0 * e * gg))
        0.5 * (n4.[9] + d - sqrt ((n4.[9] + d) * (n4.[9] + d) - 4.0 * (n4.[8] + n4.[9] * d)))

    // ------------------------------------------------------------------
    // Regione 1 : liquido compresso / liquido saturo  (T <= 623.15 K)
    // ------------------------------------------------------------------
    // (I, J, n)
    let private r1 =
        [| (0, -2, 0.14632971213167);   (0, -1, -0.84548187169114)
           (0,  0, -0.37563603672040e1);(0,  1,  0.33855169168385e1)
           (0,  2, -0.95791963387872);  (0,  3,  0.15772038513228)
           (0,  4, -0.16616417199501e-1);(0, 5,  0.81214629983568e-3)
           (1, -9,  0.28319080123804e-3);(1,-7, -0.60706301565874e-3)
           (1, -1, -0.18990068218419e-1);(1, 0, -0.32529748770505e-1)
           (1,  1, -0.21841717175414e-1);(1, 3, -0.52838357969930e-4)
           (2, -3, -0.47184321073267e-3);(2, 0, -0.30001780793026e-3)
           (2,  1,  0.47661393906987e-4);(2, 3, -0.44141845330846e-5)
           (2, 17, -0.72694996297594e-15)
           (3, -4, -0.31679644845054e-4);(3, 0, -0.28270797985312e-5)
           (3,  6, -0.85205128120103e-9)
           (4, -5, -0.22425281908000e-5);(4,-2, -0.65171222895601e-6)
           (4, 10, -0.14341729937924e-12)
           (5, -8, -0.40516996860117e-6)
           (8, -11,-0.12734301741641e-8);(8,-6, -0.17424871230634e-9)
           (21,-29,-0.68762131295531e-18)
           (23,-31, 0.14478307828521e-19)
           (29,-38, 0.26335781662795e-22)
           (30,-39,-0.11947622640071e-22)
           (31,-40, 0.18228094581404e-23)
           (32,-41,-0.93537087292458e-25) |]

    let private reg1 (pMPa: float) (tK: float) =
        let pi = pMPa / 16.53
        let tau = 1386.0 / tK
        let a = 7.1 - pi
        let b = tau - 1.222
        let mutable gam = 0.0
        let mutable gpi = 0.0
        let mutable gtau = 0.0
        let mutable gtt = 0.0
        for (i, j, n) in r1 do
            let fi = float i
            let fj = float j
            let ai = Math.Pow(a, fi)
            let bj = Math.Pow(b, fj)
            gam <- gam + n * ai * bj
            gpi <- gpi - n * fi * Math.Pow(a, fi - 1.0) * bj
            gtau <- gtau + n * ai * fj * Math.Pow(b, fj - 1.0)
            gtt <- gtt + n * ai * fj * (fj - 1.0) * Math.Pow(b, fj - 2.0)
        // v [m³/kg], h [kJ/kg], cp [kJ/kg/K], s [kJ/kg/K]
        let v = pi * gpi * Rw * tK / (pMPa * 1000.0)
        let h = tau * gtau * Rw * tK
        let cp = -tau * tau * gtt * Rw
        let s = (tau * gtau - gam) * Rw
        (v, h, cp, s)

    // ------------------------------------------------------------------
    // Regione 2 : vapore surriscaldato / vapore saturo
    // ------------------------------------------------------------------
    let private r2j0 = [| 0; 1; -5; -4; -3; -2; -1; 2; 3 |]

    let private r2n0 =
        [| -0.96927686500217e1;  0.10086655968018e2; -0.56087911283020e-2;
           0.71452738081455e-1; -0.40710498223928;    0.14240819171444e1;
           -0.43839511319450e1; -0.28408632460772;    0.21268463753307e-1 |]

    let private r2r =
        [| (1, 0,  -0.17731742473213e-2); (1, 1, -0.17834862292358e-1)
           (1, 2,  -0.45996013696365e-1); (1, 3, -0.57581259083432e-1)
           (1, 6,  -0.50325278727930e-1); (2, 1, -0.33032641670203e-4)
           (2, 2,  -0.18948987516315e-3); (2, 4, -0.39392777243355e-2)
           (2, 7,  -0.43797295650573e-1); (2,36, -0.26674547914087e-4)
           (3, 0,   0.20481737692309e-7); (3, 1,  0.43870667284435e-6)
           (3, 3,  -0.32277677238570e-4); (3, 6, -0.15033924542148e-2)
           (3,35,  -0.40668253562649e-1); (4, 1, -0.78847309559367e-9)
           (4, 2,   0.12790717852285e-7); (4, 3,  0.48225372718507e-6)
           (5, 7,   0.22922076337661e-5); (6, 3, -0.16714766451061e-10)
           (6,16,  -0.21171472321355e-2); (6,35, -0.23895741934104e2)
           (7, 0,  -0.59059564324270e-17);(7,11, -0.12621808899101e-5)
           (7,25,  -0.38946842435739e-1); (8, 8,  0.11256211360459e-10)
           (8,36,  -0.82311340897998e1);  (9,13,  0.19809712802088e-7)
           (10,4,   0.10406965210174e-18);(10,10,-0.10234747095929e-12)
           (10,14, -0.10018179379511e-8); (16,29,-0.80882908646985e-10)
           (16,50,  0.10693031879409);    (18,57,-0.33662250574171)
           (20,20,  0.89185845355421e-24);(20,35, 0.30629316876232e-12)
           (20,48, -0.42002467698208e-5); (21,21,-0.59056029685639e-25)
           (22,53,  0.37826947613457e-5); (23,39,-0.12768608934681e-14)
           (24,26,  0.73087610595061e-28);(24,40, 0.55414715350778e-16)
           (24,58, -0.94369707241210e-6) |]

    let private reg2 (pMPa: float) (tK: float) =
        let pi = pMPa
        let tau = 540.0 / tK
        // parte ideale
        let mutable g0 = log pi
        let mutable g0t = 0.0
        let mutable g0tt = 0.0
        for k in 0 .. 8 do
            let j = float r2j0.[k]
            let n = r2n0.[k]
            g0 <- g0 + n * Math.Pow(tau, j)
            g0t <- g0t + n * j * Math.Pow(tau, j - 1.0)
            g0tt <- g0tt + n * j * (j - 1.0) * Math.Pow(tau, j - 2.0)
        let g0pi = 1.0 / pi
        // parte residua
        let b = tau - 0.5
        let mutable gr = 0.0
        let mutable grpi = 0.0
        let mutable grt = 0.0
        let mutable grtt = 0.0
        for (i, j, n) in r2r do
            let fi = float i
            let fj = float j
            let pii = Math.Pow(pi, fi)
            let bj = Math.Pow(b, fj)
            gr <- gr + n * pii * bj
            grpi <- grpi + n * fi * Math.Pow(pi, fi - 1.0) * bj
            grt <- grt + n * pii * fj * Math.Pow(b, fj - 1.0)
            grtt <- grtt + n * pii * fj * (fj - 1.0) * Math.Pow(b, fj - 2.0)
        let v = pi * (g0pi + grpi) * Rw * tK / (pMPa * 1000.0)
        let h = tau * (g0t + grt) * Rw * tK
        let cp = -tau * tau * (g0tt + grtt) * Rw
        let s = (tau * (g0t + grt) - (g0 + gr)) * Rw
        (v, h, cp, s)

    // ------------------------------------------------------------------
    // Viscosità dinamica IAPWS 2008 (formulazione industriale, senza
    // termine critico mu2 -> irrilevante fuori dall'intorno critico)
    // ------------------------------------------------------------------
    let private hVisc0 = [| 1.67752; 2.20462; 0.6366564; -0.241605 |]

    let private hVisc1 =
        // (i, j, Hij)
        [| (0,0, 5.20094e-1); (1,0, 8.50895e-2); (2,0,-1.08374);    (3,0,-2.89555e-1)
           (0,1, 2.22531e-1); (1,1, 9.99115e-1); (2,1, 1.88797);    (3,1, 1.26613)
           (5,1, 1.20573e-1)
           (0,2,-2.81378e-1); (1,2,-9.06851e-1); (2,2,-7.72479e-1); (3,2,-4.89837e-1)
           (4,2,-2.57040e-1)
           (0,3, 1.61913e-1); (1,3, 2.57399e-1)
           (0,4,-3.25372e-2); (3,4, 6.98452e-2)
           (4,5, 8.72102e-3)
           (3,6,-4.35673e-3); (5,6,-5.93264e-4) |]

    /// Viscosità dinamica [Pa·s] da T [K] e densità [kg/m³]
    let viscosity (tK: float) (rho: float) =
        let tb = tK / Tc_water
        let rb = rho / Rhoc_water
        let mutable den = 0.0
        for i in 0 .. 3 do
            den <- den + hVisc0.[i] / Math.Pow(tb, float i)
        let mu0 = 100.0 * sqrt tb / den                       // µPa·s
        let mutable s = 0.0
        for (i, j, h) in hVisc1 do
            s <- s + Math.Pow(1.0 / tb - 1.0, float i) * h * Math.Pow(rb - 1.0, float j)
        let mu1 = exp (rb * s)
        mu0 * mu1 * 1e-6

    // ------------------------------------------------------------------
    // Conducibilità termica IAPWS 2011 (formulazione industriale)
    // ------------------------------------------------------------------
    let private lam0 = [| 2.443221e-3; 1.323095e-2; 6.770357e-3; -3.454586e-3; 4.096266e-4 |]

    let private lam1 =
        array2D
            [ [  1.60397357; -0.646013523;  0.111443906;  0.102997357; -0.0504123634;  0.00609859258 ]
              [  2.33771842; -2.78843778;   1.53616167;  -0.463045512;  0.0832827019; -0.00719201245 ]
              [  2.19650529; -4.54580785;   3.55777244;  -1.40944978;   0.275418278;  -0.0205938816  ]
              [ -1.21051378;  1.60812989;  -0.621178141;  0.0716373224; 0.0;           0.0           ]
              [ -2.7203370;   4.57586331;  -3.18369245;   1.1168348;   -0.19268305;    0.012913842   ] ]

    /// Conducibilità termica [W/(m·K)] da T [K] e densità [kg/m³]
    let conductivity (tK: float) (rho: float) =
        let tb = tK / Tc_water
        let rb = rho / Rhoc_water
        let mutable den = 0.0
        for k in 0 .. 4 do
            den <- den + lam0.[k] / Math.Pow(tb, float k)
        let l0 = sqrt tb / den
        let mutable s = 0.0
        for i in 0 .. 4 do
            let mutable inner = 0.0
            for j in 0 .. 5 do
                inner <- inner + lam1.[i, j] * Math.Pow(rb - 1.0, float j)
            s <- s + Math.Pow(1.0 / tb - 1.0, float i) * inner
        let l1 = exp (rb * s)
        l0 * l1 * 1e-3

    /// Tensione superficiale [N/m] da T [K]  (IAPWS 1994)
    let surfaceTension (tK: float) =
        let tau = 1.0 - tK / Tc_water
        if tau <= 0.0 then 0.0
        else 235.8e-3 * Math.Pow(tau, 1.256) * (1.0 - 0.625 * tau)

    // ------------------------------------------------------------------
    // Proprietà di saturazione (unità SI)
    // ------------------------------------------------------------------
    type SatProps =
        { P: float          // Pa
          Tsat: float       // K
          RhoL: float       // kg/m³
          RhoV: float       // kg/m³
          HL: float         // J/kg
          HV: float         // J/kg
          Hfg: float        // J/kg
          CpL: float        // J/kg/K
          CpV: float        // J/kg/K
          MuL: float        // Pa·s
          MuV: float        // Pa·s
          KL: float         // W/m/K
          KV: float         // W/m/K
          Sigma: float      // N/m
          PrL: float
          PrV: float }

    /// Proprietà di saturazione a pressione assegnata [Pa].
    let sat (pPa: float) : SatProps =
        let pMPa = pPa / 1.0e6
        let tK = tsat_K pMPa
        let (vl, hl, cpl, _) = reg1 pMPa tK
        let (vv, hv, cpv, _) = reg2 pMPa tK
        let rhol = 1.0 / vl
        let rhov = 1.0 / vv
        let mul = viscosity tK rhol
        let muv = viscosity tK rhov
        let kl = conductivity tK rhol
        let kv = conductivity tK rhov
        { P = pPa
          Tsat = tK
          RhoL = rhol
          RhoV = rhov
          HL = hl * 1000.0
          HV = hv * 1000.0
          Hfg = (hv - hl) * 1000.0
          CpL = cpl * 1000.0
          CpV = cpv * 1000.0
          MuL = mul
          MuV = muv
          KL = kl
          KV = kv
          Sigma = surfaceTension tK
          PrL = cpl * 1000.0 * mul / kl
          PrV = cpv * 1000.0 * muv / kv }

    /// Accesso diretto alle regioni (per verifica sui punti di riferimento IAPWS).
    /// Restituisce (v [m³/kg], h [kJ/kg], cp [kJ/kg/K], s [kJ/kg/K]).
    let region1 (pMPa: float) (tK: float) = reg1 pMPa tK
    let region2 (pMPa: float) (tK: float) = reg2 pMPa tK

    /// Entalpia liquido sottoraffreddato [J/kg] a (p [Pa], T [K])
    let hLiquid (pPa: float) (tK: float) =
        let (_, h, _, _) = reg1 (pPa / 1.0e6) tK
        h * 1000.0

    /// Densità liquido sottoraffreddato [kg/m³] a (p [Pa], T [K])
    let rhoLiquid (pPa: float) (tK: float) =
        let (v, _, _, _) = reg1 (pPa / 1.0e6) tK
        1.0 / v
