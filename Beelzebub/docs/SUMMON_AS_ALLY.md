# Summon-as-Ally Architecture (v0.22.x → v0.23.15)

This document captures the working solutions discovered while iterating on
the "lead a horde" feature — letting a Beelzebub-transformed player cast a
boss's summon ability and have the resulting units fight as their allies.

Every section here is a known-working pattern. If a future test session
breaks one of these behaviors, this is the first place to look before
re-researching.

## High-level architecture

Two complementary spawn hooks:

| Hook | When it fires | What it does |
|---|---|---|
| `AbilityCastStartedSystemPatch` (Hook 1) | Player casts an ability whose group name matches a "summon" pattern | If the ability is in the `SummonTargets` map → manually spawn the target unit via `ServerGameManager.InstantiateEntityImmediate` and apply player-ally setup. Stack-cap check runs here. |
| `LinkMinionToOwnerOnSpawnSystemPatch` (Hook 2) | V Rising's spawn chain naturally produces a minion entity (e.g. Undead Priest's `_RaiseHorde_`) | Walks owner chain → if owner resolves to a player with active transform → apply player-ally setup. Attribution-window matches the spawn back to the recent cast so it counts toward stack-cap. Over-cap spawns get destroyed immediately. |

Setup is centralized in `SummonAllyService.ApplyPlayerAllySetup` so both hooks
use the same recipe.

## Player-ally component recipe (`ApplyPlayerAllySetup`)

The full set of mutations on a freshly-spawned summon. Order matters in some
cases (e.g. clear convertable before adding follower). Pattern lifted from
Bloodcraft's `FamiliarBindingSystem.ModifyFollowerFactionMinion` + helpers.

1. `EntityOwner.Owner = playerCharacter`
2. `FactionReference.FactionGuid._Value = Faction_Players` (PrefabGuid `1106458752`)
3. `Follower.Followed._Value = playerCharacter`; **`Follower.ModeModifiable._Value = 0`** (leash by default — see "Vortex fix" below)
4. `Minion.MasterDeathAction = MinionMasterDeathAction.Kill`
5. Add `BlockFeedBuff` component (player-ally marker; some downstream systems key on it)
6. **Strip `ServantConvertable`** — V Rising's waygate / "subdued" check looks at this
7. **Strip `CharmSource`** — same reason
8. Clear `DropTableBuffer` entries — killed summons don't drop unwanted loot
9. Modify `DynamicCollision.AgainstPlayers` (RadiusOverride=-1, HardnessThreshold=0.1, PushStrength=0) — don't shove the player
10. Add `CanPreventDisableWhenNoPlayersInRange` with `CanDisable = ModifiableBool(false)` — V Rising won't auto-disable the entity when the player travels far
11. **Do NOT add to player's `FollowerBuffer`** — adding triggers the waygate subdued check

We do NOT zero the aggro modifiers (`AlertModifiers`, `AggroModifiers`,
`GainAggroByVicinity`, `GainAlertByVicinity`). Earlier versions (v0.23.0-5)
did, mirroring Bloodcraft, but it left summons fully passive on damage taken.
Native proximity aggro is required for autonomous combat retaliation.

## Vortex fix: dynamic ModeModifiable toggle (v0.23.15)

**Problem.** `Follower.ModeModifiable=1` enables combat autonomy but units
clip into the player's position ("vortex follow"). `ModeModifiable=0` gives
proper leash spacing but units don't engage in combat.

**Fix.** Dynamically toggle based on combat state — Bloodcraft's pattern
(`Utilities/Familiars.cs:518`, `StatChangeSystemPatch.cs:204`, etc.).

- **Default (out of combat):** mode = 0 → leash, proper spacing
- **PvE combat buff applies on player:** `SetCombatMode(steamId, true)` → mode = 1 → combat autonomy
- **PvE combat buff destroys on player:** `SetCombatMode(steamId, false)` → mode = 0

PvE combat buff PrefabGuid: `581443919`. Detected via:
- `BuffSpawnServerPatch` (combat-start)
- `UpdateBuffsBufferDestroyPatch` (combat-end)

## Stack-cap accounting (per-cast, not per-entity)

**Problem.** Early attempts counted live entities → multi-spawn abilities
like Undead Priest's `_RaiseHorde_` hit the cap after one cast.

**Fix.** `SummonStacks` is `Dictionary<int, List<List<Entity>>>` — outer list
per ability, inner list per cast group. Each cast opens a new group via
`BeginCastGroup`; entities are added via `TrackInCurrentGroup`. A group is
"alive" if any entity in it is alive. The cap counts **live groups**, not
entities. So three casts of RaiseHorde = 3/3 uses regardless of how many
entities each spawned.

`Transform_MaxStacksPerSummonAbility` config (default 3) sets the cap.

## Death detection: prune via DeathEventListenerSystem (not Health)

**Problem.** Counting via `entity.Exists()` includes dead-but-not-yet-destroyed
entities (V Rising's death-animation window can keep them in EntityManager
for several seconds). `Health.Value <= 0` check broke instead — freshly-
spawned units briefly have Value=0 before V Rising initializes from MaxHealth,
so the cap thought everything was dead and refused nothing.

**Fix.** Hook `DeathEventListenerSystem.OnUpdate` Postfix (the same patch
we already use for capture). For every `DeathEvent.Died`, call
`SummonAllyService.PruneDeadSummon(diedEntity)` which removes it from
`SummonedMinions`, `StashedSummons`, and all `SummonStacks` group lists.
Cap decrements within ~1 frame of V Rising marking the unit dead.

## Destroy path: DestroyUtility + Disabled fallback

**Problem.** `EntityManager.DestroyEntity` doesn't destroy network-tracked
player-allied entities — it fails silently. Saves accumulate corruption.

**Fix.** `TryDestroyAllPaths` in `SummonAllyService`:

1. **Try** `DestroyUtility.Destroy(EntityManager, entity)` (Bloodcraft pattern).
   Async — trust it succeeded if no exception thrown.
2. **Fallback** `EntityManager.DestroyEntity(entity)` if path 1 throws.
3. **Last resort** add `<Disabled>` component if both destroy paths throw —
   entity is hidden + neutralized even though not removed.

Admin commands (`.beelz admin desummon-all`) call this directly via
`DrainAdminQueueImmediate`. Transform revert / disconnect / over-cap also
use the immediate path now.

`TransformService.Tick` was previously gated on death events firing
(unreliable when nothing's dying), so the v0.23.0 "staged Tick-driven
queue" was abandoned in favor of immediate-drain.

## Waygate flow (auto-stash + auto-restore)

The user can use waygates without typing any command. Two hooks:

1. **Auto-stash** at cast-start. `AbilityCastStartedSystemPatch` watches for
   waypoint ability groups (`AB_Interact_UseWaypoint_AbilityGroup` 893332545,
   `AB_Interact_UseWaypoint_Castle_AbilityGroup` 695067846) and calls
   `StashAll` before V Rising's subdued check fires. Chat: "Auto-stashed N...".
2. **Auto-restore** at arrival. `UpdateBuffsBufferDestroyPatch` watches for
   `Buff_Waypoint_Travel` (`150521246`) or `Buff_Waypoint_TravelEnd`
   (`-1361133205`) destruction on the player and calls `RestoreAll`.
   Chat: "Restored N at destination."

`StashAll` (Bloodcraft `DismissFamiliar` pattern):
- Set `Follower.Followed._Value = Entity.Null` ← key: this is what V Rising's
  subdued check looks at
- Remove entity from player's `FollowerBuffer` (defensive)
- Add `<Disabled>` component
- Move from `SummonedMinions` to `StashedSummons` list

`RestoreAll` reverses everything + re-sets `ModeModifiable = 0`. Combat-mode
toggle (above) flips to 1 on first combat after restore.

Manual fallbacks: `.beelz summons stash` / `.beelz summons restore`. Plus
`.beelz tp` as a one-character alias.

## Bug fixes notable enough to remember

| Bug | Cause | Fix |
|---|---|---|
| BuffSystem_Spawn_Server iterating empty arrays | `ToComponentDataArray<PrefabGUID>` on raw `EntityQueries[0]` returns empty when archetype doesn't include `PrefabGUID` | Use `ToEntityArray` + `TryGetComponent<PrefabGUID>` per-entity instead |
| Cap blocking forever after units die | `entity.Exists()` still true during V Rising's death-animation window | DeathEventListenerSystem pruning hook |
| Refused casts can't be attributed | `RecentSummonCast` was set AFTER the cap check, so refused casts couldn't credit natural-chain spawns | Move `RecentSummonCast` assignment before the cap check |
| Save corruption from prior sessions | Pre-v0.23.7 untracked summons (10-cap leak) | `.beelz admin desummon-all` sweeps by Follower + EntityOwner-chain |
| Sweep doesn't catch all summons | First version required `BlockFeedBuff` (only present on v0.23.x+ spawns) | Broaden: EntityOwner-chain walks to player + accept Faction_Players OR BlockFeedBuff OR Minion |

## Settings reference

| Setting | Default | What |
|---|---|---|
| `Transform_SummonsAreAllies` | true | Master toggle |
| `Transform_MaxStacksPerSummonAbility` | 3 | Per-ability use cap |
| `Transform_DespawnBudgetPerFrame` | 5 | Staged-despawn rate (unused in v0.23.5+ since immediate-drain replaced staged) |
| `Transform_DespawnSummonsOnDisconnect` | true | Despawn on logout |
| `Transform_SummonLeashRadius` | 30 | Teleport-back distance |

## Patch reference (11 Harmony patches in v0.23.15)

| Patch | System | Hook | Purpose |
|---|---|---|---|
| `DeathEventListenerSystemPatch` | `DeathEventListenerSystem.OnUpdate` | Postfix | Capture loop + `SummonAllyService.PruneDeadSummon` |
| `ReplaceAbilityOnSlotSystemPatch` | `ReplaceAbilityOnSlotSystem.OnUpdate` | Postfix | Phase 5 transform spell-bar |
| `VBloodSystemPatch` | `VBloodSystem.OnUpdate` | Postfix | V-Blood kill capture |
| `GameDataInitializedPatch` | `LoadPersistenceSystemV2.SetLoadState` | Postfix | Core init |
| `AbilityCastStartedSystemPatch` | `AbilityCastStarted_SetupAbilityTargetSystem_Shared.OnUpdate` | Prefix | Summon-cast intercept + waypoint auto-stash |
| `LinkMinionToOwnerOnSpawnSystemPatch` | `LinkMinionToOwnerOnSpawnSystem.OnUpdate` | Prefix | Natural-chain summon rebind |
| `DealDamageSystemPatch` | `DealDamageSystem.OnUpdate` | Prefix | Offensive aggro injection |
| `PlayerTeleportSystemPatch` | `PlayerTeleportSystem.OnUpdate` | Postfix | Admin debug teleport summon-follow |
| `ServerBootstrapSystemPatch` | `ServerBootstrapSystem.OnUserDisconnected` | Prefix | Despawn on disconnect |
| `BuffSpawnServerPatch` | `BuffSystem_Spawn_Server.OnUpdate` | Prefix | Auto-stash (waygate channel) + combat-mode=1 on PvE combat buff |
| `UpdateBuffsBufferDestroyPatch` | `UpdateBuffsBuffer_Destroy.OnUpdate` | Postfix | Auto-restore on waygate buff destroy + combat-mode=0 on PvE combat buff end |

## What to research next

- **Auto-stash on bat-form / shapeshift teleport** — hook `ShapeshiftSystem.OnUpdate` Prefix for `AB_Shapeshift_Bat_Group` (`PrefabGUIDs.AB_Shapeshift_Bat_Group`). Same `StashAll` call.
- **Stack-buff visualization** — apply a stacking buff to the player using the ability's icon, count matches `SummonStacks[abilityGuid].Count`. Decay as groups fully die. (User-requested UX.)
- **Lifespan config** — `Transform_SummonLifetimeSeconds`. Track CastTime per group, auto-despawn after N seconds. Prevents indefinite summon persistence on long sessions.
- **Visual ability animation mapping** (Task #84) — separate workstream. When a player casts a boss ability, ideally the player model briefly takes the boss's animation. Research-heavy.
