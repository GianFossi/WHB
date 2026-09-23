"""
Adds species to data/species-database.json directly from the NASA CEA reference
data (Apache 2.0).

    python tools/add_species.py /path/to/cea/data

Unlike `import_cea.py`, which upgrades species already present, this tool
CREATES entries for species the database does not yet have. It is the route for
extending coverage to a new process family.

Two rules it enforces:

  * Cp comes from thermo.inp (NASA-9) and is mandatory. A species with no
    thermodynamic record is not added at all, rather than added with a
    placeholder that later looks like data.
  * Transport comes from trans.inp where it exists. Where it does not, the entry
    is written with transport kind "none" and the library FAILS when asked for a
    viscosity or conductivity. That is deliberate: inventing a Sutherland fit for
    methanol or DME from nothing would produce numbers indistinguishable from
    measured ones.

Isomer trap: CEA names are comma-qualified where several isomers exist
("C2H2,acetylene" against "C2H2,vinylidene"). The aliases below carry the
qualified names, and a bare formula must never be assumed to be the one wanted.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import db_guard  # noqa: E402
import cea_transport  # noqa: E402
import nasa9  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
DB_PATH = ROOT / "data" / "species-database.json"

# our key -> (CEA thermo name, display name, CEA transport name or None)
NEW_SPECIES = {
    # permanent gases
    "O2":     ("O2", "Oxygen", "O2"),
    # oxygenates
    "CH3OH":  ("CH3OH", "Methanol", "CH3OH"),
    "HCHO":   ("HCHO,formaldehy", "Formaldehyde", None),
    "DME":    ("CH3OCH3", "DimethylEther", None),
    # remaining sulfur allotropes, for Sx coverage
    "S3":     ("S3", "Trisulfur", None),
    "S4":     ("S4", "Tetrasulfur", None),
    "S5":     ("S5", "Pentasulfur", None),
    "S7":     ("S7", "Heptasulfur", None),
    # Atoms and radicals. Below about 1000 degC their equilibrium concentrations
    # are negligible and they can be ignored; above it they are what dissociation
    # produces, and without them no dissociation equilibrium can be closed.
    "H":      ("H", "AtomicHydrogen", "H"),
    "O":      ("O", "AtomicOxygen", "O"),
    "N":      ("N", "AtomicNitrogen", "N"),
    "S1":     ("S", "AtomicSulfur", None),
    "OH":     ("OH", "Hydroxyl", "OH"),
    "SH":     ("SH", "Mercapto", None),
    "SO":     ("SO", "SulfurMonoxide", None),
    "CH3":    ("CH3", "Methyl", None),
    "HO2":    ("HO2", "Hydroperoxyl", None),
    "CS":     ("CS", "CarbonMonosulfide", None),
    "NH2":    ("NH2", "Amidogen", None),
    "CN":     ("CN", "Cyano", None),
}


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    data_dir = Path(sys.argv[1])
    thermo = nasa9.parse_file(str(data_dir / "thermo.inp"))
    transport = cea_transport.parse_file(str(data_dir / "trans.inp"))

    db = db_guard.load()
    existing = {s["key"] for s in db["species"]}

    thermo_source = "NASA CEA thermo.inp (Glenn Research Center), Apache-2.0"
    transport_source = "NASA CEA trans.inp (Glenn Research Center), Apache-2.0"

    added, skipped, without_transport = [], [], []

    for key, (thermo_name, display, transport_name) in NEW_SPECIES.items():
        if key in existing:
            skipped.append(f"{key} (already present)")
            continue

        record = nasa9.find(thermo, thermo_name)
        if record is None:
            skipped.append(f"{key} (no NASA-9 record for '{thermo_name}')")
            continue

        entry = {
            "key": key,
            "name": display,
            "molarMass_kg_kmol": round(record.molar_mass, 4),
            "cpModel": {
                "kind": "nasa9",
                "segments": [],
                "nasa7Segments": [],
                "nasa9Segments": record.to_segments(thermo_source),
                "anchorCp500C": None,
            },
            "critical": None,
        }

        tr = cea_transport.find(transport, transport_name) if transport_name else None
        if tr and tr.viscosity:
            entry["transport"] = tr.to_json(transport_source)
        else:
            entry["transport"] = {
                "kind": "none",
                "viscosity": [],
                "conductivity": [],
                "reason": (
                    "No NASA CEA transport record. No Sutherland fit is supplied either: "
                    "fabricating one would produce numbers indistinguishable from measured "
                    "data. Viscosity and conductivity calls for this species fail explicitly. "
                    "To close the gap, add a measured fit or a Chung/Lucas estimate with its "
                    "provenance recorded."
                ),
            }
            without_transport.append(key)

        # The loader needs the legacy Sutherland block to be present but it is
        # never used when transport kind is nasaCea or none; write it as null so
        # nothing can silently read a fabricated value.
        entry["viscosity"] = None
        entry["conductivity"] = None

        db["species"].append(entry)
        added.append(key)

    db["species"].sort(key=lambda s: s["key"])
    db_guard.save(db, tool="add_species.py", note="new species from CEA",
                  force=db_guard.force_requested())

    print(f"{DB_PATH.name}: {len(db['species'])} species total")
    print(f"added ({len(added)}): {', '.join(added) or '-'}")
    if without_transport:
        print(f"no transport data ({len(without_transport)}): {', '.join(without_transport)}")
        print("  -> viscosity and conductivity calls for these will FAIL, by design")
    if skipped:
        print(f"skipped ({len(skipped)}): {'; '.join(skipped)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
