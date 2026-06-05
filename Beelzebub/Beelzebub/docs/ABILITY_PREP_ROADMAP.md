# Holistic ability-prep roadmap (systematic groundwork before individual curation)

**Goal:** get the ~1,813-ability dataset to a state where individual editing/fixing/blocking/chaining is
**efficient, complete, and tracked** — starting from a triaged baseline with a clear "what ships" boundary,
not from a blank slate. This is the framework + audits + tooling; the per-ability work comes *after*.

Status legend: ⬜ not started · 🟦 in progress · ✅ done.

---

## A. Schema / fields — the curation framework  (do first; small, high-leverage)

✅ **A1. `reviewStatus` per ability** — `Unreviewed | Reviewed | Approved | Blocked | Hidden`. Tracks where
each ability is in *your* curation process (orthogonal to what the ability *is*). Lets you answer "how many
left?" and drives the shippable-set rule. Lives in `ability_rules.default.json` (it's a curation decision).
**DONE (v0.111.0):** `AbilityEntry.ReviewStatus` (default `Unreviewed`), settable via `.beelz admin ability
<id> reviewstatus <val>`, shown in the per-ability dump, preserved by `defaults` reset + GUID-merge, and
serialized into the shipped default. Canonical list = `AbilityRules.ReviewStatuses`.

✅ **A2. Unified "usability" rollup (computed, not stored)** — today usability is fragmented across
`incompatible` (broken), `condition` (conditional), `enabled` (toggle), and deny-patterns (junk). Don't add a
5th overlapping field — instead **derive** a single `usability` view in the review export:
`Working · Conditional · Broken · Combo · Junk · Untested`. One place to reason about readiness.
**DONE (v0.111.0):** computed in `tools/export_review_csv.py` (precedence Junk > Broken > Combo > Conditional
> Working); `reviewStatus` stays its own orthogonal column (an ability can be Working *and* Unreviewed —
"Untested" = `reviewStatus=Unreviewed`). First run: Conditional 854 · Working 675 · Junk 191 · Combo 87 · Broken 6.

⬜ **A3. `chainGroup` / `comboParent`** — groups multi-cast combo pieces so they can be **chained** onto one
slot or the non-standalone pieces **hidden**. Directly enables the "chaining" goal. (Populated by audit B3.)

⬜ **A4. `effectTags` (optional)** — `damage/heal/cc/buff/debuff/summon/mobility`. Lightweight filter/balance
axis beyond `category`. Add only if it earns its keep in review.

---

## B. Systematic audits — populate the baseline  (the big value)

✅ **B1. Collectibility / junk audit** ★ — partition all 1,813 into real player-collectibles vs noise
(emotes, internal/debug, feed-prep, lifecycle, sequence-only, dead/cut variants). Defines the *universe*;
auto-sets junk → `Blocked/Hidden`. Deny-patterns cover some; this is the thorough one-pass triage.
**DONE:** `tools/audit_collectibility.py` → `AUDIT_COLLECTIBILITY.md` + json. **Collectible 1,596 · junk 217.**
Uses underscore-BOUNDED tokens + a SUMMON-GUARD (never junks a real summon — rescued RaiseDead/SummonTail).
A structural "no-payload" heuristic was tried and DROPPED (false-positived Chaosbarrage/LightningArc).
**APPLIED (v0.112.0):** per KDPen's decision, nothing was blocked — instead all **217** flagged abilities
(emote 24, feed 8, idle/flee 6, variant_hard 59, basic_attack 106, reaction 14) were TAGGED in the shipped
default with `ReviewStatus=Reviewed` + `ReviewTag=<type>` (`tools/apply_reviewstatus.py --write`). That pairing
is the test-log backlog (`docs/ABILITY_TEST_LOG.md`); the tags stream over the API (`review_tag=`, ApiVersion
25) so groups can be pulled in-game and evaluated for usability (emotes first). NEW field A3-adjacent
`ReviewTag` added (B1 used canonical tags). All still capturable — decisions (keep/Hide/Block) recorded per
group after in-game testing.

✅ **B2. Broken / incompatible re-audit** ★ — systematically flag abilities that genuinely don't translate to
a player (anim-rig-bound, team-filtered, corpse-required, AI-only). The shipped `incompatible` set is only
**5** (from an old chain audit) — expand it with reasons. Deterministic prefab-fingerprint pass like the
condition classifier, then verify (avoid false positives — many "broken" reports were really *conditional*).
**DONE:** `tools/audit_incompatible.py` → `AUDIT_INCOMPATIBLE.md`. **Key finding: broken-for-a-player is NOT
statically detectable** — every ability is structurally castable (0 of 1,813 lack casts), and the only reliable
broken class (anim-rig teleports) is already fully covered by the 5 (0 new). 6 boss phase-transition triggers
flagged as verify-candidates. Comprehensive broken-detection runs through tester reports → `metadata.incompatible`
(the lint already warns on incompatible+enabled), not static analysis. Nothing auto-applied.

✅ **B3. Combo / sequence audit** ★ — identify multi-cast groups (`AbilityGroupStartAbilitiesBuffer` > 1) and
AI-rotation pieces that can't work standalone. Feeds chaining + hiding (A3). The condition classifier already
flags `Combo`; this is the dedicated, verified pass.
**DONE:** `tools/audit_combos.py` → `AUDIT_COMBOS.md` + json. **146 multi-cast combo groups** (2-cast 80,
3-cast 53, 4-cast 9, plus 5/6/10/12-cast outliers). Precise signal (cast-step count), verified against
CrystalLance/FlameWhip/TrippleSpinningCross. Directly feeds A3 `chainGroup`/`comboParent`.

🟦 **B4. Condition confirmation pass** — promote the 1,146 `auto` conditions → `confirmed` via tester
verification (framework exists). Needs a tracked plan + a tester-facing export of the unconfirmed set.
**TOOLING DONE (v0.114.0):** `tools/export_condition_review.py` → `CONDITION_REVIEW_CHECKLIST.md` (1,146
unconfirmed, grouped by source unit) + `condition_review.csv`. In-game confirm: `.beelz admin ability <id>
condition <value>` writes `conditionSource=confirmed` to the override file. **The actual confirming is the
ongoing tester activity** (0 confirmed so far) — fold confirmed overrides back into the shipped metadata
before packaging.

✅ **B5. Source-unit + tier mapping** — map each ability to its source unit + difficulty tier
(Basic/Brutal/shard-boss). Enables accurate "captured from X," per-unit grouping for balance, and tier-gating.
**DONE (v0.113.0):** `tools/audit_source_tier.py` → `AUDIT_SOURCE_TIER.md`. Maps the primary source NPC's
`UnitLevel` + VBlood flag → level-derived tier band (T1<30·T2 30-46·T3 47-63·T4 64+). **1,004/1,813 mapped**
(591 VBlood; tiers T1 92·T2 146·T3 342·T4 420). **Coverage gap: 809 have no sourceNpc** → tracked for B8.
Optional next: surface tier in metadata + on the API (pending decision).

✅ **B6. Weapon-family classification review** — the family inference is a name heuristic (affects slot
resolution). Audit for mis-classification; pin the wrong ones via `WeaponFamilyMap`/per-ability `Weapons`.
**DONE (v0.113.0):** `tools/audit_weapon_family.py` replicates `WeaponFamilyClassifier` and flags suspect
gates. **Caught + FIXED a real bug:** Pollaxe/TwinBlades/Slashers were missing from the classifier →
mis-gated (Pollaxe→Axe; TwinBlades/Slashers→Magic). Added their tokens (before the generic Axe/_Slash_).
29 remaining suspects are boss abilities thematically named after weapons (FlameWhip, BloodKnight spears,
CursedSmith floating weapons) — a gate-vs-universal **design decision pending KDPen**, not a bug.

✅ **B7. Duplicate / variant audit** — `_Hard` variants, `_01/_02`, skin variants: decide collapse vs
keep-both vs hide, so collections aren't cluttered with near-duplicates.
**DONE (v0.113.0):** `tools/audit_variants.py` → `AUDIT_VARIANTS.md`. **48 base↔variant pairs**: hard 43
(of B1's 59 `variant_hard` — the other 16 have no base = standalone phase moves), gateboss 3, minion 2.
The `_Hard_` set is already tagged; gateboss/minion (5) are hide candidates pending review.

✅ **B8. Coverage report** — quantify the gaps: descriptions (~13%), categories (`Other` ~39%), conditions
(606 `Unclassified`), icons, source NPCs. Tells you how much descriptive curation remains.
**DONE (v0.114.0):** in `tools/dashboard.py` → `ABILITY_DASHBOARD.md`. **Real numbers: description 26.9% ·
metadata-category (not Other) 4.1% (!) · condition 63.2% · source NPC 55.4% · source tier 55.2%.** The
4.1% is the descriptive `categories` field (almost all `["Other"]`) — distinct from the heuristic wire
`cat=` badge. Big descriptive-curation gaps remain (B-series didn't touch descriptions).

---

## C. Tooling — make curation efficient + safe

✅ **C1. CSV review export** ★ — read-only join of `ability_metadata.json` + `ability_rules.default.json` by
GUID → one sortable/filterable sheet (the bulk-review surface; edits still happen in JSON).
**DONE (v0.111.0):** `tools/export_review_csv.py` → `tools/ability_review.csv` (UTF-8-BOM, Excel-ready, 16
columns incl. the A2 `usability` rollup). Joins via `Resources/prefab_names.tsv`. Read-only.

✅ **C2. Validation / lint tool** ★ — catch inconsistencies before every package: `incompatible` + `enabled`,
a weapon/form lock to an invalid token, a rule for a non-existent ability (orphan), `condition` vs `type`
mismatch, duplicate keys. Run in preflight.
**DONE (v0.111.0):** `tools/lint_ability_data.py` — ERRORs (orphan rule, invalid weapon/form/status/category/
difficulty, bad phase, duplicate key) exit 1 to gate preflight; WARNINGs (broken+enabled, dangling condition)
are advisory. Negative-tested. Mirrors the C# token sets — keep in sync if the enums change.

✅ **C3. Coverage / progress dashboard** — counts per `reviewStatus` / usability / condition / category;
"% reviewed", "what's left", "current shippable set size".
**DONE (v0.114.0):** `tools/dashboard.py` → `ABILITY_DASHBOARD.md` (B8 coverage + C3 progress + C4 shippable
in one report). Current: reviewStatus Unreviewed 1590 · Reviewed 222 · Blocked 1; tag backlog by type;
conditions auto 1146 / confirmed 0.

✅ **C4. "Shippable set" definition** — the explicit rule for what's IN the curated default (e.g. `enabled &&
!junk && reviewStatus >= Reviewed`) + a report of the current set, so "ready to ship" is a checkable state.
**DONE (v0.114.0):** rule = `Enabled` ∧ `ReviewStatus` ∉ {Hidden, Blocked} ∧ ¬`incompatible` ∧ ¬deny-patterned
(unless allow-listed). **Current shippable: 1,616 / 1,813 (89.1%)**, of which 1,580 still `Unreviewed` (the
curation TODO). Computed in `dashboard.py`; the rule lives there + here.

---

## Recommended order
1. ✅ **A1 + C1 + C2** (+ A2 folded into C1) — the framework + review/lint tools (DONE v0.111.0).
2. ✅ **B1 + B2 + B3** — the triage audits that set the baseline and define the shippable boundary (junk out,
   broken flagged, combos grouped). **DONE** — pending KDPen's apply decision on the B1 Hidden set.
3. ✅ **B5 + B6 + B7** — refinement (source/tier, weapon families, variants). **DONE v0.113.0** (B6 caught + fixed the Pollaxe/TwinBlades/Slashers mis-gate).
4. 🟦 **B4** — condition confirmation: tooling DONE (v0.114.0); confirming is the ongoing tester activity.
5. ✅ **B8 + C3 + C4** — coverage + dashboard + shippable-set gate (DONE v0.114.0; `ABILITY_DASHBOARD.md`).
6. ← **NEXT: individual editing/fixing/blocking/chaining** — the groundwork is complete; the set is triaged,
   tagged, tier-mapped, weapon-correct, and tracked. Remaining backlogs: the B1 review-tag test groups
   (emotes/variants/etc.), B4 condition confirmation, and the descriptive-curation gaps from B8.

Each audit follows the project's exactness discipline: deterministic prefab-data pass → verify flagged
results against the unit's kit (avoid false positives) → record with a `source`/confidence so nothing is
treated as fact until confirmed. See `docs/ABILITY_CHANGE_IMPACT.md` + `docs/ADMIN_COMMANDS_AUDIT.md`.
