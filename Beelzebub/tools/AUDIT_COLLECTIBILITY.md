# B1 — Collectibility / junk audit (auto-generated, source=auto)

Deterministic partition of the shipped ability universe. **Verify before applying** — this proposes `Hidden` only for HIGH-confidence definitional junk; everything else is for manual review. Summon abilities are guarded (never junked).

- **Total abilities:** 1813
- **Collectible (real):** 1596
- **Junk (flagged):** 217  — of which **38 propose `Hidden`** (HIGH-confidence)
- **Summon-guard rescues:** 3 (name looked like junk, but the ability summons a unit → kept)

## Category distribution

| category | count | auto-hide? |
|---|---|---|
| collectible | 1593 | — |
| basic_attack | 106 | manual review |
| variant | 59 | manual review |
| emote | 24 | YES (Hidden) |
| reaction | 14 | manual review |
| feed | 8 | YES (Hidden) |
| idle_flee | 6 | YES (Hidden) |
| rescued_summon | 3 | — |

## Junk confidence

- MEDIUM: 179
- HIGH: 38

## Samples per category (verify these against the source unit's kit)

### emote  (24)
- `AB_Emote_Vampire_Beckon_AbilityGroup` — Beckon → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Bow_AbilityGroup` — Bow → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Clap_AbilityGroup` — Clap → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Cry_AbilityGroup` — Yes → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_DanceSingle01_AbilityGroup` — Yes → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_DanceSingle02_AbilityGroup` — Yes → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Laugh_AbilityGroup` — Yes → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_No_AbilityGroup` — No → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Point_AbilityGroup` — Point → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Salute_AbilityGroup` — Salute → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Shrug_AbilityGroup` — Shrug → Hidden  <sub>AB_Emote_ prefix</sub>
- `AB_Emote_Vampire_Sit_AbilityGroup` — Сесть → Hidden  <sub>AB_Emote_ prefix</sub>

### idle_flee  (6)
- `AB_Bandit_Shared_Idle_Group_Pushups` — Demote to Member → Hidden  <sub>name token "Idle"; matches shipped DenyPattern _Idle_</sub>
- `AB_Bandit_Shared_Idle_Group_Situps` — Demote to Member → Hidden  <sub>name token "Idle"; matches shipped DenyPattern _Idle_</sub>
- `AB_Bandit_Shared_Idle_Group_Tinker` — Demote to Member → Hidden  <sub>name token "Idle"; matches shipped DenyPattern _Idle_</sub>
- `AB_Bandit_Shared_Idle_Group_Whittling` — Demote to Member → Hidden  <sub>name token "Idle"; matches shipped DenyPattern _Idle_</sub>
- `AB_Bandit_Worker_Shared_Flee_AbilityGroup` — Bandit Worker Shared Flee → Hidden  <sub>name token "Flee"; matches shipped DenyPattern _Flee_</sub>
- `AB_Undead_Priest_Elite_Idle_AbilityGroup` — Undead Priest Elite Idle → Hidden  <sub>name token "Idle"; matches shipped DenyPattern _Idle_</sub>

### feed  (8)
- `AB_Feed_01_Initiate_AbilityGroup` — Feed → Hidden  <sub>name token "Feed"</sub>
- `AB_Feed_02_Bite_Abort_AbilityGroup` — Feed → Hidden  <sub>name token "Feed"</sub>
- `AB_Feed_03_Complete_AbilityGroup` — Feed → Hidden  <sub>name token "Feed"</sub>
- `AB_FeedBoss_01_Initiate_AbilityGroup` — Hold to Extract Blood → Hidden  <sub>name token "FeedBoss"; matches shipped DenyPattern _FeedBoss_</sub>
- `AB_FeedBoss_03_Complete_AbilityGroup` — Feed → Hidden  <sub>name token "FeedBoss"; matches shipped DenyPattern _FeedBoss_</sub>
- `AB_FeedBoss_FeedOnDracula_01_Initiate_AbilityGroup` — 長押しして血を採る → Hidden  <sub>name token "FeedBoss"; matches shipped DenyPattern _FeedBoss_</sub>
- `AB_FeedBoss_FeedOnDracula_03_Complete_AbilityGroup` — Feed → Hidden  <sub>name token "FeedBoss"; matches shipped DenyPattern _FeedBoss_</sub>
- `AB_WerewolfChieftain_Feed_AbilityGroup` — プライマリー攻撃 → Hidden  <sub>name token "Feed"</sub>

### reaction  (14)
- `AB_Bandit_Thief_Counter_AbilityGroup` — Shadow Meld (review)  <sub>name token "Counter"; matches shipped DenyPattern _Counter_</sub>
- `AB_Nun_Counter_AbilityGroup` — Shadow Meld (review)  <sub>name token "Counter"; matches shipped DenyPattern _Counter_</sub>
- `AB_Scarecrow_Counter_AbilityGroup` — Shadow Meld (review)  <sub>name token "Counter"; matches shipped DenyPattern _Counter_</sub>
- `AB_SlaveMaster_Knockdown_AbilityGroup` — Primary Attack (review)  <sub>name token "Knockdown"</sub>
- `AB_WerewolfChieftain_Knockdown_AbilityGroup` — Werewolf Chieftain Knockdown (review)  <sub>name token "Knockdown"</sub>
- `AB_Bandit_Mugger_Block_AbilityGroup` — Bandit Mugger Block (review)  <sub>name token "Block"; matches shipped DenyPattern _Block_</sub>
- `AB_ChurchOfLight_CardinalAide_Block_AbilityGroup` — Church Of Light Cardinal Aide Block (review)  <sub>name token "Block"; matches shipped DenyPattern _Block_</sub>
- `AB_ChurchOfLight_Cleric_Block_AbilityGroup` — Church Of Light Cleric Block (review)  <sub>name token "Block"; matches shipped DenyPattern _Block_</sub>
- `AB_Devoted_Block_AbilityGroup` — Devoted Block (review)  <sub>name token "Block"; matches shipped DenyPattern _Block_</sub>
- `AB_Legion_Guardian_Block_AbilityGroup` — Legion Guardian Block (review)  <sub>name token "Block"; matches shipped DenyPattern _Block_</sub>
- `AB_Manticore_Stagger_AbilityGroup` — Manticore Stagger (review)  <sub>name token "Stagger"</sub>
- `AB_Militia_Guard_Block_AbilityGroup` — Militia Guard Block (review)  <sub>name token "Block"; matches shipped DenyPattern _Block_</sub>

### basic_attack  (106)
- `AB_Bandit_Mugger_MeleeAttack_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bandit_Stalker_MeleeAttack_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bandit_Stalker_VBlood_MeleeAttack_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bandit_Thug_MeleeAttack_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bandit_Worker_Gatherer_MeleeAttack_AbilityGroup` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bandit_Worker_Shared_MeleeAttack_AbilityGroup` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_BatVampire_MeleeAttack_AbilityGroup` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bear_Dire_MeleeAttack_AbilityGroup` — Основная атака (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bear_Dire_MeleeAttack_AbilityGroup_Hasted` — Attacco primario (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bear_MeleeAttack_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bear_MeleeAttack_Large_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>
- `AB_Bear_Mutant_MeleeAttack_Group` — Primary Attack (review)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_</sub>

### variant  (59)
- `AB_Bandit_Deadeye_Chaosbarrage_Hard_Group` — Bandit Deadeye Chaosbarrage Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Deadeye_ChaosNuke_Hard_Group` — Bandit Deadeye Chaos Nuke Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Deadeye_Chaosstorm_Hard_Group` — Bandit Deadeye Chaosstorm Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Fisherman_FeedSerpentLineup_Hard_AbilityGroup` — Bandit Fisherman Feed Serpent Lineup Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Fisherman_SerpentFeed_Hard_AbilityGroup` — Bandit Fisherman Serpent Feed Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Foreman_Crossbow_Hard_AbilityGroup` — Bandit Foreman Crossbow Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Foreman_Crossbow_Hard_BloodRage_AbilityGroup` — Bandit Foreman Crossbow Hard Blood Rage (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Foreman_RapidShot_Hard_AbilityGroup` — Bandit Foreman Rapid Shot Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Foreman_RapidShot_Init_Hard_AbilityGroup` — Camouflage (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_FrostArrow_RainOfArrows_Hard_AbilityGroup` — Bandit Frost Arrow Rain Of Arrows Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_FrosttArrow_MultiShot_Hard_Group` — Bandit Frostt Arrow Multi Shot Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>
- `AB_Bandit_Stalker_VBlood_Projectile_Hard_Group` — Bandit Stalker VBlood Projectile Hard (review)  <sub>name token "Hard"; matches shipped DenyPattern _Hard_</sub>

### rescued_summon  (3)
- `AB_Unholy_FallenAngel_MeleeAttack_AbilityGroup` — Primary Attack (rescued)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_; SUMMON-GUARD: condition=Summon</sub>
- `AB_Vampire_Dracula_Feed_AbilityGroup` — Arctic Leap (rescued)  <sub>name token "Feed"; SUMMON-GUARD: condition=Summon</sub>
- `AB_Vampire_Claws_Primary_MeleeAttack_Unholy_AbilityGroup` — Vampire Claws Primary Melee Attack Unholy (rescued)  <sub>name token "MeleeAttack"; matches shipped DenyPattern _MeleeAttack_; SUMMON-GUARD: condition=Summon</sub>
