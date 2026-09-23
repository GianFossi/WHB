"""
NASA-7 (CHEMKIN / Burcat) thermodynamic polynomial parser.

Record layout -- 4 fixed-column lines of 80 characters:

  line 1  cols  1-18  species name
                25-44  elemental composition (4 x [2-char element, 3-digit count])
                45     phase (G/L/S)
                46-55  T_low       56-65  T_high      66-73  T_common
                80     '1'
  line 2  5 x 15-char floats: a1..a5   HIGH temperature range   col 80 = '2'
  line 3  5 x 15-char floats: a6,a7 (high), a1,a2,a3 (low)      col 80 = '3'
  line 4  4 x 15-char floats: a4,a5,a6,a7 (low)                 col 80 = '4'

Note the ordering trap: the FIRST coefficient block is the HIGH-temperature one.
Getting this backwards produces plausible-looking but wrong Cp -- which is exactly
why the golden test against CoolProp exists.

  Cp/R    = a1 + a2*T + a3*T^2 + a4*T^3 + a5*T^4
  H/(R*T) = a1 + a2*T/2 + a3*T^2/3 + a4*T^3/4 + a5*T^4/5 + a6/T
  S/R     = a1*ln(T) + a2*T + a3*T^2/2 + a4*T^3/3 + a5*T^4/4 + a7
"""
from __future__ import annotations

from dataclasses import dataclass

R_GAS = 8.31446261815324  # J/(mol*K)


@dataclass
class Nasa7Record:
    name: str
    phase: str
    t_low: float
    t_high: float
    t_common: float
    low: list[float]   # a1..a7 valid on [t_low, t_common]
    high: list[float]  # a1..a7 valid on [t_common, t_high]

    def cp_over_r(self, t: float) -> float:
        a = self.low if t <= self.t_common else self.high
        return a[0] + a[1] * t + a[2] * t**2 + a[3] * t**3 + a[4] * t**4

    def cp_mass(self, t: float, molar_mass_kg_kmol: float) -> float:
        """Cp in J/(kg*K)."""
        return self.cp_over_r(t) * R_GAS / (molar_mass_kg_kmol / 1000.0)

    def to_segments(self, source: str) -> list[dict]:
        return [
            {"tMinK": self.t_low, "tMaxK": self.t_common, "a": self.low, "source": source},
            {"tMinK": self.t_common, "tMaxK": self.t_high, "a": self.high, "source": source},
        ]


def _floats(line: str, count: int) -> list[float]:
    """Read `count` fixed-width 15-char floats. Falls back to whitespace splitting
    for the many real-world files that are not column-exact."""
    values = []
    ok = True
    for i in range(count):
        chunk = line[i * 15:(i + 1) * 15].strip()
        if not chunk:
            ok = False
            break
        try:
            values.append(float(chunk))
        except ValueError:
            ok = False
            break
    if ok and len(values) == count:
        return values
    parts = line.split()
    if len(parts) < count:
        raise ValueError(f"expected {count} coefficients, got {len(parts)}: {line!r}")
    return [float(p) for p in parts[:count]]


def parse_record(lines: list[str]) -> Nasa7Record:
    if len(lines) != 4:
        raise ValueError(f"NASA-7 record needs 4 lines, got {len(lines)}")

    header = lines[0].rstrip("\n")
    name = header[0:18].strip().split()[0]
    phase = header[44:45].strip() or "G"

    temps = header[45:73].split()
    if len(temps) < 3:
        temps = header[45:].split()[:3]
    t_low, t_high, t_common = (float(temps[0]), float(temps[1]), float(temps[2]))

    high_1_5 = _floats(lines[1], 5)
    mid = _floats(lines[2], 5)     # a6,a7 (high) + a1,a2,a3 (low)
    low_4_7 = _floats(lines[3], 4)  # a4,a5,a6,a7 (low)

    high = high_1_5 + mid[0:2]
    low = mid[2:5] + low_4_7

    if len(high) != 7 or len(low) != 7:
        raise ValueError(f"{name}: malformed coefficient block")

    return Nasa7Record(name, phase, t_low, t_high, t_common, low, high)


def _marker(line: str) -> str:
    """The sequence digit in column 80. Real files drift, so fall back to the
    last non-blank character."""
    if len(line) >= 80 and line[79] in "1234":
        return line[79]
    stripped = line.rstrip()
    return stripped[-1] if stripped and stripped[-1] in "1234" else ""


def parse_file(path: str) -> dict[str, Nasa7Record]:
    """Parse a CHEMKIN-style thermo file.

    Records are located by the column-80 sequence markers 1-2-3-4 rather than by
    blindly buffering four lines at a time. Every real database carries pages of
    front matter, licence text and comments; buffering blindly turns those into
    hundreds of bogus 'malformed record' reports and, worse, can resynchronise
    mid-record and emit a species built from two different entries."""
    records: dict[str, Nasa7Record] = {}

    with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
        lines = [l.rstrip("\r\n") for l in fh]

    i = 0
    skipped = 0
    while i < len(lines) - 3:
        line = lines[i]
        stripped = line.strip()
        if not stripped or stripped.startswith("!") or _marker(line) != "1":
            i += 1
            continue

        block = lines[i:i + 4]
        if [_marker(b) for b in block] != ["1", "2", "3", "4"]:
            i += 1
            continue

        try:
            rec = parse_record(block)
            # Case matters: Co is cobalt, CO is carbon monoxide.
            records[rec.name] = rec
            i += 4
        except (ValueError, IndexError):
            skipped += 1
            i += 1

    if skipped:
        print(f"  {skipped} malformed record(s) skipped in {path}")
    return records


def find(records: dict[str, Nasa7Record], name: str) -> "Nasa7Record | None":
    """Exact match first; unambiguous case-insensitive match as fallback."""
    if name in records:
        return records[name]
    matches = [k for k in records if k.upper() == name.upper()]
    return records[matches[0]] if len(matches) == 1 else None


def _is_temperature_header(line: str) -> bool:
    """The 3-number line (e.g. '300.000 1000.000 5000.000') that follows THERMO."""
    parts = line.split()
    if len(parts) != 3:
        return False
    try:
        [float(p) for p in parts]
        return True
    except ValueError:
        return False


def format_record(rec: Nasa7Record) -> list[str]:
    """Inverse of parse_record -- used by the round-trip self-test."""
    def block(values):
        return "".join(f"{v:15.8E}" for v in values)

    l1 = f"{rec.name:<18}{'':6}{'':20}{rec.phase:<1}" \
         f"{rec.t_low:10.3f}{rec.t_high:10.3f}{rec.t_common:8.2f}"
    l1 = f"{l1:<79}1"
    l2 = f"{block(rec.high[0:5]):<79}2"
    l3 = f"{block(rec.high[5:7] + rec.low[0:3]):<79}3"
    l4 = f"{block(rec.low[3:7]):<79}4"
    return [l1, l2, l3, l4]


if __name__ == "__main__":
    # Round-trip self-test. Uses synthetic coefficients so it validates the
    # PARSER, not any particular species data.
    original = Nasa7Record(
        name="TESTGAS", phase="G",
        t_low=200.0, t_high=6000.0, t_common=1000.0,
        low=[3.1, 1.2e-3, -4.3e-7, 5.4e-11, -6.5e-15, -1.0e4, 3.9],
        high=[2.9, 1.5e-3, -5.0e-7, 6.0e-11, -7.0e-15, -1.1e4, 4.2],
    )
    lines = format_record(original)
    assert all(len(l) == 80 for l in lines), [len(l) for l in lines]

    back = parse_record(lines)
    assert back.name == original.name
    assert back.t_common == original.t_common
    for got, want in zip(back.low, original.low):
        assert abs(got - want) < 1e-12, (got, want)
    for got, want in zip(back.high, original.high):
        assert abs(got - want) < 1e-12, (got, want)

    # Segment selection must switch at t_common.
    assert abs(back.cp_over_r(500.0) - (
        original.low[0] + original.low[1] * 500 + original.low[2] * 500**2
        + original.low[3] * 500**3 + original.low[4] * 500**4)) < 1e-12

    print("nasa7.py round-trip self-test OK")
