"""
Apply the KDPen decisions from the B6/B7 review to ability_rules.default.json:

  1. CursedSmith summoned-weapon abilities -> UNIVERSAL (Weapons=["Magic"]). The Cursed Blacksmith
     boss summons floating weapons of every type as SPELLS, not as the player's weapon abilities — so
     they should not gate to a weapon bar. (The other ~19 boss-themed weapon abilities stay gated.)
  2. gateboss / minion variant abilities -> TAG for review (ReviewStatus=Reviewed + ReviewTag).

DRY-RUN by default; `--write` commits. Run lint + rebuild after. Idempotent.
"""
import os, sys, json, argparse
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

CURSEDSMITH_UNIVERSAL_NOTE = ("Cursed Blacksmith summons floating weapons as spells, not weapon "
                              "abilities — universal (not weapon-gated). B6 decision.")
VARIANT_TAG_NOTE = {
    'gateboss': 'gate-boss encounter variant of a base ability — evaluate hide.',
    'minion':   'minion/servant variant of a base ability — evaluate hide.',
}
VARIANT_TAG = {'gateboss': 'variant_gateboss', 'minion': 'variant_minion'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()

    here = os.path.dirname(__file__)
    wf = json.load(open(os.path.join(here, 'audit_weapon_family.json'), encoding='utf-8'))
    va = json.load(open(os.path.join(here, 'audit_variants.json'), encoding='utf-8'))
    rules = C.load_rules()
    amap = rules.setdefault('AbilityMap', {})

    cursed = sorted({r['name'] for r in wf.values() if 'CursedSmith' in r['name'] and r['suspect']})
    variants = [(r['variant'], r['kind']) for r in va.values() if r['kind'] in ('gateboss', 'minion')]

    print(f'CursedSmith -> universal: {len(cursed)}')
    for name in cursed:
        e = amap.setdefault(name, {})
        e['Weapons'] = ['Magic']
        e.setdefault('Notes', CURSEDSMITH_UNIVERSAL_NOTE)
        print(f'    {name}  Weapons=[Magic]')

    print(f'\nVariants -> tag: {len(variants)}')
    for name, kind in variants:
        e = amap.setdefault(name, {})
        e['ReviewStatus'] = 'Reviewed'
        e['ReviewTag'] = VARIANT_TAG[kind]
        e.setdefault('Notes', f'B7: {VARIANT_TAG_NOTE[kind]}')
        print(f'    {name}  ReviewTag={VARIANT_TAG[kind]}')

    if not args.write:
        print('\nDRY-RUN — nothing written. Re-run with --write.')
        return
    json.dump(rules, open(C.RULES_PATH, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    print(f'\nWROTE {os.path.normpath(C.RULES_PATH)} ({len(amap)} entries) — lint + rebuild next.')


if __name__ == '__main__':
    main()
