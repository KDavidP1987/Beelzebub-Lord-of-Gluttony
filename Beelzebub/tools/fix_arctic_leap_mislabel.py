#!/usr/bin/env python3
"""
fix_arctic_leap_mislabel.py — correct the gaming.tools scrape mislabel where ~52 movement/summon
abilities were all assigned the name "Arctic Leap" + Arctic Leap's frost description.

Only ONE ability is really Arctic Leap: AB_Frost_ArcticLeap_AbilityGroup. Every OTHER entry named
"Arctic Leap" is a scrape collision (Bat Vampire summons, Cursed Smith weapon summons, Mountain Beast
ghost-calls, leaps/teleports/dashes, etc.). This rewrites those entries' `name` to a humanized form of
their real prefab name and DROPS the bogus frost `description` (better no description than a wrong one).

Idempotent: a second run finds nothing (the names are no longer "Arctic Leap"). Run this LAST in the
metadata pipeline (after process/merge), same as sanitize_metadata_language.py.

Usage:
    python fix_arctic_leap_mislabel.py            # dry-run preview
    python fix_arctic_leap_mislabel.py --write     # apply
"""
import json, os, re, sys, glob

HERE = os.path.dirname(os.path.abspath(__file__))
META = os.path.join(HERE, "..", "Beelzebub", "Resources", "ability_metadata.json")
PREFABS = os.path.join(HERE, "..", "..", "Reference Data", "Prefabs")

# The ONE legitimate Arctic Leap — never touch it.
REAL_ARCTIC_LEAP_PREFIX = "AB_Frost_ArcticLeap"

_CAMEL = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")

def humanize(prefab_name: str) -> str:
    s = prefab_name
    if s.startswith("AB_"):
        s = s[3:]
    for suf in ("_Travel_AbilityGroup", "_AbilityGroup", "_Group", "_Travel"):
        if s.endswith(suf):
            s = s[: -len(suf)]
            break
    s = s.replace("_", " ")
    # split mashed camelCase tokens (BatVampire -> Bat Vampire)
    s = " ".join(_CAMEL.sub(" ", tok) for tok in s.split(" "))
    return re.sub(r"\s+", " ", s).strip()

def load_prefab_names():
    names = {}
    for p in glob.glob(os.path.join(PREFABS, "*PrefabGuid(*).txt")):
        base = os.path.basename(p)
        i = base.rfind("PrefabGuid(")
        guid = base[i + 11 : base.rfind(")")]
        names[guid] = base[:i].strip()
    return names

def main():
    write = "--write" in sys.argv
    meta = json.load(open(META, encoding="utf-8"))
    ab = meta["abilities"]
    pnames = load_prefab_names()

    fixed = []
    for guid, e in ab.items():
        if not isinstance(e, dict) or e.get("name") != "Arctic Leap":
            continue
        prefab = pnames.get(guid, "")
        if prefab.startswith(REAL_ARCTIC_LEAP_PREFIX):
            continue  # the genuine Arctic Leap
        new_name = humanize(prefab) if prefab else f"Ability {guid}"
        had_desc = "description" in e
        fixed.append((guid, prefab, new_name, had_desc))
        if write:
            e["name"] = new_name
            e.pop("description", None)  # drop the bogus frost text
            e["nameSource"] = "mislabel-fix"  # marker so a re-merge can preserve/redo

    print(f"{'APPLIED' if write else 'DRY-RUN'}: {len(fixed)} mislabeled 'Arctic Leap' entries")
    for guid, prefab, new_name, had_desc in fixed:
        print(f"  {guid:>12}  {prefab}\n               -> name='{new_name}'  (dropped desc={had_desc})")

    if write:
        json.dump(meta, open(META, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"\nWrote {META}")
    else:
        print("\n(dry-run; re-run with --write to apply)")

if __name__ == "__main__":
    main()
