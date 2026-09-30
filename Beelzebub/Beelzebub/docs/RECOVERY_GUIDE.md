# Repair a Stuck Player — Admin Recovery Guide

Beelzebub ships a **server-side** recovery toolkit. If a player's action bar gets into a bad state, an admin can
fix it in-game — **you never need to wipe the server.** A player's captured abilities and transform unlocks are
tied to their **Steam account**, so no step below loses them.

All admin commands are `adminOnly`. `<player>` is the in-game character name (a unique part of it is enough).

---

## Why a bar gets stuck (v0.137)

A bar slot resolves through five layers, and a reset that skips one "doesn't work" — the spell comes back from the
layer it skipped:

1. **Saved binds** — the player's universal / weapon / form sets, keyed by Steam ID (they re-apply on login, on
   every weapon equip and on form enter).
2. **Weapon rows** — Beelzebub rows added to the held weapon's equip buff (the weapon's own skills are rows too;
   they are kept).
3. **Override sources** — a transform carrier, a form or shapeshift buff, or an orphaned source left behind.
4. **Slot modifications** — the engine's per-slot modification stack (a leak here survives relog).
5. **Automatic re-inject** — layer 1 is written back on equip, login and form enter.

Since v0.137 `resetbar`, `reset-loadouts` and `purge` run **one** reset that clears all five in a fixed order,
then reads the bar back and says whether it is clean.

---

## The flow — in this order

### 1. Look first: `.beelz admin bar <player>`
Read-only; changes nothing. For each bar slot (primary, 1-6, ultimate, 8) it shows the ability the slot resolves
to, the saved bind (`universal`, `weapon:Sword`, `form:Wolf` or `none`), whether a Beelzebub row sits on the
weapon, and the slot modifications split into **gear** (the weapon's and jewellery's own — normal) and **other**
(anything else — the stuck part). It also lists override buffs and the transform record. The same readout goes to
the server log as a `[Beelz BAR]` line.

For an **offline** player it prints `offline: live bar resets on next login`, their saved sets, the transform
record and the hotkey count.

### 2. Reset: `.beelz resetbar CONFIRM` (the player) or `.beelz admin purge <player> CONFIRM` (an admin)
- **`.beelz resetbar CONFIRM`** — the player's own reset: ends any transform, clears every universal / weapon /
  form bind, removes Beelzebub weapon rows, destroys override sources, pops every leaked slot modification and
  re-applies the weapon's own rows. The weapon's skills are back **without a weapon swap**. Keeps captures, unlocks,
  hotkeys and presets.
- **`.beelz admin reset-loadouts <player> CONFIRM`** — the same reset, run by an admin on any player.
- **`.beelz admin purge <player> CONFIRM`** — the same reset **plus all hotkeys**. Use it when a player cannot run
  resetbar themselves, or when resetbar left slots overridden.

Each reply says either `bar is back to vanilla` or which slots are **still overridden**, plus the next step. The
server log gets one `[Beelz RESET]` line per run (`clean=1` or `clean=0`, `survivors=`). An offline target gets
its saved binds cleared now; the live bar resets on their next login.

If the reply says `live bar not reachable`, the character was mid-respawn or mid-load: have them relog or
respawn, then run `.beelz admin bar <player>` again.

### 3. Only if slots stay overridden after a purge and a relog: `.beelz admin reset-character <player> CONFIRM-RESET`
Unbinds the player's Steam ID and kicks them; on next login they create a new character. Their collection is
kept (Steam-keyed). **Their saved binds are kept too** — so run the purge first (step 2); reset-character alone
does not clear a bar.

---

## Commands that are NOT a bar fix on their own

- **`.beelz admin respawn <player>`** rebuilds the character in place (progress kept). The saved binds are
  Steam-keyed and re-apply on the next equip or login, so respawn alone brings a bad bind straight back.
- **`.beelz admin reset-character`** — same reason, see step 3.
- **`.beelz admin rebuildslots` / `clearslotmods`** are LEGACY levers (single-layer). Use `purge`.
- **`.beelz admin rebuildbar`** is now an alias of `.beelz admin bar`.

## Known limits (v0.137)

- **Mounted** saddle rows are not reset — have the player dismount first.
- A slot whose engine modification dump cannot be read is reported `unreadable` and never called clean; the reset
  does not touch it. Report it with the `[Beelz PURGE]` / `[Beelz RESET]` log lines.
- A stuck source destroyed by the reset is patched by the engine one tick later; the same run can still list that
  slot. Run `.beelz admin bar <player>` again.

## Back up / transfer / restore a collection

- **`.beelz admin copy-collection <player>`** — copies that player's captures + transform unlocks into an admin
  clipboard (lasts for the server session).
- **`.beelz admin paste-collection <player>`** — applies the clipboard onto a player (additive; skips duplicates).

## Notes

- **Never destroy ability-slot entities directly.** (An early experimental command did, and it crashed the server
  on the player's next login from dangling references. The shipped commands only pop modifications by id, remove
  buff rows and use the engine's own respawn / unbind paths.)
- Diagnostic dumps: `.beelz admin buffs <player>` writes the live buffs and slot override sources to the log
  (`[Beelz BUFFS]`).
