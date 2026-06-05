"""
B8 + C3 + C4 (prep-roadmap) — COVERAGE / PROGRESS / SHIPPABLE-SET dashboard.

One read-only "where do we stand" report over the shipped ability data:
  - B8 coverage: how much descriptive curation remains (descriptions, real categories, conditions,
    icons, source NPCs, source tier).
  - C3 progress: curation state — reviewStatus, reviewTag backlog, condition-confirmation status.
  - C4 shippable set: the explicit rule for "what ships as collectible" + the current set size.

Outputs:
  - ABILITY_DASHBOARD.md  (the report)
Read-only.  Run: python dashboard.py
"""
import os, sys, json
from collections import Counter
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
OUT_MD = os.path.join(HERE, 'ABILITY_DASHBOARD.md')

# ---- C4: the shippable-set rule (collectible in the curated default) ----
# An ability SHIPS as collectible iff: enabled (rule) AND not junk (deny-pattern, unless allow) AND
# reviewStatus not in {Hidden, Blocked} AND not metadata.incompatible.
def deny_hit(name, deny, allow_pat):
    if any(p in name for p in allow_pat):
        return False
    return any(p in name for p in deny)


def is_shippable(name, rule, meta_entry, deny, allow_pat, allow_guids, guid):
    if rule:
        if rule.get('Enabled', True) is False:
            return False
        rs = rule.get('ReviewStatus', 'Unreviewed')
        if rs in ('Hidden', 'Blocked'):
            return False
    if meta_entry.get('incompatible'):
        return False
    if guid in allow_guids:
        return True
    if deny_hit(name, deny, allow_pat):
        return False
    return True


def pct(n, d):
    return f'{100*n/d:.1f}%' if d else '—'


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()
    rules = C.load_rules()
    amap = rules.get('AbilityMap', {})
    deny = rules.get('DenyPatterns', [])
    allow_pat = rules.get('AllowPatterns', [])
    allow_guids = {str(x) for x in rules.get('AllowGuids', [])}

    cond = {}
    cpath = os.path.join(HERE, 'ability_conditions.json')
    if os.path.exists(cpath):
        cond = json.load(open(cpath, encoding='utf-8'))

    total = len(meta)
    # B8 coverage counters
    has_desc = has_cat = has_cond = has_icon = has_src = has_tier = 0
    # C3 progress counters
    rev_status = Counter()
    rev_tag = Counter()
    cond_source = Counter()
    cond_dist = Counter()
    # C4 shippable
    shippable = 0
    unreviewed_shippable = 0

    for gstr, m in meta.items():
        name = names.get(int(gstr), '')
        rule = amap.get(name)
        if m.get('description'):
            has_desc += 1
        cats = m.get('categories') or []
        if cats and cats != ['Other']:
            has_cat += 1
        c = m.get('condition')
        if c and c != 'Unclassified':
            has_cond += 1
            cond_dist[c] += 1
        if m.get('icon'):
            has_icon += 1
        if m.get('sourceNpcs'):
            has_src += 1
        if m.get('sourceTier'):
            has_tier += 1
        # progress
        rs = (rule or {}).get('ReviewStatus', 'Unreviewed')
        rev_status[rs] += 1
        tag = (rule or {}).get('ReviewTag')
        if tag:
            rev_tag[tag] += 1
        cs = m.get('conditionSource')
        cond_source[cs or '(unset)'] += 1
        # shippable
        if is_shippable(name, rule, m, deny, allow_pat, allow_guids, gstr):
            shippable += 1
            if rs == 'Unreviewed':
                unreviewed_shippable += 1

    L = []
    L.append('# Ability dashboard — coverage · progress · shippable set (auto-generated)\n')
    L.append(f'**{total} abilities total.** Read-only snapshot; re-run `python tools/dashboard.py` to refresh.\n')

    L.append('## C4 — Shippable (collectible) set\n')
    L.append('**Rule:** ships as collectible iff `Enabled` (rule) **and** `ReviewStatus` ∉ {Hidden, Blocked} '
             '**and** not `metadata.incompatible` **and** not deny-patterned (unless allow-listed).\n')
    L.append('> Note: with `Curation_EnforceReviewStatus` ON (default, v0.115.0+), `ReviewStatus` Blocked/Hidden '
             'IS a real runtime gate — those abilities are excluded from capture + the player catalog, so this '
             'rule matches runtime collectibility for the review dimension. It still does not model hard-blocked '
             'GUIDs, `Capture_InclusiveMode`, or difficulty gating, so treat the count as a close estimate.\n')
    L.append(f'- **Shippable now: {shippable} / {total}** ({pct(shippable, total)})')
    L.append(f'- Of those, **{unreviewed_shippable} are still `Unreviewed`** — the curation TODO before a final ship.\n')

    L.append('## C3 — Curation progress\n')
    L.append('### reviewStatus')
    L.append('| status | count |')
    L.append('|---|---|')
    for s in ('Unreviewed', 'Reviewed', 'Approved', 'Blocked', 'Hidden'):
        if rev_status.get(s):
            L.append(f'| {s} | {rev_status[s]} |')
    L.append('')
    L.append('### reviewTag backlog (tagged for follow-up testing)')
    if rev_tag:
        L.append('| tag | count |')
        L.append('|---|---|')
        for t, n in rev_tag.most_common():
            L.append(f'| {t} | {n} |')
    else:
        L.append('_none tagged_')
    L.append('')

    L.append('## B8 — Descriptive coverage gaps\n')
    L.append('| field | covered | % | gap |')
    L.append('|---|---|---|---|')
    for label, n in [('description', has_desc), ('real category (not Other)', has_cat),
                     ('condition (classified)', has_cond), ('icon', has_icon),
                     ('source NPC', has_src), ('source tier', has_tier)]:
        L.append(f'| {label} | {n} | {pct(n, total)} | {total-n} |')
    L.append('')

    L.append('## B4 — Condition confirmation status\n')
    L.append('| conditionSource | count |')
    L.append('|---|---|')
    for s, n in cond_source.most_common():
        L.append(f'| {s} | {n} |')
    L.append('')
    auto = cond_source.get('auto', 0)
    confirmed = cond_source.get('confirmed', 0) + cond_source.get('admin', 0)
    L.append(f'**{auto} auto-classified conditions await confirmation; {confirmed} confirmed.** '
             'Use `tools/export_condition_review.py` for the tester checklist.\n')
    L.append('### condition distribution (classified)')
    for c, n in cond_dist.most_common():
        L.append(f'- {c}: {n}')

    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'=== dashboard ({total} abilities) ===')
    print(f'  shippable now: {shippable} ({pct(shippable,total)}); unreviewed-shippable: {unreviewed_shippable}')
    print(f'  reviewStatus: ' + ', '.join(f'{s}={rev_status[s]}' for s in
          ('Unreviewed', 'Reviewed', 'Approved', 'Blocked', 'Hidden') if rev_status.get(s)))
    print(f'  coverage: desc {pct(has_desc,total)} · cat {pct(has_cat,total)} · cond {pct(has_cond,total)} '
          f'· src {pct(has_src,total)} · tier {pct(has_tier,total)}')
    print(f'  conditions: auto {cond_source.get("auto",0)} / confirmed {confirmed}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
