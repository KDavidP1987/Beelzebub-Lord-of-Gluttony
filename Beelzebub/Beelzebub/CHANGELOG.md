# Changelog

What's new for players and server admins, newest first. This file ships with each Thunderstore release and
lists the ten most recent versions; older versions are in
[CHANGELOG_FULL.md](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/Beelzebub/Beelzebub/docs/CHANGELOG_FULL.md),
and the technical history is the [commit log](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/commits/main).

## [0.137.6] - 2026-10-04

### Server startup errors fixed

- **No more `Couldn't remap old Modification Id` errors at startup.** Clearing or changing a spell slot could leave
  a hidden helper object behind. It was saved with your world, and every startup logged an error for it. Beelzebub
  now finds these leftovers after each startup and each bar change and removes them, after checking twice, 2 seconds
  apart, that nothing uses them any more. The first startup on this version cleans up the ones already saved.
- **Slot 7's cooldown sharing is right again.** Those leftovers could force the slot-7 spell's shared-cooldown
  setting off, and change slot 1's spell modifiers. Removing them restores the real values.
- **New admin tool: `.beelz admin modleak [player|all]`.** It is read-only and lists the hidden slot helpers and
  whether any are stale. You won't need it normally: the cleanup runs on its own, and the server log shows
  `[Beelz MODLEAK] sweep` lines.

### Fixes

- **Militia Leader's Whirlwind no longer logs a conflict warning at startup.** Its two versions disagreed on when
  you can move again (1 s and 10 s). Both now use 1 s. Existing servers pick this up with
  `.beelz admin reseed merge`.

## [0.137.5] - 2026-10-04

### Transformations can no longer stack

- **One transformation at a time.** `.beelz transform`, `.beelz phase`, `.beelz refresh`, automatic phase changes,
  travel and login now all check your current form first, so a form can no longer be applied on top of another one.
- **A boss form gets a moment to appear.** While a form is still appearing (Morgana's takes a second),
  `.beelz phase` and `.beelz refresh` reply "Still transforming — give it a moment, then try again."
- **`.beelz admin testform` asks you to revert first** while you are transformed, the same rule `.beelz transform`
  already follows.

### Fixes

- **`.beelz phase` works for Dracula and Morgana again.** Since v0.100 it refused every phase above 1 with
  "no eligible abilities". Automatic phase changes were not affected. The reply now counts the abilities that land
  on your bar, including your own loadout.
- Leaving combat while a phase was still appearing could skip the automatic return to phase 1. It now retries until
  it applies, unless you choose a phase yourself.

## [0.137.4] - 2026-10-04

### clearbar clears the whole bar for the loadout you pick

- **`.beelz clearbar [all|universal|<weapon>|<form>]` now runs the same full reset as `resetbar`**, limited to the
  loadout you name. Before, it only forgot the saved binds: a saddle ability stayed on the bar while you rode, and a
  transform (a boss form, Wolf form) kept its abilities after `clearbar universal`.
- **Your other loadouts are kept.** `clearbar sword` keeps your universal binds on the bar and your form loadouts saved.
- **A clear while riding dismounts you, and a clear while transformed ends the transform, and the reply says so**
  ("You were dismounted to reset your bar; remount to ride." / "Your transform was ended to clear the bar.").
- If something stays stuck, the reply adds a second line naming it, and the server log gets a `[Beelz RESET]` line for
  every clear. If the clear itself fails, the reply says "Could not clear ..." instead of "Nothing was bound".
- Fixed a reset that ended a transform reporting the transform's buff as "still on" when it was already gone.

## [0.137.3] - 2026-10-04

### Saddle abilities use R, C and T

- **A saddle ability now goes on slot 5, 6 or 7 — the R, C and Ultimate keys** (`.beelz form-grant mounted 5 <id>`).
  Slot 3 is refused: the game never draws a key for it, so an ability there was live but invisible. A saddle bind you
  already had on slot 3 moves to slot 5 (R) the next time the server starts, unless R already has one.
- **`.beelz resetbar` while riding now dismounts you on purpose and tells you** ("You were dismounted to reset your
  bar; remount to ride."). Before, it threw you off the horse silently. Remount and the horse's own kit is back.
- Fixed a reset removing the horse's control effect twice in one pass, which could report the reset as not clean.

## [0.137.2] - 2026-10-03

### Changing your bar while in a form now works like the weapon bar

- **`.beelz unslot` while in a form (Wolf, Bear, …) puts your weapon's skill back on that slot at once**, the way
  vanilla Wolf keeps your weapon skill on a slot its kit leaves empty. Before, the slot went blank until you
  re-equipped your weapon. A form ability of your own on that slot is left alone.
- **A grant or unslot made while in a form, a transform or on a mount now applies when that ends** — no weapon swap
  needed.

## [0.137.1] - 2026-10-03

### Granted abilities show up at once — no weapon swap

- **`.beelz grant` now changes your action bar immediately.** Before, the ability was saved but the bar kept the old
  one (and it would not cast) until you unequipped or swapped your weapon. The same goes for `.beelz weapon-grant`,
  admin grants, presets, `.beelz refresh` and the bar coming back after a transform.
- **`.beelz unslot` puts the right ability back at once** — your weapon's own skill on a weapon slot, your spellbook
  spell on a spell slot — even for an ability that was applied when you equipped the weapon.
- While you are in a form, a transform or on a mount, Beelzebub no longer writes your weapon bar over that kit; a
  grant made while transformed shows when you change back (a weapon swap shows it at once).
- Known issue: `.beelz unslot` while in a form (Wolf, etc.) leaves that slot blank until you re-equip your weapon.
  Fixed in 0.137.2.

## [0.137.0] - 2026-09-30

### One reset that actually clears a stuck action bar

A spell bar could stay "stuck" after `.beelz resetbar`: the spells came back from a layer the reset had skipped
(a saved bind, a row on your weapon, a leftover form buff, or an engine-level slot change), and often only a weapon
swap or a relog showed the real bar. Every bar reset now goes through one path that clears all of those layers, in
order, and then reads the bar back to prove it.

- **`.beelz resetbar CONFIRM` works in one go.** It ends any transform, clears every universal / weapon / form bind
  and every Beelzebub change on the live bar, and puts your weapon's own skills back **without a weapon swap**. It
  then reads the bar back: the reply either says your bar is back to vanilla or names the slots (or the form buff)
  still overriding it. Captures, unlocks, hotkeys and presets are kept.
- **Admins: `.beelz admin reset-loadouts` and `.beelz admin purge` use the same reset.** `purge` also clears
  hotkeys. Both now work on an **offline** player too: the saved binds are cleared at once and the live bar resets
  when they log in.
- **New `.beelz admin bar [player]` — start here.** A read-only readout of each bar slot (primary, 1-6, ultimate):
  the ability it resolves to, its saved bind, whether Beelzebub put a row on the weapon, the engine's slot changes
  (gear vs other) and any form/override buffs. It changes nothing. `.beelz admin rebuildbar` now shows the same
  readout; `clearslotmods` / `rebuildslots` are marked legacy — use `purge`.
- **Shapeshift forms keep their own kit by default.** A Wolf / Bear / … form with no form or universal binds used
  to fill its bar with your first six captures; it now keeps the form's own abilities. Set
  `Forms_AutoFillFromCaptures = true` (section `Forms`) for the old behaviour.
- **Every reset is logged** as one `[Beelz RESET]` line plus a `[Beelz BAR]` readout, so a bar that is still stuck
  can be diagnosed from the server log.
- **Recovery guide rewritten** around the single flow: `resetbar` → `admin bar` → `purge` → relog.
- Known limits: dismount before resetting (a mount's saddle bar is not touched); a reset that ends a timed transform
  starts that transform's normal cooldown; a row another mod put on your weapon's slots is removed too.
- **Fixed: Space, R, C and the ultimate after a reset.** Every reset since v0.43 (`resetbar`, `rebuildslots`, `purge`)
  left a hidden "empty" slot change on every bar slot, one more per reset, saved with the world: Space lost its
  shadow dash and the spell menu could not drop a spell onto R, C or T on the first try. The reset now only touches
  your weapon's own slots and removes those leftovers, so **one `.beelz resetbar CONFIRM` also repairs a bar damaged
  by an earlier version**. Admins: `.beelz admin bar-raw [player]` writes each slot's raw engine data to the server log.
- **Fixed: your spellbook spells are yours again.** Beelzebub's transform used the same hidden buff V Rising uses
  for every spell you pick in the spellbook (J), and logging in or resetting could delete it: the spellbook still
  listed the spell, but its key (Space, R, C or T) went blank and picking the same spell again did nothing. Logins,
  resets and transforms now only ever remove Beelzebub's own buff, a reset keeps your spellbook spells on their keys,
  and **spells broken by earlier versions come back on their own at your next login**.
- **Fixed:** `.beelz api catalog abilities` stopped partway with an error on abilities whose notes contain
  punctuation like "—"; long API lines are now split by size in bytes, so the whole catalog streams.
- BloodCraftHub: API version 33 (one new config key; nothing else changed).

## [0.136.0] - 2026-09-23

### Shipped baseline from the Discord testing round

The default ability rules now carry the results of the V-Blood + NPC testing round (88 Discord threads plus the
NPC sheet). Every ability it touched has a `[v0.136 baseline]` note that says why. Admins can change any of it
with `.beelz admin ability <name> …`.

- **Tester fixes are pre-set, about 210 abilities.** The abilities testers said "work, but…" now ship with the
  fix they asked for:
  - **Unrooted:** you're free to move once the wind-up fires (`freelymove`), including channels.
  - **Cooldowns** on abilities that had none, using the tester's number where they gave one (e.g. Electric Field
    120s, Field of Spears 30s).
  - **Damage** ×0.5 on the overtuned ones and ×1.5 on the weak ones.
  - **Launch abilities clamped** to a short hop (Bat Summon Minions, Frog Split, Harpy Soar).
  - **Summon limits:** Morgana's tail is limited to 1 and despawns after 30s; Harpy summons and Raise Dead
    skeletons despawn.
  - **Jade's caltrops** can't be thrown across the screen any more.
- **About 300 abilities soft-disabled.** Testers found them non-functional or not worth using, and no config can
  fix them. They no longer drop by default. An admin can turn any back on with
  `.beelz admin ability <name> enabled true`.
- **Second pass over every tester note.** 25 abilities that had been switched off came back on with the fix the
  tester described (e.g. Bandit Foreman's crossbows unrooted with a 10s cooldown, Paladin Divine Rays and Dash,
  Glassblower Glass Rain / Mirror Shield / Wind Dash, Corpse Storm at ×1.5 damage, Tourok Chaos Charge). 15 more
  got cooldowns testers asked for but the first pass missed, including Lightning Storm at 120s, Poloma Otherside,
  Paladin Heal Angel and the Yeti Hard avalanches. Zealous Cultist Ghastly Mockery and Bandit Stalker
  Reinforcement are now soft-disabled.
- **10 more abilities hard-blocked** (can't be re-enabled). Testers found each one crashes the game, breaks a
  character, or wrecks bases:
  - Sommelier Barrel Dance / Horizontal Barrel (destroys neighbouring plots).
  - Bell Ringer Ring Bell (crash).
  - Elena's Tower of Frost ×2 (stuck in the sky).
  - Castle Man Holy Beam ×2 / Triple Spinning Cross (only removable by dying).
  - Werewolf Chieftain Open The Cages.
- Previously re-enabled launch abilities that testers still rate poorly are soft-disabled again: Toad King
  Swallow, Iva Jetpack, Gargoyle Fly ×3. Dracula's Bolt Spray stays enabled while it's under review.
- **Existing servers:** run `.beelz admin reseed replace CONFIRM` (a backup is kept) to take the whole baseline.
  `reseed merge` adds the new blocks, but on a server without a baseline file it doesn't copy preset values onto
  abilities you already have.

### Cooldowns now work on abilities that have none

- A per-ability `cooldown` on an ability with **no cooldown of its own** used to do nothing. It had only
  re-timed cooldowns the game started itself. Beelzebub now starts that cooldown on the player's slot at cast
  time, and re-applies it if the game clears it. Bosses are still untouched. The global
  `Grant_MinimumCooldownSeconds` floor still only raises existing cooldowns.

## [0.135.0] - 2026-09-23

### Incompatibility locks (admin-defined)

- **Admins can now lock abilities that break the game when used together.** A lock group says "at most N
  of these abilities on one player's bar at a time", e.g.
  `.beelz admin lock add nocombo "Gargoyle WingShield, Rat Vanguard"`. Members can be ability names, IDs,
  or a whole category (`cat:Summon` = "only one summon ability at a time"). **No locks ship**; which
  combinations to lock is up to each server.
- **What players see:** binding a locked ability (`grant`, `weapon-grant`, `form-grant`, `hotkey set`) is
  refused with a 🔒 message naming the group and what it's already holding. If an admin adds a lock you're
  already breaking, the later ability is kept off your live bar (the slot shows its normal ability) and you
  get a 🔒 message. Your binds are never deleted: when the lock is lifted the ability comes back by itself
  (🔓). `.beelz cast` also refuses a locked ability. Transforms are not affected.
- Admin commands: `lock add`, `lock max`, `lock remove`, `lock list`, `lock check <player>`. Changes apply to
  everyone online immediately.

### Picking up new shipped defaults: `.beelz admin reseed`

- `ability_rules.json` is only created from the shipped defaults once, so later fixes never reached existing
  servers. **`.beelz admin reseed preview`** shows what would change. **`reseed merge`** takes the shipped
  changes you never touched and keeps your own edits; if you both changed something, yours wins and it's
  listed. **`reseed replace CONFIRM`** takes the shipped file wholesale. A backup is always kept.
- The first `merge` on an older server runs in a safe mode that only adds missing abilities and adopts
  shipped crash-blocks; later merges are full.

### Safety timers on stuck-state abilities

- The Fall Asleep family, Cassius's High Lord Leap Strike and Gaius's Corpse Buff are still **disabled**, but
  now carry a force-timeout (8 s / 4 s / 6 s). An admin who chooses to enable one gets the timer as well.

### Fixes

- A second cast of the same ability on a different slot could have its cooldown confused with the first.
- Cooldown rules now recognise charge abilities whose charges live on the cast rather than the ability, and
  skip them (bar casts and `.beelz cast`).
- `lifetime` only changes projectiles and hit areas, never other helper parts of an ability.

## [0.134.0] - 2026-09-23

### Cooldowns now only change for players

- **Per-ability `cooldown` and `cooldownscale` apply to a player's captured bar casts at the moment they cast,
  without editing game data.** Bosses keep their own cooldowns. `cooldownscale` multiplies the cooldown the
  game actually started, so cooldown reduction from gear and buffs still counts. `Grant_MinimumCooldownSeconds`
  works the same way and applies last. `.beelz cast` uses the same rules.
- Abilities that use charges are skipped (tune `charges` / `chargetime` instead). The minimum-cooldown floor
  can only raise a cooldown the game starts; it can't give a cooldown to an ability that has none.

### New tuning knobs

- **`maxstacks`**: how many times the ability's own buff stacks (1–255).
- **`projcount`**: projectiles per volley on fan, multishot and cluster abilities (up to 3× the ability's own
  count, max 16).
- **`knockback`**: knockback distance and push time multiplier (0 = none).
- **`lifetime`**: how long the ability's projectiles and areas last (buff length stays `duration`).
- **`casttime`** (experimental): windup and recovery multiplier. It refuses channels, hold-to-cast and charge
  abilities and says why.
- All of them are shown by `ability-inspect`, protected by the shared-part guard, and restored by `defaults`
  or a restart.
