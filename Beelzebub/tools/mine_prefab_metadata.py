#!/usr/bin/env python3
"""
mine_prefab_metadata.py — enrich ability_metadata.json from the prefab dump (Reference Data/Prefabs).

The dedicated-server install has no localization, and NPC ability prefabs carry no AbilityType/School or
description (those exist only for player spells / only client-side) — so REAL names/descriptions for NPC
abilities can't be mined; the testing team fills those. What IS reliably in the prefab dump and gets mined:

  - mechanic  : derived from the ability's chain components (Summon/Heal/Projectile/AoE/Movement/Channel)
                with a name-based fallback (Melee/Movement/Buff/Projectile/Summon). NEW field, always set.
  - baseCooldown / baseCastTime : the ability's actual AbilityCooldownData.Cooldown / MaxCastTime (from _Cast).
  - type / school / tier : AbilityType / AbilitySchool / SpellSchool Tier from the GROUP (player spells only).
  - name : filled (humanized prefab) where blank.
  - categories : filled from `mechanic` where blank.

Only BLANK descriptive fields are filled (curated values are never overwritten); the factual fields
(mechanic/baseCooldown/baseCastTime/tier) are (re)written each run. Idempotent.

Usage:  python mine_prefab_metadata.py            # dry-run summary
        python mine_prefab_metadata.py --write     # write enriched ability_metadata.json
"""
import json, os, re, sys, glob, bisect

HERE = os.path.dirname(os.path.abspath(__file__))
META = os.path.join(HERE, "..", "Beelzebub", "Resources", "ability_metadata.json")
PREFABS = os.path.join(HERE, "..", "..", "Reference Data", "Prefabs")

_CAMEL = re.compile(r"(?<=[a-z0-9])(?=[A-Z])")
def camel(s): return re.sub(r"\s+", " ", " ".join(_CAMEL.sub(" ", t) for t in s.split(" "))).strip()
def humanize(prefab):
    s = prefab[3:] if prefab.startswith("AB_") else prefab
    for suf in ("_Travel_AbilityGroup","_AbilityGroup","_Group","_Travel"):
        if s.endswith(suf): s = s[:-len(suf)]; break
    return camel(s.replace("_", " "))

_TXT = re.compile(rb'[ -~]{3,}')
def toks(path):
    try: return [x.decode("ascii","ignore") for x in _TXT.findall(open(path,"rb").read())]
    except Exception: return []

# component keyword -> mechanic tag
COMP = [("SpawnMinionOnGameplayEvent","Summon"), ("HealOnGameplayEvent","Heal"), ("HealingBuff","Heal"),
        ("ProjectM.Projectile","Projectile"), ("ProjectileMultiple","Projectile"), ("TargetAoE","AoE"),
        ("TravelBuff","Movement"), ("ChannelData","Channel")]
# name fragment -> mechanic tag (fallback when components are inconclusive)
NAMEHINT = [(("Dash","Leap","Teleport","Jump","Warp","Glide","Roll","Relocate","Travel","TakeOff","Fly"),"Movement"),
            (("Summon","Raise","Reinforcement","CallAdds","CallReinforce","SpawnAdds","ArmyOf"),"Summon"),
            (("Heal","Mend","Recovery"),"Heal"),
            (("Throw","Bolt","Shoot","Spit","Volley","Barrage","Projectile","Shard","Spike","Nova","Fan"),"Projectile"),
            (("MeleeAttack","SwingAttack","ChargeAttack","Primary","Bite","Slash","Smash","Claw","Strike","Whirlwind","Spin","Stomp","HammerSlam"),"Melee"),
            (("Howl","Roar","Buff","Aura","Enrage","Shield","Cloak","Camouflage","Stealth"),"Buff")]

def parse_cooldown_casttime(cast_toks):
    cd = ct = None
    for i, t in enumerate(cast_toks):
        if "MaxCastTime:" in t and ct is None:
            m = re.search(r"MaxCastTime:\s*([0-9.]+)", t)
            if m: ct = float(m.group(1))
        if "AbilityCooldownData" in t:
            for t2 in cast_toks[i:i+6]:
                m = re.search(r"\bCooldown:\s*([0-9.]+)", t2)
                if m: cd = float(m.group(1)); break
    return cd, ct

def parse_group(gtoks):
    typ = sch = tier = None
    for t in gtoks:
        m = re.search(r"AbilityType:\s*ProjectM\.AbilityTypeEnum\s+(\w+)", t)
        if m and m.group(1) not in ("Default","None"): typ = m.group(1)
        m = re.search(r"AbilitySchool:\s*ProjectM\.AbilitySchoolType\s+(\w+)", t)
        if m and m.group(1) not in ("None",): sch = m.group(1)
        m = re.search(r"SpellSchoolProgressionTier\s+(\w+)", t)
        if m: tier = m.group(1)
    return typ, sch, tier

def main():
    write = "--write" in sys.argv
    meta = json.load(open(META, encoding="utf-8"))
    ab = meta["abilities"]

    # index all prefab files: name -> path, plus a sorted name list for sibling range lookups
    files = {}
    for p in glob.glob(os.path.join(PREFABS, "*PrefabGuid(*).txt")):
        b = os.path.basename(p); i = b.rfind("PrefabGuid(")
        files[b[:i].strip()] = (p, b[i+11:b.rfind(")")])
    names_sorted = sorted(files)
    g2name = {gid: nm for nm, (_, gid) in files.items()}

    def siblings(stem):
        lo = bisect.bisect_left(names_sorted, stem + "_")
        out = []
        for j in range(lo, len(names_sorted)):
            nm = names_sorted[j]
            if not nm.startswith(stem + "_"): break
            out.append(nm)
        return out

    filled = {"name":0,"type":0,"school":0,"categories":0}
    setf = {"mechanic":0,"baseCooldown":0,"baseCastTime":0,"tier":0}
    for gid, e in ab.items():
        if not isinstance(e, dict): continue
        prefab = g2name.get(gid)
        if not prefab: continue
        stem = prefab
        for suf in ("_AbilityGroup","_Group"):
            if stem.endswith(suf): stem = stem[:-len(suf)]; break

        gtoks = toks(files[prefab][0])
        typ, sch, tier = parse_group(gtoks)

        # scan siblings for mechanic components + the _Cast for cooldown/casttime
        mech = set(); cd = ct = None
        sib = siblings(stem)
        for nm in sib:
            st = toks(files[nm][0]); s = "\n".join(st)
            for kw, tag in COMP:
                if kw in s: mech.add(tag)
            if nm.endswith("_Cast") and cd is None and ct is None:
                cd, ct = parse_cooldown_casttime(st)
        if cd is None or ct is None:  # cast time/cooldown can be on the group's own cast block
            c2, t2 = parse_cooldown_casttime(gtoks)
            cd = cd if cd is not None else c2; ct = ct if ct is not None else t2

        if not mech:  # name fallback
            low = prefab.lower()
            for frags, tag in NAMEHINT:
                if any(f.lower() in low for f in frags): mech.add(tag); break
        mech_s = "; ".join(sorted(mech))

        # factual fields (always (re)write)
        if mech_s: e["mechanic"] = mech_s; setf["mechanic"] += 1
        if cd is not None: e["baseCooldown"] = cd; setf["baseCooldown"] += 1
        if ct is not None: e["baseCastTime"] = ct; setf["baseCastTime"] += 1
        if tier: e["tier"] = tier; setf["tier"] += 1
        # fill blanks only
        if not e.get("name"): e["name"] = humanize(prefab); filled["name"] += 1
        if not e.get("type") and (typ or mech_s): e["type"] = typ or mech_s.split("; ")[0]; filled["type"] += 1
        if not e.get("school") and sch: e["school"] = sch; filled["school"] += 1
        if not e.get("categories") and mech: e["categories"] = sorted(mech); filled["categories"] += 1

    print(("APPLIED" if write else "DRY-RUN") + " — prefab metadata mine")
    print("  filled blanks:", filled)
    print("  factual fields set:", setf)
    if write:
        json.dump(meta, open(META, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print("  wrote", os.path.normpath(META))

if __name__ == "__main__":
    main()
