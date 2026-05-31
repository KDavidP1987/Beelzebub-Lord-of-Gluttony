# Werewolf / native-form transform — design & scoping (v0.95.0)

> **Status: v0.96.0 — MVP SHIPPED AS A TEST (player transform path).** The Werewolf
> Chieftain (Willfred) now unlocks a **Werewolf** transform via `BossFormRegistry`
> (entry `2079933370` → `Buff_General_Shapeshift_Werewolf_Standard` `-1598161201`),
> routed through the same `TransformService` / `TransformBuffService.ApplyForm`
> lifecycle as Dracula & Morgana. **OPEN (the core uncertainty in §5.1): the werewolf
> bar is CastOptions-based (`CO_Werewolf`), so the injected `ReplaceAbilityOnSlotBuff`
> kit may or may not render/fire — needs the in-game test below.** If injection loses
> to CastOptions, the fallback is a cosmetic werewolf with its NATIVE kit (still a win),
> or editing a cloned `CO_Werewolf` (§4b). The per-form *loadout bucket*
> `ShapeshiftForm.Werewolf` (wheel-form, wolf-skin) is a SEPARATE thing and unchanged.

## 1. The finding that started this

Beelzebub's `ShapeshiftForm.Werewolf` is currently mapped (in both
`Services/ShapeshiftAbilityService.cs` `_formByBuff` and
`Services/ShapeshiftService.cs`) to **`AB_Shapeshift_Wolf_Skin01_Buff`
(`-1158884666`)** — that is a **cosmetic wolf reskin**, NOT a werewolf. There is
no behavioural difference from the ordinary Wolf form; it just looks slightly
different.

**A genuine player-reachable werewolf form does exist.** From the prefab dump:

| Prefab | GUID | Notes |
|--------|------|-------|
| `Buff_General_Shapeshift_Werewolf_Standard` | `-1598161201` | The werewolf-curse form (non-VBlood). `BuffType: Replace`, `Persists_Through_Death`. |
| `Buff_General_Shapeshift_Werewolf_VBlood` | `-622259665` | VBlood variant. |
| `AB_Elixir_Werewolf_T01_AbilityGroup` | `-332244204` | The Cursed Forest werewolf **elixir** that applies the curse in vanilla. |
| `CO_Werewolf` | `195815988` | The **CastOptions** prefab the werewolf buff points at for its ability bar. |
| `Faction_Werewolf` | `-2024618997` | The faction the form puts you in. |

There are also the full `AB_Werewolf_*` ability groups (Bite, Dash, Howl, Melee).

## 2. Why werewolf is NOT a drop-in for the existing per-form loadout system

The vanilla **Wolf / Bear / Spider** form buffs carry a
`ReplaceAbilityOnSlotBuff` buffer — that is the hook
`ShapeshiftAbilityService.ApplyFormLoadout` uses to inject the player's loadout
onto the form bar (see the v0.95.0 exact-slot rewrite).

`Buff_General_Shapeshift_Werewolf_Standard` has **no `ReplaceAbilityOnSlotBuff`**.
Its bar is defined indirectly by:

```
ProjectM.Gameplay.Scripting.Script_Buff_ModifyCastOptions_DataServer
    CastOptionsPrefab: CO_Werewolf PrefabGuid(195815988)
```

So to put custom abilities on a werewolf bar we would have to either
(a) edit/replace the `CO_Werewolf` CastOptions prefab, or (b) add a
`ReplaceAbilityOnSlotData` + `ReplaceAbilityOnSlotBuff` to the werewolf buff
entity at spawn and confirm the HUD honours it over the CastOptions bar. **Both
are unverified** and need an in-game probe before promising custom abilities on
werewolf. The werewolf form is therefore a good candidate for a **pure cosmetic /
"become the creature" transform** first, with custom-ability support as a later
stretch goal.

## 3. Existing prior art in Beelzebub (reuse, don't reinvent)

- **`Services/ShapeshiftService.Apply(character, unitGuid, forceEnabled)`** already
  applies a native shapeshift buff via `DebugEventsSystem.ApplyBuff` and
  `Remove(...)` tears it down. This is exactly the KindredCommands-style "apply a
  form buff" mechanism. It currently only maps a handful of forms in `PickFormFor`
  and is gated by `Transform_NativeShapeshift_Enabled`.
- **`.beelz admin testform <wolf|bear|off>`** (`AdminCommands.TestForm`, v0.47) already
  drops the admin into a native form carrying their loadout and reverts via
  `Core.Transforms.Revert(steamId, ...)`. This is the closest existing lifecycle.
- **Dracula / Morgana transforms** (`TransformService`, `.beelz transform`) are the
  full player-facing transform lifecycle (apply form + replace bar + revert + logout
  cleanup + summon stashing). A werewolf transform should plug into the SAME
  lifecycle so revert / death / disconnect all clean it up for free.

## 4. Proposed design

### 4a. Minimum viable (cosmetic werewolf transform)
- **New form key.** Keep `ShapeshiftForm.Werewolf` for the per-form *loadout
  bucket* (it stays the wolf-skin for now), but add a distinct native-form target
  for the *real* werewolf buff so the two don't collide. Either a new enum value
  (`ShapeshiftForm.WerewolfCurse`) or a separate native-form table keyed by buff
  GUID — leaning toward the latter to avoid churning the loadout bucket set
  (which BCH mirrors over the wire).
- **Admin command** `\.beelz admin form <player> <werewolf|wolf|bear|...|off>` —
  applies the chosen native form buff via `ShapeshiftService.Apply` (extended with
  the werewolf GUIDs) and registers it on the player's `ActiveTransform` so
  `.beelz revert` / logout clear it. Mirror `TestForm`'s revert path.
- **Config** `Forms_WerewolfTransform_Enabled` (default off, reflection-streamed via
  `api config`) so a server can opt in and a BCH admin panel can toggle it.

### 4b. Stretch (custom abilities on werewolf)
- Probe whether adding `ReplaceAbilityOnSlotData` + a `ReplaceAbilityOnSlotBuff`
  buffer to the spawned werewolf buff entity makes the HUD render the player's
  loadout (reuse `ApplyFormLoadout`'s exact-slot injection). Use
  `.beelz admin dump <guid|form>` to inspect the live werewolf buff first.
- If the HUD ignores it (CastOptions wins), the fallback is editing a cloned
  `CO_Werewolf` — heavier, deferred.

### 4c. Player-facing (optional, later)
- If cosmetic werewolf is solid, expose it as a player transform alongside
  Dracula/Morgana (a captured "Werewolf Chieftain" VBlood could unlock it), routed
  through `TransformService` like the two bosses.

## 5. Open questions / risks
1. Does the werewolf buff honour an injected `ReplaceAbilityOnSlotBuff`, or is the
   bar locked to `CO_Werewolf`? (Probe before promising custom abilities.)
2. `Buff_Persists_Through_Death` on the werewolf buff — make sure Beelzebub's
   revert/death handling still removes it (test death while transformed).
3. The werewolf puts the player in `Faction_Werewolf` — confirm this doesn't make
   the player hostile to their own summons / clanmates, or gate PvP oddly.
4. `ModifyDropTableBuff` (`DT_Unit_Cursed_Creature_Werewolf`) is on the buff —
   verify a transformed player dying doesn't drop a creature loot table.

## 6. Files a future implementation will touch
- `Services/ShapeshiftService.cs` — werewolf GUIDs + apply/remove path.
- `Services/ShapeshiftAbilityService.cs` — (stretch) custom-ability injection on werewolf.
- `Commands/AdminCommands.cs` — `\.beelz admin form` command.
- `Config/Settings.cs` — `Forms_WerewolfTransform_Enabled`.
- `Services/TransformService.cs` — (4c) if it becomes a player transform.
- `docs/BCH_INTEGRATION_HANDOFF.md` — only if a wire surface (config key / event) is added.
