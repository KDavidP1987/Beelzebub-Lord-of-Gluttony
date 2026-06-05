"""
B7 (prep-roadmap) — DUPLICATE / VARIANT audit.

Finds base<->variant ability pairs so collections aren't cluttered with near-duplicates. A variant is
a group whose name, with a variant token removed, matches ANOTHER existing ability group (the base).
Variant tokens: _Hard_ (brutal difficulty), _GateBoss_, _Minion_/_Servant_ (add/minion cut), and a
trailing numeric suffix (_01/_02). Each pair gets a recommended action (the _Hard_ set is already
tagged `variant_hard` by B1).

Outputs:
  - audit_variants.json   {variantGuid: {variant, base, baseGuid, kind, recommend}}
  - AUDIT_VARIANTS.md      report grouped by variant kind
Read-only.  Run: python audit_variants.py
"""
import os, re, sys, json
from collections import Counter, defaultdict
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
OUT_JSON = os.path.join(HERE, 'audit_variants.json')
OUT_MD = os.path.join(HERE, 'AUDIT_VARIANTS.md')

# (kind, regex to remove the variant token, recommended action)
VARIANT_RULES = [
    ('hard',     re.compile(r'_Hard(?=_|$)'),       'brutal variant — compare unique attrs vs base; collapse or keep-both'),
    ('gateboss', re.compile(r'_GateBoss(?=_|$)'),    'gate-boss variant — usually hide (encounter-specific)'),
    ('minion',   re.compile(r'_(Minion|Servant)(?=_|$)'), 'minion/servant variant — usually hide'),
    ('numbered', re.compile(r'_(0?[2-9])(?=_|$)'),   'numbered duplicate — verify vs the base (_01) instance'),
]


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()

    # the universe of real ability-group prefab names (for base lookup)
    group_names = {n for n in names.values() if n.endswith('_AbilityGroup') or n.endswith('_Group')}
    name_to_guid = {}
    for g, n in names.items():
        name_to_guid.setdefault(n, g)
    meta_names = {names.get(int(g), '') for g in meta}

    out = {}
    kind_counts = Counter()
    by_kind = defaultdict(list)

    for gstr, m in meta.items():
        name = names.get(int(gstr), '')
        if not name:
            continue
        for kind, rx, rec in VARIANT_RULES:
            if not rx.search(name):
                continue
            base = rx.sub('', name)
            if base == name or base not in group_names:
                continue
            # base must be a different, real group (prefer one that's also in metadata)
            out[gstr] = dict(variant=name, base=base, baseGuid=name_to_guid.get(base, 0),
                             kind=kind, baseInMeta=base in meta_names, recommend=rec)
            kind_counts[kind] += 1
            by_kind[kind].append((name, base, base in meta_names))
            break  # one kind per variant (first match)

    json.dump(out, open(OUT_JSON, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    L = []
    L.append('# B7 — Duplicate / variant audit (auto-generated)\n')
    L.append('base<->variant pairs (a group whose name minus a variant token matches another existing '
             'group). Decide collapse / keep-both / hide per pair. `_Hard_` variants are already tagged '
             '`variant_hard` by B1.\n')
    L.append('## Distribution\n')
    L.append('| kind | pairs |')
    L.append('|---|---|')
    for kind, n in kind_counts.most_common():
        L.append(f'| {kind} | {n} |')
    L.append('')
    for kind, rx, rec in VARIANT_RULES:
        rows = by_kind.get(kind)
        if not rows:
            continue
        L.append(f'## {kind}  ({len(rows)}) — {rec}\n')
        for name, base, in_meta in sorted(rows):
            tag = '' if in_meta else '  <sub>(base not in metadata)</sub>'
            L.append(f'- `{name}`  ←  base `{base}`{tag}')
        L.append('')
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'Variant pairs: {sum(kind_counts.values())} | ' + ', '.join(f'{k}={v}' for k, v in kind_counts.most_common()))
    print(f'Wrote {os.path.normpath(OUT_JSON)}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
