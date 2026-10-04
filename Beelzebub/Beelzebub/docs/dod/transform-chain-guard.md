---
dod: 2
rubric: 2
kind: backlog
id: dod-20261004-7c2e
slug: transform-chain-guard
title: transform-chain-guard - no route chains one transform into another
status: draft
size: M
parent: none
created: 2026-10-04
baselined: none
closed: none
recon_commit: a38c168
coverage_author: 15/15 layers · 49/49 probes
coverage_reviewer: 0/15 layers · 0/49 probes
review: pending
---

# DoD: transform-chain-guard - no route chains one transform into another

**Size:** M — touches `Logic/` (new file), `Services/TransformService.cs`, `Commands/TransformCommands.cs`, a new check tool, a vrclient scenario and the BCH handoff; no schema change, no new external dependency (L test failed: one module family, no new dependency).
**Planned:** interactively (owner approved the decision set in `~/.claude/plans/virtual-seeking-rose.md` on 2026-10-04, every recommendation taken: one pure gate for every route (D1-A); a phase switch while the form is still spawning is refused and retried, not queued (D2-A); `admin testform` while transformed is refused with "revert first" (D3-A); no ApiVersion bump; release v0.137.5)
**Request:** "Then proceed with the additional development efforts" — the next backlog row, `transform-chain-guard` in `Beelzebub/Beelzebub/docs/BACKLOG.md`: "A recurrence guard in `TransformService.TryActivate` against chaining transforms (the pattern that left creature kits on the bar)."

## Definition of Done
- [ ] D1 · **One gate decides every route** `Logic/TransformGate.cs` `TransformGate.Decide(TransformRoute route, bool pendingForm, int activeUnit, int requestedUnit)` returns a `TransformGateVerdict`: `RefusePending` for every route when `pendingForm`; for `Activate`: `RefuseSameUnit` when `activeUnit != 0 && activeUnit == requestedUnit`, `RefuseActive` when `activeUnit != 0`, else `Allow`; for `FormTest`: `RefuseActive` when `activeUnit != 0`, else `Allow`; for `PhaseSwitch` and `Reapply`: `RefuseInactive` when `activeUnit == 0`, else `Allow`; a route value outside the enum returns `RefuseUnknown` (fail closed) · test: Beelzebub.Tests/TransformGateTests.cs (fails when: a pending form allows any route, an active transform allows Activate or FormTest, the same unit reads RefuseActive, a free player is refused Activate or FormTest, an active transform is refused PhaseSwitch or Reapply, or `(TransformRoute)99` is allowed)
- [ ] D2 · **The refusal texts** `TransformGate.Message(verdict, route, string activeName)` returns null for `Allow` and exactly: RefusePending `Still transforming — give it a moment, then try again.`; RefuseSameUnit `You're already transformed as that unit. Use .beelz revert to return to normal.`; RefuseActive on Activate `You're already transformed as <activeName>. Use .beelz revert first, then transform again.`; RefuseActive on FormTest `You're already transformed as <activeName>. Use .beelz revert first, then run testform again.`; RefuseInactive `You are not currently transformed. Use .beelz transform <unit> first.`; RefuseUnknown `That transform action is not allowed.`; every text ≤ 480 UTF-8 bytes for a 64-character name · test: Beelzebub.Tests/TransformGateTests.cs (fails when: Allow has text, a refusal text differs from the exact string, `<activeName>` is not substituted, or a text exceeds 480 bytes)
- [ ] D3 · **The log names route and reason** `TransformGate.LogLine(route, verdict, ulong steamId, int unit)` returns `[Beelz TXGUARD] refused route=<Route> reason=<Pending|SameUnit|Active|Inactive|Unknown> steamId=<id> unit=<guid>` and null for Allow · test: Beelzebub.Tests/TransformGateTests.cs (fails when: the route or reason token is missing or misspelt, or Allow yields a line)
- [ ] D4 · **Every route asks the gate first** in `Services/TransformService.cs`, `TryActivate`, `ApplyPhase`, `ReapplyActiveTransform` and `ApplyNativeFormTest` each ask the gate (`TransformGate.Decide(`, or `PhaseGate(` — which calls it — in `ApplyPhase` and `ReapplyActiveTransform`) before their first state-changing call (`ApplyForm(`, `TransformBuffService.Apply(`, `ReapplyFormAbilitiesInPlace(`, `TransformBuffService.Reapply(`, `SetActiveTransform(`, `CurrentPhase =`, `Revert(`, `ApplyPhase(`, `SendEvent(`); `ApplyNativeFormTest` contains no `Revert(` call; `Commands/TransformCommands.cs` `Phase` calls `PhaseGate(` before `ApplyPhase(` · cmd: `python Beelzebub/tools/check_transform_guard.py wiring` → `wiring: ok, 4 routes gated, testform never reverts, phase command gated` (fails when: any of the four methods has a state-changing call before its gate call or no gate call, ApplyNativeFormTest calls Revert, or Phase calls ApplyPhase without or before PhaseGate; a missing TransformService.cs or TransformCommands.cs prints `FAIL no input`)
- [ ] D5 · **A refused phase leaves no trace in state** `ApplyPhase` returns false on a refusal without assigning `CurrentPhase`, calling `SetActiveTransform` or emitting `[BEELZ:event] type=transform-phase-shift`, so the Auto-HP tick retries on its next pass; it logs the D3 line at most once per player until the gate next allows (`_guardLogged` set) · cmd: `python Beelzebub/tools/check_transform_guard.py wiring` (the D4 order check covers `CurrentPhase =`, `SetActiveTransform(` and `SendEvent(`) (fails when: ApplyPhase assigns CurrentPhase, calls SetActiveTransform or SendEvent before the Decide refusal returns)
- [ ] D6 · **Who reaches each route** `transform`, `phase` and `revert` stay self-only (no player/target/name/steam parameter, not adminOnly); `admin testform` and `admin force-transform` stay `adminOnly: true`; nothing calls `TryActivate`, `ApplyNativeFormTest` or `ApplyPhase` outside `Services/TransformService.cs`, `Commands/{TransformCommands,AdminCommands}.cs` and `Patches/UpdateBuffsBufferDestroyPatch.cs` · cmd: `python Beelzebub/tools/check_transform_guard.py actors` → `actors: ok, 3 self-only, 2 admin-only, <n> route callers in 4 allowed files` (fails when: a self-only command gains a target parameter or adminOnly, an admin command loses adminOnly, or a route method is called from another file; no .cs files prints `FAIL no input`)
- [ ] D7 · **No credential in the plugin** no `.cs`, `.json` or `.toml` file under `Beelzebub/Beelzebub` holds a credential-shaped literal · cmd: `python Beelzebub/tools/check_transform_guard.py secrets` → `secrets: ok, <n> files, 0 hits` (fails when: any `password|api_key|secret|bearer|access_token` key is assigned an 8+ character string; no such files prints `FAIL no input`)
- [ ] D8 · **The named controls exist** every D1–D3 control is a `[Fact]`/`[Theory]` method under its exact name (`REQUIRED_TESTS` in `check_transform_guard.py`, 9 names) · cmd: `python Beelzebub/tools/check_transform_guard.py tests` → `tests: ok, 9 named TransformGate controls present` (fails when: any named control is missing, renamed or loses its attribute; no test file prints `FAIL no input`)
- [ ] D9 · **In game, nothing chains** Claude's vrclient scenario `Beelzebub/tools/vrclient/scenarios/transform_chain_guard.vrs` runs (a) `.beelz transform 1` (Dracula) then `.beelz transform 0` → `Use .beelz revert first, then transform again.` + `[Beelz TXGUARD] refused route=Activate reason=Active`, and `.beelz transform 1` again → `You're already transformed as that unit.` + `reason=SameUnit`; (b) `.beelz admin testform wolf` while Dracula → `Use .beelz revert first, then run testform again.` + `route=FormTest reason=Active`; (c) `.beelz phase 2` → `Phase 2 (Bloodmage) active`, `.beelz revert`; (d) `.beelz admin force-transform Chaos 591725925` (Morgana, async form) then at once `.beelz phase 2` → no `[Error`/`Exception`/`at Beelzebub.` line, and after 3 s `.beelz phase 2` replies with `phase 2` (swapped, or "Already in phase 2."), then `.beelz revert` · cmd: `python Beelzebub/tools/vrclient/vrclient.py run Beelzebub/tools/vrclient/scenarios/transform_chain_guard.vrs` → `SCENARIO PASS transform_chain_guard 14/14` (fails when: a second transform or testform is applied over an active transform, a refusal lacks its TXGUARD line, a phase switch on a settled form fails, an error line appears, or the client cannot join — `ensure` FAIL aborts and prints `SCENARIO FAIL`)
- [ ] D10 · **Older in-game paths still pass** a plain cast and the clearbar transform case are unchanged · cmd: `python Beelzebub/tools/vrclient/vrclient.py run Beelzebub/tools/vrclient/scenarios/cast_basic.vrs` → `SCENARIO PASS cast_basic 6/6`, then the same for `clearbar_fullreset.vrs` → `SCENARIO PASS clearbar_fullreset 32/32` (fails when: any check of either scenario fails)
- [ ] D11 · **Session logs are clean** after the in-game runs and before any restart, the session's logs hold no Beelzebub stack frame and no error · cmd: `pwsh Beelzebub/tools/preflight.ps1 -LogCheck` → `PREFLIGHT OK (2 checks)` with `0 Beelzebub stack frame(s), 0 error(s)` for LogOutput.log (fails when: either log has a Beelzebub stack frame or an `[Error` line; a missing log FAILs)
- [ ] D12 · **BCH is told, no API bump** `Commands/ApiCommands.cs` keeps `ApiVersion = 35`; the handoff banner still reads `ApiVersion = 35` and gains a `v0.137.5` block naming `transform-phase-shift`, `Still transforming` and `no wire change` · cmd: `python Beelzebub/tools/check_transform_guard.py handoff` → `handoff: ok, api 35, banner 35, 4 tokens` (fails when: ApiVersion or the banner is not 35, or a token is missing; a missing file prints `FAIL no input`)
- [ ] D13 · **Release surfaces in sync** csproj and thunderstore.toml read 0.137.5, CHANGELOG.md has `## [0.137.5]` under its size cap, the README status names v0.137.5 and the root README is regenerated · cmd: `pwsh Beelzebub/tools/preflight.ps1` → `PREFLIGHT OK` with `versions csproj 0.137.5, thunderstore.toml 0.137.5` (fails when: the versions differ, CHANGELOG.md lacks the entry, the README names another version, or the root README is stale)
- [ ] D14 · **Rollback range recorded** the audit states the first and last commit of this build, the revert/rebuild/redeploy steps and that a rollback needs `no saved-data migration` · cmd: `python Beelzebub/tools/check_transform_guard.py rollback` → `rollback: ok, <first>..<last>` (fails when: the audit is missing (`FAIL no input`), the range line is absent or unresolvable, first is not an ancestor of last, or a step or the consequence is missing)
- [ ] D15 · **Every written path is committed** after the release commit `git status --porcelain` lists nothing but the owner's two private files (`Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx`, `_matrix_build.py`) · cmd: `git status --porcelain` → no other line (fails when: any other path is listed; outside a repository git exits 128, a FAIL)
- [ ] D16 · **Every touched path is declared** every path changed since `a38c168` (tracked diff plus untracked, minus the owner files) is named in the Rollout path list · cmd: `python Beelzebub/tools/check_transform_guard.py paths` → `paths: ok, <n> changed, <k> declared` (fails when: a changed path is undeclared or the list is missing; nothing changed prints `FAIL no input`)
- [ ] D17 · **Backlog row closed** `Beelzebub/Beelzebub/docs/BACKLOG.md`'s `transform-chain-guard` row (first cell) says `DONE v0.137.5` · cmd: `python Beelzebub/tools/check_transform_guard.py backlog` → `backlog: ok, transform-chain-guard DONE v0.137.5` (fails when: the row is missing or lacks `DONE v0.137.5`; a missing BACKLOG.md prints `FAIL no input`)
- [ ] D18 · **The 6.2 lesson is in the profile** `Beelzebub/Beelzebub/docs/dod/profile.md` › Project-wide notes carries a `Probe 6.2` line that asks, per collaborator, how it reports failure (throws / returns 0 / swallows) and how a deferred engine effect (DestroyTag, async form spawn) is observed in the same frame · cmd: `python Beelzebub/tools/check_transform_guard.py profile` → `profile: ok, probe 6.2 note present` (fails when: the line is missing or lacks `reports failure` or `deferred`; a missing profile.md prints `FAIL no input`)
- [ ] D19 · **Every check catches its fault** each `check_transform_guard.py` check passes a good fixture, FAILs a planted-defect fixture and FAILs an empty tree with `no input` (rollback and paths on a throwaway git repo) · cmd: `python Beelzebub/tools/check_transform_guard.py selftest` → `selftest: ok, 9 checks x good/defect/empty` (fails when: any check passes its defect fixture, fails its good fixture, or passes an empty tree)

## Purpose & typical use
A player who has unlocked a transformation types `.beelz transform <unit>`; while transformed they switch kits with
`.beelz phase <n>` (or the Auto-HP mode does it in combat) and end it with `.beelz revert`. Chaining one transform
straight into another — without a revert between — is what left players' bars stuck on a creature kit before v0.120:
the old form's teardown raced the new form's async spawn. v0.120 made `TryActivate` refuse that, and v0.49 made it
refuse while an async form is still spawning, but both live only in IL2CPP code with no test, and three other routes
still destroy-and-reapply a form with no such check: `ApplyPhase` (the phase command, the Auto-HP tick, the
combat-end reset and every `ReapplyActiveTransform` — refresh, travel-end, login, leaving a sub-form) and the admin
`testform` (which reverts and applies in the same frame). This slice puts one tested gate in front of every route, so
the stuck-kit class cannot come back through any of them. It extends the v0.49 / v0.120 guards and adds no new user
or job.

## Use cases
### Typical
- A transformed player types `.beelz transform 0`; nothing changes, the reply says to revert first, and the log has a `[Beelz TXGUARD]` line (D1, D2, D3, D9 a).
- A player in Dracula types `.beelz phase 2`; the kit swaps as before (D1, D9 c).
- An admin in a transform runs `.beelz admin testform wolf`; it is refused with "revert first" instead of chaining (D1, D2, D9 b).
### Minimal stretch
- `.beelz phase` with no transform: `You are not currently transformed …` (unchanged command check; the gate's RefuseInactive backs it) (D1, D2).
- `.beelz phase 2` right after a Morgana transform, while the serpent form is still spawning: `Still transforming — give it a moment, then try again.`; the next try works (D1, D2, D9 d).
### Maximal stretch
- Phase spam during the async window: each refused call changes nothing and logs once per player until it is allowed (D5); the Auto-HP tick keeps retrying every pass and lands when the form is ready (D5).
- A form that never finishes spawning (pending stuck): every route stays refused until `.beelz revert`, which clears the pending entry (`TransformBuffService` removes it on revert, unchanged); the log carries one line, not one per tick (D5).
- Abuse: every route is self-only or admin-only (D6); no player can chain another player's transform.

## Business rules
1. The gate (D1) is the single place the chain rules live: pending form → refuse every route; an active transform → refuse a new Activate (same unit: SameUnit) and FormTest; PhaseSwitch and Reapply require an active transform. Order inside a route: the gate is consulted before any state change (D4).
2. Precedence: a pending form outranks every other reason (it is the crash class from v0.49). The caller's own earlier refusals (unknown unit, not unlocked, disabled, difficulty, cooldown in `TryActivate`) run before the gate as today, so their messages are unchanged; the gate replaces only the v0.49 pending check and the v0.120 active check. The owner decides exceptions; no per-server setting.
3. A refused phase switch leaves `CurrentPhase` and the transform record as they were and emits no event (D5), so the Auto-HP ratchet retries; the combat-end reset to phase 1 is the one caller that does not retry — it runs only when `CurrentPhase > 1`, which cannot be true inside the first-apply pending window (the phase starts at 1 and a refused switch never raises it).
4. Time: none beyond the existing pending window (enriched on the form buff's spawn, usually one to two server ticks); no new timer.
5. "Every route" (D4) is computed by reading every caller of `TransformBuffService.ApplyForm`, `TransformBuffService.Apply` and `ReapplyFormAbilitiesInPlace` at `a38c168`: `TryActivate`, `ApplyPhase`, `ReapplyActiveTransform` (its native-form branch calls ApplyForm directly), `ApplyNativeFormTest`; `force-transform` reaches them only through `TryActivate`. The `wiring` check reads those four method bodies; D6's `actors` check fails when a route method is called from a file outside the known four, which is where a new route would appear.

## Interfaces
### Internal — reads / writes / changes (paths or symbols)
- Code read for this plan (repo paths): `Beelzebub/Beelzebub/Services/TransformService.cs` (`TryActivate`, `ApplyPhase`, `ReapplyActiveTransform`, `ApplyNativeFormTest`, `AutoAdvancePhases`), `Beelzebub/Beelzebub/Services/TransformBuffService.cs` (`HasPendingForm`, `_pendingForms` removal on enrich/revert, `ApplyForm`, `ReapplyFormAbilitiesInPlace`), `Beelzebub/Beelzebub/Commands/TransformCommands.cs` (`Phase`, `Transform`, `Revert`), `Beelzebub/Beelzebub/Commands/AdminCommands.cs` (`TestForm`, `ForceTransform`), `Beelzebub/Beelzebub/Patches/UpdateBuffsBufferDestroyPatch.cs:132`, `Beelzebub/Beelzebub/Patches/BuffSpawnServerPatch.cs:546`, `Beelzebub/Beelzebub/Services/BossFormRegistry.cs`, `Beelzebub/tools/check_clearbar.py` (pattern for the new check tool).
- New `Logic/TransformGate.cs` (pure; linked into `Beelzebub.Tests` by the existing `..\Beelzebub\Logic\*.cs` wildcard) (D1–D3).
- `Services/TransformService.cs`: the four routes call the gate (D4); new `public TransformGateVerdict PhaseGate(ulong steamId, ActiveTransform active)` used by `ApplyPhase` and the phase command; `_guardLogged` (HashSet<ulong>) for the once-per-player log (D5); `ApplyNativeFormTest` loses its revert-then-apply (D3-A).
- `Commands/TransformCommands.cs` `Phase`: asks `PhaseGate` first and replies the D2 text on a refusal (D4).
- What breaks if wrong: a gate placed after a state change re-opens the double destroy (D4 catches the order); a too-wide gate would refuse a settled phase switch (D9 c catches it in game).
- Contract (BCH): command shapes and `[BEELZ:*]` fields stay as they are (D12); a refused phase switch simply emits no `transform-phase-shift` event, the retry that applies emits it. `ApiVersion` stays 35; the handoff gets a `v0.137.5` note (D12).
### External — dependencies and their failure behaviour
- V Rising server ECS — the form-buff spawn is the slow dependency (6.2): `DebugEventsSystem.ApplyBuff` spawns a LifeTime-less form buff a tick or two later and the spawn hook enriches it. How each collaborator reports failure and how the deferred effect is observed:

  | Collaborator | Reports failure by | Deferred effect observed by | This plan |
  |---|---|---|---|
  | `TransformBuffService.ApplyForm` | returns false; an async form returns true before the buff exists | `HasPendingForm(steamId)` is true until the spawn hook enriches it | gate refuses every route while pending (D1, D4) |
  | `ReapplyFormAbilitiesInPlace` | returns false (form buff not present), logs on exception | — | its false is no longer allowed to fall through to a full ApplyForm while pending (D4) |
  | spawn-hook enrich (`TryEnrichSpawnedForm`) | removes a broken pending entry ("don't retry forever") | removes the entry on success | the gate re-allows on the next attempt (D5) |
  | `Revert` | returns (false, msg) | clears the pending entry | `revert` is the way out of a stuck pending (Use cases) |
  | `ApplyPhase` | returns false | — | returns false before any state change on a refusal (D5) |
- Slow or garbage results: a pending entry that never clears leaves every route refused (fail closed) until revert/logout; the once-per-player log keeps the log readable (D5). There is no timeout to tune.
- The game client, through vrclient (test tooling only): if the client cannot join, `ensure` fails and the scenario prints `SCENARIO FAIL` (D9); the owner is then asked to look.
- Supported runtime (6.1): game server `VRisingServer v1.1.15.0-r101082` (dev server boot line, 2026-10-04); `BepInEx.Unity.IL2CPP` `[6.0.0-be.733]`, `VampireReferenceAssemblies` `[1.1.12-r99041-b2]`, `VRising.VampireCommandFramework` `0.10.*` (`Beelzebub/Beelzebub/Beelzebub.csproj`) — unchanged. No quota, rate limit or cost: everything runs in the server process.
- Test mode: `.beelz admin force-transform` (admin-only, bypasses unlock and cooldown) for the Morgana case; it exists today and is used only by D9.
- Collaborators: the Codex post-audit is a review aid; if it cannot read the diff, its verdict is discarded and rerun with the diff and sources on stdin.

## Design
### Data
- No saved data changes: the transform record (`ActiveTransform` in `AbilityRegistry`, persisted in state.json) keeps its shape; a refusal writes nothing (D5).
- By-products — every artifact this change writes, its owner and lifetime:

  | Artifact | Where | Owner | Retention / deletion | Copies |
  |---|---|---|---|---|
  | `[Beelz TXGUARD]` log lines | `BepInEx/LogOutput.log` | server owner | overwritten at the next server start | procedure 6 copies both logs to `%TEMP%\beelz-logs-2026-10-04-txguard\` before a restart; OS temp cleanup |
  | vrclient results + screenshots | `%TEMP%\vrclient\results\`, `%TEMP%\vrclient\shots\` | dev machine owner | OS temp cleanup; never committed | none |
  | planted-fault edits | the tracked source file itself | Claude | each plant is reverted with `git checkout -- <file>` right after its check fails; D15 fails while one remains | none |
  | staged package | `Beelzebub/Beelzebub/dist/` (gitignored) | build | rewritten by every Release build | the Thunderstore upload, owner's choice |
  | deployed DLL | the dev server's `BepInEx/plugins/Beelzebub.dll` | owner | replaced by the next build | `bin/Release/net6.0/` |
  | plan, reviews, audit, pages | `Beelzebub/Beelzebub/docs/dod/`, `docs/audits/` | repo | git history | GitHub on the owner's push |

  Nothing here holds data beyond the SteamID already on every `[Beelz]` line (D14, D15).
### States
- Not transformed; transformed and settled; transformed with the async form still pending; parked by the reconnect grace (record kept, Reapply on login) — the gate covers each (D1).
- Concurrency (7.2): every route runs on the server main thread (VCF commands, the `Tick`, Harmony patches), so two routes never interleave within a frame; the race this guards is across frames — a route in frame N+1 while frame N's form buff is still unspawned — which is exactly what `pendingForm` captures (D1, D4).
- Interrupted (7.3): a refusal changes nothing, so there is nothing to undo; a stuck pending is cleared by `.beelz revert` or logout (`TransformBuffService` removal paths, unchanged). A retry after the form spawns is a normal call.
- Empty/first-run (7.1): a player with no unlocks or no transform gets the existing messages; RefuseInactive backs the phase command's existing text (D2).
### Permissions
- Actors (2.1): (a) a connected player reaches `transform`, `phase`, `revert` on their own SteamID only; (b) admins reach `testform` (self) and `force-transform` (any player) — both adminOnly; (c) the Auto-HP tick and the combat-end / travel-end / login patches reach `ApplyPhase`/`ReapplyActiveTransform` for the player they fire for; (d) an unauthenticated caller has no path: VCF handles only chat from a connected, Steam-authenticated user; (e) the server console/RCON do not run VCF commands. D6 checks (a), (b) and that no other file calls a route.
- Unauthorised path (2.2): unchanged — VCF's standard deny for adminOnly commands.
- Ownership (2.3): transform records are keyed by SteamID; `force-transform` is the only cross-player path and is adminOnly, and it now hits the same gate through `TryActivate` (D6).
### UX
- Discovery: the existing `transform`/`phase`/`testform` help texts; the refusal replies tell the player what to do next (D2).
- Feedback: one chat line per refusal, plain text; nothing new on success paths.
- Accessibility: plain chat text; every state said in words; each line under the 480-byte chat cap (D2). No new UI.
- Activation: the gate runs on every call of the four routes, including the automatic ones (Auto-HP tick, travel-end, login); the `[Beelz TXGUARD]` line shows it acted (D3, D5, D9).

## Security
- Authorization unchanged and kept by D6. Inputs: unit indexes/GUIDs and phase numbers are integers matched against the player's unlocks and `BossFormRegistry`; nothing reaches a shell, query or URL. Secrets: there is no credential source in this mod — none read, stored or logged; D7 scans the plugin's code, embedded data and manifest. Personal data: the SteamID already on every `[Beelz]` line; the TXGUARD line adds the route, reason and unit GUID only. Logs stay on the server owner's machine; vrclient artifacts stay in the dev machine's `%TEMP%`.

## Failure & observability
- Failure classes: a refused route → the D2 reply (player commands) or a D3 log line (automatic routes); a gate that wrongly allows → would show as a stuck bar or Burst crash, which D9 (d) and the D11 log check look for.
- Logged: `[Beelz TXGUARD] refused route=<Route> reason=<Reason> steamId=<id> unit=<guid>` once per player per refusal episode (D3, D5).
- In production: no telemetry; the owner runs `pwsh Beelzebub/tools/preflight.ps1 -LogCheck` after dev sessions and on tester logs (CLAUDE.md procedure 6); a run of TXGUARD lines for one player without a later success is the signal of a stuck pending, and `.beelz revert` is the response (D11).
- Gating controls and the one command behind each (12.4):

  | Probe | Command | Fails when |
  |---|---|---|
  | 2.1 | `python Beelzebub/tools/check_transform_guard.py actors` (D6) | a self-only command gains a target or adminOnly; a route is called from a new file |
  | 3.3 | `python Beelzebub/tools/check_transform_guard.py wiring` (D4, D5) | ApplyPhase writes CurrentPhase / the record before a refusal returns |
  | 4.4 | `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~TransformGateTests` (D1) | pending does not outrank active; same unit reads as another |
  | 6.2 | the same filter (D1) and `check_transform_guard.py wiring` (D4) | a pending form allows a route; a route re-applies before asking |
  | 10.1 | `python Beelzebub/tools/check_transform_guard.py actors` (D6) | as 2.1 |
  | 10.3 | `python Beelzebub/tools/check_transform_guard.py secrets` (D7) | a credential-shaped literal appears |
  | 12.4 | `python Beelzebub/tools/check_transform_guard.py selftest` (D19) and each check's planted fault (Log `note · planted`) | a check passes its defect fixture or an empty tree; a planted fault does not fail its check |
  | 14.3 | `python Beelzebub/tools/check_transform_guard.py rollback` (D14) | the range is missing/unresolvable or a step is missing |
  | 14.4 | `python Beelzebub/tools/check_transform_guard.py paths` (D16), with `git status --porcelain` (D15) | a touched path is undeclared or uncommitted |

## Performance
- One integer/bool decision per route call; the Auto-HP tick adds one dictionary lookup (`HasPendingForm`) per transformed player in combat. No hot path changes measurably.
- Bounds (13.2): `_guardLogged` holds at most one SteamID per player with a refusal in progress, removed when the gate next allows or on revert; it cannot grow past the online player count plus stale entries cleared on the next allow.
- Throughput (13.1): the gate is a few comparisons per call, far cheaper than the ECS work of an allowed call.

## Build plan
1. Create `Beelzebub/Beelzebub/Logic/TransformGate.cs` (`TransformRoute { Activate, PhaseSwitch, Reapply, FormTest }`, `TransformGateVerdict { Allow, RefusePending, RefuseSameUnit, RefuseActive, RefuseInactive, RefuseUnknown }`, `Decide`, `Message`, `LogLine`); add `Beelzebub/Beelzebub.Tests/TransformGateTests.cs` with the nine controls named in `REQUIRED_TESTS`: `Decide_fails_when_a_pending_form_allows_any_route`, `Decide_fails_when_an_active_transform_allows_a_second_activate`, `Decide_fails_when_the_same_unit_reads_as_another_unit`, `Decide_fails_when_a_free_player_is_refused`, `Decide_fails_when_formtest_chains_over_an_active_transform`, `Decide_fails_when_an_unknown_route_is_allowed`, `Message_fails_when_a_refusal_text_drifts`, `Message_fails_when_a_text_exceeds_the_chat_cap`, `LogLine_fails_when_route_or_reason_is_missing`; plant (drop the pending check; return Allow for FormTest) once each · satisfies D1, D2, D3
2. Create `Beelzebub/tools/check_transform_guard.py` (subcommands `wiring actors secrets tests handoff rollback paths backlog profile selftest`; same shape as `check_clearbar.py`: `FAIL no input` on a missing input, a `selftest` over good/defect/empty fixtures); run `tests secrets selftest`; `wiring` is expected to FAIL until step 3 (record the dry run) · satisfies D7, D8, D19
3. `Services/TransformService.cs`: `TryActivate` replaces its `HasPendingForm` and `GetActiveTransform` refusals with one `TransformGate.Decide(Activate, …)` call at the old v0.120 position (after the cooldown check, before `GetTransformAbilities`), replying `TransformGate.Message`; `PhaseGate(steamId, active)`; `ApplyPhase` and `ReapplyActiveTransform` call the gate first (Reapply route for the latter) and on a refusal log once (`_guardLogged`) and return false; `ApplyNativeFormTest` calls `Decide(FormTest, …)` and refuses instead of reverting; `Commands/TransformCommands.cs` `Phase` asks `PhaseGate` before `ApplyPhase`. Run `check_transform_guard.py wiring actors`, `dotnet test`; plant (move the ApplyPhase gate below `CurrentPhase =`; restore the testform revert) once each · satisfies D4, D5, D6
4. `docs/BCH_INTEGRATION_HANDOFF.md`: a `v0.137.5` block under the banner ("no wire change; ApiVersion stays 35; a phase switch while the form is still spawning replies `Still transforming — give it a moment, then try again.` and emits no `transform-phase-shift` until the retry applies; BCH: nothing to change"); `docs/dod/profile.md` gains the Probe 6.2 note; `check_transform_guard.py handoff profile` → ok · satisfies D12, D18
5. Release build `dotnet build Beelzebub/Beelzebub.sln -c Release -p:VRisingServerPath="C:/nonexistent"`, `dotnet test`; post-audit `Beelzebub/Beelzebub/docs/audits/transform-chain-guard.md` (`/code-review`, Codex on the diff and sources via stdin, ≤ 3 rounds, one commit per round) with `Rollback range: <first>..<last>` and the `no saved-data migration` sentence · satisfies D1–D5, D14
6. Deploy per CLAUDE.md procedure 6 (close the client, back up both logs to `%TEMP%\beelz-logs-2026-10-04-txguard\`, stop with `taskkill /PID` without `/F`, `dotnet build Beelzebub/Beelzebub.sln -c Release`, `cmp` the DLL, start); `python Beelzebub/tools/vrclient/vrclient.py ensure`; run `transform_chain_guard.vrs` (written before approval), `cast_basic.vrs`, `clearbar_fullreset.vrs`; read the screenshots; then `pwsh Beelzebub/tools/preflight.ps1 -LogCheck` · satisfies D9, D10, D11
7. `chore(release): v0.137.5` — csproj + toml, CHANGELOG.md (drop the oldest entry) + CHANGELOG_FULL.md, README status/caveats + `python Beelzebub/tools/sync_github_readme.py`, BACKLOG row `DONE v0.137.5 (transform-chain-guard): …`; `pwsh Beelzebub/tools/preflight.ps1` → PREFLIGHT OK; `check_transform_guard.py paths backlog` → ok; `git status --porcelain` · satisfies D13, D15, D16, D17

## Rollout
- Ships all at once in v0.137.5 (no flag); a server owner turns it off by installing v0.137.4.
- Backward compatibility: no command or wire change; an old BCH sees the same events, one `transform-phase-shift` later in the async case; admin `testform` now needs a `revert` first when already transformed.
- Rollback: `git revert --no-edit <first>^..<last>` over the audit's `Rollback range:` (D14), stop the server (`taskkill /PID <pid>`), `dotnet build Beelzebub/Beelzebub.sln -c Release` (redeploys the DLL), start per CLAUDE.md procedure 6. No saved-data migration: nothing persisted changes shape.
- Paths this change ships, writes or regenerates (14.4), checked by D15 and D16: `Logic/TransformGate.cs`, `Services/TransformService.cs`, `Commands/TransformCommands.cs`,
  `Beelzebub.Tests/TransformGateTests.cs`,
  `docs/BCH_INTEGRATION_HANDOFF.md`, `docs/BACKLOG.md`, `docs/audits/transform-chain-guard.md`, `docs/dod/profile.md`,
  `docs/dod/transform-chain-guard.md`, `docs/dod/transform-chain-guard.reviews.md`, `docs/dod/transform-chain-guard.review.html`, `docs/dod/transform-chain-guard.html`, `docs/dod/dod-dashboard.html`, `docs/dod/README.md` (index),
  release files (`Beelzebub.csproj`, `thunderstore.toml`, `CHANGELOG.md`, `docs/CHANGELOG_FULL.md`, `README.md`, repo-root `README.md`),
  `tools/check_transform_guard.py` (new), `tools/vrclient/scenarios/transform_chain_guard.vrs` (new);
  `dist/` (gitignored, staged by the build); outside git (D16 checks the repo only; listed for the record): the deployed DLL, the dev server's state.json and logs, the log backup `%TEMP%\beelz-logs-2026-10-04-txguard\`, `%TEMP%\vrclient\` (results, shots).

## Out of scope
- Queuing a refused phase and applying it on the form's spawn (owner decision 2, option B) — excluded 2026-10-04: the window is one or two ticks and the automatic callers retry.
- Keeping `testform`'s one-command revert-and-apply with a deferred apply (owner decision 3, option B) — excluded 2026-10-04.
- The vanilla shapeshift wheel while transformed and sub-form exit (`UpdateBuffsBufferDestroyPatch` re-applies through `ReapplyActiveTransform`, which this slice gates) — no separate change.
- `dev-snapshot`, `docs-consolidation`, `modid-remap-errors` — the next backlog rows (`docs/BACKLOG.md`), each with its own plan.

## Also considered
- Compliance/legal, localisation, running cost: not applicable — chat-only server mod, English text, no service cost.
- Operational ownership: the owner runs the dev server; tester servers get the release notes.
- Documentation and changelog: CHANGELOG entry, README scan, BCH handoff note (D12, D13).
- Analytics: none; the `[Beelz TXGUARD]` lines are the measure.
- Decommissioning: the two inline refusals in `TryActivate` and the revert in `ApplyNativeFormTest` are replaced by the gate (D4).
- Support tooling: `.beelz admin bar <player>` and `.beelz transforms` show the state, unchanged.

## Assumptions
- S-1 · validated · Chaos holds transform unlocks `0: Beatrice the Tailor` and `1: Dracula the Immortal King` · source: plan clearbar-fullreset Log spike 2026-10-04 (`.beelz transforms` as Chaos)
- S-2 · validated · Dracula's phases are named `Warrior`/`Bloodmage` and Morgana (591725925) uses the async serpent form `-1859425781` for both phases · source: `Services/BossFormRegistry.cs:69-99` read 2026-10-04
- S-3 · reversible · the Morgana pending window may close before the second chat command arrives, so D9 (d) proves "no error and the retry lands", not that the refusal text was seen in game; the refusal itself is proven by D1/D4 · fallback: if the window is never hit in game, record it in the audit as covered by unit + wiring evidence only

## Coverage
| # | Layer | Status | Probes | Pointer / reason |
|---|---|---|---|---|
| 1 | Purpose & typical use | Considered | 3/3 | Purpose & typical use |
| 2 | Actors & permissions | Considered | 3/3 | Design › Permissions › 2.1 D6; 2.2 prose: no new unauthorised path, VCF standard deny unchanged; 2.3 D6 |
| 3 | Inputs, outputs & data | Considered | 4/4 | Design › Data › 3.1 D1 D2; 3.2 D3 D12; 3.3 D5 D15 D16; 3.4 D14 |
| 4 | Business rules & invariants | Considered | 5/5 | Business rules › 4.1 D1; 4.2 D4 D5; 4.3 prose: no new timer, only the existing pending window; 4.4 D1; 4.5 D4 D6 |
| 5 | Internal interfaces | Considered | 3/3 | Interfaces › Internal › 5.1 D4; 5.2 D5 D9; 5.3 D3 D12 |
| 6 | External dependencies & contracts | Considered | 3/3 | Interfaces › External › 6.1 D9 D10; 6.2 D1 D4 D5; 6.3 D9 |
| 7 | States & lifecycle | Considered | 3/3 | Design › States › 7.1 D1 D2; 7.2 D1 D4; 7.3 D5 D9 |
| 8 | Minimal stretch | Considered | 2/2 | Use cases › Minimal stretch › 8.1 D2; 8.2 D5 D11 |
| 9 | Maximal stretch | Considered | 3/3 | Use cases › Maximal stretch › 9.1 D5; 9.2 D6; 9.3 D5 D9 |
| 10 | Security & privacy | Considered | 4/4 | Security › 10.1 D6; 10.2 prose: integers matched against unlocks and the registry only; 10.3 D7; 10.4 prose: only the SteamID already logged plus route, reason and unit |
| 11 | Design & UX | Considered | 4/4 | Design › UX › 11.1 D2; 11.2 D2; 11.3 prose: plain chat text, every state in words, under the chat cap; 11.4 D3 D9 |
| 12 | Failure handling & observability | Considered | 4/4 | Failure & observability › 12.1 D2; 12.2 D3; 12.3 D11; 12.4 D1 D2 D3 D4 D5 D6 D7 D8 D12 D14 D16 D17 D18 D19 |
| 13 | Performance & scale | Considered | 2/2 | Performance › 13.1 prose: a refusal returns before any ECS work; 13.2 D5 |
| 14 | Rollout & compatibility | Considered | 4/4 | Rollout › 14.1 D13; 14.2 D10 D12; 14.3 D14; 14.4 D15 D16 |
| 15 | Out of scope | Considered | 2/2 | Out of scope |
Gate — acceptance & testability: passed — every Considered layer 2–14 maps to ≥ 1 D-item

## Baseline

## Amendments

## Log
- 2026-10-04 · status → draft · plan
- 2026-10-04 · note · spike · recon at `a38c168`: `TryActivate` refuses pending (`TransformService.cs:132`) and active (`:192-199`); `ApplyPhase` (`:1025`) falls back to `TransformBuffService.ApplyForm` when `ReapplyFormAbilitiesInPlace` fails, with no pending check; `ApplyNativeFormTest` (`:991-994`) reverts and applies in one frame; `ForceTransform` (`AdminCommands.cs:1792`) requires clear-transform and calls `TryActivate`
- 2026-10-04 · note · dry-run · D1 · n/a · `Logic/TransformGate.cs` and its tests are added in build step 1
- 2026-10-04 · note · dry-run · D2 · n/a · added in build step 1
- 2026-10-04 · note · dry-run · D3 · n/a · added in build step 1
- 2026-10-04 · note · dry-run · D4 · cmd: `python Beelzebub/tools/check_transform_guard.py wiring` → `wiring: FAIL TryActivate: no gate call; ApplyPhase: no gate call; ReapplyActiveTransform: no gate call; ApplyNativeFormTest: no gate call; ApplyNativeFormTest: calls Revert; Phase command: ApplyPhase without or before PhaseGate` (today's code — the real failing case; wired in step 3)
- 2026-10-04 · note · dry-run · D5 · cmd: `python Beelzebub/tools/check_transform_guard.py wiring` → `wiring: FAIL … ApplyPhase: no gate call …` (the D4 run; wired in step 3)
- 2026-10-04 · note · dry-run · D6 · cmd: `python Beelzebub/tools/check_transform_guard.py actors` → `actors: ok, 3 self-only, 2 admin-only, 5 route callers in 4 allowed files`
- 2026-10-04 · note · dry-run · D7 · cmd: `python Beelzebub/tools/check_transform_guard.py secrets` → `secrets: ok, 82 files, 0 hits`
- 2026-10-04 · note · dry-run · D8 · cmd: `python Beelzebub/tools/check_transform_guard.py tests` → `tests: FAIL no input (Beelzebub/Beelzebub.Tests/TransformGateTests.cs missing)` (step 1)
- 2026-10-04 · note · dry-run · D9 · n/a · the guard and its TXGUARD lines exist only after step 3; `scenarios/transform_chain_guard.vrs` is written (14 checks counting `ensure`)
- 2026-10-04 · note · dry-run · D10 · n/a · last run 2026-10-04 on v0.137.4: `SCENARIO PASS cast_basic 6/6`, `SCENARIO PASS clearbar_fullreset 32/32` (clearbar-fullreset Log)
- 2026-10-04 · note · dry-run · D11 · n/a · runs after the step-6 session; last run `PREFLIGHT OK (2 checks)` (clearbar-fullreset step 7)
- 2026-10-04 · note · dry-run · D12 · cmd: `python Beelzebub/tools/check_transform_guard.py handoff` → `handoff: FAIL api=35 banner=35 (want 35) missing=['v0.137.5', 'Still transforming']` (step 4)
- 2026-10-04 · note · dry-run · D13 · n/a · the version line names 0.137.4 until the step-7 release commit
- 2026-10-04 · note · dry-run · D14 · cmd: `python Beelzebub/tools/check_transform_guard.py rollback` → `rollback: FAIL no input (Beelzebub/Beelzebub/docs/audits/transform-chain-guard.md missing)` (step 5)
- 2026-10-04 · note · dry-run · D15 · n/a · the plan files are uncommitted until the plan commit
- 2026-10-04 · note · dry-run · D16 · cmd: `python Beelzebub/tools/check_transform_guard.py paths` → `paths: ok, 3 changed, 24 declared`
- 2026-10-04 · note · dry-run · D17 · cmd: `python Beelzebub/tools/check_transform_guard.py backlog` → `backlog: FAIL the transform-chain-guard row is not marked `DONE v0.137.5`` (step 7)
- 2026-10-04 · note · dry-run · D18 · cmd: `python Beelzebub/tools/check_transform_guard.py profile` → `profile: FAIL no `Probe 6.2` line` (step 4)
- 2026-10-04 · note · dry-run · D19 · cmd: `python Beelzebub/tools/check_transform_guard.py selftest` → `selftest: ok, 9 checks x good/defect/empty`
