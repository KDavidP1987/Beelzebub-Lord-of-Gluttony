# Audit — transform-chain-guard (v0.137.5)

Plan: `docs/dod/transform-chain-guard.md` (20 items). It went through three Codex rounds and was approved by a human
review (Review 4) on 2026-10-04.

The guard: every route that applies or re-applies a transform form now asks one pure gate first
(`Logic/TransformGate.cs`). It refuses:
- a re-apply over an async form buff that is still spawning (the v0.49 Burst double-destroy);
- a transform-to-transform chain (v0.120);
- a phase switch with nothing active.

Before this change only `TryActivate` refused, and that lived in IL2CPP code with no test. `ApplyPhase`,
`ReapplyActiveTransform` and admin `testform` had no pending check, and `testform` reverted and applied in the same
frame. No in-game failure had been seen on those routes; this is a recurrence guard.

## Pre-audit
### Steps 1-2 · 2026-10-04 · 8913810 (+ plan draft 82bcf39)
- git status: only the owner's `docs/V0136_ABILITY_TEST_PLAN.xlsx` / `_matrix_build.py` (never staged) and the plan
  files.
- compile: 0 errors (1 pre-existing CS8073 warning in `GrantPush.cs`). tests: 243/243 with the new Logic.
- preflight: PREFLIGHT OK (10 checks) on v0.137.4.
- dod status: 0/20 verified (draft → ready on human Review 4, dc6025a → in-progress).

## Post-audit
### Steps 1-4 · 2026-10-04 · 8913810, b6b9318, fb8638c
- **Steps 1-2 (8913810, revised through review rounds 1-3):**
  - `Logic/TransformGate.cs`: `Decide`, `SafeName`, `Message`, `LogLine`.
  - `Logic/TransformGuardLog.cs`: once-per-episode TXGUARD ledger. `Cap` is 512, from 40 players × 9 reachable
    (route, reason) pairs.
  - 13 named controls in `Beelzebub.Tests/TransformGateTests.cs`.
  - `tools/check_transform_guard.py`.
- **Step 3 (b6b9318):**
  - `TryActivate` and `ApplyNativeFormTest` ask `TransformGate.Decide`. Testform no longer reverts; it refuses with
    "revert first".
  - `ApplyPhase` and `ReapplyActiveTransform` ask `PhaseGate` before any state change.
  - `Passed` logs a refusal once per (player, route, reason).
  - `Revert` calls `Forget`.
  - `ReconcileOnLogin` calls `ClearPendingForm` before its re-apply.
  - `TransformCommands.Phase` asks `PhaseGate` first.
- **Step 4 (fb8638c):**
  - BCH handoff v0.137.5 block: no wire change, ApiVersion stays 35.
  - `docs/dod/profile.md` Probe 6.2 note.
- compile: 0 errors, same single pre-existing warning. tests: 243/243.
- preflight: PREFLIGHT OK (10 checks). The version line still names 0.137.4; the release commit (step 7) bumps it.
- `check_transform_guard.py`: everything is ok — wiring, actors (8 route calls in 7 allowed methods, 7 form applies
  inside the 4 gated routes), tests, secrets, handoff, profile, selftest (10 checks).
- **Planted faults.** Each broke the code once, its check failed, then it was restored. Details are in the plan Log.
  - D4: ApplyPhase gate moved below `CurrentPhase =` → `wiring: FAIL ApplyPhase: state change before the gate`.
  - D4: the testform revert restored → `wiring: FAIL ApplyNativeFormTest: … calls Revert`.
  - D5: `Forget` dropped from Revert → `wiring: FAIL Revert: does not Forget …`.
  - D1–D3 and D20: ten Logic faults, each caught by its named test (scratchpad `plant_gate.py`, plan Log).
- **/code-review (medium, a38c168..fb8638c): 2 findings.**
  - CR1 · ACCEPTED (A2, discovered 7.3).
    - The combat-end reset to phase 1 runs once. Refused inside a phase's async spawn window, the player stayed in
      phase 2/3 out of combat and into the next fight, because auto-advance only raises the phase.
    - Fix: the patch marks `ActiveTransform.PhaseResetDue`, and any applied phase clears it. `AutoAdvancePhases`
      re-tries while `TransformGate.PhaseResetDue(due, inCombat, phase)` is true.
    - It is a flag rather than "out of combat ⇒ phase 1" because Auto mode still allows a manual `.beelz phase`.
    - Control: `PhaseResetDue_fails_when_a_refused_reset_is_dropped`, planted twice.
  - CR2 · ACCEPTED in part (A3, discovered 11.2).
    - Accepted: `.beelz refresh` during the spawn window replied "see server log". It now asks
      `PhaseGate(…, Reapply)` and replies `Still transforming — give it a moment, then try again.`. The `wiring`
      check covers Refresh (planted).
    - Not changed: a form that never spawns blocks re-apply and phase until `.beelz revert`. That is the plan's
      accepted case (Interfaces › Revert row). The misleading "next trigger retries" code comment is corrected.
- Codex verdict: APPROVED (round 3). Each round's findings and dispositions are listed below.
  - **Round 1** (a38c168..fb8638c, sources pasted in): REVISE with 4 findings, all REJECTED with reasons.
    - F1 · REJECTED · admin `force-transform` adds the unlock and clears cooldowns before `TryActivate` can refuse.
      That order dates from v0.100, and `TryActivate` has refused a pending form since v0.49. It's an admin override,
      not a refused player route, so the change does not alter it.
    - F2 · REJECTED · "timed expiry skips Revert, so the player is refused as Pending forever".
      - False: the timed path calls `TransformBuffService.Remove` → `RemoveInternal`, which drops the pending entry
        (`TransformBuffService.cs:906/987`).
      - The ledger only controls logging, and the next allowed route clears it.
      - The timed path not calling `MarkReverted` is real but pre-existing. Follow-up: a late async buff after a
        timed expiry is not destroyed by the revert-orphan guard.
    - F3 · REJECTED · "every allow re-arms logging". That is D20's specified episode.
    - F4 · REJECTED in round 1, then ACCEPTED as A3 once /code-review raised the same `refresh` reply with a
      stuck-form scenario.
  - **Round 2** (fix diff fb8638c..8fcfd8b): REVISE with 4 findings.
    - F1 · REJECTED · `ApplyPhase` records `CurrentPhase` even when the form apply itself fails. That is v0.27
      behaviour the reset already had. Retrying a genuinely failing apply every pass would loop.
    - F2 · ACCEPTED in part (A4, defect) · a manual `.beelz phase n` that returned early ("Already in phase n.")
      did not cancel an owed reset. The command now clears `PhaseResetDue` as soon as a phase is named.
      - Rejected part: a Manual → Auto mode switch reviving the reset. Phase 1 out of combat is what Auto mode
        means.
    - F3 · ACCEPTED (A4) · the verbose retry line printed every pass while a form stayed pending. It now logs only
      when the reset applies.
    - F4 · REJECTED · service-level tests need an IL2CPP harness that does not exist. Same answer as the plan
      reviews. The decision is in pure Logic and is tested.
  - **Round 3** (fix diff 8fcfd8b..05f217f): APPROVED, no findings.
- After the fixes (8fcfd8b, 05f217f): Release build 0 errors, `dotnet test` 244/244, `wiring`/`actors`/`selftest`/
  `tests` ok (14 named controls).
- dod status: 0/20 verified so far; evidence lines are added at `status` after the in-game step.

### Step 6 finding · 2026-10-04 · d2ec401, 3d9b435, 84eb186 (A5, A6)
- **First in-game run** (v0.137.5 build, dev server):
  - `cast_basic` 6/6.
  - `transform_chain_guard` 12/14. Every guard refusal and its TXGUARD line passed; both `.beelz phase 2` replies
    failed.
  - `clearbar_fullreset` 31/32 (test data — see below).
- **A5 (discovered 6.2, owner chose "fix in v0.137.5"):**
  - `.beelz phase` pre-checked a phase from the prefab's phase-tagged abilities plus the custom loadout, never the
    curated `BossForm.SetForPhase` set that `ApplyPhase` applies.
  - Since v0.100 (4ea3778), every Dracula/Morgana phase above 1 was refused with `Phase 2 has no eligible
    abilities … Aborting.`. Auto-HP was unaffected.
  - S-2 had been validated by reading code, not by running the command.
  - Fix: `TransformGate.PhaseHasAbilities(curated, natural, custom)`. Control
    `PhaseHasAbilities_fails_when_a_curated_phase_reads_empty`, planted once.
- **A6 (defect in A5's build):**
  - The reply count now comes from the same `EffectiveSet` ApplyPhase uses.
  - `BossForm.SetForPhase` never returns null.
- **Test data:**
  - The guard scenario's `force-transform … 591725925` gave Chaos a Morgana unlock, which shifted the unlock indexes
    (`transform 0` became Morgana).
  - Both scenarios now name units (`Dracula`, `Beatrice`).
- Build 0 errors; `dotnet test` 245/245; `wiring`/`actors`/`tests` ok (15 named controls).
- Codex on the A5/A6 fix diff:
  - Pass 1: REVISE. F1 (reply ignores the custom loadout) ACCEPTED → A6. F2 (a null curated entry throws)
    ACCEPTED → A6.
  - Pass 2: REVISE. F1 (`EffectiveSet` can still get null) ACCEPTED: hardened at `SetForPhase`.
  - Pass 3: APPROVED, no findings.
- Rollback range is extended to `84eb186` (it was `b6b9318..05f217f`): revert `84eb186`, `3d9b435`, `d2ec401` as well.

### Rollback
Rollback range: `b6b9318..84eb186`, plus the `chore(release): v0.137.5` commit that follows it (revert that first).
The range covers the wiring (b6b9318), the handoff note (fb8638c), the two audit fixes (8fcfd8b, 05f217f) and the phase-command fix (d2ec401, 3d9b435, 84eb186). The
pure Logic, tests and check tool from 8913810 can stay: nothing calls them once the wiring is reverted.

To roll back:
1. Stop the server: `vrclient.py close`, then `taskkill /PID <pid>` (no `/F`).
2. `git revert` the range, newest first.
3. `dotnet build Beelzebub/Beelzebub.sln -c Release` (redeploys the DLL).
4. Start the server.

There is no saved-data migration: `state.json` and `ability_rules.json` keep their shapes.
`ActiveTransform.PhaseResetDue` and the TXGUARD ledger are runtime-only. BCH needs nothing: no command shape or
`[BEELZ:*]` field changed.
