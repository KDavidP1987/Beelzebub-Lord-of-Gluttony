#!/usr/bin/env python3
"""
audit_name_vs_prefab.py — flag ability metadata whose `name` is a POSITIONAL scrape-default
("Primary Attack" / "Secondary Attack" / "Auto Attack" / "Melee Attack") that landed on a prefab
which clearly is NOT an attack (a summon, buff, pillar, etc.). Same class of scrape glitch as the
Arctic-Leap mislabel, but scoped tightly so we never rename a REAL primary attack.

A flagged entry's name is rewritten to the humanized prefab name and its (often-wrong) description dropped.

Also reports (no change) every TRANSFORM-TRIGGER prefab so we can see which transform abilities exist
(Shapeshift / Evolve / Transform / ExoForm / Morph) — used to keep them out of the assignable kit.

Usage:
    python audit_name_vs_prefab.py            # dry-run report
    python audit_name_vs_prefab.py --write     # apply the attack-mislabel renames
"""
import json, os, re, sys, glob

HERE = os.path.dirname(os.path.abspath(__file__))
META = os.path.join(HERE, "..", "Beelzebub", "Resources", "ability_metadata.json")
PREFABS = os.path.join(HERE, "..", "..", "Reference Data", "Prefabs")

POSITIONAL_ATTACK_NAMES = {"primary attack", "secondary attack", "auto attack",
                           "melee attack", "basic attack"}
# If the prefab name contains any of these, an "...Attack" name is plausibly correct → keep.
ATTACK_PREFAB_HINTS = ["primary", "secondary", "meleeattack", "autoattack", "basicattack",
                       "_attack", "melee_", "weaponattack", "_throw", "_slash", "_whip",
                       "_shoot", "_blast_", "_strike"]
TRANSFORM_HINTS = ["shapeshift", "_evolve", "_transform", "exoform", "_morph", "_becom"]

_CAMEL = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")
def humanize(prefab):
    s = prefab
    if s.startswith("AB_"): s = s[3:]
    for suf in ("_Travel_AbilityGroup", "_AbilityGroup", "_Group", "_Travel"):
        if s.endswith(suf): s = s[:-len(suf)]; break
    s = s.replace("_", " ")
    s = " ".join(_CAMEL.sub(" ", t) for t in s.split(" "))
    return re.sub(r"\s+", " ", s).strip()

def load_prefab_names():
    names = {}
    for p in glob.glob(os.path.join(PREFABS, "*PrefabGuid(*).txt")):
        b = os.path.basename(p); i = b.rfind("PrefabGuid(")
        names[b[i+11:b.rfind(")")]] = b[:i].strip()
    return names

def main():
    write = "--write" in sys.argv
    meta = json.load(open(META, encoding="utf-8"))
    ab = meta["abilities"]
    pnames = load_prefab_names()

    fixed, transforms = [], []
    for g, e in ab.items():
        if not isinstance(e, dict): continue
        pf = pnames.get(g, "")
        pfl = pf.lower()
        nm = (e.get("name") or "").strip()
        if any(h in pfl for h in TRANSFORM_HINTS):
            transforms.append((g, pf, nm))
        if nm.lower() in POSITIONAL_ATTACK_NAMES and pf and not any(h in pfl for h in ATTACK_PREFAB_HINTS):
            new = humanize(pf)
            fixed.append((g, pf, nm, new, "description" in e))
            if write:
                e["name"] = new
                e.pop("description", None)
                e["nameSource"] = "attack-mislabel-fix"

    print(f"{'APPLIED' if write else 'DRY-RUN'}: {len(fixed)} positional-attack mislabels on non-attack prefabs")
    for g, pf, nm, new, had in fixed[:60]:
        print(f"  {g:>12}  {pf}\n               '{nm}' -> '{new}'  (dropped desc={had})")
    if len(fixed) > 60:
        print(f"  … and {len(fixed)-60} more")

    print(f"\nTRANSFORM-TRIGGER prefabs in metadata (report only): {len(transforms)}")
    for g, pf, nm in transforms[:40]:
        print(f"  {g:>12}  {pf}   name='{nm}'")

    if write:
        json.dump(meta, open(META, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"\nWrote {META}")

if __name__ == "__main__":
    main()
