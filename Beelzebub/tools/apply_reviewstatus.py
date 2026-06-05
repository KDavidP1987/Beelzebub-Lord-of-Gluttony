"""
Apply the B1 audit as TRACKING TAGS on the shipped default rules (ability_rules.default.json).

Per the maintainer decision: do NOT block/disable anything — instead TAG each audited ability by
type and mark it triaged-pending-test, so whole groups can be pulled through the in-game API and
evaluated (emotes for usability, hard variants for unique attributes, etc.). The pairing
  ReviewStatus = "Reviewed"  (audit-triaged, decision pending)
  ReviewTag    = "<type>"     (emote/feed/idle_flee/variant_hard/basic_attack/reaction/...)
IS the test-log backlog. Nothing here changes gameplay (neither field is a runtime gate).

DRY-RUN by default; prints the change set. `--write` commits. Run the lint + rebuild after.

Usage:
  python apply_reviewstatus.py                 # dry-run, all audited junk categories
  python apply_reviewstatus.py --write         # apply tags
  python apply_reviewstatus.py --categories emote,feed   # subset
  python apply_reviewstatus.py --disable       # (optional) ALSO set Enabled=false — NOT default
"""
import os, sys, json, argparse
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
AUDIT = os.path.join(HERE, 'audit_collectibility.json')

# B1 audit category -> canonical ReviewTag (AbilityRules.ReviewTags)
CATEGORY_TO_TAG = {
    'emote': 'emote', 'feed': 'feed', 'idle_flee': 'idle_flee',
    'variant': 'variant_hard', 'basic_attack': 'basic_attack', 'reaction': 'reaction',
}
DEFAULT_CATEGORIES = ','.join(CATEGORY_TO_TAG)
TAG_NOTE = {
    'emote': 'emote — evaluate in-game for player usability (players want more emotes); keep if functional',
    'feed': 'feed lifecycle ability — likely non-functional for a player; verify then Hide/Block',
    'idle_flee': 'AI idle/flee behaviour — likely non-functional for a player; verify then Hide/Block',
    'variant_hard': 'brutal _Hard_ difficulty variant of a base ability — evaluate unique attributes vs the base',
    'basic_attack': 'generic primary/melee auto-attack — evaluate whether worth collecting',
    'reaction': 'reaction (counter/knockdown) — may be a real CC; evaluate in-game',
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--categories', default=DEFAULT_CATEGORIES)
    ap.add_argument('--status', default='Reviewed')
    ap.add_argument('--disable', action='store_true', help='(optional) also set Enabled=false — NOT default')
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()
    cats = {c.strip() for c in args.categories.split(',') if c.strip()}

    audit = json.load(open(AUDIT, encoding='utf-8'))
    rules = C.load_rules()
    amap = rules.setdefault('AbilityMap', {})

    targets = [(g, r) for g, r in audit.items() if r['category'] in cats and r['junk']]
    by_cat = {}
    for g, r in targets:
        by_cat.setdefault(r['category'], 0)
        by_cat[r['category']] += 1
    print(f'Categories {sorted(cats)} -> {len(targets)} abilities: '
          + ', '.join(f'{k}={v}' for k, v in sorted(by_cat.items())))

    new_entries = 0
    for g, r in targets:
        name = r['prefab']
        if not name:
            continue
        tag = CATEGORY_TO_TAG.get(r['category'], r['category'])
        e = amap.get(name)
        if e is None:
            e = {}
            new_entries += 1
        e['ReviewStatus'] = args.status
        e['ReviewTag'] = tag
        if args.disable:
            e['Enabled'] = False
        e.setdefault('Notes', f"B1 audit: {TAG_NOTE.get(tag, r['category'])}")
        amap[name] = e

    print(f'  status={args.status}  tags set  new AbilityMap entries={new_entries}  disable={args.disable}')
    if not args.write:
        print('\nDRY-RUN — nothing written. Re-run with --write to apply.')
        for g, r in targets[:6]:
            print(f"    {r['prefab']}  ->  {json.dumps(amap[r['prefab']], ensure_ascii=False)}")
        return

    json.dump(rules, open(C.RULES_PATH, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    print(f'\nWROTE {os.path.normpath(C.RULES_PATH)} ({len(amap)} AbilityMap entries) — '
          f'now: python lint_ability_data.py, then rebuild.')


if __name__ == '__main__':
    main()
