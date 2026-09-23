"""
Validates the Smith et al. (1982) WSGG coefficient set in wsgg-smith-1982.json.

    python tools/validate_wsgg.py

Checks, in order of how likely each is to catch a transcription error:

  1. GRAY-GAS WEIGHTS -- a_i >= 0 and the clear-gas weight a_0 = 1 - SUM(a_i)
     within [0,1] across 600-2400 K. A single mistyped digit in the b matrix
     drives a weight negative or the clear-gas weight out of range almost
     immediately, because the polynomials are cubic and nearly cancelling.
  2. MONOTONICITY -- emissivity rises with p*L at fixed T.
  3. CONTINUITY -- no jump in emissivity across the RR = 1/2 and RR = 2/3
     interpolation boundaries.
  4. MAGNITUDE -- spot values against the classical Hottel chart order.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODEL_PATH = ROOT / "src" / "WhbThermo.Radiation" / "wsgg-smith-1982.json"
ATM_PER_BAR = 0.9869232667160128

model = json.loads(MODEL_PATH.read_text(encoding="utf-8"))
SETS = {s["id"]: s for s in model["sets"]}


def weights(cset, t):
    return [sum(b[j] * t**j for j in range(4)) for b in cset["b"]]


def _interp(a, b, d):
    return {"id": f"{a['id']}~{b['id']}",
            "k": [ka + d * (kb - ka) for ka, kb in zip(a["k"], b["k"])],
            "b": [[x + d * (y - x) for x, y in zip(ra, rb)]
                  for ra, rb in zip(a["b"], b["b"])]}


def select(rr, pw_atm):
    unity = SETS["h2oPure"] if pw_atm > 0.5 else SETS["h2oOnly"]
    if rr <= 0.5:
        return _interp(SETS["co2Only"], SETS["ratio1"], rr / 0.5)
    if rr <= 2 / 3:
        return _interp(SETS["ratio1"], SETS["ratio2"], (rr - 0.5) / (2 / 3 - 0.5))
    return _interp(SETS["ratio2"], unity, (rr - 2 / 3) / (1 - 2 / 3))


def emissivity(t, pw_bar, pc_bar, le):
    pw, pc = pw_bar * ATM_PER_BAR, pc_bar * ATM_PER_BAR
    p = pw + pc
    if p <= 0:
        return 0.0
    cset = select(pw / p, pw)
    pl = p * le
    return sum(a * (1 - math.exp(-k * pl))
               for a, k in zip(weights(cset, t), cset["k"]))


def main() -> int:
    errors = []

    print("1. gray-gas weights over 600-2400 K")
    for sid, cset in SETS.items():
        worst_clear = 1.0
        for t in range(600, 2401, 100):
            a = weights(cset, float(t))
            clear = 1.0 - sum(a)
            worst_clear = min(worst_clear, clear)
            for i, ai in enumerate(a):
                if ai < -1e-3:
                    errors.append(f"{sid} @ {t} K: gray gas {i+1} weight = {ai:.4f} < 0")
            if not (-1e-3 <= clear <= 1 + 1e-3):
                errors.append(f"{sid} @ {t} K: clear-gas weight = {clear:.4f}")
        print(f"   {sid:10s} min clear-gas weight {worst_clear:6.3f}")

    print("2. monotonicity in p*L")
    for t in [600.0, 1200.0, 1800.0, 2400.0]:
        vals = [emissivity(t, 0.2, 0.1, le) for le in (0.001, 0.01, 0.1, 1.0, 5.0)]
        if any(b <= a for a, b in zip(vals, vals[1:])):
            errors.append(f"emissivity not monotonic in p*L at {t} K: {vals}")
    print("   ok" if not errors else "   see below")

    print("3. continuity across RR boundaries")
    for boundary in (0.5, 2 / 3):
        lo = emissivity(1200.0, 0.3 * (boundary - 1e-4), 0.3 * (1 - boundary + 1e-4), 1.0)
        hi = emissivity(1200.0, 0.3 * (boundary + 1e-4), 0.3 * (1 - boundary - 1e-4), 1.0)
        jump = abs(hi - lo)
        print(f"   RR = {boundary:.4f}: jump {jump:.2e}")
        if jump > 1e-3:
            errors.append(f"discontinuity at RR = {boundary:.4f}: {jump:.4f}")

    print("4. magnitude spot checks")
    for t, pw, pc, le, lo, hi in [
        (1200.0, 0.152, 0.152, 1.44, 0.25, 0.38),
        (1200.0, 0.0, 0.3, 1.0, 0.08, 0.20),
        (1673.15, 0.42, 0.075, 0.0475, 0.02, 0.08),
    ]:
        e = emissivity(t, pw, pc, le)
        status = "ok " if lo <= e <= hi else "FAIL"
        print(f"   [{status}] T={t:7.1f} K pw={pw:5.3f} pc={pc:5.3f} L={le:6.4f} m "
              f"-> eps = {e:.4f} (expected {lo}-{hi})")
        if not lo <= e <= hi:
            errors.append(f"emissivity {e:.4f} outside expected band {lo}-{hi}")

    print()
    if errors:
        print(f"{len(errors)} problem(s):")
        for e in errors[:20]:
            print(f"  - {e}")
        return 1
    print("all checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
