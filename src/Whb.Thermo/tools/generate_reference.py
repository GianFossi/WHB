"""
Generates tests/WhbThermo.Tests/reference/coolprop-pure.csv.

CoolProp is used ONLY here, offline, as an oracle. The F# library never depends
on it: the generated CSV is committed and frozen, so the test suite stays
deterministic and dependency-free.

    pip install CoolProp
    python tools/generate_reference.py
"""
from pathlib import Path

try:
    from CoolProp.CoolProp import PropsSI
except ImportError:
    raise SystemExit("pip install CoolProp first")

# Our key -> CoolProp fluid name. Only species CoolProp models reliably as a gas.
FLUIDS = {
    "H2": "Hydrogen", "N2": "Nitrogen", "CH4": "Methane",
    "CO": "CarbonMonoxide", "CO2": "CarbonDioxide", "H2O": "Water",
    "NH3": "Ammonia", "Ar": "Argon", "He": "Helium",
    "H2S": "HydrogenSulfide", "SO2": "SulfurDioxide",
    "C2H4": "Ethylene", "C2H6": "Ethane", "C3H8": "n-Propane",
    "C3H6": "Propylene", "C6H6": "Benzene", "C7H8": "Toluene",
    "N2O": "NitrousOxide",
}

TEMPERATURES_K = [400, 500, 600, 700, 800, 1000, 1200]
PRESSURE_BAR = 1.0

rows = []
for key, fluid in FLUIDS.items():
    # Above the equation of state's Tmax CoolProp extrapolates, and its transport
    # correlations with it: ammonia conductivity turns negative above 1000 K and
    # propane's runs twice the kinetic-theory value at 1600 K. Beyond Tmax it is
    # not a reference, so it is not used as one.
    try:
        t_max = PropsSI("Tmax", fluid)
    except Exception as exc:
        print(f"  skip {key}: {exc}")
        continue
    for tK in TEMPERATURES_K:
        if tK > t_max:
            continue
        p = PRESSURE_BAR * 1e5
        try:
            # Ideal-gas cp: the library's cp is ideal by construction (Z = 1), so
            # the real-gas Cpmass would charge it with the departure near
            # saturation (5 % for steam at 400 K and 1 bar), which is not a data error.
            cp = PropsSI("Cp0mass", "T", tK, "P", p, fluid)
            mu = PropsSI("viscosity", "T", tK, "P", p, fluid)
            k = PropsSI("conductivity", "T", tK, "P", p, fluid)
        except Exception as exc:
            print(f"  skip {key} @ {tK} K: {exc}")
            continue
        rows.append(f"{key},{tK},{PRESSURE_BAR},{cp:.6g},{mu:.6g},{k:.6g}")

out = Path(__file__).resolve().parent.parent / "tests" / "WhbThermo.Tests" / "reference"
out.mkdir(parents=True, exist_ok=True)
target = out / "coolprop-pure.csv"
target.write_text("species,T_K,P_bar,cp_J_kgK,mu_Pas,k_W_mK\n" + "\n".join(rows) + "\n")
print(f"{target}: {len(rows)} reference points")
