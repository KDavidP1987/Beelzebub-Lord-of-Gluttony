# Repair a Stuck Player — Admin Recovery Guide

Beelzebub ships a full **server-side** recovery toolkit. If a player's abilities or action
bar ever get into a bad state, an admin can fix it in-game — **you never need to wipe the
server.** Worst case is a single-player character re-roll, and even that preserves the
player's collected abilities.

All commands are `adminOnly`. Run them in chat. `<player>` is the in-game character name.

---

## TL;DR — pick by symptom

| Symptom | Try this (in order) |
|---|---|
| Bar shows boss/granted abilities the player wants gone | `.beelz resetbar` (player) or `.beelz admin reset-character` |
| Bar won't accept spellbook/weapon changes; stuck on creature/shapeshift abilities | `.beelz admin rebuildslots <player>` → relog |
| Totally stuck, nothing else works | `.beelz admin reset-character <player> CONFIRM-RESET` (fresh character) |
| Want to move/keep a player's collection | `.beelz admin copy-collection` / `paste-collection` |
| Don't know what's wrong | `.beelz admin buffs <player>` (diagnostic dump to the server log) |

> **Key fact:** a player's captured abilities + transform unlocks are tied to their **Steam
> account**, not the character. Re-rolling a character keeps the collection automatically.

---

## The commands

### Diagnose
- **`.beelz admin buffs [player]`** — dumps the player's live buffs, character prefab, equipped
  ability slots, and ability-slot override sources to the BepInEx server log (tagged
  `[Beelz BUFFS]`). Start here when you don't know what's wrong.

### Light-touch fixes (try these first)
- **`.beelz resetbar`** *(player command)* — clears the player's own Beelzebub slot bindings
  (universal + weapon-specific) and any active transform, returning the bar to vanilla.
  Keeps captures/unlocks.
- **`.beelz admin rebuildslots <player>`** — safely re-syncs the player's active ability slots
  back to their stored base abilities (values only; no entity destruction). Fixes a bar stuck
  on creature/shapeshift abilities. Have the player equip/swap a weapon or relog afterward.
- **`.beelz admin clearslotmods <player>`** — clears orphaned ability-slot modifications and
  forces the slots to rebuild from base.
- **`.beelz admin respawn <player>`** — rebuilds the player's character in place (engine
  respawn); inventory/progress preserved.

### Full reset (last resort — guaranteed clean character)
- **`.beelz admin reset-character <player> CONFIRM-RESET`** — unbinds the player's Steam ID and
  kicks them; on next login they create a **brand-new character** with a clean ability bar.
  No second mod required. Their Beelzebub collection is preserved (Steam-account-keyed). The
  old body remains in the world but is unplayable.

### Back up / transfer / restore a collection
- **`.beelz admin copy-collection <player>`** — copies that player's captures + transform
  unlocks into an admin clipboard (lasts for the server session).
- **`.beelz admin paste-collection <player>`** — applies the clipboard onto a player (additive;
  skips duplicates). Also works to transfer a collection between different players.

---

## Recommended full recovery flow

For a badly stuck player, when in doubt:

```
.beelz admin copy-collection <player>            # 1. back up their collection (safety net)
.beelz admin reset-character <player> CONFIRM-RESET   # 2. clean re-roll
# player logs back in and creates a fresh character
.beelz admin paste-collection <player>           # 3. restore the collection (usually auto-kept anyway)
```

Step 3 is normally unnecessary (the collection is Steam-account-keyed and survives the
re-roll), but it's there as belt-and-suspenders or after a `.beelz clear`.

---

## Notes

- **Root cause is fixed (v0.43.16):** the original bug — logging out while transformed could
  strand a creature's abilities on your bar — can no longer happen. This toolkit exists for
  legacy-stuck characters and general admin peace of mind.
- **Never destroy ability-slot entities directly.** (An earlier experimental command did, and
  it crashed the server on the player's next login due to dangling references. The shipped
  commands only reset/clear *values* or use the engine's own respawn/unbind paths.)
- These commands also appear in-game under `.beelz commands`.
