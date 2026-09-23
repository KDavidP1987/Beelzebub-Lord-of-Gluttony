# Beelzebub — Ability configuration guide (for server admins)

This is the one-stop reference for **controlling which abilities exist, how strong they
are, and how they behave** on your server. Everything here is editable before launch and
**live-reloadable** while the server runs.

There are **two** config surfaces:

| Surface | File | What it controls | Reload |
|---|---|---|---|
| **BepInEx config** | `BepInEx/config/kdpen.Beelzebub.cfg` | Global, server-wide switches & defaults | Restart, or it re-reads on some changes |
| **Ability rules** | `BepInEx/config/kdpen.Beelzebub/ability_rules.json` | Global deny/allow lists, global scaling defaults, and **per-ability** + **per-unit** overrides | `.beelz admin reload` (no restart) |

> The `ability_rules.json` file is created with sensible defaults the first time the
> server runs. Edit it, then run **`.beelz admin reload`** in chat to apply — no restart.

---

## 1. Quick recipes

- **"Let my testers try everything."** Default already does this: `Capture_InclusiveMode = true`
  and `Grant_EnforceTransformOnly = false`. Abilities across all V-Bloods/NPCs are capturable,
  devourable, and grantable to the normal bar.
- **"Curate a balanced server."** Set `Capture_InclusiveMode = false` (the deny lists +
  difficulty gate return) and, if you want some abilities reserved for transforms,
  `Grant_EnforceTransformOnly = true`.
- **"Disable one specific ability entirely."** Add an `AbilityMap` entry with `"Enabled": false`.
- **"Make all granted abilities hit 25% harder."** Set `Defaults.DamageScale = 1.25`.
- **"Nerf one ability's damage / change its cooldown."** Add an `AbilityMap` entry with
  `"DamageScale"` / `"CooldownScale"`.
- **"Restrict an ability to specific weapons."** Add `"Weapons": ["Reaper", "Sword"]` to its entry.
- **"These two abilities together break the game."** Lock them: `.beelz admin lock add nocombo "Ability A, Ability B"`
  — a player can then only have one of them on their active bar + hotkeys at a time (§6).
- **"Pick up the new shipped defaults without losing my edits."** `.beelz admin reseed preview`, then
  `.beelz admin reseed merge` (§7).

---

## 2. Global switches — `kdpen.Beelzebub.cfg`  (section `[Capture]`)

| Key | Default | Meaning |
|---|---|---|
| `CaptureOnKill` | `true` | Master switch for capturing abilities from kills. |
| `Capture_InclusiveMode` | `true` | **(v0.50)** When on, capture **and Devour** ignore `DenyPatterns`/`DenyGuids` and the Basic/Brutal difficulty gate — abilities across all V-Bloods/NPCs become broadly capturable for testing. A small hardcoded junk filter (idle/spawn/death stubs) and the per-ability `Enabled=false` kill-switch still apply. Turn **off** for a curated server. |
| `Grant_EnforceTransformOnly` | `false` | **(v0.50)** When off, the per-ability "transform-only" reservation is ignored, so every ability can be granted/slotted/hotkeyed/devoured to the normal bar. Turn **on** to honor the reservation (those abilities become usable only via an actual transform). |

Other relevant **global** keys:
- `DropChance_Ability_Regular` / `DropChance_Ability_VBlood` — per-ability capture chance.
- `DropChance_Devour_Regular` / `DropChance_Devour_VBlood` — **(v0.64, renamed from
  `DropChance_Transform_*`)** the rare "jackpot" Devour roll (grants a unit's whole kit at once;
  for Dracula/Morgana it unlocks transformation). Old keys are migrated to these automatically.
- `Capture_PityIncrementPerKill` / `Capture_PityMaxBonus` — bad-luck protection for **ability**
  captures. `Capture_PityIncrement_Devour` / `Capture_PityMax_Devour` — **(v0.64, new)** the same for
  the **Devour** jackpot, tunable separately (defaults to the ability values).
- `Server_DifficultyMode` (`Basic`/`Brutal` — only matters when `Capture_InclusiveMode = false`).
- `Grant_PowerScalingMode` + `Grant_PowerScalingFactor` — global granted-ability damage scaling
  (`PlayerScaled` = vanilla, or `Boosted` × factor).
- `Transform_MaxStacksPerSummonAbility` / `Transform_SummonLifetimeSeconds` / `Transform_SummonPowerFactor`
  — **global** dials that apply to **every** summon ability (transform summons *and* standalone
  captured summons, since v0.45), despite the `Transform_` prefix.
- `Abilities_ApplyConfig` **(default ON — master kill-switch, not an opt-in; renamed from
  `AbilityTuning_Enabled` in v0.66)**: when on, the per-ability config you set (cooldown, range,
  charges, interrupt, free-move, cast-speed, …) is applied by rewriting the abilities' baked prefab
  fields. Set false only to disable ALL baked ability-config edits. These edits are server-wide and
  also affect the source NPC/boss's copy of the ability (intended).

**Cooldowns (runtime since v0.134.0 — no game-data edit, bosses keep their own cooldowns):**
- `.beelz admin ability <name> cooldown <seconds>` — exact cooldown for a PLAYER's captured bar cast (`clear` to remove).
- `.beelz admin ability <name> cooldownscale <x>` — multiply the live cooldown instead (gear/buff cooldown
  reduction still counts, because the scale applies to the cooldown the game actually started).
- `Grant_MinimumCooldownSeconds` — **global** floor for captured bar casts (0 = off).
- Precedence per cast: `cooldown` if set, else live cooldown × `cooldownscale` (else `Defaults.CooldownScale`),
  then the floor. `.beelz cast` force-casts use the same math on their own tracker (minimum 1 s).
- **Abilities with no cooldown of their own (v0.136.0):** an absolute `cooldown` on such an ability is
  *started* by Beelzebub on the player's slot at cast time (the game would never start one), then re-applied
  for a few seconds if the game's cast-end clears it. Still player slot state only — no prefab edit.
- **Limits:** charge-based abilities are skipped (tune `charges` / `chargetime` instead); `cooldownscale` and the
  floor can only *raise* a cooldown the game starts (× 0 is still 0) — use an absolute `cooldown` for an ability
  with no cooldown; transforms keep their native cooldowns (their `CooldownScale` lives in `TransformMap`).
- **Shipped presets (v0.136.0):** the default rules carry the Discord tester baseline — ~210 abilities with a
  pre-set fix (`freelymove` / `cooldown` / `damagescale` / `leapheight` / summon limits), ~300 soft-disabled
  (`Enabled: false`, re-enable any time), and 10 more code hard-blocks. Each touched entry has a
  `[v0.136 baseline]` note. Regenerate with `tools/apply_tester_baseline.py --write`.
- **(v0.65) Range:** `.beelz admin ability <name> range <distance>` — max cast range (baked, `clear` to remove).

**(v0.67) More server-wide shaping** (also `Abilities_ApplyConfig`, default ON):
- `.beelz admin ability <name> charges <n>` / `chargetime <seconds>` — charge-based abilities
  (`AbilityChargesData` on the group prefab).
- `.beelz admin ability <name> aoe <radius>` — area-of-effect radius (`TargetAoE.MaxRange` on the
  ability's spawned AoE prefab, reached by walking Group→Cast→SpawnPrefab).
- `.beelz admin ability <name> projspeed <speed>` — projectile travel speed (`Projectile.Speed`,
  same walk). *(Note: a few abilities spawn deeply-nested or oddly-named projectiles the walker
  doesn't reach; those silently skip — verify in-game.)*
- Surfaced to BCH as `charges_override` / `chargetime_override` / `aoe_override` /
  `projspeed_override` (ApiVersion 13).

**(v0.68) Effect duration & healing** (also `Abilities_ApplyConfig`, default ON):
- `.beelz admin ability <name> duration <seconds>` — absolute duration of the buffs/debuffs the
  ability applies (`ApplyBuffOnGameplayEvent.OverrideDuration`).
- `.beelz admin ability <name> healing <multiplier>` — scale the ability's healing
  (`HealOnGameplayEvent`); 1.0 = unchanged. Computed from cached original values so reloads don't
  compound the multiplier.
- Surfaced to BCH as `duration_override` / `heal_mult` (ApiVersion 14).
- *(Reaches an ability's primary spawned effect; deeply-nested effects may not respond — verify in-game.)*

**(v0.133.0) Damage — section `[Damage]` in the .cfg:**

| Key | Default | Meaning |
|---|---|---|
| `Damage_Mode` | `Off` | `Off` = the legacy power window around a captured cast. `Telemetry` = also attribute every hit to the cast that caused it and count what per-hit scaling *would* do — changes nothing (`.beelz admin damage-stats`). `Scale` = per-hit: the power part of each attributed hit from a captured cast × (`Defaults` / ability `DamageScale`, × `Grant_PowerScalingFactor` when Boosted). No power window. |
| `Damage_MinFactor` / `Damage_MaxFactor` | `0` / `0` | Floor / cap on the final power factor of a scaled hit (0 = off). Min must be ≤ Max. |
| `Damage_FlatPolicy` | `Unchanged` | The flat (non-power) part of a hit: `Unchanged` or `Scaled`. %-of-max-HP damage is never scaled. |
| `Damage_ProvenanceRetentionSeconds` | `120` | How long a cast stays matchable to late-spawning damage sources (min 30). |
| `Summon_PowerMode` | `OwnerRelative` (new servers) / `Legacy` (existing) | `OwnerRelative` = a captured summon's power = your power × `Transform_SummonPowerFactor` × the ability's `summonpower`. `Legacy` = the unit's own stats × the factor. |

Damage always follows the caster's own Spell/Physical power (level, gear, potions); only PLAYER casts of
CAPTURED abilities are touched, so bosses stay vanilla. A hit that can't be tied to exactly one cast stays
vanilla. **Recommended rollout:** run `Telemetry` for a session, check `.beelz admin damage-stats`
(attribution should read *reliable*), then switch to `Scale`.

---

## 3. `ability_rules.json` — global section

```jsonc
{
  "Version": 1,

  // Capture filter (only consulted when Capture_InclusiveMode = false).
  "DenyPatterns": ["_Idle_", "_MeleeAttack_", "_Hard_", "..."],  // name substrings to block
  "DenyGuids":    [],                                            // specific ability GUIDs to block
  "AllowPatterns":[],                                            // if non-empty: ONLY these are capturable
  "AllowGuids":   [],                                            // if non-empty: ONLY these GUIDs are capturable

  // v0.50: server-wide default scaling for any ability with NO AbilityMap entry below.
  "Defaults": { "DamageScale": 1.0, "CooldownScale": 1.0 },

  // Transform-only reservation (only enforced when Grant_EnforceTransformOnly = true).
  "TransformOnlyPatterns": [],   // name substrings → ability is transform-only
  "TransformOnlyGuids":    [],   // specific ability GUIDs → transform-only

  // Per-ability capture-rate overrides (replaces the global DropChance_* for matching names).
  "DropRateOverrides": [
    { "Pattern": "_Dracula_", "RateRegular": 0.01, "RateVBlood": 0.02 }
  ],

  "AbilityMap": { /* per-ability — see §4 */ },
  "TransformMap": { /* per-unit transform tuning — see §5 */ },
  "ExclusionGroups": { /* incompatibility locks — see §6 (ships empty) */ }
}
```

- **Allow-lists win:** if `AllowPatterns` or `AllowGuids` is non-empty it becomes an exclusive
  whitelist (honored even in inclusive mode — it's a deliberate restriction).
- **`Defaults`** lets you set one global baseline instead of an entry per ability. A per-ability
  entry overrides it. `DamageScale` flows through the granted-cast power window (or per hit when
  `Damage_Mode=Scale`); `CooldownScale` multiplies the live cooldown of captured bar casts and `.beelz cast`
  force-casts (see the cooldown notes in §2).
- **Transform-only lists** mark abilities reservable for transforms. Resolution is
  per-ability `AbilityMap[...].TransformOnly` → `TransformOnlyGuids` → `TransformOnlyPatterns`
  (substring). All three are **only enforced** when `Grant_EnforceTransformOnly = true`.
- **`DropRateOverrides`** sets a per-ability capture chance (0–1) by name substring, overriding the
  global `DropChance_Ability_*` for matching abilities. First match wins; rates are clamped to 0–1.
- **Set these live:** `.beelz admin default <damagescale|cooldownscale> <value>`,
  `.beelz admin denyguid|allowguid <add|remove> <guid>`,
  `.beelz admin transformonly <add|remove> <pattern|guid>`. (`DropRateOverrides` is a JSON edit
  + `.beelz admin reload`.)

---

## 4. Per-ability config — `AbilityMap`

Key = the ability's exact prefab name (e.g. `AB_Blackfang_Morgana_CrossWindSlash_AbilityGroup`).
All fields optional; omit any you don't want to change.

```jsonc
"AbilityMap": {
  "AB_Vampire_Reaper_SpinSlash_AbilityGroup": {
    "Enabled": true,              // false = ability cannot be captured OR used (hard kill-switch)
    "DamageScale": 1.25,          // damage multiplier for granted casts (1.0 = no change)
    "CooldownScale": 0.8,         // cooldown multiplier (see note below)
    "Weapons": ["Reaper"],        // allowed weapon families; empty/absent = universal (any weapon)
    "TransformOnly": false,       // reserve for transforms only (enforced only if Grant_EnforceTransformOnly=true)
    "Difficulty": "Basic",        // "Basic" | "Brutal" (capture gate when not in inclusive mode)
    "Phase": 1,                   // multi-phase boss ability phase
    "AllowDenied": false,         // force past the deny lists (for curated abilities with deny-patterned names)

    // Cast tuning (requires Abilities_ApplyConfig = true). null/absent = leave the game's baked value.
    "Interruptible": true,        // true = dash/shield can cancel the cast
    "FreeMoveAfterCast": true,    // true = player can move the instant the cast finishes
    "CastMovementSpeed": 1.0,     // 0 = rooted during cast, 1 = full speed; null = baked default

    "Category": "Melee",          // override the BCH category badge; omit = auto from name
    "Notes": "admin annotation, not used at runtime"
  }
}
```

### Every field (generated from `Services/AbilityFieldTable.cs` — the one table the commands, validation and this doc share)

**Scope:** *Rule* = capture/availability/curation. *Runtime* = applies to PLAYER casts only, no game-data
edit, live immediately. *Baked* = a GLOBAL prefab edit re-applied by the tuner (also changes the source
NPC/boss cast). Numbers outside the range are rejected by the commands and ignored (with a warning) if
hand-edited into the JSON.

| Field | Aliases | Scope | Values | Meaning |
|---|---|---|---|---|
| `enabled` |  | Rule | on / off | on\|off — admin kill-switch (capture + use) |
| `weapons` |  | Rule | see meaning | weapon allow-list (sword,axe) and/or !blocks (!sword); 'any' clears |
| `forms` |  | Rule | see meaning | form allow-list (wolf) and/or !blocks (!mounted); 'any' clears |
| `transformonly` |  | Rule | on / off | on\|off — only usable while transformed |
| `difficulty` |  | Rule | see meaning | Basic\|Brutal capture gate |
| `phase` |  | Rule | see meaning | boss phase this ability belongs to (>=1) |
| `allowdenied` |  | Rule | on / off | on\|off — force-allow past deny patterns |
| `category` |  | Rule | see meaning | Travel\|Aoe\|Projectile\|Melee\|Summon\|Buff\|WeaponSpell\|Spell\|Other\|clear |
| `reviewstatus` | `review`, `status` | Rule | see meaning | Unreviewed\|Reviewed\|Approved\|Blocked\|Hidden |
| `reviewtag` | `tag`, `audittag` | Rule | see meaning | free-text audit tag |
| `notes` |  | Rule | see meaning | free-text note |
| `damagescale` |  | Runtime | 0.01 – 100 | damage multiplier on captured casts (1.0 = no change) |
| `cooldownscale` |  | Runtime | 0.01 – 100 | cooldown multiplier on captured casts (1.0 = no change) |
| `forcetimeout` | `effecttimeout`, `bufftimeout` | Runtime | 0 – 3600 | seconds — expire this ability's otherwise-indefinite buffs cast by a PLAYER (no prefab edit) |
| `powerwindow` | `powerwindowseconds`, `dmgwindow` | Runtime | 0 – 600 | seconds the granted-cast power buff lasts (0 = default 1.5s) |
| `summoncap` | `summonlimit`, `maxsummons` | Runtime | 0 – 100 | max simultaneous uses of this summon (0 = unlimited) |
| `summontimeout` | `summonlifetime`, `summonduration` | Runtime | 0 – 36000 | seconds before this ability's summons despawn (0 = never) |
| `summonpower` | `summonpowerscale` | Runtime | 0.01 – 100 | summon power multiplier (Summon_PowerMode=OwnerRelative: minion power = owner power × Transform_SummonPowerFactor × this) |
| `cooldown` | `cooldownseconds`, `cd` | Runtime | 0 – 3600 | absolute cooldown seconds on captured bar casts (wins over cooldownscale; still floored by Grant_MinimumCooldownSeconds) |
| `summonunits` | `summonunitspercast`, `unitspercast` | Runtime | 0 – 100 | max units one cast summons (0 = natural count) |
| `range` | `maxrange` | Baked | 0 – 500 | max cast range + projectile travel distance |
| `charges` | `maxcharges` | Baked | 0 – 100 | max charges (abilities that already use charges) |
| `chargetime` | `chargeuptime` | Baked | 0 – 3600 | recharge seconds per charge |
| `aoe` | `aoeradius`, `radius` | Baked | 0 – 100 | area-of-effect max radius |
| `projspeed` | `projectilespeed` | Baked | 0 – 500 | projectile speed |
| `leapheight` | `travelheight` | Baked | 0 – 1000 | leap/travel apex height (vanilla boss leaps ~250) |
| `duration` | `effectduration` | Baked | 0 – 3600 | applied buff/debuff duration seconds |
| `healing` | `healmult`, `healingmultiplier` | Baked | 0 – 100 | healing multiplier (1.0 = no change) |
| `interruptible` | `interrupt` | Baked | on / off / clear | on\|off\|clear — player can self-cancel the cast |
| `interruptonhit` | `interruptattack`, `breakonhit` | Baked | on / off / clear | on\|off\|clear — cast cancels when the caster is hit |
| `freemove` |  | Baked | on / off | on\|off — free movement when the cast finishes |
| `freelymove` | `freemovesecs`, `freemoveafter` | Baked | 0 – 60 | seconds into the cast before movement is freed |
| `castspeed` | `castmovementspeed` | Baked | 0 – 1 | move speed during the cast (0 = rooted .. 1 = full) |
| `maxstacks` | `stacks` | Baked | 1 – 255 | how many times the ability's own buff can stack (1-255) |
| `projcount` | `projectilecount`, `projectiles` | Baked | 1 – 16 | projectiles per volley on fan/multishot/cluster abilities (capped at 3x the ability's own count, max 16) |
| `knockback` | `knockbackscale` | Baked | 0 – 10 | knockback multiplier (distance + push time; 1.0 = no change, 0 = none) |
| `lifetime` | `spawnlifetime` | Baked | 0.1 – 600 | seconds the ability's projectiles/areas last (buff length is 'duration') |
| `casttime` | `casttimescale` | Baked | 0.1 – 10 | EXPERIMENTAL cast-time multiplier (windup + recovery; not channels/hold-to-cast/charges) |
| `allowglobalsharededit` | `sharededit` | Baked | on / off | on\|off — allow baked edits on prefabs SHARED with other abilities/bosses (only when the chain is fully decoded) |

**Shared-prefab guard (v0.132.0).** An ability's chain (group → casts → projectiles/areas/buffs/summons) is
mapped once at startup across *every* ability, bosses included. A *Baked* edit is **skipped** on any prefab
another ability also reaches, unless every ability that reaches it asks for the same value, or the entry sets
`allowglobalsharededit on` (which logs every other ability it changes). Conflicting values on a shared prefab
are rejected for all of them. A chain that couldn't be fully mapped never gets shared edits (no override).
`.beelz admin ability-inspect <ability>` shows the chain, the live numbers (incl. damage factors), what is
shared, and which writes were skipped.

**`forcetimeout` is runtime-only (v0.132.0).** It no longer edits game data: when a player's captured cast
spawns a buff with no finite lifetime, that buff instance is removed after N seconds. A buff that could also
come from another ability the player just cast is left alone (never guessed). The previous build's
prefab-level timeout is cleared by the server restart that installs this version.

**Stuck-state abilities ship disabled *with* a safety timer (v0.135.0).** The Fall Asleep family (8 s),
Cassius's High Lord Leap Strike (4 s) and Gaius's Corpse Buff ability (6 s) stay `Enabled=false`, but now
carry a `forcetimeout`, so an admin who deliberately enables one gets the timer too. Run
`.beelz admin reseed merge` to pick this up on an existing server.

**New in v0.134.0 — `maxstacks`, `projcount`, `knockback`, `lifetime`, `casttime`.** Baked, near-chain only,
from captured originals, under the shared-prefab guard. `projcount` works on fan / multishot / cluster
patterns and is capped at 3× the ability's own count (max 16). `lifetime` touches projectiles and hit areas
only — a buff's length is `duration`. `casttime` is **experimental** and refuses channels, hold-to-cast and
charge abilities (the command says why).

**Known limit — Elena's Tower of Frost `leapheight`:** its launch buff is shared with the Overseer boss, so
the shared-prefab guard skips it and the tune has no effect. `ability-inspect` shows the skip.

**Set any field live, no file editing:**
```
.beelz admin ability <name|id> <field> <value> [<field> <value> ...]   (up to 5 pairs; omit to READ)
.beelz admin tune <name|id> <knob> <value|clear>                        (shaping knobs only)
```
Lists take a comma value or `any` to clear (e.g. `weapons Reaper,Sword`, `forms !mounted`); toggles take
`on|off`; numeric/tri-state fields take `clear` to unset. Examples:
```
.beelz admin ability AB_Vampire_Reaper_SpinSlash_AbilityGroup enabled off
.beelz admin ability AB_Some_Spell_AbilityGroup weapons !Sword
.beelz admin tune AB_BatVampire_SummonMinions_AbilityGroup leapheight 30
.beelz admin ability-inspect AB_Some_Spell_AbilityGroup
.beelz admin ability-inspect export          (every ability → inspect_export.csv)
```
**After a restart:** baked edits are re-applied at startup from `ability_rules.json`. Lowering or
clearing a baked field restores the shipped value and re-applies all rules live (`reload`, `tune`,
`ability … defaults`); a restart always guarantees a clean baseline.

Other in-game shortcuts:
- `.beelz admin deny|undeny|allow|unallow <pattern>` — name-pattern capture filters.
- `.beelz admin denyguid|allowguid <add|remove> <guid>` — GUID capture filters.
- `.beelz admin transformonly <add|remove> <pattern|guid>` — bulk transform-only reservation.
- `.beelz admin default <damagescale|cooldownscale> <value>` — the global `Defaults` block (§3).
- `.beelz admin tune <ability> <knob> <value|clear>` — shaping shortcut (any Baked/Runtime knob above).
- `.beelz admin reload` — re-read the file (for hand-edits) and re-apply cast tuning.

All command edits persist to `ability_rules.json` immediately (no reload needed); `reload` is only
for picking up hand-edits or re-applying cast tuning after toggling `Abilities_ApplyConfig`.

---

## 5. Per-unit transform tuning — `TransformMap`

Key = the unit's `CHAR_*` prefab name. Governs how a transform (Dracula / Morgana) behaves —
independent of the per-ability `AbilityMap` (which governs abilities on the normal bar).

| Field | Type | Default | Effect |
|---|---|---|---|
| `Enabled` | bool | `true` | Kill-switch for unlocking/activating this transform. |
| `Difficulty` | string | `"Basic"` | `Basic`/`Brutal` gate vs. `Server_DifficultyMode`. |
| `Tier` | int | `1` | Display/curation tier. |
| `DamageScale` / `CooldownScale` / `HealthScale` / `MovementSpeedScale` | float | `1.0` | Stat multipliers while transformed (used when `PowerScalingMode = CuratedScales`). |
| `FullReplace` | bool | `false` | Force the native shapeshift visual + lock weapon swap. |
| `PowerScalingMode` | string | unset | Per-unit override: `CuratedScales`/`PrefabAbsolute`/`PlayerScaled`/`PlayerLeveled`. Unset = inherit the global `Transform_PowerScalingMode`. |
| `SlotTemplate` | object | unset | Per-slot ability override for the transformed bar: `{ "1": "AB_...", "5": "AB_..." }` (keys = slots 1–6, values = ability-group prefab names). Edited in the JSON file (the slot-by-slot editor isn't a chat command). |

**Set the scalar fields live:**
```
.beelz admin transform-set <CHAR_unit> <field> <value>
```
`field` = `enabled`, `difficulty`, `tier`, `damagescale`, `cooldownscale`, `healthscale`,
`speedscale`, `fullreplace`, `powerscalingmode` (or `inherit` to clear), `notes`. `SlotTemplate`
remains a JSON edit (then `.beelz admin reload`).

---

## 6. Incompatibility locks — `ExclusionGroups` (v0.135.0)

Some ability *combinations* break balance even when each ability is fine alone. A **lock group** says
"at most **Max** of these abilities may be in one player's active loadout at once". None ship — which
combinations to lock is your call.

```jsonc
"ExclusionGroups": {
  "nocombo":   { "Max": 1, "Members": ["AB_Gargoyle_WingShield_AbilityGroup", "830495620"], "Notes": "immortality stack" },
  "onesummon": { "Max": 1, "Members": ["cat:Summon"] }
}
```

- **Members** are ability prefab names, numeric IDs, or `cat:<Category>` (Travel, Aoe, Projectile,
  Summon, Buff, WeaponSpell, Spell, Melee, Other — the same category `ability-inspect` / the catalog shows,
  including your `category` overrides). A member that doesn't resolve is kept in the file, warned about in the
  log, and ignored.
- **Active loadout** = the bar that's actually live (the form bar in a form, the saddle bar when mounted,
  otherwise your weapon bar) in slot order, then your hotkeys in name order. **Earlier wins**; the same
  ability bound twice counts once; an ability in several groups must fit in all of them. Transforms are exempt.
- **Scope: Beelzebub-granted abilities only.** Vanilla spells/weapon skills you pick in the game's own spellbook
  aren't counted (a `cat:` group counts only granted abilities). Admin `set-slot` is an override: the bind is saved
  with a warning, and the lock still keeps it off the live bar.
- **Enforcement:** `grant` / `weapon-grant` / `form-grant` / `hotkey set` refuse a bind that would be
  locked; every bar resolve (login, weapon swap, form/mount entry, reload) keeps a locked ability off the bar
  (the slot shows its normal ability); `.beelz cast` refuses a locked ability. The player gets a 🔒 message
  naming the group and what it's holding, and 🔓 when it comes back. **Saved binds are never deleted** —
  remove the lock (or the other ability) and it returns by itself. Admin `set-slot` still saves the bind and
  warns that it's locked.

```
.beelz admin lock add <group> "<member, member, ...>"   (new groups start at max 1)
.beelz admin lock max <group> <n>
.beelz admin lock remove <group> [member]
.beelz admin lock list
.beelz admin lock check <player>
```
Every lock command saves the file and re-checks every online player immediately. BloodCraftHub reads the
groups with `.beelz api locks` (ApiVersion 32).

---

## 7. Picking up new shipped defaults — `.beelz admin reseed` (v0.135.0)

`ability_rules.json` is seeded from the shipped default only when it doesn't exist, so later curation fixes
didn't reach existing servers. `reseed` closes that gap:

| Command | Effect |
|---|---|
| `.beelz admin reseed preview` | Dry run: what a merge would add / adopt / remove, and any conflicts. Writes nothing. |
| `.beelz admin reseed merge` | 3-way merge against the default you were last seeded from (`ability_rules.baseline.json`): shipped changes land where you never changed the old default; your edits are kept; if both changed, **yours wins** and it's listed as a conflict. |
| `.beelz admin reseed replace CONFIRM` | Overwrite with the shipped default — your curation is lost. |

- Servers seeded before v0.135.0 have no baseline, so the first `merge` runs in **SAFE mode**: it only adds
  entries you don't have and adopts shipped `Enabled=false` crash-blocks. Afterwards the baseline exists and
  later merges are full 3-way.
- Every write keeps a timestamped backup (`ability_rules.json.bak-YYYYMMDD-HHMMSS`), is validated before it
  replaces the file, restores the backup on failure, and then reloads + re-applies tuning and locks.
- Two server keys naming the same ability → the merge is refused until you remove one.
- Don't hand-edit `BaselineHash` in the file; it ties the file to its baseline.

---

## 8. Precedence summary

1. **Capture eligible?** allow-list → (inclusive mode? junk filter : deny lists + difficulty) → `Enabled`.
2. **Grantable to the bar?** `Enabled` → (`Grant_EnforceTransformOnly` && `TransformOnly`).
3. **On which weapon bar?** explicit weapon-bucket placement is honored as-is; the universal
   bucket is filtered by `Weapons` (`Magic`/empty = any).
4. **Locked out?** the active loadout is resolved against `ExclusionGroups` (§6) — a locked ability stays
   off the live bar and can't be force-cast.
5. **How strong?** per-ability `DamageScale`, else `Defaults.DamageScale`, × `Grant_PowerScalingFactor`
   when Boosted — through the power window (`Damage_Mode=Off`) or per hit (`Scale`).
6. **How long the cooldown?** per-ability `cooldown`, else live cooldown × (`cooldownscale` or
   `Defaults.CooldownScale`), then `Grant_MinimumCooldownSeconds` (captured bar casts only).
