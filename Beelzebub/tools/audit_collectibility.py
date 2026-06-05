"""
B1 (prep-roadmap) — COLLECTIBILITY / JUNK audit.

Partitions the whole shipped ability universe (the ~1,813 entries in ability_metadata.json)
into real player-COLLECTIBLES vs NOISE, with a reason + confidence for every junk call.
Deterministic: name-token signals (underscore-BOUNDED, like the shipped DenyPatterns — never
substring, which would catch SpawnAdds/DeathKnight) + structural prefab signals.

EXACTNESS over coverage. Hard guards prevent the known false positives:
  - SUMMON GUARD: an ability whose condition is Summon, or whose chain spawns a CHAR_ unit, is a
    real signature summon — NEVER junk, even if its name contains "Spawn".
  - Only HIGH-confidence, definitionally-non-ability categories propose `Hidden`. MEDIUM/LOW are
    listed for manual review and proposed status is left blank (stays Unreviewed).

Categories: emote · idle_flee · lifecycle · sequence · feed · dev · reaction · basic_attack ·
variant · structural_nopayload · structural_nocast.

Outputs:
  - audit_collectibility.json   {guid: {name, prefab, junk, category, confidence, status, evidence}}
  - AUDIT_COLLECTIBILITY.md      human report (distribution + per-category samples for verification)
Read-only — proposes; does NOT write the rules file.  Run: python audit_collectibility.py
"""
import os, re, sys, json
from collections import Counter, defaultdict
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
COND_PATH = os.path.join(HERE, 'ability_conditions.json')
OUT_JSON = os.path.join(HERE, 'audit_collectibility.json')
OUT_MD = os.path.join(HERE, 'AUDIT_COLLECTIBILITY.md')

# Underscore-BOUNDED token categories. A token matches if it appears delimited by '_' (or string
# ends) in the prefab name — so "_Spawn_" matches AB_X_Spawn_AbilityGroup but NOT AB_X_SpawnAdds_*.
# (confidence, [tokens]) — HIGH = definitionally not a castable signature ability.
NAME_CATEGORIES = {
    'emote':       ('HIGH',   ['Emote']),                                   # also AB_Emote_ prefix (handled below)
    'idle_flee':   ('HIGH',   ['Idle', 'Flee']),
    'lifecycle':   ('HIGH',   ['Spawn', 'Spawning', 'Despawn', 'Disappear', 'Death', 'Wounded', 'Downed']),
    'sequence':    ('HIGH',   ['Sequence']),
    'feed':        ('HIGH',   ['FeedBoss', 'Feed', 'FeedExecute']),         # Feed_Initiate -> 'Feed' token covers
    'dev':         ('HIGH',   ['Test', 'Internal', 'DEBUG', 'TEMPLATE', 'Dummy', 'Placeholder', 'Tutorial']),
    'reaction':    ('MEDIUM', ['Block', 'Parry', 'Counter', 'Reaction', 'GetHit', 'Stagger', 'Knockback', 'Knockdown']),
    'basic_attack':('MEDIUM', ['MeleeAttack', 'PrimaryAttack', 'AutoAttack']),
    'variant':     ('MEDIUM', ['Hard', 'OLD', 'Unused', 'Deprecated', 'Backup', 'Copy']),
}
HIGH_AUTOHIDE = {'emote', 'idle_flee', 'lifecycle', 'sequence', 'feed', 'dev'}


def tokens(prefab):
    return set(prefab.replace('_AbilityGroup', '').replace('_Group', '').split('_'))


def chain_spawns_char(idx, names, casts):
    for g in C.reachable(idx, casts):
        if names.get(g, '').startswith('CHAR_'):
            return names.get(g)
    return None


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()
    rules = C.load_rules()
    deny = rules.get('DenyPatterns', [])
    cond = {}
    if os.path.exists(COND_PATH):
        cond = {k: v.get('condition') for k, v in json.load(open(COND_PATH, encoding='utf-8')).items()}

    # prefab name per metadata guid
    out = {}
    cat_counts = Counter()
    conf_counts = Counter()
    autohide = 0
    samples = defaultdict(list)

    for gstr, m in meta.items():
        g = int(gstr)
        prefab = names.get(g, '')
        tok = tokens(prefab)
        ev = []

        # --- SUMMON GUARD (computed first; vetoes any junk call) ---
        is_summon = cond.get(gstr) == 'Summon'
        char_spawn = None
        casts = []
        gtxt = C.read(idx, g)
        if gtxt:
            casts = C.group_casts(C.split_sections(gtxt))
            char_spawn = chain_spawns_char(idx, names, casts)
        summon_guarded = is_summon or bool(char_spawn)

        # --- name-token classification ---
        category, confidence = None, None
        if prefab.startswith('AB_Emote_'):
            category, confidence = 'emote', 'HIGH'
            ev.append('AB_Emote_ prefix')
        else:
            for cat, (conf, toks) in NAME_CATEGORIES.items():
                hit = next((t for t in toks if t in tok), None)
                if hit:
                    category, confidence = cat, conf
                    ev.append(f'name token "{hit}"')
                    break

        # NOTE: a structural "no payload reached" heuristic was evaluated and DROPPED — it produced
        # false positives on obviously-real abilities (Chaosbarrage, LightningArc, crossbow shots)
        # because payloads are reached via chains/components the static walk doesn't fully model.
        # Per the exactness discipline we emit no structural junk signal rather than guess.

        # cross-ref shipped deny-patterns (informational)
        deny_hit = next((p for p in deny if p in prefab), None)
        if deny_hit:
            ev.append(f'matches shipped DenyPattern {deny_hit}')

        junk = category is not None and not summon_guarded
        status = ''
        if junk and category in HIGH_AUTOHIDE and confidence == 'HIGH':
            status = 'Hidden'
            autohide += 1

        if summon_guarded and category:
            # a name-flagged entry rescued by the summon guard — record it, it's a real ability
            ev.append('SUMMON-GUARD: ' + (f'spawns {char_spawn}' if char_spawn else 'condition=Summon'))
            category = 'rescued_summon'
            junk = False

        rec = dict(name=m.get('name', ''), prefab=prefab, junk=junk,
                   category=category or 'collectible', confidence=confidence or '',
                   status=status, evidence=ev[:6])
        out[gstr] = rec
        cat_counts[rec['category']] += 1
        if junk:
            conf_counts[confidence] += 1
        if len(samples[rec['category']]) < 12:
            samples[rec['category']].append(rec)

    json.dump(out, open(OUT_JSON, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    total = len(out)
    junk_total = sum(1 for r in out.values() if r['junk'])
    collectible = total - junk_total

    # ---- report ----
    L = []
    L.append('# B1 — Collectibility / junk audit (auto-generated, source=auto)\n')
    L.append('Deterministic partition of the shipped ability universe. **Verify before applying** — '
             'this proposes `Hidden` only for HIGH-confidence definitional junk; everything else is for '
             'manual review. Summon abilities are guarded (never junked).\n')
    L.append(f'- **Total abilities:** {total}')
    L.append(f'- **Collectible (real):** {collectible}')
    L.append(f'- **Junk (flagged):** {junk_total}  — of which **{autohide} propose `Hidden`** (HIGH-confidence)')
    L.append(f'- **Summon-guard rescues:** {cat_counts.get("rescued_summon", 0)} (name looked like junk, but the ability summons a unit → kept)\n')
    L.append('## Category distribution\n')
    L.append('| category | count | auto-hide? |')
    L.append('|---|---|---|')
    for cat, n in cat_counts.most_common():
        ah = 'YES (Hidden)' if cat in HIGH_AUTOHIDE else ('—' if cat == 'collectible' or cat == 'rescued_summon' else 'manual review')
        L.append(f'| {cat} | {n} | {ah} |')
    L.append('')
    L.append('## Junk confidence\n')
    for conf, n in conf_counts.most_common():
        L.append(f'- {conf}: {n}')
    L.append('')
    L.append('## Samples per category (verify these against the source unit\'s kit)\n')
    for cat in list(NAME_CATEGORIES) + ['rescued_summon']:
        if cat not in samples:
            continue
        L.append(f'### {cat}  ({cat_counts[cat]})')
        for r in samples[cat]:
            tag = f"→ {r['status']}" if r['status'] else ('(rescued)' if cat == 'rescued_summon' else '(review)')
            L.append(f"- `{r['prefab']}` — {r['name']} {tag}  <sub>{'; '.join(r['evidence'])}</sub>")
        L.append('')
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'Total {total} | collectible {collectible} | junk {junk_total} | propose Hidden {autohide} '
          f'| summon-rescued {cat_counts.get("rescued_summon", 0)}')
    print('Categories:', ', '.join(f'{c}={n}' for c, n in cat_counts.most_common()))
    print(f'Wrote {os.path.normpath(OUT_JSON)}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
