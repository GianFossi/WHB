"""
Parser for NASA CEA `trans.inp` transport property coefficients.

Record layout:

    Ar                                V3C3  BICH ET AL (1990)
     V  200.0   1000.0   A            B             C            D
     V 1000.0   5000.0   ...
     C  200.0   1000.0   ...

The header's `VnCm` field gives the number of viscosity and conductivity
intervals. Each data line is one interval:

    ln(eta)    = A ln T + B/T + C/T^2 + D      eta   in micropoise
    ln(lambda) = A ln T + B/T + C/T^2 + D      lambda in microwatt/(cm*K)

Conversions to SI:  1 uP = 1e-7 Pa*s,  1 uW/(cm*K) = 1e-4 W/(m*K).

Why this matters here: the Sutherland fits taken from the WHB manual are two
parameter and degrade badly above about 1000 degC, which is exactly where an SRU
or reformer inlet sits. These NASA correlations are four-parameter and are
tabulated to 5000 K (15000 K for some species), so they cover the whole WHB
envelope with margin.
"""
from __future__ import annotations

import math
import re
from dataclasses import dataclass, field

# Columns 1-15 hold the species; 16-30 hold a SECOND species for binary
# interaction records. Ignoring that field merges the pair data into the pure
# species and produces a record with three times too many intervals.
_HEADER = re.compile(r"^(.{16})(.{16})\s*V(\d)C(\d)")
_DATA = re.compile(r"^\s*([VC])\s+(\d+\.\d+)\s+(\d+\.\d+)\s+(.*)$")
_NUMBER = re.compile(r"[-+]?\d*\.?\d+[DdEe][ ]?[-+]?\d+")


def _fortran(token: str) -> float:
    return float(token.strip().replace(" ", "").replace("D", "E").replace("d", "e"))


@dataclass
class TransportInterval:
    t_min: float
    t_max: float
    a: float
    b: float
    c: float
    d: float

    def _ln_value(self, t: float) -> float:
        return self.a * math.log(t) + self.b / t + self.c / (t * t) + self.d

    def viscosity(self, t: float) -> float:
        """Pa*s."""
        return math.exp(self._ln_value(t)) * 1e-7

    def conductivity(self, t: float) -> float:
        """W/(m*K)."""
        return math.exp(self._ln_value(t)) * 1e-4


@dataclass
class TransportRecord:
    name: str
    reference: str
    viscosity: list[TransportInterval] = field(default_factory=list)
    conductivity: list[TransportInterval] = field(default_factory=list)

    def _select(self, intervals, t):
        if not intervals:
            return None
        for iv in intervals:
            if iv.t_min <= t <= iv.t_max:
                return iv
        return min(intervals, key=lambda iv: min(abs(t - iv.t_min), abs(t - iv.t_max)))

    def mu(self, t: float) -> float | None:
        iv = self._select(self.viscosity, t)
        return iv.viscosity(t) if iv else None

    def k(self, t: float) -> float | None:
        iv = self._select(self.conductivity, t)
        return iv.conductivity(t) if iv else None

    def to_json(self, source: str) -> dict:
        def block(intervals):
            return [{"tMinK": iv.t_min, "tMaxK": iv.t_max,
                     "a": iv.a, "b": iv.b, "c": iv.c, "d": iv.d,
                     "source": source}
                    for iv in intervals]
        return {"kind": "nasaCea",
                "viscosity": block(self.viscosity),
                "conductivity": block(self.conductivity)}


def parse_file(path: str) -> dict[str, TransportRecord]:
    records: dict[str, TransportRecord] = {}
    current: TransportRecord | None = None

    with open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            line = raw.rstrip("\r\n")
            if not line.strip():
                continue

            data = _DATA.match(line)
            if data and current is not None:
                kind, t_min, t_max, tail = data.groups()
                numbers = [_fortran(t) for t in _NUMBER.findall(tail)]
                if len(numbers) < 4:
                    continue
                interval = TransportInterval(float(t_min), float(t_max), *numbers[:4])
                (current.viscosity if kind == "V" else current.conductivity).append(interval)
                continue

            header = _HEADER.match(line)
            if header:
                name = header.group(1).strip()
                partner = header.group(2).strip()
                if partner and partner != name:
                    current = None      # binary interaction pair, not a pure species
                    continue
                current = TransportRecord(name=name, reference=line[38:].strip())
                records[name] = current

    return records


def find(records: dict[str, TransportRecord], name: str) -> TransportRecord | None:
    """Exact match first. `Co` is cobalt, `CO` is carbon monoxide."""
    if name in records:
        return records[name]
    matches = [k for k in records if k.upper() == name.upper()]
    return records[matches[0]] if len(matches) == 1 else None


if __name__ == "__main__":
    import sys
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(1)

    recs = parse_file(sys.argv[1])
    print(f"parsed {len(recs)} transport records\n")

    # Spot checks against handbook values at 300 K.
    checks = [("Ar", 2.26e-5, 0.0177), ("N2", 1.78e-5, 0.0259),
              ("CO2", 1.50e-5, 0.0166), ("H2O", 9.8e-6, 0.0185)]
    print(f"{'spec':6s} {'mu calc':>10s} {'mu ref':>10s} {'k calc':>9s} {'k ref':>9s}")
    for name, mu_ref, k_ref in checks:
        r = find(recs, name)
        if not r:
            print(f"{name:6s} MISSING")
            continue
        mu, k = r.mu(300.0), r.k(300.0)
        print(f"{name:6s} {mu:10.3e} {mu_ref:10.3e} {k:9.4f} {k_ref:9.4f}"
              f"   ({(mu-mu_ref)/mu_ref:+.1%}, {(k-k_ref)/k_ref:+.1%})")
