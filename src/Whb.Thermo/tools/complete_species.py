"""
Completes the species database against the full property schema: vapour
pressure, critical properties, molecular formula and Fuller diffusion volumes.

    pip install chemicals
    python tools/complete_species.py

Four gaps closed, in the order they unblock each other:

  1. FORMULA. Not previously a field - the key stood in for it, which works for
     46 of 48 species and fails for DME (CH3OCH3) and S1 (S). The formula is
     needed in its own right for the diffusion volumes below.

  2. CRITICAL PROPERTIES. Only 15 of 48 had them, which meant Chung-Lee-Starling
     could not be used for the 14 species with no measured transport - the exact
     ones it exists to cover.

  3. VAPOUR PRESSURE. Perry Table 2-8 (DIPPR 101 form). Water is covered by IF97
     and sulfur by XSulfur, but the general database had nothing.

  4. FULLER DIFFUSION VOLUMES, computed from the formula by atomic increments.
     This is what the Claus condenser rating actually turns on: the gas-side
     mass transfer coefficient is controlled by diffusion of sulfur through the
     non-condensables, and without binary diffusivities the Lewis number has to
     be supplied by the caller rather than computed.

Values from `chemicals` (MIT) are handbook data and are marked as such in each
record's source field; the diffusion volumes are computed here from published
atomic increments and are marked as computed, not measured.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import db_guard  # noqa: E402

try:
    import chemicals
    from chemicals.critical import Tc, Pc
    from chemicals.acentric import omega
except ImportError:
    raise SystemExit("pip install chemicals")

ROOT = Path(__file__).resolve().parent.parent
DATA = Path(chemicals.__file__).parent
DB_PATH = ROOT / "data" / "species-database.json"

# key -> (CAS, molecular formula)
IDENTITY = {
    "H2": ("1333-74-0", "H2"), "N2": ("7727-37-9", "N2"), "O2": ("7782-44-7", "O2"),
    "Ar": ("7440-37-1", "Ar"), "He": ("7440-59-7", "He"),
    "CO": ("630-08-0", "CO"), "CO2": ("124-38-9", "CO2"), "CH4": ("74-82-8", "CH4"),
    "C2H6": ("74-84-0", "C2H6"), "C2H4": ("74-85-1", "C2H4"),
    "C2H2": ("74-86-2", "C2H2"), "C3H8": ("74-98-6", "C3H8"),
    "C3H6": ("115-07-1", "C3H6"), "C6H6": ("71-43-2", "C6H6"),
    "C7H8": ("108-88-3", "C7H8"),
    "H2O": ("7732-18-5", "H2O"), "NH3": ("7664-41-7", "NH3"),
    "H2S": ("7783-06-4", "H2S"), "SO2": ("7446-09-5", "SO2"),
    "SO3": ("7446-11-9", "SO3"), "COS": ("463-58-1", "COS"),
    "CS2": ("75-15-0", "CS2"), "HCN": ("74-90-8", "HCN"),
    "NO": ("10102-43-9", "NO"), "NO2": ("10102-44-0", "NO2"),
    "N2O": ("10024-97-2", "N2O"),
    "CH3OH": ("67-56-1", "CH4O"), "HCHO": ("50-00-0", "CH2O"),
    "DME": ("115-10-6", "C2H6O"),
    # sulfur allotropes
    "S1": (None, "S"), "S2": (None, "S2"), "S3": (None, "S3"), "S4": (None, "S4"),
    "S5": (None, "S5"), "S6": (None, "S6"), "S7": (None, "S7"), "S8": (None, "S8"),
    # atoms and radicals
    "H": (None, "H"), "O": (None, "O"), "N": (None, "N"), "OH": (None, "HO"),
    "SH": (None, "HS"), "SO": (None, "OS"), "CH3": (None, "CH3"),
    "HO2": (None, "HO2"), "CS": (None, "CS"), "NH2": (None, "H2N"),
    "CN": (None, "CN"),
}

# Fuller, Schettler & Giddings atomic diffusion volume increments [cm^3/mol].
# Published values; the aromatic-ring correction is applied for benzene and
# toluene, which is why they are not simply the sum of their atoms.
FULLER_ATOMIC = {"C": 15.9, "H": 2.31, "O": 6.11, "N": 4.54, "S": 22.9, "Cl": 21.0}
FULLER_AROMATIC_RING = -18.3

# Species whose diffusion volume is tabulated directly rather than summed. Using
# the tabulated value where it exists is more accurate than the atomic sum.
FULLER_MOLECULAR = {
    "H2": 6.12, "He": 2.67, "N2": 18.5, "O2": 16.3, "Ar": 16.2,
    "CO": 18.0, "CO2": 26.9, "H2O": 13.1, "NH3": 20.7, "N2O": 35.9,
    "SO2": 41.8,
}

AROMATIC_RINGS = {"C6H6": 1, "C7H8": 1}


def parse_formula(formula: str) -> dict[str, int]:
    counts: dict[str, int] = {}
    for element, number in re.findall(r"([A-Z][a-z]?)(\d*)", formula):
        if not element:
            continue
        counts[element] = counts.get(element, 0) + (int(number) if number else 1)
    return counts


def diffusion_volume(key: str, formula: str):
    """Fuller diffusion volume [cm^3/mol] and how it was obtained."""
    if key in FULLER_MOLECULAR:
        return FULLER_MOLECULAR[key], "Fuller et al., tabulated molecular volume"

    counts = parse_formula(formula)
    unknown = [e for e in counts if e not in FULLER_ATOMIC]
    if unknown:
        return None, f"no Fuller increment for {', '.join(unknown)}"

    total = sum(FULLER_ATOMIC[e] * n for e, n in counts.items())
    total += FULLER_AROMATIC_RING * AROMATIC_RINGS.get(key, 0)
    note = "Fuller et al., summed from atomic increments"
    if key in AROMATIC_RINGS:
        note += " with the aromatic-ring correction"
    return total, note


def read_vapour_pressure():
    """Perry Table 2-8, DIPPR 101 form."""
    path = DATA / "Vapor Pressure" / "Table 2-8 Vapor Pressure of Inorganic and Organic Liquids.tsv"
    rows = {}
    if not path.exists():
        print(f"  missing: {path.name}")
        return rows
    with open(path, encoding="utf-8", errors="replace") as fh:
        fh.readline()
        for line in fh:
            cells = line.rstrip("\n").split("\t")
            if len(cells) < 9:
                continue
            try:
                coefficients = [float(c) if c.strip() else 0.0 for c in cells[2:7]]
                t_min, t_max = float(cells[7]), float(cells[8])
            except (ValueError, IndexError):
                continue
            rows[cells[0].strip()] = {"c": coefficients, "tMinK": t_min, "tMaxK": t_max}
    return rows


def main() -> int:
    db = db_guard.load()
    vapour = read_vapour_pressure()
    print(f"vapour pressure table: {len(vapour)} compounds\n")

    counters = {"formula": 0, "critical": 0, "vapour": 0, "diffusion": 0}
    no_critical, no_vapour, no_diffusion = [], [], []

    for sp in db["species"]:
        key = sp["key"]
        cas, formula = IDENTITY.get(key, (None, key))

        # ---- 1. formula ----
        sp["formula"] = formula
        if cas:
            sp["cas"] = cas
        counters["formula"] += 1

        # ---- 2. critical properties ----
        if sp.get("critical") is None and cas:
            try:
                tc, pc, w = Tc(cas), Pc(cas), omega(cas)
            except Exception:
                tc = pc = w = None
            if tc and pc and w is not None:
                sp["critical"] = {
                    "tcK": round(tc, 3),
                    "pcBar": round(pc / 1e5, 4),
                    "acentric": round(w, 4),
                    "source": "chemicals (MIT) handbook collection: CRC / IUPAC / Poling",
                    "verified": False,
                }
                counters["critical"] += 1
        if sp.get("critical") is None:
            no_critical.append(key)

        # ---- 3. vapour pressure ----
        row = vapour.get(cas) if cas else None
        if row:
            sp["vapourPressure"] = {
                "equation": 101,
                "c": row["c"],
                "tMinK": row["tMinK"],
                "tMaxK": row["tMaxK"],
                "unit": "Pa",
                "source": "Perry's Chemical Engineers' Handbook 8th ed., Table 2-8 (DIPPR 101)",
            }
            counters["vapour"] += 1
        else:
            no_vapour.append(key)

        # ---- 4. Fuller diffusion volume ----
        volume, note = diffusion_volume(key, formula)
        if volume is not None:
            sp["diffusionVolume"] = {
                "value_cm3_mol": round(volume, 3),
                "source": note,
                "computed": key not in FULLER_MOLECULAR,
            }
            counters["diffusion"] += 1
        else:
            no_diffusion.append(f"{key} ({note})")

    db["schemaVersion"] = "2.1"
    db_guard.save(db, tool="complete_species.py", note="formula, critical, vapour pressure, diffusion volume",
                  force=db_guard.force_requested())

    total = len(db["species"])
    print(f"{DB_PATH.name}: {total} species")
    print(f"  formula added            {counters['formula']:3d}/{total}")
    print(f"  critical properties new  {counters['critical']:3d}  "
          f"(total with critical: {total - len(no_critical)}/{total})")
    print(f"  vapour pressure          {counters['vapour']:3d}/{total}")
    print(f"  diffusion volume         {counters['diffusion']:3d}/{total}")
    if no_critical:
        print(f"\nstill without critical properties ({len(no_critical)}): "
              f"{', '.join(no_critical)}")
    if no_vapour:
        print(f"\nno vapour pressure ({len(no_vapour)}): {', '.join(no_vapour)}")
    if no_diffusion:
        print(f"\nno diffusion volume ({len(no_diffusion)}): {'; '.join(no_diffusion)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
