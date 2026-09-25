"""
Replaces a DIPPR liquid record with a fit to a REFERENCE correlation, where the
DIPPR table was found wrong against it.

Only reference correlations qualify - CoolProp's extended-corresponding-states
models (e.g. propylene viscosity, Huber et al. 2003) do not, because they carry
an uncertainty of the same order as the discrepancy being corrected. The fit
keeps the DIPPR form and range of the record it replaces, so nothing downstream
changes but the numbers; the record is marked "override" so import_dippr.py
keeps it on a rebuild.

    python tools/fit_liquid_reference.py        (needs numpy and CoolProp)
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
LIQUIDS = ROOT / "data" / "liquid-properties.json"

# (liquid id, property) -> (CoolProp fluid, CoolProp output, reference, tMin, tMax)
OVERRIDES = {
    ("iC4H10", "liquidViscosity"): (
        "IsoButane", "viscosity", "Vogel, Kuechenmeister & Bich, Int. J. Thermophys. 21 (2000)",
        120.0, 310.95),
}
MAX_RESIDUAL = 0.01


def main() -> int:
    import CoolProp.CoolProp as CP

    doc = json.loads(LIQUIDS.read_text(encoding="utf-8"))
    species = {s["key"]: s for s in doc["species"]}
    for (key, prop), (fluid, output, reference, lo, hi) in OVERRIDES.items():
        record = species[key]["correlations"][prop]
        if record["equation"] != 101:
            raise SystemExit(f"{key}/{prop}: only DIPPR 101 is refitted here")
        ts = np.linspace(lo, hi, 60)
        values = np.array([CP.PropsSI(output, "T", t, "Q", 0, fluid) for t in ts])
        # DIPPR 101: ln Y = C1 + C2/T + C3 ln T + C4 T^C5. For a fixed exponent C5
        # the rest is linear least squares; the usual DIPPR exponents are tried
        # and the best kept.
        best = None
        for c5 in (1.0, 2.0, 3.0, 6.0, 10.0):
            x = np.column_stack([np.ones_like(ts), 1 / ts, np.log(ts), ts ** c5])
            sol, *_ = np.linalg.lstsq(x, np.log(values), rcond=None)
            res = float(np.max(np.abs(x @ sol - np.log(values))))
            if best is None or res < best[0]:
                best = (res, sol, c5)
        residual, sol, c5 = best
        coef = [float(sol[0]), float(sol[1]), float(sol[2]), float(sol[3]), c5]
        if residual > MAX_RESIDUAL:
            raise SystemExit(f"{key}/{prop}: DIPPR 101 cannot follow the reference ({residual:.3f})")
        old = record["c"]
        record.update({
            "c": coef,
            "tMinK": lo, "tMaxK": hi,
            "source": (f"FITTED to the saturated-liquid reference correlation of {reference} "
                       f"(via CoolProp) by tools/fit_liquid_reference.py; max fit residual "
                       f"{100*residual:.2f} %. Replaces a DIPPR record 18 % above that reference."),
            "override": True,
            "replacedCoefficients": old,
        })
        print(f"  {key}/{prop}: refitted, residual {100*residual:.2f} %")

    LIQUIDS.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
