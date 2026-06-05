# Ability-change impact discipline — solve the class, don't break the neighbours

**Why this doc exists.** Beelzebub's ability changes are rarely one-offs. Most fixes
are *global* or *class-wide* by construction, so a change aimed at one ability can
(a) silently change a **boss fight**, (b) silently change a **sibling ability** that
shares a spawned prefab, or (c) **collide** with another injection source and crash.
Before/after every ability behaviour change, walk this checklist. The goal is to
**fix the mechanism class, not the single instance**, and to prove the blast radius
is intended.

This is the authoritative process; `.claude/hooks/ability-impact-reminder.ps1` is just
the reminder net that surfaces it on edits to the mutation surfaces.

---

## 1. The mutation surfaces, by blast radius (widest first)

Know which tier you're touching — that tells you how far the change reaches.

### Tier A — GLOBAL prefab mutations (widest; persist until server restart)
`Services/AbilityTuningService.cs` (`ApplyAll` → `ApplyToPrefab` → `ApplyDownstream` →
`WriteSpawnedFields`), `Services/MountRecovery.cs` (`NeutralizeMountDisruptingCasts`),
the hard-block list in `Services/AbilityRules.cs` (`_hardBlockedGuids`).

- The ability's **group/cast prefab is shared with the source NPC/boss.** Tuning a
  player's captured ability *also changes the boss that still uses it* (cooldown,
  movement, interrupt, force-timeout — all hit the NPC too). Usually acceptable for
  player-facing knobs; **not** acceptable if it would trivialise or break a boss fight.
- The downstream walker writes onto **spawned buff/projectile prefabs that can be
  referenced by more than one ability chain.** Writing a per-ability value (duration,
  AoE radius, projectile speed, heal multiplier, movement-impair clear) onto a *shared*
  spawn prefab changes it for **every other ability whose chain reaches that prefab.**
  This is the #1 "fix-one-break-another" trap. See `CHAIN_AUDIT.md`.
- The **global min-cooldown floor** (`Grant_MinimumCooldownSeconds`) touches *every*
  ability prefab carrying `AbilityCooldownData` — a single config value is a mass edit.
- An **init-time neutralization** (the Fabian `CastImpair`-strip pattern) is a global
  prefab edit to specific GUIDs whose *pattern* usually generalizes to a whole class
  of "boss ability that disrupts a state we inject into."

### Tier B — Category / classification rules (class-wide by name heuristic)
`Services/Categorization.cs`, `Services/AbilityRules.cs` (category/weapon/form
classification), `Services/AbilityFilter.cs` (`IsJunkAbility`),
`Services/WeaponFamily.cs` + `Services/WeaponFamilyClassifier.cs`.

- A heuristic change re-buckets **hundreds** of abilities at once (history: one
  Categorization change moved **716** abilities out of `Other` — see `ABILITY_AUDIT.md`).
- "Catch this one ability's category/weapon/form" via a broadened pattern routinely
  **mis-buckets a sibling family.** Prefer an explicit per-ability override
  (`ability_rules.json` `Category`/form/weapon entry, the `!`-block/allow syntax) over
  widening a shared heuristic.

### Tier C — Per-player runtime injection (collision risk, not blast radius)
`Services/SlotApply.cs`, `Services/ShapeshiftAbilityService.cs` (forms + mounted),
`Patches/ReplaceAbilityOnSlotSystemPatch.cs`, `Services/BossFormRegistry.cs`,
`Services/TransformService.cs` / `Services/TransformBuffService.cs`.

- Multiple sources writing `ReplaceAbilityOnSlotBuff` / modifying the same
  `ProjectM.AbilityGroupSlot` can **collide** → the Burst
  `ArgumentException AppendRemovedComponentRecordError` crash (this is exactly the
  Mountup-on-grant crash). Any **new** injection surface must be checked against the
  existing ones (slot grant, weapon bar, form bar, mounted bar) for same-slot collision.
- Injection that lands **after** the bar resolves/syncs does not render — inject
  *in-resolve* via `ReplaceAbilityOnSlotSystemPatch` (the v0.89 forms fix, reused for
  Mounted). A "render" fix for one form is usually a fix for all of them.

### Tier D — Per-player live state (narrowest)
`Services/AbilityCooldownEnforcer.cs`, `Services/GrantPowerScalingService.cs`,
`Services/ForceCastService.cs`, `Services/SummonAllyService.cs`.

- Lowest cross-ability reach, but several ride the **same on-cast hook**
  (`Patches/AbilityCastStartedSystemPatch.cs`). Adding work there interacts with the
  cooldown enforcer + power scaling already on that hook — check ordering / double-fire.

---

## 2. Before you change ability behaviour — the checklist

1. **What mechanism CLASS is this?** Is the symptom specific to this one ability, or to
   a component/behaviour shared by a family (all channels, all projectiles, all summons,
   all mounted-boss casts, all `Buff_*` over-time effects)? **Prefer the class-level fix**
   and state explicitly which family it covers.
2. **Is the prefab I'm editing SHARED?**
   - Group/cast prefab → shared with the **source NPC/boss.** Does the edit harm that
     boss fight? (Movement/interrupt/force-timeout/cooldown all change the NPC.)
   - Downstream spawn prefab → is it referenced by **other** ability chains? (Generic
     `Buff_*` / shared projectile.) If so a per-ability value is a semantic collision —
     either it's genuinely shared-safe, or the change is wrong at this layer.
3. **Am I adding a new injection source?** Does it touch `ReplaceAbilityOnSlotBuff` /
   `AbilityGroupSlot`? Prove it can't collide with slot/weapon/form/mount injection on
   the same slot.
4. **Init-edit ordering.** If my global edit and another (`ApplyAll`,
   `NeutralizeMountDisruptingCasts`) touch the same prefab, is the order in `Core` init
   correct and idempotent on `.beelz admin reload`?
5. **Is it restorable?** Every global prefab edit must `CaptureOriginal(...)` a baseline
   so `.beelz admin ability ... defaults` and a restart undo it (add the field to
   `Original` + `RestorePrefab`). An un-cached edit is a permanent, un-revertable change.

## 3. After you change it — prove the blast radius

6. **Who else matches?** Run the change against the obvious siblings in the same
   category/family. List them; confirm the change is *intended* for them or correctly
   scoped out (per-ability override, GUID guard, `Has<T>()` guard).
7. **Did I narrow or widen a heuristic?** If you touched classification / junk /
   weapon-family rules, what else moved buckets? Spot-check a couple of unrelated names.
8. **Does this fix a whole tester-feedback class?** Cross-check `TESTER_FEEDBACK_TRIAGE.md`
   — a single mechanism fix (e.g. "strip the disrupting buffer," "force-timeout an
   indefinite buff," "block one injection collision") frequently resolves several
   reported abilities at once. Note which reports this closes.
9. **Log + document.** Note the cross-ability scope in CHANGELOG. If the surface is
   BCH-facing, update `BCH_INTEGRATION_HANDOFF.md` in the same change (its own rule).

---

## 4. Known shared-mechanism map (the regression-watch list)

| Shared thing | Where | Who shares it | Risk if you edit blindly |
|---|---|---|---|
| **Group/cast prefab** | every captured ability | the **source NPC/boss** | boss fight changes with the player ability |
| **Spawn prefab** (`Buff_*`, projectiles) | downstream of `WriteSpawnedFields` | **multiple ability chains** | per-ability value bleeds into siblings — see `CHAIN_AUDIT.md` |
| **`AbilityGroupSlot`** | `ReplaceAbilityOnSlotSystem` | slot grant + weapon bar + form bar + mounted bar | two writers on one slot → Burst crash |
| **On-cast hook** | `AbilityCastStartedSystemPatch` | cooldown enforcer + power scaling + force-cast | added work double-fires / reorders |
| **`ReplaceAbilityOnSlotSystem` in-resolve inject** | `ReplaceAbilityOnSlotSystemPatch` | forms + mounted | a render fix for one is a render fix for all |
| **Category/weapon/form heuristics** | `Categorization` / `AbilityRules` / `WeaponFamilyClassifier` | hundreds of abilities by name | widening to catch one mis-buckets a family |
| **Global min-cooldown floor** | `Grant_MinimumCooldownSeconds` | every ability with `AbilityCooldownData` | a config tweak is a mass edit |

## 4b. PERSISTENCE SAFETY — never structurally edit a persisted prefab buffer (hard rule)

Learned the hard way (v0.101–0.104 → save-corruption crash on v0.105 load): a global prefab
edit that changes a buffer's **element count** or **adds/removes a component** can desync the
world **save**. V Rising's `LoadPersistenceSystemV2` stores some buffers as "copy from prefab
on load" and asserts the saved instance's element count equals the prefab's. If the mod removed
buffer elements (`RemoveAt`/`Clear`) from such a prefab while a save was written, a later load with
the *unmodified* prefab aborts:

```
PersistenceV2 - ... 'AbilitySpawnPrefabOnStartCast' ... buffer element count was not the same
in the instance and the prefab → NullReferenceException (Burst) → Application abort.
```

The trace is pure game code (no mod frames), so it reads like a game bug — but the mod caused it,
and **removing the mod doesn't fix an already-written save.** Recovery is a save rollback.

**Rules:**
- **Field-VALUE edits on prefab components are safe** (cooldown, speed, `BuffModificationFlagData.
  ModificationTypes`, etc.) — they don't change counts. This is how `AbilityTuningService` and the
  v0.105 CastImpair-neutralize work. Prefer them.
- **Structural edits on prefabs are NOT safe**: `DynamicBuffer.RemoveAt/Add/Clear`, `AddComponent`/
  `RemoveComponent`. If you think you need one, find a value-edit that achieves the same effect
  (e.g. to disable a spawned buff, zero ITS flags rather than removing the spawn entry; to disable a
  spawn, point its prefab at a harmless GUID rather than `RemoveAt`-ing the element).
- If a structural prefab edit is genuinely unavoidable, it is a **release-blocking** decision —
  it can brick player saves and "uninstall the mod" won't recover them.
- Instance-level structural edits (on a live buff/entity, not the shared prefab) are fine — they
  aren't the persistence copy source and revert when the entity is destroyed (e.g. the mounted
  control-buff edits in `ApplyMountedLoadout`).

## 5. The principle in one line

> Every ability change is a change to a **class** until proven otherwise. Find the class,
> fix the class, and prove you didn't touch the neighbours you didn't mean to.

Related: `CHAIN_AUDIT.md` (shared spawn-prefab chains), `ABILITY_AUDIT.md` (category
distribution), `TESTER_FEEDBACK_TRIAGE.md` (the open feedback classes), `ABILITY_CONFIG.md`
(per-ability override surface).
