# Ability test log — grouped follow-up backlog

The B1 audit tagged abilities that need an **in-game usability decision** by TYPE. This doc is the
living backlog: evaluate a group, record the verdict, then commit the decision into
`Resources/ability_rules.default.json`. Nothing here is blocked yet — every tagged ability is still
capturable; we're deciding what to *keep, hide, or specialise* after testing.

## How the tracking works

- Each backlog ability carries, in `ability_rules.default.json`:
  - `ReviewStatus = "Reviewed"` — audit-triaged, **decision pending**.
  - `ReviewTag = "<type>"` — the group it belongs to.
- Pull a whole group **in-game** via the API: `catalog-ability` / `api info` now emit
  `review_status=` and `review_tag=` (ApiVersion ≥ 25), so BCH/an admin can filter the backlog to,
  say, every `review_tag=emote` and walk them.
- Offline review: `python tools/export_review_csv.py` → `ability_review.csv`, filter the `reviewTag`
  column. Re-tag with `python tools/audit_collectibility.py` + `apply_reviewstatus.py` if the audit
  changes.
- **Recording a verdict** (per ability or per whole group). With `Curation_EnforceReviewStatus` ON
  (default, v0.115.0+), Blocked/Hidden are a real gate — no separate `enabled off` needed:
  - keep & finalise → `.beelz admin ability <id> reviewstatus Approved` (or edit the JSON)
  - remove from collections (junk) → `reviewstatus Hidden` — drops it from capture + the player catalog
  - keep tracked but block (incompatible/unwanted) → `reviewstatus Blocked` — not capturable/grantable
  - Update this table's **Decision** column in the same change.

## Backlog groups (B1, v0.112.0)

Counts are the tagged set; evaluate in the suggested order (simplest/most-impactful first).

| order | reviewTag | count | what they are | suggested test | Decision |
|---|---|---|---|---|---|
| 1 | `emote` | 24 | `AB_Emote_Vampire_*` social emotes | Cast each as a player — do they animate/function? Players want more emotes. | _pending_ |
| 2 | `basic_attack` | 106 | generic `_MeleeAttack_` / primary autos | Are any worth collecting (unique on-hit), or all redundant with weapon basics? | _pending_ |
| 3 | `variant_hard` | 59 | `_Hard_` brutal-difficulty variants of base abilities | Compare to the base ability — unique attributes (more projectiles, extra phase)? collapse vs keep-both. | _pending_ |
| 4 | `reaction` | 14 | Counter / Knockdown reactions | Some may be real CC a player could use; test which fire usefully. | _pending_ |
| 5 | `feed` | 8 | `AB_Feed_*` / `AB_FeedBoss_*` feed lifecycle | Almost certainly non-functional for a player → Hide/Block after confirming. | _pending_ |
| 6 | `idle_flee` | 6 | `AB_Bandit_Shared_Idle_*`, Flee AI behaviours | Non-functional AI behaviours → Hide/Block after confirming. | _pending_ |

**Total backlog: 217.** Full per-ability lists: `tools/AUDIT_COLLECTIBILITY.md` (by category) and the
`reviewTag` column of `ability_review.csv`.

## Related audit outputs (not in this backlog)

- **Combos (B3)** — `tools/AUDIT_COMBOS.md`: 146 multi-cast groups, candidates for the future
  `chainGroup`/`comboParent` chaining work (roadmap A3), not a keep/hide decision.
- **Incompatible (B2)** — `tools/AUDIT_INCOMPATIBLE.md`: the confirmed broken set (5) + 6 phase-trigger
  candidates to verify in-game. Broken-for-a-player is confirmed via play, then recorded as
  `metadata.incompatible` (the lint warns if such an ability is left `Enabled`).
