<!-- GENERATED from Beelzebub/Beelzebub/README.md by Beelzebub/tools/sync_github_readme.py — edit the Thunderstore README, then re-run the script. -->
<p align="center">
  <img src="Beelzebub/Beelzebub/splash.png" alt="Beelzebub, Lord of Gluttony" width="640">
</p>

<p align="center">
  <a href="https://thunderstore.io/c/v-rising/p/kdpen/Beelzebub/"><img alt="Thunderstore" src="https://img.shields.io/badge/Thunderstore-kdpen%2FBeelzebub-2a6fdb"></a>
  <a href="https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/releases"><img alt="Release" src="https://img.shields.io/github/v/release/KDavidP1987/Beelzebub-Lord-of-Gluttony?include_prereleases&label=release"></a>
  <img alt="Status" src="https://img.shields.io/badge/status-early%20access-orange">
  <img alt="Side" src="https://img.shields.io/badge/V%20Rising-server--side-8b1e3f">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-green"></a>
</p>

# Beelzebub, Lord of Gluttony

**Devour the bestiary.** A server-side V Rising mod where every kill can teach you one of the enemy's abilities, and
a rare jackpot teaches you its entire kit at once. Slot what you collect on your action bar, bind the rest to extra
hotkeys, and take the forms of the bosses you defeat.

> **Status:** early access, **public test build (v0.137.5)**. Works end to end on dedicated servers. Expect rough
> edges, and expect test servers to be wiped. Feedback goes to the
> [issue tracker](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues).

**Source and roadmap:** [github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony) · **License:** MIT

---

## Get it

- **Thunderstore (recommended):** [https://thunderstore.io/c/v-rising/p/kdpen/Beelzebub/](https://thunderstore.io/c/v-rising/p/kdpen/Beelzebub/) — install with r2modman / Thunderstore Mod Manager.
- **GitHub Releases:** [https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/releases](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/releases) — the same zip; drop `BepInEx/plugins/Beelzebub.dll`
  into your dedicated server's `BepInEx\plugins\` folder.
- **What changed:** [player changelog](Beelzebub/Beelzebub/CHANGELOG.md) · full technical history in the git log

---

## Before you install

- **This is a test build.** V Rising has hundreds of NPC and boss abilities, and some of them misbehave when a player
  casts them. Finding out which ones are worth keeping is a goal of this test. A broken ability is useful feedback.
- **Progress is not safe yet.** Characters, collections or a whole test server may be wiped between builds.
- **Other mods are untested.** Beelzebub is designed to run alongside Bloodcraft and KindredCommands, but that has not
  been fully verified. Anything else is unknown; please report conflicts.
- **BloodCraftHub is recommended.** The client-side BloodCraftHub companion adds on-screen ability buttons, cooldowns
  and a collection book. Without it, everything works through chat commands and the six vanilla slots. The
  integration is still being built.

## How it works

1. **Kill anything.** Each kill has a chance (default 5%) to capture one of the unit's abilities.
2. **Hit the jackpot.** A rare **Devour** roll (default about 0.25%) teaches the unit's whole eligible kit in one kill.
3. **Never stay unlucky for long.** Bad-luck protection raises your odds on every dry kill and resets on a drop.
4. **Use what you collect.** Put captures on your spell slots, per weapon or for every weapon, or bind extras to
   named hotkeys.
5. **Become the boss.** Some bosses can also unlock their transformation, a separate prize with its own odds.
6. **Complete the bestiary.** Track what you have per unit and hunt what is missing.

## Features

### Collection
- Separate capture and Devour odds for regular enemies, V Bloods and shard bosses, each with bad-luck protection.
- A default filter removes filler (idle, lifecycle and plain melee abilities), so captures stay useful.
- `.beelz bestiary` shows how many of each unit's abilities you hold and what is left.
- `.beelz top` ranks the server by collection, `.beelz odds` shows your current chances, and finishing the catalog
  is announced to the server.

### Your action bar
- Put any capture on one of your six spell slots, for every weapon or for one weapon type. Changes apply at once.
- Mix captures with your own spells. Picking a vanilla spell for a slot in the spellbook hands that slot back to you.
- Go past six: bind captures to named hotkeys and fire them with `.beelz cast <name>`. Cooldowns still apply.
- Give each shapeshift form (Wolf, Bear, Rat, Spider, Toad, Werewolf, Gargoyle) its own loadout with
  `.beelz form-grant`. The form holds while you cast.
- Saddle abilities go on R, C and T while you ride.
- One command resets a stuck bar: `.beelz resetbar CONFIRM` clears every Beelzebub change and checks the result.
  Your captures are kept.

### Transformations
- **Dracula** and **Morgana** turn you into their real in-game forms, with their models, rigs and abilities.
- **Test forms:** Werewolf (Werewolf Chieftain or the common werewolf), Golem (Terah the Geomancer) and Gargoyle
  (the Tailor).
- Every form has two or more ability sets you switch with `.beelz phase`, or automatically by health.
- Bosses have more abilities than you have slots, so you choose: `.beelz tform <unit> set <phase> <slot> <index>`
  builds your own kit for each phase.
- Capturing abilities, devouring a kit and unlocking a form are three separate rolls. A form is never a free extra.
- Why only these forms: the game client decides what your character looks like, and a server-side mod cannot change
  that. These are the forms the game can already show on a player. Other units' powers come to you as abilities.

### Summons
- Captured summon abilities raise allies that fight for you, transformed or not. They scale with your level and have
  caps, leashing and clean despawns.
- `.beelz summon` calls a transformed boss's signature adds (the Toad King's frogs, the Werewolf Chieftain's wolves).
- `.beelz summons stash|restore|clear|status` manages them; `Transform_MountedSummonMode` decides whether they wait
  or follow when you mount up.

### Server tools
- Change drop rates, pity, transform rules and more live with `.beelz admin set <key> <value>`. No restart needed.
- Reshape any ability: cooldown, range, charges, area size, projectile speed, duration, healing, damage, summon
  limits, and how the cast feels (move while casting, interrupt on hit). `.beelz admin ability-inspect` shows every
  number behind an ability, and `defaults` restores the original.
- Ships with the community testing round's results: about 210 abilities pre-tuned, about 300 non-working ones
  switched off (an admin can turn any back on) and abilities that crash or break characters blocked.
- Incompatibility locks limit how many abilities of a group one player can hold, for combinations that break
  balance. None ship; your server decides.
- Optional per-hit damage scaling (`Damage_Mode`), with a telemetry mode to try it before it changes anything.
- Recovery without wipes: inspect a player's bar, clear a stuck state, or reset loadouts, online or offline.

### BloodCraftHub companion
Beelzebub exposes a chat API (`[BEELZ:*]` lines) that the client-side **BloodCraftHub** mod reads to draw ability
buttons, live cooldowns, the collection book and admin panels. Each player installs it on their own client. The
integration is being built; its Thunderstore link will be added here.

## Requirements

- A V Rising **dedicated server**. Beelzebub runs on the server only; it does not work in a Host & Play game.
- [BepInExPack V Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/)
- [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/)
- Recommended for players: BloodCraftHub (client-side).

## Installation

Install with [r2modman](https://thunderstore.io/package/ebkr/r2modman/) or the Thunderstore Mod Manager, or copy
`Beelzebub.dll` into `<VRisingDedicatedServer>\BepInEx\plugins\`. Stop the server before replacing the DLL; it is
locked while the server runs.

## Commands

The full list, with every option, is in
[COMMANDS.md](Beelzebub/Beelzebub/docs/COMMANDS.md).

**Collection**

| Command | Does |
|---|---|
| `.beelz list [vblood/shard/regular]` · `.beelz search <term>` · `.beelz info <i>` | Browse your captures |
| `.beelz bestiary` · `.beelz progress` · `.beelz catalog` | Collection progress |
| `.beelz top` · `.beelz odds` | Leaderboard and your current chances |

**Action bar**

| Command | Does |
|---|---|
| `.beelz grant <slot/primary/ultimate> <index>` | Put a capture on a slot (1-6) for every weapon |
| `.beelz weapon-grant <weapon> <slot> <index>` | Put a capture on a slot for one weapon type |
| `.beelz form-grant <form> <slot> <index>` | Build a shapeshift form's loadout (`mounted` uses slots 5-7) |
| `.beelz unslot <slot>` · `.beelz loadouts` | Free a slot · view your loadouts |
| `.beelz hotkey set <name> <index>` · `.beelz cast <name>` | Extra hotkeys beyond six |
| `.beelz clearbar [all/universal/<weapon>/<form>]` | Clear one loadout (captures kept) |
| `.beelz resetbar CONFIRM` | Back to the vanilla bar (captures kept) |

**Transformations and summons**

| Command | Does |
|---|---|
| `.beelz transforms` · `.beelz transform <name>` · `.beelz revert` | List, take and leave a form |
| `.beelz phase [n]` | Show or switch the form's ability set |
| `.beelz tform <unit> abilities/set <phase> <slot> <index>/defaults` | Build a form's kit |
| `.beelz refresh` · `.beelz detonate` | Re-apply a blank form bar · manual area burst |
| `.beelz summon [n]` · `.beelz summons stash/restore/clear/status` | Boss adds · manage your summons |

**Settings:** `.beelz verbosity silent|summary|verbose` · `.beelz silent on|off` · `.beelz help`

**Admin** (all start with `.beelz admin`, see `.beelz admin help`)

| Command | Does |
|---|---|
| `set <key> <value>` · `rules` · `reload` | Live config and the ability rules file |
| `ability <name/id> <field> <value>` · `ability <id> defaults` · `ability-inspect <ability>` | Shape and inspect abilities |
| `give` · `revoke` · `devour <player> <unitGuid>` · `transform-set <unit> <field> <value>` | Grants and transform rules |
| `lock add/max/remove/list/check` · `reseed preview/merge/replace CONFIRM` | Locks · take new shipped defaults |
| `bar <player>` · `purge <player> CONFIRM` · `cleanse <player>` · `respawn <player>` | Recovery: start with `bar` |
| `broadcast …` · `damage-stats` · `difficulty basic/brutal` | Server announcements and tuning |

## Configuration

- `BepInEx\config\kdpen.Beelzebub.cfg`: server defaults for drop chances, bad-luck protection, transform durations
  and cooldowns, summon limits, power scaling, announcements and hotkey limits. Most can be changed live with
  `.beelz admin set`.
- `ability_rules.json`: which abilities can drop and how each one is shaped. Created on first start; reload with
  `.beelz admin reload`. Reference:
  [ABILITY_CONFIG.md](Beelzebub/Beelzebub/docs/ABILITY_CONFIG.md).
- `state.json`: per-player data, keyed by Steam ID.

## Known limitations

- **Only five transformations.** A server-side mod cannot change how a player is drawn, so becoming any unit needs a
  client-side mod. That is planned through BloodCraftHub.
- **Some multi-stage boss abilities misfire.** A cast that spawns a projectile that spawns an area can lose a later
  stage when a player casts it. Many are fixed or curated out; the audit continues.
- **Some abilities look best in form.** A few are tied to a boss's skeleton and animate oddly on a vampire body.
- **Per-hit damage scaling is new** and off by default. Run a session in `Telemetry` and check
  `.beelz admin damage-stats` first.
- **A boss form takes a moment to appear.** Until it does, `.beelz phase` and `.beelz refresh` reply "Still
  transforming". If a form never finishes, `.beelz revert` clears it.
- **Untested at scale:** other mods, other server presets and large populations.

Especially useful to test: mixing captures with vanilla spells, hotkeys, the bar after weapon swaps, transforms and
dismounting, summons in group fights, the Devour rate, and reconnecting while transformed (your form and summons
should resume within 90 seconds).

## Roadmap

- **Mastery:** per-unit mastery levels and set rewards on top of the bestiary.
- **BloodCraftHub UI:** ability buttons, cooldown rings, the collection book, a transform browser and admin panels.
  The server side of this already ships.
- **More transformations:** becoming other units, through the client-side companion.
- **Summon AI:** allies that engage more reliably, and fight hostile players in PvP.
- **Ability audit:** more boss kits that fire cleanly when a player casts them.

## Feedback

Open an issue on [GitHub](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues) with what you were doing
and the `[Beelz]` lines from `BepInEx\LogOutput.log`. Bug reports, mod conflicts, broken abilities and balance notes
are all welcome.

## For developers and server admins

### Documentation
| Doc | What it covers |
|---|---|
| [Setup guide](Beelzebub/docs/SETUP_GUIDE.md) | Installing the dedicated server, BepInEx and Beelzebub; first-run config |
| [Commands](Beelzebub/Beelzebub/docs/COMMANDS.md) | Every player and admin command |
| [Ability config](Beelzebub/Beelzebub/docs/ABILITY_CONFIG.md) | Per-ability shaping knobs, cooldowns, damage modes, locks, reseed |
| [Recovery guide](Beelzebub/Beelzebub/docs/RECOVERY_GUIDE.md) | Fixing a stuck character or ability bar without a server wipe |
| [In-game test checklist](Beelzebub/Beelzebub/docs/INGAME_TEST_CHECKLIST.md) · [ability test plan (xlsx)](Beelzebub/Beelzebub/docs/V0136_ABILITY_TEST_PLAN.xlsx) | What to verify on a test server, row by row |
| [Summons as allies](Beelzebub/docs/SUMMON_AS_ALLY.md) | How captured summons become player allies |
| [Bloodcraft interop](Beelzebub/docs/INTEROP_BLOODCRAFT.md) | Running alongside Bloodcraft |
| [Ability change impact](Beelzebub/Beelzebub/docs/ABILITY_CHANGE_IMPACT.md) | Checklist for changing how an ability behaves without breaking its neighbours |
| [BloodCraftHub integration](Beelzebub/Beelzebub/docs/BCH_INTEGRATION_HANDOFF.md) | The `[BEELZ:*]` chat API contract for client UIs |

### Building from source
Requirements: the .NET 6 SDK. The V Rising, BepInEx and VCF references come from NuGet.

```powershell
cd Beelzebub
dotnet restore Beelzebub.sln
dotnet build Beelzebub.sln -c Release
dotnet test Beelzebub.Tests
```

If a V Rising Dedicated Server is installed at the default Steam path, the build also copies the DLL into its
`BepInEx\plugins` folder (override with `-p:VRisingServerPath="<path>"`; stop the server first — it locks the DLL).
The Thunderstore package is built with `tcli build` from `Beelzebub/Beelzebub/` (output in `build/`).

### Repository layout
```
Beelzebub/
├── Beelzebub/              the plugin (net6.0, BepInEx IL2CPP)
│   ├── Plugin.cs, Core.cs  entry point + service wiring
│   ├── Patches/            Harmony patches (death events, ability casts, buffs, summons, …)
│   ├── Services/           capture, slotting, transforms, tuning, cooldowns, summons, …
│   ├── Commands/           VCF chat commands (.beelz …, .beelz admin …, .beelz api …)
│   ├── Config/Settings.cs  BepInEx config bindings
│   ├── Resources/          shipped ability data, embedded in the DLL
│   │                       (ability_rules.default.json, ability_metadata.json, prefab names)
│   └── docs/               player/admin docs
├── Beelzebub.Tests/        unit tests for the pure logic
├── tools/                  Python data pipeline (metadata mining, audits, tester baseline, test sheet)
└── docs/                   setup + architecture docs
```

Contributions, bug reports and ability-viability notes are welcome — see [Feedback](#feedback).

---

## Credits

- **[Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/)** by zfolmt: its familiar system inspired
  Beelzebub's allied summons, and its approach shaped the kill hook and the real-form transformations. Beelzebub can
  also scale transform power from a player's Bloodcraft progression.
- **[KindredCommands](https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/)** by odjit: the command and
  plugin structure.
- **[VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/)** by deca: the
  chat command framework.
- **[BepInEx](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/)**: the modding framework.

These are independent projects; Beelzebub is not affiliated with or endorsed by them. Thanks also to everyone testing
on the development server: your reports decide what makes the final cut.

## License

[MIT](LICENSE)
