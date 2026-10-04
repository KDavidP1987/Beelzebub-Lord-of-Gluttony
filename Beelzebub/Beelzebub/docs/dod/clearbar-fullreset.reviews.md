# Reviews — clearbar-fullreset

## Review 1 · 2026-10-04 · codex · plan commit 82fd362 · plan 37719 B · 19 items · files 15 · 8c5d7d7c605c · prompt f07ac3a6d5f5
F1 [blocking] `2.1` is not controlled: D8/D9 test handler shape and call sites, but no command fails if an unauthenticated, console/RCON, or service caller can invoke `ClearSet`; prose that those actors have no path is not evidence.
Fix: Add one executable authorization check that enumerates every entry path and fails when ClearSet is reachable by anything except the authenticated caller or an explicitly authorized admin path.

F2 [blocking] `3.3` is incomplete: the artifact list does not state owner, retention duration, deletion procedure, and copy count for each artifact, while the cited command only verifies planner steps.
Fix: Add structured artifact rows for state, logs, backups, temporary screenshots/results, audit/review files, and deployed DLLs, plus a command that fails when any producer lacks a complete row.

F3 [blocking] `6.2` lacks decisions and controls for slow, unavailable, or garbage-returning VCF, persistence, ECS readback, filesystem/git, preflight, and vrclient/OCR dependencies; it covers only thrown reset steps and failure to join.
Fix: State timeout/fail-open/fail-closed behavior for each dependency and add a fault command that demonstrates every failure produces a non-clean result without losing kept binds.

F4 [blocking] `6.2` also misses the concrete active-form path: D12(c) clears Wolf only while on foot, so a vanilla-form override can survive or corrupt RestoreKept without the gating evidence failing.
Fix: Add an executable active-Wolf-form case, or state and enforce a fail-closed policy when the form cannot be automated.

F5 [blocking] `10.1` is not proven on every path: `check_auth` permits methods by the names `ResetBar`/`ClearBar`, does not prove their command identity or authentication boundary, and supplies no unauthorized invocation case.
Fix: Make one authorization command fail on a planted non-self ClearSet caller, renamed/decorated ClearBar handler, indirect service/job call, and unauthenticated command attempt.

F6 [blocking] `10.3` is under-controlled: D10 detects only a narrow class of literal assignments in C# and cannot fail when credentials are read from config/environment, included in replies or logs, or stored in generated artifacts.
Fix: State that ClearSet requires no credential source or rotation and add one command scanning all shipped/config/log-writing paths that fails on credential acquisition, persistence, or emission.

F7 [advisory] `10.4` names SteamID collection but does not decide minimization, log/export exposure, retention, or the audit trail.
Fix: State which identifiers each RESET/BAR line contains, who can access them, their retention/deletion policy, and whether ClearSet creates any new audit event.

F8 [blocking] `11.3` is unanswered: “chat only, nothing colour-dependent” does not decide keyboard operation, screen-reader behavior, message truncation on small screens, or acceptable limitations.
Fix: State the supported accessibility contract—for example keyboard-only command use, plain-text screen-reader exposure, and the 480-byte single-line small-screen limit—and map it to D4 or a manual item.

F9 [blocking] `12.3` substitutes an ad hoc tester/preflight inspection for a production alert or dashboard and does not say who notices or responds when `scope=ClearSet clean=0` occurs.
Fix: Define an operational detector and owner, or explicitly decide that no automated monitoring exists and give the server-owner log-check/runbook trigger.

F10 [blocking] `12.4` fails its mandatory execution rule: D1–D6, D11, D12, D16, and D19 are recorded as `n/a` or currently failing, and `check_clearbar.py` has no stated empty/good/defect selftest proving its checks fail and remain silent correctly.
Fix: Run every real check once against its exact failing input, record the output, and add selftests covering failing, passing, and empty input for every introduced check.

F11 [blocking] `12.4` has no single evidence command for its own control: “each check’s planted fault” is not a command, and several future planted faults are merely promised in Build-plan prose.
Fix: Add one selftest command that executes all planted fixtures and fails if any target command passes its defect, fails its good fixture, or reports an empty input as success.

F12 [advisory] `13.2` gives the slot bound but not the observed case that established it or whether any valid bar configuration is excluded.
Fix: Cite the engine/API source for slots 0–8 and state explicitly whether the bound excludes any valid slot or merely rejects corrupt data.

F13 [blocking] `14.3` is not presently verifiable: D16’s audit and exact commit range do not exist, so a stranger running the stated command receives `FAIL no input`.
Fix: Record the resolvable first/last commits and exact revert, rebuild, redeploy, and post-write compatibility consequences before approval.

F14 [blocking] `14.4` is incomplete and its controls inspect only repository changes: Build step 7 creates `%TEMP%\beelz-logs-2026-10-04-clearbar\`, but that backup path is absent from Paths walked, and D17/D18 cannot detect omitted external outputs.
Fix: Add every external/generated path, including the dated log-backup directory, and extend one paths command to compare declared paths with both repository and procedure-created outputs.

F15 [blocking] D19 is unverifiable by its `file` evidence as written: the required literal omits the backlog row’s backticks and does not identify a stable parser or exact resulting row, so a stranger cannot reliably determine pass/fail.
Fix: Give the exact Markdown row including backticks and version text, or replace it with a command that parses the slug and asserts `DONE v0.137.4`.

F16 [advisory] The concurrent scenario “cast already in flight while clearbar runs” is only logged by the late reread; the plan does not restore the cleared state or warn the player after the original clean reply (`7.2`, `7.3`).
Fix: Decide whether a late survivor triggers repair, changes the original run to non-clean, or produces a user/admin warning, then exercise that race once.

F17 [advisory] The maximal-stretch claim is not exercised with binds in every universal, weapon, form, and mounted set; D12(e) checks only one kept universal bind (`9.1`, `9.3`).
Fix: Add a fixture covering all bucket types and repeated ClearSet calls, verifying only the selected bucket disappears and no slot modifications accumulate.

F18 [advisory] S-2 treats inability of the current automation to operate the shapeshift wheel as validation that active vanilla-form behavior need not be tested; that assumption is not actually reversible or validated.
Fix: Use a test-only/admin form trigger, a manual evidence item, or constrain the feature so active vanilla forms are explicitly unsupported and fail closed.

7/15 layers · 36/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · `check_bar_reset.py auth` now identifies the self-only callers by COMMAND name (`SELF_RESET_COMMANDS`) with no target parameter, never the method name; a renamed command keeping `ResetBar` was planted and FAILs (Log `note · planted · D8`); D8 text widened. The unauthenticated/console part is stated, not tested: VCF handles only chat from a connected Steam-authenticated user and the console does not run VCF commands, so there is no code path to plant (Design › Permissions)
- F2 · accepted · artifacts table (owner, location, retention/deletion, copies) under Design › Data; no new command — every artifact is either in git (D17/D18) or outside the repo by design and listed
- F3 · accepted · per-dependency failure table under Interfaces › External (ClearSavedBindings, SaveBindings, RevertTransform/Dismount, ECS bar layers, RestoreKept, unreachable live bar); the thrown-RestoreKept case is a D3 test. VCF/git/preflight/OCR are tooling, not runtime dependencies: their failure fails the check that uses them (`SCENARIO FAIL`, `FAIL no input`)
- F4 · accepted · D12 (c) now clears the Wolf set while in a LIVE wolf form via `.beelz admin testform wolf` (applies `AB_Shapeshift_Wolf_Buff`); baseline run recorded
- F5 · accepted · same change as F1
- F6 · accepted · secrets scan widened to .cs/.json/.toml (code, embedded ability data, manifest) and to the JSON `"key": "…"` form; Security states there is no credential source (no API, token or account), so no rotation applies; D20 selftest plants a JSON key
- F7 · accepted · Security names the identifiers (SteamID + character name, already on every `[Beelz]` line; `set=` adds only the label), where they live and that test artifacts stay in `%TEMP%`
- F8 · accepted · Design › UX › Accessibility: plain chat text, every state in words, under the 480-byte cap, no new UI element (D4)
- F9 · accepted · Failure & observability: no automated monitoring by decision (no telemetry); the owner's `preflight -LogCheck` after sessions and on tester logs is the detector, RECOVERY_GUIDE's ladder the response
- F10 · accepted · D1–D5 now run for real (Logic + tests built before approval, 213 tests pass) and each fault was planted once (Log); D12 ran on v0.137.3 and failed 19/30 (the real failing case); D19 is a command that FAILs today; D11/D16 record their real FAIL output (built in steps 5/6)
- F11 · accepted · +D20 `check_clearbar.py selftest`: every check against a good, a defect and an empty fixture (rollback/paths on a throwaway git repo); two faults planted in the checks and caught
- F12 · rejected · advisory, and 13.2 is answered at Performance: the bound is `BarMaxSlot` (0–8), the same constant resetbar's readback uses (bar-reset v0.137.0); this change adds no slot and excludes none
- F13 · rejected · 14.3 is answered at Rollout + D16: the commit range cannot exist before the build; the plan names the exact command that verifies it and its dry run records the real `FAIL no input` (the mounted-bar-reset precedent, Review 3)
- F14 · accepted · the Rollout path list now names `%TEMP%\beelz-logs-2026-10-04-clearbar\`, `%TEMP%\vrclient\` and the scratch copies, and says D18 checks the repo only — those outside paths are by-products listed for the record (artifacts table), not shipped files
- F15 · accepted · D19 is now `cmd: python Beelzebub/tools/check_clearbar.py backlog`, which parses the row whose first cell is the slug
- F16 · accepted · prose: the late re-read (`TickLate`) logs a `late-survivor` warning for a slot overridden after the readback, same as resetbar; a restored kept bind is already overridden at the readback, so it is never reported (Design › States)
- F17 · accepted · +test `ClearSet_fails_when_clearbar_all_keeps_a_bind_of_any_set` (universal, weapon, Mounted binds all survivors after `all`), planted; repeated clears are covered by bar-reset D10 (every pushed mod popped)
- F18 · accepted · S-2 replaced: the live wolf form is reached by `testform wolf` (spike quoted)

## Review 2 · 2026-10-04 · codex · plan commit d62be28 · plan 48326 B · 20 items · files 16 · 38f788c2f98d · prompt 421803059ce1
F1 [advisory] Blind rescore: Considered—1 Purpose; 3 Data; 4 Business rules; 5 Interfaces; 8 Minimal stretch; 9 Maximal stretch; 10 Security; 11 UX; 14 Rollout; 15 Out of scope; Gap—2, 6, 7, 12, 13; N/A—none.
Fix: Update the Coverage table to these statuses and retain the cited sections only for layers whose every probe is answered.

F2 [blocking] Probe 2.1 is unanswered as a control: D8/D9 detect targeting and unauthorized `FullReset` callers, but no evidence command fails if `clearbar` becomes reachable through unauthenticated, console, or RCON dispatch.
Fix: Add one command-backed fixture that exercises every claimed entry channel and fails unless `clearbar` accepts only an authenticated player acting on their own SteamID.

F3 [blocking] Probe 6.1 is unanswered: the plan names V Rising and VCF-mediated chat but omits the versions/contracts of VCF, BepInEx, and other external runtime packages, plus quota/cost applicability.
Fix: State the supported versions and sampled command/ECS contracts for every external dependency, explicitly recording “no quota/no incremental cost” where applicable.

F4 [blocking] Probe 7.3 is unanswered: interruption before `SaveBindings` is decided, but cancellation, stale readback, interruption after the save but before live cleanup, and what a subsequent correction invalidates are not.
Fix: Define the persisted and live state after interruption at each reset boundary, declare whether cancellation exists, and state what rerun or correction supersedes.

F5 [blocking] Probe 12.4 is unanswered because the generic `dotnet test --filter …BarResetTests` commands can pass when every new ClearSet test is absent, and `check_clearbar.py selftest` does not plant that empty-test condition.
Fix: Add a selftest or dedicated evidence command that fails when each required ClearSet test/control is removed, including when the filter discovers zero ClearSet cases, and record a real failing run.

F6 [blocking] Probe 13.2 is unanswered: “slots 0–8” gives a bound but not the case that established it, behavior at the bound, or which otherwise-valid case is excluded—especially since user-bindable slots stop at 7 while reset readback includes 8.
Fix: State why reset traversal includes 0–8, why binding excludes slot 8, what happens to out-of-range state, and the observed/specification source for that limit.

F7 [advisory] Scenario hunt—an authenticated command whose sender resolves SteamID `0` has no specified result; this belongs to 7.1’s error state and 2.3’s ownership path.
Fix: Add a fixture asserting refusal without touching key `0`, or document the existing framework guarantee that makes the state unreachable.

F8 [advisory] Scenario hunt—100× queued `clearbar` calls across different players is not covered by the single-player “spam” claim; this belongs to 9.1 volume and 13.1 throughput.
Fix: Add a multi-player stress measurement or qualify the stated latency and degradation behavior under queued main-thread load.

F9 [advisory] Scenario hunt—an unknown or future bind origin such as `weapon:Sword:legacy` is treated as a kept set and can be excluded from survivors; this belongs to 4.5’s computed “every origin” set and 9.2 misuse/corrupt-state behavior.
Fix: Make unknown origins fail closed as survivors, or enumerate the closed origin grammar and test rejection of anything outside it.

F10 [advisory] D12’s expected `32/32` is verifiable, but its failure clauses do not require the screenshot assertion itself to fail when R remains visibly populated; the screenshot is captured while the decisive assertion is log-only.
Fix: Add an OCR or manual evidence item with an explicit reviewer-visible pass condition for blank R, or remove the screenshot as claimed evidence.

EARLIER: all resolved
10/15 layers · 44/49 probes
VERDICT: REVISE
### Dispositions
- F1 · rejected · advisory blind rescore; the Gap layers it lists (2, 6, 7, 12, 13) are the ones F2–F6 name, each fixed below — the author's coverage stays the plan's claim and the next round rescores it
- F2 · accepted · +D22 `check_clearbar.py entry`: fails when anything in the plugin dispatches commands outside VCF's chat hook (`CommandRegistry.Handle`, a direct ClearBar/ResetBar call, an RCON/console type); planted. The VCF contract (dispatch only from the `ChatMessageSystem` prefix with the chat event's User) is cited in Interfaces › External
- F3 · accepted · runtime versions (BepInEx 6.0.0-be.733, VampireReferenceAssemblies 1.1.12-r99041-b2, VCF 0.10.*, server 1.1.15.0-r101082), the sampled VCF dispatch contract, and "no quota, no rate limit, no per-call cost" under Interfaces › External
- F4 · accepted · Design › States: no cancel exists; the saved and live state after a stop before SaveBindings, after it, and a stale readback; a later clearbar or resetbar supersedes a partial clear
- F5 · accepted · +D21 `check_clearbar.py tests`: the 19 named D1–D5 controls must exist as [Fact]/[Theory] methods (a filtered dotnet test passes with zero cases; this fails); planted; the 3.3 gating row runs it first
- F6 · accepted · Performance › Bounds: why the readback walks 0–8 (the engine buffer the player sees, bar-reset's bar-raw dumps), why binds stop at 7 (`IsValidSlot`), that a slot-8 override is always a survivor, and that out-of-range binds are refused at SetSlot
- F7 · accepted · D6 now requires a `steamId == 0` refusal that returns before FullReset (the old handler had none); the `wiring` check enforces it, planted twice
- F8 · accepted · Performance › Throughput: queued multi-player clears qualified (4–9 ms each on the main thread, the resetbar profile); no stress measurement claimed
- F9 · accepted · `BarSet.Keeps` fails closed: an origin outside the closed grammar (`BarSet.IsKnownOrigin`) is never kept; +6 test cases, planted (D2 text updated)
- F10 · accepted · D12 no longer claims the screenshot as evidence; the decisive check is the `[Beelz BAR] binds=0 rows=0 … other=0` readback, which a populated R fails

## Review 3 · 2026-10-04 · codex · plan commit 164b38c · plan 53835 B · 22 items · files 17 · 318aa34bfb7d · prompt bb0a7d914a63
F1 [blocking] Probe 2.1 lacks one evidence command: the table distributes actor enforcement across `entry`, `selfonly`, and `auth`, so no single command fails when any reachable actor gains unauthorized capability.
Fix: Add one aggregate command that runs all three controls against the real tree and fails if any actor or entry path violates the actor matrix.

F2 [blocking] Probe 3.3 is unanswered as a control: its cited `tests`/`dotnet test` commands do not verify artifact ownership, retention, deletion, or copies, and the `%TEMP%\authplant`, `%TEMP%\BarSet.bak`, ignored `dist/`, logs, screenshots, and deployed DLL cannot make either command fail.
Fix: Add every intermediate to the artifact table and provide one data-lifecycle command that fails for an undeclared producer, missing lifecycle fields, or an undeleted temporary artifact.

F3 [blocking] Probe 6.2 does not decide slow or garbage behaviour for each collaborator: the plan covers throws and unreachable state, but not a slow synchronous ECS/save operation or malformed-but-readable dependency output.
Fix: State the timeout/no-timeout and fail-closed policy for slow and malformed results, with one command whose tests fail when that policy is removed.

F4 [blocking] Probe 10.1 lacks one complete authorization command: `check_bar_reset.py auth` checks `FullReset`/`ReadBar` callers but does not detect the direct command-dispatch channels covered separately by D22.
Fix: Provide one real-tree authorization command that combines caller authorization, self-only targeting, and all direct/indirect entry-channel checks.

F5 [blocking] Probe 12.4 is a Gap: several checks—including D12–D15 and D17—lack recorded failing, silent, and empty inputs, while D1–D5 were marked `n/a` before implementation rather than run once against the real check before approval.
Fix: Record the required failing/silent/empty real-check runs for every introduced check, including source-backed thresholds and exact outputs, before approval.

F6 [blocking] Probe 14.4 is not enforced by its evidence command: `check_clearbar.py paths` derives tracked and non-ignored untracked repository changes, so it cannot fail when declarations for `dist/`, deployed DLLs, logs, `%TEMP%` outputs, review products, or scratch directories are removed.
Fix: Make one paths-walk command compare the declared manifest with every repository, ignored/generated, deployment, review, plan-store, and temporary path walked by the Build plan.

F7 [advisory] The actual shapeshift-wheel path is untested: D12(c) uses `admin testform` through a transform record, so a normal Wolf-form buff could be removed by `DestroyOverrideSources` without incrementing `RevertTransform`, producing no “transform ended” feedback under probes 7.1 and 11.2.
Fix: Add a wheel-form-equivalent fixture or manual run and ensure ending that form produces the promised feedback.

F8 [advisory] Probe 9.1 estimates 100 clears as 0.4–0.9 seconds but assumes VCF spreads them across frames; the quoted plan provides no evidence for that scheduling behaviour or acceptable stall budget.
Fix: Treat the estimate as a worst-case main-thread stall unless measured, and record the acceptable queue/stall behaviour.

EARLIER: all resolved
9/15 layers · 43/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · +D23 `check_clearbar.py actors`: entry + selfonly + `check_bar_reset.py auth` in one command on the real tree; planted (renamed self-only command) and empty-tree runs recorded; the 2.1 gating row now names it
- F2 · accepted · the artifact table gains the plant scratch copies and `dist/`; +D24 `check_clearbar.py scratch` fails while any scratch copy is left; the lifecycle of the rest (logs, shots, DLL) is policy in the table — files outside the repo that the OS or the next build rewrite, which no repo command owns
- F3 · accepted · Interfaces › External: no timeout (one synchronous main-thread call; a slow step stalls the frame and is reported by `ms=`/`slow=1`, bar-reset D16 test); malformed-but-readable output fails closed (Unreadable never clean, unknown origin a survivor D2, untrusted counts ERR)
- F4 · accepted · same as F1 (D23 is the 10.1 row)
- F5 · rejected · advisory by rule — 12.4 is answered at Failure & observability and the Log: D1–D5 were re-run as real tests after the early `n/a` notes (Log `dry-run · D1`…`D5` → Passed, each planted); D12 ran on v0.137.3 and FAILed 19/30; D13 ran 14/14 and 6/6; D14 and D15 ran preflight; D17 ran git status; the reviewer read the superseded `n/a` lines, which stay because the Log is append-only
- F6 · rejected · 14.4 is answered at Rollout: D18 compares every repo path changed since the recon commit with the declaration; `dist/` is gitignored build output and the rest (deployed DLL, logs, %TEMP%) are not paths the change ships — they are listed for the record, owned in the artifact table, and D24 covers the only scratch the build creates
- F7 · accepted · Design › States records the limit: a wheel-entered vanilla form ends through DestroyOverrideSources with no form-ended suffix (same as resetbar today); D12 (c) covers the transform-record path; a form-ended reply is out of this slice
- F8 · accepted · Performance › Throughput: the estimate is now a worst-case single-frame stall, accepted for a self-only, typed command

## Review 4 · 2026-10-04 · human · plan commit 771ef1c · plan 57330 B · 24 items
Owner (Chaos), approving the proposed answers to the four rubric questions on `docs/dod/clearbar-fullreset.review.html` (plan-mode decision, 2026-10-04, option A):
1. Coverage — every Considered layer answers all of its probes, no N/A; the reviewer's 43/49 gap is exactly F5/F6 of Review 3 and the layers they touch, both rejected with reasons.
2. Contest — every pointer answers its probe; S-1 and S-2 were validated by spike; no decision-required assumption.
3. Hunt — one deliberate, recorded limit: a wheel-entered vanilla form ends with no form-ended reply suffix (same as resetbar today), outside this slice.
4. Test the tests — every item has evidence that fails when its control is removed; every gating probe has one command; every build step cites its items; the rejections of Review 3 F5 and F6 stand.
15/15 layers · 49/49 probes
VERDICT: READY

## Review 5 · 2026-10-04 · human · plan commit dfbbff1 · plan 83857 B · 24 items · files 18 · 1effb4aeaac3 · prompt 9f7143195b4c · scope A1,A2,A3,A4,A5,A6
Owner (Chaos), approving the proposed answers to the four rubric questions for the amendments A1-A6 on `docs/dod/clearbar-fullreset.review.html` (plan-mode decision, 2026-10-04, option A):
1. Coverage — unchanged, 15/15 layers and 49/49 probes; each control an amendment added re-answers 4.2, 4.5 and 12.4 in its amendment line.
2. Contest — every amendment's cause is evidenced: /code-review findings 1-3 (A4, A1, A2), Codex audit round 1 F2 (A3), and the A5 diagnostic line `tag=False issued=True` that proved A6.
3. Hunt — two deliberate, recorded limits: a wheel-entered vanilla form ends with no form-ended reply suffix (Review 4), and the tag-only twins `IsLive` (spellbook repair) and the orphan sweep's source skip stay unchanged (reasons in A6).
4. Test the tests — every amendment has a failing case run once against the real code or a copy of it (plan Log `planted` notes for A1-A4; the A5 in-game run, 30/32, is A6's failing case). A4 stays classified `defect`: D5 specified the `set=` field; the old session tool was not updated.
15/15 layers · 49/49 probes
VERDICT: READY
