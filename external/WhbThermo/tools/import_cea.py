"""
Builds data/species-database.json from the NASA CEA reference data.

    python tools/import_cea.py /path/to/cea/data

Expects `thermo.inp` (NASA-9 thermodynamic polynomials) and `trans.inp`
(transport property coefficients) in that directory. Both ship with the
github.com/nasa/cea repository under **Apache 2.0**, so unlike the
Goos-Burcat-Ruscic database they carry no commercial-use restriction.

What this replaces:
  * Cp        anchor-only 500 degC points and NIST Shomate  ->  NASA-9, to 6000 K
  * viscosity two-parameter Sutherland from the manual      ->  NASA 4-parameter
  * conductivity                    likewise                ->  NASA 4-parameter

Species are matched by EXACT name first. Case matters: `Co` is cobalt and `CO`
is carbon monoxide, and upper-casing the key silently swaps them -- which
produced a 21 % error in Cp before it was caught. Aliases below carry the
comma-qualified CEA names, which also disambiguate isomers: `C2H2,acetylene` is
the species we want, `C2H2,vinylidene` is not.
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

# our key -> candidate CEA names, in priority order
ALIASES = {
    "H2": ["H2"], "N2": ["N2"], "CH4": ["CH4"], "CO": ["CO"], "CO2": ["CO2"],
    "H2O": ["H2O"], "NH3": ["NH3"], "Ar": ["Ar"], "H2S": ["H2S"], "SO2": ["SO2"],
    "COS": ["COS"], "CS2": ["CS2"], "S2": ["S2"], "S6": ["S6"], "S8": ["S8"],
    "C2H4": ["C2H4"], "C2H6": ["C2H6"],
    "C3H6": ["C3H6,propylene"], "C3H8": ["C3H8"],
    "C2H2": ["C2H2,acetylene"],
    "C6H6": ["C6H6"], "C7H8": ["C7H8"],
    "NO": ["NO"], "NO2": ["NO2"], "N2O": ["N2O"], "SO3": ["SO3"],
    "HCN": ["HCN"], "He": ["He"],
}

# Transport records are indexed under the plain formula even where the
# thermodynamic record is comma-qualified.
TRANSPORT_ALIASES = {"C3H6": ["C3H6"], "C2H2": ["C2H2"]}

T_ANCHOR = 773.15          # the manual's 500 degC reference point
SHOMATE_TOLERANCE = 0.05   # NIST Shomate is an INDEPENDENT source: tight
ANCHOR_TOLERANCE = 0.20    # the manual's single point: coarse, and see below

# Validation order matters. Where a NIST Shomate fit exists it is the primary
# check, because it is independent of both NASA and the manual. The manual's
# 500 degC anchor is only a fallback -- and it turned out to be wrong: every
# hydrocarbon in that column reads 18-37 % low, while NASA-9 agrees with NIST
# Shomate to 1.6 % (CH4) and 0.1 % (C2H4). So an anchor mismatch is reported as
# a defect in the manual, not as a reason to reject NASA data.
HYDROCARBON_NOTE = ("manual anchor is low; NASA-9 cross-checks against NIST "
                    "Shomate where both exist")


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    data_dir = Path(sys.argv[1])
    thermo = nasa9.parse_file(str(data_dir / "thermo.inp"))
    transport = cea_transport.parse_file(str(data_dir / "trans.inp"))
    print(f"thermo.inp:  {len(thermo)} records")
    print(f"trans.inp:   {len(transport)} records\n")

    db = db_guard.load()
    db["schemaVersion"] = "2.0"

    thermo_source = "NASA CEA thermo.inp (Glenn Research Center), Apache-2.0"
    transport_source = "NASA CEA trans.inp (Glenn Research Center), Apache-2.0"

    merged_cp, merged_tr, rejected = [], [], []
    missing_cp, missing_tr, anchor_mismatch = [], [], []

    for sp in db["species"]:
        key = sp["key"]
        anchor = sp["cpModel"].get("anchorCp500C")

        # ---- thermodynamics ----
        rec = None
        for candidate in ALIASES.get(key, [key]):
            rec = nasa9.find(thermo, candidate)
            if rec:
                break

        if rec is None:
            missing_cp.append(key)
        else:
            cp = rec.cp_mass(T_ANCHOR) / 1000.0      # kJ/(kg*K)
            segments = sp["cpModel"].get("segments") or []
            accept, note = True, None

            if segments:
                seg = segments[0]
                t = T_ANCHOR / 1000.0
                molar = (seg["a"] + seg["b"] * t + seg["c"] * t**2
                         + seg["d"] * t**3 + seg["e"] / t**2)
                shomate = molar / sp["molarMass_kg_kmol"]
                deviation = abs(cp - shomate) / shomate
                if deviation > SHOMATE_TOLERANCE:
                    accept = False
                    rejected.append(f"{key}: NASA-9 {cp:.3f} vs NIST Shomate "
                                    f"{shomate:.3f} kJ/(kg*K) ({deviation:.0%} off)")
            elif anchor:
                deviation = abs(cp - anchor) / anchor
                if deviation > ANCHOR_TOLERANCE:
                    note = (f"{key}: NASA-9 {cp:.3f} vs manual anchor {anchor:.3f} "
                            f"kJ/(kg*K) ({deviation:+.0%})")

            if accept:
                sp["cpModel"] = {"kind": "nasa9", "segments": [], "nasa7Segments": [],
                                 "nasa9Segments": rec.to_segments(thermo_source),
                                 "anchorCp500C": anchor}
                sp["molarMass_kg_kmol"] = round(rec.molar_mass, 4)
                merged_cp.append(key)
                if note:
                    anchor_mismatch.append(note)

        # ---- transport ----
        tr = None
        for candidate in TRANSPORT_ALIASES.get(key, []) + ALIASES.get(key, [key]):
            tr = cea_transport.find(transport, candidate)
            if tr:
                break

        if tr is None or not tr.viscosity:
            missing_tr.append(key)
            sp.setdefault("transport", {"kind": "sutherland"})
        else:
            sp["transport"] = tr.to_json(transport_source)
            merged_tr.append(key)

    print(f"Cp -> NASA-9        ({len(merged_cp):2d}): {', '.join(merged_cp)}")
    print(f"transport -> NASA   ({len(merged_tr):2d}): {', '.join(merged_tr)}")
    if missing_cp:
        print(f"\nno NASA-9 record    ({len(missing_cp)}): {', '.join(missing_cp)}")
    if missing_tr:
        print(f"no NASA transport   ({len(missing_tr)}): {', '.join(missing_tr)}"
              "\n  -> these keep the Sutherland fits from the manual, which are only "
              "reliable below about 1000 degC")
    if anchor_mismatch:
        print(f"\nMANUAL ANCHOR DISAGREES ({len(anchor_mismatch)}) - NASA data used anyway:")
        for n in anchor_mismatch:
            print(f"  {n}")
        print(f"  -> {HYDROCARBON_NOTE}")
    if rejected:
        print(f"\nREJECTED against NIST Shomate ({len(rejected)}):")
        for r in rejected:
            print(f"  {r}")
        print("  -> wrong alias or an isomer; inspect before forcing")

    db["dataSources"] = {"thermodynamics": thermo_source, "transport": transport_source}
    db_guard.save(db, tool="import_cea.py", note="NASA-9 Cp and CEA transport",
                  force=db_guard.force_requested())

    pending = sum(1 for s in db["species"] if s["cpModel"]["kind"] == "anchor")
    print(f"\n{DB_PATH} written. {pending} species still anchor-only.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
