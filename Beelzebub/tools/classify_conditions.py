"""
Deterministic ability ACTIVATION-CONDITION classifier.

Parses the V Rising prefab dump (Reference Data/Prefabs) and classifies every
AB_*_AbilityGroup by HOW a player makes it do something — its activation
condition — from EXACT component fingerprints, following the spawn chain
(Group -> Cast(s) -> AbilitySpawnPrefabOnCast -> payload prefab(s)).

This is DISTINCT from the existing `incompatible` flag (which marks abilities
that are BROKEN for players). A condition label means the ability WORKS, but
only under a stated condition (aim it, be surrounded, hit something, ...).

Design priority: EXACTNESS over coverage. When the signal is ambiguous we emit
`Unclassified` (for manual review) rather than guess — to avoid false positives
that could mislabel a functional ability.

Output: ability_conditions.json  { "<guid>": {name, condition, modifiers[], descriptor, evidence[]}, ... }
Run:    python classify_conditions.py
"""
import re, os, glob, json, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

PREFABS = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Reference Data\Prefabs'
OUT = os.path.join(os.path.dirname(__file__), 'ability_conditions.json')

GUID_RE = re.compile(r'PrefabGuid\((-?\d+)\)')
COMP_RE = re.compile(r'^  ([A-Za-z][\w.]+(?:\+[\w]+)?)\s*$')
SPAWN_RE = re.compile(r'SpawnPrefab:\s*.*?PrefabGuid\((-?\d+)\)')
TARGET_RE = re.compile(r'Target:\s*\S+\s+(\w+)\s*$', re.M)
PREFABGUID_LINE_RE = re.compile(r'PrefabGUID:\s*.*?PrefabGuid\((-?\d+)\)')

def build_index():
    idx, names = {}, {}
    for fp in glob.glob(os.path.join(PREFABS, '*.txt')):
        base = os.path.basename(fp)
        m = GUID_RE.search(base)
        if m:
            g = int(m.group(1))
            idx[g] = fp
            names[g] = base[:m.start()].strip()
    return idx, names

_cache = {}
def read(idx, guid):
    if guid in _cache: return _cache[guid]
    fp = idx.get(guid)
    txt = None
    if fp:
        try:
            with open(fp, 'r', encoding='utf-8', errors='replace') as f:
                txt = f.read()
        except Exception:
            txt = None
    _cache[guid] = txt
    return txt

def split_sections(text):
    """Return dict: component_name -> list of body strings (one per occurrence)."""
    secs, cur, buf = {}, None, []
    for line in text.splitlines():
        m = COMP_RE.match(line)
        if m:
            if cur is not None:
                secs.setdefault(cur, []).append('\n'.join(buf))
            cur, buf = m.group(1), []
        else:
            buf.append(line)
    if cur is not None:
        secs.setdefault(cur, []).append('\n'.join(buf))
    return secs

def has(secs, comp):
    return comp in secs

def group_casts(secs):
    """Casts a group starts (AbilityGroupStartAbilitiesBuffer PrefabGUID entries)."""
    out = []
    for body in secs.get('ProjectM.AbilityGroupStartAbilitiesBuffer', []):
        out += [int(g) for g in PREFABGUID_LINE_RE.findall(body)]
    return out

def spawn_on_cast(secs):
    """(spawnGuid, target) pairs from AbilitySpawnPrefabOnCast (the cast payload)."""
    out = []
    for body in secs.get('ProjectM.AbilitySpawnPrefabOnCast', []):
        spawns = SPAWN_RE.findall(body)
        targets = TARGET_RE.findall(body)
        for i, g in enumerate(spawns):
            out.append((int(g), targets[i] if i < len(targets) else '?'))
    return out

# ---- chain traversal (collect every prefab the cast can reach) ----
BUFF_RE = re.compile(r'Buff\d?:\s*.*?PrefabGuid\((-?\d+)\)')

def reachable(idx, casts, max_depth=4, max_nodes=60):
    """BFS over the spawn/apply chain from the casts. Returns set of reached guids."""
    seen, frontier, depth = set(), list(casts), 0
    while frontier and depth < max_depth and len(seen) < max_nodes:
        nxt = []
        for g in frontier:
            if g in seen: continue
            seen.add(g)
            txt = read(idx, g)
            if not txt: continue
            secs = split_sections(txt)
            for body in (secs.get('ProjectM.AbilitySpawnPrefabOnCast', [])
                         + secs.get('ProjectM.AbilitySpawnPrefabOnStartCast', [])):
                nxt += [int(x) for x in SPAWN_RE.findall(body)]
            for body in secs.get('ProjectM.ApplyBuffOnGameplayEvent', []):
                nxt += [int(x) for x in BUFF_RE.findall(body)]
            for comp in ('ProjectM.UnitSpawnerOnGameplayEvent', 'ProjectM.SpawnUnitsOnGameplayEvent',
                         'ProjectM.SpawnSquadOnGameplayEvent'):
                for body in secs.get(comp, []):
                    nxt += [int(x) for x in (PREFABGUID_LINE_RE.findall(body) + GUID_RE.findall(body))]
        frontier = [g for g in nxt if g not in seen]
        depth += 1
    return seen

def has_charges(secs):
    return has(secs, 'ProjectM.AbilityChargesData')

def max_cast_time(secs):
    for body in secs.get('ProjectM.AbilityCastTimeData', []):
        m = re.search(r'MaxCastTime:\s*([\d.]+)', body)
        if m: return float(m.group(1))
    return None

MOVEMENT_HINT = re.compile(r'travel|dash|leap|gallop|teleport|veil|blink|jump|roll|sprint|retreat', re.I)
# Exact summon component fingerprints (word-bounded so "Unity"/"SpawnTag" never match).
SUMMON_COMPS = re.compile(r'(SpawnMinion|SpawnSquad|SpawnServant|SpawnAlly|SpawnUnits|UnitSpawner)')

def classify(idx, names, group_guid):
    gtext = read(idx, group_guid)
    if gtext is None:
        return dict(condition='Unclassified', modifiers=[], descriptor='no prefab data', evidence=['group prefab missing'])
    gsecs = split_sections(gtext)
    casts = group_casts(gsecs)
    name = names.get(group_guid, str(group_guid))

    modifiers, evidence = [], []
    if len(casts) > 1:
        modifiers.append('Combo'); evidence.append(f'{len(casts)} casts in group')

    sig = dict(aim=False, projectile=False, selfaoe=False, onhit=False, selfbuff=False,
               summon=False, charged=False, channel=False, movement=bool(MOVEMENT_HINT.search(name)))

    # cast-level signals
    for cguid in casts:
        csecs = split_sections(read(idx, cguid) or '')
        if has(csecs, 'ProjectM.AbilityAimPrediction'):
            sig['aim'] = True; evidence.append('AbilityAimPrediction on cast')
        if has_charges(csecs):
            sig['charged'] = True
        mct = max_cast_time(csecs)
        if mct is not None and mct >= 1.5:
            sig['channel'] = True; evidence.append(f'long cast {mct}s')

    # chain-wide signals (every reachable prefab)
    for g in reachable(idx, casts):
        nm = names.get(g, '')
        if nm.startswith('CHAR_'):
            sig['summon'] = True; evidence.append(f'spawns unit {nm}')
        secs = split_sections(read(idx, g) or '')
        if not secs: continue
        if has_charges(secs): sig['charged'] = True
        if any(SUMMON_COMPS.search(c) for c in secs):
            sig['summon'] = True; evidence.append(f'{nm or g} spawns units')
        if has(secs, 'ProjectM.Projectile'):
            sig['projectile'] = True; evidence.append(f'{nm or g} is Projectile')
        # genuine self-centered AoE: collider anchored to Owner AND it strikes (on-hit), NOT a projectile
        if has(secs, 'ProjectM.HitColliderCast') and not has(secs, 'ProjectM.Projectile'):
            for b in secs.get('ProjectM.GetTranslationOnSpawn', []):
                if 'Owner' in b:
                    sig['selfaoe'] = True; evidence.append(f'{nm or g} = Owner-anchored collider')
                    break
        if has(secs, 'ProjectM.CreateGameplayEventsOnHit'):
            sig['onhit'] = True
        for b in secs.get('ProjectM.ApplyBuffOnGameplayEvent', []):
            tg = re.findall(r'BuffTarget:\s*\S+\s+(\w+)', b)
            if tg and all(t in ('Self', 'Owner') for t in tg):
                sig['selfbuff'] = True

    if sig['charged']: modifiers.append('Charged')
    if sig['channel']: modifiers.append('Channel')

    # PRIMARY condition — precedence chosen so the strongest/most-specific signal wins.
    # CloseRange is the honest umbrella for the close-range family (point-blank AoE + melee + on-hit),
    # which share prefab fingerprints and can't be split with certainty (per the v0.107 audit decision).
    if sig['summon']:
        cond, desc = 'Summon', 'Summons units/allies — no aim needed'
    elif sig['aim'] or sig['projectile']:
        cond, desc = 'Aimed', 'Aim at a target or direction — fires downrange'
    elif sig['selfaoe'] or sig['onhit']:
        cond, desc = 'CloseRange', 'Needs enemies next to / in front of you'
    elif sig['movement']:
        cond, desc = 'Movement', 'Mobility / travel — no target needed'
    elif sig['selfbuff']:
        cond, desc = 'SelfCast', 'Self/ally effect — works on press'
    else:
        cond, desc = 'Unclassified', 'needs manual review'

    return dict(condition=cond, modifiers=modifiers, descriptor=desc, evidence=evidence[:8])

# ---- validation against known-truth cases ----
KNOWN = {
    1631838527:  'Aimed',       # LightningOrb — confirmed in-game: works when aimed at target
    -409778117:  'CloseRange',  # CallLightning — confirmed: Owner-centered burst, needs adjacency
    436038744:   'Movement',    # Horse Vampire Leap Travel — mobility
    -820078889:  'CloseRange',  # Adam Lightning Storm — self-centered storm, needs enemies near
}

def main():
    idx, names = build_index()
    groups = [g for g, n in names.items() if n.endswith('_AbilityGroup') or n.endswith('_Group')]
    print(f'Indexed {len(idx)} prefabs; {len(groups)} ability groups.\n')

    print('=== VALIDATION (known-truth cases) ===')
    for g, expect in KNOWN.items():
        r = classify(idx, names, g)
        ok = 'OK ' if r['condition'] == expect else 'XX '
        print(f"  {ok} {names.get(g,g):52} expect={expect:12} got={r['condition']:12} mods={r['modifiers']}")
        for e in r['evidence']:
            print(f"        - {e}")
    print()

    results = {}
    counts = {}
    for g in groups:
        r = classify(idx, names, g)
        results[str(g)] = dict(name=names.get(g, str(g)), **r)
        counts[r['condition']] = counts.get(r['condition'], 0) + 1

    print('=== FULL-SET condition distribution ===')
    for c, n in sorted(counts.items(), key=lambda x: -x[1]):
        print(f'  {c:14} {n}')

    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump(results, f, indent=1, ensure_ascii=False)
    print(f'\nWrote {OUT} ({len(results)} entries).')

    # Unit-grouped markdown audit (facilitates the per-unit deep-check / verification pass).
    def family(nm):
        toks = nm.replace('_AbilityGroup', '').replace('_Group', '').split('_')
        return '_'.join(toks[1:3]) if len(toks) > 2 else (toks[1] if len(toks) > 1 else nm)
    fams = {}
    for g, v in results.items():
        fams.setdefault(family(v['name']), []).append(v)
    md = os.path.join(os.path.dirname(__file__), 'ABILITY_CONDITIONS_AUDIT.md')
    with open(md, 'w', encoding='utf-8') as f:
        f.write('# Ability activation-condition audit (auto-generated)\n\n')
        f.write('Deterministic classification from prefab data. `source=auto` — confirm in-game before treating as authoritative.\n')
        f.write('Categories: Aimed · CloseRange · Summon · SelfCast · Movement · Unclassified (+ Combo/Charged/Channel modifiers).\n\n')
        f.write('| count | ' + ' | '.join(f'{c}' for c, _ in sorted(counts.items(), key=lambda x: -x[1])) + ' |\n')
        f.write('|---|' + '---|' * len(counts) + '\n')
        f.write('| | ' + ' | '.join(str(n) for _, n in sorted(counts.items(), key=lambda x: -x[1])) + ' |\n\n')
        for fam in sorted(fams):
            rows = sorted(fams[fam], key=lambda v: v['name'])
            f.write(f'## {fam}  ({len(rows)})\n\n')
            for v in rows:
                mods = (' +' + ','.join(v['modifiers'])) if v['modifiers'] else ''
                ev = v['evidence'][0] if v['evidence'] else ''
                f.write(f"- **{v['condition']}**{mods} — `{v['name']}`  <sub>{ev}</sub>\n")
            f.write('\n')
    print(f'Wrote {md} (unit-grouped, {len(fams)} families).')

if __name__ == '__main__':
    main()
