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

## `classify_conditions.py` + `merge_conditions_into_metadata.py` (v0.107.0)

Deterministic ACTIVATION-CONDITION classifier. `classify_conditions.py` parses the
prefab dump (`Reference Data/Prefabs`), follows each ability's spawn chain, and
classifies HOW a player makes it work — `Aimed` / `CloseRange` / `Summon` /
`SelfCast` / `Movement` / `Unclassified` (+ `Combo`/`Charged`/`Channel` modifiers) —
from exact component fingerprints. Validated against in-game-confirmed cases; emits
`Unclassified` rather than guess (avoids false positives). Distinct from the
`incompatible` flag: a condition means the ability WORKS but only under that condition,
and it NEVER disables anything.

```powershell
python classify_conditions.py                  # -> ability_conditions.json + ABILITY_CONDITIONS_AUDIT.md (unit-grouped)
python merge_conditions_into_metadata.py       # merges into Beelzebub/Resources/ability_metadata.json
```

`merge_*` writes `condition`/`conditionDescriptor`/`conditionModifiers`/`conditionSource="auto"`
onto each matching entry; leaves `Unclassified` unset; preserves any entry already
marked `conditionSource="confirmed"`/`"admin"` (human-verified wins). Re-run after a
V Rising patch changes ability data. Surfaced in `.beelz info` + `catalog-ability`
(ApiVersion 24); `auto` = unconfirmed candidate until verified in-game.

## `export_review_csv.py` + `lint_ability_data.py` (v0.111.0)

The maintainer curation surface. Both are **read-only** and join the two shipped
ability-data files (`Resources/ability_metadata.json` descriptive + keyed by GUID,
`Resources/ability_rules.default.json` policy + keyed by prefab name) via
`Resources/prefab_names.tsv`.

```powershell
python export_review_csv.py      # -> ability_review.csv (the bulk-review spreadsheet)
python lint_ability_data.py      # validate before packaging; exit 1 on any ERROR
```

- **`export_review_csv.py`** — one flat CSV (UTF-8-BOM, Excel-ready) with 16 columns:
  the descriptive fields, the policy fields, the `reviewStatus`, and a *computed*
  `usability` rollup (`Working/Conditional/Broken/Combo/Junk`, derived not stored).
  Edits still happen in the JSON; re-run to refresh the sheet.
- **`lint_ability_data.py`** — preflight validation. ERRORs (orphan rule, invalid
  weapon/form/`reviewStatus`/category/difficulty token, bad phase, duplicate AbilityMap
  key) exit non-zero; WARNINGs (a metadata-`incompatible` ability left `Enabled`, a
  dangling condition) are advisory. Its token sets MIRROR the C# enums in
  `Services/AbilityRules.cs` / `WeaponFamily` / `ShapeshiftForm` — keep them in sync.

## Triage audits — B1/B2/B3 (v0.111.0, prep-roadmap)

Read-only deterministic passes over the prefab dump + metadata, sharing `audit_common.py`
(prefab index / section parse / spawn-chain BFS). Each emits a json + an `AUDIT_*.md` report;
none auto-applies. See `docs/ABILITY_PREP_ROADMAP.md` for intent.

```powershell
python audit_collectibility.py   # B1: junk vs collectible   -> AUDIT_COLLECTIBILITY.md
python audit_incompatible.py     # B2: broken re-audit        -> AUDIT_INCOMPATIBLE.md
python audit_combos.py           # B3: multi-cast combos      -> AUDIT_COMBOS.md
python apply_reviewstatus.py     # turn B1 into rules edits (DRY-RUN by default; --write to commit)
```

- **`audit_collectibility.py` (B1)** — partitions all ~1,813 into collectible vs junk with a
  reason + confidence. Underscore-BOUNDED name tokens (never substring — `_Spawn_` not `Spawn`,
  which would catch SpawnAdds) + a SUMMON-GUARD (an ability that spawns a CHAR_ unit is never
  junk). Proposes `Hidden` only for HIGH-confidence definitional junk (emote/feed/idle/flee);
  medium classes (`_Hard_` variants, basic attacks, reactions) are left for manual review. A
  structural "no-payload" heuristic was evaluated and DROPPED (false-positived real abilities).
- **`audit_incompatible.py` (B2)** — consolidates the confirmed `incompatible` set and surfaces
  same-class verify-candidates. Documents the finding that broken-for-a-player isn't statically
  detectable (every ability is castable) — broad detection runs through tester reports, not here.
- **`audit_combos.py` (B3)** — multi-cast groups (AbilityGroupStartAbilitiesBuffer > 1), the
  precise chaining signal for the future `chainGroup`/`comboParent` field.
- **`apply_reviewstatus.py`** — writes the B1 result into `ability_rules.default.json`
  (`ReviewStatus`, optional `Enabled:false`, optional class `DenyPatterns`). DRY-RUN unless
  `--write`. Run the lint + rebuild after writing.

## Refinement audits — B5/B6/B7 (v0.113.0, prep-roadmap)

```powershell
python audit_source_tier.py     # B5: source unit + difficulty tier  -> AUDIT_SOURCE_TIER.md
python audit_weapon_family.py   # B6: weapon-family review            -> AUDIT_WEAPON_FAMILY.md
python audit_variants.py        # B7: duplicate/variant pairs         -> AUDIT_VARIANTS.md
```

- **`audit_source_tier.py` (B5)** — resolves each ability's primary source NPC → that unit's
  `UnitLevel` + VBlood flag → a level-derived tier band (T1<30 / T2 30-46 / T3 47-63 / T4 64+).
  Reports the source-coverage gap (abilities with no `sourceNpcs`). Then
  **`merge_tier_into_metadata.py`** bakes `sourceLevel`/`sourceTier`/`isVBlood` into
  `ability_metadata.json` (emitted on the API as `source_level`/`source_tier`/`is_vblood`, ApiVersion 26).
- **`audit_weapon_family.py` (B6)** — replicates `WeaponFamilyClassifier._heuristics` EXACTLY and
  flags SUSPECT concrete-weapon gates (a weapon token matched as an embedded substring). **Keep its
  `_HEURISTICS`/`CONCRETE` in sync with the C# table** — it's what caught the Pollaxe/TwinBlades/
  Slashers mis-gate (those weapons were missing from the classifier; now added).
- **`audit_variants.py` (B7)** — finds base↔variant pairs (`_Hard_`, `_GateBoss_`, `_Minion_`,
  numbered) where the de-tokenised name matches another real group. Recommends collapse/keep/hide.

## Dashboard + condition review — B4/B8/C3/C4 (v0.114.0)

```powershell
python dashboard.py                # B8+C3+C4: coverage, progress, shippable-set -> ABILITY_DASHBOARD.md
python export_condition_review.py  # B4: tester checklist of unconfirmed conditions -> CONDITION_REVIEW_CHECKLIST.md
```

- **`dashboard.py`** — the "where do we stand" report: descriptive-coverage gaps (B8), curation progress
  (reviewStatus / reviewTag), condition-confirmation count, and the **shippable-set** size + rule (C4 =
  `Enabled` ∧ `ReviewStatus` ∉ {Hidden, Blocked} ∧ ¬`incompatible` ∧ ¬deny-patterned). Read-only.
- **`export_condition_review.py`** — turns the 1,146 unconfirmed `auto` conditions into a tester
  checklist grouped by source unit. Confirm in-game with `.beelz admin ability <id> condition <value>`
  (writes `conditionSource=confirmed` to the server override file); fold confirmed overrides back into
  `ability_metadata.json` before packaging.
