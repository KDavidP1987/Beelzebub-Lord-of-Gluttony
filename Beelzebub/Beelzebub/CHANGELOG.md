# Changelog

What's new for players. For full implementation history, see the
[developer changelog on GitHub](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/blob/main/CHANGELOG.md).

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
