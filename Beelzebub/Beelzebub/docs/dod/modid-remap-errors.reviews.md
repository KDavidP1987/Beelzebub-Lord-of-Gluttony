# Reviews — modid-remap-errors

## Review 1 · 2026-10-04 · codex · plan uncommitted · plan 38150 B · 21 items · files 2 · a13a7fa98df4 · prompt b99d6e959b5e
F1 [blocking] `2.1` and `10.1` are unanswered: D6 checks only `adminOnly` and callers of `Clean`, not every direct and indirect route that arms or invokes the sweep; an unauthorized player reaching an alias, admin bar route, or future pop path could bypass the claimed boundary without making `actors` fail.  
Fix: enumerate every player/admin/boot/heartbeat/RCON route and make one evidence command fail when any route has the wrong authorization or mutation capability.

F2 [blocking] `2.3` is unanswered: “the sweep never targets a player by input” does not decide ownership when a character is deleted, replaced, transferred, offline, or its entity is recycled.  
Fix: state that holder ownership follows a specific stable player identity and define cleanup behavior for deleted, transferred, offline, and recycled characters.

F3 [blocking] `3.1` is unanswered: `.beelz admin modleak [player|all]` lacks an actionable input contract for omission, ambiguous names, nonexistent/offline players, excess length, and invalid tokens.  
Fix: specify the accepted grammar and limits plus the exact response for each invalid or ambiguous input.

F4 [blocking] `3.3` is unanswered: the rules backup has no retention/deletion rule, and no evidence command fails when artifact ownership or retention is omitted; D18/D19 check paths, not lifecycle.  
Fix: give every produced artifact—including rules backups, build outputs, audit/review files, logs, screenshots, and deployed DLLs—an owner, retention period, deletion mechanism, and a command that detects omissions.

F5 [blocking] `4.4` is unanswered: D1/D2 define verdict precedence but neither states who may authorize an exception when safety, cleanup, or release rules conflict.  
Fix: state the complete conflict order and name the role that alone may approve an exception.

F6 [blocking] `5.3` is unanswered: the plan introduces `LeakHolder`, row/verdict data, and service state without enumerating their fields against what the code reads and emits.  
Fix: enumerate each introduced shared type, enum value, and field—including identity/index/version and scan state—or state that each remains file-private and prove that boundary.

F7 [blocking] `6.2` is unanswered as a control: `wiring` checks call order but will not fail if exception handling, retry/re-arm behavior, malformed-dump handling, or dirty-slot recovery is removed; slow/down/rate-limited applicability is also undecided for several collaborators.  
Fix: define each collaborator’s slow, unavailable, throwing, malformed, and deferred-effect behavior and provide one command that fails when those fallbacks are absent.

F8 [blocking] `6.3` is unanswered: using a dev server and vrclient does not decide whether any diagnostic behavior, commands, fixtures, or planted-fault mode can enter production.  
Fix: state whether test mode exists and provide the build/runtime boundary that excludes test-only behavior from release artifacts.

F9 [blocking] `11.3` is unanswered: “one plain chat line under the chat cap” addresses width only, not keyboard access, screen-reader behavior, contrast, or the applicability test for those concerns.  
Fix: state which accessibility properties are supplied by V Rising/VCF, which are applicable to this text-only command, and the small-screen wrapping/truncation behavior.

F10 [blocking] `12.1` is unanswered: the plan names internal outcomes for unreadable holders and thrown cleanup but does not decide the exact admin-visible message or next action for each failure class.  
Fix: map every failure class to its user/admin message, retry behavior, and recovery instruction.

F11 [blocking] `12.4` is unanswered: D21 exercises synthetic checker fixtures, while several real checks have no recorded real failing run, silent input, and empty-input output; D10, D11, D13–D19 remain `n/a`, which rubric 2 explicitly scores as a Gap.  
Fix: run every introduced real check once against its real planted failure, benign/silent case, and empty input, recording exact output and ensuring empty input never reads as a pass.

F12 [blocking] `12.4` makes D10 unverifiable by its evidence type: the supplied `grep` merely prints matching lines and cannot itself fail on `cleaned < 7`, a `failed` line, multiple sweep lines, or the required zero counters, so a stranger cannot verify D10 from command success.  
Fix: replace it with one assertion command that exits nonzero for every listed defect and records the validated log path.

F13 [blocking] `12.4` makes D12 unverifiable by its evidence type: the commands inspect only one current log apiece, do not identify both restart backups, and `grep -c` normally exits nonzero when the desired count is zero.  
Fix: provide one assertion command over both named restart log sets that exits zero only when both remap and Whirlwind counts are zero.

F14 [blocking] `13.1` is unanswered: scan frequency and algorithm are described, but no latency, frame-time, or throughput budget is stated.  
Fix: set a measurable per-pass or per-frame budget, identify the measured hot path, and name the evidence command and workload that enforce it.

F15 [blocking] `14.1` is unanswered: “no flag” and “ships all at once” do not identify who can disable cleanup if production behavior is unsafe.  
Fix: name the authorized rollback operator and the exact disable mechanism available before and after the first destructive sweep.

F16 [advisory] `4.5`: the “every pop” computation knowingly misses equivalent removal through another engine API, while code review is the only semantic backstop.  
Fix: centralize pop/removal behind one wrapper or broaden the checker to enumerate all registry-removal APIs and require reviewed exclusions.

F17 [advisory] `9.1`: with more than 4,096 holders, “the next pass continues” is not backed by a stated cursor/order invariant; repeatedly scanning the same first 4,096 could starve later stale holders.  
Fix: specify and test continuation progress across two capped passes with a stale holder beyond the first boundary.

F18 [advisory] `9.3`: a pop after the second read but before destruction is treated as impossible because execution is main-threaded, but deferred ECS effects could still make the second snapshot stale before cleanup takes effect.  
Fix: add an in-game race scenario that applies a replacement at the confirmation boundary and verifies the newly live holder survives.

F19 [advisory] S-2 is mislabeled `reversible`: it assumes the core cleanup primitive’s semantics, and its fallback ends with “the owner re-decides,” so it is not a complete reversible decision.  
Fix: decide now whether zero removals aborts destruction and re-arms, then validate that policy before the first destructive boot.

F20 [advisory] S-3 is not cheaply reversible: restoring an entire world save can discard unrelated gameplay since the backup, while D11/D12 only detect obvious errors and remap warnings.  
Fix: describe the collateral-data window and prefer a holder-level backup/restore path or require rollback before accepting new world progress.

F21 [blocking] `12.4` makes D18 unverifiable by a stranger: “the owner’s two private files” are not named or machine-checkably allowlisted, so the reviewer cannot distinguish permitted dirt from an undeclared change.  
Fix: define the two exact paths in a committed allowlist and make `status` fail for every other tracked, untracked, or ignored generated path.

5/15 layers · 35/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · `actors` now also lists every `ModLeakService.MarkDue` caller plugin-wide and FAILs any outside `Core.TryInitialize`, `SlotApply.PushOnSlot`, `TransformBuffService.PopSlotModifications` (selftest plants a command that arms it); Clean stays Tick-only. Arming only moves when a pass runs — what it cleans is decided by `ModLeak` alone, so no route can pick a holder. Console/RCON run no VCF command; VCF only handles chat from a connected Steam-authenticated user (Design › Permissions)
- F2 · accepted · Design › Permissions › Ownership now decides offline (swept like online), deleted (dead target → never touched), recycled (index + version) and transferred (not possible in V Rising)
- F3 · accepted · Design › UX › Input states the grammar (omitted / `all` / name via `TryBarTarget`), the exact no-match reply, offline resolution, and the name/line caps
- F4 · accepted · the rules backup row now names its file (`ability_rules.json.bak-<timestamp>`) and its retention (kept until the owner deletes it); rejected: a command that checks lifecycle of files outside the repo — the Design › Data table is the record, as accepted in transform-chain-guard Review 1 F3, and D18/D19 cover every repo path
- F5 · accepted · Business rules 6: conflict order (safety, cleanup, log budget) and that only the project owner approves an exception, through an amendment
- F6 · accepted · Interfaces › Internal enumerates the public `Logic/ModLeak.cs` surface (enums, records, fields) and states that `HolderScan`/`RowScan` are internal and the sweep state private static
- F7 · accepted · `wiring` now FAILs when Clean loses its catch or DestroyTag skip, ReadSlot loses its `SlotModParse.Failed()` fallback, or Tick stops re-arming on `keptOnce > 0 || capped` (selftest plants a Clean without catch); slow/down/rate-limited stated as N/A per collaborator (in-process ECS calls) in Interfaces › External
- F8 · accepted · Interfaces › External › Test mode: no test mode in the DLL; DevChatEcho, vrclient, scenarios, check fixtures and planted faults stay outside the package
- F9 · accepted · Design › UX › Accessibility states what the game's chat box supplies, that screen readers do not apply, and the wrap/cap behaviour
- F10 · accepted · Failure & observability opens with a failure-class table: admin-visible text, retry, recovery
- F11 · accepted · D10 and D12 now have real assertion commands, each run once against the real current logs (real failing case recorded in the Log); rejected for D11, D13, D14, D16–D19: they need the deployed build, the release commit or the audit, and the dod skill records exactly that as `dry-run · n/a · <why>` — none can run before the build step it depends on
- F12 · accepted · D10 is `check_modleak.py bootsweep --log <backup>`: exits 1 on no/several boot sweep lines, fewer than 7 cleaned, any unreadable, over 250 ms, or a failed clean; selftest covers each
- F13 · accepted · D12 is `check_modleak.py restarts --dirs <r1> <r2>`: both logs per folder, a completed boot required, exits 1 on any remap error or Whirlwind reject; selftest covers ok/defect/empty
- F14 · accepted · budget 250 ms for the confirming pass at the dev boot (the largest batch); `SweepLine` now carries `<ms> ms`, D4 tests it, D10 enforces the budget
- F15 · accepted · Rollout names the operator (server owner) and the mechanism (do not install / reinstall v0.137.5); a runtime switch is out of the owner's decision set and would be an amendment
- F16 · accepted · a "pop" is now any `RemoveAbilityGroupModificationOnSlot(` or `Modifications.Remove*(` call; a wrapper refactor is out of scope
- F17 · accepted · the cap text was wrong ("the rest next pass" with no cursor): the sweep line now says `the rest not read`, Maximal stretch states that holders past the cap are reached only as earlier ones are cleaned, accepted against the derived bound (~3 200 < 4 096)
- F18 · rejected · the confirming read and the clean run in the same `Tick` call on the main thread; no pop or apply can land between them, and a row applied later lives on a new holder entity whose index/version the confirm never matches (Maximal stretch)
- F19 · accepted · S-2's fallback is now decided: if D10 or D11 shows the clear did nothing, the release stops and the `fix(modleak)` commit is reverted
- F20 · accepted · Rollout states the collateral window of the save restore (every world change since 16:30 on 2026-10-04) and that tester servers need no restore; S-3 now has the same release stop as S-2
- F21 · accepted · D18 names both paths and points at `OWNER_PATHS` in `check_modleak.py`

## Review 2 · 2026-10-04 · codex · plan commit 6a0c16e · plan 46863 B · 21 items · files 3 · bbf81cdc7c1a · prompt d0e44757ffa2
1. Considered — Purpose & typical use: `Purpose & typical use` answers 1.1–1.3.
2. Gap — Actors & permissions: `Design › Permissions` does not consistently answer or control 2.1.
3. Considered — Inputs, outputs & data: `Design › Data`, `Design › UX`, and D1–D4/D17–D19 answer 3.1–3.4.
4. Gap — Business rules & invariants: `Business rules` states 4.4, but no command enforces its precedence.
5. Considered — Internal interfaces: `Interfaces › Internal` answers 5.1–5.3.
6. Gap — External dependencies & contracts: `Interfaces › External` does not fully answer or control 6.2.
7. Considered — States & lifecycle: `Design › States` answers 7.1–7.3.
8. Considered — Minimal stretch: `Use cases › Minimal stretch` answers 8.1–8.2, although its D14 pointer is inaccurate.
9. Considered — Maximal stretch: `Use cases › Maximal stretch` answers 9.1–9.3.
10. Gap — Security & privacy: D6 does not enforce authorization on every indirect path required by 10.1.
11. Considered — Design & UX: `Design › UX` answers 11.1–11.4.
12. Gap — Failure handling & observability: the recorded dry runs do not satisfy 12.4.
13. Gap — Performance & scale: the stated cap behavior does not handle a valid starvation case under 13.2.
14. Considered — Rollout & compatibility: `Rollout` and D15–D19 answer 14.1–14.4.
15. Considered — Out of scope: `Out of scope` answers 15.1–15.2.

F1 [blocking] Probe 2.1 is contradictory: players allegedly cannot reach the sweep, yet their self-service `grant` and `resetbar` paths arm a global sweep, while `actors` checks only `adminOnly`, `Clean`, and `MarkDue` call sites—not which actors may invoke those callers.  
Fix: Satisfy 2.1 by explicitly deciding whether ordinary players may trigger global maintenance and make one command fail if any triggering command has permissions or target scope outside that decision.

F2 [blocking] Probe 10.1 is uncontrolled: `actors` would still pass if an existing player command became cross-player or an admin bar path lost authorization, because it inspects neither command authorization nor target ownership.  
Fix: Satisfy 10.1 with one evidence command that enumerates every direct and indirect trigger and fails when its authorization and self-target/admin-target constraints are removed.

F3 [blocking] Probe 4.4 has prose precedence but no enforcing command: D1–D3 test verdict primitives and `wiring` checks only that `Confirm` textually precedes `Clean`, so a changed `Tick` could pass live, unknown, or building holders through the cleanup pipeline without failing the cited control.  
Fix: Satisfy 4.4 with a test of the complete scan→confirm→clean selection proving safety verdicts always outrank cleanup and only the owner-approved exception path can bypass that rule.

F4 [blocking] Probe 6.2 is incomplete for collaborator failure: malformed or extreme clear counts, a destroy call that returns without producing `DestroyTag`, and a clean throw after modifications were cleared are undecided; notably D5 re-arms only for `keptOnce || capped`, so the promised retry after a clean failure is not controlled.  
Fix: Satisfy 6.2 by deciding retry/re-arm and conservative behavior for malformed returns, silent non-destruction, partial cleanup, and slow passes, with one fault-injection command that fails when those fallbacks are removed.

F5 [blocking] Probe 12.4 is not satisfied because the current `modid_remap.vrs` fixture—changed to add the missing capture—has never been run once against the real check, and the 250 ms tolerance cites only one prospective boot rather than three measured runs, a specification, or a reversible assumption with fallback; a stranger sees only `n/a` for D11.  
Fix: Satisfy 12.4 by recording a failing run of the exact current scenario and sourcing the 250 ms threshold from three real runs with spread, a specification, or an explicitly reversible assumption with fallback.

F6 [blocking] Probe 13.2's bound behavior is false for a concrete maximal case: if the first 4,096 query-ordered holders are live, none is cleaned, so stale holder 4,097 is never reached despite repeated re-arming.  
Fix: Satisfy 13.2 by choosing and testing pagination or a persistent cursor that eventually examines holders beyond the cap even when all earlier holders remain live.

F7 [advisory] S-2 and S-3 are not genuinely reversible: cleanup mutates the world save, tester servers have no backup, and restoring the dev backup discards unrelated world progress; clean logs and two restarts also cannot establish that destruction has no other gameplay effect.  
Fix: Label them validated-before-rollout destructive assumptions, or provide a scoped recovery mechanism and evidence covering the holder’s other observable effects.

F8 [advisory] The minimal clean-server path says the boot sweep logs nothing, while activation and production detection rely on a sweep line showing that it ran; an empty boot is therefore indistinguishable from failed activation.  
Fix: Define an explicit empty-run signal or state that silent empty activation is intentional and identify another verifiable activation indicator.

EARLIER: all resolved
9/15 layers · 43/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · Design › Permissions decides it: an ordinary player MAY arm the sweep through their own self-only grant/resetbar/clearbar; arming only schedules a coalesced pass that cleans holders `ModLeak.Select` proves stale, with no player-chosen target. `actors` now also runs `check_clearbar.py actors` (entry channel, self-only reset commands, every admin bar/purge/reset-loadouts adminOnly, every FullReset caller) and FAILs when it does
- F2 · accepted · same change as F1: removing the self-only or adminOnly reach of any command that reaches a pop now FAILs `check_modleak.py actors`
- F3 · accepted · the selection moved into pure `ModLeak.Select(firstRead, secondRead)`; `Select_fails_when_a_holder_that_is_not_stale_now_is_cleaned` proves Live/Unknown/Building/Empty are never selected whatever the first read said (planted once); `wiring` requires `ModLeak.Select` before `Clean` in Tick
- F4 · accepted · decided per case in the failure table: a clean that throws (before or after the clear) keeps the holder and re-arms the sweep (`failed > 0`, checked by `wiring`); a destroy without effect is re-read and re-selected next pass; a `DestroyTag` holder is skipped; a clear count of 0 still destroys (dead-id-only holder). Slow passes are measured on the sweep line (S-4). A runtime fault-injection harness for ECS calls is rejected: the plugin has no test seam into IL2CPP ECS and the fallbacks are enforced structurally by `wiring` plus the in-game run
- F5 · accepted · the 250 ms threshold is now S-4, a reversible assumption with a decided fallback (release stops; owner decides with three measured boots). Rejected in part: running the revised `modid_remap.vrs` before deploying the reviewed build — it needs this build's `modleak` output; its earlier form ran 6/7 (Log) and D11 is run at step 6
- F6 · accepted · real defect in the cap design: `ModLeak.Page` pages ascending entity indices from a cursor and wraps; `Tick` keeps one page per pass and moves the cursor; D22 with `Page_fails_when_a_holder_past_the_cap_is_never_read` (planted once); `wiring` requires paging in Scan
- F7 · rejected · the destructive step was the owner's explicit choice (decision 6-A, 2026-10-04) with the dev save copied first; S-2 and S-3 now carry a decided release stop, which is what makes them reversible before rollout — after rollout a cleaned holder held only dead ids and leftover mods, so there is nothing to restore
- F8 · accepted · a boot pass that finds nothing logs `ModLeak.IdleLine` (`sweep (boot): nothing stale among <n> player holder(s); <ms> ms.`), tested and planted; `restarts` now FAILs a boot without a boot sweep line
