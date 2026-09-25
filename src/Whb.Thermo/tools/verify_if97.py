"""
Verifies data/iapws-if97.json against the official verification tables of
IAPWS-IF97 (Revised Release, August 2007).

    python tools/verify_if97.py

This is the check the Leckner correlation could never have: IF97 ships with
published verification values, so an implementation either reproduces them to
the printed digits or it is wrong. No judgement calls, no chart reading.

The implementation below reads coefficients ONLY from the JSON, so it validates
the data file rather than a hard-coded copy.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "data" / "iapws-if97.json"

M = json.loads(DATA.read_text(encoding="utf-8"))
R = M["constants"]["R_kJ_kgK"]


# ------------------------------------------------------------------ region 1

def region1(t: float, p: float) -> dict:
    r = M["region1"]
    pi = p / r["piStar_MPa"]
    tau = r["tauStar_K"] / t
    I, J, n = r["I"], r["J"], r["n"]

    g = gp = gt = gpp = gtt = gpt = 0.0
    for i, j, c in zip(I, J, n):
        g += c * (7.1 - pi) ** i * (tau - 1.222) ** j
        gp -= c * i * (7.1 - pi) ** (i - 1) * (tau - 1.222) ** j
        gt += c * (7.1 - pi) ** i * j * (tau - 1.222) ** (j - 1)
        gpp += c * i * (i - 1) * (7.1 - pi) ** (i - 2) * (tau - 1.222) ** j
        gtt += c * (7.1 - pi) ** i * j * (j - 1) * (tau - 1.222) ** (j - 2)
        gpt -= c * i * (7.1 - pi) ** (i - 1) * j * (tau - 1.222) ** (j - 1)

    return {"v": pi * gp * R * t / p / 1000,
            "h": tau * gt * R * t,
            "s": R * (tau * gt - g),
            "cp": -R * tau ** 2 * gtt,
            "cv": R * (-tau ** 2 * gtt + (gp - tau * gpt) ** 2 / gpp)}


# ------------------------------------------------------------------ region 2

def region2(t: float, p: float) -> dict:
    r = M["region2"]
    pi = p / r["piStar_MPa"]
    tau = r["tauStar_K"] / t

    Jo, no = r["ideal"]["J"], r["ideal"]["n"]
    go = math.log(pi)
    got = gott = 0.0
    for j, c in zip(Jo, no):
        go += c * tau ** j
        got += c * j * tau ** (j - 1)
        gott += c * j * (j - 1) * tau ** (j - 2)
    gop = 1 / pi
    gopp = -1 / pi ** 2

    I, J, n = r["residual"]["I"], r["residual"]["J"], r["residual"]["n"]
    gr = grp = grt = grpp = grtt = grpt = 0.0
    for i, j, c in zip(I, J, n):
        gr += c * pi ** i * (tau - 0.5) ** j
        grp += c * i * pi ** (i - 1) * (tau - 0.5) ** j
        grt += c * pi ** i * j * (tau - 0.5) ** (j - 1)
        grpp += c * i * (i - 1) * pi ** (i - 2) * (tau - 0.5) ** j
        grtt += c * pi ** i * j * (j - 1) * (tau - 0.5) ** (j - 2)
        grpt += c * i * pi ** (i - 1) * j * (tau - 0.5) ** (j - 1)

    return {"v": pi * (gop + grp) * R * t / p / 1000,
            "h": tau * (got + grt) * R * t,
            "s": R * (tau * (got + grt) - (go + gr)),
            "cp": -R * tau ** 2 * (gott + grtt),
            "cv": R * (-tau ** 2 * (gott + grtt)
                       - (1 + pi * grp - tau * pi * grpt) ** 2
                       / (1 - pi ** 2 * grpp))}


# ------------------------------------------------------------------ region 4

def psat(t: float) -> float:
    n = M["region4"]["n"]
    theta = t + n[8] / (t - n[9])
    a = theta ** 2 + n[0] * theta + n[1]
    b = n[2] * theta ** 2 + n[3] * theta + n[4]
    c = n[5] * theta ** 2 + n[6] * theta + n[7]
    return (2 * c / (-b + (b ** 2 - 4 * a * c) ** 0.5)) ** 4


def tsat(p: float) -> float:
    n = M["region4"]["n"]
    beta = p ** 0.25
    e = beta ** 2 + n[2] * beta + n[5]
    f = n[0] * beta ** 2 + n[3] * beta + n[6]
    g = n[1] * beta ** 2 + n[4] * beta + n[7]
    d = 2 * g / (-f - (f ** 2 - 4 * e * g) ** 0.5)
    return (n[9] + d - ((n[9] + d) ** 2 - 4 * (n[8] + n[9] * d)) ** 0.5) / 2


# ------------------------------------------------------------------ checking

# Official IF97 verification values (Revised Release 2007, Tables 5, 15, 35).
CASES = [
    ("R1", region1, (300, 3), {"v": 0.100215168e-2, "h": 0.115331273e3,
                               "s": 0.392294792, "cp": 0.417301218e1}),
    ("R1", region1, (300, 80), {"v": 0.971180894e-3, "h": 0.184142828e3,
                                "s": 0.368563852, "cp": 0.401008987e1}),
    ("R1", region1, (500, 3), {"v": 0.120241800e-2, "h": 0.975542239e3,
                               "s": 0.258041912e1, "cp": 0.465580682e1}),
    ("R2", region2, (300, 0.0035), {"v": 0.394913866e2, "h": 0.254991145e4,
                                    "s": 0.852238967e1, "cp": 0.191300162e1}),
    ("R2", region2, (700, 0.0035), {"v": 0.923015898e2, "h": 0.333568375e4,
                                    "s": 0.101749996e2, "cp": 0.208141274e1}),
    ("R2", region2, (700, 30), {"v": 0.542946619e-2, "h": 0.263149474e4,
                                "s": 0.517540298e1, "cp": 0.103505092e2}),
]

SATURATION = [
    ("Psat", psat, 300, 0.353658941e-2),
    ("Psat", psat, 500, 2.63889776),
    ("Psat", psat, 600, 0.123443146e2),
    ("Tsat", tsat, 0.1, 0.372755919e3),
    ("Tsat", tsat, 1.0, 0.453035632e3),
    ("Tsat", tsat, 10.0, 0.584149488e3),
]

TOLERANCE = 1e-8


def main() -> int:
    failures = []

    print("basic equations")
    for label, fn, args, expected in CASES:
        got = fn(*args)
        for key, want in expected.items():
            dev = abs(got[key] - want) / abs(want)
            ok = dev < TOLERANCE
            if not ok:
                failures.append(f"{label}{args} {key}: {got[key]:.9e} vs {want:.9e}")
            print(f"  [{'ok ' if ok else 'FAIL'}] {label}{str(args):12s} {key:3s} "
                  f"{got[key]:15.9e}  ref {want:15.9e}  dev {dev:.1e}")

    print("saturation line")
    for label, fn, arg, want in SATURATION:
        got = fn(arg)
        dev = abs(got - want) / abs(want)
        ok = dev < TOLERANCE
        if not ok:
            failures.append(f"{label}({arg}): {got:.9e} vs {want:.9e}")
        print(f"  [{'ok ' if ok else 'FAIL'}] {label}({arg:<6}) {got:15.9e}  "
              f"ref {want:15.9e}  dev {dev:.1e}")

    # Round-trip: Tsat(Psat(T)) must return T.
    print("saturation round-trip")
    worst = 0.0
    for t in (300.0, 400.0, 500.0, 600.0, 640.0):
        back = tsat(psat(t))
        worst = max(worst, abs(back - t))
    print(f"  max |Tsat(Psat(T)) - T| = {worst:.2e} K")
    if worst > 1e-6:
        failures.append(f"saturation round-trip error {worst:.2e} K")

    print()
    if failures:
        print(f"{len(failures)} failure(s):")
        for f in failures:
            print(f"  - {f}")
        return 1
    print("all IF97 verification values reproduced")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
