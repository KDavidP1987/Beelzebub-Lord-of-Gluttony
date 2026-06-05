# Bloodcraft coexistence audit (IN1)

How Beelzebub (`kdpen.Beelzebub`) interacts with Bloodcraft (`io.zfolmt.Bloodcraft`)
when both are loaded on the same dedicated server.

**Audited against Bloodcraft v1.13.21 and Beelzebub v0.119.0 (ApiVersion 28).**
*(Previous revision of this doc was pinned to Beelzebub v0.15.0 and is superseded
— it predated slots 0/7, per-form loadouts, the mounted form, and hotkeys, and it
over-claimed the slot-tie outcome. See "What changed since the v0.15.0 doc" at the
bottom.)*

**TL;DR**: They coexist cleanly — no crashes, no hard conflicts. The only friction
is **shared ownership of spell-bar slots**: both mods inject
`ReplaceAbilityOnSlotBuff` entries onto the same player equip-buff. On a slot **both**
mods bind, the winner is **order-dependent and not guaranteed** (see §2) — so the
fix is to stop them contesting the same slot via two Bloodcraft config flags
(`ShiftSlot`, `UnarmedSlots`). Everything else operates on different surfaces or
composes additively.

---

## Quick reference

| Surface | Bloodcraft | Beelzebub | Status |
|---|---|---|---|
| Spell-slot injection | slots 1, 3, 4 (name-gated) | slots 0–7 (weapon-family gated) | ⚠️ **contest on 1/3/4** — flip 2 flags |
| Stat bonuses | expertise + legacy via `ModifyUnitStatBuff_DOTS` | transform-only carrier buff | ✅ additive, balance-only |
| Death / V-Blood progression | XP, expertise, legacy, familiars | ability capture, unlock rolls | ✅ independent |
| Shapeshift forms | ExoForm (Dracula/Morgana/prestige), BearFormDash | vanilla Wolf/Bear/… + Gargoyle + Mounted | 🔬 verify Dracula/Morgana overlap |
| Persistence | `config/Bloodcraft/PlayerData/…` | `config/kdpen.Beelzebub/state.json` | ✅ isolated |
| Chat commands | `.bloodcraft` / `.bc` | `.beelz` | ✅ isolated |

---

## Shared Harmony patch surfaces

Both mods patch several of the same V Rising server systems. None of the overlaps
crash, because every relevant patch is `Postfix` or `Prefix-no-skip` — neither mod
blocks the original method, neither cancels the other's work. The one that needs
thought is `ReplaceAbilityOnSlotSystem` (§2).

### 1. `DeathEventListenerSystem.OnUpdate` — both Postfix

| Mod | Purpose |
|---|---|
| Bloodcraft | Leveling XP, weapon expertise, blood legacy, familiar unlock, professions, quests |
| Beelzebub  | Ability-capture roll + transform-unlock roll on regular-mob kills |

Both iterate the death-event query independently; neither mutates the events.
Execution order doesn't matter — both are pure side-effect consumers.
**Conflict: none.**

### 2. `ReplaceAbilityOnSlotSystem.OnUpdate` — both Prefix, both append to the buffer

The only overlap worth thinking about. Both mods add `ReplaceAbilityOnSlotBuff`
entries onto the player's `EquipBuff_Weapon_*` entity so their abilities appear on
the spell bar.

**Bloodcraft** (`Patches/ReplaceAbilityOnSlotSystemPatch.cs`) keys off the
equip-buff's **prefab name**:

| Bloodcraft write | Config flag | Slot | Trigger (name contains) |
|---|---|---|---|
| First unarmed spell | `UnarmedSlots` | **1** | `unarmed` / `fishingpole` |
| Second unarmed spell | `Duality` | **4** | `unarmed` / `fishingpole` |
| Class / "shift" spell | `ShiftSlot` + `SHIFT_LOCK_KEY` | **3** | `weapon` (armed) or unarmed |
| Lock spell choices | — | reads 5/6 only | not a `WeaponLevel` entity |

> The Bloodcraft **"shift key"** is not a keybind hook — Bloodcraft has no input/
> keyboard patch. It writes the class spell into **GroupSlot index 3** and lets V
> Rising's existing shift-modifier wiring fire it. `SetSpells` only *reads* slots
> 5/6 to remember the player's spell choices; it does not write the buffer.

**Beelzebub** (`Patches/ReplaceAbilityOnSlotSystemPatch.cs` →
`Services/SlotApply.cs`) injects saved grants across **slots 0–7** on the same
`EquipBuff_Weapon_*` entity, gated by weapon-family compatibility. **Unarmed is a
real weapon family in Beelzebub** (`SlotApply.DetectFamily` :67), so Beelzebub
*also* injects on the unarmed bar — meaning every slot Bloodcraft touches (1, 3, 4)
is a slot Beelzebub can also bind.

#### ⚠️ Who wins a contested slot is NOT guaranteed

Both mods append at **`Priority = 0`** (Bloodcraft `:81/95/116`; Beelzebub
`SlotApply.cs:323`). On Beelzebub's live weapon-swap path,
`ResolveAndInjectGrants` does a plain `buffer.Add` **without removing** a
competitor's entry for that slot (`SlotApply.cs:312`) — so both entries coexist in
the buffer and the tie is broken by buffer order. That order depends on:

- **Cross-plugin Harmony prefix order**, which follows BepInEx **load order**, not
  patch-class name. Neither mod sets `[HarmonyPriority]`. It *may* land
  Bloodcraft-first today (GUID `i` < `k`), but nothing enforces it and a load-order
  change flips it silently.
- **How V Rising resolves equal-priority, same-slot duplicates** (first- vs
  last-in) — not confirmed from the source.

So on a slot both mods bind, the outcome is **order-dependent and brittle**, not a
reliable "Beelzebub wins." Treat a contested slot as undefined and avoid it.

> **Note:** Beelzebub's *manual* command path (`SlotApply.ApplyGrant` →
> `ReplaceSlotEntry` :479) DOES strip prior entries for the slot before adding —
> but that only de-dupes Beelzebub's own entries, not Bloodcraft's (different patch
> class, different invocation). It does not make Beelzebub win the cross-mod tie.

#### Recommendations (slots)

Set these in `BepInEx/config/io.zfolmt.Bloodcraft.cfg` if you want Beelzebub to own
those slots cleanly:

```ini
[Classes]
ShiftSlot    = false   # frees slot 3 for Beelzebub
UnarmedSlots = false   # frees slots 1 + 4 on the unarmed bar
```

- **Slot 3 (R / shift):** with `ShiftSlot=false`, Bloodcraft stops writing slot 3;
  Beelzebub owns it uncontested. Bloodcraft class abilities remain available through
  Bloodcraft's own mechanisms.
- **Slots 1 + 4 (unarmed):** only matters if you fight unarmed. `UnarmedSlots=false`
  frees them for Beelzebub's unarmed loadout.
- **Slots 5/6:** no conflict — Bloodcraft's `SetSpells` only reads them, and on the
  unarmed/fishingpole buff at that. Beelzebub owns them on weapons cleanly.
- **Slots 0 (primary) / 7 (ultimate):** Beelzebub binds these (since v0.91.0);
  Bloodcraft touches neither. No conflict.

**Deterministic alternative — `Interop_SlotInjectionPriority` (v0.120.0):** Beelzebub
now exposes the priority it stamps on its grant overrides as an admin config key (in
the `[Interop]` section of `kdpen.Beelzebub.cfg`, also settable live via
`.beelz admin set Interop_SlotInjectionPriority <n>`):

| Value | Effect on a contested slot |
|---|---|
| `0` (default) | Neutral / legacy — equal-priority tie, load-order-dependent (as above) |
| `1` or higher | **Beelzebub wins** the slot deterministically (beats Bloodcraft's `Priority=0`) |
| negative | **Beelzebub yields** — the other mod's bind wins |

This is harmless on a single-mod server (nothing else competes, so `0` and `1` look
identical). It's a class-wide lever (it re-asserts Beelzebub over *any* equal/lower-
priority third-party `ReplaceAbilityOnSlotBuff` writer, not just Bloodcraft) — which
is exactly the intent. The form/transform carrier buffs (`Priority=99/100`) are
unaffected. Use this **or** the Bloodcraft flag separation above — the flags hand the
slot off entirely; this just decides who wins when both still write it.

### 3. `VBloodSystem.OnUpdate` — both Prefix on feed-kills

| Mod | Purpose |
|---|---|
| Bloodcraft | Leveling XP, expertise/legacy progress, familiar unlock, quests (all config-gated) |
| Beelzebub  | V-Blood ability capture + V-Blood transform-unlock roll |

Same source query, separate effect lists, no mutation — both add side effects only.
**No conflict.**

### 4. `BuffSystem_Spawn_Server.OnUpdate` — both Postfix, different targets

Bloodcraft uses it to apply expertise/legacy stats on the bonus-stats buff;
Beelzebub uses it to apply per-form loadouts when a shapeshift form buff spawns.
Different buff entities. **No conflict.**

### 5. `StatChangeSystem.OnUpdate` — both Prefix, different targets

Bloodcraft applies class on-hit effects; Beelzebub pushes attackers into a summon's
aggro buffer. Different entities. **No conflict.**

### 6. `LinkMinionToOwnerOnSpawnSystem` / init-detection — coexist

Both Postfix minion-link (Bloodcraft = familiars, Beelzebub = summons; different
owners) and both hook init events to detect `PrefabCollectionSystem` readiness.
Independent. **No conflict.**

---

## Stat scaling (expertise / blood legacy vs Beelzebub power)

Bloodcraft applies weapon-expertise and blood-legacy bonuses as
`ModifyUnitStatBuff_DOTS` entries on a bonus-stats buff (`BonusPlayerStatsBuff`,
GUID `737485591`), driven by `WeaponManager` / `BloodManager` through the
buff-spawn path. *(Note: Bloodcraft's dedicated
`ModifyUnitStatBuffSystemSpawnPatch` is commented out in v1.13.21 — stats go
through the buff-spawn route instead.)*

Beelzebub touches player stats in only two narrow, self-reverting places:

- **Transform stat scaling** (`TransformBuffService`) — `ModifyUnitStatBuff_DOTS`
  on a transform-only carrier buff (`Buff_VBlood_Ability_Replace`, GUID
  `1171608023`). Exists only while transformed; reverts on exit.
- **Granted-ability power window** (`GrantPowerScalingService`) — a short-lived
  buff applied on cast of a captured ability. **Its default mode is `PlayerScaled`,
  which is a no-op** (the ability simply scales with the player's existing power).

**Conflict: none, technically.** V Rising's stat resolver sums all
`ModifyUnitStatBuff_DOTS` additively, so they stack without error. The only
consequence is **balance**, and because Beelzebub's grant scaling defaults to no-op
there is **no double-dip on ordinary granted abilities** — they just benefit from
the player's Bloodcraft-boosted power, as expected.

**Tuning hint:** if you run both, dial Beelzebub's per-transform `DamageScale` down
a notch — a fully-leveled Bloodcraft player already carries large `+%` expertise/
legacy bonuses, and Beelzebub's transform scale multiplies on top of that baseline.

---

## Shapeshift / transform overlap (🔬 verify in-game)

Both mods write `ReplaceAbilityOnSlotBuff` onto **shapeshift buff entities**:

- **Bloodcraft** — `Utilities/Shapeshifts.cs:ModifyShapeshiftBuff` (:337) writes
  the bar for its **ExoForms** (prestige-unlocked Evolved-Vampire **Dracula** /
  Corrupted-Serpent **Morgana** and related), and `BearFormDash` adds a dash to
  bear form. Bloodcraft's `ShapeshiftSystemPatch` otherwise only dismisses an
  active familiar on shapeshift.
- **Beelzebub** — `ShapeshiftAbilityService.ApplyFormLoadout` injects per-form
  loadouts onto **vanilla** Wolf / Bear / Rat / Spider / Toad / Gargoyle and the
  **Mounted** saddle bar (mounted = slots 3/6/7 only), at **`Priority = 99`** on the
  form buff entity. Beelzebub's slot patch explicitly recognizes its own supported
  forms and the mount buff and injects there; non-supported shapeshift buffs (e.g. a
  Bloodcraft ExoForm) fall through and are ignored (they aren't a weapon family).

For ordinary vanilla forms vs Bloodcraft ExoForms these target **different buff
entities**, so they don't collide. **The one place to verify:** Beelzebub's
`.beelz transform` uses the **Dracula / Morgana** form space — the *same* exoform
family Bloodcraft's prestige ExoForm uses. They're mutually exclusive in practice
(one active form at a time), but confirm in-game that triggering a Beelzebub
transform on a character with a Bloodcraft ExoForm unlock doesn't double-stamp the
same shapeshift buffer. Likewise, a Beelzebub bear-form loadout will override
Bloodcraft's `BearFormDash` slot.

Recommendation: have players use one mod's transform/shapeshift mechanic at a time.

---

## Player-facing collisions (none structural)

- **Chat commands:** Bloodcraft `.bloodcraft` / `.bc`, Beelzebub `.beelz` — the VCF
  dispatcher resolves them independently; no shared names.
- **Verbosity / notifications:** each mod tracks its own per-player verbosity
  (`.bloodcraft verbosity` vs `.beelz verbosity`). Independent.
- **Persistence:** Bloodcraft → `BepInEx/config/Bloodcraft/PlayerData/<steamId>/*.json`;
  Beelzebub → `BepInEx/config/kdpen.Beelzebub/state.json`. Zero overlap — wiping one
  mod's data doesn't touch the other.
- **Logs:** both write to the shared `BepInEx/LogOutput.log`; each prefixes its own
  lines (`[Beelz…]` vs `[Bloodcraft]`).

---

## Things that look like conflicts but aren't

- **"My slot 3 (or 1/4) shows/fires the wrong ability."** That's the contested-slot
  case in §2. Pick one mod for that slot — flip the Bloodcraft flag.
- **"Beelzebub transform hides my Bloodcraft class spells."** While transformed,
  Beelzebub's slot-grant branch early-returns (`ReplaceAbilityOnSlotSystemPatch.cs:97`)
  and the transform carrier buff (`Priority = 99`) owns the bar. Bloodcraft's
  class/shift spells return on revert.
- **"Both mods log similar lines."** Shared log sink; the per-mod prefixes
  disambiguate.

---

## Recommended config for cleanest interop

`BepInEx/config/io.zfolmt.Bloodcraft.cfg`:

```ini
[Classes]
ShiftSlot    = false   # if you bind slot 3 (R) via Beelzebub
UnarmedSlots = false   # if you want unarmed slots 1 + 4 free for Beelzebub
# Leave everything else — leveling, expertise, legacies, familiars, professions
# don't intersect with Beelzebub.
```

`BepInEx/config/kdpen.Beelzebub.cfg`:

```ini
[Transformation]
# Defaults are fine — the transform carrier buff manages its own lifecycle.
# If players also use Bloodcraft ExoForms, consider keeping native-shapeshift
# loadouts off to avoid form overlap:
Forms_CustomAbilities_Enabled = false
```

---

## What's NOT a cross-mod feature

Beelzebub reads no Bloodcraft data and vice-versa. There's no integration where a
Bloodcraft class spell becomes Beelzebub-grantable (or the reverse). To use a
Bloodcraft class spell in a slot, use Bloodcraft's own slot system (and
`.beelz unslot N` to free that slot in Beelzebub); to use it as a Beelzebub bind,
capture the same ability via Beelzebub's kill mechanic. Cross-mod feature
integration is intentional non-scope — the two compose by each minding its own
state and respecting the slot resolver.

---

## What changed since the v0.15.0 doc

For anyone diffing against the previous revision:

1. **Corrected the slot-tie claim.** The old doc said the resolver does
   "last-in-wins" and that "Beelzebub wins" deterministically. That reasoning relied
   on an assumed cross-plugin patch order and an unverified resolver behavior — see
   §2. A contested equal-priority slot is order-dependent; don't rely on a winner.
2. **Slots 0 and 7.** Beelzebub now binds primary (0) and ultimate (7). Bloodcraft
   touches neither — noted for completeness.
3. **Per-form loadouts (v0.95+) + Mounted form (v0.101).** New shapeshift overlap
   surface vs Bloodcraft ExoForms / BearFormDash — see the shapeshift section.
4. **Hotkeys.** `.beelz cast <name>` hotkeys are server storage + BCH UI buttons —
   no engine-slot or keybind collision with Bloodcraft.
5. **Grant power scaling default is no-op** (`PlayerScaled`), so no stat double-dip
   on ordinary granted abilities.
