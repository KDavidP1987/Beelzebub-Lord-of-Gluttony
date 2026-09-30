# Plan: Beelzebub per-ability configuration overhaul  (rev 5 — after Codex rounds 1-4)
_Locked via claudex-loop — by Claude + KDPen (2026-09-23). All decision-sheet recommendations were
accepted as-is (`~/.claude/plans/lovely-dazzling-pancake.md`). Rev 2-5 fold in Codex round-1..4 findings
(see PLAN-REVIEW-LOG.md)._

## Goal
Make captured-ability configuration **complete, correct and fair**: every knob applies identically to
every player (server-wide); damage tuning is **two-way** (nerf or boost) and always **relative to the
caster's own power** (Spell/Physical power incl. level, gear, potions) so low- and high-level-unit
abilities balance out and players reset to level 1 see captured abilities weaken accordingly. Fix the
confirmed restriction/tuning bugs, add the missing shape knobs, and **keep vanilla boss/NPC fights
untouched wherever a runtime path exists.**

Code root: `Beelzebub/Beelzebub/` (C#, net6.0, BepInEx IL2CPP, server-only). v0.131.0, `ApiVersion` 28.

## Approach — three phased releases

### P1 — Chain graph + `inspect` + bug fixes + docs  (low risk; produces data + infra for P2/P3)
1. **One ability chain graph** (new `Services/AbilityChainGraph.cs`) — the single traversal used by
   inspect, damage attribution, shared-prefab detection, tuning and restore.
   - Built at init (after prefabs load) from **every** `AbilityGroup` in the prefab collection (not just
     registered/captured ones — captured status is metadata), so bosses and unregistered abilities count
     when deciding "shared".
   - Edges: group → `AbilityGroupStartAbilitiesBuffer` casts; cast → `AbilitySpawnPrefabOnCast` /
     `AbilitySpawnPrefabOnStartCast`; any node → `SpawnPrefabOnGameplayEvent`, `SpawnPrefabOnDestroy`,
     `ApplyBuffOnGameplayEvent` (Buff0–3), projectile-fan/multishot/cluster `NewProjectile*` fields,
     `SpawnMinionOnGameplayEvent` (minion list from blob is read-only; read if decodable, else note).
   - Bounded BFS (depth ≤ 8, visited set, cycle-safe). **Completeness tracked per group/node** (rev 3):
     depth truncation, undecodable edges (e.g. blob minion lists) or component decode failures mark the
     group `incomplete`; every node reachable from an incomplete group is treated as **shared/unsafe**
     for baked edits.
   - Outputs: `nodes(group)`, `owners(prefab) → set<group>`, `IsShared(prefab)` (= >1 owner group
     **or** reached from an incomplete group).
   - **Unknown targets (rev 4):** an unresolvable edge can point at a prefab another group reaches
     normally. So: (a) minion/unit prefabs (the blob-list case) are never baked-edit targets; (b) for any
     other edge class with ≥1 unresolved target anywhere, shared-sensitive baked edits to prefabs of the
     component class that edge produces are disabled globally and reported by `inspect` + startup log.
   - Rebuilt only in the atomic reload sequence (step 13).
2. **`.beelz admin inspect <ability> [export]`** (new `Services/AbilityInspectService.cs` + command in
   `Commands/AdminCommands.cs`). Read-only. Walks the graph and prints per prefab: cooldown, cast time,
   charges, `AbilityGroupInfo.MaxRange`, `Projectile` Speed/Range, `LifeTime`, `TargetAoE`,
   `HitColliderCast` (radius, target count), `DealDamageOnGameplayEvent` Parameters (MainType,
   MainFactor, RawDamageValue, RawDamagePercent, ResourceModifier) + DamageModifierPerHit,
   `HealOnGameplayEvent`, `ApplyBuffOnGameplayEvent` (OverrideDuration, Stacks),
   `ApplyKnockbackOnGameplayEvent`, `Buff.MaxStacks`, projectile-count scripts, `SpawnMinion` Count,
   `SpellModArithmetic(Modifiable)` targets, plus `shared-with: <other groups>` and **warnings** for damage
   terms the damage system won't scale (flat, %-HP).
   - `export`: all abilities → `BepInEx/config/kdpen.Beelzebub/inspect_export.csv`. Invariant-culture
     numbers, RFC-4180 quoting, deterministic ordering, a `status`/`error` column per row, written to a
     temp file then atomically replaced. Summary line reports rows, decode failures per component; if a
     *required* component (DealDamage Parameters) fails to decode for >0 rows, the command reports FAILED
     rather than a plausible-but-incomplete export.
3. **Bug fixes** (confirmed in recon):
   - Single **field table** (name, aliases, baked?, parser, validator) drives the `ability` command parser,
     `IsBakedTuningField` (fixes the missing `leapheight` + aliases), help text and docs.
   - `ShapeshiftAbilityService.ApplyFormLoadout` fallback (~:280-294) filters by `IsEnabled` **and**
     `IsUsableInForm`; the per-form bucket path also re-checks `IsEnabled`.
   - `AbilityRules.ClassifyWeaponFamilies` (~:792): `!`-only list = universal minus blocks.
   - `ApiCommands` `api info`: emit form blocks with `!`, and weapon blocks (additive → ApiVersion 29).
   - `DualHammers` rejected by the weapon parser with a message; `WeaponFamily.cs` comment fixed
     (`Unarmed` is concrete).
   - Validation helpers for all numeric fields (finite, range-bounded; reject NaN/∞/negative where
     meaningless) used by runtime, API and inspect alike.
4. **`forcetimeout` → runtime-only instance expiry** (`AbilityTuningService` ~:420-440, 603-621, 824)
   (rev 3: no prefab edits at all — changing finite `LifeTime` compresses timing-driving buffs and leaks
   to the boss; `duration` remains the knob for that):
   - Remove all `forcetimeout` prefab writes (`AddComponent`/`RemoveComponent`/LifeTime).
   - New minimal **`Services/CastHistoryService.cs`** (rev 4) fed from `AbilityCastStartedSystemPatch`:
     per player, recent casts `{abilityGroup, castTime, fromSlot, captured?, snapshot}` — **all** player
     casts are recorded, captured or native (rev 5); P2's provenance ledger
     (step 8) extends this service rather than adding a second one.
   - Buff *instances* spawned by a player-owned captured cast are tracked (key = Entity index+version)
     when `BuffSpawnServerPatch` sees them; spawn world time recorded then; expiry evaluated every frame in
     `HeartbeatBehaviour.Update` against server world time → `DestroyUtility.Destroy`. Entries dropped
     when the entity disappears or on disconnect. **Trackers survive reload** (rev 4) — each carries its own
     timeout value snapshotted at cast, so an indefinite buff already under a timeout still expires.
     Attribution: "the buff GUID's owners ∩ that player's retained casts (captured **and** native) yields
     exactly one group, and that group is captured", else skip + log.
   - Migration: prefab structural edits live only in-process (prefabs are rebuilt at server start), so the
     deploy restart clears any previously added `LifeTime`; the restore path stops calling `RemoveComponent`.
     Documented in CHANGELOG as "restart required".
5. **`.beelz cast` / hotkeys honor Weapons/Forms** (`BeelzCommands.cs` ~:223) via
   `SlotApply.IsGrantCompatible` / `IsUsableInForm`; explicit weapon-bucket binds re-check `!` blocks at
   injection time (`SlotApply.IsGrantUsable` ~:123).
6. **Shared-prefab guard for all existing spawn-level knobs** (moved up from P3): a baked edit to a prefab
   whose `owners()` contains a group *other than* the one being tuned is skipped + logged, unless the entry
   sets `AllowGlobalSharedEdit: true` — which prints every affected group (incl. bosses) when set, and
   is honoured **only when ownership is complete** (rev 5): graph-incomplete / unknown-edge-class
   disables are non-overridable. Shared
   edits are resolved deterministically (rev 3): owners processed in sorted GUID order; if all requesting
   owners ask for the **identical** value it applies, otherwise the field is rejected for **all**
   conflicting owners with a warning naming them. Restore computes the desired value from all owning rules.
7. **Docs**: `ABILITY_CONFIG.md` §4 regenerated from the field table; stale "coming next" and dead
   `ABILITY_MAP_FORMAT.md` refs removed; restart/save caveats. BCH handoff → ApiVersion 29.

### P2 — Damage system (power-relative, two-way, player-cast only)
8. **Cast provenance ledger** (new `Services/CastProvenanceService.cs`), fed from the existing
   `AbilityCastStartedSystemPatch` (which already handles slot casts, hotkeys and `.beelz cast` force-casts).
   Per player: recent casts `{abilityGroup, castTime, fromSlot, snapshot}`, where **snapshot holds the
   effective values directly** (scale, clamp bounds, flat policy, timeout) — no generation IDs to keep
   alive (rev 4). Retained 120 s (cfg `Damage_ProvenanceRetentionSeconds`, **floor 30 s**, and a startup
   warning listing captured abilities whose known chain lifetime exceeds the setting or is indefinite).
   Captured-but-unslotted casts are included.
   - **Attribution (rev 3, fail-safe):** a chain entity gets provenance on first sight (key = Entity
     index+version): candidates = `owners(prefab)` ∩ the owning player's retained casts — **native and
     captured casts alike** (rev 5), so a native ability sharing the prefab creates ambiguity instead of
     being misattributed. **Exactly one distinct candidate group AND it is captured → attributed.
     Otherwise → unscaled (vanilla) + verbose log.** No recency tie-breaking, never "currently bound".
   - Cached only on **ephemeral chain entities** (prefab GUID is a graph node and the entity is not a
     character/player/slot-state/weapon).
   - **Persistent sources (rev 4, concrete):** if `SpellSource` is a persistent entity whose prefab GUID
     *is* a graph node (e.g. a per-player ability cast/state entity), attribute **per event** with the same
     `owners(prefab) ∩ retained casts → exactly one` rule, without caching. If `SpellSource` is the
     character itself or any non-graph entity → **unsupported, vanilla** (no correlation key exists).
   - Cache lives with the entity (dropped when it no longer exists), so indefinite buffs, delayed
     projectiles and post-swap/post-unbind hits keep their attribution; the retention window only bounds
     how long a *new* entity can be matched to a cast.
   - Values come from the matched cast's snapshot. Known edge: an entity first seen after a reload that
     happened between two casts of the same ability uses the newer cast's snapshot — accepted (only when
     an admin reloads mid-combat).
   - **Telemetry (rev 4):** per-ability counters `attributed / ambiguous / unmatched / unsupported`,
     shown by `inspect` and `api info`.
   - Ownership walk: visited-set, bound 8 hops, truncation logged.
9. **Spike (gate for step 10)** — prove safe mutation of `DealDamageEvent` in the Harmony prefix on
   `DealDamageSystem.OnUpdate`:
   - **Measure, don't assume** (rev 3): identify the systems that create `DealDamageEvent` entities and
     confirm they run before `DealDamageSystem` in the same frame; verify written values are the ones
     consumed (target HP delta matches the written factor). If the producer dependency or write safety
     cannot be established, **the gate fails**.
   - Mutation paths, in order: interop property setters → modified copy via `SetComponentData`. **Raw
     pointer writes are NOT a production path.**
   - Build a damage **truth table** from logged events (event entity, source GUID, MainType, MainFactor,
     RawDamageValue, RawDamagePercent, ResourceModifier, Modifier, target HP before/after) across direct
     hits, projectiles, AoEs, DoTs, delayed impacts, crits, PvE level difference.
   - Set an explicit Harmony `Priority` and log other patches on `DealDamageSystem.OnUpdate` at startup.
   - **If the spike fails** (rev 3, explicit failure state): the per-hit path is not shipped; the existing
     power window stays as the damage path under a renamed setting `Damage_LegacyPowerWindow` (default on,
     today's behaviour, made two-way); an admin may instead opt into `Damage_AllowBakedFallback=true`
     (baked `MainFactor`, value-only, captured, step-6 shared guard, boss leak accepted), which disables
     the legacy window. The two are mutually exclusive. No silent fallback.
10. **Per-hit scaling** (in `DealDamageSystemPatch`): for events with P2 provenance →
    `scale = Defaults.DamageScale × entry.DamageScale × (Grant_PowerScalingMode=Boosted ? factor : 1)`
    (snapshotted at cast). **Only the power term is scaled** (`MainFactor`); then
    `MainFactor` is clamped into `[Damage_MinFactor, Damage_MaxFactor]` (bounds on the *final factor*,
    0 = bound off; min ≤ max validated at load). Entries with `MainFactor ≤ 0`, healing, and non-Physical/
    Spell `MainType` are untouched. Semantics finalized from the step-9 truth table before shipping.
    O(1) lookups, no allocations, early-out when nothing configured.
11. **Flat & %-HP terms** (D3) as explicit policies, not a frequency threshold:
    `Damage_FlatPolicy = Unchanged | Scaled | PowerRelative` (default Unchanged). `Scaled`:
    `raw × scale`. `PowerRelative`: `raw × scale × casterPower(MainType) / Damage_ReferencePower` — only
    if the truth table shows the raw term is not already power-scaled. `Damage_PercentPolicy = Unchanged`
    (only option this round).
    **Documented prominently (rev 3):** `DamageScale` scales the power-derived term; flat damage follows
    `Damage_FlatPolicy`; %-HP damage is never scaled. `inspect` and `api info` report two separate things
    (rev 4): `term_coverage = full | partial | none` (static, from the damage terms) and
    `attribution = reliable | unreliable | unsupported` (runtime, from the counters: `unreliable` when
    ambiguous+unmatched exceed 20% of that ability's hits over ≥ 50 hits). Abilities are only advertised
    as damage-tunable when both are good; the 20%/50 threshold is re-evaluated from spike data.
12. **Summon power** (D4): the originating ability GUID is passed explicitly through the exact summon
    records — manual spawns (`AbilityCastStartedSystemPatch` `SummonTargets` path knows the ability) and
    the existing cast→`LinkMinionToOwnerOnSpawnSystemPatch` credit record (`AbilityCastStartedSystemPatch`
    :19/:137). If a minion can't be credited to exactly one ability, it gets Legacy scaling (no inference).
    New cfg `Summon_PowerMode = OwnerRelative | Legacy`. **Migration (rev 3):** fresh configs default to
    **OwnerRelative** (KDPen's decision); on first load of v-next, an existing `.cfg` that predates the key
    is written `Legacy` and a startup notice tells the admin how to switch. `Legacy` keeps today's
    `Transform_SummonPowerFactor` × unit-stats semantics incl. health. OwnerRelative:
    minion Physical/Spell power = owner power × `Transform_SummonPowerFactor` × per-ability
    `SummonPowerScale`; HP untouched. Verify the write survives stat refresh/level assignment; if not,
    apply via a stat-modifier buff instead. Test matrix: level-matched on/off × owner-power on/off.
13. **Atomic reload with immutable generations** (used by `reload`, startup and every mutating admin
    command incl. `defaults`) — rev 3:
    build a **prospective** rules snapshot (file parse, or current rules + the command's mutation) →
    validate → build graph → resolve shared conflicts → raise a **reload barrier** (tuning + provenance
    fail closed = vanilla for new casts) → restore baked prefabs → apply → publish rules+graph+caches as
    one immutable snapshot → drop barrier. In-flight casts/entities keep the values in their own
    cast snapshots (no reference to the old rules object needed).
    Must run on the main thread (all callers are chat commands / init, already main-thread).
14. **Retire the power window** (`GrantPowerScalingService`) once step 10 is live: `Damage_LegacyPowerWindow`
    forced off with a log line; `PowerWindowSeconds` documented deprecated. BCH: `damage_scale_eff=`,
    `term_coverage=`, `attribution=`, `summon_power=` → ApiVersion 30.

### P3 — Cooldown scale + new shape knobs
15. **All cooldown policy becomes player-only runtime** (D5 + rev 5, per D0-B):
    - **Retire baked cooldown writes**: `AbilityTuningService` stops writing `AbilityCooldownData` for
      `CooldownSeconds` and `Grant_MinimumCooldownSeconds` (restore path puts any captured originals back
      on first reload/restart) — boss cooldowns stay vanilla.
    - The enforcer applies, **only to casts with verified captured provenance** (CastHistoryService
      `captured=true`): absolute `CooldownSeconds` (replace; wins if both set), else CooldownScale
      (`Defaults × entry`) × the **engine-established live cooldown** (preserves gear/buff CDR), then the
      global floor `Grant_MinimumCooldownSeconds` (captured abilities only; native player abilities are
      never touched). Precedence documented.
    - Transform bars: the enforcer currently skips transformed players; extend it to transform casts of
      captured abilities. If that proves infeasible, transform bars keep native cooldowns (documented).
    - `.beelz cast` force-casts keep Beelzebub's own cooldown tracker (BeelzCommands ~:231), computed with
      the same precedence so both paths agree.
    Rev 3: the pending record stores `fromSlot` (captured at cast start, not re-derived later) and the
    **first observed engine duration** for the matched state entity; the multiply is applied **exactly
    once** per cast to that state entity (marked done). Charge-based abilities: scale `ChargeUpTime`
    handling is out of scope — CooldownScale on charge abilities is ignored with a warning. Casts with
    `fromSlot=false` (`.beelz cast`) skip the enforcer (removes "gave up" noise).
16. **New baked shape knobs**, each value-only, captured, via the field table, under the shared guard:
    `maxstacks` (`Buff.MaxStacks`), `projcount` (fan/multishot/cluster `Count`, hard cap ×3 of original and
    ≤ 16; `inspect` shows downstream fan-out before allowing), `knockback` (Range/Duration), `lifetime`
    (`LifeTime.Duration` on spawned projectiles/areas).
    **`casttime` is its own mini-spike**: scale `MaxCastTime` and `PostCastTime` together; reject channel /
    hold-to-cast / charge-up abilities; validate per ability in-game before listing it as supported.
17. BCH: new `*_override` fields → ApiVersion 31.

## Key decisions & tradeoffs
- **Server-wide only** (KDPen). Jewel/SpellMod per-player route dropped.
- **D0-B boss-leak policy**: damage, cooldown scale and summon power are runtime + player-cast only;
  shape knobs stay baked (they change the source NPC — accepted, guarded against shared-prefab bleed).
- **D1-B runtime per-hit damage with cast-time provenance** (rev 2: attribution from a cast ledger, not
  current bar state). Baked fallback only by explicit opt-in (rev 2).
- **D2-B**: clamp band on final factor + two-way manual scale; auto-normalization deferred.
- **D3**: explicit Flat/Percent policies, default Unchanged + inspect warnings (rev 2 replaced the 5% rule).
- **D4-B** owner-relative summons as shipped default, with a Legacy mode kept (rev 2).
- **D5-B** multiply live cooldown (rev 2: preserves native CDR).
- **D6-B**; **D7** forcetimeout = runtime instance expiry only, no prefab edits (rev 3); **D10-A**; **D11-B**.
- **Attribution fail-safe (rev 3):** ambiguous provenance → vanilla damage, never a guess.
- Rejected from Codex r1: none outright; #39 (two-player tests) accepted as best-effort — KDPen may have
  only one client; a second account/tester is needed for full coverage.

## Assumptions
1. Uncommitted V-Blood audit changes (4 re-blocks) stay untouched and ride along. — git status, memory
2. `DealDamageEvent` exposes MainType, MainFactor, RawDamageValue, RawDamagePercent, ResourceModifier
   (ProjectM.Shared.dll); power term uses the caster's power by MainType — **to be confirmed by the
   step-9 truth table** before scaling ships.
3. `DealDamageSystemPatch` is a Harmony prefix on `DealDamageSystem.OnUpdate` that already reads the
   event query each frame (aggro routing).
4. `AbilityCooldownData.Cooldown` is a ModifiableFloat (`._Value` writes in our code + Bloodcraft).
5. Prefab component edits are in-process only and rebuilt at server start, so removing the old forcetimeout
   `AddComponent` needs only the deploy restart; no structural prefab edits anywhere (ABILITY_CHANGE_IMPACT §4b).
6. `HeartbeatBehaviour.Update` runs every frame on the dedicated server (`Services/Heartbeat.cs:96`).
7. Curation defaults live in `Resources/ability_rules.default.json`; BCH changes bump `ApiVersion` + handoff.
8. No automated test harness; verification is build + local dedicated server + in-game checks.

## Verification (per phase)
- Build clean (`dotnet build Beelzebub.sln -c Release`, server stopped → auto-deploy).
- **Save safety every phase**: tune → save → clean stop → restart with mod → restart without mod → load a
  copied save; no errors, world loads.
- P1: `inspect` on a player spell, V-Blood projectile, summon, leap, a known shared prefab — values match
  the dump where visible, `shared-with` correct; `export` completes with 0 required-decode failures.
  Re-run each bug scenario (form w/o bucket + disabled ability; `weapons !Sword`; `leapheight` via
  `ability`; `.beelz cast` with wrong weapon; forcetimeout with/without LifeTime; conflicting shared rule).
- P2: truth table recorded in docs; same ability/same dummy at low and high power → damage ratio tracks
  power; DamageScale 0.5 / 2.0 halve/double the power term; clamp caps an outlier; **boss casting the same
  ability deals unchanged damage**; summons weaker at low power; 10-min soak with no errors.
  **Required attribution cases (rev 3)**, each checked via verbose provenance logs: (a) two captured
  abilities sharing a spawn prefab cast in rapid alternation → hits either attributed correctly or left
  vanilla, never cross-attributed; (b) same ability cast twice across a `reload` with a changed scale;
  (c) delayed DoT/projectile landing after weapon swap / unbind / form exit; (d) persistent sources: an ability whose `SpellSource`
  is a per-player cast/state entity → attributed per event, never cached; one whose `SpellSource` is the
  character → vanilla + counted `unsupported`; (e) `.beelz cast` of an
  unslotted captured ability → scaled. Two-player shared-prefab test if a 2nd client exists.
- P3: native-bar cooldown reflects CooldownScale *and* still benefits from gear CDR; each new knob visible
  in `inspect` before/after and restored by `defaults`; projcount cap enforced; casttime rejected on channels.

## Risks / open questions
- Spike failure → only the new per-hit path is withheld; the legacy power window (made two-way) remains the
  active damage path unless the admin turns it off or opts into the baked fallback (rev 4 wording fix).
- Attribution ambiguity for abilities sharing generic prefabs may leave some abilities `unreliable` →
  reported, not advertised as damage-tunable; admins can still tune them via the baked fallback if opted in.
- Provenance ledger misses spawns whose owner chain doesn't reach the player → those hits are unscaled
  (fail-safe = vanilla damage), surfaced by verbose telemetry.
- Hot-path cost of the damage hook in big fights (early-outs, O(1) lookups; measure in soak).
- Summon stat writes may be overwritten by game systems → stat-buff fallback.

## Out of scope
Per-player tuning; jewel/SpellMod system; chain triggers; DoT tick & bounce knobs; auto-normalization;
`Damage_PercentPolicy` scaling; the paused V-Blood audit. (Incompatibility locks + re-seed tooling moved
IN by rev 6 below.)

---

# REV 6 DELTA — backlog build (P1 shipped as v0.132.0; KDPen approved decision sheet 2026-09-23)

KDPen accepted every recommendation: D1-A build all now with the damage path behind a mode switch;
D2-A exclusion groups; D3-A suppress lower priority; D4-A reseed preview/merge/replace; D5-A stuck-buff
forcetimeouts but keep Enabled=false; D6-A Elena ToF = known limit; D7-A logic tests + in-game checklist;
D8-A separate versions 0.133 / 0.134 / 0.135.

## Changes to P2 (v0.133.0, ApiVersion 30)
- **Mode switch replaces the spike gate.** New cfg `Damage_Mode = Off | Telemetry | Scale` (default **Off**).
  - `Off`: today's behaviour. The legacy power window (`GrantPowerScalingService`) stays the damage path.
  - `Telemetry`: per-hit attribution + the would-be factor are computed and counted, plus a truth-table
    log line per attributed hit (event fields + target HP before in the prefix / after in a new
    `DealDamageSystem.OnUpdate` Postfix). No mutation. The legacy window stays active.
  - `Scale`: per-hit mutation is live. The legacy window is **disabled** for new casts (no double scaling).
  - Moving from Telemetry to Scale is the admin's spike sign-off. The baked-MainFactor fallback
    (`Damage_AllowBakedFallback`) is **not built** unless the in-game spike fails (it stays in PLAN as a
    contingency).
- **Mutation path:** copy the `DealDamageEvent`, set `MainFactor` (and `RawDamage` under `FlatPolicy=Scaled`)
  on the local copy via `Unsafe.AsRef(in copy.F)`, then `EntityManager.SetComponentData(event, copy)` in the
  existing prefix. The interop fields are readonly, but the write is to a local managed copy, not a raw
  pointer into chunk memory.
- **Ledger = extend `CastHistoryService`** (no separate CastProvenanceService). At `RecordCast`,
  `CastRecord` gains a damage snapshot: `scale = Defaults.DamageScale × entry.DamageScale × (Boosted ? factor : 1)`
  plus the min/max bounds and the flat policy. `AttributeDamage(spellSource)` follows the rev-3 rule: owners ∩
  retained casts, exactly one distinct group that is also captured. Ephemeral entities are cached by
  (index, version) and pruned when dead (cap 4096). Group/cast-kind nodes and the character itself are
  handled per-event and never cached (the character counts as unsupported).
- **Flat policy:** `Damage_FlatPolicy = Unchanged | Scaled` for now. `PowerRelative` is deferred until the
  truth table shows `RawDamage` is not already power-scaled. `RawDamagePercent` is never touched.
- **Summon_PowerMode:** as rev 5 (OwnerRelative for fresh cfg / Legacy migration for an existing cfg that
  lacks the key).
- **Admin surface:** `.beelz admin damage-stats [ability]` shows the per-ability counters and attribution
  reliability. `inspect` shows them too. `api info` adds `damage_mode= attribution= damage_scale_eff=`.

## Changes to P3 (v0.134.0, ApiVersion 31)
- **Cooldowns:**
  - `AbilityTuningService.ApplyCooldown` no longer writes (restore of captured originals is kept).
  - Rework the existing `AbilityCooldownEnforcer`. Gate on CastHistoryService `Captured` (not the v0.71.1
    "any configured" gate, which also hit bosses/natives). Resolve `absolute ?? live×scale`, then floor.
    Apply exactly once per cast, at the first observation of a started cooldown after the cast. Scale is
    skipped on charge-based abilities (warn once); an absolute value still applies.
  - Transformed players: unchanged (transforms keep native cooldowns plus their own `CooldownScale`;
    documented).
  - `.beelz cast` tracker (BeelzCommands ~256) uses the same precedence through one shared pure helper.
- **Knobs:**
  - `maxstacks` → `Buff.MaxStacks` (byte, 1..255).
  - `projcount` → `Count` on fan / fan-on-tick / multishot / cluster, ≤ min(orig×3, 16).
  - `knockback` → scales `Range` and `Duration` in the `ApplyKnockbackOnGameplayEvent` buffer.
  - `lifetime` → `LifeTime.Duration` on spawned projectiles/areas, never on buffs (buff duration stays
    `duration`).
  - All four are value-only, captured, under the shared guard; none of them is deep-safe.
- **`casttime`** (experimental, rejected with a message on `AbilityHoldToCastData` / `AbilityChargesData`
  / names containing `Channel`): scales `MaxCastTime` + `PostCastTime` together. It is flagged
  `experimental` in `inspect` and docs until validated in game.

## Incompatibility locks (v0.135.0, ApiVersion 32)
- **Data:** `RulesDto.ExclusionGroups: Dictionary<string, ExclusionGroup{ int Max=1; List<string> Members; string Notes }>`.
  - Members are an ability prefab name, a numeric GUID, or `cat:<AbilityCategory>`.
  - It ships empty; validation at load drops unknown members with a warning and clamps `Max` to ≥ 1.
- **One pure resolver** (`Logic/ExclusionResolver.cs`, no game deps):
  - Input: an ordered list of (key, abilityGuid) and a membership function.
  - For each group, it keeps the first `Max` distinct abilities in order and suppresses the rest.
    An ability in several groups must survive all of them.
  - Output: kept + suppressed (with the group name).
- **Loadout order** (the priority rule): live bar slots in ascending slot order, then hotkey bindings in
  ordinal name order.
- **Enforcement points:**
  1. `SlotApply.ResolveAndInjectGrants`: filter `slots` before injection; suppressed slots are not
     injected (their saved bind is kept).
  2. `ShapeshiftAbilityService.ApplyFormLoadout` and `ApplyMountedLoadout`: filter `perSlot`.
  3. `.beelz slot` / `weapon-grant` / `form-grant`: prospective check against the current bar for that
     bucket; refuse with the group name.
  4. `.beelz hotkey set`: prospective check of (current bar + hotkeys + new).
  5. `.beelz cast`: X is castable only if it survives the resolver over (current bar + hotkeys + X if
     unbound). The same check covers hotkey casts.
  - Transforms (whole boss kits) are exempt; documented.
- **Suppression messaging:**
  - A throttled chat notice to the player: "<ability> is locked by group '<g>' (max N) — kept <other>".
  - A `[BEELZ:event] type=slot-locked slot= a= group=` for BCH.
  - When an admin adds or removes a group, every online player's bar is re-resolved
    (`RestoreResolvedGrants`; the form loadout re-applies on the next form entry).
- **Commands:** `.beelz admin lock add <group> <member…>` (creates the group), `lock max <group> <n>`,
  `lock remove <group> [member]`, `lock list`, `lock check <player>`.
- **BCH:** `api locks` emits `[BEELZ:lock] g= max= m=` per group, plus the `slot-locked` event. Gated on
  `api>=32`.

## Reseed (v0.135.0)
- Every seed or replace writes `ability_rules.baseline.json` (a copy of the shipped default at that time).
- `.beelz admin reseed preview`: per-entry diff counts (new, changed-by-ship, changed-by-admin, conflicts).
- `.beelz admin reseed merge`: 3-way merge per AbilityMap/TransformMap entry, field by field.
  - A field is adopted from the new default when server == baseline (the admin never touched it).
  - Admin-changed fields are kept.
  - New entries are added.
  - Conflicts (both changed) keep the admin's value and are listed.
  - With no baseline: add missing entries only, and force shipped `Enabled=false` on entries whose GUID is
    in the C# hard-block set.
  - Writes a timestamped backup first, then validates, reloads and reapplies.
- `.beelz admin reseed replace CONFIRM`: backup, then overwrite with the default, write the baseline, reload.
- The merge is a pure function over `JsonElement`/dictionary trees (`Logic/RulesMerge.cs`) and is unit-tested.

## Housekeeping (v0.135.0)
- D5: add `ForceTimeoutSeconds` to the FallAsleep / Corpse Buff / High Lord Leap entries in
  `ability_rules.default.json`; `Enabled` stays false.
- D6: the Elena ToF shared-buff skip is listed as a known limit in ABILITY_CONFIG.md.
- Remove the stale "Coming next" note.

## Testing (D7)
- A new `Beelzebub.Tests` xUnit project (net6.0) that **links** `Beelzebub/Logic/*.cs` (pure, no IL2CPP)
  rather than referencing the plugin assembly. Covered:
  - ExclusionResolver (order, multi-group, max>1, categories)
  - RulesMerge (all 3-way cases plus no-baseline)
  - the numeric validator
  - CooldownMath (precedence, floor, charges)
  - DamageMath (scale, clamp both bounds, 0 = off, flat policy, skip rules)
  - AttributionRule (0 / 1 / many candidates, captured flag, overflow)
- The plugin calls the same Logic functions, so the tests cover the shipped code paths.
- `docs/INGAME_TEST_CHECKLIST.md`: numbered steps with exact commands and expected results per version,
  plus the save/restart safety test.

## Rev 6.1 — fixes from Codex rev-6 round 1 (26 findings; 25 accepted, #12 rejected)
**Locks**
- (#1, #5) **One canonical "active loadout"** = the ACTIVE bar source (form bar if in a form, saddle bar if
  mounted, else the resolved weapon bar), in slot order, then hotkeys in ordinal name order. Every point uses it,
  including **`ApplyGrant` itself** (the lowest injection layer), so no caller can bypass it.
- (#2) Prospective checks resolve the **target bucket as it will appear when active**: universal+that
  weapon's bucket for `slot`/`weapon-grant`, and that form's bucket for `form-grant`, each plus hotkeys.
- (#3) `ResolveAndInjectGrants` handles suppressed slots like auto-yielded ones: `RemoveSlotEntries`
  and then `RestoreSlotBaseValue`, after the loop. The saved bind is kept.
- (#4) Adding or removing a lock immediately re-resolves every online player, **including** active forms and
  mounts: the live form/saddle buff is found and `ApplyFormLoadout`/`ApplyMountedLoadout` is re-run, which
  clears the old entries first.
- (#6) Limits count **distinct ability GUIDs**. Duplicate bindings of a kept GUID stay; suppression is
  reported per binding key.
- (#7) The resolver returns **all** rejecting groups, sorted by name; the first one goes in the chat and
  event text.
- (#8) Unresolved members are **kept in the file**, warned about, inactive, and re-resolved on
  reload/startup.
- (#9) Event `[BEELZ:event] type=ability-locked src=slot|form|mount|hotkey|cast key=<slot#|hotkey name>
  a= group=`. The chat and events are **transition-deduped**: per player, only changes versus the last
  suppressed set are sent.

**Damage**
- (#10, #11) The path is chosen **per cast** at `RecordCast` and stored in the snapshot:
  - `Damage_Mode=Scale` → `PerHit=true` and no power window.
  - Otherwise → the legacy window as today, with `PerHit=false`.
  - Per-hit mutation happens only when the matched cast's `PerHit=true`, so an in-flight window cast is
    never also per-hit scaled.
  - Truth-table lines carry `path=window|perhit|none`.
  - Spike guidance: telemetry with DamageScale=1 (no window), then Scale mode.
- (#12 REJECTED) The reference assembly `ProjectM.Shared.dll` dump shows `DealDamageEvent.RawDamage`
  (the `RawDamageValue` name belongs to `DealDamageParameters`). The plan's field name is correct.
- (#13) A **runtime write probe**: after each mutation, read the component back. On a mismatch or
  exception, set `Damage_Mode` to **Telemetry** in memory, log an error, and report it in `damage-stats`.
  Scale mode and telemetry both keep the HP-delta check, so the admin can see a real effect.
- (#14) The prefix snapshots `(event, target, hpBefore, expected factor ratio)`. The postfix aggregates
  per target and reports the HP drop against the event count. Consumed events are fine because the target
  is read, not the event.
- (#15) One shared, cycle-safe **8-hop** owner resolver (visited set) for damage, forcetimeout and aggro;
  truncation has its own counter.
- (#16) Cache full → no insert, no eviction of live entries. That hit is attributed per event, and a
  `cache_saturated` counter is shown in `damage-stats`.

**Cooldowns**
- (#17) `CastRecord` gets a monotonic `CastId` and a cooldown-policy snapshot. At record time the enforcer
  snapshots the slot state's current `CooldownEndTime`. It enforces only when it sees a **different**
  `CooldownEndTime` with `CurrentCooldown>0` (a newly started cooldown), then marks that CastId done.
- (#18) `FromSlot`: `ForceCastService` sets a one-shot `(steamId, group)` "force-cast in progress" marker,
  which `RecordCast` consumes, so `.beelz cast` gets `FromSlot=false`. The enforcer requires
  `Captured && FromSlot`.
- (#19) Charge-based abilities are **skipped entirely** (scale and absolute), with a warning once per
  ability; `inspect` marks them `cooldown: n/a (charges)`.

**Reseed**
- (#20) The 3-way table treats **absence as a value** (tombstones): entry or field added, removed, or
  changed, on either side. Deletion by the ship side is adopted only if the server copy is still equal to
  the baseline.
- (#21, #25) Entry identity = the resolved GUID when resolvable, else the key. Aliases (name vs numeric key)
  are merged onto the server's existing key.
- (#22) Merge rules follow the schema:
  - Scalars are merged independently.
  - Lists are **atomic**, compared after normalizing (trim, case-insensitive, sorted).
  - Nested objects and unknown fields are atomic.
- (#23) After a successful merge or replace, the baseline advances to the current shipped default.
- (#24) The update is transactional:
  1. Build and validate the prospective DTO in memory.
  2. Write the backup, then the rules and baseline `.tmp` files, then `File.Replace` both.
  3. Reload and reapply; if either fails, restore the backup and old baseline and report it.
- **No-baseline mode:**
  - Add missing entries.
  - Adopt shipped `Enabled=false` where the server says true. Safety wins, and every adoption is listed
    so the admin can re-enable it.
  - Nothing else changes.
  - C# hard-blocks are enforced at runtime regardless.

**Knobs**
- (#26) Every new knob computes `desired = captured original × scale` (or the absolute value) from the
  immutable `CaptureOriginal` record, never from the live prefab. This matches the existing
  `projspeed`/`leapheight` pattern and adds unit tests for idempotence.

## Rev 6.2 — fixes from Codex rev-6 round 2 (14 findings, all accepted)
- (#1) When a group's retained casts **disagree** on the damage snapshot or path, first-sight attribution
  counts as **ambiguous**, so the hit stays vanilla.
- (#2) A **global per-hit circuit breaker** (static flag) is checked before every mutation, independent of
  cast snapshots. It is tripped by the write probe; the error is logged once and shown in `damage-stats`.
- (#3) Readback is limited: the first 20 mutations after startup or a mode change, then 1 in 256.
- (#4) Spike guidance requires the **effective** legacy scale to be 1: every per-ability DamageScale is 1
  **and** `Grant_PowerScalingMode` is not Boosted. The truth-table `path=` field makes any leftover window
  visible.
- (#5) HP-delta rows are labelled `observational`. Scale sign-off in the checklist uses **isolated
  single-hit cases**: one cast, one training dummy or low-regen target, and a comparison of paired casts at
  scale 1 and 2.
- (#6) Form and saddle buffers are snapshotted **before the first modification**: the original
  `ReplaceAbilityOnSlotBuff` entries are stored per buff entity (index+version) and dropped when the buff
  dies. Each re-resolution first restores that snapshot, then applies the filtered loadout.
- (#7) The event becomes `type=ability-locked ... locked=1|0`, emitted for both directions of a transition.
- (#8) Batch paths (`ResolveAndInjectGrants`, form and saddle apply) run the resolver **once over the full
  prospective batch**. `ApplyGrant`'s own check is used only by the single-binding callers.
- (#9) Force-cast markers expire after 2 s, are cleared synchronously when `ForceCastService` fails, and
  are consumed by the first matching cast-start. If no marker is present the cast counts as `FromSlot=true`,
  and a stale marker can at worst skip one enforcement, which fails safe to vanilla cooldown.
- (#10) Pending cooldown enforcement is a **FIFO queue per (player, group)** of `CastId`s. Each newly
  observed cooldown start (a changed `CooldownEndTime`) consumes the oldest pending entry exactly once;
  entries expire after 6 s.
- (#11) Crash-safety: the rules file stores `BaselineHash` (SHA-256 of the baseline file it was merged
  against). At merge or preview time, if the baseline file is missing or its hash differs, reseed runs in
  **no-baseline safe mode** (a startup warning explains why). No journal is needed, because a torn
  write degrades to safe mode.
- (#12) Replace-if-present, else same-directory `File.Move`, for both files; both cases are tested.
- (#13) Before merging, keys that resolve to the same GUID are detected. The merge is **refused** with a
  conflict list naming the keys, and the admin consolidates them first.
- (#14) Checklist cases are added for:
  - the native form-slot restore after a lock change,
  - a window→per-hit transition on the same ability (old delayed DoT stays vanilla),
  - force-cast marker expiry,
  - a torn reseed (baseline deleted by hand → safe mode).
  Unit tests cover the hash-mismatch fallback.

## Rev 6.3 — fixes from Codex rev-6 round 3 (2 findings, both accepted)
- (#1) **The force-cast marker is dropped.** `FromSlot` is derived structurally at `RecordCast`: true only
  when `evt.AbilityGroup` (the cast's group instance entity) equals some slot's `AbilityGroupSlot.StateEntity`
  in the caster's `AbilityGroupSlotBuffer`. This is the same state entity the enforcer re-anchors. Anything
  else (a `.beelz cast` force-cast, an unresolved entity) is `FromSlot=false` and never enforced. The enforcer
  also only touches the slot whose StateEntity **is** the recorded GroupEntity, so it cannot hit another
  cast's state.
- (#2) `BaselineHash` is transaction metadata outside the merge tree: it is ignored by diff and merge and
  computed from the exact baseline bytes being installed.
