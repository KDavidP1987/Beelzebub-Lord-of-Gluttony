# Audit — bar-reset

Plan: `docs/dod/bar-reset.md` (draft, built under the owner's waiver after Codex review 5 — see its Log).

## Pre-audit
### Steps 1-4 · 2026-09-30 · base e949878
- git status: clean except the owner's own `docs/V0136_ABILITY_TEST_PLAN.xlsx` and `_matrix_build.py` (never touched)
- compile: 0 errors (Release, `-p:VRisingServerPath=Z:\no-deploy` — no deploy)
- preflight: PREFLIGHT OK (7 checks, -SkipBuild) before any change
- dod status: 0/30 verified

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
