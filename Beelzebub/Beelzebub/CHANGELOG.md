# Changelog

All notable changes to this project will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

POC + early polish.

### Phase 1 — V-Blood hook + source classification
- Hooks `VBloodSystem.OnUpdate` (Prefix) so boss kills capture abilities through the same registry as regular mobs.
- Each captured ability is tagged `Regular` or `VBlood`. Boss-source wins on re-capture.
- `.beelz list` separates V-Bloods from regular mobs with section headers.
- 5-second per-player dedupe on V-Blood events.
- `state.json` schema bumped to v2 (forward-compatible load).

### Phase 2 — hot-reloadable ability rules
- Capture filter now reads from `BepInEx\config\kdpen.Beelzebub\ability_rules.json` (auto-created with curated defaults).
- Default deny patterns: `_Idle_`, `_Flee_`, `_Sequence_`, `_MeleeAttack_`, `_Block_`, `_Parry_`, `_Counter_`, `_Spawn_`, `_Despawn_`, `_Disappear_`, `_Death_`, `_Wounded_`, `_Hard_`, `_Test_`, `_Internal_`, `_DEBUG_`.
- Supports allow/deny by substring **or** exact GUID. Allow-mode kicks in when an allow list is non-empty.
- New `.beelz admin` admin-only commands: `rules`, `deny <pattern>`, `undeny <pattern>`, `allow <pattern>`, `unallow <pattern>`, `reload`.

### Phase 3 — in-chat notifications
- Per-player verbosity (Silent / Summary / Verbose). Server default via BepInEx config; per-player override via `.beelz verbosity <level>`.
- Sends server-initiated chat messages via `ServerChatUtils.SendSystemMessageToClient`.
- Summary on capture: "Acquired N new ability(ies) from <unit>." Verbose adds per-ability lines.
- Verbosity is persisted alongside captures/slots in `state.json` (schema bumped to v3).

### Phase 4 — drop-chance acquisition
- Per-ability roll on capture, separate defaults for Regular (5%) and V-Blood (5%).
- Configurable via BepInEx `Capture.DropChance` section: `DropChance_Ability_Regular`, `DropChance_Ability_VBlood`. Setting to 1.0 restores legacy 100% capture.
- Per-kill transform-unlock roll (default 1%) staged via `DropChance_Transform_Regular/VBlood`.

### Phase 6 — BCH-ready API surface
- New `.beelz api` command group for client-UI consumption. Every reply line starts with a `[BEELZ:<tag>]` marker so BCH can filter and parse:
  - `.beelz api version` — `[BEELZ:version] api=1 plugin=... ready=0|1`
  - `.beelz api list` — streams `[BEELZ:list]` records, terminated with `[BEELZ:end] cmd=list count=N`
  - `.beelz api slots` — streams `[BEELZ:slot]` records + end marker
  - `.beelz api transforms` — streams `[BEELZ:tx]` records + end marker
  - `.beelz api active` — single `[BEELZ:active]` line (or `[BEELZ:active] none=1`)
  - `.beelz api info <index>` — single `[BEELZ:info]` line with the same fields as `list` plus `desc=` (description hookup deferred)
  - `.beelz api verbosity` / `rules` / `transform-config` — read-only state dumps for the BCH settings panel
  - Errors use `[BEELZ:err] cmd=<name> code=<code> msg=<text>`
- New mutating commands for BCH-side delete-from-UI workflows:
  - `.beelz forget <index>` — remove one captured ability by index. Clears any slot assignment pointing at it.
  - `.beelz forget-transform <index>` — remove one transform unlock; auto-reverts if currently active.
- Wire format is bare `key=value` pairs space-separated; prefab names are guaranteed `[A-Za-z0-9_]` so no quoting needed. Lines fit comfortably under the 512-byte chat message limit.

### Phase 5 — transformation system
- Independent rule sets for Regular vs V-Blood transforms. Each source type has:
  - `Mode`: `Toggle` (active until manual revert), `Timed` (auto-revert after duration), or `Disabled`.
  - `Duration` seconds (when `Timed`).
  - `Cooldown` seconds after revert (0 = none).
  - Defaults: both = `Toggle`, 60s duration, 0s cooldown.
- New user commands:
  - `.beelz transforms` — list your unlocked transforms (grouped by source) plus the currently active one if any.
  - `.beelz transform <index|substring>` — activate a transform. Swap a weapon to apply.
  - `.beelz revert` — end the current transform.
- Per-kill transform unlock roll fires on both regular and V-Blood kills via `DropChance_Transform_*`.
- During a transform, the player's slots 1-6 are overridden with up to six of the unit's filtered `AbilityGroupSlotBuffer` abilities. Regular slot grants resume on revert.
- `state.json` schema bumped to v4 to include per-player transform unlock list (backward-compatible load).
- Auto-revert tick lives in `DeathEventListenerSystemPatch.OnUpdatePostfix` (runs every server frame, regardless of kill activity).
- Admin commands: `.beelz admin transform mode|duration|cooldown|show` for live tuning per source type.

### Capture pipeline
- Hooks `DeathEventListenerSystem.OnUpdate` postfix to detect player kills.
- Skips V-Bloods (separate hook in a future drop), traders, blockfeed targets, and units without `UnitLevel`.
- Reads the killed entity's `AbilityGroupSlotBuffer` and stores captured abilities per-SteamID, per-unit-prefab, per-ability-prefab.

### Filtering
- Default deny patterns: `_Idle_`, `_Flee_`, `_Hard_` (toggleable via config).
- Admin-configurable extra allow / deny patterns (case-insensitive substrings).

### Persistence
- JSON state at `BepInEx\config\kdpen.Beelzebub\state.json`.
- Atomic writes (write-temp + replace).
- Loaded once on game-data init; saved on each capture and on plugin unload.

### Commands (VCF)
- `.beelz list` — list your captured abilities with index.
- `.beelz grant <slot 1-6> <index>` — write a `ReplaceAbilityOnSlotBuff` to your character for the given slot.
- `.beelz clear` — forget all captured abilities (irreversible).

### Build & deploy
- `BuildToServer` MSBuild target copies the DLL to the local dedicated server's `BepInEx\plugins` after each Release build. Override via `-p:VRisingServerPath=...`.

### Known caveats
- `.beelz grant` may require a weapon swap to make the slot change visible in the spell bar — V Rising's `ReplaceAbilityOnSlotSystem` typically processes these events during equip flows. To be validated in-game.
- `state.json` is saved synchronously on each capture inside the ECS system tick. Acceptable for single-player local dev; should be debounced or async before any production use.
- Prefab name lookup is bare (returns `PrefabGuid(<int>)`). Pretty names will land when we wire a localization service or `_PrefabDataLookup` reader.
