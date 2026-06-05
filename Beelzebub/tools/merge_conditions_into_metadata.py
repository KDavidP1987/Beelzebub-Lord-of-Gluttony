"""
Merge the deterministic activation-condition audit (ability_conditions.json,
produced by classify_conditions.py) into the shipped ability_metadata.json.

- Writes `condition`, `conditionDescriptor`, `conditionModifiers`, `conditionSource="auto"`
  onto each matching ability entry (keyed by GUID).
- `Unclassified` entries are LEFT UNSET (no guess — honest absence beats a wrong label).
- Only updates entries that already exist in the metadata; reports any audit GUID
  not present (no new stubs created here).
- Idempotent: re-running overwrites the same `auto` fields. Does NOT touch any entry
  whose existing conditionSource is "confirmed"/"admin" (human-verified wins).

Run AFTER classify_conditions.py. Edits Beelzebub/Resources/ability_metadata.json in place.
"""
import json, os, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
COND = os.path.join(HERE, 'ability_conditions.json')
META = os.path.join(HERE, '..', 'Beelzebub', 'Resources', 'ability_metadata.json')

def main():
    cond = json.load(open(COND, encoding='utf-8'))
    meta = json.load(open(META, encoding='utf-8'))
    ab = meta['abilities']

    updated = skipped_unclass = missing = preserved = 0
    for guid, c in cond.items():
        if c['condition'] == 'Unclassified':
            skipped_unclass += 1
            continue
        entry = ab.get(guid)
        if entry is None:
            missing += 1
            continue
        if entry.get('conditionSource') in ('confirmed', 'admin'):
            preserved += 1
            continue  # human-verified label wins over the auto re-run
        entry['condition'] = c['condition']
        entry['conditionDescriptor'] = c['descriptor']
        if c.get('modifiers'):
            entry['conditionModifiers'] = c['modifiers']
        else:
            entry.pop('conditionModifiers', None)
        entry['conditionSource'] = 'auto'
        updated += 1

    with open(META, 'w', encoding='utf-8') as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)

    print(f'Merged conditions into {os.path.normpath(META)}')
    print(f'  updated (auto labels):   {updated}')
    print(f'  left unset (Unclassified): {skipped_unclass}')
    print(f'  preserved (human-confirmed): {preserved}')
    print(f'  audit GUID not in metadata: {missing}')

if __name__ == '__main__':
    main()
