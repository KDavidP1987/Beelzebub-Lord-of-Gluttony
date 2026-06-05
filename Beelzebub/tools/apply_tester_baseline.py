"""
Establish the functional baseline: apply the v0.100 tester VERDICTS (from docs/TESTER_ABILITY_BASELINE.md)
to ability_rules.default.json as reviewStatus.

Verdict -> reviewStatus:
  GOOD                                  -> Approved
  TUNE / NEEDS-WORK / REVIEW /
  WORKS-NOT-USABLE                      -> Reviewed
  NOT-USABLE                            -> Blocked  (reviewTag=broken)
  ...anything tagged EXPLOIT/OP          -> Reviewed (reviewTag=exploit; don't auto-block balance issues)

PRESERVES the critical crash/stuck blocks (entries already ReviewStatus=Blocked with reviewTag crash/stuck or
Enabled=false are left untouched). Parses IDs out of the worksheet table; resolves via prefab_names.tsv.
DRY-RUN by default; --write commits.
"""
import os, re, sys, json, argparse
from collections import Counter
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
HERE = os.path.dirname(__file__)
DOC = os.path.join(HERE, '..', 'Beelzebub', 'docs', 'TESTER_ABILITY_BASELINE.md')
NAMES = os.path.join(HERE, '..', 'Beelzebub', 'Resources', 'prefab_names.tsv')

VERDICTS = ['WORKS-NOT-USABLE', 'NOT-USABLE', 'NEEDS-WORK', 'GOOD', 'TUNE', 'REVIEW']  # longest-first match


def verdict_to_status(vcell):
    up = vcell.upper()
    exploit = 'EXPLOIT' in up or '/OP' in up or ' OP' in up
    base = next((v for v in VERDICTS if v in up), None)
    if exploit:
        return 'Reviewed', 'exploit'
    if base == 'GOOD':
        return 'Approved', ''
    if base == 'NOT-USABLE':
        return 'Blocked', 'broken'
    if base in ('TUNE', 'NEEDS-WORK', 'REVIEW', 'WORKS-NOT-USABLE'):
        return 'Reviewed', ''
    return None, None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()

    g2n = {}
    for line in open(NAMES, encoding='utf-8'):
        if '\t' in line:
            g, n = line.rstrip('\n').split('\t', 1)
            g2n[int(g)] = n.strip()

    rules = C.load_rules()
    amap = rules.setdefault('AbilityMap', {})

    rows = 0
    counts = Counter()
    skipped_crit = 0
    unresolved = 0
    for line in open(DOC, encoding='utf-8'):
        if not line.startswith('|'):
            continue
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if len(cells) < 4:
            continue
        # columns: Unit | Ability | ID | Verdict | Finding
        idcell, vcell = cells[2], cells[3]
        if idcell.lower() in ('id', '---') or 'ID' == idcell:
            continue
        ids = [int(x) for x in re.findall(r'-?\d{4,}', idcell)]
        if not ids:
            continue
        status, tag = verdict_to_status(vcell)
        if status is None:
            continue
        for gid in ids:
            name = g2n.get(gid)
            if not name:
                unresolved += 1
                continue
            e = amap.get(name)
            # preserve critical blocks (crash/stuck or disabled)
            if e and (e.get('Enabled') is False or e.get('ReviewTag') in ('crash', 'stuck')):
                skipped_crit += 1
                continue
            e = amap.setdefault(name, {})
            e['ReviewStatus'] = status
            if tag:
                e['ReviewTag'] = tag
            e.setdefault('Notes', f'tester v0.100: {vcell}')
            counts[status] += 1
            rows += 1

    print(f'Applied {rows} verdicts | preserved {skipped_crit} critical-blocked | unresolved-id {unresolved}')
    print('  status:', ', '.join(f'{k}={v}' for k, v in counts.most_common()))
    print(f'  AbilityMap now: {len(amap)} entries')
    if not args.write:
        print('\nDRY-RUN — nothing written. Re-run with --write.')
        return
    json.dump(rules, open(C.RULES_PATH, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    print(f'\nWROTE {os.path.normpath(C.RULES_PATH)} — lint + rebuild.')


if __name__ == '__main__':
    main()
