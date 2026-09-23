"""
Schema 3.0 rules for data/species-database.json, shared by every writer.

db_guard.save() calls normalise() and validate() on each write, so an importer
that only knows about Cp or transport still produces a complete 3.0 record:

  * id          - equal to key, and immutable once written
  * family      - from the record, else from FAMILY below; an unknown new
                  species must be given one explicitly
  * synonyms    - from the record, else from SYNONYMS below
  * elements    - parsed from the formula
  * dataQuality - each group's level follows the kind of data present; a level
                  is recomputed only when that group's data kind changes, so a
                  deliberate manual grade survives unrelated edits. The
                  validation status is never touched here: it records a check
                  someone actually made.

The F# loader implies the same levels for schema 2.x files
(SpeciesDatabase.impliedQuality); keep the two in step.
"""
from __future__ import annotations

import re

SCHEMA_VERSION = "3.0"

REFERENCE_STATE = {
    "temperatureK": 298.15,
    "pressureBar": 1.0,
    "enthalpyConvention": "NASA-9 absolute: H(298.15 K) equals the standard formation "
                          "enthalpy, elements in their reference state at zero",
    "entropyConvention": "NASA-9 absolute (third-law) entropy at the 1 bar standard state",
}

FAMILIES = {"PermanentGas", "Hydrocarbon", "Oxygenate", "SulfurCompound", "NitrogenCompound",
            "Water", "Inert", "AcidGas", "Radical", "Other"}

# Classification of the species known when schema 3.0 was introduced.
# AcidGas is reserved for the gas-treating acid gases CO2 and H2S.
FAMILY = {
    "H2": "PermanentGas", "N2": "PermanentGas", "O2": "PermanentGas", "CO": "PermanentGas",
    "Ar": "Inert", "He": "Inert",
    "H2O": "Water",
    "CO2": "AcidGas", "H2S": "AcidGas",
    "CH4": "Hydrocarbon", "C2H2": "Hydrocarbon", "C2H4": "Hydrocarbon", "C2H6": "Hydrocarbon",
    "C3H6": "Hydrocarbon", "C3H8": "Hydrocarbon", "C6H6": "Hydrocarbon", "C7H8": "Hydrocarbon",
    "CH3OH": "Oxygenate", "DME": "Oxygenate", "HCHO": "Oxygenate",
    "COS": "SulfurCompound", "CS2": "SulfurCompound", "SO2": "SulfurCompound",
    "SO3": "SulfurCompound",
    "S2": "SulfurCompound", "S3": "SulfurCompound", "S4": "SulfurCompound",
    "S5": "SulfurCompound", "S6": "SulfurCompound", "S7": "SulfurCompound",
    "S8": "SulfurCompound",
    "NH3": "NitrogenCompound", "HCN": "NitrogenCompound", "NO": "NitrogenCompound",
    "NO2": "NitrogenCompound", "N2O": "NitrogenCompound",
    "H": "Radical", "O": "Radical", "N": "Radical", "OH": "Radical", "HO2": "Radical",
    "CH3": "Radical", "CN": "Radical", "CS": "Radical", "NH2": "Radical", "S1": "Radical",
    "SH": "Radical", "SO": "Radical",
}

SYNONYMS = {
    "H2": ["Hydrogen"], "N2": ["Nitrogen"], "O2": ["Oxygen"], "CO": ["Carbon monoxide"],
    "CO2": ["Carbon dioxide"], "H2O": ["Water", "Steam", "Water vapour"],
    "Ar": ["Argon"], "He": ["Helium"], "CH4": ["Methane"], "NH3": ["Ammonia"],
    "H2S": ["Hydrogen sulfide", "Hydrogen sulphide"], "SO2": ["Sulfur dioxide", "Sulphur dioxide"],
    "SO3": ["Sulfur trioxide", "Sulphur trioxide"], "COS": ["Carbonyl sulfide", "OCS"],
    "CS2": ["Carbon disulfide"], "C2H2": ["Acetylene", "Ethyne"], "C2H4": ["Ethylene", "Ethene"],
    "C2H6": ["Ethane"], "C3H6": ["Propylene", "Propene"], "C3H8": ["Propane"],
    "C6H6": ["Benzene"], "C7H8": ["Toluene"], "CH3OH": ["Methanol"],
    "DME": ["Dimethyl ether"], "HCHO": ["Formaldehyde"], "HCN": ["Hydrogen cyanide"],
    "NO": ["Nitric oxide"], "NO2": ["Nitrogen dioxide"], "N2O": ["Nitrous oxide"],
    "S1": ["Atomic sulfur"],
}

_TOKEN = re.compile(r"([A-Z][a-z]*)(\d*)")

# Infrared activity: homonuclear diatomics and monatomics are transparent.
TRANSPARENT = {"H2", "N2", "O2", "Ar", "He", "H", "O", "N",
               "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8"}
# Radiating species that a model here covers, and the models that cover them.
COVERED = {
    "H2O": ["Smith, Shen & Friedman (1982) WSGG",
            "Leckner (1972) form, fitted to the Smith WSGG model (not independent of it)"],
    "CO2": ["Smith, Shen & Friedman (1982) WSGG",
            "Leckner (1972) form, fitted to the Smith WSGG model (not independent of it)"],
}


# EOS parameters beyond the critical constants. Water is the one species whose
# second virial coefficient must NOT come from corresponding states: its strong
# association makes the Pitzer correlation wrong by tens of percent.
EOS_PARAMETERS = {
    "H2O": [{
        "eos": "Virial",
        "secondVirial": "IAPWS-IF97 region 2, dilute-gas limit",
        "source": "IAPWS-IF97 (Wagner et al. 2000); replaces the Pitzer correlation for water",
    }],
}


# ---------- screening classifications (step 7) ----------
# Qualitative flags only. They say which damage mechanisms and hazards a species
# can drive, so a rating knows what to check; they are not verdicts, and they
# were not transcribed from an SDS.
SCREENING_SOURCE = ("screening classification by chemistry, not transcribed from an SDS; "
                    "confirm against the supplier SDS / GHS classification before use")
RADICAL_NOTE = "transient radical or atom: screen the stable species it forms instead"

_M = ("hydrogenService", "nitriding", "carburizing", "metalDusting", "sulfidation", "oxidation")
_MATERIAL = {
    "H2": ({"hydrogenService"}, "high-temperature hydrogen attack and embrittlement (API RP 571 / 941)"),
    "H2S": ({"hydrogenService", "sulfidation"},
            "sulfidation at high temperature; wet H2S cracking (HIC / SSC) below the water dew point"),
    "COS": ({"sulfidation"}, ""), "CS2": ({"sulfidation"}, ""),
    "S2": ({"sulfidation"}, ""), "S3": ({"sulfidation"}, ""), "S4": ({"sulfidation"}, ""),
    "S5": ({"sulfidation"}, ""), "S6": ({"sulfidation"}, ""), "S7": ({"sulfidation"}, ""),
    "S8": ({"sulfidation"}, ""),
    "SO2": ({"sulfidation", "oxidation"}, "mixed oxidation-sulfidation at high temperature"),
    "SO3": ({"oxidation"}, "sulfuric acid dew-point corrosion on cold surfaces"),
    "NH3": ({"nitriding"}, "nitriding of steels above about 350 degC"),
    "CO": ({"carburizing", "metalDusting"}, "metal dusting in the 400-800 degC band"),
    "CH4": ({"carburizing"}, ""), "C2H2": ({"carburizing"}, ""), "C2H4": ({"carburizing"}, ""),
    "C2H6": ({"carburizing"}, ""), "C3H6": ({"carburizing"}, ""), "C3H8": ({"carburizing"}, ""),
    "C6H6": ({"carburizing"}, ""), "C7H8": ({"carburizing"}, ""),
    "HCN": ({"carburizing", "nitriding"}, "nitrocarburizing potential"),
    "O2": ({"oxidation"}, ""),
    "H2O": ({"oxidation"}, "steam oxidation"),
    "CO2": ({"oxidation"}, ""),
    "NO": ({"oxidation"}, ""), "NO2": ({"oxidation"}, ""), "N2O": ({"oxidation"}, ""),
    "N2": (set(), "can nitride some high-alloy materials well above 1000 degC"),
    "Ar": (set(), ""), "He": (set(), ""),
    "CH3OH": (set(), "decomposes towards CO + H2 at high temperature: screen as syngas"),
    "DME": (set(), "decomposes towards CO + H2 at high temperature: screen as syngas"),
    "HCHO": (set(), "decomposes towards CO + H2 at high temperature: screen as syngas"),
}

_FLAMMABLE = {"H2", "CO", "CH4", "C2H2", "C2H4", "C2H6", "C3H6", "C3H8", "C6H6", "C7H8",
              "NH3", "H2S", "COS", "CS2", "CH3OH", "DME", "HCHO", "HCN",
              "S2", "S3", "S4", "S5", "S6", "S7", "S8"}
_TOXIC = {"CO", "H2S", "SO2", "SO3", "NH3", "HCN", "NO", "NO2", "COS", "CS2",
          "C6H6", "C7H8", "CH3OH", "HCHO"}
_CORROSIVE = {"NH3", "SO2", "SO3", "NO2"}

LENNARD_JONES_GAP = ("no checked source transcribed yet (candidates: Cantera transport data, "
                     "BSD licence; Poling et al., Appendix B)")
PHASE_GAP = ("normal boiling, melting and triple points not transcribed yet from a checked "
             "source")


def material_interaction(key: str) -> dict | None:
    if FAMILY.get(key) == "Radical":
        return {**{m: False for m in _M}, "notes": RADICAL_NOTE}
    if key not in _MATERIAL:
        return None
    flags, note = _MATERIAL[key]
    return {**{m: m in flags for m in _M}, "notes": note}


def safety(key: str) -> dict | None:
    if key not in FAMILY:
        return None
    return {"flammable": key in _FLAMMABLE, "toxic": key in _TOXIC,
            "corrosive": key in _CORROSIVE,
            "lflVolPct": None, "uflVolPct": None, "autoIgnitionK": None,
            "source": RADICAL_NOTE if FAMILY[key] == "Radical" else SCREENING_SOURCE}


def radiation(key: str) -> dict:
    """Default radiation block: participating or not, and which models cover it."""
    if key in TRANSPARENT:
        return {"participating": False, "supportedModels": [],
                "note": "transparent in the infrared (homonuclear diatomic or monatomic)"}
    if key in COVERED:
        return {"participating": True, "supportedModels": COVERED[key],
                "note": "emissivity depends on the partial pressure path p*L and the mixture"}
    return {"participating": True, "supportedModels": [],
            "note": (f"{key} absorbs in the infrared but no open WSGG or Leckner parameter set "
                     "exists for it; its emission is missing from any emissivity computed "
                     "here, which is therefore LOW")}


def elements(formula: str) -> dict[str, int]:
    """Atom counts of a simple formula; the same rules as Formula.elements in F#."""
    tokens = _TOKEN.findall(formula)
    if not formula or "".join(s + c for s, c in tokens) != formula:
        raise ValueError(f"unsupported formula '{formula}'")
    out: dict[str, int] = {}
    for symbol, count in tokens:
        out[symbol] = out.get(symbol, 0) + (int(count) if count else 1)
    return out


def _kinds(sp: dict) -> dict[str, str | None]:
    """The data kind behind each quality group, used to spot a change."""
    transport = sp.get("transport") or {}
    if transport.get("kind") in ("nasaCea", "none"):
        tkind = transport["kind"]
    elif sp.get("viscosity") and sp.get("conductivity"):
        tkind = "sutherland"
    else:
        tkind = "none"
    return {
        "thermo": (sp.get("cpModel") or {}).get("kind"),
        "transport": tkind,
        "critical": "present" if sp.get("critical") else None,
        "vapourPressure": "present" if sp.get("vapourPressure") else None,
    }


_LEVEL = {
    "thermo": {"nasa9": "A", "nasa7": "A", "shomate": "B", "anchor": "D"},
    "transport": {"nasaCea": "B", "sutherland": "C", "none": None},
    "critical": {"present": "A", None: None},
    "vapourPressure": {"present": "B", None: None},
}


def implied_level(group: str, kind: str | None) -> str | None:
    return _LEVEL[group].get(kind)


def normalise(sp: dict, previous: dict | None = None) -> dict:
    """Fill the schema 3.0 fields of one record in place and return it."""
    key = sp["key"]
    sp.setdefault("id", key)
    if "family" not in sp and key in FAMILY:
        sp["family"] = FAMILY[key]
    if "synonyms" not in sp:
        sp["synonyms"] = SYNONYMS.get(key, [])
    if "elements" not in sp:
        sp["elements"] = elements(sp.get("formula") or key)
    if "radiation" not in sp:
        sp["radiation"] = radiation(key)
    if "eosParameters" not in sp:
        sp["eosParameters"] = EOS_PARAMETERS.get(key, [])
    if "lennardJones" not in sp:
        sp["lennardJones"] = None
        sp["lennardJonesUnavailableReason"] = LENNARD_JONES_GAP
    if "phase" not in sp:
        sp["phase"] = {"unavailableReason": PHASE_GAP}
    if "materialInteraction" not in sp and material_interaction(key) is not None:
        sp["materialInteraction"] = material_interaction(key)
    if "safety" not in sp and safety(key) is not None:
        sp["safety"] = safety(key)

    quality = dict(sp.get("dataQuality") or {})
    old_kinds = _kinds(previous) if previous else None
    new_kinds = _kinds(sp)
    for group, kind in new_kinds.items():
        changed = old_kinds is not None and old_kinds[group] != kind
        if group not in quality or changed:
            level = implied_level(group, kind)
            if level is None:
                quality.pop(group, None)
            else:
                quality[group] = level
    quality.setdefault("validation", "unverified")
    sp["dataQuality"] = quality
    return sp


def validate(sp: dict) -> list[str]:
    """Problems that make a record invalid under schema 3.0."""
    key = sp.get("key", "?")
    problems = []
    if sp.get("id") != key:
        problems.append(f"{key}: id '{sp.get('id')}' must equal key")
    if sp.get("family") not in FAMILIES:
        problems.append(f"{key}: family '{sp.get('family')}' missing or unknown "
                        f"(give it one of {', '.join(sorted(FAMILIES))})")
    if not sp.get("elements"):
        problems.append(f"{key}: no elements")
    if not isinstance((sp.get("radiation") or {}).get("participating"), bool):
        problems.append(f"{key}: radiation.participating missing")
    quality = sp.get("dataQuality") or {}
    if quality.get("thermo") not in ("A", "B", "C", "D"):
        problems.append(f"{key}: dataQuality.thermo missing")
    if quality.get("validation") not in ("verified", "unverified", "knownDeviation"):
        problems.append(f"{key}: dataQuality.validation missing or unknown")
    return problems


def version(document: dict | None) -> tuple[int, ...]:
    """Schema version as a comparable tuple ("2.1" -> (2, 1)); absent is (0,)."""
    text = str((document or {}).get("schemaVersion", "0"))
    return tuple(int(part) for part in text.split("."))


def normalise_document(document: dict, previous: dict | None) -> list[str]:
    """Normalise every record of a schema 3.0 document; return what is still invalid."""
    if version(document) < version({"schemaVersion": SCHEMA_VERSION}):
        return []
    document.setdefault("referenceState", REFERENCE_STATE)
    old = {s["key"]: s for s in (previous or {}).get("species", [])}
    problems = []
    for sp in document.get("species", []):
        normalise(sp, old.get(sp["key"]))
        problems.extend(validate(sp))
    return problems
