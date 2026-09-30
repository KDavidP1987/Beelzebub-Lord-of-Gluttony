# Plan Review Log: Beelzebub per-ability configuration overhaul
Phases 0-1 (recon + interrogation) complete — plan locked with the user (all decision-sheet recommendations accepted). MAX_ROUNDS=5.

## Round 0 — Codex (void)
Windows read-only sandbox rejected every filesystem process; Codex could not read the repo. Not counted. Workaround: plan + relevant source excerpts piped on STDIN (still -s read-only).

## Round 1 — Codex (thread 01a0cf58-a77d-72e1-a747-d0269ef2d9ec)
1. **The prefix timing is assumed, not proven.** A Harmony prefix only guarantees execution before `DealDamageSystem.OnUpdate`, not after all producer jobs have completed or before every consumer; querying or writing ECS data may trigger dependency errors or miss events created inside `OnUpdate`.  
   **Fix:** Spike the exact system update order and job dependencies, explicitly complete the relevant dependency if safe, and verify each event is mutated before consumption.

2. **Harmony ordering against other mods is unspecified.** Another prefix may read, replace, or rewrite the same events before or after this patch, making damage scaling load-order dependent.  
   **Fix:** Declare a deliberate Harmony priority and document compatibility; log patch owners/priorities during startup diagnostics.

3. **Raw pointer writes are not a safe production fallback.** Offsets inferred from IL2CPP interop metadata can diverge from the native ECS layout after a game update and silently corrupt adjacent fields.  
   **Fix:** Limit production to a verified typed write/`SetComponentData`; if raw access is unavoidable, hard-gate it by game version, component size, and validated offsets, failing closed.

4. **The spike’s success criterion is too weak.** Seeing changed hit numbers does not prove the intended event field was modified safely or that every damage path uses the queried component.  
   **Fix:** Log event entity, GUID, original/new fields, target health delta, and mutation path, then test direct hits, projectiles, DoTs, AoEs, counters, and delayed impacts separately.

5. **`SpellSource prefab → ability group` cannot provide reliable attribution.** Generic projectiles, hit prefabs, buffs, and periodic effects are commonly shared, while downstream effects can lose the original cast identity entirely. “Currently bound” is not provenance.  
   **Fix:** Attach runtime cast provenance to spawned instances where possible, or maintain a short-lived entity lineage map originating at cast-start rather than inferring from current bindings.

6. **Hotkey/force-cast abilities defeat the ambiguity rule.** An ability may be captured and cast without being currently slotted, so filtering ambiguous candidates by the bound set can reject the actual ability.  
   **Fix:** Include active force-cast/cast-start provenance and captured-but-unslotted casts in attribution.

7. **Form and weapon bars make “currently bound” time-sensitive.** A delayed projectile or DoT can land after a weapon swap, form exit, unbind, reload, or reconnect and resolve to the wrong rule.  
   **Fix:** Snapshot the resolved ability and effective scale at cast/spawn time; never resolve delayed damage from the player’s current bar state.

8. **The reverse-index traversal is not specified deeply enough.** `AbilitySpawnPrefabOnCast` plus a limited deep pass will miss destroy spawns, gameplay-event spawns, applied buffs, projectile impacts, and other chained delivery mechanisms.  
   **Fix:** Define one bounded, cycle-safe graph walker covering every known spawn/apply edge and use the same graph for inspect, attribution, shared detection, restore, and tests.

9. **The shared-prefab index must include uncaptured/native abilities.** Indexing only registered abilities will label a prefab “unshared” even when a boss or an unregistered ability also reaches it.  
   **Fix:** Build the reverse index from every ability group in the prefab collection, with registered/captured status as metadata rather than an inclusion filter.

10. **`ResolveOwningPlayer` does not mean “player cast.”** Following `EntityOwner` to a player also includes summon attacks, player-owned persistent objects, and potentially dominated units; shared GUIDs could then receive captured-ability scaling.  
    **Fix:** Require both player ownership and verified captured-cast provenance; treat ownership alone only as an auxiliary signal.

11. **The four-hop ownership limit is arbitrary.** Long projectile/buff/spawn chains may exceed four owners, while cycles beyond the direct self-cycle check remain possible.  
    **Fix:** Use a small visited-entity set with a documented higher bound and telemetry for truncation/cycles.

12. **Damage semantics are under-specified.** `MainFactor` is not guaranteed to mean the same thing for every `MainType`, and `ResourceModifier`, `Modifier`, crit processing, resistances, PvP modifiers, and level-difference scaling may occur at different stages.  
    **Fix:** Build a field-by-field truth table from observed events and health deltas before defining the scaling formula.

13. **Scaling `MainFactor` does not cover “damage” uniformly.** Raw damage, target-health percentage damage, resource-derived damage, and later DoT ticks can remain unchanged, so `DamageScale 0.5/2.0` will not generally halve/double a hit.  
    **Fix:** Specify supported damage terms explicitly and report an inspect warning when an ability contains unscaled terms.

14. **The 5% threshold for flat damage is arbitrary and unsafe.** Frequency says nothing about gameplay importance; a single boss ability with large raw damage may be the main balancing problem.  
    **Fix:** Decide per damage-entry type or per ability, based on magnitude and semantics, not global occurrence count.

15. **`RawDamagePercent` is mentioned but has no policy.** Scaling percentage-of-health damage by caster power can become catastrophic, while leaving it untouched violates the advertised multiplier behavior.  
    **Fix:** Add a distinct configurable policy for percent-health damage, defaulting to unchanged with explicit inspection warnings.

16. **Reference-power conversion can double-apply power.** If a raw term already participates in another power/resource transformation, multiplying it by `casterPower/referencePower` produces nonlinear scaling.  
    **Fix:** Prove raw-field semantics per `MainType` before applying reference-power conversion and exclude already-scaled terms.

17. **Clamping `scaledFactor` is semantically surprising.** The same configured scale yields different effective multipliers based on the ability’s original factor, and negative or special-purpose factors are not addressed.  
    **Fix:** Define whether bounds clamp the multiplier or final factor, reject invalid ranges, and explicitly handle zero, negative, healing, and special damage entries.

18. **The baked fallback is materially unsafe as staged.** It is introduced in P2, while the shared-prefab guard does not arrive until P3, so fallback damage edits can bleed into sibling abilities and bosses immediately.  
    **Fix:** Make full shared-prefab indexing and guarding a prerequisite for any baked damage fallback.

19. **Accepting boss leakage contradicts the stated correctness goal without a hard operator choice.** A silent automatic fallback can globally alter boss fights after the runtime spike fails.  
    **Fix:** Disable damage tuning when runtime mutation is unavailable unless an explicit `AllowBakedDamageFallback` configuration is enabled.

20. **The `forcetimeout` migration statement is wrong.** Prefabs structurally modified earlier in the same server process still carry the added `LifeTime`; merely stopping future additions does not undo them, while removing it live repeats the persistence-risky structural mutation.  
    **Fix:** Require a clean server restart for migration, stop invoking structural restore on those prefabs, and emit a prominent save-safety warning.

21. **A timeout set keyed only by buff GUID cannot represent per-ability values.** Shared buff prefabs may be reached by several abilities with different timeout rules, and unrelated player-owned instances can match the same GUID.  
    **Fix:** Track timeout by live instance plus originating ability provenance, not prefab GUID alone.

22. **“Older than N seconds” has no specified clock source.** Indefinite instances lack `LifeTime`, and discovering them during a sweep does not reveal their actual spawn time.  
    **Fix:** Record first observation or spawn time per entity, define reload behavior, and clean tracking entries when entities disappear.

23. **The timeout sweep may destroy unrelated buffs.** Player ownership plus prefab GUID is insufficient when the prefab is shared with consumables, equipment effects, native abilities, or other mods.  
    **Fix:** Require captured-ability provenance and skip ambiguous instances with a diagnostic.

24. **Heartbeat-driven expiration has undefined cadence and lifecycle behavior.** Paused combat, no damage-system updates, reloads, disconnects, and entity reuse can make expiry late or incorrect.  
    **Fix:** Run expiry from a guaranteed server update path using world time and entity-version-aware keys.

25. **Summon scaling lacks per-ability attribution at the shown call site.** `ScaleSummonToPlayer(minion, playerCharacter)` has no ability-group argument, so `SummonPowerScale` cannot be selected reliably for shared summon prefabs.  
    **Fix:** Carry the originating ability group through summon registration and store it with the live summon record.

26. **Writing summon `UnitStats` once may be overwritten by game stat recalculation systems.** Level assignment, buffs, initialization, and equipment/stat systems can run after the proposed write.  
    **Fix:** Determine the final stat-initialization point or use the game’s supported stat-modifier mechanism, then test persistence across level/stat refreshes.

27. **Owner power replacement risks double level scaling.** Matching `UnitLevel` to the player and assigning owner-derived power can combine with V Rising’s level-difference calculations in ways the current multiplicative design did not.  
    **Fix:** Test the four combinations of level matching and owner-power scaling and define which layer is authoritative.

28. **The summon change silently breaks existing config semantics.** `Transform_SummonPowerFactor=1` currently preserves native stats; under the plan it replaces them with owner power, and factors currently also scale health.  
    **Fix:** Introduce a separate opt-in owner-power mode and preserve legacy multiplier/health behavior by default.

29. **Cooldown scaling’s baseline is undefined.** “Original × Defaults × entry” could mean shipped prefab cooldown, currently baked cooldown, or the engine-created live cooldown after player cooldown-reduction modifiers.  
    **Fix:** Define precedence explicitly and preferably scale the newly created live cooldown to preserve native player cooldown modifiers.

30. **The current enforcer removes native cooldown reduction.** Replacing `CurrentCooldown` with an absolute computed value means gear/buffs affecting cooldown may be discarded, conflicting with the power-relative fairness rationale.  
    **Fix:** For multiplier mode, multiply the engine-established cooldown; reserve absolute replacement for `CooldownSeconds`.

31. **`Defaults × entry` must distinguish omitted values from serialized defaults.** If every `AbilityEntry` deserializes `DamageScale/CooldownScale` as `1`, multiplying is fine, but legacy files, zeroes, NaN, infinity, and negative values still need validation.  
    **Fix:** Add schema validation and normalized effective-value helpers with finite bounds used by runtime, API, and inspection.

32. **Cast-time edits are not safely captured by changing `MaxCastTime` alone.** Cast duration, post-cast time, animation events, channel timing, movement locks, and gameplay-event timing can diverge.  
    **Fix:** Treat cast-time tuning as its own spike with per-ability validation and either update all coupled fields or reject incompatible abilities.

33. **Projectile-count edits can multiply secondary effects and entity load dramatically.** Increasing script counts may duplicate summons, buffs, impacts, or chained spawns rather than merely visual projectiles.  
    **Fix:** Add bounded limits, classify delivery type, inspect downstream fan-out, and soak-test worst-case entity counts.

34. **`AllowSharedEdit` does not make a per-ability edit semantically isolated.** It merely authorizes global bleed, including into boss fights and unrelated abilities.  
    **Fix:** Name it explicitly as a global shared-prefab override, require an admin warning/confirmation, and report every affected ability group.

35. **Restore ownership is unsafe for shared prefabs.** Resetting one ability to defaults can restore a shared prefab that another configured ability still expects to be modified.  
    **Fix:** Compute the desired value from all owning rules and reject conflicts, rather than restoring shared state per ability.

36. **Reload ordering is not planned.** Restoring all prefabs, rebuilding indexes, applying rules, and invalidating player caches can expose stale indices or transient baseline values.  
    **Fix:** Define an atomic reload sequence: parse/validate → build graph/index → resolve conflicts → restore → apply → swap runtime snapshots → invalidate caches.

37. **The inspection error policy can hide systematic layout failure.** Per-component catch-and-continue may produce a plausible but incomplete CSV with no indication that critical data was omitted.  
    **Fix:** Include error/status columns, aggregate failure counts, and fail the export visibly when required components could not be decoded.

38. **CSV output details are missing.** Prefab names/fields require escaping, invariant numeric formatting, deterministic ordering, and safe replacement to avoid corrupt or partially written exports.  
    **Fix:** Write invariant, properly quoted CSV to a temporary file and atomically replace the prior export on success.

39. **Verification lacks multiplayer attribution tests.** Two players casting the same shared projectile with different active bars, swaps, or delayed DoTs are the failure mode most likely to expose incorrect inference.  
    **Fix:** Add concurrent two-player tests covering shared prefabs, weapon/form changes, unbinds, force-casts, delayed ticks, and disconnects.

40. **Verification lacks restart/save-corruption testing despite known history.** A build and combat soak do not test persistence safety.  
    **Fix:** For every phase, save after tuning, stop cleanly, restart with the mod, restart without it, and verify world load from a copied save.

VERDICT: REVISE
### Claude's response (round 1)
Accepted nearly all 40 findings, restructured into rev 2:
- Attribution (#5-7,#10,#11): replaced "currently bound" inference with a cast-provenance ledger fed by AbilityCastStartedSystemPatch; first-sight entity provenance with cast-time scale snapshot; visited-set ownership walk (8 hops).
- One chain graph (#8,#9) built from EVERY ability group, used by inspect/attribution/shared-guard/restore; moved to P1.
- Spike (#1-4,#12): ordering check, truth table, explicit Harmony priority; raw pointer writes removed as a production path.
- Damage semantics (#13-17): scale only the power term; clamp applies to the final factor with validated bounds; flat/percent as explicit policies (default Unchanged) + inspect warnings; 5% rule dropped.
- Baked fallback (#18,#19): now explicit opt-in `Damage_AllowBakedFallback`, and the shared guard moved to P1.
- forcetimeout (#20-24): instance+provenance keyed, first-observed world time, per-frame HeartbeatBehaviour.Update, cleanup on disconnect/reload; migration = restart (prefab edits are in-process).
- Summons (#25-28): ability carried from cast; `Summon_PowerMode` OwnerRelative (user's decision, shipped default) | Legacy; stat-buff fallback if writes are overwritten; level test matrix.
- Cooldown (#29,#30): multiply the live engine cooldown to preserve CDR; absolute CooldownSeconds keeps replace semantics.
- #31 validation helpers; #32 casttime as its own mini-spike; #33 projcount caps; #34/#35 AllowGlobalSharedEdit + all-owner restore + conflict rejection; #36 atomic reload; #37/#38 export robustness; #40 save-safety tests.
- Partially accepted: #28 — Codex wanted legacy as default; kept OwnerRelative as the shipped default because KDPen explicitly chose owner-relative summons (D4-B), with Legacy retained. #39 — two-player tests are best-effort (may lack a second client).

## Round 2 — Codex
Rev 2 addresses most round-1 findings: unsafe raw writes are removed, fallback is explicit, shared indexing covers native abilities, flat/% damage policies are explicit, cooldown scaling preserves CDR, structural prefab edits are retired, reload ordering is defined, and save/restart testing was added. Material issues remain:

1. **The provenance ledger still guesses by recency.** If one player rapidly casts two abilities sharing a prefab, `owners(prefab) ∩ recent casts → most recent` can assign the older projectile or DoT to the newer cast.  
   **Fix:** Propagate provenance causally at entity creation/spawn edges; use recency only as an explicitly logged fail-safe that leaves ambiguous events unscaled.

2. **Repeated casts of the same ability can receive the wrong snapshot.** If configuration reloads or modifiers change between casts, an older entity first observed after the newer cast inherits the newer snapshot.  
   **Fix:** Stamp each spawned instance during its originating cast window, before another cast can supersede it.

3. **Caching provenance “for the entity’s life” is unsafe for persistent sources.** Some direct damage events may use the player, weapon, slot state, or another long-lived entity as `SpellSource`; the first cached ability would then contaminate later casts.  
   **Fix:** Cache only on verified ephemeral chain entities; attribute persistent-source damage per event/cast token and never attach lifetime provenance to the player or reusable state entities.

4. **“First sight” may be too late.** Projectiles or effects not covered by `BuffSpawnServerPatch` may first be seen at damage time, after several candidate casts have accumulated.  
   **Fix:** Add provenance hooks for every relevant spawn path discovered by the graph, or classify uncovered paths as unsupported and leave them vanilla.

5. **The graph retention period is not robust.** Static graph lifetime cannot reliably cover indefinite buffs, summons, refreshed effects, or delayed destroy-trigger chains; a 30-second floor may expire valid provenance.  
   **Fix:** Retain provenance with the live entity lineage, expiring it when entities disappear rather than primarily by a cast-age timeout.

6. **A depth limit of eight can create false “unshared” results.** Truncated traversal may miss another group reaching the same downstream prefab, after which the shared guard permits an unsafe baked edit.  
   **Fix:** Treat every node reached through a truncated frontier as unsafe for baked editing unless completeness is proven.

7. **Cycles being merely counted is insufficient.** Cycles are normal for a visited graph, but decode failures, unresolved blob edges, and depth truncation each affect completeness differently.  
   **Fix:** Track graph completeness per group/node and prohibit shared-sensitive edits whenever ownership analysis is incomplete.

8. **`forcetimeout` again changes finite `LifeTime` values.** The source comments explain that doing this compresses timing-driving buffs and changes playback speed; rev 2 reintroduces that behavior.  
   **Fix:** Implement `forcetimeout` exclusively as instance-level destruction after elapsed time; leave existing prefab `LifeTime` untouched and reserve `duration` for changing it.

9. **Finite-lifetime timeout edits also retain the boss leak unnecessarily.** Runtime provenance now exists, so there is no reason to bake `forcetimeout` onto a finite buff shared with its NPC caster.  
   **Fix:** Use the same provenanced runtime expiry mechanism for buffs both with and without `LifeTime`.

10. **Flat-damage policy omits the configured damage multiplier.** `PowerRelative` says only `RawDamageValue × casterPower/referencePower`; therefore `DamageScale=0.5/2.0` still does not tune the raw part of the hit.  
    **Fix:** Define the formula explicitly, normally `raw × effectiveDamageScale × casterPower/referencePower`, or rename/document the knob as power-term-only.

11. **The advertised goal still overstates coverage.** “Damage tuning is two-way” suggests whole-hit scaling, but percentage damage is always unchanged and flat damage defaults unchanged.  
    **Fix:** State prominently that `DamageScale` scales only supported power-derived terms and expose supported/partial status through inspect and API.

12. **Fallback behavior conflicts with the existing power-window service.** If the spike fails, step 14 never disables `GrantPowerScalingService`, so damage tuning is not actually “disabled unless baked fallback is enabled”; the old window remains active.  
    **Fix:** Specify the failure-state behavior explicitly and disable or deliberately retain the legacy window under a separately named compatibility setting.

13. **The Harmony dependency claim remains an assumption.** Existing read access succeeding does not prove that `SetComponentData` is safe or that it occurs after all event producers.  
    **Fix:** Make producer-dependency completion and write safety measured spike results, not a planned assertion; fail the gate if the exact dependency cannot be identified.

14. **Cooldown multiplication needs a one-shot baseline definition.** The plan does not say how the enforcer distinguishes the engine-established value from a value it previously multiplied, especially with repeated state scans or charge cooldown states.  
    **Fix:** Store the first observed engine duration in the pending record, apply exactly once to the matching state entity, and test charge-based/multi-state abilities.

15. **Skipping unslotted casts by checking current slots can race bar changes.** A native-bar cast followed immediately by a weapon/form swap may appear unslotted when registration or enforcement runs.  
    **Fix:** Record whether the cast originated from a live slot at cast-start and carry that fact in the pending entry.

16. **Shared-rule conflict handling lacks deterministic ownership.** “Later one is rejected” depends on dictionary/config iteration order and can change across serialization or runtime versions.  
    **Fix:** Resolve shared edits by a stable sorted group key and require identical requested values, otherwise reject the shared field for all conflicting owners.

17. **The atomic reload sequence publishes runtime state too late for provenance creation during reload.** Cast/spawn hooks could observe old snapshots while prefabs and graphs are being restored/applied.  
    **Fix:** Add a reload barrier that temporarily makes tuning/provenance fail closed, then publish graph, rules, and caches as one immutable generation.

18. **`defaults` cannot literally use the stated parse-first sequence without defining the prospective rule set.** Per-ability defaults must remove fields before conflict resolution and recomputation, or shared desired-state restoration can use stale rules.  
    **Fix:** Build a prospective rules snapshot containing the defaults mutation, resolve it fully, then commit and reapply.

19. **OwnerRelative as the shipped default is a breaking migration for existing servers.** Existing configs lacking `Summon_PowerMode` will silently switch from native-stat multiplication to owner-stat replacement.  
    **Fix:** Version the config migration, preserve `Legacy` for existing installations, and use `OwnerRelative` only for fresh configs or explicit migration consent.

20. **Summon attribution inherits the same recency ambiguity as damage.** “Ledger / existing LinkMinion paths” is not a concrete guarantee that simultaneous or delayed summon spawns map to the correct ability.  
    **Fix:** Pass the originating ability GUID through the exact summon request/link record and reject owner-plus-recency inference.

21. **Testing remains insufficient for the central attribution design.** The two-player test is optional, and there is no required same-player overlapping-cast test even though that is the primary ledger ambiguity.  
    **Fix:** Require automated or manual overlap cases: shared prefab, rapid alternating casts, same-ability casts across reload, delayed hits, and persistent `SpellSource`.

VERDICT: REVISE
### Claude's response (round 2)
Accepted: #1 (ambiguous → vanilla, no recency tie-break), #3 (cache only on ephemeral chain entities), #4 (first-sight + fail-safe covers uncovered spawn paths), #5 (cache lives with the entity; retention only bounds new matches), #6/#7 (per-group completeness; incomplete ⇒ shared/unsafe), #8/#9 (forcetimeout = runtime instance expiry only, no prefab edits), #10/#11 (flat policy formula includes scale; coverage full/partial/none reported), #12 (explicit failure state: `Damage_LegacyPowerWindow` vs opt-in baked fallback, mutually exclusive), #13 (dependency/write safety measured; gate fails otherwise), #14/#15 (fromSlot captured at cast; first engine duration; apply once; charge abilities excluded), #16 (sorted owners; identical values or reject all), #17/#18 (prospective snapshot + reload barrier + immutable generations), #19 (existing cfgs migrate to Legacy; fresh → OwnerRelative), #20 (ability GUID through exact summon records; uncreditable → Legacy), #21 (required attribution test cases a–e).
Partially rejected: #2 — per-spawn causal stamping at every spawn path is disproportionate; the residual case (reload mid-combat between two casts of the same ability) is documented as an accepted edge.

## Round 3 — Codex
Rev 3 resolves most round-2 findings, but several material gaps remain:

1. **Incomplete graph handling still permits false “unshared” results.** If group A has an undecodable edge whose unknown target is X, and group B reaches X normally, X is never known as reachable from incomplete A and can be edited as B-only. Marking known nodes from A unsafe cannot protect unknown targets.  
   **Fix:** Build reverse ownership by independently scanning all decodable prefab references; if any edge target cannot be resolved, conservatively disable shared-sensitive baked edits globally or for the affected component/edge class.

2. **Persistent-source attribution has no concrete algorithm.** For damage whose `SpellSource` is the player or another non-graph entity, `owners(prefab) ∩ retained casts` yields no candidates; saying it is “attributed per event” does not explain how tests (d) and (e) can succeed.  
   **Fix:** Define the exact event-level correlation key—such as an ability/event field or a cast-token-to-event association—and classify persistent-source paths as unsupported if no unambiguous key exists.

3. **Reload discards active `forcetimeout` tracking.** Entries are dropped on reload, so an indefinite buff already under a timeout can survive forever because its spawn hook will not run again.  
   **Fix:** Preserve existing instance trackers with their original generation, or rescan live player buffs after reload and re-establish only those whose provenance remains known.

4. **Immutable generation lifetime is unspecified.** Ledger entries retain only `configGeneration`, while in-flight entities supposedly keep old behavior; unless old snapshots remain addressable, a reload can leave dangling generation IDs or force use of current rules.  
   **Fix:** Store the effective scale, clamps, timeout, and relevant policy directly in provenance, or reference-count immutable generation objects until no casts/entities use them.

5. **The same generation issue affects newly discovered old entities.** A cast can remain eligible for 120 seconds, so all generations referenced by retained casts must remain available even when no entity has yet been attributed.  
   **Fix:** Retain generations through at least the provenance window plus all attributed-entity lifetimes, with deterministic cleanup.

6. **The fail-safe attribution may disable common abilities for the full 120-second window.** Once two abilities sharing a generic prefab have been cast, every new instance using that prefab becomes ambiguous until one cast ages out—even if the actual cast is otherwise obvious temporally. This is safe but may make damage tuning functionally unreliable.  
   **Fix:** Measure ambiguity rates during the spike, expose them in inspect/telemetry, and define a shipping threshold; do not advertise an ability as supported if attribution frequently fails.

7. **Static `damage_coverage=full|partial|none` cannot represent runtime attribution failure.** An ability may have fully scalable fields but routinely remain vanilla because provenance is ambiguous or ownership is truncated.  
   **Fix:** Separate `term_coverage` from `attribution_support`, and report runtime attributed/ambiguous/unmatched event counters.

8. **Configurable provenance retention can invalidate long chains.** An admin may set it below a projectile, area, buff, or summon delay, silently converting supported hits to vanilla.  
   **Fix:** Enforce a safe minimum derived from known chain lifetimes and warn when indefinite or unknown-duration chains cannot be guaranteed.

9. **The failure-state documentation contradicts step 9.** Risks say spike failure means damage tuning is off unless baked fallback is enabled, but step 9 keeps the legacy power window enabled by default.  
   **Fix:** State that only the new per-hit path is disabled; legacy window tuning remains active unless explicitly turned off or replaced by baked fallback.

10. **P1’s temporary timeout attribution depends on undeclared infrastructure.** It requires knowing which captured groups the player cast within a retention window before the P2 ledger is introduced.  
    **Fix:** Introduce a minimal cast-history service in P1 explicitly, then extend it into the P2 provenance ledger.

VERDICT: REVISE
### Claude's response (round 3)
All 10 accepted: #1 unknown-target rule (minion prefabs never baked targets; any other unresolved edge class disables shared-sensitive edits for that component class globally); #2 concrete persistent-source rule (graph-node persistent sources attributed per event, uncached; character/non-graph sources unsupported); #3 forcetimeout trackers survive reload with their own snapshot; #4/#5 snapshots store effective values directly (no generation lifetimes); #6/#7 split term_coverage vs runtime attribution with counters + 20%/50-hit threshold; #8 retention floor 30 s + warning; #9 risk wording fixed; #10 CastHistoryService introduced in P1 and extended in P2.

## Round 4 — Codex
Rev 4 addresses all ten round-3 findings in substance. Four material issues remain:

1. **Provenance can misattribute native player abilities that share a prefab.** If cast history records only captured casts, a later native/item ability using the same generic prefab can be attributed to the sole retained captured cast.  
   **Fix:** Record all player casts as attribution candidates; scale only when exactly one owning candidate exists and that candidate is captured.

2. **`AllowGlobalSharedEdit` must not override incomplete graph safety.** When an unresolved edge exists, the complete affected-owner list is unknowable, so the promised boss/ability warning cannot be produced safely.  
   **Fix:** Make incomplete/unknown-edge global disables non-overridable; allow `AllowGlobalSharedEdit` only when ownership is complete.

3. **Cooldown prefab edits still unnecessarily affect bosses.** `AbilityTuningService` currently bakes `CooldownSeconds` and `Grant_MinimumCooldownSeconds` into shared ability prefabs, even though the runtime enforcer now provides a player-only path. This conflicts with the stated D0 policy.  
   **Fix:** Retire baked cooldown/floor writes and enforce absolute cooldowns, scales, and floors exclusively against player live cooldown state.

4. **The existing enforcer intentionally applies configured cooldowns without requiring capture.** Unless P3 reinstates a captured-grant gate, matching native player abilities can receive Beelzebub cooldown policy.  
   **Fix:** Require verified captured-ability provenance for per-ability cooldown changes; if the global floor intentionally covers all player abilities, implement that as a separately documented policy without touching NPC prefabs.

VERDICT: REVISE
### Claude's response (round 4)
All 4 accepted: #1 cast history records native + captured casts; attribute only when the single candidate is captured. #2 AllowGlobalSharedEdit only when ownership is complete. #3 baked cooldown/floor writes retired — all cooldown policy is player-only runtime (consistent with D0-B); transform-bar coverage extended or documented. #4 enforcer gated on captured provenance; floor applies to captured abilities only.

## Round 5 — Codex
Rev 5 addresses all four round-4 findings. The remaining uncertainty is appropriately contained behind explicit spikes, fail-safe vanilla behavior, completeness guards, telemetry, and documented fallbacks. I found no new material issue that should block implementation.

VERDICT: APPROVED
## Resolution
Converged: APPROVED at round 5 of 5 (plus one void round 0). Awaiting KDPen sign-off before any code.

## Post-build inspection

Claude built P1 (v0.132.0). Fresh read-only Codex session (thread 01a0cf81-7554-7702-b6a5-316c9fa25440), bundle piped on STDIN: PLAN.md + implementer deviation notes + diff + new files. MAX_INSPECTION_ROUNDS=2.

Pre-inspection self-review fixes (Claude): decision cache keyed per root; chain completeness via GetOrWalk(root); forcetimeout never extends an already-tracked buff.

### Round 1 — Codex findings (verbatim)

## Findings

1. **High — `AbilityChainGraph.cs`, `CollectEdges` / `IsOwnershipComplete`**
   
   Failure scenario: when an edge decoder fails, only the currently walked group becomes incomplete. If that unresolved edge targets a prefab reached normally by another group, that target remains “complete” and can receive a baked edit. This is the exact unknown-target leak P1 step 1’s global component-class disable was designed to prevent.
   
   Fix: track unresolved edge classes globally and make `Allowed` reject edits to every prefab/component class potentially produced by that edge class.

2. **High — `AbilityChainGraph.cs`, `Build`**
   
   Failure scenario: a top-level exception can leave `_groups` and `_owners` partially populated, but `Built` is still set to `true`; tuning then proceeds using incomplete ownership data and may modify shared boss prefabs.
   
   Fix: build into temporary dictionaries and publish them only after a successful full build; otherwise leave `Built=false` and fail baked tuning closed.

3. **High — `CastHistoryService.cs`, `MaxCastsPerPlayer` / `RecordCast`**
   
   Failure scenario: the fixed 64-record cap can discard casts well before the promised 120-second retention period. A player can readily exceed 64 casts in two minutes; dropping an older native/shared cast can turn an ambiguous buff into an apparently unique captured match and incorrectly destroy it.
   
   Fix: preserve every cast for the retention window, or retain per-group ambiguity information when compacting rather than dropping records blindly.

4. **Medium — `AbilityRules.cs` / `AbilityFieldTable.cs`, rule loading and runtime consumers**
   
   Failure scenario: the shared numeric validators run through command edits and baked writes, but hand-edited runtime values such as `DamageScale`, `CooldownScale`, `ForceTimeoutSeconds`, and summon settings are not shown being validated at load or before API/runtime use. NaN, infinity, or out-of-range JSON can therefore reach runtime behavior or API output.
   
   Fix: validate and normalize every numeric field while constructing the loaded rules snapshot, using `AbilityFieldTable`, before publishing it.

5. **Medium — `AbilityTuningService.cs`, `ApplyChain` near-set construction**
   
   Failure scenario: direct `SpawnOnStartCast` children are classified as deep nodes, so near-only existing knobs such as `duration` and spawned-buff movement changes are skipped on them. This makes tuning behavior depend on whether the otherwise-equivalent child came from `SpawnOnCast` or `SpawnOnStartCast`.
   
   Fix: include direct `SpawnOnStartCast` children of casts in the near set, or explicitly apply all existing spawn-level knobs to both direct-spawn edge types.

6. **Medium — `BeelzCommands.cs`, `.beelz cast` weapon check**
   
   Failure scenario: if equip-buff detection returns `WeaponFamily.None`, the command skips weapon validation entirely. An unequipped player or an unrecognized/new equip buff can therefore force-cast an ability prohibited by its weapon rule.
   
   Fix: resolve a genuinely unequipped player as `Unarmed`; otherwise fail closed when the current weapon cannot be determined.

7. **Low — `AbilityInspectService.cs`, `ReadNode` projectile-count inspection**
   
   Failure scenario: inspection reports only `AbilityProjectileFanOnGameplayEvent_DataServer.Count`; fan-on-tick, multishot, and cluster count fields requested by P1 step 2 are absent, so affected abilities receive incomplete inspection/export data without an error.
   
   Fix: decode and export the count fields for every supported fan, fan-on-tick, multishot, and cluster component.

8. **Low — `CastHistoryService.cs`, `CastRecord` / `RecordCast`**
   
   Failure scenario: P1 specified `{abilityGroup, castTime, fromSlot, captured?, snapshot}`, but `CastRecord` has no `fromSlot`. P1 timeout currently does not require it, but this leaves the promised P2-extension foundation incomplete and loses provenance that cannot reliably be reconstructed later.
   
   Fix: capture and store `fromSlot` at cast start now.

## Deviation assessment

1. **Offline `script_edges.tsv`: not acceptable as implemented.** An offline table can supplement live decoding, but there is no fail-closed detection of new/omitted scripted edge classes, and missing table/loading failures do not mark ownership unsafe. The sample also includes likely non-edge references such as `Script_CarriageData.ColliderPrefab`, showing the heuristic is not authoritative.

2. **Minion blob does not mark incomplete: acceptable with a condition.** This matches rule (a) if every possible target is reliably classified as a unit and unit prefabs are never edited. The current `CHAR_` name test should be verified against all blob targets; otherwise this becomes unsafe.

3. **Command renamed `ability-inspect`: acceptable.** It avoids collision with the existing player-inspection command and preserves the required functionality.

4. **Separate `form_blocks=` / `weapon_blocks=` tokens: acceptable.** The version bump and additive tokens communicate the semantics more cleanly, although it is a deliberate wire-shape departure from the literal plan wording.

5. **Expanded graph-based tuning reach: partially acceptable.** Replacing name-stem traversal is sound and safer, but the near-set omission for `SpawnOnStartCast` causes the concrete tuning gap noted above.

6. **Older shared writes now skipped: acceptable.** This is the intended safety behavior.

7. **Legacy global cooldown floor bypasses the shared guard: acceptable for P1.** P3 explicitly retires this baked behavior; it should remain clearly documented as the temporary legacy exception.

8. **Monotonic `Stopwatch` instead of ECS world time: acceptable if the dedicated server truly cannot pause.** It is monotonic and snapshot-safe. If world simulation can pause during loading/admin suspension, it changes semantics because timers continue advancing.

9. **Four pre-existing crash re-blocks: acceptable/out of scope.** They do not appear to interact adversely with the P1 infrastructure.

10. **Graph built only at initialization: acceptable for P1’s value-only mutations, but not the final step-13 design.** It is safe while prefab topology cannot change at runtime; the later atomic reload implementation must rebuild and publish graph/rules together as specified.
### Round 1 — Claude's dispositions
- #1 ACCEPTED — per-class fail-closed (`AbilityChainGraph.UnsafeClasses`: Buff/Projectile/Any from failed decode kinds; script table missing/unreadable or drift>0 => Any). Enforced in `Decide`, no override; surfaced in startup log + ability-inspect.
- #2 ACCEPTED — build into temporaries, publish on success; failure => `Built=false`, curated baked tuning skipped (legacy global cooldown floor unaffected).
- #3 ACCEPTED — cap 512; overflow inside retention => attribution ambiguous until the window clears.
- #4 ACCEPTED — `AbilityRules.ValidateNumerics` on every loaded/seeded snapshot before publish.
- #5 ACCEPTED — SpawnOnStartCast children of casts join the near set.
- #6 ACCEPTED — `.beelz cast` fails closed on undetectable weapon when the ability has any weapon rule (`SlotApply.IsWeaponRestricted`).
- #7 ACCEPTED — fan-on-tick / multishot / cluster counts in `fan_count`.
- #8 PARTIAL — `AbilityCastStartedEvent` = {Time, Character, Ability, AbilityGroup}; no slot field. Record stores the AbilityGroup entity for P2 to resolve; no invented slot index.
- Deviation 1 (live detection of new scripted classes) — REJECTED beyond drift fail-closed: enumerating arbitrary component types per prefab at runtime isn't feasible through IL2CPP interop. Regenerate the table after game updates (drift trips fail-closed).
- Deviation 2 condition — blob targets are never walked; accepted as-is.

### Round 2 — Codex findings (verbatim; same thread, dispositions + changed code sent via `resume ... -` on STDIN)

1. **Medium — `AbilityRules.cs`, `ValidateNumerics`**
   
   Failure scenario: `Phase` is a numeric field bounded to 1–20 in `AbilityFieldTable`, but it is omitted from load-time validation, so hand-edited invalid phases can still reach runtime/API consumers.
   
   Fix: validate `e.Phase` through the same integer helper, using the field-table default/fallback required for this non-nullable field.

2. **Medium — `SlotApply.cs`, `IsWeaponRestricted`**
   
   Failure scenario: the method tests effective compatibility across every weapon, which includes name-derived weapon classification. Consequently, an ability with no configured `Weapons` rule but heuristically classified as Sword/Axe/etc. is considered “restricted” and `.beelz cast` is rejected whenever equip detection fails.
   
   Fix: make `IsWeaponRestricted` inspect whether the resolved ability rule actually contains a non-empty `Weapons` list, rather than inferring restriction from `IsGrantCompatible`.

3. **Medium — `AbilityCastStartedSystemPatch` call site / `CastHistoryService.RecordCast`**
   
   Failure scenario: the previously supplied call site invokes `RecordCast(character, groupGuid)` with two arguments; because the new third argument defaults to `Entity.Null`, `GroupEntity` remains empty and the stated P2 slot-provenance handoff is not actually recorded.
   
   Fix: pass `evt.AbilityGroup` as the third argument and remove the optional default so future callers cannot silently omit it.

4. **Medium — `AbilityChainGraph.cs`, `LoadScriptEdges`**
   
   Failure scenario: malformed or truncated table rows are silently skipped. A corrupt embedded file can therefore load as an empty or partial table with `_scriptTableFailed=false` and `ScriptEdgeDrift=0`, allowing baked edits despite missing scripted ownership edges.
   
   Fix: treat malformed non-comment rows, duplicate-conflicting rows, or an implausible row/kind count as a table failure and set unsafe class `Any`.

5. **Low — `AbilityChainGraph.cs`, `Build` before its `try` block**
   
   Failure scenario: `LoadScriptEdges()` runs outside the guarded build block and `Built` is not cleared first. If its fast-path `CountDrift()` unexpectedly throws during a later rebuild, the exception escapes and a previously published graph remains marked `Built=true`.
   
   Fix: set `Built=false` before rebuilding and place `LoadScriptEdges()` inside the build `try`, publishing `Built=true` only after all graph and table validation succeeds.
### Round 2 — Claude's dispositions
- #1 ACCEPTED — `Phase` validated at load (out of 1–20 → reset to 1, logged).
- #2 ACCEPTED — `SlotApply.IsWeaponRestricted` now reads only the admin-configured `Weapons` list (concrete entry or `!` block); name-heuristic classification no longer counts.
- #3 ALREADY DONE for the call site (it passes `evt.AbilityGroup`); ACCEPTED the hardening — the optional default is removed.
- #4 ACCEPTED — `tools/build_script_edges.py` writes a `# edges=N` integrity header (regenerated: 1171 rows, identical data); the loader fails the table (→ unsafe class Any) on any malformed row, a missing/mismatched header count, or zero rows.
- #5 ACCEPTED — `Built=false` at rebuild start; `LoadScriptEdges()` moved inside the guarded build.

Build after round 2: 0 errors / 0 warnings. Inspection rounds used: 2/2 (cap). Next gate: KDPen deploys v0.132.0 and tests in game.

# REV 6 — backlog delta review (2026-09-23; thread 01a0cfbe-0929-7b70-87ff-2a90a07a3435)

## Rev-6 Round 1 — Codex
1. **Lock enforcement is inconsistent across access paths:** slot/form/mounted filtering considers only `perSlot`, while `.beelz cast` considers bar + hotkeys, so a form or mounted ability can coexist with a conflicting hotkey and still be injected.  
   Fix: Build one canonical “currently usable abilities” set—resolved active bar/form/mount first, then hotkeys—and use it at every injection and cast point.

2. **Bucket commands check the wrong context:** `weapon-grant` and `form-grant` are prospectively checked against the current live bar, although the edited weapon/form bucket may have a different loadout and priority order.  
   Fix: Resolve the prospective target bucket exactly as it will appear when active, combine it with hotkeys, then run the resolver.

3. **Adding a lock does not remove already-injected suppressed grants:** `RestoreResolvedGrants`/`ResolveAndInjectGrants` only inject accepted entries; the supplied code does not remove an existing `ReplaceAbilityOnSlotBuff` entry merely because it is now suppressed.  
   Fix: Before reinjection, remove Beelzebub-owned overrides for all resolved saved slots, then inject only the resolver’s kept set and restore suppressed slots to their bases.

4. **Active forms remain noncompliant after an admin lock edit:** the plan explicitly delays form reapplication until the next entry, allowing forbidden combinations indefinitely in a long-lived form or mount.  
   Fix: Re-resolve and rebuild every active form/mounted instance immediately after lock mutations.

5. **Direct `ApplyGrant` remains an enforcement bypass:** the plan names `ResolveAndInjectGrants`, but the supplied `ApplyGrant` independently writes an override and has no exclusion check. Any caller not passing through the listed commands can inject a locked combination.  
   Fix: Put exclusion resolution in the lowest shared injection layer, or require `ApplyGrant` to validate a complete prospective active loadout.

6. **Priority semantics are underspecified for duplicate abilities:** “first `Max` distinct abilities” means the same ability bound in several slots/hotkeys consumes one place, but suppression output is keyed only by ability and cannot say which duplicate binding should remain/render.  
   Fix: Define whether limits count distinct GUIDs or bindings; return suppression by binding key while tracking distinct GUID occupancy separately.

7. **Multi-group diagnostics lose information:** an ability must survive every group, but the output promises only “the group name”; simultaneous failures can become order-dependent and misleading.  
   Fix: Return all rejecting groups deterministically and use a stable primary group only for the short chat/event message.

8. **Dropping unknown exclusion members is destructive and timing-sensitive:** prefab-name/GUID resolution may be unavailable during early load, and deleting unknown values prevents forward-compatible or temporarily missing abilities from becoming valid later.  
   Fix: Preserve unresolved members, warn and treat them as inactive; re-resolve after prefab initialization/reload.

9. **The lock event schema does not fit all enforcement points:** `slot-locked slot=` is undefined for hotkeys and direct casts, and repeated resolver runs may spam BCH even if chat is throttled.  
   Fix: Add `source=slot|hotkey|cast`, an optional binding key, and transition-based event deduplication.

10. **Telemetry cannot validate the per-hit scaler while the legacy power window remains active:** target HP-after includes the legacy caster-power buff, so the truth table measures a combination of old scaling and the proposed factor.  
    Fix: Add a controlled telemetry mode that suppresses the legacy window for test casts, or record paired unmodified controls and explicitly account for the active legacy multiplier.

11. **Switching to `Scale` can still double-scale:** “disabled for new casts” leaves already-instantiated carrier buffs alive for up to `powerwindow`, while new per-hit events are mutated under `Scale`.  
    Fix: Make mode part of each cast snapshot and, on entering `Scale`, remove/expire all Beelzebub power-carrier instances or delay per-hit scaling until every legacy window has drained.

12. **The proposed mutation names the wrong field:** the supplied API and earlier plan use `RawDamageValue`, but REV 6 says `RawDamage`; this is either a compile failure or mutation of the wrong member.  
    Fix: Confirm the generated interop member names in the referenced assembly and centralize copying/mutation in a compile-tested `DamageMath` adapter.

13. **Replacing the spike gate does not prove write safety:** an admin selecting `Scale` cannot establish that `Unsafe.AsRef(in copy.F)` produces a valid mutable struct or that `SetComponentData` occurs before every consumer of the event.  
    Fix: Keep a hard runtime capability probe/test result; refuse `Scale` and remain in `Telemetry` if mutation or producer/consumer ordering has not been demonstrated.

14. **Prefix/postfix HP correlation is incomplete:** damage-event entities may be consumed or destroyed during `OnUpdate`, and several events can target the same entity, so a postfix cannot reliably recover each event’s “after” HP from the original query.  
    Fix: Snapshot `(event identity, target, HP-before)` in the prefix and report target HP-after from that snapshot with explicit aggregation/“event consumed” handling.

15. **Damage ownership traversal is too shallow in the supplied code:** `ResolveOwningPlayer` stops after four `EntityOwner` hops, while the graph/provenance design allows eight-hop chains; nested sources will become unmatched before attribution is attempted.  
    Fix: Use one cycle-safe eight-hop owner resolver shared by damage, timeout, and summon paths, with separate unsupported/truncated counters.

16. **The 4096 provenance-cache cap has no defined fail-safe behavior or observability:** unlike cast-history overflow, cache saturation could silently stop caching or evict live entries and later misattribute them.  
    Fix: Never evict live provenance; mark overflowed sources unmatched/ambiguous and expose saturation/drop counters in `damage-stats`.

17. **Cooldown enforcement still lacks the cast identity required by the plan:** current pending state is keyed only by `(steamId, abilityGuid)`, so rapid recasts overwrite one another and an old cooldown state can satisfy a newer pending cast.  
    Fix: Record a monotonic cast ID, `fromSlot`, cast time, and snapshotted cooldown policy, then key completion to that cast and the specific newly-started state instance/signature.

18. **Gating cooldowns only on `Captured` is insufficient:** `.beelz cast` is also captured, yet should use its own tracker; the current `CastRecord` has no `fromSlot`, exactly as its comment admits.  
    Fix: Resolve and snapshot `fromSlot` at cast start, and make the native-bar enforcer require `Captured && FromSlot`.

19. **Absolute cooldown support for charge abilities is asserted without evidence:** the current enforcer edits `AbilityCooldownState`, while charge restoration may use a different state/timer; “absolute still applies” can be ineffective or corrupt recharge behavior.  
    Fix: Gate both scale and absolute cooldown handling for charge abilities until the exact recharge state is verified, or implement a separately tested charge path.

20. **The three-way merge omits deletion semantics:** field-by-field adoption explains changed values and additions, but not fields or entries removed from the new shipped default, nor server-side deletions relative to baseline.  
    Fix: Model presence/absence as values with tombstones and define the full 3-way table for entry deletion, field deletion, and admin deletion.

21. **Entry identity by dictionary key can duplicate renamed abilities:** if a shipped entry changes from prefab name to numeric GUID, or a prefab is renamed while retaining its GUID, the merge sees a new entry instead of the same ability.  
    Fix: Canonicalize `AbilityMap` identity to resolved GUID where possible and report unresolved/duplicate aliases as conflicts.

22. **Nested values and arrays have no merge contract:** “field by field” over `JsonElement` does not define whether `Weapons`, `Forms`, or future nested objects are atomic, element-merged, or compared with order sensitivity.  
    Fix: Define schema-aware merge rules—scalar fields independently, lists atomically with normalized comparison unless an explicit set policy exists, unknown objects preserved atomically.

23. **The baseline lifecycle after `merge` is missing:** only seed and replace are said to write the baseline; if merge leaves the old baseline, every later preview repeats shipped changes and conflicts.  
    Fix: After a successful merge/reload, atomically advance the baseline to the exact new shipped default.

24. **Rules and baseline updates are not transactional:** backup → write → validate → reload can publish an invalid rules file or leave rules and baseline from different generations after a crash.  
    Fix: Build and validate prospectively, write both temporary files, atomically replace them as a recoverable transaction, and restore the backup if reload/reapply fails.

25. **No-baseline hard-block handling is ambiguous and incomplete:** forcing `Enabled=false` only for entries “whose GUID is in the hard-block set” does not specify name-key resolution failures or existing server entries representing the same GUID.  
    Fix: Canonicalize every resolvable entry to GUID, force-disable all matching existing and newly added aliases, and report unresolved hard-block identities as fatal to reseed.

26. **Shape/cast-time scaling risks compounding on reload:** REV 6 says fields “scale” original values but does not require calculations from captured originals; repeatedly applying against already-mutated prefabs would multiply again.  
    Fix: Store immutable startup originals per prefab/component/field and always compute `desired = original × configuredScale`, including cast time, knockback, and projectile counts.

VERDICT: REVISE
### Claude's response
Accepted 25/26 -> PLAN.md 'Rev 6.1'. Rejected #12: reference-assembly dump shows DealDamageEvent.RawDamage (RawDamageValue is DealDamageParameters).

## Rev-6 Round 2 — Codex
Most prior findings are addressed, and #12 is resolved by the assembly evidence. Material issues remain:

1. **Mode snapshots still permit double-scaling across same-ability casts:** attribution selects the newest retained cast of a group, so a delayed entity from an old `window` cast first seen after a new `perhit` cast can inherit `PerHit=true` while the old power buff remains active.  
   Fix: If retained casts for the matching group have different damage snapshots/paths, treat first-sight attribution as ambiguous until only one snapshot remains.

2. **The write-probe fallback does not stop already-snapshotted Scale casts:** changing the global mode to Telemetry leaves existing `CastRecord.PerHit=true`, so they continue attempting mutation after the probe trips.  
   Fix: Add a global per-hit circuit breaker checked before every mutation, independent of the cast snapshot, and emit the failure once.

3. **Readback after every mutation is excessive hot-path overhead:** every damage event would incur both `SetComponentData` and an additional `GetComponentData`, undermining the stated O(1), low-overhead combat path.  
   Fix: Probe the first mutation per runtime/component version, then use sampled verification or an explicit diagnostic mode.

4. **“DamageScale=1 means no telemetry window” is false when Boosted mode contributes a non-1 global factor:** `ComputeEffectiveScale` can still instantiate the legacy carrier.  
   Fix: Require the complete effective legacy scale to equal 1, or explicitly disable the legacy window for controlled telemetry sessions.

5. **Aggregated HP deltas cannot prove the mutation’s factor ratio:** multiple damage events, healing, regeneration, shields, mitigation, and unrelated damage may affect the same target between prefix and postfix.  
   Fix: Label aggregated rows as observational only and require isolated one-event test cases—or hook a later per-event resolved-damage value—before Scale sign-off.

6. **Active form rebuilding cannot restore overwritten native entries from the supplied implementation:** `ApplyFormLoadout` mutates existing native `ReplaceAbilityOnSlotBuff` entries in place, so “clear old entries first” loses the original `NewGroupId` and cannot restore a newly suppressed slot’s native ability.  
   Fix: Snapshot each form/mount buffer before first modification and restore that immutable original before every re-resolution.

7. **Suppression transition messaging has no unlock representation:** transition-deduplication detects removed suppressions, but `type=ability-locked` cannot tell BCH that a previously locked binding is now available.  
   Fix: Emit `type=ability-unlocked` or add `locked=0|1` for both sides of every suppression transition.

8. **`ApplyGrant` checking the current active loadout is unsafe during batch restoration:** when several grants are being injected sequentially, the engine-visible bar may not yet contain the rest of the prospective batch, making results dependent on update timing.  
   Fix: Resolve the complete prospective batch once and pass an approved set into low-level injection; reserve `ApplyGrant`’s standalone check for single-binding callers.

9. **The cooldown force-cast marker can misclassify casts:** a `(steamId, group)` one-shot may be consumed by an intervening native cast of the same group, or remain stale if force-casting fails or emits no cast-start event.  
   Fix: Give markers a short expiry, clear them synchronously on failure, and correlate them with a ForceCast invocation token/entity where available; otherwise fail closed as `FromSlot=false`.

10. **The cooldown revision does not state how multiple pending CastIds are stored:** retaining the current `(steamId, abilityGuid) → Pending` dictionary would still overwrite an earlier cast despite adding `CastId`.  
    Fix: Specify a per-player/group queue or CastId-keyed pending table and deterministically match each newly changed cooldown state once.

11. **The claimed rules/baseline transaction is not crash-atomic:** two sequential `File.Replace` calls can leave new rules with the old baseline, or vice versa, if the process stops between them; runtime rollback cannot repair that crash case.  
    Fix: Add a transaction journal/generation manifest and perform startup recovery, or store rules and their baseline in one atomically replaced envelope.

12. **`File.Replace` does not handle the no-baseline case:** the first merge has no destination baseline file, so replacing `baseline.tmp` into it can fail.  
    Fix: Use replace-if-present and same-volume atomic move-if-absent, with both cases covered by tests.

13. **GUID alias collisions remain undefined:** if the server already contains two keys resolving to the same GUID, “merge onto the server’s existing key” does not identify which key wins and may preserve contradictory entries.  
    Fix: Detect duplicate resolved GUIDs before merging and either reject with a conflict or consolidate them using an explicit deterministic rule.

14. **The test plan still misses the new failure-prone integration cases:** pure resolver/merge tests will not cover native form restoration, same-group window→per-hit transitions, force-marker expiry, or crash recovery between the two file replacements.  
    Fix: Add targeted integration/checklist cases for all four scenarios, plus a simulated transaction interruption test.

VERDICT: REVISE
### Claude's response
Accepted all 14 -> 'Rev 6.2'.

## Rev-6 Round 3 — Codex
Rev 6.2 addresses 13 of the 14 prior findings adequately. One material cooldown-attribution flaw remains:

1. **Force-cast marker loss is not fail-safe as claimed:** if the marker expires or is consumed by an intervening same-group bar cast, the actual force-cast is classified as `FromSlot=true`. If that ability is also slotted, the native-bar enforcer can match its cooldown state and apply slot cooldown policy to a `.beelz cast`; meanwhile, the genuine bar cast that consumed the marker is skipped.  
   Fix: Do not default marker absence to `FromSlot=true`; determine slot provenance from `CastRecord.GroupEntity`/the player’s `AbilityGroupSlot` state, using the marker only to prove a force-cast, and classify unresolved provenance as unknown/unenforced.

A smaller specification gap should also be closed during implementation:

2. **`BaselineHash` must be excluded from the schema-level three-way merge:** otherwise it can be treated as an admin-changed scalar or unknown field and generate artificial conflicts.  
   Fix: Treat `BaselineHash` as transaction metadata outside the merge tree and compute it only after serializing the baseline bytes that will be installed.

VERDICT: REVISE
### Claude's response
Accepted both -> 'Rev 6.3' (structural FromSlot via slot StateEntity; BaselineHash outside merge).

## Rev-6 Round 4 — Codex
Rev 6.3 addresses both remaining findings. Structural `GroupEntity` matching makes cooldown provenance fail-safe, and `BaselineHash` is correctly isolated from merge semantics. No new material issues found.

VERDICT: APPROVED
Converged: APPROVED at rev-6 round 4.

## Post-build inspection — P2 (v0.133.0), round 1 (fresh thread 01a0cfcb-8ee9-7981-b206-eeac9cde898d)

- **High — `Services/DamageScaler.SnapshotFor`**  
  **Failure scenario:** The effective per-hit scale omits `Defaults.DamageScale` by passing `1f` into `DamageMath.EffectiveScale`; global default tuning has no effect in Scale or Telemetry modes.  
  **Fix:** Pass the active rules snapshot’s `Defaults.DamageScale` as `defaultsScale`.

- **High — `Logic/DamageMath.TryComputeHit`**  
  **Failure scenario:** An event with `MainFactor <= 0` still has positive `RawDamage` scaled under `FlatPolicy.Scaled`, despite the specification requiring such entries to remain entirely untouched.  
  **Fix:** Return `false` before modifying either term unless `mainFactor` is finite and greater than zero.

- **High — `Services/DamageScaler.Process` provenance cache**  
  **Failure scenario:** Only successful attribution is cached. An initially ambiguous or unmatched long-lived entity can become attributed after older casts expire, causing later ticks from the same entity to be scaled despite its first-sight result being vanilla.  
  **Fix:** Cache terminal negative outcomes for ephemeral entities as well as successful provenance, and retain them until the entity dies.

- **Medium — `Services/CastHistoryService.RetentionSeconds`**  
  **Failure scenario:** Provenance retention is hard-coded to 120 seconds; `Damage_ProvenanceRetentionSeconds`, its 30-second floor, and warnings for longer/indefinite chains are not implemented.  
  **Fix:** Load and validate the configured retention value and emit startup warnings for captured chains exceeding it.

- **Medium — `Services/DamageScaler.Process` / `Trace`**  
  **Failure scenario:** `Damage_Mode=Telemetry` does not itself produce the required truth-table line or HP probe; both occur only when the unrelated mutable `DamageScaler.Trace` flag is enabled.  
  **Fix:** Record and log truth-table telemetry whenever mode is `Telemetry`, optionally retaining `Trace` as an additional Scale-mode diagnostic.

- **Medium — `Services/DamageScaler.RecordProbe` / `FlushProbes`**  
  **Failure scenario:** Multiple events against one target are collapsed while retaining only the first event’s group, path, and factor ratio; the output also omits the event entity and per-event HP-before association, so mixed simultaneous hits produce misleading truth-table evidence.  
  **Fix:** Store per-event probe records, or aggregate only compatible records while logging each event identity and its prefix HP snapshot.

- **Medium — `Patches/DealDamageSystemPatch` Harmony declaration**  
  **Failure scenario:** The patch has no explicit Harmony priority, so ordering against other `DealDamageSystem.OnUpdate` prefixes is uncontrolled and the mutation may occur before or after competing consumers/modifiers. Startup patch-owner reporting is also absent.  
  **Fix:** Add an explicit `[HarmonyPriority(...)]` and enumerate/log other patches on this method at startup.

- **Medium — `Services/DamageScaler.ResolveOwningPlayer`**  
  **Failure scenario:** The owner walk detects only self-links and cycles returning directly to `start`; a cycle such as `A → B → C → B` repeatedly traverses until the hop limit and is incorrectly reported as truncation.  
  **Fix:** Track every visited entity index/version during the eight-hop walk and stop immediately on any repeat.

- **Medium — `Services/DamageScaler.ResetCounters` / mode handling**  
  **Failure scenario:** `_writes` is never reset when `Damage_Mode` changes, so returning to Scale mode does not verify its first 20 mutations as required and may perform only the 1-in-256 probe.  
  **Fix:** Detect mode transitions and reset the write-probe sampling counter on every change.

- **Medium — `Services/DamageScaler` counters and reporting**  
  **Failure scenario:** `Unsupported` is global rather than per ability, so `damage-stats`, `inspect`, and reliability calculations cannot report the required per-ability unsupported count or include unsupported attributed attempts in that ability’s runtime assessment.  
  **Fix:** Attribute unsupported cases to a determinable candidate group where possible and maintain unsupported counters in each group’s `Counters`.

- **Medium — `Services/SummonAllyService.ScaleSummonToPlayer`**  
  **Failure scenario:** Owner-relative power is written directly to `UnitStats` with no verification or persistent modifier; a later level/stat refresh can overwrite it, reverting the summon to baked power.  
  **Fix:** Verify after stat refresh/level assignment and, if overwritten, apply the owner-relative values through a durable stat-modifier buff.

- **Low — `Services/DamageScaler.Process` persistent/ephemeral classification**  
  **Failure scenario:** Cache eligibility is determined only from prefab-level `Root`/`StartAbility` graph edges; it does not explicitly exclude slot-state or weapon entities, and a prefab used as both persistent and ephemeral is globally treated as persistent, allowing delayed ephemeral hits to lose attribution after retention expires.  
  **Fix:** Classify the actual source entity by components/entity role, excluding character, player, slot-state, and weapon entities while caching genuine ephemeral instances.

### Claude's dispositions
1. Defaults.DamageScale omitted — **REJECTED.** `GetDamageScale` already returns the entry value, or `Defaults.DamageScale` when there is no entry (the long-standing semantics, used by the legacy window too). Passing Defaults as well would double-apply it for every un-entried ability.
2. MainFactor ≤ 0 still scales RawDamage — **ACCEPTED.** `TryComputeHit` now returns false before touching either term; test added.
3. Negative first-sight outcomes not cached — **ACCEPTED.** Ambiguous/Unmatched/Native verdicts on ephemeral sources are cached as group 0 (vanilla for life).
4. Retention hard-coded — **ACCEPTED (partial).** New cfg `Damage_ProvenanceRetentionSeconds` (default 120, floor 30). The startup chain-lifetime warning is NOT built: the prefab data has no reliable chain-lifetime signal; logged as a known gap.
5. Telemetry needs Trace to log — **ACCEPTED.** Truth-table lines and HP probes are on whenever the mode is Telemetry; the postfix flushes whenever probes exist.
6. Probe collapsing — **ACCEPTED.** Per-hit lines now carry the event entity. A target hit by disagreeing casts/paths in one update reports `MIXED` instead of a misleading ratio.
7. Harmony priority / co-patchers — **ACCEPTED.** Prefix is `Priority.First`; other owners on `DealDamageSystem.OnUpdate` are logged when Core becomes ready.
8. Owner-walk cycles — **ACCEPTED.** Full visited set over the 8 hops.
9. Write sampling not reset on mode change — **ACCEPTED.** `EffectiveMode` resets the sampling counter on every transition.
10. Unsupported per ability — **REJECTED.** "Unsupported" means no correlation key exists (character-sourced or non-graph source), so there is no group to charge it to; it stays a global counter.
11. Summon stat overwrite — **DEFERRED to the in-game checklist** (already listed). A durable modifier buff is only worth building if the checklist shows the write being reverted.
12. Persistent/ephemeral by prefab — **REJECTED.** Characters are excluded (`source == player`), and weapons/non-graph entities are unsupported. Slot-state entities are Root/StartAbility prefabs, so they are persistent. A dual-use prefab is treated as persistent, which is the fail-safe direction: per-event attribution, vanilla once the cast ages out.

Build clean, 41/41 tests pass. Round 2 not run: every accepted fix is local and mechanical.

## Post-build inspection — P3 (v0.134.0), round 1 (fresh read-only thread)

### Codex findings (verbatim)

- **High — `Services/AbilityCooldownEnforcer.cs`, `HasCharges`; `Commands/BeelzCommands.cs`, `.beelz cast` cooldown calculation.** Charge detection checks only `AbilityChargesData` on the group prefab, while charge data may reside on a direct `StartAbility` cast prefab; the command path unconditionally passes `hasCharges: false`. Such abilities incorrectly receive cooldown overrides/scaling despite rev 6.1 #19 requiring charge abilities to be skipped entirely. **Fix:** use one shared graph-aware charge detector covering the group and direct cast prefabs, and pass its result to both runtime enforcement and `CooldownMath.Resolve` in `.beelz cast`.

- **Medium — `Services/AbilityCooldownEnforcer.cs`, `OnCast`; `Patches/AbilityCastStartedSystemPatch.cs`.** `OnCast` retrieves `Latest(steamId, guid)` rather than the record created for the current event. Because the patch catches `RecordCast` failure and still invokes `OnCast`, a previous record for the same ability can be queued again, causing double/stale enforcement against its old `GroupEntity`. **Fix:** make `RecordCast` return the new `CastRecord`/`CastId` and pass it directly to `OnCast`; skip enforcement when recording fails.

- **Medium — `Services/AbilityCooldownEnforcer.cs`, `Tick`.** After consuming a cooldown, the code rebases every remaining queue entry’s `PreEnd[index]` even when that entry records a different `GroupEntity`. If the same ability GUID is present through two slot-state instances, a write to one slot contaminates the other slot’s snapshot and can make its next real cooldown look already observed or changed spuriously. **Fix:** rebase only pending entries whose `GroupEntity == p.GroupEntity`, or key queues by the recorded state entity in addition to player/group.

- **Medium — `Services/AbilityTuningService.cs`, `WriteSpawnedFields` lifetime block.** `lifetime` writes every near, non-`Buff` prefab carrying `LifeTime`; it does not require the prefab to be a projectile or area. An exclusive helper/event prefab with `LifeTime` can therefore have unrelated scheduling/destruction semantics changed, exceeding the “spawned projectiles/areas only” requirement. **Fix:** require an explicit supported projectile/area component classification before capturing or writing `LifeTime.Duration`.

### Claude's dispositions
- **#1 High (charge detection only on group; `.beelz cast` passed hasCharges:false)** — ACCEPTED. `AbilityCooldownEnforcer.HasCharges` now walks the group + its direct StartAbility casts via AbilityChainGraph (same scope as CastTimeRefusal); `.beelz cast` passes `HasCharges(abilityGuid)` to CooldownMath.Resolve.
- **#2 Medium (OnCast used Latest, stale record possible)** — ACCEPTED. `RecordCast` returns the new CastRecord; the patch passes it to `OnCast`; null (failed record) or a group mismatch = no enforcement.
- **#3 Medium (PreEnd rebase across different GroupEntity)** — ACCEPTED. Rebase only pending entries with the same GroupEntity.
- **#4 Medium (lifetime on any non-Buff LifeTime prefab)** — ACCEPTED. Now requires `Projectile` or a `HitColliderCast` buffer.

Rebuilt 0/0, tests 44/44. Round 2 not run (fixes are small and local; the locks/reseed inspection below re-reads these files).

## Post-build inspection — locks + reseed (v0.135.0), round 1 (fresh read-only thread)

### Codex findings (verbatim)

- **High — `Services/ExclusionService.cs` — `ActiveBar`, `FilterBar`, `CheckBind`**  
  The canonical loadout contains only Beelzebub-saved injections. Vanilla weapon-slot abilities and native form/saddle abilities are absent. A native ability and a granted ability belonging to the same exclusion group can therefore coexist on the live bar, and category groups undercount the loadout.  
  **Fix:** construct the lock input from the complete resolved live slot state/native snapshot, overlay proposed grants, and then append hotkeys.

- **High — `Commands/AdminCommands.cs` — admin grant / weapon-grant paths around the added `CheckBind` calls**  
  These paths only append a `[LOCKED: … saved, but kept off the bar]` warning and continue saving the bind. The stated contract requires grant, weapon-grant, and form-grant to refuse incompatible assignments; player-facing commands do refuse them. This creates an admin-only path that installs binds the plan says must be rejected.  
  **Fix:** return immediately when `CheckBind` is non-null, before mutating or persisting the registry.

- **High — `Services/SlotApply.cs` — `ResolveAndInjectGrants`**  
  Surviving slots are appended with `buffer.Add` without first removing existing entries for those slots. Reapply operations—lock edits, reloads, reseeds, refreshes—can accumulate duplicate overrides on the equip buff, potentially leaving an older locked ability effective depending on engine ordering and growing the buffer indefinitely.  
  **Fix:** call `RemoveSlotEntries(buffer, slot)` before adding each surviving override, while still deferring structural operations until after buffer work.

- **Medium — `Services/ExclusionService.cs` — `CheckCast` and other prospective checks**  
  Active transforms are not exempt. `.beelz cast` calls `CheckCast` while transformed, but `ActiveBar` models the underlying weapon/form bindings rather than recognizing `GetActiveTransform`; an unrelated underlying conflict can therefore refuse a cast during a transform. Binding checks made during a transform can likewise enforce locks against a bar that is not active.  
  **Fix:** bypass exclusion enforcement whenever `AbilityRegistry.GetActiveTransform(steamId)` is non-null.

- **Medium — `Services/AbilityCooldownEnforcer.cs` — `_pending`, `Tick`**  
  Pending casts are FIFO-keyed only by `(steamId, abilityGuid)`, although cooldown detection is per `GroupEntity`. If the same ability is available from two slot-state entities and the second slot starts its cooldown before the first, the first queue item blocks inspection of the second until it starts or expires. The second cast can consequently miss its six-second window and retain vanilla cooldown.  
  **Fix:** queue independently by `(steamId, abilityGuid, GroupEntity)` or scan/process each group’s head independently instead of globally blocking on `q.Peek()`.

- **Medium — `Services/ReseedService.cs` — `Write`**  
  The transaction advances the baseline before `AfterWrite`. If reload/reapply then throws, the catch restores the old rules file but leaves the new baseline in place and does not reload the restored rules. Disk metadata is inconsistent, the next merge falls into SAFE mode, and in-memory state may reflect a partially loaded failed candidate despite the success message claiming the previous rules were kept.  
  **Fix:** stage both rules and baseline, commit them as one recoverable transaction, and on any post-commit failure restore both files and explicitly reload the restored rules.

- **Low — `Services/ReseedService.cs` — `Write`**  
  The baseline update uses `File.WriteAllText` directly, despite the plan’s atomic-write requirement. A crash or disk failure can truncate `ability_rules.baseline.json`; the main rules file remains valid but all subsequent merges degrade to SAFE mode.  
  **Fix:** write the baseline to a sibling temporary file and atomically replace/move it, retaining or restoring its prior version on failure.

- **Low — `Services/ExclusionService.cs` — `Report` / `FilterBar`**  
  Notice state is keyed by `(player, source)`, but switching active sources does not clear the old source’s `_last` entry. A player who leaves a form or dismounts can retain stale suppression state; on returning to that source, an unchanged suppression produces no new locked notice, and unlock events may be emitted much later against an unrelated transition.  
  **Fix:** clear/report the previous active source on source transitions, or maintain one per-player active-source snapshot and discard inactive-source state.

### Claude's dispositions
- **#1 High (vanilla/native abilities not in the lock loadout)** — REJECTED (scope). Locks govern Beelzebub-GRANTED abilities (D2: "on one player's resolved bar" of grants); vanilla spellbook/weapon abilities are the game's own balance, and a player re-picking a vanilla spell already auto-yields our slot. Documented in ABILITY_CONFIG §6 ("Scope: Beelzebub-granted abilities only").
- **#2 High (admin set-slot doesn't refuse)** — REJECTED (by design). Admin set-slot is an explicit override (it already bypasses Enabled/TransformOnly with warnings); it now also warns `[LOCKED: …]`, and the bar resolve still keeps the ability off the live bar. Documented.
- **#3 High (duplicate overrides on re-resolve)** — ACCEPTED. `ResolveAndInjectGrants` strips the slot's entries before adding (same as ApplyGrant/ReplaceSlotEntry); CopyCooldown computed before the strip.
- **#4 Medium (transforms not exempt at cast)** — ACCEPTED for `.beelz cast` (CheckCast returns null while transformed). Bind checks while transformed still check the underlying bar on purpose (that's the bar the bind lands on).
- **#5 Medium (cooldown FIFO blocks across slot states)** — ACCEPTED. Queue key is now (steamId, ability, GroupEntity).
- **#6 Medium / #7 Low (reseed not transactional; baseline not atomic)** — ACCEPTED. Both files staged to .tmp and committed with Replace/Move; baseline backed up; any commit failure restores both (or deletes a new baseline when none existed → SAFE mode). Reload/reapply is post-commit and reported, not rolled back.
- **#8 Low (stale notice state across sources)** — ACCEPTED. Reporting for slot/form/mount discards the player's other-source state.

Rebuilt 0/0, tests 44/44. Round 2 not run.
