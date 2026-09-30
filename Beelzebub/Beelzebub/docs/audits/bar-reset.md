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
