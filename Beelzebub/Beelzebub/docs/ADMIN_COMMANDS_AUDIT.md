# Admin-command audit — validity + the ability-shaping tester report

Audit prompted by a BCH admin-tester report on the `.beelz admin` ability-shaping
commands. Two questions: (1) is each admin command **valid** (registered, parses,
applies), and (2) for commands that must **apply to a player**, do they?

## 0. CRITICAL CONTEXT — version gap

The tester server (`D:\home\sid_2021275\…`) runs the **last PACKAGED build, v0.100.0**.
Local dev is **v0.107.0** — everything since 0.101 is unshipped. So:

- A few findings are likely **already fixed** on 0.107 (e.g. `interruptonhit on` parses
  correctly in current code — `ParseOnOff("on") == true`; the tester's "expects on|off|clear"
  rejection cannot occur on 0.107).
- The **shaping engine (`AbilityTuningService`) is essentially unchanged** 0.100→0.107, so
  the structural bugs below ARE reproducible on current code.
- **Action:** package a current build for the testers and re-run this matrix before
  treating any single finding as still-open.

## 1. Command surface — all registered & valid

All 50 `.beelz admin` verbs are registered VCF commands (compile-checked): `ability`,
`tune`, `tune-list`, `give`, `devour`, `revoke`, `dump`, `inspect`, `scan-abilities`,
`reload`, `broadcast`, `broadcast-msg`, `allow/deny/allowguid/denyguid/unallow/undeny`,
`set/set-slot/clear-slot/set-weapon-slot/…`, `force-transform/give-transform/revoke-transform`,
`reset-character/reset-loadouts/respawn/rebuildbar/rebuildslots`, `unmount/testmount/testform`,
`desummon/desummon-all`, `freeze-captures`, `copy-collection/paste-collection`, `snapshot`,
`wipe-all`, etc. No dead/unwired verbs found.

**Player-applicability:** per-player commands (`give`, `devour`, `revoke`, `reset-character`,
`respawn`, `force-transform`, `set-slot`, `copy/paste-collection`, `unmount`, …) take a player
argument and resolve it. The **shaping** commands (`ability`/`tune`) are by design **GLOBAL
prefab edits** — they retune the ability for *every* player who has it (the capture/grant model
carries the tuned version to each player). That is the intended "applies to a player" path:
tune globally → all holders get it. There is no per-player ability-shaping (and shouldn't be —
it's a shared prefab).

## 2. Root-cause CLASSES (each maps several tester findings)

### A. AoE-delivered effects sit DEEP in the spawn chain — CORRECTED by `tools/trace_walker.py`
The first hypothesis ("just follow `ApplyBuffOnGameplayEvent`") was **DISPROVEN by tracing the
actual tester cases**:
- **Spider Queen AoE** (range/projspeed ❌): broadening adds **+0** prefabs — there is **no
  `Projectile` anywhere**; it's a `TargetAoE` thrown effect. `range`/`projspeed` are simply the
  **wrong fields** (nothing to speed up). NOT a coverage bug.
- **Nun AoE heal** (❌): the heal lives on `AB_Nun_AoE_Ground_Delay` (`HealOnGameplayEvent` 5%/tick),
  reached via `Cast → AbilitySpawnPrefabOnCast → AoE_Throw → **SpawnPrefabOnDestroy** → Ground_Delay`
  (depth 3, a spawn edge the walker doesn't follow — and `ApplyBuffOnGameplayEvent` doesn't reach it
  either). One hop further is **`Buff_General_FadingSnare`**, a **SHARED generic buff**.
- **Working cases** (Chain Bolt projectile, Priest/Cleric targeted heals): their effect prefab is
  **already inside current reach** (1–2 hops, ability-specific). Broadening would NEWLY touch their
  side-prefabs (Chain Bolt's `Unholy_Debuff`/`Snare_Buff`) — i.e. it **risks the working cases**.

**Conclusion:** a GLOBAL walker broadening is both **insufficient** (AoE effects need deeper, varied
spawn edges like `SpawnPrefabOnDestroy`) **and dangerous** (deeper reach hits SHARED generic buffs —
`Buff_General_*` — so `duration`/`forcetimeout`/`lifetime` would bleed across every ability that uses
them). **Do NOT broaden the global walker.**

**Safe fix instead (two parts):**
1. **Honest non-applicability feedback (zero risk):** at set-time, check whether the field's target
   component is reachable for that ability; if not, reply *why* (e.g. "projspeed: this ability has no
   projectile" / "healing: this AoE delivers its heal through a chain we don't retune") instead of a
   silent no-op. Converts confusing ❌s into clear admin info.
2. **Surgical deeper-reach WITH a shared-prefab guard (targeted, validated):** for `healing`/`aoe`/
   `projspeed` only — fields whose components live on ability-specific effect prefabs — extend the
   walk to follow `SpawnPrefabOnDestroy`/spawn edges BUT skip any prefab whose name isn't
   `AB_<thisAbility>_*` / is `Buff_General_*`/shared. Never extend the risky LifeTime/duration fields
   into shared buffs. Validate per-case with `trace_walker.py` before shipping.

### B. Force-timeout semantics — compresses instead of cancelling
Tester: "it alters the SPEED of the ability instead of cancelling it." `forcetimeout` sets
`LifeTime.Duration` on the spawned buff; when that buff drives the channel/animation, shortening
its lifetime plays it faster rather than ending it cleanly. Tester wants **two** behaviors split:
(i) a clean **cancel/expire after N s**, (ii) the speed effect as its own knob.

**Fix (design):** `forcetimeout` should add a `LifeTime` + `Destroy` only to genuinely
*indefinite* buffs (no existing LifeTime), and NOT shorten a buff whose LifeTime drives timing.
For finite/timing-driving buffs, a clean cancel needs a different mechanism (interrupt/`DestroyOnGameplayEvent`).
Confirm with the user whether to split into `forcetimeout` (cancel) + a separate playback knob.

### C. Cast-speed knob does nothing
`castspeed` writes `ModifyMovementDuringCastData.MovementSpeedMultiplier`. Tester: no effect (and
suspects force-timeout is doing what castspeed should). Likely the tested abilities don't carry
`ModifyMovementDuringCastData`, or the write is on the cast prefab while the movement lock lives
on a spawned channel buff (same component-location issue as the old freelymove bug). Needs a
per-ability dump to confirm where the movement component actually is.

### D. Channel/non-standard casts — interrupt & free-move miss
`interruptible` / `interruptonhit` / `freemove` / `freelymove` write `AbilityInterruptData` /
`ModifyMovementDuringCastData` on the CAST prefab. For Mountain Rumbler / Arcane Imprisonment
(channels) those components are absent or the relevant ones live on a spawned channel buff, so the
write no-ops. Same root family as A/C — the field's target component isn't where we write.

### E. Summon governance — quantization + hard cap
`summonunits`: 1-2 → spawns 3, 3-6 correct, 7+ stuck at 6; default Raise Dead spawns 6 (3 with the
native unholy-circle, 3 custom). The override partially drives the count but the native spawner has
its own min/structure (the "unholy circle" ones are the boss's native spawns; ours are additive).
Needs a look at how `summonunits`/`summoncap` map onto the unit-spawner buffer vs the native spawn.
**`summontimeout` WORKS ✅** (despawned all 6 at 10 s).

### F. Charges / charge-time — apply only to charge-system abilities
`charges`/`chargetime` only affect abilities that already carry `AbilityChargesData` (the command
warns when they don't). The tested abilities likely have no charge system → "no effect" is
*expected*, not a bug — but verify the warning is actually shown via the `ability` path and the
test targets weren't charge abilities.

### G. Confirmed WORKING (no action) — from the report
`cooldown` (Spider Queen AoE ✅), `range`/`projspeed` on **projectiles** (Chain Bolt ✅),
**targeted** healing (Priest Heal Bomb, Cleric Heal ✅), `damage` on single/2-part manual
(Toad Double Tongue Slap ✅), `duration` (Cleric Heal, Tourok Parry, Arcane Imprisonment ✅),
`summontimeout` ✅. The split is consistent: **single-target/projectile/cast-prefab fields work;
AoE-delivered + channel-spawned fields fail** → all point back to class A/D.

## 3. Recommended fix priority

1. **Class A (walker coverage)** — single highest-value fix; recovers AoE range/projspeed/heal/
   radius + multi-hit damage at once. Do it carefully (high blast radius; extend `_originals`,
   validate a sample before/after, cap BFS depth).
2. **Class B (force-timeout split)** — needs a quick design confirm from the user (cancel vs speed).
3. **Class C/D (cast-speed + channel interrupt/free-move)** — per-ability dump to locate the real
   component, then route the write there (same pattern as the freelymove channel fix).
4. **Class E (summon quantization)** — investigate the spawner count mapping.
5. **Re-test everything on a fresh 0.107 package** — closes the version-gap unknowns (incl. the
   already-fixed `interruptonhit` parse).

## 4. Not-bugs / clarifications
- `interruptonhit on` rejection — **fixed on 0.107** (version gap).
- Global shaping "not applying to a player" — by design global; affects all holders.
