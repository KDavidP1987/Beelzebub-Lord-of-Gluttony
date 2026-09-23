# Tester Ability Matrix — how to fill it in

File: **`TESTER_ABILITY_MATRIX.csv`** (open in Excel / Google Sheets). **One row per ability in the game's
ability set (1816).** Fill the blanks as a group, then send it back.

Regenerate anytime with `tools/build_tester_matrix_csv.py` (keeps prepopulated data, picks up new abilities).

> **v0.136.0 — the results now ship.** The working copy's `FINAL:` verdicts and `Phase 2: Cfg …` recommendations
> (`Reference Data/TESTING DATA/TESTER_ABILITY_MATRIX.xlsx`) are applied to the shipped defaults by
> `tools/apply_tester_baseline.py --write` (Hard → blocked, Soft with a config fix → fix pre-set, Soft without
> one → `Enabled: false`). Re-run it after the matrix changes, then rebuild.

## Columns

**Identity (don't edit):** `Ability`, `ID` (PrefabGUID — the key we tune by), `UnitName` (who you capture it from).
- `UnitName` is the English unit name resolved from the unit's prefab. A value ending in **`(inferred)`** was
  *guessed* from the ability's prefab name (the scrape didn't attribute it) — **please verify/correct it.**
- A **blank** `UnitName` is usually a generic / player / consumable ability with no single source unit.

**Descriptive metadata (cols D–O, don't edit — context for your decisions):** `Description`, `Type`, `School`,
`Categories`, `Mechanic` (what it does — Summon/Heal/Projectile/AoE/Movement/Melee/Buff/Channel, mined from
the prefab), `Condition` (how it activates — Aimed/Summon/Movement/…), `SpellTier` (player-spell tier),
`SourceTier`/`SourceLevel` (the source unit's tier/level), `IsVBlood`, `BaseCooldown`/`BaseCastTime` (the
ability's actual values from the prefab).
- These were enriched by mining the game's prefab data, so most are now filled. **Blanks that remain are the
  team's to fill:** `Description` (NPC abilities have no in-game description text — only ~425 player-facing
  ones exist), and `School` (only player spells have a spell school). A `UnitName` ending in `(inferred)`
  was guessed from the prefab name — verify it.

**Weapon columns** (`Magic … TwinBlades`) and **Form columns** (`Wolf … Mounted`):
- Mark **`Y`** in a weapon/form where the ability **should be usable**.
- **Leave a row entirely blank across weapons = "usable everywhere"** (no restriction). This is the default for most abilities — only fill them when an ability should be *limited*.
- ⚠️ **Allow-list semantics:** as soon as you put `Y` in *one* weapon column, the ability becomes usable **ONLY** in the weapons you marked `Y`. So "Sword + Axe only" = `Y` in just those two.
- To instead **blacklist** one weapon/form while leaving it universal elsewhere, put **`N`** in that single column (e.g. `N` under `Mounted` = "usable everywhere except while mounted").
- Cells already showing `Y`/`N` are existing shipped restrictions — change if you disagree.

**`Enabled`** — `Y` = capturable/usable, `N` = blocked. Already set to `N` for abilities we've blocked
(see `Crash/Issue`). Set `N` if you think an ability shouldn't be available at all.

**Config columns** — put the value you think the ability *should* have (leave blank for "no change / default"):
| Column | Meaning |
|---|---|
| Cooldown | seconds between casts |
| Range | max cast/aim distance |
| Charges / ChargeTime | charge count / recharge seconds (dash-style) |
| AoE | area radius |
| ProjSpeed | projectile travel speed |
| LeapHeight | leap/jump apex — **lower this for "flings me into the sky" abilities** (vanilla boss leaps ≈ 250; try 20–40) |
| Duration | applied buff/debuff length (seconds) |
| Healing | heal multiplier |
| DamageScale / CooldownScale | ×multiplier on damage / cooldown (1.0 = unchanged) |
| ForceTimeout | force an otherwise-permanent effect to expire after N seconds |
| PowerWindow | seconds the granted-cast power buff lasts |
| SummonCap / SummonTimeout / SummonUnits | summon limit / lifetime / count per cast |
| CastSpeed | movement speed while casting (0 = rooted, 1 = full) |
| FreelyMove | seconds into a cast you're free to move |
| InterruptOnHit | `Y` = the cast cancels when you're hit |
| TransformOnly | `Y` = only usable while transformed, not on the normal action bar |

**`Crash/Issue`** — mark anything that **crashes the server/game** or otherwise breaks. Use a clear tag, e.g.
`SERVER CRASH`, `GAME CRASH`, `STUCK`, `INVISIBLE`, `EXPLOIT`, `DOESN'T WORK`. Rows already flagged are the
ones we've blocked from the first test wave — confirm or correct them.

**`Notes`** — anything else: how it behaves, what it *should* do, balance thoughts, fix ideas.

## Tester-feedback audit columns (cols 65–68, added 2026-06-10)

These four columns are the **enable/disable baseline** distilled from every tester report in
`Reference Data/beelz-vblood/` (the V-Blood/boss Discord threads) + `Reference Data/BEELZ Testing.xlsx`
(Domino's NPC sheet), reconciled against the existing `TESTER_ABILITY_BASELINE.md` verdicts and the coded
hard-block list. **456 abilities had tester feedback and are annotated; the ~1,360 untested rows are left
blank** (per the decision to keep untested abilities enabled by default — only flag what testers actually hit).

| Column | Meaning |
|---|---|
| `TesterVerdict` | the raw tester signal — `GOOD`/`TUNE`/`REVIEW`/`NEEDS-WORK`/`NOT-USABLE`/`WORKS-NOT-USABLE` (v0.100 legend) or `good`/`tune`/`works-unsure`/`needs-work`/`broken`/`great`/`exploit`/`stuck`/`crash-break` (newer v0.131 threads). |
| `TesterFinding` | one-line summary of what the tester observed (`[Unit] Ability: finding`). |
| `Recommendation` | **`Enable`** (ship enabled by default), **`Soft-Disable`** (ship `Enabled=false`, admin can opt in), or **`Hard-Disable`** (code-locked, cannot be enabled). |
| `DisableType` | blank for Enable · **`Soft (default config)`** · **`Hard (code-locked)`**. |

**How the recommendation was derived:**
- **Enable** — works well / fun, with or without tuning (`GOOD`, `TUNE`, and `REVIEW` items a tester explicitly
  praised). "Needs tuning" (locked-in-place, cooldown) does **not** demote to disabled — those are config fixes.
- **Soft-Disable** — functional-but-not-worth-shipping-on (animation-only, no-damage, doesn't-summon,
  underwhelming duplicates, "unsure if usable"), recoverable launch/stuck (fling-to-sky, fall-asleep, escapable
  invisibility, off-map fly), and **balance exploits** (Rat Vanguard, Gargoyle Wing Shield, Tailor Shapeshift,
  Corpse Pile Dig, Matka one-shot Mosquito). All reversible by an admin.
- **Hard-Disable** — the 7 confirmed **crash / permanent-character-break** abilities already in the code
  hard-block list: Sir Erwin Mountup (server crash), Technician Fiddle + Gaius Twinblade Throw (game crash),
  Gaius Corpse Buff ×2 + Cassius Leap Strike (permanent stuck/T-pose), Spider Baneling Explode Poison
  (permanent invisibility). *(Candidate to add: Militia Bell Ringer "Ring Bell" — same sword-E crash class as
  Fiddle, flagged in the NPC sheet but not yet code-locked.)*

**Tally:** 173 Enable · 276 Soft-Disable · 7 Hard-Disable. This is an audit/recommendation layer — it does **not**
change the `Enabled` column or the shipped `ability_rules.default.json`; applying it to the mod data is a
separate follow-up. Regenerate with `_matrix_build.py` at the workspace root.

## What's already prepopulated
- Existing weapon/form restrictions (`Y`/`N`).
- `Enabled = N` + a `Crash/Issue` tag on every ability blocked from the first test wave (crashes, character-
  breaks, exploits).
- Config values already shipped (e.g. the launch abilities show their `LeapHeight` + `Cooldown`).
- `Notes` carries the shipped reason where one exists.
