"""
Merge the B5 source-tier audit (audit_source_tier.json) into ability_metadata.json.

Writes `sourceLevel` (int), `sourceTier` (T1..T4), `isVBlood` (bool) onto each ability entry that has
a resolved source unit. Entries with no source NPC are left unset. Idempotent — re-running overwrites
the same three derived fields and touches nothing else.

Run AFTER audit_source_tier.py. Edits Beelzebub/Resources/ability_metadata.json in place.
"""
import json, os, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
TIER = os.path.join(HERE, 'audit_source_tier.json')
META = os.path.join(HERE, '..', 'Beelzebub', 'Resources', 'ability_metadata.json')


def main():
    tier = json.load(open(TIER, encoding='utf-8'))
    meta = json.load(open(META, encoding='utf-8'))
    ab = meta['abilities']

    updated = skipped = missing = 0
    for guid, t in tier.items():
        entry = ab.get(guid)
        if entry is None:
            missing += 1
            continue
        if t.get('level') is None:
            # no source mapped — clear any stale fields, leave unset
            for k in ('sourceLevel', 'sourceTier', 'isVBlood'):
                entry.pop(k, None)
            skipped += 1
            continue
        entry['sourceLevel'] = t['level']
        entry['sourceTier'] = t['tier']
        entry['isVBlood'] = bool(t['isVBlood'])
        updated += 1

    with open(META, 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)

    print(f'Merged tier into {os.path.normpath(META)}')
    print(f'  updated (has source): {updated}')
    print(f'  left unset (no source): {skipped}')
    print(f'  tier GUID not in metadata: {missing}')


if __name__ == '__main__':
    main()
