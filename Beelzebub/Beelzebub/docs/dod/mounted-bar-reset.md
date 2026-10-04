---
dod: 2
rubric: 2
kind: backlog
id: dod-20261003-m7b3
slug: mounted-bar-reset
title: Mounted bar - visible saddle keys and an honest reset on a horse
status: in-progress
size: M
parent: none
created: 2026-10-03
baselined: 2026-10-04
closed: none
recon_commit: 0a135f5
coverage_author: 15/15 layers · 49/49 probes
coverage_reviewer: 15/15 layers · 49/49 probes
review: human
---

# DoD: Mounted bar - visible saddle keys and an honest reset on a horse

**Size:** M — touches `Logic/`, `Services/`, `Commands/`, the persistence load path and the BCH handoff; no new external dependency; the state.json shape is unchanged (one value moves) (L test failed: no schema change, no new dependency).
**Planned:** interactively (owner approved the decision set in `~/.claude/plans/noble-squishing-lake.md` on 2026-10-03, every recommendation taken: reset dismounts on purpose and says so; saddle slots 5, 6, 7; ApiVersion 33 → 34; a dod plan; push after release)
**Request:** "I completed step 5, and it dismounted me from the horse. ... when I mounted, I did not see the ability that was assigned to the Slot 3. ... for testing purposes, I did reassign the ability to slot 6, and when I mounted the horse, I can see that ability in the C key, slot 6." — and the backlog row `mounted-bar-reset` in `Beelzebub/Beelzebub/docs/BACKLOG.md`.

## Definition of Done
- [ ] D1 · **Saddle slots are R, C and T** `Logic/MountedSlots.cs` `MountedSlots.Allowed` is exactly {5, 6, 7}; `IsValid(slot)` is true for 5, 6, 7 only; `Hint` reads `5, 6, 7 (the R, C, and Ultimate keys)`; `RejectMessage()` returns exactly `Mounted form only uses slots 5, 6, 7 (the R, C, and Ultimate keys) — the other slots are riding controls (Q/E/space) and can't hold a saddle ability. Re-grant this to slot 5, 6, or 7.` · test: Beelzebub.Tests/MountedSlotsTests.cs (fails when: 3 is valid, 5 is invalid, any of 0, 1, 2, 4, 8 is valid, or the reject message differs from the exact text, e.g. names slot 3)
- [ ] D2 · **Old slot-3 binds move to R** `MountedSlots.Migrate(IDictionary<int,int> mounted)` moves a slot-3 bind to slot 5 when slot 5 has no bind, drops it when slot 5 is taken, leaves every other slot untouched, returns what it did (`Moved` / `Dropped` / `None`), and a second call returns `None` · test: Beelzebub.Tests/MountedSlotsTests.cs (fails when: a slot-3 bind survives, a taken slot 5 is overwritten, another slot changes, or a second call reports a change; an empty map returns `None` and stays empty, or 10,000 player maps take 200 ms or more to migrate)
- [ ] D3 · **The move runs at load and is saved** `PersistenceService` load calls `MountedSlots.Migrate` on each player's Mounted form binds before `LoadFormSlotsSnapshot`, logs `[Beelz MOUNT] <steamId> saddle slot 3 -> 5 (moved)` or `(dropped: slot 5 taken)` once per changed player, and calls `RequestSave()` when any player changed · manual: with the server stopped, add `"Mounted": {"3": 1621601748}` under Chaos's (76561198039548286) `FormSlots` in `BepInEx/config/kdpen.Beelzebub/state.json`, start the server; PASS when LogOutput shows `76561198039548286 saddle slot 3 -> 5 (moved)` and, after the next save, state.json holds `"5": 1621601748` and no `"3"` under `Mounted`
- [ ] D4 · **Reset plans a dismount when riding** `BarResetPlanner.Plan(scope, online, liveReady, transform, mounted)` adds `Dismount` only when online, liveReady and mounted, after SaveBindings and before DestroyOverrideSources; `BarResetRunner` calls `IBarResetOps.Dismount()`; `Dismount` is not in `RequiredForClean`, and a failed Dismount makes the run not clean · test: Beelzebub.Tests/BarResetTests.cs (fails when: Dismount is planned for an unmounted, offline or not-liveReady character, is planned after DestroyOverrideSources, a mounted plan is Clean=false only because Dismount is absent from RequiredForClean, or a Dismount that throws leaves Clean=true)
- [ ] D5 · **The reply says you were dismounted** `BarResetReply` adds exactly `You were dismounted to reset your bar; remount to ride.` (PlayerReset) or `<name> was dismounted to reset the bar.` (admin scopes) when `CountOf(Dismount) > 0`, and no such line otherwise · test: Beelzebub.Tests/BarResetReplyTests.cs (fails when: a run with Dismount:1 has no dismount line or a different text, or a run with Dismount:0, Dismount:ERR or no Dismount step has one)
- [ ] D6 · **BCH ApiVersion is 34** `Commands/ApiCommands.cs` `ApiVersion = 34` and the handoff banner reads `ApiVersion = 34` · cmd: `pwsh Beelzebub/tools/preflight.ps1` → `PREFLIGHT OK` with `api banner ApiCommands.cs 34, handoff banner 34` (fails when: the banner and ApiVersion differ — `api banner` FAIL; a handoff with no banner yields no number and FAILs)
- [ ] D7 · **Handoff tells BCH what changed** `Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md` has a `v0.137.3` block containing `api>=34`, `5/6/7` and `You were dismounted`, a version-table row `| 34 | 0.137.3 |`, and the v0.101.0 block's `slots **3/6/7** only` text now ends with `(slots 5/6/7 from v0.137.3)` · cmd: `python Beelzebub/tools/check_mounted_bar.py handoff` → `handoff: ok, api 34, banner 34, 6 tokens` (fails when: any of `| 34 | 0.137.3 |`, `v0.137.3`, `api>=34`, `5/6/7`, `You were dismounted`, `(slots 5/6/7 from v0.137.3)` is missing, the banner differs from `ApiVersion`, or `ApiVersion` is not 34; a missing handoff or ApiCommands.cs prints `FAIL no input`)
- [ ] D8 · **Saddle ability shows on R** on a riding horse a Mounted bind on slot 5 shows on the R key and casts, while Q, E and Space keep the horse's own abilities · manual: connect to 127.0.0.1:9876 as Chaos holding a sword, `.beelz resetbar CONFIRM`, `.beelz admin testmount on Chaos`, mount, `.beelz admin bar-raw Chaos` (PASS: slots 5, 6, 7 each show `set=Empty` from the horse-kit source — the live game still matches rule 1's prefab contract; otherwise stop and re-plan), dismount, `.beelz form-grant mounted 3 1621601748` (PASS: rejected with the D1 text), `.beelz form-grant mounted 5 1621601748`, mount the same horse again; PASS when R shows and casts Knife Throw and Q/E/Space are the horse's
- [ ] D9 · **Reset on a horse dismounts and says so** `.beelz resetbar CONFIRM` while riding dismounts the player, the reply carries the D5 line, the log shows `Dismount:1` before `DestroyOverrideSources:0` with `clean=1`, and remounting gives the vanilla horse kit · manual: after D8 while mounted, `.beelz resetbar CONFIRM`; PASS when the player is on foot, the reply has `You were dismounted`, the `[Beelz RESET]` line has `Dismount:1,DestroyOverrideSources:0` and `clean=1`, a remount shows leap on Q and gallop on E with R blank, then `.beelz admin testmount off Chaos` and `.beelz resetbar CONFIRM` leave `.beelz admin bar Chaos` at binds=0 other=0
- [ ] D10 · **Release surfaces in sync** csproj and thunderstore.toml read 0.137.3, CHANGELOG.md carries a `## [0.137.3]` entry under its size cap, the README status line names v0.137.3 and the root README is regenerated · cmd: `pwsh Beelzebub/tools/preflight.ps1` → `PREFLIGHT OK` with `versions csproj 0.137.3, thunderstore.toml 0.137.3` (fails when: the version fields differ, CHANGELOG.md has no entry for the csproj version, the README status names another version, or the root README is stale)
- [ ] D11 · **Admin paths stay admin-only** every `beelz admin` command (incl. `testmount`, `reset-loadouts`, `purge`, `bar`) carries `adminOnly: true`, `resetbar` takes no target, and every caller of `BarResetService.FullReset`/`ReadBar` is the self-only `ResetBar` handler or an adminOnly method · cmd: `python Beelzebub/tools/check_bar_reset.py auth` → `auth: ok, <n> admin commands, <k> reset callers` (fails when: an admin command loses adminOnly, resetbar gains a player parameter, a patch, service or API handler calls FullReset or ReadBar, or fewer than 50 admin commands parse — a parse that finds none prints FAIL)
- [ ] D12 · **Rollback range recorded** the audit record states the first and last commit of this build and the rollback consequence for migrated state · cmd: `python Beelzebub/tools/check_mounted_bar.py rollback` → `rollback: ok, <first>..<last>` (fails when: the audit is missing (`FAIL no input`), has no `Rollback range: <first>..<last>` line, either id is not a commit, first is not an ancestor of last, the audit lacks any of the rollback steps `git revert`, `dotnet build Beelzebub/Beelzebub.sln -c Release`, `taskkill /PID`, or it does not state the `inert slot-5 bind` consequence)
- [ ] D13 · **Every written path is committed** after the release commit nothing this build wrote is left uncommitted; the owner's two private working files (`Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx`, `_matrix_build.py`) are ignored whether present or not · cmd: `git status --porcelain` → no line naming any other path (fails when: any other path is listed — e.g. an untracked new Logic file, the plan, the reviews file or the audit; run outside the repository, git exits 128 with `not a git repository`, which is a FAIL)
- [ ] D14 · **Backlog row closed** `Beelzebub/Beelzebub/docs/BACKLOG.md` marks `mounted-bar-reset` done in v0.137.3 · file: Beelzebub/Beelzebub/docs/BACKLOG.md contains "| mounted-bar-reset | DONE v0.137.3"
- [ ] D15 · **Only the load path migrates** `MountedSlots.Migrate` is called exactly once outside `Logic/`, from `Services/PersistenceService.cs`; no command, patch, API handler or other service reaches it · cmd: `python Beelzebub/tools/check_mounted_bar.py callers` → `callers: ok, <n> files, 1 call (Beelzebub/Beelzebub/Services/PersistenceService.cs:<line>)` (fails when: the call is missing, a second call exists anywhere, or the one call is outside PersistenceService.cs; no .cs files prints `FAIL no input`)
- [ ] D16 · **No credential in the plugin** no C# source under `Beelzebub/Beelzebub` (the only code that writes state.json, replies and log lines) holds a credential-shaped literal (`password|api_key|secret|bearer|access_token = "<8+ chars>"`) · cmd: `python Beelzebub/tools/check_mounted_bar.py secrets` → `secrets: ok, <n> files, 0 hits` (fails when: any such literal appears; no .cs files prints `FAIL no input`)
- [ ] D17 · **Player commands stay self-only** `form-grant` (`FormGrant`) and `resetbar` (`ResetBar`) take no player/target/name/steam parameter and are not `adminOnly`, so a player can only change their own bar · cmd: `python Beelzebub/tools/check_mounted_bar.py selfonly` → `selfonly: ok, 2 self-only commands` (fails when: either command gains a target-like parameter, becomes adminOnly, or is not found; a missing BeelzCommands.cs prints `FAIL no input`)
- [ ] D18 · **Every touched path is declared** every path changed since the recon commit `0a135f5` (tracked diff plus untracked, minus the two owner files) is named in the plan's Rollout path list · cmd: `python Beelzebub/tools/check_mounted_bar.py paths` → `paths: ok, <n> changed, <k> declared` (fails when: a changed path is not declared, or the Rollout path list is missing; nothing changed prints `FAIL no input`)

## Purpose & typical use
A player who rides a horse can bind captured abilities to the saddle bar (`.beelz form-grant mounted <slot> <ability>`)
and can reset a stuck bar (`.beelz resetbar CONFIRM`). Two things went wrong on 2026-10-03:
- The saddle offered slots 3, 6 and 7. Slot 3 is not a key the game client draws, so a slot-3 bind was live on the
  server but invisible. R is slot 5.
- A reset while riding silently threw the player off the horse, and still reported `clean=1`.

This slice makes the saddle offer the keys a player can actually see (R, C, T), carries old slot-3 binds over to R,
and makes the reset's dismount deliberate and announced instead of accidental. It extends the existing Mounted form
(v0.101.0) and the layered reset (v0.137.0, plan `bar-reset`); it adds no new user or job.

## Use cases
### Typical
- A player runs `.beelz form-grant mounted 5 1621601748`, mounts, and sees Knife Throw on R (D1, D8).
- A player whose saddle bar is stuck runs `.beelz resetbar CONFIRM` while riding; they land on foot, the reply says so,
  and their bar is vanilla (D4, D5, D9).
- An admin runs `.beelz admin reset-loadouts <player> CONFIRM` on a riding player; the reply says the player was
  dismounted (D5).
### Minimal stretch
- A player with no Mounted binds mounts: nothing changes; the horse kit shows as vanilla (`BuildMountedBar` returns an
  empty bar, no injection — unchanged code).
- A player with no horse runs `resetbar`: no Dismount step is planned and the reply has no dismount line (D4, D5).
- A state.json with no `FormSlots` or no `Mounted` entry: `Migrate` sees an empty map and returns `None` (D2).
- A malformed Mounted entry: a slot key that is not an int is skipped by the existing parser
  (`PersistenceService` FormSlots loop); an int slot outside {3, 5, 6, 7} is left alone by `Migrate` and filtered out
  of the saddle bar by `BuildMountedBar` (D1, D2).
### Maximal stretch
- A server with many players holding slot-3 binds: the migration is one dictionary pass per player at load, one log
  line per changed player, and one save request in total (D3). Supported bound: 10,000 players in state.json, migrated
  in under 200 ms (D2 measures it; tester servers hold tens, so this is 100× and more). Above the bound the cost stays
  linear and the migration still completes; the only degradation is a longer load and one log line per changed player,
  written once (a migrated file has nothing left to move).
- A player who binds R, C and T and also had a slot-3 bind: slot 5 is taken, so the slot-3 bind is dropped and logged
  `dropped: slot 5 taken` (D2, D3).
- Repeated resets while riding: the first dismounts; the next ones find no mount and plan no Dismount (D4).

## Business rules
1. The saddle slots are exactly 5 (R), 6 (C) and 7 (T). Source of truth: `MountedSlots.Allowed` (D1). The evidence:
   every `AB_Interact_Mount_Owner_Buff_*` prefab in `Reference Data/Prefabs/` ends slots 5, 6 and 7 with an Empty
   row (the basic horse puts its thrust on 5 then Empty; the vampire horses put it on 7 then Empty), and the
   2026-10-03 live log showed `slot=5/6/7 set=Empty source=318712:2` while mounted. Slots 1 (Q, leap), 2 (Space,
   vampire-horse jump), 4 (E, gallop) and 0 (primary) belong to the horse and are never offered. Slot 3 is never
   drawn by the client (owner test 2026-10-03; unmounted `admin bar` shows it with no source).
2. A saved Mounted bind on slot 3 moves to slot 5 if slot 5 has none; otherwise it is dropped. It never overwrites a
   bind the player chose (D2). The move runs on every load and is idempotent: a migrated file has no slot 3 left.
3. A reset while riding dismounts the player through the `Dismount` step, counted and announced (D4, D5). The orphan
   sweep (`DestroyOwnedAbilitySlotOrphans`) is unchanged: after a successful Dismount it finds no mount buff; if
   Dismount failed, the sweep may still remove it, and the run is already not clean (D4).
4. Precedence: when the player's own bind on slot 5 and a migrated slot-3 bind collide, the player's slot-5 bind wins
   (rule 2, D2). When the horse kit and a saddle bind share a slot (5, 6, 7), the saddle bind wins — that is the
   purpose of the bind (existing `ApplyMountedLoadout` priority 100, shown in game by D8). The owner decides any
   exception; there is no per-server override.
5. Time: none of these rules depend on time; the migration has no cut-off and runs on every load (a no-op once done).
6. "Every mount buff" (rules 3 and the mounted check) is `ShapeshiftAbilityService.IsMountBuff`: the 6 GUIDs in
   `_mountBuffs` plus any buff whose prefab name contains `Interact_Mount_Owner_Buff`. The set was computed by
   listing `Reference Data/Prefabs/` for `AB_Interact_Mount_Owner_Buff*` (6 files, 2026-10-03; the dump is a static
   export, nothing in this build adds to it). A future mount under another name would be missed: the reset would not
   plan a Dismount and the sweep would take the buff as before (a silent dismount), which a D9-style log shows as
   `DestroyOverrideSources:1` with no `Dismount`.

## Interfaces
### Internal — reads / writes / changes (paths or symbols)
- Code read for this plan (repo paths): `Beelzebub/Beelzebub/Logic/BarReset.cs`, `Beelzebub/Beelzebub/Logic/BarResetReply.cs`,
  `Beelzebub/Beelzebub/Services/BarResetService.cs`, `Beelzebub/Beelzebub/Services/ShapeshiftAbilityService.cs`,
  `Beelzebub/Beelzebub/Services/PersistenceService.cs`, `Beelzebub/Beelzebub/Services/TransformBuffService.cs`.
- New `Beelzebub/Beelzebub/Logic/MountedSlots.cs` (`Allowed`, `IsValid`, `Hint`, `RejectMessage`, `Migrate`), linked
  into `Beelzebub.Tests` the way the other `Logic/*.cs` files are (D1, D2).
- `Services/ShapeshiftAbilityService.cs`: `_mountedSlots`, `IsValidMountedSlot`, `MountedSlotsHint` read
  `MountedSlots`; new `public static bool IsMountedAny(Entity character)` (BuffBuffer scan with `IsMountBuff`, moved
  from the private `SlotApply.MountedAny`, which then calls it) (D1, D4).
- `Commands/BeelzCommands.cs:943-945` (form-grant reject) replies `MountedSlots.RejectMessage()` (D1, D8).
- `Services/PersistenceService.cs` (FormSlots load, ~line 104) calls `MountedSlots.Migrate` (D3).
- `Logic/BarReset.cs`: `BarResetStep.Dismount`; `BarResetPlanner.Plan(..., bool mounted = false)`;
  `IBarResetOps.Dismount()`; runner `Invoke` case (D4). `Logic/BarResetReply.cs`: the dismount line (D5).
- `Services/BarResetService.cs`: `FullReset` passes `ShapeshiftAbilityService.IsMountedAny(character)`; `Dismount()`
  destroys each live BuffBuffer buff with `IsMountBuff` via `TransformBuffService.SafeDestroyBuff`, returns the
  count, throws when a mount buff is still live afterwards (D4).
- Twin left unchanged on purpose: `AdminCommands` `rebuildslots` (`Commands/AdminCommands.cs:1584`) still calls
  `DestroyOwnedAbilitySlotOrphans`, which still dismounts a rider; it is a `LEGACY:` command (bar-reset D14).
- What breaks if wrong: a wrong slot set hides saddle binds or overrides a riding key (D8 shows it); a missing Dismount
  leaves the mount buff to the sweep (back to a silent dismount; D9 shows `DestroyOverrideSources:1`).
- Contract change (BCH): the valid `form-grant mounted <slot>` values change from {3,6,7} to {5,6,7}; the reset reply
  gains one line; `ApiVersion` 33 → 34 (D6, D7). The `[BEELZ:form-slot]` line shape is unchanged; its `slot=` field
  can now carry 5. No other field is added or removed.
- Test fakes in `BarResetTests` and `BarResetReplyTests` gain a `Dismount()` member (D4, D5).
### External — dependencies and their failure behaviour
- V Rising server ECS (`BuffBuffer`, `ReplaceAbilityOnSlotBuff`, the mount control buffs). If the mount buff cannot be
  destroyed, `Dismount` throws, the runner records `Dismount:ERR`, the remaining steps still run (bar-reset D12), the
  run is not clean and the reply lists `failed: Dismount` (D4, D5).
- Persistence: if the save after a migration fails, the dirty flag stays set and the heartbeat retries (existing
  `PersistenceService`); a crash before any save leaves the old file, and the next load migrates again (idempotent, D2).
- The prefab dump (`Reference Data/Prefabs/AB_Interact_Mount_Owner_Buff_*`, all 6 record types read 2026-10-03) is
  the input contract for rule 1. Supported game: `VRisingServer v1.1.15.0-r101082` (Steam build 25169871, the dev
  server's boot line 2026-10-04); no other build is claimed. D8 first runs `.beelz admin bar-raw Chaos` while mounted
  and requires slots 5, 6, 7 to read `set=Empty` from the horse-kit source; if a game patch changes that, D8 fails at
  that step and the slice is re-planned rather than shipped (a hidden or overridden key is the symptom in the field).
- Test mode: `.beelz admin testmount on|off` (admin-only, D11) spawns and removes a test horse; it exists today and is
  used only by D8/D9. If it fails, the test is rerun on a real owned horse.
- Collaborators: the Codex post-audit is a review aid, not a runtime dependency; if it cannot read the diff, its
  verdict is discarded and rerun with the diff on stdin.

## Design
### Data
- state.json (`BepInEx/config/kdpen.Beelzebub/state.json`, owned by the plugin, one file per server, kept until a reset
  or `admin purge` clears the binds): `players[].FormSlots.Mounted` is a slot → ability map. The migration rewrites
  `"3"` to `"5"` (or removes `"3"`) in memory at load and requests a save; the format version (8) does not change (D3).
- By-products: the `[Beelz MOUNT]` migration lines live in `BepInEx/LogOutput.log`, overwritten by the next server
  start (the procedure backs both logs up to `%TEMP%\beelz-logs-<date>-<label>\` first; those copies are the owner's,
  left for the OS temp cleanup). The audit record and the plan/reviews files are kept in git (D12, D13).
### States
- Unmounted: saddle binds are stored, not shown. Mounted: `TickMountedForms` injects them on 5/6/7.
- Reset while mounted: Dismount → the mount handler logs `dismounted (control buff gone)` and forgets the saddle
  snapshot → the remaining reset steps run on a foot bar (D4, D9).
- Concurrency: a reset runs synchronously inside one VCF command on the server main thread (the 2026-10-03 runs took
  6 ms), so no remount, disconnect or second reset can interleave between planning and Dismount. The migration runs
  on load before any player connects.
- Interrupted: a server stop before the save leaves state.json untouched; the next load migrates again (D2).
- Correction while riding: a `form-grant mounted` change while mounted is saved at once and shown on the next
  mount (the existing reply says "re-enter to apply changes"); a reset clears it. There is no undo of a reset other
  than re-granting; a Dismount cannot be undone except by remounting.
### Permissions
- Actors and paths (2.1): (a) a connected player, authenticated by Steam before the game lets them in, reaches only
  `.beelz form-grant mounted` and `.beelz resetbar` on their own SteamID; (b) an admin (VCF admin list) also reaches the
  `adminOnly` commands; (c) an unauthenticated caller has no path — the game drops the connection before chat exists;
  (d) the server console and RCON do not run VCF chat commands, so they reach nothing here; (e) no service, patch, API
  handler or timer calls the reset outside the commands (D11) or the migration outside the load path (D15). The
  migration runs as the server process at load, on data the server owns.
- `.beelz form-grant mounted` is a player command on the caller's own binds (unchanged). `.beelz resetbar` resets the
  caller only; `.beelz admin reset-loadouts` / `admin purge` / `admin testmount` / `admin bar` are `adminOnly: true`
  (D11). A non-admin who types an admin command gets VCF's standard deny; nothing is logged by Beelzebub. The
  migration has no command surface. Ownership: binds are keyed by SteamID; an admin reset targets the character
  resolved when the command runs; a target that disconnects before it is handled by the existing offline path
  (bar-reset D11: saved state only). The migration only moves a player's own bind within their own map.
### UX
- Discovery: the form-grant reply and reject text (D1, D8), the saddle bar itself (D8), the reset reply (D5).
- Feedback: reject → the D1 text; reset on a horse → `You were dismounted to reset your bar; remount to ride.` (D5).
  No new screen; chat only, read as plain text; nothing depends on colour.
- Activation: the migration runs on every load without being asked (D3); the dismount line appears only when a
  Dismount ran — never on a foot reset (D5).

## Security
- Authorization is unchanged on every path, and `check_bar_reset.py auth` keeps it so (D11).
- Inputs: slot numbers are ints parsed by VCF; the ability id is checked by the existing form-grant validation. Nothing
  reaches a shell, query or URL.
- Secrets: this slice reads, stores and logs no credential, and the plugin holds none (D16 scans every C# source
  under `Beelzebub/Beelzebub`, the only code that writes state.json, replies and log lines). Its only new outputs are fixed texts asserted exactly by
  tests (the reject text D1, the dismount line D5) and the migration line (D3), which carries the SteamID the plugin
  already logs on every `[Beelz]` line. No rotation applies.

## Failure & observability
- `Dismount` failure: `[Beelz RESET] run=<n> step Dismount failed: <reason>` warning, `Dismount:ERR`, the reply lists
  `failed: Dismount` (D4, D5).
- Migration: one `[Beelz MOUNT] <steamId> saddle slot 3 -> 5 (...)` line per changed player (D3); none otherwise.
- How we know it is broken in production: a reset on a horse whose `[Beelz RESET]` line shows
  `DestroyOverrideSources:1` with no `Dismount`, or a tester report of a blank saddle key; `preflight.ps1 -LogCheck`
  reads each session log (D9).
- Gating controls and the one command behind each (12.4):

  | Probe | Command | Fails when |
  |---|---|---|
  | 2.1 | `python Beelzebub/tools/check_mounted_bar.py selfonly` (D17), with `check_bar_reset.py auth` (D11) for the admin side | an admin command loses adminOnly, resetbar gains a target, or a non-command caller reaches FullReset/ReadBar; zero parsed → FAIL |
  | 3.3 | `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~MountedSlotsTests` (D2) | a slot-3 bind survives, a taken slot 5 is overwritten, or a second migrate changes the map |
  | 4.4 | `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~MountedSlotsTests` (D2) | a taken slot 5 is overwritten |
  | 6.2 | `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetTests` (D4) | a throwing Dismount leaves Clean=true |
  | 10.1 | `python Beelzebub/tools/check_mounted_bar.py callers` (D15) | the migration gains a caller outside the load path |
  | 10.3 | `python Beelzebub/tools/check_mounted_bar.py secrets` (D16) | a credential-shaped literal appears in the plugin's C# |
  | 12.4 | each check's planted fault (Log `note · planted`) | the planted fault does not fail its check |
  | 14.3 | `python Beelzebub/tools/check_mounted_bar.py rollback` (D12) | the range is missing or unresolvable, or the slot-5 consequence is not stated |
  | 14.4 | `python Beelzebub/tools/check_mounted_bar.py paths` (D18), with `git status --porcelain` (D13) | a touched path is undeclared (D18) or uncommitted (D13) |

  Every check above has been run on the draft: the test commands pass, and each one's fault was planted once and
  failed it (Log, 2026-10-04 `note · planted`); `handoff`, `callers` and `rollback` FAIL today because what they
  check is built in steps 3, 7 and 8 — that is their real failing output, recorded as their dry run.

## Performance
- The migration is O(players) at load; the reset adds one BuffBuffer scan (≤ ~100 buffs) to a command that already
  scans every slot. No hot path changes; `TickMountedForms` is unchanged.
- Bounds: three saddle slots (rule 1); the slot set is the bound and excludes slot 3 deliberately (D1).

## Build plan
1. Create `Beelzebub/Beelzebub/Logic/MountedSlots.cs` with `Allowed = {5,6,7}`, `IsValid`, `Hint`, `RejectMessage()`,
   `Migrate(IDictionary<int,int>) → MigrateResult {None, Moved, Dropped}`; link it in
   `Beelzebub/Beelzebub.Tests/Beelzebub.Tests.csproj` like the other `Logic/*.cs` files · satisfies D1, D2
2. Add `Beelzebub/Beelzebub.Tests/MountedSlotsTests.cs` with the `fails when` cases of D1 and D2 named
   `X_fails_when_<defect>`. Plant each fault once (allow slot 3; drop the slot-5-taken guard), see the test fail,
   restore; log `note · planted · D<n> · ...` · satisfies D1, D2
3. Wire D1: `ShapeshiftAbilityService._mountedSlots`, `IsValidMountedSlot`, `MountedSlotsHint` read `MountedSlots`;
   `BeelzCommands` form-grant reject replies `MountedSlots.RejectMessage()`; move `SlotApply.MountedAny` to
   `ShapeshiftAbilityService.IsMountedAny` (public) and call it from `SlotApply`. Wire D3 in `PersistenceService`
   FormSlots load: call `Migrate` on the Mounted map, log, and `RequestSave()` after the load when any changed ·
   satisfies D1, D3, D15
4. `Logic/BarReset.cs`: add `BarResetStep.Dismount`, the `mounted` planner parameter (default false), the ops member
   and the runner case. `BarResetService`: pass `IsMountedAny`, implement `Dismount()`. Extend `BarResetTests` (fake
   ops member + D4 cases); plant the fault (plan Dismount after DestroyOverrideSources) once · satisfies D4
5. `Logic/BarResetReply.cs`: the dismount line for self and admin scopes; `BarResetReplyTests` cases for D5; plant
   (drop the CountOf guard) once · satisfies D5
6. Run `python Beelzebub/tools/check_bar_reset.py` (all checks incl. `commands`, `auth`, `selftest`); fix any check
   that fails because of the new `IBarResetOps.Dismount` member. Run `python Beelzebub/tools/check_mounted_bar.py
   callers`, `secrets` and `selfonly` (all must print `ok`) · satisfies D11, D15, D16, D17
7. `Commands/ApiCommands.cs` `ApiVersion = 34`; handoff: banner, v0.137.3 block (slots 5/6/7, `api>=34`, the dismount
   reply line, "BCH: offer R/C/T for Mounted when api>=34, else 3/6/7"), the version-table row `| 34 | 0.137.3 |`, and
   `(slots 5/6/7 from v0.137.3)` after the v0.101.0 block's `slots **3/6/7** only` text; then
   `python Beelzebub/tools/check_mounted_bar.py handoff` must print `ok` · satisfies D6, D7
8. Release build `dotnet build Beelzebub/Beelzebub.sln -c Release -p:VRisingServerPath="C:/nonexistent"`, `dotnet test`,
   then post-audit `Beelzebub/Beelzebub/docs/audits/mounted-bar-reset.md` (Codex on the diff via stdin, ≤ 3 rounds,
   one commit per round), with a `Rollback range: <first>..<last>` line and the sentence stating that after a rollback
   a migrated save holds an `inert slot-5 bind`; `python Beelzebub/tools/check_mounted_bar.py rollback` must print `ok`
   · satisfies D1, D2, D4, D5, D12
9. Deploy per CLAUDE.md procedure 6 (back up both logs to `%TEMP%\beelz-logs-2026-10-03-mounted\`, stop, build,
   `cmp` the DLL); before starting, add the D3 fixture bind to state.json; start; read the log for D3; hand the owner
   the D8/D9 steps; monitor `[Beelz (MOUNT|RESET|BAR|GRANT)]` · satisfies D3, D8, D9
10. After the session: `pwsh Beelzebub/tools/preflight.ps1 -LogCheck`, read every `[Error`/`[Warning` in both logs.
    Then `chore(release): v0.137.3` — csproj + toml, CHANGELOG.md (drop the oldest entry) + CHANGELOG_FULL.md, README
    status + `python Beelzebub/tools/sync_github_readme.py`, BACKLOG row DONE; `pwsh Beelzebub/tools/preflight.ps1`
    → PREFLIGHT OK; `python Beelzebub/tools/check_mounted_bar.py paths` → ok; `git status --porcelain` · satisfies D6,
    D10, D13, D14, D18

## Rollout
- Ships all at once in v0.137.3 (no flag); the server owner turns it off by installing v0.137.2.
- Backward compatibility: an old BCH (api 33) still offers 3/6/7; slot 3 is now rejected with the D1 text naming
  5/6/7, so the user sees why. Saved slot-3 binds are migrated (D3). Other forms, weapons and the foot reset are
  unchanged.
- Rollback: `git revert --no-edit <first>^..<last>` over the range recorded in the audit as `Rollback range:` (D12), stop
  the server (`taskkill /PID <pid>`), `dotnet build Beelzebub/Beelzebub.sln -c Release` (redeploys the DLL),
  then start the server per CLAUDE.md procedure 6. After the migration has written state.json, v0.137.2 reads a Mounted bind on slot 5, which its
  `BuildMountedBar` filters out (`Array.IndexOf(_mountedSlots, slot) >= 0` with {3,6,7}): an inert slot-5 bind, not
  harmful, and `resetbar` clears it. The reset change writes nothing persistent.
- Paths this change ships, writes or regenerates (14.4), all checked by D13 and D18: `Logic/MountedSlots.cs`,
  `Logic/BarReset.cs`, `Logic/BarResetReply.cs`, `Services/{ShapeshiftAbilityService,SlotApply,PersistenceService,
  BarResetService}.cs`, `Commands/{BeelzCommands,ApiCommands}.cs`,
  `Beelzebub.Tests/{MountedSlotsTests,BarResetTests,BarResetReplyTests}.cs`, `Beelzebub.Tests/Beelzebub.Tests.csproj`,
  `docs/BCH_INTEGRATION_HANDOFF.md`, `docs/BACKLOG.md`, `docs/audits/mounted-bar-reset.md`,
  `docs/dod/mounted-bar-reset.md`, `docs/dod/mounted-bar-reset.reviews.md`, `docs/dod/mounted-bar-reset.review.html` (review page), `docs/dod/README.md` (index), release
  files (`Beelzebub.csproj`, `thunderstore.toml`, `CHANGELOG.md`, `docs/CHANGELOG_FULL.md`, `README.md`, repo-root
  `README.md`), `dist/` (gitignored, staged by the build), `tools/check_mounted_bar.py` (new), `tools/check_bar_reset.py` only if step 6 needs it; outside
  git: the deployed DLL and the dev server's `state.json` and logs.

## Out of scope
- Keeping the rider mounted through a reset (owner decision option B) — excluded 2026-10-03: the horse kit's mod
  source has no prefab, so telling it from a leak needs a new rule, and the result would not be a vanilla bar.
- Per-horse saddle slots (keeping a vampire horse's thrust on T) — excluded: rule 1's evidence says T ends Empty on
  every horse. If D8 on a vampire horse ever shows a thrust on T, that is a new backlog item, not part of this slice.
- `rebuildslots` dismounting a rider — excluded, a LEGACY command; the reset is the supported path.
- `clearbar-fullreset`, `transform-chain-guard`, `modid-remap-errors` — the next backlog rows (`docs/BACKLOG.md`),
  each with its own plan.

## Also considered
- Compliance/legal, localisation, running cost: not applicable — a chat-only server mod, English text, no service cost.
- Operational ownership: the owner runs the dev server; tester servers get the release notes.
- Documentation and changelog: CHANGELOG entry, README caveat scan and the BCH handoff block (D7, D10).
- Analytics: none; the log lines in Failure & observability are the measure.
- Decommissioning: the slot-3 saddle path is removed, and its saved data migrates (D3).
- Support tooling: `.beelz admin bar <player>` already shows the saddle binds as `bind=form:Mounted`.

## Assumptions
- S-1 · validated · slot 5 is the R key, 6 is C, 7 is T, and slot 3 is not drawn · source: owner test 2026-10-03 (slot-3 bind invisible, slot-6 bind on C) + `admin bar` unmounted (slot 5 Shadowbolt from the spellbook on R)
- S-2 · reversible · on a vampire horse slot 7 resolves to Empty, not the thrust, so binding T overrides nothing the rider uses · fallback: drop 7 from `MountedSlots.Allowed`, extend `Migrate` to move or drop slot-7 binds like slot 3, update the D1/D2 tests, the reject text and the handoff, and bump ApiVersion again — about one build step plus a release
- S-3 · validated · every mount control buff in the game data is matched by `ShapeshiftAbilityService.IsMountBuff` · source: `Reference Data/Prefabs/AB_Interact_Mount_Owner_Buff_*` (6 files, all in `_mountBuffs`) read 2026-10-03

## Coverage
| # | Layer | Status | Probes | Pointer / reason |
|---|---|---|---|---|
| 1 | Purpose & typical use | Considered | 3/3 | Purpose & typical use |
| 2 | Actors & permissions | Considered | 3/3 | Design › Permissions › 2.1 D11 D15 D17; 2.2 prose: VCF standard deny for non-admins, unchanged; 2.3 D2 D11 |
| 3 | Inputs, outputs & data | Considered | 4/4 | Design › Data › 3.1 D1 D2; 3.2 D5 D7; 3.3 D2 D3 D13; 3.4 D2 D3 |
| 4 | Business rules & invariants | Considered | 5/5 | Business rules › 4.1 D1; 4.2 D2; 4.3 D2; 4.4 D2 D8; 4.5 D4 D9 |
| 5 | Internal interfaces | Considered | 3/3 | Interfaces › Internal › 5.1 D4; 5.2 D9; 5.3 D6 D7 |
| 6 | External dependencies & contracts | Considered | 3/3 | Interfaces › External › 6.1 D8; 6.2 D2 D4; 6.3 D8 D11 |
| 7 | States & lifecycle | Considered | 3/3 | Design › States › 7.1 D2 D4; 7.2 D4; 7.3 D2 D3 |
| 8 | Minimal stretch | Considered | 2/2 | Use cases › Minimal stretch › 8.1 D2 D4 D5; 8.2 D3 |
| 9 | Maximal stretch | Considered | 3/3 | Use cases › Maximal stretch › 9.1 D2 D3; 9.2 D1 D2; 9.3 D2 D4 |
| 10 | Security & privacy | Considered | 4/4 | Security › 10.1 D11 D15; 10.2 prose: slot ints and ability ids are validated by VCF and form-grant; 10.3 D16; 10.4 prose: only the SteamID already on every log line |
| 11 | Design & UX | Considered | 4/4 | Design › UX › 11.1 D1 D8; 11.2 D1 D5; 11.3 prose: chat text only, nothing colour-dependent; 11.4 D3 D5 |
| 12 | Failure handling & observability | Considered | 4/4 | Failure & observability › 12.1 D4 D5; 12.2 D3 D9; 12.3 D9; 12.4 D1 D2 D4 D5 D7 D11 D12 D13 D15 D16 D17 D18 |
| 13 | Performance & scale | Considered | 2/2 | Performance › 13.1 prose: one load pass and one buff scan per reset, no hot path; 13.2 D1 |
| 14 | Rollout & compatibility | Considered | 4/4 | Rollout › 14.1 D10; 14.2 D1 D3; 14.3 D12; 14.4 D13 D18 |
| 15 | Out of scope | Considered | 2/2 | Out of scope |
Gate — acceptance & testability: passed — every Considered layer 2–14 maps to ≥ 1 D-item

## Baseline
- [ ] D1 · **Saddle slots are R, C and T** `Logic/MountedSlots.cs` `MountedSlots.Allowed` is exactly {5, 6, 7}; `IsValid(slot)` is true for 5, 6, 7 only; `Hint` reads `5, 6, 7 (the R, C, and Ultimate keys)`; `RejectMessage()` returns exactly `Mounted form only uses slots 5, 6, 7 (the R, C, and Ultimate keys) — the other slots are riding controls (Q/E/space) and can't hold a saddle ability. Re-grant this to slot 5, 6, or 7.` · test: Beelzebub.Tests/MountedSlotsTests.cs (fails when: 3 is valid, 5 is invalid, any of 0, 1, 2, 4, 8 is valid, or the reject message differs from the exact text, e.g. names slot 3)
- [ ] D2 · **Old slot-3 binds move to R** `MountedSlots.Migrate(IDictionary<int,int> mounted)` moves a slot-3 bind to slot 5 when slot 5 has no bind, drops it when slot 5 is taken, leaves every other slot untouched, returns what it did (`Moved` / `Dropped` / `None`), and a second call returns `None` · test: Beelzebub.Tests/MountedSlotsTests.cs (fails when: a slot-3 bind survives, a taken slot 5 is overwritten, another slot changes, or a second call reports a change; an empty map returns `None` and stays empty, or 10,000 player maps take 200 ms or more to migrate)
- [ ] D3 · **The move runs at load and is saved** `PersistenceService` load calls `MountedSlots.Migrate` on each player's Mounted form binds before `LoadFormSlotsSnapshot`, logs `[Beelz MOUNT] <steamId> saddle slot 3 -> 5 (moved)` or `(dropped: slot 5 taken)` once per changed player, and calls `RequestSave()` when any player changed · manual: with the server stopped, add `"Mounted": {"3": 1621601748}` under Chaos's (76561198039548286) `FormSlots` in `BepInEx/config/kdpen.Beelzebub/state.json`, start the server; PASS when LogOutput shows `76561198039548286 saddle slot 3 -> 5 (moved)` and, after the next save, state.json holds `"5": 1621601748` and no `"3"` under `Mounted`
- [ ] D4 · **Reset plans a dismount when riding** `BarResetPlanner.Plan(scope, online, liveReady, transform, mounted)` adds `Dismount` only when online, liveReady and mounted, after SaveBindings and before DestroyOverrideSources; `BarResetRunner` calls `IBarResetOps.Dismount()`; `Dismount` is not in `RequiredForClean`, and a failed Dismount makes the run not clean · test: Beelzebub.Tests/BarResetTests.cs (fails when: Dismount is planned for an unmounted, offline or not-liveReady character, is planned after DestroyOverrideSources, a mounted plan is Clean=false only because Dismount is absent from RequiredForClean, or a Dismount that throws leaves Clean=true)
- [ ] D5 · **The reply says you were dismounted** `BarResetReply` adds exactly `You were dismounted to reset your bar; remount to ride.` (PlayerReset) or `<name> was dismounted to reset the bar.` (admin scopes) when `CountOf(Dismount) > 0`, and no such line otherwise · test: Beelzebub.Tests/BarResetReplyTests.cs (fails when: a run with Dismount:1 has no dismount line or a different text, or a run with Dismount:0, Dismount:ERR or no Dismount step has one)
- [ ] D6 · **BCH ApiVersion is 34** `Commands/ApiCommands.cs` `ApiVersion = 34` and the handoff banner reads `ApiVersion = 34` · cmd: `pwsh Beelzebub/tools/preflight.ps1` → `PREFLIGHT OK` with `api banner ApiCommands.cs 34, handoff banner 34` (fails when: the banner and ApiVersion differ — `api banner` FAIL; a handoff with no banner yields no number and FAILs)
- [ ] D7 · **Handoff tells BCH what changed** `Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md` has a `v0.137.3` block containing `api>=34`, `5/6/7` and `You were dismounted`, a version-table row `| 34 | 0.137.3 |`, and the v0.101.0 block's `slots **3/6/7** only` text now ends with `(slots 5/6/7 from v0.137.3)` · cmd: `python Beelzebub/tools/check_mounted_bar.py handoff` → `handoff: ok, api 34, banner 34, 6 tokens` (fails when: any of `| 34 | 0.137.3 |`, `v0.137.3`, `api>=34`, `5/6/7`, `You were dismounted`, `(slots 5/6/7 from v0.137.3)` is missing, the banner differs from `ApiVersion`, or `ApiVersion` is not 34; a missing handoff or ApiCommands.cs prints `FAIL no input`)
- [ ] D8 · **Saddle ability shows on R** on a riding horse a Mounted bind on slot 5 shows on the R key and casts, while Q, E and Space keep the horse's own abilities · manual: connect to 127.0.0.1:9876 as Chaos holding a sword, `.beelz resetbar CONFIRM`, `.beelz admin testmount on Chaos`, mount, `.beelz admin bar-raw Chaos` (PASS: slots 5, 6, 7 each show `set=Empty` from the horse-kit source — the live game still matches rule 1's prefab contract; otherwise stop and re-plan), dismount, `.beelz form-grant mounted 3 1621601748` (PASS: rejected with the D1 text), `.beelz form-grant mounted 5 1621601748`, mount the same horse again; PASS when R shows and casts Knife Throw and Q/E/Space are the horse's
- [ ] D9 · **Reset on a horse dismounts and says so** `.beelz resetbar CONFIRM` while riding dismounts the player, the reply carries the D5 line, the log shows `Dismount:1` before `DestroyOverrideSources:0` with `clean=1`, and remounting gives the vanilla horse kit · manual: after D8 while mounted, `.beelz resetbar CONFIRM`; PASS when the player is on foot, the reply has `You were dismounted`, the `[Beelz RESET]` line has `Dismount:1,DestroyOverrideSources:0` and `clean=1`, a remount shows leap on Q and gallop on E with R blank, then `.beelz admin testmount off Chaos` and `.beelz resetbar CONFIRM` leave `.beelz admin bar Chaos` at binds=0 other=0
- [ ] D10 · **Release surfaces in sync** csproj and thunderstore.toml read 0.137.3, CHANGELOG.md carries a `## [0.137.3]` entry under its size cap, the README status line names v0.137.3 and the root README is regenerated · cmd: `pwsh Beelzebub/tools/preflight.ps1` → `PREFLIGHT OK` with `versions csproj 0.137.3, thunderstore.toml 0.137.3` (fails when: the version fields differ, CHANGELOG.md has no entry for the csproj version, the README status names another version, or the root README is stale)
- [ ] D11 · **Admin paths stay admin-only** every `beelz admin` command (incl. `testmount`, `reset-loadouts`, `purge`, `bar`) carries `adminOnly: true`, `resetbar` takes no target, and every caller of `BarResetService.FullReset`/`ReadBar` is the self-only `ResetBar` handler or an adminOnly method · cmd: `python Beelzebub/tools/check_bar_reset.py auth` → `auth: ok, <n> admin commands, <k> reset callers` (fails when: an admin command loses adminOnly, resetbar gains a player parameter, a patch, service or API handler calls FullReset or ReadBar, or fewer than 50 admin commands parse — a parse that finds none prints FAIL)
- [ ] D12 · **Rollback range recorded** the audit record states the first and last commit of this build and the rollback consequence for migrated state · cmd: `python Beelzebub/tools/check_mounted_bar.py rollback` → `rollback: ok, <first>..<last>` (fails when: the audit is missing (`FAIL no input`), has no `Rollback range: <first>..<last>` line, either id is not a commit, first is not an ancestor of last, the audit lacks any of the rollback steps `git revert`, `dotnet build Beelzebub/Beelzebub.sln -c Release`, `taskkill /PID`, or it does not state the `inert slot-5 bind` consequence)
- [ ] D13 · **Every written path is committed** after the release commit nothing this build wrote is left uncommitted; the owner's two private working files (`Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx`, `_matrix_build.py`) are ignored whether present or not · cmd: `git status --porcelain` → no line naming any other path (fails when: any other path is listed — e.g. an untracked new Logic file, the plan, the reviews file or the audit; run outside the repository, git exits 128 with `not a git repository`, which is a FAIL)
- [ ] D14 · **Backlog row closed** `Beelzebub/Beelzebub/docs/BACKLOG.md` marks `mounted-bar-reset` done in v0.137.3 · file: Beelzebub/Beelzebub/docs/BACKLOG.md contains "| mounted-bar-reset | DONE v0.137.3"
- [ ] D15 · **Only the load path migrates** `MountedSlots.Migrate` is called exactly once outside `Logic/`, from `Services/PersistenceService.cs`; no command, patch, API handler or other service reaches it · cmd: `python Beelzebub/tools/check_mounted_bar.py callers` → `callers: ok, <n> files, 1 call (Beelzebub/Beelzebub/Services/PersistenceService.cs:<line>)` (fails when: the call is missing, a second call exists anywhere, or the one call is outside PersistenceService.cs; no .cs files prints `FAIL no input`)
- [ ] D16 · **No credential in the plugin** no C# source under `Beelzebub/Beelzebub` (the only code that writes state.json, replies and log lines) holds a credential-shaped literal (`password|api_key|secret|bearer|access_token = "<8+ chars>"`) · cmd: `python Beelzebub/tools/check_mounted_bar.py secrets` → `secrets: ok, <n> files, 0 hits` (fails when: any such literal appears; no .cs files prints `FAIL no input`)
- [ ] D17 · **Player commands stay self-only** `form-grant` (`FormGrant`) and `resetbar` (`ResetBar`) take no player/target/name/steam parameter and are not `adminOnly`, so a player can only change their own bar · cmd: `python Beelzebub/tools/check_mounted_bar.py selfonly` → `selfonly: ok, 2 self-only commands` (fails when: either command gains a target-like parameter, becomes adminOnly, or is not found; a missing BeelzCommands.cs prints `FAIL no input`)
- [ ] D18 · **Every touched path is declared** every path changed since the recon commit `0a135f5` (tracked diff plus untracked, minus the two owner files) is named in the plan's Rollout path list · cmd: `python Beelzebub/tools/check_mounted_bar.py paths` → `paths: ok, <n> changed, <k> declared` (fails when: a changed path is not declared, or the Rollout path list is missing; nothing changed prints `FAIL no input`)

## Amendments

## Log
- 2026-10-03 · status → draft · plan
- 2026-10-03 · note · spike · `.beelz admin bar-raw Chaos` while mounted → slot=3 mod=1587 KnifeThrow source=318712:2 (x7, invisible on the client), slot=5/6/7 set=Empty source=318712:2; `.beelz resetbar CONFIRM` while mounted → `destroyed owned ability-slot source AB_Interact_Mount_Owner_Buff_Horse (#854656674)` then `[Beelz MOUNT] dismounted (control buff gone)`, `clean=1`
- 2026-10-03 · note · spike · `Reference Data/Prefabs/AB_Interact_Mount_Owner_Buff_*` (6 files) → basic/generic: 1 leap, 4 gallop, 5 thrust then Empty, 2/3/6/7 Empty; vampire (4 variants): 1 leap, 2 horse jump, 4 gallop, 7 thrust then Empty, 3/5/6 Empty
- 2026-10-03 · note · dry-run · D1 · n/a · Beelzebub.Tests/MountedSlotsTests.cs does not exist until build step 2
- 2026-10-03 · note · dry-run · D2 · n/a · Beelzebub.Tests/MountedSlotsTests.cs does not exist until build step 2
- 2026-10-03 · note · dry-run · D4 · n/a · the Dismount step and its BarResetTests cases are added in build step 4
- 2026-10-03 · note · dry-run · D5 · n/a · the dismount reply line and its BarResetReplyTests cases are added in build step 5
- 2026-10-03 · note · dry-run · D6 · cmd: `pwsh Beelzebub/tools/preflight.ps1` → PREFLIGHT OK (10 checks), `api banner ApiCommands.cs 33, handoff banner 33` (the banner check ran and compared both numbers)
- 2026-10-03 · note · dry-run · D10 · cmd: `pwsh Beelzebub/tools/preflight.ps1` → PREFLIGHT OK (10 checks), `versions csproj 0.137.2, thunderstore.toml 0.137.2`, `changelog entry for [0.137.2]: True`
- 2026-10-03 · note · dry-run · D11 · cmd: `python Beelzebub/tools/check_bar_reset.py auth` → `auth: ok, 70 admin commands, 4 reset callers`
- 2026-10-03 · note · dry-run · D13 · cmd: `git status --porcelain` → ` M Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx`, `?? Beelzebub/Beelzebub/docs/dod/mounted-bar-reset.md`, `?? _matrix_build.py` (the untracked plan is a real failing case: a written path not yet committed)
- 2026-10-04 · note · resumed after a power cut: git fsck clean, no zeroed files, world saves AutoSave_3016-3019 pass `gzip -t`, server state.json and ability_rules.json parse; logs backed up to `%TEMP%\beelz-logs-2026-10-03-powercut\`
- 2026-10-04 · note · spike · dev server boot line → `VRisingServer v1.1.15.0-r101082-b2 (202609071358)`; appmanifest_1829350 buildid 25169871
- 2026-10-04 · note · dry-run · D1 · test: `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~MountedSlotsTests` → Passed 6/6
- 2026-10-04 · note · dry-run · D2 · test: `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~MountedSlotsTests` → Passed 6/6 (incl. Migrate_fails_when_10000_players_take_over_200ms)
- 2026-10-04 · note · dry-run · D4 · test: `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetTests` → Passed 39/39
- 2026-10-04 · note · dry-run · D5 · test: `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetReplyTests` → Passed 17/17
- 2026-10-04 · note · dry-run · D7 · cmd: `python Beelzebub/tools/check_mounted_bar.py handoff` → `handoff: FAIL api=33 banner=33 (want 34) missing=[...]` (built in step 7)
- 2026-10-04 · note · dry-run · D12 · cmd: `python Beelzebub/tools/check_mounted_bar.py rollback` → `rollback: FAIL no input (Beelzebub/Beelzebub/docs/audits/mounted-bar-reset.md missing)` (written in step 8)
- 2026-10-04 · note · dry-run · D15 · cmd: `python Beelzebub/tools/check_mounted_bar.py callers` → `callers: FAIL want exactly 1 call in Beelzebub/Beelzebub/Services/PersistenceService.cs, found 0: []` (wired in step 3)
- 2026-10-04 · note · dry-run · D16 · cmd: `python Beelzebub/tools/check_mounted_bar.py secrets` → `secrets: ok, 77 files, 0 hits`
- 2026-10-04 · note · planted · D1 · `Allowed = {3,5,6,7}` → `IsValid_fails_when_slot_3_or_a_riding_key_is_allowed [FAIL]`, Failed 1/6; restored
- 2026-10-04 · note · planted · D2 · slot-5-taken guard deleted → `Migrate_fails_when_a_taken_slot_5_is_overwritten [FAIL]`, Failed 1/6; restored
- 2026-10-04 · note · planted · D4 · Dismount planned after DestroyOverrideSources → `Plan_fails_when_Dismount_is_missing_or_after_the_orphan_sweep` [FAIL] for PlayerReset, AdminLoadouts, Purge, Failed 3/39; restored
- 2026-10-04 · note · planted · D5 · CountOf guard dropped → `ForReset_fails_when_a_dismount_line_appears_without_a_dismount [FAIL]`, Failed 1/17; restored
- 2026-10-04 · note · planted · D15 · a `MountedSlots.Migrate(null)` call in `Commands/ZzPlanted.cs` → `callers: FAIL want exactly 1 call in ...PersistenceService.cs, found 1: ['Beelzebub/Beelzebub/Commands/ZzPlanted.cs:1']`; removed
- 2026-10-04 · note · planted · D16 · `const string apiKey = "sk-abcdefghijklmnop"` in `Logic/ZzPlanted.cs` → `secrets: FAIL 1 credential-shaped literal(s)`; removed. After all restores: full suite Passed 186/186
- 2026-10-04 · note · dry-run · D17 · cmd: `python Beelzebub/tools/check_mounted_bar.py selfonly` → `selfonly: ok, 2 self-only commands`
- 2026-10-04 · note · dry-run · D18 · cmd: `python Beelzebub/tools/check_mounted_bar.py paths` → `paths: ok, 9 changed, 28 declared`
- 2026-10-04 · note · dry-run · D13 · cmd: `git status --porcelain` → owner files plus the uncommitted plan, reviews, Logic and tool files (a real failing case until the release commit)
- 2026-10-04 · note · planted · D17 · `string player = null` added to `FormGrant` → `selfonly: FAIL ["form-grant: target parameter ['string player = null']"]`; `adminOnly: true` on resetbar → `selfonly: FAIL ['resetbar: adminOnly']`; restored (the first plant passed, exposing a parser bug that read `null` as the parameter name — fixed before this run)
- 2026-10-04 · note · planted · D18 · an untracked `Beelzebub/Beelzebub/ZzUndeclared.txt` → `paths: FAIL 1 changed path(s) not declared: ['Beelzebub/Beelzebub/ZzUndeclared.txt']`; removed
- 2026-10-04 · status → ready · approve · review: human
- 2026-10-04 · status → in-progress · start
