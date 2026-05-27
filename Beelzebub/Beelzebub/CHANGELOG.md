# Changelog

What's new for players. This is the canonical changelog — it ships on Thunderstore
(bundled with the release) and lives in the repo on GitHub. For the full technical
history, see the [commit log / releases](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/commits/main).

## [0.45.0] - 2026-05-27

### Summons work without transforming + clearer loadouts

- **Summon abilities now work in normal form.** Captured summon abilities (Raise-Dead,
  Reinforcements, skeleton/bat callers, and the like) cast while you are **not** transformed now
  spawn proper player-allied minions that follow you, fight alongside you, respect the
  per-ability summon cap, and clean up on death / logout — exactly like summons cast while
  transformed. Previously they only behaved mid-transform, so casting one in normal form could
  leave an untracked or hostile spawn.
- **`.beelz summons` works untransformed**, with a new **`clear`** action to despawn all your
  summons at once (`.beelz summons clear`). Stash / restore (waygate-safe) work the same way.
- **New `.beelz loadouts`** — at a glance, see your universal "basic" set, each per-weapon set,
  and which one is active for the weapon you're holding. The per-weapon loadout system already
  existed (`.beelz weapon-grant <weapon|auto> <slot> <index>`); this just makes it easy to see.
  A per-weapon set overrides your universal set on its slots, swapping weapons switches sets
  automatically, and unarmed / spellcasting is its own weapon "family".

## [0.44.0] - 2026-05-26

### The mod is now about COLLECTING ABILITIES — "Devour" replaces unit transformation

Beelzebub's focus is now squarely on **stealing abilities and stacking them on your bar**. The
big change to how the mod works:

- **"Devour" — the new jackpot.** Every kill still has a small chance to capture **one** of a
  unit's abilities. On a rare jackpot roll you now **Devour** the unit instead: you learn **ALL**
  of its eligible abilities at once ("⭐ DEVOURED Foulrot — learned all 6 of its abilities!").
  Slot whichever you like with `.beelz grant`. This replaces the old "unlock a transform into the
  unit" jackpot for every unit that can't actually be rendered.
- **Transformation is now Dracula & Morgana only.** Those are the only two units the game can
  actually render you as, so they remain true transformations (`.beelz transform` / `revert`,
  with their phases, summons, and detonations intact). **Becoming any other unit has been moved to
  a researched, postponed "phase two"** — it needs a future client-side companion mod to render
  the model, which the server alone cannot do.
- **Your collection is safe.** If you'd previously unlocked transforms into other units, those are
  automatically converted on first load: you simply **keep all of those units' abilities** (granted
  to your pool via Devour). Nothing you earned is lost. Dracula/Morgana transforms are preserved.
- **New admin command `.beelz admin devour <player> <unitGuid>`** — grant a player a unit's entire
  ability kit in one go (the admin alternative to the old force-transform for non-boss units).
- Drop rates retuned: per-ability capture stays ~5%, the Devour jackpot defaults to ~0.25% (both
  admin-configurable).
- **Pity now survives restarts.** Your bad-luck-protection streak (the bonus that builds on dry
  kills) is saved with the rest of your data, so a server reboot no longer resets your progress
  toward the next drop.
- **Per-ability cooldown tuning is live for on-demand casts.** Admins can set a `CooldownScale` per
  ability in the rules file; it now applies to `.beelz cast` (the expanded-hotkey action bar), so a
  powerful captured ability can be given a longer on-demand cooldown.

> **Why the change?** A server-side mod can't render your character as an arbitrary creature — the
> game decides your model on the client. Rather than ship "transformations" that didn't visually do
> anything for most units, the mod now leans into what it does brilliantly: a deep
> ability-collection game. Full creature transformation remains on the roadmap as a phase-two
> feature pending a client-side renderer.

## [0.43.23] - 2026-05-26

### Friendly names, a safer `.beelz clear`, and grouped help

- **Capture notifications now show in-game names.** When you capture an ability or unlock a
  transformation, the chat message uses the unit's and ability's friendly in-game name (e.g.
  "Kodiak the Bear") instead of the raw asset id — matching what `.beelz list` and
  `.beelz transforms` already show.
- **`.beelz clear` now asks for confirmation.** Typing `.beelz clear` shows exactly what would be
  wiped (your captures, transform unlocks, slot binds, hotkeys, presets) and points you to
  `.beelz resetbar` if you only meant to reset your action bar. To actually wipe everything you now
  type `.beelz clear CONFIRM` — no more nuking your collection with one mistyped command.
- **Grouped, discoverable help.** Every command is reachable from chat: `.beelz commands` is now a
  complete, sectioned list, and each command group has its own help —
  `.beelz admin help`, `.beelz api help`, and `.beelz hotkey help` — so you can drill into just the
  commands you care about.

## [0.43.22] - 2026-05-26

### Recovery commands in the help list + admin recovery guide

- The recovery commands now appear in `.beelz commands`, and `.beelz resetbar` is listed for
  players. Added a **"Repair a Stuck Player"** admin guide (`docs/RECOVERY_GUIDE.md`) covering
  the full toolkit and recommended flow. No gameplay changes.

## [0.43.21] - 2026-05-26

### Reset a character from the server — `.beelz admin reset-character`

- **New admin command `.beelz admin reset-character <player> CONFIRM-RESET`.** Cleanly resets a
  player's character: it unbinds their Steam ID and kicks them, so on next login they create a
  brand-new character — no second mod required. Their Beelzebub collection is preserved (it's
  tied to their Steam account), and it pairs with `copy/paste-collection` for a full
  recover-from-corruption flow. The old body remains in the world but unplayable. Confirmation
  token required since it's a character-level action.

## [0.43.20] - 2026-05-26

### Back up & transfer a player's collection — `.beelz admin copy/paste-collection`

- **New admin commands `.beelz admin copy-collection <player>` and `.beelz admin
  paste-collection <player>`.** Copy a player's captured abilities + transform unlocks to an
  admin clipboard, then paste them onto any character — a fast way to back up a collection
  before a character re-roll (or transfer it to another player). Pasting is additive and skips
  duplicates. *(Note: your collection is tied to your Steam account, not the character, so a
  normal re-roll keeps it automatically — this is for backups, transfers, and restoring after
  a `.beelz clear`.)*

## [0.43.19] - 2026-05-26

### Safe ability-slot re-sync — `.beelz admin rebuildslots`

- **`.beelz admin rebuildslots [player]` is now safe and non-destructive.** It re-syncs a
  character's active ability slots back to their stored base abilities using the game's own
  slot-setter — repairing a bar stuck on creature/shapeshift abilities by writing *values*, never
  destroying slot entities. (The previous build's version destroyed the slot entities, which could
  crash the server on the player's next login — that approach is removed.)

## [0.43.18] - 2026-05-26

### Last-resort ability-bar rebuild — `.beelz admin rebuildslots` (withdrawn)

- *(Withdrawn in 0.43.19 — the entity-destruction approach could crash the server on relog.)*

## [0.43.17] - 2026-05-26

### Recovery for an already-stuck ability bar — `.beelz admin clearslotmods`

- **New admin command `.beelz admin clearslotmods [player]`** repairs a character whose ability
  bar got frozen on a creature kit by the old transform bug. It clears the orphaned ability-slot
  modifications left behind in the game's modification system and forces the slots to rebuild
  from your real abilities — reaching the deep engine state that resets, respawns, and even
  wiping your data couldn't touch.

## [0.43.16] - 2026-05-26

### Root-cause fix: logging out while transformed no longer corrupts your bar

- **Fixed the cause of the stuck/frozen ability bar.** If you logged out (or dropped) while
  transformed, the game's ungraceful removal of the form on disconnect could leave the
  creature's abilities permanently welded to your action bar — surviving relogs, resets, even
  a respawn. Beelzebub now tears the transform down **cleanly the moment you disconnect**, so
  the game properly removes those abilities before your character is saved. Your transform is
  still kept for the reconnect grace window and re-applied when you return. New transforms are
  protected going forward; a character already stuck from the old bug is a separate recovery.

## [0.43.15] - 2026-05-26

### Definitive fix for a stuck/frozen ability bar — `.beelz admin respawn`

- **New admin command `.beelz admin respawn [player]`** rebuilds a player's character on the
  spot using the game's own respawn, giving them a fresh, clean ability bar. This is the
  reliable cure for a bar frozen on a creature kit after a shapeshift was left ungracefully
  (e.g. logging out mid-form) — the kind of stuck bar that ignores `resetbar`, weapon swaps,
  and even relogging. Inventory, equipment, blood, and progress are preserved (same as a
  normal death + respawn). `.beelz resetbar` points you here if your bar is stuck this way.
- Backed out the previous slot-clearing/reinit reset attempts that couldn't reach the
  stuck state (and produced console noise); `resetbar` keeps the binding/buff cleanup.

## [0.43.13] - 2026-05-26

### `resetbar` now force-clears a frozen ability bar

- **`.beelz resetbar` now authoritatively resets your ability slots.** A transformation
  applies its abilities at high priority; if those stuck in your bar's resolved state after
  the transform ended, your normal weapon/spell abilities (lower priority) couldn't overwrite
  them — leaving the bar frozen on creature abilities and ignoring spellbook/weapon changes.
  `resetbar` now clears the resolved slots directly through the game's own slot system and
  pushes the change to your client. If any slots look empty afterward, re-equip your weapon
  and re-slot your spells to repopulate them.
- Also clears orphaned ability-override sources (by ownership) and, for admins, `.beelz admin
  buffs` reports your entity type, equipped-ability catalog, and every override source you own.
- *(Admins)* `.beelz admin buffs` now also reports your character's entity type, persistent
  equipped-ability catalog, and **every ability-slot override source that belongs to you**
  — the full picture for diagnosing a stuck bar.

## [0.43.9] - 2026-05-26

### `resetbar` now clears a stuck creature/shapeshift bar too

- **`.beelz resetbar` is more thorough.** It now scans your *live* buffs directly and
  strips any lingering transform, creature/shapeshift, or boss-form buff that was holding
  your action bar hostage — not just the bindings it knew about. If a transform ever
  leaves you wearing a creature's abilities, this fully returns you to your vampire bar.
- *(Admins)* New diagnostic `.beelz admin buffs [player]` dumps a player's active buffs
  and which ones override ability slots to the server log — for tracking down a stuck bar.

## [0.43.8] - 2026-05-26

### Reset your bar to normal in one command

- **New: `.beelz resetbar`.** Ends any active transformation and removes **all** your
  Beelzebub slot bindings at once, snapping your action bar back to its normal vampire
  state (your spells + weapon skills). Handy if you've granted boss abilities to your
  slots and want your original bar back — previously you had to clear each slot with
  `.beelz unslot` one at a time. Your captured abilities and transform unlocks are kept;
  re-grant anything with `.beelz grant`.

## [0.43.7] - 2026-05-26

### No more getting stuck after a disconnect

- **Fixed: logging out while transformed could leave you "stuck."** You could come back
  to a frozen ability bar locked to the transformed unit — unable to revert, unable to
  grant abilities to your bar, with new transforms not taking effect. Beelzebub now
  **reconciles your state every time you log in**: any leftover transform from a previous
  session (including one stranded by a server restart) is cleared and your normal
  abilities are restored. You should never log in to a broken bar again.
- **New: reconnect grace window.** If you disconnect while transformed, your
  transformation and its summons are now **kept for a short window** (default **90s**) so
  an abrupt drop or a quick relog puts you right back where you were — same form, same
  summons. If you don't return in time, the transform reverts and the summons are
  dismissed cleanly. Configurable via `Transform_ReconnectGraceSeconds` (`0` = revert
  immediately on disconnect, `-1` = keep until you manually revert).

## [0.43.6] - 2026-05-24

### Summons keep up with you

- **Summoned allies now scale to you.** A summon used to keep the original boss-add's
  level and damage, so it fell behind as you levelled. Now a summon matches **your
  level** on spawn (`Transform_SummonMatchPlayerLevel`, on by default), with an admin
  power dial (`Transform_SummonPowerFactor`) to make summons hit/tank harder or softer.
  Applies to transform summons and your standalone signature summons alike.

## [0.43.5] - 2026-05-24

### Tune ability power

- **Granted abilities already scale with you** — when you cast a captured boss ability,
  it uses *your* Physical/Spell Power and crit, so it grows with your level, gear, and
  prestige (it isn't frozen at boss-level damage).
- **New admin scaling controls on top:** a global `Grant_PowerScalingMode`
  (`PlayerScaled` default / `Boosted`) with `Grant_PowerScalingFactor`, plus the
  **per-ability `DamageScale`** in the rules file is now actually applied — for nerfing,
  buffing, or rescuing the occasional flat-damage ability.

## [0.43.4] - 2026-05-24

### Keep a boss's summon as your own

- **Unlocking a unit's transform now also teaches you its signature summon** as a
  standalone ability. Slot it or bind it to a hotkey and summon **even when you're not
  transformed**. Applies retroactively to units you've already unlocked. Toggle with
  `Capture_GrantSignatureSummons`.

## [0.43.3] - 2026-05-24

### Call in the adds — `.beelz summon`

- **New `.beelz summon` command.** While transformed, call your unit's signature
  add-summon — the Toad King's frogs, the Werewolf Chieftain's caged wolves, and more —
  the ones bosses normally only trigger at low health. The spawns fight as your allies.
  Cooldown via `Transform_SummonCooldownSeconds`.

## [0.43.2] - 2026-05-24

### Transform stability fixes

- **Animal forms stay put.** Native shapeshift forms (frog/toad, wolf, bear, …) no
  longer flicker out the instant you cast or take a hit.
- **Revert always works.** Fixed a case where reverting reported success but left your
  spell bar unchanged.
- **Log out safely while transformed.** Disconnecting now returns you to your base form
  on next login instead of leaving you in a half-transformed state.

## [0.43.1] - 2026-05-24

### Morgana fixed + reworked (critical)

- **Fixed a server crash when transforming into Morgana.** Her serpent form could
  abort the server on transform; that's resolved.
- **Morgana is now a two-stage serpent**, like Dracula's switchable kits: a ranged
  **Spectral** kit and a melee **Serpent** kit, swapped with `.beelz phase 1/2`. (A
  humanoid first stage isn't possible — V Rising can't render her human model on a
  player — so both stages use her serpent form.)

## [0.43.0] - 2026-05-24

### First public test release 🎉

Beelzebub is going out for **server testing** — this is an early alpha/beta build.
See the mod page for the full testing disclaimer, known limitations, and roadmap.
Built on the shoulders of the V Rising modding community — special thanks to
**Bloodcraft** by zfolmt, whose ability-grant, familiar/ally, and ExoForm patterns
informed several of Beelzebub's systems.

### For companion-app developers (BloodCraftHub)

- `.beelz api transform-config` now reports the **shard-boss** category too
  (`src=S`, alongside `src=R`/`src=V`) so a client can read all three transform
  mode/duration/cooldown profiles in one call.

## [0.42.0] - 2026-05-24

### Summons handle horse-riding your way

When you're transformed with summons fighting for you and you **hop on a horse**,
admins now choose what happens via the new `Transform_MountedSummonMode` setting:

- **Stash** (default) — your summons are tucked away the moment you mount and
  brought right back when you dismount, so they don't scramble after a galloping
  horse.
- **Follow** — your summons stay in the world, leash to you on the mount, and keep
  engaging in combat as you ride.

Either way, **dismounting now restores your transformation's ability bar** instead
of leaving you on the horse's default abilities — the same fix we shipped for
reverting, bat/waygate travel, and combat shapeshifts.

## [0.41.1] - 2026-05-24

### Combat shapeshifts no longer drop your transform kit

Following up on 0.41.0: leaving a **combat shapeshift** (wolf, bear, etc.) while
transformed now **automatically** re-applies your transformation's abilities — same
as bat/waygate travel already did. `.beelz refresh` remains as a manual fallback.

## [0.41.0] - 2026-05-24

### Your ability bar no longer vanishes

Fixed the cases where your spell bar could go blank or lose abilities:

- **After reverting a transformation**, your custom-bound abilities now come back
  immediately — no more empty bar, and no need to swap a weapon to get them back.
- **After flying as a bat / leaving a wolf or other form while still transformed**,
  your transformation's abilities are re-applied on arrival instead of dropping to
  your weapon's defaults.
- **Per-weapon loadouts** (set with `.beelz weapon-grant`) keep working as you swap
  weapons and are now restored reliably alongside the above.

And a safety net for anything we missed: **`.beelz refresh`** re-applies your correct
bar on demand — whether you're transformed or not.

## [0.40.0] - 2026-05-24

### Cast abilities beyond your 6 slots — an expanded action bar

You're no longer limited to six abilities. Bind any captured ability to a named
hotkey (`.beelz hotkey set <name> <index>`) and **fire it on demand with
`.beelz cast <name>`** (or, soon, a BloodCraftHub button). Each cast respects the
ability's own cooldown, so it's not spammable. Admins control how many hotkeys a
player can have with `Hotkeys_MaxPerPlayer`, and can switch the feature off with
`Hotkeys_Enabled`.

This also answers the "I can't put an ability on my ultimate slot" problem: the
ultimate slot can't be rebound on the normal action bar, but `.beelz cast` doesn't
need a slot at all — so any captured ability (ultimates included) can be fired this
way. (Abilities whose animations are tied to a specific boss's body still read best
while transformed into that boss.)

## [0.39.0] - 2026-05-24

### Shard bosses, V-Blood filters, and admin/config plumbing

- **Shard bosses are their own category.** Dracula, Adam the Firstborn, Solarus the
  Immaculate, The Winged Horror (Talzur), Megara the Serpent Queen, and Gorecrusher the
  Behemoth (configurable via `Transform_ShardBossNames`) now have their
  own transform mode/duration/cooldown (`Transform_*_ShardBoss`) and a separate
  cooldown bucket — so you can, say, time-limit the powerful shard-boss forms while
  leaving other V-Bloods as toggles, and a shard cooldown won't block regular V-Blood transforms.
- **Filter your lists.** `.beelz transforms vblood` / `.beelz list vblood` (also
  `shard` / `regular`) show a shorter, focused list. The transforms list also now
  splits shard bosses into their own section.

### Admin & companion-app plumbing
- `.beelz admin set <key> <value>` — change any setting live (persists to the
  config), instead of editing the file + restarting.
- New BCH-facing reads: `.beelz api config` (all settings), `.beelz api cooldowns`
  (per-category cooldown timers), a `shard` flag on transform/catalog data, and a
  `config-changed` event. (API version 3.)

## [0.38.0] - 2026-05-23

### Bad-luck protection, Morgana's second form, and friendlier names

- **Bad-luck protection (pity):** every kill that drops nothing now nudges your
  odds up a little (default +0.25% per kill, admin-configurable), resetting to
  baseline the moment you get a drop — so a cold streak can't last forever. Ability
  and transform luck are tracked separately, per source (regular vs V-Blood).
- **Morgana now has two switchable forms** like Dracula: a melee "Serpent" kit and
  a ranged "Spectral" kit — swap with `.beelz phase 1/2`. (The Spectral kit is new
  and experimental; some of her scripted spells may need tuning.)
- **Friendlier names everywhere:** `.beelz search`, the "forgot" messages, and the
  admin grant/revoke/transform replies now show proper in-game names instead of
  raw asset names (search still matches either).

### Behind the scenes
- The manual-detonation registry now documents the boss-nova expansion candidates
  to verify (Dracula CrimsonNova, Cardinal LightNova, Gloomrot ImplodingOrb, …).
- Added `docs/CHAIN_AUDIT.md` — the chained-ability test plan for a tracing session.

## [0.37.0] - 2026-05-23

### Bestiary — your collection book

A new way to see your collection at a glance. **`.beelz bestiary`** lists every
unit you've collected from, showing how many of its abilities you've captured
(e.g. 3/7), whether you've unlocked its transform, and whether it's a V-Blood —
with a "complete" marker once you've collected everything from it.

**`.beelz bestiary unit <name>`** drills into a single unit and lists each of its
abilities with a ✓ (collected) or · (still to find), so you know exactly what's
left to hunt. The companion app can read the same data via `.beelz api bestiary`.

## [0.36.0] - 2026-05-23

### Fire boss AoEs on demand: `.beelz detonate`

Some boss abilities only unleash their big area-of-effect blast under a specific
condition — the Undead Priest, for example, only sets off its projectile Nova at
the end of a teleport. Now, while transformed into a unit that has one of these
signature blasts, you can trigger it yourself any time with **`.beelz detonate`**.

It fires the real AoE at your position, on your team, so it actually hits enemies.
There's a short cooldown (admin-configurable via `Transform_ManualDetonateCooldownSeconds`,
default 5s) to keep it from being spammed. Right now the Undead Priest's Nova is
wired up; more bosses can be added over time. (A natural fit for a hotkey button
in a future companion app.)

## [0.35.0] - 2026-05-23

### Richer ability info + a more complete companion-app feed

Groundwork for the BloodCraftHub companion app — mostly invisible in normal play:

- `.beelz` ability info now reports the real ability description, its magic
  school, base cooldown, and which weapon its animation belongs to.
- The companion-app event feed is now complete: it also fires when you forget an
  ability, forget a transform, or wipe your collection, and it now reliably
  reports *every* way a transform ends (manual revert, switching forms, an admin
  ending it, or a server-wide revert) — so the app's display never goes stale.

No gameplay changes; existing setups are unaffected.

## [0.34.0] - 2026-05-23

### Wield-the-right-weapon guidance for ability animations

Weapon-based abilities (sword slams, axe swings, spear lunges, and so on) carry
their swing animation baked in — they only *look* right when you're holding that
kind of weapon. Beelzebub now tells you which one:

- Granting a weapon-based ability shows **"✋ Wield <weapon> for the correct
  animation."**
- The transform loadout list tags each weapon-based slot with its weapon (✋), so
  you can pick a matching weapon for the unit you're becoming.

Spells and universal abilities are never tagged — they don't need a weapon.

New admin toggle `Grant_EnforceWeaponMatch` (default **off**): turn it on to make
`.beelz grant` *require* you to bind weapon-based abilities through
`.beelz weapon-grant <weapon>` (which ties them to the right weapon's loadout
automatically), instead of just advising it.

## [0.33.0] - 2026-05-23

### Real shapeshift transforms for animal-form units

Transforming into a unit that matches one of V Rising's real creature forms —
wolves, bears, rats, spiders, toads, werewolves, and the Tailor's gargoyle —
now puts you in that **actual form** (model, rig, and all) and **keeps you
there while you fight**, using the same persistent-form tech that powers the
Dracula and Morgana transforms. Previously these only flickered a cosmetic form
that dropped the instant you cast anything; now the form sticks and your
captured abilities play on it.

Units that don't match a real V Rising form (most humanoids, undead, constructs)
still transform the same as before — spell bar and stats, no model swap — because
the game simply doesn't ship a form for them (a hard engine limit; the only way
to render arbitrary units is a future client-side companion mod).

New admin toggle `Transform_RealFormWhenAvailable` (default **on**) controls this
server-wide; the curated boss forms (Dracula/Morgana) always use their form
regardless. Per-unit control still lives in the transform rules.

## [0.25.0] - 2026-05-23

### Mist Walk AoE now fires — plus a chain diagnostic for testers

The Undead Priest's Mist Walk now detonates its area-of-effect blast at your
teleport destination, the way the boss does. (The game was suppressing the
blast for players because of an internal condition meant for the NPC; we now
trigger it ourselves on arrival, on your team, so it actually hits enemies.)

This also adds a behind-the-scenes **chain tracer** (only active with verbose
logging). When you cast an ability while transformed, it records what that
ability's chain actually produces — so when a boss ability only half-works, we
can pinpoint exactly where it breaks and fix it. Harmless and silent unless you
turn verbose logging on. Great for group testing sessions.

## [0.24.6] - 2026-05-23

### Mistwalk AOE chain — third hook for the proxy spawn

Looked at the full Mistwalk chain in the prefab data and found the
specific missing piece. The chain is:

```
Cast → Travel Phase → Travel End (buff) → ProxySpawner → 12 projectiles
```

The Travel End buff has a `SpawnPrefabOnGameplayEvent` pointing to the
SAME ProxySpawner that the standalone Projectile Nova uses. Our existing
hooks (`ScriptSpawnServer`, `BuffSystem_Spawn_Server`) catch the buff —
but they DON'T catch the ProxySpawner, because it's not a buff and is
spawned by a different V Rising system.

v0.24.6 adds a broader sweep alongside our existing hook that catches
non-buff chain entities. It runs every other tick during combat,
queries entities with EntityOwner + Team (excluding players, summons,
and buffs), and applies the same team/faction fixup if the owner chain
reaches a Beelzebub-transformed player. Already-processed entities are
remembered so the sweep doesn't redo work.

### Test plan

1. Restart server, transform → Undead Priest
2. Use Mistwalk (Space) at an enemy group — expected:
   - You teleport to the location
   - The damage burst now visible/effective at destination (AOE finally fires)
3. Use the C-key ultimate Nova (this already worked) — still should work
4. With verbose logging, look for `[Beelz CHAIN] fixup` lines — should
   show ProxySpawner-class entities being processed by the sweep now,
   not just buffs

### If it works

Other AOE/chained boss abilities should also start working — this hook
isn't Priest-specific, it catches any chain entity owned by a
transformed player.

### If it still doesn't fire

The chain may go even deeper than ProxySpawner, OR the team-rewrite is
happening AFTER the projectiles have already inherited the original NPC
team. In that case we'd need to also hook the projectile entities
themselves, OR pre-rewrite the prefab template. Report what you see and
we'll iterate.

## [0.24.5] - 2026-05-23

### Slot mapping now matches V Rising's real keybinds

v0.24.4 used the wrong slot indices — my mental model didn't match the
actual game. User testing exposed the correct layout:

| Slot | Key | Conventional use |
|---|---|---|
| 1 | Q | Primary weapon attack |
| 2 | Space | Travel / teleport |
| 3 | Shift | Usually empty (BCH may override) |
| 4 | E | Secondary weapon attack |
| 5 | R | First spell slot |
| 6 | C | Second spell slot |

The heuristic now places abilities accordingly:

- `_Projectile_Group`, `_MeleeAttack_`, `_Auto_` → **Slot 1 (Q)**
- `_Teleport_`, `_Travel_`, `_Dash_`, `_Leap_`, `_PhaseShift_` → **Slot 2 (Space)**
- `_Charge_`, `_Smash_`, `_Slam_`, `_Heavy_`, `_Cleave_`, `_Strike_` → **Slot 4 (E)**
- `_Hard_`, `_Ultimate_` → **Slot 6 (C)**
- everything else → fills slots 3, 5 (and whatever the heuristic missed) in order

For Undead Priest, transforming now gives you:
- **Q**: Projectile (basic attack)
- **Space**: Teleport Travel (Mistwalk) — where it belongs
- **Shift**: spell fill
- **E**: spell fill
- **R**: spell fill
- **C**: Projectile Nova Hard (the ultimate-equivalent)

### Admin slot template (per-V-Blood override)

Every `TransformMap` entry in `ability_rules.json` now supports a
`SlotTemplate` field. If set, the admin's curated assignment wins over
the heuristic. Example:

```json
{
  "CHAR_Undead_Priest_VBlood": {
    "Enabled": true,
    "Difficulty": "Basic",
    "Tier": 4,
    "SlotTemplate": {
      "1": "AB_Undead_Priest_Elite_Projectile_Group",
      "2": "AB_Undead_Priest_Elite_Teleport_Travel_AbilityGroup",
      "3": "AB_Undead_Priest_Elite_RaiseDead_AbilityGroup",
      "4": "AB_Undead_Priest_Elite_ProjectileNova_AbilityGroup",
      "5": "AB_Undead_Priest_Elite_RaiseHorde_AbilityGroup",
      "6": "AB_Undead_Priest_Elite_ProjectileNova_Hard_AbilityGroup"
    }
  }
}
```

Partial templates work — unspecified slots get filled by the heuristic
from the remaining abilities. Admins can curate the exact loadout per
V-Blood for their server.

### Ultimate slot research deferred

You tried slots 0, 7, 8 — all rejected as invalid via `.beelz grant`.
V Rising's ultimate slot (the R-key shard ultimate in vanilla) appears
to use a different mechanism than `ReplaceAbilityOnSlotBuff`. Will
research and add support in a follow-up release. For now, the boss's
Hard-tier ultimate lands on slot 6 (C key) as the highest available
slot.

### Phase-based abilities

You mentioned bosses with abilities that depend on combat phase. This
is already supported in the codebase (TX5 phase system) — see
`.beelz phase <n>` to switch loadouts mid-transform. Combined with the
new admin SlotTemplate, you can curate distinct loadouts per phase.

## [0.24.4] - 2026-05-23

### Smart slot mapping + broader chain fixup

Two issues from v0.24.3 testing:

**1. Travel + Ultimate land on wrong keys.** Boss's natural ability order
was applied 1:1 to your spell bar — the priest's teleport ended up on
the Spell slot instead of Shift, and the Hard-tier ultimate AOE landed
on a Spell key instead of the Ultimate position. Fixed by classifying
abilities by their name pattern:

- `_Projectile_Group`, `_MeleeAttack_`, `_Auto_` → **Primary** (slot 1)
- `_Teleport_`, `_Travel_`, `_Dash_`, `_Leap_`, `_PhaseShift_` → **Travel/Shift** (slot 4)
- `_Hard_`, `_Ultimate_` → **Ultimate-equivalent** (slot 6)
- everything else fills slots 2, 3, 5 in original order

For Undead Priest, this means:
- Slot 1 (Primary): Projectile (basic)
- Slot 2/3/5 (Spells): Projectile Nova, Raise Dead, Raise Horde (in natural order)
- **Slot 4 (Shift): Teleport Travel (Mistwalk)** — finally on the right key
- **Slot 6 (Ultimate): Projectile Nova Hard** — on the ultimate position

**2. Mistwalk AOE chain didn't fire.** The Travel ability spawns a Phase
entity (during travel) then a Travel_End buff at the destination. The
Travel_End buff was caught by our v0.24.3 engine hook, but maybe at the
wrong stage of V Rising's spawn pipeline. v0.24.4 adds a second hook
on `BuffSystem_Spawn_Server.OnUpdate` that does the same chain-fixup —
two hooks cover the spawn lifecycle at different points. Should mean
more chain entities get their Team/Faction rewritten correctly.

### Test plan

1. Restart server (DLL deployed)
2. Transform → Undead Priest
3. **Check spell bar layout** — chat should show:
   ```
   Primary: Projectile  
   Spell slots: ProjectileNova, RaiseDead, RaiseHorde
   Travel/Shift: Teleport Travel (Mistwalk)
   Ultimate: ProjectileNova Hard
   ```
4. Cast Mistwalk (Shift) — expected: teleport + AOE fires at destination
   (the freeze/damage burst the user reported missing)
5. Cast the Ultimate (R or whatever key your client uses for ult) —
   expected: 360° Nova fires (already worked in v0.24.3)
6. Cast all your spells — expected: all functional

### Diagnostic

With verbose logging on, look for `[Beelz CHAIN][buff-spawn]` log lines
— shows the new BuffSpawnServer fixup firing. Complements the existing
`[Beelz CHAIN] fixup` from ScriptSpawnServerPatch.

## [0.24.3] - 2026-05-23

### Attempt to fix AOE/projectile abilities (engine hook)

You pushed back on warning-as-fix for the Priest Projectile Nova. Fair —
Bloodcraft makes familiars cast NPC abilities successfully, so the
technique exists. This release adds an engine-level hook that attempts
to apply the same fix to player-cast abilities.

### What changed

V Rising's NPC ability chains carry hard-coded NPC team info on every
spawned entity (ProxySpawner, projectiles, etc.). When a transformed
player casts a boss ability, the chain spawns entities stamped with
`Team=1` (NPC) and `TeamReference` pointing to NPC team singleton.
Collision/hit filters then say "skip my own team" — and miss the actual
enemies (who are also Team=1).

The new patch (`ScriptSpawnServerPatch`) watches every spawned entity
during the chain. For entities whose owner traces back to a Beelzebub-
transformed player, it rewrites:
- `Team` to match the player's team
- `TeamReference` to match the player's team reference  
- `FactionReference` to `Faction_Players`
- `EntityOwner` to point at the player directly

Same component setup Bloodcraft uses for combat familiars
(FamiliarBindingSystem.cs:413-415) — applied to chain entities instead
of the familiar itself.

### What this should fix

- **Priest Projectile Nova (basic + Hard)** — the 360° AOE that fires
  12 projectiles. Previously projectiles landed but did no damage.
- **Bishop TrippleBolt and similar chained projectiles** — owner-chain
  loss class.
- **Any other AOE/projectile boss ability** with the same chain pattern.

The `⚠ INCOMPATIBLE` warning was removed from these (the fix is being
attempted). Try and report whether they now hit enemies.

### What this will NOT fix

The 5 abilities still tagged Incompatible are bound to NPC animation
rigs — the BOSS skeleton has bones named `hand_R` etc. that the chain
references; the player skeleton doesn't have those bones. The animation
doesn't play; no hook can re-rig the player's model. These remain tagged:

- Bishop Eternal Darkness
- Bishop Shadow Step  
- Priest Teleport Travel (and other `_Teleport_Travel_` variants)

### Test plan

1. Restart server.
2. Transform → Undead Priest.
3. Cast Projectile Nova. Expected: projectiles fire AND hit nearby
   enemies (not players, not your summons).
4. Same for Projectile Nova (Hard) on the Ultimate slot.
5. Try Bishop's TrippleBolt if you have that transform.
6. If you see `[Beelz CHAIN] fixup` in `LogOutput.log` (with verbose
   logging on), the hook is firing.

### If it doesn't work

Engine-hook approaches are experimental — V Rising's IL2CPP entity
pipeline has many edge cases. If the hook doesn't catch the spawned
proxies (because they go through a different system than ScriptSpawn
Server), we may need to add additional hooks. Report what you observe
and we'll iterate.

## [0.24.2] - 2026-05-23

### Priest fixes + known-broken-ability warnings

**1. Priest RaiseDead now works (#93).** Previously: casting RaiseDead
on a transformed player produced nothing. Reason: V Rising's RaiseDead
chain animates EXISTING corpses via an area-of-effect detector. When
the player casts it, there are typically no corpses in range → silence.
Fix: added explicit summon-target entries so RaiseDead manually spawns
2 skeleton soldiers at the player's position, same way Bishop's
ShadowSoldier and Stonebreaker's Reinforcement work.

**2. "⚠ INCOMPATIBLE" warnings on broken abilities.** Some V-Blood
abilities can't fire correctly when cast by a player — most commonly
because they're bound to NPC-specific animation rigs (Bishop's Shadow
Step, Eternal Darkness; Priest's Teleport Travel) or because they use
AOE proxy-spawner patterns that fire projectiles on the NPC team rather
than the player's team (Priest's Projectile Nova). 

These now show a warning when you look them up or transform into a unit
with them:

```
Priest Of Shadows Projectile Nova [Other / ⚠ INCOMPATIBLE]
⚠ This ability is known to misbehave when cast by a player
  (AOE-Proxy: fires 360° projectile nova but team check makes them miss enemies).
```

Initial curation tags 8 known-broken abilities. Admins can flag more
(or unflag) via `ability_metadata_overrides.json` using the new
`incompatible: true` + `incompatibleReason: "..."` fields.

**3. Audit support.** `.beelz admin scan-abilities` now reports an
`incompatible` count and includes the reason in the discovered-
abilities.json output, so admins can see at a glance which abilities
need engine-side fixes vs admin curation.

### Why these abilities are broken

These are V Rising engine-level limitations:
- **AnimRig** — boss dash/phase abilities reference specific bones on
  the NPC skeleton (`hand_R`, etc.). Player skeleton doesn't have them.
- **AOE-Proxy** — ProxySpawner entities inherit `Team = 1` (NPCs),
  causing fan-projectiles to miss player-allied targets.
- **OwnerChain** — multi-step chains lose the owner reference on
  intermediate spawn entities.

A future engine-level Harmony hook (planned for v0.25.x) can resolve
AOE-Proxy and OwnerChain cases. AnimRig is likely permanent — the
client doesn't render those animations on the player skeleton anyway.

## [0.24.1] - 2026-05-23

### Ability info surfaces in-game now

v0.24.0 shipped a 1,813-entry ability database but the data was only
reachable via `.beelz info <name>` — the action bar tooltip stayed
blank for V-Blood / NPC abilities. That's a hard V Rising client-side
limitation (server mods can't modify what the client renders in
tooltips). v0.24.1 routes around it with two chat-based access paths:

**1. Auto-chat on transform.** When you transform into a V-Blood, the
mod immediately sends a multi-line summary of your new spell bar:

```
--- Bishop Of Shadows spell bar ---
  Primary: Bishop Primary Attack · cd 0.0s
  Q-Weapon: Bishop Shadow Step [Other] · cd 8.0s
  Spell 1: Bishop Shadow Soldier [Other] · cd 30.0s
  Spell 2: Bishop Eternal Darkness [Other] · cd 45.0s
  Ultimate: Bishop Shadow Dance [Other] · cd 90.0s
Use .beelz active to re-display this anytime. .beelz info <name> for full details.
```

Descriptions show up on the next line for abilities that have them (the
~238 player-castable spells from gaming.tools). For NPC-only abilities
(most V-Blood kits), you get name + school + cooldown — enough to know
what's on each slot at a glance.

**2. `.beelz active` command.** Re-displays your current spell bar with
the same info, anytime. Works whether you're transformed (shows the
transform's loadout) or not (shows your universal slot bindings).

### Why this matters

V Rising renders action bar tooltips entirely client-side from the
client's local prefab data + StreamingAssets localization. Stunlock
shipped localized text for player-castable spells but mostly not for
NPC/V-Blood abilities. The server can send the player a chat message —
it can't repaint a UI element on their screen. So chat-based access is
the real fix server-side.

A proper in-tooltip rendering would require a client-side companion
mod (BloodCraftHub integration, planned long-term).

## [0.24.0] - 2026-05-23

### Ability info finally works

Until now, the only display for a captured ability was the raw prefab
name — e.g., `AB_Undead_Priest_Elite_RaiseHorde_AbilityGroup`. This
release adds a comprehensive ability database with proper titles,
descriptions, schools, types, source NPCs, cooldowns, cast times, and
ranges.

### New: `.beelz info <index|name>`

Players can now look up any ability:

```
.beelz info 0          (by your .beelz list index)
.beelz info raise hor  (by name substring)
.beelz info bloodrite  (any ability, even ones you haven't captured)
```

Returns the full report:
- Title + school/type tags
- Description with damage and duration values filled in
- Cooldown, cast time, range, behavior (Instant / Channeling / etc.)
- Source NPCs (who drops this ability)
- Provenance (shipped / admin-override / fallback)

### Architecture

Two separate databases keep concerns clean:

- `ability_metadata.json` (embedded with the mod) — **ability reference
  info**: name, description, school, type, source NPCs, icon. Sourced
  from a one-time scrape of vrising.gaming.tools/abilities + processed
  into the mod's schema. Never makes HTTP calls at runtime.

- `ability_rules.json` (per-server) — **admin policy**: Enabled/Disabled,
  Transform-Only flags, DamageScale overrides, drop-rate overrides.
  Unchanged from prior releases.

- `ability_metadata_overrides.json` (per-server, optional) — **admin
  metadata overrides**. If you want to customize a description or
  re-name an ability for your server, drop entries here. Wins over
  the shipped data.

### New admin tool: `.beelz admin scan-abilities`

Walks every ability the mod knows about (captures across all players +
every `AB_*_AbilityGroup` in the game), resolves metadata + runtime
stats for each, and writes a full audit report to `config/kdpen.Beelzebub/
discovered_abilities.json`. Use it to find abilities lacking curated
descriptions on your server.

### Why some abilities still say "no description"

The initial scrape covers V Rising's main 1,439 ability database from
vrising.gaming.tools. Some prefabs (especially internal cast/buff
entities and modded additions) won't have curated descriptions. The
in-chat output gracefully falls back to a humanized prefab name + the
ECS-derived stats (cooldown, cast time, range, behavior).

If a description is missing for an ability you care about, you can
add it via `ability_metadata_overrides.json` — same schema as the
shipped file.

## [0.23.19] - 2026-05-23

### Horde unison — the whole horde now engages together

v0.23.18 fixed defensive aggro (horde reacts when one member is hit),
but two issues remained:

1. **Player attacking didn't trigger horde response.** The horde just
   stood there while the player fought.
2. **Per-group reaction on damage.** Only the cluster near the attacked
   unit engaged — the rest of the horde stayed put.

Both came from the same root cause: V Rising's combat AI needs more than
just "an enemy in your aggro buffer" to make a unit engage. It also needs:
* The unit's combat-anchor position (used for "am I in my zone?" checks)
* The unit to be physically close enough to reach the target
* The unit to be in combat mode (not leash mode)

We only had the first two pieces partially set. Now any combat trigger —
player entering combat, player dealing damage, or any summon taking
damage — runs a full **horde re-prime**:

* Combat anchor updated to current player position
* Every summon teleported back to player if beyond leash radius
* Every summon's aggro buffer re-seeded from your current targets
* Every summon set to combat mode + Combat behavior state

Throttled to once per second per player so the re-prime doesn't fire
expensive mutations every damage tick.

### Net effect

Player initiates combat → whole horde pulled in and engages immediately.
One summon gets hit somewhere → whole horde pulled to that fight.
Walking away mid-combat → leash pulls them back the next time combat
triggers.

### Configurable

`Transform_SummonLeashRadius` (default 30) controls how close the leash
keeps the horde to you. Lower = tighter formation, more aggressive
pull-ins. Higher = more freedom to wander.

### Test plan

1. Transform → Undead Priest. Cast Raise Horde 1-3 times.
2. Walk to an enemy group. Player attacks first.
   **Expected**: whole horde engages.
3. Move during combat. Some summons follow, others lag behind.
   **Expected**: next combat trigger pulls all of them in.
4. Test with multiple cast groups (3 hordes worth of skeletons).
   **Expected**: all clusters engage together when any combat trigger fires.

## [0.23.18] - 2026-05-23

### Horde combat AI — final fix

The 3-cast summon cap is verified working. This release fixes the
remaining issue: hordes engaging briefly then disengaging during combat.

**Root cause**: V Rising's combat AI does NOT auto-inject attackers into
the AggroBuffer of player-allied units (it reserves that auto-injection
for free NPCs only). When your horde took damage, nothing pushed the
attacker into their target list — so they reacted for one tick from
residual aggro, then went idle.

**Fix**: a new patch that listens for any damage event server-wide and,
if the damaged entity is one of your summons, broadcasts the attacker
into the EVERY summon's aggro buffer. Hit one priest skeleton, the whole
horde swarms the attacker. Plus an explicit combat-mode flip on the
struck unit so leash-mode summons immediately become combat-ready.

Combined with the existing patches (player→enemy damage broadcast,
PvE-combat-buff mode toggle, Return/Idle interception), the horde now
maintains continuous engagement through extended fights.

### Test plan

1. Transform → Undead Priest. Cast Raise Horde once.
2. Walk near an enemy group. Confirm horde engages when player attacks.
3. Let one skeleton get hit by an enemy. **Expected**: the whole horde
   immediately turns on the attacker.
4. Walk away mid-combat, let enemies follow. Confirm horde stays engaged.
5. Test with other summon abilities — Bishop ShadowSoldiers, Stonebreaker
   Reinforcements — same engagement behavior.

## [0.23.17] - 2026-05-22

### Two fixes for v0.23.16 regressions

**1. 3rd summon cast no longer silently fails.** v0.23.16 fixed the cap
not enforcing — but introduced a subtle bug where the 3rd cast (cap=3)
got admitted at cast-start, then had its own spawns destroyed before they
could appear in-world. From the player's view: 1st and 2nd casts produced
horde; 3rd produced nothing, no error message; 4th was never reached.
Fixed: the cap accounting now uses different semantics at "cast start"
(empty groups count, so rapid re-casts get refused) vs at "spawn arrival"
(only populated groups count, so the destination group isn't double-
counted against its own spawns).

**2. Restored horde engages enemies again.** After waygate teleport,
restored summons stayed in their pre-stash state (typically idle,
since you stashed them at a waygate = out of combat) and never engaged
enemies at the destination. Fixed: restore now explicitly resets each
unit's combat state (BehaviourTreeState = Follow, mode = leash,
Aggroable + AggroConsumer re-enabled) and re-seeds aggro from your
current targets — so any combat already in progress at arrival
immediately propagates to every restored summon.

### Test plan

1. Transform → Undead Priest.
2. Cast Raise Horde 4 times rapidly.
3. Casts 1-3 spawn horde; cast 4 refused with chat message.
4. Engage enemies pre-teleport: confirm all horde members engage.
5. Waygate to a new region. Engage enemies. Confirm same engagement.

## [0.23.16] - 2026-05-22

### Two priest-related fixes

**1. Summon use limit actually works again.** Casting Undead Priest's
Raise Horde / Raise Dead while transformed used to let you summon
endlessly, ignoring the 3-cast cap. The priest channels for ~2 seconds
before V Rising spawns the skeletons, which fell outside our 3-second
attribution window — so the cap thought no summons existed and let you
keep casting. Fixed by extending the window to 10 seconds and adding
empty-cast-group tracking so a freshly-opened group counts against the
cap even before any skeletons appear. Three Raise Horde casts now hits
3/3 uses correctly; the 4th is refused until existing summons die.

**2. Horde combat AI no longer inconsistent.** Some Priest summons
engaged when struck, others stood idle even while being hit. Two missing
pieces compared to Bloodcraft's familiar pattern:

- Some V Rising unit prefabs (notably graveyard-spawned skeletons) ship
  with `Aggroable` disabled — they literally cannot acquire targets.
  Setup now enables `Aggroable` + `AggroConsumer` on every summon so the
  unit can participate in combat at all.
- When a unit transitioned to `Return` (gave up, walking home) or
  `Idle` (no target), it got stuck. A new patch intercepts those state
  changes for tracked summons and force-flips them back to `Follow` —
  same trick Bloodcraft uses for stranded familiars.

Result: every Priest minion in a horde now engages whoever attacks the
player, instead of a random subset of them.

### For admins

Enable `VerboseLogging = true` in `BepInEx/config/kdpen.Beelzebub.cfg`
to log the full summon lifecycle to `LogOutput.log` — useful when
reporting summon-related bugs.

## [0.23.0] - 2026-05-22

### Summons that actually fight + stack limits + teleport-follow

Five fixes addressing v0.22.1 test feedback ("summons spawned but stood
around, kept casting forever, didn't despawn on revert, couldn't waygate").

**1. Combat AI** — summons now actively engage the player's enemies instead
of standing around. Added the missing components Bloodcraft uses on its
familiars (BlockFeedBuff marker; zeroed AlertModifiers / AggroModifiers /
GainAggroByVicinity / GainAlertByVicinity to disable broken proximity
detection); plus a periodic aggro-buffer injection that pushes the player's
active enemies into each summon's combat queue ~3x/sec. The proximity zero
+ manual injection pattern is exactly how Bloodcraft's familiars fight.

**2. Stack-cap per ability** — new `Transform_MaxStacksPerSummonAbility`
setting (default 3). After 3 casts of, say, Undead Priest's Raise Horde,
the 4th cast is refused with a chat message: "Summon limit reached (3/3)
for Raise Horde. Wait for existing summons to die." For natural-chain
summons (where V Rising's pipeline produces the entities, not us), over-cap
spawns are intercepted at LinkMinion and queued for staged destroy. Counter
decays as minions die (lazy filter on each check).

**3. Staged despawn** — the v0.22.0 crash was caused by destroying 36
entities in a single frame. v0.22.1 capped tracking at 10 which masked the
crash but caused leaks. v0.23.0 tracks ALL summons (no cap) and destroys
them in a per-frame budget (`Transform_DespawnBudgetPerFrame`, default 5).
Even with 100 summons queued, the drain spreads safely across ~20 frames.

**4. Teleport-follow** — added `PlayerTeleportSystemPatch` postfix that
detects waygate / bat-form / admin teleports and repositions every live
summon to the player's new position with a radial scatter. No more leaving
your horde stranded at the spawn waygate when you bat-fly to a fight.

**5. New command `.beelz summons [off|on|status]`** — toggle summons
on/off, or query stack status. `.beelz summons off` queues all active
minions for staged despawn and blocks future casts; `on` re-enables;
no-arg shows live count per ability vs cap.

### Bug fixes

- **`.beelz transforms` crash with many unlocks** — at 13+ unlocks the
  command output exceeded VCF's 510-byte FixedString512Bytes cap and
  threw `Truncation while copying`. Per-line emit now (same pattern as
  `.beelz list` paginated in v0.21.0).

### Known gaps

- Natural-chain summons that don't match our pattern names (e.g. Undead
  Priest variants other than `_RaiseHorde_`) still need investigation —
  watch for `[Beelz SUMMON] ... no manual-spawn entry` log lines and
  paste them back if a known boss summon isn't firing.
- Visual ability animation mapping (Task #84) still deferred — was waiting
  on summon stability across multiple V-Bloods.

## [0.22.1] - 2026-05-22

### Stonebreaker summon target + crash fix

Three fixes from v0.22.0 test logs:

**1. Stonebreaker reinforcements now spawn** — added `CHAR_Bandit_Worker_Miner`
as the target. Best guess for what an Errol-the-Stonebreaker summon should be;
report back if the actual boss summons something different and we'll swap.

**2. Server crash on revert fixed** — your last test ended with the log
`despawned 36/36 summoned minion(s)` right before the server crashed.
V Rising can't reliably destroy that many entities in a single frame. We now
cap tracked minions at 10 per transform — extra summons still spawn allied,
but they're not tracked for cleanup (they despawn naturally on player death
via V Rising's existing minion lifecycle).

**3. Version string fix** — your v0.22.0 log showed `Plugin kdpen.Beelzebub
v0.21.1` even though the new code was loaded. Stale cached metadata.
Clean rebuild this round actually regenerated it; should now correctly
report `Plugin kdpen.Beelzebub v0.22.1` on next server start.

### Notes from the test logs

Undead Priest abilities (regular AND V-Blood) work via V Rising's natural
spawn chain — confirmed in your log. Our cast-start hook does NOT fire for
those because their names use `_RaiseDead_` / `_RaiseHorde_` not `_Summon_` /
`_Reinforcement_`. So we don't intercept; V Rising handles, and our LinkMinion
patch just rebinds the owner as a safety net. The Priest is a working
reference case — if Stonebreaker's manual spawn works after v0.22.1, we have
both architectures (natural chain + manual intercept) covered.

## [0.22.0] - 2026-05-22

### Summons actually fire now (manual spawn architecture)

The "cast plays sound, no unit appears" problem from v0.20.0 / v0.21.x is
fixed via a new architecture: instead of patching V Rising's spawn chain
(which dies before reaching us for player-cast summons), we now hook the
cast-start system and **spawn the unit manually** at the moment a
transformed player initiates the cast. Same player-ally setup as before
(faction, follower, owner) plus auto-despawn on `.beelz revert`.

**Initial curated targets**: only Bishop of Shadows' Shadow Soldiers are
in the spawn-target map confirmed. Every other V-Blood's summon will log
a `[Beelz SUMMON] ... Spawn target unknown` warning the first time it's
cast — paste those lines back and I'll add them to the map.

**Bonus**: two new shapeshift forms added to the visual roster — Werewolf
(for werewolf-themed V-Bloods) and Tailor Gargoyle (specifically for the
Tailor's phase-2 form). Still no visual swap possible for humanoid
bandits, undead, or constructs — V Rising's shapeshift system only
supports its own shipped model roster (~10 forms). Same limit that means
EXO form works but Stonebreaker doesn't.

**Visual ability animation mapping** (your earlier ask about giving
captured abilities a visual feedback) is filed for the next release —
the summon work was the higher priority unblock.

To pick up v0.22.0: stop the server, restart, then in chat enable
`VerboseLogging` and cast a summon. The logs will tell us which entries
need to be added to the spawn-target map.

## [0.21.1] - 2026-05-22

### Diagnose summons + clearer transform message

**For debugging summons-don't-spawn**: enable
`VerboseLogging = true` in `BepInEx/config/kdpen.Beelzebub.cfg`, transform,
cast the summon, and check `LogOutput.log` for `[Beelz DIAG]` lines.
They'll dump every entity V Rising spawns during the cast, with owner
info, so we can pinpoint where the spawn chain stops. Paste the lines
back if summons still don't appear and we'll target the specific
failure point.

**Clearer transform message**: when you transform into a unit V Rising
can't visually morph you into (humanoid bandits, undead, golems —
basically everything except wolves/bears/rats/spiders/toads), the
success message now explicitly says so:

> (No visual model swap — V Rising only provides Wolf/Bear/Rat/Spider/Toad
> shapeshift forms; this unit doesn't map. Spell bar + stats only.)

So you know upfront that the spell bar is the only thing changing.

Filed for future research (task #84): mapping captured abilities to
player-character animations so the cast has a visual, or temporarily
applying a shapeshift form just for the cast duration. Both are
non-trivial and deferred until summon-as-ally is confirmed working.

## [0.21.0] - 2026-05-22

### `.beelz list` is now paginated — and there's a new `.beelz search`

Two long-overdue UX fixes for anyone with a lot of captures.

**`.beelz list [page]`** — paginated, 15 captures per page. Page 1 also
shows your slot bindings + currently-equipped weapon. Footer tells you how
to go to the next page. The index next to each capture is preserved
across pages so `.beelz grant <slot> <index>` still works with any
number you see.

**`.beelz search <term>`** — case-insensitive substring match against
ability and unit names. Up to 25 results. Examples:

```
.beelz search bolt       → every ability with "bolt" in name
.beelz search bishop     → everything captured from any Bishop
.beelz search frostmaw   → all Frostmaw drops
```

Each match shows its original index so you can immediately
`.beelz grant <slot> <index>` from a search result.

No data changes — just chat-output ergonomics for long collections.

## [0.20.1] - 2026-05-22

### Curated matrix installed + audit-driven polish

Six audit findings actioned (one verified false-positive and dismissed).

**For players** — two new commands:

- **`.beelz catalog`** [page] — every curated V-Blood transformation with
  `[X]` for ones you've unlocked, `[ ]` for ones still to hunt. Sorted by
  tier (early game first). Paginated 10 per page. Use this to plan what
  to fight next.

**Behavior change**: gate-boss kills (the easier castle-gate variants) no
longer grant separate transform unlocks. Defeat the main V-Blood to unlock
that transformation. Ability captures from gate-boss kills still work
normally — useful for previewing a boss's kit before the main fight.

**For admins** — eight new commands:

Remote slot binding:
- `.beelz admin set-slot <player> <slot> <abilityGuid>` — bind a player's
  universal slot remotely.
- `.beelz admin set-weapon-slot <player> <weapon> <slot> <abilityGuid>`
  — bind a player's weapon-specific slot.
- `.beelz admin clear-slot <player> <slot>` — clear universal slot.
- `.beelz admin clear-weapon-slot <player> <weapon> <slot>` — clear
  weapon-specific slot.

Bulk operations:
- `.beelz admin revert-all` — force-end every active transformation on
  the server.
- `.beelz admin freeze-captures <on|off|status>` — runtime toggle for
  the master capture switch (no config reload needed).
- `.beelz admin snapshot` — high-level state dump (player count, active
  transforms, curated entry counts, server mode, build).
- `.beelz admin wipe-all CONFIRM-WIPE` — DESTRUCTIVE: wipe all player
  data. Requires literal `CONFIRM-WIPE` token as a typo-guard. Reverts
  active transforms first so carrier buffs and summoned minions clean
  up gracefully.

**Curated matrix installed**: the 443-ability + 64-V-Blood curated config
is now active. You'll see Tier / Difficulty / Notes / FullReplace flags /
PowerScalingMode overrides per-unit in `.beelz preview` and on the BCH
API. (Was sitting in `Beelzebub/docs/` but the runtime never read it.)
Existing config backed up to `ability_rules.json.backup-pre-v0.20.0`.

## [0.20.0] - 2026-05-22

### Summons now fight FOR you, not against you

The "lead a horde" feature. When you cast a summon ability while
transformed (Stonebreaker's reinforcements, Bishop of Shadows' shadow
soldiers, etc.), the spawned NPCs:

- **Spawn on your team** — they attack enemies, not you.
- **Follow you** — leashed-mode follower behavior.
- **Despawn on revert** — `.beelz revert` clears all your summons too.
  No wandering mob trail.

Mechanically: a Harmony Prefix patch on V Rising's
`LinkMinionToOwnerOnSpawnSystem` catches each freshly-spawned minion
where the originating caster is a Beelzebub-transformed player, then
rebinds its `EntityOwner` / `Faction` / `Follower` / `Minion` components
to point at you. Pattern lifted from Bloodcraft's
`FamiliarBindingSystem.ModifyFollowerFactionMinion`.

The default deny-list no longer filters summon abilities. Existing
installations need to edit `ability_rules.json` to remove
`"_Summon_"`, `"_Summoning_"`, `"_CallReinforcements_"`,
`"_Reinforcement_"` from `DenyPatterns` (or delete the file to
regenerate).

New config: `Transform_SummonsAreAllies` (default `true`). Set false
to disable the rebind and leave V Rising's vanilla spawn handling
in place.

To test after restart:

1. Edit `BepInEx/config/kdpen.Beelzebub/ability_rules.json` — remove
   the four summon-related entries from `DenyPatterns`.
2. `.beelz admin reload` in chat.
3. `.beelz transform StoneBreaker` → `.beelz current` should now show
   Reinforcement on the bar.
4. Cast Reinforcement → 2-3 bandit-stone-worker NPCs appear around you,
   fighting on your side.
5. `.beelz revert` → minions despawn.

## [0.19.0] - 2026-05-22

### Big fix: V-Bloods now show their real "ultimate" on the bar

If a V-Blood transform was missing one of its signature attacks (Stonebreaker
without MountainRumbler, Bishop of Shadows missing his shadow nova, etc.), this
release fixes it. Cause was the Hard-difficulty filter (Z3) failing to
recognize that V-Blood basic abilities and their Hard duplicates are siblings
because of the `_VBlood_` infix asymmetry. Hard duplicates were leaking
through, consuming slot space, and pushing real signature abilities off the
bar. Fixed.

Stonebreaker's bar now has SwingAttack + RockSmash + MountainRumbler +
SpinAttack (4 real abilities). Reinforcement (summon) stays filtered
because it doesn't work without an NPC caster context (see below).

### `.beelz preview` no longer crashes on big units

The chat reply was concatenating into a single message that blew past V
Rising's 510-byte reply cap. Now each slot is emitted as its own line.
Same fix applied to `.beelz current`.

### `_Reinforcement_` filtered by default

V Rising's NPC summon abilities don't spawn anything when cast by a player
(faction/team setup needs an NPC caster context). v0.18.0 added a default
filter for `_Summon_` / `_CallReinforcements_` but missed the bare
`_Reinforcement_` pattern Stonebreaker uses. Added.

If your `ability_rules.json` is from a previous version: add
`"_Reinforcement_"` to your `DenyPatterns` array, OR delete the file to
regenerate.

### New scaling mode: `PlayerLeveled`

Closes the "I want transforms to scale with my character's level, not just
be flat boss-tier" design conversation. New value for
`Transform_PowerScalingMode` in your config (or per-entry in
`ability_rules.json`):

- **`PlayerLeveled`** — reads your CHAR_ prefab's stats AND your current
  UnitLevel, applies `boss_stat × clamp(player_level / max_level, 0, 1)`.
  At low level the transform's bonus is small. At max level you get the
  full boss tier. On Bloodcraft servers your UnitLevel reflects
  Bloodcraft's leveling — so prestige resets reduce the transform bonus
  proportionally.

Plus a `Transform_PlayerLeveled_MaxLevel` config (default 90 = V Rising's
typical max gear level) to anchor the curve. Bloodcraft servers with
raised caps should bump this to match.

### Summon allies — researched, deferred

Bloodcraft makes familiars side with the player by overriding their Team /
Faction on spawn. We could plausibly do the same for transient summon-
ability spawns from a transformed Beelzebub player. It needs a dedicated
session of research + implementation: hooking the spawn system, marking
spawns as Beelzebub-triggered, faction override, follow logic, cleanup on
revert. Filed as task #74. For now summons stay filtered.

### Visual model still won't change — V Rising hard limit

Reiterating: V Rising exposes exactly five native shapeshift forms (Wolf,
Bear, Rat, Spider, Toad). For Errol the Stonebreaker, the Putrid Rat
Ravager, and ~95% of V-Bloods, transforming will swap your spell bar +
stats but keep your character model the same. Server-side mods can't add
new shapeshift forms.

## [0.18.0] - 2026-05-22

### See what's on your bar — `.beelz current` + `.beelz preview`

Two new chat commands from in-game testing feedback:

- **`.beelz current`** — prints what's effectively on your spell bar right
  now. Transform-aware: while transformed it shows the unit's slot loadout
  for the current phase, while not transformed it shows your saved
  grants resolved for the weapon you're holding. Each slot shows the
  ability name + a category badge (`Ultimate`, `Aoe`, `Travel`, etc.).
  Use this to debug "why isn't this ability on my bar?"
- **`.beelz preview <index|name>`** — shows what abilities you'd get if
  you transformed into a unit, *before* activating. Lists every phase
  the unit has curated, the difficulty + tier + scaling mode + notes,
  and the server difficulty mode (so you know what's gated). Helps you
  pick which V-Blood to transform into.

### Summon abilities are filtered by default

V Rising's NPC summon abilities (StoneBreaker's golems, Bishop of Shadows'
shadow soldiers, etc.) cast but don't actually summon when used by a
player — the spawner code needs an NPC caster context. The player just
does the cast animation with no effect. So these abilities now default
to filtered out (`_Summon_`, `_Summoning_`, `_CallReinforcements_` added
to the default `DenyPatterns`). Same for cinematic feed-prep abilities
(`_FeedBoss_`, `_Feed_Initiate_`).

**Existing installs**: your `ability_rules.json` is loaded as-is. To
pick up the new filter entries, either edit the file to add them to the
`DenyPatterns` list, or delete the file (it'll regenerate on next start
with the new defaults — you'll lose any custom curation, so back it up
first).

### Visual model — known V Rising hard limit

If your character model isn't changing during a transform, here's why:
V Rising only exposes five native shapeshift forms (Wolf, Bear, Rat,
Spider, Toad). Server-side mods can't create new ones. Transforming into
anything outside those families (humanoids, undead, golems, etc.) won't
change your model — only the spell bar + stats. This is a V Rising
limitation, not a Beelzebub bug.

For "boss form" feel without a model swap, combine `FullReplace: true` +
`PrefabAbsolute` scaling + `MovementSpeedScale > 1` so it feels
mechanically powerful even visually unchanged.

## [0.17.1] - 2026-05-22

### Hotfix — transforms now actually swap your spell bar

Bug surfaced in v0.17.0 testing: typing `.beelz transform <unit>` reported
success and let you `.beelz revert` later, but the captured abilities never
showed up on your action bar.

Cause was a buff-creation timing problem: V Rising's `ApplyBuff` event
queues for next tick, and my code tried to enrich the buff immediately on
the same frame — found nothing, gave up silently, but left you with the
"transformed" status. Now using V Rising's synchronous buff-instantiation
API instead. Apply, get the entity back, enrich it on the same frame.

If you were on v0.17.0 with a transform stuck in the "registered but not
applied" state, `.beelz revert` to clear it, then re-run `.beelz transform
<unit>` and the spell bar should swap immediately.

## [0.17.0] - 2026-05-22

### Pick how transforms get their power (TX7) + locked weapons during
### FullReplace (TX8)

Admin choice: when a player casts a captured ability while transformed,
where does its power come from? Set `Transform_PowerScalingMode` in your
config (or `PowerScalingMode` per-entry in `ability_rules.json`):

- **`CuratedScales`** (default — current behavior): admin tunes each
  transform via `DamageScale` / `HealthScale` / `CooldownScale` /
  `MovementSpeedScale`.
- **`PrefabAbsolute`**: auto-read the boss's `UnitStats` from its CHAR_
  prefab and apply matching stats to the player while transformed.
  Player hits *exactly* as hard as the boss. Ignores player level.
- **`PlayerScaled`**: apply nothing. V Rising's vanilla formula scales
  the captured ability by the player's own PhysicalPower / SpellPower —
  including any Bloodcraft expertise, blood-quality, or level buffs
  the player carries. **Bloodcraft prestige resets automatically rescale
  the ability**. The Bloodcraft-friendly mode.

Picking a mode:

| Your setup | Recommended |
|---|---|
| Bloodcraft + prestige should matter | `PlayerScaled` |
| Solo/coop, want per-boss curation | `CuratedScales` |
| Boss-form RP, transforms = legitimate boss fights | `PrefabAbsolute` |
| Mixed | Global `PlayerScaled`, per-entry override to `PrefabAbsolute` for the specific units you want cinematic |

**FullReplace also locks your weapon now.** Any transform marked
`"FullReplace": true` in `ability_rules.json` now adds V Rising's native
`BlockEquipmentSwapping` to the carrier buff — players cannot swap weapons
mid-transform. Auto-clears on `.beelz revert`.

BCH integrators: `scaling_mode=<value>` appended to `.beelz api transforms`
and `.beelz api catalog units` so the UI can render a badge for the
chosen mode.

See `docs/ABILITY_MAP_FORMAT.md` "Power scaling modes" section for the
full design + per-mode mechanics.

## [0.16.0] - 2026-05-22

### TX3 — "Be the NPC" mode (FullReplace)

You can now mark individual transforms as **FullReplace** in
`ability_rules.json` and force the native shapeshift visual on for that
specific unit, without flipping the global `Transform_NativeShapeshift_Enabled`
config. Pair it with curated TX6 scales for the full cinematic moment.

```json
"CHAR_Forest_Wolf_VBlood": {
  "Enabled": true, "Difficulty": "Basic", "Tier": 2,
  "DamageScale": 0.8, "MovementSpeedScale": 1.25,
  "FullReplace": true,
  "Notes": "Alpha Wolf — fast, slightly weaker damage."
}
```

Activates as: model swaps to a wolf, your spell bar is the Alpha Wolf's
kit, your stats get +25% speed and -20% damage. **The visual drops on
your first spell cast** (V Rising vanilla — native shapeshift forms exit
on off-form casts). Mechanically you still keep the spell bar + stats
through the visual drop; only the model reverts.

So FullReplace is a 1-3 second cinematic that announces "I AM the wolf"
plus the persistent power profile, not a permanent model swap. If a
future V Rising update exposes a way to block the auto-exit, FullReplace
becomes a permanent-while-active mode for free.

BCH integrators: catalog and transform endpoints now include
`full_replace=0|1` so the UI can render a distinct badge.

See `docs/ABILITY_MAP_FORMAT.md` for the full TransformMap schema.

## [0.15.1] - 2026-05-22

### Better data for BCH UI badges (IN2)

For server admins / BCH integrators only — players see no behavior change.

When BCH (or any other API consumer) reads the parseable `.beelz api *`
endpoints, every captured ability now carries a `cat=...` tag (`Ultimate`,
`Travel`, `Aoe`, `Projectile`, `Summon`, `Buff`, `WeaponSpell`, `Spell`, or
`Other`) and every transform unlock carries a `type=...` tag (`Vampire`,
`Undead`, `Beast`, `Humanoid`, `Construct`, `Demon`, or `Other`). BCH can
use these for icons/colors/tooltips without parsing the raw prefab name.

Backward-compatible: new fields are appended to each `[BEELZ:list]`,
`[BEELZ:tx]`, `[BEELZ:catalog-unit]`, and `[BEELZ:catalog-ability]` line.
Existing parsers that don't know about them just ignore them.

`captured_at` timestamp (the third IN2 item) is deferred — it needs a
state-file schema bump and we don't want to pair that with the still-
untested v0.14.0 + v0.15.0 captures. Revisit when there's a concrete UI ask.

## [0.15.0] - 2026-05-22

### Admin balance dials for transformations (TX6)

Each entry in your `TransformMap` (the per-V-Blood matrix in
`ability_rules.json`) now accepts four new fields that let you tune how
strong each transformation is, all defaulting to `1.0` (no change):

- **`DamageScale`** — scales both physical and spell damage while the player
  is transformed into that unit. `1.5` = +50%, `0.5` = -50%.
- **`CooldownScale`** — `>1.0` makes cooldowns longer (admin nerf), `<1.0`
  makes them shorter. Applies to both spells and weapon abilities.
- **`HealthScale`** — scales the player's MaxHealth while transformed.
- **`MovementSpeedScale`** — scales movement speed while transformed.

These let you keep an OP transformation available without it dominating —
e.g. give the Dracula transform a `DamageScale: 0.5, CooldownScale: 1.5,
HealthScale: 0.7` profile so it costs more than it gives. Or buff a weak
transform with `DamageScale: 1.3`. All bonuses apply only while the
transform is active and lift cleanly on revert.

The new `.beelz api transforms` and `.beelz api catalog units` responses
include these four scales per entry so BCH (when its UI catches up) can
render a "Power Profile" on each transform card.

See the updated `docs/ABILITY_MAP_FORMAT.md` for the full schema + a
worked Dracula-style nerf example.

## [0.14.0] - 2026-05-22

### Bug-fix release — three big symptoms, one root cause

In-game testing of 0.13.0 hit three problems with a single shared cause: casting
any ability dropped the wolf/bear/etc visual the moment you fired it; `.beelz
revert` brought back your weapon abilities but left your spell-book spells (slots
5/6) empty; and the server log filled with `Clearing entity X which is a
modification source ... AbilityGroupSlot` warnings on weapon swaps. All three are
fixed by rebuilding the transform spell-bar pipeline around a dedicated carrier
buff (the same pattern Bloodcraft uses for exoform).

- **Revert now restores your full pre-transform spell bar**, including the
  spells you chose in your spell book. No more empty slots 5/6.
- **Native shapeshift visuals are now opt-in.** Default off — the form would
  drop on first cast anyway. Admins who still want the wolf model can enable
  `Transform_NativeShapeshift_Enabled` in the config knowing the form is purely
  cosmetic and will end at the first spell.
- **No more `AbilityGroupSlot` warnings** flooding the server log on weapon
  swaps and autosaves.
- **Bishop of Death (and other "Hard-only" V-Bloods) get their full kit on
  Basic-mode servers.** Some V-Bloods only have a `_Hard_` variant of certain
  abilities — that's the only version the boss itself has — so they're now
  allowed through on Basic mode instead of being filtered out and leaving you
  with fewer slots than the boss carries.
- **Type `.beelz` (no subcommand) for a five-line overview** of how the mod
  works + where to find the full command list. `.beelz help` still gives the
  walkthrough; `.beelz commands` still lists every chat command.

## [0.13.0] - Unreleased

### TX5 — Multi-phase transformation handling

- Bosses like the Geomancer (Human↔Golem), Morgana, Valyr and Iva have
  in-fight phase shifts. Beelzebub now models this via a per-ability
  `Phase` field in `AbilityMap` (int, default 1). Admins curate which
  abilities belong to phase 2 / 3 of a boss's kit; the runtime filters
  the transformed spell bar by phase.
- New `.beelz phase <n>` chat command — while transformed, swap your
  spell bar to the unit's phase-N abilities. No args = show current +
  available phases for the active transform.
- For bosses without multi-phase mechanics, everything stays at Phase=1
  and the command shows "this unit doesn't have multi-phase mechanics
  curated." — backward-compatible no-op.
- `.beelz api active` now includes `phase=<current> phases=<csv>` so BCH
  can render a phase selector.
- New BCH event `[BEELZ:event] type=transform-phase-shift u=… un=… phase=<n>`
  fires each time the bar swaps.

### Example curation (Geomancer)
```json
"AB_Geomancer_Human_Projectile_Group":        { "Phase": 1, "Notes": "Phase 1 — human form ranged" }
"AB_Geomancer_Transform_ToGolem_AbilityGroup":{ "Phase": 1, "Notes": "Phase 1 trigger to golem" }
"AB_Geomancer_GroundSlam_Group":              { "Phase": 2, "Notes": "Phase 2 — golem form melee" }
"AB_Geomancer_UndergroundTremmors_AbilityGroup":{ "Phase": 2, "Notes": "Phase 2 ground AoE" }
```
Then `.beelz transform geomancer` → phase 1 abilities. `.beelz phase 2`
→ swap to golem-form abilities.

## [0.12.0] - Unreleased

### IN3 — Catalog endpoints (BCH "collection book") + IN4 — Progress endpoint completion

- New `.beelz api catalog` returns a summary line:
  `[BEELZ:catalog-summary] abilities=N units=M server_mode=Basic|Brutal`.
- New `.beelz api catalog units [page]` streams every curated transform
  target with its matrix attributes (Enabled / Difficulty / Tier / Notes).
  Paginated, 40 entries per page.
- New `.beelz api catalog abilities [page]` streams every curated ability
  with full attributes (Weapons, Forms, TransformOnly, Enabled,
  Difficulty, DamageScale, CooldownScale, Notes). Paginated, 40 per page.
- Wire format `[BEELZ:catalog-unit]` / `[BEELZ:catalog-ability]` per row,
  `[BEELZ:end] cmd=… count=… total=… page=… pages=…` trailer per call.
- `.beelz api progress` now uses the curated `TransformMap.Count` as the
  transform total (was a hardcoded 61 placeholder). `.beelz progress`
  chat-side display matches.
- `.beelz commands` cheat-sheet adds `catalog` to the api line.

## [0.11.0] - Unreleased

### TX4 — Brutal-vs-Basic difficulty gating

- New config knob `Server_DifficultyMode` (string, default `"Basic"`,
  values: `"Basic"` | `"Brutal"`). Admin specifies the server's mode once;
  the runtime gates Brutal-tagged content accordingly.
- Each `AbilityMap` entry now has a `Difficulty` field (string, default
  `"Basic"`). Matches the `Difficulty` field that's been on TransformMap
  entries since TX1.
- New `.beelz admin difficulty [basic|brutal]` command — show current mode
  with no argument; set it with one. Lets you switch modes mid-session
  without editing the config file.
- Gating points:
  - `AbilityFilter.ShouldCapture` refuses Brutal-tagged abilities on a
    Basic server. Auto-tags any ability whose prefab name contains
    `_Hard_` as Brutal unless the matrix overrides.
  - `TransformService.TryActivate` refuses Brutal-tagged transformations
    on a Basic server with a clear message.
  - Transform-unlock rolls (regular kills + V-Blood kills) skip the
    Brutal-only roll on a Basic server.
- `.beelz api info` now emits `difficulty=Basic|Brutal` on every ability.
- BCH wire-format gains the difficulty field for tooltip display.

## [0.10.1] - Unreleased

### TX2 — TransformMap curated for all 60+ V-Bloods

- `docs/ability_map.curated.json` now ships a complete TransformMap section
  with 64 entries (61 V-Bloods + a few non-V-Blood transform-source units
  like Undead Leader). Tier distribution: 1×7, 2×9, 3×14, 4×24, 5×10.
- All entries default to `Enabled: true` and `Difficulty: "Basic"`. Admins
  flip individual `Enabled` to false to disable broken or undesired
  transforms.
- Notes field provides admin context: which boss this is in V Rising, what
  weapon family the boss uses, any known TX5 / TX6 special considerations
  (e.g. Geomancer's built-in phase shift, Beatrice's gargoyle phase 2 glitch).
- Drop the file as-is into `BepInEx\config\kdpen.Beelzebub\ability_rules.json`,
  then `.beelz admin reload` to pick up changes live.

## [0.10.0] - Unreleased

### TX1 — TransformMap matrix (per-unit attributes for transformations)

- New `TransformMap` section in `ability_rules.json`. Mirrors the existing
  `AbilityMap` workflow but keyed by `CHAR_*` prefab names (e.g.
  `CHAR_Vampire_Dracula_VBlood`). Per-unit fields:
  - `Enabled` (bool, default true) — admin kill-switch. False blocks both the
    transform-unlock roll on kill AND `.beelz transform` activation. Lets you
    surgically disable a broken or undesired transform without removing the
    feature for everyone.
  - `Difficulty` (string, "Basic" or "Brutal") — placeholder for the TX4
    difficulty gate. Currently stored but not enforced.
  - `Tier` (int 1-5) — power tier; will drive TX6 global scaling.
  - `Notes` (string) — admin annotation; surfaced via `.beelz api transforms`.
- Wired into the runtime:
  - `TransformService.TryActivate` refuses to start a transform if the unit
    is `Enabled: false` (player gets a chat message naming the unit).
  - `DeathEventListenerSystemPatch` skips the transform-unlock roll for
    disabled units.
  - `VBloodSystemPatch` skips the V-Blood transform-unlock roll for disabled
    units.
- `.beelz api transforms` now includes `enabled=` / `difficulty=` / `tier=`
  on every unlock line — BCH can render matrix data alongside the list.
- Documentation:
  - `docs/ABILITY_MAP_FORMAT.md` has a new "transformation matrix" section.
  - `docs/ability_map.curated.json` seeded with two example entries
    (Dracula, Forest Wolf). TX2 will curate the full ~60 entries.

## [0.9.0] - Unreleased

### AT-series — admin tooling + progress %

- **`.beelz admin inspect <player>`** — see another player's full state:
  capture counts (V-Blood / regular), universal + per-weapon slot binds,
  transform unlocks, active transform, hotkeys, verbosity, BCH event status.
- **`.beelz admin revoke <player> <unitGuid> <abilityGuid> [reason]`** —
  remove a capture. Audit-logged.
- **`.beelz admin revoke-transform <player> <unitGuid> [reason]`** —
  remove a transform unlock. Auto-reverts if the player is mid-transform
  into that unit. Audit-logged.
- **`.beelz admin force-transform <player> <unitGuid>`** — start a
  transformation on another player. Bypasses unlock + cooldown. Useful
  for GM events, debugging, recovery. Audit-logged.
- **`.beelz admin clear-transform <player>`** — end another player's
  active transformation. Audit-logged.
- Every grant / revoke / force / clear writes a `[Beelz AUDIT]` line to
  `BepInEx\LogOutput.log` with admin SteamID + target + reason for
  diagnosing player reports.

### Collection progress
- **`.beelz progress`** — your own collection: captured / total, transforms
  unlocked, slot+hotkey bind counts.
- **`.beelz admin progress <player>`** — view another player's progress.
- **`.beelz api progress`** — BCH-readable wire-format version (partial IN4).

## [0.8.0] - Unreleased

### W4 — Named hotkey bindings (server-side scaffold)

- New chat commands under `.beelz hotkey`:
  - `.beelz hotkey set <name> <index>` — bind a named hotkey to a captured
    ability. Names are case-insensitive and admin-configurable; defaults to
    a 5-per-player cap (raise via `Hotkeys_MaxPerPlayer`).
  - `.beelz hotkey clear <name>` — remove a binding.
  - `.beelz hotkey list` — see your bindings + cap.
- New API endpoint `.beelz api hotkeys` streams the caller's bindings in
  the BCH wire format for the client UI to render buttons.
- New config knobs in `Hotkeys` section:
  - `Hotkeys_Enabled` (default true) — admin master switch.
  - `Hotkeys_MaxPerPlayer` (default 5) — per-player cap.
- `Enabled: false` and `TransformOnly: true` matrix rules are respected at
  bind time — you can't hotkey a disabled or transform-only ability.
- state.json schema bumped to v6 (forward-compat from v5). New
  `PlayerDto.Hotkeys` is a `Dictionary<string, int>` (name → ability GUID).

### Server-side only — cast-trigger pending BCH

- The actual "press a button → ability fires" mechanism lives in BCH's
  client mod. The server stores the binding and exposes it via the API;
  BCH renders the UI and triggers casts. Once BCH is ready, we'll wire
  the trigger path (likely a chat command BCH sends to the server, which
  resolves the bound ability and fires it on the player).

## [0.7.2] - Unreleased

### Richer BCH API tooltips (A1-full repurposed)

- V Rising's localization data isn't shipped on the dedicated server, so
  there's no in-game text source for ability descriptions on our end. The
  pragmatic solution: surface the **admin-curated `Notes` field from the
  `AbilityMap` matrix** as the description. You're already curating it
  for weapon/form classification; the same Notes line becomes the tooltip.
- `.beelz api info <i>` now emits, in addition to the previous fields:
  - `desc=<Notes>` from the matrix, or "Captured from <unit>." fallback.
  - `weapons=Sword,GreatSword` — the resolved weapon-family list.
  - `forms=any` or `forms=Wolf,Bear` — form restriction.
  - `transform_only=0|1`, `enabled=0|1`.
  - `damage_scale=1.00`, `cooldown_scale=1.00`.
- Wire-format value safety: spaces are encoded as `_` and `=` as `-` so
  multi-word descriptions don't break BCH's key=value parser.
- `.beelz api slots` extended for W3: now streams universal + per-weapon
  buckets, tagged with `bucket=any` or `bucket=<weapon>`, plus a
  `[BEELZ:slot-current] weapon=<X>` footer showing the live weapon.

## [0.7.1] - Unreleased

### W5 telemetry (damage/cooldown scaling still pending)

- Damage-event telemetry: with VerboseLogging on, the server now logs every
  damage event whose source ability matches one of your Beelzebub-granted
  slots. Useful for admins curating DamageScale values to verify which
  abilities are actually firing.
- The full DamageScale / CooldownScale runtime is **NOT yet active** —
  V Rising's `DealDamageEvent` struct has readonly fields in the IL2CPP
  wrapper, so direct damage mutation at the damage-system layer is blocked.
  Path forward is a per-cast stat-buff approach (apply temporary SpellPower
  / PhysicalPower modifier). That's a focused future session.

## [0.7.0] - Unreleased

### Per-weapon slot loadouts (W3)

- New `.beelz weapon-grant <weapon|auto> <slot> <i>` command lets you bind a
  captured ability to a slot **for a specific weapon family**. Different
  weapons can hold different abilities in the same slot number.
  - `auto` resolves to whatever you're currently wielding.
  - Valid families: Sword, GreatSword, Axe, Mace, DualHammers, Spear,
    Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe,
    Slashers, TwinBlades, Unarmed, FishingPole.
- New `.beelz weapon-unslot <weapon|auto> <slot>` clears a weapon-specific
  bind.
- `.beelz grant` still exists — it now writes to the **universal** bucket
  (any weapon), same as before.
- Precedence: weapon-specific binding wins over universal on the same slot
  when you wield that weapon. Switch weapons → slots auto-swap.
- `.beelz list` now shows both universal slots and per-weapon slot
  buckets, plus what weapon you're currently wielding.
- `state.json` schema bumped to v5. Existing v4 saves load as universal
  bindings — your old grants survive untouched.

## [0.6.0] - Unreleased

### Layered slot precedence (W2)

- `.beelz grant` now respects each ability's `Weapons[]` matrix entry:
  - **Universal abilities** (tagged `Magic`, or with empty `Weapons[]`) apply
    on top of any weapon, including unarmed and fishing pole.
  - **Weapon-tagged abilities** (e.g. `["Sword", "GreatSword"]`) only
    fire when wielding one of the named weapons. Equipping a crossbow
    silently falls back to the crossbow's natural ability for that slot.
- Replaces the old "applies only when UNARMED" gate. The reply you get
  from `.beelz grant` now lists which weapons the ability activates on.
- Reactive: swapping weapons re-evaluates compatibility per saved slot
  on the next `ReplaceAbilityOnSlot` event — no manual reload needed.
- Auto-revert and `.beelz revert` also re-apply grants weapon-aware.
- Recognized weapon families (parsed from the `EquipBuff_Weapon_*` buff
  on the player): Sword, GreatSword, Axe, Mace, DualHammers, Spear,
  Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe,
  Slashers, TwinBlades, Unarmed, FishingPole.
- `Enabled: false` and `TransformOnly: true` are also respected at
  injection time — if you mark an ability as either, an existing slot
  binding to it goes silently inactive until you change the rule.

## [0.5.3] - Unreleased

### Admin controls per ability (Enabled, DamageScale, CooldownScale)

- Each `AbilityMap` entry gained three new fields:
  - `Enabled: true` (default) — set to `false` to kill-switch an ability.
    Blocks new captures AND blocks `.beelz grant` / transform pickup. Surgical
    per-ability removal without wiping the player's existing collection.
  - `DamageScale: 1.0` (default) — placeholder for the W5 damage-scaling
    runtime. Curate values now; runtime catches up later.
  - `CooldownScale: 1.0` (default) — same, for cooldown scaling.
- The curated `ability_rules.json` now has these fields populated on all 443
  V-Blood entries with default values, ready to be edited.
- Transform-only abilities now also refuse `.beelz grant` with a clear message
  (was scaffolded in 0.5.2; runtime gate added here).

## [0.5.2] - Unreleased

### Ability matrix scaffolding (W1, prep for W2)

- `ability_rules.json` gains a new `AbilityMap` section: per-ability matrix
  with **multiple weapon types per ability**, optional shapeshift-form
  restriction, and a per-ability `TransformOnly` flag.
- Admins curate this file directly. Hot-reload via `.beelz admin reload`.
- Two new docs: `Beelzebub/docs/ABILITY_MAP_FORMAT.md` (format reference
  + location of the live runtime file) and `Beelzebub/docs/ability_map.starter.json`
  (a copy-pasteable starter with examples).
- No behavior change yet — the cascade that consumes this matrix lands in
  W2 (the layered slot-precedence work). 0.5.2 is the data-layer scaffold.

## [0.5.1] - Unreleased

### Shapeshift mapping trim (player feedback)

- Removed Human-disguise mapping — the form moves much slower than real
  humanoid NPCs and felt wrong. Humanoid captures now stay as the base
  vampire model.
- Removed Golem mapping — wasn't reliably player-usable.
- Kept: Wolf (incl. Werewolf), Bear, Rat, Spider (incl. Arachnid), Toad
  (incl. Frog).
- Trimmed the "(No native shapeshift form matched...)" reply — that's
  the expected case for most captures now and doesn't need an apology.

## [0.5.0] - Unreleased

### Visual shapeshift (A4)

- `.beelz transform <unit>` now applies a **visual model swap** alongside
  the spell-bar swap, using V Rising's eight native shapeshift form buffs:
  Wolf, Bear, Rat, Spider, Toad, Golem (tier-2), Human disguise.
- The mod maps your captured unit to the closest native form by name:
  Werewolves and wolf-variants → Wolf; bear-family → Bear; spider/arachnid
  variants → Spider; humanoids (Bandit, Militia, Villager, V-Hunter,
  Vampire, Witch, Cultist, Paladin, Knight, Priest, Farmer, Guard...) →
  Human disguise.
- `.beelz revert` removes the visual form. Auto-revert (Timed mode)
  removes it too.
- If a captured unit doesn't fit any heuristic bucket (banshees, Treant,
  Manticore, undead skeletons, etc.), only the spell bar changes — your
  vampire model stays. The reply tells you which form was used.
- Why heuristic: V Rising's engine has no generic "morph into arbitrary
  CHAR_" API. Eight native forms is the realistic ceiling. Future
  community discoveries may unlock more; this gets us the visible payoff
  today.

## [0.4.0] - Unreleased

### Seamless slot apply (A2)

- **No more "swap a weapon to apply"**: `.beelz grant`, `.beelz unslot`,
  `.beelz preset load`, `.beelz transform`, and `.beelz revert` now apply
  to your spell bar instantly. Grants apply when you're unarmed (a weapon
  still wins for its own slots). Transforms apply regardless of weapon and
  restore your previous bar on revert.
- Under the hood: the mod mutates the player's existing EquipBuff weapon
  buffer in-place and calls `ServerGameManager.ModifyAbilityGroupOnSlot`,
  the same approach Bloodcraft uses for its NPC-spell shift slot.
- Auto-revert (Timed mode) also restores the spell bar in-place.

## [0.3.2] - Unreleased

### Fixes from second test
- **No more spurious captures from resource nodes or summons**: the death-event
  hook now requires the killed prefab name to start with `CHAR_` and not
  contain `_Summon` or `_Servant`. Previously, breaking a sulfur deposit or
  killing a temporary summoned skeleton could roll a transform unlock.
- **Transform reply is clearer**: when you `.beelz transform <unit>`, the
  server now notes that the visual model swap is still in development.
  Your spell bar swaps; the model stays for now.

## [0.3.1] - Unreleased

### Fixes from first real test
- **Weapon abilities no longer overridden**: captured-ability slot grants now
  only apply while you're UNARMED (or holding a fishing pole). Equipping a
  weapon shows the weapon's natural abilities, as expected. Transforms
  still override the spell bar regardless of weapon (that's the
  "you ARE the unit" intent).
- **`.beelz help`** no longer errors. Replaced the multi-line reply with
  multiple short single-line messages. New `.beelz commands` lists every
  command available.
- **Crash mitigation**: defensive try/catch added to every per-frame patch
  (death-event, V-Blood, slot-replace, transform tick). A single bad
  ability or unit can no longer take down the server — the exception
  goes to `BepInEx\LogOutput.log` and processing continues.
- **`start_server_local.bat`** now `pause`s on exit so the CMD window
  stays open after a crash; you can see the last lines of output and
  read the log file paths from the bat's own farewell message.

## [0.3.0] - Unreleased

### Fourth-wave feature extensions

- **`.beelz help`** — in-chat walkthrough explaining the capture → list →
  grant → swap-weapon → transform loop. No more guessing where to start.
- **Slot loadout presets**: `.beelz preset save <name>`, `load <name>`,
  `list`, `delete <name>`. Snapshot your current slot assignments under
  any number of named loadouts and switch between them; swap a weapon
  after `load` to apply.
- **Per-ability rate overrides** in `ability_rules.json`: add entries
  to a new `dropRateOverrides` list specifying `pattern`, `rateRegular`,
  `rateVBlood`. The first matching pattern wins and overrides the global
  drop chance for that ability. Lets admins curate gameplay-defining
  abilities (e.g. boss ultimates) to drop at different rates than filler.
- **Per-unit-tier rate multipliers**: new `Capture.Tier` config section
  with `MidThreshold` (default 30) and `HighThreshold` (default 60) for
  `UnitLevel`, plus `Multiplier_Low/Mid/High` (all default 1.0). The
  killed unit's level determines the tier, and the matching multiplier
  scales both ability and transform drop chances. Curve high-level
  fights to be more rewarding, or flatten the curve entirely.
- **Admin grant**: `.beelz admin give <player> <unitGuid> <abilityGuid>`
  and `.beelz admin give-transform <player> <unitGuid>` to seed captures
  or transform unlocks for events, testing, or restoring after a wipe.
  Source (Regular vs VBlood) is inferred from whether the unit prefab
  has `VBloodUnit` / `VBloodConsumeSource`.

### [0.2.0] surface (still Unreleased)

Early access. Functional end-to-end; rough edges around the "swap a weapon to apply"
behavior noted under Known caveats below.

### Features
- **Defeat-to-devour:** any kill rolls a chance to capture the unit's abilities
  (default 5% per ability) and a smaller chance to unlock the ability to
  transform into that unit (default 1%).
- **V-Bloods count too** — boss kills go through a dedicated event hook and
  appear in their own section of `.beelz list`.
- **Assign captured abilities to your six spell slots** via chat. Swap a
  weapon to apply the change.
- **Transformations** — once unlocked, become the unit; all six of your spell
  slots fill with their abilities. Configurable mode per source type
  (Toggle / Timed / Disabled), independent for V-Bloods vs regular mobs.
- **Hot-reloadable filter rules** strip non-castable abilities by default
  (melee animations, idle filler, lifecycle events, Brutal-difficulty
  duplicates).
- **Per-player chat verbosity** — Silent / Summary / Verbose.
- **Admin controls** for drop rates, allow/deny rules, transform modes.
- **BCH-ready API surface** — structured chat output for the BloodCraftHub
  client UI to consume.

### Player commands
- `.beelz list` — view captured abilities and slot assignments
- `.beelz grant <slot 1-6> <index>` — assign a captured ability to a slot
- `.beelz unslot <slot>` — clear a slot
- `.beelz forget <index>` — delete one captured ability
- `.beelz clear` — wipe everything
- `.beelz verbosity <silent|summary|verbose>` — chat detail level
- `.beelz transforms` — list unlocked transformations
- `.beelz transform <index|substring>` — become a unit
- `.beelz revert` — end your current transformation

### Shared-kill credit (new)
Admins can now configure whether the killer gets exclusive capture rolls
or whether nearby players share. In `BepInEx\config\kdpen.Beelzebub.cfg`:
- `Capture_ShareCreditMode = KillerOnly` (default) — only the killer rolls
- `Capture_ShareCreditMode = Proximity` — every online player within
  `Capture_ShareCreditRadius` of the kill rolls their own captures
  independently. Default radius is 30m. The killer is always included
  even if outside the radius (handles ranged kills).

### Admin commands
- `.beelz admin rules` / `deny` / `undeny` / `allow` / `unallow` / `reload`
- `.beelz admin transform mode <regular|vblood> <toggle|timed|disabled>`
- `.beelz admin transform duration <regular|vblood> <seconds>`
- `.beelz admin transform cooldown <regular|vblood> <seconds>`
- `.beelz admin transform show`

### Configuration
`BepInEx\config\kdpen.Beelzebub.cfg` for server-wide rates and defaults.
`BepInEx\config\kdpen.Beelzebub\ability_rules.json` for hot-reloadable filter rules.
`BepInEx\config\kdpen.Beelzebub\state.json` for per-player state.

### Known caveats
- **Swap a weapon to apply.** `.beelz grant`, `.beelz transform`, and
  `.beelz revert` all queue a slot change that V Rising commits on its next
  natural slot-update event. Eliminating this is on the roadmap.
- **No visual shapeshift VFX** — only the spell bar changes when you
  transform. Your model stays the same.
- **Not every ability works in every slot.** Spell slots 5 and 6 generally
  accept projectiles and AoE; basic melee animations won't appear. The
  default filter strips most non-castable cases.
