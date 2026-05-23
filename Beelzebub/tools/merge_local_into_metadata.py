"""Merge local prefab-name stubs into ability_metadata.json so every
AbilityGroup/Group prefab in the local dump has at least a name entry.

Strategy:
  - Start with the scraped gaming.tools data (preserves 1,426 rich entries).
  - For every AB_*_AbilityGroup / AB_*_Group in prefab_names.tsv NOT already
    in the metadata, add a stub entry with a HUMANIZED name derived from the
    raw prefab name. This keeps the file complete even where gaming.tools is
    silent.
  - Stub entries have only `name` set — description/school/type/etc. remain
    null. Admin can fill them via ability_metadata_overrides.json.

Output: replaces ability_metadata.json in place.
"""
import json, re, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

TSV = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Beelzebub\Beelzebub\Resources\prefab_names.tsv'
METADATA = r'C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\Beelzebub Lord of Gluttony\Beelzebub\Beelzebub\Resources\ability_metadata.json'

PREFIXES = ('AB_', 'CHAR_', 'Buff_', 'Item_', 'TM_', 'SpellMod_')
SUFFIXES = ('_AbilityGroup', '_Group', '_Cast', '_Throw', '_VBlood')

def humanize(prefab_name: str) -> str:
    s = prefab_name
    for p in PREFIXES:
        if s.startswith(p):
            s = s[len(p):]
            break
    for sfx in SUFFIXES:
        if s.endswith(sfx):
            s = s[:-len(sfx)]
            break
    s = s.replace('_', ' ')
    # Insert space before each uppercase that follows a lowercase or digit.
    out = []
    for i, c in enumerate(s):
        if i > 0 and c.isupper() and (s[i-1].islower() or s[i-1].isdigit()):
            out.append(' ')
        out.append(c)
    return re.sub(r'\s+', ' ', ''.join(out)).strip()

# Load existing metadata.
with open(METADATA, 'r', encoding='utf-8') as f:
    data = json.load(f)
abilities = data['abilities']
print(f'Existing metadata: {len(abilities)} entries')

# Load local prefab dump and find AbilityGroup/Group prefabs.
local = {}
with open(TSV, 'r', encoding='utf-8') as f:
    for line in f:
        parts = line.rstrip('\n').split('\t')
        if len(parts) != 2:
            continue
        guid_s, name = parts
        if not name.startswith('AB_'):
            continue
        if not (name.endswith('_AbilityGroup') or name.endswith('_Group')):
            continue
        try:
            guid = int(guid_s)
        except ValueError:
            continue
        local[guid] = name

print(f'Local AbilityGroup/Group prefabs: {len(local)}')

# Synthesize stubs for missing entries.
added = 0
for guid, prefab_name in local.items():
    key = str(guid)
    if key in abilities:
        continue
    abilities[key] = {
        'name': humanize(prefab_name),
        # No description / school / type / sourceNpcs — pure stub.
        # ECS-derived fields (cooldown, cast time, range) will populate at runtime.
    }
    added += 1

print(f'Added {added} stub entries.')
print(f'Final metadata: {len(abilities)} entries.')

# Save back.
data['abilities'] = abilities
with open(METADATA, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
print(f'Wrote {METADATA}.')

import os
print(f'File size: {os.path.getsize(METADATA):,} bytes')
