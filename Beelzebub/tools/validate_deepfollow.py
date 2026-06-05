"""
Validation: simulate the EXACT v0.108.0 guarded deep-follow (DeepFollowAbilitySpecific)
to prove it (a) reaches the broken AoE effects, (b) NEVER edits a shared/non-ability-specific
prefab, (c) leaves the working cases unchanged. Mirrors the C# logic 1:1.
"""
import re, os, glob, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
PREFABS = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Reference Data\Prefabs'
GUID_RE = re.compile(r'PrefabGuid\((-?\d+)\)')
COMP_RE = re.compile(r'^  ([A-Za-z][\w.]+(?:\+[\w]+)?)\s*$')
SPAWN_RE = re.compile(r'SpawnPrefab:\s*.*?PrefabGuid\((-?\d+)\)')
BUFF_RE = re.compile(r'Buff0:\s*.*?PrefabGuid\((-?\d+)\)')
PG_RE = re.compile(r'PrefabGUID:\s*.*?PrefabGuid\((-?\d+)\)')

def index():
    idx, names = {}, {}
    for fp in glob.glob(os.path.join(PREFABS, '*.txt')):
        m = GUID_RE.search(os.path.basename(fp))
        if m: g=int(m.group(1)); idx[g]=fp; names[g]=os.path.basename(fp)[:m.start()].strip()
    return idx, names
_c={}
def read(idx,g):
    if g in _c: return _c[g]
    t=None; fp=idx.get(g)
    if fp:
        try: t=open(fp,encoding='utf-8',errors='replace').read()
        except: pass
    _c[g]=t; return t
def secs(t):
    s,cur,buf={},None,[]
    for ln in (t or '').splitlines():
        m=COMP_RE.match(ln)
        if m:
            if cur: s.setdefault(cur,[]).append('\n'.join(buf))
            cur,buf=m.group(1),[]
        else: buf.append(ln)
    if cur: s.setdefault(cur,[]).append('\n'.join(buf))
    return s
def stem(nm): return re.sub(r'_(AbilityGroup|Group)$','',nm)

# which deepSafe field each prefab would receive (component-guarded, same as WriteSpawnedFields deepSafe)
def deep_fields(s):
    out=[]
    if 'ProjectM.TargetAoE' in s: out.append('aoe')
    if 'ProjectM.Projectile' in s: out.append('projspeed/range')
    if 'ProjectM.HealOnGameplayEvent' in s: out.append('healing')
    return out

def near_roots(idx, group):
    s=secs(read(idx,group)); roots=[]
    for b in s.get('ProjectM.AbilityGroupStartAbilitiesBuffer',[]):
        for cg in PG_RE.findall(b):
            cg=int(cg); roots.append(cg)
            cs=secs(read(idx,cg))
            for bb in cs.get('ProjectM.AbilitySpawnPrefabOnCast',[]):
                roots += [int(x) for x in SPAWN_RE.findall(bb)]
    return roots

def deepfollow(idx, names, group):
    st=stem(names[group]); seen=set(); edited=[]; skipped_shared=[]
    q=list(near_roots(idx,group)); budget=60
    while q and budget>0:
        budget-=1; g=q.pop(0)
        if g in seen: continue
        seen.add(g)
        nm=names.get(g,str(g)); s=secs(read(idx,g))
        if not s: continue
        spec = nm==st or nm.startswith(st+'_')
        if not spec:
            if deep_fields(s): skipped_shared.append((nm, deep_fields(s)))   # would-be-touched-but-GUARDED
            continue
        f=deep_fields(s)
        if f: edited.append((nm,f))
        # enqueue children (ability-specific only, same as C#)
        for bb in s.get('ProjectM.SpawnPrefabOnDestroy',[]): q+=[int(x) for x in SPAWN_RE.findall(bb)]
        for bb in s.get('ProjectM.AbilitySpawnPrefabOnCast',[]): q+=[int(x) for x in SPAWN_RE.findall(bb)]
        for bb in s.get('ProjectM.ApplyBuffOnGameplayEvent',[]): q+=[int(x) for x in BUFF_RE.findall(bb)]
    return st, edited, skipped_shared

CASES={'Nun AoE (heal was BROKEN)':919394375,'Spider Queen AoE (was BROKEN)':1573712465,
       'Chain Bolt (WORKS-must be unchanged)':-323939446,'Priest Heal Bomb (WORKS)':-1282606238}

def main():
    idx,names=index()
    for label,g in CASES.items():
        if g not in names: print(f'?? {label}: guid {g} not found'); continue
        st,edited,shared=deepfollow(idx,names,g)
        print(f'\n==== {label}  [stem={st}] ====')
        print('  DEEP-EDIT (ability-specific prefabs the guarded pass WOULD tune):')
        for nm,f in edited: print(f'     ✔ {nm:48} {f}')
        if not edited: print('     (none — effect already in near reach, or no deep ability-specific effect)')
        if shared:
            print('  GUARD STOPPED (shared/non-ability prefabs with tunable comps — NEVER edited):')
            for nm,f in shared: print(f'     ⛔ {nm:48} {f}')
        else:
            print('  GUARD: no shared prefab reached. ✓')

if __name__=='__main__': main()
