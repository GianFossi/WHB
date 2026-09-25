"""
Adds IAPWS-IF97 regions 3 and 5 to data/iapws-if97.json.

The coefficients are copied from the `iapws` Python package (MIT licence,
J.J. Gomez-Romero), which transcribes Tables 30 (region 3) and 37/38
(region 5) of the IAPWS Revised Release on IF97 (2007). They are standard
constants; the package is only the machine-readable transcription. Every value
is written with repr(), so it round-trips to the same double.

The F# tests check the result against the release's own verification tables
(Tables 33 and 42) and against the package at a grid of states.

    pip install iapws
    python tools/import_if97_regions.py
"""
from __future__ import annotations

import json
from pathlib import Path

from iapws import _iapws97Constants as C

ROOT = Path(__file__).resolve().parent.parent
TARGET = ROOT / "data" / "iapws-if97.json"
SOURCE = ("IAPWS-IF97 Revised Release (2007), transcribed from the `iapws` Python "
          "package (MIT) by tools/import_if97_regions.py")


def floats(a) -> list[float]:
    return [float(x) for x in a]


def ints(a) -> list[int]:
    return [int(x) for x in a]


def main() -> int:
    doc = json.loads(TARGET.read_text(encoding="utf-8"))
    doc["region3"] = {
        "description": ("Near-critical region, Helmholtz form phi(delta, tau) = n1 ln delta + "
                        "SUM n_i delta^I tau^J, delta = rho/322 kg/m3, tau = 647.096 K / T. "
                        "Bounded below by 623.15 K and the B23 line, above by B23 and 100 MPa."),
        "n1": 1.0658070028513,
        "I": ints(C.Region3_Li), "J": ints(C.Region3_Lj), "n": floats(C.Region3_n),
        "source": SOURCE,
    }
    doc["region5"] = {
        "description": ("High-temperature steam, Gibbs form gamma = gamma_o + gamma_r, "
                        "pi = P/1 MPa, tau = 1000 K / T. 1073.15-2273.15 K, P <= 50 MPa."),
        "piStar_MPa": 1.0, "tauStar_K": 1000.0,
        "range": {"tMinK": 1073.15, "tMaxK": 2273.15, "pMaxMPa": 50.0},
        "ideal": {"j": ints(C.Region5_cp0_Jo), "n": floats(C.Region5_cp0_no)},
        "residual": {"I": ints(C.Region5_Li), "J": ints(C.Region5_Lj), "n": floats(C.Region5_n)},
        "source": SOURCE,
    }
    TARGET.write_text(json.dumps(doc, indent=2), encoding="utf-8")
    print(f"region 3: {len(doc['region3']['n'])} terms + n1; region 5: "
          f"{len(doc['region5']['ideal']['n'])} ideal + {len(doc['region5']['residual']['n'])} residual")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
