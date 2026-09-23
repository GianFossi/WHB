"""
One-off migration of data/species-database.json from schema 2.x to 3.0.

Adds, per species: id (equal to key, immutable), family, synonyms, elements
(parsed from the formula) and dataQuality; and, at the root, the reference
state the NASA-9 data is on. Nothing is removed or recomputed, so the guard sees
additions only. The field rules live in schema_v3.py, which db_guard applies on
every later write; this script only adds the validation status found by the
CoolProp comparison, which is a finding rather than a rule.

    python tools/migrate_schema_v3.py
"""
from __future__ import annotations

import schema_v3
from db_guard import load, save

# Checked against the CoolProp pure-fluid reference in
# tests/WhbThermo.Tests/reference/coolprop-pure.csv (PureComponentGoldenTests,
# tolerances cp 3 %, mu 4 %, k 13 %).
COOLPROP = "CoolProp pure-fluid reference, 400-1600 K at 1 bar (PureComponentGoldenTests)"
VERIFIED = {"Ar", "C2H6", "CH4", "CO2", "H2", "H2O", "He", "N2", "NH3"}
KNOWN_DEVIATION = {
    "C3H8": "Sutherland transport vs CoolProp, 400-1600 K: mu +7 to +18 %, k -28 to -82 %",
    "C3H6": "Sutherland transport vs CoolProp, 400-1600 K: mu +4 to +5 %, k -22 to -70 %",
    "C6H6": "Sutherland transport vs CoolProp, 500-1600 K: mu -14 to +10 %, k -41 to -66 %",
    "C7H8": "Sutherland transport vs CoolProp, 500-1600 K: mu +5 to +12 %, k -43 to -71 %",
}


def main() -> None:
    db = load()
    missing = [s["key"] for s in db["species"] if s["key"] not in schema_v3.FAMILY]
    if missing:
        raise SystemExit(f"no family assigned for: {missing}")

    db["schemaVersion"] = schema_v3.SCHEMA_VERSION
    db["referenceState"] = schema_v3.REFERENCE_STATE
    migrated = []
    for sp in db["species"]:
        key = sp["key"]
        # Field order: identity first, then the existing record unchanged.
        new = {"key": key, "id": key}
        for field, value in sp.items():
            if field == "key":
                continue
            new[field] = value
            if field == "name":
                new["synonyms"] = schema_v3.SYNONYMS.get(key, [])
                new["family"] = schema_v3.FAMILY[key]
                new["elements"] = schema_v3.elements(sp.get("formula") or key)
        schema_v3.normalise(new)
        if key in KNOWN_DEVIATION:
            new["dataQuality"]["validation"] = "knownDeviation"
            new["dataQuality"]["validationNote"] = KNOWN_DEVIATION[key]
        elif key in VERIFIED:
            new["dataQuality"]["validation"] = "verified"
            new["dataQuality"]["validationNote"] = COOLPROP
        migrated.append(new)
    db["species"] = migrated
    save(db, tool="migrate_schema_v3.py",
         note="schema 3.0: id, family, synonyms, elements, dataQuality, referenceState")


if __name__ == "__main__":
    main()
