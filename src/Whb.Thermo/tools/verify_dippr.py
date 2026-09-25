"""
Screens every DIPPR correlation in data/liquid-properties.json against CoolProp.

    python tools/verify_dippr.py

Only points INSIDE each correlation's own validity range are judged. Outside it
a fit is not expected to hold, and grading it there would either fail good data
or force tolerances so wide they catch nothing.

This screen is what found the n-pentane liquid viscosity record: it deviates 19
to 49 % across its whole tabulated range while n-butane and n-hexane, fitted the
same way from the same table, stay within 2 %. A defect confined to one species
across all temperatures is a bad record, not a correlation limitation.
"""
from __future__ import annotations

import csv
import json
import math
from pathlib import Path

try:
    import CoolProp.CoolProp as CP
except ImportError:
    raise SystemExit("pip install CoolProp")

ROOT = Path(__file__).resolve().parent.parent
DB_PATH = ROOT / "data" / "liquid-properties.json"

FLUIDS = {
    "H2O": "Water", "NH3": "Ammonia", "H2S": "HydrogenSulfide",
    "SO2": "SulfurDioxide", "CO2": "CarbonDioxide", "C2H4": "Ethylene",
    "C2H6": "Ethane", "C3H6": "Propylene", "C3H8": "n-Propane",
    "iC4H10": "IsoButane", "nC4H10": "n-Butane", "nC5H12": "n-Pentane",
    "nC6H14": "n-Hexane", "nC7H16": "n-Heptane", "nC8H18": "n-Octane",
    "nC10H22": "n-Decane", "cC6H12": "CycloHexane", "C6H6": "Benzene",
    "C7H8": "Toluene", "oC8H10": "o-Xylene", "CH3OH": "Methanol",
    "C2H5OH": "Ethanol", "CH4": "Methane",
}

# property -> (correlation name, CoolProp key, conversion from raw, tolerance)
PROPERTIES = {
    "rho": ("liquidMolarDensity", "D", lambda v, m: v * m / 1000.0, 0.03),
    "mu": ("liquidViscosity", "viscosity", lambda v, m: v, 0.15),
    "k": ("liquidConductivity", "conductivity", lambda v, m: v, 0.15),
    "cp": ("liquidMolarHeatCapacity", "Cpmass", lambda v, m: v / m, 0.10),
    "hvap": ("heatOfVaporisation", None, lambda v, m: v * 1000.0 / m, 0.08),
}

REDUCED = [0.45, 0.55, 0.65, 0.75, 0.85]


def evaluate(equation: int, c, t: float, tc):
    try:
        if equation == 100:
            return sum(v * t ** i for i, v in enumerate(c))
        if equation == 101:
            return math.exp(c[0] + c[1] / t + c[2] * math.log(t) + c[3] * t ** c[4])
        if equation == 102:
            return c[0] * t ** c[1] / (1 + c[2] / t + c[3] / (t * t))
        if equation == 105:
            return c[0] / c[1] ** (1 + (1 - t / c[2]) ** c[3])
        if equation == 106:
            if not tc or t >= tc:
                return None
            tr = t / tc
            return c[0] * (1 - tr) ** (c[1] + c[2] * tr + c[3] * tr ** 2)
        if equation == 114:
            if not tc or t >= tc:
                return None
            tau = 1 - t / tc
            a, b, cc, d = c[0], c[1], c[2], c[3]
            return (a * a / tau + b - 2 * a * cc * tau - a * d * tau ** 2
                    - cc * cc * tau ** 3 / 3 - cc * d * tau ** 4 / 2
                    - d * d * tau ** 5 / 5)
    except (ValueError, ZeroDivisionError, OverflowError):
        return None
    return None


def main() -> int:
    db = {s["key"]: s for s in json.loads(DB_PATH.read_text(encoding="utf-8"))["species"]}
    suspects, checked, skipped = [], 0, 0
    worst = {}

    for key, fluid in FLUIDS.items():
        species = db.get(key)
        if not species:
            continue
        molar = species["molarMass_g_mol"]
        try:
            t_crit = CP.PropsSI("Tcrit", fluid)
        except Exception:
            continue

        for label, (name, cp_key, convert, tolerance) in PROPERTIES.items():
            correlation = species["correlations"].get(name)
            if not correlation:
                continue

            deviations = []
            for tr in REDUCED:
                t = tr * t_crit
                t_min = correlation["tMinK"] or 0.0
                t_max = correlation["tMaxK"] or 1e9
                if not t_min <= t <= t_max:
                    skipped += 1
                    continue
                value = evaluate(correlation["equation"], correlation["c"], t,
                                 correlation.get("tcK") or species["tcK"])
                if value is None or value <= 0:
                    continue
                try:
                    if cp_key is None:
                        reference = (CP.PropsSI("H", "T", t, "Q", 1, fluid)
                                     - CP.PropsSI("H", "T", t, "Q", 0, fluid))
                    else:
                        reference = CP.PropsSI(cp_key, "T", t, "Q", 0, fluid)
                except Exception:
                    continue
                deviations.append((abs(convert(value, molar) - reference) / reference, tr))
                checked += 1

            if not deviations:
                continue
            peak, peak_tr = max(deviations)
            if peak > worst.get(label, (0.0, "", 0.0))[0]:
                worst[label] = (peak, key, peak_tr)

            # A defect across the WHOLE range is a bad record; a defect only at
            # the top of the range is the correlation running out of validity.
            over = [d for d, _ in deviations if d > tolerance]
            if len(over) >= max(2, len(deviations) - 1):
                suspects.append((key, label, min(d for d, _ in deviations),
                                 peak, len(deviations)))

    print(f"{checked} comparisons, {skipped} points skipped as outside the fit range\n")
    print("worst deviation per property:")
    for label, (peak, key, tr) in sorted(worst.items()):
        print(f"  {label:5s} {peak:7.1%}  {key} at Tr = {tr}")

    if suspects:
        print(f"\nSUSPECT RECORDS ({len(suspects)}) - deviating across the whole range:")
        for key, label, low, high, count in suspects:
            print(f"  {key:8s} {label:5s}  {low:.1%} to {high:.1%} over {count} points")
        print("  A species that is wrong everywhere while its homologues are right")
        print("  is a bad table entry, not a correlation limitation.")
        return 1

    print("\nno suspect records")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
