# Audit — modid-remap-errors (v0.137.6)

Plan: `docs/dod/modid-remap-errors.md` (22 items). Three Codex rounds, then a human review (Review 4, READY) on
2026-10-04, after which the plan was approved and started.

**The problem.** Each startup logged `Couldn't remap old Modification Id … AbilityGroupSlotModificationBuffer` errors.
They came from the engine's prefab-less slot-override holders:
- When the mod popped a holder's GroupGuid mod by id, the holder survived, kept alive by its CopyCooldown and
  SpellMods mods.
- It was saved with dead ids. Those ids error at boot, and some were remapped onto other mods' ids.
- Six such holders forced slot 7's CopyCooldown to False.

**The fix.**
- `Logic/ModLeak.cs` (pure) decides which holders are stale: a row is live only when its id **and** its source holder
  match; a holder is cleaned only after two reads at least 2 s apart; reads page past the 4096 cap and arming
  coalesces.
- `Services/ModLeakService.cs` runs it from the heartbeat. For each confirmed holder it calls
  `ClearLooseSourceModifications`, sets `DirtyTag` on the slots, then calls `DestroyUtility.Destroy`.
- The sweep is armed by boot and by both pop sites.
- New read-only admin command: `.beelz admin modleak [player|all]`.
- Data: both Militia Leader Whirlwind v2 groups now have FreeMoveAfterSeconds 1.0.

## Pre-audit
### Steps 1-4 · 2026-10-04 · 3e3715c..daa5b29 (built during the diagnostic and the review rounds)
- git status: only the owner's `docs/V0136_ABILITY_TEST_PLAN.xlsx` / `_matrix_build.py` (never staged).
- compile: 0 errors (pre-existing warnings only). tests: 258/258 at approval (1c79729).
- preflight: PREFLIGHT OK on v0.137.5 before the build.
- dod status: 0/22 verified at approval (draft → ready on human Review 4 → in-progress).

## Post-audit
### Steps 1-6 · 2026-10-04 · 3e3715c..daa5b29
- **compile / tests / preflight:**
  - Release build: 0 errors.
  - `dotnet test`: 260/260, with 15 named ModLeak controls.
  - `pwsh Beelzebub/tools/preflight.ps1 -LogCheck`: PREFLIGHT OK (2 checks) after every in-game session.
- **check_modleak.py:** all ok — `selftest` (12 checks × good/defect/empty), `wiring`, `actors`, `tests`, `data`,
  `bootsweep`, `restarts`, `handoff`, `backlog`.
- **Planted faults.** Each broke the code once, was caught, and was restored byte-exactly. Details are in the plan
  Log.
  - Caught by their named xUnit control: Row, Holder, Confirm, Select, Page, IdleLine, Arm, LogIdle and MorePages.
  - Caught by `wiring` FAIL: the PrefabGUID skip removed, the wall clock used, the fresh-cycle re-arm removed, and
    the mid-cycle condition narrowed.
- **/code-review (681f313..e0db2f0): 2 findings, plus 1 test-gap note.**
  - CR1 · ACCEPTED (A2, discovered 13.2).
    - Defect: with more than 4096 holders every page reported capped, so the sweep never slept.
    - Fix: pure `ModLeak.MorePages` ends the cycle once every holder was read.
    - Control: `MorePages_fails_when_a_server_over_the_cap_never_idles`, planted.
  - CR2 · ACCEPTED (A3, defect). A holder already carrying `DestroyTag` was counted as a failed clean and re-armed the
    sweep. It is now skipped (Clean returns null). This is the same finding as Codex F6.
  - CR3 (note) · REJECTED.
    - The concern: holders might have their GroupGuid mod registered under the equip buff, so live holders would read
      Stale.
    - The in-game run shows otherwise: the 4 holders behind live grants read `row=Live` with themselves as the
      source, and every sweep after a grant reported `nothing stale`.
    - `clearbar_fullreset.vrs` (32 checks of bar contents) passes after the sweeps.
- **Codex verdict:** REVISE (round 3, checker-only finding fixed). Each round's findings and dispositions follow;
  the round cap (3) is reached.
  - **Round 1 (681f313..378b93d), REVISE:**
    - F1 · REJECTED: an empty-id row is not evidence of death. Empty holders are never cleaned (conservative), and
      none were seen (the diagnostic had 11 holders, 0 empty).
    - F2 · ACCEPTED (A3): Scan skips holders carrying a `PrefabGUID`, so only prefab-less holders are candidates. A
      `wiring` check enforces it, planted once.
    - F3 · ACCEPTED (A4): the two-read gap runs on a monotonic stopwatch clock. `wiring` FAILs on `DateTime.UtcNow`
      in the service, planted once.
    - F4 · REJECTED: the second read and the clean run back to back on the main thread in one heartbeat call, with no
      ECS system update in between, so nothing can turn a holder live in that window.
    - F5 · REJECTED: queries made with `EntityManager.CreateEntityQuery` belong to the EntityManager, and the repo
      never disposes them (`EntityExtensions.cs`, `ChatNotifier.cs`). The entity array is disposed.
    - F6 · ACCEPTED (A3): see CR2.
    - F7 · REJECTED: `tests` checks that the controls exist. Running them is the D1–D4 and D22 evidence
      (`dotnet test`).
    - F8 · ACCEPTED: the data check rejects a JSON bool.
    - F9 · REJECTED: the clean path's behaviour is proven in game (7 holders cleaned at boot, then 0 remap errors on
      restart). A C# parser for the checker is out of proportion.
  - **Round 2 (378b93d..062bd05), REVISE:**
    - F1 · ACCEPTED (A5): a pop during a paged cycle could leave a stale holder behind the cursor. MarkDue now flags a
      pop that lands mid-cycle, and Tick runs one fresh cycle from 0. A `wiring` check enforces it, planted once.
  - **Round 3 (062bd05..5bac9d5), REVISE:**
    - F1 · ACCEPTED (checker only): `wiring` now requires MarkDue's exact condition
      `_firstRead != null || _cycleRead > 0`, with a new `wiring_midpage` fixture, planted once (daa5b29).
- **In game** (vrclient as Chaos, dev server `127.0.0.1:9876`):
  1. The first boot of the build cleaned the 7 saved stale holders, 24 leftover mods in all, in 16 ms
     (`bootsweep: ok`, D10).
  2. `.beelz admin reseed merge` adopted both Whirlwind values (`adopted (2)`).
  3. Scenarios on e0db2f0 and again on the final daa5b29: `SCENARIO PASS modid_remap 9/9`, `cast_basic 6/6`,
     `clearbar_fullreset 32/32`.
     - The first modid_remap run (8/9) is what led to A1: grant + resetbar no longer leaves a stale holder, so the
       pop-armed pass was idle and silent.
  4. During `clearbar_fullreset`, one reset still leaked a holder, and the sweep confirmed and cleaned it two reads
     later (`cleaned 1 stale holder(s), 3 leftover mod(s)`). The leak path is still live in vanilla's reset handling,
     and the sweep is what closes it.
  5. Restarts: `restarts: ok, 2 boot(s) … 0 remap errors, 0 Whirlwind rejects` (D12). The final build's own boot also
     logged 0 remap errors.
  - Log check: `PREFLIGHT OK (2 checks)`, `0 Beelzebub stack frame(s), 0 error(s)`.
- **Measurement to watch (S-4):**
  - Boot passes: 16, 28, 28 and 33 ms.
  - Pop-armed cleaning passes in play: 10 ms (e0db2f0 session) and **268 ms** (daa5b29 session, cleaning 1 holder).
  - Decision 9-A makes the budget a recorded measurement rather than a release stop. The 268 ms sample is a single
    in-play pass and is logged as backlog row `modleak-pass-cost`: time the read and the clean separately before
    changing anything.
- dod status: evidence lines for D10–D14; the rest are recorded at the release step.

## Rollback
Rollback range: `3e3715c..daa5b29`
1. `git revert --no-edit 3e3715c..daa5b29` (newest first), or revert the `chore(release): v0.137.6` commit's version
   and docs together with it.
2. Stop the server gracefully: `taskkill /PID <pid>`, without `/F`.
3. `dotnet build Beelzebub/Beelzebub.sln -c Release` (deploys the DLL), then start the server.
4. Holders that were cleaned are not restored by a code rollback. They held only dead ids and leftover mods. If one
   must come back, stop the server and copy `%TEMP%\beelz-save-2026-10-04-pre-modleak` over
   `save-data-nyardev`. This discards later world progress on the dev save.
