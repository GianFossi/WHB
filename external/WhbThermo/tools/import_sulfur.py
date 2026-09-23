"""
Builds data/sulfur-species.json: NASA-9 polynomials for all eight gas-phase
sulfur allotropes, from the NASA CEA thermo.inp (Apache 2.0).

    python tools/import_sulfur.py /path/to/cea/data

The main species database carries only S2, S6 and S8, which is what the WHB gas
mixture needs. A sulfur condenser needs all of S1 to S8, because the allotrope
distribution shifts continuously along the condensation path and it is that
shift - not just sensible cooling - that carries much of the heat release.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import nasa9  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "sulfur-species.json"

# allotrope -> (CEA name, atoms per molecule)
ALLOTROPES = {
    "S": ("S", 1), "S2": ("S2", 2), "S3": ("S3", 3), "S4": ("S4", 4),
    "S5": ("S5", 5), "S6": ("S6", 6), "S7": ("S7", 7), "S8": ("S8", 8),
}

# Liquid sulfur, the reference phase for vapour pressure. The equilibrium
# n S(L) -> S_n(g) gives each allotrope's partial pressure directly from the
# Gibbs energies, so the vapour pressure is consistent with the latent heats and
# with the speciation by construction rather than by a separate correlation.
LIQUID = ("S(L)", "SL")

ATOMIC_MASS = 32.065  # g/mol


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    records = nasa9.parse_file(str(Path(sys.argv[1]) / "thermo.inp"))
    source = "NASA CEA thermo.inp (Glenn Research Center), Apache-2.0"

    species, missing = [], []
    for key, (name, atoms) in ALLOTROPES.items():
        record = nasa9.find(records, name)
        if record is None:
            missing.append(key)
            continue
        species.append({
            "key": key,
            "atoms": atoms,
            "molarMass_g_mol": round(record.molar_mass, 4),
            "nasa9Segments": record.to_segments(source),
        })

    liquid = nasa9.find(records, LIQUID[0])
    if liquid is None:
        print(f"WARNING: {LIQUID[0]} not found; vapour pressure cannot be derived")
        liquid_entry = None
    else:
        liquid_entry = {
            "key": LIQUID[1],
            "atoms": 1,
            "molarMass_g_mol": round(liquid.molar_mass, 4),
            "nasa9Segments": liquid.to_segments(source),
        }

    document = {
        "schemaVersion": "1.1",
        "description": "Gas-phase sulfur allotropes S1-S8 for reactive condensation modelling",
        "atomicMass_g_mol": ATOMIC_MASS,
        "source": source,
        "reference": (
            "Speciation behaviour cross-checked against Gamson & Elkins (1953) and "
            "Paskall (1979) as summarised in the Claus process literature: below "
            "about 700 degF the vapour is dominated by S6 and S8; above about "
            "1000 degF it is predominantly S2."
        ),
        "species": species,
        "liquid": liquid_entry,
        "vapourPressureValidity": {
            "tMinC": 120.0, "tMaxC": 350.0,
            "note": ("Derived as n S(L) -> S_n(g) from CEA Gibbs energies. Honest limit: "
                     "at the normal boiling point (444.6 degC) it gives 0.68 bar against "
                     "1.013 expected, so it must not be used above about 350 degC. Inside "
                     "the condenser window it is consistent: 5.6 Pa at 120 degC, 32 Pa at "
                     "150 degC, 6.07 kPa at 300 degC."),
        },
    }

    OUT.write_text(json.dumps(document, indent=2), encoding="utf-8")
    print(f"{OUT.name}: {len(species)} allotropes"
          + (" + liquid reference" if liquid_entry else " (NO liquid reference)"))
    for s in species:
        print(f"  {s['key']:3s} M = {s['molarMass_g_mol']:8.3f}  "
              f"{len(s['nasa9Segments'])} segments")
    if missing:
        print(f"missing: {', '.join(missing)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
