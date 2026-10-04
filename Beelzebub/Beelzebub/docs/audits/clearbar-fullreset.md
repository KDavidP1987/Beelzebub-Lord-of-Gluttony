# Audit — clearbar-fullreset (v0.137.4)

Plan: `docs/dod/clearbar-fullreset.md` (24 items). The plan went through three Codex rounds and was approved by a
human review (Review 4) on 2026-10-04.

The bug: `.beelz clearbar` cleared only the saved Steam-keyed binds (layer 1). It skipped the other layers, so:
- a rider kept their saddle rows (`other>0` after `clearbar all`);
- a transform outlived `clearbar universal`;
- no `[Beelz BARRESET]` line was written, so a clear left no evidence.

It failed 19/30 in game on v0.137.3 (plan Log, D12 dry-run).

## Pre-audit
### Steps 1-3 · 2026-10-04 · 82fd362 (+ plan draft)
- git status: only the owner's `docs/V0136_ABILITY_TEST_PLAN.xlsx` / `_matrix_build.py` (never staged) and the plan
  files.
- compile: 0 errors. tests: 185 passed before the new Logic was added.
- preflight: PREFLIGHT OK (10 checks) on v0.137.3.
- dod status: 0/24 verified (draft).

### Steps 4-5 · 2026-10-04 · 5146780
- git status: clean apart from the owner files.
- compile: 0 errors (2 pre-existing CS8073 warnings in `GrantPush.cs`). tests: 219/219.
- dod status: 0/24 verified (ready → in-progress, human Review 4).

## Post-audit
### Steps 1-5 · 2026-10-04 · d62be28, 065472d, d520a48
- **Steps 1-3 (d62be28):** the pure logic.
  - `Logic/BarSet.cs`: the set a clear targets. `Keeps` fails closed on any origin outside the
    `universal | weapon:<Name> | form:<Name>` grammar.
  - `Logic/BarReset.cs`: `BarResetScope.ClearSet` and `BarResetStep.RestoreKept`. RestoreKept runs after Reapply;
    a kept bind survives on purpose.
  - `BarResetReply.ForClear`, and the `set=` log field, which only ClearSet writes.
- **Step 4 (065472d):**
  - `BarResetService.FullReset(..., BarSet clearSet)` → `ClearChosenSet` → `RestoreKept` →
    `SlotApply.RestoreResolvedGrants`. The RevertTransform reason is `clearbar`.
  - `ClearBar` refuses SteamID 0, then parses → FullReset → ForClear → `[BEELZ:event] type=slot-cleared`.
  - `check_bar_reset.py`: the clearbar exemption is removed, 4 FullReset call sites are required, and
    `SELF_RESET_COMMANDS`.
- **Step 5 (d520a48):** `ApiVersion` 35, plus the handoff banner, v0.137.4 block and version row.
- compile: 0 errors. tests: 219/219.
- preflight: PREFLIGHT OK (10 checks) — `bar-reset … 4 FullReset call sites | auth: ok, 70 admin commands,
  5 reset callers`. The version line still names 0.137.3, which the release commit (step 8) bumps.
- `check_clearbar.py`: wiring, selfonly, secrets, tests, entry, actors, handoff and selftest are ok
  (`selftest: ok, 9 checks x good/defect/empty`).
- **Planted faults** (each broke the code once, its check failed, then restored — details in the plan Log):
  - D1 RestoreKept dropped from the ClearSet plan → 4 BarResetTests fail
  - D2 `Keeps` keeps its own origin → 6+ fail; `Keeps` back to the old `none`-only test → the 6 unknown-origin
    cases fail
  - D3 the kept rule ignores `Other` → `ClearSet_fails_when_a_kept_slot_with_an_other_mod_reads_clean`;
    `All.Keeps` true → `ClearSet_fails_when_clearbar_all_keeps_a_bind_of_any_set`
  - D4 the dismount suffix without its step count → 2 BarResetReplyTests fail
  - D5 ` set=` on every scope → `Format_fails_when_a_ClearSet_line_lacks_set_or_another_scope_gains_it`
  - D6 a `ClearAllSlots` call in a ClearBar copy → `wiring: FAIL` and `commands: FAIL`; the SteamID-0 refusal
    without `return` / after FullReset → `wiring: FAIL no steamId == 0 refusal before the reset`
  - D7 `PopSlotMods()` in a command file → `commands: FAIL`
  - D8 a FullReset call in a patch → `auth: FAIL`; `resetbar` renamed `resetother` → `auth: FAIL … without adminOnly`
  - D9 `string player = null` on ClearBar → `selfonly: FAIL`
  - D10 an `apiKey` literal → `secrets: FAIL`
  - D18 an undeclared file → `paths: FAIL`
  - D20/D21/D22 a check weakened on a temp copy → `selftest: FAIL 1: <check> want FAIL`
  - D24: every scratch copy deleted → `scratch: ok`
- **/code-review (medium, 82fd362..HEAD): 3 findings, all ACCEPTED.** Each was recorded as a plan amendment before
  it was built, and each fix's control was planted once.
  - CR1 · ACCEPTED (A4, defect) · `check_bar_reset.py session`'s `_RESET` regex had no room for ` set=`, so every
    clearbar line raised `D30: malformed line`. The regex takes an optional `set=`; the session selftest has a
    ClearSet line.
  - CR2 · ACCEPTED (A1, discovered 4.5) · `BindOrigins` lists a form bind whether or not the player is in that form,
    and the reset ends every form. So an injected row on a form-bound slot read as kept, and the run as clean.
    - A form-bound slot WITH a row is now a survivor; a saved form bind with no row stays kept.
    - The first cut (never keep any form origin) broke the kept-universal case and was narrowed.
  - CR3 · ACCEPTED (A2, discovered 6.2) · `ForClear` headlined `Nothing was bound` when ClearSavedBindings failed.
    It now headlines `Could not clear <what>.`.
  - Not raised, checked by the reviewer: the shapeshift-wheel form ending silently is the plan's accepted limit
    (Design).
- Codex verdict: APPROVED (round 2) — round 1 REVISE with 2 findings (1 ACCEPTED, 1 REJECTED), fixed in 1488f99; round 2 on the fix diff `d520a48..1488f99`, sources pasted in, no reads attempted: APPROVED with no findings.
  - The first run read no file (its sandbox blocked every read) and returned APPROVED from the diff alone. That
    verdict was discarded, and round 1 was rerun with the full sources pasted into the prompt.
  - R1 F1 · REJECTED · RevertTransform/Dismount are gated on `liveReady`. That gate is the v0.137.0 planner's design
    (44498e2), not this diff. When it blocks, the reply's second line says the live bar is not reachable, so the
    player is not told the clear worked. Out of this slice.
  - R1 F2 · ACCEPTED (A3, discovered 6.2) · `RestoreKept` called `RestoreResolvedGrants`, which swallows every
    exception (and a missing equip buff or an active transform) as 0. A failed restore therefore read as a passing
    step. It now calls `SlotApply.RestoreResolvedGrantsOrThrow`, which shares its core with the old method. D6's
    `wiring` check fails on the swallowing call.
    - Twins checked: EmptyPush throws on a short push, Reapply has no catch, Dismount throws on a live mount buff.
- After the round-1 fixes:
  - build: 0 errors. tests: 221/221.
  - `check_clearbar` all ok (`paths` first caught the undeclared `Services/SlotApply.cs`, then was declared).
  - `check_bar_reset all` ok.
- Round 2 (Codex, `d520a48..1488f99`): APPROVED, no findings.
- Plan: A2/A3 name gating probe 6.2, so `review: pending` — a fresh plan review is owed before `close`.

### Step 7 · 2026-10-04 · d91c1bd (A5 diagnostic), f7c51c2 (A6 fix)
- **Deploy and runs:** each deploy closed the client, backed up both logs to `%TEMP%\beelz-logs-2026-10-04-clearbar*`,
  stopped the server with `taskkill /PID` (no `/F`), built, checked the DLL with `cmp` (identical), then started the
  server and ran `vrclient ensure`.
  1. **On 1488f99:** `clearbar_fullreset` 30/32.
     - Both RevertTransform runs read `survivors=none clean=0`: case f (`set=universal` while Beatrice) and case c
       (`set=form:Wolf`).
     - The reply's second line named `override buff still on: AB_Tailor_Shapeshift_Gargoyle_Buff` / `AB_Shapeshift_Wolf_Buff`.
     - The cause was not proven, so the next build was a diagnostic (procedure 4, amendment A5).
  2. **On d91c1bd:** 30/32 again.
     - `[Beelz READBACK] override buff=Entity(582732:35) prefab=AB_Tailor_Shapeshift_Gargoyle_Buff … tag=False
       issued=True` — the very entity `Remove` destroyed in the same reset. The DestroyTag lags the deferred destroy
       (the mounted-bar-reset A2 class).
  3. **On f7c51c2 (A6):** the readback also skips a buff whose destroy the ledger issued. `SlotApply.BarOwnedElsewhere`
     keeps the tag-only test.
     - Twins `IsLive` (spellbook repair) and the orphan sweep's source skip were left unchanged, with the reasons in A6.
     - Results: `SCENARIO PASS clearbar_fullreset 32/32`, `SCENARIO PASS mounted_reset 14/14`,
       `SCENARIO PASS cast_basic 6/6`, and no `[Beelz READBACK] … still live` line.
     - Shots read: after `clearbar all` while mounted, the bar shows the plain Sword kit plus the vampire spells, with
       no saddle ability.
- log check: `pwsh Beelzebub/tools/preflight.ps1 -LogCheck` → PREFLIGHT OK (2 checks).
  - LogOutput: 0 Beelzebub stack frames, 0 errors. The 2 warnings are the startup `[Beelz TUNE]` notices.
  - NyarDev: 0 errors. Its 226 `LogMissingPrefab` stack lines appear in every backup of the day (pre-existing).
- `check_bar_reset.py session`: every ClearSet line parsed (the A4 fix works — no D30). It fails only the bar-reset
  scenario's own D6/D8/D9 steps, which this session did not run.
- Round 3 (Codex, `985bebc..f7c51c2`, sources pasted, no reads attempted): APPROVED, no findings. The post-audit is
  finished at round 3 of 3.
- Codex verdict: APPROVED (round 3) — on the A5/A6 diff; rounds 1-2 above.

### Rollback
Rollback range: `d62be28..524e2c7`, plus the `chore(release): v0.137.4` commit that follows it (revert that first).

To roll back:
1. Stop the server: `vrclient.py close`, then `taskkill /PID <pid>` (no `/F`).
2. `git revert` the range, newest first.
3. `dotnet build Beelzebub/Beelzebub.sln -c Release` (redeploys the DLL).
4. Start the server.

There is no saved-data migration: `state.json` and `ability_rules.json` keep their shapes. A clear made by v0.137.4
writes the same buckets the old handler cleared. BCH gates the new fields on `api>=35`; on a rollback to 34 those
fields simply disappear.
