"""
NASA-9 (CEA / PAC / Goos-Burcat-Ruscic) thermodynamic polynomial parser.

Record layout:

  line 1   species name, then free-text comment / reference
  line 2   col 1-2  number of temperature intervals
           col 4-9  reference date code
           col 11-50 elemental composition (5 x [2-char element, 6-char count])
           col 51   phase (0 = gas)
           col 52-65 molar mass [g/mol]
           col 66-80 enthalpy of formation at 298.15 K [J/mol]
  then, per interval:
           header   T_min, T_max, n_coeff, seven exponents, H(298)-H(0) [J/mol]
           line A   a1..a5      (5 x 16 chars, Fortran D exponent notation)
           line B   a6, a7, 0, b1, b2

Property equations (exponents -2,-1,0,1,2,3,4):

  Cp/R    = a1 T^-2 + a2 T^-1 + a3 + a4 T + a5 T^2 + a6 T^3 + a7 T^4
  H/(R T) = -a1 T^-2 + a2 ln(T)/T + a3 + a4 T/2 + a5 T^2/3 + a6 T^3/4
            + a7 T^4/5 + b1/T
  S/R     = -a1 T^-2/2 - a2 T^-1 + a3 ln(T) + a4 T + a5 T^2/2 + a6 T^3/3
            + a7 T^4/4 + b2

Why NASA-9 rather than NASA-7: the extra T^-2 and T^-1 terms fit the low
temperature range far better (NASA-7 typically starts at 200-300 K, NASA-9 at
50 K), and the Goos-Burcat-Ruscic revision carries formation enthalpies updated
from the Active Thermochemical Tables.

LICENCE: the Goos-Burcat-Ruscic database is free for non-commercial use and may
not be redistributed inside commercial software. This parser reads a file the
user supplies; no coefficients are bundled with this repository, and the merged
output is written to an EXTERNAL json file that is loaded at runtime rather than
embedded in the assembly.
"""
from __future__ import annotations

import re
from dataclasses import dataclass

R_GAS = 8.31446261815324  # J/(mol*K)

# NASA-9 interval header. NEWNASA-style files separate T_max from the coefficient
# count with a space; NASA's own thermo.inp runs them together ("1000.0007"), so
# the separator is optional.
_INTERVAL = re.compile(r"^\s*\d+\.\d+\s+\d+\.\d+\s*\d\s+-2\.0")
_HEADER = re.compile(r"^\s*([1-9])\s+\S")


def _fortran(token: str) -> float:
    """Fortran D exponent notation -> float."""
    return float(token.strip().replace("D", "E").replace("d", "e"))


_NUMBER = re.compile(r"[-+]?\d*\.?\d+[DdEe][-+]?\d+")


def _coefficients(line: str, count: int = 5) -> list[float]:
    """Read `count` coefficients from a NASA-9 data line.

    Tokenising on the mandatory D exponent is the primary strategy, not the
    fallback: real copies of this database drift off the nominal 16-character
    field width, and a fixed-column read then silently splits a number in two
    (producing values like '3-1.829145268E+0'). The exponent marker makes each
    token unambiguous regardless of spacing."""
    tokens = _NUMBER.findall(line)
    if len(tokens) >= count:
        return [_fortran(t) for t in tokens[:count]]

    values = []
    for i in range(count):
        chunk = line[i * 16:(i + 1) * 16].strip()
        if not chunk:
            break
        try:
            values.append(_fortran(chunk))
        except ValueError:
            break
    return values


@dataclass
class Nasa9Interval:
    t_min: float
    t_max: float
    a: list[float]   # a1..a7
    b: list[float]   # b1, b2

    def cp_over_r(self, t: float) -> float:
        a = self.a
        return (a[0] * t**-2 + a[1] / t + a[2] + a[3] * t
                + a[4] * t**2 + a[5] * t**3 + a[6] * t**4)

    def h_over_rt(self, t: float) -> float:
        import math
        a = self.a
        return (-a[0] * t**-2 + a[1] * math.log(t) / t + a[2] + a[3] * t / 2
                + a[4] * t**2 / 3 + a[5] * t**3 / 4 + a[6] * t**4 / 5
                + self.b[0] / t)


@dataclass
class Nasa9Record:
    name: str
    comment: str
    molar_mass: float          # g/mol
    hf298: float               # J/mol
    intervals: list[Nasa9Interval]

    def interval_for(self, t: float) -> Nasa9Interval:
        for iv in self.intervals:
            if iv.t_min <= t <= iv.t_max:
                return iv
        return min(self.intervals,
                   key=lambda iv: min(abs(t - iv.t_min), abs(t - iv.t_max)))

    def cp_mass(self, t: float) -> float:
        """Cp in J/(kg*K)."""
        return self.interval_for(t).cp_over_r(t) * R_GAS / (self.molar_mass / 1000.0)

    def to_segments(self, source: str) -> list[dict]:
        """Drop the sub-200 K intervals: a WHB never sees them, and carrying them
        would only widen the JSON for no engineering benefit."""
        return [{"tMinK": iv.t_min, "tMaxK": iv.t_max,
                 "a": list(iv.a), "b": list(iv.b), "source": source}
                for iv in self.intervals if iv.t_max > 250.0]


def find(records: dict[str, "Nasa9Record"], name: str) -> "Nasa9Record | None":
    """Look up a species, exact match first.

    Case matters in chemical formulae and the database exploits that: `Co` is
    cobalt and `CO` is carbon monoxide. Upper-casing the key silently replaces
    one with the other -- which produced a 21 % Cp error before this was caught.
    A case-insensitive match is offered only as a fallback, and only when it is
    unambiguous."""
    if name in records:
        return records[name]
    matches = [k for k in records if k.upper() == name.upper()]
    return records[matches[0]] if len(matches) == 1 else None


def parse_file(path: str) -> dict[str, Nasa9Record]:
    with open(path, encoding="utf-8", errors="replace") as fh:
        lines = fh.read().split("\n")

    records: dict[str, Nasa9Record] = {}
    i = 0
    while i < len(lines) - 3:
        line = lines[i]

        # A record header is a line starting with the interval count, whose
        # following line is an interval header.
        m = _HEADER.match(line)
        if not m or not _INTERVAL.match(lines[i + 1] if i + 1 < len(lines) else ""):
            i += 1
            continue

        name_line = lines[i - 1] if i > 0 else ""
        name = name_line.split()[0] if name_line.strip() else ""
        if not name or name.startswith("!"):
            i += 1
            continue

        # Molar mass and Hf298 are the last two numeric fields. Reading them by
        # regex rather than fixed columns survives the minor column drift that
        # real copies of this database contain.
        tail = re.findall(r"[-+]?\d+\.\d+", line[50:])
        if len(tail) < 2:
            i += 1
            continue
        try:
            n_intervals = int(m.group(1))
            molar_mass = float(tail[-2])
            hf298 = float(tail[-1])
        except ValueError:
            i += 1
            continue
        if molar_mass <= 0.0:
            i += 1
            continue

        intervals = []
        j = i + 1
        ok = True
        for _ in range(n_intervals):
            if j + 2 >= len(lines) or not _INTERVAL.match(lines[j]):
                ok = False
                break
            parts = lines[j].split()
            t_min, t_max = float(parts[0]), float(parts[1])
            first = _coefficients(lines[j + 1], 5)
            # The second data line is a6, a7, (unused slot), b1, b2. In
            # thermo.inp the unused slot is blank rather than 0.0, so the line
            # yields four numbers instead of five - both layouts occur in the
            # wild and both are accepted here.
            second = _NUMBER.findall(lines[j + 2])
            second = [_fortran(t) for t in second]
            if len(first) != 5 or len(second) not in (4, 5):
                ok = False
                break
            if len(second) == 5:
                a67, b12 = second[0:2], [second[3], second[4]]
            else:
                a67, b12 = second[0:2], [second[2], second[3]]
            intervals.append(Nasa9Interval(
                t_min=t_min, t_max=t_max,
                a=first + a67,
                b=b12))
            j += 3

        if ok and intervals:
            records[name] = Nasa9Record(
                name=name, comment=name_line[len(name):].strip(),
                molar_mass=molar_mass, hf298=hf298, intervals=intervals)
            i = j
        else:
            i += 1

    return records


# Species this project needs, in the naming used by the database.
WHB_SPECIES = ["H2", "N2", "CH4", "CO", "CO2", "H2O", "NH3", "Ar", "H2S", "SO2",
               "COS", "CS2", "S2", "S6", "S8", "C2H4", "C2H6", "C3H6", "C3H8",
               "C2H2", "C6H6", "C7H8", "NO", "NO2", "N2O", "SO3", "HCN", "He"]


def _main() -> int:
    """Scan a NASA-9 file and report coverage of the WHB species set.

        python tools/nasa9.py path/to/file.txt
    """
    import sys
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    records = parse_file(sys.argv[1])
    print(f"parsed {len(records)} records\n")

    resolved = {s: find(records, s) for s in WHB_SPECIES}
    found = [s for s in WHB_SPECIES if resolved[s] is not None]
    missing = [s for s in WHB_SPECIES if resolved[s] is None]

    print(f"present ({len(found)}):")
    for s in found:
        rec = resolved[s]
        print(f"  {s:6s} M = {rec.molar_mass:8.3f}  {len(rec.intervals)} intervals"
              f"  | {rec.comment[:55]}")

    print(f"\nmissing ({len(missing)}): {', '.join(missing) or '-'}")

    if missing:
        print("\nA record whose comment mentions 'excited', 'singlet', 'cyclo', "
              "'anion', 'cation' or an isomer name is NOT the ground state and "
              "must not be used for process gas properties.")
    return 0


if __name__ == "__main__":
    raise SystemExit(_main())
