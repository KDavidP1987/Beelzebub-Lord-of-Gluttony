# Beelzebub, Lord of Gluttony

> **Devour the bestiary.** Every kill is a chance to steal a unit's abilities — and the rarest of all lets you *become* the unit and fight with its full kit.

A **server-side** V Rising mod that turns the whole bestiary into a collection-and-mastery loop. Defeat anything — a lowly bandit or a Soul Shard boss — and roll a chance to capture its abilities or unlock the power to transform into it. Slot those abilities, bind extras to your own hotkeys, and hunt the realm to complete your collection.

**Source · issues · roadmap:** [github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony) · **License:** MIT

> **Status:** active early access (v0.40.0). Functional end-to-end; slot changes apply instantly. Built for private/community servers — bring your testers and send feedback to the issue tracker.

---

## The loop

1. **Kill things.** Each kill rolls a small chance to capture one of the unit's abilities (default 5%) and a rarer chance to unlock transforming into it (default 1%).
2. **Bad luck doesn't last** — a built-in pity system nudges your odds up on every dry kill and resets when you finally get a drop.
3. **Wield what you collect.** Assign abilities to your spell slots, or bind extras to named hotkeys for an expanded action bar.
4. **Become the unit.** Spend the rare transform unlocks to take on a unit's form and full ability set — including real boss forms.
5. **Complete the bestiary.** Track your progress per-unit and hunt down what you're missing.

## Features

### Capture & collect
- **Every kill rolls** for abilities + a transform unlock, with separate odds for regular mobs, V-Bloods, and the shard bosses.
- **Escalating bad-luck protection (pity)** — the longer your dry streak, the better your odds, until it pays out.
- **Smart default filter** strips junk (idle/melee-filler/lifecycle abilities) so your collection stays useful.
- **Bestiary collection book** (`.beelz bestiary`) — see, per unit, how many of its abilities you've captured (X/Y), whether you've unlocked its transform, and what's left to hunt.

### Use your abilities
- **Assign captures to your six spell slots** — universal, or bound to a specific weapon family. Applies instantly.
- **Expanded action bar** — you're not capped at six. Bind any captured ability to a named hotkey and fire it on demand with `.beelz cast <name>` (cooldown-respecting). A companion app can surface these as on-screen buttons for 10, 15, 20+ abilities.
- **Wield-the-right-weapon hints** — weapon-based abilities tell you which weapon makes their animation read correctly.

### Transform into units
- **Become any unit you've unlocked.** Your bar fills with its abilities.
- **Real boss forms** — Dracula and Morgana transform into their actual in-game forms (model, rig, and the abilities that need them), with **switchable kits** via `.beelz phase`. Animal-type units (wolves, bears, spiders, toads, …) likewise use their real shapeshift form.
- **Shard bosses are their own tier** — Dracula, Adam the Firstborn, Solarus the Immaculate, The Winged Horror, Megara the Serpent Queen, and Gorecrusher the Behemoth can have their own transform mode/duration/cooldown, separate from ordinary V-Bloods.
- **Summons fight for you** — abilities that raise minions spawn them as your allies, with caps, leashing, and clean despawn.
- **Manual detonation** — fire a boss's signature AoE on demand (`.beelz detonate`).

### Admin & server control
- **Live config** — change drop rates, transform rules, pity, shard-boss settings and more at runtime with `.beelz admin set <key> <value>` (persists; no restart).
- **Curated rules** in a hot-reloadable JSON: allow/deny lists, per-ability weapon/difficulty/scaling, per-unit transform tiers and stat scales.
- **Difficulty gating, grant/revoke, force-transform, inspect, audit logging** — full operator toolkit.

### Companion-app ready (BloodCraftHub)
A structured `[BEELZ:*]` chat API lets the client-side **BloodCraftHub** mod read your collection, slots, transforms, cooldowns, and settings — and render on-screen buttons (including the expanded action bar) and admin panels.

## Requirements

- A V Rising **Dedicated Server** (Steam Tool AppID 1829350). Beelzebub is server-side — it does **not** run on a "Host & Play" private game.
- [BepInExPack_V_Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) and [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/).

## Installation

Install with [r2modman](https://thunderstore.io/package/ebkr/r2modman/) / Thunderstore Mod Manager (recommended), or drop `Beelzebub.dll` into `<VRisingDedicatedServer>\BepInEx\plugins\`. **Stop the server before replacing the DLL** — it's file-locked while running.

## Command cheat-sheet

**Collect & inspect:** `.beelz list [vblood|shard|regular]` · `.beelz search <term>` · `.beelz info <i>` · `.beelz bestiary` · `.beelz progress` · `.beelz catalog`
**Use abilities:** `.beelz grant <slot 1-6> <index>` · `.beelz weapon-grant <weapon> <slot> <index>` · `.beelz hotkey set <name> <index>` → `.beelz cast <name>`
**Transform:** `.beelz transforms [vblood|shard|regular]` · `.beelz transform <name>` · `.beelz phase [n]` · `.beelz revert` · `.beelz detonate` · `.beelz summons <stash|restore|status>`
**Admin:** `.beelz admin set <key> <value>` · `.beelz admin transform mode/duration/cooldown …` · `.beelz admin give/revoke …` · `.beelz admin rules` / `deny` / `allow` / `reload` · `.beelz admin difficulty <basic|brutal>`
**Settings:** `.beelz verbosity <silent|summary|verbose>` · `.beelz help` · `.beelz commands`

## Configuration

`BepInEx\config\kdpen.Beelzebub.cfg` holds server defaults (drop chances, pity, transform modes/durations/cooldowns per category incl. shard bosses, summon caps, hotkey limits, difficulty). Most can also be changed live with `.beelz admin set`. `ability_rules.json` holds the curation matrix (auto-created); `state.json` holds per-player data.

## Honest caveats

- **Model changes are limited by the engine.** A server can only render the ~10 forms V Rising actually ships (the boss forms + the native shapeshifts). Most humanoid/undead/construct units transform as **abilities + stats only** (your model doesn't change) — true "become any NPC" would require a client-side companion mod. Boss/animal forms get the full model.
- **Some boss abilities read best in-form.** A few abilities are tied to a unit's skeleton; cast on your vampire body they may not animate perfectly. They work correctly while transformed into that unit.
- **Early access** — expect rough edges; reports are gold.

## Feedback

Open an issue on [GitHub](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues). Log lines tagged `[Beelz]` in `BepInEx\LogOutput.log` are the most useful diagnostic — paste them with what you were doing.

## License

[MIT](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/LICENSE).
