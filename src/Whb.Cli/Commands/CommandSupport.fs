module Whb.Cli.CommandSupport

open System
open System.IO
open System.Text.Json
open System.Globalization
open Whb.Core
open Whb.Core.Constants
open Whb.Core.Options
open Whb.Cli

let tryParseFloat (text: string) =
    match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, value -> Some value
    | _ -> None

let floatGrid (a: float) (b: float) (step: float) =
    let dx = abs step
    if dx <= 0.0 then invalidArg "step" "Step must be positive."
    let lo = min a b
    let hi = max a b
    [ let mutable x = lo
      while x <= hi + 1e-9 * dx do
          yield x
          x <- x + dx ]

let filteredArgs (rest: string list) =
    let rec loop acc remaining =
        match remaining with
        | ("--out" | "--options") :: _ :: tail -> loop acc tail
        | head :: tail -> loop (head :: acc) tail
        | [] -> List.rev acc
    loop [] rest

let resolveCaseArg (rest: string list) =
    match filteredArgs rest with
    | f :: _ when File.Exists f -> Some f, CaseLoader.loadCase f
    | f :: _ when not (f.StartsWith("--")) ->
        eprintfn "Case file not found: %s" f
        raise (FileNotFoundException("Case file not found", f))
    | _ -> None, Defaults.referenceCase

let template = """{
  "nome": "WHB reformer secondario - caso base",
  "materiale": "T11",
  "materiale_mantello": "SA-516",
  "campata_non_supportata_m": 1.20,
  "reticolo": "60",
  "smorzamento_log": 0.03,
  "t_montaggio_C": 20.0,
  "sezioni_assiali": 90,
  "bande_verticali": 12,
  "infittimento_imbocco": 10.0,
  "ricircolo_interno": false,

  "bypass": {
    "presente": true,
    "frazione": -1,
    "t_uscita_target_C": 355.0,
    "liner_id_mm": 275.0,
    "liner_od_mm": 281.0,
    "liner_materiale": "601",
    "isolante_od_mm": 284.0,
    "isolante": "saffil",
    "tubo_od_mm": 300.0,
    "tubo_materiale": "SA-192",
    "fouling_m2KW": 0.00050,
    "k_localizzati": 1.5,
    "valvola_a_valle": true,
    "valvola_apertura_gradi": -1,
    "valvola_apertura_min_gradi": 15.0,
    "valvola_apertura_max_gradi": 70.0,
    "t_miscelata_min_C": 350.0,
    "t_miscelata_max_C": 360.0,
    "v_lavaggio_min_ms": 1.5,
    "rhov2_max_valvola": 40000.0
  },

  "bypass_frazione_aperta": 0.10,

  "tubi": {
    "numero": 848,
    "di_mm": 32.0,
    "do_mm": 38.1,
    "lunghezza_m": 12.998,
    "passo_mm": 50.8,
    "sfalsato": true,
    "mantello_id_mm": 2025.0,
    "otl_mm": 1711.11,
    "itl_mm": 571.0,
    "diaframma_od_mm": 2015.0,
    "mantello_wt_mm": 58.0,
    "rugosita_mm": 0.045
  },

  "ferrula": {
    "presente": true,
    "lunghezza_mm": 200.0,
    "lunghezze": [ { "frazione": 1.0, "lunghezza_mm": 200.0 } ],
    "bore_mm": 26.7,
    "manicotto_od_mm": 30.0,
    "manicotto_materiale": "800",
    "isolante": "saffil"
  },

  "gas": {
    "composizione": { "H2": 0.3707, "N2": 0.1577, "CO": 0.0863, "CO2": 0.0546, "CH4": 0.0027, "AR": 0.0020, "H2O": 0.3260 },
    "portata_kgs": 85.42,
    "maggiorazione": 1.0,
    "t_ingresso_C": 967.5,
    "p_ingresso_bara": 34.74,
    "z": 1.0,
    "modello_gas": "realistico",
    "modello_claus": "frozen",
    "claus_cinetica": {
      "fattore_severita": 0.15,
      "fattore_tau": 0.35,
      "sottopassi": 8,
      "claus_a_1s": 500000.0,
      "claus_ea_kjmol": 60.0,
      "cos_a_1s": 200000.0,
      "cos_ea_kjmol": 70.0,
      "cs2_a_1s": 300000.0,
      "cs2_ea_kjmol": 90.0
    },
    "fouling_m2KW": 0.00050,
    "emissivita_parete": 0.85,
    "irraggiamento": true,
    "coeff_imbocco": 1.4,
    "correlazione": "gnielinski",
    "miscelazione": "wilke",
    "gas_reale": true,
    "shift": "congelata",
    "shift_t_freeze_C": 700.0,
    "shift_frazione": 0.3
  },

  "vapore": {
    "pressione_bara": 117.84,
    "dnbr_min": 2.0,
    "fouling_m2KW": 0.00015,
    "rugosita_um": 1.0,
    "fattore_fascio": 1.5,
    "correlazione": "mostinski",
    "ebollizione_flusso": "chen",
    "modello_chf": "palen",
    "csf": 0.013,
    "t_alimento_C": 250.0
  },

  "condensatore_zolfo": {
    "presente": false,
    "usa_uscita_whb": true,
    "sezioni": 24,
    "tempo_residenza_s": 1.0,
    "dp_mbar": 20.0,
    "t_uscita_target_C": 145.0,
    "t_parete_C": 140.0,
    "t_refrigerante_C": 135.0,
    "u_assunto_Wm2K": 60.0,
    "gas_ingresso": {
      "composizione": { "N2": 0.70, "H2O": 0.10, "H2S": 0.12, "SO2": 0.04, "S2": 0.04 },
      "portata_kgs": 10.0,
      "maggiorazione": 1.0,
      "t_ingresso_C": 220.0,
      "p_ingresso_bara": 1.7,
      "z": 1.0,
      "modello_gas": "realistico",
      "modello_claus": "kinetic",
      "claus_cinetica": {
        "fattore_severita": 0.15,
        "fattore_tau": 0.35,
        "sottopassi": 8,
        "claus_a_1s": 500000.0,
        "claus_ea_kjmol": 60.0,
        "cos_a_1s": 200000.0,
        "cos_ea_kjmol": 70.0,
        "cs2_a_1s": 300000.0,
        "cs2_ea_kjmol": 90.0
      },
      "miscelazione": "wilke",
      "gas_reale": true,
      "shift": "congelata",
      "shift_t_freeze_C": 700.0,
      "shift_frazione": 0.3
    }
  },

  "circuito": {
    "dz_drum_whb_m": 6.0,
    "offset_livello_m": 0.0,
    "interne_drum_mbar": 50.0,
    "modello_vuoto": "zuber",
    "modello_attrito": "friedel",

    "riser": [
      { "tag": "R1", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 0.90, "angolo_gradi": 0 },
      { "tag": "R2", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 2.60, "angolo_gradi": 0 },
      { "tag": "R3", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 5.50, "angolo_gradi": 0 },
      { "tag": "R4", "nps": "24\" Sch.120", "id_mm": 518.0, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 9.30, "angolo_gradi": 0 },
      { "tag": "R5", "nps": "6\" Sch.120",  "id_mm": 139.7, "n": 1, "diritti_mm": [2700], "curve": [], "z_m": 12.70, "angolo_gradi": 0, "nota": "estremita' fredda" }
    ],

    "downcomer": [
      { "tag": "DC1", "nps": "18\" Sch.120", "id_mm": 387.2, "n": 1, "diritti_mm": [250,2623,2376],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":2} ], "z_m": 0.80, "angolo_gradi": 150 },
      { "tag": "DC2", "nps": "18\" Sch.120", "id_mm": 387.2, "n": 1, "diritti_mm": [250,2623,2376],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":2} ], "z_m": 0.80, "angolo_gradi": 210 },
      { "tag": "DC3", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [250,3040,2621],
        "curve": [ {"gradi":60,"r_su_d":1.5,"n":1}, {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":1} ], "z_m": 2.50, "angolo_gradi": 150 },
      { "tag": "DC4", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [250,3040,2621],
        "curve": [ {"gradi":60,"r_su_d":1.5,"n":1}, {"gradi":90,"r_su_d":1.5,"n":1}, {"gradi":30,"r_su_d":1.5,"n":1} ], "z_m": 2.50, "angolo_gradi": 210 },
      { "tag": "DC5", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 4.50, "angolo_gradi": 180 },
      { "tag": "DC6", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 6.80, "angolo_gradi": 180 },
      { "tag": "DC7", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 9.00, "angolo_gradi": 180 },
      { "tag": "DC8", "nps": "16\" Sch.120", "id_mm": 344.6, "n": 1, "diritti_mm": [500,2873,1159,1377],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2}, {"gradi":30,"r_su_d":1.5,"n":3} ], "z_m": 11.00, "angolo_gradi": 180 },
      { "tag": "DC9", "nps": "4\" Sch.120", "id_mm": 92.1, "n": 1, "diritti_mm": [500,3000,1500],
        "curve": [ {"gradi":90,"r_su_d":1.5,"n":2} ], "z_m": 12.70, "angolo_gradi": 180, "nota": "estremita' fredda" }
    ]
  },

  "drum": {
    "modello_attivo": true,
    "id_mm": 3000.0,
    "lunghezza_tt_mm": 13000.0,
    "livello_normale_mm": 1650.0,
    "calm_box_n": 4,
    "calm_box_risers_per_box": 1,
    "calm_box_area_m2": 0.22799999999999998,
    "calm_box_lunghezza_m": 2.30,
    "calm_box_dh_m": 0.47010309278350513,
    "canale_curvatura_gradi": 150.0,
    "calm_box_top_opening_m2": 0.35,
    "calm_box_opening_above_level": true,
    "calm_box_k_extra": 1.0,
    "calm_box_waterfall_m": 0.30,
    "downcomer_entry_area_m2": 0.0,
    "downcomer_vortex_breaker_k": 0.5,
    "demister_area_m2": 20.8,
    "demister_k": 2.0,
    "camini_numero": 8,
    "camini_id_mm": 202.7,
    "vapore_esterno_kgs": 14.12,
    "dp_costruttore_mbar": -1.0
  },

  "bocchelli": {
    "n_riser": 0,
    "n_downcomer": 0,
    "v_downcomer_ms": 2.0,
    "rhov2_max_riser": 6000.0,
    "rhov2_max_downcomer": 3000.0
  },

  "vincoli": [
    { "chiave": "dnbr_min", "min": 2.0, "peso": 1.0, "richiesto": true },
    { "chiave": "t_metallo_tubi_max_c", "max": 450.0, "peso": 1.0, "richiesto": true },
    { "chiave": "dp_gas_mbar", "max": 300.0, "peso": 0.5, "richiesto": true },
    { "chiave": "v_vcrit", "max": 0.80, "peso": 1.0, "richiesto": true },
    { "chiave": "peso_whb_kg", "max": 999999.0, "peso": 0.2, "richiesto": false },
    { "chiave": "ingombro_whb_m2", "max": 999999.0, "peso": 0.2, "richiesto": false }
  ],

  "rating": {
    "carichi": [
      { "nome": "base" },
      { "nome": "110%", "fattore_portata_gas": 1.10 }
    ]
  },

  "optimize": {
    "carichi": [
      { "nome": "base" },
      { "nome": "110%", "fattore_portata_gas": 1.10 }
    ],
    "variabili": [
      { "chiave": "lunghezza_ferrula_mm", "min": 100.0, "max": 350.0, "passo": 25.0 },
      { "chiave": "lunghezza_tubi_m", "min": 11.0, "max": 14.0, "passo": 0.25 },
      { "chiave": "numero_tubi", "min": 780.0, "max": 900.0, "passo": 4.0 }
    ],
    "obiettivo": [
      { "chiave": "peso_whb_kg", "peso": 1.0, "senso": "min" },
      { "chiave": "ingombro_whb_m2", "peso": 0.25, "senso": "min" },
      { "chiave": "ingombro_drum_m2", "peso": 0.10, "senso": "min" }
    ],
    "max_iterazioni": 80,
    "tolleranza": 0.001
  },

  "design": {
    "carichi": [
      { "nome": "base" },
      { "nome": "110%", "fattore_portata_gas": 1.10 }
    ],
    "obiettivo": [
      { "chiave": "peso_whb_kg", "peso": 1.0, "senso": "min" },
      { "chiave": "ingombro_whb_m2", "peso": 0.25, "senso": "min" }
    ],
    "spazio": {
      "numero_tubi": [800, 848, 896],
      "lunghezza_tubi_m": [12.0, 13.0, 14.0],
      "lunghezza_ferrula_mm": [150.0, 200.0, 250.0],
      "mantello_id_mm": [1950.0, 2025.0, 2100.0],
      "passo_tubi_mm": [48.0, 50.8, 53.0],
      "quota_drum_m": [5.5, 6.0, 6.5]
    }
  }
}
"""

let selfTest () =
    let ci = CultureInfo.InvariantCulture
    let mutable fails = 0
    let check name (got: float) (exp: float) (tol: float) =
        let err = abs (got - exp) / abs exp
        if err > tol then fails <- fails + 1
        printfn "  %-50s %14s  (rif. %-14s) %s"
            name (got.ToString("G8", ci)) (exp.ToString("G8", ci)) (if err <= tol then "OK" else "FALLITO")

    printfn "IAPWS-IF97 - punti di riferimento ufficiali"
    check "psat(300 K) [MPa]" (Steam.psat_MPa 300.0) 0.353658941e-2 1e-7
    check "psat(500 K) [MPa]" (Steam.psat_MPa 500.0) 0.263889776e1 1e-7
    check "Tsat(0.1 MPa) [K]" (Steam.tsat_K 0.1) 372.755919 1e-7
    check "Tsat(10 MPa) [K]" (Steam.tsat_K 10.0) 584.149488 1e-7
    let (v1, h1, cp1, s1) = Steam.region1 3.0 300.0
    check "R1 (3 MPa,300 K) v" v1 0.100215168e-2 1e-8
    check "R1 (3 MPa,300 K) h" h1 0.115331273e3 1e-8
    check "R1 (3 MPa,300 K) cp" cp1 0.417301218e1 1e-8
    check "R1 (3 MPa,300 K) s" s1 0.392294792 1e-8
    let (v1b, h1b, _, _) = Steam.region1 80.0 300.0
    check "R1 (80 MPa,300 K) v" v1b 0.971180894e-3 1e-8
    check "R1 (80 MPa,300 K) h" h1b 0.184142828e3 1e-8
    let (v1c, h1c, _, _) = Steam.region1 3.0 500.0
    check "R1 (3 MPa,500 K) v" v1c 0.120241800e-2 1e-8
    check "R1 (3 MPa,500 K) h" h1c 0.975542239e3 1e-8
    let (v2, h2, cp2, _) = Steam.region2 0.0035 300.0
    check "R2 (0.0035 MPa,300 K) v" v2 0.394913866e2 1e-8
    check "R2 (0.0035 MPa,300 K) h" h2 0.254991145e4 1e-8
    check "R2 (0.0035 MPa,300 K) cp" cp2 0.191300162e1 1e-8
    let (v2b, h2b, _, _) = Steam.region2 0.0035 700.0
    check "R2 (0.0035 MPa,700 K) v" v2b 0.923015898e2 1e-8
    check "R2 (0.0035 MPa,700 K) h" h2b 0.333568375e4 1e-8
    let (v2c, h2c, _, _) = Steam.region2 30.0 700.0
    check "R2 (30 MPa,700 K) v" v2c 0.542946619e-2 1e-8
    check "R2 (30 MPa,700 K) h" h2c 0.263149474e4 1e-8
    check "mu(298.15 K,998) [uPa s]" (Steam.viscosity 298.15 998.0 * 1e6) 889.735100 1e-5
    check "k(298.15 K,998) [mW/m/K]" (Steam.conductivity 298.15 998.0 * 1e3) 607.712868 1e-4
    check "sigma(300 K) [mN/m]" (Steam.surfaceTension 300.0 * 1e3) 71.6893 1e-4

    printfn ""
    printfn "Proprieta' dei gas"
    let air = [ GasProps.N2, 0.79; GasProps.O2, 0.21 ]
    let p300 = GasProps.mix air 300.0 101325.0 1.0
    check "aria 300 K rho [kg/m3]" p300.Rho 1.177 0.01
    check "aria 300 K cp [J/kg/K]" p300.Cp 1005.0 0.02
    check "aria 300 K mu [uPa s]" (p300.Mu * 1e6) 18.5 0.03
    check "aria 300 K k [W/m/K]" p300.K 0.0263 0.05

    printfn ""
    printfn "Confronto con il datasheet (miscela di riferimento)"
    let comp = Defaults.referenceComposition
    let pin = GasProps.mix comp (cToK 967.5) (barToPa 34.74) 1.0
    let pout = GasProps.mix comp (cToK 355.0) (barToPa 34.44) 1.0
    check "MW miscela [kg/kmol]" (GasProps.mixMolarMass comp * 1000.0) 15.99 2e-3
    check "rho ingresso [kg/m3]" pin.Rho 5.36 0.02
    check "rho uscita [kg/m3]" pout.Rho 10.48 0.02
    check "cp ingresso [kJ/kg/K]" (pin.Cp / 1000.0) 2.353 0.05
    check "cp uscita [kJ/kg/K]" (pout.Cp / 1000.0) 2.119 0.05
    let pinM = GasProps.mixWith GasProps.MolarAverage comp (cToK 967.5) (barToPa 34.74) 1.0
    let poutM = GasProps.mixWith GasProps.MolarAverage comp (cToK 355.0) (barToPa 34.44) 1.0
    printfn "  --- mu e k: il datasheet usa la media molare, il codice per default Wilke"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "k ingresso [W/m/K]" (pin.K.ToString("G6", ci)) (pinM.K.ToString("G6", ci)) "0.1722"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "k uscita [W/m/K]" (pout.K.ToString("G6", ci)) (poutM.K.ToString("G6", ci)) "0.1011"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "mu ingresso [cP]" ((pin.Mu * 1000.0).ToString("G6", ci)) ((pinM.Mu * 1000.0).ToString("G6", ci)) "0.0376"
    printfn "  %-50s %14s  (media molare %-10s rif. datasheet %s)"
        "mu uscita [cP]" ((pout.Mu * 1000.0).ToString("G6", ci)) ((poutM.Mu * 1000.0).ToString("G6", ci)) "0.0223"
    check "media molare: mu ingresso [cP]" (pinM.Mu * 1000.0) 0.0376 0.05
    check "media molare: k ingresso [W/m/K]" pinM.K 0.1722 0.10
    let sat = Steam.sat (barToPa 117.84)
    check "Tsat a 117.84 bar [C]" (kToC sat.Tsat) 323.3 5e-4

    printfn ""
    printfn "Gas reale: secondo coefficiente del viriale (p = 34.74 bar)"
    printfn "  %-9s %14s %10s %14s %14s" "T [K]" "B_H2O [m3/mol]" "Z mix" "h_res [kJ/kg]" "cp_res [J/kgK]"
    let mwx = GasProps.mixMolarMass (GasProps.normalize comp)
    for t in [ 628.0; 700.0; 850.0; 1000.0; 1240.0 ] do
        let bw = GasProps.Virial.bWater t
        let (z, hr, cpr) = GasProps.Virial.residual (GasProps.normalize comp) t (barToPa 34.74)
        printfn "  %-9.0f %14.4e %10.5f %14.2f %14.2f" t bw z (hr / mwx / 1000.0) (cpr / mwx)
    let dh_id =
        GasProps.enthalpyAbs comp (cToK 967.5) - GasProps.enthalpyAbs comp (cToK 355.0)
    let dh_re =
        GasProps.enthalpyAbsReal true comp (cToK 967.5) (barToPa 34.74)
        - GasProps.enthalpyAbsReal true comp (cToK 355.0) (barToPa 34.44)
    printfn "  salto entalpico 967.5 -> 355 C: ideale %.1f kJ/kg, reale %.1f kJ/kg  (%+.2f %%)"
        (dh_id / 1000.0) (dh_re / 1000.0) (100.0 * (dh_re / dh_id - 1.0))

    printfn ""
    printfn "Shift: K_p(700 K) = %.3f ; K_p(1000 K) = %.3f" (Shift.kp 700.0) (Shift.kp 1000.0)
    printfn ""
    if fails = 0 then printfn "TUTTI I CONTROLLI SUPERATI" else printfn "%d CONTROLLI FALLITI" fails
    fails

let writeDefaultOptions path =
    let dir = Path.GetDirectoryName(Path.GetFullPath path)
    if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
    Options.saveTemplate path Options.defaultOptions
    printfn "Project options written to: %s" (Path.GetFullPath path)
    0

let rememberRecentFiles (optionsPath: string option) (casePath: string option) =
    try
        RecentFilesStore.persistUpdate RecentFilesStore.defaultPath optionsPath casePath
    with ex ->
        eprintfn "Recent files store update skipped: %s" ex.Message

let githubPlan optionsPath =
    let opts = Options.load optionsPath
    let plan = GitHubTransfer.plan opts
    printfn "GitHub transfer plan"
    printfn "  repository: %s" (if String.IsNullOrWhiteSpace plan.RepositoryUrl then "(not set)" else plan.RepositoryUrl)
    printfn "  branch:     %s" plan.Branch
    printfn "  commit:     %s" plan.CommitMessage
    printfn ""
    for c in plan.Commands do
        printfn "  %s" c
    0

let githubPush optionsPath =
    let opts = Options.load optionsPath
    match GitHubTransfer.execute (Directory.GetCurrentDirectory()) opts with
    | Ok output ->
        printfn "GitHub transfer completed."
        if not (String.IsNullOrWhiteSpace output) then printfn "%s" output
        0
    | Error err ->
        eprintfn "GitHub transfer failed: %s" err
        3

let writeSulphurTable (path: string) (pressureBara: float) (sAtoms: float) (inertMols: float)
                      (tMinC: float) (tMaxC: float) (stepC: float) =
    let pPa = barToPa pressureBara
    let sb = Text.StringBuilder()
    sb.AppendLine("T[C];p_sat_total[Pa];p_sulphur_dry[Pa];p_sulphur_eq[Pa];y_sulphur_eq[-];mean_atomicity_eq[-];nS2_eq[mol];nS6_eq[mol];nS8_eq[mol];condensing[-];condensed_atoms[mol];condensed_fraction[-];mu_liq[Pa.s]") |> ignore
    for tC in floatGrid tMinC tMaxC stepC do
        let tK = cToK tC
        let dry = Sulphur.speciate tK pPa sAtoms inertMols
        let cond = Sulphur.condenserState tK pPa sAtoms inertMols
        let vap = cond.Vapour
        let values =
            [ tC
              Sulphur.pSatTotal tK
              dry.PS2 + dry.PS6 + dry.PS8
              cond.PSulphur
              vap.YSulphur
              vap.MeanAtomicity
              vap.NS2
              vap.NS6
              vap.NS8
              (if cond.Condensing then 1.0 else 0.0)
              cond.NCondensed
              cond.CondensedFraction
              Sulphur.muLiquid tK ]
            |> List.map (fun v -> v.ToString("G6", CultureInfo.InvariantCulture))
        sb.AppendLine(String.Join(";", values)) |> ignore
    let dir = Path.GetDirectoryName(Path.GetFullPath path)
    if not (String.IsNullOrWhiteSpace dir) then Directory.CreateDirectory dir |> ignore
    File.WriteAllText(path, sb.ToString())
    let hotT = max tMinC tMaxC
    let hot = Sulphur.speciate (cToK hotT) pPa sAtoms inertMols
    let pSulphurHot = hot.PS2 + hot.PS6 + hot.PS8
    let dewC = kToC (Sulphur.dewPoint pSulphurHot)
    printfn "Tabella zolfo %g-%g C (passo %g C) scritta in %s" (min tMinC tMaxC) (max tMinC tMaxC) stepC (Path.GetFullPath path)
    printfn "  p = %.3f bar(a), S-atomi = %.6g mol, inerti = %.6g mol" pressureBara sAtoms inertMols
    printfn "  Hot-end dry sulphur partial pressure = %.3f Pa, dew point = %.1f C" pSulphurHot dewC
    0

/// Short form printed on a usage error, where the user needs the shape of the
/// command line and not the manual.
let printUsage () =
    printfn "Usage:"
    printfn "  whb [case.json] [--out <folder>] [--options <whb.options.json>]"
    printfn "  whb --template [file.json]"
    printfn "  whb --options-template [file.json]"
    printfn "  whb --selftest"
    printfn "  whb --steamtable [file.csv] [--tmin <C>] [--tmax <C>] [--step <C>]"
    printfn "  whb --sulphur [file.csv] [--pressure-bara <bar>] [--s-atoms-mols <mol>] [--inert-mols <mol>]"
    printfn "                 [--tmin <C>] [--tmax <C>] [--step <C>]"
    printfn "  whb --sulphur-condenser [case.json] [--out <folder>]"
    printfn "  whb --rating [case.json] [--out <folder>]"
    printfn "  whb --design [case.json] [--out <folder>]"
    printfn "  whb --loads [case.json] [--out <folder>]"
    printfn "  whb --sizing [case.json] [--out <folder>]"
    printfn "  whb --optimize [case.json] [--out <folder>]"
    printfn "  whb --optimize-legacy [case.json] [--out <folder>]"
    printfn "  whb --github-plan [options.json]"
    printfn "  whb --github-push [options.json]"
    printfn ""
    printfn "If no case file is provided, the reference case is used."
    printfn "Run 'whb --help' for the full list of commands and options."

/// Full manual: every command, every flag, every options-file key and every exit
/// code the program can return.
let printHelp () =
    let rule = String('-', 78)
    printfn "WHB / PGC - thermal, hydraulic and diagnostic calculations for fire-tube"
    printfn "waste heat boilers and process gas coolers."
    printfn ""
    printfn "Usage:  whb [command] [case.json] [options]"
    printfn ""
    printfn "With no command and no case file, the built-in reference case is run."
    printfn "A case file may be given to any calculation command; when omitted, the"
    printfn "reference case is used."
    printfn ""
    printfn "%s" rule
    printfn "COMMANDS"
    printfn "%s" rule
    printfn "  (none) [case.json]      Full design run: thermal, hydraulic, bypass,"
    printfn "                          vibration and mechanical checks, then all report"
    printfn "                          and CSV files. This is the normal command."
    printfn ""
    printfn "  --sizing [case.json]    Design run reported as a sizing sheet only."
    printfn "                          Writes dimensionamento.txt and nothing else."
    printfn ""
    printfn "  --rating [case.json]    Verifies one fixed geometry against one or more"
    printfn "                          configured load cases and explicit constraints."
    printfn "                          All checks go through the same shared thermal/"
    printfn "                          process plus mechanical verification engine used"
    printfn "                          by the other modes. Writes rating.txt and"
    printfn "                          rating.csv plus interfaccia_meccanica.txt."
    printfn ""
    printfn "  --optimize [case.json]  Modifies one existing geometry within explicit"
    printfn "                          variable bounds to minimize configured weight/"
    printfn "                          envelope objectives while keeping every required"
    printfn "                          load case inside the same shared verification"
    printfn "                          constraints. Writes ottimizzazione.txt plus"
    printfn "                          interfaccia_meccanica.txt."
    printfn ""
    printfn "  --design [case.json]    Explores a discrete greenfield geometry space,"
    printfn "                          starting from the non-varied details of the case"
    printfn "                          and selecting the best candidate under the same"
    printfn "                          shared verification engine and constraints."
    printfn "                          Writes design.txt plus interfaccia_meccanica.txt."
    printfn ""
    printfn "  --loads [case.json]     Partial-load campaign at 50, 60, 70, 80, 90, 100"
    printfn "                          and 110 %% of gas flow, on a reduced 40 x 8 grid."
    printfn "                          Writes carichi.txt and carichi.csv."
    printfn ""
    printfn "  --optimize-legacy       Legacy constrained search for the largest duty that still"
    printfn "                          satisfies DNBR, metal temperature, gas pressure"
    printfn "                          drop and flow-induced vibration, moving ferrule"
    printfn "                          length and tube length. Writes ottimizzazione_legacy.txt,"
    printfn "                          which reports not only where the optimum is but"
    printfn "                          WHAT HOLDS IT THERE: an active constraint, the edge"
    printfn "                          of the search range, a genuine interior stationary"
    printfn "                          point, or no feasible point at all."
    printfn "                          Every evaluation is a full coupled solve, so this"
    printfn "                          takes minutes, not seconds."
    printfn ""
    printfn "  --selftest              Check the installed correlations and property"
    printfn "                          functions against published reference values."
    printfn "                          Writes nothing; exits non-zero on a mismatch."
    printfn ""
    printfn "  --steamtable [file.csv] Write a saturation table from tmin to tmax."
    printfn "                          Default file name: steam_saturation_table.csv"
    printfn "                          Default range: 20 to 310 degC every 10 degC."
    printfn ""
    printfn "  --sulphur [file.csv]    Write a standalone sulphur-process sweep:"
    printfn "                          S2/S6/S8 equilibrium, total sulphur saturation"
    printfn "                          pressure, onset of condensation and condensed"
    printfn "                          fraction against temperature."
    printfn "                          Default file name: sulphur_table.csv"
    printfn "                          Defaults: 1.7 bar(a), 8 mol S-atoms, 100 mol"
    printfn "                          inerts, 120 to 350 degC every 10 degC."
    printfn ""
    printfn "  --sulphur-condenser     Dedicated Claus sulphur-condenser run."
    printfn "                          Reads the condensatore_zolfo section of the case"
    printfn "                          file. If usa_uscita_whb = true, it first solves"
    printfn "                          the base WHB and then feeds the solved mixed"
    printfn "                          outlet gas into the dedicated condenser module."
    printfn "                          Otherwise it runs on condensatore_zolfo.gas_ingresso"
    printfn "                          only. Writes sulphur_condenser.txt and"
    printfn "                          sulphur_condenser_profile.csv."
    printfn ""
    printfn "  --template [file.json]  Write a commented case template."
    printfn "                          Default file name: case.json"
    printfn ""
    printfn "  --options-template [f]  Write a project options file with the documented"
    printfn "                          defaults. Default file name: whb.options.json"
    printfn ""
    printfn "  --github-plan [opt]     Print the git commands the transfer would run,"
    printfn "                          without running any of them."
    printfn ""
    printfn "  --github-push [opt]     Execute that transfer."
    printfn ""
    printfn "  --help, -h              This text."
    printfn ""
    printfn "%s" rule
    printfn "OPTIONS"
    printfn "%s" rule
    printfn "  --out <folder>          Output subfolder under results/. Created if missing."
    printfn "                          Absolute or external paths are folded back under results/."
    printfn ""
    printfn "  --options <file>        Project options file to read."
    printfn "                          Default: whb.options.json in the current folder."
    printfn "                          Keys absent from the file keep their documented"
    printfn "                          default, so a partial file is safe."
    printfn ""
    printfn "Both options may be combined with any calculation command."
    printfn ""
    printfn "%s" rule
    printfn "PROJECT OPTIONS FILE (whb.options.json)"
    printfn "%s" rule
    printfn "  folders.resultsFolder           Default output folder."
    printfn "  folders.tempFolder              Temporary and preflight files."
    printfn "  folders.casesFolder             Convention for case files."
    printfn "  folders.databasesFolder         Convention for property databases."
    printfn "  folders.reportsFolder           Convention for report material."
    printfn "  folders.packagesFolder          Convention for package artifacts."
    printfn ""
    printfn "  logging.enabled                 Timestamped phase logging. Default true,"
    printfn "                                  and active on every calculation command."
    printfn "  logging.logFile                 Log file path. Default logs/whb-run.log"
    printfn ""
    printfn "  reporting.generateFullReport    Write report.txt. Default true."
    printfn "  reporting.generateHtmlReport    Write report.html. Default true."
    printfn ""
    printfn "  calculation.axialSections       Axial grid sections. Default 90."
    printfn "  calculation.verticalBands       Vertical bands. Default 12."
    printfn "  calculation.parallelism         Bypass-map points solved concurrently."
    printfn "                                  Changes run time only, never results."
    printfn "                                  Use 1 to force a sequential run."
    printfn "                                  Default: processor count."
    printfn "  calculation.bypassMapMode       adaptive | fast | full | fixed."
    printfn "                                  Because the map is solved concurrently,"
    printfn "                                  'full' costs little more than 'adaptive'."
    printfn "  calculation.bypassTargetToleranceK"
    printfn "                                  Tolerance on the target mixed outlet"
    printfn "                                  temperature. Default 0.5 K."
    printfn "  calculation.gasPropertyCache    Reuse repeated gas-property evaluations."
    printfn "  calculation.correlationValidityWarnings"
    printfn "                                  Raise findings when a correlation is used"
    printfn "                                  outside its usual validity range."
    printfn "  calculation.useRealGas          Legacy switch; prefer gas.modello_gas in"
    printfn "                                  the case file."
    printfn "  calculation.strictValidation    Stricter input consistency checks."
    printfn "  calculation.dutyToleranceFraction"
    printfn "                                  Duty tolerance for acceptance checks."
    printfn ""
    printfn "  .user/recent-files.json         Machine-local recent case/options history."
    printfn "                                  This state is kept outside whb.options.json."
    printfn ""
    printfn "%s" rule
    printfn "CASE FILE"
    printfn "%s" rule
    printfn "  The JSON case file uses engineering datasheet language, grouped in"
    printfn "  sections: gas, vapore, tubi, ferrula, circuito, drum, bypass, materiali."
    printfn "  Mode-specific optional sections are: vincoli, rating, optimize, design."
    printfn "  Start from 'whb --template case.json'; the full field list is in"
    printfn "  docs/INPUT_SCHEMA.md."
    printfn ""
    printfn "  Pressures ending in _bara are absolute; pressure drops are differential."
    printfn ""
    printfn "%s" rule
    printfn "OUTPUT FILES"
    printfn "%s" rule
    printfn "  report.txt / report.html   Full engineering report."
    printfn "  criticita.txt              Findings and warnings, most severe first."
    printfn "  pds_comparison.txt/.csv    Comparison against the client datasheet."
    printfn "  inventory_summary.txt/.csv Water volumes and estimated metal weights."
    printfn "  interfaccia_meccanica.txt  Prepared interface for future mechanical"
    printfn "                             code calculations."
    printfn "  celle.csv                  Cell-by-cell thermal field."
    printfn "  profilo_assiale.csv        Axial profiles."
    printfn "  tensioni.csv               Stress field."
    printfn "  valvola_bypass.csv         Bypass valve sweep."
    printfn "  vibrazioni.txt             Vibration screening per band."
    printfn "  maldistribuzione.txt       Maldistribution sensitivity."
    printfn "  dimensionamento.txt        Sizing sheet (--sizing, and normal runs)."
    printfn "  rating.txt / rating.csv    Shared-engine geometry rating."
    printfn "  carichi.txt / carichi.csv  Partial-load curves (--loads)."
    printfn "  ottimizzazione.txt         Shared-engine optimize result (--optimize)."
    printfn "  ottimizzazione_legacy.txt  Legacy maximize-duty search (--optimize-legacy)."
    printfn "  design.txt                 Shared-engine greenfield design result (--design)."
    printfn "  sulphur_table.csv          Standalone sulphur sweep (--sulphur)."
    printfn "  sulphur_condenser.txt      Dedicated Claus sulphur-condenser report."
    printfn "  sulphur_condenser_profile.csv"
    printfn "                             Axial profile for the dedicated sulphur condenser."
    printfn ""
    printfn "%s" rule
    printfn "EXIT CODES"
    printfn "%s" rule
    printfn "  0   Success."
    printfn "  1   Unhandled error; the message is printed on stderr."
    printfn "  2   Usage error: unknown option, or case file not found."
    printfn "  3   GitHub transfer failed."
    printfn "  4   Invalid JSON in the case or options file."
    printfn "  5   File or folder access error."
    printfn ""
    printfn "%s" rule
    printfn "EXAMPLES"
    printfn "%s" rule
    printfn "  whb                                    Run the reference case."
    printfn "  whb my-case.json --out run1             Run a case into results/run1."
    printfn "  whb --template my-case.json            Start a new case file."
    printfn "  whb my-case.json --options prj.json    Use a specific options file."
    printfn "  whb --rating my-case.json              Rate one geometry on configured loads."
    printfn "  whb --optimize my-case.json            Optimize an existing geometry."
    printfn "  whb --design my-case.json              Explore a greenfield geometry space."
    printfn "  whb --loads my-case.json               Partial-load curves."
    printfn "  whb --optimize-legacy my-case.json     Legacy maximize-duty search."
    printfn "  whb --selftest                         Verify the installation."
    printfn "  whb --sulphur sulphur.csv --pressure-bara 1.7 --s-atoms-mols 8 --inert-mols 100"
    printfn "  whb --sulphur-condenser claus-case.json --out condenser"
    printfn ""
    printfn "This software is a design aid. It is not a certified pressure-vessel or"
    printfn "boiler-code tool and does not replace code calculations or vendor"
    printfn "verification."

let getOpt name def args =
    let rec go = function
        | a :: b :: _ when a = name -> b
        | _ :: rest -> go rest
        | [] -> def
    go args
