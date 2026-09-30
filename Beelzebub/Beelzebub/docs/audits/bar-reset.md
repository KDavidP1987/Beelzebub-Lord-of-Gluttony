# Audit — bar-reset

Plan: `docs/dod/bar-reset.md` (draft, built under the owner's waiver after Codex review 5 — see its Log).

## Pre-audit
### Steps 1-4 · 2026-09-30 · base e949878
- git status: clean except the owner's own `docs/V0136_ABILITY_TEST_PLAN.xlsx` and `_matrix_build.py` (never touched)
- compile: 0 errors (Release, `-p:VRisingServerPath=Z:\no-deploy` — no deploy)
- preflight: PREFLIGHT OK (7 checks, -SkipBuild) before any change
- dod status: 0/30 verified

### Step 6 · 2026-09-30 · base 5109c36
- git status: clean except the owner's own `docs/V0136_ABILITY_TEST_PLAN.xlsx` and `_matrix_build.py` (never touched)
- compile: 0 errors (Release, no deploy)
- dod status: 0/30 verified (build under the round-5 waiver; items are verified at step 9/10)
- read before building: `Reference Data/Prefabs/EquipBuff_Weapon_Sword_Base` — the prefab carries the weapon's own
  ReplaceAbilityOnSlotBuff rows (0 primary, 1 Whirlwind, 4 Shockwave); the old resetbar's ClearGrant 0-7 loop stripped
  them → recorded in the plan Log, +D31

## Post-audit
### Steps 1-4 · 2026-09-30 · pure logic, form-bar fill, checker
- compile / tests: Release build 0 errors; `dotnet test` 97 passed (was 43 before the plan)
- planted faults (by hand, before the harness exists — step 5 replaces these with tracked patches):
  - D1 SlotModDump: read-every-section, unparseable-line-ignored, ModId-0-accepted, empty-dump-entry — each failed its test, passed restored
  - D2 D3 D11 D12 D24 D27 BarReset: equip-after-pop, two EmptyPush, unknown-scope-throws, PlayerReset-clears-hotkeys,
    offline-live-step, runner-stops-on-throw, unreadable-clean, decide-not-skipping, save-ignored — each failed, passed restored.
    `empty plan clean` (drop `steps.Count > 0`) did NOT fail: the guard was redundant (an empty plan never saves, so
    `Saved` already makes it unclean) — the guard was removed and the comment says why.
  - D16 BarResetLog: newline-kept, no-slow-flag, unreadable-other-0, no-chunking — each failed, passed restored
  - D25 BarResetInput: CONFIRMED-accepted, case-sensitive — each failed, passed restored
  - D4 FormBarFill: captures-without-autoFill, universal-beats-form, empty-not-native — each failed, passed restored
- checker: `check_bar_reset.py selftest` → `selftest: ok, 30 cases`; on the repo config/auth/paths/data ok, commands/text/docs/audit
  FAIL as expected until steps 7-9
- preflight: gains check `bar-reset` (`check_bar_reset.py all`) and `-LogCheck -LogDir <dir>`; a fixture log with an
  `   at Beelzebub.` line → `FAIL log LogOutput.log 1 Beelzebub stack frame(s)`

### Steps 1-4 · review round 1 · 2026-09-30 · reviewed f0f346c
- /code-review (medium, f0f346c~1..f0f346c), 4 findings:
  - CR1 ACCEPTED — `GroupGuidStillModified` returned true for an unreadable dump, so the legacy purge's PASS 2 destroy
    backstop fired (the opposite of D24). Now: unreadable → the slot is logged `[Beelz PURGE] … dump unreadable — slot
    skipped` and neither popped nor harvested; `GroupGuidStillModified` = Readable && HasMods.
  - CR2 PARTLY ACCEPTED — the GroupGuid section now ends at ANY `- ` field header (another component's `[ModId` lines no
    longer make the slot unreadable). REJECTED the rest (loosen the entry regex so odd-shaped lines are still popped by
    id): D24 is the owner-approved rule that an unknown shape is never acted on blind; the legacy commands that relied on
    the loose parse are replaced by FullReset in step 7 of this same release, and an unreadable slot is reported, never clean.
  - CR3 ACCEPTED — the RESET line said `survivors=none` for a failed save / thrown step / offline run. It now ends with
    `clean=<0|1>` from `BarResetResult.Clean`; the session check requires clean=1.
  - CR4 ACCEPTED — `[Beelz FORM]` had no target and was logged before the lock filter. Now
    `[Beelz FORM] target=<steamId> form=<name> source=<…>` via `BarResetLog.FormatForm`, after `FilterBar`, `native` when
    the filtered bar is empty; the session check keeps only FORM lines whose Steam ID the target's own lines printed.
- Codex verdict: REVISE — 6 blocking, 1 advisory on f0f346c (`codex exec -s read-only`, CLI 0.151.0); all ACCEPTED:
  - F1 ACCEPTED — a null `Readback()` became an empty readable bar → could be Clean. Now a failed step `no readback`.
  - F2 ACCEPTED — the runner trusted any plan; `{SaveBindings, Readback}` could be clean. `Clean` now also requires every
    `BarResetRunner.RequiredForClean` step (ClearSavedBindings … Readback) to have run.
  - F3 ACCEPTED — the id/source adapters ignored `Readable`; they now return nothing for an unreadable dump (same fix as CR1).
  - F4 ACCEPTED — D7/D8 accepted resets with `:ERR` or missing layers; `_reset_ok` requires clean=1, survivors=none,
    every required step, SaveBindings:1, no ERR (D10 too).
  - F5 ACCEPTED — same as CR4.
  - F6 ACCEPTED — BAR part sets were concatenated unchecked; `_join_bar_parts` fails a missing, duplicate, out-of-order,
    mixed-header or unterminated set.
  - F7 ACCEPTED (advisory) — `GroupGuidBackup` matched the GroupGuid prefix; the field token is now exactly
    `- AbilityGroupSlot.GroupGuid:`.
- compile / tests: Release build 0 errors 0 warnings (no deploy); `dotnet test` 103 passed (+6)
- planted faults: null-readback-becomes-empty, clean-without-complete-plan, clean=1-always, FormatForm-ignores-empty-bar,
  GroupGuid-token-without-colon, section-ends-only-at-AbilityGroupSlot — each failed its new test, passed restored
- checker: `selftest: ok, 35 cases` (session gains 5 defect trees: not clean, another player's FORM, a step ERR, a
  skipped layer, a truncated part set); `data: ok, 17 artifacts` (new `[Beelz PURGE]` warning row)
- plan: D1 D2 D4 D16 D24 D29 D30 text tightened to match (Log note, same date)

### Steps 1-4 · review round 2 · 2026-09-30 · reviewed d43cb9e
- /code-review (medium, d43cb9e), 3 findings, all ACCEPTED:
  - CR1 ACCEPTED — no selftest defect touched the Purge line, so dropping `_reset_ok` from D8 went unnoticed. New
    anchored fixtures edit the Purge line only and run 3 only (the driver takes an optional anchor).
  - CR2 ACCEPTED — D7 took any clean PlayerReset, so a failed run rescued by a retry passed. D7 is now the FIRST
    PlayerReset (Business rules 7: a failure blocks the release, it is not retried away). Its isolated fixture is
    covered jointly with D9 (the Wolf line sits between runs 1 and 2) — recorded, not separately planted.
  - CR3 ACCEPTED — `FieldPrefix = "- "` let an indented `- ` sub-line end the GroupGuid section and hide later
    `[ModId` lines as Readable. A header is now `^- <Word>.<Word>:`.
- Codex verdict: REVISE — 2 blocking on d43cb9e, both ACCEPTED:
  - F1 ACCEPTED — `_reset_ok` built a dict, so `SaveBindings:ERR,SaveBindings:1` overwrote the ERR. The raw tokens are
    now checked for `:ERR` before any dict. (A separate duplicate-name guard was added, then removed: a planted fault
    showed it redundant with the raw check — no fixture could fail it alone.)
  - F2 ACCEPTED — `_RESET` was unanchored, so `clean=10` read as `clean=1`. It now ends `clean=[01](?: slow=1)?\s*$`.
- compile / tests: `dotnet test` 104 passed (+1)
- planted faults: D8-without-_reset_ok, D10-without-_reset_ok, unanchored-clean, raw-ERR-allowed — each made selftest
  FAIL on its defect tree, ok restored; header-is-any-dash-line — failed its new test, passed restored
- checker: `selftest: ok, 40 cases`

### Steps 1-4 · review round 3 (final) · 2026-09-30 · reviewed ab61c77
- Codex verdict: REVISE — 1 blocking on ab61c77, ACCEPTED:
  - F1 ACCEPTED — a malformed first PlayerReset line (e.g. `clean=10`) failed `_RESET`, was silently dropped, and a
    later clean retry became "the first" PlayerReset for D7. Any `[Beelz RESET]` / `[Beelz BAR]` line that does not parse
    now fails the session (the mod writes those tags only through BarResetLog). New fixture: a malformed run 0 before run 1.
- /code-review: not rerun for round 3 — the round-2 /code-review findings were all fixed in ab61c77 and this round's
  single change is the checker line above.
- planted faults: malformed-line-check-removed → `selftest: FAIL session ok on its defect tree 11`, restored ok
- checker: `selftest: ok, 41 cases`
- Round cap reached (3 of 3, CLAUDE.md › Development procedure step 3). The round-3 fix is verified by its planted fault,
  not by a fourth review; the whole steps 1-9 diff gets a fresh Codex pass in build step 9.

### Step 6 · 2026-09-30 · game-side reset service
- files: `Services/BarResetService.cs` (IBarResetOps + FullReset / ReadBar / TickLate), `SlotApply` RemoveInjectedRows /
  ReapplyEquipRows / InjectedRowSlots / HasEquipBuff, `TransformBuffService` PopSlotModifications / ReadSlotMods,
  `PersistenceService.TrySaveSync` (SaveSync calls it; the catch deletes `state.json.tmp`), `Heartbeat` calls TickLate,
  `Logic/EquipRows.cs` (+D31)
- compile / tests: Release build 0 errors (no deploy); `dotnet test` 109 passed (+5 EquipRowsTests)
- planted faults: D31 vanilla-row-injected, copies-not-counted, slot-range-ignored, null-prefab-acts,
  missing-not-reported — each failed its test, passed restored
- checker: config ok, paths ok (41), data ok (17), auth ok; commands/text/docs/audit FAIL as expected until steps 7-9
- not unit-testable here (IL2CPP): the game calls behind IBarResetOps — verified in the release-candidate session (D6-D10, D30)


### Step 6 · review round 1 · 2026-09-30 · reviewed e3ee3e8
- /code-review (medium, e3ee3e8), 6 findings:
  - CR1 ACCEPTED as a documented limitation — a destroyed stuck source is patched by the engine one tick later, so
    that run reads the slot as a survivor (not clean). Safe direction (never falsely clean); the reply points to
    `.beelz admin bar`. Business rules 4.
  - CR2 ACCEPTED — ReapplyEquipRows pushed each row-less slot's stored base with the equip buff as source, pinning the
    spell until a weapon swap (the D7 symptom, moved to spell slots). Reapply is rows-only again, as the plan said.
  - CR3 ACCEPTED — the readback walked all ~296 slot entities (any non-bar mod = never clean; 2-3 registry dumps each).
    It now reads bar slots 0-8 only; a mod sourced by the character or a slot entity counts as vanilla. Business
    rules 5 and D6 updated. PopSlotMods still covers every slot.
  - CR4 ACCEPTED as a documented limitation — an offline reset drops a parked transform record (the plan's D11 rule),
    so the reconnect path no longer despawns its carrier; the next reset's DestroyOverrideSources removes it.
    Business rules 6.
  - CR5 REJECTED — purge no longer clears transform cooldowns: Business rules 6 (owner-approved plan) decides
    "cooldowns are not a reset concern and are left alone". A change made in the fix pass was reverted.
  - CR6 ACCEPTED as a documented limitation — mounted saddle rows are not read or cleared; dismount first. Business
    rules 4, Out of scope, backlog slug `mounted-bar-reset`.
- Codex verdict: REVISE — 4 blocking, 1 advisory on e3ee3e8:
  - F1 ACCEPTED — a failed `RemoveAbilityGroupModificationOnSlot` was logged and swallowed; PopSlotModifications now
    finishes every slot and then throws, so the step is an ERR.
  - F2 REJECTED — "re-read each slot after the setter": the engine resolves the slot in its own system job later, so a
    same-call re-read is not a reliable signal; the readback and the D7/D30 session checks are the verification.
  - F3 ACCEPTED — a held equip buff that vanished after planning made ClearEquipEntries / EmptyPush / Reapply return 0
    as success; they now throw "no held equip buff" (step ERR).
  - F4 ACCEPTED — same as CR2.
  - F5 ACCEPTED (advisory) — the post-pop dump now checks the slot entity still exists; a vanished one is a failure.
- compile / tests: Release build 0 errors (no deploy); `dotnet test` 109 passed
- not unit-testable (IL2CPP game calls): verified in the release-candidate session (D6-D10, D30)

### Step 6 · review round 2 · 2026-09-30 · reviewed 8fb6f3b
- Codex verdict: REVISE — 3 blocking (the first run could not read the repo from its sandbox and returned no verdict;
  it was rerun with the diff and the files pasted into the prompt, from the repo root):
  - F1 ACCEPTED — a `DestroyUtility.Destroy` exception in the backstop was logged and dropped; it is now a pop failure
    (step ERR).
  - F2 ACCEPTED — `FormatEntityModifications` turned an engine exception into "", which parses as a readable dump with
    no mods (falsely clean). New `TryFormatEntityModifications` + `SlotModParse.Failed()`: PopSlotMods and the readback
    treat a formatter failure as Unreadable. Test `Failed_read_fails_when_it_is_readable_or_lets_the_slot_be_purged`
    (planted: `Failed()` returning a readable parse → the test failed; restored → passed).
  - F3 ACCEPTED — an unreadable slot above 8 was skipped by PopSlotMods and never reached the 0-8 readback; any skipped
    (or unreadable-after-pop) slot is now a pop failure, so the reset is not clean. D24's "neither popped nor destroyed"
    still holds for that slot.
- /code-review: waived for this round — the reviewed diff is the ~50-line round-1 fix already covered line by line by
  the Codex pass; round 3 runs both reviewers on the round-2 fix.
- compile / tests: Release build 0 errors (no deploy); `dotnet test` 110 passed

### Step 6 · review round 3 (final) · 2026-09-30 · reviewed 08a2b2f
- Codex verdict: READY (no findings).
- Fresh-context subagent review (in place of /code-review: the working tree already holds step-7 changes, so the
  reviewer read the commits only) — nothing blocking, 3 advisory:
  - A1 ACCEPTED as a watched risk, no code change — one routinely unreadable dump on a non-bar slot would make
    PopSlotMods ERR on every reset. D24 says any unreadable slot is never clean, so the ERR stays; the per-slot
    `[Beelz PURGE] slot[n] … dump unreadable` warning is the diagnostic (CLAUDE.md "diagnostic before fix"), and D30
    fails on a PopSlotMods ERR in the release-candidate session, which then becomes a `defect` fix.
  - A2 ACCEPTED, fixed — the destroy backstop could destroy a source that also drives a skipped (unreadable) slot;
    with any skipped slot the backstop is now not run (logged). Not unit-testable (IL2CPP entity calls); verified by
    the Release build and read-through.
  - A3 REJECTED for this diff — `SlotPurgeDecision.Decide` pops character/slot-sourced mods too; this predates the
    reviewed commit and follows the plan ("pop EVERY GroupGuid mod … gear too"); the D7/D10 session checks
    (`gear=` stable, weapon skills present) are its evidence.
  - cosmetic (`"no prefab"` label never prints): no behavioural effect, left.
- Step-7 test hygiene found in the same pass: two planted faults on BarResetReply did not exercise their tests
  (surrogate input too short; a plant that broke compilation) — input fixed, both replanted and caught.
- compile / tests: Release build 0 errors (no deploy); `dotnet test` 110 passed on the committed tree
- Step 6 post-audit CLOSED after 3 rounds.
