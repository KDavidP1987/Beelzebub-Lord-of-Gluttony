# Tester Feedback Triage & Crash/Break Research

Source: Shadow Realm dev-server tester reports (Discord export in
`Reference Data/beelz-vblood/`), all against **Beelzebub 0.55.0 / BCH 0.18.x**.
Research done against the v0.100.0 code + the `Reference Data/Prefabs/` dump.

**Guiding principle (per KDPen):** these crashes only force a server/client
*restart* — no data corruption — so for each one we **research a resolution
first** and only fall back to blocking when no usable ability can remain.

**Key finding:** Almost every crash/break is **neutralizable via the existing
init-time prefab-edit pattern** (`Services/AbilityTuningService.cs`:
`prefab.With((ref Comp c)=>...)`, plus structural add/remove like the v0.85
LifeTime add and v0.72 DynamicBuffer writes). Only **Sir Erwin Mountup** has no
usable remainder → block.

Recurring root-cause classes:
1. **NPC-context server scripts** (`Script_*_DataServer`, ScriptSpawn) that
   null-deref when the caster is a player instead of the boss → **server crash**.
2. **`SpawnMinionOnGameplayEvent`** carrying NPC archetype blobs / death-mirror
   links → **server crash** or **forced player death**.
3. **Oversized `TravelBuff`** (Height/MaxRange/MaxHeightDiff) → **launch to space
   / terrain clip**.
4. **Buffs with no `LifeTime`** (or `LifeTime`=960s, or `Buff_Persists_Through_Death`)
   → **permanent stuck / invisibility**.
5. **`AbilityGroupComboState` / long no-interrupt casts / `MoveDuringCastData`
   null curves** → **client crash & slot-state desync**.
6. **`HitColliderCast`-gated summons** + unrecognized summon-name patterns →
   **summons do nothing for a solo player**.

⚠️ **Shared-prefab caution:** some target prefabs are GENERAL/shared assets.
Editing them globally affects other units. Flagged per-item below. Prefer editing
the ability-specific spawn/apply entry over a `Buff_General_*` where possible.

---

## TIER 1 — Server crashes

### 1. Dracula Spell Stone Bolt Spray — `1957691133`  ✅ NOT REPRODUCED on 0.100
**TEST 2026-06-01:** cast repeatedly on 0.100.0 — **no crash**. Log shows the fan
firing and `[Beelz CHAIN]` fixup reassigning the spawned Projectile/Buff entities to
the player (team/owner) — v0.100's chain-fixup appears to already neutralize the
predicted NPC-context NRE. **Action: testers re-verify on 0.100 (they were on 0.55).**
Chain: Group → Cast `-1651386524` → StartupBuff `-1989513093` → Buff `-877173379`.
**Root:** `AbilityProjectileFanOnGameplayEvent_DataServer.SetRandomTargetInRadiusToSpellTarget`
on Buff `-877173379` — the random-enemy lookup needs Dracula's AI/spell-target
context; null-derefs on a player caster (server-side script = no try/catch → server down).
**Resolution:** on `-877173379`, set `SetRandomTargetInRadiusToSpellTarget=0`,
`RandomSpellTargetHitFilter=None`, `SetSpellTargetToSelf=true`. Ability still
sprays projectiles in the aim direction. **Conf: Med.** If it still crashes,
strip the `RunScriptOnGameplayEvent` listener entirely.

### 2. Morgana combo — `-1980019894` (Swarm) → `1242557903` (Orb Barrage)  ✅ NOT REPRODUCED on 0.100
**TEST 2026-06-01:** **no crash** on 0.100.0 — and Swarm wouldn't even fire. The
assignment-time `Clearing entity … ProjectM.AbilityGroupSlot … modification will be
cleaned` lines are the benign ReplaceAbilityOnSlot warnings (present on every grant,
incl. Test 3 — normal). **Action: testers re-verify; "Swarm won't trigger" is a
separate Tier-3 issue.**
**Root (orig hypothesis):** both spawn `ScriptSpawn` entities carrying
`Script_SpawnThrowTowardsNearbyVampires_DataServer` + a `TargetFilterCondition`
blob the boss satisfies but a player doesn't. The combo runs two concurrent
ScriptSpawn paths on the same player entity (Swarm's 5s `Thrower -730747480`
ticking while OrbBarrage's Travel buff `376576928` also spawns one) → crash.
TriggerToPlayer prefabs: **`1912709201`** (Swarm) and **`320348317`** (OrbBarrage).
**Resolution:** strip `Script_SpawnThrowTowardsNearbyVampires_DataServer` +
`ScriptSpawn` from both TriggerToPlayer prefabs; also strip the Swarm Thrower's
`SpawnPrefabOnGameplayEvent` entry. Swarm keeps travel + fear knockback;
OrbBarrage keeps travel + Throw01/02/03. **Conf: Med-High.** Test each alone,
then the combo.

### 3. Sir Erwin "Militia Fabian Mountup" — `-1623080868`  🐎 CONFIRMED — RESOLVE (controllable mount)

**RESOLVE DESIGN (2026-06-01, research):** KDPen wants this turned into a usable
"summon a rideable horse that despawns on an unrelated cast" rather than blocked.
Feasible — **Option A**:
- **Root cause (confirmed):** the mount control buff `AB_Interact_Mount_Owner_Buff_Horse`
  (854656674) carries `ProjectM.ReplaceAbilityOnSlotData` — mounting REPLACES the bar.
  Beelz grants use their own `ReplaceAbilityOnSlot` modification sources on the same
  `ProjectM.AbilityGroupSlot` → two owners collide → the `AppendRemovedComponentRecordError`
  Burst abort. (Same class as the Fiddle slot-desync.)
- **Two mount models:** rideable = `ProjectM.Mountable`{Mounter, MountBuff} + `Interactable`
  + InteractAbility `AB_Interact_Mount_AbilityGroup` (1734264526) — on `CHAR_Mount_Horse`
  (1149585723) / `CHAR_Mount_Horse_Vampire` (-1502865710). Boss steed = `ProjectM.UnitMount`
  (NPC self-mount, NO Mountable) → not player-rideable; its rider-attach/shared-health buffs
  are the crash layer.
- **Fix:** on Mountup cast for a Beelz player — suppress the native steed; spawn a vanilla
  horse (vampire one = thematic/immortal); **clear Beelz grants, mount the player (set
  `Mountable.Mounter`=player + apply MountBuff 854656674/-978792376, or force-cast 1734264526),
  re-apply grants on dismount** — the single-bar-owner discipline (same as shapeshift forms)
  is what removes the slot collision.
- **Despawn-on-unrelated-cast:** in `AbilityCastStartedSystemPatch`, track `(steamId→mountEntity)`;
  if the player has a Beelz mount and casts an ability whose name lacks "Fabian"/"FabiansSteed"
  (excluding interact-mount 1734264526 / demount roll 744300418 / `AB_Demount` -230952990),
  dismount + `SummonAllyService.EnqueueAdminDespawn` (staged drain — NEVER destroy inline).
- **Recovery (needed NOW for the stuck test char):** admin cmd strips rider/mount buffs
  (105565888, 1666453956, 1182509317, 1542512910, -863496809, 854656674/-978792376; sweep
  BuffBuffer for names containing MountRider/Mountup/FabiansSteed/Mount_Owner_Buff), clears
  `UnitMounter`/`Mounter`, re-applies grants (`SlotApply.ApplyAll`), destroys the orphan steed
  (826666431 + minion -1509286426 via radius query → staged despawn).
- **Reuse:** KindredCommands `SpawnNpcCommands.SpawnHorse` (clean horse spawn + Mountable edit);
  Bloodcraft `IsMounter()`/`GetUnitMountEntity` (resolve rider's mount), `TryApplyBuff`/`TryRemoveBuff`;
  in-mod `SummonAllyService` despawn queue, `SlotApply.ApplyAll`, `AbilityRegistry.Clear*Bucket`,
  `ForceCastService`, `Heartbeat`/`TransformService.Tick` drain.
- **Load-bearing uncertainty (test in-game):** the mount handshake — direct `Mountable.Mounter`
  set + MountBuff vs force-casting interact 1734264526 — which yields proper input/camera control.
  Build in stages to de-risk.

ORIGINAL (block) analysis retained below for reference:

**TEST 2026-06-01 (server crash + persistent corruption):** spawns
`CHAR_Militia_FabiansSteed` (Entity 632585:8); summon-link **SKIPS** it (`NO recent
summon cast registered … untransformed`) → uncontrollable AI steed under the player.
On the **next cast/move** the server aborts from a Burst job:
`System.ArgumentException … EntityComponentStore::AppendRemovedComponentRecordError(Entity, ComponentType)`.
Mechanism: the mounted state replaces the ability bar, colliding with Beelz's
slot-grant modifications (the `modification source for ProjectM.AbilityGroupSlot`
cleanup seen on assignment) → ECS structural error. **Player is left STUCK on the
steed; survives relog; re-crashes on move/cast.**

⚠️ **`denyguid` is BYPASSED in `Capture_InclusiveMode` (current test default)** — only
the `_alwaysJunk` list + per-ability `Enabled=false` survive inclusive mode, and
**neither stops an already-slotted copy from casting** (no cast-time cancel exists).
**Containment needed:** (a) an inclusive-safe hard block at **grant AND cast** (a
danger-GUID set honored regardless of inclusive mode + a cast-start cancel);
(b) a **runtime guard** that despawns the steed / strips the rider-attach the instant
it spawns for a player (defense in depth); (c) a **recovery** path for already-stuck
characters (strip mount-rider + kill orphaned steed) — needed NOW for the test char.

Chain: Cast `-1682625133` → Travel_Phase `-1988219418` → SpawnMount `1615695021`
(`SpawnMinionOnGameplayEvent` → steed NPC) → MountRider buffs (`105565888` Attach
w/ `MilitiaRiderBuff` + `SetBuffTargetBehaviourTreeStateToOwner`; `1666453956`
`Script_SharedHealthPoolBuff_DataServer`).
**Root:** three independent NPC-pair-bound crash vectors (steed spawn blob, rider
BT-state on a non-BT player, shared-health script needing both halves).
**Resolution:** none leaves a usable ability — stripping all three yields only a
0.6s hop. **Recommend block** (deny-guid `-1623080868`). **Conf: High.**

---

## TIER 2 — Client/game crashes

### 4. Gaius "Twinblade Throw" — `1322698651`  ✳ neutralize (test which)
Chain: Cast `-215946678` → Trigger `1647360591` → Throw `599983966` → SpellObject
`-1244341833` → Projectile `660830020`.
**Root (two candidates):**
- SpellObject `-1244341833` runs `RemoveBuffOnGameplayEvent` on the player's own
  `AB_Vampire_TwinBlades_Javelin_Recast_Dash` buff (`469216240`) — a Dash/move-control
  buff; client mishandles a remove for a dash state it never started.
- Projectile `660830020` has `Script_HomingSpell_DataShared` (scripted net entity
  expecting the Gaius rig).
**Resolution:** strip `RemoveBuffOnGameplayEvent`(+Entry) from SpellObject; strip
`Script_HomingSpell_DataShared`/`HomingSpellTag`/`ScriptSpawn`/`ScriptUpdate` from
Projectile. If still crashing, null the Throw's `TargetAOESequence` (Gaius throw
anim on the player skeleton). **Conf: Med** — in-game timing test isolates which.

### 5. Gloomrot Technician Fiddle — `1485838951` (and Bell Ringer class)  ✳ neutralize
Symptom: with Fiddle bound to sword, the **vanilla sword E** crashes the client.
**Root:** Fiddle group carries `AbilityGroupComboState{ComboLength:2}` (sword E has
none) → combo-index/slot-state desync after the slot reverts; plus Cast01 `197978642`
& Cast02 `-749784555` have `MoveDuringCastData{MoveType:MovementCurve, CurveX/Y/Z:null}`
— null NPC animation-curve blob crashes the client movement sampler.
**Resolution:** strip `AbilityGroupComboState` from `1485838951`; strip
`MoveDuringCastData` from both casts. Bell Ringer is a sibling class (7s
no-interrupt cast → slot desync) — verify after Fiddle fix. **Conf: Med-High.**
This is the most likely culprit behind the broader "vanilla E crashes when X is
bound" reports — a general combo/long-cast slot-desync class worth a systemic guard.

---

## TIER 2 — Character launch / terrain clip

> All are oversized `TravelBuff`s; pure field clamps, no component removal.

### 6. General Elena "Tower of Frost" — `1431473799` & `2057952818`  ✳ neutralize
Both share Cast `1668005661` → IcicleSpawnThrow `1573363803` → on-hit ApplyBuff
**`AB_ChurchOfLight_Overseer_IceRecovery_LaunchBuff 2030766507`**.
**Root:** that launch buff is `TravelBuff{Height:4, MaxRange:9, MaxHeightDiff:3,
SnapToEndPositionOnDestroy:true}` — fine vs a stationary boss target, "to the moon"
on a player. Self-travel `2017147638` is secondary.
**Resolution:** clamp `2030766507` to Height≈0.5, MaxRange≈3, MaxHeightDiff≈1 (and
TravelBuffSpawn MinRange/MaxRange≈2-3). **Conf: High.**
⚠️ **Shared:** `2030766507` also used by the ChurchOfLight Overseer boss — global
edit reduces that boss's knockback too (acceptable; boss isn't granting abilities).

### 7. Albert/Toad King "Swallow"+"Spit" — `1292896032` / `-1238687119`  ✳ neutralize
Swallow attaches the target to the toad's belly (`TargetSwallowedBuff -915145807`).
Feeding mid-swallow races the force-cast Spit → **Spit_LaunchBuff `1750172438`**
(`TravelBuff{Height:8, MaxRange:16, MaxHeightDiff:10}`) launches the caster.
**Resolution:** clamp `1750172438` to Height≈2, MaxRange≈6, MaxHeightDiff≈3; and
add `BlockFeedBuff` to `-915145807` so you can't feed while swallowed (kills the
race). **Conf: High.** (Tester also wants: not locked-in-place, recast swallow→spit,
no forced 14s spit — separate balance/UX, not crash.)

### 8. Ziva "Jetpack Take Off" — `-1770586075`  ✳ neutralize
Chain: Cast `-1194965669` → TakeOff_Phase `-64994941` → FlightBuff `-149348905`
(20s) → Land_Phase `-503852392`.
**Root:** Land_Phase `TravelBuff{DenyLowerHeight:true, MaxRange:18, ...}` — the
`DenyLowerHeight` flag forces an at-or-above endpoint, clipping into ceilings/terrain.
**Resolution:** on `-503852392` set `DenyLowerHeight=false`, Height≈1, MaxRange≈6,
MaxHeightDiff≈2; reduce FlightBuff `-149348905` LifeTime 20→~4s. **Conf: High.**
⚠️ Cast has `RunScriptOnCastStarted/PreCastEnded` that may also drive physics —
verify in-game the clamps fully tame it.

---

## TIER 2 — Character corruption (invisibility / forced death)

### 9. Spider Baneling "Explode Poison" — `-891106318`  ✳ neutralize
Chain: Cast `-68475657` → Hit `-2042355927` → ApplyBuff **`Buff_General_HideCorpse 1160901934`**
(no `LifeTime`, has `Buff_Persists_Through_Death`). The baneling self-destructs;
on the original NPC the entity is recycled, but a player persists → **permanent
invisibility** surviving death/relog/unequip.
**Resolution (preferred, avoids shared-buff edit):** strip the
`ApplyBuffOnGameplayEvent` entry that applies HideCorpse from the baneling's `_Hit`
prefab `-2042355927` — explosion/damage/poison still fire. **Conf: High.**
⚠️ **Shared:** `Buff_General_HideCorpse` is a GENERAL corpse-hide buff used widely —
do NOT add a global LifeTime to it (would affect every corpse-hide). Edit the
baneling chain instead. (Also worth a safety net: a respawn cleanup that strips
HideCorpse from any player — see "Systemic guards" below.)

### 10. Leandra ShadowStep `1325722355` / TrippleBolt `-1795148379`  ✳ neutralize
Both spawn shadow-clone minions via `SpawnMinionOnGameplayEvent` with
`TriggerMasterDeathActionOnDowned:true` + **`MasterDeathAction:Kill`** — when a
clone dies, the master (player) is instantly killed regardless of HP.
Prefabs carrying the buffer: `450546330` (ShadowStep_Phase), `-1721058902`
(ShadowSoldier_Summon — also used by #B8), `1132277864` (TrippleBolt_Centre), and
almost certainly `TrippleBolt_Left`/`_Right` (verify GUIDs `159545095`/`743487414`).
**Resolution:** on each buffer set `TriggerMasterDeathActionOnDowned=false`,
`MasterDeathAction=None` (DynamicBuffer write, same pattern as v0.72 healing fix).
**Conf: High** (mechanism) / Med-High (buffer write — confirm `MasterDeathAction`
enum value & read Left/Right first).

---

## TIER 3 — Stuck / lock (non-crash)

### 11. Gaius "Corpse Buff" — `-89125940`  ✳ neutralize
Buff `Undead_AreanaChampion_CorpseBuff -485230865`: **no `LifeTime`** +
`DisableAggroBuff` + huge `BuffModificationFlagData` (Immaterial/GhostMode/Immortal
composite) → permanent locked/phased/non-targetable.
**Resolution:** add `LifeTime{Duration≈3s, EndAction:Destroy}` to `-485230865`.
**Conf: Very High.**

### 12. Treant & Golem "Fall Asleep"  ✳ neutralize (or exclude)
Sleeping-idle buffs have `LifeTime.Duration=960` (16 min) + movement-impair flag,
and `ForceCastOnGameplayEvent`→WakeUp on destroy. Buffs:
`AB_Treant_FallAsleep_Buff_SleepingIdle -1206507658`,
`AB_Treant_Corrupted_FallAsleep_Buff_SleepingIdle -293207555` (+ Golem variants).
**Resolution:** reduce each `LifeTime.Duration` to ≈3s → self-clears and auto
force-casts WakeUp, freeing the player. **Conf: Very High.** (Or treat the
sleep/wake pairs as junk/exclude — they're useless solo.)

---

## TIER 3 — Broken summons (cluster)

Affected (animate but spawn nothing solo): Grayson/Stalker Reinforcement `-1749428209`
(grp `1106149033`), Quincey/Tourok Call Reinforcements `-3835897`, Octavian Call Adds
`1870073064`, Spider Queen Spawn Adds `-1779071085` (combo of 4), Wolf Boss Howl
`247740796`, Poloma Pixie `-1036943907`, Infiltrator Army of Shadows `1986068244`,
Bishop Shadow Soldier `461701172`.

**Common roots:**
- **(all)** `_Summon` is a `DestroyOnSpawn` trigger whose `HitColliderCast`
  (`OnSpawn`, `Hittable`, ≤4) only fires `SpawnMinionOnGameplayEvent` if it detects
  nearby hittable entities → a solo player in open world spawns nothing.
- **(B3-B8)** the ability names (`_CallAdds_`, `_SpawnAdds_`, `_Howl_`, `_Pixie_`,
  `_ArmyOfShadows_`, `_ShadowSoldier_`) aren't in `SummonNamePatterns`
  (`Patches/AbilityCastStartedSystemPatch.cs`) → never tracked/adopted as allies.
  Bishop even has a `SummonTargets` entry that's unreachable for this reason.
- **(Howl/Pixie/Army)** extra `ConditionBlob` gates on the listeners (boss context).

**General fix (iterative, Med effort):** use the existing manual-spawn machinery
(`SummonAllyService` + `SummonTargets`): (a) extend `SummonNamePatterns` with the
missing fragments; (b) add `SummonTargets` entries mapping each ability → its CHAR_
unit GUID + count (discover the GUID via `VerboseLogging` + a cast against an
in-range target, read the LinkMinion log); (c) Bishop is a one-line pattern fix.
Priority: Reinforcement (B1/B2) and Bishop (B8) are lowest-lift; Infiltrator (B7,
multi-step buff-owner chain) is highest — consider deferring/excluding.
**Conf: High** for the pattern/name analysis, **Med** for ConditionBlob behavior
(needs an in-range test).

---

## TIER 4 — Balance: "locked in place" (already mechanically solved)

The single most common complaint (dozens of abilities root the player during cast)
is **already addressable** with the v0.87+ tuning knobs — no code needed, just
config-data curation per ability:
- windup root → `.beelz admin ability <id> freelymove <sec>`
  (`ModifyMovementDuringCastData.Duration`)
- channel root → same knob also clears `BuffModificationFlagData` MovementImpair on
  the spawned channel buff.

Action: build a curated `ability_rules.json` pass of `freelymove`/cooldown entries
for the worst offenders named in the reports (Toad King kit, Stone Breaker, Frost
Archer, Bishop of Death, Spider Queen, Foreman, etc.). Data task, not code.

Other balance flags (own pass): no-cooldown offenders (Errol Mountain Rumbler),
OP exploits (Gargoyle Wing Shield `1460741503`/`1637511208` immortality+heal; Rat
Vanguard `830495620` infinite invuln summons), invisibility that doesn't hide
(Keely/Polora camouflage), summon-cap UX ("wait for ALL to die").

---

## Proposed implementation: `AbilitySafetyService`

A new init-time service (sibling to `AbilityTuningService`, run after the prefab
map is ready and on `.beelz admin reload`) that applies the Tier 1-3 prefab
neutralizations above, each guarded + logged, behind a config toggle
(`Safety_NeutralizeBreakingAbilities`, default on). Group edits so each is
independently verifiable in-game. Pair with a small **deny list** for the
genuinely-unsalvageable (Sir Erwin Mountup) honored even in InclusiveMode.

**Systemic guards worth adding** (defense in depth):
- respawn/player cleanup that strips known leak buffs (HideCorpse, CorpseBuff,
  sleep, immaterial composites) from any player entity each tick/on-downed.
- a generic "combo-state / long-no-interrupt cast" guard for granted abilities to
  prevent the Fiddle/Bell-Ringer slot-desync class wholesale.

**Validation loop:** build → deploy to dev server → testers re-cast the specific
IDs above → confirm crash/lock gone & ability still does something useful → iterate
on the Med-confidence ones (Dracula, Morgana, Twinblade, summons).
