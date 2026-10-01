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

### Step 7 · 2026-09-30 · base 0ddf7a2
- git status: step-7 work in progress only (Logic/BarReset.cs, BarResetReply.cs, the Services readback fields), plus the
  owner's own `docs/V0136_ABILITY_TEST_PLAN.xlsx` and `_matrix_build.py` (never touched)
- compile: 0 errors (Release, no deploy); tests 110 passed
- checker before the rewire: commands FAIL (ClearAllLoadouts / ClearAllSlots / PurgeAbilitySlotModifications /
  ForceResetAbilitySlots in ResetBar, ResetLoadouts, Purge), text FAIL (ForceAbilityBarReinit call, `.beelz slot`
  reply, two LEGACY prefixes missing) — the step-7 targets, as planned

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

### Step 7 · 2026-09-30 · commands on the one reset path
- files: `Commands/BeelzCommands.cs` ResetBar → FullReset(PlayerReset) + BarResetReply (BCH slot-cleared events for the
  previously bound universal slots kept), help/commands lines point at `admin bar`; `Commands/AdminCommands.cs`
  ResetLoadouts / Purge → FullReset (Audit lines kept), new `admin bar` (ReadBar + ForBar, `[Beelz BAR]` logged when
  online), `rebuildbar` = alias of `bar`, `clearslotmods` / `rebuildslots` descriptions start `LEGACY:`, the
  `.beelz slot` reply gone, admin help recovery block rewritten; `Services/BarResetService.ReadBar` fills Ability,
  OverrideBuffs (new read-only `TransformBuffService.ListOverrideBuffs`, same predicate as the destroy sweep), Offline /
  SavedSets / Transform / Hotkeys and always the 9 bar slots; `Logic/BarResetReply.cs` (pure) +
  `BarResetReplyTests` (11); `docs/COMMANDS.md`
- compile / tests: Release build 0 errors (no deploy); `dotnet test` 121 passed
- planted faults (BarResetReply): angle brackets kept, surrogate pair split, problems as separate lines, slot 0 not
  "primary", not-reachable headline dropped — each failed its test, passed restored (two first attempts did not
  exercise their test and were fixed — see step 6 round 3)
- checker: commands ok (20 symbols, 3 FullReset call sites), text ok, auth ok (69 admin commands, 4 reset callers),
  config ok, paths ok (44), data ok (17), selftest ok (41); docs FAIL until step 8 (BACKLOG.md, recovery guide)
- left in place on purpose: `TransformBuffService.PurgeAbilitySlotModifications` and `ForceAbilityBarReinit` have no
  callers now; removed after the release-candidate session so a rollback stays a plain revert
- not unit-testable here (IL2CPP / VCF): the handlers themselves — verified in the release-candidate session (D6-D8)

### Step 8 · 2026-09-30 · recovery guide, backlog
- files: `docs/RECOVERY_GUIDE.md` rewritten to the one flow (admin bar → resetbar / purge → reset-character only after a
  purge and a relog; respawn and reset-character keep the Steam-keyed binds; known limits: mounted rows, unreadable
  slots, next-tick patch); `docs/BACKLOG.md` new (clearbar-fullreset, transform-chain-guard, docs-consolidation,
  dev-snapshot, mounted-bar-reset). `ApiVersion = 33` and the handoff v0.137 entry landed with steps 1-4 (f0f346c).
- checker: docs ok, paths ok (44)

### Step 7 · review round 1 · 2026-09-30 · reviewed 44498e2
- Codex verdict: REVISE — 1 blocking:
  - F1 ACCEPTED — the confirm prompts and the no-match reply interpolated the typed `player` / looked-up name raw (a
    long or `<`-bearing token could exceed VCF's 512 bytes and throw, or inject markup). They now go through
    `BarResetReply.Name` (made public) + `Cap`. Test `Name_fails_when_markup_control_characters_or_long_names_pass_through`
    (planted: angle stripping removed → failed; restored → passed).
- Fresh-context subagent review — nothing blocking, 7 advisory:
  - A1 ACCEPTED — an offline purge cannot pop the leaked slot mods (live-only), yet the reply promised the bar resets;
    it now adds "if it is still stuck then, run this again while they are online".
  - A2 ACCEPTED — an online target with `liveReady=false` got no RevertTransform but lost its transform record, stranding
    the form buff/summons; ClearSavedBindings now keeps the record in that case (the reset is Unreadable, not clean, and
    the reply sends the admin to relog/respawn and re-run).
  - A3 ACCEPTED — `.beelz admin purge CONFIRM` bound CONFIRM as a name fragment; that shape is now refused with the
    correct usage.
  - A4 REJECTED — `you` meaning the sender is the deliberate convention of purge / bar / rebuildbar (and the command
    descriptions say "default: you"); a player whose name starts with "you" is reached with a longer fragment.
  - A5 ACCEPTED — with no equip buff, `admin bar` printed `row=no`; every slot's rows are now reported unreadable then.
  - A6 ACCEPTED — `admin bar` writes an `Audit` line (offline, binds, rows, other).
  - A7 ACCEPTED — the RemoveAllFormsAndShapeshifts doc comment is back on its method.
- compile / tests: Release build 0 errors (no deploy); `dotnet test` 122 passed; checker commands / text / auth ok
- not unit-testable (VCF handlers, IL2CPP): A2 A3 A5 A6 — verified by build and read-through; D6-D8 in the session

### Step 7 · review round 2 · 2026-09-30 · reviewed c4e3664
- Codex verdict: READY (no findings) — the round-1 fixes F1 A1 A2 A3 A5 A6 checked against the pasted handlers.
- /code-review: waived — round 1's fresh-context subagent covered this surface; the round-2 diff is its own fixes.
- Step 7 post-audit CLOSED after 2 rounds.

### Step 5 · 2026-09-30 · fault harness (commits 7baa616, cc8497e, 49fe6ba)
- planted faults: `python Beelzebub/tools/fault_harness.py bar-reset --skip D19` on 49fe6ba, patch tree a0fa64654f214a39138d148671a4e9ed706353a8 (`git rev-parse HEAD:Beelzebub/tools/faults/bar-reset`):
  `harness: ok, 131 faults, 113 clauses, 1 silent, 5 deferred (D22: gate not met) (not run: D19)`
  - every one of the 131 patches: planted → its command failed for the planted reason (a failing test, a checker FAIL line,
    PREFLIGHT FAILED) → `git apply -R` → the command passed again. D2 "stays silent on the documented order" is proven by
    the clean baseline; D22's five clauses wait for the rollback line (build step 10); D19's four patches run once this
    record is committed (its preflight baseline needs `audit: ok`) and their result is added below.
- what the harness found (each fixed in its own commit before the recorded run):
  - run 1 (54 of 131 exercised): under `core.autocrlf` `git apply -R` rewrote line endings, so reverted files stayed
    "modified" and later patches were skipped as a dirty tree — the harness now restores each file's exact bytes.
  - `check_bar_reset.py auth` read `string player = null` as the parameter `null`: a defaulted player/target parameter
    never counted as targeting (D15 hole). Fixed; `auth: ok, 69 admin commands` unchanged on the repo.
  - `check_bar_reset.py data` never flagged a script that only `open(...,'w')`s and did not require a `dist/` row (D28).
  - `check_bar_reset.py docs` accepted a backlog slug mentioned in another row's prose (D26); it now needs the slug's row.
  - selftest crashed instead of FAILing when a fixture could not be built.
  - D2 "a plan missing any of …": no test removed ONE required layer; new Theory `Runner_fails_when_a_plan_missing_one_required_layer_is_clean`.
  - `Beelzebub/.gitignore` ignores `manifest.json` and `logs/`: the manifest and fixture logs were never committed. A
    faults-dir `.gitignore` re-includes them, and the harness FAILs on any untracked file in its dir.
  - four plants did not reach their check (D13 empty set, D28 script writers, D29 empty/good tree) and were re-aimed.
- D19 on 91b1bd5 (`fault_harness.py bar-reset --only D19`; the preflight baseline printed PREFLIGHT OK on the v0.136.0 tree):
  `harness: ok, 4 faults, 4 clauses` — toml version, CHANGELOG entry, README status line and a `.beelz slot ` reply each
  made preflight print PREFLIGHT FAILED, and it passed again after each revert. Together with the run above: 135 faults,
  117 clauses, every test/cmd item except D22 (deferred to build step 10). D19's patches name v0.136.0 and are re-made
  with `--make` against the release commit in step 10.

### Step 5 · review round 1 · 2026-09-30 · reviewed e28c66f
- Codex verdict: REVISE — 6 blocking, 1 advisory:
  - F1 ACCEPTED — D2's stays-silent clause had no patch; it now plants a changed expected order into the order test.
  - F2 REJECTED — D22 is deferred by the plan itself (build step 5: faults are planted once their targets exist); the
    summary line names it, and the harness FAILs once the rollback line exists and a D22 entry still has no patch.
  - F3 ACCEPTED (partly) — the hash check is `check_bar_reset.py audit`'s job (D21 splits it); the harness now also
    refuses uncommitted changes in its faults dir and prints the patch tree it ran. `audit` now reads the LAST recorded
    tree (the record is append-only; it read the first).
  - F4 ACCEPTED (partly) — one patch per entry is enforced; D20/D30 patches may only touch the tracked fixture logs. Whether a
    patch plants the clause it claims stays a review item (this record lists every patch by name in the manifest).
  - F5 ACCEPTED — the snapshot bytes are restored even when the apply, the command or the revert fails; a non-line-ending
    difference is reported as REVERT FAILED.
  - F6 ACCEPTED — "caught" needs the command's own verdict: its last line (`<sub>: FAIL …`, `PREFLIGHT FAILED`) or dotnet's
    `Failed!` summary; MSB/NU/NETSDK errors count as a broken build.
  - F7 REJECTED — D23 needs a plant that creates an untracked file; the harness already requires that file to be absent.
- planted faults: `python Beelzebub/tools/fault_harness.py bar-reset --skip D19` on c5e4dc7:
  `harness: ok, 132 faults, 113 clauses, 5 deferred (D22: gate not met) (not run: D19), patch tree f4a3529b2df93662f06e7d39ebd1f6186b350d81`

- D19 on 4bffc98 (preflight baseline PREFLIGHT OK): `harness: ok, 4 faults, 4 clauses (not run: all but D19), patch tree f4a3529b2df93662f06e7d39ebd1f6186b350d81`

### Step 5 · review round 2 · 2026-09-30 · reviewed 4bffc98
- Codex verdict: REVISE — 2 blocking, both ACCEPTED:
  - F1 — the harness appended stderr after stdout, so "the last line" was not the command's real last line; stderr is now
    merged into stdout as written (`stderr=STDOUT`).
  - F2 — `audit` bound a `harness: ok` line and a `patch tree` hash separately; it now reads the hash from the last
    `harness: ok, <n> faults …, patch tree <sha>` summary line (the harness prints both on one line since round 1; the
    selftest fixture follows).
- re-check with the merged output (all three command kinds: checker, pwsh -LogCheck, dotnet test):
  `harness: ok, 32 faults, 26 clauses (not run: D1 D2 D3 D4 D11 D12 D13 D14 D15 D16 D17 D18 D19 D22 D23 D24 D26 D27 D28 D29 D31), patch tree f4a3529b2df93662f06e7d39ebd1f6186b350d81`
  — the full run repeats in build step 9. `selftest: ok, 41 cases`, `audit: ok`.

### Step 5 · review round 3 (final) · 2026-09-30 · reviewed 60199ce
- Codex verdict: CLEAN — F1 and F2 confirmed fixed; no blocking findings.
  - A1 (advisory) REJECTED — anchor the summary regex with `^…$`: the record quotes each harness summary inside a
    bullet and backticks, so an anchored match would reject the real records; the status and the tree are already
    read from one line, and the last such line wins.
- Step 5 post-audit closed.

### Step 9 · post-audit of steps 1-8 · review round 1 · 2026-09-30 · reviewed 5401c27 (`git diff 3fbc2d4..5401c27`)
- preflight on 5401c27: PREFLIGHT OK (10 checks; build 0 errors; tests 130 passed; bar-reset config/commands/text/auth/docs/audit/paths/data ok)
- harness on 5401c27: `harness: ok, 132 faults, 113 clauses, 5 deferred (D22: gate not met) (not run: D19), patch tree f4a3529b2df93662f06e7d39ebd1f6186b350d81`
- Codex verdict: REVISE — 2 blocking, both ACCEPTED:
  - C1 — `Clean` ignored `Readback.OverrideBuffs`: a carrier/form/shapeshift buff that survives DestroyOverrideSources
    re-patches the bar through its own ReplaceAbilityOnSlotBuff, which no slot reading shows. Clean now also requires
    no override buff left; `ListOverrideBuffs` skips buffs already queued with DestroyTag (SafeDestroyBuff only queues,
    so the same-frame readback would otherwise list every buff it just destroyed); the reply names the buff (D24).
  - C2 — a RevertTransform that throws was followed by ClearSavedBindings dropping the transform record, so a retry
    no longer planned the revert. `IBarResetOps.ClearSavedBindings(keepTransformRecord)`; the runner passes true
    when this run's RevertTransform failed (D12).
- fresh-context subagent (/code-review role): no blocking; 8 advisory:
  - S1 ACCEPTED — same as C1.
  - S2 ACCEPTED — EmptyPush swallowed per-slot errors and returned a short count; it is now an ERR below 9 pushed slots.
  - S3 REJECTED as a change, ACCEPTED as a documented limitation — ClearEquipEntries removes every slot 0-7 row the
    equip-buff prefab does not carry, including another mod's; the code cannot tell whose row it is. Business rules 4.
  - S4 REJECTED — "gear mods popped and not restored": the weapon's own mods come back through Reapply (D7's PASS is the
    visible weapon skills, before and after a swap and a relog), and a pre-reset gear count cannot be compared because
    Beelzebub's injected rows are themselves equip-buff (gear) sourced. `admin bar` prints every gear source per slot
    (D6), so a non-weapon `Item_*` source lost in the in-game session would show there.
  - S5 ACCEPTED — a failed TrySaveSync now also calls RequestSave, so the heartbeat retries the write.
  - S6 REJECTED — the transform cooldown started by RevertTransform is Business rules 6 ("cooldowns are left alone");
    the release notes say so.
  - S7 ACCEPTED — RevertTransform passed "bar reset (<scope>)", a new transform-ended reason with parentheses; it now
    passes the pre-0.137 reasons (resetbar / admin reset-loadouts / admin purge), so the BCH wire is unchanged.
  - S8 ACCEPTED — assumption S-2's per-slot reapply diagnostic is logged as `[Beelz REAPPLY] slot= before= after=`
    (own tag: `[Beelz RESET]` lines are schema-checked by D30; Data row added); Interfaces drops the stale
    RestoreResolvedGrants.
- planted once by hand before trusting them: `Runner_fails_when_a_surviving_override_buff_is_clean`,
  `Runner_fails_when_a_failed_revert_drops_the_transform_record`, `ForReset_fails_when_a_surviving_override_buff_is_not_named`
  — each failed with its fault, passed restored; tests 133 passed. Harness entries D12-failed-revert-drops-record,
  D24-override-buff-clean, D24-override-buff-unnamed added; patches re-made (144 entries).
- harness on 41139ef (`--skip D19`): 134 of 135 caught; `D28-row-lacks-field: patch does not apply` (its patch predated the
  REAPPLY Data row beside its context) — patches re-made in 3e333de, then `--only D23,D28` (the two items whose patches changed):
  `harness: ok, 12 faults, 12 clauses (not run: D1 D2 D3 D4 D5 D11 D12 D13 D14 D15 D16 D17 D18 D19 D20 D22 D24 D25 D26 D27 D29 D30 D31), patch tree 0bd1cf8e55abed87ad2bcffbe29d1e081e1add30`
- fixes: 5eae953, 41139ef, 3e333de

### Step 9 · review round 2 · 2026-09-30 · reviewed 6516eab (`git diff 5401c27..6516eab`, code + tests)
- Codex verdict: CLEAN — C1, C2, S2, S5, S7, S8 confirmed fixed, no new finding (no false-unclean on a normal reset:
  buffs queued with DestroyTag are not listed).
- Step 9 code review closed after round 2.
- D19 on a9eb3a1 (preflight baseline: `audit: ok`):
  `harness: ok, 4 faults, 4 clauses (not run: D1 D2 D3 D4 D5 D11 D12 D13 D14 D15 D16 D17 D18 D20 D22 D23 D24 D25 D26 D27 D28 D29 D30 D31), patch tree 0bd1cf8e55abed87ad2bcffbe29d1e081e1add30`

### Step 10 · D32 empty-push leak · review round 1 · 2026-09-30 · reviewed d4f0cfe (`git diff dafc7de..d4f0cfe`, code + tests)
- Evidence for the fix (Development procedure 4, diagnostic first): `.beelz admin bar-raw Chaos` at 19:14 and 19:15 on the
  release candidate showed an Empty GroupGuid mod sourced by `EquipBuff_Weapon_Unarmed_Start01` on every bar slot 0-8,
  slot 2 `PrefabGuid(0) (Base: PrefabGuid(-433204738))`, stacks growing one per reset and surviving a restart; logs kept
  in `%TEMP%/beelz-logs-2026-09-30-rc1-tstuck/`. Owner Decision F1 option A, F2 option A (discovered, plan note + D32).
- Release build ok; tests 143 passed; every D32 control planted by the harness (`--only D32`: 12 of 12 caught).
- Codex verdict: REVISE —
  - C1 REJECTED — "the re-pop loop pops every entry in `after.Entries`, not only `d.ModIdsToPop`": `SlotPurgeDecision.Decide`
    already selects every GroupGuid entry of the slot (gear ones too; Reapply re-adds the weapon's), so re-popping the
    fresh snapshot's entries keeps the same set; the loop only repeats while the count keeps falling, capped at 16.
  - C2 ACCEPTED — the leak rule used `GearRule` (any `EquipBuff*` / `Item_*` source), so a vanilla armour or item Empty on
    a non-weapon slot would have read as a leak (false-unclean). It now needs a WEAPON equip-buff source
    (`SlotOwnership.IsWeaponBuff`, the only source ForceResetAbilitySlots ever used).
  - C3 ACCEPTED — an unknown owned set classified a weapon-buff Empty as gear (a possible false-clean). `ClassifyEmpty`
    returns Unknown and the slot reads unreadable (never clean).
- planted: `ClassifyEmpty_fails_when_an_unknown_owned_set_reads_as_known`, `IsWeaponBuff_fails_when_armour_counts_or_a_weapon_buff_does_not`
  and the reworked leak controls — harness entries D32-unknown-not-unknown, D32-armour-is-weapon, D32-weapon-not-weapon,
  D32-non-weapon-leaked (158 entries); tests 145 passed.

### Step 10 · D32 empty-push leak · review round 2 · 2026-09-30 · reviewed bed6897 (`git diff d4f0cfe..bed6897`, code + tests)
- Codex verdict: CLEAN — C2 (weapon equip-buff source only) and C3 (unknown owned set → unreadable) confirmed fixed, no new
  finding. D32 code review closed after round 2.
- the D3/D18/D23/D28 patches no longer applied after the plan and code edits — re-made in ba61a9a and a02c0d3, then the full run:
  `harness: ok, 149 faults, 132 clauses, 5 deferred (D22: gate not met) (not run: D19), patch tree 2ca1c81b7549abecb10d036695fcc59bfa573555`

### Step 10 · D33 spellbook spell buffs · review round 1 · 2026-09-30 · reviewed 35c9ac7 (`git diff 90d7956^..35c9ac7`, code + tests)
- Evidence for the fix (Development procedure 4, diagnostic first, 90d7956): at login `[Beelz SPELLBUF] destroying spellbook
  buff Entity(345239:1) … slot=5 ability=AB_Blood_Shadowbolt_AbilityGroup` from the reconnect reconcile with no transform
  active; `bar-raw` then listed spellbook entries T CrimsonBeam, Space VeilOfShadow and R Shadowbolt `state=MISSING`.
  Logs in `%TEMP%/beelz-logs-2026-09-30-rc3-spellbuf/`. Owner decisions G1-G4 option A.
- Release build ok; tests 150 passed; `--only D33`: 8 of 8 caught. In game on 35c9ac7: login logged `repaired` for slots
  5, 2, 7, bar-raw showed all four entries `state=ok`, the owner equipped and unequipped spells freely — but a resetbar
  then emptied R, C and T (defect, plan Log): PopSlotMods popped the GroupGuid mods that place the spellbook spells.
- Codex verdict: FINDINGS —
  - K1 ACCEPTED (high) — Apply read the spellbook only after instantiating the carrier; an unreadable spellbook (null) made
    the fresh carrier "not ours", Apply returned without removing it, and every teardown would then skip it. The spellbook
    is now read first and an unreadable one aborts before anything is created; the post-instantiate check uses that set.
  - K2 ACCEPTED (medium) — a failed re-create could leave a partial entry while logging "removed". `FinishRepair` now
    re-scans the slot after every attempt: a live entry is configured (repaired), an entry without a live buff is removed.
  - K3 REJECTED (medium) — "identify our carrier positively": carriers saved by earlier versions carry no unique mark (the
    only flag set, RemoveOnDisconnect, is not ours alone), so positive identity cannot cover old saves; the spellbook read
    only fails on an exception, which is logged, and the next login or reset retries the removal.
  - K4 ACCEPTED (low) — the repair snapshot and each RemoveAt are guarded, and the entry at the captured index is
    re-validated (slot, ability, buff still dead) before it is removed.
- Same round (defect above): `SpellbookBuffs.IsSpellbookMod` — Decide and the re-pop rounds keep the mod that sets a slot to
  its spellbook spell (and never destroy its source); the readback counts it as legitimate (`spellbook`); `[Beelz LEAK]`
  gains `kept=`. Tests 152 passed; new faults D33-spell-mod-missed, D33-other-slot-kept, D33-unreadable-kept,
  D33-kept-popped (170 entries); all non-D19 patches re-made against the new tree.
