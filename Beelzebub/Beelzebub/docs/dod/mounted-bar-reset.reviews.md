# Reviews — mounted-bar-reset

## Review 1 · 2026-10-03 · codex · plan uncommitted · plan 29405 B · 10 items · files 8 · 5b844399aa16 · prompt 76482c9f3a57
F1 [blocking] Probe 12.4 is unanswered: D1/D2/D4/D5/D6 were never run against the real checks, while D3/D8/D9/D10 lack the required failing, silent, and empty inputs; the recorded `n/a` dry runs explicitly make this probe a Gap.  
Fix: run every introduced check before approval, record its real failure output, silent valid case, and non-passing empty-input output, with one named evidence command per gating probe.

F2 [blocking] Probe 2.1 has no evidence command that fails when a player can reach an admin path: D9 exercises an authorized admin, and the prose reference to an existing `auth` check is not a D-item or recorded run.  
Fix: add a D-item whose command is `python Beelzebub/tools/check_bar_reset.py auth`, record its passing and planted-failure outputs, and cover player, admin, unauthenticated/console, and service-triggered paths.

F3 [blocking] Probe 2.3 is unanswered because SteamID ownership is stated, but replacement mid-reset, disconnect/reconnect ownership, and an admin target changing state during the operation have no defined outcome.  
Fix: decide that the reset remains bound to the captured SteamID and specify whether disconnect, reconnect, or entity replacement aborts, continues saved-state-only, or retries.

F4 [blocking] Probe 3.3 is unanswered for all produced artifacts: `state.json` ownership is named, but retention/deletion of migration logs, temporary save files, test fixtures, audit output, staged `dist/`, and backups under `%TEMP%` is absent or merely described as “rotated.”  
Fix: enumerate each persistent/by-product artifact with owner, retention period, deletion mechanism, single-instance rule, and a command that verifies the policy.

F5 [blocking] Probe 4.5 is unanswered because “every mount buff” relies on a prefab glob and name rule without deciding whether the enumeration includes untracked/generated files or files created during the build, and D6 tests only selected names rather than the computed set.  
Fix: define the exact walk over tracked plus generated prefab records, state its exclusions and known misses, and add a command that compares every discovered mount-buff record with `IsMountBuff`.

F6 [blocking] Probe 4.4 lacks one evidence command for the complete precedence control: D2 can test the 3→5 collision, but “saddle bind beats horse kit” and “owner decides exceptions” are supported only by prose/manual observation.  
Fix: name one command that fails when either precedence changes, or split the probe into command-backed D-items covering migration collision and saddle-versus-native priority.

F7 [blocking] Probe 6.2 is unanswered for several dependencies: failure behavior is specified only for mount-buff destruction, not malformed/missing prefab data, persistence save failure after migration, unavailable slot-resolution systems, testmount failure, or the Codex post-audit collaborator.  
Fix: decide the fail-closed/degraded behavior for each dependency and add an evidence command that removes or corrupts each dependency and fails if that behavior is absent.

F8 [blocking] Probe 10.1 has no command that fails when authorization is removed from every direct and indirect path; D1/D9 do not test migration, admin reset-loadouts, purge, testmount, bar, BCH/API, or internal invocation boundaries.  
Fix: add a security D-item with a single authorization-check command covering all entry points and record a planted missing-guard failure.

F9 [blocking] Probe 10.3 has no evidence command for the claimed no-secrets boundary; D7 is an API-version documentation check and cannot detect credentials added to state, replies, or `[Beelz MOUNT/RESET]` logs.  
Fix: add a secret-scanning/log-fixture command that fails when credential-shaped values appear in persisted state, replies, or logs, and state the applicable rotation policy or explicitly prove no credential exists.

F10 [blocking] Probe 12.4 also lacks a gating-control command matrix: no single command is named for 3.3, 4.4, 6.2, 10.1, 10.3, 12.4, 14.3, or 14.4, so removing those controls would not necessarily fail approval.  
Fix: add a table mapping each gating probe to exactly one command, its planted defect, expected failure text, valid silent input, and empty-input output.

F11 [blocking] Probe 14.3 is supported only by rollback prose: neither D10 nor preflight fails when rollback steps, post-migration consequences, or the first-to-last build commit range are absent.  
Fix: add a command-backed rollback D-item that validates recorded commit endpoints and performs a dry-run showing that v0.137.2 can load the migrated state safely.

F12 [blocking] Probe 14.4 lists paths but has no command that reconstructs them from all Build-plan steps and fails when a shipped, generated, ignored, review, plan-store, deployment, backup, or live-state path is omitted.  
Fix: add a path-manifest check command covering every step and record a planted omitted-path failure.

F13 [blocking] D1 is not verifiable by its stated test because its `fails when` cases cover slot validity and text but not the requirement that five consumers use `MountedSlots` and contain no independent slot literals; a stranger could pass the test while leaving duplicated literals.  
Fix: add a source-contract test or command that fails when any named consumer does not reference `MountedSlots` or contains its own mounted-slot set.

F14 [blocking] D3 is not verifiable by its single manual fixture because it checks only the moved case, not the slot-5 collision/drop case, one log per changed player, one save request total, unchanged players, or invocation before `LoadFormSlotsSnapshot`.  
Fix: add automated load-path tests for moved, dropped, unchanged, multi-player, call ordering, and exactly-one save request, retaining the live manual run as integration evidence.

F15 [blocking] D6 is not verifiable by `OrphanSourcesTests.cs`: those unit cases can pass while `DestroyOwnedAbilitySlotOrphans` ignores `OrphanSources.Keep` and still destroys mount buffs.  
Fix: add an integration/source-contract test that removes the `Keep` call and demonstrably fails.

F16 [blocking] D7 is not verifiable by preflight as described: its failure contract checks only banner/version parity and missing handoff, not the v0.137.3 block, `5/6/7`, `api>=34` guidance, reset text, version-table row, or removal of `3/6/7`.  
Fix: extend preflight to assert every D7 contract statement and record a planted stale-handoff failure.

F17 [blocking] D10 is not verifiable by preflight as described because no stated check verifies the BACKLOG row is `DONE`, both changelog entries contain the release, the README status specifically names v0.137.3, or all required release surfaces exist.  
Fix: extend preflight with explicit assertions for every D10 surface and record its complete output.

F18 [blocking] Probe 7.3 is unanswered: interruption during migration is covered, but cancel/undo, stale mounted detection, correction of a bind while riding, and what that correction invalidates are not decided.  
Fix: state whether bind corrections immediately restore/reinject the saddle snapshot, require remount, or are rejected during a reset, and define cancellation/undo behavior.

F19 [blocking] Probe 15.2 is unanswered for `mounted-vampire-thrust`: the plan calls it deferred to a backlog row that is absent from the quoted backlog, so the work has no existing slug or issue.  
Fix: either add the named backlog item before approval or classify per-horse saddle behavior as explicitly excluded rather than deferred.

F20 [advisory] The reversible S-2 assumption is not cheap to reverse: dropping slot 7 changes BCH guidance, migration policy, tests, documentation, API expectations, and may discard existing slot-7 binds—not merely “one constant.”  
Fix: replace its fallback with the full compatibility/migration action or validate vampire-horse T before approval.

F21 [advisory] Minimal-stretch scenario: a player has malformed `"Mounted"` data or an out-of-range slot in `state.json`; the plan does not state whether load skips the entry, rejects the player record, or aborts migration (layers 3 and 7, probes 3.1 and 7.1).  
Fix: add the chosen invalid-persistence behavior and a fixture for it.

F22 [advisory] Concurrent scenario: the player remounts after the planner’s mounted snapshot but before `Dismount`; the plan calls this a “normal remount” without deciding whether the newly created mount buff is removed or survives while the foot-bar reset proceeds (layer 7, probe 7.2).  
Fix: define execution-time revalidation and test the remount race.

F23 [advisory] Unauthorized scenario: a non-admin invokes each admin alias/API path, but the only planned observation is VCF’s generic deny and no Beelzebub audit entry, leaving support unable to distinguish attempted misuse from an absent command (layers 2 and 12, probes 2.2 and 12.2).  
Fix: decide whether standard VCF denial is sufficient or add a privacy-safe authorization-denial audit event.

F24 [advisory] Maximal-stretch scenario: thousands of changed players cause one log line per player and a full-state rewrite, but the plan supplies no measured load-time or log-volume budget despite claiming acceptable O(players) behavior (layer 13, probe 13.1).  
Fix: record a representative large-state run and its latency/log-size spread.

5/15 layers · 32/49 probes
VERDICT: REVISE
### Dispositions
- F1 · rejected · advisory by rule — 12.4 is answered at Failure & observability (gating-control table): the new tests are created by build steps 2, 4, 5, which run each failing input by planting its fault once in that step; the dry-run notes record `n/a` with the reason, which the plan grammar allows for a check that cannot run yet; the existing commands (preflight, auth, git status) were dry-run
- F2 · accepted · +D11 `check_bar_reset.py auth` (dry-run recorded); 2.1 now maps to D11
- F3 · accepted · Design › Permissions + States: a reset is synchronous on the main thread in one command (6 ms), the target is resolved when the command runs, a disconnect before it takes the bar-reset offline path
- F4 · accepted · Design › Data now names each by-product (LogOutput lines, %TEMP% log backups owned by the owner, audit and plan files in git) with its owner and lifetime; D13 checks every written path is committed
- F5 · accepted · Business rules 6 states the walk (listing `AB_Interact_Mount_Owner_Buff*` in the static prefab export, 6 files, nothing generated by the build) and the known miss with how it shows
- F6 · rejected · advisory by rule — 4.4 is answered at Business rules 4: the migration collision is D2's test command; saddle-over-horse-kit is existing priority-100 behaviour shown by D8; the owner is named as the decider
- F7 · accepted · Interfaces › External now covers persistence save failure (dirty flag + heartbeat retry, idempotent re-migration), testmount failure, and the Codex collaborator; malformed data under Use cases › Minimal stretch
- F8 · accepted · 10.1 now maps to D11 (`check_bar_reset.py auth` covers every beelz admin command and every FullReset/ReadBar caller)
- F9 · accepted · Security states no credential is read, stored or logged; 10.3 maps to D1 and D5, whose exact-text tests fail if any extra content enters the new outputs
- F10 · accepted · gating-control table (probe → command → fails when) added under Failure & observability
- F11 · accepted · +D12 audit `Rollback range:`; Rollout states why v0.137.2 loads migrated state safely (its BuildMountedBar filters slot 5)
- F12 · accepted · +D13 `git status --porcelain` after the release commit (fails on any uncommitted written path; empty output is a FAIL); dry-run recorded with a real failing case
- F13 · accepted · D1 narrowed to the pure members and the exact reject text; the consumer wiring is verified in game by D8 (slot 3 rejected with the D1 text, slot 5 accepted and shown on R)
- F14 · rejected · advisory by rule — 3.3/7.3 are answered: the moved/dropped/unchanged/idempotent cases are D2's test; D3's manual is the integration evidence for the load call and the save
- F15 · accepted · D6 (OrphanSources) removed: after a successful Dismount the sweep never sees a mount buff, so the keep had no observable effect and would have changed rebuildslots; Business rules 3 and Out of scope updated
- F16 · accepted · old D7 split: D6 = ApiVersion/banner via preflight; D7 = file check on the handoff (`| 34 | 0.137.3 |`), with the block contents stated
- F17 · accepted · D10 limited to what preflight checks (versions, CHANGELOG entry and cap, README status, root README); +D14 file check for the BACKLOG row
- F18 · accepted · Design › States: correction while riding (saved, shown on the next mount), no undo of a reset except re-granting, a dismount undone by remounting
- F19 · accepted · per-horse saddle slots moved to excluded, with the condition that would open a new backlog item
- F20 · accepted · S-2 fallback now states the full cost (allowed set, migration, tests, text, handoff, ApiVersion)
- F21 · accepted · Use cases › Minimal stretch: non-int slot keys are skipped by the existing parser; out-of-set int slots are left by Migrate and filtered by BuildMountedBar
- F22 · accepted · Design › States: the reset is one synchronous command on the main thread, so no remount interleaves
- F23 · rejected · VCF's standard deny is the project-wide behaviour for every admin command; no new audit event in this slice
- F24 · rejected · tester servers hold tens of players; the migration is one dictionary pass per player and one save, no budget needed at that scale (Use cases › Maximal stretch)

## Review 2 · 2026-10-03 · codex · plan uncommitted · plan 33242 B · 14 items · files 10 · 92b3e1507d44 · prompt 2c6a1a41719e
F1 [blocking] Probe 12.4 is unanswered: D1, D2, D4, and D5 explicitly record `n/a` instead of an observed failing run, while the rubric says an unrun failing case is a Gap.
Fix: Run each planted fault against the real check before approval and record the command, failing input, actual output, silent input, and empty-input output.

F2 [blocking] Probe 3.3 lacks the required single evidence command: D3 is a manual server procedure, so no command automatically fails when migration, persistence, retention, or deletion behavior is absent.
Fix: Add one executable migration fixture command that loads old state, verifies the rewritten and persisted state, reruns for idempotency, and fails on retained slot 3 or overwritten slot 5.

F3 [blocking] Probe 10.3 is unsupported by its named command: `MountedSlotsTests|BarResetReplyTests` checks fixed reply text and cannot fail if a credential is introduced, stored, or logged.
Fix: Name the project’s credential sources and add one evidence command that fails when those values or prohibited secret patterns reach state, migration logs, reset logs, or replies.

F4 [blocking] Probe 2.1 is incomplete: the plan decides player and admin access but does not decide whether unauthenticated callers, server-console callers, service accounts, or indirect API/event paths can reach the changed operations; D11 only checks command annotations and selected reset callers.
Fix: State the permission for every reachable actor/path and extend the auth command to fail when any changed mutation becomes reachable outside the self-only or `adminOnly` surfaces.

F5 [blocking] Probe 6.1 is unanswered: the V Rising/ECS contract has no supported game version, compatibility range, or actionable behavior when the six-record prefab sample no longer matches the running game.
Fix: State the supported V Rising build and make one compatibility check fail when live mount records differ from the sampled slot contract.

F6 [blocking] Probe 9.1 is unanswered: “tester servers hold tens of players, not thousands” dismisses rather than decides the required 100× case, with no bound or degradation behavior for thousands of migrated players.
Fix: State the supported player bound and what happens at 100× expected volume, including a measured or assumed load-time budget and fallback.

F7 [blocking] Probe 14.3 is not verifiable by D12: checking only for the literal `Rollback range:` cannot establish that first and last commits are present or that the post-migration rollback consequence is recorded, so a stranger could pass the file evidence with an empty label.
Fix: Replace D12 with a command that parses two valid commit IDs, verifies their ancestry/range, and requires the slot-5 post-migration consequence and recovery instruction.

F8 [blocking] Probe 5.3 is not verifiable by D7: its file evidence checks only `| 34 | 0.137.3 |`, so the item passes without the required banner, `api>=34`, `5/6/7`, dismount wording, or corrected v0.101.0 contract.
Fix: Use one documentation-contract command that asserts every required token and compares the handoff version to `ApiCommands.ApiVersion`.

F9 [advisory] The maximal-stretch unauthorized scenario is not exercised: an old BCH running as a normal player repeatedly submits mounted slot 3 after upgrade, but no acceptance item verifies the rejection remains self-only, rate-safe, and non-mutating (9.2).
Fix: Add an old-client compatibility fixture or manual run confirming repeated rejected grants neither mutate state nor emit privileged information.

F10 [advisory] S-2 is not cheaply reversible: the quoted prefab evidence says vampire mounts place thrust on slot 7 before Empty, while D8 tests only the test horse; discovering that T still matters requires another API release and potentially destructive migration of existing slot-7 binds (4.1).
Fix: Test D8 on at least one vampire-horse variant before release, or exclude slot 7 until its client-visible precedence is observed.

F11 [advisory] The concurrent scenario is overstated: main-thread execution prevents instruction-level interleaving, but it does not prove the character, equip buff, or mount entity remains valid across ECS structural changes triggered during `Dismount` and subsequent reset steps (7.2).
Fix: Add a fixture where the mount/equip entity disappears during Dismount and assert the reset reports not-clean without corrupting saved binds.

EARLIER: all resolved
8/15 layers · 42/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · the tests now exist: D1, D2, D4, D5 dry-run as real test runs and each fault was planted once and failed its test (Log 2026-10-04 `note · planted`); D7, D12, D15 record their real FAIL output as the dry run
- F2 · accepted · 3.3's single command is D2's `dotnet test ... MountedSlotsTests` (retained slot 3, overwritten slot 5, non-idempotent rerun each fail it); no new artifact is produced besides state.json, whose lifetime is stated in Design › Data; 3.3 also maps to D13
- F3 · accepted · +D16 `check_mounted_bar.py secrets` scans every plugin C# source (the only writer of state, replies and logs) for credential-shaped literals; planted and failed
- F4 · accepted · Design › Permissions now lists every actor/path (player, admin, unauthenticated, console/RCON, service/patch/API/timer); +D15 `check_mounted_bar.py callers` fails when the migration gains a caller outside the load path; D11 keeps the reset callers
- F5 · accepted · Interfaces › External names the supported build (VRisingServer v1.1.15.0-r101082, Steam build 25169871) and D8 now opens with a live `bar-raw` check of slots 5/6/7 against rule 1, stopping the slice if it differs
- F6 · accepted · Maximal stretch states the bound (10,000 players < 200 ms, measured by a new D2 case) and the above-bound behaviour (linear, completes, one-time log lines)
- F7 · accepted · D12 is now `check_mounted_bar.py rollback`: parses both ids, checks they are commits and ancestry, and requires the `inert slot-5 bind` consequence
- F8 · accepted · D7 is now `check_mounted_bar.py handoff`: every contract token plus banner == ApiVersion == 34
- F9 · rejected · a rejected `form-grant` returns before any write (existing code path, unchanged), and the reply is the same fixed D1 text for every caller; VCF rate-limits nothing project-wide and this slice adds no exposure
- F10 · rejected · slot 7 has been a saddle slot since v0.101.0 (the {3,6,7} set) with no tester report of a lost vampire-horse thrust; the owner approved {5,6,7} on 2026-10-03; S-2's fallback states the full cost if a vampire-horse D8 ever shows otherwise
- F11 · rejected · SaveBindings runs before Dismount, so saved binds cannot be corrupted by anything Dismount does; a Dismount that throws is already tested to make the run not clean (D4); the following steps re-read entities each step (bar-reset D12)

## Review 3 · 2026-10-04 · codex · plan uncommitted · plan 40314 B · 16 items · files 10 · 92b3e1507d44 · prompt ea91b1f02e12
F1 `blocking` — Probe 2.1/10.1 remains unanswered: D11’s `auth` command checks reset/admin callers but cannot fail if `form-grant mounted` gains a target-player path or otherwise stops being caller-owned.
Fix: Extend `check_bar_reset.py auth` or add one named command that fails unless every player mutation remains self-only and every privileged mounted/reset path remains `adminOnly`.

F2 `blocking` — Probe 3.3 remains unanswered: the named `MountedSlotsTests` command verifies migration semantics, not where every produced artifact is stored, retained, or deleted; removing the stated log/temp/audit retention policy would not fail it.
Fix: Add a file/check item that inventories state, logs, temp backups, audit/review records, build output, and deployed DLL with owner, retention, and deletion disposition, and fails when an artifact or field is absent.

F3 `blocking` — Probe 4.4 remains unanswered: D2 fails on the slot-5 collision precedence but no evidence command fails if “the owner decides any exception” is removed or changed.
Fix: Make a named policy check assert both the slot-5-wins rule and the owner-only exception authority.

F4 `blocking` — Probe 6.2 remains unanswered: `BarResetTests` covers a throwing `Dismount`, but not slow/unavailable/garbage persistence, unreadable ECS data, stale prefab data, failed test-mount tooling, or unavailable review tooling.
Fix: State the behavior for each dependency mode and map the runtime-critical cases to a command that fails when the corresponding fallback or fail-closed behavior is removed.

F5 `blocking` — Probe 12.4 remains unanswered: several checks have only “n/a until built” or ordinary pre-build failure output, and the plan does not specify failing, silent, and empty inputs for every introduced check; the blanket “each check’s planted fault” row is not an executable evidence command.
Fix: Give every D-item check its concrete failing input, silent input, empty input/output, source for thresholds, real-check dry-run output, and one reproducible command that executes those fixtures.

F6 `blocking` — Probe 14.3 remains unanswered: `check_mounted_bar.py rollback` validates a commit range and consequence but can still pass if the exact revert, rebuild, and redeploy instructions are absent.
Fix: Make the rollback check require the exact `git revert <range>`, rebuild, and redeploy procedure plus the post-migration slot-5 consequence.

F7 `blocking` — Probe 14.4 remains unanswered: `git status --porcelain` proves only that writes are committed; an omitted but committed generated/review/release path passes, so it cannot enforce the claimed complete path walk.
Fix: Add a manifest check that derives or enumerates every path touched by each Build-plan step and fails on either an undeclared touched path or a declared path absent from the walk.

F8 `blocking` — D13 is unverifiable by a stranger: it requires two unrelated owner files to appear dirty and treats a correctly clean checkout as failure, without evidence that those paths must exist in every verification environment.
Fix: Verify that all slice-owned paths are clean while explicitly ignoring the two named owner paths if present; do not require unrelated dirtiness.

F9 `advisory` — Scenario hunt, minimal stretch (7.1): an online mounted player whose equip buff is temporarily absent is `liveReady=false`, so the plan skips `Dismount`, clears saved binds, and leaves the rider mounted without establishing what the user sees after the next bar resolution.
Fix: Add an amendment describing the expected reply and recovery behavior for mounted-but-not-liveReady state.

F10 `advisory` — Scenario hunt, concurrent path (7.2): the “main thread means no interleave” claim does not cover deferred ECS destruction; the mount buff may remain observable with `DestroyTag` after `Dismount`, causing the stipulated “still live” check to throw despite a successful queued dismount.
Fix: Define “dismounted” in terms of `DestroyTag`/next-tick state and add the corresponding fixture or live observation.

F11 `advisory` — Scenario hunt, unauthorized path (2.2): the plan says VCF denies non-admins but D11 only checks annotations, so it never observes the actual non-admin response or confirms that `testmount`, `bar`, `purge`, and `reset-loadouts` produce no Beelzebub side effect.
Fix: Record one non-admin live check covering the denial response and unchanged state.

F12 `advisory` — S-2 is not cheaply reversible: dropping slot 7 requires another saved-data policy, tests, handoff change, API bump, build, deployment, and release.
Fix: Mark S-2 `decision-required` until D8 validates the supported horse set, or mark it validated only after that run; do not classify it as reversible.

Scoring: 1 Considered (`Purpose & typical use`); 2 Gap (2.1); 3 Gap (3.3); 4 Gap (4.4); 5 Considered (`Interfaces › Internal`); 6 Gap (6.2); 7 Considered (`Design › States`); 8 Considered (`Use cases › Minimal stretch`); 9 Considered (`Use cases › Maximal stretch`); 10 Gap (10.1); 11 Considered (`Design › UX`); 12 Gap (12.4); 13 Considered (`Performance`); 14 Gap (14.3, 14.4); 15 Considered (`Out of scope`).

EARLIER: all resolved
8/15 layers · 41/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · +D17 `check_mounted_bar.py selfonly`: form-grant and resetbar take no target-like parameter and are not adminOnly; both faults planted and failed (2.1 now D11 D15 D17)
- F2 · rejected · advisory by rule — 3.3 is answered with a decision at Design › Data (state.json owned by the plugin, kept until reset/purge; LogOutput overwritten on next start; %TEMP% backups the owner's; audit/plan/reviews in git); a retention sentence is a policy, not a control a command can remove; D2 is the control on the data the slice changes and D13 keeps every written file committed
- F3 · rejected · advisory by rule — 4.4 is answered: the collision rule is D2's command; "the owner decides any exception" is a governance statement with no code path, so no command can fail on it
- F4 · rejected · advisory by rule — 6.2 is answered per dependency at Interfaces › External (ECS failure → Dismount:ERR, not clean, D4; persistence → dirty flag + heartbeat retry, idempotent re-migrate, D2; prefab drift → D8's bar-raw step stops the slice; testmount → rerun on an owned horse; Codex → rerun via stdin); the runtime-critical modes map to D2 and D4
- F5 · rejected · every introduced check has now been dry-run and had its fault planted (Log 2026-10-04: D1 D2 D4 D5 D15 D16 D17 D18 planted and failed; D7 D12 D13 D15 record their real FAIL before build); each check's empty input prints `FAIL no input` by construction; asking for a fixture harness on top is more rigour on an answered probe
- F6 · accepted · D12's command now also requires `git revert`, `dotnet build Beelzebub/Beelzebub.sln -c Release` and `taskkill /PID`; Rollout states the exact revert/stop/build/start sequence
- F7 · accepted · +D18 `check_mounted_bar.py paths`: every path changed since 0a135f5 (tracked + untracked) must be in the Rollout path list; planted and failed (14.4 now D13 D18)
- F8 · accepted · D13 reworded: the two owner files are ignored whether present or not; any other listed path fails; outside a repository git exits 128, a FAIL
- F9 · rejected · an online player with no equip buff is the existing bar-reset liveReady=false path (bar-reset plan D11: saved state only, reply says so); a rider always holds a weapon equip buff, and D9 exercises the live case
- F10 · rejected · `TransformBuffService.SafeDestroyBuff` is the same call the orphan sweep already uses on this buff (logged `dismounted (control buff gone)` in the 2026-10-03 spike, same frame); Dismount's "still live" check counts buffs without DestroyTag, which the builder implements in step 4 — D9's live run observes it
- F11 · rejected · VCF's adminOnly gate runs before any Beelzebub handler code, so a denied call has no Beelzebub side effect by construction; D11 asserts the gate on every admin command
- F12 · rejected · S-2's fallback states its full cost (round-2 F20 disposition); slot 7 has been a saddle slot since v0.101.0 with no report, and the owner chose {5,6,7} knowingly on 2026-10-03

## Review 4 · 2026-10-04 · human · plan uncommitted · plan 41608 B · 18 items
Owner (Chaos), answering the four rubric questions on `docs/dod/mounted-bar-reset.review.html`:
1. Coverage — accepted as is: the disputed probes 3.3, 4.4, 6.2, 12.4 carry written decisions and every check was fault-planted.
2. Contest — S-2 stays `reversible`; the slot set {5,6,7} approved 2026-10-03 stands; a vampire-horse thrust on T would be a new backlog item.
3. Hunt — no unhandled scenario the owner can name.
4. Test the tests — every item verifiable by its evidence; every gating probe has one failing command; every build step cites its items.
15/15 layers · 49/49 probes
VERDICT: READY
