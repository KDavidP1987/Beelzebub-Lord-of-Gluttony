"""
B3 (prep-roadmap) — COMBO / SEQUENCE audit.

Identifies multi-cast ability groups and non-standalone sequence pieces, so combo abilities can be
chained onto one slot (field A3 chainGroup/comboParent) or their fragments hidden. Uses a PRECISE,
verifiable signal — the number of cast steps a group starts (AbilityGroupStartAbilitiesBuffer) —
plus the `_Sequence_`/`_Combo_` name token and the condition classifier's `Combo` modifier as
corroboration. No fuzzy inference.

A group with >1 cast step is a multi-cast combo (e.g. a 3-hit melee chain or a wind-up→release pair).
The individual cast prefabs are the combo's pieces; the GROUP is the standalone unit a player binds.

Outputs:
  - audit_combos.json   {guid: {name, prefab, castCount, casts[], combo(bool), sequencePiece(bool), evidence[]}}
  - AUDIT_COMBOS.md     report (multi-cast groups by cast-count, sequence pieces)
Read-only.  Run: python audit_combos.py
"""
import os, re, sys, json
from collections import Counter, defaultdict
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
COND_PATH = os.path.join(HERE, 'ability_conditions.json')
OUT_JSON = os.path.join(HERE, 'audit_combos.json')
OUT_MD = os.path.join(HERE, 'AUDIT_COMBOS.md')

SEQ_TOKENS = ('Sequence', 'Combo')


def main():
    idx, names = C.build_index()
    meta = C.load_metadata()
    cond_mods = {}
    if os.path.exists(COND_PATH):
        cond_mods = {k: (v.get('modifiers') or []) for k, v in json.load(open(COND_PATH, encoding='utf-8')).items()}

    out = {}
    count_hist = Counter()
    combo_total = 0
    seq_pieces = 0

    for gstr, m in meta.items():
        g = int(gstr)
        prefab = names.get(g, '')
        gtxt = C.read(idx, g)
        casts = C.group_casts(C.split_sections(gtxt)) if gtxt else []
        ev = []

        toks = set(prefab.replace('_AbilityGroup', '').replace('_Group', '').split('_'))
        seq_name = next((t for t in SEQ_TOKENS if t in toks), None)
        has_combo_mod = 'Combo' in cond_mods.get(gstr, [])

        combo = len(casts) > 1
        if combo:
            ev.append(f'{len(casts)} cast steps')
        if seq_name:
            ev.append(f'name token "{seq_name}"')
        if has_combo_mod:
            ev.append('condition modifier Combo')

        # a "sequence piece" = named like a sequence/combo fragment AND single-cast (a step, not the whole)
        sequence_piece = bool(seq_name) and not combo

        rec = dict(name=m.get('name', ''), prefab=prefab, castCount=len(casts),
                   casts=[names.get(c, str(c)) for c in casts],
                   combo=combo, sequencePiece=sequence_piece, evidence=ev)
        out[gstr] = rec
        if combo:
            combo_total += 1
            count_hist[len(casts)] += 1
        if sequence_piece:
            seq_pieces += 1

    json.dump(out, open(OUT_JSON, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)

    L = []
    L.append('# B3 — Combo / sequence audit (auto-generated, source=auto)\n')
    L.append('Multi-cast groups (a player binds the GROUP; it fires its cast steps in sequence) and '
             'sequence-named fragments. Precise signal = AbilityGroupStartAbilitiesBuffer cast count. '
             'These feed the A3 `chainGroup`/`comboParent` field and the "hide non-standalone pieces" goal.\n')
    L.append(f'- **Multi-cast combo groups:** {combo_total}')
    L.append(f'- **Sequence-named single-cast pieces:** {seq_pieces}')
    L.append('\n## Combo groups by cast-step count\n')
    L.append('| cast steps | groups |')
    L.append('|---|---|')
    for n in sorted(count_hist):
        L.append(f'| {n} | {count_hist[n]} |')
    L.append('')
    # list the biggest combos (most cast steps) for review
    biggest = sorted((r for r in out.values() if r['combo']), key=lambda r: -r['castCount'])[:40]
    L.append('## Largest combos (top 40 by cast-step count)\n')
    for r in biggest:
        L.append(f"- **{r['castCount']}** — `{r['prefab']}` ({r['name']})  <sub>{' · '.join(r['casts'][:6])}</sub>")
    L.append('')
    if seq_pieces:
        L.append('## Sequence-named single-cast pieces (candidate hide / chain-fragment)\n')
        for r in (x for x in out.values() if x['sequencePiece']):
            L.append(f"- `{r['prefab']}` ({r['name']})")
        L.append('')
    open(OUT_MD, 'w', encoding='utf-8').write('\n'.join(L))

    print(f'Multi-cast combo groups: {combo_total} | sequence-named pieces: {seq_pieces}')
    print('Cast-count histogram:', ', '.join(f'{n}x={count_hist[n]}' for n in sorted(count_hist)))
    print(f'Wrote {os.path.normpath(OUT_JSON)}')
    print(f'Wrote {os.path.normpath(OUT_MD)}')


if __name__ == '__main__':
    main()
