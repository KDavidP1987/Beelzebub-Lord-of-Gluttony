"""
C1 (prep-roadmap) — read-only ability REVIEW EXPORT.

Joins the two shipped ability-data files into ONE flat, sortable/filterable CSV so the
whole ~1,800-ability set can be eyeballed in a spreadsheet. This is a *review surface only* —
it never writes back; edits still happen in the JSON sources, then re-run this to refresh.

  - ability_metadata.json      (DESCRIPTIVE, keyed by ability-group GUID, camelCase)
  - ability_rules.default.json (POLICY, keyed by ability-group prefab NAME, PascalCase)
  - prefab_names.tsv           (GUID <-> prefab-name join key)

Also computes A2's "usability" rollup *here* (derived, NOT a stored 5th field): a single
what-it-is read over incompatible/condition/enabled/deny-pattern. `reviewStatus` is kept as
its own ORTHOGONAL column (a Working ability can still be Unreviewed) per the A2 note.

Output: tools/ability_review.csv  (UTF-8 with BOM so Excel reads unicode cleanly).

Usage:  python export_review_csv.py
Read-only: touches no source file.
"""
import json, os, sys, csv

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
RES  = os.path.join(HERE, '..', 'Beelzebub', 'Resources')
META = os.path.join(RES, 'ability_metadata.json')
RULES = os.path.join(RES, 'ability_rules.default.json')
NAMES = os.path.join(RES, 'prefab_names.tsv')
OUT  = os.path.join(HERE, 'ability_review.csv')

# Conditions that impose a real activation requirement (→ "Conditional"). SelfCast/Unclassified
# do not gate the player, so they are NOT treated as conditional.
GATING_CONDITIONS = {'Aimed', 'CloseRange', 'Summon', 'Movement'}


def load_names(path):
    """guid(str) -> prefab name, from the tab-separated dump."""
    g2n = {}
    with open(path, encoding='utf-8') as f:
        for line in f:
            line = line.rstrip('\n')
            if not line or '\t' not in line:
                continue
            guid, name = line.split('\t', 1)
            g2n[guid.strip()] = name.strip()
    return g2n


def deny_hit(name, deny_patterns, allow_patterns):
    """Replicates the rules-file substring deny/allow check (NOT the full runtime junk filter)."""
    if not name:
        return False
    if any(p in name for p in allow_patterns):
        return False
    return any(p in name for p in deny_patterns)


def usability(meta_entry, rule, name, deny_patterns, allow_patterns, allow_guids, deny_guids, guid):
    """A2 rollup — precedence order, first match wins. Orthogonal to reviewStatus."""
    review = (rule or {}).get('ReviewStatus', 'Unreviewed')
    enabled = (rule or {}).get('Enabled', True)
    incompatible = bool(meta_entry.get('incompatible'))
    cond = meta_entry.get('condition')
    mods = meta_entry.get('conditionModifiers') or []

    junk = review == 'Hidden' or guid in deny_guids or (
        deny_hit(name, deny_patterns, allow_patterns) and guid not in allow_guids)
    if junk:
        return 'Junk'
    if incompatible or enabled is False or review == 'Blocked':
        return 'Broken'
    if 'Combo' in mods:
        return 'Combo'
    if cond in GATING_CONDITIONS:
        return 'Conditional'
    return 'Working'


def main():
    meta = json.load(open(META, encoding='utf-8'))['abilities']
    rules = json.load(open(RULES, encoding='utf-8'))
    g2n = load_names(NAMES)

    amap = rules.get('AbilityMap', {})
    deny_patterns = rules.get('DenyPatterns', [])
    allow_patterns = rules.get('AllowPatterns', [])
    allow_guids = {str(x) for x in rules.get('AllowGuids', [])}
    deny_guids = {str(x) for x in rules.get('DenyGuids', [])}

    cols = [
        'guid', 'name', 'groupPrefab', 'category', 'condition', 'conditionModifiers',
        'conditionSource', 'incompatible', 'enabled', 'reviewStatus', 'reviewTag', 'usability',
        'weapons', 'forms', 'sourceUnit', 'hasRuleEntry', 'notes',
    ]
    rows = []
    for guid, m in meta.items():
        name = g2n.get(guid, '')
        rule = amap.get(name) if name else None
        src = m.get('sourceNpcs') or []
        rows.append({
            'guid': guid,
            'name': m.get('name', ''),
            'groupPrefab': name,
            'category': '|'.join(m.get('categories', []) or []),
            'condition': m.get('condition', ''),
            'conditionModifiers': '|'.join(m.get('conditionModifiers', []) or []),
            'conditionSource': m.get('conditionSource', ''),
            'incompatible': 'yes' if m.get('incompatible') else '',
            'enabled': '' if (rule or {}).get('Enabled', True) else 'no',
            'reviewStatus': (rule or {}).get('ReviewStatus', 'Unreviewed'),
            'reviewTag': (rule or {}).get('ReviewTag', ''),
            'usability': usability(m, rule, name, deny_patterns, allow_patterns,
                                   allow_guids, deny_guids, guid),
            'weapons': '|'.join((rule or {}).get('Weapons', []) or []),
            'forms': '|'.join((rule or {}).get('Forms', []) or []),
            'sourceUnit': src[0]['name'] if src else '',
            'hasRuleEntry': 'yes' if rule else '',
            'notes': (rule or {}).get('Notes', ''),
        })

    rows.sort(key=lambda r: (r['usability'], r['name'].lower()))

    with open(OUT, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=cols)
        w.writeheader()
        w.writerows(rows)

    # Summary to console.
    from collections import Counter
    by_use = Counter(r['usability'] for r in rows)
    by_rev = Counter(r['reviewStatus'] for r in rows)
    by_tag = Counter(r['reviewTag'] for r in rows if r['reviewTag'])
    print(f'Wrote {os.path.normpath(OUT)}  ({len(rows)} abilities)')
    print('  usability:    ' + ', '.join(f'{k}={v}' for k, v in by_use.most_common()))
    print('  reviewStatus: ' + ', '.join(f'{k}={v}' for k, v in by_rev.most_common()))
    if by_tag:
        print('  reviewTag:    ' + ', '.join(f'{k}={v}' for k, v in by_tag.most_common()))
    print(f'  rule entries: {sum(1 for r in rows if r["hasRuleEntry"])} of {len(rows)} have an AbilityMap entry')


if __name__ == '__main__':
    main()
