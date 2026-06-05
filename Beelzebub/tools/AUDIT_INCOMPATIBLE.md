# B2 — Broken / incompatible re-audit (auto-generated)

**Finding:** broken-for-a-player is almost never statically detectable. Every ability in the dump is structurally castable (0 of 1,813 have no cast steps), and the only reliably-detectable broken class (NPC anim-rig teleports) is already covered by the shipped `incompatible` set. The roadmap's other sub-types (team-filtered / corpse-required / AI-only) have no prefab fingerprint that separates them from working *conditional* abilities — so flagging them statically would reproduce the Erwin false-positive. **Nothing here is auto-applied; candidates are for in-game verification.**

## Confirmed incompatible (5) — the shipped baseline

- `AB_Undead_BishopOfShadows_EternalDarkness_AbilityGroup` — Caminhada de Névoa  <sub>AnimRig: phase ability bound to NPC animation rig</sub>
- `AB_Undead_BishopOfShadows_ShadowStep_AbilityGroup` — Mist Walk  <sub>AnimRig: phase ability bound to NPC animation rig</sub>
- `AB_Undead_Infiltrator_ShadowStep_AbilityGroup` — Undead Infiltrator Shadow Step  <sub>AnimRig: phase ability bound to NPC animation rig</sub>
- `AB_Undead_Priest_Elite_Teleport_Travel_AbilityGroup` — Mist Walk  <sub>AnimRig: travel/dash sequence bound to NPC animation rig</sub>
- `AB_Undead_ShadowSoldier_GateBoss_ShadowStep_AbilityGroup` — Mist Walk  <sub>AnimRig: phase ability bound to NPC animation rig</sub>

## Candidates: anim-rig teleport family (0) — VERIFY in-game before blocking

Same shape as the confirmed set (NPC ShadowStep / Mist-Walk / teleport-travel). Likely rig-bound, but confirm — some NPC teleports DO work for a player.


## Candidates: boss phase-transition triggers (6) — likely internal, VERIFY

Phase-change logic gates, not signature abilities — usually do nothing useful when a player casts them. Confirm, then `Hidden`/`Blocked` as appropriate.

- `AB_VHunter_CastleMan_WhipAoEChangePhase_AbilityGroup` — VHunter Castle Man Whip AoEChange Phase
- `AB_Blackfang_Valyr_PhaseDual_BomberDuelQuake_AbilityGroup` — Blackfang Valyr Phase Dual Bomber Duel Quake
- `AB_Blackfang_Valyr_PhaseDual_ChaseTargetBuff_AbilityGroup` — Blackfang Valyr Phase Dual Chase Target Buff
- `AB_Blackfang_Valyr_PhaseDual_MeleeAttack_AbilityGroup` — Blackfang Valyr Phase Dual Melee Attack
- `AB_Blackfang_Valyr_PhaseDual_SeismicJump_AbilityGroup` — Blackfang Valyr Phase Dual Seismic Jump
- `AB_Blackfang_Valyr_PhaseDual_Tackle_AbilityGroup` — Blackfang Valyr Phase Dual Tackle

## How broken-detection actually proceeds

1. Tester reports + the B4 condition-confirmation pass surface genuinely-broken abilities in play.
2. Each is recorded in `ability_metadata.json` as `incompatible` + reason (the authoritative broken record).
3. The lint warns if an `incompatible` ability is still `Enabled`. This audit just keeps the candidate frontier visible.