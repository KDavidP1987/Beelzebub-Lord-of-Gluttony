# Developer changelog

Full implementation-detail history. Phase numbering, schema versions, internal architecture notes, and rationale for design choices live here.

The **player-facing changelog** that ships in the Thunderstore zip lives at [`Beelzebub/Beelzebub/CHANGELOG.md`](Beelzebub/Beelzebub/CHANGELOG.md) — it's trimmed to user-visible changes and stays well under Thunderstore's ~32 KB cap.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.24.5] - 2026-05-23

### Slot semantics CORRECTED + admin SlotTemplate field

User testing 2026-05-23 exposed wrong slot indices in v0.24.4's
`ReorderForPlayerSlots`. Verified actual V Rising keybind layout:

| ReplaceAbilityOnSlotBuff.Slot | Key | Conventional |
|---|---|---|
| 1 | Q | Primary weapon attack |
| 2 | Space | Travel / teleport |
| 3 | Shift | Usually empty (BCH override-target) |
| 4 | E | Secondary weapon attack |
| 5 | R | Spell slot 1 |
| 6 | C | Spell slot 2 |

Slots 0, 7, 8 rejected by V Rising — confirmed by user trying them via
`.beelz grant`. The vanilla "Ultimate" (R-key shard ability) appears to
use a separate slot mechanism not addressable via `ReplaceAbilityOnSlot
Buff` — research deferred to a future release.

### Heuristic re-mapped to correct indices

`ReorderForPlayerSlots` rewritten:
- Primary detection → idx 0 (was correct)
- Travel detection → **idx 1** (was idx 3 — wrong)
- Heavy/secondary attack detection (NEW category) → **idx 3**: `_Charge_`,
  `_Smash_`, `_Slam_`, `_Heavy_`, `_Cleave_`, `_Strike_`
- Ultimate (Hard) detection → **idx 5** (was correct, but now lands on
  slot 6 = C, not slot 6 = my-mental-model-of-ultimate)
- Spell fill → idx 2 (Shift) and idx 4 (R), in original boss-buffer order

### Admin SlotTemplate field

New optional field on `TransformMap` entries:

```csharp
public Dictionary<string, string> SlotTemplate { get; set; } = null;
```

Schema: keys are slot numbers "1"-"6" (string-typed because JSON), values
are ability-group prefab names. Looked up by name match (case-insensitive)
against the unit's filtered ability list.

`ReorderForPlayerSlots` now takes an optional `unitPrefabGuid` parameter.
When non-zero, it consults `AbilityRules.GetTransformSlotTemplate(unit
PrefabGuid)`; if a template exists, those slot bindings win over the
heuristic. Unspecified slots fall through to the heuristic for the
remaining abilities.

Partial templates work — admins can pin specific slots (e.g., "always
put Mistwalk on Space") and let the heuristic fill the rest.

### Display labels updated

`TransformService.BroadcastTransformLoadout` and `TransformCommands.
EmitSlotLine` updated their slot label arrays:

OLD: `{ "Primary", "Mounted", "Q-Weapon", "Travel", "Ult-Q", "Spell 1",
"Spell 2", "Ultimate" }`

NEW: `{ "Primary (Q)", "Travel (Space)", "Shift", "Heavy (E)",
"Spell 1 (R)", "Spell 2 (C)" }`

Indices are 1-indexed in display, 0-indexed internally as before.

### Files

- `Beelzebub/Beelzebub/Services/AbilityRules.cs` — `SlotTemplate` field
  on `TransformEntry` + `GetTransformSlotTemplate` helper.
- `Beelzebub/Beelzebub/Services/TransformService.cs` — `ReorderForPlayer
  Slots` rewritten with correct slot indices + admin-template precedence;
  new `IsHeavyAttack` classifier. `BroadcastTransformLoadout` slot label
  array.
- `Beelzebub/Beelzebub/Commands/TransformCommands.cs` — `EmitSlotLine`
  label array.

### Backwards compatibility

Existing `ability_rules.json` files don't have `SlotTemplate` — that
field defaults to null. Behavior for existing servers: pure heuristic
classification with the corrected indices. No migration required.

### Ultimate slot research notes (for next iteration)

V Rising's "Ultimate" slot (slot 7 in some references, slot 0 in others
— V Rising's IL2CPP shows it as a separate "UltimateData" component on
the player entity, not a generic spell slot). Likely needs a different
component setup than `ReplaceAbilityOnSlotBuff`. Investigation queued
for v0.25.0+.

---

## [0.24.4] - 2026-05-23

### Slot semantics + dual-hook chain coverage

v0.24.3 confirmed the engine-hook approach works (Shift-key Projectile
Nova fires correctly). Two follow-up issues:

**1. Boss ability order doesn't match player UI slot semantics.**
`GetTransformAbilities` was returning abilities in the boss's
`AbilityGroupSlotBuffer` natural order. `TransformBuffService.Apply`
applied them 1:1 to player slots 1..6. Result: Priest's
`Teleport_Travel` (a dash) landed on slot 2 (Mounted) when it should be
slot 4 (Shift). `ProjectileNova_Hard` (the ultimate) landed on slot 6
(Spell 2) — close to ultimate but not labeled as such.

**2. Mistwalk's destination AOE didn't fire** even after v0.24.3's
engine hook. The Travel chain: `Cast` → `Phase` (travel buff) →
`Travel_End` (destination buff, freezes + damages). Travel_End IS a
buff that ScriptSpawnServer should catch. Possibly the timing of the
hook misses it, or the AOE damage entity is spawned downstream from
Travel_End via `SpawnPrefabOnDestroy` and isn't itself a buff.

### Fix #1: `TransformService.ReorderForPlayerSlots`

New static helper, called at the tail of `GetTransformAbilities`.
Classifies each ability by name-pattern substring and places it in the
matching player slot index:

| Player slot | Index | Heuristic |
|---|---|---|
| Primary | 0 | `_Projectile_Group`, `_MeleeAttack_`, `_Auto_`, plain `_Projectile_` (not `_Nova_`/`_Hard_`/`_Charge_`) |
| Mounted / Spell 1 | 1 | filled from leftover spells |
| Q-Weapon | 2 | filled from leftover spells |
| Travel (Shift) | 3 | `_Teleport_`, `_Travel_`, `_Dash_`, `_Leap_`, `_PhaseShift_` |
| Spell | 4 | filled from leftover spells |
| Ultimate-equivalent | 5 | `_Hard_`, `_Ultimate_` |

Classified categories (Primary/Travel/Ultimate) win their slots first;
remaining "spell" abilities fill the open slots 1, 2, 4 in original
order. If the boss has more than 6 valid abilities (rare), the extras
overflow into the next available slot until the list is full.

Patterns are tunable. Heuristics may misclassify rare abilities; if
that happens the user can override by editing the result in code or
adding admin curation in a future release.

### Fix #2: `BuffSpawnServerPatch` generic chain fixup

Extended the existing patch with the same chain-ownership logic as
`ScriptSpawnServerPatch`. Two-hook coverage:

* `ScriptSpawnServer.OnUpdate` Prefix (v0.24.3) — catches buff entities
  at one stage of the spawn pipeline.
* `BuffSystem_Spawn_Server.OnUpdate` Prefix (v0.24.4, existing patch
  extended) — catches buff entities at a DIFFERENT stage of the spawn
  pipeline. Same fixup logic; together they should cover more chain
  depths.

Both hooks skip entities tagged `BlockFeedBuff` or `Minion` (the
existing summon-as-ally pipeline owns those). Both walk owner chain up
to 4 hops looking for a Beelzebub-transformed player. Both rewrite
`EntityOwner`, `Team`, `TeamReference`, `FactionReference`.

### Files

- `Beelzebub/Beelzebub/Services/TransformService.cs` —
  `ReorderForPlayerSlots`, `IsPrimaryAttack`, `IsTravelAbility`,
  `IsUltimate` static helpers. `GetTransformAbilities` now returns
  reordered list.
- `Beelzebub/Beelzebub/Patches/BuffSpawnServerPatch.cs` — generic
  chain-fixup loop inside the existing OnUpdatePrefix; `ResolveOwning
  Player` and `FixupBuffOwnership` helpers added.

### Patch count

14 Harmony patches active (unchanged — both BuffSpawnServer fixup and
the existing waygate detection live in the same patch class).

### What this still doesn't address

If the Mistwalk AOE entity that spawns from Travel_End's
`SpawnPrefabOnDestroy` is a *non-buff* (e.g., a projectile fan
spawner), neither ScriptSpawnServer nor BuffSpawnServer will catch it.
That would need a third hook on `SpawnPrefabOnDestroySystem` or similar.
v0.24.4 ships the dual-buff coverage; if testing shows the chain still
breaks, the next release adds the non-buff spawn hook.

---

## [0.24.3] - 2026-05-23

### Engine hook attempt for AOE chain ownership

User pushback on v0.24.2's "Incompatible" warning approach: "I don't want
to warn that an ability is not possible. Bloodcraft familiars can use
these abilities — the player should be able to as well." Fair point —
the technique exists, we just hadn't wired it up.

### Approach (Bloodcraft pattern)

Bloodcraft's `FamiliarBindingSystem.cs:413-415` sets up familiar combat
team:
```csharp
familiar.With((ref Team team) => team.Value = 2);
familiar.With((ref TeamReference teamReference) => teamReference.Value._Value = _unitTeamSingleton);
```

This makes the familiar's spawned children (proxy spawners, projectiles)
inherit player-compatible team. We can't do this to the player (the
player has their own clan team), but we CAN do it to each spawned chain
entity at the moment it's created.

### Patch: `ScriptSpawnServerPatch`

Hooks `ScriptSpawnServer.OnUpdate` Prefix — same system Bloodcraft uses
for stat injection. For every spawned entity with `EntityOwner`:
1. Walk owner chain (up to 4 hops) looking for a player root
2. Skip if not Beelzebub-transformed
3. Skip if entity already has `BlockFeedBuff` or `Minion` (handled by
   the v0.23.x summon-as-ally / LinkMinion pipeline)
4. Otherwise rewrite:
   - `EntityOwner.Owner = playerCharacter` (direct, no intermediate)
   - `Team.Value` ← copy from player
   - `TeamReference.Value._Value` ← copy from player
   - `FactionReference.FactionGuid._Value = Faction_Players (1106458752)`

Verbose log line `[Beelz CHAIN] fixup ... team=X faction=Y owner=Z` on
each rewrite for diagnostic visibility.

### Curation refinement

`tools/refine_incompatible_v2.py` updates the shipped `ability_metadata.json`:
- **Removed `incompatible` tag** from AOE-Proxy and OwnerChain entries
  (Priest ProjectileNova basic + Hard, Bishop TrippleBolt) — the engine
  hook should handle these
- **Kept `incompatible` tag** for AnimRig entries (Bishop EternalDarkness,
  ShadowStep, all `_Teleport_Travel_` variants) — these are bound to NPC
  skeleton bones; the player skeleton doesn't have them. No server-side
  fix can re-rig the client's animation system.

Net: 5 abilities still tagged incompatible (down from 8).

### Risks acknowledged

- **`ScriptSpawnServer` may not catch all proxy/projectile spawns.** The
  system processes entities through a centralized query; the exact
  archetype filter depends on V Rising's internals. Some chain entities
  may bypass this system entirely (e.g., transient projectile spawns from
  a different system). If the hook doesn't catch ProxySpawner, we'll need
  additional hooks (`SpawnPrefabOnGameplayEventSystem` is the audit-doc
  candidate).
- **Rewriting Team on chain entities may have side effects** — V Rising's
  collision and aggro systems read these. We're matching the player's
  team values exactly, so the side effects should be minimal — but
  test for any "my own summons attacking me" type regressions.
- **OwnerChain rewrite (EntityOwner.Owner = playerCharacter direct) could
  break some attribution mechanics** where intermediate owners are
  expected. Mitigation: only applied to entities NOT marked as Minion
  or BlockFeedBuff (we let the existing summon pipeline own those).

### Files

- `Beelzebub/Beelzebub/Patches/ScriptSpawnServerPatch.cs` — NEW patch.
- `Beelzebub/Beelzebub/Resources/ability_metadata.json` — refined to
  untag 3 entries the hook attempts to fix.
- `Beelzebub/tools/refine_incompatible_v2.py` — reproducible refinement
  script.

### Patch count

14 Harmony patches active (was 13 in v0.24.2).

### What's NEXT if this works / doesn't work

If the hook works: queue similar work for non-AOE chains (single
projectiles, dash phases without animation binding) and broader test
coverage.

If the hook doesn't catch all entities: add additional spawn-system
hooks (`SpawnPrefabOnGameplayEventSystem`, `CreateGameplayEventOnTickSystem
_Spawn`, `LinkMinionToOwnerOnSpawnSystem` extended to non-Minion archetypes).

If it causes side effects: tighten the entity filter (require specific
prefab name patterns, or require explicit opt-in via `ability_metadata.json`
flag like `engineHookFix: true`).

---

## [0.24.2] - 2026-05-23

### #93 (Priest RaiseDead) + Incompatible-ability schema/tagging

User report: Priest has "a smaller, stronger summon variant that doesn't
fire" (#93) and "two AOE abilities not functioning properly." Deep-dive
on the Priest's prefab chain in `Reference Data/Prefabs/`:

**Priest VBlood ability slot loadout** (from `CHAR_Undead_Priest_VBlood`'s
`AbilityGroupSlotBuffer`):

| Slot | Ability | Failure class | v0.24.2 state |
|---|---|---|---|
| 0 | Projectile (basic + Hard) | None known | Works |
| 1 | Teleport_Travel | AnimRig (class 2) | Tagged Incompatible |
| 2 | ProjectileNova | AOE-Proxy (class 3+4) | Tagged Incompatible |
| 3 | RaiseDead (the #93) | Corpse-Required | **FIXED via SummonTargets** |
| 4 | RaiseHorde | None | Works (v0.23.x summon-as-ally) |
| 7 | ProjectileNova_Hard | AOE-Proxy | Tagged Incompatible |
| 8 | Projectile_Hard | None | Works |

### #93 fix — RaiseDead via manual spawn

V Rising's RaiseDead chain works by:
1. Cast applies `ChannelBuff` to caster
2. ChannelBuff fires events that spawn `Summon_Melee` + `Summon_Ranged`
   *HitTrigger carrier* entities at the caster's position
3. The carriers detect existing skeleton corpses in range and apply
   `RaiseDead_Minion_Buff` to them, animating them

For a transformed player, step 3 fails almost always: players aren't
typically standing on graveyards, so no corpses to animate.

Fix: added `AB_Undead_Priest_RaiseDead_AbilityGroup` (both Elite and
non-Elite, both `_AbilityGroup` and `_Group` suffixes) to `SummonTargets`
with target `CHAR_Undead_SkeletonSoldier_Base` × 2. Same pattern as
Bishop's ShadowSoldier (which has worked since v0.22.0) — manual
`InstantiateEntityImmediate` at the caster's position bypasses the
broken corpse-required chain.

### Schema addition: `incompatible` + `incompatibleReason`

`AbilityMetadataEntry` and `AbilityInfo` (the merged record returned by
`AbilityMetadataService.Resolve`) now carry two new fields:

```csharp
public bool Incompatible { get; set; }
public string IncompatibleReason { get; set; }
```

Override-and-shipped priority follows the existing pattern. `Resolve`
layers them through.

### Initial curation: 8 abilities tagged

Tagged via `tools/add_incompatible_flags.py`:

- **3 hard-mapped GUIDs**: Priest ProjectileNova (basic + Hard), Priest
  Teleport_Travel
- **5 pattern-matched** (substring on prefab name):
  - `_EternalDarkness_` (Bishop)
  - `_ShadowStep_` (Bishop)
  - `_Teleport_Travel_` (catch-all)
  - `_TrippleBolt_` (Bishop)

Reasons reference the chain-failure classes catalogued in
`project_ability_chain_audit.md`:

- **AnimRig** — TravelBuff + HideWeapon bound to NPC skeleton bones
- **AOE-Proxy** — ProxySpawner pattern with `Team = 1` (NPCs only)
- **OwnerChain** — multi-step chain loses owner mid-spawn
- **Offset / TeamFilter** — for catalog completeness, not currently tagged
- **Corpse-Required** — natural-chain animates existing corpses; no
  player corpses available (now FIXED via manual-spawn for Priest)

### UX wiring

The Incompatible flag surfaces in three places:

1. **`.beelz info <name>`** — header includes `[⚠ INCOMPATIBLE]` tag;
   dedicated warning line beneath with reason.
2. **`.beelz active`** — per-slot lines append `⚠` for any incompatible
   ability.
3. **Auto-chat on transform** — same `⚠` per slot, plus a "may misbehave
   when you cast it — {reason}" follow-up line.

Admin can override per-server via `ability_metadata_overrides.json` —
e.g., set an ability to `incompatible: false` to suppress the warning
on a server where they've patched the underlying issue.

### `.beelz admin scan-abilities` extension

The audit report now includes `incompatible` and `incompatibleReason`
fields per entry. Top-level chat reply also shows the incompatible
count, e.g., `Scanned 1813 abilities: 1426 curated, 387 fallback-only,
8 marked incompatible.`

### Files

- `Beelzebub/Beelzebub/Patches/AbilityCastStartedSystemPatch.cs` —
  added 4 RaiseDead entries (Elite + non-Elite × `_AbilityGroup` +
  `_Group`) to `SummonTargets`.
- `Beelzebub/Beelzebub/Services/AbilityMetadataService.cs` —
  `Incompatible` / `IncompatibleReason` fields on both `AbilityMetadataEntry`
  and `AbilityInfo`; `Resolve` layers them through.
- `Beelzebub/Beelzebub/Resources/ability_metadata.json` — 8 entries
  tagged with the new fields.
- `Beelzebub/tools/add_incompatible_flags.py` — reproducible curation
  script; admins can fork to flag more.
- `Beelzebub/Beelzebub/Commands/BeelzCommands.cs` — `.beelz info`
  shows warning.
- `Beelzebub/Beelzebub/Commands/TransformCommands.cs` —
  `EmitSlotLine` appends `⚠`.
- `Beelzebub/Beelzebub/Services/TransformService.cs` —
  `BroadcastTransformLoadout` emits per-slot warning with reason.
- `Beelzebub/Beelzebub/Commands/AdminCommands.cs` —
  `.beelz admin scan-abilities` includes new fields + count.

### Next planned (v0.24.3 or v0.25.0)

The engine-level Harmony hook on `SpawnPrefabOnGameplayEventSystem`
(or similar) that walks owner chains for newly-spawned proxy/projectile
entities and applies player Faction + Team. Would unfix AOE-Proxy +
OwnerChain classes wholesale. Research/risk-heavy → defer.

---

## [0.24.1] - 2026-05-23

### Surface ability metadata in-game (chat-based)

User feedback on v0.24.0: the 1,813-entry metadata database is built and
loaded, but it's only accessible via `.beelz info <name>` chat command.
The action bar tooltip (the natural place to read ability text) still
shows "No Name" for V-Blood and NPC abilities.

### Architectural limitation acknowledged

V Rising's action bar tooltip is rendered entirely client-side:
1. Server sends the slotted ability's PrefabGUID
2. Client looks up the prefab locally
3. Client reads `ManagedAbilityGroupData.name` UUID (a localization key)
4. Client renders text from its StreamingAssets localization tables

For V-Blood / NPC abilities, the localization UUID is typically empty or
points to a key Stunlock never shipped client-side text for. The server
has no way to inject text into a client-side tooltip render.

The actual fix requires a client-side mod (BloodCraftHub integration is
the long-term plan per `CLAUDE.md`). Until then, v0.24.1 surfaces the
data via chat channels.

### Two chat-based access paths

**1. Auto-chat on transform** (`TransformService.BroadcastTransformLoadout`).
After a successful `.beelz transform`, the mod sends a multi-line chat
summary of every populated slot:

```
--- <Unit Name> spell bar ---
  Primary: <Ability Name> [School] · cd 8.0s
    <description if curated>
  Q-Weapon: ...
  Spell 1: ...
  ...
Use .beelz active to re-display this anytime.
```

Slot labels match V Rising's conventional spell bar positions (Primary /
Mounted / Q-Weapon / Travel / Ult-Q / Spell 1 / Spell 2 / Ultimate).
Falls back to humanized prefab name when no curated entry exists. Wires
through `AbilityMetadataService.Resolve` so it includes both curated
metadata AND runtime ECS-derived fields (cooldown / cast time).

**2. `.beelz active` command** (`TransformCommands.Active`). Player can
re-display the same loadout anytime. Branches on whether the player is
currently transformed:
* Transformed → show the transform's ability loadout via
  `Core.Transforms.GetTransformAbilities(unitGuid, phase)`.
* Not transformed → show the player's universal slot bindings via
  `AbilityRegistry.GetSlots`.

### Files

- `Beelzebub/Beelzebub/Services/TransformService.cs` — `BroadcastTransform
  Loadout` method + invocation after successful transform-apply.
- `Beelzebub/Beelzebub/Commands/TransformCommands.cs` — `.beelz active`
  command + `EmitSlotLine` helper.

### Patch count

13 Harmony patches active (unchanged from v0.24.0).

---

## [0.24.0] - 2026-05-23

### R1 — Ability metadata foundation

Players have asked "what does this ability do?" since v0.2.0. Until now
the only display has been the raw prefab name (e.g.,
`AB_Undead_Priest_Elite_RaiseHorde_AbilityGroup`). v0.24.0 ships a
foundation for ability metadata — title, description, school, type,
source NPCs, cooldown, cast time, range — wired into a new chat command.

### Two-database architecture (clarifying separation)

The mod already had `ability_rules.json` for admin POLICY (Enabled/Disabled,
DamageScale, drop-rate overrides, etc.). v0.24.0 introduces a SECOND
database with different lifecycle + ownership:

| File | Purpose | Lifecycle | Ownership |
|---|---|---|---|
| `ability_rules.json` (existing) | Admin POLICY: enabled, transform-only, weapon-locked, scaling overrides | Changes per-server | Per-server admin |
| `ability_metadata.json` (new, EMBEDDED) | REFERENCE: name, description, school, type, source NPC, icon, parameters | Mostly static (write-once from scrape) | Shipped with mod |
| `ability_metadata_overrides.json` (new, optional) | Admin per-server overrides of metadata fields | When admin tweaks | Per-server admin |

### Data sourcing — scraped + ECS-merged

The shipped `ability_metadata.json` is built from a one-time scrape of
`vrising.gaming.tools/abilities` — the site exposes structured embedded
JSON on each ability detail page. Scrape pulls:
- Localized name
- Localized description with `%placeholder%` tokens (substituted at
  render time from a parameters dict)
- Ability school (Blood / Chaos / Frost / Storm / Unholy / Illusion)
- Ability type (Offensive / Defensive / Movement / etc.)
- Categories
- Source NPCs (which V-Bloods or units the ability comes from)
- Icon path

At runtime, the new `AbilityMetadataService` MERGES this with ECS-derived
data read from the live prefab entities via `PrefabCollectionSystem._PrefabLookupMap`:
- Cooldown (`AbilityCooldownData.Cooldown` on the Cast prefab linked
  from `AbilityGroupStartAbilitiesBuffer`)
- Cast time (`AbilityCastTimeData.MaxCastTime`)
- Min/max range (`AbilityGroupInfo`)
- Behavior type (Channeling / Instant / Charging / etc.)

So shipped data covers "what is this ability conceptually" and ECS covers
"what are the current numeric stats" — admins who change cooldowns via
other mods will see the live values automatically.

### New: `.beelz info <index|name>`

Player command, no admin required:

```
.beelz info 0          ← by your .beelz list index
.beelz info raise hor  ← by name substring (matches "Raise Horde")
```

Returns:
- Header: name + [School / Type] tags
- Description with parameters substituted
- Stats: cooldown · cast time · range · behavior type
- Source NPCs ("Source: Nicholaus the Fallen")
- Admin policy (if drop-rate or damage-scale overrides apply)
- Provenance footer (shipped / override / fallback + ID)

### New: `.beelz admin scan-abilities`

Admin discovery tool. Walks every captured ability across all players +
every AB_*_AbilityGroup in the global prefab map. For each, resolves
metadata + ECS-derived fields and writes a report to
`config/kdpen.Beelzebub/discovered_abilities.json`. Surfaces every
ability lacking a curated description so admins know what to fill in
via `ability_metadata_overrides.json`.

### Files

- `Beelzebub/Beelzebub/Services/AbilityMetadataService.cs` — NEW. Loader
  for embedded `ability_metadata.json` + on-disk overrides + ECS prefab
  probe + search.
- `Beelzebub/Beelzebub/Resources/ability_metadata.json` — NEW shipped data.
  Built from a one-time scrape of vrising.gaming.tools.
- `Beelzebub/Beelzebub/Beelzebub.csproj` — embed the new JSON resource.
- `Beelzebub/Beelzebub/Core.cs` — instantiate + load `AbilityMetadata`
  service during init.
- `Beelzebub/Beelzebub/Commands/BeelzCommands.cs` — `.beelz info` command.
- `Beelzebub/Beelzebub/Commands/AdminCommands.cs` — `.beelz admin
  scan-abilities` command.

### How the scrape was done (reproducibility)

The build-time scrape lives in `tools/scrape_abilities.py` and
`tools/process_to_metadata.py` (added in this release as documentation).
Process:

1. Fetch `https://vrising.gaming.tools/abilities` to enumerate all 1,439
   ability slugs.
2. For each slug, fetch the detail page with English Accept-Language.
3. Extract the embedded `"body":"{...}"` JSON string from the HTML.
4. Decode + parse into per-ability entries.
5. Post-process to our schema: drop the gaming.tools-specific fields,
   normalize icon paths, fold duplicate parameters, gate on
   `entityType == "AbilityGroup"` so we don't include Buffs/Casts.
6. Output to `Resources/ability_metadata.json`.

The mod itself NEVER makes HTTP requests. The scrape happens offline,
the result ships embedded in the DLL.

### Coverage audit (pre-ship)

Before deploy, audited shipped metadata against the local prefab dump
(`Resources/prefab_names.tsv`, 23,534 entries). Findings:

- 1,751 `AB_*_AbilityGroup` / `AB_*_Group` prefabs in the local dump
- 1,426 from gaming.tools scrape
- **387 missing** (mostly NPC-only abilities: boss specials, emote-aggro
  variants, weapon coatings, mounted attacks)
- 62 extras in gaming.tools (likely newer V Rising additions not in our
  prefab dump)

The 387 missing were back-filled with stub entries (humanized name only)
via `tools/merge_local_into_metadata.py`. Final ship: **1,813 entries**
covering every AbilityGroup the engine knows about. Stubs still get full
ECS-derived stats (cooldown, cast time, range) at runtime — only the
description / school / type / source-NPC fields are null pending admin
curation via `ability_metadata_overrides.json`.

### Patch count

13 Harmony patches active (unchanged from v0.23.19).

---

## [0.23.19] - 2026-05-23

### B2 finalize-finalize — full horde re-prime on every combat trigger

v0.23.18's StatChangeSystem hook + offensive DealDamage broadcast were
necessary but not sufficient. User report: "Player attacks → horde
doesn't respond. They only attack when they are attacked. And it's per
group — only the cluster near the attacked unit responds."

### Root cause (Bloodcraft pattern comparison)

Bloodcraft's familiar combat-entry path on PvE combat buff (BuffSpawnServer
Patches.cs:127-136) calls TWO methods, not one:

```csharp
Familiars.HandleFamiliarEnteringCombat(buffTarget, familiar);
Familiars.SyncAggro(buffTarget, familiar);
```

`HandleFamiliarEnteringCombat` itself does THREE things:
1. `Follower.ModeModifiable = 1` (we had this — `SetCombatMode`)
2. `SetPreCombatPosition` — sets `AggroConsumer.PreCombatPosition` to the
   player's current position (**we did NOT have this**)
3. `TryReturnFamiliar` — teleport the familiar to the player if beyond
   leash distance (**we did NOT have this on combat-entry**)

Without #2, the AI's combat-range checks use whatever stale anchor was set
at spawn (often 50+ units from the player after movement) → unit thinks
"I'm too far from my combat zone" and refuses to engage. Without #3, units
that spawned at a different player position remain at that position and
can't reach the current combat.

Without `SyncAggro` at combat-entry, the units have no targets in their
AggroBuffer for the moments before the player's offensive damage event
propagates — so the user sees "no response when I attack."

### Fix — `HandleHordeEnteringCombat`

New service method that fully re-primes every summon for combat:

1. `Follower.ModeModifiable = 1` (combat autonomy)
2. `AggroConsumer.PreCombatPosition = player.Position` (combat anchor)
3. **Leash teleport** — units beyond `Transform_SummonLeashRadius` get
   pulled to the player with radial scatter. This is the missing piece
   for true horde unison: spatial clusters no longer matter because we
   pull everyone in on combat-entry.
4. `SyncAggro` — read player's `InverseAggroBufferElement`, push to each
   summon's `AggroBuffer` (Bloodcraft's pattern).
5. `BehaviourTreeState = Combat` (wake the AI immediately).

**Throttled to once per second per player** via an internal dictionary,
so sustained damage ticks during combat don't trigger expensive re-prime
mutations every frame. The actual aggro injection (`PushTargetToAllSummons`)
is unthrottled — only the full re-prime is.

### Called from ALL combat triggers

| Trigger | Patch | Call |
|---|---|---|
| Player enters combat | `BuffSpawnServerPatch` (PvE combat buff) | `SetCombatMode(true)` + `HandleHordeEnteringCombat` |
| Player damages enemy | `DealDamageSystemPatch.RouteAggro` | `PushTargetToAllSummons` + `HandleHordeEnteringCombat` |
| Summon takes damage | `StatChangeSystemPatch` → `ReactToDamageOnSummon` | `PushTargetToAllSummons` + `HandleHordeEnteringCombat` |

Any combat trigger pulls the WHOLE horde in. No more per-group response.

### Files

- `Beelzebub/Beelzebub/Services/SummonAllyService.cs` — `HandleHordeEnteringCombat`
  method + `_lastHordeReprime` throttle dictionary + `ReprimeThrottle` constant.
  `ReactToDamageOnSummon` now calls `HandleHordeEnteringCombat`.
- `Beelzebub/Beelzebub/Patches/BuffSpawnServerPatch.cs` — PvE combat buff
  branch calls `HandleHordeEnteringCombat`.
- `Beelzebub/Beelzebub/Patches/DealDamageSystemPatch.cs` — `RouteAggro`
  calls `HandleHordeEnteringCombat`.

### Diagnostic

New verbose log line: `[Beelz SUMMON][horde-combat]` — per re-prime
event, reports counts of woken, teleported, and aggro-seeded summons.
If this never appears, no combat trigger is firing. If it appears with
`teleported > 0` consistently, your horde is spawned far from where you're
fighting (consider lowering `Transform_SummonLeashRadius` for tighter
formation).

### Patch count

13 Harmony patches active (unchanged from v0.23.18).

---

## [0.23.18] - 2026-05-23

### B2 finalize — defensive aggro reaction (StatChangeSystem hook)

User reported the v0.23.17 cap fix verified, but combat AI is still
inconsistent — "engages briefly then drops out." Specifically:
* Player initiating combat with a creature: horde did not react
* Creature attacking horde members: brief reaction, then stopped
* Could not maintain sustained combat engagement

### Root cause

Bloodcraft's familiar combat pattern revealed the missing piece:
V Rising's native combat AI **does not auto-inject attackers into the
AggroBuffer of player-allied units**. The engine reserves that auto-
injection for free NPCs. Player-allied units (Follower.Followed pointing
at a player) are assumed to be commanded by the controlling player.

Our existing patches covered:
* **Offensive aggro** (player → enemy) via `DealDamageSystemPatch`
* **Combat-mode toggle** via `BuffSpawnServerPatch` + `UpdateBuffsBuffer
  DestroyPatch` watching PvE combat buff
* **State-change interception** via `BehaviourStateChangedSystemPatch`
  (v0.23.16) forcing Return/Idle → Follow

But we had **no defensive aggro path** (enemy → summon). When the player
wasn't dealing damage but the summons were being attacked, nothing pushed
the attacker into the summon's AggroBuffer. The brief reaction the user
observed was V Rising's one-tick residual aggro from being struck; the
"stopped reacting" was the AggroBuffer going empty immediately after.

### Fix — new patch + horde-aware reaction

**New patch** `StatChangeSystemPatch` — hooks `StatChangeSystem.OnUpdate`
Prefix. For every `DamageTakenEvent`:

1. Check if the damaged entity is a tracked summon
   (`SummonAllyService.IsTrackedSummon`).
2. Resolve the attacker by walking the EntityOwner chain from
   `damageTakenEvent.Source` (mirrors Bloodcraft's `Entity.GetOwner`).
3. Skip self-damage, owner-self damage, and friendly-fire from another
   tracked summon of the same player.
4. Call `SummonAllyService.ReactToDamageOnSummon(summon, attacker, player)`.

**New method** `SummonAllyService.ReactToDamageOnSummon`:

1. Broadcasts the attacker into the **ENTIRE horde's** AggroBuffer via
   `PushTargetToAllSummons` — so one struck skeleton draws all 6 raise-
   horde skeletons onto the attacker. This is the "horde" feel.
2. Forces `Follower.ModeModifiable = 1` on the struck unit (defensive;
   BuffSpawnServerPatch handles the player-side flip on PvE combat buff
   but that won't fire if only the summons are being attacked and the
   player remains untouched).
3. `PushTargetToAllSummons` already flips BehaviourTreeState → Combat
   on each summon as a side effect, so the AI evaluates the fresh
   AggroBuffer this tick rather than waiting for next cycle.

### Combat AI patch stack — summary

The complete picture for player-allied combat AI as of v0.23.18:

| Trigger | Patch | What it does |
|---|---|---|
| Spawn-time | `SummonAllyService.ApplyPlayerAllySetup` | Aggroable + AggroConsumer enable, BehaviourTreeState = Follow, faction/follower/owner setup |
| Player damages enemy | `DealDamageSystemPatch` | Push target to all summons' AggroBuffer + BTS=Combat |
| **Summon takes damage** | **`StatChangeSystemPatch` (NEW)** | **Push attacker to all summons + mode=1** |
| Player enters combat | `BuffSpawnServerPatch` (PvE combat buff applied) | `SetCombatMode(true)` — all summons mode=1 |
| Player exits combat | `UpdateBuffsBufferDestroyPatch` | `SetCombatMode(false)` — all summons mode=0 (leash) |
| Unit tries to give up | `BehaviourStateChangedSystemPatch` (v0.23.16) | Return/Idle → Follow |
| Periodic | `SummonAllyService.SyncAggroAll` (Tick) | Re-seed from player's InverseAggroBuffer |
| Waygate restore | `SummonAllyService.RestoreAll` (v0.23.17) | Full combat-readiness re-prime |

### Diagnostic logging

Added verbose log lines at every aggro entry point so the next test
cycle can verify each path fires independently:

* `[Beelz SUMMON][offensive]` — DealDamageSystemPatch RouteAggro fires
* `[Beelz SUMMON][combat-on]` — BuffSpawnServerPatch sees PvE combat buff
* `[Beelz SUMMON] react-to-damage` — StatChangeSystemPatch fires (NEW)

### Files

- `Beelzebub/Beelzebub/Patches/StatChangeSystemPatch.cs` — NEW.
- `Beelzebub/Beelzebub/Services/SummonAllyService.cs` — `ReactToDamageOnSummon`
  method.
- `Beelzebub/Beelzebub/Patches/DealDamageSystemPatch.cs` — diagnostic log.
- `Beelzebub/Beelzebub/Patches/BuffSpawnServerPatch.cs` — diagnostic log.

### Patch count

13 Harmony patches active (was 12 in v0.23.17).

### Generality

Applies to every player-allied summon (Bishop ShadowSoldiers, Stonebreaker
Reinforcements, Priest RaiseHorde/RaiseDead, future curated additions).
StatChangeSystem fires for every damage event; the filter is purely
"is the damaged entity in any active transform's SummonedMinions list",
not per-ability.

---

## [0.23.17] - 2026-05-22

### Two regressions introduced by v0.23.16 (B1 fix-fix + B2 restore-fix)

User reported v0.23.16 produced two new failure modes during the test
cycle: (1) the 3rd Priest summon cast silently produced no skeletons (no
chat message either), only 2 casts worked; (2) after waygate teleport,
the restored horde no longer engaged enemies even when player took damage.
Both are second-order effects of the v0.23.16 changes.

### B1 — 3rd cast silently destroyed

**Root cause.** v0.23.16's empty-group TTL (which keeps freshly-opened
cast groups alive within the attribution window so the cap counts them)
was queried by BOTH the cast-start cap check AND the LinkMinion over-cap
check. They have different correctness requirements:

* **Cast-start** wants to count empty-within-window groups → so rapid
  re-casts get refused before any spawns land.
* **LinkMinion** wants to count populated groups only → because the
  incoming spawn IS about to populate the empty group, and counting it
  would double-count.

When the 3rd cast was admitted at cast-start (saw 2 populated + opening
the 3rd empty), its spawns arriving at LinkMinion saw `liveCount=3`
(empty 3rd counted), `cap=3` → over-cap path → destroyed its own spawns
silently. From the player's view: 3rd cast produced nothing, no error
message, cast cooldown still consumed.

**Fix.** Split `LiveCastCount` into two methods:

* `LiveCastCount` (existing semantics) — populated + empty-within-window.
  Used by `AbilityCastStartedSystemPatch` cap check.
* `LivePopulatedCastCount` (new) — populated only. Used by
  `LinkMinionToOwnerOnSpawnSystemPatch` over-cap destroy check.

This preserves both fixes: empty groups still count against new casts
at cast-start (so rapid re-casts get refused), but the LinkMinion path
only refuses spawns that match a refused cast (where no group exists
for them at all).

### B2 — Restored horde no longer engages

**Root cause.** `RestoreAll` set `ModeModifiable=1` and re-bound
`Follower.Followed` to the player — but didn't touch the BehaviourTree
state, didn't re-enable Aggroable/AggroConsumer, and didn't re-seed
aggro from the player's current targets. Units stashed at a waygate
were typically in Idle (out of combat). Restored units retained that
Idle state, and our new BehaviourStateChanged patch only intercepts
active state TRANSITIONS — not units already-stuck in Idle. So they
sat passive at the destination.

**Fix.** `RestoreAll` now fully re-primes combat readiness for every
restored entity:

1. `BehaviourTreeState = Follow` (explicit reset, not waiting for
   a transition that never comes).
2. `ModeModifiable = 0` (leash default; v0.23.15 combat-buff hook
   flips to 1 on next combat). Was 1 (immediate combat), which caused
   vortex-follow per pre-v0.23.15 testing.
3. `Aggroable.Value._Value = true` + `AggroConsumer.Active._Value = true`
   re-enabled (some units lose these during the Disabled period).
4. `SyncAggro` invoked to re-seed aggro from the player's current
   `InverseAggroBuffer`. Any combat already in progress at arrival
   immediately populates the restored summons' aggro buffers.

### Test plan

1. Restart server. Verbose logging recommended for first pass.
2. Transform → Undead Priest. Cast RaiseHorde 4 times in succession.
   * Expected: 1st, 2nd, 3rd casts each produce horde. 4th refused
     with "Summon use limit reached (3/3)" chat message.
3. Engage enemies. Confirm all horde members engage (B2 fix verified
   in v0.23.16; this release preserves it).
4. Teleport via waygate to a different region.
5. Engage NEW enemies post-teleport. Expected: every restored horde
   member engages immediately.

### Files

- `Beelzebub/Beelzebub/Services/SummonAllyService.cs` — `LiveCastCount`
  split into `LiveCastCount` + `LivePopulatedCastCount` (shared helper
  `CountAliveGroups`). `RestoreAll` re-primes BehaviourTreeState +
  Aggroable + AggroConsumer + SyncAggro, ModeModifiable=0.
- `Beelzebub/Beelzebub/Patches/LinkMinionToOwnerOnSpawnSystemPatch.cs`
  — switched over-cap check from `LiveCastCount` to
  `LivePopulatedCastCount`.

### Patch count

12 Harmony patches active (unchanged from v0.23.16).

---

## [0.23.16] - 2026-05-22

### Tier-0 fixes: Priest cap regression (B1) + horde combat AI inconsistency (B2)

User reported v0.23.15 still allows continuous Priest summoning without
hitting the 3-cast cap, and that Priest horde minions engage inconsistently
(some retaliate when struck, others stand idle). Both diagnosed via
Bloodcraft pattern comparison.

### B1 — cap regression

**Root cause.** `LiveCastCount` pruned empty cast groups immediately. The
Priest channels RaiseHorde/RaiseDead for ~2 seconds before V Rising's
spawn chain produces the actual entities. The 3-second `AttributionWindow`
caught the first spawn but typically missed the rest, leaving the cast
group either empty or partially populated. When all entities died,
`LiveCastCount` pruned the group → cap count returned 0 → cap was never
enforced.

**Fix.**

1. **Window bump** — `AttributionWindow` extended from 3s → 10s. Channeled
   priest summons + V Rising's buff→spawn→rebind pipeline frequently
   produce entities 4-6s after cast start.
2. **Empty-group TTL** — `LiveCastCount` no longer prunes empty groups
   that are still within the attribution window. A freshly-opened group
   counts against the cap even before any entities arrive, ensuring rapid
   re-casts are refused. After the window expires, an empty group is
   pruned normally (confirmed orphan: spawns never arrived).
3. **Time-aligned tracking** — new `ActiveTransform.SummonStackTimes`
   dictionary parallel to `SummonStacks`. `BeginCastGroup` records cast
   time per group; `LiveCastCount` removes from both lists in lockstep.

### B2 — combat AI inconsistency

**Root cause.** Bloodcraft pattern analysis revealed two missing pieces:

1. No `BehaviourStateChangedSystem` interception. When a Priest minion
   transitioned to `Return` (gave up on combat, walking back to leash
   anchor) or `Idle` (no target, nothing to do), it got stuck. Bloodcraft
   force-flips these transitions to `Follow` for tracked familiars; we
   weren't doing it for summons.
2. No `Aggroable.Value` / `AggroConsumer.Active` enable in
   `ApplyPlayerAllySetup`. Some V Rising unit prefabs spawn with these
   flags false (skeleton variants spawned from graveyard chains, etc.).
   With them false, the unit cannot acquire targets or process aggro
   events — the SyncAggro pushes had no effect.

**Fix.**

1. **New patch** `BehaviourStateChangedSystemPatch` — hooks
   `CreateGameplayEventOnBehaviourStateChangedSystem.OnUpdate` Prefix.
   For each state-change event whose target is a tracked summon (via new
   `SummonAllyService.IsTrackedSummon`), force `Return`/`Idle` → `Follow`
   on both the target entity and the event payload. Same pattern Bloodcraft
   uses for familiars.
2. **Aggro enable in setup** — `ApplyPlayerAllySetup` now sets
   `Aggroable.Value._Value = true` (with DistanceFactor / AggroFactor = 1)
   and `AggroConsumer.Active._Value = true` when those components exist.
   Ensures every spawned minion can both acquire and react to targets.
3. **Initial Follow state** — `BehaviourTreeState.Value = Follow` on
   spawn. Combined with #1, prevents minions from initializing into
   an Idle/Return state that requires user damage to break out of.
4. **`IsTrackedSummon` helper** in `SummonAllyService` — scans every
   active transform's `SummonedMinions` list. Used by the new
   BehaviourStateChanged patch to identify "our" entities.

### Diagnostic logging

Verbose-mode (`VerboseLogging = true` in BepInEx.cfg) now emits per-stage
logs across the full summon lifecycle:

* `[Beelz SUMMON][cast]` — every cast event from a transformed player +
  cap result + cast-group open.
* `[Beelz SUMMON][link]` — every LinkMinion entity inspection + owner
  chain result + attribution window check (with elapsed time).
* `[Beelz SUMMON][track]` — every TrackInCurrentGroup outcome
  (added vs skipped).
* `[Beelz SUMMON][setup-ok]` — per-summon component state after
  `ApplyPlayerAllySetup` (faction, followerMode, BehaviourTreeState,
  AggroBuffer length).
* `[Beelz SUMMON] state-fix` — every BehaviourTreeState force-flip.

Lets a future test cycle confirm where the chain breaks if any regression
appears in this area.

### Files

- `Beelzebub/Beelzebub/Patches/BehaviourStateChangedSystemPatch.cs` — NEW.
- `Beelzebub/Beelzebub/Services/SummonAllyService.cs` — Aggroable/AggroConsumer
  enable + initial BehaviourTreeState=Follow + IsTrackedSummon helper +
  empty-group TTL in LiveCastCount + SummonStackTimes maintenance in
  BeginCastGroup + diagnostic logging in TrackInCurrentGroup.
- `Beelzebub/Beelzebub/Patches/AbilityCastStartedSystemPatch.cs` — bumped
  AttributionWindow 3s→10s + diagnostic logging.
- `Beelzebub/Beelzebub/Patches/LinkMinionToOwnerOnSpawnSystemPatch.cs` —
  attribution-elapsed diagnostic logging.
- `Beelzebub/Beelzebub/Services/AbilityRegistry.cs` — `ActiveTransform.SummonStackTimes`
  parallel dictionary.

### Patch count

12 Harmony patches now active (was 11 in v0.23.15).

---

## [0.22.0] - 2026-05-22

### Summon-cast intercept (Option 1) — manual spawn at cast-start

The v0.21.1 diagnostic confirmed our v0.20.0 `LinkMinionToOwnerOnSpawnSystem`
Prefix patch was the wrong hook: V Rising's spawn chain dies BEFORE reaching
that system when a player casts a summon (zero `[Beelz DIAG]` lines in
user's test log). The intermediate `AbilitySpawnPrefabOnCast →
SpawnMinionOnGameplayEvent` steps are gated on NPC-only caster context.

This release implements **Option 1** from the v0.21.1 plan: hook the
cast-start system directly and **manually spawn** the target unit ourselves,
bypassing V Rising's broken chain entirely.

### Architecture

- **New patch** `Patches/AbilityCastStartedSystemPatch.cs`:
  - `[HarmonyPatch(typeof(AbilityCastStarted_SetupAbilityTargetSystem_Shared),
    nameof(...OnUpdate))]` Prefix — same hook Bloodcraft uses for
    familiar-cast detection (`AbilityRunScriptsSystemPatch.cs:73`).
  - Iterates `AbilityCastStartedEvent` components from
    `__instance.EntityQueries[0]`.
  - For each event with a player caster + active Beelzebub transform +
    summon-name pattern match (`_Summon_` / `_Reinforcement_` /
    `_CallReinforcements_` / `_Summoning_`):
    - Looks up the target CHAR_ prefab + spawn count in a curated
      `SummonTargets` map.
    - Calls `ServerGameManager.InstantiateEntityImmediate(caster, target)`
      to manually create the unit. Returns the spawned entity in the same
      frame (same sync-instantiation pattern as TransformBuffService in
      v0.17.1).
    - Positions the spawned minion near the player via the player's
      `LocalToWorld.Position`, with a small radial scatter when
      `spawnCount > 1`.
    - Applies the player-ally setup (`EntityOwner`, `FactionReference =
      Faction_Players`, `Follower` to player, `Minion.MasterDeathAction =
      Kill`) — same recipe as v0.20.0's `LinkMinionToOwnerOnSpawnSystemPatch`.
    - Tracks the spawned minion on `ActiveTransform.SummonedMinions` for
      cleanup on `.beelz revert` (v0.20.0 lifecycle code).
  - Dedupe window of 250 ms per (steamId, abilityGuid) to prevent
    double-spawn if V Rising fires the cast event across multiple ticks.

### Initial curated map

Currently populated with the one confirmed entry from earlier prefab research:

```
AB_Undead_BishopOfShadows_ShadowSoldier_Group     → CHAR_Undead_ShadowSoldier ×2
AB_Undead_BishopOfShadows_ShadowSoldier_AbilityGroup → CHAR_Undead_ShadowSoldier ×2
```

Pending entries (TODO — populate from real test data):

```
AB_Bandit_StoneBreaker_VBlood_Reinforcement_Group      → ???
AB_Bandit_Stalker_VBlood_Reinforcement_Group           → ???
AB_Bandit_Tourok_VBlood_CallReinforcements_AbilityGroup → ???
AB_ChurchOfLight_Overseer_Reinforcement_AbilityGroup    → ???
AB_Bandit_Foreman_Reinforcement_Group                  → ???
```

**For unknown summons**: a warning is logged with the ability name. User
can paste back what they see + we add to the map. Each entry needs the
correct CHAR_ prefab GUID and spawn count for that V-Blood.

### Diagnostic flow

On a player-cast summon by a transformed player, the log emits one of:

- `[Beelz SUMMON] {steamId} cast {ability} → spawned N allied {target} unit(s)` — success
- `[Beelz SUMMON] cast by {steamId} (transformed): {ability} — NO entry in
  SummonTargets map. Spawn target unknown. Add to ... to enable manual
  spawn.` — needs curation
- Error lines if `InstantiateEntityImmediate` throws

### Shapeshift roster expansion

Also in v0.22.0 (queued from v0.21.1 source change):
- `Buff_General_Shapeshift_Werewolf_Standard`-equivalent skin added for
  werewolf-themed V-Bloods
- `AB_Tailor_Shapeshift_Gargoyle_Buff` (`-395216184`) added for the Tailor
  V-Blood transform (uses the boss's phase-2 form)

The V Rising shapeshift system supports specific shipped models. EXO form
(Bloodcraft) uses Evolved Vampire / Corrupted Serpent — also V-Rising-
shipped forms. We can use any shipped form; we can't add new ones.

### What still won't work after v0.22.0

- **Character model swap for arbitrary V-Bloods**: Stonebreaker, Bishop,
  Cursed Smith, etc. — V Rising doesn't ship shapeshift forms for those.
  Hard engine limit.
- **Summons for V-Bloods not yet in the curated map**: warning logs will
  guide which entries to add. Iterative.

### How to populate the map

1. Set `VerboseLogging = true` in
   `BepInEx/config/kdpen.Beelzebub.cfg`.
2. Transform into a V-Blood with a summon ability.
3. Cast the summon.
4. Check `LogOutput.log` for `[Beelz SUMMON] cast by ... NO entry in
   SummonTargets map. Spawn target unknown.` lines.
5. Paste the lines back to me + your best guess of which CHAR_ the V-Blood
   summons in the actual boss fight, and we'll add it.

### Visual ability animation mapping (Task #84) — still queued

Per user request: research animation-mapping or partial-transform-on-cast
for visual feedback. Filed but not implemented this release — the summon
work was the higher-priority blocker. v0.23.x or later.

### Files touched

- New: `Patches/AbilityCastStartedSystemPatch.cs` — cast-start hook +
  manual spawn + curated target map.
- `Services/ShapeshiftService.cs` (queued from v0.21.1) — Werewolf +
  Tailor Gargoyle added to roster.
- csproj + thunderstore: 0.21.1 → 0.22.0.
- Both changelogs.

### Build status

Compile clean (zero warnings, zero errors). DLL is at
`Beelzebub/Beelzebub/bin/Release/net6.0/Beelzebub.dll`. The auto-copy to
the live plugins folder failed because your server is running and locking
the DLL — stop the server, the next build will deploy. (Or copy the DLL
manually.)

---

## [0.21.1] - 2026-05-22

### Summon-spawn diagnostic + clearer transform message

From in-game test of v0.21.0: Stonebreaker transform showed the correct
spell bar (MountainRumbler now visible — Z3 fix confirmed working) and
abilities cast, but the Reinforcement summon plays its cast audio without
spawning any units. v0.20.0's `LinkMinionToOwnerOnSpawnSystem` patch was
designed to rebind minions that V Rising's spawn chain INSTANTIATES. If
the chain stops before reaching that system, our patch never sees the
entity and the player gets "sound + nothing."

### Diagnostic added

`LinkMinionToOwnerOnSpawnSystemPatch.OnUpdatePrefix` now dumps every
entity in its query when `VerboseLogging = true` in the config. Format:

```
[Beelz DIAG] LinkMinion tick: N entity(s) in query.
[Beelz DIAG]   - entity=… prefab=… owner=… ([PLAYER])
```

To debug a summon ability that "fires but spawns nothing":

1. Set `VerboseLogging = true` in `BepInEx/config/kdpen.Beelzebub.cfg`.
2. Restart server (config-binding) OR `.beelz admin reload`.
3. Transform into the relevant V-Blood.
4. Cast the summon ability.
5. Check `BepInEx/LogOutput.log` for `[Beelz DIAG]` lines.

Three diagnostic outcomes:

- **Zero `[Beelz DIAG]` lines after the cast** → V Rising's spawn chain
  isn't reaching `LinkMinionToOwnerOnSpawnSystem` at all. The cast's
  `AbilitySpawnPrefabOnCast` → `SpawnMinionOnGameplayEvent` chain is
  failing before instantiating any entity. Likely cause: an intermediate
  step is gated on the caster having an NPC-only component (Spawner,
  AiBehaviour, etc.) that players don't have. Fix would require an
  earlier intercept (cast-system level), not a late rebind.
- **`[Beelz DIAG]` lines show entities but `owner=<no EntityOwner>`** →
  V Rising is spawning the minion without an owner reference. The
  `SetTeamToOwner=True` flag on `SpawnMinionOnGameplayEvent` requires
  an `EntityOwner` to read FROM. Cast chain is providing owner=null.
- **`[Beelz DIAG]` lines show entities owned by SOMETHING but not the
  player** → owner is the carrier buff, the cast prefab, or an
  intermediate entity. Our `ResolveOwningPlayer` walks 4 hops; if it
  fails, the chain may need deeper walking or a different lookup
  strategy.

User: please run the diag and paste the relevant log lines. With those
in hand, the next iteration can target the correct fix.

### Clearer transform message when no model swap is possible

The `.beelz transform <unit>` success message previously said nothing
about the visual outcome when the unit doesn't match a native shapeshift
form. Players were reasonably reading "transformed" as implying a model
change. Now the message explicitly notes:

> Transformed into CHAR_Bandit_StoneBreaker_VBlood (4 ability slots).
> (No visual model swap — V Rising only provides Wolf/Bear/Rat/Spider/Toad
> shapeshift forms; this unit doesn't map. Spell bar + stats only.)
> Your spell bar now wields the unit's abilities. Use .beelz revert to end.

When a native form IS applied (FullReplace=true on a Wolf-flavored
unit, or `Transform_NativeShapeshift_Enabled=true`), the message still
shows the visual hint as before.

### Visual-ability-animation research filed for future

User-requested feature: when player casts a captured ability, give the
character a visual animation instead of standing still. Two design
candidates filed under task #84:

- **Option A — Animation-mapping table**: build a registry of player-
  character animations (vampire melee, dash, channeling spell, ground-
  slam ultimate, etc.). Curate per-ability mapping in a new field on
  `AbilityEntry` like `"VisualAnimation": "Slam"`. Runtime fires the
  player animation alongside the ability cast. Requires discovering
  V Rising's player animation registry — non-trivial research.

- **Option B — Partial transform during cast**: when player initiates
  a cast, temporarily apply the corresponding native shapeshift form,
  let the cast finish, then revert. Avoids the "off-form cast exits
  the form" problem only if we can suppress the exit. May require
  overriding the form's `BuffCategory.Shapeshift` behavior.

Deferred — neither is small. Will revisit after summon-as-ally is
confirmed working in-game (the more pressing user issue) and the v1.0
release cut.

### Notes

- `MountainRumbler` showing up on Stonebreaker's bar is the v0.19.0 Z3
  fix landing correctly. The user previously reported it as a "missing
  ultimate" — confirmed resolved.
- Character model unchanged on Stonebreaker is the V Rising hard limit
  (Stonebreaker is humanoid, no native form). New transform message
  now communicates this explicitly.

### Files touched

- `Patches/LinkMinionToOwnerOnSpawnSystemPatch.cs` — diagnostic log added.
- `Services/TransformService.cs` — explicit no-visual message.
- csproj + thunderstore: 0.21.0 → 0.21.1.
- Both changelogs.

---

## [0.21.0] - 2026-05-22

### Post-audit polish #8 — `.beelz list` paginated + new `.beelz search`

The audit's leftover polish queue. Once a player accumulates 50+ captures,
`.beelz list` was building a single concatenated chat reply that blew
past VCF's 510-byte cap (same class of bug as `.beelz preview` pre-0.19.0,
but it was just lurking). Also surfaced from the audit: no way to filter
captures to find a specific ability.

### `.beelz list [page]` — paginated

- 15 captures per page (one chat reply per capture line, header lines
  emitted separately).
- Page 1 shows the slot-binding header (universal + per-weapon bound
  slots, currently-equipped weapon).
- All pages show `Captured N. Page X/Y:` header + one reply per ability:
  `  42: [V] StoneBreaker → MountainRumbler`
- Original index preserved across pagination — `.beelz grant <slot>
  <index>` still works with any printed index from any page.
- Footer hints `More? .beelz list 2` and `Search: .beelz search <term>`
  when pages > 1.

### `.beelz search <term>`

- Case-insensitive substring match against both ability and unit names.
- Returns up to 25 matches; if more exist, prints
  `…more than 25 results. Narrow your search term.`
- Same compact format as paginated list — original index preserved for
  binding commands.
- Examples:
  - `.beelz search bolt` → every ability with "bolt" in name
  - `.beelz search bishop` → every ability captured from any Bishop unit
  - `.beelz search vblood` → every V-Blood capture

### Why this matters

The mod is designed for the long-tail collection use case — admins enable
moderate drop rates, players accumulate 100+ captures over a season.
Without pagination the most-used command was unusable at scale, and
without search there was no way to actually find anything in that list.
Both gaps surface immediately the moment a real player starts collecting.

### Files touched

- `Commands/BeelzCommands.cs` — `List` rewritten to paginate, new `Search`
  command, factored-out `EmitSlotHeader` helper.
- csproj + thunderstore: 0.20.1 → 0.21.0.
- Both changelogs.

### Audit backlog status after this release

Post-audit polish items remaining:

- W4 hotkey cast-trigger BCH return path — BCH-blocked (no UI consumer yet)
- IN2.captured_at timestamp — schema migration deferred
- W5 dead schema field cleanup — preserves backward compat, low priority
- Preset import/export — niche

Nothing remaining is critical or high-frequency. Beelzebub is in
maintenance + polish territory until BCH UI development picks up or
specific gameplay testing surfaces new bugs.

---

## [0.20.1] - 2026-05-22

### Audit pass — six fixes/additions from the v0.20.0 audit report

Each finding was re-verified before action. One agent finding turned out to
be a false positive and was dismissed; the others all implemented.

### AUDIT-1 ✅ Curated matrix installed on live config

Previously the live `ability_rules.json` had only the global filter (deny
patterns) — no `AbilityMap`, no `TransformMap`. So the 443-ability + 64-V-Blood
curation work at `Beelzebub/docs/ability_map.curated.json` was sitting unused.

Action taken: backed up the user's existing file as
`ability_rules.json.backup-pre-v0.20.0`, installed the curated file as
the new live `ability_rules.json`, then patched in the v0.18.0+ deny-pattern
additions (`_FeedBoss_`, `_Feed_Initiate_`) that the older curated file
predates. Summon-related deny entries deliberately NOT added (v0.20.0 wants
summons through).

### AUDIT-2 ❌ FALSE POSITIVE — no action

The audit agent flagged three TransformMap entries as referencing non-existent
prefabs:
- `CHAR_Forest_Bear_Dire_VBlood`
- `CHAR_Militia_Longbowman_LightArrow_VBlood`
- `CHAR_Undead_Leader_VBlood`

**Verified false**: all three prefabs DO exist in V Rising, with lowercase
`_Vblood` (e.g. `CHAR_Forest_Bear_Dire_Vblood`). The agent did a
case-sensitive file comparison. `AbilityRules.NormalizeTransformMap` uses
`StringComparer.OrdinalIgnoreCase`, so the case mismatch doesn't break
lookups. No fix needed.

### AUDIT-3 ✅ Dead `AbilityCategory.Ultimate` enum value removed

Verified zero V Rising prefabs use `_Ultimate_` token. Removed from
classifier; left the enum value `2` reserved with a comment for any
future signature/boss-tier heuristic that might want to fill it.

### AUDIT-4 ✅ Admin remote slot binding

Four new commands in `AdminCommands.cs`:

- `.beelz admin set-slot <player> <slot> <abilityGuid>` — universal slot.
- `.beelz admin set-weapon-slot <player> <weapon> <slot> <abilityGuid>` — weapon-specific.
- `.beelz admin clear-slot <player> <slot>` — clear universal slot.
- `.beelz admin clear-weapon-slot <player> <weapon> <slot>` — clear weapon-specific.

Each applies live when the player is online + wielding a compatible weapon
(via `SlotApply.ApplyGrant`/`ClearGrant`), and persists for offline players.
Surfaces warnings for `TransformOnly` / `Enabled=false` overrides. All
audit-logged with full detail.

### AUDIT-5 ✅ Player-facing `.beelz catalog` (hunt list)

Players had `.beelz transforms` to see what they'd already unlocked but no
way to see what was still left to hunt. New command lists every TransformMap
entry sorted by Tier ascending (early-game first), with `[X]` for unlocked
and `[ ]` for still-to-hunt. Paginated 10 per page to fit under VCF's reply
cap. Footer shows `unlocked / total` and `Page N/M`. New entries also added:
`Beelzebub.EntityExtensions.GetPrefabName().Humanize()` is leaned on for
readable display names (strips `CHAR_` / `_VBlood` etc.).

### AUDIT-6 ✅ Bulk admin operations

Four new admin commands:

- `.beelz admin revert-all` — force-end every active transformation on the
  server. Snapshots steam-IDs before iterating to avoid concurrent-modification
  bugs. Logs reverted-count.
- `.beelz admin freeze-captures <on|off|status>` — runtime toggle for the
  `CaptureOnKill` master switch. No config reload needed.
- `.beelz admin snapshot` — high-level state dump to chat: player count,
  active transforms, curated entry counts, server mode, scaling mode, build
  version. Useful for "what's going on right now."
- `.beelz admin wipe-all CONFIRM-WIPE` — destructive: clears every player's
  data. Requires literal `CONFIRM-WIPE` arg as a typo-guard. Reverts any
  active transforms first (so carrier buffs + summoned minions clean up
  gracefully), then wipes the registry. Returns counts.

Added `AbilityRegistry.WipeAll()` helper that clears every internal
dictionary atomically and returns (players, abilities, transforms) wiped.

### AUDIT-7 ✅ Gate-boss handling

V Rising has 17 gate-boss variants (`CHAR_*_VBlood_GateBoss_Minor|Major`)
— easier prefab copies of main V-Bloods that exist for the castle-gate
unlock fights. Pre-0.20.1, defeating a gate-boss could grant a separate
transform unlock for that exact prefab GUID — duplicate of the main version
but with default Tier=1 / Difficulty=Basic / no curation. Clutter.

Decision: **filter gate-boss kills out of the transform-unlock roll** but
preserve ability captures (the abilities ARE the main boss's kit, so
capturing from a gate-boss kill is a legitimate "preview" pathway).

Implementation: substring check for `_GateBoss_` in the killed unit's
prefab name, both in `DeathEventListenerSystemPatch.ProcessForParticipant`
and `VBloodSystemPatch.OnUpdatePostfix`. Skip the transform-roll branch
when matched; verbose-log when verbose logging is on.

### Files touched

- `Beelzebub/docs/ability_map.curated.json` → installed at server config dir.
- `Services/Categorization.cs` — Ultimate removed; comment marks reserved value.
- `Commands/AdminCommands.cs` — four set-slot/clear-slot commands + four bulk ops.
- `Commands/TransformCommands.cs` — `.beelz catalog` command + helper.
- `Services/AbilityRegistry.cs` — new `WipeAll` method.
- `Patches/DeathEventListenerSystemPatch.cs` — gate-boss filter on transform-roll.
- `Patches/VBloodSystemPatch.cs` — gate-boss filter on transform-roll.
- csproj + thunderstore: 0.20.0 → 0.20.1.
- Both changelogs.

### Verified before action

Each finding from the v0.20.0 audit was spot-checked against the source
data (live config, prefab dump, classifier code) before any code change.
AUDIT-2 turned out to be a false positive from case-sensitive comparison
and was dismissed without action.

---

## [0.20.0] - 2026-05-22

### Summon-as-ally support (Task #74) — the "lead a horde" use case

The user identified this as a key gameplay vision: transforms (and granted
summon abilities) should let the player become a small army's worth of NPC
allies, not just a ghost-caster whose summon abilities silently fail. This
release implements that.

### Research summary

Three parallel research agents investigated V Rising's spawn surface,
the Bloodcraft familiar-binding pipeline, and the actual prefab structure
of NPC summon abilities. Findings:

1. **Hook surface**: V Rising's `LinkMinionToOwnerOnSpawnSystem.OnUpdate`
   fires when a freshly-spawned entity with `EntityOwner + Minion + SpawnTag`
   needs to be linked to its owner. Perfect Harmony Prefix target —
   Bloodcraft uses this exact system for the same kind of intercept.
2. **Component recipe** (from Bloodcraft `FamiliarBindingSystem.ModifyFollowerFactionMinion`):
   `EntityOwner.Owner = player`, `FactionReference.FactionGuid = Faction_Players`,
   `Follower.Followed = player` + `Follower.ModeModifiable = 0` (leash mode),
   `Minion.MasterDeathAction = Kill` (clean despawn when player dies).
3. **V Rising defaults already partly work**: the spawn-event component
   `SpawnMinionOnGameplayEvent` has `SetTeamToOwner: True` and
   `InheritOwnerFaction: True`, so direct player-cast summons should
   already get the right team. Our patch is the safety net for the
   chained-spawn case where intermediate effects (carrier buff → trigger
   prefab → minion) can muddle the owner chain.

### Implementation

- **New** `Patches/LinkMinionToOwnerOnSpawnSystemPatch.cs` — Prefix on
  `LinkMinionToOwnerOnSpawnSystem.OnUpdate`. Iterates the system's
  `_Query` (EntityOwner + Minion + SpawnTag), walks each entity's
  EntityOwner chain (up to 4 hops) to find the originating player,
  and if that player has an active Beelzebub transform, applies the
  player-ally component override:
  - `EntityOwner.Owner = playerCharacter` (defensive rewrite).
  - `FactionReference.FactionGuid = 1106458752` (Faction_Players).
  - `Follower.Followed = playerCharacter`, `ModeModifiable = 0`.
  - `Minion.MasterDeathAction = Kill`.
  - Tracks the minion entity on `ActiveTransform.SummonedMinions`.
- **`ActiveTransform.SummonedMinions`** (runtime list) — populated by the
  patch as minions spawn; consumed by `TransformService.Revert` and
  `TransformService.Tick` on transform end.
- **`TransformService.Revert`** + **`Tick`** — both now iterate
  `SummonedMinions` and call `EntityManager.DestroyEntity` on each.
  Clean lifecycle: when the transform ends, every minion summoned during
  it disappears too. No wandering-mob trail across the world.
- **Config**: new `Transform_SummonsAreAllies` (default `true`). Set to
  `false` to disable the rebind machinery entirely (summons revert to
  V Rising's vanilla spawn behavior — which on direct player casts
  usually works via `SetTeamToOwner=true`, but breaks for chained
  summons like Stonebreaker reinforcements).
- **Default `DenyPatterns` cleanup**: removed `_Summon_`, `_Summoning_`,
  `_CallReinforcements_`, `_Reinforcement_` from the defaults. Summons
  are now allowed through capture + grant + transform spell-bar by
  default. Existing `ability_rules.json` files retain their copies of
  these patterns until edited.

### What admins need to do for existing installations

The `ability_rules.json` written by v0.18.0 / v0.19.0 still has the
summon patterns in its `DenyPatterns` array. To pick up the v0.20.0
behavior:

```json
"DenyPatterns": [
  "_Idle_", "_Flee_", "_Sequence_",
  "_MeleeAttack_", "_Block_", "_Parry_", "_Counter_",
  "_Spawn_", "_Despawn_", "_Disappear_", "_Death_", "_Wounded_",
  "_Hard_",
  // REMOVE THESE FOUR if you want summons:
  // "_Summon_", "_Summoning_", "_CallReinforcements_", "_Reinforcement_",
  "_FeedBoss_", "_Feed_Initiate_",
  "_Test_", "_Internal_", "_DEBUG_"
]
```

Then `.beelz admin reload` (no restart needed).

### What this enables, concretely

- Transform into **Stonebreaker** → `.beelz current` now shows
  `Reinforcement` in your bar. Cast it → 2-3 bandit-stone-worker NPCs
  spawn around you, **on your team**, attacking nearby enemies.
- Transform into **Bishop of Shadows** → cast `ShadowSoldier_Cast` →
  shadow soldiers spawn allied.
- `.beelz revert` despawns all minions you summoned. They don't persist.
- Same applies to `.beelz grant`-bound summon abilities (when not
  transformed — though for the non-transformed path V Rising's vanilla
  spawn machinery does most of the work; our patch is a safety net).

### Known limitations carried forward

- **Visual model still doesn't change** for units outside the 5 native
  shapeshift forms (Wolf/Bear/Rat/Spider/Toad). V Rising hard limit.
- **Cast chain edge cases**: some boss summons use a sub-event chain
  (cast → trigger → minion). If a specific summon doesn't spawn anything
  even with this release, the chain has a step that's still gated on
  NPC-caster context. Report which ability and we'll patch the
  intermediate.
- **Follower count is unbounded** — there's no per-player cap on the
  number of active summons. A Bishop of Shadows transform spammed
  could in theory spawn a hundred soldiers. Future work: per-transform
  summon-count cap config.
- **Despawn is hard delete** — `EntityManager.DestroyEntity`. No death
  animation, no loot drop. By design (these are temporary transform
  summons, not real bandits).

### Files touched

- New: `Patches/LinkMinionToOwnerOnSpawnSystemPatch.cs`.
- Modified: `Services/AbilityRegistry.cs` (`SummonedMinions` on
  `ActiveTransform`), `Services/TransformService.cs` (despawn on
  revert + tick), `Services/AbilityRules.cs` (default DenyPatterns),
  `Config/Settings.cs` (Transform_SummonsAreAllies).
- csproj + thunderstore: 0.19.0 → 0.20.0.
- Both changelogs.

---

## [0.19.0] - 2026-05-22

### Three bug fixes + a new scaling mode + a tracked research task

In-game testing of v0.18.0 surfaced two correctness bugs and one immediate
crash, plus a substantive design conversation about how transformations
should interact with player progression. Addressing each in priority order.

### Bug 1 — Z3 Hard-only filter fails for V-Blood naming asymmetry

Confirmed via the user's `.beelz preview StoneBreaker` output: Hard
variants (`AB_X_RockSmash_Hard_AbilityGroup`) were leaking through into
the transform spell bar despite Z3's "filter Hard on Basic mode unless
no Basic sibling exists" carve-out. Root cause: V-Blood abilities carry
`_VBlood_` in their basic name but NOT in their Hard duplicate, so the
literal sibling-name match always failed. Z3 incorrectly concluded the
Hard variant was Hard-only and let it through. Worse, the Hard
duplicates consumed slot space that would otherwise have gone to real
abilities like `MountainRumbler` (StoneBreaker's "ultimate" the user
remembered seeing in the boss fight but not on the transformed bar).

Fix: `TransformService.GetTransformAbilities` now normalizes ability
names for sibling comparison by stripping **both** `_Hard_` AND
`_VBlood_` tokens before building the basic-set lookup. New
`NormalizeForHardCompare` helper. Z3's carve-out is preserved for genuine
Hard-only abilities (e.g. Bishop of Death's ChainBolt).

### Bug 2 — `.beelz preview` crashes on units with >5 abilities

Stack trace: `Il2CppException: FixedString512Bytes: Truncation while
copying ...`. VCF's `ctx.Reply()` accepts a fixed-512-byte string; my
preview output for StoneBreaker (~600 bytes once formatted) blew past
the cap. Fix: both `.beelz current` and `.beelz preview` now emit one
chat reply per slot rather than building a single concatenated message.
Each reply line is comfortably under the cap.

### Bug 3 — `_Reinforcement_` summons still get through

The default DenyPatterns added in v0.18.0 covered `_Summon_` /
`_Summoning_` / `_CallReinforcements_` but missed the bare
`_Reinforcement_` pattern that StoneBreaker (and several other V-Bloods)
use. Added. **Existing installations**: same advice as v0.18.0 — your
`ability_rules.json` is loaded as-is. Add `_Reinforcement_` to the
`DenyPatterns` array, or delete the file to regenerate.

### TX7-extended — New `PlayerLeveled` scaling mode

Closes the "I want my transformation to be powerful but only if my
character is also powerful" design conversation. Hybrid of the existing
`PrefabAbsolute` (read CHAR_ prefab UnitStats) and a level-progression
multiplier.

- New `PowerScalingMode.PlayerLeveled` enum value.
- New config `Transform_PlayerLeveled_MaxLevel` (default `90`).
- `TransformBuffService.ComputePlayerLevelFactor(character)` reads the
  player's `UnitLevel.Level._Value` and returns
  `clamp(level / max_level, 0, 1)`.
- `AttachPrefabAbsolute` takes a new `scaleFactor` parameter (1.0 for
  PrefabAbsolute, the computed factor for PlayerLeveled). Boss stats are
  multiplied by it before being added.

On a vanilla server `UnitLevel` reflects gear level; on a Bloodcraft
server it reflects Bloodcraft's effective level (which Bloodcraft writes
via `ModifyUnitLevelBuff` based on its own leveling system). So
PlayerLeveled naturally tracks Bloodcraft progression and resets when a
player prestiges (their UnitLevel drops, factor drops, transform power
drops). Closes the prestige-compatibility ask.

The four modes side-by-side:

| Mode | Source | Bloodcraft prestige |
|---|---|---|
| `CuratedScales` | admin-curated `*Scale` fields | indirect (admin tunes) |
| `PrefabAbsolute` | CHAR_ prefab full boss tier | none (ignores level) |
| `PlayerScaled` | none — vanilla formula | automatic (player's natural stats track Bloodcraft) |
| `PlayerLeveled` *(new)* | CHAR_ prefab × player level curve | automatic (level drops → factor drops → bonus drops) |

### Tracked task #74 — Summon-as-ally support (research, deferred)

The user's question is fair: Bloodcraft makes NPC familiars side with
the player by overriding Team / Faction on spawn and adding follow
logic. We could plausibly do the same for transient summon-ability
spawns triggered by a transformed Beelzebub player. Realistic scope is
a dedicated 1-2 session research + implementation effort — needs:

- Hook a spawn system (likely `BuffSystem_Spawn_Server` or
  `LinkMinionToOwnerOnSpawnSystem`) to catch the moment a summon's units
  materialize.
- Identify those units as Beelzebub-triggered (via the caster's active
  carrier buff or a marker on the cast).
- Modify the spawned units' `Team` / `Faction` to player-friendly.
- Add an `EntityOwner` pointing at the player.
- (Optionally) Follow logic so they don't wander.
- Track the spawned units on the `ActiveTransform` record.
- Despawn on revert.

Filed as task #74. Until then, summon abilities remain filtered by
default (the cast goes off with no effect; better to hide them entirely
than confuse players).

### Files touched

- `Services/TransformService.cs` — Z3 normalization + new
  `NormalizeForHardCompare` helper.
- `Commands/TransformCommands.cs` — `.beelz current` and `.beelz preview`
  rewritten to emit per-slot reply lines.
- `Services/AbilityRules.cs` — `_Reinforcement_` added to default
  `DenyPatterns`; `PlayerLeveled` added to mode parser.
- `Services/Categorization.cs` — `PlayerLeveled` enum value + docstring.
- `Config/Settings.cs` — `Transform_PlayerLeveled_MaxLevel` config +
  PowerScalingMode docstring updated.
- `Services/TransformBuffService.cs` — `AttachStatScales` takes character
  entity; `PlayerLeveled` branch; `AttachPrefabAbsolute` takes
  `scaleFactor`; `ComputePlayerLevelFactor` helper.
- csproj + thunderstore: 0.18.0 → 0.19.0
- Both changelogs.

### Visual model — still won't change

Reiterating because it surfaced again in testing: V Rising exposes only
five native shapeshift forms (Wolf/Bear/Rat/Spider/Toad). For any other
unit type (humanoid bandits like Errol, gore-swine like Putrid Rat
Ravager, etc.), the player's model stays the same. This is hardcoded
into V Rising's shapeshift system and unreachable from a server-side
mod. **It is not a Beelzebub bug.** Your spell bar swaps, your stats
change (per TX6/TX7), your weapon locks if FullReplace is set — but the
visual model only changes for the five matching unit families.

**Note about Errol** (Stonebreaker): you'd transform into him with the
correct ability bar after the v0.19.0 Z3 fix (MountainRumbler restored),
but your character will still look like Chaos, not Errol. The only way
to change that would be V Rising itself shipping a "fully arbitrary
shapeshift" API.

---

## [0.18.0] - 2026-05-22

### Visibility commands + summon-ability filter (from user testing)

In-game testing of v0.17.1 surfaced three concerns: (1) the visual model
doesn't change for non-shapeshift-eligible transforms, (2) summon abilities
cast but don't actually spawn allies, (3) the player wanted a way to see
exactly what's on their bar when something looks "missing." Three responses:

### New `.beelz current` command

Shows what's effectively on the player's spell bar right now, transform-aware.

- While transformed: lists the carrier-buff slot loadout for the current
  phase, with the AbilityCategory badge per slot (`Ultimate`, `Aoe`, etc.).
  Calls out any unused slots ("unit has no more eligible abilities").
- While not transformed: lists the resolved slot map for the player's
  currently-equipped weapon family, flagging any saved grant that's
  INACTIVE because the weapon doesn't match.

Lets players debug "why isn't X on my bar?" themselves without needing the
admin to inspect.

### New `.beelz preview <unit>` command

Shows what abilities a transform target would grant *before* activating it.
Accepts an index (from `.beelz transforms`) or a substring of the unit name.
Lists every phase the unit has curated, with per-slot ability names + category
badges. Surfaces the TransformMap entry's `Difficulty`, `Tier`,
`PowerScalingMode`, `Notes`, plus the server difficulty mode. Footer reminds
the player about denylist-filtered abilities so they don't expect impossible
captures.

Helps decide which V-Blood to transform into without committing.

### Default DenyPatterns extended

V Rising's summon abilities (e.g., StoneBreaker calling stone golems, Bishop
of Shadows summoning shadow soldiers) **cast but don't actually spawn units
for players**. The spawner code requires an NPC caster context — it reads
the caster's faction/team/spawner config to set up the summoned units, and
players don't carry those. The player performs the cast animation but
nothing materializes.

Workaround: filter these abilities at the capture / transform-grant layer
so they don't pollute the spell bar in the first place. New default
DenyPatterns entries:

- `_Summon_`, `_Summoning_`, `_CallReinforcements_` — the bulk of NPC
  summon abilities
- `_FeedBoss_`, `_Feed_Initiate_` — cinematic feed-prep abilities that have
  no offensive use

**Existing installations**: the user's `ability_rules.json` is loaded as-is
without merging defaults. To pick up the new entries on an existing
installation, either (a) manually add the new patterns to the file's
`DenyPatterns` array, OR (b) delete `ability_rules.json` and let Beelzebub
regenerate it from defaults on next start (you'll lose any custom curation).

### Documentation: visual model change is hard-limited

V Rising exposes exactly five native shapeshift-form buffs as transformable
visuals (Wolf, Bear, Rat, Spider, Toad). Server-side mods cannot create new
shapeshift forms. So for any captured unit that isn't a wolf/bear/rat/
spider/toad, **the player's character model stays the same** even when
`FullReplace: true` or `Transform_NativeShapeshift_Enabled: true`. We
already documented this in the TX3 + Z2 notes; calling it out again here
because it surfaced in testing as a surprise.

Workaround for "cinematic boss form" servers: combine `FullReplace: true` +
`PrefabAbsolute` scaling mode + a strong `MovementSpeedScale` if you want
the transform to feel mechanically distinct even without a model change.

### About "missing" abilities

If a unit appears to have an ability in the boss fight that doesn't show
up on your transformed spell bar, it's almost certainly one of:

1. **A script-driven AI behavior** — the boss has phase-trigger logic in
   its behavior tree that fires abilities outside the `AbilityGroupSlotBuffer`.
   These are inaccessible to server-side mods. Examples: Solarus's pillar
   phase-shift, the Tailor's Gargoyle transformation, StoneBreaker's golem
   summons (the summon is on the bar; the *script* that calls it during the
   fight is separate).
2. **A filter rejection** — admin curation (`Enabled: false`,
   `TransformOnly: true`) or default DenyPatterns. Run
   `.beelz preview <unit>` to see what made it through the filter.
3. **A Hard-only variant on a Basic-mode server** without a Basic sibling
   — the Z3 carve-out lets these through on Basic mode, but for units
   where the Hard variant IS the only variant.

### Files touched

- `Commands/TransformCommands.cs` — `Current` and `Preview` commands added.
- `Services/AbilityRules.cs` — default DenyPatterns extended.
- csproj + thunderstore: 0.17.1 → 0.18.0
- Both changelogs.

---

## [0.17.1] - 2026-05-22

### Hotfix — transform actually applies its spell bar now

Symptom (caught in user testing of v0.17.0): `.beelz transform <unit>` returned
the "Transformed into X" success message and registered the ActiveTransform in
the registry, but the player's spell bar never changed and no abilities
transposed to the action bar.

Root cause: `DebugEventsSystem.ApplyBuff` is asynchronous — it queues an
`ApplyBuffDebugEvent` for the next system tick to process. The buff entity
doesn't exist on the current frame. My `TransformBuffService.Apply` called
`TryGetBuff` immediately after `ApplyBuff`, got back `false`, logged a warning
("carrier buff not found after ApplyBuff event"), and returned `false` —
**aborting the slot-override and stat-scale enrichment entirely**. But
`TransformService.TryActivate` had already set the `ActiveTransform` record
before calling `Apply`, so the player saw a "successful" transform with no
mechanical effect.

Fix: switch to `ServerGameManager.TryInstantiateBuffEntityImmediate(source,
target, prefabGuid, out buffEntity)`. This is the synchronous version —
creates the buff entity in the current frame and returns it directly. The
enrichment (ReplaceAbilityOnSlotBuff entries + ModifyUnitStatBuff_DOTS stats
+ BlockEquipmentSwapping component) now happens on the same buff entity that
will actually carry the transform. Pattern confirmed by Bloodcraft
`FamiliarBindingSystem.cs:795` + `Buffs.cs:91`.

### Note on the residual AbilityGroupSlot connect warnings

User testing showed five `Clearing entity X which is a modification source for
entry ... (ProjectM.AbilityGroupSlot)` warnings on player connect, on a player
who has 0 saved Beelzebub slot grants. Beelzebub's
`ReplaceAbilityOnSlotSystemPatch` returns early without adding to the buffer
in that case — we are not the modification source. Investigation suggests
these are vanilla V Rising re-evaluating the player's EquipBuff_Weapon
natural ability layout when the entity recycles on connect. Pre-Z1
versions had this AS WELL as a flood of additional warnings during gameplay;
Z1 killed the gameplay-time flood but cannot suppress the engine's connect-
time re-evaluation. Cosmetic, harmless, vanilla.

---

## [0.17.0] - 2026-05-22

### TX7 — Power-scaling mode (admin-selectable)

A long-standing design tension resolves: when a player casts a captured
ability, what determines the damage? Three modes, switchable globally or
per-transformation:

- **`CuratedScales`** (default — preserves v0.15.0 behavior): the four
  `*Scale` fields on each `TransformMap` entry are applied as
  `ModifyUnitStatBuff_DOTS` overlays. Admin-curated.
- **`PrefabAbsolute`** (new): reads the CHAR_ prefab's `UnitStats`
  component (PhysicalPower, SpellPower, PhysicalResistance, SpellResistance)
  + `Health.MaxHealth`, applies matching boss-tier values via
  `ModificationType.Add`. The player effectively becomes the boss
  statistically. "True boss form" mode. Ignores player progression.
- **`PlayerScaled`** (new): applies **nothing**. V Rising's vanilla damage
  formula uses the player's own `PhysicalPower`/`SpellPower` — which
  Bloodcraft modifies via its expertise/blood/level buffs. The captured
  ability auto-rescales with player progression and Bloodcraft prestige
  resets. The **Bloodcraft-prestige-compatible** mode.

Implementation:

- `Services/Categorization.cs` gains the `PowerScalingMode` enum.
- `Settings.Transform_PowerScalingMode` (string, default `"CuratedScales"`).
  Set in `BepInEx/config/kdpen.Beelzebub.cfg`.
- `RulesDto.TransformEntry.PowerScalingMode` (string, default null).
  Per-entry override; empty inherits global. Case-insensitive parsing.
- `AbilityRules.GetTransformPowerScalingMode(unitGuid)` resolves the
  effective mode (per-entry override > global config > default).
- `TransformBuffService.AttachStatScales` dispatches on the resolved mode:
  - `CuratedScales` → factored-out `AttachCuratedScales` (existing TX6 logic).
  - `PrefabAbsolute` → new `AttachPrefabAbsolute` (reads UnitStats + Health
    from the CHAR_ prefab via `PrefabCollectionSystem._PrefabLookupMap`).
  - `PlayerScaled` → no-op; returns 0.
- `AddStat` helper extended to accept an explicit `ModificationType`
  (was hard-coded to `MultiplyBaseAdd`; PrefabAbsolute needs `Add`).

### TX8 — Weapon-swap lock during FullReplace

Builds on the Bloodcraft-confirmed research finding that V Rising exposes a
native `BlockEquipmentSwapping` component. Adding it to a buff entity blocks
weapon swapping for the buff's duration. Auto-clears on buff destroy — no
manual cleanup.

- `TransformBuffService.Apply` now calls `AttachWeaponSwapLock(buffEntity)`
  when the transform's `FullReplace` flag is true. The player cannot swap to
  a different weapon mid-transform until they `.beelz revert` (or the timed
  transform expires).
- Defensive: the component-add is wrapped in try/catch with a warning
  log line — if the assembly ever drops `BlockEquipmentSwapping` we
  degrade to a no-op instead of crashing.

### BCH API additions

- `.beelz api transforms` — `[BEELZ:tx]` lines append
  `scaling_mode={CuratedScales|PrefabAbsolute|PlayerScaled}` (the resolved
  effective value).
- `.beelz api catalog units` — `[BEELZ:catalog-unit]` lines append
  `scaling_mode={<raw-value>|inherit}` so admins can see at a glance
  which entries override the global default and which inherit.

### Docs

- `Beelzebub/docs/ABILITY_MAP_FORMAT.md` — new "Power scaling modes" section
  with a per-mode breakdown, a "picking a mode" quick guide, and explicit
  notes on the Bloodcraft-prestige interaction. The `FullReplace` row
  updated to call out TX8 weapon-swap-lock behavior.

### Resolves the deferred TX3 items

The two items I deferred when shipping TX3 in v0.16.0 are now done:

1. **Auto-derive stats from CHAR_ prefab** — implemented as the
   `PrefabAbsolute` power-scaling mode. Admins opt in per-transformation
   or globally.
2. **Weapon-swap lock during FullReplace** — implemented via
   `BlockEquipmentSwapping` (TX8).

### Notes

- Crit chance/damage stats are not directly readable from the V Rising
  `UnitStats` wrapper (the field names aren't exposed cleanly in the
  IL2CPP types). PrefabAbsolute does NOT include those. Admins who want
  crit-tier scaling can layer it with CuratedScales-mode entries.
- `PlayerScaled` mode does nothing at the carrier-buff level — V Rising
  handles the scaling natively. The captured ability's BASE damage is
  still the boss's value (read from the AB_ prefab), only the multiplier
  comes from the player. A boss with very high base damage will still
  hit hard even at low player level; admins should curate
  `DamageScale < 1.0` in CuratedScales mode for the abilities that need
  taming.

---

## [0.16.0] - 2026-05-22

### TX3 — FullReplace transformation mode

Final TX-cluster feature. A per-transformation opt-in flag in
`TransformMap` that bundles the "be the NPC" cinematic — bypasses the
global native-shapeshift config so admins can light up the visual for
specific units (Wolf-flavored V-Bloods, etc.) without enabling it for
every transform on the server.

- `TransformEntry` gains `FullReplace` bool field (default `false`). Lives
  alongside the TX6 scale fields — together they form the full "boss form"
  curation surface (visual + stats + spell bar).
- `AbilityRules.IsTransformFullReplace(unitGuid)` getter.
- `NormalizeTransformMap` reads the flag with default `false`. Backward-
  compatible — existing `ability_rules.json` files load unchanged.
- `ShapeshiftService.Apply` gains a `forceEnabled` parameter (default
  `false`). When `true`, bypasses the
  `Transform_NativeShapeshift_Enabled` global config.
- `TransformService.TryActivate` reads the FullReplace flag from the
  TransformMap entry and passes `forceEnabled` accordingly. Players
  transforming into a FullReplace-marked unit see the native shapeshift
  even when the global config is off; non-FullReplace units still respect
  the global toggle.

### Known limit (V Rising vanilla, not fixable server-side)

Native shapeshift forms (Wolf/Bear/Rat/Spider/Toad) auto-exit when the
player casts any ability NOT in the form's baked-in moveset — which
captured boss abilities always are. So FullReplace gives you a 1-3
second cinematic moment of the model swap, then the visual drops on the
first spell. The carrier buff (Z1) keeps the spell bar AND the TX6 stat
scales intact across the visual-drop, so mechanically you still ARE the
unit — just the model reverts to your vampire.

If V Rising ever exposes a "block off-form cast exit" surface, FullReplace
will become a permanent-while-active mode for free. The flag is forward-
compatible: admins can curate it now, runtime catches up later.

### BCH API additions

- `.beelz api catalog units` — `[BEELZ:catalog-unit]` lines append
  `full_replace={0|1}`.
- `.beelz api transforms` — `[BEELZ:tx]` lines append `full_replace={0|1}`.

Backward-compatible append; older parsers ignore.

### Docs

- `Beelzebub/docs/ABILITY_MAP_FORMAT.md` — TransformMap table row added
  for `FullReplace` with the cinematic-mode caveat documented. Wolf
  example bumped to demonstrate the typical "Alpha Wolf with FullReplace +
  speed buff + slight damage nerf" curation.

### Notes / what's NOT in this release

- **Weapon-swap lock during FullReplace** — initially scoped, deferred.
  V Rising's vanilla doesn't appear to expose a clean "block equipment
  swap" buff category that Bloodcraft's exoform uses; investigation
  needed before adding. The visual-drop on first cast is already the
  dominant immersion-break, so weapon-swap-lock is lower-priority polish.
- **Reading the NPC's actual UnitStats** as a scale source. Original TX3
  scope mentioned "stat profile". Implemented as: admin curates
  DamageScale/HealthScale/etc. in the matrix. Auto-deriving scales from
  the CHAR_ prefab's stats would risk OP transforms on bosses like
  Dracula and is intentionally not done. Admins should curate.

### Backlog after this release

Per-cluster pending only:
- **W5** — per-ability scaling runtime. Still research-blocked
  (DealDamageEvent readonly fields).
- **IN2.captured_at** — schema migration deferred.
- **TX5-extended** — cross-unit boss-phase pairing (Tailor → Gargoyle).
  Optional polish; lower priority than testing the v0.14.0–v0.16.0
  stack in-game.

---

## [0.15.1] - 2026-05-22

### IN2 — Richer BCH data granularity (`ability_category` + `transform_type`)

Builds on v0.15.0's TX6-scale exposure. BCH now gets two derived badge fields
in addition to the raw prefab names: a category for each ability and a type
for each transform unit. Both come from pure name-substring classification —
no AbilityMap lookup, no per-player state, safe to call before init.

- New `Services/Categorization.cs`:
  - `AbilityCategory` enum: `Other`, `Travel`, `Ultimate`, `Aoe`,
    `Projectile`, `Summon`, `Buff`, `WeaponSpell`, `Spell`.
    Most-specific patterns match first (e.g. `_Ultimate_` always wins over
    `_Buff_` even though the ultimate is itself a self-buff).
  - `TransformType` enum: `Other`, `Vampire`, `Undead`, `Beast`,
    `Humanoid`, `Construct`, `Demon`. Demon-bosses (Dracula, Solarus, Adam,
    Manticore) override their broader-frame classifications.
  - Pure static class. No dependencies on Core / AbilityRules / EntityManager.
- BCH API endpoints now emit the derived fields. All additions are appended
  (backward-compatible — older BCH parsers ignore the new tokens):
  - `.beelz api list` — `[BEELZ:list]` lines append `cat=<AbilityCategory>
    type=<TransformType>`.
  - `.beelz api transforms` — `[BEELZ:tx]` lines append `type=<TransformType>`.
  - `.beelz api catalog units` — `[BEELZ:catalog-unit]` lines append
    `type=<TransformType>`.
  - `.beelz api catalog abilities` — `[BEELZ:catalog-ability]` lines append
    `cat=<AbilityCategory>`.

### Deferred from IN2

- `captured_at` per-capture timestamp. Would need a state.json schema
  migration on the persistence layer (`CapturedAbility` gains a
  `CapturedAtUtc` field, missing-on-load defaults to `DateTime.MinValue`).
  Skipped this round to keep the change purely additive on the API surface —
  no risk to the still-untested v0.14.0 / v0.15.0 captures. Revisit when the
  user requests "captured 3 days ago" UI affordances in BCH.

### Notes for admins

- The derived classifications are best-effort heuristics on prefab names.
  Genuinely odd / borderline cases (e.g. a "stance buff that's actually an
  Aoe trigger") fall into `Other`. Admins can override the visible category
  by editing the `Notes` field in `ability_rules.json` — BCH's UI typically
  shows the Notes near the category badge.

---

## [0.15.0] - 2026-05-22

### TX6 — Per-transformation power scaling

The Z1 carrier-buff refactor in 0.14.0 incidentally unblocked TX6 and TX3: a
`ModifyUnitStatBuff_DOTS` buffer attached to the carrier buff scales the
player's stats only for the duration of the transform, with no orphaned state
on revert. This release ships TX6 — four per-transformation balance dials in
`TransformMap`.

- `TransformEntry` gains four float fields, all defaulting to 1.0 (no change):
  - `DamageScale` — applied as `MultiplyBaseAdd` on both `UnitStatType.PhysicalPower`
    AND `UnitStatType.SpellPower`. `1.5` = +50% output, `0.5` = -50%.
  - `CooldownScale` — applied as `MultiplyBaseAdd` on both
    `UnitStatType.SpellCooldownRecoveryRate` AND
    `UnitStatType.WeaponCooldownRecoveryRate`, with the value transformed to
    `(1/scale) - 1` so `CooldownScale > 1.0` = slower recovery (longer
    cooldowns, admin nerf) and `< 1.0` = faster recovery.
  - `HealthScale` — applied as `MultiplyBaseAdd` on `UnitStatType.MaxHealth`.
  - `MovementSpeedScale` — applied as `MultiplyBaseAdd` on
    `UnitStatType.MovementSpeed`.
- `AbilityRules.NormalizeTransformMap` clamps non-positive scales to `1.0` on
  load (typo protection — admin can't accidentally zero a player's health).
- `AbilityRules.GetTransform{Damage,Cooldown,Health,MovementSpeed}Scale` +
  `HasTransformScales(unitGuid)` helpers added for the runtime and the API.
- `TransformBuffService.Apply` now takes an optional `unitPrefabGuid`
  parameter (default `0` = skip scaling — legacy callsites stay quiet).
  After attaching the slot-override entries, it calls a new
  `AttachStatScales(buffEntity, unitPrefabGuid)` private helper that:
  - Reads each scale from `AbilityRules`.
  - Skips fields equal to `1.0` (within float epsilon) so the buff stays slim.
  - Appends `ModifyUnitStatBuff_DOTS` entries to the buff entity with
    `ModificationType.MultiplyBaseAdd` and `Modifier = 1`. Each entry's `Id`
    comes from `ModificationIDs.Create().NewModificationId()` so V Rising
    tracks them per-buff for clean teardown.
- `TransformService.TryActivate` passes `unitPrefabGuid` through to the
  carrier-buff apply call.
- Verbose log line in `TransformBuffService.Apply` now reports
  `stats=<count>` alongside `abilities=<count>` so admins can confirm the
  scaling attached without enabling per-modifier tracing.
- `Beelzebub/docs/ABILITY_MAP_FORMAT.md` — TransformMap section rewritten
  with the new fields, a curated example showing a buff-and-nerf Dracula
  config (DamageScale 0.5, CooldownScale 1.5, HealthScale 0.7), and stat-scale
  notes explaining clamp behavior, the carrier-buff teardown invariant, and
  why `Tier` doesn't auto-derive scales.

### IN2 (partial) — Scales exposed via BCH API

- `.beelz api transforms` (per-player unlock list) now appends
  `damage_scale=… cooldown_scale=… health_scale=… speed_scale=…` to every
  `[BEELZ:tx]` line. BCH can render a "Power Profile" hover on each transform
  in a single round-trip.
- `.beelz api catalog units` (catalog endpoint) appends the same four scale
  fields to each `[BEELZ:catalog-unit]` line. Format change but
  backward-compatible — new fields are appended, not interleaved, so existing
  BCH parsers ignore them harmlessly.

### IN1 — Bloodcraft coexistence audit

- New `Beelzebub/docs/INTEROP_BLOODCRAFT.md`. Pure documentation
  deliverable; no code changes. Tested against Bloodcraft v1.13.21 and
  Beelzebub v0.15.0.
- Documents the four shared Harmony patch surfaces
  (`DeathEventListenerSystem`, `ReplaceAbilityOnSlotSystem`,
  `VBloodSystem`, `GameDataInitialized`), confirms no structural conflict,
  spells out the one slot-3 contest and the two Bloodcraft config flags
  (`ShiftSlot`, `UnarmedSlots`) that admins can flip for the cleanest
  parallel install. Calls out the Beelzebub v0.14.0+ carrier-buff priority
  (99) that masks Bloodcraft class spells during a `.beelz transform`.
- Stat-scale interaction note: Beelzebub TX6 scaling stacks additively
  with Bloodcraft expertise/blood bonuses via V Rising's stat resolver.
  Servers running both mods should tune Beelzebub `DamageScale` values
  down a notch to compensate.

### IN5 — Cross-mod setup guide

- New `Beelzebub/docs/SETUP_GUIDE.md`. Pure docs.
- End-to-end install order (BepInEx → VCF → Beelzebub), first-run
  verification checklist, config tour grouped by section, four common
  admin recipes (low-rate "rare collection", high-rate "social PvE",
  Brutal-balance with TX6 scales, transform-free), optional Bloodcraft +
  BCH pairing notes pointing at the audit doc, troubleshooting section
  covering the seven most common failure modes.

### Notes

- Scale logic only activates while transformed — the `.beelz grant` path
  (slot binds while NOT transformed) is unaffected. That path is W5 scope
  and still research-blocked.
- TX3 (FullReplace mode) is still pending but no longer architecturally
  blocked — a FullReplace transform would just be a TX6 transform with an
  extreme scaling profile and possibly the native shapeshift visual on.
- The existing curated `ability_map.curated.json` has no TX6 fields yet —
  default `1.0` for every transform until admins curate. The Dracula
  example in the docs is illustrative only.
- Docs land in the source repo (`Beelzebub/docs/`) — they don't ship in
  the Thunderstore zip (`BuildToDist` MSBuild target only stages
  `Beelzebub.dll` + `CHANGELOG.md`). Players reach them via the GitHub
  repo link in the README.

---

## [0.14.0] - 2026-05-22

In-game testing of 0.13.0 surfaced three symptoms with one shared root cause: (a)
casting any captured ability while transformed dropped the native shapeshift form,
(b) `.beelz revert` restored weapon abilities but left spell-book slots 5/6 empty,
(c) the server console flooded with `Clearing entity X which is a modification
source for entry … (ProjectM.AbilityGroupSlot)` warnings during weapon swaps.
Diagnosis: pre-Z1 we mutated `EquipBuff_Weapon_*`'s `ReplaceAbilityOnSlotBuff` and
called `ServerGameManager.ModifyAbilityGroupOnSlot` directly. That registered the
EquipBuff as a modification source (the warning), and calling Modify with
`PrefabGUID.Empty` on revert pinned the spell-book slots to literal empty rather
than restoring the player's chosen spells. This release replaces that approach
with Bloodcraft's exoform pattern: a single carrier buff owns all the overrides
and V Rising resolves the rest naturally on revert.

### Z1 — Carrier-buff refactor for transform spell bar

- New `Services/TransformBuffService.cs`: applies V Rising's native carrier
  prefab `Buff_VBlood_Ability_Replace` (GUID 1171608023) to the player via
  `DebugEventsSystem.ApplyBuff`, then enriches the resulting buff entity with
  a `ReplaceAbilityOnSlotBuff` buffer (`Target = ReplaceAbilityTarget.BuffTarget`,
  `Priority = 99`, `CastBlockType = WholeCast`) carrying the transform's slot
  overrides. Lifetime mirrors the transform mode — Timed gets an explicit
  `LifeTime.Duration`, Toggle gets `Duration = -1, EndAction = None`.
  `RemoveOnDisconnect` is OR'd into BuffCategory so disconnects clean up
  automatically.
- `Apply`, `Reapply` (used by `.beelz phase` to swap loadouts without
  dropping the buff), and `Remove` (destroys the carrier via
  `DestroyUtility.Destroy`, then forces `ReplaceAbilityOnSlotSystem.OnUpdate()`).
- `ActiveTransform.StashedOverrides` is gone — no more capture-and-restore
  dance. When the carrier buff is destroyed, V Rising re-resolves slots
  from the natural sources (weapon naturals + player spell-book) on its
  own. Slots 5/6 return to the player's chosen spells without any further
  work from us.
- `SlotApply.ApplyTransform` / `SlotApply.ApplyRevert` removed (transforms
  no longer touch the EquipBuff_Weapon's buffer at all).
- `SlotApply.ApplyGrant` / `ClearGrant`: dropped the
  `ServerGameManager.ModifyAbilityGroupOnSlot` call that was registering
  EquipBuff entities as modification sources. We now just mutate the
  buffer and trigger `ReplaceAbilityOnSlotSystem.OnUpdate()` to apply
  this frame.
- `ReplaceAbilityOnSlotSystemPatch`: the active-transform injection
  branch is gone. The carrier buff carries the overrides; we no longer
  need to stamp transform abilities into every EquipBuff that V Rising
  rebuilds on weapon swap. Saved-grant injection (W2) is still active
  but is skipped while transformed (those grants would just be replaced
  by the higher-priority carrier-buff entries).
- `Core.ReplaceAbilityOnSlotSystem`: new system reference initialized in
  `InitializeAfterLoaded` so services can force-tick the system to apply
  buffer changes within the same frame.
- New `Entity.With<T>` extension in `EntityExtensions.cs` for in-place
  ref-mutate of unmanaged components (Bloodcraft VExtensions parity).

### Z2 — Native shapeshift VFX is opt-in (default off)

- New config: `Transformation.Transform_NativeShapeshift_Enabled` (default `false`).
- V Rising's native shapeshift forms (Wolf, Bear, Rat, Spider, Toad)
  auto-exit when the player casts any ability not in the form's baked-in
  moveset — which Beelzebub's captured abilities always are. The visual
  form would drop on the first cast, while the spell bar (now on the
  carrier buff) would persist. To avoid that mismatch, the heuristic
  shapeshift application from A4 is now gated on this flag. Default off:
  player keeps their vampire model and the captured spell bar.
- Admins who want the cosmetic on wolf/bear-flavored transforms can set
  the flag true. The transform activation message warns that the form
  will break on first cast.

### Z3 — Hard-only ability fallback on Basic-mode servers

- Some V-Bloods carry only a `_Hard_` variant of a given ability and have
  no Basic sibling on their prefab (e.g. Bishop of Death has
  `AB_Undead_BishopOfDeath_ChainBolt_Hard_AbilityGroup` but no
  `AB_Undead_BishopOfDeath_ChainBolt_AbilityGroup`). On a Basic-mode
  server, TX4's `_Hard_` filter previously dropped those entirely,
  leaving the player with one fewer ability than the boss naturally
  carries (4 vs 5 for Bishop of Death).
- `TransformService.GetTransformAbilities` now pre-passes the unit's
  ability list to build a name-set of Basic-tier abilities present on
  the prefab. Each `_Hard_` ability gets a sibling-name lookup — if the
  Basic sibling is absent, the Hard variant is allowed through even on
  Basic mode (V Rising uses the same ability on the boss itself).

### Z4 — Bare `.beelz` short-help command

- New `Commands/RootCommands.cs`: top-level `[Command("beelz")]` outside
  the `[CommandGroup("beelz")]` so VCF resolves bare `.beelz` to this
  method while subcommands (`.beelz help`, `.beelz list`, …) still hit
  `BeelzCommands`. Prints a five-line overview pointing at `.beelz help`,
  `.beelz commands`, and the Thunderstore page.

### Known limitations carried forward

- TX3 (FullReplace) and TX6 (per-transform power scaling) still depend
  on the per-cast stat-buff infra blocked by `DealDamageEvent` readonly
  fields. Z1's carrier buff is the right host for that future work —
  `ModifyUnitStatBuff_DOTS` entries on the carrier buff would scale
  damage/cooldown without touching DealDamageEvent at all.
- W5 (per-cast scaling runtime) is still research-blocked for the same
  reason. Telemetry-only patch from 0.7.2 still ships.

---

## [0.13.0] - Unreleased

### TX5 — Multi-phase transformation handling

- `AbilityEntry.Phase` (int, default 1) added to `RulesDto.AbilityMap`.
  Default ensures backward compatibility — no curated phase = phase 1.
- `NormalizeAbilityMap` clamps `Phase <= 0` to 1 (treats malformed input
  as phase 1).
- `AbilityRules.GetAbilityPhase(name)` reads the matrix entry; defaults
  to 1 if no entry exists.
- `ActiveTransform.CurrentPhase` (int, runtime-only, default 1) tracks
  which phase the player is currently in. Set to 1 on transform start;
  switched via `.beelz phase <n>`.
- `TransformService.GetTransformAbilities(unitGuid, phase=1)` accepts an
  optional phase argument. The internal AbilityGroupSlotBuffer walk now
  filters by `Core.AbilityRules.GetAbilityPhase(name) == phase`. Default
  phase=1 keeps the prior call site unchanged.
- `TransformService.GetAvailablePhases(unitGuid)` returns the set of
  distinct phase values defined for the unit's natural abilities. Always
  includes phase 1.
- New `Commands/TransformCommands.Phase` chat command:
  - `.beelz phase` (no args) — display current phase + available phases
    for the active transform. Friendly hint when the unit only has 1
    phase curated.
  - `.beelz phase <n>` — validate that phase n is available, refetch the
    unit's abilities filtered to that phase, then call
    `SlotApply.ApplyTransform(character, abilities, out _)` to swap the
    bar in-place (no weapon swap needed, courtesy of A2).
  - Stash from the original transform activation is preserved — phase
    shifts don't disturb the player's pre-transform slot loadout, so
    `.beelz revert` still restores cleanly.
- `Commands/ApiCommands.Active` reply line now includes `phase=<current>
  phases=<csv>` so BCH can render a phase selector UI per active transform.
- New BCH event line `[BEELZ:event] type=transform-phase-shift u=<guid>
  un=<name> phase=<n>` published on each successful phase swap.

### Design caveats

- Phase swaps happen in-fight at the player's command — there's no
  automatic health-threshold trigger yet. That's a TX5-follow-up if
  desired; the chat command is enough for the user's "let players
  manually trigger phase transitions" vision.
- Phase 2+ abilities still need to be discoverable in the unit's natural
  `AbilityGroupSlotBuffer` — if the prefab doesn't list them (e.g. the
  boss spawns a child entity with a separate ability set in phase 2),
  Beelzebub can't see them. For now, multi-phase only works for units
  whose phase-2 abilities are present on the same V-Blood prefab. Most
  V-Bloods qualify; a few (Tailor → Gargoyle, Morgana's full transformation)
  spawn separate entities and would need explicit unit-pairing curation
  (future TX5-extended work).

---

## [0.12.0] - Unreleased

### IN3 — Catalog endpoints for BCH collection book

- `Commands/ApiCommands` gains three new endpoints under `[CommandGroup("beelz api")]`:
  - `catalog` (no args) — emits `[BEELZ:catalog-summary] abilities=<N>
    units=<M> server_mode=<X>`. One-line BCH probe to size its UI lists.
  - `catalog units [page]` — streams `[BEELZ:catalog-unit]` lines for the
    TransformMap, one per entry. Each line carries `un=`, `enabled=`,
    `difficulty=`, `tier=`, `notes=`. Paginated 40-per-page; default page 0.
    Terminator `[BEELZ:end] cmd=catalog-units count=<N> total=<M> page=<P>
    pages=<Q>`.
  - `catalog abilities [page]` — streams `[BEELZ:catalog-ability]` lines
    for the AbilityMap. Each line carries `an=`, `weapons=`, `forms=`,
    `transform_only=`, `enabled=`, `difficulty=`, `damage_scale=`,
    `cooldown_scale=`, `notes=`. Same pagination + terminator.
- Pagination implementation: in-memory `Skip(page * 40).Take(40)` over an
  `OrderBy(name, OrdinalIgnoreCase)` to keep order stable across pages and
  matrix reloads. Page 0 is the first page (mirroring BCH's zero-indexed
  list conventions).
- All values pass through the existing `SafeToken` escape so multi-word
  `Notes` don't break the BCH key=value parser.

### IN4 — Progress endpoint completion (denominator from curated TransformMap)

- `Commands/ApiCommands.EmitProgress` and `BeelzCommands.ShowProgress` now
  read the transform total from `Core.AbilityRules.Current.TransformMap.Count`
  instead of the hardcoded 61 placeholder from 0.9.0. Falls back to 61 if
  the matrix is empty (defensive).
- Chat-mode `.beelz progress` + `.beelz admin progress <player>` updated
  to match — the % shown now uses the curated denominator (64 in the
  current curated file).

### Notes for BCH integration

- Catalog endpoints are idempotent reads — BCH can call them at session
  start and cache. Re-poll on `.beelz admin reload` event (BCH listens for
  the existing `[BEELZ:event]` stream which already fires `type=reload`).
- 443 abilities × 1 line each = ~12 chat pages at 40-per-page. BCH should
  drive a small loop iterating until `count < pageSize`.

---

## [0.11.0] - Unreleased

### TX4 — Brutal-vs-Basic difficulty enforcement

- New `Config/Settings.Server_DifficultyMode` (string, default `"Basic"`).
  Bound to BepInEx config section `Server`. Admin sets to match their
  V Rising server's actual difficulty preset. No runtime auto-detection —
  the V Rising `ServerGameSettings` exposes `GameModeType` (PvP/PvE) but
  not the difficulty enum in a clean public surface, and Bloodcraft
  confirms no one uses it for this gating. Explicit admin opt-in is more
  reliable.
- `Services/AbilityRules` gains:
  - `AbilityEntry.Difficulty` field (string, default `"Basic"`) — mirrors
    the field already on `TransformEntry` since TX1.
  - `static GetServerDifficulty()` — reads the config + normalizes to
    `"Basic"` / `"Brutal"`. Static so non-instance callers can hit it.
  - `GetAbilityDifficulty(name)` — three-tier resolution:
    1. AbilityMap entry's explicit `Difficulty`.
    2. Name-substring fallback: anything containing `_Hard_` defaults to
       `Brutal` (V Rising naming convention for brutal-mode-only variants).
    3. Default `Basic`.
  - `static IsDifficultyAllowed(entry, server)` — Basic entries always
    pass; Brutal entries only pass on Brutal servers.
  - `NormalizeAbilityMap` defaults the new `Difficulty` field to `"Basic"`
    when JSON is missing it.
- Runtime gating wired in:
  - `Services/AbilityFilter.ShouldCapture` — after the existing
    `Enabled` check, refuses Brutal-tagged abilities on a Basic server
    with reason `"ability difficulty Brutal not allowed on Basic server"`.
  - `Services/TransformService.TryActivate` — after the existing
    `IsTransformUnitEnabled` gate, refuses Brutal-tagged transformations
    with a chat-friendly message naming the unit + the mismatch.
  - `Patches/DeathEventListenerSystemPatch` — early-return on the
    transform-unlock roll if the unit's transform difficulty doesn't
    match the server mode. Stacks with the existing `IsTransformUnitEnabled`
    gate.
  - `Patches/VBloodSystemPatch` — same gate inline with the V-Blood
    transform-unlock chance check.
- `Commands/AdminCommands` adds `.beelz admin difficulty [basic|brutal]`.
  No argument = show current mode + summary of effect. With argument =
  set the config value (works mid-session, no server restart needed).
- `Commands/ApiCommands.Info` emits `difficulty=Basic|Brutal` on every
  `[BEELZ:info]` reply so BCH renders the difficulty badge in tooltips.

### Heuristic auto-tagging

- Abilities with `_Hard_` in the prefab name are auto-classified as
  Brutal in the fallback path. This means **admins running a Basic
  server get Brutal-only abilities filtered out automatically** without
  having to curate every Brutal entry by hand.
- The admin can override per-ability via `AbilityMap[name].Difficulty:
  "Basic"` if they specifically want a `_Hard_`-named ability to be
  Basic-available.

### Backwards compatibility

- No state.json schema bump. AbilityMap's `Difficulty` field is read with
  the C# auto-property default `"Basic"` when the JSON omits it, so
  existing curated files continue to work — just default to Basic for
  every ability until an admin curates.

---

## [0.10.1] - Unreleased

### TX2 — TransformMap curated for all V-Bloods

- `Beelzebub/docs/ability_map.curated.json` updated in-place to add 64
  TransformMap entries — every V-Blood from the prefab dump plus the
  non-V-Blood transform-source `CHAR_Undead_Leader_VBlood`. Curation
  approach mirrors `ability_map.curated.json`'s W1 work: enumerate, tier,
  annotate.
- Tier assignments (1=early Farbane through 5=endgame Dracula/Blackfang)
  derived from typical V Rising gear-progression order. Approximate —
  admin can edit. Distribution: 7+9+14+24+10 across tiers 1-5.
- `Difficulty` all set to `"Basic"`; the Brutal-mode gate is TX4 work and
  will refine this field once enforcement lands.
- All entries shipped with `Enabled: true`; admins flip to false for
  surgical disabling of broken or undesired transforms.
- Notes field carries admin context: V Rising boss name, weapon family of
  the unit's natural moveset, and a few TX5 / TX6 prompts (e.g.
  Geomancer's phase shift, Beatrice's Gargoyle phase-2 glitch, Iva's
  weapon-swap loadouts).
- No C# code changes — pure data refresh. Version bumped to 0.10.1 so the
  user's deployed DLL has a clean version marker matching the curated
  data file.

### How to roll it out
- Stop server.
- Copy `Beelzebub/docs/ability_map.curated.json` to
  `BepInEx/config/kdpen.Beelzebub/ability_rules.json` (overwrite).
- Start server. Or in a live session: drop the file in, then
  `.beelz admin reload`.

---

## [0.10.0] - Unreleased

### TX1 — TransformMap matrix schema

- New `TransformMap` section in `RulesDto`, keyed by CHAR_* prefab name
  (case-insensitive dictionary). Each entry is a `TransformEntry` with:
  - `Enabled` (bool, default true)
  - `Difficulty` (string "Basic" or "Brutal", default "Basic")
  - `Tier` (int 1-5, default 1)
  - `Notes` (string)
- `AbilityRules.NormalizeTransformMap` rebuilds entries with safe defaults
  (positive Tier, non-null fields) the same way `NormalizeAbilityMap` does.
- New query helpers:
  - `IsTransformUnitEnabled(int unitPrefabGuid)` — resolves the GUID to a
    prefab name then dict lookup. Default true when no entry exists, so
    unknown units pass through.
  - `GetTransformDifficulty(int)` / `GetTransformTier(int)` /
    `GetTransformNotes(int)` — defaults to "Basic" / 1 / null when absent.
- Runtime wiring:
  - `TransformService.TryActivate` adds the `IsTransformUnitEnabled` check
    after the existing unlock check; rejects with a chat-friendly message
    "Transformation into &lt;X&gt; is currently disabled by the server admin."
  - `Patches/DeathEventListenerSystemPatch.Process` early-returns the
    transform-unlock roll path when the unit is disabled.
  - `Patches/VBloodSystemPatch.Prefix` adds the same gate to the V-Blood
    transform-unlock roll inline with the existing chance check.
- `.beelz api transforms` reply lines now include the matrix attributes:
  `enabled=<0|1> difficulty=<Basic|Brutal> tier=<n>`.
- Documentation:
  - `docs/ABILITY_MAP_FORMAT.md` gets a "transformation matrix" section
    explaining the new fields with an example.
  - `docs/ability_map.curated.json` seeded with two example TransformMap
    entries (Dracula / Forest Wolf) showing tier + difficulty patterns.
    TX2 will curate the full V-Blood set.

### Backwards compatibility
- No state.json schema bump — `TransformMap` lives in `ability_rules.json`
  alongside the existing `AbilityMap`. Existing rules files without the
  field load fine (`NormalizeTransformMap` handles null input).

---

## [0.9.0] - Unreleased

### AT-series — admin tooling + collection progress

- **AT1**: `.beelz admin inspect <player>` (`Commands/AdminCommands.cs:Inspect`)
  resolves player by name via `EntityExtensions.FindCharacterByName`, then
  emits a multi-section chat reply: captures by source, slot bind counts
  (universal + per-weapon buckets via `AllWeaponSlots`), transform unlocks,
  active transform, hotkey count, verbosity, BCH-events flag.
- **AT2**: `.beelz admin revoke <player> <unitGuid> <abilityGuid> [reason]`
  routes to `AbilityRegistry.Forget`. Optional reason string defaults to
  `"admin"` and shows in audit log.
- **AT3**: `.beelz admin revoke-transform <player> <unitGuid> [reason]`.
  Detects "player is currently transformed into the unit being revoked"
  case and calls `Transforms.Revert(steamId, "admin revoke")` first to
  clean up active state before pulling the unlock.
- **AT4**: `.beelz admin force-transform <player> <unitGuid>` ensures the
  unlock exists (`AddTransformUnlock`), clears any cooldown
  (`SetCooldownUntil(..., DateTime.MinValue)`), then defers to the regular
  `Transforms.TryActivate` so slot/visual application reuses the same code
  paths. `.beelz admin clear-transform <player>` calls `Transforms.Revert`.
- **AT5**: `.beelz progress` (in `BeelzCommands`) + `.beelz admin progress
  <player>` (in `AdminCommands`) share the internal helper
  `BeelzCommands.ShowProgress(ctx, steamId, subjectLabel)`. The helper
  reports captures/total (denominator = `AbilityMap.Count`), transforms/61
  (placeholder until TX1 ships a curated TransformMap), V-Blood vs Regular
  breakdowns, and bind counts.
- **Audit logging helper** `AdminCommands.Audit(ctx, action, targetSteamId,
  targetName, detail)` writes `[Beelz AUDIT] admin=<sid> action=<name>
  target=<sid> (<name>) <detail>` to `BepInEx\LogOutput.log`. Format is
  grep-friendly for log auditing tools.

### Partial IN4 — BCH-facing progress
- `.beelz api progress` emits a `[BEELZ:progress] ...` line with
  abilities_captured/total/pct, transforms_unlocked/total/pct,
  vblood_abilities, vblood_transforms. Same data the chat reply uses, in
  the wire format. Counts toward IN4 (collection-tracking endpoint).
- Full IN4 will add a streaming `[BEELZ:catalog-*]` endpoint pair (IN3)
  so BCH can render the "what's possible" master list — not in 0.9.0.

### Help / commands updates
- `.beelz help` now mentions `.beelz progress`.
- `.beelz commands` cheat-sheet adds the new admin commands and the
  `progress` items.
- `.beelz api …` line includes `progress`.

### Behavioural notes
- No state.json schema bump — all reads, no new persisted fields.
- All admin grants/revokes use the existing `Forget` / `Add*` helpers, so
  semantics match the player-facing `.beelz forget` and the legacy `.beelz
  admin give`. Just adds the reverse direction + audit log.

---

## [0.8.0] - Unreleased

### W4 — Named hotkey bindings (server-side scaffold)

- `AbilityRegistry._hotkeys` (`Dict<ulong, Dict<string, int>>`) stores
  name → ability GUID bindings per player. Names case-insensitive and trimmed.
- New API methods on `AbilityRegistry`:
  - `SetHotkey`, `ClearHotkey`, `GetHotkey`, `HotkeyCount`, `ListHotkeys`
  - `HotkeysSnapshot` / `LoadHotkeysSnapshot` for persistence.
  - `Clear(steamId)` also wipes the player's hotkeys.
- Persistence `state.json` bumped from v5 → **v6**. New `PlayerDto.Hotkeys`
  field (`Dictionary<string, int>`). v5 saves load fine — Hotkeys field
  absent means empty bindings.
- New `Config/Settings`:
  - `Hotkeys_Enabled` (bool, default true) — gates `.beelz hotkey` commands
    and the API.
  - `Hotkeys_MaxPerPlayer` (int, default 5) — per-player binding cap.
- New `Commands/HotkeyCommands.cs` registers `[CommandGroup("beelz hotkey")]`:
  - `set <name> <index>` — bind with all the same gates the slot grant
    pipeline uses (Enabled / TransformOnly checks).
  - `clear <name>` — remove a binding.
  - `list` — show bindings + cap.
- New `.beelz api hotkeys` endpoint streams:
  - `[BEELZ:hotkeys-config] enabled=<0|1> max=<N>` header.
  - `[BEELZ:hotkey] name=<n> a=<guid> an=<prefab>` per binding.
  - `[BEELZ:end] cmd=hotkeys count=<N>` terminator.
- `.beelz commands` cheat-sheet and `.beelz api …` line updated to mention
  the new commands.
- BCH event emissions: `type=hotkey-set` and `type=hotkey-cleared`
  alongside the existing `[BEELZ:event]` chat-API stream.

### Limitations / what's NOT in 0.8.0

- **No cast-trigger mechanism.** The server stores bindings + exposes them
  to BCH, but pressing a BCH button doesn't fire an ability yet. That's
  the BCH client-side mod's job, plus a server-side resolver that BCH
  will call once its UI is ready. Tracked separately when BCH integration
  starts.
- No interaction with the active transform's spell bar — hotkeys are
  independent of slots 1-6 and the transform pickup.

---

## [0.7.2] - Unreleased

### A1-full reframed — BCH API exposes the AbilityMap matrix as tooltip data

- V Rising's localization JSONs ship on the **client only** — the dedicated
  server has none of the text data we'd need for a "real" LocalizationManager
  read. The Stunlock.Localization.dll interop is on the server, but without
  source strings it just returns blanks/keys.
- Pivoted A1-full: surface the admin-curated `AbilityMap[name].Notes` as the
  tooltip description. The user is already curating these for weapon/form
  classification — same field, doubled use.
- `Commands/ApiCommands.cs:Info` extended. The `[BEELZ:info]` reply now
  includes:
  - `desc=<Notes>` — admin annotation, with whitespace → `_` and `=` → `-`
    sanitization so values stay one bare token. Fallback to "Captured from
    <unit>." when no curated notes exist.
  - `weapons=Sword,GreatSword` — comma-joined `WeaponFamily.ToString()`
    list from `AbilityRules.ClassifyWeaponFamilies`.
  - `forms=any` (when empty) or `forms=Wolf,Bear` — `GetFormRestriction`
    result.
  - `transform_only=0|1`, `enabled=0|1` — admin gates as integers.
  - `damage_scale=1.00`, `cooldown_scale=1.00` — float with two decimals.
- `Commands/ApiCommands.cs:Slots` updated for W3:
  - Emits universal bucket first with `bucket=any`, then each weapon
    bucket with `bucket=<WeaponFamily>`.
  - Trailing `[BEELZ:slot-current] weapon=<X>` line so BCH knows which
    bucket is currently active for the player.
- New helper `SafeToken(string)` on ApiCommands handles the wire-format
  value escaping (whitespace → `_`, `=` → `-`).

### Why not real LocalizationManager

- Stunlock's localized strings are in client-only StreamingAssets that the
  dedicated server install lacks. Even if we wired
  `LocalizationManager.GetLocalizedString(key)`, it would return nothing
  useful server-side.
- The admin-curated `Notes` route is strictly better in practice: it's
  human-written, server-known, and the admin controls the wording.
- A1-full task closes here. If someone later finds a way to ship V Rising
  text data on the server side, we can layer it in as a fallback.

---

## [0.7.1] - Unreleased

### W5 attempt — telemetry hook landed, damage-mutation blocked

- New `Patches/DealDamageSystemPatch.cs`. Prefix on `DealDamageSystem.OnUpdate`
  that iterates `__instance._Query`, finds damage events where:
  1. `SpellSource` has a player owner (`EntityOwner.Owner.IsPlayer()`).
  2. The source's prefab name (e.g. `AB_*_Cast`, `AB_*_Projectile`) is a
     stem-prefix-match against one of the player's Beelzebub-granted
     abilities (universal + per-weapon buckets, stripped of `_AbilityGroup`/
     `_Group` suffix).
- Gated behind `Settings.VerboseLogging` — silent in normal operation.
- When matched, logs: `[Beelz] dmg-event player=<sid> src=<name>
  matched-grant=<name> damage-scale=<f> (telemetry only — scaling not yet applied)`.

### Why damage mutation didn't ship in this version

- DealDamageEvent's IL2CPP interop wrapper marks every field readonly
  (SpellSource, Target, MainType, MainFactor, ResourceModifier, Modifier,
  MaterialModifiers). The standard `entity.With((ref T t) => t.F = x)`
  pattern fails compile because we can't assign to readonly fields even
  inside the ref delegate.
- Bloodcraft's commented-out `DealDamageSystemPatch` (in
  `Patches/StatChangeSystemPatch.cs`, line ~533+) confirms the constraint —
  they only ever destroy event entities (`EntityManager.DestroyEntity`) but
  never modify values.
- The realistic next-session path is the Bloodcraft class-buff pattern:
  apply a `ModifyUnitStatBuff_DOTS` (SpellPower / PhysicalPower modifier)
  to the player ON cast of a Beelzebub-granted ability, scaled by the
  ability's `DamageScale`. Remove the buff after the cast resolves. Same
  approach for `CooldownScale` via the cast-cooldown pipeline.

### Task #48 deferred (not closed)

- W5 remains in_progress. The schema fields `DamageScale` and `CooldownScale`
  on AbilityEntry continue to be inert (read but not yet wired to runtime).
  Admins can curate values now; the runtime catches up later.

---

## [0.7.0] - Unreleased

### W3 — Per-weapon slot loadouts + `.beelz weapon-grant` command surface

- `AbilityRegistry` now stores two parallel slot buckets per player:
  - **Universal bucket** (`_slotAssignments`) — flat `Dict<int, int>`. Set
    via `.beelz grant`. Activates on any weapon subject to per-ability
    `IsGrantCompatible` checks. Backward-compatible storage for existing
    saves.
  - **Weapon-specific bucket** (`_weaponSlots`) — `Dict<WeaponFamily, Dict<int, int>>`.
    Set via `.beelz weapon-grant`. Wins over universal when the wielded
    weapon family matches.
- New `AbilityRegistry` API:
  - `SetSlot(steamId, WeaponFamily, slot, abilityGuid)` — write to either
    bucket. `WeaponFamily.None` / `Magic` routes to universal; concrete
    families go to the weapon bucket.
  - `ClearSlot(steamId, WeaponFamily, slot)` — same.
  - `GetSlotsResolved(steamId, currentWeapon)` — merges universal + the
    requested weapon's bucket. Weapon entries overwrite universal entries
    on conflicting slots.
  - `GetWeaponSlots(steamId, weapon)` — raw bucket lookup.
  - `AllWeaponSlots(steamId)` — full per-weapon snapshot for `.beelz list`.
  - `WeaponSlotsSnapshot` / `LoadWeaponSlotsSnapshot` — persistence hooks.
- `ReplaceAbilityOnSlotSystemPatch.ProcessEvent` swapped `GetSlots` →
  `GetSlotsResolved(steamId, currentWeapon)` so the live-injected slot
  map reflects both buckets.
- `SlotApply.ApplyRevert` and `SlotApply.GetCurrentWeapon` updated to use
  the resolved map.
- Persistence schema bumped to v5. `PlayerDto.WeaponSlots` is a new
  `Dictionary<string, Dictionary<string, int>>` (outer key = WeaponFamily
  enum name, inner key = slot string). Forward-compat: v4 saves with no
  WeaponSlots field load fine (universal bucket only). v5 saves are not
  back-compatible with 0.6.0 (which would ignore the new field — also
  fine, just no weapon-specific loadouts).
- New commands in `BeelzCommands`:
  - `.beelz weapon-grant <weapon|auto> <slot> <i>` — parses weapon string
    (case-insensitive Enum.TryParse against WeaponFamily, with `auto`
    resolving via `SlotApply.GetCurrentWeapon`). Enforces Enabled /
    TransformOnly gates. Applies in-place via SlotApply if currently
    wielding the matching family; otherwise just persists.
  - `.beelz weapon-unslot <weapon|auto> <slot>` — clears a weapon-specific
    bind; clears the live slot if currently wielding that weapon.
- `.beelz list` rewritten:
  - Shows "Universal slots (any weapon):" if any universal binds exist.
  - Adds one line per weapon family with binds, e.g. "Sword slots: [1] X,
    [2] Y".
  - Footer: "Currently wielding: <weapon>" so the player can correlate
    which loadout is active.
- `.beelz help` walkthrough + `.beelz commands` cheat-sheet updated to
  mention the new commands.
- BCH API events: new `[BEELZ:event] type=weapon-slot-granted ...` and
  `type=weapon-slot-cleared ...` lines on the same chat-API surface.

### Behavioral notes

- Players upgrading from 0.6.0 keep their existing grants — those flow
  into the universal bucket on first load. They can re-grant to
  weapon-specific buckets at any time without losing the universal binds.
- The C1 preset system still snapshots only the universal bucket. Per-
  weapon presets are a future enhancement.

---

## [0.6.0] - Unreleased

### W2 — Layered slot precedence (transform > weapon-grant > universal > V Rising natural)

- New `SlotApply.DetectFamily(equipBuffName)` parses the player's currently-active
  `EquipBuff_Weapon_*` buff into a `WeaponFamily`. Handles the special cases
  (`EquipBuff_Weapon_Unarmed_Start01` → Unarmed, `EquipBuff_Weapon_FishingPole_Base`
  → FishingPole) and the more-specific-first ordering (GreatSword before Sword,
  DualHammers before Mace, etc.).
- New `SlotApply.IsGrantCompatible(abilityGuid, weapon)` is the central
  compatibility check. Resolves through `AbilityRules`:
  - `IsEnabled(name, guid)` false → not compatible.
  - `IsTransformOnly(name, guid)` true → not compatible (transform path bypasses).
  - `ClassifyWeaponFamilies(name)` contains Magic or None → universal, compatible.
  - Otherwise: list must include `weapon`.
- `SlotApply.ApplyGrant` no longer gates on "unarmed only" — uses
  `IsGrantCompatible` instead. Same for `ClearGrant` (which now runs regardless
  of weapon, since the saved assignment is removed regardless).
- `SlotApply.ApplyRevert` re-applies saved grants weapon-aware after transform
  ends (rather than the previous "only if unarmed").
- `ReplaceAbilityOnSlotSystemPatch.ProcessEvent` rewritten — the old isUnarmed
  string-match gate is gone. For each saved slot, calls `IsGrantCompatible`
  against the event entity's parsed weapon family. Compatible grants are
  injected, incompatible ones skipped (the weapon's natural ability shows).
- `WeaponFamily` enum extended with `Pollaxe`, `Slashers`, `TwinBlades`,
  `FishingPole` after a prefab-dump scan revealed those EquipBuff names.
- `BeelzCommands.Grant` reply text + help walkthrough updated to drop the
  "applies only when UNARMED" framing. The reply now names the compatible
  weapon families for the assigned ability (or "universal (any weapon)" for
  Magic).
- `BeelzCommands.Grant` command description updated to match.

### Behavioral changes worth flagging

- Players who had grants saved before 0.6.0 will see their grants honor the
  matrix automatically. Universal/Magic abilities they grant to slots 5/6 now
  fire on any weapon — not just unarmed. This is the intended new behavior.
- Players who had weapon-specific grants (e.g. Sword-tagged ability in slot 1)
  on their saved bar will only see the grant fire when wielding a sword. If
  they were testing on an unarmed character, that grant will appear inactive
  until they equip a sword.

---

## [0.5.3] - Unreleased

### Per-ability admin controls

- `AbilityEntry` gains three new fields:
  - `Enabled` (bool, default true) — when false, the ability is blocked from
    capture (via `AbilityFilter.ShouldCapture`) AND blocked from `.beelz grant`
    (via the new explicit check in `BeelzCommands.Grant`). Transform pickup is
    also blocked because `TransformService.GetTransformAbilities` passes through
    `AbilityFilter`. Effectively a per-ability kill-switch surgical to a single
    GUID/name, complementing the bulk `DenyGuids` / `DenyPatterns`.
  - `DamageScale` (float, default 1.0) — admin-curated damage multiplier.
    Read via `AbilityRules.GetDamageScale`. **Inert** until the W5 runtime
    ships (Harmony hook on damage event to scale only when source ability
    was Beelzebub-granted, leaving NPC casters alone).
  - `CooldownScale` (float, default 1.0) — same pattern as DamageScale.
    Read via `AbilityRules.GetCooldownScale`. Inert until W5.
- `AbilityEntry.Enabled` defaults to true via the C# auto-property initializer;
  System.Text.Json leaves missing fields at the constructor default, so existing
  configs without the field continue to behave as enabled.
- `NormalizeAbilityMap` rebuilds entries with the new fields; `DamageScale` /
  `CooldownScale` get clamped to a positive default if a non-positive value
  comes in from JSON.
- `BeelzCommands.Grant` adds two new gates with chat-friendly error messages:
  `Enabled=false` → "is currently disabled by the server admin", and
  `TransformOnly=true` → "is reserved for .beelz transform — cannot be granted
  to a slot". (Task #41 transform-only runtime gate landed here.)
- Curated `ability_map.curated.json` was extended in-place: every one of the
  443 entries now carries the three new fields with default values.

### Pending — W5 (filed)
- Runtime for `DamageScale` + `CooldownScale`. Pattern: Harmony hook on the
  damage / cast pipeline that checks "did this ability get cast from a
  Beelzebub-granted slot" before scaling — so NPC casters keep their natural
  damage. Bloodcraft's class-buff system is the reference pattern.

---

## [0.5.2] - Unreleased

### Ability matrix (W1 data layer, prep for W2)

- `RulesDto.AbilityMap` replaces the simpler `WeaponFamilyMap` that was
  scaffolded in 0.5.1's dev branch. New shape:
  ```json
  "AbilityMap": {
    "<exact ability prefab name>": {
      "Weapons": ["Sword", "GreatSword"],
      "Forms": ["Wolf"],
      "TransformOnly": false,
      "Notes": "free text"
    }
  }
  ```
- Per-ability multi-axis classification:
  - `Weapons[]` — one ability can be valid for many weapon families.
    Empty = universal (any weapon).
  - `Forms[]` — restrict to specific shapeshift forms (Wolf / Bear / Rat
    / Spider / Toad). Empty = no form restriction.
  - `TransformOnly` — blocks `.beelz grant` for the ability. Resolved by
    `AbilityRules.IsTransformOnly` ahead of the bulk
    `TransformOnlyPatterns` / `TransformOnlyGuids` lists, which still
    exist for substring/GUID-based bulk rules (task #41).
- New query methods on `AbilityRules`:
  - `ClassifyWeaponFamilies(name)` — full list of weapon families per
    matrix entry (or single fallback from the name-substring classifier).
  - `ClassifyWeaponFamily(name)` — primary family, for legacy/display
    callers.
  - `GetFormRestriction(name)` — list of `ShapeshiftForm` values.
- New `ShapeshiftForm` enum mirrors the trimmed shapeshift heuristic
  (Wolf, Bear, Rat, Spider, Toad — no Human / Golem, per 0.5.1 trim).
- `WeaponFamilyClassifier.Classify` signature retained for back-compat;
  the live admin path now flows through `AbilityRules.ClassifyWeaponFamilies`
  which reads `AbilityMap` first.
- No runtime behavior change yet — the cascade lives in W2.

### Docs
- New `Beelzebub/docs/ABILITY_MAP_FORMAT.md` — format reference, runtime
  file location (`...\BepInEx\config\kdpen.Beelzebub\ability_rules.json`),
  reload command, valid-value lists, edge-case warnings.
- New `Beelzebub/docs/ability_map.starter.json` — copy-pasteable starter
  with annotated examples (Paladin melee, Hunter bow, Wolf bite,
  Solarus pillar, ...).

---

## [0.5.1] - Unreleased

### Shapeshift heuristic trim (player feedback)

- `ShapeshiftService.PickFormFor` dropped the Human and Golem mappings:
  - `AB_Shapeshift_Human_Buff` moves much slower than real humanoid NPCs;
    not a good disguise. Humanoid captures (Bandit/Militia/Villager/etc.)
    now fall through to "no visual change" and keep the player's vampire model.
  - `AB_Shapeshift_Golem_T02_Buff` isn't reliably player-usable. Same fate.
- Forms still used: `Wolf` (-351718282), `Bear` (-1569370346), `Rat`
  (902394170), `Spider` (124832551), `Toad` (-1038422434).
- The "no form matched" reply line was removed from `.beelz transform`
  output — falling through to vampire is now the expected case for most
  captures and doesn't need a callout.

### Backlog item filed
- Task #41 — transform-only / OP-reserved abilities. Some captured
  abilities should be reserved for `.beelz transform` use only and not
  grantable to a spell slot, either because they're OP or because the
  animation only reads right while wearing the unit's form. Design
  approach: add `TransformOnlyPatterns` / `TransformOnlyGuids` to
  `ability_rules.json` and gate `.beelz grant`.

---

## [0.5.0] - Unreleased

### A4 — visual shapeshift (heuristic)

- New `Services/ShapeshiftService.cs`. Maps a captured CHAR_ unit to one
  of V Rising's eight native shapeshift form buffs by name pattern (the
  ceiling — engine has no generic per-unit model swap; see
  `project_a4_shapeshift_research`). Lookup table (PrefabGUIDs hard-coded
  from the prefab dump):
  - `AB_Shapeshift_Wolf_Buff` (-351718282) — for Werewolf, Wolf family
  - `AB_Shapeshift_Bear_Buff` (-1569370346)
  - `AB_Shapeshift_Rat_Buff` (902394170)
  - `AB_Shapeshift_Spider_Buff` (124832551) — also Arachnid
  - `AB_Shapeshift_Toad_Buff` (-1038422434) — also Frog
  - `AB_Shapeshift_Golem_T02_Buff` (914043867) — StoneGolem too
  - `AB_Shapeshift_Human_Buff` (-53860211) — for Bandit, Militia,
    Villager, VHunter, Vampire, Witch, Cultist, Paladin, Knight, Priest,
    Nun, Farmer, Worker, Merchant, Guard
- Application via Bloodcraft pattern: `DebugEventsSystem.ApplyBuff` with
  `ApplyBuffDebugEvent { BuffPrefabGUID, Who = NetworkId }` and
  `FromCharacter { Character, User }`. Removal via
  `DestroyUtility.Destroy(EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff)`
  after `ServerGameManager.TryGetBuff(character, form.ToIdentifier(), out buff)`.
- New `Core.DebugEventsSystem` accessor, populated in `Core.TryInitialize`.
- `ActiveTransform.AppliedShapeshiftForm` (int, runtime-only) holds the
  applied form's PrefabGUID hash so revert/auto-revert know what to drop.
  0 means no native form matched.
- Reply text from `.beelz transform` notes which form was applied, or
  "(No native shapeshift form matched — model unchanged.)" for unmatched
  units. The old "(Visual model swap is still in development...)" hint
  is removed.

### Known limitations of A4

- Eight forms is V Rising's hard ceiling; many CHAR_ types (banshees,
  Treant, Manticore, Wendigo, skeleton undead, Blackbrew, Drone,
  Stoneborn, etc.) silently fall through to "no visual change". The
  spell bar still swaps via A2.
- The applied buff IS V Rising's native shapeshift; it interacts with the
  game's normal shapeshift mechanics. If a player uses their normal
  vampire shapeshift (`.beelz transform` is unrelated to that), it may
  collide. Tracking as A4-edge; reproducible reports welcome.
- Heuristic table is name-substring based. Adding more mappings in
  `ShapeshiftService.PickFormFor` is safe and additive.

---

## [0.4.0] - Unreleased

### A2 — seamless slot apply

- New `Services/SlotApply.cs`. Bloodcraft-pattern in-place mutation:
  - Walks the player's `BuffBuffer` to find the active `EquipBuff_Weapon_*`
    (covers weapons + `EquipBuff_Weapon_Unarmed_Start01` +
    `EquipBuff_Weapon_FishingPole_Base`, all of which share the
    `EquipBuff_Weapon` prefix).
  - Reads its `ReplaceAbilityOnSlotBuff` dynamic buffer; removes any
    pre-existing entry on the target slot; adds the new entry.
  - Calls `ServerGameManager.ModifyAbilityGroupOnSlot(buffEntity, character,
    slot, ability)` to apply the change without a weapon swap. Same call
    Bloodcraft uses in `Utilities/Classes.cs:HandleNPCSpell`.
- `ApplyGrant` — for `.beelz grant` and `.beelz preset load`. Only fires
  when the active equip-buff is unarmed/fishingpole; weapons keep their
  natural abilities. No-op while a transform is active (transform owns the bar).
- `ApplyTransform` — for `.beelz transform`. Stashes the buffer entries it
  displaces into `ActiveTransform.StashedOverrides` so revert can put them back.
- `ApplyRevert` — for `.beelz revert` and the per-frame Timed auto-revert.
  Removes our slot-1..6 entries, restores the stashed entries, re-applies
  saved grants if unarmed.
- `ClearGrant` — for `.beelz unslot`.
- Reply text and command descriptions updated to drop the "swap a weapon
  to apply" caveat. Grants still gate on UNARMED; that wording stays.
- `TransformService.Revert` signature changed from `bool` → `(bool reverted,
  bool appliedNow)` so callers can tailor the chat reply.
- The existing `ReplaceAbilityOnSlotSystemPatch.OnUpdatePrefix` is still
  active and important — it covers V Rising's own naturally-spawned
  ReplaceAbilityOnSlot events (login, weapon swap, jewel equip). SlotApply
  handles the on-demand command path.

### Known limitations of A2

- `StashedOverrides` is runtime-only (not persisted). If the server restarts
  while a player is mid-transform, the player will see "swap a weapon to
  restore" on revert until they swap once. Acceptable — revert across
  server restart is an edge case.
- If a player swaps weapons WHILE TRANSFORMED, the stashed entries are
  from the previous weapon's buffer. `ReplaceAbilityOnSlotSystemPatch`
  still re-applies the transform on the new weapon's equip-event, so
  combat continues correctly; the stash may produce stale entries on the
  next revert. Player can swap a weapon once to refresh.

---

## [0.3.2] - Unreleased

### F2 fixes from second test
- **Eligibility gate** in `DeathEventListenerSystemPatch.Process`: prefab
  name must start with `CHAR_` and not contain `_Summon` or `_Servant`.
  Previously, killing a `TM_Sulfur_02_Stage1_Resource` (tile model
  resource node) or `CHAR_*_Summon` (temporary summoned ally) could fire
  the transform-unlock roll because they still had a `UnitLevel`
  component. The new gate keeps captures + transform unlocks tied to
  real combat targets.
- `.beelz transform` reply explicitly notes that visual model swap is
  still in development. Sets correct expectations — abilities swap
  but the player model stays the same. Tracked as A4.

## [0.3.1] - Unreleased

### F1 fixes from first real test
- **F1-a** spell-vs-weapon: `ReplaceAbilityOnSlotSystemPatch.OnUpdatePrefix`
  now reads the event entity's `PrefabGUID`, looks up the name via
  `Core.PrefabNames`, and only injects saved slot grants when the name
  contains `unarmed` or `fishingpole` (case-insensitive). Transforms
  bypass this gate — they always override slots 1-N, which is the
  intended EXO-style "you ARE the unit" behavior.
- **F1-b** `.beelz help` rewritten as multiple short `ctx.Reply` calls
  (was failing with "internal error" — likely because of embedded `\n`
  characters and/or oversize message in V Rising's chat path). New
  `.beelz commands` lists every command.
- **F1-c** Defensive try/catch wraps the per-event bodies of
  `DeathEventListenerSystemPatch`, `VBloodSystemPatch`, and
  `ReplaceAbilityOnSlotSystemPatch`, plus `TransformService.Tick` and
  `Persistence.MaybeSave`. Each failure logs to `Core.Log.LogError`
  with the failing entity / kill so the next combat crash leaves a
  traceable line. `start_server_local.bat` updated to `pause` on exit.

## [0.3.0] - Unreleased

### Fourth-wave feature extensions

- **C5** `.beelz help` — paginated chat walkthrough of the capture →
  list → grant → swap-weapon → transform loop.
- **C1** Slot loadout presets (`.beelz preset save|load|list|delete`).
  Per-player named snapshots of slot assignments; persisted in
  `state.json` alongside captures/slots/transforms. Loading a preset
  replaces current slot assignments — swap a weapon to apply.
- **C2** Per-ability rate overrides in `ability_rules.json`. New
  `DropRateOverrides` list, each entry `{ Pattern, RateRegular,
  RateVBlood }`. First matching pattern wins; falls back to the global
  setting. `AbilityRules.TryGetRateOverride` is the lookup helper.
- **C3** Per-unit-tier multipliers via new `Capture.Tier` config
  section. `Capture_TierMidThreshold` / `Capture_TierHighThreshold` on
  `UnitLevel`, with `Capture_TierMultiplier_Low/Mid/High` (all default
  1.0 — no effect until tuned). Multiplier applied to both ability
  and transform drop chances; `EntityExtensions.ResolveTierMultiplier`
  is the helper.
- **C4** Admin grant commands: `.beelz admin give <player> <unitGuid>
  <abilityGuid>` and `.beelz admin give-transform <player> <unitGuid>`.
  Source classified automatically via `PrefabGUID.IsVBloodUnit()` which
  checks the prefab entity for `VBloodUnit` / `VBloodConsumeSource`.
  Player resolved by `EntityExtensions.FindCharacterByName` (case-
  insensitive substring; ambiguous matches return Null).

State.json schema extended: per-player `Presets` map of
`{ name: { slot: abilityGuid } }`. Forward-compatible load — older
files default to no presets.

## [0.2.0] - Unreleased

Initial POC plus the post-roadmap polish waves.

### POC — core capture / grant loop

- **Hooks `DeathEventListenerSystem.OnUpdate` (postfix)** to detect player kills.
  Skips V-Bloods (handled by `VBloodSystem`), traders, blockfeed targets, and
  units without `UnitLevel`.
- **Reads the killed entity's `AbilityGroupSlotBuffer`** and stores captured
  abilities per-SteamID, per-unit-prefab, per-ability-prefab.
- **Persistence** at `BepInEx\config\kdpen.Beelzebub\state.json`. Atomic
  write-temp + replace. Schema v1 on initial release.
- **Default deny patterns** (then): `_Idle_`, `_Flee_`, `_Hard_`. Admin-config
  extra allow / deny strings via BepInEx config (later replaced in Phase 2).
- **Commands**: `.beelz list`, `.beelz grant`, `.beelz clear`.
- **Build & deploy**: `BuildToServer` MSBuild target copies the DLL to the
  local dedicated server's `BepInEx\plugins` after each Release build.
  Override via `-p:VRisingServerPath=...`.

### Phase 1 — V-Blood hook + source classification

- Hooks `VBloodSystem.OnUpdate` (Prefix) so boss kills capture abilities
  through the same registry as regular mobs.
- Each captured ability is tagged `Regular` or `VBlood`. Boss-source wins on
  re-capture if the same ability later drops from a V-Blood.
- `.beelz list` separates V-Bloods from regular mobs with section headers.
- 5-second per-player dedupe on V-Blood events (mirrors Bloodcraft cache).
- `state.json` schema bumped to v2 (forward-compatible load — v1 entries
  default `source=Regular`).

### Phase 2 — hot-reloadable ability rules

- Capture filter now reads from
  `BepInEx\config\kdpen.Beelzebub\ability_rules.json`, auto-created on first
  launch with the curated default deny list:
  `_Idle_`, `_Flee_`, `_Sequence_`, `_MeleeAttack_`, `_Block_`, `_Parry_`,
  `_Counter_`, `_Spawn_`, `_Despawn_`, `_Disappear_`, `_Death_`, `_Wounded_`,
  `_Hard_`, `_Test_`, `_Internal_`, `_DEBUG_`.
- Supports allow/deny by substring **or** exact GUID. Allow-mode kicks in
  when an allow list is non-empty.
- New `.beelz admin` admin-only commands: `rules`, `deny <pattern>`,
  `undeny <pattern>`, `allow <pattern>`, `unallow <pattern>`, `reload`.

### Phase 3 — in-chat notifications

- Per-player verbosity (`Silent` / `Summary` / `Verbose`). Server default via
  BepInEx config; per-player override via `.beelz verbosity <level>`.
- Sends server-initiated chat messages via
  `ServerChatUtils.SendSystemMessageToClient` (FixedString512Bytes).
- Summary on capture: "Acquired N new ability(ies) from <unit>." Verbose adds
  per-ability lines.
- Verbosity persisted alongside captures/slots in `state.json`
  (schema bumped to v3).

### Phase 4 — drop-chance acquisition

- Per-ability roll on capture, separate defaults for Regular (5%) and
  V-Blood (5%).
- Configurable via BepInEx `Capture.DropChance` section:
  `DropChance_Ability_Regular`, `DropChance_Ability_VBlood`. Setting to 1.0
  restores legacy 100% capture.
- Per-kill transform-unlock roll (default 1%) staged via
  `DropChance_Transform_Regular/VBlood`.

### Phase 5 — transformation system

- Independent rule sets for Regular vs V-Blood transforms. Each source type
  has:
  - `Mode`: `Toggle` (active until manual revert), `Timed` (auto-revert
    after duration), or `Disabled`.
  - `Duration` seconds (when `Timed`).
  - `Cooldown` seconds after revert (0 = none).
  - Defaults: both = `Toggle`, 60s duration, 0s cooldown.
- New user commands:
  - `.beelz transforms` — list unlocks (grouped by source) plus the currently
    active one.
  - `.beelz transform <index|substring>` — activate. Swap a weapon to apply.
  - `.beelz revert` — end the current transform.
- Per-kill transform unlock roll fires on both regular and V-Blood kills via
  `DropChance_Transform_*`.
- During a transform, the player's slots 1-6 are overridden with up to six of
  the unit's filtered `AbilityGroupSlotBuffer` abilities. Regular slot grants
  resume on revert.
- `state.json` schema bumped to v4.
- Auto-revert tick lives in `DeathEventListenerSystemPatch.OnUpdatePostfix`
  (runs every server frame, regardless of kill activity).
- Admin commands: `.beelz admin transform mode|duration|cooldown|show` for
  live tuning per source type.

### Phase 6 — BCH-ready API surface

- New `.beelz api` command group for client-UI consumption. Every reply line
  starts with a `[BEELZ:<tag>]` marker so BCH can filter and parse:
  - `.beelz api version` — `[BEELZ:version] api=1 plugin=... ready=0|1`
  - `.beelz api list` — streams `[BEELZ:list]` records, terminated with
    `[BEELZ:end] cmd=list count=N`
  - `.beelz api slots` — streams `[BEELZ:slot]` records + end marker
  - `.beelz api transforms` — streams `[BEELZ:tx]` records + end marker
  - `.beelz api active` — single `[BEELZ:active]` line (or
    `[BEELZ:active] none=1`)
  - `.beelz api info <index>` — single `[BEELZ:info]` line with the same
    fields as `list` plus `label=` and `desc=`
  - `.beelz api verbosity` / `rules` / `transform-config` — read-only state
    dumps for the BCH settings panel
  - Errors use `[BEELZ:err] cmd=<name> code=<code> msg=<text>`
- New mutating commands for BCH-side delete-from-UI workflows:
  - `.beelz forget <index>` — remove one captured ability by index. Clears
    any slot assignment pointing at it.
  - `.beelz forget-transform <index>` — remove one transform unlock; reverts
    if currently active.
- Wire format is bare `key=value` pairs space-separated; prefab names are
  guaranteed `[A-Za-z0-9_]` so no quoting needed. Lines fit comfortably under
  the 512-byte chat message limit.

### First-wave polish (post-roadmap)

- **A3** Auto-revert chat routing: when a Timed transform expires, the
  player gets a `Summary`-level chat ping ("Transformation ended (UnitName).
  Swap a weapon to restore.") instead of only a server-side log line.
- **B1** Debounced `state.json` saves: `RequestSave()` marks dirty,
  `MaybeSave()` writes at most once per second from the per-frame tick.
  Eliminates blocking JSON writes during heavy combat. `SaveSync()` retained
  as the synchronous escape hatch for `Plugin.Unload`.
- **B3** Per-frame kill aggregation: an AoE wipe of five mobs now emits one
  `Summary` line ("Acquired 12 abilities across 5 units (CHAR_X, CHAR_Y×3,
  ...)") instead of five. `Verbose` per-ability lines stay inline. V-Blood
  path unchanged — boss kills keep their per-event messaging.

### Third-wave (partial) — shared-kill credit
- **B2** New `Capture_ShareCreditMode` setting: `KillerOnly` (default, legacy
  behavior) or `Proximity` (every online player within
  `Capture_ShareCreditRadius` of the kill site gets their own independent
  capture roll). Mirrors Bloodcraft's "death participants" model conceptually
  but implemented purely via `LocalToWorld` proximity in the death patch —
  no dependency on Bloodcraft's PlayerService cache or activity grid.
- Distance check is XZ-plane (V Rising convention; ignores height) and uses
  squared-distance to avoid `sqrt` per player per kill.
- The killer is always included even if outside the radius (long-ranged
  kills like archery shouldn't lose credit to the shooter).
- Refactored `DeathEventListenerSystemPatch.Process` into
  `ResolveParticipants(killer, died)` → `ProcessForParticipant(participant,
  ...)` so each player runs the full capture + transform-unlock roll
  independently. Per-frame aggregation still works correctly because each
  player's `KillAggregate` is keyed by their own SteamId.

### Deferred from third wave
- **A2** (seamless slot apply — no weapon swap) and **A4** (visual
  shapeshift VFX on transform) remain pending. Both touch ECS internals
  where empirical testing matters more than design speculation.

### Second-wave polish (BCH-blockers)

- **E4** Push-style sync for BCH: per-player `EmitApiEvents` flag (default
  off, persisted) gates `[BEELZ:event]` lines on key state changes —
  captures, transform unlocks, transform activate/end, slot grant/clear.
  Independent of chat verbosity so BCH can keep up while the player keeps
  chat quiet. New `.beelz api bch <on|off|status>` command for BCH to toggle.
- **A1-lite** Humanized labels: `.beelz api info` now emits a readable
  `label=` (e.g. "Bandit Bomb Throw" instead of
  "AB_Bandit_BombThrow_AbilityGroup") via a prefab-name humanizer. Real
  LocalizationManager-driven descriptions are still pending.

### Release prep

- **D1** Final icon chosen: `05_devouring_vortex` — spiral of red orbs into a
  central void. Visual metaphor for ability devouring.
- **D2** README rewrite with full command cheat-sheet and feature overview.
- **D3** `tcli build` pipeline verified end-to-end — produces a ~333 KB
  upload-ready zip. New `BuildToDist` MSBuild target stages the DLL where
  `tcli`'s default mapping picks it up.
- **D4** MIT LICENSE at repo root.
- **D5** Pending: bump csproj `<Version>` and `thunderstore.toml`
  `versionNumber` to `0.2.0` when publish-ready.

### Known caveats

- `.beelz grant`, `.beelz transform`, and `.beelz revert` all queue a slot
  change that V Rising applies on its next natural `ReplaceAbilityOnSlot`
  event (weapon swap, jewel equip). Eliminating this is tracked as A2.
- No visual shapeshift VFX on transform — only the spell bar changes.
  Tracked as A4.
- Not every ability is usable in every slot. The default filter strips most
  obvious cases; per-slot validation is tracked as part of Phase 6 follow-up.
- Real localized ability descriptions are not yet wired. The current
  `desc=` field on `.beelz api info` emits a humanized prefab name only.
  Tracked as A1-full.
