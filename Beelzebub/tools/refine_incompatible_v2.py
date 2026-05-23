"""v0.24.3: refine the incompatible-ability tagging now that we have
the ScriptSpawnServer engine hook attempting to fix Team/Faction on
spawned proxy/projectile entities.

Changes:
  - REMOVE incompatible tag from AOE-Proxy abilities (ProjectileNova etc.)
    — the new engine hook attempts to handle these
  - REMOVE incompatible tag from OwnerChain abilities (TrippleBolt etc.)
    — same hook should handle these
  - KEEP incompatible tag on AnimRig abilities (Bishop ShadowStep,
    EternalDarkness, all Teleport_Travel variants) — these are bound to
    NPC animation skeletons; the client can't render them on a player.
    No server-side fix possible.
"""
import json, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

METADATA = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Beelzebub\Beelzebub\Resources\ability_metadata.json'

# Reasons we KEEP — true client-side limitations, no server-side fix.
KEEP_REASONS = ('AnimRig',)

with open(METADATA, 'r', encoding='utf-8') as f:
    data = json.load(f)

before_count = 0
kept_count = 0
removed_count = 0

for key, entry in data['abilities'].items():
    if not entry.get('incompatible'):
        continue
    before_count += 1
    reason = (entry.get('incompatibleReason') or '').lower()
    if any(k.lower() in reason for k in KEEP_REASONS):
        kept_count += 1
        continue
    entry.pop('incompatible', None)
    entry.pop('incompatibleReason', None)
    removed_count += 1

print(f'Tagged incompatible BEFORE: {before_count}')
print(f'Kept (AnimRig — truly broken): {kept_count}')
print(f'Removed (engine hook will attempt): {removed_count}')

with open(METADATA, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
import os
print(f'Wrote {METADATA} ({os.path.getsize(METADATA):,} bytes)')
