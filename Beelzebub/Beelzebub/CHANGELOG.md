# Changelog

Newest first: the ten most recent versions, in short. Fuller notes and older versions are in
[CHANGELOG_FULL.md](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/Beelzebub/Beelzebub/docs/CHANGELOG_FULL.md).

## [0.137.7] - 2026-10-04

- The mod page now states that this is a **pre-1.0 alpha**: install it only on a test server, or one you are willing
  to reset.
- New **road to 1.0** section on the mod page.
- The client companion BloodCraftHub is now called **Raphael**. Nothing else changes; it keeps working as before.
- This changelog is shorter.

## [0.137.6] - 2026-10-04

- **Fixed:** the `Couldn't remap old Modification Id` errors at startup. Changing a spell slot could leave a hidden
  helper behind in the save; Beelzebub now removes these leftovers on its own, including ones saved by older versions.
- **Fixed:** those leftovers could also turn off slot 7's shared cooldown and change slot 1's spell modifiers.
- **Fixed:** Militia Leader's Whirlwind no longer logs a conflict warning at startup (existing servers:
  `.beelz admin reseed merge`).
- Admin: new read-only `.beelz admin modleak [player|all]` lists the hidden slot helpers.

## [0.137.5] - 2026-10-04

- **One transformation at a time.** Transforms, phase changes, travel and login no longer stack a form on top of
  another one.
- While a boss form is still appearing, `.beelz phase` and `.beelz refresh` reply "Still transforming".
- **Fixed:** `.beelz phase` refused every phase above 1 for Dracula and Morgana.
- **Fixed:** leaving combat mid-phase-change could skip the automatic return to phase 1.

## [0.137.4] - 2026-10-04

- **`.beelz clearbar [all|universal|<weapon>|<form>]` now fully clears the loadout you name**, like `resetbar`.
  Before, saddle and transform abilities could stay on the bar. Your other loadouts are kept.
- Clearing while riding dismounts you, and clearing while transformed ends the transform; the reply says so.
- If something stays stuck, the reply names it.

## [0.137.3] - 2026-10-04

- **Saddle abilities go on slot 5, 6 or 7 (R, C, Ultimate).** Slot 3 is refused because the game never shows a key
  for it; existing slot-3 binds move to slot 5.
- `.beelz resetbar` while riding now dismounts you and tells you, instead of silently.

## [0.137.2] - 2026-10-03

- `.beelz unslot` in a beast form (Wolf, Bear, ...) puts your weapon skill back on that slot at once.
- Grants and unslots made while in a form, transformed or mounted apply as soon as that ends, with no weapon swap.

## [0.137.1] - 2026-10-03

- **Granted abilities show up on your bar at once.** Before, `.beelz grant` (and weapon grants, admin grants,
  presets, `refresh`) needed a weapon swap before the ability appeared and could be cast.
- `.beelz unslot` puts your weapon skill or spellbook spell back at once.

## [0.137.0] - 2026-09-30

- **`.beelz resetbar CONFIRM` clears a stuck action bar in one go**, without a weapon swap, and tells you whether the
  bar is back to normal. Captures, unlocks, hotkeys and presets are kept. It also repairs bars damaged by older
  versions.
- **Fixed:** Space, R, C and the ultimate breaking after a reset (lost dash, spells that would not drop onto a slot).
- **Fixed:** spellbook spells going blank on their key after a login or reset. Affected spells come back at next login.
- Beast forms keep their own abilities by default (`Forms_AutoFillFromCaptures = true` restores the old behaviour).
- Admin: `reset-loadouts` and `purge` use the same reset and work on offline players; new read-only
  `.beelz admin bar [player]` shows what is on each slot and why.

## [0.136.0] - 2026-09-23

- **New default ability rules from the Discord testing round:**
  - About 210 abilities ship with the tester's fix: unrooted casts, added cooldowns, damage adjustments, shorter
    launches, summon limits.
  - About 300 that could not be fixed are off by default; admins can turn them back on.
  - 10 more that crash the game or break characters or bases are blocked for good.
- Existing servers: `.beelz admin reseed replace CONFIRM` takes the new defaults (a backup is kept).
- A per-ability `cooldown` now works on abilities that have no cooldown of their own.

## [0.135.0] - 2026-09-23

- **Admin locks:** limit which abilities can share one bar, e.g.
  `.beelz admin lock add nocombo "Gargoyle WingShield, Rat Vanguard"`. None ship by default.
- **`.beelz admin reseed preview | merge | replace CONFIRM`** brings shipped default fixes to an existing server,
  keeping your own edits on `merge`.
- Safety timeouts on stuck-state abilities (still disabled by default).
- Fixed cooldown mix-ups when the same ability is on two slots, and on charge abilities.
