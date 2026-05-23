# Ability Map Format

How to curate which weapons and shapeshift forms each captured ability is compatible with, and which abilities are reserved for transform-only use.

## Where the file lives

The runtime rules file is created automatically on first server boot at:

```
C:\Program Files (x86)\Steam\steamapps\common\VRisingDedicatedServer\BepInEx\config\kdpen.Beelzebub\ability_rules.json
```

(On a server you don't host yourself, it's `<install root>\BepInEx\config\kdpen.Beelzebub\ability_rules.json`.)

You can hand-edit this file when the server is **stopped**, then start the server. To reload while the server is running, use the in-chat command:

```
.beelz admin reload
```

Editing in Program Files needs administrator privileges. If that's annoying, copy the file out, edit, and copy it back.

## What the file looks like (overall shape)

```json
{
  "Version": 1,
  "DenyPatterns":  ["_Idle_", "_Flee_", "_MeleeAttack_", ...],
  "AllowPatterns": [],
  "DenyGuids":     [],
  "AllowGuids":    [],
  "DropRateOverrides": [],
  "AbilityMap": {
    "<ability prefab name>": { "Weapons": [...], "Forms": [...], "TransformOnly": false, "Notes": "" }
  },
  "TransformOnlyPatterns": [],
  "TransformOnlyGuids":    [],
  "TransformMap": {
    "<CHAR_ prefab name>": { "Enabled": true, "Difficulty": "Basic", "Tier": 3, "Notes": "" }
  }
}
```

The bits you'll actually be editing are `AbilityMap` (per-ability matrix), `TransformMap` (per-unit transformation matrix — TX1), and `TransformOnlyPatterns` / `TransformOnlyGuids` (bulk substring/GUID rules for transform-only abilities).

## The matrix — `AbilityMap`

Each ability gets one entry, keyed by its exact prefab name (no quotes around the key text in JSON syntax; **JSON does** require the surrounding quotes — see examples below).

| Field | Type | Meaning |
|---|---|---|
| `Weapons` | array of strings | Weapon families this ability is allowed to slot into. Empty / missing = universal (works with any weapon). |
| `Forms` | array of strings | Shapeshift forms this ability is restricted to. Empty / missing = not form-restricted. |
| `TransformOnly` | bool | `true` = `.beelz grant` will refuse to bind this ability. Only fires while transformed. |
| `Enabled` | bool (default `true`) | Admin kill-switch. `false` = blocks both capture on kill AND `.beelz grant` / transform pickup. The ability vanishes from the mod entirely without affecting NPC casters. |
| `DamageScale` | float (default `1.0`) | Multiplier on damage when the ability is cast from a Beelzebub-granted slot. **Currently inert** — value is stored but takes no effect until the W5 runtime ships. Curate now; runtime catches up. |
| `CooldownScale` | float (default `1.0`) | Multiplier on cooldown for Beelzebub-granted casts. Same inert-for-now caveat as DamageScale. |
| `Notes` | string | Free-text annotation — ignored by the mod, for your own reference. |

### Valid `Weapons` values

`Magic`, `Unarmed`, `Sword`, `GreatSword`, `Axe`, `Mace`, `DualHammers`, `Spear`, `Daggers`, `Crossbow`, `Longbow`, `Pistols`, `Reaper`, `Whip`, `Claws`

Case-insensitive in the parser. `"Sword"` and `"sword"` both work.

### Valid `Forms` values

`Wolf`, `Bear`, `Rat`, `Spider`, `Toad`

These are the 5 native V Rising shapeshift forms Beelzebub maps captured units onto (see "What to test" notes in the README).

## Examples

```json
"AbilityMap": {
  "AB_Paladin_MeleeAttack_AbilityGroup": {
    "Weapons": ["Sword", "GreatSword"],
    "Forms": [],
    "TransformOnly": false,
    "Enabled": true,
    "DamageScale": 1.0,
    "CooldownScale": 1.0,
    "Notes": "Physical melee — works with any sword-family weapon"
  },

  "AB_Vampire_Dracula_BloodBoltSwarm_AbilityGroup": {
    "Weapons": ["Magic"],
    "Forms": [],
    "TransformOnly": true,
    "Enabled": true,
    "DamageScale": 0.6,
    "CooldownScale": 1.5,
    "Notes": "Boss signature — kept enabled but nerfed for balance (once W5 ships)"
  },

  "AB_Solarus_HolyPillar_AbilityGroup": {
    "Weapons": [],
    "Forms": [],
    "TransformOnly": false,
    "Enabled": false,
    "Notes": "Removed from the mod entirely — pillar broke a server"
  },

  "AB_Paladin_HolyNuke_AbilityGroup": {
    "Weapons": ["Magic"],
    "Forms": [],
    "TransformOnly": false,
    "Notes": "Holy spell — universal spell slot"
  },

  "AB_Shapeshift_Wolf_Bite_Bleed_AbilityGroup": {
    "Weapons": [],
    "Forms": ["Wolf"],
    "TransformOnly": true,
    "Notes": "Wolf bite — only fires while in Wolf form"
  },

  "AB_Solarus_HolyPillar_AbilityGroup": {
    "Weapons": [],
    "Forms": [],
    "TransformOnly": true,
    "Notes": "Too OP for normal grants — transform-only"
  },

  "AB_Bandit_Hunter_Bow_Group": {
    "Weapons": ["Longbow", "Crossbow"],
    "Forms": [],
    "TransformOnly": false,
    "Notes": "Generic ranged shot — fits any bow-like weapon"
  }
}
```

## The transformation matrix — `TransformMap` (TX1 + TX6)

Mirrors `AbilityMap` but for the CHAR_ units a player can transform into. Key = exact CHAR_ prefab name (typically `CHAR_*_VBlood` for V-Bloods).

| Field | Type | Meaning |
|---|---|---|
| `Enabled` | bool (default `true`) | Admin kill-switch. `false` blocks transform unlock rolls AND blocks `.beelz transform` activation even for players who already have the unlock. Surgical per-unit removal of broken or undesired transforms. |
| `Difficulty` | string (default `"Basic"`) | Either `"Basic"` or `"Brutal"`. Enforced by TX4 — a `Brutal` transform can't be activated on a `Basic` server. |
| `Tier` | int (default `1`) | 1-5 power tier the admin assigns. Display + curation use; not directly applied at runtime — use the `*Scale` fields below to bind tier to actual gameplay effect. |
| `DamageScale` | float (default `1.0`) | **TX6, v0.15.0**. While transformed, scales **both** `PhysicalPower` and `SpellPower`. `1.0` = no change, `1.5` = +50% damage, `0.5` = -50%. Implemented via `ModifyUnitStatBuff_DOTS` on the carrier buff — the bonus auto-clears on revert. |
| `CooldownScale` | float (default `1.0`) | **TX6, v0.15.0**. Scales both spell and weapon cooldown recovery. `1.0` = no change. `>1.0` = **longer** cooldowns (admin nerf — slower recovery). `<1.0` = shorter cooldowns. Internally maps to recovery-rate delta `(1/CooldownScale) - 1`. |
| `HealthScale` | float (default `1.0`) | **TX6, v0.15.0**. Scales MaxHealth while transformed. |
| `MovementSpeedScale` | float (default `1.0`) | **TX6, v0.15.0**. Scales movement speed while transformed. |
| `FullReplace` | bool (default `false`) | **TX3, v0.16.0**. Mark this transform as "be the NPC" mode. Force-enables the native shapeshift visual (Wolf/Bear/Rat/Spider/Toad when the unit name matches) regardless of the global `Transform_NativeShapeshift_Enabled` config. **TX8, v0.17.0**: also adds V Rising's native `BlockEquipmentSwapping` component — the player cannot swap weapons mid-transform until they revert. **Known limit**: the native shapeshift visual still drops on first off-form cast (V Rising vanilla; not fixable server-side). FullReplace gives you ~1-3 seconds of model swap plus the stat profile + locked weapon, not a permanent transformation. |
| `PowerScalingMode` | string (default empty = inherit global) | **TX7, v0.17.0**. Per-transformation power-scaling source. One of `"CuratedScales"`, `"PrefabAbsolute"`, or `"PlayerScaled"`. Empty/missing = inherit the global `Transform_PowerScalingMode` config. See the **Power Scaling Modes** section below for what each value does. Case-insensitive on load; typo'd values fall back to the global default. |
| `Notes` | string | Free-text annotation; surfaced via `.beelz api transforms`. |

### Example

```json
"TransformMap": {
  "CHAR_Villager_Tailor_VBlood": {
    "Enabled": true,
    "Difficulty": "Basic",
    "Tier": 4,
    "Notes": "Beatrice the Tailor — phase shift to Gargoyle is glitchy, monitor for crashes."
  },
  "CHAR_Forest_Wolf_VBlood": {
    "Enabled": true,
    "Difficulty": "Basic",
    "Tier": 2,
    "DamageScale": 0.8,
    "MovementSpeedScale": 1.25,
    "FullReplace": true,
    "Notes": "Alpha Wolf — fast, slightly weaker damage to compensate. FullReplace: you BECOME the wolf (visual + spell bar + stats) until first cast drops the form."
  },
  "CHAR_Vampire_Dracula_VBlood": {
    "Enabled": true,
    "Difficulty": "Brutal",
    "Tier": 5,
    "DamageScale": 0.5,
    "CooldownScale": 1.5,
    "HealthScale": 0.7,
    "Notes": "Dracula transform — heavy admin nerf so it's not a one-button wipe button."
  }
}
```

Setting `Enabled: false` is still the cleanest kill-switch for transforms you don't want available at all. Use the `*Scale` fields for balance dials when you want the transform available but tuned.

**Stat-scale notes**:

- Scales are applied via a `ModifyUnitStatBuff_DOTS` buffer on Beelzebub's carrier buff. When `.beelz revert` (or auto-revert) destroys the buff, all bonuses lift cleanly — V Rising recomputes the player's stats from the union of remaining buffs. No leftover state.
- `0.0` or negative scales are clamped to `1.0` on load (no change) so a typo can't zero out a player's health or damage.
- `1.0` is a no-op — those modifiers are skipped, so the carrier buff stays slim if you don't curate scaling.
- The `Tier` field is purely admin-facing — it does NOT auto-derive scales. You're expected to set the explicit scale fields if you want gameplay effect.

## Power scaling modes (TX7, v0.17.0)

The four `*Scale` fields above only apply when the transform is in **CuratedScales** mode (the default). The runtime supports two other modes that source power from elsewhere — pick the one that fits your server.

Set globally in `BepInEx/config/kdpen.Beelzebub.cfg`:
```ini
[Transformation]
Transform_PowerScalingMode = CuratedScales   # or PrefabAbsolute or PlayerScaled
```

Override per-transformation via the `PowerScalingMode` field on a TransformMap entry. Per-entry value wins.

### `CuratedScales` (default)

Apply the four `*Scale` fields as `ModifyUnitStatBuff_DOTS` overlays. The player's natural `PhysicalPower` / `SpellPower` still scale the ability's base damage on top. Use when you want fine-grained per-unit control of how powerful each transform feels.

### `PrefabAbsolute`

Read the CHAR_ prefab's `UnitStats` component (PhysicalPower, SpellPower, PhysicalResistance, SpellResistance) plus the `Health` component's MaxHealth, and apply matching boss-tier values to the player via `ModificationType.Add`. The player effectively *becomes* the boss statistically — Bishop's `PhysicalPower` is your `PhysicalPower` while transformed. Ignores player progression.

Notes:
- The `*Scale` fields are **ignored** in this mode — admins who want tuning on top of PrefabAbsolute should layer it as a separate Bloodcraft-style buff or use CuratedScales instead.
- Crit chance/damage are not exposed cleanly on `UnitStats`, so PrefabAbsolute does not include those. Layer them via CuratedScales if needed.

Use for "true boss form" servers where the transformation is meant to feel overpowering.

### `PlayerScaled`

Apply **nothing**. The captured ability uses V Rising's vanilla damage formula, which multiplies the ability's base damage by the player's own `PhysicalPower`/`SpellPower`. That includes any Bloodcraft expertise / blood-quality / level buffs the player carries. The ability auto-rescales as the player levels up, prestiges, swaps blood quality, or equips different gear — no Beelzebub intervention.

This is the **Bloodcraft-prestige-compatible** mode. When a player prestiges and their stats drop, the captured abilities drop in effective output proportionally. Use for servers running Bloodcraft where balance should follow player progression.

### Picking a mode (quick guide)

| Your server | Recommended mode |
|---|---|
| Vanilla V Rising or Bloodcraft, casual PvE | `PlayerScaled` |
| Bloodcraft + you want prestige to matter | `PlayerScaled` |
| Solo / coop server, you want curated per-V-Blood balance | `CuratedScales` |
| "Boss-form" RP server where transforms = legitimate boss fights | `PrefabAbsolute` |
| Mixed needs | Set global to `PlayerScaled`, then per-entry override to `PrefabAbsolute` for the specific bosses you want to feel cinematic |

## Bulk rules (for the long tail)

Sometimes you want a rule that hits dozens of abilities. The classifier already does name-substring heuristics for common patterns (`*_Sword_*` → Sword, `*_Crossbow_*` → Crossbow, etc.), so you only need `AbilityMap` for **overrides**.

For transform-only, use these collections if you have a substring/GUID pattern that catches a group:

```json
"TransformOnlyPatterns": ["_Ultimate_VBlood_", "_Solarus_Pillar_"],
"TransformOnlyGuids":    [123456789, -987654321]
```

Substring match is case-insensitive. GUID is the integer hash you'd see in the prefab filename or via `.beelz api info`.

## How the runtime resolves it

When the cascade is implemented (task W2):

1. `AbilityMap[name]` is the highest-priority source — admin curation wins.
2. If the ability has no entry in `AbilityMap`, the name-substring classifier picks a single weapon family.
3. If neither yields a match, the ability is treated as `Magic` (universal).

`TransformOnly` is resolved in the same order: matrix entry first, then `TransformOnlyGuids`, then `TransformOnlyPatterns`. Any of them true = transform-only.

## Sanity checks

- The file must remain valid JSON. Trailing commas break it; matching brackets and quotes matter. If `.beelz admin reload` errors, the runtime falls back to defaults and logs the error to `BepInEx\LogOutput.log`.
- Adding an entry for an ability that doesn't exist in the game is harmless — it just never matches.
- Removing an entry is safe — the heuristic classifier picks up the slack.
- You can ship a partial file. Empty `AbilityMap: {}` is fine.

## Starter file

A blank starter is at `Beelzebub/docs/ability_map.starter.json` in the source tree. Copy it into your server's `BepInEx\config\kdpen.Beelzebub\` directory if you want a fresh start, or merge entries from it into your existing `ability_rules.json`.
