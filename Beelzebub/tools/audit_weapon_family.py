"""
B6 (prep-roadmap) — WEAPON-FAMILY classification review.

The weapon family that gates an ability to a weapon bar is inferred by a NAME-substring heuristic
(Services/WeaponFamilyClassifier.cs). Because several of its tokens are UNBOUNDED substrings
("Axe", "Mace", "Sword", "Bow"...), it can coincidentally match inside an unrelated word and mis-gate
an ability to a weapon. This audit replicates the heuristic EXACTLY and flags the suspicious matches
so they can be pinned via a per-ability `Weapons` override.

SUSPECT = the heuristic assigned a CONCRETE weapon (not Magic/Unarmed) via an UNBOUNDED token that is
NOT underscore/word-bounded in the name (e.g. "Axe" matched inside "Relaxe...") — a likely false gate.

Keep `_HEURISTICS` in sync with WeaponFamilyClassifier.cs `_heuristics`.

Outputs:
  - audit_weapon_family.json  {guid: {name, family, token, bounded, suspect}}
  - AUDIT_WEAPON_FAMILY.md    report (family distribution + suspects to pin)
Read-only.  Run: python audit_weapon_family.py
"""
import os, re, sys, json
from collections import Counter
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
OUT_JSON = os.path.join(HERE, 'audit_weapon_family.json')
OUT_MD = os.path.join(HERE, 'AUDIT_WEAPON_FAMILY.md')

# EXACT order from WeaponFamilyClassifier.cs _heuristics (first match wins).
_HEURISTICS = [
    # v0.112.0 (B6 fix): Pollaxe/TwinBlades/Slashers added before the generic Axe/_Slash_/Sword tokens.
    ('_Pollaxe_', 'Pollaxe'), ('Pollaxe', 'Pollaxe'),
    ('_TwinBlades_', 'TwinBlades'), ('TwinBlades', 'TwinBlades'),
    ('_Slashers_', 'Slashers'), ('Slashers', 'Slashers'),
    ('_GreatSword_', 'GreatSword'), ('GreatSword', 'GreatSword'),
    ('_Crossbow_', 'Crossbow'), ('Crossbow', 'Crossbow'),
    ('_Longbow_', 'Longbow'), ('Longbow', 'Longbow'),
    ('_Pistols_', 'Pistols'), ('Pistols', 'Pistols'), ('_Pistol_', 'Pistols'),
    ('_Daggers_', 'Daggers'), ('Daggers', 'Daggers'), ('_Dagger_', 'Daggers'),
    ('_Reaper_', 'Reaper'), ('Reaper', 'Reaper'), ('_Scythe_', 'Reaper'),
    ('_Whip_', 'Whip'), ('Whip', 'Whip'),
    ('_Claws_', 'Claws'), ('Claws', 'Claws'),
    ('_Spear_', 'Spear'), ('Spear', 'Spear'), ('_Lance_', 'Spear'),
    ('_Axe_', 'Axe'), ('Axe', 'Axe'), ('_Cleaver_', 'Axe'),
    ('_Mace_', 'Mace'), ('Mace', 'Mace'), ('_Hammer_', 'Mace'),
    ('_Sword_', 'Sword'), ('Sword', 'Sword'), ('_Slash_', 'Sword'),
    ('_Bow_', 'Longbow'), ('Shortbow', 'Longbow'), ('_Bow', 'Longbow'),
    ('_Spell_', 'Magic'), ('_Magic_', 'Magic'), ('_Nuke_', 'Magic'), ('_Bolt_', 'Magic'),
    ('_Frost_', 'Magic'), ('_Fire_', 'Magic'), ('_Holy_', 'Magic'), ('_Unholy_', 'Magic'),
    ('_Blood', 'Magic'), ('_Storm_', 'Magic'), ('_Lightning_', 'Magic'), ('_Curse_', 'Magic'),
    ('_Hex_', 'Magic'),
    ('_Unarmed_', 'Unarmed'), ('_Fist_', 'Unarmed'), ('_Punch_', 'Unarmed'),
]
CONCRETE = {'GreatSword', 'Crossbow', 'Longbow', 'Pistols', 'Daggers', 'Reaper', 'Whip', 'Claws',
            'Spear', 'Axe', 'Mace', 'Sword', 'Pollaxe', 'TwinBlades', 'Slashers'}


def classify(name):
    low = name.lower()
    for token, fam in _HEURISTICS:
        if token.lower() in low:
            return fam, token
    return 'Magic', '(default)'


def is_word_bounded(name, token):
    """Token appears delimited (underscore/start/end/case-boundary) — a real weapon tag, not embedded."""
    if token.startswith('_') or token.endswith('_'):
        return True  # the token itself encodes a boundary
    # unbounded token: require it to sit at an underscore/camel boundary in the name
    for m in re.finditer(re.escape(token), name, re.I):
        i, j = m.start(), m.end()
        left_ok = i == 0 or name[i - 1] in '_' or name[i - 1].islower() and name[i].isupper()
        right_ok = j == len(name) or name[j] in '_' or (name[j].isupper())
        # underscore-bounded form present anywhere is the strongest signal
        if f'_{token}_'.lower() in name.lower() or name.lower().endswith('_' + token.lower()) \
           or f'_{token}'.lower() in name.lower():
            return True
        if (i == 0 or name[i - 1] == '_') and (j == len(name) or name[j] == '_' or name[j].isupper()):
            return True
    return False


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()

    out = {}
    fam_counts = Counter()
    suspects = []
    for gstr, m in meta.items():
        name = names.get(int(gstr), '') or m.get('name', '')
        fam, token = classify(name)
        bounded = True
        suspect = False
        if fam in CONCRETE:
            bounded = is_word_bounded(name, token)
            suspect = not bounded
        out[gstr] = dict(name=name, family=fam, token=token, bounded=bounded, suspect=suspect)
        fam_counts[fam] += 1
        if suspect:
            suspects.append((name, fam, token, m.get('name', '')))

    json.dump(out, open(OUT_JSON, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    L = []
    L.append('# B6 — Weapon-family classification review (auto-generated)\n')
    L.append('Replicates the `WeaponFamilyClassifier` name heuristic and flags SUSPECT concrete-weapon '
             'gates (a weapon token matched as an embedded substring, not a real weapon tag). Pin a wrong '
             'one with a per-ability `Weapons` override in `ability_rules.default.json`.\n')
    L.append('## Family distribution (heuristic output over the full set)\n')
    L.append('| family | count |')
    L.append('|---|---|')
    for fam, n in fam_counts.most_common():
        L.append(f'| {fam} | {n} |')
    L.append('')
    L.append(f'## SUSPECT concrete-weapon gates ({len(suspects)}) — verify / pin\n')
    if not suspects:
        L.append('_None — every concrete-weapon assignment came from a word-bounded weapon tag._')
    else:
        for name, fam, token, disp in sorted(suspects):
            L.append(f"- `{name}` → **{fam}** (matched `{token}`)  <sub>{disp}</sub>")
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    concrete_total = sum(fam_counts[f] for f in CONCRETE)
    print(f'Families: Magic={fam_counts["Magic"]} concrete-weapon={concrete_total} '
          f'Unarmed={fam_counts.get("Unarmed",0)} | SUSPECT gates: {len(suspects)}')
    print('Top concrete:', ', '.join(f'{f}={fam_counts[f]}' for f in
          sorted(CONCRETE, key=lambda f: -fam_counts[f]) if fam_counts[f]))
    print(f'Wrote {os.path.normpath(OUT_JSON)}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
