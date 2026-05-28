# Beelzebub — Ability metadata audit (2026-05-27)

Audit of the ability data the mod surfaces, prompted by "many abilities show as *Other* or lack
good info." Measured against the shipped data (`Resources/prefab_names.tsv`,
`Resources/ability_metadata.json`). Reproduce with the script in the appendix.

## 1. How many abilities

| Set | Count |
|---|---|
| `AB_*` prefabs total (groups + casts + hits + buffs + projectiles…) | 8,262 |
| `AB_*_AbilityGroup` (the castable groups Beelzebub classifies/lists) | **1,476** |
| Entries in the shipped `ability_metadata.json` | 1,813 |

The **AbilityGroup** is the unit of capture/grant/classification; the other `AB_*` prefabs are
its internal cast/hit/projectile pieces.

## 2. Category coverage — the "Other" problem (FIXED in v0.51.0)

`Categorization.ClassifyAbility` assigns each ability a `cat=` badge from its name. The old
heuristic required strict `_Token_` boundaries, so most ability names — which are the *move*
(`CrossWindSlash`, `LoomingMists`, `MountainRumbler`), not a tagged token — fell through to `Other`.

| Heuristic | Abilities in `Other` | % of 1,476 |
|---|---|---|
| Old (≤ v0.50) | 1,297 | **87 %** |
| New (v0.51.0) | 581 | **39 %** |

**716 abilities** moved out of `Other`. New distribution:

| Category | Count |
|---|---|
| Other | 581 |
| Melee | 218 |
| Spell | 211 |
| Travel | 143 |
| Projectile | 134 |
| Buff | 89 |
| Aoe | 60 |
| Summon | 31 |
| WeaponSpell | 9 |

The remaining ~39% `Other` are genuinely ambiguous names (proper-noun move names with no
mechanical tell). Admins can pin any of them with a per-ability `"Category"` override in
`ability_rules.json` (see `ABILITY_CONFIG.md`). Pushing the heuristic harder risks
mis-bucketing, so explicit override is the right tool for the long tail.

## 3. Description / school / type coverage — the remaining gap

| Field | Entries with it | % of 1,813 |
|---|---|---|
| `name` | 1,813 | 100 % |
| `description` | 238 | **13 %** |
| `school` | 75 | 4 % |
| `type` | 56 | 3 % |

**Where descriptions come from:** the V Rising **prefab data contains no human-readable text** —
no description, school, or display name, only ECS/gameplay fields (cooldown, cast time, range,
behavior type). The 238 existing descriptions carry `%param%` placeholders that match the game's
in-game tooltips exactly, i.e. they were extracted from **V Rising's localization string table**.

**Why we can't just scrape a website:** `vrising.gaming.tools` was previously evaluated and is a
dead end for this — it exposes the same GUID/name data we already have, with **no descriptions,
stats, or appearance** (see the `reference_vrising_gaming_tools_data` note). So the only real
source for more descriptions is the game's own localization, the same origin as the existing 238.

**Recommended follow-up (data work, not code):** extend `ability_metadata.json` by extracting the
remaining `AB_*_AbilityGroup` tooltip strings + `%param%` values from V Rising's localization data
(the proven pipeline behind the current 238) and merging them in batches. This is incremental and
independent of the mod's code — the resolver (`AbilityMetadataService`) already prefers shipped +
admin-override data and only falls back to a humanized prefab name, so coverage improves the
moment entries are added. Runtime fields (cooldown / cast time / range) are already read live from
the prefab, so those need no curation.

## 4. Status

- ✅ **Categorization** broadened (v0.51.0) — `Other` 87 % → 39 %; admin `Category` override added.
- ✅ **Runtime fields** (cooldown/cast-time/range/behavior) resolved live from prefab data.
- ⏳ **Descriptions/school/type** — 13 %/4 %/3 %. Needs the localization-extraction data pass above;
  not blocked by code.

---

## Appendix — reproduce the numbers

Run from `Beelzebub/Beelzebub/Resources`:

```python
import re, json
from collections import Counter
names=[l.split('\t')[1].strip() for l in open('prefab_names.tsv',encoding='utf-8') if '\t' in l]
ag=[n for n in names if re.match(r'^AB_.*_AbilityGroup$', n)]
print("AbilityGroups:", len(ag))
d=json.load(open('ability_metadata.json',encoding='utf-8'))['abilities']
print("metadata entries:", len(d),
      "with desc:", sum(1 for v in d.values() if v.get('description')))
# Category counts: mirror Categorization.ClassifyAbility (see Services/Categorization.cs).
```
