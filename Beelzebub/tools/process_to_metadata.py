"""Post-process raw_abilities.json into ability_metadata.json (Beelzebub schema).

Input (raw): dict keyed by slug, values are gaming.tools embedded JSON dumps.
Output: ability_metadata.json keyed by PrefabGUID string with our trimmed schema.

Schema (matches AbilityMetadataEntry / AbilityMetadataFile in C#):
{
  "version": 1,
  "abilities": {
    "<prefabGuid>": {
      "name": "...",
      "description": "...",
      "school": "...",
      "type": "...",
      "categories": ["..."],
      "icon": "...",
      "sourceNpcs": [{"guid": int, "name": "..."}],
      "parameters": {"key": "value"}
    }
  }
}
"""
import json
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

RAW_FILE = 'raw_abilities.json'
OUT_FILE = 'ability_metadata.json'

def build_params(raw_params):
    """Convert localizedStringBuilderParameter list → simple dict.

    The list may contain duplicates (gaming.tools returns parameters in repeated
    order matching the description placeholder positions). We keep the FIRST
    value per key — they're consistent across duplicates.
    """
    if not raw_params:
        return None
    out = {}
    for p in raw_params:
        key = p.get('key', '').strip().lower()
        if not key or key in out:
            continue
        val = p.get('value', '')
        fmt = p.get('numericFormat', '')
        # Normalize percentages: gaming.tools encodes "100" with numericFormat
        # "Percent100" to mean "100%". Render as "100%".
        if fmt == 'Percent100':
            val = f'{val}%'
        out[key] = str(val)
    return out or None

def build_npcs(raw_npcs):
    if not raw_npcs:
        return None
    out = []
    for npc in raw_npcs:
        name = npc.get('name') or npc.get('alternateName')
        guid = npc.get('id')
        if not name or guid is None:
            continue
        out.append({'guid': guid, 'name': name})
    return out or None

def process_one(slug, body):
    """Convert one gaming.tools body dict into our ability_metadata entry."""
    guid = body.get('id')
    if guid is None:
        return None, None

    # Skip entities that aren't AbilityGroups (could be Buffs, Casts, etc.).
    if body.get('entityType') != 'AbilityGroup':
        return None, None

    name = body.get('name')

    # Normalize description: gaming.tools embeds %placeholder% tokens. We keep
    # them as-is — the C# side substitutes from `parameters` at render time.
    description = body.get('description')
    if description:
        description = description.replace('\\n', '\n').strip()

    school = body.get('abilitySchool')
    atype = body.get('abilityType')
    cats = body.get('abilityCategories') or None
    icon = body.get('iconPath')
    # Normalize icon path — strip the {height} placeholder + the /images/ prefix.
    if icon:
        icon = icon.replace('/images/{height}/', '').replace('.webp', '')

    params = body.get('prefab', {}).get('localizedStringBuilderParameter')

    entry = {
        'name': name,
        'description': description,
        'school': school,
        'type': atype,
        'categories': cats,
        'icon': icon,
        'sourceNpcs': build_npcs(body.get('npcs')),
        'parameters': build_params(params),
    }
    # Drop null fields to keep the JSON compact.
    entry = {k: v for k, v in entry.items() if v is not None and v != []}
    return guid, entry

def main():
    with open(RAW_FILE, 'r', encoding='utf-8') as f:
        raw = json.load(f)
    print(f'Loaded {len(raw)} raw entries')

    metadata = {}
    skipped = 0
    no_desc = 0
    by_school = {}
    for slug, body in raw.items():
        guid, entry = process_one(slug, body)
        if guid is None:
            skipped += 1
            continue
        # Key must be string for JSON.
        metadata[str(guid)] = entry
        if 'description' not in entry:
            no_desc += 1
        school = entry.get('school', '(none)')
        by_school[school] = by_school.get(school, 0) + 1

    out = {'version': 1, 'abilities': metadata}
    with open(OUT_FILE, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    print(f'Wrote {len(metadata)} ability entries to {OUT_FILE} (skipped {skipped} non-AbilityGroup).')
    print(f'Entries WITHOUT description text: {no_desc}/{len(metadata)}')
    print('By school:')
    for s, c in sorted(by_school.items(), key=lambda kv: -kv[1])[:20]:
        print(f'  {s}: {c}')

if __name__ == '__main__':
    main()
