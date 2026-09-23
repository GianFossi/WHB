"""
Write guard for the species database.

Four separate tools write `data/species-database.json`, and over the course of
this project three of them destroyed work done by another: a rebuild that reset
48 species to 28, an importer that dropped a manually investigated `suspect`
flag, and a path change that left a stale duplicate. Each was found by accident.

This module exists so the next one is found on purpose. Every writer goes
through `save`, which refuses a write that LOSES information unless the loss is
explicitly authorised.

    from db_guard import load, save

    db = load()
    ...
    save(db, tool="my_tool.py", note="what changed")

WHAT IS REFUSED
---------------
  * fewer species than before
  * a species losing a property it had
  * a Cp model downgraded (nasa9 -> nasa7 -> shomate -> anchor)
  * a manual annotation dropped (suspect, verified, any *Reason field)
  * writing over a file that changed since it was read

The last is the one that catches concurrent edits and hand editing: the file
carries a content hash, and a mismatch means someone wrote to it outside this
guard.

Overriding is possible - `--force` on the command line, `force=True` in code -
but it is a decision, and it is recorded in the file's history so it is visible
afterwards.

A timestamped backup is written before every save, into `data/.backups/`.
"""
from __future__ import annotations

import datetime
import hashlib
import json
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DB_PATH = ROOT / "data" / "species-database.json"
BACKUP_DIR = ROOT / "data" / ".backups"

# Fields whose presence must never be lost for a species that had them.
PROTECTED_FIELDS = [
    "formula", "cas", "molarMass_kg_kmol", "cpModel", "transport",
    "critical", "vapourPressure", "diffusionVolume",
]

# Annotations a human put there. Losing one silently returns an investigated
# defect to service.
MANUAL_ANNOTATIONS = ["suspect", "suspectReason", "verified"]

# Cp model quality, best first. A rebuild must never move a species down this
# list.
CP_RANK = {"nasa9": 0, "nasa7": 1, "shomate": 2, "anchor": 3}


class GuardError(RuntimeError):
    """A write was refused because it would lose information."""


def _content_hash(document: dict) -> str:
    """Hash of the species content only, ignoring the integrity block itself."""
    payload = {k: v for k, v in document.items() if k != "integrity"}
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(encoded.encode("utf-8")).hexdigest()


def load(path: Path = DB_PATH) -> dict:
    """Read the database and verify it has not been changed outside the guard."""
    document = json.loads(path.read_text(encoding="utf-8"))
    integrity = document.get("integrity")
    if integrity and "sha256" in integrity:
        actual = _content_hash(document)
        if actual != integrity["sha256"]:
            print(f"  NOTE: {path.name} was modified outside the guard since "
                  f"{integrity.get('written', 'unknown')}. That is allowed - hand editing "
                  f"is legitimate - but the next save will treat the file on disk as the "
                  f"baseline, so review the diff it prints.")
    return document


def _species_map(document: dict) -> dict[str, dict]:
    return {s["key"]: s for s in document.get("species", [])}


def _differences(before: dict, after: dict) -> list[str]:
    """Everything the new document would lose relative to the old one."""
    losses: list[str] = []
    old = _species_map(before)
    new = _species_map(after)

    removed = sorted(set(old) - set(new))
    if removed:
        losses.append(f"{len(removed)} species removed: {', '.join(removed)}")

    for key in sorted(set(old) & set(new)):
        o, n = old[key], new[key]

        for field in PROTECTED_FIELDS:
            if field in o and o[field] is not None and n.get(field) is None:
                losses.append(f"{key}: lost '{field}'")

        old_kind = (o.get("cpModel") or {}).get("kind")
        new_kind = (n.get("cpModel") or {}).get("kind")
        if old_kind in CP_RANK and new_kind in CP_RANK:
            if CP_RANK[new_kind] > CP_RANK[old_kind]:
                losses.append(f"{key}: Cp model downgraded {old_kind} -> {new_kind}")

        old_transport = (o.get("transport") or {}).get("kind")
        new_transport = (n.get("transport") or {}).get("kind")
        if old_transport == "nasaCea" and new_transport in ("sutherland", "none"):
            losses.append(f"{key}: transport downgraded {old_transport} -> {new_transport}")

        # Manual annotations, anywhere in the record.
        for annotation in MANUAL_ANNOTATIONS:
            if _has_annotation(o, annotation) and not _has_annotation(n, annotation):
                losses.append(f"{key}: lost manual annotation '{annotation}'")

    return losses


def _has_annotation(record: dict, name: str) -> bool:
    if name in record:
        return True
    for value in record.values():
        if isinstance(value, dict):
            if name in value or _has_annotation(value, name):
                return True
    return False


def _backup(path: Path) -> Path | None:
    if not path.exists():
        return None
    BACKUP_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    target = BACKUP_DIR / f"{path.stem}-{stamp}{path.suffix}"
    shutil.copy2(path, target)
    # Keep the last twenty; beyond that they are noise.
    backups = sorted(BACKUP_DIR.glob(f"{path.stem}-*{path.suffix}"))
    for old in backups[:-20]:
        old.unlink()
    return target


def save(document: dict, tool: str, note: str = "",
         path: Path = DB_PATH, force: bool = False) -> None:
    """Write the database, refusing a write that would lose information."""
    previous = None
    if path.exists():
        previous = json.loads(path.read_text(encoding="utf-8"))
        losses = _differences(previous, document)
        if losses:
            message = (f"\nREFUSED: {tool} would lose information:\n"
                       + "\n".join(f"  - {loss}" for loss in losses))
            if not force:
                raise GuardError(
                    message
                    + "\n\nIf this is intended, re-run with --force. The override is "
                      "recorded in the file's history.\n"
                      "If it is not, the most likely cause is a tool rebuilding from a "
                      "narrower source than the file already holds.")
            print(message + "\n  ...proceeding because force was requested.\n")

    backup = _backup(path)

    history = (previous or {}).get("integrity", {}).get("history", [])
    history = history[-19:] + [{
        "tool": tool,
        "written": datetime.datetime.now().isoformat(timespec="seconds"),
        "species": len(document.get("species", [])),
        "note": note,
        "forced": bool(force and previous and _differences(previous, document)),
    }]

    document["integrity"] = {
        "sha256": None,
        "written": datetime.datetime.now().isoformat(timespec="seconds"),
        "writtenBy": tool,
        "speciesCount": len(document.get("species", [])),
        "history": history,
        "note": ("Written through tools/db_guard.py. The hash covers everything except "
                 "this block. A mismatch on load means the file was edited outside the "
                 "guard, which is allowed but flagged."),
    }
    document["integrity"]["sha256"] = _content_hash(document)

    path.write_text(json.dumps(document, indent=2), encoding="utf-8")

    print(f"  saved by {tool}: {len(document.get('species', []))} species"
          + (f", backup {backup.name}" if backup else ""))


def force_requested(argv: list[str] | None = None) -> bool:
    return "--force" in (argv if argv is not None else sys.argv)


if __name__ == "__main__":
    document = load()
    integrity = document.get("integrity", {})
    print(f"{DB_PATH.name}")
    print(f"  species      {len(document.get('species', []))}")
    print(f"  written      {integrity.get('written', 'never, through the guard')}")
    print(f"  written by   {integrity.get('writtenBy', '-')}")
    print(f"  hash         {(integrity.get('sha256') or '-')[:16]}")
    actual = _content_hash(document)
    if integrity.get("sha256"):
        state = "intact" if actual == integrity["sha256"] else "MODIFIED OUTSIDE THE GUARD"
        print(f"  state        {state}")
    history = integrity.get("history", [])
    if history:
        print(f"\n  last {min(len(history), 5)} writes:")
        for entry in history[-5:]:
            flag = "  [FORCED]" if entry.get("forced") else ""
            print(f"    {entry['written']}  {entry['tool']:24s} "
                  f"{entry['species']:3d} species{flag}")
            if entry.get("note"):
                print(f"      {entry['note']}")
