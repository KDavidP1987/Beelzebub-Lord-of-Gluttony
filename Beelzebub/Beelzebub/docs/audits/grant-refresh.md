# Audit — grant-refresh (v0.137.1)

Defect fix, no dod plan: the owner asked for speed (2026-10-03) and the fix was built diagnostic-first (CLAUDE.md
development procedure 4). Bug: a `.beelz grant` was saved but the live bar kept the old ability, and it would not
cast, until a weapon swap or unequip.

## Pre-audit
### Diagnostic · 2026-10-03 · 9ce8b23
- Added `[Beelz GRANTRAW]` slot reads before, after, next frame and 2 s after a grant, plus the admin `grant-push` probe.
- In-game result: the slot kept resolving the weapon's Whirlwind next frame and 2 s later, even though our row sat on
  the held `EquipBuff_Weapon_*`. `ReplaceAbilityOnSlotSystem` reads a buff's rows only when the buff spawns. A
  `ModifyAbilityGroupOnSlot` push (probe) showed the ability at once. The owner confirmed both, steps 2–5.
- The engine applies the rows at equip time under another source entity (`581913:259`, no prefab) than the held
  buff (`581914:252`).
- git status: clean apart from the owner's `V0136_ABILITY_TEST_PLAN.xlsx` / `_matrix_build.py` (never staged).

## Post-audit
### Fix · 2026-10-03 · 59957bb, 55179a7, 8709783, acdb64f (round 3), diagnostic removed ed7a09c
- Fix: `SlotApply.ApplyGrant` / `RestoreResolvedGrants` also push the slot live, with the held equip buff as the
  source (`PushLive` → `PushOnSlot`).
  - First they pop the slot's earlier mods from that buff (`GrantPush.OwnModIds`), plus, on unslot, yield, lock or
    re-grant, every mod that sets the removed or replaced ability (`GrantPush.ModsToPop`).
  - They never pop a mod owned by a spellbook buff or by another live prefab buff, i.e. a form, transform or mount
    (`TransformBuffService.KeepForeignMods`, `GrantPush.OwnedByOtherBuff`).
  - `RestoreSlotBaseValue` restores the weapon's highest-priority prefab row, else the base, and never pushes Empty.
  - Nothing is pushed while a form, transform or mount owns the bar (`BarOwnedElsewhere`).
- compile: 0 errors. tests: 167 passed.
- planted faults (each broke the code once, the test failed, restored):
  - `OwnModIds_fails_when_a_mod_from_another_source_is_popped`
  - `…_recycled_buff_index_with_another_version_is_popped`
  - `…_repeated_mod_id_is_popped_twice`
  - `…_unreadable_dump_yields_an_id`
  - `ShouldPush_fails_when_an_Empty_base_is_pushed`
  - `ModsToPop_fails_when_an_equip_time_grant_mod_survives_an_unslot`
  - `ModsToPop_fails_when_a_spellbook_mod_or_an_unrelated_mod_is_popped`
  - `ModsToPop_fails_when_an_unreadable_dump_yields_an_id`
  - `WeaponRow_fails_when_a_lower_priority_or_other_slot_row_wins`
  - `OwnedByOtherBuff_fails_when_a_form_buffs_mod_is_popped_by_ability` (fault: `OwnedByOtherBuff` → false; 1 failed)
  - `ModsToPop_fails_when_a_replaced_equip_time_grant_survives_a_regrant` (fault: drop the by-ability clause; 3 failed)
- review round 1 (fresh-context subagent), all ACCEPTED, fixed in 55179a7:
  - F1: an equip-time grant mod survived an unslot.
  - F2: a restore pushed onto a form bar.
  - F3: a vanilla mount was not detected.
  - F4: the first weapon row was used instead of the highest-priority one.
- Codex round 1: the sandbox blocked every command, so it read nothing. Its "FINDINGS" verdict was discarded (dod
  rule: no verdict without reading). It was re-run with the diff on stdin.
- Codex round 2 — FINDINGS, both ACCEPTED (8709783):
  1. A by-ability pop during an unslot in a form removed a form or mount mod that sets the same ability. Fix: keep
     mods sourced by any other live prefab buff.
  2. A re-grant A→B left A's equip-time mod under B, so A resurfaced after B was unslotted. Fix: the grant and
     re-resolve paths pop the replaced row's ability.
- Codex round 3 — FINDINGS, 1 ACCEPTED (acdb64f): yield and lock passed the bind's ability instead of the
  one on the row being removed (a stale A). Fix: capture `SlotRowAbility` before `RemoveSlotEntries`. Round cap
  (3) reached; the fix mirrors the round-2 pattern and was not re-reviewed.
- Codex verdict: FINDINGS (round 3) — all findings ACCEPTED and fixed; no open finding.
- in-game (round-1 build, 2026-10-03, owner):
  - Steps 2–9 passed: a grant shows and casts at once; a re-grant switches; unslot restores the vanilla spell or
    weapon skill (`popped=2` on the equip-time unslot); universal grants persist across weapon swaps and unarmed;
    `resetbar` reverts the bar.
  - In Wolf form the log shows `skipped=bar-owned-elsewhere`.
- log check (that session): `PREFLIGHT OK (2 checks)`. LogOutput had 0 errors and 2 known TUNE warnings. NyarDev
  had 4 `Couldn't remap old Modification Id` errors (backlog `modid-remap-errors`). `check_bar_reset.py session`
  FAILS by design: that session was not the bar-reset script.
- in-game (rounds 2–3 build, 2026-10-03, owner): steps 1–7 PASS — a re-grant after a weapon swap, then an
  unslot, gives Whirlwind back (Codex round-2 finding 2 confirmed fixed). Casting a non-Wolf ability leaves Wolf form,
  as in vanilla. Step 8 FAIL (minor): `.beelz unslot 1` while a wolf leaves Q blank (`why=restore set=none popped=2`),
  and it stays blank after the form exit (re-applied 0 grants) until a weapon re-equip. Owner decision (plan mode):
  ship v0.137.1, then fix as v0.137.2 under the dod plan `form-bar-edits` (backlog `unslot-in-form` + `grant-in-form`),
  diagnostic first. Step 10 reset clean.
- Known limitations (backlog):
  - `grant-in-form`: a grant made while in a form shows only after a weapon swap.
  - A follow-up `RestoreResolvedGrants` after each grant re-pushes the same abilities. Harmless; one extra pop and
    push per bound slot.
- Release tooling: `check_bar_reset.py` paths/data checks are bounded at the v0.137.0 release commit (c3b1872), so
  later features do not trip the shipped plan. Fault: with the bound off, the live repo fails `paths` (undeclared
  `GrantPush.cs`, `GrantPushTests.cs`) and `data` (`[Beelz GRANT]` with no row). Bounded, both are ok; selftest
  stays at 41 cases ok.
