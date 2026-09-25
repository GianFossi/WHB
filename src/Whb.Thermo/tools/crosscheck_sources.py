"""
Cross-checks the shipped species database against an independent thermochemical
source.

    python tools/crosscheck_sources.py /path/to/THERM_DAT.txt

Why this exists: the shipped Cp data comes from NASA CEA (Apache 2.0). The
Goos-Burcat-Ruscic database is an entirely independent compilation, but its
licence forbids inclusion in commercial software. It can still be used as a
VALIDATOR -- reading a file the user supplies, comparing, and reporting. Nothing
from it is written into the database, so no restricted data is ever
redistributed.

Two independent compilations agreeing to a fraction of a percent is far stronger
evidence than either one alone. Where they disagree, the species is worth
looking at by hand before it goes into a rating.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import nasa7  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
DB_PATH = ROOT / "data" / "species-database.json"

R_GAS = 8.31446261815324

# Temperatures spanning WHB service: cold end to SRU inlet.
TEMPERATURES = [400.0, 600.0, 773.15, 1000.0, 1400.0, 1673.15]

WARN = 0.03   # 3 % -- worth a look
FAIL = 0.10   # 10 % -- almost certainly a different species or isomer


def cp_from_db(species: dict, t: float) -> float | None:
    """kJ/(kg*K) from whichever model the database carries."""
    model = species["cpModel"]
    molar = species["molarMass_kg_kmol"]

    if model["kind"] == "nasa9":
        segments = model["nasa9Segments"]
        seg = next((s for s in segments if s["tMinK"] <= t <= s["tMaxK"]), None)
        if seg is None:
            return None
        a = seg["a"]
        cp_r = (a[0] / t**2 + a[1] / t + a[2] + a[3] * t
                + a[4] * t**2 + a[5] * t**3 + a[6] * t**4)
        return cp_r * R_GAS / (molar / 1000.0) / 1000.0

    if model["kind"] == "nasa7":
        segments = model["nasa7Segments"]
        seg = next((s for s in segments if s["tMinK"] <= t <= s["tMaxK"]), None)
        if seg is None:
            return None
        a = seg["a"]
        cp_r = a[0] + a[1] * t + a[2] * t**2 + a[3] * t**3 + a[4] * t**4
        return cp_r * R_GAS / (molar / 1000.0) / 1000.0

    if model["kind"] == "shomate":
        seg = next((s for s in model["segments"] if s["tMinK"] <= t <= s["tMaxK"]), None)
        if seg is None:
            return None
        x = t / 1000.0
        molar_cp = (seg["a"] + seg["b"] * x + seg["c"] * x**2
                    + seg["d"] * x**3 + seg["e"] / x**2)
        return molar_cp / molar

    return None


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    reference = nasa7.parse_file(sys.argv[1])
    print(f"reference: {len(reference)} records from {Path(sys.argv[1]).name}\n")

    db = json.loads(DB_PATH.read_text(encoding="utf-8"))

    agree, warn, fail, uncovered = [], [], [], []

    for species in db["species"]:
        key = species["key"]
        rec = nasa7.find(reference, key)
        if rec is None:
            uncovered.append(key)
            continue

        worst = 0.0
        worst_t = None
        compared = 0
        for t in TEMPERATURES:
            ours = cp_from_db(species, t)
            if ours is None:
                continue
            theirs = rec.cp_mass(t, species["molarMass_kg_kmol"]) / 1000.0
            compared += 1
            deviation = abs(ours - theirs) / theirs
            if deviation > worst:
                worst, worst_t = deviation, t

        if compared == 0:
            uncovered.append(key)
        elif worst > FAIL:
            fail.append((key, worst, worst_t))
        elif worst > WARN:
            warn.append((key, worst, worst_t))
        else:
            agree.append((key, worst))

    print(f"AGREE within {WARN:.0%} ({len(agree)}):")
    for key, worst in sorted(agree, key=lambda x: -x[1]):
        print(f"  {key:6s} worst deviation {worst:.2%}")

    if warn:
        print(f"\nWORTH A LOOK, {WARN:.0%}-{FAIL:.0%} ({len(warn)}):")
        for key, worst, t in warn:
            print(f"  {key:6s} {worst:.1%} at {t:.0f} K")

    if fail:
        print(f"\nDISAGREE by more than {FAIL:.0%} ({len(fail)}):")
        for key, worst, t in fail:
            print(f"  {key:6s} {worst:.1%} at {t:.0f} K")
        print("  -> probably a different isomer or a name collision; inspect both records")

    if uncovered:
        print(f"\nnot in the reference file ({len(uncovered)}): {', '.join(uncovered)}")

    print()
    if fail:
        print(f"{len(fail)} species disagree materially between two independent compilations")
        return 1
    print(f"{len(agree)} species confirmed against an independent compilation")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
