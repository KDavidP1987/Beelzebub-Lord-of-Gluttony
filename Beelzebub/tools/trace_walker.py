"""
Walker-coverage research tool. For a given ability GUID, compares:
  - CURRENT reach  = Group -> Cast(AbilityGroupStartAbilitiesBuffer) -> AbilitySpawnPrefabOnCast spawns
                     (exactly what AbilityTuningService.ApplyDownstream walks today)
  - BROADENED reach = current + follow ApplyBuffOnGameplayEvent (Buff0..3) + AbilitySpawnPrefabOnStartCast

For every reached prefab it flags which TUNABLE components it carries, so we can see:
  (a) do the WORKING cases keep their tunable component inside CURRENT reach (=> broadening can't change them)?
  (b) do the BROKEN cases only expose their tunable component under BROADENED reach (=> broadening fixes them)?
  (c) are newly-reached prefabs SHARED by other abilities (the blast-radius hazard)?

Run: python trace_walker.py
"""
import re, os, glob, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
PREFABS = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Reference Data\Prefabs'

GUID_RE = re.compile(r'PrefabGuid\((-?\d+)\)')
COMP_RE = re.compile(r'^  ([A-Za-z][\w.]+(?:\+[\w]+)?)\s*$')
SPAWN_RE = re.compile(r'SpawnPrefab:\s*.*?PrefabGuid\((-?\d+)\)')
BUFF_RE = re.compile(r'Buff\d?:\s*.*?PrefabGuid\((-?\d+)\)')
PG_RE = re.compile(r'PrefabGUID:\s*.*?PrefabGuid\((-?\d+)\)')

# tunable components keyed by the admin field they drive
TUNABLE = {
    'ProjectM.Projectile':              'range/projspeed (Projectile.Range/Speed)',
    'ProjectM.TargetAoE':               'aoe (TargetAoE.MaxRange)',
    'ProjectM.HealOnGameplayEvent':     'healing (HealOnGameplayEvent buffer)',
    'ProjectM.ApplyBuffOnGameplayEvent':'duration (ApplyBuffOnGameplayEvent.OverrideDuration)',
    'ProjectM.AbilityCooldownData':     'cooldown',
    'ProjectM.AbilityGroupInfo':        'range aim-clamp (AbilityGroupInfo.MaxRange)',
    'ProjectM.AbilityChargesData':      'charges/chargetime',
    'ProjectM.LifeTime':                'duration/forcetimeout (LifeTime)',
    'ProjectM.ModifyMovementDuringCastData':'castspeed/freemove',
    'ProjectM.AbilityInterruptData':    'interruptible/interruptonhit',
}

def build_index():
    idx, names = {}, {}
    for fp in glob.glob(os.path.join(PREFABS, '*.txt')):
        m = GUID_RE.search(os.path.basename(fp))
        if m:
            g = int(m.group(1)); idx[g] = fp; names[g] = os.path.basename(fp)[:m.start()].strip()
    return idx, names

_cache = {}
def read(idx, g):
    if g in _cache: return _cache[g]
    fp = idx.get(g); t = None
    if fp:
        try: t = open(fp, encoding='utf-8', errors='replace').read()
        except: t = None
    _cache[g] = t; return t

def sections(text):
    secs, cur, buf = {}, None, []
    for line in text.splitlines():
        m = COMP_RE.match(line)
        if m:
            if cur: secs.setdefault(cur, []).append('\n'.join(buf))
            cur, buf = m.group(1), []
        else: buf.append(line)
    if cur: secs.setdefault(cur, []).append('\n'.join(buf))
    return secs

def casts(idx, group):
    s = sections(read(idx, group) or '')
    out = []
    for b in s.get('ProjectM.AbilityGroupStartAbilitiesBuffer', []): out += [int(x) for x in PG_RE.findall(b)]
    return out

def reach(idx, group, broadened):
    seen, frontier = set(), list(casts(idx, group))
    base = set(frontier)
    depth = 0
    while frontier and depth < 6 and len(seen) < 80:
        nxt = []
        for g in frontier:
            if g in seen: continue
            seen.add(g)
            s = sections(read(idx, g) or '')
            for b in s.get('ProjectM.AbilitySpawnPrefabOnCast', []): nxt += [int(x) for x in SPAWN_RE.findall(b)]
            if broadened:
                for b in s.get('ProjectM.AbilitySpawnPrefabOnStartCast', []): nxt += [int(x) for x in SPAWN_RE.findall(b)]
                for b in s.get('ProjectM.ApplyBuffOnGameplayEvent', []): nxt += [int(x) for x in BUFF_RE.findall(b)]
        frontier = [g for g in nxt if g not in seen]; depth += 1
    return seen

def tunables(idx, g):
    s = sections(read(idx, g) or '')
    return [TUNABLE[c] for c in TUNABLE if c in s]

CASES = {
    'Chain Bolt Hard (range/projspeed WORKS)':      -323939446,
    'Spider Queen AoE (range/projspeed BROKEN)':     1573712465,
    'Priest Heal Bomb (healing WORKS)':             -1282606238,
    'Cleric Heal (healing WORKS)':                  -923957888,
    'Nun AoE (healing BROKEN)':                       919394375,
    'Nun AoE VBlood (healing BROKEN)':              1267698255,
}

def main():
    idx, names = build_index()
    # precompute which prefabs are reached by OTHER abilities (shared-prefab hazard)
    for label, g in CASES.items():
        cur = reach(idx, g, False)
        broad = reach(idx, g, True)
        added = broad - cur
        print(f'\n==== {label}   [{names.get(g,g)}] ====')
        print(f'  CURRENT reach: {len(cur)} prefabs   BROADENED: {len(broad)} (+{len(added)})')
        print('  -- tunable components in CURRENT reach --')
        for p in sorted(cur):
            t = tunables(idx, p)
            if t: print(f'     {names.get(p,p):48} {t}')
        if added:
            print('  -- NEWLY reached by broadening (only these can change) --')
            for p in sorted(added):
                t = tunables(idx, p)
                tag = '  <-- tunable!' if t else ''
                print(f'     {names.get(p,p):48} {t}{tag}')

if __name__ == '__main__':
    main()
