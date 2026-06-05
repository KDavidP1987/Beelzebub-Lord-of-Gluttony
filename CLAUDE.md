# CLAUDE.md

Guidance for Claude Code when working in this workspace.

## What this workspace is

A working directory for **Beelzebub** (working title: *Beelzebub, Lord of
Gluttony*), a **server-side** BepInEx IL2CPP plugin for V Rising's dedicated
server. The mod's premise: when a player defeats a unit (V-Blood or otherwise),
the player can be granted that unit's signature ability — assignable to one of
the player's six spell slots or to admin-defined extra hotkeys.

The only buildable project lives at:

```
Beelzebub/                              ← target codebase (Beelzebub.sln)
```

Everything outside that subtree is **reference-only source drops or game
asset data** — read for context, do not edit without an explicit user request.

When the user says "the mod", "this codebase", or "our mod" without
qualification, they mean `Beelzebub/`.

## Sibling project — DO NOT edit from this workspace

**BloodCraftHub (BCH)** is a separate client-side mod by the same author,
living at:

```
C:\Users\KDPen\OneDrive\Documents\CURSOR PROJECTS\Games\V Rising\BloodCraftUI 2\
```

That project has its own `CLAUDE.md`, its own auto-memory namespace, and its
own GitHub repo. Beelzebub starts standalone — eventually BCH will integrate
with Beelzebub (on-screen ability buttons / cooldown display) but that
integration goes through chat-command shapes the way BCH already talks to
Bloodcraft. **Never edit files under `BloodCraftUI 2\` from a session rooted
in this workspace** unless the user explicitly cross-references the work.

## BCH integration handoff — keep it current

`Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md` is the **living contract**
BCH builds against (the chat-command + config + `[BEELZ:*]` API surface, plus
the experimental model-swap / animation-fidelity work BCH must own). It is
authored here and carried to the BCH workspace.

**Rule:** whenever ongoing work changes anything BCH-facing — a chat command
(player/admin/`api`), a `[BEELZ:*]` line or its fields, a config key, or a
transform capability — update the handoff doc **in the same change** so BCH
stays buildable. If a change is purely internal (no contract impact), no doc
update is needed.

A `PostToolUse` hook (`.claude/hooks/bch-relevance-reminder.ps1`, wired in
`settings.local.json`) fires on edits to `Commands/*.cs`, `Config/Settings.cs`,
and the BCH-facing wire-schema/roster Services
(`Services/{AbilityRules,ShapeshiftAbilityService,Categorization,WeaponFamily}.cs`
— the `AbilityEntry` fields emitted in `catalog-ability`, the `forms`/`weapons`
allow-/`!`block syntax, the `ShapeshiftForm` roster, the `cat=` categories), and
surfaces a reminder. The hook is a backstop covering the most common surface
files — BCH relevance can be broader, so use judgment: this CLAUDE.md rule is
authoritative, the hook is just the net. Canonical wire-API source is
`Commands/ApiCommands.cs` (`ApiVersion`); bump it there and reflect it in the doc.

**When you DO bump `ApiVersion` or touch a BCH-facing surface, the handoff update
must include, at minimum:** the new/changed command or `[BEELZ:*]` line with its
exact token shape, whether it's additive or wire-breaking, the `api>=N`
capability gate BCH should check, and a one-line "what BCH should do to consume
it." Keep the handoff's top banner (`ApiVersion = N`) in sync with the file.

## Reference-only paths (do NOT edit)

A `PreToolUse` hook in `.claude/settings.local.json` warns when an edit
targets these paths or escapes into the sibling BCH workspace; treat the
warning as a stop sign unless the user explicitly asked for the edit.

- `Learning Mods/Bloodcraft-main/` — **server-side** Bloodcraft mod by zfolmt
  (v1.13.21). Source for the death-event Harmony hook pattern (look at
  `Patches/DeathEventListenerSystemPatch.cs` or similar), the
  ability-granting / spell-slot pattern, and the IL2CPP ECS conventions for
  V Rising server-side code. Plugin GUID `io.zfolmt.Bloodcraft`.
- `Learning Mods/KindredCommands-main/` — server-side admin command mod by
  odjit (v2.5.8). Source for the simpler VCF command + `Plugin.cs` pattern
  Beelzebub's scaffold is modeled on. Plugin GUID
  `aa.odjit.KindredCommands`.
- `Learning Mods/VampireCommandFramework-main/` — VCF framework (used by
  Bloodcraft + KindredCommands). Beelzebub will declare this as a
  `[BepInDependency]` and consume the NuGet package
  `VRising.VampireCommandFramework`.
- `Prefabs/` — full V Rising prefab dump (~23,500 `.txt` files), one per
  prefab GUID. Filenames follow `<PrefabName> PrefabGuid(<int>).txt`. Used
  to look up ability prefab GUIDs by name and confirm component layout
  before referencing them in code. **DO NOT modify these files** — they are
  the reference asset record. To find an ability: glob
  `Prefabs/AB_<keyword>*.txt`.
- `Reference Data/` — currently empty; reserved for additional reference
  exports (component-type lists, EntityQuery dumps, etc.) as the user adds
  them.

## Beelzebub project layout

```
Beelzebub/
├── Beelzebub/                  ← C# project root
│   ├── Beelzebub.csproj        ← single version source (auto-generates MyPluginInfo)
│   ├── Plugin.cs               ← entry point (BasePlugin.Load)
│   ├── Patches/                ← Harmony patches (death-event hook, ability cast patches, ...)
│   ├── Services/               ← AbilityGrantService, PlayerAbilityStateService, ...
│   ├── Commands/               ← VCF commands (.beelz grant, .beelz slot, .beelz list, ...)
│   ├── Config/Settings.cs      ← BepInEx config bindings
│   └── thunderstore.toml       ← Thunderstore manifest (versionNumber synced to csproj)
├── Beelzebub.sln
├── tools/                      ← (TBD) bump-version.ps1, preflight.ps1, package-release.ps1
└── docs/                       ← (TBD) ARCHITECTURE, LESSONS_LEARNED as they grow
```

- Plugin GUID: `kdpen.Beelzebub`
- Target framework: `net6.0`, IL2CPP via `BepInEx.Unity.IL2CPP` 6.0.0-be.733,
  V Rising types via `VampireReferenceAssemblies`, commands via
  `VRising.VampireCommandFramework`.
- Repo: **not yet initialized**. Will be its own git repo separate from BCH.

## Build & local deploy

Standard server-side build (no deploy):

```powershell
cd "Beelzebub"
dotnet restore Beelzebub.sln
dotnet build Beelzebub.sln -c Release
```

**Deploy target — confirmed 2026-05-21.** Beelzebub deploys to the local
V Rising Dedicated Server (Steam Tool AppID 1829350) at:
`C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer\BepInEx\plugins`

The csproj defines `VRisingServerPath` and `VRisingPluginsPath` properties;
`BuildToServer` runs `AfterTargets="Build"` with `Condition="Exists(...)"`
so it gracefully no-ops if the server folder is missing. Override on the
command line with `-p:VRisingServerPath="<alternative-path>"` if needed.

The dedicated server file-locks the DLL while running — **stop the server
process** before redeploying.

## Versioning — single source

`BepInEx.PluginInfoProps` auto-generates `MyPluginInfo.PLUGIN_VERSION` from
`<Version>` in `Beelzebub.csproj`. There is no version constant to maintain
in `Plugin.cs`. Once tooling is added, the bump-version script will sync
csproj + `thunderstore.toml` (+ CHANGELOG if added).

## Release & changelog discipline — keep the release surfaces in sync

Three artifacts describe a release and **must move together** in the same
`chore(release)` commit, or they drift (the README sat at v0.4.0 while the code
reached v0.40 — don't repeat that):

1. **Version** — `Beelzebub.csproj <Version>` **and** `thunderstore.toml
   versionNumber`. Keep them identical.
2. **`CHANGELOG.md`** — player-facing release notes. This **one file is both**
   changelogs: it ships to Thunderstore (auto-staged to `dist/` by the
   `BuildToDist` target) **and** lives on GitHub. The Conventional-Commits git
   log is the deeper technical history — there is intentionally no second
   changelog file to keep in sync.
3. **`README.md`** — the Thunderstore mod page (front page) and GitHub landing
   page. (`thunderstore.toml`'s `description` is the short listing tagline, ≤250
   chars.)

**On every version bump, in that one commit:**
- Sync both version fields.
- Add a `## [x.y.z] - <date>` `CHANGELOG.md` entry in player-facing language.
  **Multi-phase batches:** if several feature-versions ship together, give
  **each** version its own entry — never collapse or skip one (that's how gaps
  appear).
- Scan `README.md` for staleness vs. what shipped: the status/version line, the
  feature list, the command cheat-sheet, and the honest-caveats section. Update
  whatever the release changed.

A `PostToolUse` hook (`.claude/hooks/release-sync-reminder.ps1`, wired in
`settings.local.json`) fires on edits to `Beelzebub.csproj`/`thunderstore.toml`
and surfaces this checklist. The hook is a backstop (and `.claude/` is gitignored,
so it's local-only); **this CLAUDE.md rule is the authoritative, shared process.**

## Things to watch out for

These will grow as the project hits real gotchas. Empty for now — first
entry will come from the POC attempt.

- **IL2CPP, not Mono**: use `Il2CppInterop` patterns. Static field
  initializers that touch `ComponentType.ReadOnly(Il2CppType.Of<T>())` will
  NRE at `Plugin.Load` because `TypeManager` isn't built yet. Defer those
  reads to first use, or to an `OnGameInitialized`-style hook (see
  `KindredCommands/Plugin.cs:HasLoaded()` for the pattern of waiting for
  `PrefabCollectionSystem.SpawnableNameToPrefabGuidDictionary` to populate).
- **Server context check**: `Plugin.Load` should early-return if
  `Application.productName != "VRisingServer"` — same as Bloodcraft and
  KindredCommands. Beelzebub is server-only; do not assume client harness
  exists.
- **Death-event hook selection**: V Rising has multiple "death" surfaces.
  Bloodcraft hooks the unit's death via a specific Harmony patch — read its
  patch first before picking which system to patch. Hooking the wrong
  system can fire too early (before loot/XP/etc.), too late (entity already
  destroyed), or duplicate-fire for multi-hit kills.

## Cross-ability impact discipline — solve the class, not the instance

Beelzebub's ability changes are **rarely one-offs**, so treat every ability
behaviour change as a change to a **class** until proven otherwise. The structural
reasons (grounded in the code):

- **Global prefab edits are shared.** `AbilityTuningService.ApplyAll` mutates an
  ability's group/cast prefab — which **is the source NPC/boss's prefab** — and its
  downstream walker writes onto **spawn prefabs that other ability chains can also
  reference.** So a per-ability tweak can change a boss fight or a sibling spell.
- **Heuristics are class-wide.** A `Categorization`/`WeaponFamily`/`AbilityFilter`
  change re-buckets hundreds of abilities at once (one change moved 716 out of `Other`).
- **Injection sources collide.** Slot, weapon, form, and mounted bars all write
  `ReplaceAbilityOnSlotBuff` on the same `AbilityGroupSlot`; two writers on one slot is
  the Burst `AppendRemovedComponentRecordError` crash (the Mountup bug).

**Rule:** whenever you change how an ability behaves, work the checklist in
`Beelzebub/Beelzebub/docs/ABILITY_CHANGE_IMPACT.md` — find the mechanism *class*, fix
the class, prefer a scalable class-level fix (or an explicit per-ability override) over a
widened heuristic, and **prove you didn't touch neighbours you didn't mean to** (shared
spawn prefab? boss fight? sibling bucket? slot collision?). Every global prefab edit must
`CaptureOriginal` so `defaults`/restart can undo it. A single mechanism fix often closes
several `TESTER_FEEDBACK_TRIAGE.md` reports at once — note which.

A `PostToolUse` hook (`.claude/hooks/ability-impact-reminder.ps1`, wired in
`settings.local.json`) fires on edits to the ability-mutation surfaces (the
`AbilityTuningService`/`AbilityRules`/`Categorization`/`SlotApply`/`ShapeshiftAbilityService`/
`MountRecovery`/… Services + the `ReplaceAbilityOnSlotSystemPatch`/`AbilityCastStartedSystemPatch`
Patches) and surfaces the checklist. The hook is a backstop (and `.claude/` is gitignored,
so it's local-only); **this CLAUDE.md rule + the impact doc are the authoritative, shared process.**

## Ability-data sync discipline — one source per file; the DLL is built from it

The mod's shipped ability data lives in TWO hand-editable files, and they ARE the single source of
truth — there is **no separate "integrated" copy to maintain alongside them.** The DLL **embeds them
at build**, so editing the Resources file + rebuilding makes "what ships in the DLL" identical to
"what you edit." A change is made in **one** place, not duplicated into two.

- `Beelzebub/Beelzebub/Resources/ability_metadata.json` — descriptive (name / description / type /
  categories / `condition` / `incompatible`), keyed by GUID.
- `Beelzebub/Beelzebub/Resources/ability_rules.default.json` — policy/config (`Enabled` / `Weapons` /
  `Forms` with `!`blacklist / shaping), keyed by prefab name; seeded onto a fresh server.

**Rules when curating the DEFAULT / shipped version:**
1. **Edit the Resources source file — never only an in-game command.** A `.beelz admin` command changes
   a LIVE server's per-server `ability_rules.json`; that does NOT flow back to the shipped default. A
   curation decision meant for the package MUST be written into `ability_rules.default.json`.
2. **When changing ability data on the user's behalf, make the change in the Resources source file**
   (which is simultaneously the editable file AND the embedded/shipped data — one edit covers both),
   then rebuild so the embedded copy isn't stale. Do NOT leave a curation change applied only at runtime.
3. **Conflict → ASK.** If the change would overwrite an existing shipped value (the default already sets
   `Enabled`/`Weapons`/`Forms`/a `condition` differently than the new request), surface the current
   value and ask before overwriting.
4. **Metadata is partly GENERATED** (the `tools/` scrape → process → merge → conditions pipeline).
   Freehand edits to descriptive fields can be overwritten by a pipeline re-run; durable per-server
   curation goes via `ability_metadata_overrides.json`, and a hand-set `condition` should carry
   `conditionSource: "confirmed"` so `merge_conditions_into_metadata.py` preserves it on re-merge.

A `PostToolUse` hook (`.claude/hooks/ability-data-sync-reminder.ps1`, wired in `settings.local.json`)
fires on edits to those Resources files (and the condition pipeline) and surfaces this checklist +
the rebuild reminder. The hook is a backstop (`.claude/` is gitignored, local-only); this CLAUDE.md
rule + `Beelzebub/Beelzebub/docs/ABILITY_DATA_EDITING.md` are the authoritative, shared process.

## Git workflow

Once the repo is initialized:

- Conventional Commits, same style as BCH:
  `feat|fix|chore|docs|refactor|test|build|ci|perf|style|revert(scope)?: subject`
- Release commits: `chore(release): vX.Y.Z`.
- `gh` CLI is already authenticated as `KDavidP1987` for the user; new
  GitHub repo creation will be needed before first push.

## Session-start sanity checks

When starting a new session in this workspace, before making changes:

1. `cd Beelzebub && git status` (once repo exists) — confirm clean state.
2. If you're about to edit a file under `Learning Mods/`, `Prefabs/`, or
   `Reference Data/`, STOP — those paths are reference-only.
3. If the user is asking for a feature that involves BCH integration,
   confirm whether the work belongs in this workspace (server-side mod
   surface) or BCH's workspace (client UI surface). The two never
   cross-edit each other; integrations go through chat commands.
