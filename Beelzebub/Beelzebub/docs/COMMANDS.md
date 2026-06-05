# Beelzebub command reference

The complete chat-command surface, grouped by prefix. Every command starts with `.beelz`.
In-game, the live help mirrors this: `.beelz commands` (players), `.beelz admin help` (admins),
`.beelz api help` (read/BCH), `.beelz hotkey help`. This file is the human reference; the
authoritative behavior is always the `[Command(...)]` descriptions in `Commands/*.cs`.

Prefixes:
- `.beelz <cmd>` — player commands (collection, slots, transforms).
- `.beelz admin <cmd>` — admin-only (curation, grants, recovery, config). Requires admin.
- `.beelz api <cmd>` — machine-readable `[BEELZ:*]` data streams for the BloodCraftHub UI (a few are admin).
- `.beelz hotkey <cmd>` — named extra-hotkey bindings beyond the 6 spell slots.

Selectors: most commands that take an ability accept either its **list index** (from `.beelz list`)
or its **stable ability id** (shown in `.beelz list`); the stable id is safer because indices shift
as you capture more.

---

## Player — collection

| Command | What it does |
|---|---|
| `.beelz` / `.beelz help` / `.beelz commands` | Overview · guided walkthrough · full command list. |
| `.beelz list` | Your captured abilities (index, name, source unit, stable id). |
| `.beelz search <text>` | Search your captures by name. |
| `.beelz info <index\|name>` | Full info on a captured ability (school, cooldown, source, activation condition). |
| `.beelz bestiary [page]` / `.beelz bestiary unit <name>` | Collection book / one unit's abilities you hold. |
| `.beelz progress` | Your collection-completion %. |
| `.beelz top` | Collection leaderboard. |
| `.beelz odds` | Your current capture drop-chance + pity state. |
| `.beelz forget <index\|id>` | Permanently drop a captured ability (also clears any hotkeys bound to it). |
| `.beelz silent <on\|off>` | Mute the per-capture chat notification. |
| `.beelz verbosity <quiet\|normal\|verbose>` | How much Beelzebub tells you. |

## Player — spell slots & loadouts

| Command | What it does |
|---|---|
| `.beelz grant <index\|id> <slot>` | Bind a captured ability to a normal spell slot. Slots **0 (primary/left-click)**, **1–6**, **7 (ultimate/T)**. |
| `.beelz unslot <slot>` *(alias: `clear-slot`)* | Clear one universal slot binding. |
| `.beelz weapon-grant <index\|id> <weapon> <slot>` | Bind an ability only while wielding a given weapon family. |
| `.beelz weapon-unslot <weapon> <slot>` | Clear one per-weapon binding. |
| `.beelz form-grant <index\|id> <form> <slot>` | Bind an ability into a shapeshift form's bar. |
| `.beelz form-unslot <form> <slot>` | Clear one per-form binding. |
| `.beelz clearbar [all\|universal\|<weapon>\|<form>]` | Clear a whole bucket of binds (no confirm). |
| `.beelz resetbar CONFIRM` | Hard-reset your bar to vanilla (captures kept). |
| `.beelz loadouts` | Show all your slot/weapon/form binds. |
| `.beelz active` / `.beelz current` | Your live spell bar with per-slot ability info (handy in transforms). |
| `.beelz cast <index\|id>` | Force-cast a captured ability on demand (respects its cooldown). |
| `.beelz preset save\|load\|list\|delete <name>` | Save/restore a universal-slot loadout preset. |

## Player — hotkeys (`.beelz hotkey …`)

| Command | What it does |
|---|---|
| `.beelz hotkey set <name> <index>` | Bind a captured ability to a named extra hotkey. |
| `.beelz hotkey list` | List your hotkey bindings. |
| `.beelz hotkey clear <name>` | Remove one hotkey binding. |
| `.beelz hotkey help` | Hotkey help. |

## Player — transforms (`.beelz …`)

| Command | What it does |
|---|---|
| `.beelz transforms` | Your transform unlocks (+ remaining time when active). |
| `.beelz transform <index\|unit>` | Transform into an unlocked boss/form. |
| `.beelz preview <unit>` | Preview a transform's kit before using it. |
| `.beelz catalog [page]` | Curated boss-kit reference (tier/difficulty). |
| `.beelz revert` | End your current transform. |
| `.beelz phase [n]` | Show / switch a multi-phase form's loadout. |
| `.beelz tform <unit> abilities\|set <phase> <slot> <index>\|clear\|defaults` | Customize which of a boss's kit fill its form slots. |
| `.beelz forget-transform <unit>` | Drop a transform unlock. |
| `.beelz summons <stash\|restore\|clear\|status>` / `.beelz tp` | Manage your ally summons (`tp` = stash before teleport). |
| `.beelz summon [n]` / `.beelz detonate` | A transformed boss's add-summon / detonate signature (must be transformed). |
| `.beelz clear CONFIRM` | Wipe ALL your binds, presets, and hotkeys (captures kept). |

---

## Admin — rules / curation (`.beelz admin …`)

| Command | What it does |
|---|---|
| `.beelz admin rules` / `reload` | Show / re-read the ability rules config. |
| `.beelz admin ability <name\|id> [<field> <value> …]` | Set per-ability rule(s) live (up to 5 pairs); omit fields to READ. See **fields** below. |
| `.beelz admin ability-set <id> "(field=value)(field2=value2)…"` | **Bulk-set many fields** in one command (parens or `;`-separated; handles spaces/commas). |
| `.beelz admin ability <id> defaults` / `all defaults` | Reset one/every ability's shaping to shipped baseline. |
| `.beelz admin tune <ability> <field> <value>` / `tune-list` | One-field shaping shortcut / list shaped abilities. |
| `.beelz admin deny\|undeny\|allow\|unallow <pattern>` | Capture name-pattern filters. |
| `.beelz admin denyguid\|allowguid <add\|remove> <guid>` | Capture GUID filters. |
| `.beelz admin transformonly <add\|remove> <pattern\|guid>` | Reserve abilities to transform-only. |
| `.beelz admin freeze-captures <on\|off\|status>` | Master capture-on-kill toggle. |

**`ability` / `ability-set` fields:** `enabled, weapons, forms, transformonly, difficulty, phase,
allowdenied, damagescale, cooldownscale, cooldown, range, charges, chargetime, aoe, projspeed,
duration, healing, summoncap, summontimeout, summonunits, forcetimeout, category, reviewstatus,
reviewtag, condition, interruptible, interruptonhit, freemove, freelymove, castspeed, notes`.
`weapons`/`forms` take a comma list (whitelist) or `!X` (blacklist) or `any` to clear. `reviewstatus`
∈ {Unreviewed, Reviewed, Approved, **Blocked**, **Hidden**} — Blocked/Hidden gate capture (see
`Curation_EnforceReviewStatus`). `condition` ∈ {Aimed, CloseRange, Summon, SelfCast, Movement} (sets
conditionSource=confirmed).

## Admin — transforms

| Command | What it does |
|---|---|
| `.beelz admin transform-set <CHAR_unit> <field> <value>` | Per-unit transform rule (enabled, difficulty, tier, damagescale, cooldownscale, healthscale, speedscale, fullreplace, powerscalingmode, notes). |
| `.beelz admin transform mode\|duration\|cooldown <regular\|vblood> <…>` | Global transform tuning. |
| `.beelz admin transform show` | Current transform settings. |
| `.beelz admin difficulty [basic\|brutal]` | Server difficulty gating. |
| `.beelz admin testform <wolf\|bear\|off>` | TEST: enter a native form with your loadout. |

## Admin — player grants

| Command | What it does |
|---|---|
| `.beelz admin give\|revoke <player> <unitGuid> <abilityGuid>` | Grant / remove one captured ability. |
| `.beelz admin devour <player> <unitGuid>` | Grant a unit's ENTIRE kit at once. |
| `.beelz admin give-transform\|revoke-transform\|force-transform\|clear-transform <player> [unitGuid]` | Manage a player's renderable forms. |
| `.beelz admin set-slot\|clear-slot <player> <slot> [abilityGuid]` | Set a player's universal slot bind. Slot **0–7** (0=primary, 7=ultimate). `clear-slot` alias: `unslot`. |
| `.beelz admin set-weapon-slot\|clear-weapon-slot <player> <weapon> <slot> [abilityGuid]` | Set a player's per-weapon bind (slot 0–7). `clear-weapon-slot` alias: `weapon-unslot`. |
| `.beelz admin reset-loadouts <player> CONFIRM` | Clear ALL of a player's loadouts (captures kept). Requires `CONFIRM`. |

## Admin — inspect / broadcasts

| Command | What it does |
|---|---|
| `.beelz admin inspect <player>` / `progress <player>` | View a player's state. |
| `.beelz admin snapshot` | Server-wide summary. |
| `.beelz admin scan-abilities` | Dump ability metadata to disk. |
| `.beelz admin dump <abilityGuid\|form>` | Log an ability's ECS component chain (diagnostic). |
| `.beelz admin broadcast <status\|leaderboard…\|complete…\|test>` | Server announcement controls. |
| `.beelz admin broadcast-msg <complete\|leaderboard> <list\|add\|remove\|edit>` | Manage announcement messages (quote multi-word text). |

## Admin — recovery (fix a stuck player, no wipe)

| Command | What it does |
|---|---|
| `.beelz admin respawn <player>` | Rebuild a stuck bar by respawning in place. |
| `.beelz admin purge <player> CONFIRM` | **Last resort.** Wipe ALL bar integration to vanilla — incl. the engine-level modification **leak** that survives relog/respawn/resetbar. Ends + un-parks any transform, clears every slot/form/weapon/hotkey binding. Captures + transform unlocks are KEPT; the player re-slots afterward. Player must be online. |
| `.beelz admin rebuildslots\|clearslotmods\|rebuildbar <player>` | Slot/bar repair levers. |
| `.beelz admin unmount <player>` | Force-dismount + clear stuck mount buffs. |
| `.beelz admin buffs <player>` | Diagnostic: dump buffs + slot overrides to the log. |
| `.beelz admin desummon <player>` / `desummon-all` / `revert-all` | Clean up summons / end transforms. |
| `.beelz admin copy-collection <player>` / `paste-collection <player>` | Backup + restore a collection (single clipboard). |
| `.beelz admin reset-character <player> CONFIRM-RESET` | Fresh character (collection preserved). |

## Admin — danger

| Command | What it does |
|---|---|
| `.beelz admin set <key> <value>` | Set any config key live (keys via `.beelz api config`). |
| `.beelz admin wipe-all CONFIRM-WIPE` | Wipe ALL player data on the server (irreversible). |

---

## API / read (`.beelz api …`) — BloodCraftHub data streams

These emit `[BEELZ:*]` lines a client parses; most don't change anything. Wire contract = `ApiVersion`
(see `Commands/ApiCommands.cs`).

| Command | What it streams |
|---|---|
| `.beelz api version` / `bch <on\|off\|status>` | API+plugin version / toggle your event stream. |
| `.beelz api list` / `slots` / `transforms` / `hotkeys` / `active` | Your own captured abilities / binds / unlocks / hotkeys / live bar. |
| `.beelz api info <index>` / `info-guid <guid>` | Ability tooltip data by list index or PrefabGUID. |
| `.beelz api progress` / `bestiary [page]` | Collection-completion data / collection book. |
| `.beelz api catalog [units\|abilities] [page] [filter value]` | Curated-catalog streams. **Filters:** `weapon, cat, unit, form, search, tag, reviewstatus, tier, vblood` — load just a subset fast. |
| `.beelz api catalog abilities-all [page] [filter value]` *(admin)* | EVERY ability group for configuration (same filters). |
| `.beelz api rules` / `config` / `cooldowns` / `transform-config` | Server config + state streams. |
| `.beelz api tform-kit <unit>` / `tform-binds <unit>` | A transform's kit / your custom binds for it. |
| `.beelz api broadcast-msgs <pool>` *(admin)* | The announcement message pool. |
| `.beelz api verbosity` | Your verbosity setting. |
