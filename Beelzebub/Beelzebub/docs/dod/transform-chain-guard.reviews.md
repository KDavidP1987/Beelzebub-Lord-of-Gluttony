# Reviews — transform-chain-guard

## Review 1 · 2026-10-04 · codex · plan uncommitted · plan 41000 B · 19 items · files 11 · 465f0a85e393 · prompt 4f140088f94e
F1 [blocking] Probe `2.1` is unanswered as a control: `actors` checks five command attributes and caller file locations, but cannot fail if an unauthenticated, console, RCON, or newly added method inside an allowed file reaches a route.
Fix: make one `actors` command enumerate every entry channel and call site, verify the authenticated actor/target relationship for each, and fail on any unclassified path.

F2 [blocking] Probe `3.1` is unanswered: `TransformGate.Message` accepts `activeName`, but the plan gives no decision for null, empty, control-character, markup-like, or longer-than-64 names and no invalid-input response.
Fix: specify normalization and rejection/substitution rules for every gate input, including a hard UTF-8 output bound for arbitrary `activeName`, and test those rules.

F3 [blocking] Probe `3.3` has no valid evidence command: `wiring` only inspects `ApplyPhase` state writes and cannot fail if TXGUARD logs, `_guardLogged` entries, temp results, screenshots, backups, staged packages, or planted edits violate their stated retention/deletion rules.
Fix: add one artifact-lifecycle command that verifies every produced artifact’s location, ownership, retention and deletion/cleanup path, including `_guardLogged` cleanup.

F4 [blocking] Probe `4.5` is unanswered: “every route” is derived from three callee searches at one commit, while `wiring` checks only four named method bodies and `actors` permits calls anywhere in four files; a new direct `ApplyForm` call in an allowed file is invisible.
Fix: define the route set from a repository-wide call scan including tracked and untracked build-created files, fail on every unmatched caller, and have the selftest plant a new route inside an allowed file.

F5 [blocking] Probe `6.2` lacks its required control: the cited unit/wiring commands prove gate ordering but do not fail when collaborators return false, throw, remove a pending entry after failed enrichment, or leave a deferred spawn/destroy unobserved.
Fix: provide one failure-injection command covering each named collaborator’s slow, false, throwing, garbage and deferred-effect path, with the required resulting state and message/log.

F6 [blocking] Probe `8.2` is unanswered: D5/D11 do not decide what remains after a player triggers one refusal and never uses the feature again; `_guardLogged` can retain the SteamID indefinitely.
Fix: state and verify cleanup on allow, revert, logout and shutdown, with a bounded fallback for a player who never invokes another transform route.

F7 [blocking] Probe `9.1` is unanswered: phase spam is repetition, not the required 100× volume case, and the plan gives no player/route volume, degradation mode or overload behavior.
Fix: choose a 100× workload, state the acceptable degradation and bound, and map it to a measurable evidence item.

F8 [blocking] Probe `10.1` is unanswered by the same incomplete `actors` control: file allowlisting does not prove authorization on automatic login/travel/combat paths or prevent an unauthorized caller added inside an allowed file.
Fix: make the authorization command verify every direct and indirect path’s actor, target SteamID derivation and privilege requirement, and plant unauthorized internal and command paths.

F9 [blocking] Probe `10.3` is unanswered as a control: the secrets regex detects only assigned credential-shaped literals; it would pass code that reads a token from configuration/environment and logs or exports it.
Fix: define permitted secret sources and rotation behavior, forbidden sinks, and add one command that fails on secret reads reaching logs, chat, events, files or process arguments.

F10 [blocking] Probe `12.4` is a Gap because D1–D3, D9–D11 and D13 are recorded as `n/a` or historical rather than run once against the real introduced checks; rubric 2 explicitly says an unrun failing case is a Gap.
Fix: before approval, run each real check with its failing, silent and empty input, record exact output, and ensure empty input never reads as a pass.

F11 [blocking] D5 is unverifiable by its stated `cmd` evidence: `wiring` checks mutation order only and cannot verify “at most once per player,” reset-on-allow behavior, logout/revert cleanup, or Auto-HP retry.
Fix: give D5 a test/command that exercises repeated refusal, allow, refusal-again, revert and logout, asserting log counts, set cleanup and retry state.

F12 [blocking] Probe `13.2` is unanswered and internally contradicted: `_guardLogged` is said to be bounded by online players, but cleanup is only promised on a later allow or revert, so disconnected players can accumulate; no bound source or behavior at the bound is stated.
Fix: choose a concrete cleanup policy and maximum, cite its source, state what happens at capacity and what valid case is excluded, and test it.

F13 [advisory] Minimal-stretch scenario: `activeName` is empty or contains a newline while an active transform is refused, potentially producing an ambiguous or multi-line chat/log record; this belongs to probe `3.1`.
Fix: add normalization fixtures for empty, control-character and maximum-size names.

F14 [advisory] Maximal-stretch scenario: many distinct players each trigger one refusal and disconnect without allowing or reverting, growing `_guardLogged`; this belongs to probes `9.1` and `13.2`.
Fix: add a high-cardinality lifecycle test once the cleanup/bound policy is chosen.

F15 [advisory] Concurrent/unauthorized scenario: a new admin helper inside `AdminCommands.cs` calls `ApplyPhase` for another player without `adminOnly`; current file allowlisting can pass it, affecting probes `2.1`, `4.5` and `10.1`.
Fix: plant this exact defect in `actors`/`selftest` and require the authorization scan to reject it.

F16 [advisory] S-3 is not meaningfully `reversible`: if chat timing never intersects the pending window, the proposed fallback permanently weakens D9 from an end-to-end concurrency test to unit/static evidence.
Fix: make the scenario deterministically hold or inject pending state, or classify the live pending case as untested rather than calling the assumption reversible.

6/15 layers · 38/49 probes
VERDICT: REVISE

### Dispositions
- F1 · accepted · `actors` now places every TryActivate / ApplyNativeFormTest / ApplyPhase call in one of 7 known (file, method) callers and every TransformBuffService form apply inside the 4 gated routes, plugin-wide; the unauthenticated/console part is stated, not tested: VCF handles only chat from a connected Steam-authenticated user and the console does not run VCF commands, so there is no code path to plant (Design › Permissions)
- F2 · accepted · `TransformGate.SafeName`: control characters and `<`/`>` dropped, cut to 64 + `…`, null/blank → `another unit`; D2 text and `Message_fails_when_a_name_breaks_the_line`, planted
- F3 · rejected · 3.3 is answered at Design › Data: the artifact table gives every by-product's location, owner, retention and copies, and the only new in-memory state (the TXGUARD ledger) is now bounded and cleaned by D20; logs, vrclient results and backups are pre-existing by-products of the dev procedure, not of this change, and a command cannot observe their OS-level retention — the clearbar-fullreset precedent (Review 1 F2) accepted the table without a command
- F4 · accepted · same change as F1: a new form apply anywhere outside the four routes, or a new route caller inside an allowed file, FAILs `actors`; the selftest plants both
- F5 · rejected · this slice adds one collaborator read, `TransformBuffService.HasPendingForm` (a dictionary lookup that cannot be slow, throw or return garbage); the downstream collaborators (ApplyForm, ReapplyFormAbilitiesInPlace, the spawn-hook enrich, Revert) are unchanged, their failure reporting is tabulated under Interfaces › External, and the deferred spawn is exactly the `pendingForm` input whose every route is refused by D1 and ordered by D4; injecting IL2CPP collaborators has no harness here (the profile: ECS calls are verified in game, D9)
- F6 · accepted · new `Logic/TransformGuardLog` (D20): entries dropped on the player's next allowed route (`Allowed`) and on revert (`Forget`, wired by D5), and the whole ledger cleared at 256 entries, so a player who never returns costs one entry until then; Use cases › 8.2 line
- F7 · accepted · Use cases › Maximal stretch: a 100× case (40 players × 10 phase commands a second), its cost and what degrades; D20's 10 000-player test
- F8 · accepted · same as F1, plus Design › Permissions (c): each automatic path acts only on the player its event fired for (the tick's own records, the combat-end buff target) and takes no SteamID from input
- F9 · rejected · 10.3: the mod has no credential source (no API, token or account), so there is nothing to read, rotate or leak; D7 scans code, embedded data and manifest for credential literals — the same answer the clearbar-fullreset and mounted-bar-reset reviews accepted
- F10 · accepted · Logic and tests built before approval and run (243/243); each D1–D3/D20 fault planted once; D11 and D13 run now for real (Log); D9 and D10 need the build (D9) or ran today on v0.137.4 (D10) and keep their recorded reason
- F11 · accepted · D5 now names the ledger wiring (`ShouldLog`, `Allowed`, `Forget` in Revert) and the `wiring` check fails when Revert does not Forget; the ledger behaviour itself is D20's tests
- F12 · accepted · Performance › Bounds: Cap 256, its source (40-player server × 4 routes × 5 reasons), behaviour at the bound (cleared) and the excluded case (one repeated line)
- F13 · accepted · same as F2
- F14 · accepted · `GuardLog_fails_when_it_grows_past_its_cap` (10 000 distinct one-time refusals ≤ 256 entries), planted
- F15 · accepted · the exact defect is planted twice: in the selftest fixture and against the real AdminCommands.cs (`actors: FAIL ['…/AdminCommands.cs:PhaseOther calls ApplyPhase']`)
- F16 · accepted · S-3 removed; Design › States states the known test limit (the pending window cannot be hit from chat; the in-game run proves no-error and the retry; the refusal is proven by D1/D4/D5) — no debug hook added
