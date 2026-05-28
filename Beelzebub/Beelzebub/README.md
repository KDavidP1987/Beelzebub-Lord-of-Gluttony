<p align="center">
  <img src="https://raw.githubusercontent.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/main/Beelzebub/Beelzebub/splash.png" alt="Beelzebub, Lord of Gluttony" width="640">
</p>

# Beelzebub, Lord of Gluttony

> **Devour the bestiary.** Every kill is a chance to steal a unit's abilities — and the rarest of all lets you *devour its entire kit in one blow*.

---

## ⚠️ EARLY ACCESS (pre-1.0) — read this first

**Beelzebub is an early-access, pre-1.0, _server-side_ V Rising mod.** It works
end-to-end and runs on the developer's server, but it is being launched for **wide
testing** — expect rough edges, and treat it as experimental, not production-ready.
**By installing, you're helping test it.** 🦇

Before you install, please understand:

- **Not every ability is guaranteed to work.** V Rising has an *enormous* roster of
  unit abilities, and a captured ability is essentially an NPC/boss power dropped onto
  a player — some won't behave, animate, or fire correctly out of their original
  context. **A core goal of this test is to evaluate ability viability** so we can trim
  the bestiary and ability catalog down to what's genuinely valuable and usable for a
  player. Finding the ones that *don't* work is useful feedback, not a failure.
- **🔌 Strongly recommended: install BloodCraftHub.** Beelzebub is built to pair with
  the client-side **BloodCraftHub** companion app — on-screen ability buttons, cooldown
  display, your collection book, and more. Without it you're limited to chat commands
  and the vanilla six slots, so **to get the most out of Beelzebub you'll really want
  BloodCraftHub.** *(BCH ↔ Beelzebub integration is actively being built out; features
  land as testing progresses. Thunderstore link: **coming soon**.)*
- **Mod compatibility is not guaranteed — use at your own risk.** We *aim* to integrate
  cleanly with **Bloodcraft** and **KindredCommands** (Beelzebub is designed to coexist
  with both), but that integration is **not yet finalized or fully tested**. We can't
  guarantee compatibility with other mods — run it alongside anything else at your own
  risk, and please report conflicts.
- **Your progress is not safe yet.** On test servers, characters, collections, and even
  the **entire server may be wiped without notice** as we patch and rebuild. Don't get
  attached.

**Feedback is the whole point** — bug reports, mod-conflict reports, ability-viability
notes, and balance feedback are all hugely appreciated (see [Feedback](#feedback)).
Reports with `[Beelz]` log lines are gold.

---

A **server-side** V Rising mod that turns the whole bestiary into a collection-and-mastery loop. Defeat anything — a lowly bandit or a Soul Shard boss — and roll a chance to capture one of its abilities, or hit the rare **Devour** jackpot and learn its *entire* kit at once. Slot those abilities, bind extras to your own hotkeys, and hunt the realm to complete your collection.

**Source · issues · roadmap:** [github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony) · **License:** MIT

> **Status:** active early access / **public test build (v0.55.0)**. Functional end-to-end; slot changes apply instantly. Built for private/community servers — bring your testers and send feedback to the issue tracker.

---

## 📸 Screenshots

*Coming soon — screenshots and short clips from the development test server will be added here as we capture them. (Placeholder for an upcoming release.)*

---

## The loop

1. **Kill things.** Each kill rolls a small chance to capture one of the unit's abilities (default 5%), plus a rare **Devour** jackpot (default ~0.25%) that grants the unit's *entire* eligible kit at once.
2. **Bad luck doesn't last** — a built-in pity system nudges your odds up on every dry kill and resets when you finally get a drop.
3. **Wield what you collect.** Assign abilities to your spell slots, or bind extras to named hotkeys for an expanded action bar.
4. **Devour the rare ones.** The jackpot teaches you a whole unit's kit in a single kill. (Dracula & Morgana additionally unlock a true visual *transformation* — see below.)
5. **Complete the bestiary.** Track your progress per-unit and hunt down what you're missing.

## Features

### Capture & collect
- **Every kill rolls** for a single ability, plus a rare **Devour** jackpot that grants the unit's *whole* eligible kit at once — separate odds for regular mobs, V-Bloods, and shard bosses.
- **"⭐ DEVOURED Foulrot — learned all 6 of its abilities!"** The jackpot is the headline moment: a full kit in one kill, then you pick what to slot.
- **Escalating bad-luck protection (pity)** — the longer your dry streak, the better your odds, until it pays out.
- **Smart default filter** strips junk (idle/melee-filler/lifecycle abilities) so your collection stays useful.
- **Bestiary collection book** (`.beelz bestiary`) — see, per unit, how many of its abilities you've captured (X/Y) and what's left to hunt.

### Use your abilities
- **Assign captures to your six spell slots** — universal, or bound to a specific weapon family. Applies instantly.
- **Expanded action bar** — you're not capped at six. Bind any captured ability to a named hotkey and fire it on demand with `.beelz cast <name>` (cooldown-respecting). A companion app can surface these as on-screen buttons for 10, 15, 20+ abilities.
- **Wield-the-right-weapon hints** — weapon-based abilities tell you which weapon makes their animation read correctly.

### Transform — Dracula & Morgana
- **Two real boss forms.** Dracula and Morgana transform into their actual in-game forms — model, rig, and the abilities that need them — with **switchable kits** via `.beelz phase`, signature summons, and a manual AoE detonation (`.beelz detonate`).
- **Why only two?** A server-side mod *cannot* render your character as an arbitrary creature — the game decides your on-screen model on the client. Dracula and Morgana ship as player-renderable forms; every other unit's powers are instead collected as **abilities** (capture / Devour) and slotted onto your normal bar. **Becoming any other unit is a researched, postponed "phase two" feature** — it requires a future client-side companion mod to render the model, which the server alone can't do.
- **Summons fight for you — transformed or not.** Abilities that raise minions spawn them as your allies whether you're transformed **or** casting a captured summon ability in normal form (v0.45), with caps, leashing, and clean despawn. They **scale to your level** with an admin power dial; hop on a horse and your summons either stash-and-restore or keep following, your choice (`Transform_MountedSummonMode`). Manage them anytime with `.beelz summons <stash|restore|clear|status>`.
- **Signature add-summons** (`.beelz summon`) — call the adds a boss normally only spawns at low health (the Toad King's frogs, the Werewolf Chieftain's caged wolves, …). You also **learn a unit's summon as a standalone ability** you can slot or hotkey and use anytime.

### Admin & server control
- **Live config** — change drop rates, transform rules, pity, shard-boss settings and more at runtime with `.beelz admin set <key> <value>` (persists; no restart).
- **Power scaling, your way** — transforms and granted abilities already scale with the player's stats (so they track level/gear/prestige); on top of that, admins get global scaling modes and **per-ability damage tuning**, plus summon level-matching and a summon power factor.
- **Curated rules** in a hot-reloadable JSON: allow/deny lists, per-ability weapon/difficulty/scaling, per-unit transform tiers and stat scales. Full admin reference in [`docs/ABILITY_CONFIG.md`](docs/ABILITY_CONFIG.md).
- **Inclusive testing mode (on by default, v0.50)** — `Capture_InclusiveMode` makes abilities across **all** V-Bloods/NPCs broadly capturable, and `Grant_EnforceTransformOnly` (off by default) drops the "only for transformation" wall so any ability can be slotted on your normal bar. Flip both off for a curated, balanced server.
- **Cast tuning (opt-in, v0.46)** — make long casts **interruptible** (dash out / raise a shield to cancel) and **free the player to move once the cast finishes** (instead of staying rooted for the whole effect), per-ability via the rules file or `.beelz admin tune`. Off by default (`AbilityTuning_Enabled`); note it edits the ability's shared cast data, so the source NPC/boss cast changes too — enable, tune one ability, and test.
- **Custom abilities on shapeshift forms (experimental, v0.48)** — `Forms_CustomAbilities_Enabled` (off by default): shift into **Wolf or Bear** (the current test forms) via the in-game wheel and your loadout's abilities are loaded onto the form bar, with the form's "break on cast" removed so the form *holds* while you cast. Feasibility test for a full per-form-loadout system (assign abilities per form, like the per-weapon loadouts).
- **Difficulty gating, grant/revoke, `devour` (bulk-grant a unit's whole kit), inspect, audit logging** — full operator toolkit.

### 🔌 BloodCraftHub companion app — strongly recommended
Beelzebub is built to pair with the **client-side BloodCraftHub (BCH)** mod. A structured
`[BEELZ:*]` chat API lets BCH read your collection, slots, transforms, cooldowns, and settings,
and render **on-screen ability buttons** (including the expanded action bar beyond six slots),
**live cooldowns**, your **collection book**, and **admin panels**. Without BCH you're limited to
chat commands and the vanilla six slots — so for the intended experience, **install BloodCraftHub**.
*(Thunderstore link coming soon. The BCH ↔ Beelzebub integration is actively being built out;
features land as testing progresses.)*

## Requirements

- A V Rising **Dedicated Server** (Steam Tool AppID 1829350). Beelzebub is server-side — it does **not** run on a "Host & Play" private game.
- [BepInExPack_V_Rising](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/) and [VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/).
- **Strongly recommended (client-side):** the **BloodCraftHub** companion mod, installed by each player who wants the full experience (on-screen ability buttons, cooldowns, collection UI). Beelzebub works without it via chat commands, but BCH is how it's meant to be played. *(Thunderstore link coming soon.)*

## Installation

Install with [r2modman](https://thunderstore.io/package/ebkr/r2modman/) / Thunderstore Mod Manager (recommended), or drop `Beelzebub.dll` into `<VRisingDedicatedServer>\BepInEx\plugins\`. **Stop the server before replacing the DLL** — it's file-locked while running.

## Command cheat-sheet

**Collect & inspect:** `.beelz list [vblood|shard|regular]` · `.beelz search <term>` · `.beelz info <i>` · `.beelz bestiary` · `.beelz progress` · `.beelz catalog`
**Use abilities:** `.beelz grant <slot 1-6> <index>` · `.beelz weapon-grant <weapon> <slot> <index>` · `.beelz loadouts` (view universal + per-weapon sets) · `.beelz unslot <slot>` · `.beelz resetbar` (clear all bindings → vanilla bar) · `.beelz hotkey set <name> <index>` → `.beelz cast <name>`
**Summons:** `.beelz summons <stash|restore|clear|status>` (works for captured summon abilities, not just transforms) · `.beelz summon [n]` (a transformed boss's signature add-summon)
**Transform (Dracula & Morgana):** `.beelz transforms` · `.beelz transform <name>` · `.beelz phase [n]` · `.beelz revert` · `.beelz refresh` (re-apply your bar if it ever goes blank) · `.beelz detonate`
**Admin:** `.beelz admin set <key> <value>` · `.beelz admin devour <player> <unitGuid>` (grant a unit's whole kit) · `.beelz admin give/revoke …` · `.beelz admin rules` / `deny` / `allow` / `reload` · `.beelz admin difficulty <basic|brutal>` · `.beelz admin tune <ability> <interrupt|freemove|castspeed>` (opt-in cast tuning) · `.beelz admin testform <wolf|bear|off>` (experimental form test) · `.beelz admin help`
**Settings:** `.beelz verbosity <silent|summary|verbose>` · `.beelz help` · `.beelz commands`

## Configuration

`BepInEx\config\kdpen.Beelzebub.cfg` holds server defaults (drop chances, pity, transform modes/durations/cooldowns per category incl. shard bosses, summon caps + lifetime + level-matching + power factor, mounted-summon behavior, granted-ability power scaling, hotkey limits, difficulty). Most can also be changed live with `.beelz admin set`. `ability_rules.json` holds the curation matrix incl. per-ability damage scaling (auto-created, hot-reload with `.beelz admin reload`); `state.json` holds per-player data.

---

## ⚠️ Known limitations & what still needs testing

Honest, up front. These are the areas we **know** are rough or unverified at wide
scale — they're exactly what this test release is meant to shake out. If you can
help confirm or break any of these, that's the most valuable feedback we can get.

- **Transformation is intentionally Dracula & Morgana only.** A server-side mod
  *cannot* render your character as an arbitrary creature — the game decides your
  on-screen model on the client. Rather than ship "transformations" that don't
  visually change anything for most units, every other unit's powers are collected
  as **abilities** (capture / the Devour jackpot) and slotted onto your normal bar.
  True "become any unit" visuals are a **researched, postponed phase-two feature**
  that needs a client-side companion mod to render the model (planned via
  BloodCraftHub). Dracula & Morgana ship as player-renderable forms, so they remain
  full transformations today.
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
  behavior in group combat, whether the Devour jackpot rate feels right, and
  **disconnect/reconnect while transformed** — a quick relog should resume your form
  and summons (within `Transform_ReconnectGraceSeconds`, default 90s), and any login
  should always land you on a working ability bar.

If something breaks: grab the `[Beelz]`-tagged lines from
`BepInEx\LogOutput.log` and open an issue with what you were doing.

## 🗺️ Roadmap

Where this is heading (subject to change based on your feedback):

- **Collection & mastery loop** — per-unit *mastery levels* and *milestone/set
  rewards* on top of the existing bestiary + pity systems.
- **BloodCraftHub companion UI** — on-screen ability buttons (including the expanded
  action bar), live cooldown rings, the collection book, a transform browser, and
  admin panels. The server-side contract for this already ships in Beelzebub.
- **Phase two: creature transformation** — becoming units beyond Dracula & Morgana
  (model + animation), achievable only via a client-side companion mod (BloodCraftHub).
  Researched and on the roadmap; postponed because a server can't drive client rendering.
- **Continued ability-chain auditing** — get more boss kits firing cleanly when cast
  by a player, and expand the manual-detonation roster.
- **Quality of life** — persisting pity across restarts, broadcasting config changes
  to all connected companion clients, and more curation tooling for admins.

Want to influence priorities? Open an issue — early feedback shapes the order.

## 🙏 Credits & acknowledgements

Beelzebub stands on the shoulders of the V Rising server-modding community:

- **[Bloodcraft](https://thunderstore.io/c/v-rising/p/zfolmt/Bloodcraft/) by zfolmt** —
  a major inspiration and reference for this build. In particular, Bloodcraft's
  **familiar system** directly inspired Beelzebub's player-allied summons, and its
  approach informed our death-event capture hook and the **ExoForm** real-form transform
  technique (applying a unit's actual form buff so its abilities work). Beelzebub is
  designed to **coexist** with Bloodcraft and can optionally scale transform power using a
  player's Bloodcraft progression. Huge thanks to zfolmt.
- **[KindredCommands](https://thunderstore.io/c/v-rising/p/odjit/KindredCommands/) by odjit** —
  reference for the command + plugin scaffold patterns.
- **[VampireCommandFramework](https://thunderstore.io/c/v-rising/p/deca/VampireCommandFramework/) by deca** —
  the command framework Beelzebub's chat interface is built on.
- **[BepInEx](https://thunderstore.io/c/v-rising/p/BepInEx/BepInExPack_V_Rising/)** —
  the modding framework that makes all of this possible.

These are independent projects by their respective authors; Beelzebub is not
affiliated with or endorsed by them. All credit for their work is theirs.

### 🧪 Testers
Heartfelt thanks to everyone helping shake this out on the development test server — your
bug reports, ability-viability notes, and balance feedback directly shape what makes the
final cut:

- *<tester names / Discord handles to be added here>*

**Want to help test?** We're spinning up a development test server now — jump in via our
community (see [Feedback](#feedback)) and you'll be credited here. Just remember it's early
access: expect bugs, and progress (or the whole server) may be wiped as we iterate.

## Feedback

Open an issue on [GitHub](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues). Log lines tagged `[Beelz]` in `BepInEx\LogOutput.log` are the most useful diagnostic — paste them with what you were doing. Because this is an active test build, **bug reports, mod-conflict reports, and balance feedback are all hugely appreciated.**

## License

[MIT](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/LICENSE).
