# Developer changelog

Full implementation-detail history. Phase numbering, schema versions, internal architecture notes, and rationale for design choices live here.

The **player-facing changelog** that ships in the Thunderstore zip lives at [`Beelzebub/Beelzebub/CHANGELOG.md`](Beelzebub/Beelzebub/CHANGELOG.md) — it's trimmed to user-visible changes and stays well under Thunderstore's ~32 KB cap.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

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
