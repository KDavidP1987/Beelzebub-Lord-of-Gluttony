# Beelzebub, Lord of Gluttony

> **Devour the bestiary.** Every kill is a chance to steal a unit's abilities — or unlock the form of the unit itself.

A server-side V Rising mod. Defeat any unit (regular mob or V-Blood boss) and roll a chance to capture its abilities. Assign captures to your spell slots; rare unlocks let you *become* the unit and wield its full ability set.

**Source / issues / roadmap:** [github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony)
**License:** [MIT](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/LICENSE)

> **Status:** early access (v0.4.0). Functional end-to-end; slot changes apply instantly. Drop into a private server and iterate — share weird kills back to the issue tracker.

## What it does

- **Every kill rolls a chance** to capture the unit's abilities (default 5%) and a smaller chance to unlock the ability to transform into that unit (default 1%).
- **Boss kills (V-Bloods) have their own roll** through the V-Blood event path. Captures are tagged by source so you can see at a glance what came from a boss.
- **Default filter** strips abilities that won't work in spell slots (melee animations, idle filler, lifecycle events, Brutal-difficulty duplicates) so you're not flooded with useless captures.
- **Assign captures to your spell slots** via chat command. Applies instantly while unarmed.
- **Transform into any unit you've unlocked.** All six of your spell slots fill with the unit's filtered ability list. Configurable: toggle until manual revert, or time-limited with cooldown.
- **Per-player chat verbosity** — silent if you prefer minimum spam, summary for one line per kill, verbose for per-ability detail.
- **Admin-configurable rules** in a hot-reloadable JSON file plus live `.beelz admin` chat commands.
- **BCH-ready API** — a structured chat surface for the [BloodCraftHub](https://thunderstore.io) client UI to consume.

## Requirements

- A V Rising **Dedicated Server** (Steam Tool AppID 1829350) — Beelzebub does not run on the client / "Host & Play" private game mode.
- [BepInExPack_V_Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) 1.733.2 or compatible.
- [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/) 0.10.4 or compatible.

## Installation

Install via [r2modman](https://thunderstore.io/package/ebkr/r2modman/) (recommended) or drop `Beelzebub.dll` into `<VRisingDedicatedServer>\BepInEx\plugins\`. Stop the server before replacing the DLL — it is file-locked while running.

## Command cheat-sheet

### Player commands

| Command | What it does |
|---|---|
| `.beelz list` | List captured abilities (grouped by source, then by unit) and your current slot assignments. |
| `.beelz transforms` | List unlocked transformations + your currently active transform if any. |
| `.beelz grant <slot> <index>` | Assign captured ability at `<index>` to spell slot `<1-6>`. **Applies instantly.** |
| `.beelz unslot <slot>` | Clear a slot assignment. Applies instantly. |
| `.beelz transform <index\|substring>` | Activate transformation. Applies instantly. |
| `.beelz revert` | End your current transformation. Swap a weapon to restore. |
| `.beelz forget <index>` | Delete one captured ability (also clears any slot pointing at it). |
| `.beelz forget-transform <index>` | Delete one transformation unlock. |
| `.beelz clear` | Wipe all your captured abilities, slot assignments, and transforms. |
| `.beelz verbosity <silent\|summary\|verbose>` | Set your in-chat notification level. |

### Admin commands (`adminOnly:true`)

| Command | What it does |
|---|---|
| `.beelz admin rules` | Show the loaded ability-filter rules. |
| `.beelz admin deny <pattern>` / `undeny <pattern>` | Add / remove a substring from the deny list. |
| `.beelz admin allow <pattern>` / `unallow <pattern>` | Add / remove a substring from the allow list. When non-empty, only matching abilities are captured. |
| `.beelz admin reload` | Re-read `ability_rules.json` from disk (for hand edits). |
| `.beelz admin transform mode <regular\|vblood> <toggle\|timed\|disabled>` | Set transform mode per source type. |
| `.beelz admin transform duration <regular\|vblood> <seconds>` | Auto-revert duration in `Timed` mode. |
| `.beelz admin transform cooldown <regular\|vblood> <seconds>` | Cooldown after revert. |
| `.beelz admin transform show` | Show current transform config. |

### BCH-readable API (`[BEELZ:...]` markers)

| Command | Reply marker(s) |
|---|---|
| `.beelz api version` | `[BEELZ:version]` |
| `.beelz api list` | `[BEELZ:list]` streamed + `[BEELZ:end]` |
| `.beelz api slots` | `[BEELZ:slot]` streamed + `[BEELZ:end]` |
| `.beelz api transforms` | `[BEELZ:tx]` streamed + `[BEELZ:end]` |
| `.beelz api active` | `[BEELZ:active]` (one line) |
| `.beelz api info <index>` | `[BEELZ:info]` (one line) |
| `.beelz api verbosity` / `rules` / `transform-config` | Single-line state dumps |

## Configuration

`BepInEx\config\kdpen.Beelzebub.cfg` controls server-wide defaults:

```ini
[Capture]
CaptureOnKill = true

[Capture.DropChance]
DropChance_Ability_Regular = 0.05
DropChance_Ability_VBlood = 0.05
DropChance_Transform_Regular = 0.01
DropChance_Transform_VBlood = 0.01

[Notifications]
DefaultVerbosity = Summary

[Transformation]
Transform_Mode_Regular = Toggle
Transform_Mode_VBlood = Toggle
Transform_DurationSeconds_Regular = 60
Transform_DurationSeconds_VBlood = 60
Transform_CooldownSeconds_Regular = 0
Transform_CooldownSeconds_VBlood = 0
```

`BepInEx\config\kdpen.Beelzebub\ability_rules.json` controls the ability filter (auto-created on first run with curated defaults).

`BepInEx\config\kdpen.Beelzebub\state.json` is the per-player state file (captures, slots, transforms, verbosity). Atomic writes, debounced ~1s.

## Known caveats

- **Grants apply only while UNARMED.** Once you equip a weapon, the weapon's natural abilities win for its slots — that's by design. Transforms override the spell bar regardless of weapon. Slot changes (grant/unslot/transform/revert) apply instantly via in-place buffer mutation — no more weapon-swap dance.
- **No visual shapeshift VFX.** Only the spell bar changes when you transform; your model stays the same. A unit→shapeshift-buff mapping is on the roadmap.
- **Not every ability works in every slot.** Spell slots 5 and 6 generally accept projectile / AoE abilities; basic melee animations won't appear. The default filter strips most non-castable cases, but a few survive — experiment, and report patterns we should add.

## Bug reports & feedback

Open an issue at [github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues). Server-side log lines tagged `[Beelz]` in `BepInEx\LogOutput.log` are the most useful diagnostic — paste the relevant lines plus what you were doing.

## License

[MIT](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/LICENSE).
