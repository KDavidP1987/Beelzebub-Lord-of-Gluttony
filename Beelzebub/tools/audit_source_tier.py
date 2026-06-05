"""
B5 (prep-roadmap) — SOURCE-UNIT + TIER mapping.

For every ability, resolve its primary source NPC and that unit's LEVEL + VBlood status from the
prefab dump, and derive a difficulty TIER band. Enables accurate "captured from X", per-unit
grouping, and tier-gating. Also reports the source-coverage gap (abilities with no sourceNpc).

Tier bands are derived from UnitLevel.Level (V Rising has no single "tier" field) — labelled with
the level range so they stay honest:
  T1 <30 · T2 30-46 · T3 47-63 · T4 64+   (VBlood flagged separately)

Outputs:
  - audit_source_tier.json  {guid: {name, sourceUnit, sourceGuid, level, isVBlood, tier}}
  - AUDIT_SOURCE_TIER.md    report (tier distribution, VBlood count, coverage gap, per-unit counts)
Read-only.  Run: python audit_source_tier.py
"""
import os, re, sys, json
from collections import Counter, defaultdict
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
OUT_JSON = os.path.join(HERE, 'audit_source_tier.json')
OUT_MD = os.path.join(HERE, 'AUDIT_SOURCE_TIER.md')

LEVEL_RE = re.compile(r'Level:\s*(-?\d+)')


def tier_of(level):
    if level is None:
        return '-'
    if level < 30:
        return 'T1'
    if level < 47:
        return 'T2'
    if level < 64:
        return 'T3'
    return 'T4'


def unit_level_and_vblood(idx, guid):
    txt = C.read(idx, guid)
    if not txt:
        return None, False
    secs = C.split_sections(txt)
    level = None
    for body in secs.get('ProjectM.UnitLevel', []):
        m = LEVEL_RE.search(body)
        if m:
            level = int(m.group(1))
            break
    is_vblood = 'ProjectM.VBloodUnit' in secs
    return level, is_vblood


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()

    unit_cache = {}
    out = {}
    tier_counts = Counter()
    vblood = 0
    no_source = 0
    per_unit = defaultdict(int)

    for gstr, m in meta.items():
        src = m.get('sourceNpcs') or []
        if not src:
            no_source += 1
            out[gstr] = dict(name=m.get('name', ''), sourceUnit='-', sourceGuid=0,
                             level=None, isVBlood=False, tier='-')
            continue
        sg = src[0]['guid']
        sname = src[0].get('name', '') or names.get(sg, '')
        if sg not in unit_cache:
            unit_cache[sg] = unit_level_and_vblood(idx, sg)
        level, isvb = unit_cache[sg]
        t = tier_of(level)
        out[gstr] = dict(name=m.get('name', ''), sourceUnit=sname, sourceGuid=sg,
                         level=level, isVBlood=isvb, tier=t)
        tier_counts[t] += 1
        if isvb:
            vblood += 1
        per_unit[sname] += 1

    json.dump(out, open(OUT_JSON, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    L = []
    L.append('# B5 — Source-unit + tier mapping (auto-generated)\n')
    L.append('Primary source NPC + that unit\'s level/VBlood, with a level-derived tier band '
             '(`T1<30 · T2 30-46 · T3 47-63 · T4 64+`). Use for "captured from X", per-unit grouping, '
             'and tier-gating. Level comes from the unit prefab\'s `UnitLevel`.\n')
    L.append(f'- **Mapped (has source):** {len(meta) - no_source} / {len(meta)}')
    L.append(f'- **No source NPC (coverage gap):** {no_source}')
    L.append(f'- **VBlood-sourced abilities:** {vblood}\n')
    L.append('## Tier distribution\n')
    L.append('| tier | abilities |')
    L.append('|---|---|')
    for t in ('T1', 'T2', 'T3', 'T4', '-'):
        if tier_counts.get(t):
            L.append(f'| {t} | {tier_counts[t]} |')
    L.append('')
    L.append('## Top source units (by ability count)\n')
    for unit, n in sorted(per_unit.items(), key=lambda x: -x[1])[:30]:
        L.append(f'- {n:3}  {unit}')
    L.append('')
    L.append('## Note on the coverage gap\n')
    L.append(f'{no_source} abilities have no `sourceNpcs` in the metadata (many are name-only backfill '
             'stubs or shared/sequence prefabs). These show `tier=-`; B8 (coverage report) tracks closing '
             'this. Not an error — just unmapped.')
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'Mapped {len(meta)-no_source}/{len(meta)} | VBlood {vblood} | no-source {no_source}')
    print('Tiers:', ', '.join(f'{t}={tier_counts[t]}' for t in ('T1', 'T2', 'T3', 'T4', '-') if tier_counts.get(t)))
    print(f'Wrote {os.path.normpath(OUT_JSON)}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
