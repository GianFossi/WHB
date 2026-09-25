"""
Verifies the critical properties in the species database against CoolProp.

    python tools/verify_critical.py

These constants were originally entered with a citation to Poling, *Properties
of Gases and Liquids*, 5th ed., App. A. That citation asserted a verification
that had not been performed. This script performs it, and the database now
records which entries are actually verified and which are not.

Critical properties are unused today -- everything runs ideal-gas -- but they
are the input a cubic or SAFT equation of state would need, so a wrong value
would surface only once real-gas behaviour is switched on for the high-pressure
services (ammonia synloop at 130-250 bar).
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

try:
    import CoolProp.CoolProp as CP
except ImportError:
    raise SystemExit("pip install CoolProp")

ROOT = Path(__file__).resolve().parent.parent
DB_PATH = ROOT / "data" / "species-database.json"

FLUIDS = {
    "H2": "Hydrogen", "N2": "Nitrogen", "CH4": "Methane", "CO": "CarbonMonoxide",
    "CO2": "CarbonDioxide", "H2O": "Water", "NH3": "Ammonia", "Ar": "Argon",
    "He": "Helium", "H2S": "HydrogenSulfide", "SO2": "SulfurDioxide",
    "C2H4": "Ethylene", "N2O": "NitrousOxide", "C2H6": "Ethane",
    "C3H8": "n-Propane", "C3H6": "Propylene", "C6H6": "Benzene", "C7H8": "Toluene",
}

TOLERANCE_T = 0.01
TOLERANCE_P = 0.01
TOLERANCE_W = 0.02


def main() -> int:
    db = json.loads(DB_PATH.read_text(encoding="utf-8"))
    failures, unverifiable, checked = [], [], 0

    print(f"{'spec':6s}{'Tc':>9s}{'ref':>9s}{'dev':>8s}"
          f"{'Pc':>9s}{'ref':>9s}{'dev':>8s}{'omega':>8s}{'ref':>8s}")

    for species in db["species"]:
        critical = species.get("critical")
        if not critical:
            continue
        key = species["key"]
        if key not in FLUIDS:
            unverifiable.append(key)
            continue

        fluid = FLUIDS[key]
        tc = CP.PropsSI("Tcrit", fluid)
        pc = CP.PropsSI("pcrit", fluid) / 1e5
        omega = CP.PropsSI("acentric", fluid)

        dt = (critical["tcK"] - tc) / tc
        dp = (critical["pcBar"] - pc) / pc
        dw = critical["acentric"] - omega
        checked += 1

        bad = abs(dt) > TOLERANCE_T or abs(dp) > TOLERANCE_P or abs(dw) > TOLERANCE_W
        print(f"{key:6s}{critical['tcK']:9.2f}{tc:9.2f}{dt:+8.2%}"
              f"{critical['pcBar']:9.2f}{pc:9.2f}{dp:+8.2%}"
              f"{critical['acentric']:8.3f}{omega:8.3f}{'  <-- FAIL' if bad else ''}")
        if bad:
            failures.append(key)

    print(f"\n{checked} verified against CoolProp")
    if unverifiable:
        print(f"no reference fluid, still UNVERIFIED ({len(unverifiable)}): "
              f"{', '.join(unverifiable)}")
    missing = [s["key"] for s in db["species"] if not s.get("critical")]
    if missing:
        print(f"no critical properties at all ({len(missing)}): {', '.join(missing)}")

    if failures:
        print(f"\n{len(failures)} outside tolerance: {', '.join(failures)}")
        return 1
    print("\nall checkable entries agree")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
