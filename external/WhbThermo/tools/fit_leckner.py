"""
Fits the Leckner-FORM polynomial coefficients used by WhbThermo.Radiation.

    python tools/fit_leckner.py                    # calibrate against Smith WSGG
    python tools/fit_leckner.py --csv ref.csv      # calibrate against your own data

WHY THIS EXISTS
---------------
The original Leckner (1972) coefficient tables are behind a paywalled journal and
are not reproduced in any open source we could verify. Rather than transcribe
them from memory -- which would be exactly the failure mode this repository is
built to prevent -- we fit the SAME functional form to a reference dataset and
label the result honestly.

    log10(eps_i) = SUM_i a_i (log10 pL)^i        a_i = SUM_j c[j][i] (T/1000)^j
    delta_eps    = zeta (1 - zeta) * SUM_j SUM_m [ d[j][m] + e[j][m] zeta ]
                                      * theta^j (log10 pL_tot)^m
                   with zeta = p_H2O / (p_H2O + p_CO2)

The zeta(1-zeta) prefactor enforces the physical boundary condition exactly:
band overlap vanishes when either species is absent. Without it the fit has to
learn those two zeros from data and never quite does. The second matrix e adds
the zeta asymmetry -- overlap is NOT symmetric about zeta = 0.5, and a symmetric
prefactor alone leaves large errors at CO2-rich compositions.

Because log10(eps) is LINEAR in c, the fit is an ordinary least-squares problem,
not an iterative optimisation: there are no convergence or local-minimum issues.

REFERENCE DATA
--------------
Default: the Smith, Shen & Friedman (1982) WSGG model already validated in this
repository. The resulting coefficients are therefore a Leckner-form REPARA-
METRISATION of Smith WSGG -- useful when a calculation must be presented in the
chart-correlation format, but carrying no independent accuracy.

Better: pass --csv with columns species,T_K,pL_atm_m,emissivity from a
line-by-line or SNB source. A suitable open dataset is the EM2C SNB total
emissivity set (300-2900 K, pL 0.01-50 atm*m) published in Data in Brief;
fit against that and the coefficients become genuinely independent.

Whatever the source, it is recorded in the `source` field of the JSON so the
provenance travels with the numbers.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
MODEL_PATH = ROOT / "src" / "WhbThermo.Radiation" / "radiation-models.json"
WSGG_PATH = ROOT / "src" / "WhbThermo.Radiation" / "wsgg-smith-1982.json"

ATM_PER_BAR = 0.9869232667160128
N_T = 3    # highest power of (T/1000)
M_PL = 3   # highest power of log10(pL)

# Fit envelope: WHB service, 250-1400 degC, short tube beam lengths upward.
T_GRID = [600.0, 700.0, 800.0, 900.0, 1000.0, 1200.0, 1400.0, 1600.0, 1800.0, 2000.0, 2200.0]
PL_GRID = [0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0]


# ---------------------------------------------------------------- WSGG oracle

def _load_wsgg():
    doc = json.loads(WSGG_PATH.read_text(encoding="utf-8"))
    return {s["id"]: s for s in doc["sets"]}


def _interp(a, b, d):
    return {"k": [ka + d * (kb - ka) for ka, kb in zip(a["k"], b["k"])],
            "b": [[x + d * (y - x) for x, y in zip(ra, rb)]
                  for ra, rb in zip(a["b"], b["b"])]}


def _select(sets, rr, pw_atm):
    unity = sets["h2oPure"] if pw_atm > 0.5 else sets["h2oOnly"]
    if rr <= 0.5:
        return _interp(sets["co2Only"], sets["ratio1"], rr / 0.5)
    if rr <= 2 / 3:
        return _interp(sets["ratio1"], sets["ratio2"], (rr - 0.5) / (2 / 3 - 0.5))
    return _interp(sets["ratio2"], unity, (rr - 2 / 3) / (1 - 2 / 3))


def wsgg_emissivity(sets, t, pw_atm, pc_atm, le):
    p = pw_atm + pc_atm
    if p <= 0:
        return 0.0
    cset = _select(sets, pw_atm / p, pw_atm)
    pl = p * le
    return sum((sum(b[j] * t**j for j in range(4))) * (1 - math.exp(-k * pl))
               for k, b in zip(cset["k"], cset["b"]))


# ------------------------------------------------------------------- fitting

def design_row(t_k: float, pl: float) -> np.ndarray:
    """One row of the design matrix: theta^j * (log10 pL)^i, flattened c[j][i]."""
    theta = t_k / 1000.0
    log_pl = math.log10(pl)
    return np.array([theta**j * log_pl**i
                     for j in range(N_T + 1)
                     for i in range(M_PL + 1)])


def fit_species(samples: list[tuple[float, float, float]]) -> tuple[list[list[float]], dict]:
    """samples = [(T_K, pL, emissivity)]. Returns (c matrix, fit statistics)."""
    usable = [(t, pl, e) for t, pl, e in samples if e > 1e-8]
    if len(usable) < (N_T + 1) * (M_PL + 1):
        raise SystemExit(f"need at least {(N_T+1)*(M_PL+1)} samples, got {len(usable)}")

    a = np.vstack([design_row(t, pl) for t, pl, _ in usable])
    y = np.array([math.log10(e) for _, _, e in usable])

    coefficients, *_ = np.linalg.lstsq(a, y, rcond=None)

    predicted = 10 ** (a @ coefficients)
    actual = np.array([e for _, _, e in usable])
    relative = np.abs(predicted - actual) / actual

    c = coefficients.reshape(N_T + 1, M_PL + 1)
    stats = {"points": len(usable),
             "meanRelError": float(relative.mean()),
             "maxRelError": float(relative.max())}
    return [[float(v) for v in row] for row in c], stats


def fit_overlap(samples: list[tuple[float, float, float, float]]) -> tuple[list[list[float]], dict]:
    """samples = [(T_K, zeta, pL_total, delta_eps)] -> d matrix.

    delta_eps = zeta(1-zeta) * SUM_j SUM_m d[j][m] theta^j (log10 pL)^m

    Linear in d, so ordinary least squares. The zeta(1-zeta) prefactor is applied
    to the DESIGN matrix rather than dividing the target, which keeps the system
    well conditioned near zeta = 0 and zeta = 1."""
    rows, targets = [], []
    for t, zeta, pl, delta in samples:
        theta = t / 1000.0
        log_pl = math.log10(pl)
        prefactor = zeta * (1.0 - zeta)
        basis = [theta**j * log_pl**m
                 for j in range(N_T + 1) for m in range(M_PL + 1)]
        rows.append([prefactor * b for b in basis]
                    + [prefactor * zeta * b for b in basis])
        targets.append(delta)

    a = np.array(rows)
    y = np.array(targets)
    coefficients, *_ = np.linalg.lstsq(a, y, rcond=None)

    residual = np.abs(a @ coefficients - y)
    # Split by regime: in-tube WHB beam lengths give pL well under 1 atm*m,
    # so accuracy there matters far more than in the furnace-scale tail.
    tube = np.array([pl <= 1.0 for _, _, pl, _ in samples])
    half = (N_T + 1) * (M_PL + 1)
    d = coefficients[:half].reshape(N_T + 1, M_PL + 1)
    e = coefficients[half:].reshape(N_T + 1, M_PL + 1)
    stats = {"points": len(targets),
             "maxAbsResidual": float(residual.max()),
             "meanAbsResidual": float(residual.mean()),
             "maxAbsResidual_pL_below_1": float(residual[tube].max()),
             "meanAbsResidual_pL_below_1": float(residual[tube].mean())}
    return ([[float(v) for v in row] for row in d],
            [[float(v) for v in row] for row in e]), stats


# ---------------------------------------------------------------------- main

def samples_from_wsgg():
    sets = _load_wsgg()
    water, dioxide, overlap = [], [], []

    for t in T_GRID:
        for pl in PL_GRID:
            water.append((t, pl, wsgg_emissivity(sets, t, pl, 0.0, 1.0)))
            dioxide.append((t, pl, wsgg_emissivity(sets, t, 0.0, pl, 1.0)))

    for t in T_GRID:
        for zeta in (0.2, 0.35, 0.5, 0.65, 0.8):
            for pl in PL_GRID:
                pw, pc = pl * zeta, pl * (1 - zeta)
                ew = wsgg_emissivity(sets, t, pw, 0.0, 1.0)
                ec = wsgg_emissivity(sets, t, 0.0, pc, 1.0)
                em = wsgg_emissivity(sets, t, pw, pc, 1.0)
                overlap.append((t, zeta, pl, ew + ec - em))

    return water, dioxide, overlap, (
        "FITTED to Smith, Shen & Friedman (1982) WSGG by tools/fit_leckner.py. "
        "Leckner FORM, not Leckner's published coefficients - carries no accuracy "
        "independent of the WSGG model it was calibrated against.")


def samples_from_csv(path: str):
    water, dioxide = [], []
    with open(path, newline="", encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            entry = (float(row["T_K"]), float(row["pL_atm_m"]), float(row["emissivity"]))
            (water if row["species"].upper() == "H2O" else dioxide).append(entry)
    if not water or not dioxide:
        raise SystemExit("CSV must contain both H2O and CO2 rows")
    return water, dioxide, [], f"FITTED by tools/fit_leckner.py to {Path(path).name}"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--csv", help="reference data: species,T_K,pL_atm_m,emissivity")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if args.csv:
        water, dioxide, overlap, source = samples_from_csv(args.csv)
    else:
        water, dioxide, overlap, source = samples_from_wsgg()

    c_water, s_water = fit_species(water)
    c_dioxide, s_dioxide = fit_species(dioxide)

    print(f"H2O: {s_water['points']} points, mean error {s_water['meanRelError']:.2%}, "
          f"max {s_water['maxRelError']:.2%}")
    print(f"CO2: {s_dioxide['points']} points, mean error {s_dioxide['meanRelError']:.2%}, "
          f"max {s_dioxide['maxRelError']:.2%}")

    if overlap:
        (d_overlap, e_overlap), s_overlap = fit_overlap(overlap)
        print(f"overlap: {s_overlap['points']} points, "
              f"mean |residual| {s_overlap['meanAbsResidual']:.5f}, "
              f"max {s_overlap['maxAbsResidual']:.5f} (absolute emissivity units); "
              f"for pL <= 1 atm*m: mean {s_overlap['meanAbsResidual_pL_below_1']:.5f}, "
              f"max {s_overlap['maxAbsResidual_pL_below_1']:.5f}")
    else:
        d_overlap, e_overlap = [], []
        print("overlap: no data in CSV, matrices left empty")

    doc = json.loads(MODEL_PATH.read_text(encoding="utf-8"))
    doc["status"] = "POPULATED by tools/fit_leckner.py -- read the source field"
    doc["h2o"]["c"] = c_water
    doc["h2o"]["source"] = source
    doc["co2"]["c"] = c_dioxide
    doc["co2"]["source"] = source
    doc["overlap"]["c"] = d_overlap
    doc["overlap"]["cAsym"] = e_overlap
    doc["overlap"]["source"] = source
    doc["fitStatistics"] = {"h2o": s_water, "co2": s_dioxide,
                            "overlap": s_overlap if overlap else None}

    if args.dry_run:
        print("\ndry run, file not written")
        return 0

    MODEL_PATH.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    print(f"\n{MODEL_PATH} written")
    print("Now run: python tools/validate_leckner.py")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
