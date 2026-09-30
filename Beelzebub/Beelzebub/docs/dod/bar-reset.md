---
dod: 2
rubric: 2
id: dod-20260930-b4r1
slug: bar-reset
title: Action-bar reset that clears every layer
status: draft
size: L
parent: none
created: 2026-09-30
baselined: none
closed: none
commit: 3fbc2d4
coverage_author: 15/15 layers · 49/49 probes
coverage_reviewer: pending
review: pending
---

# DoD: Action-bar reset that clears every layer

**Size:** L — touches four modules (`Logic/`, `Services/`, `Commands/`, `Config/`) plus docs and tooling; no schema or external-dependency change (L test: multiple modules).
**Planned:** interactively (owner approved the decision set D1–D6 of `~/.claude/plans/deep-growing-stallman.md` on 2026-09-30, all recommendations taken)
**Request:** "I noticed that my action bar got stuck After I had attached custom spells to it. it didn't seem like all of the different resets fully or easily cleared it out."

## Definition of Done
- [ ] D1 · **Mod-dump parser is pure and tested** `Logic/SlotModDump.cs` parses the engine's modification dump into GroupGuid entries (mod id, set-to prefab, source entity index:version) from the exact `- AbilityGroupSlot.GroupGuid:` field only, ends that section at the next `- ` field header of any component, ignores every other field section, and reports `Unreadable` when the text contains a `[ModId` line it cannot parse; `TransformBuffService` uses it instead of its private parsers and takes no mod id or source from an Unreadable dump · test: Beelzebub.Tests/SlotModDumpTests.cs (fails when: a `[ModId]` line under `- AbilityGroupSlot.CopyCooldown`, `- AbilityGroupSlot.GroupGuidBackup` or another component's field after GroupGuid is returned as a GroupGuid entry or makes the dump Unreadable, an empty dump yields any entry, or an unparseable `[ModId` line yields Readable)
- [ ] D2 · **One fixed reset order** `Logic/BarReset.cs` `BarResetPlanner.Plan(scope, online, liveReady, transformActiveOrParked)` returns, for an online character whose live components are present (`liveReady`), the steps in this order: RevertTransform, ClearSavedBindings, ClearHotkeys (Purge only), SaveBindings, ClearEquipEntries, DestroyOverrideSources, PopSlotMods, EmptyPush, Reapply, Readback — RevertTransform only when a transform is active or parked; ClearSavedBindings also drops the active/parked transform record · test: Beelzebub.Tests/BarResetTests.cs (fails when: ClearEquipEntries is missing or ordered after PopSlotMods, SaveBindings comes before ClearSavedBindings, or EmptyPush appears more than once; stays silent on the documented order; an unknown scope value throws instead of returning an empty plan, or the runner reports an empty plan, or a plan missing any of ClearSavedBindings, SaveBindings, ClearEquipEntries, DestroyOverrideSources, PopSlotMods, EmptyPush, Reapply or Readback, as Clean=true — such a plan is an error result, `Clean=false`)
- [ ] D3 · **Scope keeps the right data** `PlayerReset` (resetbar) and `AdminLoadouts` (reset-loadouts) clear universal/weapon/form binds, transform loadouts and the slot baseline and keep captures, unlocks, hotkeys and presets; `Purge` also clears hotkeys; no scope clears captures, unlocks or presets · test: Beelzebub.Tests/BarResetTests.cs (fails when: PlayerReset plans ClearHotkeys, or Purge omits it)
- [ ] D4 · **Form bar skips captures by default** `Logic/FormBarFill.cs` returns form binds first, else universal binds, else (only when `autoFillFromCaptures` is true) the first six usable captures; `ShapeshiftAbilityService.BuildFormBar` calls it and logs one `[Beelz FORM] target=<steamId> form=<name> source=<form|universal|captures|native>` line (`BarResetLog.FormatForm`) AFTER the incompatibility-lock filter, printing `native` when the filtered bar is empty · test: Beelzebub.Tests/FormBarFillTests.cs (fails when: the result's Source is not `native` for an empty bar, with no form or universal binds and autoFill=false any capture is returned, or a form bind loses to a universal bind on the same slot; empty inputs return an empty bar)
- [ ] D5 · **Config key defaults off** `Config/Settings.cs` binds `Forms_AutoFillFromCaptures` in section `Forms`, default `false` · cmd: `python Beelzebub/tools/check_bar_reset.py config` → `config: ok` (fails when: the Bind call is missing, or its default is true — the check prints FAIL, never ok, when it finds no Bind call)
- [ ] D6 · **Bar diagnostic** `.beelz admin bar [player]` (adminOnly) prints, for the bar slots 0-8, one line each: resolved ability name, saved bind (universal/weapon/form), Beelzebub row on the equip buff yes/no, override buffs, GroupGuid mods split gear-sourced (with the source prefab name it kept) vs other-sourced (or `unreadable`); for an offline target it prints instead one line `offline: live bar resets on next login` then one line per saved set (universal, each weapon, each form) with its bound slots, the transform record and the hotkey count; the log gets one `[Beelz BAR]` line (D16 `FormatBar`) covering the bar slots 0-8 that have any bind, row or GroupGuid mod; it changes nothing · manual: Test fixture steps 1-3, then (a) `.beelz admin bar PerpetualChaos` twice; PASS when 9 slot lines (1-6, primary, ultimate and the remaining bar slot) are printed, slots 1 and 4 show source `bind` with the Sword set, every other shown slot shows `none`, each gear-sourced mod names its source prefab, and the two `[Beelz BAR]` log lines are identical (read-only: nothing changed between them); (b) `.beelz hotkey list` before and after (a) is identical; (c) with a second account logged out after `.beelz grant 2 0` and `.beelz hotkey set test 0`, `.beelz admin bar <that name>`; PASS when it prints the `offline:` line, the universal/weapon/form sets with slot 2 bound, the transform record (`none`) and `hotkeys=1`, and a later login shows slot 2 still bound
- [ ] D7 · **resetbar returns the bar to vanilla** after `.beelz resetbar CONFIRM` the readback shows no saved bind, no Beelzebub equip row and no other-sourced mod on any slot, the weapon's own skills are on the bar without a weapon swap, Space shows its base dash and R, C and T take a vanilla spell on the first try; it is tested on the release candidate before the `chore(release)` commit and a failure blocks that commit (Business rules 7) · manual: Test fixture steps 1-3, `.beelz resetbar CONFIRM`, then `.beelz admin bar PerpetualChaos`; PASS when it shows 0 binds and 0 other-sourced mods, the weapon skills are visible before any swap, `.beelz admin bar-raw Chaos` shows no weapon-buff Empty mod on slots 2, 3, 5, 6, 7 or 8, Space has the dash, a vanilla spell drops onto R, C and T on the first try, and they stay after a weapon swap and a relog
- [ ] D8 · **purge clears the held weapon at once** after `.beelz admin purge PerpetualChaos CONFIRM` bound spells leave the live bar immediately (no weapon swap) and hotkeys are gone · manual: Test fixture steps 1-4, `.beelz admin purge PerpetualChaos CONFIRM`; PASS when the two slots show vanilla abilities without a swap and `.beelz hotkey list` is empty
- [ ] D9 · **Forms stay clean after a reset** with no form or universal binds, entering a native form after resetbar shows the form's own kit, not captured abilities · manual: after D7, shapeshift to Wolf; PASS when the bar shows only Wolf abilities
- [ ] D10 · **No mod pile-up** running resetbar three times in a row leaves every slot's other-sourced mod count at 0 (a weapon-buff Empty mod on a slot the weapon does not own counts as other, D32) and the gear-sourced count equal to its value after the first run · manual: Test fixture steps 1-3, `.beelz resetbar CONFIRM` ×3 with `.beelz admin bar PerpetualChaos` after each; PASS when the per-slot counts do not grow
- [ ] D11 · **Offline target is safe** for an offline character the planner returns only ClearSavedBindings (which drops a parked transform record too), ClearHotkeys (Purge only) and SaveBindings, and the reply says the live bar resets on next login; an online character with `liveReady=false` (its `BuffBuffer`, `AbilityGroupSlotBuffer` or held `EquipBuff_Weapon_*` missing, e.g. mid-respawn) gets the same saved-only plan plus Readback, and the result is `Clean=false`, `Unreadable=true`, reply "live bar not reachable — relog or respawn, then .beelz admin bar" · test: Beelzebub.Tests/BarResetTests.cs (fails when: any live step is planned for online=false, an offline plan with a parked transform omits ClearSavedBindings or SaveBindings, or online with liveReady=false plans a live step or yields Clean=true)
- [ ] D12 · **A failing step does not stop the rest** `BarResetRunner.Run(IBarResetOps, steps)` runs every step, catches each exception, records it as that step's failure and continues; when RevertTransform failed, ClearSavedBindings keeps the active/parked transform record so a retry reverts it · test: Beelzebub.Tests/BarResetTests.cs (fails when: a fake ops whose ClearEquipEntries throws makes PopSlotMods or Readback not run, or a fake ops whose RevertTransform throws is not told to keep the transform record)
- [ ] D13 · **Commands share one path** `resetbar`, `admin reset-loadouts` and `admin purge` call `BarResetService.FullReset`; under `Commands/` the only reset entry points allowed are `BarResetService.FullReset` and `BarResetService.ReadBar` (allowlist) — every other reset-layer symbol is rejected, the set being every member of `IBarResetOps` (read from `Logic/BarReset.cs` at check time), every public `BarResetService` member other than the two, and `RemoveInjectedRows`, `ReapplyEquipRows`, `PopSlotModifications`, `ForceResetAbilitySlots`, `PurgeAbilitySlotModifications`, `ClearAllLoadouts`, `ClearAllSlots`, `RestoreSlotBaseValue`, `TrySaveSync`; the only exempt methods are the `LEGACY:` handlers of `clearslotmods` and `rebuildslots` · cmd: `python Beelzebub/tools/check_bar_reset.py commands` → `commands: ok, <n> symbols` (fails when: a non-exempt method under Commands/ references any symbol in the set, including a new IBarResetOps member, or FullReset is called fewer than 3 times — zero call sites or an empty symbol set prints FAIL)
- [ ] D14 · **Dead and misleading text removed** `admin rebuildbar` runs the `admin bar` handler instead of `ForceAbilityBarReinit`; `clearslotmods` and `rebuildslots` descriptions start with `LEGACY:`; no reply names the non-existent `.beelz slot` · cmd: `python Beelzebub/tools/check_bar_reset.py text` → `text: ok` (fails when: Commands/ calls ForceAbilityBarReinit, a string contains ".beelz slot " or ".beelz slot.", or either LEGACY prefix is missing)
- [ ] D15 · **Targeting commands are admin-only** every `[Command]` in a class whose `[CommandGroup]` is `beelz admin`, and every `[Command]` anywhere whose method has a parameter named `player` or `target`, carries `adminOnly: true`; player commands `resetbar`/`clearbar` take no target parameter; every caller of `BarResetService.FullReset` or `ReadBar` anywhere under `Beelzebub/Beelzebub/` is the `ResetBar` handler (self only, target = the sender's own character) or a method carrying `adminOnly: true` · cmd: `python Beelzebub/tools/check_bar_reset.py auth` → `auth: ok, <n> admin commands` (fails when: one such command lacks adminOnly: true, resetbar gains a player parameter, a patch, hook, service or API handler calls FullReset or ReadBar, or fewer than 50 admin commands are parsed — a parse that finds none prints FAIL)
- [ ] D16 · **Reset log line has a fixed schema** `Logic/BarResetLog.Format(result, targetName, steamId, elapsedMs)` returns exactly one line `[Beelz RESET] run=<n> scope=<s> target=<name> (<steamId>) ms=<n> steps=<Step:count|Step:ERR> ... survivors=<slots|none|unreadable> clean=<0|1>` (clean=1 only for a `Clean` result) plus ` slow=1` when ms > 250, the name passed through `LogSafe` (control characters and `[`/`]` removed, capped at 32 characters), and no field outside that list; `BarResetLog.FormatBar(readback, targetName, steamId)` returns exactly one line `[Beelz BAR] target=<name> (<steamId>) binds=<n> rows=<n> gear=<n> other=<n|unreadable> slots=<i:bind|none:gear:other,...|none>` — above 400 characters the slots list is split into continuation lines ending ` part=<k>/<n>`, each with the same target prefix, and `FormatLate(targetName, steamId, runId, slot)` returns `[Beelz RESET] late-survivor target=<name> (<steamId>) run=<runId> slot=<n>` where `runId` is the `run=<n>` field also printed on that reset's summary line · test: Beelzebub.Tests/BarResetLogTests.cs (fails when: a name containing a newline yields two lines, a step is missing, survivors=none is printed for an unreadable readback, clean=1 is printed for a result with a failed step, ms=300 lacks slow=1, or FormatBar prints other=0 for an unreadable slot)
- [ ] D17 · **Recovery guide rewritten** `docs/RECOVERY_GUIDE.md` gives one ordered flow — `.beelz admin bar` → `.beelz resetbar CONFIRM` or `.beelz admin purge` → (only if survivors remain) `.beelz admin reset-character` after a purge — and states that `respawn` and `reset-character` both keep the Steam-keyed binds, so neither is a bar fix on its own · cmd: `python Beelzebub/tools/check_bar_reset.py docs` → `docs: ok` (fails when: the guide lacks any of the three commands, lists them out of order, or still calls reset-character or respawn "guaranteed clean")
- [ ] D18 · **BCH handoff and ApiVersion** `ApiVersion` is 33; the handoff banner reads `ApiVersion = 33` and a v0.137 entry names `Forms_AutoFillFromCaptures`, `admin bar` and `api>=33` · cmd: `python Beelzebub/tools/check_bar_reset.py docs` → `docs: ok` (fails when: ApiCommands.cs and the banner disagree, or the entry lacks the key or the gate)
- [ ] D19 · **Release gate passes** version 0.137.0 in csproj and toml, CHANGELOG entries in both changelog files, README status line, root README regenerated, and `check_bar_reset.py all` inside preflight · cmd: `pwsh Beelzebub/tools/preflight.ps1` → PREFLIGHT OK (fails when: the csproj and toml versions differ, CHANGELOG.md has no [0.137.0] entry, the README status line names another version, or any check_bar_reset subcheck fails)
- [ ] D20 · **Session logs clean** after the in-game session, before any restart · cmd: `pwsh Beelzebub/tools/preflight.ps1 -LogCheck -LogDir <dir>` (default: the dev server's two logs) → 0 Beelzebub stack frames in both logs (fails when: LogOutput.log contains a line starting "at Beelzebub." or either log is missing or empty)
- [ ] D21 · **Planted faults are re-runnable** `Beelzebub/tools/fault_harness.py bar-reset` reads `Beelzebub/tools/faults/bar-reset/manifest.json` (one entry per patch: D-item, evidence command, the exact `fails when` clause it plants), and checks it against this plan: every `test`/`cmd` item (D1 D2 D3 D4 D5 D11 D12 D13 D14 D15 D16 D17 D18 D19 D20 D22 D23 D24 D25 D26 D27 D28 D29 D30) and every clause of its `fails when:` has a patch; each patch is a real source or fixture change applied with `git apply` — never a runtime argument (D19: toml version changed, `[0.137.0]` entry removed, README status line changed, a `.beelz slot ` reply added; D20: an `at Beelzebub.` line added to, or the whole of, the tracked fixture `Beelzebub/tools/faults/bar-reset/logs/LogOutput.log` emptied, run with `-LogDir` on that fixture dir; D30: each defect clause planted into the tracked fixture `Beelzebub/tools/faults/bar-reset/logs/session.log`); it runs the entry's command expecting a non-zero exit, reverts with `git apply -R` and expects zero; the audit record stores its output and the patch-directory hash · cmd: `python Beelzebub/tools/fault_harness.py bar-reset` → `harness: ok, <n> faults, <k> clauses` (fails when: a patch's command still exits 0 while planted, fails after revert, an automated item or one of its fails-when clauses has no manifest entry, a manifest entry's command is not that item's evidence command, or the working tree is dirty for a patched file; zero patches found prints FAIL)
- [ ] D22 · **Rollback range resolves** after the `chore(release): v0.137.0` commit, a follow-up `docs(audit)` commit adds to the audit record `rollback: git revert --no-edit <first>^..<release>` with both full SHAs; `check_bar_reset.py rollback` resolves both with `git rev-parse`, requires `<release>` to be the commit whose subject is `chore(release): v0.137.0`, requires `<first>` to be an ancestor of it and a descendant of 3fbc2d4, and checks that `PersistenceService` still writes `Version = 8` · cmd: `python Beelzebub/tools/check_bar_reset.py rollback` → `rollback: ok` (fails when: a SHA does not resolve, the release subject differs, the order is wrong, the state version changed, or no rollback line exists)
- [ ] D23 · **Only declared paths changed** every path in `git diff --name-only 3fbc2d4..HEAD` plus `git status --porcelain=v1 --untracked-files=all` (excluding the owner's own `Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx` and `_matrix_build.py`) appears in Rollout › Paths walked · cmd: `python Beelzebub/tools/check_bar_reset.py paths` → `paths: ok, <n> paths` (fails when: a changed or untracked path is not in the list; zero parsed list entries prints FAIL)
- [ ] D24 · **Unreadable is never clean** when any slot's dump is `Unreadable`, `BarResetResult.Clean` is false, PopSlotMods skips that slot and destroys no source for it, and the reply says "could not read slot mods"; likewise a readback that still lists an override buff (not queued for destruction) is never clean and the reply names the buff · test: Beelzebub.Tests/BarResetTests.cs (fails when: a fake readback returning Unreadable, or returning null, produces Clean=true or a destroy call for that slot; a fake readback listing a surviving override buff produces Clean=true or a reply that does not name it)
- [ ] D25 · **Input rules are one function** `Logic/BarResetInput.IsConfirm(token)` accepts only `CONFIRM` (trimmed, case-insensitive) and every reset command uses it; a missing or other token changes nothing and replies with the exact command to re-run · test: Beelzebub.Tests/BarResetInputTests.cs (fails when: `confirm`, ` CONFIRM ` is rejected, or `CONFIRMED`, empty or null is accepted)
- [ ] D26 · **Deferred work has a home** `Beelzebub/Beelzebub/docs/BACKLOG.md` lists each item under Out of scope with the slug it will be planned under · cmd: `python Beelzebub/tools/check_bar_reset.py docs` → `docs: ok` (fails when: BACKLOG.md is missing or lacks any of clearbar-fullreset, transform-chain-guard, docs-consolidation, dev-snapshot)
- [ ] D27 · **A failed save is never clean** SaveBindings calls a new `PersistenceService.TrySaveSync()` (returns false instead of swallowing the error); false or an exception makes that step ERR, `Clean=false`, and the reply says "not saved — binds may return after a restart, run it again" · test: Beelzebub.Tests/BarResetTests.cs (fails when: a fake ops whose SaveBindings returns false yields Clean=true, or later live steps do not run)
- [ ] D28 · **Every artifact has a retention row** each artifact the feature writes (state.json binds, `[Beelz RESET]`, `[Beelz BAR]`, the Audit line, the cfg key, log copies, review files, audit record, deployed DLL) has a row in Design › Data naming owner, location, retention and copies · cmd: `python Beelzebub/tools/check_bar_reset.py data` → `data: ok, <n> artifacts` (fails when: a row lacks one of the four fields, a declared producer has no matching row — producers are found by scanning the files in Rollout › Paths walked for a `[Beelz <TAG>` log literal, a `Config.Bind(` key, `File.Write`/`File.Replace`, `open(...,'w')`, `mkdtemp` or a `bin/`/`dist/` output; zero parsed rows prints FAIL)
- [ ] D29 · **The checker checks itself** `check_bar_reset.py selftest` runs each of the subcommands config, commands, text, auth, docs, audit, paths, data, rollback and session (never itself) against three generated fixture trees in a temp dir — empty (each must FAIL with "no input"), one planted defect per subcommand plus ten more for `session` — not clean, only another player's FORM line, a step :ERR, a skipped layer, a truncated BAR part set, a bad Purge line only, a bad run 3 only, a duplicate step hiding :ERR, `clean=10`, :ERR on a non-save step, a malformed first reset line (each must FAIL) — and a good tree (must print ok) — then deletes the trees · cmd: `python Beelzebub/tools/check_bar_reset.py selftest` → `selftest: ok, 41 cases` (fails when: any subcommand prints ok on the empty or defect tree, or FAIL on the good tree)
- [ ] D30 · **Session log proves the in-game steps** `check_bar_reset.py session <log>` reads a copied `LogOutput.log` from the release-candidate session and checks, in order: D6 — two consecutive identical `[Beelz BAR]` lines for PerpetualChaos whose `slots=` show `bind` on slots 1 and 4; D7 — the FIRST `[Beelz RESET] scope=PlayerReset` line (a later retry does not rescue a failed run) with `survivors=none clean=1`, every required step present and no raw `:ERR` token, the line ending at `clean=` or ` slow=1`, a following `[Beelz BAR]` with `binds=0 rows=0 other=0`, and no `late-survivor` line for that target; D8 — a `[Beelz RESET] scope=Purge` line with `survivors=none clean=1`, every required step, no `:ERR`, and `ClearHotkeys:1`; D9 — a `[Beelz FORM] target=<PerpetualChaos's Steam ID> form=Wolf source=native` line after the D7 reset; BAR `part=k/n` lines are joined only as a complete, ordered, same-header set; D10 — three consecutive PlayerReset runs whose `[Beelz BAR]` lines keep `other=0` and the same `gear=` · cmd: `python Beelzebub/tools/check_bar_reset.py session %TEMP%/beelz-logs-<date>/LogOutput.log` → `session: ok D6 D7 D8 D9 D10` (fails when: any of the five line groups is missing, a BAR line after a reset shows `other=` above 0 or `unreadable`, the two D6 lines differ, a D10 `gear=` value changes, a `late-survivor` line follows the D7 reset, a reset line has `clean=0` or a step `:ERR` or lacks a required step, the only Wolf FORM line is another player's, a BAR part set is truncated, mixed or out of order, or any `[Beelz RESET]`/`[Beelz BAR]` line does not parse; an empty or missing log prints FAIL no input)
- [ ] D31 · **Weapon's own rows survive a reset** `Logic/EquipRows.cs` `EquipRowDiff.InjectedIndices(live, prefab)` returns only the rows on slots 0-7 of the held equip buff that its prefab does not carry (slot + ability, counting copies) and `MissingPrefabIndices` the prefab rows absent from the live buffer; `SlotApply.RemoveInjectedRows` removes the first and puts back the second, and an unknown prefab removes nothing and makes the step ERR · test: Beelzebub.Tests/EquipRowsTests.cs (fails when: a vanilla Sword row on slot 0, 1 or 4 is returned as injected, an extra copy of a vanilla row is kept, a row on slot 8 is returned, a null prefab returns a non-null list, or a stripped prefab row is not reported missing)
- [ ] D32 · **Empty push only on weapon slots** `Logic/BarReset.cs` `SlotOwnership.PushTargets(prefabRowSlots, maxSlot)` returns the distinct slots 0-maxSlot the held equip buff's PREFAB carries a row for, and `ForceResetAbilitySlots` (EmptyPush, `admin purge`'s legacy path, `rebuildslots`) pushes Empty only to those and ERRs when the prefab rows cannot be read; `SlotOwnership.IsLeakedEmpty` makes a gear-sourced Empty mod on a slot the weapon does not own count as other in the readback (never clean); PopSlotMods pops a slot again while its GroupGuid mod count keeps falling (`SlotOwnership.PopAgain`, at most 16 rounds) and logs `[Beelz LEAK]` for every bar slot that had mods · test: Beelzebub.Tests/SlotOwnershipTests.cs (fails when: a slot with no prefab row, or one above maxSlot, is a push target; a duplicated row slot is pushed twice; a null row list yields a target; a gear Empty mod on a slot the weapon does not own is not leaked, or one on an owned slot, a non-Empty gear mod, a non-gear mod, or any mod with an unknown owned set is; PopAgain stops while the count is still falling below the cap, or continues when the count did not fall or the cap is reached)

## Purpose & typical use
A player binds captured spells to their bar (`.beelz grant` / `weapon-grant` / `form-grant`), later wants the bar
back to normal, and says "reset my bar" — `.beelz resetbar CONFIRM`. An admin helping a stuck player says "show me
what is on their bar, then clean it" — `.beelz admin bar <player>` then `.beelz admin purge <player> CONFIRM`.
Frequency: rare per player, but every stuck case today costs an admin session. It replaces the dozen
partly-overlapping resets (`resetbar`, `clearbar`, `reset-loadouts`, `purge`, `rebuildslots`, `clearslotmods`,
`rebuildbar`, `respawn`) as the recommended path; those names remain.

## Use cases
### Typical
The owner (character PerpetualChaos) binds two spells on the Sword bar, the bar sticks, runs `admin bar` to see
which layer holds the spells, runs `resetbar`, and the bar is vanilla without a relog (D6, D7).
### Minimal stretch
A player with nothing bound runs `resetbar CONFIRM`: the reply says 0 bindings removed and the readback shows no
survivors; nothing else changes (D10). A player runs `resetbar` with no token: nothing changes and the reply shows
the exact command (D25). An admin runs `admin bar` on an offline name: the reply says the player is offline and
shows only saved binds (D11). A one-time use leaves only: two log lines (overwritten at the next boot), one
existing `Audit(...)` line, and the new `.cfg` key (written once at first boot, harmless) — nothing nags (D5, D16).
### Test fixture (for the manual items D6-D10)
The owner runs the dev server at 127.0.0.1:9876 (BepInEx + this build; shared with Nyarlathotep). The tester logs in
as the admin character PerpetualChaos (SteamID 76561198039548286, admin via the server's `adminlist.txt`), reads
replies in the in-game chat window and log lines in `BepInEx/LogOutput.log`. Steps:
1. Equip a Sword.
2. `.beelz admin give PerpetualChaos <unitGuid> <abilityGuid>` twice, with two enabled abilities (unit and ability GUID) from
   `.beelz api catalog abilities`; `.beelz list` then shows them as index 0 and 1.
3. `.beelz grant 1 0` and `.beelz grant 4 1` — slots 1 and 4 show the two spells.
4. (D8 only) `.beelz hotkey set test 0` — `.beelz hotkey list` shows one entry.
### Maximal stretch
A player runs `resetbar` repeatedly (D10 — no pile-up); an admin purges while the player is transformed or parked
mid-disconnect (D2); a player with 300 captures and binds in every weapon and form set resets (ClearSavedBindings
is one dictionary removal per set — D3); a name crafted to forge log lines is neutralised by `LogSafe` (D16).

## Business rules
1. Layer order is fixed (D2): saved binds are cleared before live layers so the re-inject hooks (weapon equip
   `ReplaceAbilityOnSlotSystemPatch`, login `TransformService.ReconcileOrphanBuffs`, form enter
   `ShapeshiftAbilityService`) find nothing to put back; equip rows are removed before mods are popped so no
   removed row is re-applied; exactly one Empty push happens, after the pop (D2, D10).
2. Scope table (D3): PlayerReset = AdminLoadouts = binds + transform loadouts + baseline; Purge = that + hotkeys.
   Captures, transform unlocks and presets are never touched by any reset (`.beelz clear` and `admin wipe-all`
   stay the only ways to delete them). Precedence when a caller could mean two scopes: the command decides — the
   scope is a fixed argument of each command, never inferred; the owner decides exceptions (D3).
3. Form bar precedence (D4): form binds > universal binds > captures (only with `Forms_AutoFillFromCaptures`).
4. "Gear-sourced" (D6, D10) means a modification whose source entity's prefab name starts with `EquipBuff` or
   `Item_` — the same test `DestroyOwnedAbilitySlotOrphans` uses today; every other source is "other-sourced".
   Known limitation (accepted): a foreign override whose source prefab happens to be named `Item_*`/`EquipBuff*` is
   treated as gear and kept; `admin bar` prints each kept source's prefab name so an admin can see it (D6). A mod
   sourced by the character itself or one of its slot entities is the engine's own and counts as vanilla
   ("character"), never other-sourced. Known limitation (accepted, safe direction): when PopSlotMods has to destroy
   a stuck source, the engine patches that slot one tick later, so that run reads it as a survivor and reports
   not clean; the reply says to run `.beelz admin bar`, which then shows it clean. Known limitation (accepted): a
   reset while mounted does not read or clear the saddle's `ReplaceAbilityOnSlotWhenMountedBuffElement` rows —
   dismount first (backlog slug `mounted-bar-reset`). Known limitation (accepted): ClearEquipEntries removes every
   row on slots 0-7 that the equip-buff prefab does not carry, so a row another server mod put on the same weapon
   equip buff is removed too (and reads `row=yes` until then); that mod re-adds it on its own terms.
5. "Every X" sets (probe 4.5): *every slot* for PopSlotMods = every index of the character's
   `AbilityGroupSlotBuffer` (computed at run time; the buffer holds about 296); the readback — the `[Beelz BAR]` log
   line, the survivors that decide Clean, and the chat — covers the bar slots 0-8 only, the slots a player sees
   (D6, D24). *Every admin or targeting command* = computed by
   parsing all `Commands/*.cs` at check time, not a hand list, so a new alias is caught (D15). *Every changed
   path* = `git diff` plus `git status --porcelain=v1 --untracked-files=all`, so new, untracked and generated
   tracked files count; ignored build outputs (`bin/`, `obj/`, `dist/`) are declared, not diffed (D23).
6. Temporal: a transform "parked" by the disconnect grace (`BeginReconnectGrace`) counts as active for
   RevertTransform (D2); for an offline target ClearSavedBindings drops its record so login does not revive it
   (D11) — known limitation (accepted): with the record gone, the reconnect path no longer despawns that
   transform's summons or carrier, so a carrier that survived on the body can pin the bar at login; the next
   reset's DestroyOverrideSources removes it. Cooldowns are not a reset concern and are left alone (the pre-0.137
   purge also cleared transform cooldowns; the plan keeps them).
7. Weapon skills come back without a swap (decided 2026-09-30): D7 is a release condition. The in-game session runs
   on an uncommitted **release candidate** (version already 0.137.0, PREFLIGHT OK) before the `chore(release)`
   commit exists; if any of D6–D10 fails, no release commit is made — the fix is recorded as a `defect` amendment,
   committed as its own step, and the candidate is rebuilt and re-tested. The contract is not relaxed to "swap".
   The release commit, and so the rollback range (D22), is created only after D30 passes on the session log.
8. Durable means saved: a reset reports clean only after `TrySaveSync` wrote `state.json` (D27).

## Interfaces
### Internal — reads / writes / changes (paths or symbols)
- Reads: `AbilityRegistry` (`GetSlots`, `GetFormSlots`, `ListHotkeys`, `GetActiveTransform`), the character's
  `AbilityGroupSlotBuffer`, `ModificationsRegistry.GetFormattedEntityModificationsMessage` (D1, D6).
- Writes: `AbilityRegistry.ClearAllLoadouts` / `ClearHotkey` / `ClearActiveTransform`; equip-buff
  `ReplaceAbilityOnSlotBuff` rows via a new `SlotApply.RemoveInjectedRows(character)` that removes rows only and
  does **not** call `RestoreSlotBaseValue` (the call that piled mods up — D10);
  `TransformBuffService.RemoveAllFormsAndShapeshifts`, `DestroyOwnedAbilitySlotOrphans`; the pop pass of
  `PurgeAbilitySlotModifications` split out without its trailing `ForceResetAbilitySlots`; one
  `ForceResetAbilitySlots`; Reapply = re-run the equip buff's remaining (vanilla) rows through
  `ServerGameManager.ModifyAbilityGroupOnSlot(equipBuff, character, slot, group)`, logging
  `[Beelz REAPPLY] slot=<n> before=<guid> after=<guid>` per slot (assumption S-2's diagnostic; its own tag, since
  `[Beelz RESET]` lines are schema-checked by D30) (D7, D8, D13); SaveBindings = new `PersistenceService.TrySaveSync()` returning bool (`SaveSync` keeps its
  signature and calls it) (D27).
- Changes: `ShapeshiftAbilityService.BuildFormBar` fallback (D4); `Beelzebub/Beelzebub/Commands/BeelzCommands.cs`
  `resetbar`; `Beelzebub/Beelzebub/Commands/AdminCommands.cs` `purge`, `reset-loadouts`, `rebuildbar`, new `bar`
  (D13–D15). What breaks if wrong: a missed layer leaves spells on the bar (the reported bug); an over-broad pop
  removes weapon skills until a swap (guarded by Reapply and the readback, D7, D24).
- New shared contracts in `Logic/`: `BarResetStep`, `BarResetScope`, `IBarResetOps`, `BarResetResult`
  (`Steps: list of (Step, Count, Error)`, `Survivors: list of slot index`, `Unreadable: bool`, `Clean: bool`),
  `SlotModEntry(ModId, SetToGuid, SourceIndex, SourceVersion)`, `BarResetLog.Format`, `LogSafe.Field`,
  `BarResetInput.IsConfirm` (D1, D2, D16, D24, D25).
### Inputs
- `player` (admin commands): matched by the existing `EntityExtensions.FindCharacterByName` — exact or unique
  match; no match or several → reply `No (or ambiguous) player match for '<name>'.` and nothing changes (D15).
- `confirm`: `BarResetInput.IsConfirm` (D25). Extra or missing positional arguments never reach our code: VCF answers with
  its usage text (VCF 0.10 arity binding); a missing CONFIRM token is our own no-op path (D25).
### External — dependencies and their failure behaviour
- V Rising dedicated server (the build on the dev server, BepInEx runtime 6.0.7; reference assemblies
  `VampireReferenceAssemblies` 1.1.12-r99041-b2), BepInEx IL2CPP 6.0.0-be.733, VampireCommandFramework 0.10.x. No
  quota or cost — all local. Engine calls used: `ModificationsRegistry`, `ServerGameManager`
  `RemoveAbilityGroupModificationOnSlot` / `ModifyAbilityGroupOnSlot`, `DestroyUtility`.
- Dump record classes sampled (probe 6.1): field headers `- AbilityGroupSlot.<Field>: …`, modification lines
  `[ModId n] Set PrefabGuid(x) from Entity(i:v) (…)`, and anything else (blank, other text) — ignored. A
  `[ModId` line that does not match the Set/from shape makes the dump `Unreadable` (D1).
- Failure behaviour (probe 6.2): an engine call that throws → that step records ERR, later steps still run (D12);
  an unreadable dump → no pop or destroy for that slot, `Clean=false`, reply says so (D24); target lookup fails →
  reply, nothing changes (Inputs); VCF fails to dispatch → our code never runs, nothing changes; a logging call
  that throws is caught by the runner like any step and never aborts the reset (D12); a `state.json` write that
  fails (disk, lock, serialisation) → SaveBindings ERR, `Clean=false`, the reply tells the player to run it again,
  and a restart would reload the old binds — so it is never reported as done (D27); engine work that lands after
  the command's tick (a projectile or buff from a cast already in flight) → see States 7.3. BCH is not called by a reset;
  it only reads `api config` / `api slots` afterwards and sees an additive key (D18).
- Test mode: none exists in the engine; `Logic/` is exercised through fakes of `IBarResetOps` compiled into
  `Beelzebub.Tests` only (the test csproj links `Logic/*.cs` one way).

## Design
### Data
No saved-data shape changes: `state.json` (`PersistenceService`, `Version = 8`) keeps its fields; a reset only
removes entries (D22 checks the version). Artifacts and retention (probe 3.3) — one row each, parsed by
`check_bar_reset.py data` (D28):
- artifact: state.json binds · owner: the server · location: `BepInEx/config/kdpen.Beelzebub/state.json` · retention: cleared entries are gone once SaveBindings succeeds (D27) · copies: one, plus `state.json.tmp` during the write — `TrySaveSync` deletes the `.tmp` (best effort) in its catch when `WriteAllText`/`Replace`/`Move` fails; if that delete also fails, the next save's `WriteAllText` overwrites it, so at most one `.tmp` exists and it is never read at boot
- artifact: [Beelz RESET] line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (D16)
- artifact: [Beelz BAR] line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (D6)
- artifact: [Beelz REAPPLY] line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (assumption S-2)
- artifact: [Beelz BARRAW] line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (build step 10 diagnostic)
- artifact: [Beelz LEAK] line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (D32 pop-loop evidence)
- artifact: [Beelz FORM] line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (D4, D30)
- artifact: [Beelz PURGE] unreadable-dump warning · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot · copies: one prior boot in `LogOutput.1.log` (D1, D24)
- artifact: fault fixture logs · owner: the repo · location: `Beelzebub/tools/faults/bar-reset/logs/` · retention: tracked, kept with the plan · copies: one (D21, D30)
- artifact: Audit line · owner: the server · location: `BepInEx/LogOutput.log` · retention: overwritten at the next boot (unchanged behaviour) · copies: one prior boot
- artifact: Forms_AutoFillFromCaptures · owner: the server admin · location: `BepInEx/config/kdpen.Beelzebub.cfg` · retention: until an admin edits it · copies: one (D5)
- artifact: session log copies · owner: the project owner · location: `%TEMP%\beelz-logs-<date>\` · retention: deleted by the owner after the audit entry is written · copies: one
- artifact: review prompts · owner: the dod script · location: `%TEMP%\dod-review-*.txt` · retention: swept by the script after 24 h · copies: one per round
- artifact: review page · owner: the repo · location: `docs/dod/bar-reset.review.html` · retention: tracked, replaced each round · copies: one (D23)
- artifact: audit record · owner: the repo · location: `docs/audits/bar-reset.md` · retention: tracked, append-only · copies: one (D21, D22)
- artifact: selftest fixture trees · owner: check_bar_reset.py · location: a `tempfile.mkdtemp()` dir · retention: deleted before selftest exits, also on failure · copies: one per run (D29)
- artifact: fault-harness output · owner: the repo · location: pasted into `docs/audits/bar-reset.md` · retention: tracked, append-only · copies: one per run (D21)
- artifact: fault patches · owner: the repo (written by `fault_harness.py --make`) · location: `Beelzebub/tools/faults/bar-reset/` · retention: tracked, kept with the plan · copies: one (D21)
- artifact: test and build outputs · owner: dotnet · location: `bin/`, `obj/`, `Beelzebub.Tests/bin`, `dist/` (gitignored) · retention: overwritten by each build · copies: one
- artifact: deployed DLL · owner: the server admin · location: `BepInEx/plugins/Beelzebub.dll` · retention: replaced by the next deploy · copies: one
Nothing to migrate (probe 3.4).
### States
- Online / offline target (D11); online but partial — the character exists and lacks `BuffBuffer`,
  `AbilityGroupSlotBuffer` or the held `EquipBuff_Weapon_*` (mid-respawn, mid-load): `liveReady=false`, saved
  binds are still cleared and saved, no live step runs, the result is `Clean=false`/`Unreadable` and the reply says
  to relog or respawn and re-run `admin bar` — never a silent Clean (D11, D24); transformed / parked / normal (D2); in a native form (the form bar is rebuilt on
  the next form enter — D9); mid-cast — the reset runs on the server tick of the command, a cast in flight
  finishes with its own ability.
- Concurrency (probe 7.2): two admins resetting the same player, or the player running resetbar while an admin
  purges — chat commands run on the main server thread one at a time (S-3), so they serialise; the second run
  finds nothing and reports 0 (D10). The re-inject hooks cannot interleave inside a command.
- Stale state / re-entry (probe 7.3): a reset has no undo — the reply says captures are kept and binds can be
  re-granted; a relog after the reset re-injects nothing because the saved binds are gone (D7). A cast already in
  flight can land an override after the Readback; it has no saved bind behind it, so the next weapon swap or
  relog drops it, and re-running `admin bar` shows it and `resetbar` clears it (D6, D10). `Clean` means clean at
  Readback; D7's PASS line re-checks after a swap and a relog, so a late survivor fails D7. To tell that race from a
  good reset, `FullReset` re-reads the bar one server tick later and logs `[Beelz RESET] late-survivor slot=<n>`
  for any slot that became overridden (D16); D30 fails on such a line after the D7 reset.
### Permissions
- Actors (probe 2.1): a player may run `resetbar` / `clearbar` on themselves only (no target parameter, D15); an
  admin may run `admin bar` / `purge` / `reset-loadouts` / `rebuildbar` on any player or themselves (D15); the
  server console has no chat sender and VCF does not dispatch these to it; an unauthenticated client never
  reaches command dispatch — the server only accepts chat from a connected, Steam-authenticated user, and VCF
  dispatches from that chat system with the sender's `User` (S-3), so there is no anonymous path to a handler.
- Unauthorized path (probe 2.2): VCF denies non-admins with its own message; our code never runs (D15).
- Ownership (probe 2.3): binds belong to the Steam account; an admin acting on another player is logged by the
  existing `Audit(...)` line plus `[Beelz RESET]` naming the target (D16). A character re-roll keeps the Steam
  account, so its binds follow — the guide says so (D17).
### UX
- Discovery (probe 11.1): `.beelz help` and `.beelz commands` list `admin bar`; the resetbar reply ends with
  "still stuck? ask an admin for .beelz admin bar"; the recovery guide leads with it (D6, D17).
- Feedback (probe 11.2): each reset replies in at most two lines under VCF's 512-byte cap: counts per layer and
  either "bar is vanilla" or "slots still overridden: <list> — tell an admin" (D7, D16, D24). Replies avoid `<` `>`.
- Accessibility (probe 11.3): chat text only; no colour-only meaning; slot numbers written as the player sees them
  (1-6, primary, ultimate) (D6).
- Activation (probe 11.4): nothing runs unasked — every reset is an explicit command with CONFIRM (D13, D25); the
  form bar change applies on the next form enter (D4); the evidence it ran is the `[Beelz RESET]` line (D16).

## Security
- Authorization on every path (probe 10.1): the entry points are exactly the commands whose methods call
  `BarResetService.FullReset` or the bar readback (D13), all admin-only when they can target someone (D15).
- Injection (probe 10.2): the player argument is only matched and printed; printed names and dump text go through
  `LogSafe` (D16); the dump parser uses anchored, non-backtracking regexes on single lines (D1).
- Secrets (probe 10.3): none are read or written; the log line is built only from the enumerated fields of
  `BarResetLog.Format` (D16).
- Personal data (probe 10.4): steamId + character name in the server log only, as the existing `Audit` lines do;
  nothing leaves the server (D16).

## Failure & observability
- What the user sees (probe 12.1): a failed step names itself in the reply ("PopSlotMods failed: <msg>") and the
  next action — swap weapon, relog, or ask an admin (D12, D24).
- Logged (probe 12.2): one `[Beelz RESET]` summary per run plus `[Beelz BAR]` readback before and after (D6, D16).
- Knowing it is broken (probe 12.3): there is no alerting on a private server; the signal is `survivors=` not
  `none` in the log and `preflight -LogCheck` after each session (D16, D20).
- Failing cases (probe 12.4): the check matrix below lists every check with its failing input, its silent input
  and its empty input (which never passes). Every automated check — including the session-log check D30 that
  turns D6–D10 into a command — gets a planted fault per `fails when` clause (D21), and the checker tests itself
  on empty, defect and good fixture trees (D29). The in-game PASS lines stay the tester's view; D30 is the
  command that fails when their evidence is absent.

| Item | Check | Fails on | Silent on | Empty input |
|---|---|---|---|---|
| D1 | SlotModDumpTests | CopyCooldown mod returned as GroupGuid | GroupGuid-only dump | empty dump gives 0 entries, Readable |
| D2 D3 D11 D12 D24 D27 | BarResetTests | wrong order or scope, live step offline, runner stops, unreadable or unsaved reported clean | the documented plans | unknown scope gives an empty plan |
| D4 | FormBarFillTests | capture used with autoFill=false | form bind wins | no binds gives an empty bar |
| D16 | BarResetLogTests | newline in name gives 2 lines | plain name | no steps still gives one line |
| D25 | BarResetInputTests | `CONFIRMED` accepted | ` confirm ` | null or empty gives false |
| D5 D13 D14 D15 D17 D18 D22 D23 D26 D28 | check_bar_reset.py subchecks | the defect named in the item | the good tree | nothing parsed prints FAIL no input |
| D21 | fault_harness.py | a planted patch leaves its check green | every patch caught | no patches prints FAIL |
| D29 | check_bar_reset.py selftest | a subcheck ok on the empty or defect tree | all correct | no fixtures built prints FAIL |
| D19 | preflight.ps1 | toml version differs, no [0.137.0] entry, stale README status | the release tree | missing file prints FAIL |
| D20 | preflight.ps1 -LogCheck | an `at Beelzebub.` frame | clean logs | missing or empty log prints FAIL |
| D30 | check_bar_reset.py session | D6: the two BAR lines differ or slot 1/4 lacks `bind`; D7: `other=1` or `late-survivor` after the reset; D8: `survivors=2` or no `ClearHotkeys:1`; D9: `source=captures`; D10: `gear=` grows | a log of a passing session | empty or missing log prints FAIL no input |
| D6 D7 D8 D9 D10 | manual, Test fixture (tester's view; D30 is their command) | D6: slot 1 shows a vanilla ability; D7: slot 4 still shows the bound spell; D8: the spells stay until a swap; D9: a captured spell on the Wolf bar; D10: other-sourced count 0→1→2 | every PASS line met | a fixture step fails (e.g. the grant of index 0 fails): the run counts as failed |

- Gating probes, one evidence command each: 2.1 → `python Beelzebub/tools/check_bar_reset.py auth` (D15);
  3.3 → `python Beelzebub/tools/check_bar_reset.py data` (D28); 4.4 →
  `dotnet test Beelzebub/Beelzebub.Tests --filter "FullyQualifiedName~BarResetTests|FullyQualifiedName~FormBarFillTests"`
  (fails when either the scope rule or the form precedence is removed — D3, D4); 6.2 →
  `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetTests` (D12, D24, D27); 10.1 →
  `python Beelzebub/tools/check_bar_reset.py auth` (D13, D15 — `auth` also counts the FullReset call sites);
  10.3 → `dotnet test Beelzebub/Beelzebub.Tests --filter FullyQualifiedName~BarResetLogTests` (D16); 12.4 →
  `python Beelzebub/tools/fault_harness.py bar-reset` (D21 — plants a failing case for every `fails when` clause of
  every automated check, unit tests, preflight, checker and the session-log check D30 that covers D6–D10); 14.3 → `python Beelzebub/tools/check_bar_reset.py rollback`
  (D22); 14.4 → `python Beelzebub/tools/check_bar_reset.py paths` (D23).

## Performance
- Budget (probe 13.1): a reset runs once per command; the hot part is formatting the modification dump for each
  slot entity (about 296; the existing purge does this in one tick). Budget: under 100 ms on the dev server; the
  accepted upper bound is 250 ms, above which the `[Beelz RESET]` line carries `slow=1` (D16) and the step-10
  session records the largest `ms=` seen during D7-D10 in the audit record; above 250 ms that is a `defect`
  amendment for staged processing. A reset is a rare, typed command, so one long tick is
  accepted rather than split across frames. Accepted risk (owner-visible, recorded here on purpose): a character
  with mods on many of its ~296 slots may breach 250 ms on the first run; no synthetic stress fixture is built,
  because the engine formatter cannot run outside the server — the real worst case is observed in the session.
- Limits (probe 13.2): chat readback prints at most 9 slot lines truncated to fit 512 bytes; the log gets the rest
  (D6). `LogSafe` caps names at 32 characters (V Rising names are shorter) (D16).

## Build plan
1. Create `Beelzebub/Beelzebub/Logic/SlotModDump.cs` (namespace `Beelzebub.Logic`, no IL2CPP types): `record struct SlotModEntry(int ModId, int SetToGuid, int SourceIndex, int SourceVersion)`; `static SlotModParse ParseGroupGuid(string dump)` returning entries + `Readable`. Port the three parsers in `Beelzebub/Beelzebub/Services/TransformBuffService.cs` (`GroupGuidStillModified`, `ParseGroupGuidModIds`, `ParseGroupGuidSources`) onto it. Add `Beelzebub/Beelzebub.Tests/SlotModDumpTests.cs` with dumps in the formatter shape quoted above `_modIdRx`, a CopyCooldown section, an unparseable `[ModId` line and an empty string · satisfies D1
2. Create `Beelzebub/Beelzebub/Logic/BarReset.cs` (`BarResetScope`, `BarResetStep`, `BarResetPlanner.Plan`, `IBarResetOps`, `BarResetRunner.Run`, `BarResetResult`), `Logic/BarResetLog.cs` (`Format`, `FormatBar`, `FormatLate`, `LogSafe.Field`) and `Logic/BarResetInput.cs` (`IsConfirm`); `IBarResetOps.SaveBindings()` returns bool. Add `Beelzebub/Beelzebub.Tests/BarResetTests.cs`, `BarResetLogTests.cs`, `BarResetInputTests.cs` · satisfies D2, D3, D11, D12, D16, D24, D25, D27
3. Create `Beelzebub/Beelzebub/Logic/FormBarFill.cs` (`Build(formBinds, universalBinds, captures, usable, autoFillFromCaptures)` returning the bar and its `Source`; the caller logs `[Beelz FORM]`); add `Forms_AutoFillFromCaptures` (section `Forms`, default false) to `Beelzebub/Beelzebub/Config/Settings.cs`; replace the fallback block in `Beelzebub/Beelzebub/Services/ShapeshiftAbilityService.cs` (the `perSlot` fill near `ListFor(steamId)`) with a call to it. Add `Beelzebub/Beelzebub.Tests/FormBarFillTests.cs` · satisfies D4, D5
4. Create `Beelzebub/tools/check_bar_reset.py` with subcommands `config`, `commands`, `text`, `auth`, `docs`, `audit`, `paths`, `data`, `rollback`, `session <log>`, `selftest`, `all` (`all` runs every subcommand except `rollback`, `session` and `selftest`; `commands` builds its symbol set from `Logic/BarReset.cs` and `Services/BarResetService.cs` per D13; `selftest` builds empty, defect and good fixture trees under a temp dir and runs the ten named subcommands against them with `--root <dir>`; each prints `<name>: ok …` or `<name>: FAIL <reason>` and exits 1 on FAIL; each FAILs when it parses nothing); call `check_bar_reset.py all` from `Beelzebub/tools/preflight.ps1` as check `bar-reset`, and add a `-LogDir <dir>` parameter to its `-LogCheck` mode (default: the dev server's `BepInEx/LogOutput.log` and `logs/NyarDev.log`). Commit steps 1-4 (`feat(bar-reset): pure reset planner, dump parser, form-bar fill, checker`) · satisfies D5, D13, D14, D15, D17, D18, D20, D22, D23, D28, D29, D30
5. Create `Beelzebub/tools/fault_harness.py`, `Beelzebub/tools/faults/bar-reset/manifest.json`, the tracked fixture logs `Beelzebub/tools/faults/bar-reset/logs/LogOutput.log`, `NyarDev.log` (clean) and `session.log` (a passing D6–D10 session in the D16/D4 line formats), and one patch per `fails when` clause of D1 D2 D3 D4 D5 D11 D12 D13 D14 D15 D16 D17 D18 D19 D20 D22 D23 D24 D25 D26 D27 D28 D29 D30 — each a source or fixture change, never a runtime argument (D19 and D20 as listed in D21). Plant them one at a time by running the harness, and paste its output with `git rev-parse HEAD:Beelzebub/tools/faults/bar-reset` under `planted faults:` in `Beelzebub/Beelzebub/docs/audits/bar-reset.md` (faults for D13–D19, D22, D23 and D26 are planted once their targets exist, in steps 7-9) · satisfies D21
6. Create `Beelzebub/Beelzebub/Logic/EquipRows.cs` (`EquipRow`, `EquipRowDiff.InjectedIndices` / `MissingPrefabIndices`) with `Beelzebub/Beelzebub.Tests/EquipRowsTests.cs`. In `Beelzebub/Beelzebub/Services/SlotApply.cs` add `RemoveInjectedRows(Entity character)` (remove the injected rows 0-7 — rows the equip-buff prefab does not carry — and put back stripped prefab rows, no `RestoreSlotBaseValue`) and `ReapplyEquipRows(Entity character)` (for each bar slot 0-8: the highest-priority remaining row, else the slot's stored base → `ModifyAbilityGroupOnSlot(equipBuff, character, slot, ability)`, then mark dirty). In `Services/TransformBuffService.cs` split `PurgeAbilitySlotModifications` into `PopSlotModifications(character)` (passes 1-2, skipping unreadable slots) and keep `ForceResetAbilitySlots` as the separate EmptyPush; add `ReadBar(Entity character)` using `SlotModDump` and the gear-source rule. Create `Beelzebub/Beelzebub/Services/BarResetService.cs` implementing `IBarResetOps`, with `FullReset(Entity character, ulong steamId, string name, BarResetScope scope)` that computes `liveReady` (the character has `BuffBuffer` and `AbilityGroupSlotBuffer` and a held `EquipBuff_Weapon_*`), plans, runs, logs `BarResetLog.Format`, schedules one next-tick re-read that logs `FormatLate` per newly overridden slot, and returns the result; `Services/Heartbeat.cs` calls its per-frame `TickLate()`. In `Beelzebub/Beelzebub/Services/PersistenceService.cs` add `public bool TrySaveSync()` (the body of `SaveSync`, returning false in the catch) and make `SaveSync()` call it. Add `SlotOwnership` (`PushTargets`, `IsLeakedEmpty`, `PopAgain`) to `Logic/BarReset.cs` with `Beelzebub/Beelzebub.Tests/SlotOwnershipTests.cs`; `ForceResetAbilitySlots` takes its targets from `SlotApply.TryGetOwnedSlots`, `ReadSlotMods` counts a leaked Empty as other, and `PopSlotModifications` re-pops while the count falls and logs `[Beelz LEAK]` · satisfies D7, D8, D10, D24, D27, D31, D32
7. Rewire `ResetBar` in `Beelzebub/Beelzebub/Commands/BeelzCommands.cs` and `ResetLoadouts` / `Purge` in `Beelzebub/Beelzebub/Commands/AdminCommands.cs` to `BarResetService.FullReset` and `BarResetInput.IsConfirm`; add `[Command("bar", adminOnly: true)]` printing `ReadBar`; make `rebuildbar` call the same handler; prefix `clearslotmods` / `rebuildslots` descriptions with `LEGACY:`; replace ".beelz slot" in replies with ".beelz grant". Update `Beelzebub/Beelzebub/docs/COMMANDS.md` and the `.beelz commands` list. Commit (`feat(bar-reset): one layered reset for resetbar, purge, reset-loadouts; admin bar`) · satisfies D6, D13, D14, D15
8. Rewrite `Beelzebub/Beelzebub/docs/RECOVERY_GUIDE.md` to the single flow; create `Beelzebub/Beelzebub/docs/BACKLOG.md` with the Out-of-scope items and their slugs; bump `ApiVersion` to 33 in `Beelzebub/Beelzebub/Commands/ApiCommands.cs`; add the v0.137 entry and banner to `Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md` (additive; `api>=33`; BCH may show the key in its config view, nothing else to consume). Commit (`docs(bar-reset): recovery guide, backlog, BCH handoff`) · satisfies D17, D18, D26
9. Post-audit of steps 1-8: `pwsh Beelzebub/tools/preflight.ps1`, `python Beelzebub/tools/fault_harness.py bar-reset`, `/code-review`, a read-only Codex pass on `git diff 3fbc2d4..HEAD`; up to 3 rounds, one commit per round; write `## Post-audit` with `Codex verdict:`. Then prepare the release candidate (uncommitted working tree): sync `Beelzebub/Beelzebub/Beelzebub.csproj` `<Version>` and `Beelzebub/Beelzebub/thunderstore.toml` `versionNumber`; add `## [0.137.0]` to `Beelzebub/Beelzebub/CHANGELOG.md` and `Beelzebub/Beelzebub/docs/CHANGELOG_FULL.md` and drop the oldest entry from `CHANGELOG.md`; update the README status line and command cheat-sheet in `Beelzebub/Beelzebub/README.md`; `python Beelzebub/tools/sync_github_readme.py`; `pwsh Beelzebub/tools/preflight.ps1` → PREFLIGHT OK (its `check_bar_reset.py all` excludes `rollback` and `session`). Do not commit the release yet · satisfies D19, D23
10. Release-candidate session, then release. Deploy only after the owner stops the dev server (shared with Nyarlathotep): `dotnet build Beelzebub/Beelzebub.sln -c Release` copies the candidate DLL. Hand the owner the numbered in-game test (Use cases › Test fixture, then D6–D10 with their PASS lines, at 127.0.0.1:9876). After the session, before any restart: copy both logs to `%TEMP%\beelz-logs-<date>\`, run `pwsh Beelzebub/tools/preflight.ps1 -LogCheck` and `python Beelzebub/tools/check_bar_reset.py session %TEMP%/beelz-logs-<date>/LogOutput.log`, read every `[Error]`/`[Warning]` in both logs, record results with `status`. If any of D6–D10 or D30 fails: no release commit — record a `defect` amendment, fix and commit it as its own step, rebuild the candidate and repeat this step (Business rules 7). When all pass: commit `chore(release): v0.137.0`, then add the `rollback:` line with both SHAs to the audit record, commit `docs(audit): bar-reset rollback range`, and run `python Beelzebub/tools/check_bar_reset.py rollback` · satisfies D6, D7, D8, D9, D10, D20, D22, D30

## Work breakdown
- W1 · **Pure logic and tests**
- W1.1 · **Dump parser** · items: D1 · steps: 1
- W1.2 · **Reset planner, runner, log, input** · items: D2 D3 D11 D12 D16 D24 D25 D27 · steps: 2
- W1.3 · **Form bar fill** · items: D4 · steps: 3
- W1.4 · **Fault harness** · items: D21 · steps: 5
- W2 · **Game-side reset**
- W2.1 · **Reset service** · items: D7 D8 D10 D31 D32 · steps: 6
- W2.2 · **Commands** · items: D6 D13 D14 D15 · steps: 7
- W3 · **Checks, docs, release**
- W3.1 · **Checker and config** · items: D5 D23 D28 D29 · steps: 4
- W3.2 · **Guide, backlog, handoff** · items: D17 D18 D26 · steps: 8
- W3.3 · **Release gate** · items: D19 · steps: 9
- W3.4 · **Candidate session and release** · items: D9 D20 D22 D30 · steps: 10

## Rollout
- Ships (probe 14.1) all at once in v0.137.0. There is no runtime kill switch for the reset because nothing runs
  unless an admin or the player types a reset command; the owner (server admin) disables the feature by rolling
  back (below). `Forms_AutoFillFromCaptures=true` restores the old form-bar fill (D5).
- Compatibility (probe 14.2): command names and arguments unchanged; `rebuildbar` keeps its name as an alias;
  `state.json` unchanged; BCH sees one extra key in `api config` and ApiVersion 33 (D18).
- Rollback (probe 14.3): in the repository `git revert --no-edit <first>^..<release>` over the range recorded and
  checked in the audit (D22); on a server, install the v0.136.0 DLL — still possible after data was written,
  because a reset only removes entries and v0.136 reads the same `state.json` Version 8; the new `.cfg` key is
  ignored by v0.136. Cleared binds are not restored by a rollback (they were cleared on purpose).
- Paths walked (probe 14.4, D23) — the list `check_bar_reset.py paths` parses:
  - `Beelzebub/Beelzebub/Logic/SlotModDump.cs`
  - `Beelzebub/Beelzebub/Logic/BarReset.cs`
  - `Beelzebub/Beelzebub/Logic/BarResetLog.cs`
  - `Beelzebub/Beelzebub/Logic/BarResetInput.cs`
  - `Beelzebub/Beelzebub/Logic/FormBarFill.cs`
  - `Beelzebub/Beelzebub/Logic/EquipRows.cs`
  - `Beelzebub/Beelzebub/Logic/BarResetReply.cs`
  - `Beelzebub/Beelzebub/Logic/ReplyChunks.cs`
  - `Beelzebub/Beelzebub.Tests/SlotModDumpTests.cs`
  - `Beelzebub/Beelzebub.Tests/BarResetTests.cs`
  - `Beelzebub/Beelzebub.Tests/BarResetLogTests.cs`
  - `Beelzebub/Beelzebub.Tests/BarResetInputTests.cs`
  - `Beelzebub/Beelzebub.Tests/FormBarFillTests.cs`
  - `Beelzebub/Beelzebub.Tests/EquipRowsTests.cs`
  - `Beelzebub/Beelzebub.Tests/BarResetReplyTests.cs`
  - `Beelzebub/Beelzebub.Tests/ReplyChunksTests.cs`
  - `Beelzebub/Beelzebub.Tests/SlotOwnershipTests.cs`
  - `CLAUDE.md` (Development procedure 5-6: the release-candidate session's server procedure)
  - `Beelzebub/Beelzebub/Services/BarResetService.cs`
  - `Beelzebub/Beelzebub/Services/AbilityRegistry.cs`
  - `Beelzebub/Beelzebub/Services/SlotApply.cs`
  - `Beelzebub/Beelzebub/Services/PersistenceService.cs`
  - `Beelzebub/Beelzebub/Services/TransformBuffService.cs`
  - `Beelzebub/Beelzebub/Services/ShapeshiftAbilityService.cs`
  - `Beelzebub/Beelzebub/Services/Heartbeat.cs`
  - `Beelzebub/Beelzebub/Commands/BeelzCommands.cs`
  - `Beelzebub/Beelzebub/Commands/AdminCommands.cs`
  - `Beelzebub/Beelzebub/Commands/ApiCommands.cs`
  - `Beelzebub/Beelzebub/Config/Settings.cs`
  - `Beelzebub/Beelzebub/docs/RECOVERY_GUIDE.md`
  - `Beelzebub/Beelzebub/docs/COMMANDS.md`
  - `Beelzebub/Beelzebub/docs/BACKLOG.md`
  - `Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md`
  - `Beelzebub/Beelzebub/docs/CHANGELOG_FULL.md`
  - `Beelzebub/Beelzebub/docs/dod/bar-reset.md`
  - `Beelzebub/Beelzebub/docs/dod/bar-reset.reviews.md`
  - `Beelzebub/Beelzebub/docs/dod/bar-reset.review.html`
  - `Beelzebub/Beelzebub/docs/dod/README.md`
  - `Beelzebub/Beelzebub/docs/audits/bar-reset.md`
  - `Beelzebub/Beelzebub/CHANGELOG.md`
  - `Beelzebub/Beelzebub/README.md`
  - `Beelzebub/Beelzebub/Beelzebub.csproj`
  - `Beelzebub/Beelzebub/thunderstore.toml`
  - `Beelzebub/tools/check_bar_reset.py`
  - `Beelzebub/tools/fault_harness.py`
  - `Beelzebub/tools/faults/bar-reset/` (one `.patch` per planted fault; the checker matches the prefix)
  - `Beelzebub/tools/preflight.ps1`
  - `README.md`
  - Declared, not diffed: ignored outputs `bin/`, `obj/`, `dist/`; outside the repo the dev server's
    `BepInEx/plugins/Beelzebub.dll` and `BepInEx/config/kdpen.Beelzebub.cfg`, log copies in `%TEMP%`, review
    prompts `%TEMP%\dod-review-*.txt`.

## Out of scope
- Removing any command name (owner decision D5-A); folding `clearbar` into FullReset (it clears one chosen set and
  already re-resolves) — backlog slug `clearbar-fullreset`.
- Transform-chaining recurrence guard in `TransformService.TryActivate` — backlog slug `transform-chain-guard`.
- Merging the two docs folders, splitting the oversized docs, porting `dev-snapshot.ps1` — backlog slugs
  `docs-consolidation` and `dev-snapshot` (deferred in the process adoption, 2026-09-30).
- Presets and captures are deliberately untouched by resets (Business rules 2).
- Mounted saddle rows (`ReplaceAbilityOnSlotWhenMountedBuffElement`) are not reset — backlog slug `mounted-bar-reset`
  (Business rules 4).

## Also considered
- Patching each command in place (D1-B) — rejected: the twelve commands would keep drifting.
- Two releases, diagnostic first (D3-B) — rejected by the owner; the diagnostic ships in the same release and the
  test session runs it before the reset, keeping "diagnostic before fix".
- Keeping a pre-reset snapshot to undo a reset — rejected: the only thing a reset deletes is what the user asked to
  delete (binds); live layers are rebuilt from equipment. A failed Reapply is a release-blocking defect (Business
  rules 7), not a state to restore from.
- Operational ownership: the server owner runs resets and reads the logs; the runbook is `RECOVERY_GUIDE.md` (D17).
- Decommissioning: the old recovery paths stay as commands, marked `LEGACY:` (D14); the guide no longer
  recommends them (D17).
- Documentation and changelog: D17–D19. Support tooling: `admin bar` is the support view (D6).
- Compliance and retention law: not applicable — tested by listing what is stored (Design › Data): no new personal
  data; steamId and names were already logged. Localisation: not applicable — every reply is English chat, as
  today. Analytics: not applicable — no telemetry exists and none is added; `[Beelz RESET]` is diagnostics only.
  Running cost: none — no metered service.

## Assumptions
- S-1 · validated · the engine formatter prints GroupGuid mods as `[ModId n] Set PrefabGuid(x) from Entity(i:v)` under `- AbilityGroupSlot.GroupGuid` · source: Beelzebub/Beelzebub/Services/TransformBuffService.cs comment above `_modIdRx` (v0.122 live log)
- S-2 · reversible · Reapply (re-running the equip buff's vanilla rows through `ModifyAbilityGroupOnSlot`) puts weapon skills back without a swap · fallback: step 6 also logs `[Beelz RESET] reapply slot=<n> before=<guid> after=<guid>` per slot, so if D7 fails the cause is named first (Development procedure 4); the fix is then recorded as a `defect` amendment and D7 stays a release condition — no bind data is at risk, because a reset deletes only binds the user asked to delete
- S-3 · validated · chat commands run on the server main thread one at a time · source: VampireCommandFramework dispatches from the chat message system update (Learning Mods/VampireCommandFramework-main)
- S-4 · validated · owner accepted every recommendation (D1-A … D6) · source: user approved plan deep-growing-stallman.md 2026-09-30
- S-5 · validated · saved binds are Steam-keyed and re-inject on login and weapon equip, so `respawn` and `reset-character` do not clear them whatever entity they build · source: Beelzebub/Beelzebub/Services/AbilityRegistry.cs (steamId-keyed sets) and ReplaceAbilityOnSlotSystemPatch / TransformService.ReconcileOrphanBuffs re-inject paths

## Coverage
| # | Layer | Status | Probes | Pointer / reason |
|---|---|---|---|---|
| 1 | Purpose & typical use | Considered | 3/3 | Purpose & typical use |
| 2 | Actors & permissions | Considered | 3/3 | Design › Permissions › 2.1 D15 D13; 2.2 D15; 2.3 D16 D17 |
| 3 | Inputs, outputs & data | Considered | 4/4 | Design › Data › 3.1 D25 D15; 3.2 D16 D18; 3.3 D28 D27 D16 D5; 3.4 prose: state.json shape unchanged, nothing migrates |
| 4 | Business rules & invariants | Considered | 5/5 | Business rules › 4.1 D2 D3 D7 D31; 4.2 D2 D10; 4.3 D2; 4.4 D3 D4 D7 D30; 4.5 D6 D15 D23 D24 |
| 5 | Internal interfaces | Considered | 3/3 | Interfaces › 5.1 D1 D6; 5.2 D7 D8 D13; 5.3 D1 D2 D16 D30 |
| 6 | External dependencies & contracts | Considered | 3/3 | Interfaces › External › 6.1 D1 D18; 6.2 D12 D24 D27; 6.3 D12 |
| 7 | States & lifecycle | Considered | 3/3 | Design › States › 7.1 D11 D9 D24; 7.2 D10; 7.3 D7 D6 D10 D16 D30 |
| 8 | Minimal stretch | Considered | 2/2 | Use cases › Minimal stretch › 8.1 D11 D10 D25; 8.2 D5 D16 |
| 9 | Maximal stretch | Considered | 3/3 | Use cases › Maximal stretch › 9.1 D3 D16; 9.2 D16 D15; 9.3 D10 |
| 10 | Security & privacy | Considered | 4/4 | Security › 10.1 D13 D15; 10.2 D1 D16; 10.3 D16; 10.4 D16 |
| 11 | Design & UX | Considered | 4/4 | Design › UX › 11.1 D6 D17; 11.2 D7 D16 D24; 11.3 D6; 11.4 D13 D4 D16 |
| 12 | Failure handling & observability | Considered | 4/4 | Failure & observability › 12.1 D12 D24; 12.2 D6 D16; 12.3 D16 D20; 12.4 D21 D29 D30 D1 D19 D20 |
| 13 | Performance & scale | Considered | 2/2 | Performance › 13.1 D16; 13.2 D6 D16 |
| 14 | Rollout & compatibility | Considered | 4/4 | Rollout › 14.1 D5 D22; 14.2 D18; 14.3 D22 D27; 14.4 D23 |
| 15 | Out of scope | Considered | 2/2 | Out of scope |
Gate — acceptance & testability: passed — every Considered layer 2–14 maps to ≥ 1 D-item

## Baseline

## Amendments

## Log
- 2026-09-30 · status → draft · plan
- 2026-09-30 · note · round cap · after Review 3 · owner: approved Decision R1 option A — authorize one more Codex round (round 4) and stop there; if REVISE, bring the findings back to the owner
- 2026-09-30 · note · Review 4 REVISE — F1-F8 accepted and applied (+D30 session-log check; D2 D6 D7 D11 D13 D16 D19 D20 D21 D29 revised; release moved after the candidate session)
- 2026-09-30 · note · round cap · after Review 4 · owner: approved Decision R2 option A — apply the Review 4 fixes, run one final Codex round (round 5); if REVISE with only rigor findings, record them as advisory work and build as a draft under an owner waiver; if a new design gap, bring it back to the owner
- 2026-09-30 · note · Review 5 REVISE — F1-F6 and F8 accepted and applied as text (no new design gap: F1 engine auth boundary stated, F2 existing save path given its .tmp retention, F3 D6 manual cases), F7 rejected with reason
- 2026-09-30 · review skipped — owner waiver after round 5 (Decision R2 option A fallback): Review 5 raised only rigor findings, applied as text; the plan stays draft, unbaselined, and is built as a draft
- 2026-09-30 · note · build steps 1-4: `check_bar_reset.py audit` defined as — docs/audits/bar-reset.md holds Pre-audit, Post-audit, `Codex verdict:`, `planted faults:`, a `harness: ok` line and a `patch tree <sha>` equal to `git rev-parse HEAD:Beelzebub/tools/faults/bar-reset` (the plan named the subcommand without its content)
- 2026-09-30 · note · build steps 1-4: D13 exempt list gains `clearbar` — it calls `ClearAllSlots` for one chosen set and is out of scope (backlog clearbar-fullreset); the plan listed only the two LEGACY handlers
- 2026-09-30 · note · build steps 1-4: D28 `data` scans only the lines this feature added since 3fbc2d4 (whole file when untracked) for producers — pre-existing tags such as `[Beelz PURGE]` are not this feature's artifacts
- 2026-09-30 · note · build steps 1-4: BarResetRunner dropped its `steps.Count > 0` guard — a planted fault showed it redundant (an empty plan never saves, so `Saved` already fails Clean); D2's empty-plan test still covers the rule
- 2026-09-30 · note · post-audit round 1 of build steps 1-4 (/code-review + Codex diff pass, all findings in docs/audits/bar-reset.md): D1 D2 D4 D16 D24 D29 D30 tightened — exact GroupGuid field token and any-header section end, no action from an unreadable dump in the legacy purge, a null readback or a plan missing a live layer never clean, `clean=` on the RESET line, `[Beelz FORM] target=<steamId>` logged after the lock filter, D30 requires clean resets and validates BAR part sets; selftest now 35 cases
- 2026-09-30 · note · post-audit round 2 of build steps 1-4: D1 section header is `- <Component>.<Field>:` (a `- ` sub-line never ends it); D30 anchors D7 on the first PlayerReset, anchors the RESET schema at the line end, rejects a raw `:ERR` token even when a duplicate step follows; D29 selftest 40 cases
- 2026-09-30 · note · post-audit round 3 (final) of build steps 1-4: D30 fails on any `[Beelz RESET]`/`[Beelz BAR]` line that does not parse (a dropped malformed first reset let a retry satisfy D7); D29 selftest 41 cases
- 2026-09-30 · note · build step 6 (discovered, would be an amendment on an in-progress plan): the held equip buff's PREFAB carries the weapon's own ReplaceAbilityOnSlotBuff rows (Reference Data/Prefabs `EquipBuff_Weapon_Sword_Base`: slot 0 primary, 1 Whirlwind, 4 Shockwave), and the old resetbar's ClearGrant 0-7 loop stripped them — the likely cause of D7's "skills only return after a swap". "Beelzebub row" is now defined as a row the prefab does not carry (+D31, pure `EquipRowDiff`); stripped prefab rows are put back; Reapply sets each bar slot 0-8 to its weapon row or else its stored base (the plan said rows only — spell slots would stay Empty after the Empty push); the late re-read runs from the per-frame `HeartbeatBehaviour.Update` (no next-tick scheduler existed) — Heartbeat.cs added to Paths walked
- 2026-09-30 · note · post-audit round 1 of build step 6 (/code-review + Codex on e3ee3e8, dispositions in docs/audits/bar-reset.md): Reapply is rows-only again (pushing the stored base pinned spell slots); the readback covers bar slots 0-8 with character/slot-entity sources counted vanilla (Business rules 5 and D6 updated); PopSlotMods, ClearEquipEntries, EmptyPush and Reapply now ERR instead of silently succeeding; three limitations recorded in Business rules 4 and 6 (+backlog slug mounted-bar-reset)
- 2026-09-30 · note · post-audit round 2 of build step 6 (Codex on 8fb6f3b): PopSlotMods now ERRs on an unreadable slot anywhere (the readback only covers the bar), on a formatter exception (new `SlotModParse.Failed()`, never a readable empty dump) and on a failed source destroy; D24 unchanged (such a slot is still neither popped nor destroyed)
- 2026-09-30 · note · post-audit round 3 (final) of build step 6 (Codex READY on 08a2b2f; fresh-context subagent review, 3 advisory): with any unreadable slot the destroy backstop is skipped (its sources are unknown — D24); watched risk for the release-candidate session: a routinely unreadable dump on a non-bar slot would make PopSlotMods ERR on every reset — the `[Beelz PURGE] slot[n] … dump unreadable` warning names it and D30 fails on the ERR, which then becomes a `defect` fix
- 2026-09-30 · note · build step 7 (discovered, would be an amendment on an in-progress plan): the reply text is a pure module `Logic/BarResetReply.cs` with `BarResetReplyTests` (at most two lines per reset, 480-byte cap, no `<` `>`, primary/ultimate labels — Design › UX), `BarResetResult` carries Online/LiveReady, `AbilityRegistry.IsUniversalBucket` is internal (BindOrigins skips the universal bucket) — the three paths added to Paths walked; `PurgeAbilitySlotModifications` and `ForceAbilityBarReinit` have no callers left and stay until after the release-candidate session (rollback stays a plain revert)
- 2026-09-30 · note · post-audit round 1 of build step 7 (Codex + fresh-context subagent on 44498e2): every reset/bar reply that echoes a typed or looked-up name goes through `BarResetReply.Name` + `Cap`; the offline reply adds "if it is still stuck then, run this again while they are online" (leaked slot mods need a live character; D11's wording kept); an online-but-not-live reset KEEPS the active transform record (no RevertTransform is planned, so dropping it would strand the form buff — D2's "drops the record" holds for live and offline targets); `admin bar` reports every slot `unreadable` while the equip buff is missing; `admin bar` writes an Audit line; `.beelz admin purge CONFIRM` (no name) is refused instead of searching for a player named like the token
- 2026-09-30 · note · build step 5 (fault harness, D21): one manifest entry per planted fault, `clauses` are exact substrings of the item's fails-when text and the harness FAILs when what is left over is more than connectives; a `test:` item's command is `dotnet test Beelzebub/Beelzebub.Tests/Beelzebub.Tests.csproj -c Release --filter FullyQualifiedName~<TestClass>`; D21 does not plant into itself; D31 (added after D21 was written) is included; D2's stays-silent clause is `silent` (the baseline proves it); D22 is `deferred` behind the rollback line; D19's patches name the current version and are re-made (`--make`) after the release; `--skip`/`--only` name what they left out in the summary line. The harness found five checker holes (auth defaulted params, data open-w / dist, docs backlog rows, selftest crash) and an untracked manifest — all fixed before the recorded run (docs/audits/bar-reset.md › Step 5)
- 2026-09-30 · note · post-audit round 1 of build step 9 (Codex + fresh-context subagent on 5401c27, dispositions in docs/audits/bar-reset.md): Clean also requires no override buff left at the readback (the same-frame listing skips buffs already queued with DestroyTag) and the reply names a survivor (D24); a failed RevertTransform keeps the transform record (D12); a failed synchronous save also marks the store dirty for the heartbeat retry (D27); EmptyPush is an ERR when fewer than 9 slots were pushed; RevertTransform passes the pre-0.137 transform-ended reasons (resetbar / admin reset-loadouts / admin purge), so BCH sees no new token; Reapply logs S-2's per-slot diagnostic as `[Beelz REAPPLY]`; Business rules 4 records the foreign equip-row limitation; Interfaces drops the stale RestoreResolvedGrants
- 2026-09-30 · note · build step 9 done: post-audit closed after review round 2 (Codex CLEAN on a9eb3a1); every fault planted (D19 on a9eb3a1, the rest on 41139ef/3e333de); release candidate 0.137.0 prepared uncommitted (csproj, toml, both changelogs with [0.127.0] dropped from CHANGELOG.md, README status/cheat-sheet/limitations, root README synced) — preflight PREFLIGHT OK (10 checks, 133 tests). Next: build step 10, deploy on the owner's go-ahead
- 2026-09-30 · note · build step 10, release-candidate session (defect, outside this plan's feature): `.beelz api catalog abilities` (Test fixture step 2) threw `FixedString512Bytes: Truncation` in `ApiCommands.ReplyChunked` and aborted the stream — the 420-character chunk budget overflowed VCF's 512-byte cap on the v0.136 notes' multi-byte punctuation; cause proven by the exception, so fixed directly: `Logic/ReplyChunks.Pack` packs by UTF-8 bytes (≤500 per line, an oversize token cut at a character boundary), wire shape unchanged, `ReplyChunksTests` (4 controls, each planted once); its stack trace would fail D20 for that session, so the session restarts on the fixed build
- 2026-09-30 · note · build step 10 (discovered): the Test fixture's character (Steam ID 76561198039548286) is named `Chaos` in the log, not `PerpetualChaos`; `check_bar_reset.py session` matched the name only, so it gains `--target <name>` (default `PerpetualChaos`, the tracked fixture log unchanged) and step 10 runs it with `--target Chaos`; the in-game commands take the name the server resolves
- 2026-09-30 · note · build step 10, release-candidate session (defect, D7 — not yet proven): after a clean resetbar (`clean=1`, `other=0`) the owner could not drop a vanilla spell into T (ultimate, slot 7) and R/C took it only with trouble; Space (slot 2) is left empty instead of the fallback shadow dash. `admin bar` shows 2 equip-buff (gear) mods on every bar slot 0-8 although the Sword owns only 0, 1 and 4 — the working theory is the one Empty push (`ForceResetAbilitySlots`, all 9 slots, sourced by the equip buff) pinning slots the weapon does not own and masking their Base ability. Diagnostic first (Development procedure 4): read-only `.beelz admin bar-raw [player]` logs each bar slot's raw engine dump (current + Base GroupGuid, every mod with its set ability and source prefab) as `[Beelz BARRAW]`; the fix waits for its output. Also seen (outside this plan): a new `.beelz grant` shows on the client only after a weapon swap — backlog
- 2026-09-30 · note · build step 10 (discovered, would be an amendment on an in-progress plan · layer: 6.1 — the engine contract of `ModifyAbilityGroupOnSlot` was not sampled for slots the weapon does not own): the D7 defect is proven by `.beelz admin bar-raw` (19:14/19:15, logs in `%TEMP%/beelz-logs-2026-09-30-rc1-tstuck/` and the live LogOutput): every bar slot 0-8 carries an Empty GroupGuid mod sourced by the held weapon equip buff — the EmptyPush step (`ForceResetAbilitySlots`, all 9 slots) adds one on each run, and on slots with no weapon row (2 Space, 3, 5 R, 6 C, 7 T, 8) it masks the stored base (slot 2 `PrefabGuid(0) (Base: PrefabGuid(-433204738))`, the dash) and blocks the spellbook pick; the stack grows run to run (slot 2: 1,2,3,4,5,6 over six runs) and survives a server restart; the readback called it gear, so the run said `clean=1`. Owner Decision F1 option A: push Empty only to the weapon's prefab-row slots, count a gear Empty on any other slot as other (never clean, popped), pop again while the count falls, log `[Beelz LEAK]` (+D32, D7 D10 revised; Data row added). Pre-0.137 `rebuildslots`/`purge`/`resetbar` since v0.43.13 pushed the same Empties, so one fixed reset also heals bars damaged by earlier versions (14.2)
