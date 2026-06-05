"""
Shared prefab-dump parsing for the ability AUDIT tools (B1 junk / B2 incompatible / B3 combos).

Extracted from classify_conditions.py so the three audits index + parse the prefab dump the
same way (no drift). Read-only; never writes a source file.

Exposes: PREFABS path, build_index(), read(), split_sections(), has(), group_casts(),
spawn_on_cast(), reachable(), has_charges(), max_cast_time(), load_metadata(), load_rules().
"""
import re, os, glob, json

PREFABS = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Reference Data\Prefabs'
_RES = os.path.join(os.path.dirname(__file__), '..', 'Beelzebub', 'Resources')
META_PATH = os.path.join(_RES, 'ability_metadata.json')
RULES_PATH = os.path.join(_RES, 'ability_rules.default.json')

GUID_RE = re.compile(r'PrefabGuid\((-?\d+)\)')
COMP_RE = re.compile(r'^  ([A-Za-z][\w.]+(?:\+[\w]+)?)\s*$')
SPAWN_RE = re.compile(r'SpawnPrefab:\s*.*?PrefabGuid\((-?\d+)\)')
TARGET_RE = re.compile(r'Target:\s*\S+\s+(\w+)\s*$', re.M)
PREFABGUID_LINE_RE = re.compile(r'PrefabGUID:\s*.*?PrefabGuid\((-?\d+)\)')
BUFF_RE = re.compile(r'Buff\d?:\s*.*?PrefabGuid\((-?\d+)\)')


def build_index():
    """guid -> filepath, guid -> prefab name (parsed from the dump filenames)."""
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
    if guid in _cache:
        return _cache[guid]
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
    """component_name -> list of body strings (one per occurrence)."""
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
    """Cast guids a group starts (AbilityGroupStartAbilitiesBuffer)."""
    out = []
    for body in secs.get('ProjectM.AbilityGroupStartAbilitiesBuffer', []):
        out += [int(g) for g in PREFABGUID_LINE_RE.findall(body)]
    return out


def spawn_on_cast(secs):
    """(spawnGuid, target) pairs from AbilitySpawnPrefabOnCast."""
    out = []
    for body in secs.get('ProjectM.AbilitySpawnPrefabOnCast', []):
        spawns = SPAWN_RE.findall(body)
        targets = TARGET_RE.findall(body)
        for i, g in enumerate(spawns):
            out.append((int(g), targets[i] if i < len(targets) else '?'))
    return out


def reachable(idx, casts, max_depth=4, max_nodes=60):
    """BFS over the spawn/apply chain from the casts. Returns set of reached guids."""
    seen, frontier, depth = set(), list(casts), 0
    while frontier and depth < max_depth and len(seen) < max_nodes:
        nxt = []
        for g in frontier:
            if g in seen:
                continue
            seen.add(g)
            txt = read(idx, g)
            if not txt:
                continue
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
        if m:
            return float(m.group(1))
    return None


def load_metadata():
    return json.load(open(META_PATH, encoding='utf-8'))['abilities']


def load_rules():
    return json.load(open(RULES_PATH, encoding='utf-8'))
