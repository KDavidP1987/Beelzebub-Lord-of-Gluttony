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

**(v0.65) Per-ability absolute cooldown & range** (require `Abilities_ApplyConfig` — baked, GLOBAL edits):
- `.beelz admin ability <name> cooldown <seconds>` — set an exact cooldown (`clear` to remove).
- `.beelz admin ability <name> range <distance>` — set the max cast range (`clear` to remove).
- (Shortcuts: `.beelz admin tune <name> cooldown <s>` / `... range <d>`.)
- `Grant_MinimumCooldownSeconds` — **global** floor applied to every ability's cooldown (0 = off).
- These also surface to BloodCraftHub as `cooldown_override=` / `range_override=` on `api info` and
  `catalog-ability` (ApiVersion 12).

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

> **Coming next (multi-ability rules):** *stack limits* (per-ability summon cap), *incompatible-ability
> locks* (block conflicting binds), and *chain triggers* (one ability auto-firing another).

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
  "TransformMap": { /* per-unit transform tuning — see §5 */ }
}
```

- **Allow-lists win:** if `AllowPatterns` or `AllowGuids` is non-empty it becomes an exclusive
  whitelist (honored even in inclusive mode — it's a deliberate restriction).
- **`Defaults`** lets you set one global baseline instead of an entry per ability. A per-ability
  entry overrides it. `DamageScale` flows through the granted-cast power window; `CooldownScale`
  flows through `.beelz cast` force-casts (see the cooldown note in §4).
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

| Field | Type | Default | Effect |
|---|---|---|---|
| `Enabled` | bool | `true` | **Hard kill-switch.** `false` blocks both capture and use, always. |
| `DamageScale` | float | `1.0` | Granted-cast damage multiplier (× the global `Grant_PowerScalingFactor` when `Boosted`). |
| `CooldownScale` | float | `1.0` | Cooldown multiplier. **Applies to `.beelz cast` force-casts today;** native spell-bar slot cooldowns remain the game's own (a deeper hook is tracked). |
| `Weapons` | string[] | `[]` | Allowed weapon families. Empty = universal. Valid: Sword, GreatSword, Axe, Mace, Spear, Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe, Slashers, TwinBlades, Unarmed, FishingPole, Magic. |
| `TransformOnly` | bool | `false` | Reserve for transforms — **only enforced when `Grant_EnforceTransformOnly = true`**. |
| `Difficulty` | string | `"Basic"` | Capture gate vs. `Server_DifficultyMode` (ignored in inclusive mode). |
| `Phase` | int | `1` | Boss multi-phase grouping for transform loadouts. |
| `AllowDenied` | bool | `false` | Force this ability past the deny lists (still honors `Enabled`). |
| `Interruptible` / `FreeMoveAfterCast` / `CastMovementSpeed` | bool?/bool/float? | unset | Cast tuning (needs `Abilities_ApplyConfig`). ⚠ edits the ability's **shared** cast data, so the source NPC/boss cast changes too. |
| `Category` | string | unset | Override the BCH category badge (`Travel`/`Aoe`/`Projectile`/`Melee`/`Summon`/`Buff`/`WeaponSpell`/`Spell`/`Other`). Omit = auto-classify from the name. |

**Set any of these live, no file editing (v0.53.0):**
```
.beelz admin ability <name> <field> <value>
```
`field` is any column above: `enabled`, `weapons`, `forms`, `transformonly`, `difficulty`,
`phase`, `allowdenied`, `damagescale`, `cooldownscale`, `category`, `interruptible`, `freemove`,
`castspeed`, `notes`. Lists take a comma value or `any` to clear (e.g. `weapons Reaper,Sword`);
toggles take `on|off`; `interruptible`/`castspeed`/`category` take `clear` to unset. Examples:
```
.beelz admin ability AB_Vampire_Reaper_SpinSlash_AbilityGroup enabled off
.beelz admin ability AB_Blackfang_Morgana_CrossWindSlash_AbilityGroup category Melee
.beelz admin ability AB_Some_Spell_AbilityGroup weapons Reaper,Sword
.beelz admin ability AB_Some_Spell_AbilityGroup damagescale 1.25
```
Other in-game shortcuts:
- `.beelz admin deny|undeny|allow|unallow <pattern>` — name-pattern capture filters.
- `.beelz admin denyguid|allowguid <add|remove> <guid>` — GUID capture filters.
- `.beelz admin transformonly <add|remove> <pattern|guid>` — bulk transform-only reservation.
- `.beelz admin default <damagescale|cooldownscale> <value>` — the global `Defaults` block (§3).
- `.beelz admin tune <ability> <interrupt|freemove|castspeed> <on|off|0..1>` — cast-tuning shortcut.
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

## 6. Precedence summary

1. **Capture eligible?** allow-list → (inclusive mode? junk filter : deny lists + difficulty) → `Enabled`.
2. **Grantable to the bar?** `Enabled` → (`Grant_EnforceTransformOnly` && `TransformOnly`).
3. **On which weapon bar?** explicit weapon-bucket placement is honored as-is; the universal
   bucket is filtered by `Weapons` (`Magic`/empty = any).
4. **How strong / how long the cooldown?** per-ability `DamageScale`/`CooldownScale`, else
   `Defaults.*`, then the global `Grant_PowerScaling*`.
