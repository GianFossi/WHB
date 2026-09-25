"""
Replaces the legacy Sutherland transport of selected species with a Chung-Lee-
Starling estimate, fitted to the NASA CEA four-parameter form

    ln(phi) = A ln T + B/T + C/T^2 + D      (micropoise, microwatt/(cm K))

so the loader and every consumer read it exactly like CEA data.

Why: the manual's two-parameter Sutherland fits for C3H8, C3H6, C6H6 and C7H8
put the gas conductivity 20-65 % low at WHB temperatures. Three independent
sources agree on that - CoolProp inside its range, Cantera (GRI-Mech 3.0
kinetic theory) and Chung - so the old data, not the references, is wrong.

The result is an ESTIMATE (quality C) and says so in every interval's source.
It is checked here, before writing, against CoolProp below each fluid's EOS
Tmax (the only range where CoolProp is a reference) and, where the species
exists in GRI-Mech 3.0, against Cantera up to 1600 K. The measured deviations
go into dataQuality.validationNote.

Chung is evaluated exactly as src/WhbThermo.Properties/ChungEstimate.fs does
(non-polar form; the dipoles here, <= 0.4 D, change mu_r^4 terms by < 1e-4).

Needs numpy, CoolProp and cantera:
    python tools/fit_transport_chung.py
"""
from __future__ import annotations

import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import db_guard  # noqa: E402

R = 8.31446261815324
SPECIES = {"C3H8": "n-Propane", "C3H6": "Propylene", "C6H6": "Benzene", "C7H8": "Toluene"}
INTERVALS = [(300.0, 1000.0), (1000.0, 3000.0)]
MAX_FIT_RESIDUAL = 0.005        # 0.5 % in ln space is ~0.5 % in the property
SOURCE = ("ESTIMATE: Chung-Lee-Starling (1988) low-pressure transport from Tc, Vc and "
          "omega of this database, fitted to the NASA CEA form by tools/fit_transport_chung.py")


def nasa9_cp(sp: dict, t: float) -> float:
    for g in sp["cpModel"]["nasa9Segments"]:
        if g["tMinK"] <= t <= g["tMaxK"]:
            a = g["a"]
            return R * (a[0] / t**2 + a[1] / t + a[2] + a[3] * t + a[4] * t**2
                        + a[5] * t**3 + a[6] * t**4)
    raise ValueError(f"{sp['key']}: no NASA-9 segment at {t} K")


def chung(sp: dict, t: float) -> tuple[float, float]:
    """(mu [Pa s], k [W/(m K)]) as ChungEstimate.fs computes them."""
    c = sp["critical"]
    tc, vc, w, m = c["tcK"], c["vcCm3Mol"], c["acentric"], sp["molarMass_kg_kmol"]
    ts = 1.2593 * t / tc
    omega = (1.16145 * ts ** -0.14874 + 0.52487 * math.exp(-0.7732 * ts)
             + 2.16178 * math.exp(-2.43787 * ts))
    mu = 40.785 * (1 - 0.2756 * w) * math.sqrt(m * t) / (vc ** (2 / 3) * omega) * 1e-7
    alpha = (nasa9_cp(sp, t) - R) / R - 1.5
    beta = 0.7862 - 0.7109 * w + 1.3168 * w * w
    z = 2.0 + 10.5 * (t / tc) ** 2
    psi = 1 + alpha * ((0.215 + 0.28288 * alpha - 1.061 * beta + 0.26665 * z)
                       / (0.6366 + beta * z + 1.061 * alpha * beta))
    return mu, 3.75 * psi * mu * R / (m / 1000)


def fit(ts: np.ndarray, values: np.ndarray) -> tuple[np.ndarray, float]:
    """Least squares on ln(phi) = A ln T + B/T + C/T^2 + D; returns (A, B, C, D), max |residual|."""
    x = np.column_stack([np.log(ts), 1 / ts, 1 / ts**2, np.ones_like(ts)])
    y = np.log(values)
    coef, *_ = np.linalg.lstsq(x, y, rcond=None)
    return coef, float(np.max(np.abs(x @ coef - y)))


def evaluate(intervals: list[dict], t: float) -> float:
    iv = next(i for i in intervals if i["tMinK"] <= t <= i["tMaxK"])
    return math.exp(iv["a"] * math.log(t) + iv["b"] / t + iv["c"] / t**2 + iv["d"])


def main() -> int:
    import CoolProp.CoolProp as CP
    import cantera as ct

    gas = ct.Solution("gri30.yaml")
    gas.transport_model = "mixture-averaged"

    db = db_guard.load()
    records = {s["key"]: s for s in db["species"]}
    for key, fluid in SPECIES.items():
        sp = records[key]
        blocks = {"viscosity": [], "conductivity": []}
        for lo, hi in INTERVALS:
            ts = np.linspace(lo, hi, 60)
            mu = np.array([chung(sp, t)[0] for t in ts]) * 1e7      # micropoise
            k = np.array([chung(sp, t)[1] for t in ts]) * 1e4       # microwatt/(cm K)
            for name, values in (("viscosity", mu), ("conductivity", k)):
                coef, residual = fit(ts, values)
                if residual > MAX_FIT_RESIDUAL:
                    raise SystemExit(f"{key} {name} {lo}-{hi} K: fit residual {residual:.4f}")
                blocks[name].append({"tMinK": lo, "tMaxK": hi, "a": float(coef[0]),
                                     "b": float(coef[1]), "c": float(coef[2]),
                                     "d": float(coef[3]), "source": SOURCE})

        # Checks against the references, where they are references.
        tmax = CP.PropsSI("Tmax", fluid)
        dev_k, dev_mu = [], []
        for t in np.arange(400.0, tmax + 1.0, 25.0):
            k_cp = CP.PropsSI("conductivity", "T", t, "P", 1e5, fluid)
            mu_cp = CP.PropsSI("viscosity", "T", t, "P", 1e5, fluid)
            dev_k.append(evaluate(blocks["conductivity"], t) * 1e-4 / k_cp - 1)
            dev_mu.append(evaluate(blocks["viscosity"], t) * 1e-7 / mu_cp - 1)
        note = (f"Chung estimate vs CoolProp 400-{tmax:.0f} K (its EOS Tmax): "
                f"k {100*min(dev_k):+.1f} to {100*max(dev_k):+.1f} %, "
                f"mu {100*min(dev_mu):+.1f} to {100*max(dev_mu):+.1f} %")
        if key in gas.species_names:
            dev_ct = []
            for t in np.arange(400.0, 1601.0, 100.0):
                gas.TPX = t, 1e5, {key: 1.0}
                dev_ct.append(evaluate(blocks["conductivity"], t) * 1e-4 / gas.thermal_conductivity - 1)
            note += (f"; vs Cantera GRI-Mech 3.0 400-1600 K: k {100*min(dev_ct):+.1f} to "
                     f"{100*max(dev_ct):+.1f} %")
        worst = max(abs(d) for d in dev_k + dev_mu)
        if worst > 0.10:
            raise SystemExit(f"{key}: estimate off CoolProp by {100*worst:.1f} % inside its range")

        sp["transport"] = {"kind": "nasaCea", "viscosity": blocks["viscosity"],
                           "conductivity": blocks["conductivity"]}
        quality = sp.setdefault("dataQuality", {})
        quality["transport"] = "C"
        quality["validation"] = "verified"
        quality["validationNote"] = note
        print(f"  {key}: {note}")

    db_guard.save(db, tool="fit_transport_chung.py",
                  note="C3H8, C3H6, C6H6, C7H8 transport: Chung estimate replaces legacy Sutherland")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
