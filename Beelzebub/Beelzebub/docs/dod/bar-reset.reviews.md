# Reviews — bar-reset

## Review 1 · 2026-09-30 · codex · plan uncommitted · plan 31191 B · 23 items · files 3 · 3ff0e2b1ef9c · prompt 2c0b8e00bb6c

F1 [blocking] `2.1` is unanswered as a control: D15’s `rg` succeeds when one declaration lacks `adminOnly: true`, and absence of matches can also masquerade as success in the stated “every match” inspection.
Fix: add an authorization test covering player, admin, console, and unauthenticated callers, evidenced by `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~ResetCommandAuthorizationTests`, which fails when any actor gains an unauthorized operation.

F2 [blocking] `3.1` is a Gap: the plan never decides accepted player-name length/ambiguity, missing or malformed `CONFIRM`, excess arguments, unavailable character/entity, or the exact invalid-input response.
Fix: specify each command’s input grammar, limits, lookup semantics, and rejection response, then map them to command-parser tests.

F3 [blocking] `3.3` is unanswered for all produced artifacts: retention and deletion are omitted for `Audit(...)`, `[Beelz BAR]` dumps, `%TEMP%` log copies, the audit record, generated review files, the new config key, and the replaced server DLL.
Fix: state owner, location, retention, deletion, and single-copy/multiple-copy status for each artifact, with `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetPersistenceTests` failing when reset state or prohibited diagnostic content persists.

F4 [blocking] `4.5` is a Gap: “every slot” uses the full buffer for reset/readback but D6 exposes only slots 0–8, while “every reset command,” “every changed path,” and “each check” rely on hand-maintained sets with no decision for newly created, untracked, ignored, generated, or differently declared members.
Fix: define the authoritative computation for each “every X” set, its known exclusions, and how untracked/generated inputs are included; test slot enumeration and derive command/path sets from source plus `git status --porcelain=v1 --untracked-files=all`.

F5 [blocking] `6.1` is a Gap: the V Rising build and VCF version are unspecified, quotas/cost are not decided, and contracts are sampled only for expected GroupGuid dump records rather than every formatter record type the parser may receive.
Fix: pin supported engine/VCF versions, state quota/cost applicability, enumerate sampled dump record classes, and define behavior for each unsupported class.

F6 [blocking] `6.2` is unanswered for VCF and BCH and only partially answered for the engine: command dispatch failure, unavailable target lookup, malformed/partial API data, logging failure, and collaborator failure have no decided behavior.
Fix: define slow/down/garbage behavior for every dependency and add `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetDependencyFailureTests`, which fails when a dependency failure escapes, aborts later reset steps, or reports a clean bar.

F7 [blocking] `8.2` is a Gap: D5 concerns form auto-fill, not one-time-use leakage; the plan does not decide whether one reset leaves diagnostic dumps, audit records, config materialization, temporary log copies, or future prompts.
Fix: state exactly what persists or nags after a single reset and the cleanup/retention behavior for each remnant.

F8 [blocking] `9.2` and `10.2` are Gaps: hostile player names or engine dump text containing newlines, control codes, oversized fields, regex-hostile content, or forged `[Beelz RESET]` fragments can reach chat/log output without a sanitization decision.
Fix: define length bounds and escaping for chat/log fields plus parser complexity limits, and add hostile-input fixtures proving no log injection, chat markup injection, or unbounded parse occurs.

F9 [blocking] `10.1` is not enforced on every path: the plan asserts four entry points while naming at least `resetbar`, `reset-loadouts`, `purge`, `bar`, and `rebuildbar`, and D13/D15 can miss aliases, indirect service calls, alternate helper names, or multiline attributes.
Fix: enumerate every callable path and add `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~ResetAuthorizationPathTests`, which fails when any admin-targeting path lacks an authorization check or any player path accepts another target.

F10 [blocking] `10.3` is prose-only: D16 claims secrets are never logged, but its manual evidence checks only one summary line and `survivors=none`, so removing redaction would not fail.
Fix: add a logger-capture test with planted token/password values, evidenced by `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetSecretLoggingTests`, which fails if credentials appear in reset, bar, exception, or audit output.

F11 [blocking] `12.4` is a Gap: most checks name only a failure condition, not a silent input and empty-input output; D5, D13–D15, D17–D18, and D21–D23 are grep/file-presence inspections that do not reliably fail when the claimed control is removed, and no single evidence command is assigned to each gating probe.
Fix: give every check failing/silent/empty fixtures and assign these control commands: `ResetCommandAuthorizationTests` for `2.1`, `BarResetPersistenceTests` for `3.3`, `BarResetPrecedenceTests` for `4.4`, `BarResetDependencyFailureTests` for `6.2`, `ResetAuthorizationPathTests` for `10.1`, `BarResetSecretLoggingTests` for `10.3`, `EvidenceSelfTests` for `12.4`, `RollbackAuditTests` for `14.3`, and `PathsWalkedTests` for `14.4`.

F12 [blocking] D16 is unverifiable by its stated manual evidence: a stranger checking one `[Beelz RESET]` line cannot verify the required step list/count-or-error format, target identity, elapsed time, exactly-once behavior, or absence of chat text, tokens, and passwords.
Fix: replace it with a logger-capture test asserting the complete schema, cardinality, and redaction, leaving the live session only as supplementary evidence.

F13 [blocking] D17, D18, D21, and D22 are unverifiable by their `file` evidence: containing one phrase does not prove the recovery flow, ApiVersion 33/banner/gate, six planted fault records, or an exact rollback range and command.
Fix: use a documentation validation command that asserts every required field and cross-file value, and make it exit nonzero when any individual claim is absent or inconsistent.

F14 [blocking] `14.1` is a Gap: “all at once” is decided, but the plan does not name who can disable the reset feature; `Forms_AutoFillFromCaptures` only restores form-fill behavior and is not a reset kill switch.
Fix: state that rollback by a named operator is the disable mechanism, or introduce a named runtime switch and identify who may change it.

F15 [blocking] `14.3` lacks a working control: D22 verifies only the text `rollback: git revert --no-edit`, so a missing/wrong commit range or an impossible post-write rollback still passes.
Fix: record explicit inclusive commits and add `pwsh Beelzebub/tools/verify-bar-reset-rollback.ps1`, which fails unless the audited range resolves, covers every release commit, and the documented server downgrade remains compatible with the resulting state/config.

F16 [blocking] `14.4` and D23 are unverifiable: “compared against the list” is not an executable command, the list mixes fully qualified and relative paths, and `git diff 3fbc2d4..HEAD` omits untracked/ignored build-created files and cannot validate outside-repo writes.
Fix: provide one executable path-audit command combining committed diff, `git status --porcelain=v1 --untracked-files=all`, declared generated/ignored outputs, and explicit outside-repo targets, failing on any undeclared path.

F17 [blocking] `15.2` is a Gap: deferred work is described as “process adoption, 2026-09-30” without the required slug or issue, so a builder cannot locate its owner or acceptance boundary.
Fix: attach each deferred item to a concrete issue/plan slug, or state after checking the backlog that nothing is deferred.

F18 [advisory] The three unhandled scenarios are: a player name containing a newline forges a reset log entry (`10.2`); a game patch returns a nonempty but wholly unfamiliar dump and destructive cleanup proceeds with incomplete visibility (`6.2`/`12.4`); and a newly added reset alias bypasses the fixed grep set and targets another player (`2.1`/`10.1`).
Fix: include these as hostile, dependency-garbage, and unauthorized-path fixtures in the named test suites.

F19 [advisory] S-2 is not cheaply reversible: a failed Reapply has already deleted saved bindings and live modifications, while “swap weapons” does not restore deleted configuration and is therefore mitigation rather than a fallback.
Fix: classify it as a validated-before-release assumption or preserve a pre-reset snapshot until readback succeeds, with an explicit restore policy.

F20 [advisory] `Also considered` is silent on operational ownership, decommissioning of the old recommended recovery paths, and explicit retention-law analysis; “compliance” and “analytics” are bundled into a broad N/A without their applicability tests.
Fix: add one line per required topic naming the operator/runbook, old-path disposition, and the concrete test supporting each N/A.

5/15 layers · 33/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · D15 is now `check_bar_reset.py auth`, which parses every `[Command]` in Commands/*.cs (group `beelz admin` or a `player`/`target` parameter) and FAILs on one missing `adminOnly: true` or on fewer than 50 parsed commands; the console/unauthenticated actors are answered in Design › Permissions (VCF dispatch)
- F2 · accepted · Interfaces › Inputs states the lookup semantics and rejection reply; D25 (BarResetInput.IsConfirm, tested) owns the CONFIRM grammar; arity is VCF's
- F3 · accepted · Design › Data lists owner, location, retention and copies for every artifact (state.json, log lines, cfg key, log copies, review files, audit record, DLL)
- F4 · accepted · Business rules 5 defines every slot (whole AbilityGroupSlotBuffer, chat shows 0-8 plus a count), every command (parsed from source by D15) and every path (diff plus untracked, ignored outputs declared) — D6, D15, D23, D24
- F5 · accepted · Interfaces › External pins BepInEx 6.0.0-be.733, VampireReferenceAssemblies 1.1.12-r99041-b2, VCF 0.10.x, states no quota/cost, and enumerates the dump record classes; unmatched `[ModId` lines make the dump Unreadable (D1)
- F6 · accepted · failure behaviour per dependency in Interfaces › External; new D24 (unreadable never clean, no destroy) and D12 (runner continues) carry the tests
- F7 · accepted · Use cases › Minimal stretch lists exactly what a one-time use leaves (two log lines, one Audit line, one cfg key) and that nothing nags
- F8 · accepted · D16 now tests `BarResetLog.Format` + `LogSafe` (control chars and brackets stripped, 32-char cap, one line); D1 uses anchored single-line regexes
- F9 · accepted · D13 + D15 checker computes the entry points from source (FullReset call sites, every targeting command) rather than a hand list; rebuildbar routes to the `bar` handler
- F10 · accepted · D16 is a unit test of the only function that builds the line, restricted to enumerated fields; no secret is read by the feature (Security 10.3)
- F11 · accepted · every check_bar_reset subcheck FAILs on empty parse; each D-item names failing, silent and empty cases; gating-probe → evidence map in Failure & observability 12.4 (checker subcommands instead of the reviewer's suggested xUnit names, since these are source/doc checks)
- F12 · accepted · D16 replaced by the BarResetLogTests unit test (schema, one line, sanitized name, unreadable ≠ none)
- F13 · accepted · D17, D18, D21, D22 now `check_bar_reset.py docs` / `audit`, which assert each field and cross-file value and exit non-zero on any missing one
- F14 · accepted · Rollout 14.1 names the owner (server admin) and rollback as the disable mechanism; reset only runs when typed
- F15 · accepted · D22 `check_bar_reset.py audit` resolves both commits with git rev-parse, requires the release commit inside the range, and checks state Version 8 (v0.136 downgrade compatibility stated in Rollout)
- F16 · accepted · D23 `check_bar_reset.py paths` combines `git diff 3fbc2d4..HEAD` and `git status --porcelain=v1 --untracked-files=all` against the parsed Paths walked list (all repo-relative); ignored and outside-repo outputs are declared
- F17 · accepted · new D26 + `docs/BACKLOG.md` with slugs clearbar-fullreset, transform-chain-guard, docs-consolidation, dev-snapshot, referenced from Out of scope
- F18 · accepted · covered by D16 (newline name), D24 (unfamiliar dump → Unreadable, no destroy) and D15 (new alias parsed from source)
- F19 · accepted · S-2 fallback reworded: a reset deletes only binds the user asked to delete; a failed Reapply loses no data (weapon skills return on swap); snapshot rejected in Also considered
- F20 · accepted · Also considered adds operational ownership/runbook, decommissioning (LEGACY), and N/A tests for compliance, localisation, analytics and cost

## Review 2 · 2026-09-30 · codex · plan uncommitted · plan 39607 B · 26 items · files 13 · 65f06e4c3d68 · prompt b99e3af5fb15

1. Considered — Purpose & typical use (all probes: “Purpose & typical use”).
2. Considered — Actors & permissions (all probes: “Design › Permissions,” D13, D15–D17).
3. Gap — Inputs, outputs & data: probe 3.3 lacks enforceable evidence for retention and deletion of every artifact.
4. Considered — Business rules & invariants (“Business rules,” D2–D4, D6, D10, D15, D23–D24).
5. Considered — Internal interfaces (“Interfaces › Internal,” D1–D2, D6–D8, D13, D16, D24).
6. Gap — External dependencies & contracts: probe 6.2 does not cover persistence, delayed engine work, or all stated collaborators.
7. Considered — States & lifecycle (“Design › States,” D2, D7, D9–D11).
8. Considered — Minimal stretch (“Use cases › Minimal stretch,” D5, D10–D11, D16, D25).
9. Considered — Maximal stretch (“Use cases › Maximal stretch,” D3, D10, D15–D16).
10. Considered — Security & privacy (“Security,” D1, D13, D15–D16).
11. Considered — Design & UX (“Design › UX,” D4, D6–D7, D13, D16–D17, D24–D25).
12. Gap — Failure handling & observability: probe 12.4’s claimed failing-case inventory is incomplete.
13. Considered — Performance & scale (“Performance,” D6, D16).
14. Considered — Rollout & compatibility (“Rollout,” D5, D18, D22–D23).
15. Considered — Out of scope (“Out of scope” and D26).

F1 [blocking] Probe 3.3 is unanswered by evidence: D3 only tests reset scope and cannot verify the stated locations, retention, deletion, or single-copy status of logs, temporary copies, review files, audit records, configuration, and deployed DLL.
Fix: Add one named evidence command that inventories every produced artifact and fails when its owner, location, retention/deletion rule, or multiplicity differs from the Design › Data table.

F2 [blocking] Probe 6.2 is unanswered for `RequestSave` failure and delayed engine work: the plan only tests step exceptions and unreadable dumps, so a reset may read clean in memory, fail to persist, and resurrect binds after restart.
Fix: Decide that save failure makes the result non-clean and test, with one named command, that it is logged/reported while a restart simulation cannot treat the reset as durable.

F3 [blocking] Probe 12.4 is unanswered: D21 plants faults for only 14 items, while checks introduced by D6–D10, D18–D20, D22–D23 and D26 lack stated failing input, silent input, and empty-input output; the assertion that “each check’s D-item” supplies these is false.
Fix: Add a complete check matrix and one selftest command covering every introduced check, including empty discovery, real-input spelling, planted real-run state, and an empty result that reports “no input,” never success.

F4 [blocking] Probe 4.4 has no single evidence command: the mapping names `BarResetTests + FormBarFillTests`, contrary to the one-command-per-gating-probe requirement, so removal of either scope precedence or form precedence is not tied to one declared failing command.
Fix: Name one command such as `dotnet test Beelzebub.Tests --filter "BarReset|FormBarFill"` and state that it fails when either precedence rule is removed.

F5 [blocking] Probe 11.2’s D6 evidence is not independently verifiable by a stranger: it assumes a running server at `127.0.0.1:9876`, a pre-existing `PerpetualChaos` character, credentials, captures, and a known method for observing replies, none of which the manual evidence specifies.
Fix: Give a reproducible server/tester setup and fixture procedure, or replace D6 with an automated fixture command that captures and asserts the nine diagnostic lines and repeat-run counts.

F6 [blocking] Probe 4.1 is internally undecided: D7 requires vanilla weapon skills without a swap, while reversible assumption S-2 permits accepting a swap and amending the plan if Reapply fails; that fallback changes the acceptance contract and is not cheap reversal.
Fix: Decide now whether no-swap restoration is mandatory; either retain D7 and block release on failure, or explicitly change D7 and the UX contract to require a weapon swap.

F7 [advisory] The offline-Purge scenario is risky: D11 omits `RevertTransform` for every offline target even when its transform is parked, while D2 says parked transforms count as active, so reconnect may revive state the admin believed was purged.
Fix: Add a planner case for an offline parked transform and either clear its registry state or explicitly report that Purge remains pending until login.

F8 [advisory] A reset issued during an in-flight cast allows the cast to finish, but the plan does not handle a delayed projectile, buff, or spawn that creates a new override source after Readback, so the immediate clean result can become stale.
Fix: Add a post-tick reconciliation/readback or document and test the next-login/next-tick cleanup behavior under probe 7.3.

F9 [advisory] The maximal case gives a 100 ms budget for roughly 296 formatted registry dumps but defines no behavior when that budget is exceeded, making the command capable of monopolizing the server tick.
Fix: Measure the 296-slot case and define either an accepted upper bound, staged processing, or a warning threshold.

EARLIER: all resolved
12/15 layers · 46/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · Design › Data rewritten as one row per artifact (owner, location, retention, copies); +D28 `check_bar_reset.py data` fails on a missing field, on a logged tag or Forms config key with no row, or on zero rows
- F2 · accepted · +D27 SaveBindings step calls new `PersistenceService.TrySaveSync()`; false or throw → ERR, Clean=false, reply says binds may return after restart; Business rules 8; failure behaviour added to Interfaces › External
- F3 · accepted · Failure & observability now carries the full check matrix (failing / silent / empty per check, manual items included); D21 plants faults for every automated item (D19/D20 via fixtures); +D29 `check_bar_reset.py selftest` over empty, defect and good fixture trees
- F4 · accepted · 4.4 mapped to one command `dotnet test Beelzebub/Beelzebub.Tests --filter "FullyQualifiedName~BarResetTests|FullyQualifiedName~FormBarFillTests"`, failing when either rule is removed
- F5 · accepted · new Use cases › Test fixture (server address, admin character and how admin is granted, where replies and logs are read, fixture commands to create captures/binds/hotkey); D6-D10 reference it and carry PASS lines
- F6 · accepted · decided: no-swap restoration is mandatory (Business rules 7); D7 blocks the release; S-2 fallback changed to a server-side equip-buff re-apply that keeps D7's contract
- F7 · accepted · D11 + Business rules 6: offline ClearSavedBindings drops the parked transform record, SaveBindings follows; test case added
- F8 · accepted · States 7.3 documents an in-flight cast landing after Readback (no saved bind behind it; next swap/relog drops it; re-run shows and clears it — D6, D10)
- F9 · accepted · Performance: accepted upper bound 250 ms, `slow=1` in the log line (tested in D16), recorded as a finding for staged processing

## Review 3 · 2026-09-30 · codex · plan uncommitted · plan 47477 B · 29 items · files 13 · 87e9cf28a44a · prompt 21f89129fb7f

F1 `blocking` — Probe `3.3` is unanswered: D28’s artifact inventory omits the generated self-test fixture trees and test/build outputs, while its checker only discovers selected log tags and Forms keys rather than every artifact the feature produces.
Fix: Add owner/location/retention/copies rows for fixture trees and test/build by-products, and make `check_bar_reset.py data` fail when any declared producer lacks a matching row.

F2 `blocking` — Probe `10.1` lacks a control for indirect callers: `auth` checks command annotations and counts `FullReset` calls, but does not fail if a job, hook, API handler, or other non-command path invokes `FullReset` or bar readback without authorization.
Fix: Make `auth` enumerate every repository-wide caller of `FullReset` and the diagnostic readback and allow only the named self-only player command or `adminOnly` command handlers.

F3 `blocking` — Probe `12.4` is not evidenced by its single gating command: `check_bar_reset.py selftest` exercises checker subcommands, not the unit-test, preflight, or manual checks listed in the failing-case matrix, so removing those checks can still leave the gating command green.
Fix: Make one non-recursive self-test command run planted failing, silent, and empty cases for every introduced automated check, while explicitly recording manual checks as manual-only cases.

F4 `blocking` — D21 is unverifiable by `cmd`: `check_bar_reset.py audit` can confirm that prose claims faults failed and passed, but a stranger cannot verify that the faults were actually planted or that the stated commands produced those results.
Fix: Persist command, fault patch, failing output, restored passing output, and hashes—or have a reproducible fault harness execute every planted defect itself.

F5 `blocking` — D29 is internally unverifiable because it says `selftest` runs “every subcheck,” which includes `selftest` itself, while Build-plan step 4 silently changes the contract to “every other subcheck.”
Fix: Define D29 as exercising every named non-selftest subcommand and list those subcommands explicitly.

F6 `blocking` — Probe `14.3` is not executable as sequenced: D22 requires the audit record to name a resolvable range containing the release commit, but step 9 writes and checks that record before the release commit exists, then creates the commit whose immutable hash the record must already contain.
Fix: Create the release commit first, then add a follow-up audit commit recording immutable first/release SHAs and verify the range from that follow-up commit.

F7 `advisory` — Scenario for `7.3`: a cast already in flight can create an override after Readback reports `Clean=true`, contradicting D7’s immediate-vanilla promise even though States explicitly permits this race.
Fix: Quiesce or re-read after deferred engine work, or define such late survivors as a failed reset rather than reporting clean.

F8 `advisory` — Scenario for `9.1`: the plan accepts a 296-slot dump walk on the main server tick but has no acceptance evidence for the stated 250 ms bound; `slow=1` observes a breach after it has already stalled the server.
Fix: Add a measured maximal fixture/session case and a staged-processing fallback threshold.

F9 `advisory` — S-2 is not credibly `reversible`: its fallback, “re-apply the held weapon’s equip buff server-side,” names no proven API or teardown semantics and may itself add modification rows or recreate the pile-up.
Fix: Mark it validated only after an isolated in-game proof, or specify and test the exact fallback operation before implementation.

F10 `advisory` — D17/S-5 requires documentation saying `respawn` reuses the same character entity, but the supplied `TransformBuffService` commentary describes `RespawnCharacter` as constructing a fresh character entity; the docs checker would validate wording, not truth.
Fix: Resolve the contradiction from an entity-ID trace and make the guide state the observed behavior rather than hard-coding the current assumption.

F11 `advisory` — Minimal-stretch scenario for `8.1`: `admin bar` on an offline target promises saved binds, but D6 defines its output through live `AbilityGroupSlotBuffer`, equip rows, and modification dumps without defining the offline reduced schema.
Fix: Specify the exact offline fields and messages and cover them in the D11 test.

F12 `advisory` — Unauthorized-path scenario for `2.2`: the plan relies on VCF’s denial message but gives no evidence case showing a non-admin invocation neither logs diagnostics nor mutates saved state.
Fix: Add a negative dispatch fixture or manual case that invokes each targeting command as a non-admin and verifies denial plus unchanged state.

EARLIER: all resolved
12/15 layers · 46/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · Design › Data adds rows for selftest fixture trees, harness output, fault patches and test/build outputs; D28 now finds producers by scanning the Paths-walked files (log literals, Config.Bind, file writes, mkdtemp, build outputs) and fails on any without a row
- F2 · accepted · D15 `auth` enumerates every caller of FullReset and ReadBar under Beelzebub/Beelzebub/ and allows only the self-only ResetBar handler or adminOnly methods
- F3 · accepted · 12.4 now maps to one command, `fault_harness.py bar-reset` (D21), which plants a failing case for every automated check (unit tests, preflight, checker) and lists D6-D10 as manual-only
- F4 · accepted · D21 replaced prose records with a re-runnable harness over tracked patches in `Beelzebub/tools/faults/bar-reset/`; the audit stores its output and the patch-tree hash
- F5 · accepted · D29 names the nine subcommands it runs (never itself) and the exact case count, 27
- F6 · accepted · D22 moved to a `rollback` subcommand run after a follow-up `docs(audit)` commit that records both SHAs; `all` (in preflight) excludes it; step 9 resequenced
- F7 · accepted · States 7.3: Clean means clean at Readback; D7's PASS line re-checks after a swap and a relog, so a late survivor fails D7
- F8 · accepted · step-10 session records the largest `ms=`; above 250 ms is a `defect` amendment for staged processing
- F9 · accepted · S-2 fallback is now diagnostic-first: per-slot `reapply` log line names the cause before any fix; D7 stays a release condition
- F10 · accepted · S-5 rewritten to the code-backed fact (binds are Steam-keyed and re-inject, so respawn and reset-character never clear them, whatever entity they build); D17 no longer claims entity reuse
- F11 · accepted · D6 defines the offline readout (offline line, saved sets with bound slots, transform record, hotkey count)
- F12 · rejected · advisory — VCF checks `adminOnly` before invoking the method (Learning Mods/VampireCommandFramework-main/VCF.Core/Basics/BasicAdminCheck.cs `return !cmd.AdminOnly || ctx.IsAdmin`, covered by VCF.Tests/BasicAdminMiddlewareTests.cs User_Denied_AdminOnly), so the control is the attribute, which D15 enforces for every targeting command and every FullReset/ReadBar caller; a second non-admin account for a manual case is not available on the dev server

## Review 4 · 2026-09-30 · codex · plan uncommitted · plan 51599 B · 29 items · files 13 · 87e9cf28a44a · prompt 884dc96069fc

Blind score: 1 Considered—Purpose; 2 Considered—Permissions; 3 Considered—Inputs/Design › Data; 4 Gap—Business rules; 5 Considered—Interfaces; 6 Considered—External dependencies; 7 Gap—Design › States; 8 Considered—Minimal stretch; 9 Considered—Maximal stretch; 10 Considered—Security; 11 Considered—UX; 12 Gap—Failure & observability; 13 Considered—Performance; 14 Considered—Rollout; 15 Considered—Out of scope.

F1 `blocking` — Probe 4.4 is contradictory: step 9 creates the release commit before step 10 runs D7, while Business rule 7 says a D7 failure must be fixed before that release.  
Fix: Satisfy 4.4 by deciding that D6–D10 run against a release candidate before `chore(release)`, or explicitly defining the commit as non-release and specifying how post-test fixes alter the checked rollback range.

F2 `blocking` — Probe 12.4 remains unanswered for manual checks D6–D10: the matrix gives only “PASS line not met,” not a concrete failing input, silent input, and non-passing empty input, while the sole gating command deliberately reports these checks as `manual-only`.  
Fix: Satisfy 12.4 by specifying those three inputs for each D6–D10 and adding them to an evidence command that exits non-zero when the corresponding control or fixture state is absent.

F3 `blocking` — D13 is unverifiable for probe 5.2: its command claims that Commands cannot call “a layer helper,” but the checker only rejects four named legacy helpers and would pass direct calls to new helpers such as `RemoveInjectedRows`, `PopSlotModifications`, `EmptyPush`, or `ReapplyEquipRows`; a stranger therefore cannot verify the stated single-path contract.  
Fix: Satisfy 5.2 by making `commands` enforce an explicit allowlist of reset entry points or reject every reset-layer mutation symbol outside `BarResetService`.

F4 `blocking` — Probe 7.1 is unanswered for an online target whose character exists but temporarily lacks `BuffBuffer`, `AbilityGroupSlotBuffer`, or `EquipBuff_Weapon`: this partial-state scenario can make live operations return zero and potentially report `Clean` without resetting anything.  
Fix: Satisfy 7.1 by deciding that missing required live components produces an unreadable/deferred result with `Clean=false`, preserves saved-state clearing, and instructs the user when to retry.

F5 `blocking` — D21 does not verify its own stated coverage for probe 12.4: it requires a patch for every automated item, but step 5 substitutes runtime arguments for D19 and D20; `-ChangelogMaxKB 1` tests changelog size rather than D19’s version/entry controls.  
Fix: Satisfy 12.4 by planting source or fixture faults that remove each D19 and D20 control actually claimed, and require the harness manifest to map every automated D-item to its exact `fails when`.

F6 `advisory` — Abuse scenario (9.2): an unrelated override source whose prefab name begins `Item_` or `EquipBuff` is classified as gear and survives purge, although provenance—not naming—determines whether it is legitimate.  
Fix: Base gear classification on verified component/source ownership, or document the prefix rule as a compatibility limitation and expose the preserved source in `admin bar`.

F7 `advisory` — Concurrent scenario (7.3): an in-flight cast may create an override after Readback, so the command can log `survivors=none` before the bar becomes dirty; the plan describes recovery but not how operators distinguish this race from a successful reset.  
Fix: Add a post-tick diagnostic recheck or log a distinct “clean-at-readback; late work possible” state.

F8 `advisory` — Maximal-stretch scenario (13.1): scanning roughly 296 slot dumps plus logging every modified slot may exceed 250 ms, but the plan postpones the staged-processing design until after observing the breach.  
Fix: Record the chosen synchronous risk explicitly and add a stress fixture with many modified slots before the in-game release-candidate test.

EARLIER: all resolved
12/15 layers · 46/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · Business rules 7 and steps 9-10: the in-game session runs on an uncommitted release candidate (PREFLIGHT OK) before `chore(release)`; any D6-D10/D30 failure means no release commit, a `defect` amendment, a fix and a re-test; the release commit and rollback range (D22) follow only after D30 passes
- F2 · accepted · +D30 `check_bar_reset.py session <log>` turns D6-D10 into a command over the copied session log (BAR/RESET/FORM line groups; empty log FAILs); the check matrix gives each of D6-D10 a concrete failing input, a silent input and an empty input; D21 plants each D30 clause into a tracked fixture log
- F3 · accepted · D13 is now an allowlist: under Commands/ only `FullReset` and `ReadBar` are allowed; the rejected set is every `IBarResetOps` member (read at check time), every other public `BarResetService` member and the named layer helpers; only the LEGACY clearslotmods/rebuildslots handlers are exempt
- F4 · accepted · D2 planner takes `liveReady`; D11 and States 7.1: an online character missing BuffBuffer / AbilityGroupSlotBuffer / the held EquipBuff gets the saved-only plan plus Readback, `Clean=false`, `Unreadable`, and a relog-or-respawn reply; the test fails on a live step or Clean=true
- F5 · accepted · D21 reads a `manifest.json` mapping every `fails when` clause of every automated item to a real source or fixture patch (never a runtime argument); D19 patches the toml version, the [0.137.0] entry, the README status and a `.beelz slot ` reply; D20 patches a tracked fixture log run via the new `-LogDir`
- F6 · accepted · Business rules 4 records the prefix rule as an accepted limitation; D6 prints each kept gear source's prefab name
- F7 · accepted · FullReset re-reads one tick later and logs `[Beelz RESET] late-survivor slot=<n>` (D16 `FormatLate`); D30 fails on one after the D7 reset
- F8 · accepted · Performance records the synchronous worst case as an explicit accepted risk; no synthetic stress fixture (the engine formatter only runs in the server); the session records the largest `ms=`

## Review 5 · 2026-09-30 · codex · plan uncommitted · plan 59008 B · 30 items · files 13 · 87e9cf28a44a · prompt 73c8feb2afbc

F1 `blocking` — Probe `2.1` is unanswered: Permissions omits the explicitly required unauthenticated actor, and `check_bar_reset.py auth` only checks command annotations/callers, so it cannot fail if an unauthenticated dispatch path reaches a handler.  
Fix: State that unauthenticated clients cannot reach VCF command dispatch and make the auth evidence command fail when that boundary is bypassed.

F2 `blocking` — Probe `3.3` is unanswered for `state.json.tmp`: the Data row calls it transient but gives no retention/deletion rule after `File.WriteAllText`, `File.Replace`, or `File.Move` fails, while D28 checks only that four fields exist.  
Fix: Decide whether failed temporary files are deleted immediately or retained for recovery, and make `check_bar_reset.py data` fail when that lifecycle control is absent.

F3 `blocking` — D6 is not verifiable by its stated `manual` evidence: its two-command fixture checks only two online bound slots and identical logs, so a stranger cannot verify the claimed offline rendering, slots 0–8, saved-set enumeration, transform record, hotkey count, source classification, or read-only behavior.  
Fix: Add an executable D6 evidence command or explicit manual cases covering each claimed online/offline output and a before/after state comparison proving no mutation.

F4 `advisory` — Concurrent scenario, layer 7 probe `7.2`: two serialized resets can schedule overlapping next-tick readbacks, but `FormatLate(slot)` contains neither target nor originating reset identity, so a survivor from another player/run can be attributed to D7.  
Fix: Include target and a reset correlation ID in late-survivor records and correlate them in the session checker.

F5 `advisory` — Minimal-stretch scenario, layer 7 probe `7.1`: an unknown scope deliberately returns an empty plan, but the plan does not state whether that result is forced unclean/unreadable or could be reported as a successful zero-step reset.  
Fix: Specify that an empty/unknown-scope plan is an error result and add the corresponding assertion to D2.

F6 `advisory` — Maximal-stretch scenario, layer 13 probe `13.2`: D6 requires one `[Beelz BAR]` line containing every modified slot, but no bound is defined for hundreds of entries, so the diagnostic may exceed the logger’s practical line capacity and become unverifiable or truncated.  
Fix: Define a maximum line size and deterministic continuation/chunk format, then teach D16/D30 to validate all chunks.

F7 `advisory` — The `reversible` S-2 assumption is not genuinely cheap to reverse: its fallback is only diagnostic logging and another development cycle if the engine cannot restore weapon skills by replaying equip rows.  
Fix: Relabel S-2 as an implementation hypothesis validated by D7, or name a concrete alternate engine restoration path.

F8 `advisory` — Blind layer scoring: 1 Considered—Purpose; 2 Gap (`2.1`); 3 Gap (`3.3`); 4 Considered—Business rules; 5 Considered—Interfaces/Internal; 6 Considered—Interfaces/External; 7 Considered—Design/States; 8 Considered—Use cases/Minimal stretch; 9 Considered—Use cases/Maximal stretch; 10 Considered—Security; 11 Considered—Design/UX; 12 Considered—Failure & observability; 13 Considered—Performance; 14 Considered—Rollout; 15 Considered—Out of scope; no layer is N/A.  
Fix: Close F1–F3, then all 15 layers and 49 probes can score Considered.

EARLIER: all resolved
13/15 layers · 47/49 probes
VERDICT: REVISE
### Dispositions
- F1 · accepted · Permissions 2.1 names the unauthenticated actor: the server only accepts chat from a connected Steam-authenticated user and VCF dispatches from that system with the sender's User (S-3), so no anonymous path reaches a handler; an engine boundary no repository check can plant, so no new command
- F2 · accepted · Design › Data: TrySaveSync deletes state.json.tmp best-effort in its catch; a surviving .tmp is overwritten by the next save and never read at boot (at most one copy)
- F3 · accepted · D6 manual evidence now covers the 9 bar-slot lines, set name, per-slot `none`, kept gear source names, read-only (identical BAR lines and hotkey list before/after) and an offline target's full readout
- F4 · accepted · `[Beelz RESET]` gains `run=<n>`; FormatLate carries target, steamId and runId so D30 correlates a late survivor to its run
- F5 · accepted · D2: an empty plan (unknown scope) is an error result, Clean=false
- F6 · accepted · D16 FormatBar splits the slots list above 400 characters into `part=<k>/<n>` continuation lines
- F7 · rejected · S-2 keeps `reversible`: the thing reversed is the Reapply implementation, no user data is at risk (a reset deletes only binds the user asked to delete), and the fallback is the project's diagnostic-first procedure with D7 as a release condition; relabelling adds no control
- F8 · accepted · scoring note; F1-F3 closed above
