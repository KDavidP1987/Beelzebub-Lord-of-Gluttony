# Changelog

What's new for players. This is the canonical changelog — it ships on Thunderstore
(bundled with the release) and lives in the repo on GitHub. For the full technical
history, see the [commit log / releases](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/commits/main).

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
- Behind the scenes: removed two unused legacy reset helpers.

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
- BCH: ApiVersion 33 (additive — one new config key, no API line changes).

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

## [0.133.0] - 2026-09-23

### Per-hit damage scaling (off by default)

- **New `Damage_Mode` setting.** `Off` (default) keeps the old short power boost around a captured cast.
  `Telemetry` works out which of your casts caused each hit and counts what per-hit scaling *would* do,
  without changing anything; check it with `.beelz admin damage-stats`. `Scale` applies your `DamageScale` to
  each of those hits directly, with no power window, so nearby attacks aren't boosted by accident.
- Damage keeps following your own Spell/Physical power (level, gear, potions). Boost or nerf with
  `DamageScale`, clamp the result with `Damage_MinFactor` / `Damage_MaxFactor`, and choose whether the flat
  part scales with `Damage_FlatPolicy`. %-of-max-HP damage is never scaled. Only players' captured abilities
  are affected; bosses stay vanilla, and a hit that can't be traced to exactly one cast is left alone.
- **`Summon_PowerMode`**: new servers use `OwnerRelative`, where a captured summon's power follows yours.
  Existing servers stay on `Legacy` (the unit's own stats) until you switch.
- Recommended: play one session in `Telemetry`, check that attribution reads *reliable*, then switch to `Scale`.

## [0.132.0] - 2026-09-23

**Server restart required** (a full restart, not `.beelz admin reload`) — it clears the old
prefab-level `forcetimeout` edits and maps every ability chain.

### Ability tuning is safer and easier to see

- **New `.beelz admin ability-inspect <ability>`** shows every number behind an ability, read-only: cooldown,
  cast time, charges, range, projectile speed/range, how long effects last, AoE and hit radius, **damage
  factors**, heals, buff duration/stacks, knockback, max stacks and projectile/minion counts. It also shows
  which parts are **shared with other abilities or bosses**, and warns about flat or %-of-HP damage.
  `.beelz admin ability-inspect export` writes every ability to `inspect_export.csv` in the Beelzebub config
  folder.
- **Tuning no longer leaks into other abilities.** Many boss abilities share projectiles, areas or buffs. A
  tuned value (cooldown, range, AoE, projectile speed, duration, healing and so on) is now **skipped on any
  part another ability also uses**, unless every ability that uses it asks for the same value. Admins can
  opt in with `allowglobalsharededit on`, which logs every other ability it changes.
- **`forcetimeout` now only affects players.** It no longer changes game data, so bosses keep their own
  effects. When a player casts the captured ability, their own never-ending buff from it is removed after
  the set time.
- **`leapheight` and field aliases (`cd`, `radius`, `interrupt`, …) now apply immediately**, not only
  after a restart. `tune` and `ability` share one field list, and out-of-range or non-number values are
  rejected with the allowed range.

### Weapon and form restrictions fixed

- **`forms !Mounted`-style blocks now work everywhere.** Blocked forms are no longer shown as *allowed*,
  and a form's fallback abilities now respect `enabled` and the form lock.
- **Block-only weapon lists work** (`weapons !Sword` = any weapon except swords). `DualHammers` is rejected
  with a message, since it isn't a real weapon family.
- **`.beelz cast` now honors the ability's weapon and form restrictions**, the same way the spell bar does.

### Crash-safety re-blocks (from tester reports)

- **Re-blocked 4 abilities that were unblocked in 0.129:** Morgana Swarm and Orb Barrage (server crash when
  chained together), and Leandra ShadowStep and TrippleBolt (killed the caster; minions never despawned).
  Existing servers still need their `ability_rules.json` re-seeded to pick up the data-side block, but the
  code-side block applies right away.

### For BloodCraftHub

- API version **29**: `api info` adds `form_blocks=` / `weapon_blocks=`, and `forms=` now lists only the
  allowed forms.

## [0.131.0] - 2026-06-05

### New recovery command: cleanse stuck states

- **`.beelz admin cleanse <player> [buffNameOrGuid]`** — strips stuck STATE buffs from a player. Fixes a
  character left **invisible / phased / immaterial** after casting an ability that won't clear with respawn
  or relog. With no buff argument it removes the known culprits (HideCorpse / Corpse / Invisible / Immaterial
  / Camouflage / Stealth); pass a buff name-substring or ID to strip a specific one. Pair with
  `.beelz admin buffs <player>` to identify the exact buff first.
