# Changelog

What's new for players. This is the canonical changelog — it ships on Thunderstore
(bundled with the release) and lives in the repo on GitHub. For the full technical
history, see the [commit log / releases](https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/commits/main).

## [0.100.0] - 2026-05-31

### Build your own transformation loadouts + full transform admin controls

- **Custom transformation loadouts (per player).** Each boss has more abilities than you have slots — now you
  choose which ones to wield. New commands:
  - `.beelz tform <unit> abilities` — list that transform's full ability kit (with indices).
  - `.beelz tform <unit> set <phase> <slot> <index>` — put one of its abilities on a phase slot (slot 0 =
    primary/left-click, 7 = ultimate). Slots you don't set keep the curated default.
  - `.beelz tform <unit> clear <phase> <slot>` · `.beelz tform <unit> defaults` (reset).
  - You can even define a **phase 2** on a form that didn't have one. `<unit>` accepts the index from
    `.beelz transforms`, a name, or the unit id. Loadouts are per-player and persist across restarts.
- **Full transform admin controls.**
  - **Master switch:** `Transform_Enabled` (default on) turns ALL transformations off server-wide.
  - **Per-transformation duration & cooldown:** `.beelz admin transform-set <unit> duration <sec>` and
    `... cooldown <sec>` override the category defaults for a single form.
  - **Cooldown budget scope:** `Transform_CooldownScope` = `PerCategory` (default), `PerTransformation`
    (each form its own cooldown — e.g. 30 min/day *per* form), or `Global` (one cooldown across all forms —
    e.g. 30 min/day total). Pairs with per-form duration to build "once a day for 30 minutes" rules.
  - **Blocking:** whole-server via `Transform_Enabled`; per category via `Transform_Mode_* = Disabled`;
    per unit via `.beelz admin transform-set <unit> enabled false`.
  - **Power scaling** (already present, confirmed): `Transform_PowerScalingMode` + a per-unit `scaling_mode`
    override — `PrefabAbsolute` (the boss's own power), `PlayerScaled`/`PlayerLeveled` (match your level), or
    `CuratedScales` (admin multipliers).

### Broadcast message management + stabilization pass

- **Manage announcement messages individually:** `.beelz admin broadcast-msg <complete|leaderboard>
  <list|add|remove|edit>` — `add "<text>"`, `remove <n>`, `edit <n> "<text>"`, `list`. No more editing one
  giant config string. (Wrap multi-word messages in quotes.)
- **Full-mod audit hardening** (pre-stable): fixed a bug where a player's *Devour* bad-luck (pity) protection
  was wiped on server restart; corrected the now-outdated "Dracula & Morgana only" wording across the player
  and admin help/replies (5 transforms exist now); `.beelz unslot` / `weapon-unslot` / `form-unslot` now
  accept the **primary** / **ultimate** slot tokens (so you can unbind them without clearing a whole bar);
  `.beelz loadouts` now also lists your per-**form** loadouts; `.beelz admin tune-list` now shows **every**
  shaping field set on an ability (cooldown/range/aoe/etc.), not just cast modifiers; `.beelz forget-transform`
  now reverts the form first if you're in it; and a new **`.beelz admin reset-loadouts <player>`** clears a
  player's bindings + custom loadouts + active transform while **keeping** their collection.
- **Companion-app ability list improved (for BloodCraftHub).** The ability catalog now reports each ability's
  owning unit + IDs (so uncaptured abilities show their source), and there are now two views: the normal list
  is what players can collect (for progress %), and admins get a full list of *every* ability regardless of
  whether it's enabled (for configuration).

## [0.99.1] - 2026-05-31

### Form phase-switching fix

- **`.beelz phase` now works on the Werewolf, Golem, and Gargoyle forms.** Switching to phase 2 was silently
  failing on those forms (a timing issue with how the creature forms re-apply); the kit now swaps in place
  on the form you're already wearing, so phase 1 ⟷ phase 2 works like it does for Dracula & Morgana.
- **Dracula's experimental "Wolf" phase was removed.** Turning into a wolf mid-Dracula-transform was a model
  swap that doesn't work through the phase system — Dracula's wolf is really its own shapeshift, so it'll be
  revisited as a standalone transform later. Dracula is back to Warrior / Bloodmage.
- *Known limitation being investigated:* some boss abilities don't fire while transformed — they're either
  bound to the original creature's skeleton/animation or are multi-step "chain" abilities that don't fully
  resolve on a player. Report which slots are dead per form and they'll be swapped for working ones.

## [0.99.0] - 2026-05-31

### Full multi-phase boss kits + phase-switchable forms

- **Capturing and devouring a boss now covers its FULL kit — every phase.** Bosses swap in different
  abilities across their combat phases, and several (the Geomancer, Werewolf Chieftain, Tailor) even start
  the fight in a *human* form. Before, you could only collect a boss's base/phase-1 bar — so devouring the
  Geomancer might give you his human abilities and miss the golem kit entirely. Now capture and Devour draw
  from the boss's complete cross-phase ability set, so you can collect everything it uses.
- **The new transforms are now phase-switchable** like Dracula & Morgana. Use `.beelz phase` to swap kits:
  - **Werewolf** — *Feral* (agile bleed) ⟷ *Alpha* (heavy crowd-control).
  - **Golem** — *Earthshaper* (control) ⟷ *Enraged* (guardians + smashes).
  - **Gargoyle** — *Sentinel* (grounded defense) ⟷ *Skyterror* (flight).
  ⚠️ Still test builds — report which abilities fire on each form.

## [0.98.0] - 2026-05-31

### Transformations are now their own gated prize + more forms

- **Capturing, devouring, and transforming are now three separate rolls** — each with its own drop chance
  and its own bad-luck (pity) protection. Before, a boss's rare jackpot gave *either* its whole kit *or* its
  transformation. Now, on a transform boss, you can devour its abilities **and** still chase its
  transformation as a separate, rarer prize you work toward — and devouring a boss never just hands you its
  form for free. New configurable chances: `DropChance_TransformUnlock_VBlood` / `_Regular` (default rarer
  than Devour), with `Capture_PityIncrement_Transform` / `_Max_Transform` dials.
- **Basic Werewolf transformation** — the common werewolf NPC (not the boss) can now grant a basic werewolf
  form, separate from and more accessible than the Werewolf Chieftain's. The Chieftain now uses the V-Blood
  werewolf form so the two look distinct.
- **Dracula's Wolf form (experimental)** — Dracula gains a third phase: `.beelz phase 3` turns him into a
  wolf with a feral kit (claw, bite, leap, howl); `.beelz phase 1` returns to his vampire form. ⚠️ Test
  build — report how the model swap and the leave-wolf-phase transition behave.

## [0.97.0] - 2026-05-31

### Two more transformations (Golem & Gargoyle) + natural fallback on form bars

- **Two new transformations to test:**
  - **Golem** — defeat **Terah the Geomancer** to unlock the iron-golem form, with an earth-shaper kit
    (slam, ground slam, rock slam, raise guardians, enrage, enraged smash, underground tremors).
  - **Gargoyle** — defeat **the Tailor** to unlock the gargoyle form (wing-shield, take flight, dive).
  Both join Dracula, Morgana & Werewolf on the transform list. ⚠️ **Test builds** — the golem and gargoyle
  forms were never meant for players, so some abilities may not render/fire; please report what works.
  Admins can grant without the kill: `.beelz admin give-transform <player> -1065970933` (Golem) or
  `-1942352521` (Gargoyle).
  - *Note:* there's no separate "boss" form for Bear, Spider, or Toad — V Rising only ships the wheel
    forms for those; the Werewolf existed because the game ships a dedicated werewolf curse form.
- **Form abilities now keep the form's natural move as a fallback.** When you put a captured ability on a
  shapeshift-form slot it overrides that slot — but any slot you *don't* assign now keeps the form's own
  natural ability (e.g. the wolf's leap stays on the space-bar) instead of being blanked. This matches how
  your normal bar falls back to vanilla abilities on unassigned slots.

## [0.96.0] - 2026-05-31

### Become a Werewolf (new transformation)

- **Defeat the Werewolf Chieftain (Willfred) to unlock the Werewolf transformation.** It joins Dracula and
  Morgana as a real, model-swapping form — you take on the cursed-forest werewolf and a werewolf ability kit
  (claw, bite, leap-dash, multi-bite, knockdown, shadow-dash, stealth, howl). Activate with
  `.beelz transform Werewolf`, leave with `.beelz revert`, like the other transforms.
- ⚠️ **This is a test build of the werewolf form.** The werewolf's bar is built differently from the other
  bosses, so some injected abilities may not appear or fire correctly yet — please report which of the 8 do
  and don't work so the kit can be tuned. Admins can grant it for testing without the kill via
  `.beelz admin give-transform <player> 2079933370`.

## [0.95.0] - 2026-05-31

### Form abilities now fill every slot + quieter server log

- **Your form loadout now lands on the exact slots you grant — on every form.** Before, a form only showed
  abilities on the slots its vanilla bar happened to pre-define, mapped in order: Wolf showed 2, Bear showed
  3 (even though it has room for 7), Spider showed 1, and a grant to slot 5 could land somewhere else. Now
  each granted ability is placed on the **exact slot** you chose and slots the form doesn't natively use are
  added, so your loadout shows up where you put it. (Slots you don't assign keep the form's natural ability —
  see v0.97.) *Please re-test wolf, bear, and spider and report which slots show up.*
- **Werewolf form clarified.** The "Werewolf" form has always been a cosmetic wolf reskin, not the real
  werewolf-curse form — a proper werewolf transform is now scoped for a future update (see
  `docs/WEREWOLF_FORM_TRANSFORM_DESIGN.md`).
- **Quieter server log.** Toggling an ability on/off no longer needlessly re-applies (and logs) all cast
  tuning — that re-tune now runs only when you actually change a tuning field. The remaining `[Beelz TUNE]`
  detail lines are now behind verbose logging, so turning verbose off gives a clean log.

## [0.94.0] - 2026-05-30

### Per-bucket clear-bar + all form skins recognized

- **New `.beelz clearbar` command (no confirmation).** Clear exactly the loadout you mean:
  - `.beelz clearbar` or `.beelz clearbar all` — every set (universal + all weapons + all forms)
  - `.beelz clearbar universal` — your any-weapon set
  - `.beelz clearbar sword` / `spear` / `unarmed` / `crossbow` / … — that weapon's set
  - `.beelz clearbar wolf` / `bear` / … — that form's set
  Your captured abilities are kept. This replaces the awkward all-or-nothing clear (the old `.beelz
  resetbar` still works but needs a `CONFIRM`, which is why the companion-app "clear bar" button had stopped
  working — companion apps should call `.beelz clearbar`).
- **All shapeshift-form skins are now recognized.** Form detection is no longer tied to exact skin GUIDs, so
  every player skin of a form (wolf/bear/etc., including tailor/cosmetic variants) gets your custom form
  abilities. Admins can verify the full list with the new `.beelz admin dump forms`.
- Reset/clear now also clears the primary and ultimate slots (added in v0.91).

## [0.93.0] - 2026-05-30

### freelymove now works on channeled / sustained-fire abilities

`freelymove` previously only freed the cast wind-up — so on an ability like a continuous arrow barrage you
could move for the first second, then got locked again the moment it started firing. The fix: `freelymove`
now also lifts the movement-lock on the ability's *channel/firing* buff, so you can move for the whole
ability (after the brief wind-up you specify). This also clears up the stuttery movement that came from
being freed and re-locked mid-cast. `defaults` restores the original lock.

## [0.92.0] - 2026-05-30

### Diagnostics + label fix

- **Ultimate slot is the `T` key.** Corrected the labeling — `.beelz grant ultimate <id>` binds to the T-key
  ultimate slot (the `r` alias is replaced by `t`).
- **Deeper admin diagnostic.** `.beelz admin dump <abilityGuid>` now also logs the field *values* of an
  ability's movement/cast-lock components, to pin down why a given ability roots you (used to investigate
  free-movement on channeled abilities).

## [0.91.0] - 2026-05-30

### Forms exit on non-form casts + assign to primary & ultimate slots

- **Shapeshift forms now exit when you cast a non-form ability.** The form holds while you cast your
  assigned form abilities (and the form's own native bite/leap), but casting a genuinely foreign ability
  drops you out of the form — instead of being stuck until you manually leave via the wheel.
- **Assign abilities to the primary attack and ultimate slots.** `.beelz grant` / `weapon-grant` /
  `form-grant` now accept **`primary`** (left-click attack) and **`ultimate`** (the T key) in addition to
  slots 1-6 — e.g. `.beelz grant ultimate <ability ID>` or `.beelz grant primary <ability ID>`. This is why
  a granted wolf-form ability appeared on the ultimate slot: the wolf form renders on the ultimate slot, and
  now you can deliberately target it (and the primary) like Bloodcraft's ExoForm.

## [0.90.0] - 2026-05-30

### Forms hold through casting + freelymove works on channels (root-caused via component inspection)

- **Shapeshift forms no longer drop when you cast an injected ability.** The ability appeared on the form
  bar (v0.89) but casting it kicked you out of the form. The real exit trigger was a different component
  than we'd been neutralizing — now identified and stripped, so the form **holds while you cast** your
  granted abilities.
- **`freelymove` now frees movement on channeled / locked abilities.** A channel's "can't move" comes from
  a movement-impair flag on the spell's own buff (not the cast windup we were editing). `freelymove` now
  clears that flag too, so you can move while the ability runs. `defaults` restores the original lock.
- **New admin diagnostic:** `.beelz admin dump <abilityGuid|form>` logs an ability's full component chain
  (or your active form buff) to the server log — handy for reporting exactly how an ability behaves.

## [0.89.0] - 2026-05-30

### Fixes from testing: form abilities now render + freelymove works on channels

- **Shapeshift form abilities now actually appear on the form bar.** The previous fix injected them
  correctly but a moment too late — after the game had already drawn the form's bar — so nothing showed.
  The loadout is now injected at the exact moment the form bar is built (the same path the weapon bar
  uses), so your granted abilities render on the form and are castable.
- **`freelymove` now works on channeled/locked abilities.** It had no effect because the ability's
  movement lock was set to "use the whole cast duration," which overrode the seconds you specified.
  We now clear that flag when you set `freelymove`, so the timer you give is respected — e.g.
  `freelymove 2` frees you ~2 seconds into a channel that otherwise roots you the whole time.

## [0.88.0] - 2026-05-30

### Server announcements: collection-complete + periodic leaderboard

- **100% collection broadcast (default ON).** When a player collects every capturable ability, the whole
  server sees a thematic announcement. Customize the message pool (pipe-separated, `%player%` placeholder)
  via the `Broadcast_CollectionComplete_Messages` config; toggle with `Broadcast_CollectionComplete_Enabled`.
- **Periodic leaderboard broadcast (default OFF).** Optionally announce the top collectors server-wide on a
  schedule. Admins toggle/configure it live:
  - `.beelz admin broadcast leaderboard on|off`
  - `.beelz admin broadcast interval <minutes>` (60 = hourly, 1440 = daily)
  - `.beelz admin broadcast top <1-5>` — how many to list
  - `.beelz admin broadcast complete on|off` — the 100% announcement
  - `.beelz admin broadcast test` — send one now · `.beelz admin broadcast status` — show current settings
  Message pool (`%top%` / `%count%` placeholders) lives in `Broadcast_Leaderboard_Messages`.

## [0.87.0] - 2026-05-30

### New cast modifiers: free movement mid-cast + interrupt-on-attack

Two admin ability modifiers (server-wide, applied when `Abilities_ApplyConfig` is on — the default):

- **`.beelz admin ability <name|id> freelymove <seconds>`** — for spells that root you in place during a
  long cast, this frees you to move **after that many seconds into the cast**, while the spell keeps going.
  (E.g. a spell that locks you for its whole 5s cast → `freelymove 2` releases you after 2s.) `freelymove 0`
  lets you move immediately; `clear` (or `defaults`) restores the original lock.
- **`.beelz admin ability <name|id> interruptonhit on`** — makes an ability **cancel when the caster is
  attacked** (takes damage). Some abilities don't stop when you're hit and probably should — this fixes
  that per-ability. `off` removes it; `clear`/`defaults` restores the baked behavior. (This is separate from
  `interruptible`, which is the player's own dash/shield self-cancel.)

Both are reset by `.beelz admin ability <id> defaults` (and the existing `interrupt`/`freemove`/`castspeed`
modifiers are now restorable by `defaults` too).

## [0.86.0] - 2026-05-30

### Fixes: shapeshift-form abilities now actually appear + honest collection %

- **Shapeshift form abilities now show up on the form bar.** `.beelz form-grant`'d abilities were being
  injected through a hook the form-entry never reached, so entering a (wolf/bear/…) form showed only its
  native bar. Form entry is now detected reliably (on the shapeshift event itself) and your granted
  abilities are injected the instant the form appears — including skinned forms.
- **Collection % no longer exceeds 100%.** `.beelz top` and `.beelz progress` measured your collection
  against the curated rules list (~450), which a full-devour player can blow past — so it showed nonsense
  like 133%. The denominator is now the **full set of capturable abilities in the game** (~1400), so the
  percentage is honest and 100% genuinely means you've collected everything. The "🏆 COLLECTION COMPLETE"
  milestone now fires at true 100% (it used to trigger far too early).

## [0.85.0] - 2026-05-30

### Force-timeout: make indefinite ability effects expire

Some abilities apply effects/buffs that otherwise last **forever**. Admins can now force them to expire:

- `.beelz admin ability <name|id> forcetimeout <seconds>` — the ability's spawned effects/buffs will now
  auto-expire after that many seconds, even if they had no built-in duration (it adds one).
- `forcetimeout 0` / `clear` (or `.beelz admin ability <id> defaults`) removes the forced timeout and
  restores the effect's original behavior.

This complements `duration` (which adjusts effects that already have a length); `forcetimeout` is for the
truly indefinite ones.

## [0.84.0] - 2026-05-30

### Form abilities now appear on the form bar + collection-complete + BCH tooltip lookup

- **Shapeshift form abilities now show up.** A form (wolf/bear/…) only has a couple of ability slots, and
  they aren't your normal spell slots — so `.beelz form-grant` to slot 5/6 went to a slot the form never
  displays. Your form abilities now map onto the slots the form actually renders (in order), so they
  appear and are castable.
- **Collection-complete milestone.** Capture the last available ability and you get a one-time
  "🏆 COLLECTION COMPLETE!" message (and BCH gets a `collection-complete` event).
- **(BCH) Ability tooltips by GUID.** New `api info-guid <guid>` returns full tooltip data for any ability
  by its ID — fixing "No name"/generic tooltips for the abilities on your active bar.

## [0.83.0] - 2026-05-30

### Player extras: leaderboard, odds, quieter messages, pity options

- **`.beelz top`** — a server leaderboard of the players who've collected the most abilities (count + %).
  Admins are excluded so the competition is fair.
- **`.beelz odds`** — see your current ability/devour drop chances and your accumulated pity (bad-luck
  protection) bonus.
- **`.beelz silent on`** — hide the "you already knew all of its abilities" message when you devour a unit
  you've fully collected (`.beelz silent off` to bring it back).
- **New admin config `Capture_PitySessionBased`** — set it true to make pity reset each time a player logs
  out (session-based), or leave it false (default) for permanent, multi-session pity.

## [0.82.0] - 2026-05-30

### Summon timeout / cooldown now tick even while idle

Building on v0.81: the periodic heartbeat now also runs on a real per-frame driver, so summon timeouts and
the cooldown enforcer fire on time even when you're standing still (e.g. you set a long cooldown and can't
re-cast). Previously they only advanced while something was happening (a cast, combat, a death).

## [0.81.0] - 2026-05-30

### Fix: summon timeout & live config changes now apply reliably

Time-based features (summon auto-despawn, the granted-ability cooldown enforcer, debounced saves) were
only being processed when a unit died — so if you weren't killing anything, summons outlived their timer
(then despawned all at once on the next kill) and per-ability cooldown/config changes didn't take effect
until you reloaded. They now run on a steady heartbeat during normal play, so:
- Each summon group despawns ~on its own timer (timers are per-cast, not shared).
- Per-ability cooldown / summon settings apply live without a reload.

## [0.80.0] - 2026-05-30

### Fixes from testing: skinned forms, summon cap meaning, summon timeout

- **Shapeshift form abilities now work with skinned forms.** Custom form loadouts weren't appearing
  because cosmetic form *skins* (e.g. the wolf "Skin02"/"Blackfang" variants) weren't recognized — only
  the base form was. All skin variants are now detected, so `.beelz form-grant` applies in them.
- **`summoncap` is now a true "number of active uses" cap.** Previously, setting a low cap on a
  multi-unit summon (e.g. a 10-skeleton horde) wrongly trimmed it to that many *skeletons*. Now `summoncap`
  only limits how many simultaneous casts are active; the cast itself keeps its full unit count.
- **New `summonunits` knob** for what the old behavior accidentally did, on purpose: cap the number of
  **units a single cast** summons. `.beelz admin ability <id> summonunits <n>` (0 = the ability's natural
  count). It's independent of `summoncap` — whichever limit is hit first applies.
- **Summon timeout reliability.** Per-ability `summontimeout` now resolves the ability correctly (it
  could fall back to the global timeout before), and expirations are logged when verbose logging is on.

## [0.79.0] - 2026-05-29

### Summon governance: caps + a 30-second timeout on all summons

Summon abilities are now governed by default so hordes can't accumulate forever:
- **Cap** — at most **3** simultaneous "uses" of a given summon ability (this already existed; it now
  applies to every summon ability, transform or captured).
- **Timeout** — summons now **auto-despawn after 30 seconds** by default (previously off). Set
  `Transform_SummonLifetimeSeconds = 0` to let them live forever again.
- **Per-ability overrides** — admins can override either for a specific ability, and the per-ability
  value wins over the global default:
  - `.beelz admin ability <name|id> summoncap <n>` (0 = unlimited)
  - `.beelz admin ability <name|id> summontimeout <seconds>` (0 = never expires)
- Reset with `.beelz admin ability <id> defaults` (or `all defaults`) like the other ability config.

## [0.78.0] - 2026-05-29

### Shapeshift forms now behave like a weapon loadout

Each form (Wolf/Bear/Rat/Spider/Toad/Werewolf/Gargoyle) now works like a weapon family for your bar:
- **Enter a form** → its assigned abilities (`.beelz form-grant`) auto-load into your slots.
- **Leave the form** → your bar reverts to whichever **weapon group** you currently have equipped.
- `.beelz form-grant` / `.beelz weapon-grant` now also accept an **ability ID** (not just a list index),
  matching `.beelz grant`.

(Requires `Forms_CustomAbilities_Enabled`, which is on by default as of v0.75.)

## [0.77.0] - 2026-05-29

### Fix: a configured cooldown no longer "bleeds" onto other abilities

Setting a long cooldown on one ability could make *other* abilities you placed on the same slot suddenly
inherit that long cooldown (cooldown was sticking to the slot, not the ability). Now a slot only carries
its cooldown forward when the **same** ability re-resolves there (e.g. on a weapon swap, so your
in-progress cooldown is preserved); placing a **different** ability on that slot gives it its own
cooldown. Cooldown follows the ability, not the slot.

## [0.76.0] - 2026-05-29

### Fix: ability info command crash + stable ability indexes + grant by ID

- **`.beelz info` / `api info` no longer crashes.** With the ability-config fields added over recent
  versions, the info line outgrew the chat system's size limit and threw an error (the command failed
  outright; the BCH catalog could abort mid-stream). Long lines are now sent in parts and reassembled.
- **Your ability indexes are now stable across logins.** Previously the list order could shuffle each
  session, so a memorized "index 5" wasn't reliable. The list is now sorted deterministically.
- **`.beelz grant` accepts an ability ID.** `.beelz grant <slot> <index OR ability ID>` — pass the
  stable ability ID (from `.beelz list`) to bind a specific ability without worrying about its index.
- **`.beelz resetbar` now asks for confirmation** (`.beelz resetbar CONFIRM`) so you can't wipe all
  your slot bindings by accident. (Your captures + unlocks are always kept either way.)
- Setting a **cooldown** on a charge-based ability (e.g. a dash) now tells you it's recharge-governed and
  points you at `chargetime`/`charges`.

## [0.75.0] - 2026-05-29

### Per-form ability loadouts are now ON by default

The experimental "custom abilities on shapeshift forms" feature (`Forms_CustomAbilities_Enabled`) now
defaults to **on**, so `.beelz form-grant` works out of the box and your loadout carries into Wolf/Bear
forms (and the form holds while you cast). Set `Forms_CustomAbilities_Enabled = false` to restore stock
vanilla form behavior. Still experimental — feedback welcome.

## [0.74.0] - 2026-05-29

### Fix: a vanilla spell you pick now stays put across weapon swaps

When you used the in-game spellbook to put a normal spell back onto a slot that held a granted ability,
it worked — until you switched weapons and back, at which point the granted ability re-appeared over your
pick. Two causes: the "you re-picked this slot" baseline was only captured on a weapon swap (so a
grant-then-repick had nothing to compare against), and a never-before-used weapon would re-adopt the
re-picked ability and re-mask it.

- The baseline is now captured the moment an ability is granted to a slot.
- A vanilla re-pick on a spell slot is now recognized on **any** weapon (we detect that the slot's base
  is weapon-independent), so your pick sticks across every weapon group and the granted ability is
  cleared from all of them — instead of creeping back on a weapon swap.
- Weapon-specific ability slots (whose ability legitimately differs per weapon) are unaffected — they
  won't be cleared by mistake.

## [0.73.0] - 2026-05-29

### Ability shaping: range & duration reach more, clearer charges feedback

- **`range` now shortens projectiles too.** On a projectile spell, `range` previously only changed the
  aim/cast clamp while the projectile kept flying its full distance. It now also clamps the projectile's
  actual travel, so a smaller `range` visibly shortens the spell.
- **`duration` now affects over-time / channel effects.** In addition to applied buff/debuff length, it
  now sets the lifetime of directly-spawned effect buffs (e.g. a heal/DoT channel), so `duration` actually
  shortens or lengthens those. (It still does **not** change an ability's cast/channel *time* — that would
  re-root the caster — only how long the resulting effect lasts.)
- **Charges feedback.** `charges`/`chargetime` only work on abilities that already have a charge system
  (we can't add one). Setting them on an ability without charges now tells you so instead of silently
  doing nothing. Reminder: set **one field per command** (`charges`, then `chargetime` separately).

## [0.72.0] - 2026-05-29

### Fix: ability healing config (and the crash); new "reset to defaults"

- **Healing config fixed.** Setting an ability's `healing` multiplier was reading and writing the wrong
  internal data, which made channeled/over-time heals behave erratically — dropping to a trickle instead
  of scaling, and **crashing the server** when set to 0. Healing now scales correctly from the ability's
  original values (so reloads don't compound it), and `healing 0` is safe.
- **Reset to shipped defaults.** New admin commands to undo ability shaping without restarting the server:
  - `.beelz admin ability <id> defaults` — reset one ability's tuning (cooldown, range, charges, AoE,
    projectile speed, duration, healing, interrupt/freemove/cast-speed, damage/cooldown scale) to baseline.
  - `.beelz admin ability all defaults` — reset every ability at once.
  - Your capture/availability rules (enabled, weapons, forms, deny lists) are left untouched. A server
    restart always restores a full baseline as well.

### Known notes from testing

- `charges`/`chargetime` only apply to abilities that already have a charge system; `range` on a
  projectile spell is the aim/cast clamp (the projectile's own travel is tuned separately); `duration`
  sets applied buff/debuff length, not an ability's channel time. Broader coverage for these is planned.

## [0.71.2] - 2026-05-29

### Fix: ability tweaks were being wiped on reload/restart

The new per-ability settings (cooldown, range, charges, AoE radius, projectile speed, effect duration,
healing) were saved to the config file correctly, but the server **dropped them from memory** every
time the rules were (re)loaded — so they only worked until the next `.beelz admin reload` or restart.
They now persist properly, which is what was blocking the cooldown config from ever taking effect.

## [0.71.0] - 2026-05-29

### Ability cooldown config now actually applies to abilities you cast

Setting an ability's cooldown wrote the value but didn't change the cooldown you experienced when
casting a captured ability from your bar — V Rising drives a granted ability's live cooldown from
per-player slot state, not the value we were editing.

- Beelzebub now **enforces the configured cooldown directly on the slot** the moment you cast: a
  captured ability with a set cooldown (or under the global minimum-cooldown floor) is re-anchored to
  that value. Lengthen or shorten any captured ability's cooldown and it takes effect on the next cast.
- The earlier prefab-based cooldown still drives the displayed value and NPC casts; this adds the live
  player-cast enforcement on top.

## [0.70.0] - 2026-05-29

### Fix: ID-set ability tweaks no longer lost on already-configured abilities

- When an ability set by **ID** was auto-migrated to its name (v0.69), and that ability already had
  server-curated settings, the migration **dropped** your tweak instead of merging it. It now merges
  correctly, so a cooldown/range/etc. you set by ID sticks even on a pre-configured ability.
- Ability tweaks that **match no ability** (usually a wrong name/ID) are now flagged in the server log
  so admins can spot the mistake.

## [0.69.0] - 2026-05-29

### Fix: ability-config commands now accept an ability ID (not just its name)

The ability-tuning commands (`.beelz admin ability …` / `.beelz admin tune …`) only worked if you
typed the ability's full prefab **name**. If you used the **ID** shown in `.beelz list` / your loadout
/ BloodCraftHub (e.g. `.beelz admin ability 874909393 cooldown 10`), it silently saved a setting that
never applied — because the tuner matches abilities by name.

- These commands now accept **either the ID or the name**, resolving the ID to the ability automatically.
- Any settings you'd already saved by ID are **auto-migrated** to the correct ability on the next load
  / `.beelz admin reload` — so a value you set earlier will start working without re-entering it.
- This also fixes ability config driven from BloodCraftHub (which uses IDs).

## [0.68.0] - 2026-05-29

### Ability shaping: effect duration & healing (admin)

Two more server-wide ability levers (applied by default — see v0.66):

- **Effect/debuff duration** — `.beelz admin ability <name> duration <seconds>` sets how long the
  buffs/debuffs an ability applies last.
- **Healing** — `.beelz admin ability <name> healing <multiplier>` scales an ability's healing
  (1.0 = unchanged, 1.5 = +50%, 0.5 = half). Computed from the ability's original values, so repeated
  `.beelz admin reload`s don't stack the multiplier.
- (Shortcuts via `.beelz admin tune <name> <duration|healing> <value>`.)
- BloodCraftHub sees these as `duration_override` / `heal_mult` on ability info + the catalog.
- **Caveat:** these reach an ability's primary spawned effect; a few abilities with deeply-nested
  effects may not respond — verify in-game. (Next: multi-ability rules — incompatible-ability locks,
  chain triggers, and stack limits.)

## [0.67.0] - 2026-05-29

### More ability shaping: charges, AoE radius, projectile speed (admin)

Expanding the server-wide ability-config surface (all applied by default — see v0.66):

- **Charges** — `.beelz admin ability <name> charges <n>` and `chargetime <seconds>` control
  charge-based abilities (how many casts before recharge, and the recharge time).
- **AoE radius** — `.beelz admin ability <name> aoe <radius>` resizes an ability's area of effect.
- **Projectile speed** — `.beelz admin ability <name> projspeed <speed>` changes how fast its
  projectile travels.
- (Shortcuts via `.beelz admin tune <name> <charges|chargetime|aoe|projspeed> <value>`.)
- BloodCraftHub sees these as `charges_override` / `chargetime_override` / `aoe_override` /
  `projspeed_override` on ability info + the catalog.
- These are **server-wide** edits (consistent for every player, and the source NPC/boss too), applied
  automatically. More levers (effect duration, healing) and multi-ability rules (incompatible-ability
  locks, chain triggers, stack limits) are coming next.

## [0.66.0] - 2026-05-29

### Ability configuration is now on by default (no opt-in)

Server-wide ability balancing is meant to be **defined and adjustable out of the box** — not something
you have to switch on first.

- The old `AbilityTuning_Enabled` switch (default off) is replaced by **`Abilities_ApplyConfig`
  (default ON)** — a master kill-switch, not an opt-in. Any per-ability config you set (cooldown,
  range, charges, interrupt, free-move, cast-speed, …) now **applies automatically** at server start
  and on `.beelz admin reload`. Existing servers adopt the new default automatically (the old key is
  migrated out).
- These edits are **server-wide**: an ability behaves the configured way for *every* player who casts
  it, and — because V Rising shares the ability's data — the source NPC/boss's version changes too.
  That's intended: you're configuring how the ability functions on the server, consistently.
- Set `Abilities_ApplyConfig` to false only if you want to disable all baked ability-config edits.

*(This is the foundation for an expanding server-wide ability-config surface — damage, AoE, durations,
charges, and more — landing in upcoming updates.)*

## [0.65.0] - 2026-05-29

### Tune captured abilities: set cooldowns and range (admin)

The first half of expanded per-ability balancing — for tuning whether a captured ability is actually
*viable* on your server. Requires `AbilityTuning_Enabled` (these are baked, server-wide edits).

- **Absolute cooldown.** `.beelz admin ability <name> cooldown <seconds>` (or `.beelz admin tune
  <name> cooldown <seconds>`) sets an ability's exact cooldown — rein in a spammy capture or speed up
  a sluggish one.
- **Max cast range.** `.beelz admin ability <name> range <distance>` sets how far it can be cast.
- **Global minimum cooldown.** New `Grant_MinimumCooldownSeconds` config floors *every* ability's
  cooldown at once — a one-setting clamp on zero/low-cooldown abilities server-wide.
- BloodCraftHub now sees these via `cooldown_override` / `range_override` on ability info + the catalog.
- **Caveat:** like the existing cast-tuning, these are GLOBAL prefab edits — they also change the
  source NPC/boss version of the ability. (Per-ability *healing scale* and *effect duration* aren't
  exposed — they're baked into each ability's spawned effects with no safe override point. Stack
  limits, incompatible-ability locks, and chain triggers are planned for the next update.)

## [0.64.0] - 2026-05-29

### Clearer "Devour" config + separate Devour luck dial

Config clarity for admins — no gameplay change out of the box; existing settings are migrated for you.

- **"Devour" naming.** The rare jackpot drop-chance settings are renamed to say what they do:
  `DropChance_Transform_Regular` → **`DropChance_Devour_Regular`** and `DropChance_Transform_VBlood`
  → **`DropChance_Devour_VBlood`**. If you'd tuned the old keys, your values are carried over
  automatically (the old keys are removed from the config file).
- **Separate Devour bad-luck protection.** New **`Capture_PityIncrement_Devour`** /
  **`Capture_PityMax_Devour`** let you tune the Devour jackpot's pity curve independently from
  ability-capture pity. They default to your existing ability-pity values, so nothing changes until
  you adjust them.
- **Global dials clarified.** Config descriptions now make clear that the summon caps/lifetime/power
  settings apply to **every** summon ability (not just transforms). (A global minimum-cooldown floor
  and per-ability cooldown/range/duration overrides are coming with the per-ability tuning expansion.)

## [0.63.0] - 2026-05-29

### Granted abilities show instantly, and vanilla picks stick across weapons

- **No more weapon-swap dance.** When you grant a captured ability to a slot (or load a preset, or
  `.beelz refresh`), it now appears on your action bar immediately — you no longer have to switch
  weapons back and forth to make it show up.
- **Picking a vanilla spell now wins everywhere.** If you re-select one of your normal spells in the
  in-game spellbook on a slot that had a captured ability, that choice now sticks across **all** your
  weapons — swapping weapons no longer re-applies the captured ability over your vanilla pick. (Note:
  this clears that slot's Beelz bind for every weapon set; re-grant per-weapon afterward if you want a
  weapon-specific bind back. Shapeshift-form loadouts are unaffected.)

## [0.62.0] - 2026-05-29

### Your slotted abilities now reliably return after a relog

- **Grants re-apply on login.** When you reconnect, your universal (and current-weapon) slot binds
  are now re-applied to your action bar immediately, instead of sometimes staying vanilla until you
  swapped weapons or ran `.beelz refresh`. This was the last spot in the grant lifecycle that didn't
  explicitly restore your loadout (weapon-swap, transform revert, reset, and refresh already did).
- No new commands or settings; universal (Magic) abilities apply on any weapon as before.

## [0.61.0] - 2026-05-29

### Stability: a bad spell slot can no longer break your bar

Hardening pass on the slot system — no new commands or settings, nothing to reconfigure.

- **Out-of-range slot guard.** Slot binds are now validated (1-6) both when saved and when applied
  to your action bar, and a bind is skipped if your live bar doesn't (yet) have that slot. This
  closes a path where a corrupt or stale saved slot could cause an `IndexOutOfRange` while
  resolving your bar (the long-standing intermittent "grant → bar" error). Bad binds are logged and
  ignored instead of crashing; valid binds that arrive during a transient bar state simply apply on
  the next resolve.
- **Capture-luck (pity) tracking hardened** against malformed saved data.

## [0.60.0] - 2026-05-28

### Better ability descriptions & magic-school tags

A data update that nearly doubles how many abilities come with a real, in-game description and a
magic-school label — so the in-app guide, ability info, and BloodCraftHub's Bestiary show far more
useful detail.

- **Descriptions roughly doubled** (about 13% → 26% of all abilities). The text now comes straight
  from V Rising's own tooltip strings, so it matches what you'd read in-game.
- **Magic-school tags more than doubled** (about 4% → 9%) — Blood, Chaos, Frost, Illusion, Storm, Unholy.
- **Fixed broken/foreign entries:** a number of abilities previously showed placeholder text or text
  in the wrong language (Russian, German, Chinese, French). Those are now correct English or cleanly
  blank (falling back to the friendly name).
- No new commands or settings; nothing to reconfigure. Admins can still override any description or
  school per ability in `ability_metadata_overrides.json`.

## [0.59.0] - 2026-05-28

### Per-form ability loadouts (Wolf, Bear, Rat, Spider, Toad, Werewolf, Gargoyle)

Build a distinct captured-ability set for each shapeshift form, the same way you can per weapon.

- **`.beelz form-grant <form> <slot> <index>`** binds a captured ability to a slot for a specific
  form, and **`.beelz form-unslot <form> <slot>`** clears it. Enter that form from the in-game
  shapeshift wheel and your custom abilities are on its bar (the form also holds through casting
  instead of dropping you out). Use `auto` for the form you're currently in.
- Your per-form sets are saved with the rest of your collection and survive relogs. If you haven't
  built a set for a form yet, it falls back to your universal binds.
- **This is opt-in / experimental:** it only takes effect when the server admin enables
  `Forms_CustomAbilities_Enabled`. Visuals are limited to the seven forms V Rising itself ships.

## [0.58.0] - 2026-05-28

### Friendly names + ability descriptions for the companion app

More polish for the BloodCraftHub collection book — these are data the companion app reads; they
don't change gameplay.

- **Friendly unit & ability names in your collection feed.** The captured-ability stream now carries
  proper display names (e.g. "Blackfang Morgana") instead of leaving the companion app to clean up
  raw prefab names — so boss and ability names read correctly out of the box.
- **Descriptions & magic school on catalog entries.** The collection catalog now includes an ability
  description and its magic school where we have them, so the Bestiary can show that text on entries
  you haven't captured yet. (Coverage is partial today — about 1 in 8 abilities has a written
  description so far; the rest fill in as we extract more from the game's own text.)

## [0.57.0] - 2026-05-28

### Complete collection book + cleaner ability categories

Mostly for the BloodCraftHub companion app's collection/Bestiary view, plus a tidier ability-type
badge everywhere.

- **The collection catalog now lists every capturable ability**, not just the ones an admin has
  hand-curated in the rules file. BloodCraftHub's Bestiary "Missing" list is now complete — you can
  see the whole pool you're collecting toward. (Each entry is tagged as curated or auto-discovered.)
- **Fewer abilities show as "Other."** The category classifier got smarter about primary/heavy
  attacks (and tells melee from ranged-weapon attacks), unarmed strikes (kick/punch), ground fields,
  pistols, and electric discharges — so more abilities show a meaningful type badge. Admins can still
  pin any stubborn one with a `Category` override in `ability_rules.json`.

## [0.56.0] - 2026-05-28

### Mix and match: the in-game spellbook can take a slot back from a captured ability

Fixes the most-reported loadout problem from testing: once you put captured abilities on your bar,
vanilla spells from the in-game spellbook seemed to "refuse to attach," and clearing/refreshing was
inconsistent.

- **Pick a vanilla spell in the spellbook and it just works again.** If you assign a different
  in-game ability to a slot that currently holds a captured ability, Beelzebub now notices and
  **releases that slot back to you** — your spellbook pick sticks. Mix captured abilities and vanilla
  spells freely across your six slots. (This is per weapon set, so it doesn't disturb your other
  weapon loadouts.) Releasing a slot this way drops its saved bind — just re-grant it anytime with
  `.beelz grant` / `.beelz weapon-grant` (or from BloodCraftHub).
- **Clearing a slot now returns it to vanilla immediately.** `.beelz unslot`, `.beelz resetbar`, and
  the per-slot Clear in BloodCraftHub now snap the slot straight back to your in-game ability on the
  spot — no more swapping weapons or relogging to "unstick" it.

## [0.55.0] - 2026-05-27

### Public test-launch prep — front page & docs

Documentation/front-page pass ahead of the first public Thunderstore test release. **No gameplay
changes** — the mod build is identical to v0.54.0.

- Rewrote the front page with a clear **early-access (pre-1.0, server-side)** header up top, and
  spelled out the important caveats: not every captured ability is guaranteed to work (evaluating
  **ability viability** is a core part of testing, so we can trim the catalog to what's actually
  fun and usable), a **strong recommendation to run the BloodCraftHub companion app**, and explicit
  **mod-compatibility** notes (we aim to integrate with Bloodcraft and KindredCommands but that
  isn't finalized — other mods are use-at-your-own-risk).
- Added/clarified installation steps, the roadmap, **tester acknowledgements**, a screenshots
  placeholder, and credit to Bloodcraft's familiar system as an inspiration.

## [0.54.0] - 2026-05-27

### BloodCraftHub can now read every ability detail

Completes the audit by closing the wire-API holes — the companion app can build full tooltips and
admin panels without hand-parsing the rules file.

- **`api info`** now also reports the category badge, the description's cast time / range /
  behavior, the multi-phase grouping, allow-denied, the cast-tuning state (interruptible / free-
  move / cast-speed), and whether the category is an admin override.
- **`catalog-abilities`** adds the same per-ability rule fields (phase, allow-denied, tuning,
  category-override) so an admin panel reflects the full rules matrix.
- **`api rules`** now surfaces the global `Defaults` scaling baseline and the transform-only lists;
  **`catalog-units`** surfaces each unit's `SlotTemplate`.
- **Config-change notifications broadcast to all connected clients** (were sent only to the admin
  who made the change), so every open panel refreshes.

*BloodCraftHub note: ApiVersion → 8 (all additive). The `cat=` field is the category **name**
(e.g. `Melee`), not a number — treat any unknown name as `Other`.*

## [0.53.0] - 2026-05-27

### Admins can configure any ability live — no JSON editing

From the configuration audit: nearly every per-ability and per-unit setting used to require
hand-editing `ability_rules.json`. Now an admin can set all of them in-game, and they persist
instantly.

- **`.beelz admin ability <name> <field> <value>`** — set *any* per-ability rule live: enable/
  disable, allowed weapons, forms, transform-only, difficulty, phase, damage & cooldown scaling,
  category badge, cast tuning, notes.
- **`.beelz admin transform-set <CHAR_unit> <field> <value>`** — set per-unit transform tuning
  (enabled, difficulty, tier, the four stat scales, full-replace, power-scaling mode, notes).
- **`.beelz admin default <damagescale|cooldownscale> <value>`** — server-wide scaling baseline.
- **`.beelz admin denyguid|allowguid <add|remove> <guid>`** and **`.beelz admin transformonly
  <add|remove> <pattern|guid>`** — the GUID-based capture filters and bulk transform-only lists
  (previously file-only).
- **Sturdier config:** invalid values in the rules file are now caught and corrected with a
  warning (bad difficulty → Basic, unknown category ignored, cast-speed clamped to 0–1, drop
  rates clamped to 0–1), and a failed file-write is reported instead of silently claiming success.
- Full reference refreshed in `docs/ABILITY_CONFIG.md`.

## [0.52.0] - 2026-05-27

### Chained abilities fire untransformed

Some captured abilities would slot onto your bar but "do nothing" when cast in normal form —
their projectiles/AoEs spawned on the enemy team and passed right through your targets, because
the team-fixup that makes chain effects player-friendly only ran while you were *transformed*.

- **Captured chain abilities now hit while untransformed.** When you cast a captured ability in
  normal form, the effects it spawns (projectiles, ground AoEs, and teleport-and-detonate blasts
  like the Undead Priest's Mist Walk) are now retargeted to *your* team for a few seconds after
  the cast — so they damage enemies instead of fizzling. Summons already worked untransformed
  (v0.45); this closes the gap for the rest.
- Tightly scoped: only effects owned by a player who *just cast a captured Beelzebub ability* are
  touched, so your normal weapon/spell abilities and other mods are unaffected.

Known limit: a few abilities are animation/rig-bound to their original creature and still won't
look or fire perfectly without that form — that's the model-swap ceiling, not something a
server-side mod can fully close.

## [0.51.0] - 2026-05-27

### Fewer "Other" abilities — smarter categorization

Many abilities were showing up in a vague **"Other"** category. This build classifies them
properly so BloodCraftHub (and `.beelz list`) can badge them correctly.

- **Broadened auto-categorization.** Abilities named after the move itself (CrossWindSlash,
  LoomingMists, MountainRumbler, …) now resolve to a real category instead of "Other." Added a
  new **Melee** category for weapon strikes, widened the Travel/AoE/Projectile/Buff/Spell tells,
  and added a magic-school fallback so themed boss spells land as spells.
- **Admin can override any badge.** Set `"Category": "Melee"` (or Travel/Aoe/Projectile/Summon/
  Buff/WeaponSpell/Spell) on an ability in `ability_rules.json` to force its category — see
  `docs/ABILITY_CONFIG.md`.

*BloodCraftHub note: ability `cat=` now includes the new `Melee` value and reflects the broader
classification — treat any unknown `cat=` as "Other" (forward-compatible).*

## [0.50.0] - 2026-05-27

### Test everything: inclusive capture, no transform-only wall, admin config guide

This build opens the ability system up for broad testing and gives admins a real config file.

- **Inclusive capture (on by default).** New `Capture_InclusiveMode` makes abilities across
  **all** V-Bloods and NPCs broadly capturable and devourable — the old deny lists and the
  Basic/Brutal difficulty gate are bypassed, so far fewer abilities are hidden. A small junk
  filter (idle/spawn/death animation stubs) and the per-ability off-switch still apply. Set it
  to `false` for a curated server and the old filtering returns.
- **No more "only available for transformation" wall (by default).** New
  `Grant_EnforceTransformOnly` (default **off**) lets you grant, slot, hotkey, and devour
  *every* ability to your normal bar for testing. Turn it on to reserve flagged abilities for
  transforms again.
- **Admin config guide + global defaults.** New `docs/ABILITY_CONFIG.md` documents every
  global and per-ability setting (enable/disable, damage & cooldown scaling, weapon allow-list,
  cast tuning) and how to live-reload with `.beelz admin reload`. New `Defaults` block in
  `ability_rules.json` sets a server-wide damage/cooldown baseline in one place.

Note: a few abilities still won't *fire* correctly when cast untransformed (chained / multi-part
abilities) — that's the next build's focus.

## [0.49.0] - 2026-05-27

### Fixes from BCH testing: Morgana crash, Reaper loadouts, "twin hammer"

Three fixes from the latest play-test.

- **Morgana transform no longer crashes the server.** Transforming into Morgana (and any
  rapid double-trigger of a transform) could hit a hard server crash. Two guards close it:
  the form buff is never torn down twice, and a second transform is politely refused
  (*"Still transforming — give it a moment"*) while the first is still applying. Dracula and
  all other transforms are unaffected.
- **Reaper (and every) weapon loadout now registers.** Abilities you assign to a specific
  weapon's bar — e.g. your Reaper set — were being silently dropped when you drew that weapon,
  because the game re-guessed each ability's "home weapon" from its name and tossed anything
  that didn't match. Now an ability you *explicitly* place on a weapon's bar stays there (only
  the admin kill-switch / transform-only reservation can remove it). The universal loadout still
  filters by weapon so a sword spell won't auto-fire on a crossbow.
- **Removed the phantom "Dual Hammers" weapon option.** Dual Hammers exists only as unused,
  unobtainable data in V Rising (no craftable weapon), so it no longer appears as a selectable
  weapon family, and any ability named for it is now treated as universal (usable on any bar)
  instead of being locked to a weapon nobody can equip.

## [0.48.0] - 2026-05-27

### Experimental: custom abilities on vanilla forms (Wolf/Bear test) — the real mechanism

This implements the **actual** path for "use your own abilities in a shapeshift form": you unlock
forms from bosses and enter them with the in-game shapeshift wheel as normal, and Beelzebub
recognizes the form and loads your abilities into it. (v0.47.0's admin command forced the form a
different way; this is how it really works in play.)

- Enable `Forms_CustomAbilities_Enabled` (off by default). Then, when you shift into **Wolf or
  Bear** (the current test forms), your current loadout's abilities are placed on the form's bar,
  and the form's "break on cast" trigger is removed — so you can actually *cast* them without
  dropping out of the form (vanilla travel forms normally exit the instant you cast something).
- Safe by design: the form keeps its normal "exit on logout" behavior, and the changes live on the
  form buff itself, so it can't get your action bar stuck. Turn the setting off to restore vanilla.

This is the make-or-break feasibility test. If Wolf/Bear hold through casting in-game, the next
step is full per-form ability assignment (a loadout per form, auto-applied when you shift into it,
like the per-weapon loadouts) across all the forms. If a form won't hold, we'll learn which and why.

## [0.47.0] - 2026-05-27

### Experimental: native-form test (admin) — groundwork for per-form ability sets

First step toward "wolf/bear/etc. forms with your own ability set" (the same idea as the
per-weapon loadouts, but for shapeshift forms). This release adds an **admin test command only**,
so we can verify the make-or-break question in-game before building the full feature: does a
native shapeshift form *hold* when you cast your own (non-form) abilities, or does it drop on
the first cast?

- **`.beelz admin testform <wolf|bear|off>`** — puts you into Wolf or Bear form (using the same
  persistent form mechanism Dracula and Morgana use) carrying your current loadout's abilities,
  so you can test casting them in-form. `off` exits. It routes through the normal transform
  lifecycle, so `.beelz revert` ends it and logging out clears it automatically — it cannot get
  your action bar stuck.

Nothing changes for normal play — this only does anything when an admin runs the test command.

## [0.46.0] - 2026-05-27

### Make long casts interruptible + free movement after a cast (opt-in)

Two admin-curated cast tweaks, **off by default** — turn on the new `AbilityTuning_Enabled` server
setting, then tag abilities in `ability_rules.json` (or with `.beelz admin tune`):

- **Interruptible casts.** Mark an ability `interrupt on` and its long cast can be cancelled by
  player action — dash out of danger or raise a shield mid-cast instead of being locked into the
  cast. `interrupt off` makes it uninterruptible.
- **Free movement after the cast.** Some spells keep you rooted *after* the cast bar finishes,
  until the whole effect ends. Mark an ability `freemove on` and the movement lock is clamped to
  the cast itself, so you can move the moment casting completes. `castspeed <0..1>` also tunes how
  fast you move *during* the cast (0 = rooted, 1 = full speed).
- New admin commands: `.beelz admin tune <ability> <interrupt|freemove|castspeed> <on|off|0..1>`
  and `.beelz admin tune-list`; `.beelz admin reload` re-applies tuning live (no restart).

> **Heads-up:** this rewrites the ability's shared cast data, so it also changes how the original
> NPC/boss casts that same ability. It's opt-in — enable it, tune one ability, and test in-game
> before curating broadly. (A movement-lock that comes from a buff rather than the cast itself
> isn't covered by this.)

## [0.45.0] - 2026-05-27

### Summons work without transforming + clearer loadouts

- **Summon abilities now work in normal form.** Captured summon abilities (Raise-Dead,
  Reinforcements, skeleton/bat callers, and the like) cast while you are **not** transformed now
  spawn proper player-allied minions that follow you, fight alongside you, respect the
  per-ability summon cap, and clean up on death / logout — exactly like summons cast while
  transformed. Previously they only behaved mid-transform, so casting one in normal form could
  leave an untracked or hostile spawn.
- **`.beelz summons` works untransformed**, with a new **`clear`** action to despawn all your
  summons at once (`.beelz summons clear`). Stash / restore (waygate-safe) work the same way.
- **New `.beelz loadouts`** — at a glance, see your universal "basic" set, each per-weapon set,
  and which one is active for the weapon you're holding. The per-weapon loadout system already
  existed (`.beelz weapon-grant <weapon|auto> <slot> <index>`); this just makes it easy to see.
  A per-weapon set overrides your universal set on its slots, swapping weapons switches sets
  automatically, and unarmed / spellcasting is its own weapon "family".

## [0.44.0] - 2026-05-26

### The mod is now about COLLECTING ABILITIES — "Devour" replaces unit transformation

Beelzebub's focus is now squarely on **stealing abilities and stacking them on your bar**. The
big change to how the mod works:

- **"Devour" — the new jackpot.** Every kill still has a small chance to capture **one** of a
  unit's abilities. On a rare jackpot roll you now **Devour** the unit instead: you learn **ALL**
  of its eligible abilities at once ("⭐ DEVOURED Foulrot — learned all 6 of its abilities!").
  Slot whichever you like with `.beelz grant`. This replaces the old "unlock a transform into the
  unit" jackpot for every unit that can't actually be rendered.
- **Transformation is now Dracula & Morgana only.** Those are the only two units the game can
  actually render you as, so they remain true transformations (`.beelz transform` / `revert`,
  with their phases, summons, and detonations intact). **Becoming any other unit has been moved to
  a researched, postponed "phase two"** — it needs a future client-side companion mod to render
  the model, which the server alone cannot do.
- **Your collection is safe.** If you'd previously unlocked transforms into other units, those are
  automatically converted on first load: you simply **keep all of those units' abilities** (granted
  to your pool via Devour). Nothing you earned is lost. Dracula/Morgana transforms are preserved.
- **New admin command `.beelz admin devour <player> <unitGuid>`** — grant a player a unit's entire
  ability kit in one go (the admin alternative to the old force-transform for non-boss units).
- Drop rates retuned: per-ability capture stays ~5%, the Devour jackpot defaults to ~0.25% (both
  admin-configurable).
- **Pity now survives restarts.** Your bad-luck-protection streak (the bonus that builds on dry
  kills) is saved with the rest of your data, so a server reboot no longer resets your progress
  toward the next drop.
- **Per-ability cooldown tuning is live for on-demand casts.** Admins can set a `CooldownScale` per
  ability in the rules file; it now applies to `.beelz cast` (the expanded-hotkey action bar), so a
  powerful captured ability can be given a longer on-demand cooldown.

> **Why the change?** A server-side mod can't render your character as an arbitrary creature — the
> game decides your model on the client. Rather than ship "transformations" that didn't visually do
> anything for most units, the mod now leans into what it does brilliantly: a deep
> ability-collection game. Full creature transformation remains on the roadmap as a phase-two
> feature pending a client-side renderer.

## [0.43.23] - 2026-05-26

### Friendly names, a safer `.beelz clear`, and grouped help

- **Capture notifications now show in-game names.** When you capture an ability or unlock a
  transformation, the chat message uses the unit's and ability's friendly in-game name (e.g.
  "Kodiak the Bear") instead of the raw asset id — matching what `.beelz list` and
  `.beelz transforms` already show.
- **`.beelz clear` now asks for confirmation.** Typing `.beelz clear` shows exactly what would be
  wiped (your captures, transform unlocks, slot binds, hotkeys, presets) and points you to
  `.beelz resetbar` if you only meant to reset your action bar. To actually wipe everything you now
  type `.beelz clear CONFIRM` — no more nuking your collection with one mistyped command.
- **Grouped, discoverable help.** Every command is reachable from chat: `.beelz commands` is now a
  complete, sectioned list, and each command group has its own help —
  `.beelz admin help`, `.beelz api help`, and `.beelz hotkey help` — so you can drill into just the
  commands you care about.

## [0.43.22] - 2026-05-26

### Recovery commands in the help list + admin recovery guide

- The recovery commands now appear in `.beelz commands`, and `.beelz resetbar` is listed for
  players. Added a **"Repair a Stuck Player"** admin guide (`docs/RECOVERY_GUIDE.md`) covering
  the full toolkit and recommended flow. No gameplay changes.

## [0.43.21] - 2026-05-26

### Reset a character from the server — `.beelz admin reset-character`

- **New admin command `.beelz admin reset-character <player> CONFIRM-RESET`.** Cleanly resets a
  player's character: it unbinds their Steam ID and kicks them, so on next login they create a
  brand-new character — no second mod required. Their Beelzebub collection is preserved (it's
  tied to their Steam account), and it pairs with `copy/paste-collection` for a full
  recover-from-corruption flow. The old body remains in the world but unplayable. Confirmation
  token required since it's a character-level action.

## [0.43.20] - 2026-05-26

### Back up & transfer a player's collection — `.beelz admin copy/paste-collection`

- **New admin commands `.beelz admin copy-collection <player>` and `.beelz admin
  paste-collection <player>`.** Copy a player's captured abilities + transform unlocks to an
  admin clipboard, then paste them onto any character — a fast way to back up a collection
  before a character re-roll (or transfer it to another player). Pasting is additive and skips
  duplicates. *(Note: your collection is tied to your Steam account, not the character, so a
  normal re-roll keeps it automatically — this is for backups, transfers, and restoring after
  a `.beelz clear`.)*

## [0.43.19] - 2026-05-26

### Safe ability-slot re-sync — `.beelz admin rebuildslots`

- **`.beelz admin rebuildslots [player]` is now safe and non-destructive.** It re-syncs a
  character's active ability slots back to their stored base abilities using the game's own
  slot-setter — repairing a bar stuck on creature/shapeshift abilities by writing *values*, never
  destroying slot entities. (The previous build's version destroyed the slot entities, which could
  crash the server on the player's next login — that approach is removed.)

## [0.43.18] - 2026-05-26

### Last-resort ability-bar rebuild — `.beelz admin rebuildslots` (withdrawn)

- *(Withdrawn in 0.43.19 — the entity-destruction approach could crash the server on relog.)*

## [0.43.17] - 2026-05-26

### Recovery for an already-stuck ability bar — `.beelz admin clearslotmods`

- **New admin command `.beelz admin clearslotmods [player]`** repairs a character whose ability
  bar got frozen on a creature kit by the old transform bug. It clears the orphaned ability-slot
  modifications left behind in the game's modification system and forces the slots to rebuild
  from your real abilities — reaching the deep engine state that resets, respawns, and even
  wiping your data couldn't touch.

## [0.43.16] - 2026-05-26

### Root-cause fix: logging out while transformed no longer corrupts your bar

- **Fixed the cause of the stuck/frozen ability bar.** If you logged out (or dropped) while
  transformed, the game's ungraceful removal of the form on disconnect could leave the
  creature's abilities permanently welded to your action bar — surviving relogs, resets, even
  a respawn. Beelzebub now tears the transform down **cleanly the moment you disconnect**, so
  the game properly removes those abilities before your character is saved. Your transform is
  still kept for the reconnect grace window and re-applied when you return. New transforms are
  protected going forward; a character already stuck from the old bug is a separate recovery.

## [0.43.15] - 2026-05-26

### Definitive fix for a stuck/frozen ability bar — `.beelz admin respawn`

- **New admin command `.beelz admin respawn [player]`** rebuilds a player's character on the
  spot using the game's own respawn, giving them a fresh, clean ability bar. This is the
  reliable cure for a bar frozen on a creature kit after a shapeshift was left ungracefully
  (e.g. logging out mid-form) — the kind of stuck bar that ignores `resetbar`, weapon swaps,
  and even relogging. Inventory, equipment, blood, and progress are preserved (same as a
  normal death + respawn). `.beelz resetbar` points you here if your bar is stuck this way.
- Backed out the previous slot-clearing/reinit reset attempts that couldn't reach the
  stuck state (and produced console noise); `resetbar` keeps the binding/buff cleanup.

## [0.43.13] - 2026-05-26

### `resetbar` now force-clears a frozen ability bar

- **`.beelz resetbar` now authoritatively resets your ability slots.** A transformation
  applies its abilities at high priority; if those stuck in your bar's resolved state after
  the transform ended, your normal weapon/spell abilities (lower priority) couldn't overwrite
  them — leaving the bar frozen on creature abilities and ignoring spellbook/weapon changes.
  `resetbar` now clears the resolved slots directly through the game's own slot system and
  pushes the change to your client. If any slots look empty afterward, re-equip your weapon
  and re-slot your spells to repopulate them.
- Also clears orphaned ability-override sources (by ownership) and, for admins, `.beelz admin
  buffs` reports your entity type, equipped-ability catalog, and every override source you own.
- *(Admins)* `.beelz admin buffs` now also reports your character's entity type, persistent
  equipped-ability catalog, and **every ability-slot override source that belongs to you**
  — the full picture for diagnosing a stuck bar.

## [0.43.9] - 2026-05-26

### `resetbar` now clears a stuck creature/shapeshift bar too

- **`.beelz resetbar` is more thorough.** It now scans your *live* buffs directly and
  strips any lingering transform, creature/shapeshift, or boss-form buff that was holding
  your action bar hostage — not just the bindings it knew about. If a transform ever
  leaves you wearing a creature's abilities, this fully returns you to your vampire bar.
- *(Admins)* New diagnostic `.beelz admin buffs [player]` dumps a player's active buffs
  and which ones override ability slots to the server log — for tracking down a stuck bar.

## [0.43.8] - 2026-05-26

### Reset your bar to normal in one command

- **New: `.beelz resetbar`.** Ends any active transformation and removes **all** your
  Beelzebub slot bindings at once, snapping your action bar back to its normal vampire
  state (your spells + weapon skills). Handy if you've granted boss abilities to your
  slots and want your original bar back — previously you had to clear each slot with
  `.beelz unslot` one at a time. Your captured abilities and transform unlocks are kept;
  re-grant anything with `.beelz grant`.

## [0.43.7] - 2026-05-26

### No more getting stuck after a disconnect

- **Fixed: logging out while transformed could leave you "stuck."** You could come back
  to a frozen ability bar locked to the transformed unit — unable to revert, unable to
  grant abilities to your bar, with new transforms not taking effect. Beelzebub now
  **reconciles your state every time you log in**: any leftover transform from a previous
  session (including one stranded by a server restart) is cleared and your normal
  abilities are restored. You should never log in to a broken bar again.
- **New: reconnect grace window.** If you disconnect while transformed, your
  transformation and its summons are now **kept for a short window** (default **90s**) so
  an abrupt drop or a quick relog puts you right back where you were — same form, same
  summons. If you don't return in time, the transform reverts and the summons are
  dismissed cleanly. Configurable via `Transform_ReconnectGraceSeconds` (`0` = revert
  immediately on disconnect, `-1` = keep until you manually revert).

## [0.43.6] - 2026-05-24

### Summons keep up with you

- **Summoned allies now scale to you.** A summon used to keep the original boss-add's
  level and damage, so it fell behind as you levelled. Now a summon matches **your
  level** on spawn (`Transform_SummonMatchPlayerLevel`, on by default), with an admin
  power dial (`Transform_SummonPowerFactor`) to make summons hit/tank harder or softer.
  Applies to transform summons and your standalone signature summons alike.

## [0.43.5] - 2026-05-24

### Tune ability power

- **Granted abilities already scale with you** — when you cast a captured boss ability,
  it uses *your* Physical/Spell Power and crit, so it grows with your level, gear, and
  prestige (it isn't frozen at boss-level damage).
- **New admin scaling controls on top:** a global `Grant_PowerScalingMode`
  (`PlayerScaled` default / `Boosted`) with `Grant_PowerScalingFactor`, plus the
  **per-ability `DamageScale`** in the rules file is now actually applied — for nerfing,
  buffing, or rescuing the occasional flat-damage ability.

## [0.43.4] - 2026-05-24

### Keep a boss's summon as your own

- **Unlocking a unit's transform now also teaches you its signature summon** as a
  standalone ability. Slot it or bind it to a hotkey and summon **even when you're not
  transformed**. Applies retroactively to units you've already unlocked. Toggle with
  `Capture_GrantSignatureSummons`.

## [0.43.3] - 2026-05-24

### Call in the adds — `.beelz summon`

- **New `.beelz summon` command.** While transformed, call your unit's signature
  add-summon — the Toad King's frogs, the Werewolf Chieftain's caged wolves, and more —
  the ones bosses normally only trigger at low health. The spawns fight as your allies.
  Cooldown via `Transform_SummonCooldownSeconds`.

## [0.43.2] - 2026-05-24

### Transform stability fixes

- **Animal forms stay put.** Native shapeshift forms (frog/toad, wolf, bear, …) no
  longer flicker out the instant you cast or take a hit.
- **Revert always works.** Fixed a case where reverting reported success but left your
  spell bar unchanged.
- **Log out safely while transformed.** Disconnecting now returns you to your base form
  on next login instead of leaving you in a half-transformed state.

## [0.43.1] - 2026-05-24

### Morgana fixed + reworked (critical)

- **Fixed a server crash when transforming into Morgana.** Her serpent form could
  abort the server on transform; that's resolved.
- **Morgana is now a two-stage serpent**, like Dracula's switchable kits: a ranged
  **Spectral** kit and a melee **Serpent** kit, swapped with `.beelz phase 1/2`. (A
  humanoid first stage isn't possible — V Rising can't render her human model on a
  player — so both stages use her serpent form.)

## [0.43.0] - 2026-05-24

### First public test release 🎉

Beelzebub is going out for **server testing** — this is an early alpha/beta build.
See the mod page for the full testing disclaimer, known limitations, and roadmap.
Built on the shoulders of the V Rising modding community — special thanks to
**Bloodcraft** by zfolmt, whose ability-grant, familiar/ally, and ExoForm patterns
informed several of Beelzebub's systems.

### For companion-app developers (BloodCraftHub)

- `.beelz api transform-config` now reports the **shard-boss** category too
  (`src=S`, alongside `src=R`/`src=V`) so a client can read all three transform
  mode/duration/cooldown profiles in one call.

## [0.42.0] - 2026-05-24

### Summons handle horse-riding your way

When you're transformed with summons fighting for you and you **hop on a horse**,
admins now choose what happens via the new `Transform_MountedSummonMode` setting:

- **Stash** (default) — your summons are tucked away the moment you mount and
  brought right back when you dismount, so they don't scramble after a galloping
  horse.
- **Follow** — your summons stay in the world, leash to you on the mount, and keep
  engaging in combat as you ride.

Either way, **dismounting now restores your transformation's ability bar** instead
of leaving you on the horse's default abilities — the same fix we shipped for
reverting, bat/waygate travel, and combat shapeshifts.

## [0.41.1] - 2026-05-24

### Combat shapeshifts no longer drop your transform kit

Following up on 0.41.0: leaving a **combat shapeshift** (wolf, bear, etc.) while
transformed now **automatically** re-applies your transformation's abilities — same
as bat/waygate travel already did. `.beelz refresh` remains as a manual fallback.

## [0.41.0] - 2026-05-24

### Your ability bar no longer vanishes

Fixed the cases where your spell bar could go blank or lose abilities:

- **After reverting a transformation**, your custom-bound abilities now come back
  immediately — no more empty bar, and no need to swap a weapon to get them back.
- **After flying as a bat / leaving a wolf or other form while still transformed**,
  your transformation's abilities are re-applied on arrival instead of dropping to
  your weapon's defaults.
- **Per-weapon loadouts** (set with `.beelz weapon-grant`) keep working as you swap
  weapons and are now restored reliably alongside the above.

And a safety net for anything we missed: **`.beelz refresh`** re-applies your correct
bar on demand — whether you're transformed or not.

## [0.40.0] - 2026-05-24

### Cast abilities beyond your 6 slots — an expanded action bar

You're no longer limited to six abilities. Bind any captured ability to a named
hotkey (`.beelz hotkey set <name> <index>`) and **fire it on demand with
`.beelz cast <name>`** (or, soon, a BloodCraftHub button). Each cast respects the
ability's own cooldown, so it's not spammable. Admins control how many hotkeys a
player can have with `Hotkeys_MaxPerPlayer`, and can switch the feature off with
`Hotkeys_Enabled`.

This also answers the "I can't put an ability on my ultimate slot" problem: the
ultimate slot can't be rebound on the normal action bar, but `.beelz cast` doesn't
need a slot at all — so any captured ability (ultimates included) can be fired this
way. (Abilities whose animations are tied to a specific boss's body still read best
while transformed into that boss.)

## [0.39.0] - 2026-05-24

### Shard bosses, V-Blood filters, and admin/config plumbing

- **Shard bosses are their own category.** Dracula, Adam the Firstborn, Solarus the
  Immaculate, The Winged Horror (Talzur), Megara the Serpent Queen, and Gorecrusher the
  Behemoth (configurable via `Transform_ShardBossNames`) now have their
  own transform mode/duration/cooldown (`Transform_*_ShardBoss`) and a separate
  cooldown bucket — so you can, say, time-limit the powerful shard-boss forms while
  leaving other V-Bloods as toggles, and a shard cooldown won't block regular V-Blood transforms.
- **Filter your lists.** `.beelz transforms vblood` / `.beelz list vblood` (also
  `shard` / `regular`) show a shorter, focused list. The transforms list also now
  splits shard bosses into their own section.

### Admin & companion-app plumbing
- `.beelz admin set <key> <value>` — change any setting live (persists to the
  config), instead of editing the file + restarting.
- New BCH-facing reads: `.beelz api config` (all settings), `.beelz api cooldowns`
  (per-category cooldown timers), a `shard` flag on transform/catalog data, and a
  `config-changed` event. (API version 3.)

## [0.38.0] - 2026-05-23

### Bad-luck protection, Morgana's second form, and friendlier names

- **Bad-luck protection (pity):** every kill that drops nothing now nudges your
  odds up a little (default +0.25% per kill, admin-configurable), resetting to
  baseline the moment you get a drop — so a cold streak can't last forever. Ability
  and transform luck are tracked separately, per source (regular vs V-Blood).
- **Morgana now has two switchable forms** like Dracula: a melee "Serpent" kit and
  a ranged "Spectral" kit — swap with `.beelz phase 1/2`. (The Spectral kit is new
  and experimental; some of her scripted spells may need tuning.)
- **Friendlier names everywhere:** `.beelz search`, the "forgot" messages, and the
  admin grant/revoke/transform replies now show proper in-game names instead of
  raw asset names (search still matches either).

### Behind the scenes
- The manual-detonation registry now documents the boss-nova expansion candidates
  to verify (Dracula CrimsonNova, Cardinal LightNova, Gloomrot ImplodingOrb, …).
- Added `docs/CHAIN_AUDIT.md` — the chained-ability test plan for a tracing session.

## [0.37.0] - 2026-05-23

### Bestiary — your collection book

A new way to see your collection at a glance. **`.beelz bestiary`** lists every
unit you've collected from, showing how many of its abilities you've captured
(e.g. 3/7), whether you've unlocked its transform, and whether it's a V-Blood —
with a "complete" marker once you've collected everything from it.

**`.beelz bestiary unit <name>`** drills into a single unit and lists each of its
abilities with a ✓ (collected) or · (still to find), so you know exactly what's
left to hunt. The companion app can read the same data via `.beelz api bestiary`.

## [0.36.0] - 2026-05-23

### Fire boss AoEs on demand: `.beelz detonate`

Some boss abilities only unleash their big area-of-effect blast under a specific
condition — the Undead Priest, for example, only sets off its projectile Nova at
the end of a teleport. Now, while transformed into a unit that has one of these
signature blasts, you can trigger it yourself any time with **`.beelz detonate`**.

It fires the real AoE at your position, on your team, so it actually hits enemies.
There's a short cooldown (admin-configurable via `Transform_ManualDetonateCooldownSeconds`,
default 5s) to keep it from being spammed. Right now the Undead Priest's Nova is
wired up; more bosses can be added over time. (A natural fit for a hotkey button
in a future companion app.)

## [0.35.0] - 2026-05-23

### Richer ability info + a more complete companion-app feed

Groundwork for the BloodCraftHub companion app — mostly invisible in normal play:

- `.beelz` ability info now reports the real ability description, its magic
  school, base cooldown, and which weapon its animation belongs to.
- The companion-app event feed is now complete: it also fires when you forget an
  ability, forget a transform, or wipe your collection, and it now reliably
  reports *every* way a transform ends (manual revert, switching forms, an admin
  ending it, or a server-wide revert) — so the app's display never goes stale.

No gameplay changes; existing setups are unaffected.

## [0.34.0] - 2026-05-23

### Wield-the-right-weapon guidance for ability animations

Weapon-based abilities (sword slams, axe swings, spear lunges, and so on) carry
their swing animation baked in — they only *look* right when you're holding that
kind of weapon. Beelzebub now tells you which one:

- Granting a weapon-based ability shows **"✋ Wield <weapon> for the correct
  animation."**
- The transform loadout list tags each weapon-based slot with its weapon (✋), so
  you can pick a matching weapon for the unit you're becoming.

Spells and universal abilities are never tagged — they don't need a weapon.

New admin toggle `Grant_EnforceWeaponMatch` (default **off**): turn it on to make
`.beelz grant` *require* you to bind weapon-based abilities through
`.beelz weapon-grant <weapon>` (which ties them to the right weapon's loadout
automatically), instead of just advising it.

## [0.33.0] - 2026-05-23

### Real shapeshift transforms for animal-form units

Transforming into a unit that matches one of V Rising's real creature forms —
wolves, bears, rats, spiders, toads, werewolves, and the Tailor's gargoyle —
now puts you in that **actual form** (model, rig, and all) and **keeps you
there while you fight**, using the same persistent-form tech that powers the
Dracula and Morgana transforms. Previously these only flickered a cosmetic form
that dropped the instant you cast anything; now the form sticks and your
captured abilities play on it.

Units that don't match a real V Rising form (most humanoids, undead, constructs)
still transform the same as before — spell bar and stats, no model swap — because
the game simply doesn't ship a form for them (a hard engine limit; the only way
to render arbitrary units is a future client-side companion mod).

New admin toggle `Transform_RealFormWhenAvailable` (default **on**) controls this
server-wide; the curated boss forms (Dracula/Morgana) always use their form
regardless. Per-unit control still lives in the transform rules.

## [0.25.0] - 2026-05-23

### Mist Walk AoE now fires — plus a chain diagnostic for testers

The Undead Priest's Mist Walk now detonates its area-of-effect blast at your
teleport destination, the way the boss does. (The game was suppressing the
blast for players because of an internal condition meant for the NPC; we now
trigger it ourselves on arrival, on your team, so it actually hits enemies.)

This also adds a behind-the-scenes **chain tracer** (only active with verbose
logging). When you cast an ability while transformed, it records what that
ability's chain actually produces — so when a boss ability only half-works, we
can pinpoint exactly where it breaks and fix it. Harmless and silent unless you
turn verbose logging on. Great for group testing sessions.

## [0.24.6] - 2026-05-23

### Mistwalk AOE chain — third hook for the proxy spawn

Looked at the full Mistwalk chain in the prefab data and found the
specific missing piece. The chain is:

```
Cast → Travel Phase → Travel End (buff) → ProxySpawner → 12 projectiles
```

The Travel End buff has a `SpawnPrefabOnGameplayEvent` pointing to the
SAME ProxySpawner that the standalone Projectile Nova uses. Our existing
hooks (`ScriptSpawnServer`, `BuffSystem_Spawn_Server`) catch the buff —
but they DON'T catch the ProxySpawner, because it's not a buff and is
spawned by a different V Rising system.

v0.24.6 adds a broader sweep alongside our existing hook that catches
non-buff chain entities. It runs every other tick during combat,
queries entities with EntityOwner + Team (excluding players, summons,
and buffs), and applies the same team/faction fixup if the owner chain
reaches a Beelzebub-transformed player. Already-processed entities are
remembered so the sweep doesn't redo work.

### Test plan

1. Restart server, transform → Undead Priest
2. Use Mistwalk (Space) at an enemy group — expected:
   - You teleport to the location
   - The damage burst now visible/effective at destination (AOE finally fires)
3. Use the C-key ultimate Nova (this already worked) — still should work
4. With verbose logging, look for `[Beelz CHAIN] fixup` lines — should
   show ProxySpawner-class entities being processed by the sweep now,
   not just buffs

### If it works

Other AOE/chained boss abilities should also start working — this hook
isn't Priest-specific, it catches any chain entity owned by a
transformed player.

### If it still doesn't fire

The chain may go even deeper than ProxySpawner, OR the team-rewrite is
happening AFTER the projectiles have already inherited the original NPC
team. In that case we'd need to also hook the projectile entities
themselves, OR pre-rewrite the prefab template. Report what you see and
we'll iterate.

## [0.24.5] - 2026-05-23

### Slot mapping now matches V Rising's real keybinds

v0.24.4 used the wrong slot indices — my mental model didn't match the
actual game. User testing exposed the correct layout:

| Slot | Key | Conventional use |
|---|---|---|
| 1 | Q | Primary weapon attack |
| 2 | Space | Travel / teleport |
| 3 | Shift | Usually empty (BCH may override) |
| 4 | E | Secondary weapon attack |
| 5 | R | First spell slot |
| 6 | C | Second spell slot |

The heuristic now places abilities accordingly:

- `_Projectile_Group`, `_MeleeAttack_`, `_Auto_` → **Slot 1 (Q)**
- `_Teleport_`, `_Travel_`, `_Dash_`, `_Leap_`, `_PhaseShift_` → **Slot 2 (Space)**
- `_Charge_`, `_Smash_`, `_Slam_`, `_Heavy_`, `_Cleave_`, `_Strike_` → **Slot 4 (E)**
- `_Hard_`, `_Ultimate_` → **Slot 6 (C)**
- everything else → fills slots 3, 5 (and whatever the heuristic missed) in order

For Undead Priest, transforming now gives you:
- **Q**: Projectile (basic attack)
- **Space**: Teleport Travel (Mistwalk) — where it belongs
- **Shift**: spell fill
- **E**: spell fill
- **R**: spell fill
- **C**: Projectile Nova Hard (the ultimate-equivalent)

### Admin slot template (per-V-Blood override)

Every `TransformMap` entry in `ability_rules.json` now supports a
`SlotTemplate` field. If set, the admin's curated assignment wins over
the heuristic. Example:

```json
{
  "CHAR_Undead_Priest_VBlood": {
    "Enabled": true,
    "Difficulty": "Basic",
    "Tier": 4,
    "SlotTemplate": {
      "1": "AB_Undead_Priest_Elite_Projectile_Group",
      "2": "AB_Undead_Priest_Elite_Teleport_Travel_AbilityGroup",
      "3": "AB_Undead_Priest_Elite_RaiseDead_AbilityGroup",
      "4": "AB_Undead_Priest_Elite_ProjectileNova_AbilityGroup",
      "5": "AB_Undead_Priest_Elite_RaiseHorde_AbilityGroup",
      "6": "AB_Undead_Priest_Elite_ProjectileNova_Hard_AbilityGroup"
    }
  }
}
```

Partial templates work — unspecified slots get filled by the heuristic
from the remaining abilities. Admins can curate the exact loadout per
V-Blood for their server.

### Ultimate slot research deferred

You tried slots 0, 7, 8 — all rejected as invalid via `.beelz grant`.
V Rising's ultimate slot (the R-key shard ultimate in vanilla) appears
to use a different mechanism than `ReplaceAbilityOnSlotBuff`. Will
research and add support in a follow-up release. For now, the boss's
Hard-tier ultimate lands on slot 6 (C key) as the highest available
slot.

### Phase-based abilities

You mentioned bosses with abilities that depend on combat phase. This
is already supported in the codebase (TX5 phase system) — see
`.beelz phase <n>` to switch loadouts mid-transform. Combined with the
new admin SlotTemplate, you can curate distinct loadouts per phase.

## [0.24.4] - 2026-05-23

### Smart slot mapping + broader chain fixup

Two issues from v0.24.3 testing:

**1. Travel + Ultimate land on wrong keys.** Boss's natural ability order
was applied 1:1 to your spell bar — the priest's teleport ended up on
the Spell slot instead of Shift, and the Hard-tier ultimate AOE landed
on a Spell key instead of the Ultimate position. Fixed by classifying
abilities by their name pattern:

- `_Projectile_Group`, `_MeleeAttack_`, `_Auto_` → **Primary** (slot 1)
- `_Teleport_`, `_Travel_`, `_Dash_`, `_Leap_`, `_PhaseShift_` → **Travel/Shift** (slot 4)
- `_Hard_`, `_Ultimate_` → **Ultimate-equivalent** (slot 6)
- everything else fills slots 2, 3, 5 in original order

For Undead Priest, this means:
- Slot 1 (Primary): Projectile (basic)
- Slot 2/3/5 (Spells): Projectile Nova, Raise Dead, Raise Horde (in natural order)
- **Slot 4 (Shift): Teleport Travel (Mistwalk)** — finally on the right key
- **Slot 6 (Ultimate): Projectile Nova Hard** — on the ultimate position

**2. Mistwalk AOE chain didn't fire.** The Travel ability spawns a Phase
entity (during travel) then a Travel_End buff at the destination. The
Travel_End buff was caught by our v0.24.3 engine hook, but maybe at the
wrong stage of V Rising's spawn pipeline. v0.24.4 adds a second hook
on `BuffSystem_Spawn_Server.OnUpdate` that does the same chain-fixup —
two hooks cover the spawn lifecycle at different points. Should mean
more chain entities get their Team/Faction rewritten correctly.

### Test plan

1. Restart server (DLL deployed)
2. Transform → Undead Priest
3. **Check spell bar layout** — chat should show:
   ```
   Primary: Projectile  
   Spell slots: ProjectileNova, RaiseDead, RaiseHorde
   Travel/Shift: Teleport Travel (Mistwalk)
   Ultimate: ProjectileNova Hard
   ```
4. Cast Mistwalk (Shift) — expected: teleport + AOE fires at destination
   (the freeze/damage burst the user reported missing)
5. Cast the Ultimate (R or whatever key your client uses for ult) —
   expected: 360° Nova fires (already worked in v0.24.3)
6. Cast all your spells — expected: all functional

### Diagnostic

With verbose logging on, look for `[Beelz CHAIN][buff-spawn]` log lines
— shows the new BuffSpawnServer fixup firing. Complements the existing
`[Beelz CHAIN] fixup` from ScriptSpawnServerPatch.

## [0.24.3] - 2026-05-23

### Attempt to fix AOE/projectile abilities (engine hook)

You pushed back on warning-as-fix for the Priest Projectile Nova. Fair —
Bloodcraft makes familiars cast NPC abilities successfully, so the
technique exists. This release adds an engine-level hook that attempts
to apply the same fix to player-cast abilities.

### What changed

V Rising's NPC ability chains carry hard-coded NPC team info on every
spawned entity (ProxySpawner, projectiles, etc.). When a transformed
player casts a boss ability, the chain spawns entities stamped with
`Team=1` (NPC) and `TeamReference` pointing to NPC team singleton.
Collision/hit filters then say "skip my own team" — and miss the actual
enemies (who are also Team=1).

The new patch (`ScriptSpawnServerPatch`) watches every spawned entity
during the chain. For entities whose owner traces back to a Beelzebub-
transformed player, it rewrites:
- `Team` to match the player's team
- `TeamReference` to match the player's team reference  
- `FactionReference` to `Faction_Players`
- `EntityOwner` to point at the player directly

Same component setup Bloodcraft uses for combat familiars
(FamiliarBindingSystem.cs:413-415) — applied to chain entities instead
of the familiar itself.

### What this should fix

- **Priest Projectile Nova (basic + Hard)** — the 360° AOE that fires
  12 projectiles. Previously projectiles landed but did no damage.
- **Bishop TrippleBolt and similar chained projectiles** — owner-chain
  loss class.
- **Any other AOE/projectile boss ability** with the same chain pattern.

The `⚠ INCOMPATIBLE` warning was removed from these (the fix is being
attempted). Try and report whether they now hit enemies.

### What this will NOT fix

The 5 abilities still tagged Incompatible are bound to NPC animation
rigs — the BOSS skeleton has bones named `hand_R` etc. that the chain
references; the player skeleton doesn't have those bones. The animation
doesn't play; no hook can re-rig the player's model. These remain tagged:

- Bishop Eternal Darkness
- Bishop Shadow Step  
- Priest Teleport Travel (and other `_Teleport_Travel_` variants)

### Test plan

1. Restart server.
2. Transform → Undead Priest.
3. Cast Projectile Nova. Expected: projectiles fire AND hit nearby
   enemies (not players, not your summons).
4. Same for Projectile Nova (Hard) on the Ultimate slot.
5. Try Bishop's TrippleBolt if you have that transform.
6. If you see `[Beelz CHAIN] fixup` in `LogOutput.log` (with verbose
   logging on), the hook is firing.

### If it doesn't work

Engine-hook approaches are experimental — V Rising's IL2CPP entity
pipeline has many edge cases. If the hook doesn't catch the spawned
proxies (because they go through a different system than ScriptSpawn
Server), we may need to add additional hooks. Report what you observe
and we'll iterate.

## [0.24.2] - 2026-05-23

### Priest fixes + known-broken-ability warnings

**1. Priest RaiseDead now works (#93).** Previously: casting RaiseDead
on a transformed player produced nothing. Reason: V Rising's RaiseDead
chain animates EXISTING corpses via an area-of-effect detector. When
the player casts it, there are typically no corpses in range → silence.
Fix: added explicit summon-target entries so RaiseDead manually spawns
2 skeleton soldiers at the player's position, same way Bishop's
ShadowSoldier and Stonebreaker's Reinforcement work.

**2. "⚠ INCOMPATIBLE" warnings on broken abilities.** Some V-Blood
abilities can't fire correctly when cast by a player — most commonly
because they're bound to NPC-specific animation rigs (Bishop's Shadow
Step, Eternal Darkness; Priest's Teleport Travel) or because they use
AOE proxy-spawner patterns that fire projectiles on the NPC team rather
than the player's team (Priest's Projectile Nova). 

These now show a warning when you look them up or transform into a unit
with them:

```
Priest Of Shadows Projectile Nova [Other / ⚠ INCOMPATIBLE]
⚠ This ability is known to misbehave when cast by a player
  (AOE-Proxy: fires 360° projectile nova but team check makes them miss enemies).
```

Initial curation tags 8 known-broken abilities. Admins can flag more
(or unflag) via `ability_metadata_overrides.json` using the new
`incompatible: true` + `incompatibleReason: "..."` fields.

**3. Audit support.** `.beelz admin scan-abilities` now reports an
`incompatible` count and includes the reason in the discovered-
abilities.json output, so admins can see at a glance which abilities
need engine-side fixes vs admin curation.

### Why these abilities are broken

These are V Rising engine-level limitations:
- **AnimRig** — boss dash/phase abilities reference specific bones on
  the NPC skeleton (`hand_R`, etc.). Player skeleton doesn't have them.
- **AOE-Proxy** — ProxySpawner entities inherit `Team = 1` (NPCs),
  causing fan-projectiles to miss player-allied targets.
- **OwnerChain** — multi-step chains lose the owner reference on
  intermediate spawn entities.

A future engine-level Harmony hook (planned for v0.25.x) can resolve
AOE-Proxy and OwnerChain cases. AnimRig is likely permanent — the
client doesn't render those animations on the player skeleton anyway.

## [0.24.1] - 2026-05-23

### Ability info surfaces in-game now

v0.24.0 shipped a 1,813-entry ability database but the data was only
reachable via `.beelz info <name>` — the action bar tooltip stayed
blank for V-Blood / NPC abilities. That's a hard V Rising client-side
limitation (server mods can't modify what the client renders in
tooltips). v0.24.1 routes around it with two chat-based access paths:

**1. Auto-chat on transform.** When you transform into a V-Blood, the
mod immediately sends a multi-line summary of your new spell bar:

```
--- Bishop Of Shadows spell bar ---
  Primary: Bishop Primary Attack · cd 0.0s
  Q-Weapon: Bishop Shadow Step [Other] · cd 8.0s
  Spell 1: Bishop Shadow Soldier [Other] · cd 30.0s
  Spell 2: Bishop Eternal Darkness [Other] · cd 45.0s
  Ultimate: Bishop Shadow Dance [Other] · cd 90.0s
Use .beelz active to re-display this anytime. .beelz info <name> for full details.
```

Descriptions show up on the next line for abilities that have them (the
~238 player-castable spells from gaming.tools). For NPC-only abilities
(most V-Blood kits), you get name + school + cooldown — enough to know
what's on each slot at a glance.

**2. `.beelz active` command.** Re-displays your current spell bar with
the same info, anytime. Works whether you're transformed (shows the
transform's loadout) or not (shows your universal slot bindings).

### Why this matters

V Rising renders action bar tooltips entirely client-side from the
client's local prefab data + StreamingAssets localization. Stunlock
shipped localized text for player-castable spells but mostly not for
NPC/V-Blood abilities. The server can send the player a chat message —
it can't repaint a UI element on their screen. So chat-based access is
the real fix server-side.

A proper in-tooltip rendering would require a client-side companion
mod (BloodCraftHub integration, planned long-term).

## [0.24.0] - 2026-05-23

### Ability info finally works

Until now, the only display for a captured ability was the raw prefab
name — e.g., `AB_Undead_Priest_Elite_RaiseHorde_AbilityGroup`. This
release adds a comprehensive ability database with proper titles,
descriptions, schools, types, source NPCs, cooldowns, cast times, and
ranges.

### New: `.beelz info <index|name>`

Players can now look up any ability:

```
.beelz info 0          (by your .beelz list index)
.beelz info raise hor  (by name substring)
.beelz info bloodrite  (any ability, even ones you haven't captured)
```

Returns the full report:
- Title + school/type tags
- Description with damage and duration values filled in
- Cooldown, cast time, range, behavior (Instant / Channeling / etc.)
- Source NPCs (who drops this ability)
- Provenance (shipped / admin-override / fallback)

### Architecture

Two separate databases keep concerns clean:

- `ability_metadata.json` (embedded with the mod) — **ability reference
  info**: name, description, school, type, source NPCs, icon. Sourced
  from a one-time scrape of vrising.gaming.tools/abilities + processed
  into the mod's schema. Never makes HTTP calls at runtime.

- `ability_rules.json` (per-server) — **admin policy**: Enabled/Disabled,
  Transform-Only flags, DamageScale overrides, drop-rate overrides.
  Unchanged from prior releases.

- `ability_metadata_overrides.json` (per-server, optional) — **admin
  metadata overrides**. If you want to customize a description or
  re-name an ability for your server, drop entries here. Wins over
  the shipped data.

### New admin tool: `.beelz admin scan-abilities`

Walks every ability the mod knows about (captures across all players +
every `AB_*_AbilityGroup` in the game), resolves metadata + runtime
stats for each, and writes a full audit report to `config/kdpen.Beelzebub/
discovered_abilities.json`. Use it to find abilities lacking curated
descriptions on your server.

### Why some abilities still say "no description"

The initial scrape covers V Rising's main 1,439 ability database from
vrising.gaming.tools. Some prefabs (especially internal cast/buff
entities and modded additions) won't have curated descriptions. The
in-chat output gracefully falls back to a humanized prefab name + the
ECS-derived stats (cooldown, cast time, range, behavior).

If a description is missing for an ability you care about, you can
add it via `ability_metadata_overrides.json` — same schema as the
shipped file.

## [0.23.19] - 2026-05-23

### Horde unison — the whole horde now engages together

v0.23.18 fixed defensive aggro (horde reacts when one member is hit),
but two issues remained:

1. **Player attacking didn't trigger horde response.** The horde just
   stood there while the player fought.
2. **Per-group reaction on damage.** Only the cluster near the attacked
   unit engaged — the rest of the horde stayed put.

Both came from the same root cause: V Rising's combat AI needs more than
just "an enemy in your aggro buffer" to make a unit engage. It also needs:
* The unit's combat-anchor position (used for "am I in my zone?" checks)
* The unit to be physically close enough to reach the target
* The unit to be in combat mode (not leash mode)

We only had the first two pieces partially set. Now any combat trigger —
player entering combat, player dealing damage, or any summon taking
damage — runs a full **horde re-prime**:

* Combat anchor updated to current player position
* Every summon teleported back to player if beyond leash radius
* Every summon's aggro buffer re-seeded from your current targets
* Every summon set to combat mode + Combat behavior state

Throttled to once per second per player so the re-prime doesn't fire
expensive mutations every damage tick.

### Net effect

Player initiates combat → whole horde pulled in and engages immediately.
One summon gets hit somewhere → whole horde pulled to that fight.
Walking away mid-combat → leash pulls them back the next time combat
triggers.

### Configurable

`Transform_SummonLeashRadius` (default 30) controls how close the leash
keeps the horde to you. Lower = tighter formation, more aggressive
pull-ins. Higher = more freedom to wander.

### Test plan

1. Transform → Undead Priest. Cast Raise Horde 1-3 times.
2. Walk to an enemy group. Player attacks first.
   **Expected**: whole horde engages.
3. Move during combat. Some summons follow, others lag behind.
   **Expected**: next combat trigger pulls all of them in.
4. Test with multiple cast groups (3 hordes worth of skeletons).
   **Expected**: all clusters engage together when any combat trigger fires.

## [0.23.18] - 2026-05-23

### Horde combat AI — final fix

The 3-cast summon cap is verified working. This release fixes the
remaining issue: hordes engaging briefly then disengaging during combat.

**Root cause**: V Rising's combat AI does NOT auto-inject attackers into
the AggroBuffer of player-allied units (it reserves that auto-injection
for free NPCs only). When your horde took damage, nothing pushed the
attacker into their target list — so they reacted for one tick from
residual aggro, then went idle.

**Fix**: a new patch that listens for any damage event server-wide and,
if the damaged entity is one of your summons, broadcasts the attacker
into the EVERY summon's aggro buffer. Hit one priest skeleton, the whole
horde swarms the attacker. Plus an explicit combat-mode flip on the
struck unit so leash-mode summons immediately become combat-ready.

Combined with the existing patches (player→enemy damage broadcast,
PvE-combat-buff mode toggle, Return/Idle interception), the horde now
maintains continuous engagement through extended fights.

### Test plan

1. Transform → Undead Priest. Cast Raise Horde once.
2. Walk near an enemy group. Confirm horde engages when player attacks.
3. Let one skeleton get hit by an enemy. **Expected**: the whole horde
   immediately turns on the attacker.
4. Walk away mid-combat, let enemies follow. Confirm horde stays engaged.
5. Test with other summon abilities — Bishop ShadowSoldiers, Stonebreaker
   Reinforcements — same engagement behavior.

## [0.23.17] - 2026-05-22

### Two fixes for v0.23.16 regressions

**1. 3rd summon cast no longer silently fails.** v0.23.16 fixed the cap
not enforcing — but introduced a subtle bug where the 3rd cast (cap=3)
got admitted at cast-start, then had its own spawns destroyed before they
could appear in-world. From the player's view: 1st and 2nd casts produced
horde; 3rd produced nothing, no error message; 4th was never reached.
Fixed: the cap accounting now uses different semantics at "cast start"
(empty groups count, so rapid re-casts get refused) vs at "spawn arrival"
(only populated groups count, so the destination group isn't double-
counted against its own spawns).

**2. Restored horde engages enemies again.** After waygate teleport,
restored summons stayed in their pre-stash state (typically idle,
since you stashed them at a waygate = out of combat) and never engaged
enemies at the destination. Fixed: restore now explicitly resets each
unit's combat state (BehaviourTreeState = Follow, mode = leash,
Aggroable + AggroConsumer re-enabled) and re-seeds aggro from your
current targets — so any combat already in progress at arrival
immediately propagates to every restored summon.

### Test plan

1. Transform → Undead Priest.
2. Cast Raise Horde 4 times rapidly.
3. Casts 1-3 spawn horde; cast 4 refused with chat message.
4. Engage enemies pre-teleport: confirm all horde members engage.
5. Waygate to a new region. Engage enemies. Confirm same engagement.

## [0.23.16] - 2026-05-22

### Two priest-related fixes

**1. Summon use limit actually works again.** Casting Undead Priest's
Raise Horde / Raise Dead while transformed used to let you summon
endlessly, ignoring the 3-cast cap. The priest channels for ~2 seconds
before V Rising spawns the skeletons, which fell outside our 3-second
attribution window — so the cap thought no summons existed and let you
keep casting. Fixed by extending the window to 10 seconds and adding
empty-cast-group tracking so a freshly-opened group counts against the
cap even before any skeletons appear. Three Raise Horde casts now hits
3/3 uses correctly; the 4th is refused until existing summons die.

**2. Horde combat AI no longer inconsistent.** Some Priest summons
engaged when struck, others stood idle even while being hit. Two missing
pieces compared to Bloodcraft's familiar pattern:

- Some V Rising unit prefabs (notably graveyard-spawned skeletons) ship
  with `Aggroable` disabled — they literally cannot acquire targets.
  Setup now enables `Aggroable` + `AggroConsumer` on every summon so the
  unit can participate in combat at all.
- When a unit transitioned to `Return` (gave up, walking home) or
  `Idle` (no target), it got stuck. A new patch intercepts those state
  changes for tracked summons and force-flips them back to `Follow` —
  same trick Bloodcraft uses for stranded familiars.

Result: every Priest minion in a horde now engages whoever attacks the
player, instead of a random subset of them.

### For admins

Enable `VerboseLogging = true` in `BepInEx/config/kdpen.Beelzebub.cfg`
to log the full summon lifecycle to `LogOutput.log` — useful when
reporting summon-related bugs.

## [0.23.0] - 2026-05-22

### Summons that actually fight + stack limits + teleport-follow

Five fixes addressing v0.22.1 test feedback ("summons spawned but stood
around, kept casting forever, didn't despawn on revert, couldn't waygate").

**1. Combat AI** — summons now actively engage the player's enemies instead
of standing around. Added the missing components Bloodcraft uses on its
familiars (BlockFeedBuff marker; zeroed AlertModifiers / AggroModifiers /
GainAggroByVicinity / GainAlertByVicinity to disable broken proximity
detection); plus a periodic aggro-buffer injection that pushes the player's
active enemies into each summon's combat queue ~3x/sec. The proximity zero
+ manual injection pattern is exactly how Bloodcraft's familiars fight.

**2. Stack-cap per ability** — new `Transform_MaxStacksPerSummonAbility`
setting (default 3). After 3 casts of, say, Undead Priest's Raise Horde,
the 4th cast is refused with a chat message: "Summon limit reached (3/3)
for Raise Horde. Wait for existing summons to die." For natural-chain
summons (where V Rising's pipeline produces the entities, not us), over-cap
spawns are intercepted at LinkMinion and queued for staged destroy. Counter
decays as minions die (lazy filter on each check).

**3. Staged despawn** — the v0.22.0 crash was caused by destroying 36
entities in a single frame. v0.22.1 capped tracking at 10 which masked the
crash but caused leaks. v0.23.0 tracks ALL summons (no cap) and destroys
them in a per-frame budget (`Transform_DespawnBudgetPerFrame`, default 5).
Even with 100 summons queued, the drain spreads safely across ~20 frames.

**4. Teleport-follow** — added `PlayerTeleportSystemPatch` postfix that
detects waygate / bat-form / admin teleports and repositions every live
summon to the player's new position with a radial scatter. No more leaving
your horde stranded at the spawn waygate when you bat-fly to a fight.

**5. New command `.beelz summons [off|on|status]`** — toggle summons
on/off, or query stack status. `.beelz summons off` queues all active
minions for staged despawn and blocks future casts; `on` re-enables;
no-arg shows live count per ability vs cap.

### Bug fixes

- **`.beelz transforms` crash with many unlocks** — at 13+ unlocks the
  command output exceeded VCF's 510-byte FixedString512Bytes cap and
  threw `Truncation while copying`. Per-line emit now (same pattern as
  `.beelz list` paginated in v0.21.0).

### Known gaps

- Natural-chain summons that don't match our pattern names (e.g. Undead
  Priest variants other than `_RaiseHorde_`) still need investigation —
  watch for `[Beelz SUMMON] ... no manual-spawn entry` log lines and
  paste them back if a known boss summon isn't firing.
- Visual ability animation mapping (Task #84) still deferred — was waiting
  on summon stability across multiple V-Bloods.

## [0.22.1] - 2026-05-22

### Stonebreaker summon target + crash fix

Three fixes from v0.22.0 test logs:

**1. Stonebreaker reinforcements now spawn** — added `CHAR_Bandit_Worker_Miner`
as the target. Best guess for what an Errol-the-Stonebreaker summon should be;
report back if the actual boss summons something different and we'll swap.

**2. Server crash on revert fixed** — your last test ended with the log
`despawned 36/36 summoned minion(s)` right before the server crashed.
V Rising can't reliably destroy that many entities in a single frame. We now
cap tracked minions at 10 per transform — extra summons still spawn allied,
but they're not tracked for cleanup (they despawn naturally on player death
via V Rising's existing minion lifecycle).

**3. Version string fix** — your v0.22.0 log showed `Plugin kdpen.Beelzebub
v0.21.1` even though the new code was loaded. Stale cached metadata.
Clean rebuild this round actually regenerated it; should now correctly
report `Plugin kdpen.Beelzebub v0.22.1` on next server start.

### Notes from the test logs

Undead Priest abilities (regular AND V-Blood) work via V Rising's natural
spawn chain — confirmed in your log. Our cast-start hook does NOT fire for
those because their names use `_RaiseDead_` / `_RaiseHorde_` not `_Summon_` /
`_Reinforcement_`. So we don't intercept; V Rising handles, and our LinkMinion
patch just rebinds the owner as a safety net. The Priest is a working
reference case — if Stonebreaker's manual spawn works after v0.22.1, we have
both architectures (natural chain + manual intercept) covered.

## [0.22.0] - 2026-05-22

### Summons actually fire now (manual spawn architecture)

The "cast plays sound, no unit appears" problem from v0.20.0 / v0.21.x is
fixed via a new architecture: instead of patching V Rising's spawn chain
(which dies before reaching us for player-cast summons), we now hook the
cast-start system and **spawn the unit manually** at the moment a
transformed player initiates the cast. Same player-ally setup as before
(faction, follower, owner) plus auto-despawn on `.beelz revert`.

**Initial curated targets**: only Bishop of Shadows' Shadow Soldiers are
in the spawn-target map confirmed. Every other V-Blood's summon will log
a `[Beelz SUMMON] ... Spawn target unknown` warning the first time it's
cast — paste those lines back and I'll add them to the map.

**Bonus**: two new shapeshift forms added to the visual roster — Werewolf
(for werewolf-themed V-Bloods) and Tailor Gargoyle (specifically for the
Tailor's phase-2 form). Still no visual swap possible for humanoid
bandits, undead, or constructs — V Rising's shapeshift system only
supports its own shipped model roster (~10 forms). Same limit that means
EXO form works but Stonebreaker doesn't.

**Visual ability animation mapping** (your earlier ask about giving
captured abilities a visual feedback) is filed for the next release —
the summon work was the higher priority unblock.

To pick up v0.22.0: stop the server, restart, then in chat enable
`VerboseLogging` and cast a summon. The logs will tell us which entries
need to be added to the spawn-target map.

## [0.21.1] - 2026-05-22

### Diagnose summons + clearer transform message

**For debugging summons-don't-spawn**: enable
`VerboseLogging = true` in `BepInEx/config/kdpen.Beelzebub.cfg`, transform,
cast the summon, and check `LogOutput.log` for `[Beelz DIAG]` lines.
They'll dump every entity V Rising spawns during the cast, with owner
info, so we can pinpoint where the spawn chain stops. Paste the lines
back if summons still don't appear and we'll target the specific
failure point.

**Clearer transform message**: when you transform into a unit V Rising
can't visually morph you into (humanoid bandits, undead, golems —
basically everything except wolves/bears/rats/spiders/toads), the
success message now explicitly says so:

> (No visual model swap — V Rising only provides Wolf/Bear/Rat/Spider/Toad
> shapeshift forms; this unit doesn't map. Spell bar + stats only.)

So you know upfront that the spell bar is the only thing changing.

Filed for future research (task #84): mapping captured abilities to
player-character animations so the cast has a visual, or temporarily
applying a shapeshift form just for the cast duration. Both are
non-trivial and deferred until summon-as-ally is confirmed working.

## [0.21.0] - 2026-05-22

### `.beelz list` is now paginated — and there's a new `.beelz search`

Two long-overdue UX fixes for anyone with a lot of captures.

**`.beelz list [page]`** — paginated, 15 captures per page. Page 1 also
shows your slot bindings + currently-equipped weapon. Footer tells you how
to go to the next page. The index next to each capture is preserved
across pages so `.beelz grant <slot> <index>` still works with any
number you see.

**`.beelz search <term>`** — case-insensitive substring match against
ability and unit names. Up to 25 results. Examples:

```
.beelz search bolt       → every ability with "bolt" in name
.beelz search bishop     → everything captured from any Bishop
.beelz search frostmaw   → all Frostmaw drops
```

Each match shows its original index so you can immediately
`.beelz grant <slot> <index>` from a search result.

No data changes — just chat-output ergonomics for long collections.

## [0.20.1] - 2026-05-22

### Curated matrix installed + audit-driven polish

Six audit findings actioned (one verified false-positive and dismissed).

**For players** — two new commands:

- **`.beelz catalog`** [page] — every curated V-Blood transformation with
  `[X]` for ones you've unlocked, `[ ]` for ones still to hunt. Sorted by
  tier (early game first). Paginated 10 per page. Use this to plan what
  to fight next.

**Behavior change**: gate-boss kills (the easier castle-gate variants) no
longer grant separate transform unlocks. Defeat the main V-Blood to unlock
that transformation. Ability captures from gate-boss kills still work
normally — useful for previewing a boss's kit before the main fight.

**For admins** — eight new commands:

Remote slot binding:
- `.beelz admin set-slot <player> <slot> <abilityGuid>` — bind a player's
  universal slot remotely.
- `.beelz admin set-weapon-slot <player> <weapon> <slot> <abilityGuid>`
  — bind a player's weapon-specific slot.
- `.beelz admin clear-slot <player> <slot>` — clear universal slot.
- `.beelz admin clear-weapon-slot <player> <weapon> <slot>` — clear
  weapon-specific slot.

Bulk operations:
- `.beelz admin revert-all` — force-end every active transformation on
  the server.
- `.beelz admin freeze-captures <on|off|status>` — runtime toggle for
  the master capture switch (no config reload needed).
- `.beelz admin snapshot` — high-level state dump (player count, active
  transforms, curated entry counts, server mode, build).
- `.beelz admin wipe-all CONFIRM-WIPE` — DESTRUCTIVE: wipe all player
  data. Requires literal `CONFIRM-WIPE` token as a typo-guard. Reverts
  active transforms first so carrier buffs and summoned minions clean
  up gracefully.

**Curated matrix installed**: the 443-ability + 64-V-Blood curated config
is now active. You'll see Tier / Difficulty / Notes / FullReplace flags /
PowerScalingMode overrides per-unit in `.beelz preview` and on the BCH
API. (Was sitting in `Beelzebub/docs/` but the runtime never read it.)
Existing config backed up to `ability_rules.json.backup-pre-v0.20.0`.

## [0.20.0] - 2026-05-22

### Summons now fight FOR you, not against you

The "lead a horde" feature. When you cast a summon ability while
transformed (Stonebreaker's reinforcements, Bishop of Shadows' shadow
soldiers, etc.), the spawned NPCs:

- **Spawn on your team** — they attack enemies, not you.
- **Follow you** — leashed-mode follower behavior.
- **Despawn on revert** — `.beelz revert` clears all your summons too.
  No wandering mob trail.

Mechanically: a Harmony Prefix patch on V Rising's
`LinkMinionToOwnerOnSpawnSystem` catches each freshly-spawned minion
where the originating caster is a Beelzebub-transformed player, then
rebinds its `EntityOwner` / `Faction` / `Follower` / `Minion` components
to point at you. Pattern lifted from Bloodcraft's
`FamiliarBindingSystem.ModifyFollowerFactionMinion`.

The default deny-list no longer filters summon abilities. Existing
installations need to edit `ability_rules.json` to remove
`"_Summon_"`, `"_Summoning_"`, `"_CallReinforcements_"`,
`"_Reinforcement_"` from `DenyPatterns` (or delete the file to
regenerate).

New config: `Transform_SummonsAreAllies` (default `true`). Set false
to disable the rebind and leave V Rising's vanilla spawn handling
in place.

To test after restart:

1. Edit `BepInEx/config/kdpen.Beelzebub/ability_rules.json` — remove
   the four summon-related entries from `DenyPatterns`.
2. `.beelz admin reload` in chat.
3. `.beelz transform StoneBreaker` → `.beelz current` should now show
   Reinforcement on the bar.
4. Cast Reinforcement → 2-3 bandit-stone-worker NPCs appear around you,
   fighting on your side.
5. `.beelz revert` → minions despawn.

## [0.19.0] - 2026-05-22

### Big fix: V-Bloods now show their real "ultimate" on the bar

If a V-Blood transform was missing one of its signature attacks (Stonebreaker
without MountainRumbler, Bishop of Shadows missing his shadow nova, etc.), this
release fixes it. Cause was the Hard-difficulty filter (Z3) failing to
recognize that V-Blood basic abilities and their Hard duplicates are siblings
because of the `_VBlood_` infix asymmetry. Hard duplicates were leaking
through, consuming slot space, and pushing real signature abilities off the
bar. Fixed.

Stonebreaker's bar now has SwingAttack + RockSmash + MountainRumbler +
SpinAttack (4 real abilities). Reinforcement (summon) stays filtered
because it doesn't work without an NPC caster context (see below).

### `.beelz preview` no longer crashes on big units

The chat reply was concatenating into a single message that blew past V
Rising's 510-byte reply cap. Now each slot is emitted as its own line.
Same fix applied to `.beelz current`.

### `_Reinforcement_` filtered by default

V Rising's NPC summon abilities don't spawn anything when cast by a player
(faction/team setup needs an NPC caster context). v0.18.0 added a default
filter for `_Summon_` / `_CallReinforcements_` but missed the bare
`_Reinforcement_` pattern Stonebreaker uses. Added.

If your `ability_rules.json` is from a previous version: add
`"_Reinforcement_"` to your `DenyPatterns` array, OR delete the file to
regenerate.

### New scaling mode: `PlayerLeveled`

Closes the "I want transforms to scale with my character's level, not just
be flat boss-tier" design conversation. New value for
`Transform_PowerScalingMode` in your config (or per-entry in
`ability_rules.json`):

- **`PlayerLeveled`** — reads your CHAR_ prefab's stats AND your current
  UnitLevel, applies `boss_stat × clamp(player_level / max_level, 0, 1)`.
  At low level the transform's bonus is small. At max level you get the
  full boss tier. On Bloodcraft servers your UnitLevel reflects
  Bloodcraft's leveling — so prestige resets reduce the transform bonus
  proportionally.

Plus a `Transform_PlayerLeveled_MaxLevel` config (default 90 = V Rising's
typical max gear level) to anchor the curve. Bloodcraft servers with
raised caps should bump this to match.

### Summon allies — researched, deferred

Bloodcraft makes familiars side with the player by overriding their Team /
Faction on spawn. We could plausibly do the same for transient summon-
ability spawns from a transformed Beelzebub player. It needs a dedicated
session of research + implementation: hooking the spawn system, marking
spawns as Beelzebub-triggered, faction override, follow logic, cleanup on
revert. Filed as task #74. For now summons stay filtered.

### Visual model still won't change — V Rising hard limit

Reiterating: V Rising exposes exactly five native shapeshift forms (Wolf,
Bear, Rat, Spider, Toad). For Errol the Stonebreaker, the Putrid Rat
Ravager, and ~95% of V-Bloods, transforming will swap your spell bar +
stats but keep your character model the same. Server-side mods can't add
new shapeshift forms.

## [0.18.0] - 2026-05-22

### See what's on your bar — `.beelz current` + `.beelz preview`

Two new chat commands from in-game testing feedback:

- **`.beelz current`** — prints what's effectively on your spell bar right
  now. Transform-aware: while transformed it shows the unit's slot loadout
  for the current phase, while not transformed it shows your saved
  grants resolved for the weapon you're holding. Each slot shows the
  ability name + a category badge (`Ultimate`, `Aoe`, `Travel`, etc.).
  Use this to debug "why isn't this ability on my bar?"
- **`.beelz preview <index|name>`** — shows what abilities you'd get if
  you transformed into a unit, *before* activating. Lists every phase
  the unit has curated, the difficulty + tier + scaling mode + notes,
  and the server difficulty mode (so you know what's gated). Helps you
  pick which V-Blood to transform into.

### Summon abilities are filtered by default

V Rising's NPC summon abilities (StoneBreaker's golems, Bishop of Shadows'
shadow soldiers, etc.) cast but don't actually summon when used by a
player — the spawner code needs an NPC caster context. The player just
does the cast animation with no effect. So these abilities now default
to filtered out (`_Summon_`, `_Summoning_`, `_CallReinforcements_` added
to the default `DenyPatterns`). Same for cinematic feed-prep abilities
(`_FeedBoss_`, `_Feed_Initiate_`).

**Existing installs**: your `ability_rules.json` is loaded as-is. To
pick up the new filter entries, either edit the file to add them to the
`DenyPatterns` list, or delete the file (it'll regenerate on next start
with the new defaults — you'll lose any custom curation, so back it up
first).

### Visual model — known V Rising hard limit

If your character model isn't changing during a transform, here's why:
V Rising only exposes five native shapeshift forms (Wolf, Bear, Rat,
Spider, Toad). Server-side mods can't create new ones. Transforming into
anything outside those families (humanoids, undead, golems, etc.) won't
change your model — only the spell bar + stats. This is a V Rising
limitation, not a Beelzebub bug.

For "boss form" feel without a model swap, combine `FullReplace: true` +
`PrefabAbsolute` scaling + `MovementSpeedScale > 1` so it feels
mechanically powerful even visually unchanged.

## [0.17.1] - 2026-05-22

### Hotfix — transforms now actually swap your spell bar

Bug surfaced in v0.17.0 testing: typing `.beelz transform <unit>` reported
success and let you `.beelz revert` later, but the captured abilities never
showed up on your action bar.

Cause was a buff-creation timing problem: V Rising's `ApplyBuff` event
queues for next tick, and my code tried to enrich the buff immediately on
the same frame — found nothing, gave up silently, but left you with the
"transformed" status. Now using V Rising's synchronous buff-instantiation
API instead. Apply, get the entity back, enrich it on the same frame.

If you were on v0.17.0 with a transform stuck in the "registered but not
applied" state, `.beelz revert` to clear it, then re-run `.beelz transform
<unit>` and the spell bar should swap immediately.

## [0.17.0] - 2026-05-22

### Pick how transforms get their power (TX7) + locked weapons during
### FullReplace (TX8)

Admin choice: when a player casts a captured ability while transformed,
where does its power come from? Set `Transform_PowerScalingMode` in your
config (or `PowerScalingMode` per-entry in `ability_rules.json`):

- **`CuratedScales`** (default — current behavior): admin tunes each
  transform via `DamageScale` / `HealthScale` / `CooldownScale` /
  `MovementSpeedScale`.
- **`PrefabAbsolute`**: auto-read the boss's `UnitStats` from its CHAR_
  prefab and apply matching stats to the player while transformed.
  Player hits *exactly* as hard as the boss. Ignores player level.
- **`PlayerScaled`**: apply nothing. V Rising's vanilla formula scales
  the captured ability by the player's own PhysicalPower / SpellPower —
  including any Bloodcraft expertise, blood-quality, or level buffs
  the player carries. **Bloodcraft prestige resets automatically rescale
  the ability**. The Bloodcraft-friendly mode.

Picking a mode:

| Your setup | Recommended |
|---|---|
| Bloodcraft + prestige should matter | `PlayerScaled` |
| Solo/coop, want per-boss curation | `CuratedScales` |
| Boss-form RP, transforms = legitimate boss fights | `PrefabAbsolute` |
| Mixed | Global `PlayerScaled`, per-entry override to `PrefabAbsolute` for the specific units you want cinematic |

**FullReplace also locks your weapon now.** Any transform marked
`"FullReplace": true` in `ability_rules.json` now adds V Rising's native
`BlockEquipmentSwapping` to the carrier buff — players cannot swap weapons
mid-transform. Auto-clears on `.beelz revert`.

BCH integrators: `scaling_mode=<value>` appended to `.beelz api transforms`
and `.beelz api catalog units` so the UI can render a badge for the
chosen mode.

See `docs/ABILITY_MAP_FORMAT.md` "Power scaling modes" section for the
full design + per-mode mechanics.

## [0.16.0] - 2026-05-22

### TX3 — "Be the NPC" mode (FullReplace)

You can now mark individual transforms as **FullReplace** in
`ability_rules.json` and force the native shapeshift visual on for that
specific unit, without flipping the global `Transform_NativeShapeshift_Enabled`
config. Pair it with curated TX6 scales for the full cinematic moment.

```json
"CHAR_Forest_Wolf_VBlood": {
  "Enabled": true, "Difficulty": "Basic", "Tier": 2,
  "DamageScale": 0.8, "MovementSpeedScale": 1.25,
  "FullReplace": true,
  "Notes": "Alpha Wolf — fast, slightly weaker damage."
}
```

Activates as: model swaps to a wolf, your spell bar is the Alpha Wolf's
kit, your stats get +25% speed and -20% damage. **The visual drops on
your first spell cast** (V Rising vanilla — native shapeshift forms exit
on off-form casts). Mechanically you still keep the spell bar + stats
through the visual drop; only the model reverts.

So FullReplace is a 1-3 second cinematic that announces "I AM the wolf"
plus the persistent power profile, not a permanent model swap. If a
future V Rising update exposes a way to block the auto-exit, FullReplace
becomes a permanent-while-active mode for free.

BCH integrators: catalog and transform endpoints now include
`full_replace=0|1` so the UI can render a distinct badge.

See `docs/ABILITY_MAP_FORMAT.md` for the full TransformMap schema.

## [0.15.1] - 2026-05-22

### Better data for BCH UI badges (IN2)

For server admins / BCH integrators only — players see no behavior change.

When BCH (or any other API consumer) reads the parseable `.beelz api *`
endpoints, every captured ability now carries a `cat=...` tag (`Ultimate`,
`Travel`, `Aoe`, `Projectile`, `Summon`, `Buff`, `WeaponSpell`, `Spell`, or
`Other`) and every transform unlock carries a `type=...` tag (`Vampire`,
`Undead`, `Beast`, `Humanoid`, `Construct`, `Demon`, or `Other`). BCH can
use these for icons/colors/tooltips without parsing the raw prefab name.

Backward-compatible: new fields are appended to each `[BEELZ:list]`,
`[BEELZ:tx]`, `[BEELZ:catalog-unit]`, and `[BEELZ:catalog-ability]` line.
Existing parsers that don't know about them just ignore them.

`captured_at` timestamp (the third IN2 item) is deferred — it needs a
state-file schema bump and we don't want to pair that with the still-
untested v0.14.0 + v0.15.0 captures. Revisit when there's a concrete UI ask.

## [0.15.0] - 2026-05-22

### Admin balance dials for transformations (TX6)

Each entry in your `TransformMap` (the per-V-Blood matrix in
`ability_rules.json`) now accepts four new fields that let you tune how
strong each transformation is, all defaulting to `1.0` (no change):

- **`DamageScale`** — scales both physical and spell damage while the player
  is transformed into that unit. `1.5` = +50%, `0.5` = -50%.
- **`CooldownScale`** — `>1.0` makes cooldowns longer (admin nerf), `<1.0`
  makes them shorter. Applies to both spells and weapon abilities.
- **`HealthScale`** — scales the player's MaxHealth while transformed.
- **`MovementSpeedScale`** — scales movement speed while transformed.

These let you keep an OP transformation available without it dominating —
e.g. give the Dracula transform a `DamageScale: 0.5, CooldownScale: 1.5,
HealthScale: 0.7` profile so it costs more than it gives. Or buff a weak
transform with `DamageScale: 1.3`. All bonuses apply only while the
transform is active and lift cleanly on revert.

The new `.beelz api transforms` and `.beelz api catalog units` responses
include these four scales per entry so BCH (when its UI catches up) can
render a "Power Profile" on each transform card.

See the updated `docs/ABILITY_MAP_FORMAT.md` for the full schema + a
worked Dracula-style nerf example.

## [0.14.0] - 2026-05-22

### Bug-fix release — three big symptoms, one root cause

In-game testing of 0.13.0 hit three problems with a single shared cause: casting
any ability dropped the wolf/bear/etc visual the moment you fired it; `.beelz
revert` brought back your weapon abilities but left your spell-book spells (slots
5/6) empty; and the server log filled with `Clearing entity X which is a
modification source ... AbilityGroupSlot` warnings on weapon swaps. All three are
fixed by rebuilding the transform spell-bar pipeline around a dedicated carrier
buff (the same pattern Bloodcraft uses for exoform).

- **Revert now restores your full pre-transform spell bar**, including the
  spells you chose in your spell book. No more empty slots 5/6.
- **Native shapeshift visuals are now opt-in.** Default off — the form would
  drop on first cast anyway. Admins who still want the wolf model can enable
  `Transform_NativeShapeshift_Enabled` in the config knowing the form is purely
  cosmetic and will end at the first spell.
- **No more `AbilityGroupSlot` warnings** flooding the server log on weapon
  swaps and autosaves.
- **Bishop of Death (and other "Hard-only" V-Bloods) get their full kit on
  Basic-mode servers.** Some V-Bloods only have a `_Hard_` variant of certain
  abilities — that's the only version the boss itself has — so they're now
  allowed through on Basic mode instead of being filtered out and leaving you
  with fewer slots than the boss carries.
- **Type `.beelz` (no subcommand) for a five-line overview** of how the mod
  works + where to find the full command list. `.beelz help` still gives the
  walkthrough; `.beelz commands` still lists every chat command.

## [0.13.0] - Unreleased

### TX5 — Multi-phase transformation handling

- Bosses like the Geomancer (Human↔Golem), Morgana, Valyr and Iva have
  in-fight phase shifts. Beelzebub now models this via a per-ability
  `Phase` field in `AbilityMap` (int, default 1). Admins curate which
  abilities belong to phase 2 / 3 of a boss's kit; the runtime filters
  the transformed spell bar by phase.
- New `.beelz phase <n>` chat command — while transformed, swap your
  spell bar to the unit's phase-N abilities. No args = show current +
  available phases for the active transform.
- For bosses without multi-phase mechanics, everything stays at Phase=1
  and the command shows "this unit doesn't have multi-phase mechanics
  curated." — backward-compatible no-op.
- `.beelz api active` now includes `phase=<current> phases=<csv>` so BCH
  can render a phase selector.
- New BCH event `[BEELZ:event] type=transform-phase-shift u=… un=… phase=<n>`
  fires each time the bar swaps.

### Example curation (Geomancer)
```json
"AB_Geomancer_Human_Projectile_Group":        { "Phase": 1, "Notes": "Phase 1 — human form ranged" }
"AB_Geomancer_Transform_ToGolem_AbilityGroup":{ "Phase": 1, "Notes": "Phase 1 trigger to golem" }
"AB_Geomancer_GroundSlam_Group":              { "Phase": 2, "Notes": "Phase 2 — golem form melee" }
"AB_Geomancer_UndergroundTremmors_AbilityGroup":{ "Phase": 2, "Notes": "Phase 2 ground AoE" }
```
Then `.beelz transform geomancer` → phase 1 abilities. `.beelz phase 2`
→ swap to golem-form abilities.

## [0.12.0] - Unreleased

### IN3 — Catalog endpoints (BCH "collection book") + IN4 — Progress endpoint completion

- New `.beelz api catalog` returns a summary line:
  `[BEELZ:catalog-summary] abilities=N units=M server_mode=Basic|Brutal`.
- New `.beelz api catalog units [page]` streams every curated transform
  target with its matrix attributes (Enabled / Difficulty / Tier / Notes).
  Paginated, 40 entries per page.
- New `.beelz api catalog abilities [page]` streams every curated ability
  with full attributes (Weapons, Forms, TransformOnly, Enabled,
  Difficulty, DamageScale, CooldownScale, Notes). Paginated, 40 per page.
- Wire format `[BEELZ:catalog-unit]` / `[BEELZ:catalog-ability]` per row,
  `[BEELZ:end] cmd=… count=… total=… page=… pages=…` trailer per call.
- `.beelz api progress` now uses the curated `TransformMap.Count` as the
  transform total (was a hardcoded 61 placeholder). `.beelz progress`
  chat-side display matches.
- `.beelz commands` cheat-sheet adds `catalog` to the api line.

## [0.11.0] - Unreleased

### TX4 — Brutal-vs-Basic difficulty gating

- New config knob `Server_DifficultyMode` (string, default `"Basic"`,
  values: `"Basic"` | `"Brutal"`). Admin specifies the server's mode once;
  the runtime gates Brutal-tagged content accordingly.
- Each `AbilityMap` entry now has a `Difficulty` field (string, default
  `"Basic"`). Matches the `Difficulty` field that's been on TransformMap
  entries since TX1.
- New `.beelz admin difficulty [basic|brutal]` command — show current mode
  with no argument; set it with one. Lets you switch modes mid-session
  without editing the config file.
- Gating points:
  - `AbilityFilter.ShouldCapture` refuses Brutal-tagged abilities on a
    Basic server. Auto-tags any ability whose prefab name contains
    `_Hard_` as Brutal unless the matrix overrides.
  - `TransformService.TryActivate` refuses Brutal-tagged transformations
    on a Basic server with a clear message.
  - Transform-unlock rolls (regular kills + V-Blood kills) skip the
    Brutal-only roll on a Basic server.
- `.beelz api info` now emits `difficulty=Basic|Brutal` on every ability.
- BCH wire-format gains the difficulty field for tooltip display.

## [0.10.1] - Unreleased

### TX2 — TransformMap curated for all 60+ V-Bloods

- `docs/ability_map.curated.json` now ships a complete TransformMap section
  with 64 entries (61 V-Bloods + a few non-V-Blood transform-source units
  like Undead Leader). Tier distribution: 1×7, 2×9, 3×14, 4×24, 5×10.
- All entries default to `Enabled: true` and `Difficulty: "Basic"`. Admins
  flip individual `Enabled` to false to disable broken or undesired
  transforms.
- Notes field provides admin context: which boss this is in V Rising, what
  weapon family the boss uses, any known TX5 / TX6 special considerations
  (e.g. Geomancer's built-in phase shift, Beatrice's gargoyle phase 2 glitch).
- Drop the file as-is into `BepInEx\config\kdpen.Beelzebub\ability_rules.json`,
  then `.beelz admin reload` to pick up changes live.

## [0.10.0] - Unreleased

### TX1 — TransformMap matrix (per-unit attributes for transformations)

- New `TransformMap` section in `ability_rules.json`. Mirrors the existing
  `AbilityMap` workflow but keyed by `CHAR_*` prefab names (e.g.
  `CHAR_Vampire_Dracula_VBlood`). Per-unit fields:
  - `Enabled` (bool, default true) — admin kill-switch. False blocks both the
    transform-unlock roll on kill AND `.beelz transform` activation. Lets you
    surgically disable a broken or undesired transform without removing the
    feature for everyone.
  - `Difficulty` (string, "Basic" or "Brutal") — placeholder for the TX4
    difficulty gate. Currently stored but not enforced.
  - `Tier` (int 1-5) — power tier; will drive TX6 global scaling.
  - `Notes` (string) — admin annotation; surfaced via `.beelz api transforms`.
- Wired into the runtime:
  - `TransformService.TryActivate` refuses to start a transform if the unit
    is `Enabled: false` (player gets a chat message naming the unit).
  - `DeathEventListenerSystemPatch` skips the transform-unlock roll for
    disabled units.
  - `VBloodSystemPatch` skips the V-Blood transform-unlock roll for disabled
    units.
- `.beelz api transforms` now includes `enabled=` / `difficulty=` / `tier=`
  on every unlock line — BCH can render matrix data alongside the list.
- Documentation:
  - `docs/ABILITY_MAP_FORMAT.md` has a new "transformation matrix" section.
  - `docs/ability_map.curated.json` seeded with two example entries
    (Dracula, Forest Wolf). TX2 will curate the full ~60 entries.

## [0.9.0] - Unreleased

### AT-series — admin tooling + progress %

- **`.beelz admin inspect <player>`** — see another player's full state:
  capture counts (V-Blood / regular), universal + per-weapon slot binds,
  transform unlocks, active transform, hotkeys, verbosity, BCH event status.
- **`.beelz admin revoke <player> <unitGuid> <abilityGuid> [reason]`** —
  remove a capture. Audit-logged.
- **`.beelz admin revoke-transform <player> <unitGuid> [reason]`** —
  remove a transform unlock. Auto-reverts if the player is mid-transform
  into that unit. Audit-logged.
- **`.beelz admin force-transform <player> <unitGuid>`** — start a
  transformation on another player. Bypasses unlock + cooldown. Useful
  for GM events, debugging, recovery. Audit-logged.
- **`.beelz admin clear-transform <player>`** — end another player's
  active transformation. Audit-logged.
- Every grant / revoke / force / clear writes a `[Beelz AUDIT]` line to
  `BepInEx\LogOutput.log` with admin SteamID + target + reason for
  diagnosing player reports.

### Collection progress
- **`.beelz progress`** — your own collection: captured / total, transforms
  unlocked, slot+hotkey bind counts.
- **`.beelz admin progress <player>`** — view another player's progress.
- **`.beelz api progress`** — BCH-readable wire-format version (partial IN4).

## [0.8.0] - Unreleased

### W4 — Named hotkey bindings (server-side scaffold)

- New chat commands under `.beelz hotkey`:
  - `.beelz hotkey set <name> <index>` — bind a named hotkey to a captured
    ability. Names are case-insensitive and admin-configurable; defaults to
    a 5-per-player cap (raise via `Hotkeys_MaxPerPlayer`).
  - `.beelz hotkey clear <name>` — remove a binding.
  - `.beelz hotkey list` — see your bindings + cap.
- New API endpoint `.beelz api hotkeys` streams the caller's bindings in
  the BCH wire format for the client UI to render buttons.
- New config knobs in `Hotkeys` section:
  - `Hotkeys_Enabled` (default true) — admin master switch.
  - `Hotkeys_MaxPerPlayer` (default 5) — per-player cap.
- `Enabled: false` and `TransformOnly: true` matrix rules are respected at
  bind time — you can't hotkey a disabled or transform-only ability.
- state.json schema bumped to v6 (forward-compat from v5). New
  `PlayerDto.Hotkeys` is a `Dictionary<string, int>` (name → ability GUID).

### Server-side only — cast-trigger pending BCH

- The actual "press a button → ability fires" mechanism lives in BCH's
  client mod. The server stores the binding and exposes it via the API;
  BCH renders the UI and triggers casts. Once BCH is ready, we'll wire
  the trigger path (likely a chat command BCH sends to the server, which
  resolves the bound ability and fires it on the player).

## [0.7.2] - Unreleased

### Richer BCH API tooltips (A1-full repurposed)

- V Rising's localization data isn't shipped on the dedicated server, so
  there's no in-game text source for ability descriptions on our end. The
  pragmatic solution: surface the **admin-curated `Notes` field from the
  `AbilityMap` matrix** as the description. You're already curating it
  for weapon/form classification; the same Notes line becomes the tooltip.
- `.beelz api info <i>` now emits, in addition to the previous fields:
  - `desc=<Notes>` from the matrix, or "Captured from <unit>." fallback.
  - `weapons=Sword,GreatSword` — the resolved weapon-family list.
  - `forms=any` or `forms=Wolf,Bear` — form restriction.
  - `transform_only=0|1`, `enabled=0|1`.
  - `damage_scale=1.00`, `cooldown_scale=1.00`.
- Wire-format value safety: spaces are encoded as `_` and `=` as `-` so
  multi-word descriptions don't break BCH's key=value parser.
- `.beelz api slots` extended for W3: now streams universal + per-weapon
  buckets, tagged with `bucket=any` or `bucket=<weapon>`, plus a
  `[BEELZ:slot-current] weapon=<X>` footer showing the live weapon.

## [0.7.1] - Unreleased

### W5 telemetry (damage/cooldown scaling still pending)

- Damage-event telemetry: with VerboseLogging on, the server now logs every
  damage event whose source ability matches one of your Beelzebub-granted
  slots. Useful for admins curating DamageScale values to verify which
  abilities are actually firing.
- The full DamageScale / CooldownScale runtime is **NOT yet active** —
  V Rising's `DealDamageEvent` struct has readonly fields in the IL2CPP
  wrapper, so direct damage mutation at the damage-system layer is blocked.
  Path forward is a per-cast stat-buff approach (apply temporary SpellPower
  / PhysicalPower modifier). That's a focused future session.

## [0.7.0] - Unreleased

### Per-weapon slot loadouts (W3)

- New `.beelz weapon-grant <weapon|auto> <slot> <i>` command lets you bind a
  captured ability to a slot **for a specific weapon family**. Different
  weapons can hold different abilities in the same slot number.
  - `auto` resolves to whatever you're currently wielding.
  - Valid families: Sword, GreatSword, Axe, Mace, DualHammers, Spear,
    Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe,
    Slashers, TwinBlades, Unarmed, FishingPole.
- New `.beelz weapon-unslot <weapon|auto> <slot>` clears a weapon-specific
  bind.
- `.beelz grant` still exists — it now writes to the **universal** bucket
  (any weapon), same as before.
- Precedence: weapon-specific binding wins over universal on the same slot
  when you wield that weapon. Switch weapons → slots auto-swap.
- `.beelz list` now shows both universal slots and per-weapon slot
  buckets, plus what weapon you're currently wielding.
- `state.json` schema bumped to v5. Existing v4 saves load as universal
  bindings — your old grants survive untouched.

## [0.6.0] - Unreleased

### Layered slot precedence (W2)

- `.beelz grant` now respects each ability's `Weapons[]` matrix entry:
  - **Universal abilities** (tagged `Magic`, or with empty `Weapons[]`) apply
    on top of any weapon, including unarmed and fishing pole.
  - **Weapon-tagged abilities** (e.g. `["Sword", "GreatSword"]`) only
    fire when wielding one of the named weapons. Equipping a crossbow
    silently falls back to the crossbow's natural ability for that slot.
- Replaces the old "applies only when UNARMED" gate. The reply you get
  from `.beelz grant` now lists which weapons the ability activates on.
- Reactive: swapping weapons re-evaluates compatibility per saved slot
  on the next `ReplaceAbilityOnSlot` event — no manual reload needed.
- Auto-revert and `.beelz revert` also re-apply grants weapon-aware.
- Recognized weapon families (parsed from the `EquipBuff_Weapon_*` buff
  on the player): Sword, GreatSword, Axe, Mace, DualHammers, Spear,
  Daggers, Crossbow, Longbow, Pistols, Reaper, Whip, Claws, Pollaxe,
  Slashers, TwinBlades, Unarmed, FishingPole.
- `Enabled: false` and `TransformOnly: true` are also respected at
  injection time — if you mark an ability as either, an existing slot
  binding to it goes silently inactive until you change the rule.

## [0.5.3] - Unreleased

### Admin controls per ability (Enabled, DamageScale, CooldownScale)

- Each `AbilityMap` entry gained three new fields:
  - `Enabled: true` (default) — set to `false` to kill-switch an ability.
    Blocks new captures AND blocks `.beelz grant` / transform pickup. Surgical
    per-ability removal without wiping the player's existing collection.
  - `DamageScale: 1.0` (default) — placeholder for the W5 damage-scaling
    runtime. Curate values now; runtime catches up later.
  - `CooldownScale: 1.0` (default) — same, for cooldown scaling.
- The curated `ability_rules.json` now has these fields populated on all 443
  V-Blood entries with default values, ready to be edited.
- Transform-only abilities now also refuse `.beelz grant` with a clear message
  (was scaffolded in 0.5.2; runtime gate added here).

## [0.5.2] - Unreleased

### Ability matrix scaffolding (W1, prep for W2)

- `ability_rules.json` gains a new `AbilityMap` section: per-ability matrix
  with **multiple weapon types per ability**, optional shapeshift-form
  restriction, and a per-ability `TransformOnly` flag.
- Admins curate this file directly. Hot-reload via `.beelz admin reload`.
- Two new docs: `Beelzebub/docs/ABILITY_MAP_FORMAT.md` (format reference
  + location of the live runtime file) and `Beelzebub/docs/ability_map.starter.json`
  (a copy-pasteable starter with examples).
- No behavior change yet — the cascade that consumes this matrix lands in
  W2 (the layered slot-precedence work). 0.5.2 is the data-layer scaffold.

## [0.5.1] - Unreleased

### Shapeshift mapping trim (player feedback)

- Removed Human-disguise mapping — the form moves much slower than real
  humanoid NPCs and felt wrong. Humanoid captures now stay as the base
  vampire model.
- Removed Golem mapping — wasn't reliably player-usable.
- Kept: Wolf (incl. Werewolf), Bear, Rat, Spider (incl. Arachnid), Toad
  (incl. Frog).
- Trimmed the "(No native shapeshift form matched...)" reply — that's
  the expected case for most captures now and doesn't need an apology.

## [0.5.0] - Unreleased

### Visual shapeshift (A4)

- `.beelz transform <unit>` now applies a **visual model swap** alongside
  the spell-bar swap, using V Rising's eight native shapeshift form buffs:
  Wolf, Bear, Rat, Spider, Toad, Golem (tier-2), Human disguise.
- The mod maps your captured unit to the closest native form by name:
  Werewolves and wolf-variants → Wolf; bear-family → Bear; spider/arachnid
  variants → Spider; humanoids (Bandit, Militia, Villager, V-Hunter,
  Vampire, Witch, Cultist, Paladin, Knight, Priest, Farmer, Guard...) →
  Human disguise.
- `.beelz revert` removes the visual form. Auto-revert (Timed mode)
  removes it too.
- If a captured unit doesn't fit any heuristic bucket (banshees, Treant,
  Manticore, undead skeletons, etc.), only the spell bar changes — your
  vampire model stays. The reply tells you which form was used.
- Why heuristic: V Rising's engine has no generic "morph into arbitrary
  CHAR_" API. Eight native forms is the realistic ceiling. Future
  community discoveries may unlock more; this gets us the visible payoff
  today.

## [0.4.0] - Unreleased

### Seamless slot apply (A2)

- **No more "swap a weapon to apply"**: `.beelz grant`, `.beelz unslot`,
  `.beelz preset load`, `.beelz transform`, and `.beelz revert` now apply
  to your spell bar instantly. Grants apply when you're unarmed (a weapon
  still wins for its own slots). Transforms apply regardless of weapon and
  restore your previous bar on revert.
- Under the hood: the mod mutates the player's existing EquipBuff weapon
  buffer in-place and calls `ServerGameManager.ModifyAbilityGroupOnSlot`,
  the same approach Bloodcraft uses for its NPC-spell shift slot.
- Auto-revert (Timed mode) also restores the spell bar in-place.

## [0.3.2] - Unreleased

### Fixes from second test
- **No more spurious captures from resource nodes or summons**: the death-event
  hook now requires the killed prefab name to start with `CHAR_` and not
  contain `_Summon` or `_Servant`. Previously, breaking a sulfur deposit or
  killing a temporary summoned skeleton could roll a transform unlock.
- **Transform reply is clearer**: when you `.beelz transform <unit>`, the
  server now notes that the visual model swap is still in development.
  Your spell bar swaps; the model stays for now.

## [0.3.1] - Unreleased

### Fixes from first real test
- **Weapon abilities no longer overridden**: captured-ability slot grants now
  only apply while you're UNARMED (or holding a fishing pole). Equipping a
  weapon shows the weapon's natural abilities, as expected. Transforms
  still override the spell bar regardless of weapon (that's the
  "you ARE the unit" intent).
- **`.beelz help`** no longer errors. Replaced the multi-line reply with
  multiple short single-line messages. New `.beelz commands` lists every
  command available.
- **Crash mitigation**: defensive try/catch added to every per-frame patch
  (death-event, V-Blood, slot-replace, transform tick). A single bad
  ability or unit can no longer take down the server — the exception
  goes to `BepInEx\LogOutput.log` and processing continues.
- **`start_server_local.bat`** now `pause`s on exit so the CMD window
  stays open after a crash; you can see the last lines of output and
  read the log file paths from the bat's own farewell message.

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
