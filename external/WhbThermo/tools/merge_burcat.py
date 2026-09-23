"""
Merges NASA-7 coefficients from a Burcat / CHEMKIN thermo file into
species-database.json, closing the 13 species that are still anchor-only.

Usage:
    python tools/merge_burcat.py path/to/BURCAT.THR
    python tools/merge_burcat.py path/to/BURCAT.THR --all      # also overwrite Shomate species
    python tools/merge_burcat.py path/to/BURCAT.THR --dry-run

Get the file from the Burcat/Ruscic "Third Millennium Ideal Gas and Condensed
Phase Thermochemical Database" (Argonne / TU Eindhoven mirrors), or use any
CHEMKIN-format thermo file -- e.g. the Claus / sulfur chemistry mechanisms,
which cover S2/S6/S8, COS and CS2 far better than NIST does.

The merge is deliberately conservative:
  * only species listed in ALIASES are touched;
  * a merged record is sanity-checked against the manual's 500 degC anchor,
    and REJECTED if it deviates by more than the tolerance below.
That check is what catches a wrong Burcat name or a high/low block swap.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import db_guard  # noqa: E402
from nasa7 import parse_file  # noqa: E402

# The live database. An older version of this script pointed at a stale copy
# under src/WhbThermo.Data and wrote it without the guard.
DB_PATH = db_guard.DB_PATH

# our key -> candidate names in the thermo file (first match wins, case-insensitive)
ALIASES = {
    "COS":  ["COS", "OCS"],
    "CS2":  ["CS2"],
    "S2":   ["S2"],
    "S6":   ["S6"],
    "S8":   ["S8"],
    "C2H6": ["C2H6", "ETHANE"],
    "C3H6": ["C3H6", "PROPYLENE", "C3H6-PROPYLENE"],
    "C3H8": ["C3H8", "PROPANE"],
    "C2H2": ["C2H2", "ACETYLENE"],
    "C6H6": ["C6H6", "BENZENE"],
    "C7H8": ["C7H8", "TOLUENE", "C6H5CH3"],
    "SO3":  ["SO3"],
    "HCN":  ["HCN"],
    # already Shomate-backed; merged only with --all
    "H2": ["H2"], "N2": ["N2"], "CH4": ["CH4"], "CO": ["CO"], "CO2": ["CO2"],
    "H2O": ["H2O"], "NH3": ["NH3"], "AR": ["AR"], "H2S": ["H2S"], "SO2": ["SO2"],
    "NO": ["NO"], "NO2": ["NO2"], "N2O": ["N2O"], "C2H4": ["C2H4"], "HE": ["HE"],
}

# The manual's 500 degC anchor is coarse; accept a generous band but reject nonsense.
ANCHOR_TOLERANCE = 0.15
T_ANCHOR = 773.15


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("thermo_file")
    ap.add_argument("--all", action="store_true",
                    help="also replace species that already have a Shomate fit")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--force", action="store_true",
                    help="merge even if the 500 degC anchor check fails")
    args = ap.parse_args()

    records = parse_file(args.thermo_file)
    print(f"parsed {len(records)} records from {args.thermo_file}")

    db = db_guard.load()
    source = f"Burcat/CHEMKIN NASA-7, {Path(args.thermo_file).name}"

    merged, skipped, rejected = [], [], []

    for sp in db["species"]:
        key = sp["key"]
        kind = sp["cpModel"]["kind"]
        if kind != "anchor" and not args.all:
            continue

        candidates = ALIASES.get(key.upper(), [key])
        rec = next((records[c.upper()] for c in candidates if c.upper() in records), None)
        if rec is None:
            skipped.append(key)
            continue

        anchor = sp["cpModel"].get("anchorCp500C")
        if anchor:
            cp = rec.cp_mass(T_ANCHOR, sp["molarMass_kg_kmol"]) / 1000.0  # kJ/(kg*K)
            deviation = abs(cp - anchor) / anchor
            if deviation > ANCHOR_TOLERANCE and not args.force:
                rejected.append(f"{key}: NASA-7 gives {cp:.3f} vs anchor {anchor:.3f} "
                                f"kJ/(kg*K) ({deviation:.0%} off)")
                continue

        sp["cpModel"] = {
            "kind": "nasa7",
            "segments": [],
            "nasa7Segments": rec.to_segments(source),
            "anchorCp500C": anchor,
        }
        merged.append(key)

    print(f"\nmerged   ({len(merged)}): {', '.join(merged) or '-'}")
    print(f"not found ({len(skipped)}): {', '.join(skipped) or '-'}")
    if rejected:
        print(f"\nREJECTED by anchor check ({len(rejected)}):")
        for r in rejected:
            print(f"  {r}")
        print("  -> wrong alias, or high/low coefficient blocks swapped. "
              "Inspect before using --force.")

    if args.dry_run:
        print("\ndry run, database not written")
        return 0

    db_guard.save(db, tool="merge_burcat.py", note=f"NASA-7 from {Path(args.thermo_file).name}",
                  force=args.force)
    remaining = sum(1 for s in db["species"] if s["cpModel"]["kind"] == "anchor")
    print(f"\n{DB_PATH} written. {remaining} species still anchor-only.")
    print("Now lower `allowed` in DatabaseTests.'pending-completion count does not regress'"
          f" to {remaining}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
