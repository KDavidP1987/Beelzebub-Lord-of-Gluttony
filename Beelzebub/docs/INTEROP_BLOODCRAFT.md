# Bloodcraft coexistence audit (IN1)

How Beelzebub (`kdpen.Beelzebub`) interacts with Bloodcraft (`io.zfolmt.Bloodcraft`)
when both are loaded on the same dedicated server. Tested against **Bloodcraft v1.13.21**
and **Beelzebub v0.15.0**.

**TL;DR**: They coexist cleanly out of the box. There's exactly one config knob
to flip if you want them perfectly out of each other's way (Bloodcraft's
`ShiftSlot`). Everything else either operates on different surfaces or composes
naturally.

---

## Shared Harmony patch surfaces

Both mods patch the same V Rising server systems. None of the overlaps cause
direct conflict because all the relevant patches are `Postfix` or
`Prefix-no-skip` (neither mod blocks the original method, neither mod cancels
the other's work).

### 1. `DeathEventListenerSystem.OnUpdate` — both Postfix

| Mod | Purpose | Patch type |
|---|---|---|
| Bloodcraft | Leveling XP, expertise (weapon), legacy (blood), familiar unlock, professions, quest progress | Postfix |
| Beelzebub  | Ability capture roll + transform-unlock roll on regular-mob kills | Postfix |

Both iterate `__instance._DeathEventQuery` independently. Neither mutates the
death events. Patch execution order (alphabetical by Harmony ID) is
**Bloodcraft → Beelzebub**, but order doesn't matter here — both are pure
side-effect consumers.

**Conflict**: none. Same kill triggers both mods' bookkeeping.

### 2. `ReplaceAbilityOnSlotSystem.OnUpdate` — both Prefix, both add to the buffer

This is the only overlap that's worth thinking about. Both mods inject
`ReplaceAbilityOnSlotBuff` entries onto the player's `EquipBuff_Weapon_*`
entity to make captured/class abilities show up on the spell bar.

| Mod | Slots it touches | Trigger |
|---|---|---|
| Bloodcraft | Slot 3 (`ShiftSlot` class spell), Slots 1 + 4 (`UnarmedSlots` extra spells while unarmed/fishingpole), full overwrite when not wielding a weapon (`SetSpells`) | When the player has class abilities and the config flags are on |
| Beelzebub  | Any of slots 1–6 the player has bound via `.beelz grant <slot> <index>` (universal) or `.beelz weapon-grant <weapon> <slot> <index>` (per-weapon) — gated by weapon-family compatibility | When the player has saved Beelzebub grants |

Both prefixes run in the same frame. Each calls `buffer.Add(...)` for its own
slot bindings. The buffer can carry multiple entries for the same slot —
V Rising's resolver picks the **last entry added with the highest `Priority`**
(both mods use `Priority = 0`, so last-in-wins for ties).

Patch order: Bloodcraft prefix runs first, Beelzebub prefix second. **If both
mods bind the same slot, Beelzebub wins** (its entry was added more recently
to the buffer). That's usually the right behavior — players are explicitly
choosing the Beelzebub bind by typing the chat command.

**Important wrinkle (Beelzebub v0.14.0+)**: while a player is transformed
(`.beelz transform <unit>`), Beelzebub's Prefix early-returns and adds nothing
to the EquipBuff buffer. Transform slot overrides live on a dedicated carrier
buff (`Buff_VBlood_Ability_Replace`) with `Priority = 99`, which wins over
both Bloodcraft AND Beelzebub's own grant injection. So during a transform,
Bloodcraft's class/shift spells are visually masked by the transform — they
return on revert.

#### Recommendations

- **Slot 3 (`Q` ability)**: if you bind slot 3 with `.beelz grant 3 …`,
  Bloodcraft's class shift spell will be in the buffer too but will lose
  on the priority tie. To avoid an invisible-but-loaded conflict, either:
  - Set Bloodcraft `ShiftSlot = false` in the Bloodcraft config, OR
  - Just leave it — Beelzebub wins. Bloodcraft's class abilities are
    still available through Bloodcraft's other mechanisms (e.g. its own
    cast triggers).
- **Slots 1 + 4 unarmed**: only relevant if you fight unarmed. Bloodcraft's
  `UnarmedSlots = true` will compete with any Beelzebub bind on slots 1/4.
  Same priority-tie rule — Beelzebub wins. Recommend Bloodcraft
  `UnarmedSlots = false` if you're using Beelzebub for the unarmed kit.
- **Slots 5/6 (spell slots)**: Bloodcraft's `SetSpells` only fires on the
  unarmed/fishingpole equip-buff, not weapon equip-buffs. Beelzebub's grants
  win cleanly on weapons. No flip needed.

### 3. `VBloodSystem.OnUpdate` — both Prefix on feed-kills

| Mod | Purpose |
|---|---|
| Bloodcraft | Leveling XP boost, expertise progress, blood-legacy progress, familiar unlock, quest progress (all gated by per-system config flags) |
| Beelzebub  | V-Blood ability capture + V-Blood transform-unlock roll |

Different EventList for each iteration but same source query. No mutation —
both purely add side effects. Beelzebub runs after Bloodcraft (alphabetical).
**No conflict.**

### 4. `GameDataInitialized` / init-time patches — coexist

Both mods hook init events to detect when `PrefabCollectionSystem` is
populated. They run independently; neither blocks the other.

---

## Player-facing collisions (none structural)

### Chat commands — different prefixes

Bloodcraft uses `.bloodcraft` (or `.bc`). Beelzebub uses `.beelz`. The
underlying VCF dispatcher resolves them independently — no shared command names.

### Verbosity / notifications

Both mods have their own verbosity-per-player tracking. Bloodcraft:
`.bloodcraft verbosity`. Beelzebub: `.beelz verbosity <silent|summary|verbose>`.
Independent — players set each separately.

### Persistence

| Mod | Data path |
|---|---|
| Bloodcraft | `BepInEx/config/Bloodcraft/PlayerData/<steamId>/*.json` (one file per subsystem) |
| Beelzebub  | `BepInEx/config/kdpen.Beelzebub/state.json` (single file) |

Zero overlap. Wiping one mod's data doesn't touch the other.

### Transforms

Bloodcraft's exoform shapeshifts (`Buff_General_Shapeshift_Werewolf_Standard`,
`Buff_General_Shapeshift_Werewolf_VBlood`) are different buff prefabs from
Beelzebub's transformation carrier (`Buff_VBlood_Ability_Replace` GUID
1171608023). Two players could in principle stack Beelzebub transform + Bloodcraft
shapeshift simultaneously — but in practice native shapeshift forms drop on any
off-form cast, so layering them is pointless. Recommend players pick one mod's
transform mechanic at a time and ignore the other while it's active.

### Damage scaling

Bloodcraft applies its own damage/stat modifications via `ModifyUnitStatBuff_DOTS`
on class buffs, blood quality, expertise, etc. Beelzebub's TX6 stat scaling
(v0.15.0) does the same thing but on a separate carrier buff that only exists
during a transform. Both stack additively in V Rising's stat resolver — a
50%-physical-power Bloodcraft expertise bonus PLUS a 50%-physical-power
Beelzebub transform scale both apply.

**Tuning hint**: if a server runs both mods, dial Beelzebub's `DamageScale`
values down a notch — a fully-leveled Bloodcraft player with weapon expertise
already has +X% baseline. Beelzebub scales are multiplied on top of that.

---

## Things that look like conflicts but aren't

- **"Both mods log similar `[BeelzAUDIT]` / `[Bloodcraft]` lines"** — they
  write to the same `BepInEx/LogOutput.log` because that's the shared log
  sink. Each mod's prefix on its log lines disambiguates.
- **"My slot 3 sometimes shows the wrong ability"** — this is the
  Bloodcraft/Beelzebub slot-3 contest. Pick one and disable the other's
  binding for that slot per the recommendations above.
- **"Beelzebub transform overrides Bloodcraft class spells"** — by design,
  v0.14.0+. The carrier buff has `Priority = 99`. Class spells return on revert.

---

## Recommended Bloodcraft config flags for cleanest interop

In `BepInEx/config/Bloodcraft.cfg`:

```ini
[Classes]
ShiftSlot = false           # if you bind slot 3 via Beelzebub
UnarmedSlots = false        # if you want unarmed slots 1+4 free for Beelzebub

[Quality]
# Leave the rest alone — they don't intersect with Beelzebub.
```

In `BepInEx/config/kdpen.Beelzebub.cfg`:

```ini
[Transformation]
# Leave defaults — Beelzebub's carrier buff handles its own lifecycle.
Transform_NativeShapeshift_Enabled = false
```

These two flips give you Beelzebub's full slot stack with Bloodcraft running
in parallel and never stepping on it. The rest of Bloodcraft (leveling,
expertise, familiars, etc.) runs unmodified.

---

## What's NOT supported as cross-mod feature

Beelzebub doesn't read any Bloodcraft data. There's no integration where, for
example, Bloodcraft class abilities become Beelzebub-grantable. If you want
to use a Bloodcraft class spell as your slot binding, you either use
Bloodcraft's own slot system (and disable Beelzebub for that slot via
`.beelz unslot N`) or capture the same ability via Beelzebub's normal kill
mechanic and bind it that way.

Cross-mod feature integration is intentional non-scope. Both mods compose by
each minding their own state and respecting the slot-priority resolver.
