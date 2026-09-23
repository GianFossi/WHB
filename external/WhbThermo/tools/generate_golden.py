"""
Generates the golden reference files for the WhbThermo test campaign.

    pip install CoolProp cantera iapws
    python tools/generate_golden.py

Three independent oracles, each chosen for what it is actually authoritative on:

  CoolProp  reference Helmholtz equations of state -> pure gas cp, mu, k
  Cantera   kinetic-theory transport -> validates our MIXING RULE in isolation
            from our species data
  iapws     independent IAPWS-IF97 implementation -> water and steam grid

The mixing-rule file is the important one. Comparing a full mixture calculation
end to end conflates two possible errors: bad pure-species data and a bad mixing
rule. So the golden file records Cantera's OWN pure-species mu and k alongside
its mixture result. Feeding those pure values into our Wilke implementation must
reproduce Cantera's mixture value to a fraction of a percent, because at that
point both sides are evaluating the same formula on the same inputs. Any
deviation is a defect in the formula, not in the database.

One asymmetry to know about. For VISCOSITY Cantera implements Wilke, so the
agreement is exact and the test tolerance can be a fraction of a percent. For
CONDUCTIVITY Cantera implements Mathur-Tondon-Saxena, which is a different model
from the Wassiljewa / Mason-Saxena formulation this project uses, so it cannot
serve as a direct oracle. Instead the golden file records the rigorous bounds

    1 / SUM(x_i / k_i)   <=   k_mix   <=   SUM(x_i k_i)

which any physically admissible mixture conductivity must satisfy, plus the
Mathur value for reference. The spread between the two models reaches 14 % on
H2-rich mixtures -- real model uncertainty, not implementation error, and worth
knowing before quoting a conductivity to three figures.

Everything is written as frozen CSV. None of these packages is a runtime or test
dependency; the committed files are.
"""
from __future__ import annotations

import csv
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "tests" / "WhbThermo.Tests" / "reference"
OUT.mkdir(parents=True, exist_ok=True)

R_GAS = 8.31446261815324


# ---------------------------------------------------------------- pure fluids

# our key -> CoolProp fluid name
COOLPROP = {
    "H2": "Hydrogen", "N2": "Nitrogen", "CH4": "Methane", "CO": "CarbonMonoxide",
    "CO2": "CarbonDioxide", "H2O": "Water", "NH3": "Ammonia", "Ar": "Argon",
    "He": "Helium", "H2S": "HydrogenSulfide", "SO2": "SulfurDioxide",
    "C2H4": "Ethylene", "C2H6": "Ethane", "C3H8": "n-Propane",
    "C3H6": "Propylene", "C6H6": "Benzene", "C7H8": "Toluene", "N2O": "NitrousOxide",
}

# 1 bar keeps every fluid firmly in the ideal-gas region, so a deviation is a
# defect in our correlations rather than a missing real-gas term.
PURE_TEMPERATURES = [400, 500, 600, 700, 800, 1000, 1200, 1400, 1600]

# An oracle is only an oracle inside its own validity range, and CoolProp does
# not always refuse politely outside it -- it returned k = 0.0017 W/(m*K) for
# ammonia at 1000 K, roughly two orders of magnitude low, with no error. Two
# filters keep such points out of the golden file:
#
#   1. a margin above the 1 bar saturation temperature, so the comparison is
#      never made where real-gas departure would masquerade as a fit error;
#   2. a physical monotonicity screen on the reference series itself -- gas
#      viscosity and conductivity rise with temperature, so a point that falls
#      is the oracle failing, not our correlation.
SATURATION_MARGIN = 1.25


def write_pure() -> int:
    import CoolProp.CoolProp as CP

    rows = []
    discarded = {"saturation": 0, "monotonicity": 0, "error": 0}

    for key, fluid in COOLPROP.items():
        try:
            t_min = CP.PropsSI("T", "P", 1e5, "Q", 1, fluid) * SATURATION_MARGIN
        except Exception:
            t_min = 0.0

        series = []
        for t in PURE_TEMPERATURES:
            if t < t_min:
                discarded["saturation"] += 1
                continue
            try:
                cp = CP.PropsSI("Cpmass", "T", t, "P", 1e5, fluid)
                mu = CP.PropsSI("viscosity", "T", t, "P", 1e5, fluid)
                k = CP.PropsSI("conductivity", "T", t, "P", 1e5, fluid)
            except Exception:
                discarded["error"] += 1
                continue
            series.append((t, cp, mu, k))

        # Drop the tail from the first point where the reference stops behaving
        # like a gas. Everything beyond it is extrapolation.
        kept = []
        for i, point in enumerate(series):
            if i > 0:
                _, _, mu_prev, k_prev = series[i - 1]
                if point[2] <= mu_prev or point[3] <= k_prev:
                    discarded["monotonicity"] += len(series) - i
                    break
            kept.append(point)

        for t, cp, mu, k in kept:
            rows.append([key, t, 1.0, f"{cp:.7g}", f"{mu:.7g}", f"{k:.7g}"])

    path = OUT / "coolprop-pure.csv"
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["species", "T_K", "P_bar", "cp_J_kgK", "mu_Pas", "k_W_mK"])
        w.writerows(rows)
    print(f"{path.name}: {len(rows)} points, {len({r[0] for r in rows})} species "
          f"(discarded: {discarded['saturation']} near saturation, "
          f"{discarded['monotonicity']} where the oracle stopped behaving like a gas, "
          f"{discarded['error']} errors)")
    return len(rows)


# ------------------------------------------------------------- mixing rules

# Compositions drawn from the process units in the design manual.
MIXTURES = {
    "sru_claus":      {"H2S": 0.04, "SO2": 0.02, "N2": 0.60, "H2O": 0.28, "CO2": 0.06},
    "smr_syngas":     {"H2": 0.50, "CO": 0.10, "CO2": 0.08, "CH4": 0.04, "H2O": 0.28},
    "ammonia_synloop": {"H2": 0.55, "N2": 0.20, "NH3": 0.15, "CH4": 0.07, "AR": 0.03},
    "ethylene_tle":   {"C2H4": 0.30, "C2H6": 0.18, "H2": 0.14, "CH4": 0.22,
                       "C3H8": 0.06, "C2H2": 0.02, "H2O": 0.08},
    "flue_gas":       {"N2": 0.72, "CO2": 0.11, "H2O": 0.10, "NO": 0.005,
                       "NO2": 0.005, "AR": 0.06},
    "nitric_acid":    {"NO": 0.09, "N2": 0.62, "H2O": 0.16, "N2O": 0.01, "CO2": 0.12},
    "dri_reformer":   {"CO": 0.35, "H2": 0.40, "CO2": 0.10, "H2O": 0.08, "N2": 0.07},
    "binary_h2_n2":   {"H2": 0.50, "N2": 0.50},
    "binary_co2_h2o": {"CO2": 0.40, "H2O": 0.60},
    "dilute_h2":      {"H2": 0.02, "N2": 0.98},
}

# Cantera uses AR/HE; we use Ar/He.
CANTERA_NAME = {"Ar": "AR", "He": "HE"}
MIX_TEMPERATURES = [600, 900, 1200, 1500, 1673]


def write_mixing() -> int:
    import cantera as ct

    gas = ct.Solution("gri30.yaml")
    available = set(gas.species_names)

    component_rows, mixture_rows = [], []
    skipped = []

    for name, composition in MIXTURES.items():
        if not set(composition) <= available:
            skipped.append((name, sorted(set(composition) - available)))
            continue

        for t in MIX_TEMPERATURES:
            gas.TPX = float(t), ct.one_atm, composition
            mu_mix, k_mix = gas.viscosity, gas.thermal_conductivity
            cp_mix = gas.cp_mass

            # Pure-species values at the SAME temperature and pressure, so the
            # mixing rule is the only thing under test.
            pure = {}
            for species, fraction in composition.items():
                gas.TPX = float(t), ct.one_atm, {species: 1.0}
                pure[species] = (gas.viscosity, gas.thermal_conductivity,
                                 gas.molecular_weights[gas.species_index(species)])

            for species, fraction in composition.items():
                mu_i, k_i, mw = pure[species]
                component_rows.append([name, t, species, f"{fraction:.6g}",
                                       f"{mw:.6g}", f"{mu_i:.7g}", f"{k_i:.7g}"])

            y = list(composition.values())
            total_y = sum(y)
            y = [v / total_y for v in y]
            k_pure = [pure[sp][1] for sp in composition]
            upper = sum(yi * ki for yi, ki in zip(y, k_pure))
            lower = 1.0 / sum(yi / ki for yi, ki in zip(y, k_pure))

            mixture_rows.append([name, t, f"{mu_mix:.7g}", f"{k_mix:.7g}",
                                 f"{cp_mix:.7g}", f"{lower:.7g}", f"{upper:.7g}"])

    for path, header, rows in [
        (OUT / "cantera-mixture-components.csv",
         ["case", "T_K", "species", "moleFraction", "molarMass_g_mol", "mu_Pas", "k_W_mK"],
         component_rows),
        (OUT / "cantera-mixture-results.csv",
         ["case", "T_K", "mu_Pas", "k_mathur_W_mK", "cp_J_kgK",
          "k_lowerBound_W_mK", "k_upperBound_W_mK"], mixture_rows),
    ]:
        with open(path, "w", newline="", encoding="utf-8") as fh:
            w = csv.writer(fh)
            w.writerow(header)
            w.writerows(rows)
        print(f"{path.name}: {len(rows)} rows")

    if skipped:
        for name, missing in skipped:
            print(f"  skipped {name}: gri30 lacks {', '.join(missing)}")

    return len(mixture_rows)


def check_mixing_formula() -> None:
    """Verify our Wilke implementation against Cantera before shipping the file.

    If this does not agree, the golden file is worthless and the F# test would
    only be reproducing a shared misunderstanding."""
    rows = list(csv.DictReader(open(OUT / "cantera-mixture-components.csv", encoding="utf-8")))
    results = {(r["case"], r["T_K"]): r
               for r in csv.DictReader(open(OUT / "cantera-mixture-results.csv", encoding="utf-8"))}

    grouped: dict[tuple[str, str], list[dict]] = {}
    for r in rows:
        grouped.setdefault((r["case"], r["T_K"]), []).append(r)

    worst_mu = worst_spread = 0.0
    outside_bounds = 0
    for key, components in grouped.items():
        y = [float(c["moleFraction"]) for c in components]
        total = sum(y)
        y = [v / total for v in y]
        m = [float(c["molarMass_g_mol"]) for c in components]
        mu = [float(c["mu_Pas"]) for c in components]
        k = [float(c["k_W_mK"]) for c in components]
        n = len(y)

        def phi(eps):
            return [[eps * (1 + math.sqrt(mu[i] / mu[j]) * (m[j] / m[i]) ** 0.25) ** 2
                     / math.sqrt(8 * (1 + m[i] / m[j])) for j in range(n)] for i in range(n)]

        # Wilke for viscosity, Wassiljewa for conductivity, both with the
        # viscosity-ratio phi_ij the design manual writes out.
        #
        # The manual names the "Mason & Saxena modification", whose epsilon
        # factor of 1.065 multiplies A_ij. Applying that on top of the
        # viscosity-ratio phi drives k_mix BELOW the rigorous lower bound in
        # roughly half of these cases, which is not admissible. The genuine
        # Mason-Saxena form uses the ratio of monatomic TRANSLATIONAL
        # conductivities, not of viscosities, so epsilon does not belong on this
        # phi. We therefore use epsilon = 1 and stay inside the bounds.
        pw = phi(1.0)
        pk = phi(1.0)
        mu_mix = sum(y[i] * mu[i] / sum(y[j] * pw[i][j] for j in range(n)) for i in range(n))
        k_mix = sum(y[i] * k[i] / sum(y[j] * pk[i][j] for j in range(n)) for i in range(n))

        ref = results[key]
        worst_mu = max(worst_mu, abs(mu_mix - float(ref["mu_Pas"])) / float(ref["mu_Pas"]))

        lower = float(ref["k_lowerBound_W_mK"])
        upper = float(ref["k_upperBound_W_mK"])
        if not lower - 1e-12 <= k_mix <= upper + 1e-12:
            outside_bounds += 1
        mathur = float(ref["k_mathur_W_mK"])
        worst_spread = max(worst_spread, abs(k_mix - mathur) / mathur)

    print(f"\nWilke viscosity vs Cantera:        worst deviation {worst_mu:.4%}")
    print(f"Mason-Saxena k within bounds:      {len(grouped) - outside_bounds}"
          f"/{len(grouped)} cases")
    print(f"Mason-Saxena vs Mathur-Tondon-Saxena: worst spread {worst_spread:.2%} "
          "(model difference, not error)")


# ----------------------------------------------------------------- IF97 grid

def write_steam() -> int:
    from iapws import IAPWS97

    rows = []

    # Region 1, compressed liquid: sub-cooled feedwater into a WHB drum.
    for t in [300, 350, 400, 450, 500, 550, 600]:
        for p in [1, 5, 20, 50, 100]:
            try:
                s = IAPWS97(T=float(t), P=float(p))
                if s.phase != "Liquid":
                    continue
                rows.append(["region1", t, p, f"{s.v:.9g}", f"{s.h:.9g}",
                             f"{s.s:.9g}", f"{s.cp:.9g}"])
            except Exception:
                continue

    # Region 2, superheated steam.
    for t in [400, 500, 600, 700, 800, 900, 1000]:
        for p in [0.01, 0.1, 1, 5, 10, 30]:
            try:
                s = IAPWS97(T=float(t), P=float(p))
                if s.phase != "Vapour":
                    continue
                rows.append(["region2", t, p, f"{s.v:.9g}", f"{s.h:.9g}",
                             f"{s.s:.9g}", f"{s.cp:.9g}"])
            except Exception:
                continue

    path = OUT / "iapws-states.csv"
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["region", "T_K", "P_MPa", "v_m3_kg", "h_kJ_kg", "s_kJ_kgK", "cp_kJ_kgK"])
        w.writerows(rows)
    print(f"{path.name}: {len(rows)} states")

    # Saturation line at the pressures a WHB drum actually runs at.
    sat_rows = []
    for p_bar in [5, 10, 20, 30, 40, 60, 80, 100, 120, 140, 160]:
        s = IAPWS97(P=p_bar / 10.0, x=0)
        v = IAPWS97(P=p_bar / 10.0, x=1)
        sat_rows.append([p_bar, f"{s.T:.9g}", f"{1/s.v:.9g}", f"{1/v.v:.9g}",
                         f"{s.h:.9g}", f"{v.h:.9g}", f"{v.h - s.h:.9g}"])

    path = OUT / "iapws-saturation.csv"
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["P_bar", "Tsat_K", "rho_l_kg_m3", "rho_v_kg_m3",
                    "h_l_kJ_kg", "h_v_kJ_kg", "hfg_kJ_kg"])
        w.writerows(sat_rows)
    print(f"{path.name}: {len(sat_rows)} pressures")

    return len(rows) + len(sat_rows)


# ---------------------------------------------------------------- liquids

# our key -> CoolProp fluid. Saturated liquid states, so the comparison is at
# the same condition the DIPPR correlations were fitted to.
LIQUIDS = {
    "H2O": "Water", "NH3": "Ammonia", "H2S": "HydrogenSulfide",
    "SO2": "SulfurDioxide", "CS2": "CarbonylSulfide",
    "CO2": "CarbonDioxide", "C2H4": "Ethylene", "C2H6": "Ethane",
    "C3H6": "Propylene", "C3H8": "n-Propane", "iC4H10": "IsoButane",
    "nC4H10": "n-Butane", "nC5H12": "n-Pentane", "nC6H14": "n-Hexane",
    "nC7H16": "n-Heptane", "nC8H18": "n-Octane", "nC10H22": "n-Decane",
    "cC6H12": "CycloHexane", "C6H6": "Benzene", "C7H8": "Toluene",
    "oC8H10": "o-Xylene", "CH3OH": "Methanol", "C2H5OH": "Ethanol",
    "CH4": "Methane",
}

# Reduced temperatures rather than absolute, so every fluid is sampled over a
# comparable part of its liquid range instead of clustering near one end.
REDUCED_TEMPERATURES = [0.45, 0.55, 0.65, 0.75, 0.85]


def write_liquids() -> int:
    import CoolProp.CoolProp as CP

    rows = []
    for key, fluid in LIQUIDS.items():
        try:
            t_crit = CP.PropsSI("Tcrit", fluid)
            t_triple = CP.PropsSI("Ttriple", fluid)
        except Exception:
            print(f"  skip {key}: not a CoolProp fluid")
            continue

        for tr in REDUCED_TEMPERATURES:
            t = tr * t_crit
            if t <= t_triple:
                continue
            try:
                rho = CP.PropsSI("D", "T", t, "Q", 0, fluid)
                mu = CP.PropsSI("viscosity", "T", t, "Q", 0, fluid)
                k = CP.PropsSI("conductivity", "T", t, "Q", 0, fluid)
                cp = CP.PropsSI("Cpmass", "T", t, "Q", 0, fluid)
                hv = (CP.PropsSI("H", "T", t, "Q", 1, fluid)
                      - CP.PropsSI("H", "T", t, "Q", 0, fluid))
            except Exception:
                continue
            rows.append([key, f"{t:.4f}", f"{tr:.2f}", f"{rho:.7g}", f"{mu:.7g}",
                         f"{k:.7g}", f"{cp:.7g}", f"{hv:.7g}"])

    path = OUT / "coolprop-liquids.csv"
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["species", "T_K", "Tr", "rho_kg_m3", "mu_Pas", "k_W_mK",
                    "cp_J_kgK", "hvap_J_kg"])
        w.writerows(rows)
    print(f"{path.name}: {len(rows)} saturated liquid states, "
          f"{len({r[0] for r in rows})} species")
    return len(rows)


if __name__ == "__main__":
    write_pure()
    write_liquids()
    write_mixing()
    check_mixing_formula()
    write_steam()
    print("\ngolden files written to tests/WhbThermo.Tests/reference/")
