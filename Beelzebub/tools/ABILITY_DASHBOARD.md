# Ability dashboard — coverage · progress · shippable set (auto-generated)

**1813 abilities total.** Read-only snapshot; re-run `python tools/dashboard.py` to refresh.

## C4 — Shippable (collectible) set

**Rule:** ships as collectible iff `Enabled` (rule) **and** `ReviewStatus` ∉ {Hidden, Blocked} **and** not `metadata.incompatible` **and** not deny-patterned (unless allow-listed).

> Note: with `Curation_EnforceReviewStatus` ON (default, v0.115.0+), `ReviewStatus` Blocked/Hidden IS a real runtime gate — those abilities are excluded from capture + the player catalog, so this rule matches runtime collectibility for the review dimension. It still does not model hard-blocked GUIDs, `Capture_InclusiveMode`, or difficulty gating, so treat the count as a close estimate.

- **Shippable now: 1543 / 1813** (85.1%)
- Of those, **1326 are still `Unreviewed`** — the curation TODO before a final ship.

## C3 — Curation progress

### reviewStatus
| status | count |
|---|---|
| Unreviewed | 1332 |
| Reviewed | 323 |
| Approved | 75 |
| Blocked | 83 |

### reviewTag backlog (tagged for follow-up testing)
| tag | count |
|---|---|
| basic_attack | 105 |
| broken | 56 |
| variant_hard | 53 |
| emote | 21 |
| stuck | 19 |
| reaction | 14 |
| feed | 8 |
| crash | 7 |
| idle_flee | 6 |
| exploit | 4 |
| variant_gateboss | 3 |
| variant_minion | 2 |

## B8 — Descriptive coverage gaps

| field | covered | % | gap |
|---|---|---|---|
| description | 488 | 26.9% | 1325 |
| real category (not Other) | 75 | 4.1% | 1738 |
| condition (classified) | 1146 | 63.2% | 667 |
| icon | 1426 | 78.7% | 387 |
| source NPC | 1004 | 55.4% | 809 |
| source tier | 1000 | 55.2% | 813 |

## B4 — Condition confirmation status

| conditionSource | count |
|---|---|
| auto | 1146 |
| (unset) | 667 |

**1146 auto-classified conditions await confirmation; 0 confirmed.** Use `tools/export_condition_review.py` for the tester checklist.

### condition distribution (classified)
- CloseRange: 617
- Aimed: 297
- Summon: 90
- Movement: 88
- SelfCast: 54