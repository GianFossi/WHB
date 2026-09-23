"""
Generates src/WhbThermo.Data/species-database.json.

Design rule: every numeric record carries its own `source` and validity range.
Species whose Cp is not yet backed by a published Shomate/NASA fit are emitted
with cpModel.kind = "anchor" -- the F# loader turns those into WARNINGS, not
into silent bad numbers.

To complete the database: replace each "anchor" entry with a "shomate" entry
taken from NIST WebBook or the Burcat/NASA thermodynamic database.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import db_guard  # noqa: E402

MANUAL = "WHB/PGC Design Manual s3, Sutherland fit"
NIST = "NIST Chemistry WebBook, Shomate equation (VERIFY before production use)"

# key, name, M [kg/kmol], mu0 [1e-6 Pa.s], S_mu [K], k0 [1e-3 W/m/K], S_k [K], cp500 [kJ/kg/K]
TRANSPORT = [
    ("H2",   "Hydrogen",          2.016,   8.76,  72.0, 168.2,  97.0, 14.52),
    ("N2",   "Nitrogen",         28.013,  17.54, 111.0,  24.0, 150.0,  1.09),
    ("CH4",  "Methane",          16.042,  10.87, 198.0,  31.4, 400.0,  2.85),
    ("CO",   "CarbonMonoxide",   28.010,  17.20, 118.0,  23.2, 140.0,  1.10),
    ("CO2",  "CarbonDioxide",    44.010,  14.70, 240.0,  14.5, 320.0,  1.14),
    ("H2O",  "WaterVapour",      18.015,  12.00, 650.0,  18.1, 800.0,  2.13),
    ("NH3",  "Ammonia",          17.031,   9.82, 370.0,  22.0, 500.0,  2.65),
    ("Ar",   "Argon",            39.948,  22.10, 144.0,  16.3, 170.0,  0.52),
    ("H2S",  "HydrogenSulfide", 34.080,  12.40, 331.0,  13.0, 450.0,  1.18),
    ("SO2",  "SulfurDioxide",   64.060,  12.55, 416.0,   8.6, 480.0,  0.77),
    ("COS",  "CarbonylSulfide", 60.070,  12.00, 380.0,  10.5, 420.0,  0.84),
    ("CS2",  "CarbonDisulfide", 76.140,   9.90, 450.0,   7.5, 510.0,  0.71),
    ("S2",   "DiatomicSulfur",  64.130,  11.50, 500.0,   9.0, 550.0,  0.58),
    ("S6",   "Hexasulfur",     192.380,  10.20, 600.0,   6.8, 650.0,  0.69),
    ("S8",   "Octasulfur",     256.510,   9.50, 650.0,   5.5, 700.0,  0.72),
    ("C2H4", "Ethylene",         28.054,  10.08, 226.0,  17.5, 350.0,  2.38),
    ("C2H6", "Ethane",           30.070,   9.10, 252.0,  18.0, 380.0,  2.76),
    ("C3H6", "Propylene",        42.080,   8.35, 290.0,  15.2, 400.0,  2.45),
    ("C3H8", "Propane",          44.096,   8.00, 310.0,  15.0, 420.0,  2.82),
    ("C2H2", "Acetylene",        26.038,  10.20, 210.0,  19.5, 320.0,  2.05),
    ("C6H6", "Benzene",          78.110,   7.50, 380.0,   9.5, 450.0,  1.85),
    ("C7H8", "Toluene",          92.140,   6.90, 410.0,   9.0, 470.0,  1.90),
    ("NO",   "NitricOxide",      30.006,  18.80, 128.0,  23.8, 160.0,  1.05),
    ("NO2",  "NitrogenDioxide",  46.006,  14.10, 270.0,  13.0, 350.0,  1.01),
    ("N2O",  "NitrousOxide",     44.013,  14.60, 260.0,  15.1, 340.0,  1.09),
    ("SO3",  "SulfurTrioxide",  80.060,  13.50, 450.0,  10.0, 500.0,  0.95),
    ("HCN",  "HydrogenCyanide",  27.025,  11.20, 280.0,  16.5, 360.0,  1.72),
    ("He",   "Helium",            4.003,  19.60,  79.0, 150.0, 100.0,  5.19),
]

# Shomate: Cp[J/mol/K] = A + B*t + C*t^2 + D*t^3 + E/t^2,  t = T[K]/1000
# key -> list of (tMinK, tMaxK, A, B, C, D, E, F, G, H)
SHOMATE = {
    "N2":  [(500, 2000,  19.50583,  19.88705,  -8.598535,   1.369784,  0.527601,   -4.935202, 212.3900,    0.0)],
    "H2O": [(500, 1700,  30.09200,   6.832514,  6.793435,  -2.534480,  0.082139, -250.8810,  223.3967, -241.8264)],
    "CO2": [(298, 1200,  24.99735,  55.18696, -33.69137,    7.948387, -0.136638, -403.6075,  228.2431, -393.5224)],
    "CO":  [(298, 1300,  25.56759,   6.096130,  4.054656,  -2.671301,  0.131021, -118.0089,  227.3665, -110.5271)],
    "H2":  [(298, 1000,  33.066178, -11.363417, 11.432816, -2.772874, -0.158558,   -9.980797, 172.707974,  0.0)],
    "CH4": [(298, 1300,  -0.703029, 108.4773,  -42.52157,   5.862788,  0.678565,  -76.84376,  158.7163,  -74.87310)],
    "NH3": [(298, 1400,  19.99563,   49.77119, -15.37599,   1.921168,  0.189174,  -53.30667,  203.8591,  -45.89806)],
    "Ar":  [(298, 6000,  20.78600,    2.825911e-7, -1.464191e-7, 1.092131e-8, -3.661371e-8, -6.197350, 179.9990, 0.0)],
    "He":  [(298, 6000,  20.78603,    4.850638e-10, -1.582916e-10, 1.525102e-11, 3.196347e-11, -6.197341, 153.0, 0.0)],
    "SO2": [(298, 1200,  21.43049,   74.35094, -57.75217,  16.35534,   0.086731, -305.7688,  254.8872, -296.8422)],
    "H2S": [(298, 1400,  26.88412,   18.67809,   3.434203, -3.378702,  0.135882,  -28.91211, 233.3747,  -20.50202)],
    "NO":  [(298, 1200,  23.83491,   12.58878,  -1.139011, -1.497459,  0.214194,   83.35783, 237.1219,   90.29114)],
    "NO2": [(298, 1200,  16.10857,   75.89525, -54.38740,  14.30777,   0.239423,   26.17464, 240.5386,   33.09502)],
    "N2O": [(298, 1400,  27.67988,   51.14898, -30.64454,   6.847911, -0.157906,   71.24934, 238.6164,   82.04824)],
    "C2H4":[(298, 1200,  -6.387880, 184.4019, -112.9718,   28.49593,   0.315540,   48.17332, 163.1568,   52.46694)],
}

# key -> (Tc [K], Pc [bar], omega [-])
CRITICAL = {
    "H2": (33.15, 12.96, -0.219), "N2": (126.20, 33.98, 0.037),
    "CH4": (190.56, 45.99, 0.011), "CO": (132.85, 34.94, 0.045),
    "CO2": (304.13, 73.77, 0.224), "H2O": (647.10, 220.64, 0.345),
    "NH3": (405.40, 113.30, 0.256), "Ar": (150.69, 48.63, -0.002),
    "H2S": (373.40, 89.63, 0.090), "SO2": (430.80, 78.84, 0.245),
    "NO": (180.00, 64.80, 0.583), "NO2": (431.00, 101.00, 0.851),
    "N2O": (309.60, 72.45, 0.162), "C2H4": (282.34, 50.41, 0.087),
    "He": (5.195, 2.276, -0.390),
}

species = []
for key, name, M, mu0, smu, k0, sk, cp500 in TRANSPORT:
    if key in SHOMATE:
        segs = [{
            "tMinK": s[0], "tMaxK": s[1],
            "a": s[2], "b": s[3], "c": s[4], "d": s[5],
            "e": s[6], "f": s[7], "g": s[8], "h": s[9],
            "source": NIST,
        } for s in SHOMATE[key]]
        cp = {"kind": "shomate", "segments": segs, "nasa7Segments": [], "anchorCp500C": cp500}
    else:
        cp = {"kind": "anchor", "segments": [], "nasa7Segments": [], "anchorCp500C": cp500,
              "source": MANUAL + " -- single point at 500 degC, NO temperature dependence"}

    tc = CRITICAL.get(key)
    species.append({
        "key": key,
        "name": name,
        "molarMass_kg_kmol": M,
        "viscosity":    {"coeff0": mu0 * 1e-6, "sutherlandK": smu, "tRefK": 273.15,
                         "tMinK": 250.0, "tMaxK": 1800.0, "source": MANUAL},
        "conductivity": {"coeff0": k0 * 1e-3, "sutherlandK": sk, "tRefK": 273.15,
                         "tMinK": 250.0, "tMaxK": 1800.0, "source": MANUAL},
        "cpModel": cp,
        "critical": None if tc is None else
                    {"tcK": tc[0], "pcBar": tc[1], "acentric": tc[2],
                     "source": "Poling et al., Properties of Gases and Liquids, 5th ed., App. A"},
    })

# Preserve everything the other importers added. Regenerating the base tables
# must never silently undo merge_burcat.py, import_cea.py or add_species.py --
# the third time in this project a rebuild would have destroyed manual work.
existing_path = Path(__file__).resolve().parent.parent / "data" / "species-database.json"
preserved = 0
if existing_path.exists():
    old = {s["key"]: s for s in db_guard.load(existing_path)["species"]}
    for sp in species:
        prev = old.get(sp["key"])
        if not prev:
            continue
        # Any Cp model better than what the base tables produce wins, and the
        # transport block travels with it.
        if prev["cpModel"]["kind"] in ("nasa9", "nasa7"):
            sp["cpModel"] = prev["cpModel"]
            sp["molarMass_kg_kmol"] = prev.get("molarMass_kg_kmol", sp["molarMass_kg_kmol"])
            preserved += 1
        if "transport" in prev:
            sp["transport"] = prev["transport"]
        if prev.get("critical") is not None:
            sp["critical"] = prev["critical"]

    # Species that exist only in the current file - added by add_species.py from
    # CEA - are carried over whole. The base tables know nothing about them, so
    # without this a rebuild silently deletes them.
    known = {sp["key"] for sp in species}
    carried = [entry for key, entry in old.items() if key not in known]
    species.extend(carried)
    species.sort(key=lambda s: s["key"])
    preserved += len(carried)

doc = {
    "schemaVersion": "1.1",
    "description": "WHB/PGC process gas species database. Sutherland transport + Shomate Cp.",
    "species": species,
}

out = Path(__file__).resolve().parent.parent / "data" / "species-database.json"
db_guard.save(doc, tool="build_database.py",
              note="rebuilt base tables from the manual's Sutherland and Shomate data",
              path=out, force=db_guard.force_requested())

done = sum(1 for s in species if s["cpModel"]["kind"] == "shomate")
nasa = sum(1 for s in species if s["cpModel"]["kind"] == "nasa7")
pending = sum(1 for s in species if s["cpModel"]["kind"] == "anchor")
print(f"{out}: {len(species)} species -- {done} Shomate, {nasa} NASA-7 "
      f"({preserved} preserved), {pending} pending")
