# Beelzebub build-time tooling

Scripts that produce data SHIPPED with the mod. None of these are required at
runtime — the game itself never makes HTTP requests.

## `scrape_abilities.py`

One-time scrape of `vrising.gaming.tools/abilities` to produce
`raw_abilities.json` — every ability's full embedded JSON dump. Re-runnable;
skips slugs already present.

```powershell
# Initial download of the abilities index page (needed once):
curl -A "Mozilla/5.0 (...)" -o abilities_index.html https://vrising.gaming.tools/abilities

# Then scrape:
python scrape_abilities.py
```

Output: `raw_abilities.json` (~30-40 MB).

## `process_to_metadata.py`

Reads `raw_abilities.json` and produces `ability_metadata.json` in the
Beelzebub schema (matches `AbilityMetadataFile` / `AbilityMetadataEntry`
in `Services/AbilityMetadataService.cs`).

```powershell
python process_to_metadata.py
```

Output: `ability_metadata.json`. Copy this to `Beelzebub/Resources/`
to update the shipped data. The file is embedded into the DLL via the
`EmbeddedResource` entry in `Beelzebub.csproj`.

## `merge_local_into_metadata.py`

Backfills `ability_metadata.json` with stub entries for every
`AB_*_AbilityGroup` / `AB_*_Group` in `Beelzebub/Resources/prefab_names.tsv`
that wasn't covered by the scrape. Each stub has only a humanized `name`;
ECS-derived fields (cooldown, cast time, range) populate at runtime via
`PrefabCollectionSystem` probe.

Run AFTER `process_to_metadata.py` (it edits `Beelzebub/Resources/ability_
metadata.json` in place):

```powershell
python merge_local_into_metadata.py
```

Why: gaming.tools' scrape covered 1,426 abilities (mostly player-castable
spells + V-Blood boss kits). The local prefab dump has 1,751 AbilityGroups
total — the gap (~387 entries) is NPC-only abilities (boss specials,
emote-aggro variants, weapon coatings, etc.) that gaming.tools doesn't
catalogue. The merge ensures every ability the engine knows about has at
least a name + ECS stats in the lookup.

## When to re-run

- V Rising patch changes ability data (new bosses, balance tweaks)
- gaming.tools updates their database with previously-missing descriptions
- We discover the schema needs additional fields

The mod loads the embedded JSON at startup. Admins can override per-ability
on their own server via `BepInEx/config/kdpen.Beelzebub/ability_metadata_overrides.json`.
