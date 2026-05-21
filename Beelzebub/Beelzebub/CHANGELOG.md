# Changelog

All notable changes to this project will be documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and
this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - Unreleased

Initial POC.

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
