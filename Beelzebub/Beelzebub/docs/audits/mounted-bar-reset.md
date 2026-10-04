# Audit — mounted-bar-reset (v0.137.3)

Plan: `docs/dod/mounted-bar-reset.md` (18 items). It was approved by a human review on 2026-10-04 after three Codex
plan rounds. The bug, reported by the owner on 2026-10-03:
- A saddle bind on slot 3 was live on the server but invisible; the client never draws slot 3.
- A `resetbar` while riding silently dismounted the player and still reported `clean=1`.

## Pre-audit
### Resume after power cut · 2026-10-04 · 0a135f5 (+ uncommitted logic)
- Integrity: `git fsck` is clean, and no in-progress file was truncated or zeroed (NUL scan).
- World saves AutoSave_3016-3019 pass `gzip -t`.
- The server's `state.json` and `ability_rules.json` parse.
- Logs are backed up to `%TEMP%\beelz-logs-2026-10-03-powercut\`.
- git status: the uncommitted `Logic/MountedSlots.cs`, `Logic/BarReset.cs`, `Logic/BarResetReply.cs` and their tests,
  plus the plan files; also the owner's `V0136_ABILITY_TEST_PLAN.xlsx` / `_matrix_build.py` (never staged).
- compile: the plugin had 1 expected error (CS0535, `BarResetService` lacking `Dismount()`, built in step 4). The
  test project passed 185/185.
- dod status: 0/18 verified (plan draft → ready → in-progress on 2026-10-04).

## Post-audit
### Steps 1-7 · 2026-10-04 · 21b8c53, 1310c5d, 6da0fdf, 6e58185, 334f67f, 2f39be6; review round cca456d
- **Steps 1-2 (21b8c53):** `Logic/MountedSlots.cs` (`Allowed` {5,6,7}, `Hint`, `RejectMessage`, `Migrate`) and
  `MountedSlotsTests`.
- **Step 3 (1310c5d):**
  - `ShapeshiftAbilityService._mountedSlots` reads `MountedSlots.Allowed`.
  - The form-grant reject replies `RejectMessage()`.
  - `SlotApply.MountedAny` → public `ShapeshiftAbilityService.IsMountedAny`.
  - The `PersistenceService` FormSlots load migrates the Mounted map, logs `[Beelz MOUNT] <steamId> saddle slot 3 -> 5`,
    and calls `RequestSave()` once.
  - Built alone against the committed logic: Build succeeded.
- **Step 4 (6da0fdf):**
  - `BarResetStep.Dismount` is planned after SaveBindings and before DestroyOverrideSources when the character is
    online, live-ready and mounted.
  - `BarResetService.Dismount` → `TransformBuffService.DestroyMountBuffs`: the guarded `SafeDestroyBuff`, the same
    call the orphan sweep made in the 2026-10-03 spike. It throws when a mount buff is still live (neither gone nor
    DestroyTag'd).
  - Built and tested alone: 182 passed.
- **Step 5 (6e58185):** the dismount sentence on the reply headline, only when `CountOf(Dismount) > 0`.
- **Step 6 (2f39be6):**
  - `check_bar_reset.py all` / `auth` / `selftest` ok; `check_mounted_bar.py callers` / `secrets` / `selfonly` ok.
  - preflight then failed `bar-reset docs: ApiVersion is 34, expected 33`, so its exact match became `>= 33`.
    Planted: 32 → FAIL.
- **Step 7 (334f67f):** `ApiVersion` 34; handoff banner, v0.137.3 block, version row; `check_mounted_bar.py handoff` ok.
- compile: 0 errors. tests: 186 passed.
- preflight: all ok except the expected version/CHANGELOG items, which belong to the release commit (step 10).
- **Planted faults** (each broke the code once, its check failed, then restored):
  - D1 `Allowed` += 3 → `IsValid_fails_when_slot_3_or_a_riding_key_is_allowed` FAIL
  - D2 slot-5 guard removed → `Migrate_fails_when_a_taken_slot_5_is_overwritten` FAIL
  - D4 Dismount after the sweep → `Plan_fails_when_Dismount_is_missing_or_after_the_orphan_sweep` FAIL x3
  - D5 CountOf guard dropped → `ForReset_fails_when_a_dismount_line_appears_without_a_dismount` FAIL
  - D15 a second `Migrate` caller → `callers: FAIL`
  - D16 an `apiKey` literal → `secrets: FAIL`
  - D17 a `player` parameter on FormGrant / `adminOnly` on resetbar → `selfonly: FAIL`. The first plant exposed a
    parser bug (it read `null` as the parameter name), fixed before the plant was repeated.
  - D18 an undeclared file → `paths: FAIL`
- **/code-review (medium, 0a135f5..HEAD):** no correctness bugs. One low finding, ACCEPTED (fixed in cca456d): a
  slot-3 bind of 0 logged `dropped: slot 5 taken`; it now logs `dropped: empty bind`. Doc nit ACCEPTED: the handoff's
  `api version` example now reads `api=34 plugin=0.137.3`.
- Codex verdict: APPROVED (round 2) — round 1 REVISE with one finding, REJECTED; round 2 APPROVED with two advisories.
  - R1 F1 (slot 3 filtered before Migrate) · REJECTED · the FormSlots loop keeps every int slot
    (`int.TryParse` → `slots[slot]`). `IsValidMountedSlot` is called only by form-grant. Codex withdrew it in round 2.
  - R2 F1 (200 ms timing test is flaky) · REJECTED · the bound is in the frozen D2 baseline; 10,000 migrations take
    about 1 ms locally, a 100×+ margin.
  - R2 F2 (a dropped bind still counted in the `Loaded …` total) · ACCEPTED · fixed in cca456d.
  - R3 (fix commit cca456d only) REVISE: F1 · ACCEPTED · slot 3 moving onto an explicit 0 on slot 5 shrank the map without
    lowering the loaded-slot total; fixed in 9d5c5b2 by subtracting the map's size change.
- Round cap: three Codex rounds were run. The R3 fix is a one-line counter change (build ok, 186 tests pass), so a
  fourth round is waived; the D3 live load below exercises the changed line.
- in-game: pending (step 9: D3 fixture, D8, D9).

Rollback range: 21b8c53..9d5c5b2
Rollback: `git revert --no-edit 21b8c53^..9d5c5b2`, stop the server (`taskkill /PID <pid>`), then
`dotnet build Beelzebub/Beelzebub.sln -c Release` (redeploys the DLL), then start the server.
Consequence for migrated state: a state.json already migrated by v0.137.3 holds the old slot-3 saddle bind on slot 5.
v0.137.2's `BuildMountedBar` filters slot 5 out of {3,6,7}, so it is an inert slot-5 bind: not shown, not harmful, and
cleared by `resetbar`. The reset change writes nothing persistent. (The range is widened to the release commit at
step 10.)
