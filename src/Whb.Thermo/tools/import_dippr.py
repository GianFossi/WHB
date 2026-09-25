"""
Builds data/liquid-properties.json from the open DIPPR-form correlation tables
shipped with the `chemicals` package (MIT licence).

    pip install chemicals
    python tools/import_dippr.py

WHAT THIS IS, AND WHAT IT IS NOT
--------------------------------
The full DIPPR 801 database is an AIChE subscription product and cannot be
retrieved. What can be is the subset published in the open literature:

  * Perry's Chemical Engineers' Handbook, 8th ed., Tables 2-312 to 2-315 and
    2-153 - DIPPR-form correlations with their coefficients and validity ranges,
    for roughly 340 compounds;
  * VDI Heat Atlas PPDS polynomials, for roughly 270 compounds.

Both are redistributed by `chemicals` under MIT. The numerical values are facts
and not themselves copyrightable, but they originate in copyrighted handbooks,
so the same rule as the rest of this project applies: the data live in an
EXTERNAL file loaded at runtime, never embedded in an assembly.

DIPPR EQUATION FORMS
--------------------
  100  Y = A + BT + CT^2 + DT^3 + ET^4
  101  Y = exp(A + B/T + C ln T + D T^E)
  102  Y = A T^B / (1 + C/T + D/T^2)
  105  Y = A / B^(1 + (1 - T/C)^D)                      [kmol/m^3]
  106  Y = A (1 - Tr)^(B + C Tr + D Tr^2 + E Tr^3)
  114  Y = A^2/t + B - 2ACt - ADt^2 - C^2 t^3/3
           - CD t^4/2 - D^2 t^5/5,      t = 1 - Tr

Form 105 returns a MOLAR density in kmol/m^3, so it needs the molar mass to
become kg/m^3. Form 114 returns a molar heat capacity in J/(kmol*K). Getting
either unit wrong produces a plausible number that is out by three orders of
magnitude, so the importer records the equation number and the output unit
alongside every coefficient set.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

try:
    import chemicals
except ImportError:
    raise SystemExit("pip install chemicals")

ROOT = Path(__file__).resolve().parent.parent
DATA = Path(chemicals.__file__).parent
OUT = ROOT / "data" / "liquid-properties.json"

PERRY = "Perry's Chemical Engineers' Handbook, 8th ed. (DIPPR-form correlation)"
VDI = "VDI Heat Atlas, PPDS polynomial"

# key -> (CAS, name, molar mass g/mol)
SPECIES = {
    "H2O": ("7732-18-5", "Water", 18.015),
    "NH3": ("7664-41-7", "Ammonia", 17.031),
    "H2S": ("7783-06-4", "HydrogenSulfide", 34.081),
    "SO2": ("7446-09-5", "SulfurDioxide", 64.064),
    "SO3": ("7446-11-9", "SulfurTrioxide", 80.063),
    "CS2": ("75-15-0", "CarbonDisulfide", 76.141),
    "HCN": ("74-90-8", "HydrogenCyanide", 27.025),
    "CO2": ("124-38-9", "CarbonDioxide", 44.010),
    "N2": ("7727-37-9", "Nitrogen", 28.014),
    "CH4": ("74-82-8", "Methane", 16.043),
    "C2H4": ("74-85-1", "Ethylene", 28.054),
    "C2H6": ("74-84-0", "Ethane", 30.069),
    "C2H2": ("74-86-2", "Acetylene", 26.038),
    "C3H6": ("115-07-1", "Propylene", 42.081),
    "C3H8": ("74-98-6", "Propane", 44.096),
    "iC4H10": ("75-28-5", "Isobutane", 58.122),
    "nC4H10": ("106-97-8", "n-Butane", 58.122),
    "nC5H12": ("109-66-0", "n-Pentane", 72.149),
    "nC6H14": ("110-54-3", "n-Hexane", 86.175),
    "nC7H16": ("142-82-5", "n-Heptane", 100.202),
    "nC8H18": ("111-65-9", "n-Octane", 114.229),
    "nC10H22": ("124-18-5", "n-Decane", 142.282),
    "cC6H12": ("110-82-7", "Cyclohexane", 84.159),
    "C6H6": ("71-43-2", "Benzene", 78.112),
    "C7H8": ("108-88-3", "Toluene", 92.138),
    "oC8H10": ("95-47-6", "o-Xylene", 106.165),
    "C8H8": ("100-42-5", "Styrene", 104.149),
    "C10H8": ("91-20-3", "Naphthalene", 128.171),
    "CH3OH": ("67-56-1", "Methanol", 32.042),
    "C2H5OH": ("64-17-5", "Ethanol", 46.068),
}

# label -> (path, equation, coefficient count, output unit, source, leadingTc)
#
# Two layout traps, both found by comparing against CoolProp rather than by
# reading the header:
#
#   * Perry Table 2-150 (heat of vaporisation) puts the CRITICAL TEMPERATURE in
#     the first numeric column, ahead of C1..C4. Reading it as a coefficient
#     silently produces a heat of vaporisation of the right shape and the wrong
#     magnitude.
#   * The eq-105 density table is tabulated in mol/m^3, not the kmol/m^3 the
#     DIPPR definition implies. That is a factor of 1000 and it does not
#     announce itself.
TABLES = {
    "liquidViscosity": (
        "Viscosity/Table 2-313 Viscosity of Inorganic and Organic Liquids.tsv",
        101, 5, "Pa*s", PERRY),
    "liquidConductivity": (
        "Thermal Conductivity/Table 2-315 Thermal Conductivity of Inorganic and Organic Liquids.tsv",
        100, 5, "W/(m*K)", PERRY),
    "liquidMolarDensity": (
        "Density/Perry Parameters 105.tsv",
        105, 4, "mol/m^3", PERRY),
    "liquidMolarHeatCapacity": (
        "Heat Capacity/Perry_Table_2-153_DIPPR_100.tsv",
        100, 5, "J/(kmol*K)", PERRY),
    "gasViscosity": (
        "Viscosity/Table 2-312 Vapor Viscosity of Inorganic and Organic Substances.tsv",
        102, 4, "Pa*s", PERRY),
    "gasConductivity": (
        "Thermal Conductivity/Table 2-314 Vapor Thermal Conductivity of Inorganic and Organic Substances.tsv",
        102, 4, "W/(m*K)", PERRY),
    "heatOfVaporisation": (
        "Phase Change/Table 2-150 Heats of Vaporization of Inorganic and Organic Liquids.tsv",
        106, 4, "kJ/kmol", PERRY, True),
}

# The 114 table has a different column layout: A B C D E Tmin Tmax, no Tc column.
TABLE_114 = ("Heat Capacity/Perry_Table_2-153_DIPPR_114.tsv", 114, 5, "J/(kmol*K)", PERRY)


def read_table(relative: str, count: int, leading_tc: bool = False) -> dict[str, dict]:
    """`count` is the number of COEFFICIENTS; a leading critical temperature is
    read as one extra leading value."""
    count = count + (1 if leading_tc else 0)
    path = DATA / relative
    if not path.exists():
        print(f"  missing table: {relative}")
        return {}

    rows = {}
    with open(path, encoding="utf-8", errors="replace") as fh:
        header = fh.readline().rstrip("\n").split("\t")
        # Coefficients start after CAS and the name column(s).
        for line in fh:
            cells = line.rstrip("\n").split("\t")
            if len(cells) < count + 2:
                continue
            cas = cells[0].strip()
            try:
                coefficients = [float(c) if c.strip() else 0.0
                                for c in cells[2:2 + count]]
                remaining = [c for c in cells[2 + count:] if c.strip()]
                t_min = float(remaining[0]) if len(remaining) >= 2 else None
                t_max = float(remaining[1]) if len(remaining) >= 2 else None
            except ValueError:
                continue
            rows[cas] = {"c": coefficients, "tMinK": t_min, "tMaxK": t_max}
    return rows


def evaluate(equation: int, c: list[float], t: float, tc: float | None = None) -> float | None:
    """Reference evaluation, used only for the self-check below."""
    try:
        if equation == 100:
            return sum(v * t ** i for i, v in enumerate(c))
        if equation == 101:
            return math.exp(c[0] + c[1] / t + c[2] * math.log(t) + c[3] * t ** c[4])
        if equation == 102:
            return c[0] * t ** c[1] / (1 + c[2] / t + c[3] / (t * t))
        if equation == 105:
            return c[0] / c[1] ** (1 + (1 - t / c[2]) ** c[3])
        if equation == 106:
            if tc is None or t >= tc:
                return None
            tr = t / tc
            return c[0] * (1 - tr) ** (c[1] + c[2] * tr + c[3] * tr ** 2 + c[4] * tr ** 3)
        if equation == 114:
            if tc is None or t >= tc:
                return None
            tau = 1 - t / tc
            a, b, cc, d = c[0], c[1], c[2], c[3]
            return (a * a / tau + b - 2 * a * cc * tau - a * d * tau ** 2
                    - cc * cc * tau ** 3 / 3 - cc * d * tau ** 4 / 2
                    - d * d * tau ** 5 / 5)
    except (ValueError, ZeroDivisionError, OverflowError):
        return None
    return None


def main() -> int:
    tables = {label: read_table(spec[0], spec[2], len(spec) > 5 and spec[5])
              for label, spec in TABLES.items()}
    table114 = read_table(TABLE_114[0], TABLE_114[2])

    species_out = []
    coverage = {label: 0 for label in TABLES}
    coverage["liquidMolarHeatCapacity114"] = 0

    for key, (cas, name, molar_mass) in SPECIES.items():
        # Forms 106 and 114 are written in reduced temperature, so a critical
        # temperature is not optional decoration for them - without it those
        # correlations cannot be evaluated at all.
        try:
            from chemicals.critical import Tc as critical_temperature
            tc = critical_temperature(cas)
        except Exception:
            tc = None

        entry = {"key": key, "cas": cas, "name": name,
                 "molarMass_g_mol": molar_mass, "tcK": tc, "correlations": {}}

        for label, spec in TABLES.items():
            row = tables[label].get(cas)
            if not row:
                continue
            equation, unit, source = spec[1], spec[3], spec[4]
            leading_tc = len(spec) > 5 and spec[5]
            coefficients = row["c"]
            table_tc = None
            if leading_tc:
                table_tc = coefficients[0]
                coefficients = coefficients[1:]
            record = {
                "equation": equation, "c": coefficients,
                "tMinK": row["tMinK"], "tMaxK": row["tMaxK"],
                "unit": unit, "source": source,
            }
            if table_tc:
                # Use the critical temperature the correlation was fitted with,
                # not a handbook value that may differ in the last digits.
                record["tcK"] = table_tc
            entry["correlations"][label] = record
            coverage[label] += 1

        # Form 114 is a fallback for liquid Cp where the 100 form is absent.
        if "liquidMolarHeatCapacity" not in entry["correlations"]:
            row = table114.get(cas)
            if row:
                entry["correlations"]["liquidMolarHeatCapacity"] = {
                    "equation": 114, "c": row["c"],
                    "tMinK": row["tMinK"], "tMaxK": row["tMaxK"],
                    "unit": TABLE_114[3], "source": TABLE_114[4],
                }
                coverage["liquidMolarHeatCapacity114"] += 1

        species_out.append(entry)

    # Preserve manual annotations. Re-running the import must never silently
    # discard a record that was investigated and flagged: verify_dippr.py found
    # the n-pentane viscosity defect once, and a rebuild that dropped the flag
    # would quietly return that bad record to service.
    preserved = 0
    if OUT.exists():
        previous = {s["key"]: s for s in json.loads(OUT.read_text(encoding="utf-8"))["species"]}
        for entry in species_out:
            old = previous.get(entry["key"])
            if not old:
                continue
            for name in list(entry["correlations"]):
                old_correlation = old.get("correlations", {}).get(name, {})
                # A record replaced by a fit to a reference correlation
                # (tools/fit_liquid_reference.py) is kept whole: the DIPPR table
                # it replaced is the one that was found wrong.
                if old_correlation.get("override"):
                    entry["correlations"][name] = old_correlation
                    preserved += 1
                    continue
                correlation = entry["correlations"][name]
                if old_correlation.get("singlePoint"):
                    correlation["singlePoint"] = True
                for field in ("suspect", "suspectReason"):
                    if field in old_correlation:
                        correlation[field] = old_correlation[field]
                        if field == "suspect":
                            preserved += 1

    document = {
        "schemaVersion": "1.0",
        "description": "DIPPR-form temperature-dependent correlations for liquids and vapours",
        "equationForms": {
            "100": "Y = A + B T + C T^2 + D T^3 + E T^4",
            "101": "Y = exp(A + B/T + C ln T + D T^E)",
            "102": "Y = A T^B / (1 + C/T + D/T^2)",
            "105": "Y = A / B^(1 + (1 - T/C)^D)   [molar, kmol/m^3]",
            "106": "Y = A (1 - Tr)^(B + C Tr + D Tr^2 + E Tr^3)   [needs Tc]",
            "114": "Y = A^2/t + B - 2ACt - ADt^2 - C^2 t^3/3 - CD t^4/2 - D^2 t^5/5, t = 1 - Tr",
        },
        "licence": (
            "Coefficients redistributed by the `chemicals` package under MIT. The values "
            "originate in Perry's Chemical Engineers' Handbook 8th ed. and the VDI Heat "
            "Atlas. Numerical data are facts, but this file is kept EXTERNAL to the "
            "assemblies and is not redistributed inside compiled software."
        ),
        "species": species_out,
    }

    OUT.write_text(json.dumps(document, indent=2), encoding="utf-8")

    print(f"{OUT.name}: {len(species_out)} species"
          + (f" ({preserved} manual flag(s) preserved)" if preserved else ""))
    for label, count in coverage.items():
        print(f"  {label:32s} {count:3d}")

    missing = {label: [s["key"] for s in species_out
                       if label not in s["correlations"]]
               for label in TABLES}
    for label, keys in missing.items():
        if keys:
            print(f"  missing {label}: {', '.join(keys)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
