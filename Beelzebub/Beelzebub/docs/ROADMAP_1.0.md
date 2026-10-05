# Road to 1.0

Written 2026-10-04 at v0.137.6. Before this there was no formal 1.0 list; this one was assembled from `BACKLOG.md`,
the README's Roadmap and Known limitations, `V0136_ABILITY_TEST_PLAN.xlsx`, `TESTER_FEEDBACK_TRIAGE.md`,
`ABILITY_PREP_ROADMAP.md`, `CHAIN_AUDIT.md` and `ABILITY_AUDIT.md`. The public copy (Discord post) is at the end.

Engineering items go through `BACKLOG.md` (one slug each, planned with `dod plan <slug>`). This file is the umbrella:
when an item below gets a backlog slug, write the slug next to it here.

## 1. Ability testing (v0.136 ability test plan)

Source: `docs/V0136_ABILITY_TEST_PLAN.xlsx` (sheets Start here, Presets, Hard-blocked, Soft-disabled). At 2026-10-04
the owner's copy had 4 of 615 rows filled in; the testers' own copies were not seen.

| Group | Count | What is checked |
|---|---|---|
| Presets | 209 | Re-tuned cooldowns, ranges and cast feel, against the sheet's Expected result (Pass / Partial / Fail) |
| Hard-blocked | 20 | Abilities that crashed, killed or launched players: they never drop and cannot be granted |
| Soft-disabled | 386 | Off by default: each gets a verdict (re-enable / re-enable with a tweak / keep off) |

General play to cover alongside the sheet: captured and vanilla spells mixed on one bar, hotkeys, the bar after weapon
swaps, transforms and dismounting, summons in group fights, the Devour (capture) rate, and reconnecting while
transformed (form and summons back within 90 s). Optional for server admins: a session with `Damage_Mode = Telemetry`
and the `.beelz admin damage-stats` output.

## 2. Ability reconfiguration (from the test results)

All curation goes into `Resources/ability_rules.default.json` / `ability_metadata.json` (CLAUDE.md › Ability-data sync
discipline) and follows `ABILITY_CHANGE_IMPACT.md` (solve the class, not the instance).

1. Re-tune every preset that comes back Partial or Fail.
2. Apply the soft-disabled verdicts to the shipped defaults.
3. Make problem abilities safe instead of blocked where possible (from `TESTER_FEEDBACK_TRIAGE.md` tiers 2-3, not
   re-checked on v0.137.6): Gaius Twinblade Throw and Corpse Buff, Spider Baneling Explode, Treant and Golem Fall Asleep,
   Ziva Jetpack, Albert / Toad King Swallow and Spit, Gloomrot Technician Fiddle / Bell Ringer, Elena Tower of Frost,
   Leandra ShadowStep / TrippleBolt. Triage proposes an `AbilitySafetyService`.
4. Fix the broken-summons cluster (triage tier 3).
5. Resume the multi-stage boss ability audit (`CHAIN_AUDIT.md`, paused): casts that lose a later stage.
6. Fill in missing ability names, descriptions, schools and types (`ABILITY_AUDIT.md` coverage gap;
   `ABILITY_PREP_ROADMAP.md` A-C for the curation schema, audits and tooling).

## 3. Stability

1. Startup `Couldn't remap old Modification Id` errors - DONE v0.137.6 (`modid-remap-errors`).
2. A transformation that expires on its own does not fully clean up - backlog `timed-revert-orphan`.
3. One in-play bar-cleanup pass took 268 ms - backlog `modleak-pass-cost` (diagnostic first).
4. Re-run the mounted-bar and form/transform bar scenarios on v0.137.6 (not re-run after the modleak sweep).
5. Tooling and docs - backlog `vrclient-close-server-quit`, `dev-snapshot`, `docs-consolidation`.

## 4. Planned features (which ones are in 1.0 is the owner's open decision)

1. Mastery - per-unit mastery levels and set rewards on top of the bestiary.
2. Raphael (client UI; formerly "BloodCraftHub UI") - ability buttons, cooldown rings, collection book, transform browser, admin panels (server side
   already ships; client work lives in the BCH workspace, contract in `BCH_INTEGRATION_HANDOFF.md`).
3. More transformations - UNDER INVESTIGATION, feasibility unknown (server-side model swap is a hard limit; whether a
   client-side mod can do it is not established). Owner wants to circle back to it.
4. Summon AI - allies that engage more reliably, PvP support.

## 5. Before 1.0

- Testing at scale and alongside other mods.
- Close out the README's Known limitations (only five transformations, some multi-stage boss abilities misfire, some
  abilities animate oddly on a vampire body, per-hit damage scaling is new, boss forms take a moment to appear).

The public roadmap lives in the README's "Roadmap: the road to 1.0" section (Thunderstore page + generated GitHub
README); keep it in step with this file.

## Public copy (Discord, posted 2026-10-04 - before the Raphael rename and the transformations status change)

```
# Beelzebub - Road to 1.0
Current build: v0.137.6 (public test)

**1. Ability testing**
- Presets (209 abilities): re-tuned cooldowns, ranges and cast feel, each checked against its expected result
- Hard-blocked (20): abilities that crashed, killed or launched players - confirm they never drop
- Soft-disabled (386): off by default - each gets a verdict: re-enable, re-enable with a tweak, or keep off
- General play: mixed captured and vanilla bars, weapon swaps, transforms, dismounting, summons in group fights, Devour rate, reconnecting while transformed

**2. Ability reconfiguration**
- Re-tune presets that test Partial or Fail
- Apply the soft-disabled verdicts to the shipped defaults
- Make problem abilities safe instead of blocked where possible (Gaius Twinblade Throw, Spider Baneling Explode, Ziva Jetpack, Toad King Swallow and Spit, and others)
- Fix broken summon abilities
- Finish the multi-stage boss ability audit
- Fill in missing ability names and descriptions

**3. Stability**
- Startup "Couldn't remap old Modification Id" errors - fixed in v0.137.6
- Transformations that expire on their own do not fully clean up
- Performance check on the bar cleanup
- Re-check mounted and beast-form bars

**4. Planned features (1.0 scope not final)**
- Mastery: per-unit mastery levels and set rewards
- BloodCraftHub UI: ability buttons, cooldown rings, collection book, transform browser, admin panels
- More transformations through the client-side companion
- Summon AI: more reliable allies, PvP support

**5. Before 1.0**
- Testing at scale and alongside other mods
- Close out the known limitations listed on the mod page

Feedback and bug reports: https://github.com/KDavidP1987/Beelzebub-Lord-of-Gluttony/issues
```
