# In-game test checklist — v0.133.0 → v0.136.0

Run this on the local server **before** friends join. Each step lists the exact command and what you should
see. Tick the box, or write down what you saw instead. Logic-only parts are already covered by
`dotnet test Beelzebub.Tests` (44 tests); this list covers what only a live server can prove.

**Setup**
- [ ] Stop the server, deploy the new `Beelzebub.dll`, start the server. The log shows
      `[Beelz] ... patched` lines with no errors, and, if another mod also patches damage, a `[Beelz DMG] other patches on DealDamageSystem.OnUpdate: …` line (note which mods).
- [ ] Set `VerboseLogging = true` in `kdpen.Beelzebub.cfg` for this session (`.beelz admin set VerboseLogging true`).
- [ ] Have 3–4 captured abilities to hand: **A** a plain projectile spell, **B** a summon, **C** an ability with a
      DoT/lingering area, **D** a charge ability (if you have one). `.beelz list` shows their indexes and IDs.

---

## 1. Reseed (do this first — it puts the new shipped defaults on your server)

- [ ] `.beelz admin reseed preview` → prints `SAFE mode (no valid baseline)` (your server predates v0.135) with
      added/adopted counts, and writes nothing (the file's modified time is unchanged).
- [ ] `.beelz admin reseed merge` → same summary + `written. Backup: ability_rules.json.bak-…`. The backup file
      exists next to `ability_rules.json`; `ability_rules.baseline.json` now exists too.
- [ ] `.beelz admin reseed preview` again → **no** SAFE mode; `added 0, adopted 0, removed 0`.
- [ ] Your own edits survived: `.beelz admin ability <an ability you had tuned>` shows your value.
- [ ] **Torn baseline:** delete `ability_rules.baseline.json` by hand → `.beelz admin reseed preview` says SAFE
      mode again (not an error). Run `reseed merge` to restore it.
- [ ] `.beelz admin reseed replace` (no CONFIRM) → refuses and explains. Don't run it with CONFIRM unless you want
      to lose your curation.
- [ ] The stuck-state abilities picked up their timers: `.beelz admin ability AB_Bear_FallAsleep_Group` shows
      `forcetimeout=8` and `enabled=off`.

## 2. Incompatibility locks

Use two abilities you can slot, **X** and **Y** (names or IDs from `.beelz list`).

- [ ] `.beelz admin lock list` → "No lock groups".
- [ ] Slot both: `.beelz grant 1 <X>` and `.beelz grant 2 <Y>`. Both show on the bar.
- [ ] `.beelz admin lock add test "<X>, <Y>"` → "Lock 'test' (max 1): added 2 … Re-checked 1 online player(s)."
      **Y leaves slot 2** (the slot shows its normal ability), and you get a 🔒 message naming `test` and X.
- [ ] `.beelz admin lock check <you>` lists slot 2 as locked by `test`.
- [ ] Swap weapons and back, then relog → Y stays off the bar and the 🔒 message does **not** repeat on every swap.
- [ ] `.beelz grant 3 <Y>` → refused with 🔒 (not bound). `.beelz cast <index of Y>` → refused with 🔒.
- [ ] `.beelz hotkey set yy <index of Y>` → refused with 🔒.
- [ ] `.beelz admin lock max test 2` → Y comes back on slot 2 by itself with a 🔓 message.
- [ ] `.beelz admin lock max test 1`, then `.beelz admin lock remove test <Y>` → Y back (🔓).
- [ ] **Category lock:** `.beelz admin lock add summons "cat:Summon"` with two summon abilities slotted → only the
      lower slot stays.
- [ ] **Form bar (native slot restore):** `.beelz form-grant wolf 1 <X>` and `.beelz form-grant wolf 2 <Y>`, lock them
      (`lock add f "<X>, <Y>"`), shift into Wolf → slot 2 shows the **wolf's own ability**, not Y and not blank.
      `lock remove f` **while still in wolf** → Y appears on slot 2 without leaving the form.
- [ ] **Saddle bar:** same with `form-grant mounted 3/6 …` while riding.
- [ ] Transforms ignore locks: `.beelz transform` into a form whose kit contains a locked ability → it works.
- [ ] `.beelz admin reload` after hand-editing `ExclusionGroups` in the JSON → "Locks re-checked for N online player(s)".
- [ ] `.beelz api locks` → one `[BEELZ:lock] g=… max=… m=…` per group, then `[BEELZ:end] cmd=locks count=N`.
- [ ] Clean up: `.beelz admin lock remove test`, `lock remove summons`, `lock remove f`.

## 3. Cooldowns (runtime)

- [ ] Note A's normal cooldown (cast it; watch the bar).
- [ ] `.beelz admin tune <A> cooldown 20` → cast A from the bar: cooldown shows **20 s**. With `VerboseLogging`
      the log shows `[Beelz CD] cast #… cooldown -> 20.00s`.
- [ ] Kill/aggro the boss A came from (or check `ability-inspect <A>`) → the **boss's** cooldown is unchanged.
- [ ] `.beelz admin tune <A> cooldown clear` then `.beelz admin tune <A> cooldownscale 0.5` → half the normal cooldown.
      Equip cooldown-reduction gear → it still shortens it further.
- [ ] `.beelz admin set Grant_MinimumCooldownSeconds 10` with an ability whose cooldown is under 10 s → becomes 10 s.
      An ability with **no** cooldown at all stays at 0 (known limit). Reset the floor to 0 afterwards.
- [ ] Put A on two slots (universal slot 1 + a weapon slot) and cast from both quickly → each slot gets its own
      cooldown, neither is skipped or doubled.
- [ ] `.beelz cast <A>` (force-cast) uses the same cooldown, minimum 1 s.
- [ ] Charge ability D with a `cooldown` rule → the log warns once "uses charges — … not applied" and the charges
      behave normally.
- [ ] `.beelz admin ability-inspect <A>` shows a `cooldown:` line with `runtime` and the Applied/Expired counters.

## 4. New knobs (baked)

For each, set it, cast, then `… clear` and confirm it's back to normal. `ability-inspect` should show the new value.
- [ ] `maxstacks` on a stacking buff (e.g. 1 → the buff no longer stacks).
- [ ] `projcount` on a fan/multishot ability (e.g. double it) — more projectiles per volley; `projcount 99` is
      rejected (1–16) and values above 3× the original are capped.
- [ ] `knockback 0` on a knockback ability → no push; `knockback 2` → farther.
- [ ] `lifetime` on C → its lingering area lasts the new time; buffs are **not** affected.
- [ ] `casttime 0.5` on a normal spell → faster windup. On a channel / hold-to-cast / charge ability → refused
      with the reason.
- [ ] **Save/restart safety:** set two knobs, restart the server, kill the source boss → **boss behaves vanilla**
      (bosses are protected by the shared-part guard) and your tuned player cast still has the values.

## 5. Damage (`Damage_Mode`)

- [ ] `.beelz admin set Damage_Mode Telemetry`. Fight for a few minutes with A, B and C. Damage feels unchanged.
- [ ] `.beelz admin damage-stats` → attribution reads **reliable** for A (few ambiguous hits). Write down anything
      reading `unreliable`.
- [ ] `.beelz admin tune <A> damagescale 2`, `.beelz admin set Damage_Mode Scale`. Hit a training dummy/single mob
      with A alone (no other damage sources) → roughly double damage. `damage-stats` counts scaled hits.
- [ ] **Window → per-hit transition:** cast C (DoT) in `Off` mode, switch to `Scale` while the DoT is still ticking →
      the old DoT's remaining ticks stay vanilla; a fresh cast of C is scaled.
- [ ] `Damage_MaxFactor` set low (e.g. 1.2) → the double-damage test is capped.
- [ ] A boss using the same ability hits you for its normal damage.
- [ ] Summon B with `Summon_PowerMode OwnerRelative` → its damage follows your power. (Known open item: another
      mod overwriting summon stats — note anything odd.)
- [ ] If you want to keep it: leave `Scale`; otherwise set `Damage_Mode Off`.

## 6. Recovery / sanity

- [ ] Log out and back in → bar is correct, no errors in the log.
- [ ] `.beelz admin cleanse <you>` still works (no stuck buffs to remove is fine).
- [ ] Stop the server → the log shows a clean shutdown; `ability_rules.json` is valid JSON.

**Known limits (not bugs):** Elena's Tower of Frost ignores `leapheight` (hard-blocked in v0.136 for that reason);
the global cooldown *floor* can't add a cooldown to an ability that has none (a per-ability absolute `cooldown` can,
since v0.136 — see §7); transforms keep their own cooldowns; charge abilities ignore cooldown rules.

---

## 7. v0.136.0 tester baseline — pre-set fixes from the Discord testing round

The shipped defaults now carry the testers' fixes. Every entry the baseline touched has a `[v0.136 baseline]`
note (`.beelz admin ability <name>` shows it). **Nothing below needs tuning by hand. Just grant, cast, and compare.**
The full per-ability workbook (every preset, hard-block and soft-disable, with Pass/Fail tracking) is
`docs/V0136_ABILITY_TEST_PLAN.xlsx`, generated by `tools/build_ability_test_sheet.py`.

**Setup: get the new defaults onto your server.**

- [ ] Your server predates the baseline file, so `reseed merge` runs in SAFE mode. That mode adds missing entries
      and adopts new blocks, but it does **not** copy knob values onto entries you already have. Run
      **`.beelz admin reseed replace CONFIRM`** once. It keeps a timestamped backup, and you lose your own tuning
      (re-apply it after if you want). After this, future `reseed merge` runs are full 3-way merges.
- [ ] `.beelz admin ability AB_Lucie_LiquidFire_AbilityGroup` shows `cooldown=10` and `freelymove=0.5`. If you
      see this, the preset data is live.
- [ ] Turn on `VerboseLogging` so the `[Beelz CD]` / `[Beelz TUNE]` lines show what fired.

To grant a test ability, run `.beelz admin give <you> <unitGuid> <abilityGuid>` and then `.beelz grant <slot> <abilityGuid>`.
The unit GUID is only the source label.

### 7a. Unrooted ("locked in place" → you can move once the wind-up fires)
| ✓ | Ability | give `<unit> <ability>` | Preset | Pass when |
|---|---|---|---|---|
| [ ] | Lucie · Liquid Fire | `1295855316 846507754` | freelymove 0.5, cooldown 10 | you can walk ~0.5s into the cast; the next cast waits 10s |
| [ ] | Clive · Cluster Bomb Throw | `1896428751 -444905742` | freelymove 1.0 | free to move ~1s in (was ~2.5s locked) |
| [ ] | Errol · Mountain Rumbler | `-2025101517 593979922` | freelymove 2.0, cooldown 10 | move after ~2s (was ~6s); 10s cooldown |
| [ ] | Matka · Explode Mosquito | `-910296704 -1729075022` | freelymove 0.5 | move almost immediately; the 3 mushroom blasts still fire |
| [ ] | Dracula · Ring of Blood (**channel**) | `-327335305 -7407393` | freelymove 2.0, cooldown 10 | free after ~2s of the ~8s channel; the ring still closes |

If a row fails, check the log for `freelymove=SKIP(shared)`. That means the ability's parts are shared with
another chain, so the edit was refused on purpose. Note which ability it was.

### 7b. Cooldowns (runtime; the boss keeps its own)
| ✓ | Ability | give | Preset | Pass when |
|---|---|---|---|---|
| [ ] | Carver · Whirlwind Init | `-1669199769 2103931135` | cooldown 10 (**native 0 → started by Beelzebub**) | can't recast for 10s; log `no native cooldown — started 10.00s` |
| [ ] | Valencia · Field of Spears | `495971434 -1730693034` | cooldown 30 (native 0) | 30s before the next cast |
| [ ] | Terrorclaw · Avalanche | `-1347412392 151283351` | cooldown 12 (native 0) | 12s |
| [ ] | Adam · Electric Field | `1233988687 -1503327574` | cooldown 120 (native 1s) | 2-minute lockout; log `cooldown -> 120.00s` |
| [ ] | Talzur · Breath From Above | `-393555055 995945059` | cooldown 10 (native 0.5) | 10s |
| [ ] | Sommelier · Blood Bolt | `192051202 215933642` | cooldownscale 0.5 | ~3s instead of 6s |

**New in v0.136:** abilities with no cooldown of their own (the "spammable, no cooldown" reports) now get their
preset cooldown *started* by Beelzebub, which then re-asserts it if the game clears it. Watch for two things:
- **The client shows no cooldown swipe, but the recast is refused anyway.** That's a pass, cosmetic only.
- **You can recast before the cooldown is up.** That's a fail. Tell me which ability.

### 7c. Damage (default `Damage_Mode=Off` applies it through the power window)
| ✓ | Ability | give | Preset | Pass when |
|---|---|---|---|---|
| [ ] | Morgana · Spectral Hell | `591725925 1185642044` | damagescale 0.5, freelymove 0.5 | on a dummy, roughly half the damage it did before |
| [ ] | Beatrice · Wing Shield Emerge | `-1942352521 88850785` | damagescale 0.5 | spam no longer shreds packs |
| [ ] | Primal/Leandra · Bishop Projectile Hard | `-1805216630 651637774` | damagescale 1.5 | ~1.5× damage |
| [ ] | Meredith · Light Arrow Heal Shot | `850622034 -1509483896` | damagescale 1.5 | ~1.5× damage |

### 7d. Leaps, summons, range
| ✓ | Ability | give | Preset | Pass when |
|---|---|---|---|---|
| [ ] | Styx · Bat Summon Minions | `1112948824 -597709516` | leapheight 12 | a short hop instead of the ~50-tile launch; the bats still spawn |
| [ ] | Albert · Frog Split Travel | `-203043163 1808492070` | leapheight 12 | a low hop (was ~14 tiles up) |
| [ ] | Morgana · Summon Tail | `591725925 -195933675` | summoncap 1, summontimeout 30, damagescale 0.5 | a 2nd cast doesn't add a 2nd tail; the tail despawns after 30s |
| [ ] | Nicholaus · Priest Raise Dead | `153390636 -2034290170` | summontimeout 60 | the skeletons despawn after 60s |
| [ ] | Jade · Caltrops Hard | `-1968372384 1372353064` | range 20 | can't throw them screens away any more |

### 7e. Blacklist and hard-blocks
- [ ] `.beelz admin ability AB_Cursed_ToadKing_Swallow_AbilityGroup` → `enabled=off`. This is soft-disabled; an admin can
      turn it back on with `.beelz admin ability AB_Cursed_ToadKing_Swallow_AbilityGroup enabled true`.
- [ ] Kill Elena (Tower of Frost) or the Sommelier (Barrel Dance). Neither ability is captured. Both are now
      hard-blocked in code (crash / stuck-in-sky / plot-destroying) and can't be re-enabled.
- [ ] `.beelz admin ability AB_Sommelier_BarrelDance_AbilityGroup` → `enabled=off`, `reviewstatus=Blocked`.

### 7f. Re-audit additions (second pass over every tester note)
These were switched off or left untuned by the first pass. Their notes start with `Re-audit`.
| ✓ | Ability | give | Preset | Pass when |
|---|---|---|---|---|
| [ ] | Rufus · Bandit Foreman Crossbow | `2122229952 -2010697707` | freelymove 1.3, cooldown 10 | captured again; free to move after ~1.3s (was 3-4s rooted) |
| [ ] | Solarus · Paladin Divine Rays | `-740796338 51772774` | freelymove 1.2, cooldown 10, damagescale 1.5 | not spammable; move after ~1.2s |
| [ ] | Solarus · Paladin Heal Angel | `-740796338 -552723816` | cooldown 10 (native 0) | heal can't be spammed |
| [ ] | Grethel · Glass Rain | `910988233 -212014657` | cooldown 20 | 20s between casts |
| [ ] | Cassius · Corpse Storm | `-496360395 1006960825` | damagescale 1.5 | captured again; ~1.5× damage |
| [ ] | Adam · Eye of the Storm (Lightning Storm) | `1233988687 -820078889` | cooldown 120 | 2-minute lockout (tester asked for 100-120s) |
| [ ] | Polora · Otherside | `-484556888 1795809188` | cooldown 8 | invisibility/invulnerability no longer spammable |

**If anything in §7 misbehaves**, `.beelz admin ability <name> defaults` removes that one ability's preset,
so you can confirm the preset caused it.
