"""Add `incompatible` + `incompatibleReason` flags to ability_metadata.json
for known-broken abilities per the chain-failure audit.

Categories (from project_ability_chain_audit.md):
  Offset       — projectile spawns at hardcoded NPC bone offset
  AnimRig      — TravelBuff + HideWeapon bound to NPC skeleton bones
  OwnerChain   — multi-step chain loses owner reference
  TeamFilter   — spawned children filter on Team=1 (NPC)
  AOE-Proxy    — ProxySpawner AOE with NPC team; projectiles fire but miss
  Corpse-Required — natural chain animates existing corpses; player has none
"""
import json, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

METADATA = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Beelzebub\Beelzebub\Resources\ability_metadata.json'

# Initial curation. Each entry: prefabGuid -> (reason, free-text description).
# Sourced from the v0.23 chain audit + Priest deep-dive in v0.24.2.
INCOMPATIBLES = {
    # --- Priest of Shadows ---
    # ProjectileNova — 360° projectile fan from ProxySpawner with Team=1.
    # 12 projectiles fire but team check makes them miss. AOE failure.
    1988754598: ('AOE-Proxy', 'fires 360° projectile nova but team check makes them miss enemies'),
    1619461812: ('AOE-Proxy', 'fires 360° projectile nova (Hard variant) but team check makes them miss enemies'),
    # Teleport_Travel — Phase entity with NPC bone-binding (class 2)
    -1125894381: ('AnimRig', 'travel/dash sequence bound to NPC animation rig'),

    # --- Bishop of Shadows ---
    # EternalDarkness Phase — TravelBuff + HideWeapon bone-bound
    # (Bishop EternalDarkness ability group — looked up by name pattern)
    # ShadowStep — same pattern
    # These are looked up by name below since we don't have hardcoded GUIDs.
}

# Name-pattern based incompatibles for abilities we haven't hard-mapped above.
# Substring match (case-insensitive) on the prefab name.
INCOMPATIBLE_PATTERNS = [
    ('_EternalDarkness_', 'AnimRig', 'phase ability bound to NPC animation rig'),
    ('_ShadowStep_', 'AnimRig', 'phase ability bound to NPC animation rig'),
    ('_Teleport_Travel_', 'AnimRig', 'travel/dash sequence bound to NPC animation rig'),
    ('_TrippleBolt_', 'OwnerChain', 'multi-bolt chain loses owner reference mid-spawn'),
]

def main():
    with open(METADATA, 'r', encoding='utf-8') as f:
        data = json.load(f)

    abilities = data['abilities']
    tagged = 0

    # Hard-mapped GUIDs.
    for guid, (reason, desc) in INCOMPATIBLES.items():
        key = str(guid)
        if key not in abilities:
            # Not in metadata — skip silently (would be a stub entry we don't have).
            continue
        abilities[key]['incompatible'] = True
        abilities[key]['incompatibleReason'] = f'{reason}: {desc}'
        tagged += 1

    # Pattern-based — needs a name lookup table. Build one from existing entries
    # plus the local prefab_names.tsv for entries whose stubs we have.
    # Use the entry's 'name' field as a humanized title. For pattern matching,
    # we need the RAW prefab name. We don't store it, so build it on the fly
    # from a local map.
    PREFAB_NAMES_TSV = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Beelzebub\Beelzebub\Resources\prefab_names.tsv'
    raw_names = {}
    with open(PREFAB_NAMES_TSV, 'r', encoding='utf-8') as f:
        for line in f:
            parts = line.rstrip('\n').split('\t')
            if len(parts) == 2:
                try: raw_names[int(parts[0])] = parts[1]
                except ValueError: pass

    pattern_tagged = 0
    for key, entry in abilities.items():
        if entry.get('incompatible'): continue
        guid = int(key)
        raw_name = raw_names.get(guid, '')
        if not raw_name: continue
        for pat, reason, desc in INCOMPATIBLE_PATTERNS:
            if pat.lower() in raw_name.lower():
                entry['incompatible'] = True
                entry['incompatibleReason'] = f'{reason}: {desc}'
                pattern_tagged += 1
                break

    print(f'Hard-tagged: {tagged}')
    print(f'Pattern-tagged: {pattern_tagged}')
    print(f'Total marked incompatible: {tagged + pattern_tagged}')

    data['abilities'] = abilities
    with open(METADATA, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    import os
    print(f'Wrote {METADATA} ({os.path.getsize(METADATA):,} bytes)')

if __name__ == '__main__':
    main()
