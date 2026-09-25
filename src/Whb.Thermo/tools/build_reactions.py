"""
Builds data/reactions.json, the reaction database, from the species database.

Reactions are kept out of the species records: a reaction belongs to several
species at once. Each entry states its stoichiometry by species id, the model
its equilibrium constant comes from, an optional kinetic model, and the range
it is valid over. That range is not typed in: it is the intersection of the
NASA-9 ranges of the species involved, so it cannot claim more than the data
supports. Every reaction is checked for atom balance before it is written.

    python tools/build_reactions.py
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPECIES = ROOT / "data" / "species-database.json"
OUT = ROOT / "data" / "reactions.json"

# id (immutable) -> (equation, stoichiometry, note)
REACTIONS = {
    "waterGasShift": ("CO + H2O -> CO2 + H2",
                      {"CO": -1, "H2O": -1, "CO2": 1, "H2": 1},
                      "Sets the H2/CO ratio of a cooling syngas while it is still reactive."),
    "h2sCracking": ("2 H2S -> 2 H2 + S2",
                    {"H2S": -2, "H2": 2, "S2": 1},
                    "Decides how much sulfur leaves a Claus thermal stage as S2."),
    "sulfurDepolymerisation": ("S8 -> 4 S2",
                               {"S8": -1, "S2": 4},
                               "Why the thermal stage makes S2 and the catalytic stage S8."),
    "ammoniaCracking": ("2 NH3 -> N2 + 3 H2",
                        {"NH3": -2, "N2": 1, "H2": 3},
                        "Ammonia destruction in a Claus furnace or an ammonia plant WHB."),
    "clausReaction": ("2 H2S + SO2 -> 3/2 S2 + 2 H2O",
                      {"H2S": -2, "SO2": -1, "S2": 1.5, "H2O": 2},
                      "Thermal Claus reaction written to S2, the high-temperature allotrope."),
    "cosHydrolysis": ("COS + H2O -> CO2 + H2S",
                      {"COS": -1, "H2O": -1, "CO2": 1, "H2S": 1},
                      "COS destruction; kinetically slow below catalyst temperatures."),
    "sulfurDioxideDissociation": ("2 SO2 -> S2 + 2 O2",
                                  {"SO2": -2, "S2": 1, "O2": 2},
                                  "Negligible at WHB temperatures; kept as a check that it is."),
}


def nasa9_range(sp: dict) -> tuple[float, float]:
    segments = sp["cpModel"]["nasa9Segments"]
    return min(s["tMinK"] for s in segments), max(s["tMaxK"] for s in segments)


def main() -> int:
    species = {s["key"]: s for s in json.loads(SPECIES.read_text(encoding="utf-8"))["species"]}
    entries = []
    for rid, (equation, stoich, note) in REACTIONS.items():
        missing = [k for k in stoich if k not in species]
        if missing:
            raise SystemExit(f"{rid}: unknown species {missing}")
        balance: dict[str, float] = {}
        for key, nu in stoich.items():
            for element, count in species[key]["elements"].items():
                balance[element] = balance.get(element, 0.0) + nu * count
        unbalanced = {e: v for e, v in balance.items() if abs(v) > 1e-12}
        if unbalanced:
            raise SystemExit(f"{rid}: atoms not conserved {unbalanced}")
        lo = max(nasa9_range(species[k])[0] for k in stoich)
        hi = min(nasa9_range(species[k])[1] for k in stoich)
        entries.append({
            "id": rid,
            "equation": equation,
            "stoichiometry": stoich,
            "equilibriumModel": "nasa9Gibbs",
            "kineticModel": None,
            "validity": {"tMinK": round(lo, 2), "tMaxK": round(hi, 2)},
            "source": "Equilibrium constant from the NASA-9 Gibbs energies of species-database.json",
            "note": note,
        })

    document = {
        "schemaVersion": "1.0",
        "description": ("Reaction database. Stoichiometry by species id; the equilibrium "
                        "constant is computed from the species data, never stored. Validity is "
                        "the intersection of the NASA-9 ranges of the species involved. Written "
                        "by tools/build_reactions.py."),
        "reactions": entries,
    }
    OUT.write_text(json.dumps(document, indent=2), encoding="utf-8")
    for e in entries:
        print(f"  {e['id']:26s} {e['equation']:32s} "
              f"{e['validity']['tMinK']:.0f}-{e['validity']['tMaxK']:.0f} K")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
