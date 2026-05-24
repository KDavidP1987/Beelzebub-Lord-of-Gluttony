<p align="center">
  <img src="https://raw.githubusercontent.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/main/Beelzebub/Beelzebub/splash.png" alt="Beelzebub, Lord of Gluttony" width="640">
</p>

# Beelzebub, Lord of Gluttony

> **Devour the bestiary.** Every kill is a chance to steal a unit's abilities — and the rarest of all lets you *become* the unit and fight with its full kit.

---

## ⚠️ EARLY TEST RELEASE — please read first

**This mod is brand new and is being launched for testing.** It is effectively in
**alpha/beta**. It works end-to-end and has been tested on the developer's own
server, but it has **not** had wide testing yet. Before you install it on anything
you care about, know that:

1. **This is a testing release, just getting launched.** Expect rough edges and
   please treat it as experimental, not production-ready.
2. **We are not yet aware of incompatibilities with other mods.** It has not been
   tested alongside most of the modding ecosystem — if you run other server mods,
   there may be conflicts we don't know about yet.
3. **We don't yet know how it behaves across different servers / configurations.**
   Different server settings, presets, population sizes, and hardware are untested.
   Something that works on one server may misbehave on another.
4. **Feedback is genuinely wanted — it's still being actively developed.** If you
   hit a bug, a conflict, or just something that feels off, please tell us (see
   [Feedback](#feedback)). Reports with `[Beelz]` log lines are gold.

**By installing, you're helping test it. Thank you — that's exactly what this
release is for.** 🦇

---

A **server-side** V Rising mod that turns the whole bestiary into a collection-and-mastery loop. Defeat anything — a lowly bandit or a Soul Shard boss — and roll a chance to capture its abilities or unlock the power to transform into it. Slot those abilities, bind extras to your own hotkeys, and hunt the realm to complete your collection.

**Source · issues · roadmap:** [github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony) · **License:** MIT

> **Status:** active early access / **public test build (v0.43.0)**. Functional end-to-end; slot changes apply instantly. Built for private/community servers — bring your testers and send feedback to the issue tracker.

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
- **Summons fight for you** — abilities that raise minions spawn them as your allies, with caps, leashing, and clean despawn. Hop on a horse and your summons either stash-and-restore or keep following into combat, your choice (`Transform_MountedSummonMode`).
- **Manual detonation** — fire a boss's signature AoE on demand (`.beelz detonate`).

### Admin & server control
- **Live config** — change drop rates, transform rules, pity, shard-boss settings and more at runtime with `.beelz admin set <key> <value>` (persists; no restart).
- **Curated rules** in a hot-reloadable JSON: allow/deny lists, per-ability weapon/difficulty/scaling, per-unit transform tiers and stat scales.
- **Difficulty gating, grant/revoke, force-transform, inspect, audit logging** — full operator toolkit.

### Companion-app ready (BloodCraftHub)
A structured `[BEELZ:*]` chat API lets the client-side **BloodCraftHub** mod read your collection, slots, transforms, cooldowns, and settings — and render on-screen buttons (including the expanded action bar) and admin panels. *(BloodCraftHub integration is in development.)*

## Requirements

- A V Rising **Dedicated Server** (Steam Tool AppID 1829350). Beelzebub is server-side — it does **not** run on a "Host & Play" private game.
- [BepInExPack_V_Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) and [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/).

## Installation

Install with [r2modman](https://thunderstore.io/package/ebkr/r2modman/) / Thunderstore Mod Manager (recommended), or drop `Beelzebub.dll` into `<VRisingDedicatedServer>\BepInEx\plugins\`. **Stop the server before replacing the DLL** — it's file-locked while running.

## Command cheat-sheet

**Collect & inspect:** `.beelz list [vblood|shard|regular]` · `.beelz search <term>` · `.beelz info <i>` · `.beelz bestiary` · `.beelz progress` · `.beelz catalog`
**Use abilities:** `.beelz grant <slot 1-6> <index>` · `.beelz weapon-grant <weapon> <slot> <index>` · `.beelz hotkey set <name> <index>` → `.beelz cast <name>`
**Transform:** `.beelz transforms [vblood|shard|regular]` · `.beelz transform <name>` · `.beelz phase [n]` · `.beelz revert` · `.beelz refresh` (re-apply your bar if it ever goes blank) · `.beelz detonate` · `.beelz summons <stash|restore|status>`
**Admin:** `.beelz admin set <key> <value>` · `.beelz admin transform mode/duration/cooldown …` · `.beelz admin give/revoke …` · `.beelz admin rules` / `deny` / `allow` / `reload` · `.beelz admin difficulty <basic|brutal>`
**Settings:** `.beelz verbosity <silent|summary|verbose>` · `.beelz help` · `.beelz commands`

## Configuration

`BepInEx\config\kdpen.Beelzebub.cfg` holds server defaults (drop chances, pity, transform modes/durations/cooldowns per category incl. shard bosses, summon caps, mounted-summon behavior, hotkey limits, difficulty). Most can also be changed live with `.beelz admin set`. `ability_rules.json` holds the curation matrix (auto-created); `state.json` holds per-player data.

---

## ⚠️ Known limitations & what still needs testing

Honest, up front. These are the areas we **know** are rough or unverified at wide
scale — they're exactly what this test release is meant to shake out. If you can
help confirm or break any of these, that's the most valuable feedback we can get.

- **Full transformations / model fidelity is limited by the engine.** A server can
  only render the ~10 forms V Rising actually ships (the boss forms + the native
  animal shapeshifts). Most humanoid/undead/construct units transform as
  **abilities + stats only** — your *model* doesn't change, you just get the kit.
  True "become any NPC" visually would require a client-side companion mod
  (planned via BloodCraftHub). Boss/animal forms get the full model.
- **Ability chaining can misfire.** Some captured boss abilities are multi-stage
  "chains" (a cast that spawns a projectile that spawns an AoE, etc.). A handful of
  these don't fully complete when cast by a player instead of the original NPC —
  the cast animates but a later stage may not fire, or may not be correctly
  team-attributed. We've fixed many (e.g. the Undead Priest's nova) and curated
  around others, but this is an area we're still actively auditing.
- **Some boss abilities read best in-form.** A few abilities are tied to a unit's
  skeleton; cast on your vampire body they may not animate perfectly. They work
  correctly while transformed into that unit.
- **Mod compatibility is unverified.** We have not tested against the broader mod
  ecosystem. It shares some Harmony patch surfaces with Bloodcraft (and is designed
  to coexist), but other mods are unknown territory — please report conflicts.
- **Cross-server / config behavior is unverified.** Different presets, difficulty
  modes, populations, and hardware haven't been tested. The power-scaling modes for
  transforms in particular benefit from real-world tuning feedback.
- **Things we'd especially love tested:** the expanded action bar (`.beelz cast`),
  ability-bar persistence across weapon swaps / transforms / dismounting, summon
  behavior in group combat, and whether shard-boss transforms feel balanced.

If something breaks: grab the `[Beelz]`-tagged lines from
`BepInEx\LogOutput.log` and open an issue with what you were doing.

## 🗺️ Roadmap

Where this is heading (subject to change based on your feedback):

- **Collection & mastery loop** — per-unit *mastery levels* and *milestone/set
  rewards* on top of the existing bestiary + pity systems.
- **BloodCraftHub companion UI** — on-screen ability buttons (including the expanded
  action bar), live cooldown rings, the collection book, a transform browser, and
  admin panels. The server-side contract for this already ships in Beelzebub.
- **Model & animation fidelity** — true "look like the unit you've become" for
  non-boss transforms, achievable only via that client-side companion mod.
- **Continued ability-chain auditing** — get more boss kits firing cleanly when cast
  by a player, and expand the manual-detonation roster.
- **Quality of life** — persisting pity across restarts, broadcasting config changes
  to all connected companion clients, and more curation tooling for admins.

Want to influence priorities? Open an issue — early feedback shapes the order.

## 🙏 Credits & acknowledgements

Beelzebub stands on the shoulders of the V Rising server-modding community:

- **[Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) by zfolmt** —
  a major inspiration and reference for this build. Beelzebub's death-event capture
  hook, its player-allied summon/familiar handling, and the **ExoForm** real-form
  transform technique (applying a unit's actual form buff so its abilities work) were
  all informed by studying Bloodcraft's approach. Beelzebub is designed to **coexist**
  with Bloodcraft, and can optionally scale transform power using a player's Bloodcraft
  progression. Huge thanks to zfolmt.
- **[KindredCommands](https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/) by odjit** —
  reference for the command + plugin scaffold patterns.
- **[VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/) by deca** —
  the command framework Beelzebub's chat interface is built on.
- **[BepInEx](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/)** —
  the modding framework that makes all of this possible.

These are independent projects by their respective authors; Beelzebub is not
affiliated with or endorsed by them. All credit for their work is theirs.

## Feedback

Open an issue on [GitHub](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues). Log lines tagged `[Beelz]` in `BepInEx\LogOutput.log` are the most useful diagnostic — paste them with what you were doing. Because this is an active test build, **bug reports, mod-conflict reports, and balance feedback are all hugely appreciated.**

## License

[MIT](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/LICENSE).
