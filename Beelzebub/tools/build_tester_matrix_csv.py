#!/usr/bin/env python3
"""
build_tester_matrix_csv.py — generate a tester-fillable ability matrix CSV.

Rows: EVERY ability in ability_metadata.json (the full curated ability set) PLUS any ability that has a
curated rule. Columns: identity (Ability/ID/Unit), descriptive metadata (Description/Type/School/Categories/
Condition/SourceTier/SourceLevel/IsVBlood), one column per Weapon family, one per Form, Enabled, every
per-ability config knob, a Crash/Issue flag, and Notes.

Prepopulated from shipped data:
  - UnitName: resolved to the ENGLISH unit prefab name (CHAR_*) — fixes scrape foreign-text + missing names.
  - Descriptive columns from ability_metadata.json (foreign text dropped, English-only).
  - Weapon/Form columns: 'Y' allow-listed / 'N' '!'blacklisted (from rule Weapons/Forms).
  - Enabled + every config value already set in ability_rules.default.json.
  - Crash/Issue + Notes from the rule ReviewTag/Notes (the v0.100 tester crash/stuck/exploit triage).

Usage: python build_tester_matrix_csv.py   ->  ../Beelzebub/docs/TESTER_ABILITY_MATRIX.csv
"""
import json, os, csv, glob, re

HERE = os.path.dirname(os.path.abspath(__file__))
META = os.path.join(HERE, "..", "Beelzebub", "Resources", "ability_metadata.json")
RULES = os.path.join(HERE, "..", "Beelzebub", "Resources", "ability_rules.default.json")
PREFABS = os.path.join(HERE, "..", "..", "Reference Data", "Prefabs")
OUT = os.path.join(HERE, "..", "Beelzebub", "docs", "TESTER_ABILITY_MATRIX.csv")

WEAPONS = ["Magic","Unarmed","Sword","GreatSword","Axe","Mace","DualHammers","Spear","Daggers",
           "Crossbow","Longbow","Pistols","Reaper","Whip","Claws","Pollaxe","Slashers","TwinBlades"]
FORMS = ["Wolf","Bear","Rat","Spider","Toad","Werewolf","Gargoyle","Mounted"]
CONFIG = [
    ("Cooldown","CooldownSeconds"), ("Range","MaxRangeOverride"), ("Charges","ChargesMax"),
    ("ChargeTime","ChargeTimeSeconds"), ("AoE","AoeRadius"), ("ProjSpeed","ProjectileSpeed"),
    ("LeapHeight","LeapHeight"), ("Duration","EffectDurationSeconds"), ("Healing","HealingMultiplier"),
    ("DamageScale","DamageScale"), ("CooldownScale","CooldownScale"), ("ForceTimeout","ForceTimeoutSeconds"),
    ("PowerWindow","PowerWindowSeconds"), ("SummonCap","SummonCap"), ("SummonTimeout","SummonTimeoutSeconds"),
    ("SummonUnits","SummonUnitsPerCast"), ("CastSpeed","CastMovementSpeed"), ("FreelyMove","FreeMoveAfterSeconds"),
    ("InterruptOnHit","InterruptOnHit"), ("TransformOnly","TransformOnly"),
]
_WLOOK = {w.lower(): w for w in WEAPONS}
_FLOOK = {f.lower(): f for f in FORMS}
_ALLOWED = set("’‘“”—–…•°×→§")          # punctuation we keep
_CAMEL = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")

def is_foreign(s):
    """True if the string has non-Latin script chars (CJK / Cyrillic / etc.)."""
    for c in (s or ""):
        o = ord(c)
        if o < 128 or c in _ALLOWED: continue
        if 0xC0 <= o <= 0x17F: continue   # accented Latin (café, etc.) — keep
        return True
    return False

def camel(s):
    return re.sub(r"\s+", " ", " ".join(_CAMEL.sub(" ", t) for t in s.split(" "))).strip()

def load_prefab_names():
    names = {}
    for p in glob.glob(os.path.join(PREFABS, "*PrefabGuid(*).txt")):
        b = os.path.basename(p); i = b.rfind("PrefabGuid(")
        names[b[i+11:b.rfind(")")]] = b[:i].strip()
    return names

PNAMES = load_prefab_names()

def humanize_ability(prefab):
    s = prefab[3:] if prefab.startswith("AB_") else prefab
    for suf in ("_Travel_AbilityGroup","_AbilityGroup","_Group","_Travel"):
        if s.endswith(suf): s = s[:-len(suf)]; break
    return camel(s.replace("_", " "))

_UNIT_SUFFIXES = ("_VBlood_GateBoss_Major","_VBlood_GateBoss_Minor","_GateBoss_Major","_GateBoss_Minor",
                  "_VBlood","_Servant","_Minion","_Standard","_HomePos")
def unit_english(guid, scraped):
    """English unit name from the unit's CHAR_ prefab; fall back to a clean scraped name."""
    pf = PNAMES.get(str(guid), "")
    if pf.startswith("CHAR_"):
        s = pf[5:]
        for suf in _UNIT_SUFFIXES:
            if s.endswith(suf): s = s[:-len(suf)]; break
        return camel(s.replace("_", " "))
    if scraped and not is_foreign(scraped): return scraped.strip()
    return ""

# Factions whose prefab names are reliably AB_<Faction>_<Unit>_<Action> — for these we can infer the unit
# (token0+token1) when the scrape gave no sourceNpc. Single-name factions (Harpy_, Bear_) and generic/player
# prefixes (Vampire_, Interact_, Consumable_…) are NOT inferred (token1 there is an action, not a unit).
_COMPOUND_FACTIONS = {"bandit","militia","undead","churchoflight","legion","blackfang","cursed",
                      "gloomrot","tribe","vermin","winter","geomancer","wendigo","harpy"}

def derive_unit(prefab):
    if not prefab.startswith("AB_"): return ""
    toks = prefab[3:].split("_")
    if len(toks) < 2 or toks[0].lower() not in _COMPOUND_FACTIONS: return ""
    return camel(toks[0] + " " + toks[1]) + " (inferred)"

def units_for(e, prefab):
    out, seen = [], set()
    for n in (e.get("sourceNpcs") or []):
        nm = unit_english(n.get("guid"), n.get("name", ""))
        if nm and nm.lower() not in seen:
            seen.add(nm.lower()); out.append(nm)
    if out: return "; ".join(out)
    return derive_unit(prefab)   # fallback for compound-faction abilities the scrape missed

def mark_list(tokens, lookup):
    out = {}
    for t in (tokens or []):
        t = str(t).strip()
        if not t: continue
        neg = t.startswith("!")
        col = lookup.get((t[1:] if neg else t).lower())
        if col: out[col] = "N" if neg else "Y"
    return out

def crash_flag(e):
    tag = (e.get("ReviewTag") or "").lower()
    if tag == "crash": return "CRASH (blocked)"
    if tag == "stuck": return "STUCK/CHAR-BREAK (blocked)"
    if tag == "exploit": return "EXPLOIT (blocked)"
    n = (e.get("Notes") or "").lower()
    if "server-crash" in n or "server crash" in n: return "CRASH (blocked)"
    if "character-break" in n or "char-break" in n: return "STUCK/CHAR-BREAK (blocked)"
    return ""

def clean_text(s):
    s = (s or "").replace("\n", " ").replace("\r", " ").strip()
    return "" if is_foreign(s) else re.sub(r"\s+", " ", s)

def main():
    meta = json.load(open(META, encoding="utf-8"))["abilities"]
    rules = json.load(open(RULES, encoding="utf-8")).get("AbilityMap", {})

    name2guid = {}
    for g, nm in PNAMES.items():
        name2guid.setdefault(nm, g)

    guids = set(g for g in meta if isinstance(meta.get(g), dict))   # ALL metadata abilities
    for key in rules:                                                # + any curated rule not in metadata
        if key in name2guid: guids.add(name2guid[key])

    desc_cols = ["Description","Type","School","Categories","Mechanic","Condition","SpellTier",
                 "SourceTier","SourceLevel","IsVBlood","BaseCooldown","BaseCastTime"]
    header = (["Ability","ID","UnitName"] + desc_cols + WEAPONS + FORMS + ["Enabled"]
              + [c for c, _ in CONFIG] + ["Crash/Issue","Notes"])
    rows = []
    for g in guids:
        e = meta.get(g, {}) if isinstance(meta.get(g), dict) else {}
        prefab = PNAMES.get(g, "")
        name = e.get("name") or humanize_ability(prefab) or g
        if is_foreign(name): name = humanize_ability(prefab) or g
        r = rules.get(prefab, {}) if prefab else {}

        row = [name, g, units_for(e, prefab)]
        row += [
            clean_text(e.get("description")),
            (e.get("type") or ""),
            (e.get("school") or ""),
            "; ".join(e.get("categories") or []),
            (e.get("mechanic") or ""),
            (e.get("condition") or ""),
            (e.get("tier") or ""),
            (e.get("sourceTier") or ""),
            (e.get("sourceLevel") if e.get("sourceLevel") is not None else ""),
            ("Y" if e.get("isVBlood") else ("" if e.get("isVBlood") is None else "N")),
            (e.get("baseCooldown") if e.get("baseCooldown") is not None else ""),
            (e.get("baseCastTime") if e.get("baseCastTime") is not None else ""),
        ]
        wmark = mark_list(r.get("Weapons"), _WLOOK); fmark = mark_list(r.get("Forms"), _FLOOK)
        row += [wmark.get(w, "") for w in WEAPONS]
        row += [fmark.get(f, "") for f in FORMS]
        en = r.get("Enabled"); row.append("N" if en is False else ("Y" if en is True else ""))
        for _, field in CONFIG:
            v = r.get(field)
            if field in ("DamageScale","CooldownScale") and (v == 1.0 or v == 1): v = None
            row.append("" if v is None else v)
        row.append(crash_flag(r))
        row.append((r.get("Notes") or "").strip())
        rows.append(row)

    rows.sort(key=lambda x: (str(x[2]).lower(), str(x[0]).lower()))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f); w.writerow(header); w.writerows(rows)
    print(f"Wrote {os.path.normpath(OUT)}")
    print(f"  {len(rows)} ability rows, {len(header)} columns")
    print(f"  rows with a unit name: {sum(1 for x in rows if x[2])}")
    print(f"  rows with a description: {sum(1 for x in rows if x[3])}")
    print(f"  crash/issue flags: {sum(1 for x in rows if x[-2])}")

if __name__ == "__main__":
    main()
