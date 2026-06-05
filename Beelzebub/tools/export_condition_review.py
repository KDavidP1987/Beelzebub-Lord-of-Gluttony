"""
B4 (prep-roadmap) — tester-facing CONDITION-CONFIRMATION checklist.

Exports the auto-classified activation conditions that still need in-game confirmation, grouped by
source unit so a tester can walk a boss's kit and tick each one. The auto labels come from
classify_conditions.py; this turns the unconfirmed subset into a walkable checklist.

Confirm flow (per ability), in-game:
  .beelz admin ability <id> condition <Aimed|CloseRange|Summon|SelfCast|Movement> confirmed
which writes the confirmed condition to the server's ability_metadata_overrides.json. Fold confirmed
overrides back into the shipped ability_metadata.json before packaging (conditionSource="confirmed"
is preserved by merge_conditions_into_metadata.py).

Outputs:
  - CONDITION_REVIEW_CHECKLIST.md  (grouped by source unit; auto/unconfirmed only)
  - condition_review.csv           (flat, for spreadsheet tracking)
Read-only.  Run: python export_condition_review.py
"""
import os, sys, json, csv
from collections import defaultdict
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
OUT_MD = os.path.join(HERE, 'CONDITION_REVIEW_CHECKLIST.md')
OUT_CSV = os.path.join(HERE, 'condition_review.csv')


def main():
    meta = C.load_metadata()

    rows = []
    by_unit = defaultdict(list)
    for guid, m in meta.items():
        cond = m.get('condition')
        if not cond or cond == 'Unclassified':
            continue
        if m.get('conditionSource') not in (None, 'auto'):
            continue  # already confirmed/admin
        src = m.get('sourceNpcs') or []
        unit = src[0]['name'] if src else '(no source unit)'
        tier = m.get('sourceTier', '-')
        row = dict(guid=guid, name=m.get('name', ''), unit=unit, tier=tier or '-',
                   condition=cond, descriptor=m.get('conditionDescriptor', ''),
                   modifiers='|'.join(m.get('conditionModifiers', []) or []))
        rows.append(row)
        by_unit[unit].append(row)

    with open(OUT_CSV, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['guid', 'name', 'unit', 'tier', 'condition', 'modifiers', 'descriptor'])
        w.writeheader()
        for r in sorted(rows, key=lambda r: (r['unit'], r['name'])):
            w.writerow(r)

    L = []
    L.append('# Condition-confirmation checklist (auto, unconfirmed)\n')
    L.append(f'{len(rows)} auto-classified conditions to confirm in-game, grouped by source unit. '
             'Confirm with `.beelz admin ability <id> condition <value> confirmed` (writes to the server '
             'override file). See `export_condition_review.py` header for the full flow.\n')
    L.append('Legend: each line is `[ ] condition (mods) — ability  (id)`. Tick when verified in-game.\n')
    # units with the most abilities first (boss kits are the efficient way to test)
    for unit in sorted(by_unit, key=lambda u: (-len(by_unit[u]), u)):
        items = sorted(by_unit[unit], key=lambda r: r['name'])
        tier = items[0]['tier']
        L.append(f'## {unit}  ({len(items)}{"" if tier in ("-","") else " · "+tier})\n')
        for r in items:
            mods = f" ({r['modifiers']})" if r['modifiers'] else ''
            L.append(f"- [ ] **{r['condition']}**{mods} — {r['name']}  `{r['guid']}`")
        L.append('')
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'{len(rows)} unconfirmed conditions across {len(by_unit)} source units.')
    print(f'Wrote {os.path.normpath(OUT_MD)}')
    print(f'Wrote {os.path.normpath(OUT_CSV)}')


if __name__ == '__main__':
    main()
