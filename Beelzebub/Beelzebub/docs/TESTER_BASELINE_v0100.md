# Functional baseline — v0.100.0 tester feedback (full round)

Built from the re-extracted `Reference Data/beelz-vblood` Discord export (Shadow Realm test server,
BCH 0.20.0 / Beelz 0.100.0), ~58 channels reviewed against the testers' own scheme:

**Verdicts:** ✅ good-as-is · ✅🔧 good-but-tune · ✅❓ works/unsure · ✅❌ works-but-not-usable (animation) ·
🔧 needs-work · ❓ review · ❌ not-usable.
**Failure classes:** 1=won't cast · 2=casts-but-nothing · 3=character STUCK · 4=HIGH (your game crashes) ·
5=CRITICAL (server crashes).

Companion docs:
- **`TESTER_ABILITY_BASELINE.md`** — the full per-ability verdict tables (the per-unit worksheet, ~250 abilities).
- **`TESTER_FEEDBACK_TRIAGE.md`** — the earlier deep crash-resolution research (root-cause classes + per-crash
  fix candidates). Use it for *how to fix* a crasher rather than block it.

> **Framing:** this is **v0.100.0** feedback; we shipped v0.101→v0.117 since. Each item below is tagged
> **[FIXED]** / **[RETEST]** / **[OPEN]** / **[GAP]** against current `main` so we only chase real gaps.

---

## PART A — CRITICAL: block / hide (crash & stuck)

Confirmed server-crash (5), game-crash (4), character-stuck (3). Status checked vs our hard-block list (only
Mountup today) + the `incompatible` metadata set (the 5 ShadowStep/MistWalk anim-rig entries).

| Cls | Unit | Ability | ID | Effect | Status / action |
|---|---|---|---|---|---|
| 5 | Sir Erwin | Militia Fabian Mountup | -1623080868 | server crash | **[FIXED]** hard-blocked v0.101 |
| 5 | Morgana/Megara | Swarm-Travel → Orb-Barrage (combo) | -1980019894 → 1242557903 | server crash when chained | **[GAP]** block one of the pair |
| 5 | Dracula | SpellStone Bolt Spray | 1957691133 | server-crash channel | **[GAP]** block (verify repro) |
| 4 | Gaius | Arena Champion Twinblade Throw | *(no id)* | game crash | **[GAP]** block by name |
| 4 | Gaius | Arena Champion Corpse Buff | *(no id)* | locks in place, aggro off | **[GAP]** block by name |
| 4 | Gloomrot Technician | Fiddle | 1485838951 | crashes game when vanilla sword-E used while bound | **[GAP]** block |
| 4 | Leandra | BishopOfShadows ShadowStep | 1325722355 | **kills caster regardless of HP** + non-despawn clones | **[GAP]** only `incompatible`-flagged now → upgrade to **Blocked** |
| 4 | Leandra | BishopOfShadows TrippleBolt | -1795148379 | same lethal self-kill | **[GAP]** block |
| 3 | Cassius | High Lord Leap Strike | 938684260 | permanent T-pose (must self-kill) | **[GAP]** block |
| 3 | Spider Baneling | Explode Poison | -891106318 | permanent invisibility on respawn | **[GAP]** block |
| 3 | General Elena | Ice Ranger Tower Of Frost | 1431473799 | flings you to space | **[GAP]** block |
| 3 | General Elena | Ice Ranger Tower Of Frost Low Health | 2057952818 | flings you to space | **[GAP]** block |
| 3 | Albert (Toad King) | Poison Leap Travel | 1790744720 | crouch-pose lock after landing | **[GAP]** block or fix lock |
| 3 | Albert (Toad King) | Swallow | 1292896032 | stratosphere launch when feeding swallowed target | **[GAP]** block or guard |
| 3 | Treant + Golem/Geomancer | "Fall Asleep" pair | *(no id)* | toggle-lock until Wake-Up | **[GAP]** block the Fall-Asleep family |
| 3 | Beatrice (Tailor) | Gargoyle Fly Start/End | -382913708 / 1563014858 / 1551140710 | land out-of-bounds / terrain-stuck | **[GAP]** block or bound-check |
| 3 | Ziva (Iva) | Bomber Jetpack Take Off | -1770586075 | terrain clip / out-of-map | **[GAP]** block or restrict |

**~16 abilities to `reviewStatus=Blocked` now.** With the v0.115 curation gate, `Blocked` actually removes them
from capture. Most have IDs (straight into `ability_rules.default.json`); the 3 id-less ones need a name lookup.
Where the earlier `TESTER_FEEDBACK_TRIAGE.md` has a fix candidate (e.g. neutralize the NPC script), prefer that
over a permanent block.

---

## PART B — Admin-config bugs: status vs current `main`  (biggest "already-closed" win)

The "Admin Ability Config Settings" channel catalogued which shaping knobs worked at v0.100. **v0.101→0.117
fixed a large share.**

| v0.100 finding | Current status |
|---|---|
| AoE **healing** unaffected by `healing` (Nun AoE) | **[FIXED]** v0.108 deep-follow writes healing onto the AoE spawn (validated on Nun) — RETEST |
| **Range / projspeed** fail on AoE (Spider Queen) | **[FIXED]** v0.108 deep-follow reaches the AoE prefab — RETEST |
| **Force Timeout** alters speed instead of cancelling | **[FIXED]** v0.108 force-timeout split (LifeTime only added to indefinite buffs) |
| **interruptonhit** rejects "on" (parse bug) | **[FIXED]** v0.107 token-parse fix |
| **Cast Speed** does nothing | **[OPEN]** (class C) — needs a per-ability component dump |
| **Damage x** only affects first hit on multi-hit chains | **[OPEN]** scale hits only the first spawn |
| **Cooldown x** failed (Frog Vomit, Chain Bolt) | **[RETEST]** v0.71 cooldown enforcer + v0.108 walker changed this path |
| **Summon Units** count clamps oddly (1-2→3, 7+→6) | **[OPEN]** (class E) summon quantization |
| **Charges / Charge Time** not functional | **[EXPECTED]** those abilities have no charge system; v0.73 added a "no charge system" warning — confirm it fires |
| **AoE Radius** not functional | **[RETEST]** v0.108 deep-follow writes `aoe` onto the AoE spawn |
| **Effect Duration** vs applied-effect duration (wants split) | **[PARTIAL]** we have `duration` + `forcetimeout`; the ground-effect-vs-cast split is the open part |
| Requested: **Summon Health / Level modifier** | **[GAP/FEATURE]** not implemented |

**Net: ~4 fixed, ~3 retest, ~4 open, 1 feature request.** A fresh config-test pass on a current build clears most.

---

## PART C — Cross-cutting themes (each hits many abilities)

1. **"Locked in place" / endlag is the #1 complaint.** Dozens of abilities root you for the whole cast. **The
   lever already exists:** `.beelz admin ability <id> freelymove <sec>` (+ `freemove`, `castspeed`). A
   systematic freelymove pass converts most "needs-work-because-locked" verdicts to good — **highest-value
   action, no code change.**
2. **Summons that don't summon** (Wolf Howl, Nun Spawn Minions, Foulrot Ghosts, Grayson/Errol/Rufus
   Reinforcement, Cassius Raise Dead, Octavian Call Adds, Meredith Spawn Minions, Spider Queen Spawn Adds,
   Quincey Reinforcements). **[OPEN]** — the unit-spawner path likely doesn't fire on a player for these; one
   mechanism fix closes ~10. (All `condition=Summon` in our classifier.)
3. **Summon lifecycle/UX** — don't despawn on death (zone-filling); "limit 3/3" blocks reuse until all die.
   **[PARTIAL]** — we have `summontimeout`/`summoncap`; top-up-vs-all-or-nothing is open.
4. **Bar refresh is wonky** — new abilities need a weapon-swap/clearbar to apply; reset doesn't restore vanilla
   spells. **[OPEN]** — a player `.beelz refresh` force-resolve would help.
5. **Transform/invuln exploits** — Beatrice→Gargoyle (i-frames + heal + sun-immune + fly-OOB); Corpse-Pile dig
   (invincible+attack+no-CD); Rat Vanguard (16s invuln + 6 permanent rats). **[GAP]** balance/block pass.
6. **Duplicate variants** — units with 3-4 identical dash/shot variants; testers say "only need one." → our B7
   variant audit + `reviewTag=variant_hard`; collapse/hide the redundant ones.

---

## PART D — Functional baseline (worksheet)

Full per-ability verdict tables: **`TESTER_ABILITY_BASELINE.md`**, grouped by unit. Map each verdict to our
`reviewStatus`: **GOOD→`Approved`**, **TUNE/NEEDS-WORK/REVIEW→`Reviewed`** (+ note), **NOT-USABLE
(broken/crash/stuck)→`Blocked`/`Hidden`**. That populates the functional baseline in our curation framework and
makes "what ships" a checkable state (the dashboard's shippable count).

Units reviewed (A–Z): Albert(ToadKing), Alpha, Bane, Beatrice, Ben, Cassius, Christina(Nun), Clive, Cyril,
Domina, Errol, Finn, Foulrot, Frostmaw, General Elena, Goreswine, Grayson, Grethel, Jade, Keely, Kodia, Kriig,
Leandra, Lidia, Maja, Megara/Morgana, Meredith, Nibbles, Nicholaus, Octavian, Polora, Quincey, Raziel(WIP),
Rufus, Terah, Tristan, Ungora, Ziva. (~38 units, ~250 abilities with explicit verdicts.)

---

## PART E — Prioritized gap-closing plan

1. **Block the Part-A criticals** (~16) in the shipped default — immediate safety (one `ability-set` batch +
   3 name lookups).
2. **Retest Part-B fixed/retest items** on a current build — likely clears ~7 of 12 admin-config issues.
3. **Systematic freelymove pass** on the locked-in-place set (Part C #1) — biggest verdict bucket, no code change.
4. **Summon-doesn't-summon investigation** (Part C #2) — one fix closes ~10 abilities.
5. **Apply GOOD→`Approved`, broken→`Blocked`** — establishes the baseline in `reviewStatus`.
6. **Open code items:** cast-speed, damage-x-multihit, summon-units quantization (per-ability dumps; class C/D/E).
7. **Balance/exploit + duplicate collapse** (Part C #5/#6) — later.
