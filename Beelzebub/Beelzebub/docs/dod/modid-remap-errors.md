---
dod: 2
rubric: 2
kind: backlog
id: dod-20261004-9d41
slug: modid-remap-errors
title: modid-remap-errors - no stale slot-override holder outlives a pop
status: draft
size: M
parent: none
created: 2026-10-04
baselined: none
closed: none
recon_commit: 681f313
coverage_author: 15/15 layers · 49/49 probes
coverage_reviewer: pending
review: pending
---

# DoD: modid-remap-errors - no stale slot-override holder outlives a pop

**Size:** M — touches `Logic/` (new file), a new `Services/ModLeakService.cs`, three wiring points (`Heartbeat`, `Core`, the two pop sites), one admin command, one shipped data value, a new check tool, a vrclient scenario and the BCH handoff; no schema change, no new external dependency (L test failed: one module family, no new dependency).
**Planned:** interactively. The owner approved two plan-mode decision sets in `~/.claude/plans/glimmering-dreaming-diffie.md` on 2026-10-04, taking every recommendation. The 224 vanilla `PrefabLookupMap … unknown state` warnings get no action (decision 3-A). Both Whirlwind v2 groups go to `FreeMoveAfterSeconds` 1.0 (decision 4-B). The release is v0.137.6 as a local `chore(release)` commit, and Claude asks before any push (decision 5-A). A stale holder is cleaned by clearing its mods and then destroying it (decision 6-A). The cleanup is a heartbeat mark-and-sweep with a two-read confirm (decision 7-A).
**Request:** "I've been running into some errors when the server is starting, relating to Beelzebub. Can you please check into those and resolve them?" This matches backlog row `modid-remap-errors` in `Beelzebub/Beelzebub/docs/BACKLOG.md`.

## Definition of Done
- [ ] D1 · **A row is live only from its own holder** `Logic/ModLeak.cs` `ModLeak.Row(int modId, SlotModParse slot, LeakHolder holder)` returns `Empty` for `modId <= 0`; `Unknown` when `slot` is null or not `Readable`; `Live` when the slot's GroupGuid entries hold an entry with that `ModId` AND `SourceIndex`/`SourceVersion` equal to the holder; else `Dangling` · test: Beelzebub.Tests/ModLeakTests.cs (fails when: an id live under another source reads Live, a CopyCooldown-section id reads Live, an unreadable or null slot reads Dangling, or id 0 is not Empty)
- [ ] D2 · **A holder is stale only when wholly dead** `ModLeak.Holder(LeakHolder, bool allInitialized, rows)` returns `Building` when not `allInitialized`; `Empty` for null/no rows or rows with no id; `Live` when any row is Live; `Unknown` when any row is Unknown (and none Live); `Stale` when every row with an id is Dangling · test: Beelzebub.Tests/ModLeakTests.cs (fails when: a holder with one live row reads Stale, a wholly dead holder reads anything but Stale, a not-initialized holder or one with an unreadable slot reads Stale)
- [ ] D3 · **Cleaned only when stale twice** `ModLeak.Confirm(first, second)` returns the holders (index AND version) in both reads; null in either returns empty · test: Beelzebub.Tests/ModLeakTests.cs (fails when: a holder seen stale in only one read, or a recycled index with another version, is returned)
- [ ] D4 · **The log lines say what happened** `ModLeak.SweepLine(why, cleaned, modsCleared, keptOnce, unknown, capped)` returns exactly `[Beelz MODLEAK] sweep (boot): cleaned 7 stale holder(s), 22 leftover mod(s) removed; 0 seen stale once (rechecked next pass), 0 unreadable.` for `("boot",7,22,0,0,false)` and adds `; capped at 4096 holders, the rest next pass` when capped; `ModLeak.RowLine` is one line (control characters and spaces stripped from names, names cut to 80 characters, blank → `-`) · test: Beelzebub.Tests/ModLeakTests.cs (fails when: a count or the cap is missing from the sweep line, or a newline in a name survives into a row line)
- [ ] D5 · **Every pop arms a safe sweep** every method in the plugin that calls `RemoveAbilityGroupModificationOnSlot` calls `ModLeakService.MarkDue(` after the pop, inside the same outermost method (today `SlotApply.PushOnSlot`, `TransformBuffService.PopSlotModifications`); `Heartbeat.Pulse` calls `ModLeakService.Tick(`; `Core.TryInitialize` calls `ModLeakService.MarkDue(` after `IsReady = true`; `ModLeakService.Tick` calls `ModLeak.Confirm(` before `Clean(`; `Clean` calls `ClearLooseSourceModifications(` before `DestroyUtility.Destroy(` · cmd: `python Beelzebub/tools/check_modleak.py wiring` → `wiring: ok, 2 pop site(s) mark the sweep, heartbeat ticks it, boot marks it, confirm before clean, clear before destroy` (fails when: a pop site — including one in a local function whose outer method never marks — lacks MarkDue, the heartbeat or boot call is missing, Tick cleans without Confirm, or Clean destroys before clearing; missing service/heartbeat/core prints `FAIL no input`)
- [ ] D6 · **Only the sweep cleans** `.beelz admin modleak [player|all]` is `adminOnly: true` and read-only; `ModLeakService.Clean` is called only from `ModLeakService.Tick` · cmd: `python Beelzebub/tools/check_modleak.py actors` → `actors: ok, modleak adminOnly, 1 Clean call(s), all inside ModLeakService.Tick` (fails when: modleak loses adminOnly, or any other method — a command included — calls Clean; no .cs files prints `FAIL no input`)
- [ ] D7 · **No credential in the plugin** no `.cs`, `.json` or `.toml` file under `Beelzebub/Beelzebub` holds a credential-shaped literal · cmd: `python Beelzebub/tools/check_modleak.py secrets` → `secrets: ok, <n> files, 0 hits` (fails when: a `password|api_key|secret|bearer|access_token` key is assigned an 8+ character string; no such files prints `FAIL no input`)
- [ ] D8 · **The named controls exist** the 9 D1–D4 controls are `[Fact]` methods under their exact names (`REQUIRED_TESTS` in `check_modleak.py`) · cmd: `python Beelzebub/tools/check_modleak.py tests` → `tests: ok, 9 named ModLeak controls present` (fails when: a control is missing, renamed or loses its attribute; no test file prints `FAIL no input`)
- [ ] D9 · **Whirlwind values agree** `Resources/ability_rules.default.json` › `AbilityMap` gives both `AB_Militia_Leader_Whirlwind_v2_AbilityGroup` and `AB_Militia_Leader_Whirlwind_v2_Init_AbilityGroup` `FreeMoveAfterSeconds` 1.0 · cmd: `python Beelzebub/tools/check_modleak.py data` → `data: ok, both Whirlwind v2 groups FreeMoveAfterSeconds 1.0` (fails when: either group is missing, lacks the field, or holds another value; a missing file prints `FAIL no input`)
- [ ] D10 · **First boot cleans saved holders** on the dev server (save backed up at `%TEMP%\beelz-save-2026-10-04-pre-modleak`, 7 stale holders 327217–327223 seen by the diagnostic), the first boot of this build logs one sweep that cleans at least 7 holders, with no MODLEAK warning · cmd: `grep -E "\[Beelz MODLEAK\] (sweep \(boot\)|clean holder=.*failed)" BepInEx/LogOutput.log` (server root) → one `sweep (boot): cleaned <n≥7> stale holder(s), <m> leftover mod(s) removed; 0 seen stale once …` line and no `failed` line (fails when: no boot sweep line, fewer than 7 cleaned, or a clean failed)
- [ ] D11 · **In game, pops leave nothing behind** Claude's scenario `Beelzebub/tools/vrclient/scenarios/modid_remap.vrs`: after the boot sweep `.beelz admin modleak` reports `stale=0 unknown=0` for Chaos; `.beelz admin bar-raw` shows `slot=7 - AbilityGroupSlot.CopyCooldown: True (Base: True)`; a `.beelz grant 1 1621601748` + `.beelz resetbar CONFIRM` is followed within 20 s by `[Beelz MODLEAK] sweep (bar-reset): cleaned <n≥1>`; `modleak` and `modleak all` then report `stale=0`; no `[Error`/`Exception`/`at Beelzebub.` line · cmd: `python Beelzebub/tools/vrclient/vrclient.py run Beelzebub/tools/vrclient/scenarios/modid_remap.vrs` → `SCENARIO PASS modid_remap 10/10` (fails when: a stale or unreadable holder remains, slot 7's CopyCooldown still differs from its base, the bar reset is not followed by a cleaning sweep, an error line appears, or the client cannot join — `ensure` FAIL prints `SCENARIO FAIL`)
- [ ] D12 · **Two restarts, no remap error** after the in-game run, two graceful restarts (procedure 6, logs backed up before each) each boot with zero remap errors and no Whirlwind conflict warning (after `.beelz admin reseed merge` brought the dev server's rules file up to the new default) · cmd: `grep -c "Couldn't remap old Modification Id" logs/NyarDev.log` → `0` after each restart, and `grep -c "AB_Militia_Leader_Whirlwind_v2_Cast rejected" BepInEx/LogOutput.log` → `0` (fails when: either count is above 0 after either restart)
- [ ] D13 · **Older scenarios still pass** a plain cast and the clearbar full reset behave as before · cmd: `python Beelzebub/tools/vrclient/vrclient.py run Beelzebub/tools/vrclient/scenarios/cast_basic.vrs` → `SCENARIO PASS cast_basic 6/6`, then `clearbar_fullreset.vrs` → `SCENARIO PASS clearbar_fullreset 32/32` (fails when: any check of either scenario fails)
- [ ] D14 · **Session logs are clean** after the in-game runs and before a restart · cmd: `pwsh Beelzebub/tools/preflight.ps1 -LogCheck` → `PREFLIGHT OK (2 checks)` with `0 Beelzebub stack frame(s), 0 error(s)` for LogOutput.log (fails when: either log has a Beelzebub stack frame or an `[Error` line; a missing log FAILs)
- [ ] D15 · **BCH is told, no API bump** `Commands/ApiCommands.cs` keeps `ApiVersion = 35`; the handoff banner reads `ApiVersion = 35` and gains one `v0.137.6` line naming the admin `modleak` command and `no wire change` · cmd: `python Beelzebub/tools/check_modleak.py handoff` → `handoff: ok, api 35, banner 35, v0.137.6 note carries 3 tokens` (fails when: ApiVersion or the banner is not 35, or the v0.137.6 line lacks a token; a missing file prints `FAIL no input`)
- [ ] D16 · **Release surfaces in sync** csproj and thunderstore.toml read 0.137.6, CHANGELOG.md has `## [0.137.6]` under its size cap, the README status names v0.137.6, the root README is regenerated · cmd: `pwsh Beelzebub/tools/preflight.ps1` → `PREFLIGHT OK` with `versions csproj 0.137.6, thunderstore.toml 0.137.6` (fails when: the versions differ, the entry is missing, the README names another version, or the root README is stale)
- [ ] D17 · **Rollback range recorded** the audit states the first and last commit of this build, the revert/rebuild/redeploy steps, and that a rollback restores `save-data-nyardev` from the pre-change copy only if a cleaned holder must come back · cmd: `python Beelzebub/tools/check_modleak.py rollback` → `rollback: ok, <first>..<last>` (fails when: the audit is missing (`FAIL no input`), the range is absent or unresolvable, first is not an ancestor of last, or a step or the save name is missing)
- [ ] D18 · **Every written path is committed** after the release commit `git status --porcelain` lists only the owner's two private files · cmd: `python Beelzebub/tools/check_modleak.py status` → `status: ok, nothing uncommitted but <n> owner file(s)` (fails when: any other path is listed; outside a repository prints `FAIL no input`)
- [ ] D19 · **Every touched path is declared** every path changed since `681f313` (tracked plus untracked, minus the owner files) is named in the Rollout path list · cmd: `python Beelzebub/tools/check_modleak.py paths` → `paths: ok, <n> changed, <k> declared` (fails when: a changed path is undeclared or the list is missing; nothing changed prints `FAIL no input`)
- [ ] D20 · **Backlog row closed** `docs/BACKLOG.md`'s `modid-remap-errors` row says `DONE v0.137.6` · cmd: `python Beelzebub/tools/check_modleak.py backlog` → `backlog: ok, modid-remap-errors DONE v0.137.6` (fails when: the row is missing or lacks `DONE v0.137.6`; a missing BACKLOG.md prints `FAIL no input`)
- [ ] D21 · **Every check catches its fault** each `check_modleak.py` check passes a good fixture, FAILs a planted-defect fixture and FAILs an empty tree with `no input` (rollback, paths, status on a throwaway git repo) · cmd: `python Beelzebub/tools/check_modleak.py selftest` → `selftest: ok, 10 checks x good/defect/empty` (fails when: any check passes its defect fixture, fails its good fixture, or passes an empty tree)

## Purpose & typical use
Every boot of the dev server logged a handful of `Couldn't remap old Modification Id … AbilityGroupSlotModificationBuffer`
errors. They come from slot-override **holders**. When the game applies a slot-override row, the engine creates a
prefab-less entity (`ReplaceAbilityOnSlotBuff_AllInitialized` + `AbilityGroupSlotModificationBuffer`) that is the
source of three mods per slot: GroupGuid (which ability), CopyCooldown and SpellModsSource. Beelzebub's bar reset and
grant pop the GroupGuid mod by id. The holder keeps its other two mods, so the engine never destroys it. It is saved,
and at the next boot its dead ids fail to remap or, worse, get remapped onto other mods' ids. The diagnostic on
2026-10-04 also found a gameplay effect: six stale holders forced slot 7's `CopyCooldown` to False (base True). This
slice cleans such holders: the heartbeat finds a holder none of whose rows still sets a slot, confirms it on a
second read, clears its leftover mods through the engine and destroys it. Users are the server owner (clean boots)
and players (correct cooldown sharing); nobody runs anything new.

## Use cases
### Typical
- The owner boots the server: the first boot after the update logs `[Beelz MODLEAK] sweep (boot): cleaned 7 …`; the next boots have no remap error (D10, D12).
- A player runs `.beelz resetbar CONFIRM`: a couple of seconds later the sweep cleans the holders the reset left (D5, D11).
- An admin checks one player with `.beelz admin modleak Chaos`: the reply counts holders by verdict; details go to the log (D6, D11).
### Minimal stretch
- A server with no stale holder: the boot sweep reads, finds nothing, logs nothing and sleeps (D5; `Tick` sleeps when the first read is clean).
- A player whose holders are all live: nothing is touched (D2).
### Maximal stretch
- A slot dump mid-change (the engine still applying a new row): a holder read stale once is kept, re-read 2 s later, and cleaned only if still stale (D3).
- A holder still being built (no `AllInitialized`) or a slot whose dump does not parse: never cleaned (D2).
- Volume (100×): 40 players × 25 holders = 1 000 holders; one pass reads each holder's buffer and parses each target slot once (cached per slot). The pass is capped at 4 096 holders; beyond that the line says so and the next pass continues (D4). Passes only run after a boot or a pop, never every frame.
- Used once (8.2): nothing is persisted by this slice; the sweep state is three static fields reset by every pass.
- Abuse: players cannot reach the sweep or the report; the sweep runs on its own and the report is admin-only (D6).

## Business rules
1. A row is **Live** only when its GroupGuid id is among the target slot's GroupGuid mods **with this holder as source** (D1). Id alone is not enough: the boot remap already put dead ids onto other mods' ids (holder 327222 row → 1540, 327223's CopyCooldown mod).
2. A holder is **Stale** only when it is initialized, every slot it targets was read, and none of its rows is Live (D2). One live row keeps the whole holder.
3. Only holders stale in two reads at least 2 s apart are cleaned (D3). A pop during a pass restarts it with two fresh reads (`MarkDue` resets the first read).
4. Cleaning order: `ClearLooseSourceModifications(holder)` first (removes its CopyCooldown and SpellModsSource mods so the slot falls back to its base), mark the target slots dirty, then `DestroyUtility.Destroy` (D5).
5. "Every pop" (4.5) is computed by searching the plugin for `RemoveAbilityGroupModificationOnSlot` at `681f313`: two call sites, `SlotApply.PushOnSlot` and `TransformBuffService.PopSlotModifications` (inside the local function `Pop`). The `wiring` check repeats that search over every `.cs` file on every run, so a third pop site that does not mark FAILs. Blind spot: a pop through another engine API (`ModificationsRegistry.RemoveModification` directly) — none exists today; `/code-review` and Codex read the diff for one.
6. Holders that target non-players are left alone (the scan accepts a holder only when every row targets a `PlayerCharacter`): NPC and boss holders are vanilla's own.

## Interfaces
### Internal — reads / writes / changes (paths or symbols)
- Read for this plan: `Services/SlotApply.cs` (`PushOnSlot`:726, `MarkSlotDirty`:572), `Services/TransformBuffService.cs` (`PopSlotModifications`:1226), `Services/Heartbeat.cs` (`Pulse`), `Core.cs` (`TryInitialize`), `Logic/SlotModDump.cs` (`ParseGroupGuid`, `SlotModEntry`), `Commands/AdminCommands.cs` (`BarRaw`, `TryBarTarget`), `tools/check_transform_guard.py` (check-tool pattern).
- New `Logic/ModLeak.cs` (pure, linked into the tests) (D1–D4); new `Services/ModLeakService.cs` (`MarkDue`, `Tick`, `Scan`, `Clean`, `Describe`) (D5).
- Wiring: `Heartbeat.Pulse` → `Tick`; `Core.TryInitialize` → `MarkDue("boot")`; `PushOnSlot` → `MarkDue("grant")` when it popped; `PopSlotModifications` → `MarkDue("bar-reset")` when it popped (D5).
- New admin command `modleak [player|all]` (read-only report) (D6).
- Data: `Resources/ability_rules.default.json` Whirlwind v2 group 10.0 → 1.0 (D9).
- What breaks if wrong: a too-wide stale test would destroy a live holder and strip a granted ability (D1–D3 controls, D11 in game); a missed pop site leaves the leak (D5).
- Contract (BCH): no command shape or `[BEELZ:*]` line changes; one new admin-only command; `ApiVersion` stays 35 (D15).
### External — dependencies and their failure behaviour
- V Rising server ECS (6.2):

  | Collaborator | Reports failure by | Deferred effect observed by | This plan |
  |---|---|---|---|
  | `ModificationsRegistry.GetFormattedEntityModificationsMessage` | throws, or a dump that does not parse | — | `Unknown`, never cleaned (D1, D2) |
  | `ModificationsRegistry.ClearLooseSourceModifications(holder, ref em)` | returns the count removed (0 = none); may throw | slot values re-resolve when the slot is dirty | count logged; the slot gets `AbilityGroupSlot.DirtyTag`; a throw is logged and the holder kept (D5) |
  | `DestroyUtility.Destroy` | throws | adds `DestroyTag`; the entity goes later | `Clean` skips a holder that already has `DestroyTag` or no longer exists (D5) |
  | `ServerGameManager.RemoveAbilityGroupModificationOnSlot` (our pops) | throws (caught at the call site) | the holder's other mods stay — the leak itself | each pop site arms the sweep (D5) |
  | `CompressModificationIdsOnLoadSystem` (boot) | logs the remap error | runs before our init | the boot sweep runs after `IsReady`; D12 counts the errors |
- Supported runtime (6.1): `VRisingServer v1.1.15.0-r101082`, `BepInEx.Unity.IL2CPP` 6.0.0-be.733, `VampireReferenceAssemblies` 1.1.12-r99041-b2, `VRising.VampireCommandFramework` 0.10.* — unchanged.
- The game client through vrclient (test tooling): if it cannot join, `ensure` fails and the scenario prints `SCENARIO FAIL` (D11).
- Collaborators: Codex is a review aid; a verdict produced without reading the diff is discarded and rerun with the diff on stdin.

## Design
### Data
- Saved data: the cleanup **removes** saved entities (the stale holders) and their leftover mods from the world save. That is the point of the fix and cannot be undone without the save copy `%TEMP%\beelz-save-2026-10-04-pre-modleak` (D17). Nothing Beelzebub persists (state.json, rules) changes shape.
- By-products:

  | Artifact | Where | Owner | Retention / deletion | Copies |
  |---|---|---|---|---|
  | `[Beelz MODLEAK]` log lines | `BepInEx/LogOutput.log` | server owner | overwritten at the next start | procedure 6 backups in `%TEMP%\beelz-logs-2026-10-04-modleak*\` |
  | pre-change save copy | `%TEMP%\beelz-save-2026-10-04-pre-modleak` | dev machine owner | OS temp cleanup; kept until the release is pushed | none |
  | rules backup by `reseed merge` | `BepInEx/config/kdpen.Beelzebub/` | server owner | kept by the reseed command | none |
  | vrclient results + shots | `%TEMP%\vrclient\` | dev machine owner | OS temp cleanup | none |
  | planted-fault edits | the tracked source file | Claude | restored right after each check fails; D18 fails while one remains | none |
  | deployed DLL, `dist/` | dev server plugins, `Beelzebub/Beelzebub/dist/` (gitignored) | build | replaced by the next build | `bin/Release/net6.0/` |
### States
- Sweep states: idle (`_dueAt` = MaxValue); due (first read at `_dueAt`); first read held (second read ≥ 2 s later); after the second read it cleans and goes idle, or re-arms when a holder was seen stale only once or the pass was capped (D3, D5).
- Concurrency (7.2): every caller (`Pulse`, commands, `TryInitialize`, the pops) runs on the server main thread; the race is across frames — the engine applying a new row in frame N while the sweep reads in frame N+1 — covered by the two-read confirm and the `AllInitialized` gate (D2, D3).
- Interrupted (7.3): a server stop between the reads loses only the in-memory first read; the next boot marks the sweep again (D5). A clean that throws keeps the holder and logs a warning; the next pass retries it.
- Empty/first-run (7.1): no holders → the first read finds nothing and the sweep sleeps without a line (D5).
### Permissions
- Actors (2.1): (a) no player path reaches the sweep — it runs from the heartbeat, armed by boot and by the pops that the existing self-only commands (`grant`, `resetbar`) and admin bar commands already trigger; (b) admins reach the read-only `modleak` report (adminOnly); (c) server console/RCON run no VCF command. D6 checks (b) and that only `Tick` calls `Clean`.
- Unauthorised path (2.2): VCF's standard deny for adminOnly; the report changes nothing.
- Ownership (2.3): the sweep acts on holders whose every row targets a player character; it never targets a player by input.
### UX
- No player-facing change except that cooldown sharing on slot 7 is correct again (D11).
- Admin: `.beelz admin modleak [player|all]` replies one line `modleak <who>: <n> slot-override holder(s), <m> row(s) — live a, stale b, unreadable c, building d, empty e. Details: LogOutput.log [Beelz MODLEAK].` (under the chat cap via `BarResetReply.Cap`).
- Activation (11.4): the sweep runs on its own after boot and after every pop; the `[Beelz MODLEAK] sweep` line shows it acted (D10, D11).

## Security
- Authorization: D6. Inputs: the report takes a player name or `all`, resolved by the existing `TryBarTarget`; nothing reaches a shell, query or URL. Secrets: none in this mod; D7 scans. Personal data: SteamIDs already on every `[Beelz]` line; MODLEAK lines add entity ids and mod ids only. Logs stay on the owner's machine.

## Failure & observability
- Failure classes: a holder unreadable → `Unknown`, counted on the sweep line, never cleaned; a clean throwing → warning `[Beelz MODLEAK] clean holder=… failed: …`, holder kept; a sweep throwing → `Heartbeat` logs the error and the next pulse runs again.
- Logged: one sweep line per pass that found anything (D4), one `cleaned holder=` line per holder.
- In production: the owner reads the boot log; `grep -c "Couldn't remap"` on `logs/<server>.log` is the signal (D12); `.beelz admin modleak all` is the support tool.
- Gating controls (12.4):

  | Probe | Command | Fails when |
  |---|---|---|
  | 2.1 | `python Beelzebub/tools/check_modleak.py actors` (D6) | modleak loses adminOnly; anything but Tick calls Clean |
  | 3.3 | `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~ModLeakTests` (D1–D3) | a live or unreadable holder reads Stale; a once-seen holder is confirmed |
  | 4.4 | the same filter (D1, D2) | an id live under another source reads Live |
  | 6.2 | `python Beelzebub/tools/check_modleak.py wiring` (D5) | Clean destroys before clearing; Tick cleans unconfirmed |
  | 10.1 | `check_modleak.py actors` (D6) | as 2.1 |
  | 10.3 | `check_modleak.py secrets` (D7) | a credential-shaped literal appears |
  | 12.4 | `check_modleak.py selftest` (D21) and each control's planted fault (Log `note · planted`) | a check passes its defect fixture or an empty tree; a planted fault does not fail its control |
  | 14.3 | `check_modleak.py rollback` (D17) | the range is missing or a step is missing |
  | 14.4 | `check_modleak.py paths` (D19) with `status` (D18) | a touched path is undeclared or uncommitted |

## Performance
- Throughput (13.1): a pass reads every holder buffer once and parses each target slot once (cached); passes run only after boot or a pop, at most one read per 2 s while due.
- Bounds (13.2): a pass reads at most `ModLeak.Cap` = 4 096 holders. Source: 40 players × 6–8 slots × up to ~10 holders each in the worst diagnostic pile-up = ~3 200; 4 096 is the next power of two. At the bound the pass stops, says so on the sweep line, and re-arms (D4).

## Build plan
1. (Built before approval, during the diagnostic spike.) `Logic/ModLeak.cs` and `Beelzebub.Tests/ModLeakTests.cs` with the 9 controls in `REQUIRED_TESTS`; each fault planted once (Log `note · planted`) · satisfies D1, D2, D3, D4
2. (Built before approval.) `Services/ModLeakService.cs`; wiring in `Services/Heartbeat.cs` (`Pulse`), `Core.cs` (`TryInitialize`), `Services/SlotApply.cs` (`PushOnSlot`), `Services/TransformBuffService.cs` (`PopSlotModifications`); the `modleak` command in `Commands/AdminCommands.cs`; `dotnet build Beelzebub/Beelzebub/Beelzebub.csproj -c Release -p:VRisingServerPath="C:\none"` and `dotnet test Beelzebub/Beelzebub.Tests` · satisfies D5, D6
3. (Built before approval.) `Resources/ability_rules.default.json` Whirlwind v2 group 1.0 · satisfies D9
4. (Written before approval.) `tools/check_modleak.py` (`wiring actors secrets tests data handoff rollback paths backlog status selftest`) and `tools/vrclient/scenarios/modid_remap.vrs` · satisfies D7, D8, D21
5. Post-audit `docs/audits/modid-remap-errors.md` (Release build, `dotnet test`, `/code-review`, Codex on the diff via stdin, ≤ 3 rounds, one commit per round) with `Rollback range: <first>..<last>` and the save-restore sentence · satisfies D17
6. Deploy per CLAUDE.md procedure 6 (close the client; back up both logs to `%TEMP%\beelz-logs-2026-10-04-modleak\`; `taskkill /PID` without `/F`; `dotnet build Beelzebub/Beelzebub.sln -c Release`; `cmp` the DLL; start); check the boot sweep (D10); `vrclient.py ensure`; `chat .beelz admin reseed merge`; run `modid_remap.vrs`, `cast_basic.vrs`, `clearbar_fullreset.vrs`; `pwsh Beelzebub/tools/preflight.ps1 -LogCheck`; then two restarts (logs backed up to `…-modleak-r1\`, `…-modleak-r2\`) with the D12 counts · satisfies D10, D11, D12, D13, D14
7. `docs/BCH_INTEGRATION_HANDOFF.md` v0.137.6 line; `chore(release): v0.137.6` — csproj + toml, CHANGELOG.md (drop the oldest) + CHANGELOG_FULL.md, README status, `python Beelzebub/tools/sync_github_readme.py`, BACKLOG row `DONE v0.137.6 (modid-remap-errors): …`; `pwsh Beelzebub/tools/preflight.ps1` → PREFLIGHT OK; `check_modleak.py handoff paths backlog status` → ok · satisfies D15, D16, D18, D19, D20

## Rollout
- Ships all at once in v0.137.6 (no flag). The first boot on each server cleans that server's stale holders.
- Backward compatibility: no command or wire change; BCH sees nothing new. A server that has already edited the Whirlwind values keeps them; `.beelz admin reseed merge` takes the new default only where the admin never changed it.
- Rollback: `git revert --no-edit <first>^..<last>` over the audit's `Rollback range:` (D17), stop the server (`taskkill /PID <pid>`), `dotnet build Beelzebub/Beelzebub.sln -c Release`, start per procedure 6. Holders already cleaned stay cleaned (desired); to bring them back, stop the server and copy `%TEMP%\beelz-save-2026-10-04-pre-modleak` over `save-data-nyardev`.
- Paths this change ships, writes or regenerates (14.4), checked by D18 and D19: `Logic/ModLeak.cs`, `Services/ModLeakService.cs`, `Services/Heartbeat.cs`, `Core.cs`, `Services/SlotApply.cs`, `Services/TransformBuffService.cs`, `Commands/AdminCommands.cs`, `Resources/ability_rules.default.json`,
  `Beelzebub.Tests/ModLeakTests.cs`,
  `docs/BCH_INTEGRATION_HANDOFF.md`, `docs/BACKLOG.md`, `docs/audits/modid-remap-errors.md`,
  `docs/dod/modid-remap-errors.md`, `docs/dod/modid-remap-errors.reviews.md`, `docs/dod/modid-remap-errors.review.html`, `docs/dod/modid-remap-errors.html`, `docs/dod/dod-dashboard.html`, `docs/dod/README.md` (index), `docs/dod/profile.md`,
  release files (`Beelzebub.csproj`, `thunderstore.toml`, `CHANGELOG.md`, `docs/CHANGELOG_FULL.md`, `README.md`, repo-root `README.md`),
  `tools/check_modleak.py` (new), `tools/vrclient/scenarios/modid_remap.vrs` (new);
  `dist/` (gitignored); outside git (listed for the record): the deployed DLL, the dev server's save, rules file and logs, the backups in `%TEMP%`.

## Out of scope
- The 224 vanilla `PrefabLookupMap … unknown state` warnings (decision 3-A, 2026-10-04): engine data, not Beelzebub.
- Changing the pops to also clear their holder in the same frame (decision 7-B, excluded 2026-10-04: same-frame deferred effects, profile Probe 6.2).
- Mods leaked by NPC/boss holders (business rule 6).
- `dev-snapshot`, `docs-consolidation`, `timed-revert-orphan` — other backlog rows.

## Also considered
- Compliance, localisation, running cost: not applicable — server mod, English log/chat text, no service.
- Operational ownership: the owner runs the dev server; tester servers get the release note.
- Documentation: CHANGELOG entry, README scan, BCH handoff line (D15, D16).
- Analytics: none; the MODLEAK lines are the measure.
- Decommissioning: the diagnostic-only verdicts of the spike build (`verdict=` per row) are replaced by holder verdicts.
- Support tooling: `.beelz admin modleak` and the existing `bar-raw`.

## Assumptions
- S-1 · validated · the 7 stale holders 327217–327223 are Chaos's, and their CopyCooldown mods force slot 7 to False · source: Log spike 2026-10-04 (`modleak` + `bar-raw` on the diagnostic build)
- S-2 · reversible · `ClearLooseSourceModifications` removes every mod whose source is the holder, as its name and signature say; it has not been called on this server yet · fallback: D10/D11 observe it in game (slot 7 back to True, leftover-mods count > 0); if it removes nothing, `Clean` keeps the holder alive (destroy only after a non-zero clear is the follow-up) and the owner re-decides
- S-3 · reversible · destroying a holder the engine no longer applies has no other effect · fallback: D11's error check and D12's restarts observe it; the save copy restores it

## Coverage
| # | Layer | Status | Probes | Pointer / reason |
|---|---|---|---|---|
| 1 | Purpose & typical use | Considered | 3/3 | Purpose & typical use |
| 2 | Actors & permissions | Considered | 3/3 | Design › Permissions › 2.1 D6 D21; 2.2 prose: no new unauthorised path, VCF standard deny unchanged; 2.3 D6 |
| 3 | Inputs, outputs & data | Considered | 4/4 | Design › Data › 3.1 D1 D2; 3.2 D4 D15; 3.3 D2 D3 D18 D19; 3.4 D17 |
| 4 | Business rules & invariants | Considered | 5/5 | Business rules › 4.1 D1 D2; 4.2 D5; 4.3 D3; 4.4 D1; 4.5 D5 |
| 5 | Internal interfaces | Considered | 3/3 | Interfaces › Internal › 5.1 D5; 5.2 D6 D11; 5.3 D4 D15 |
| 6 | External dependencies & contracts | Considered | 3/3 | Interfaces › External › 6.1 D11 D13; 6.2 D2 D5 D10; 6.3 D11 |
| 7 | States & lifecycle | Considered | 3/3 | Design › States › 7.1 D5; 7.2 D2 D3; 7.3 D5 D12 |
| 8 | Minimal stretch | Considered | 2/2 | Use cases › Minimal stretch › 8.1 D2; 8.2 D14 |
| 9 | Maximal stretch | Considered | 3/3 | Use cases › Maximal stretch › 9.1 D4; 9.2 D6; 9.3 D3 D11 |
| 10 | Security & privacy | Considered | 4/4 | Security › 10.1 D6; 10.2 prose: a player name or `all`, resolved by the existing TryBarTarget only; 10.3 D7; 10.4 prose: entity and mod ids added to lines that already carry the SteamID |
| 11 | Design & UX | Considered | 4/4 | Design › UX › 11.1 D11; 11.2 D6; 11.3 prose: one plain chat line under the chat cap, counts in words; 11.4 D10 D11 |
| 12 | Failure handling & observability | Considered | 4/4 | Failure & observability › 12.1 D2; 12.2 D4 D10; 12.3 D14; 12.4 D1 D2 D3 D4 D5 D6 D7 D8 D9 D15 D17 D19 D20 D21 |
| 13 | Performance & scale | Considered | 2/2 | Performance › 13.1 prose: passes run only after boot or a pop, slot parses cached; 13.2 D4 |
| 14 | Rollout & compatibility | Considered | 4/4 | Rollout › 14.1 D16; 14.2 D12 D13 D15; 14.3 D17; 14.4 D18 D19 |
| 15 | Out of scope | Considered | 2/2 | Out of scope |
Gate — acceptance & testability: passed — every Considered layer 2–14 maps to ≥ 1 D-item

## Baseline

## Amendments

## Log
- 2026-10-04 · status → draft · plan
- 2026-10-04 · note · spike · diagnostic build (`.beelz admin modleak` + `bar-raw`, run 16:34): 25 rows on 11 prefab-less holders, all targeting Chaos; holders 327217–327223 are the entities the boot's remap errors name; their GroupGuid ids are gone from the registry, their CopyCooldown mods (and on slot 1 a SpellModsSource mod) are live; slot 7 `CopyCooldown: False (Base: True)`; 327222's row id 1540 is 327223's CopyCooldown mod (remap collision). Save copied to `%TEMP%\beelz-save-2026-10-04-pre-modleak`
- 2026-10-04 · note · spike · `RemoveAbilityGroupModificationOnSlot` call sites at 681f313: `SlotApply.cs:737` (`PushOnSlot`), `TransformBuffService.cs:1262` (local `Pop` inside `PopSlotModifications`)
- 2026-10-04 · note · spike · test data: Chaos's 250 captures no longer hold Knife Throw (1621601748) — the grant step of the diagnostic scenario replied `'1621601748' is neither a valid list index (0-249) nor an ability ID you've captured`; `modid_remap.vrs` restores it with `.beelz admin give Chaos 613251918 1621601748` (Undead Infiltrator V Blood) before granting
- 2026-10-04 · note · planted · D1 · source match dropped from Row → `Row_fails_when_an_id_live_under_another_source_reads_live` fails; GroupGuid section filter dropped in `SlotModDump.ParseGroupGuid` → `Row_fails_when_a_copycooldown_id_counts_as_a_slot_setter` fails; `Readable` check dropped → `Row_fails_when_an_unreadable_slot_or_empty_id_reads_dangling` fails; each restored
- 2026-10-04 · note · planted · D2 · Live check dropped → `Holder_fails_when_a_holder_with_one_live_row_reads_stale`; Stale → Empty → `Holder_fails_when_a_dead_holder_is_not_stale`; Unknown check dropped → `Holder_fails_when_an_unfinished_or_unreadable_holder_is_stale`; each restored
- 2026-10-04 · note · planted · D3 · intersection replaced by the second read → `Confirm_fails_when_a_holder_seen_stale_once_is_cleaned`; restored
- 2026-10-04 · note · planted · D4 · cap suffix dropped → `SweepLine_fails_when_a_count_or_the_cap_is_not_named`; control-character strip dropped → `RowLine_fails_when_a_name_breaks_the_line`; restored, `dotnet test` 254/254
- 2026-10-04 · note · dry-run · D1 · test: `dotnet test Beelzebub/Beelzebub.Tests` → `Passed! - Failed: 0, Passed: 254`
- 2026-10-04 · note · dry-run · D2 · test: `dotnet test Beelzebub/Beelzebub.Tests` → `Passed! - Failed: 0, Passed: 254`
- 2026-10-04 · note · dry-run · D3 · test: `dotnet test Beelzebub/Beelzebub.Tests` → `Passed! - Failed: 0, Passed: 254`
- 2026-10-04 · note · dry-run · D4 · test: `dotnet test Beelzebub/Beelzebub.Tests` → `Passed! - Failed: 0, Passed: 254`
- 2026-10-04 · note · dry-run · D5 · cmd: `python Beelzebub/tools/check_modleak.py wiring` → `wiring: ok, 2 pop site(s) mark the sweep, heartbeat ticks it, boot marks it, confirm before clean, clear before destroy` (first run, before the outer-method fix: `wiring: FAIL …TransformBuffService.cs:Pop pops a slot mod without MarkDue after it` — the local-function case, now a selftest fixture)
- 2026-10-04 · note · dry-run · D6 · cmd: `python Beelzebub/tools/check_modleak.py actors` → `actors: ok, modleak adminOnly, 1 Clean call(s), all inside ModLeakService.Tick`
- 2026-10-04 · note · dry-run · D7 · cmd: `python Beelzebub/tools/check_modleak.py secrets` → `secrets: ok, 86 files, 0 hits`
- 2026-10-04 · note · dry-run · D8 · cmd: `python Beelzebub/tools/check_modleak.py tests` → `tests: ok, 9 named ModLeak controls present`
- 2026-10-04 · note · dry-run · D9 · cmd: `python Beelzebub/tools/check_modleak.py data` → `data: ok, both Whirlwind v2 groups FreeMoveAfterSeconds 1.0` (at 681f313 the v2 group held 10.0 — the real failing case)
- 2026-10-04 · note · dry-run · D10 · n/a · needs the deployed build's first boot (step 6)
- 2026-10-04 · note · dry-run · D11 · n/a · needs the deployed build (step 6); the diagnostic run of the earlier scenario reached 6/7 (grant step: capture missing, see the test-data spike)
- 2026-10-04 · note · dry-run · D12 · cmd: `grep -c "Couldn't remap old Modification Id" logs/NyarDev.log` → non-zero on the current (diagnostic) boot — the real failing case
- 2026-10-04 · note · dry-run · D13 · n/a · last run 2026-10-04 on v0.137.5: `SCENARIO PASS cast_basic 6/6`, `SCENARIO PASS clearbar_fullreset 32/32` (transform-chain-guard Log); both need the Knife Throw capture `modid_remap.vrs` restores
- 2026-10-04 · note · dry-run · D14 · n/a · run after the in-game session (step 6)
- 2026-10-04 · note · dry-run · D15 · cmd: `python Beelzebub/tools/check_modleak.py handoff` → `handoff: FAIL api=35 banner=35 (want 35) v0.137.6 line missing=['v0.137.6', 'modleak', 'no wire change']` (step 7)
- 2026-10-04 · note · dry-run · D16 · n/a · versions still 0.137.5 (step 7)
- 2026-10-04 · note · dry-run · D17 · n/a · the audit is written in step 5 (`rollback: FAIL no input` until then)
- 2026-10-04 · note · dry-run · D18 · n/a · checked after the release commit (step 7)
- 2026-10-04 · note · dry-run · D19 · n/a · the path list is checked after step 7
- 2026-10-04 · note · dry-run · D20 · cmd: `python Beelzebub/tools/check_modleak.py backlog` → `backlog: FAIL the modid-remap-errors row is not marked `DONE v0.137.6`` (step 7)
- 2026-10-04 · note · dry-run · D21 · cmd: `python Beelzebub/tools/check_modleak.py selftest` → `selftest: ok, 10 checks x good/defect/empty`
