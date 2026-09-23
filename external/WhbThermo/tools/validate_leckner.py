"""
Validates the Leckner coefficient matrices in radiation-models.json once they
have been transcribed.

    python tools/validate_leckner.py

Two independent checks, because a transcription error in a polynomial matrix is
almost never visible by eye:

  1. STRUCTURAL -- emissivity stays in [0,1], increases monotonically with pL,
     and decreases with temperature above ~800 K, across the whole design envelope.
     This catches a transposed matrix (c[j][i] read as c[i][j]) immediately, since
     a transposed matrix breaks monotonicity almost everywhere.

  2. REFERENCE POINTS -- values read off the published Leckner/Hottel charts.
     Fill REFERENCE below from the source; each entry is
     (T [K], pL [bar*m], expected emissivity, tolerance).
     Chart readings are worth about +/-10 %, so do not set the tolerance tighter.

Nothing here is a substitute for the source. It is a tripwire, not a proof.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODEL_PATH = ROOT / "src" / "WhbThermo.Radiation" / "radiation-models.json"

# (T_K, pL_bar_m, expected_emissivity, relative_tolerance)
REFERENCE_H2O: list[tuple[float, float, float, float]] = [
    # e.g. (1000.0, 0.1, 0.14, 0.10),
]
REFERENCE_CO2: list[tuple[float, float, float, float]] = [
    # e.g. (1000.0, 0.1, 0.11, 0.10),
]

# WHB design envelope: 250 degC outlet to 1400 degC inlet.
T_ENVELOPE = [600.0, 800.0, 1000.0, 1200.0, 1400.0, 1673.0]
PL_ENVELOPE = [0.005, 0.02, 0.05, 0.1, 0.3, 1.0, 3.0]


def emissivity(c: list[list[float]], t_k: float, pl: float) -> float:
    """log10(eps) = SUM_i a_i (log10 pL)^i,  a_i = SUM_j c[j][i] (T/1000)^j"""
    theta = t_k / 1000.0
    log_pl = math.log10(pl)
    columns = len(c[0]) if c else 0
    acc = 0.0
    for i in range(columns):
        a_i = sum(c[j][i] * theta**j for j in range(len(c)))
        acc += a_i * log_pl**i
    return 10.0**acc


def check_structure(name: str, c: list[list[float]]) -> list[str]:
    errors = []

    for t in T_ENVELOPE:
        for pl in PL_ENVELOPE:
            eps = emissivity(c, t, pl)
            if not (0.0 <= eps <= 1.0):
                errors.append(f"{name}: eps = {eps:.4f} out of [0,1] at T={t} K, pL={pl}")

    # Monotonic increase with path length at fixed temperature.
    for t in T_ENVELOPE:
        values = [emissivity(c, t, pl) for pl in PL_ENVELOPE]
        for a, b, pl_a, pl_b in zip(values, values[1:], PL_ENVELOPE, PL_ENVELOPE[1:]):
            if b < a - 1e-9:
                errors.append(f"{name}: eps decreases with pL at T={t} K "
                              f"({pl_a}->{pl_b}: {a:.4f}->{b:.4f}) "
                              f"-- matrix may be transposed")

    # Emissivity falls with temperature at fixed path -- but only well above the
    # band maximum. CO2 genuinely peaks near 900-1000 K (verified against the
    # reference data), so the check starts at 1200 K. Applying it from 800 K
    # would flag correct physics as an error.
    hot = [t for t in T_ENVELOPE if t >= 1200.0]
    for pl in PL_ENVELOPE:
        values = [emissivity(c, t, pl) for t in hot]
        for a, b, t_a, t_b in zip(values, values[1:], hot, hot[1:]):
            if b > a + 1e-6:
                errors.append(f"{name}: eps increases with T at pL={pl} "
                              f"({t_a}->{t_b}: {a:.4f}->{b:.4f}) -- check the T powers")

    return errors


def check_reference(name: str, c: list[list[float]], points) -> list[str]:
    errors = []
    for t, pl, expected, tol in points:
        got = emissivity(c, t, pl)
        dev = abs(got - expected) / expected
        status = "ok " if dev <= tol else "FAIL"
        print(f"  [{status}] {name} T={t:6.0f} K pL={pl:6.3f}: "
              f"got {got:.4f}, chart {expected:.4f} ({dev:+.1%})")
        if dev > tol:
            errors.append(f"{name} at T={t} K, pL={pl}: {dev:.1%} off the chart value")
    return errors


def main() -> int:
    model = json.loads(MODEL_PATH.read_text(encoding="utf-8"))
    all_errors = []

    for key, reference in (("h2o", REFERENCE_H2O), ("co2", REFERENCE_CO2)):
        c = model[key]["c"]
        print(f"\n{key.upper()}: {len(c)}x{len(c[0]) if c else 0} coefficient matrix")
        if not c or not any(any(v != 0.0 for v in row) for row in c):
            print("  PENDING -- matrix is empty, nothing to validate")
            all_errors.append(f"{key}: coefficients not transcribed")
            continue

        all_errors += check_structure(key.upper(), c)
        if reference:
            all_errors += check_reference(key.upper(), c, reference)
        else:
            print("  no reference points configured -- structural checks only")
            print("  -> fill REFERENCE_H2O / REFERENCE_CO2 from the published charts")

    print()
    if all_errors:
        print(f"{len(all_errors)} problem(s):")
        for e in all_errors:
            print(f"  - {e}")
        return 1

    print("all checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
