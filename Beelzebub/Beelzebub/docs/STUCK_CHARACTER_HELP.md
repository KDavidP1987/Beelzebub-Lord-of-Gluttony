# Stuck Character Help (Discord paste-ready)

**For the server owner:** this is written to be pasted into Discord for a player who's
locked into a bad state from Beelzebub abilities/transforms. The player steps are
addressed to "you"; the admin steps are written in the "An admin can…" perspective.
It's a little long for one Discord message (2,000-char limit) — paste it in chunks by
section, or post each step as its own message. Copy everything below the line.

---

# 🩸 Stuck Character? Here's How to Get Unlocked

Sorry you got locked up after logging out! Good news — this is fixable in-game, no server wipe needed, and **your collection is safe no matter what.** Your captured abilities and transform unlocks are tied to your Steam account (not your character), so even a full character reset keeps everything you've collected.

Work through these in order. Most people are unstuck by the first couple.

## ✅ Step 1 — Try these yourself

Log back in and run these in chat. They all keep your collection:

```
.beelz revert
```
Exits any active transformation and restores your spell bar right away.

```
.beelz refresh
```
Re-applies your ability bar — fixes a blank or wrong bar after reverting or leaving a form.

```
.beelz summons clear
```
Despawns all your summons, in case those are the problem.

```
.beelz clearbar
```
Clears your granted ability bindings (no confirmation needed) and returns the bar to vanilla. You can also target just one set, e.g. `.beelz clearbar universal`, `.beelz clearbar sword`, or `.beelz clearbar wolf`.

```
.beelz resetbar CONFIRM
```
The bigger hammer: ends any active transform **and** removes all your slot bindings, back to a vanilla bar. You keep your captures and unlocks. (You must type the word **CONFIRM**.)

⚠️ **Don't use `.beelz clear`** — that one wipes your entire collection, and we don't need that.

## 🛠️ Step 2 — Ask an admin

If the self-fixes don't clear it, give an admin your character name. None of these touch your collection. An admin will usually diagnose first, then apply the lightest fix that works:

- `.beelz admin buffs <player>` — an admin can dump which buffs are locking your bar.
- `.beelz admin inspect <player>` — an admin can review your Beelzebub state.
- `.beelz admin clear-transform <player>` — an admin can drop a transform you're stuck in.
- `.beelz admin rebuildslots <player>` — an admin can re-sync your slots back to your base abilities (fixes a bar stuck on creature/shapeshift abilities); swap a weapon or relog afterward.
- `.beelz admin clearslotmods <player>` — an admin can clear orphaned slot modifications and force a rebuild from base.
- `.beelz admin respawn <player>` — an admin can respawn your character on the spot so the game rebuilds it fresh; your inventory, equipment, blood, and progress are all preserved.
- `.beelz admin desummon <player>` — an admin can clean up any stuck summons following you.

## 🔄 Step 3 — Clean re-roll (last resort, still keeps your collection)

If nothing above works, an admin can do a full reset:

- An admin will back up your collection first: `.beelz admin copy-collection <player>`.
- Then `.beelz admin reset-character <player> CONFIRM-RESET` — this unbinds your character and kicks you, and on your next login you'll create a brand-new character with a clean bar. Your old body stays in the world but you won't play it.
- Log back in, make the fresh character, and an admin can restore anything if needed with `.beelz admin paste-collection <player>` (usually it's already there automatically).

Most of the time `.beelz admin respawn` or `.beelz admin rebuildslots` sorts it out and the re-roll isn't needed. Just tell us which self-fixes you tried and what happened, and an admin will take it from there.
