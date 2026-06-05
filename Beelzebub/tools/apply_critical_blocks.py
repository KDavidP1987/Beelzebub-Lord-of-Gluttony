"""
Apply the v0.100 tester CRITICAL list (crash / stuck) as hard blocks to ability_rules.default.json.

Sets Enabled=false + ReviewStatus=Blocked + ReviewTag=crash|stuck on each confirmed game/server-breaker
or character-stuck ability. Resolves IDs -> prefab name via Resources/prefab_names.tsv; the few without an
ID are resolved by keyword search (reported for confirmation). DRY-RUN by default; --write commits.

Source: docs/TESTER_BASELINE_v0100.md Part A.
"""
import os, sys, json, argparse
import audit_common as C

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
HERE = os.path.dirname(__file__)
NAMES = os.path.join(HERE, '..', 'Beelzebub', 'Resources', 'prefab_names.tsv')

# (id|None, keyword if no id, tag, note)
# ⚠️ v0.129.0 — this list was PRUNED as testers re-verified. DO NOT re-add the removed entries:
#   • UNBLOCKED (could not reproduce on current build): Dracula Bolt Spray 1957691133, Morgana Swarm
#     -1980019894 + Orb Barrage 1242557903, Leandra ShadowStep 1325722355 + TrippleBolt -1795148379.
#   • RECONFIGURED instead of blocked (launch → leapheight clamp + cooldown, see ability_rules.default.json):
#     Elena ToF 1431473799 / 2057952818, Toad King Poison Leap 1790744720 / Swallow 1292896032 / Spit
#     -1238687119, Gargoyle Fly -382913708 / 1563014858 / 1551140710, Ziva Jetpack -1770586075.
# What remains below = still-blocked confirmed crashers + character-breaks + the 2 OP exploits.
CRITICALS = [
    (1485838951,  None,                 'crash',   'GAME CRASH (Gloomrot Technician Fiddle) when vanilla sword-E used while bound - tester v0.100'),
    (1322698651,  None,                 'crash',   'GAME CRASH (Gaius Undead Arena Champion Twinblade Throw) - tester v0.100'),
    (938684260,   None,                 'stuck',   'STUCK: permanent T-pose, must self-kill (Cassius High Lord Leap Strike) - tester v0.100'),
    (-891106318,  None,                 'stuck',   'STUCK: permanent invisibility on respawn (Spider Baneling Explode Poison) - tester v0.100'),
    (-485230865,  None,                 'stuck',   'CHARACTER-BREAK: locks in place, aggro off (Gaius Corpse Buff - the BUFF) - tester v0.100'),
    (-89125940,   None,                 'stuck',   'CHARACTER-BREAK: permanent locked/phased/invisible, survives relog (Gaius Corpse Buff ABILITY GROUP) - re-audit 2026-06-05'),
    # v0.129.0 — OP exploits (balance, data-block only, NOT hard-block):
    (1460741503,  None,                 'exploit', 'EXPLOIT: immortality + heal (Gargoyle Wing Shield) - 2026-06-05'),
    (830495620,   None,                 'exploit', 'EXPLOIT: infinite-invuln summons (Rat Vanguard) - 2026-06-05'),
]
# Treant/Golem "Fall Asleep" family — reported as toggle-lock. Block all AB_*FallAsleep* GROUPS by keyword.
FALLASLEEP_KEYWORD = 'FallAsleep'

# Confirmed crash + permanent-character-break GUIDs also get the C# hard-block (bulletproof; honored even in
# Capture_InclusiveMode, which testers run). Recoverable-stuck ones (space-launch / OOB / Fall-Asleep) are
# data-blocked only (Enabled=false), so they can be unblocked for fix-research.
# v0.129.0: pruned to match the C# _hardBlockedGuids after the re-test unblocks (Dracula/Morgana/Leandra removed).
HARDBLOCK = {1485838951, 1322698651, -485230865, 938684260, -891106318, -89125940}


def load_g2n():
    g2n = {}
    for line in open(NAMES, encoding='utf-8'):
        if '\t' in line:
            g, n = line.rstrip('\n').split('\t', 1)
            g2n[int(g)] = n.strip()
    return g2n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()
    g2n = load_g2n()
    n2g = {n: g for g, n in g2n.items()}
    rules = C.load_rules()
    amap = rules.setdefault('AbilityMap', {})

    targets = []   # (name, tag, note)
    unresolved = []
    for gid, kw, tag, note in CRITICALS:
        if gid is not None:
            name = g2n.get(gid)
            if not name:
                unresolved.append((gid, kw, note)); continue
            targets.append((name, tag, note))
        else:
            # keyword search over ability-group names
            hits = [n for n in n2g if kw.lower() in n.lower() and (n.endswith('_AbilityGroup') or n.endswith('_Group'))]
            if not hits:
                unresolved.append((None, kw, note)); continue
            for n in hits:
                targets.append((n, tag, note))
    # Fall-Asleep family (Treant/Golem) — block the GROUPs; exclude the Bear ones already reviewed if desired (keep all)
    fa = [n for n in n2g if FALLASLEEP_KEYWORD.lower() in n.lower() and (n.endswith('_AbilityGroup') or n.endswith('_Group'))]
    for n in fa:
        targets.append((n, 'stuck', 'STUCK: toggle-lock until Wake Up (Treant/Golem/Bear Fall Asleep) - tester v0.100'))

    # de-dup by name
    seen = set(); uniq = []
    for name, tag, note in targets:
        if name in seen: continue
        seen.add(name); uniq.append((name, tag, note))

    print(f'CRITICAL BLOCKS -> {len(uniq)} abilities')
    for name, tag, note in uniq:
        e = amap.setdefault(name, {})
        e['Enabled'] = False
        e['ReviewStatus'] = 'Blocked'
        e['ReviewTag'] = tag
        e.setdefault('Notes', note)
        print(f'   [{tag:5}] {name}')
    if unresolved:
        print('\nUNRESOLVED (resolve manually):')
        for gid, kw, note in unresolved:
            print(f'   id={gid} kw={kw}  ({note})')
    print('\nC# _hardBlockedGuids to ADD (server-crashers):', sorted(HARDBLOCK))

    if not args.write:
        print('\nDRY-RUN — nothing written. Re-run with --write.')
        return
    json.dump(rules, open(C.RULES_PATH, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    print(f'\nWROTE {os.path.normpath(C.RULES_PATH)} ({len(amap)} entries) — lint + rebuild.')


if __name__ == '__main__':
    main()
