# B6 — Weapon-family classification review (auto-generated)

Replicates the `WeaponFamilyClassifier` name heuristic and flags SUSPECT concrete-weapon gates (a weapon token matched as an embedded substring, not a real weapon tag). Pin a wrong one with a per-ability `Weapons` override in `ability_rules.default.json`.

## Family distribution (heuristic output over the full set)

| family | count |
|---|---|
| Magic | 1680 |
| Crossbow | 18 |
| Whip | 16 |
| Sword | 16 |
| Spear | 16 |
| Longbow | 8 |
| Mace | 8 |
| Axe | 7 |
| Slashers | 7 |
| Claws | 7 |
| Pistols | 6 |
| Pollaxe | 5 |
| TwinBlades | 5 |
| GreatSword | 4 |
| Reaper | 4 |
| Daggers | 4 |
| Unarmed | 2 |

## SUSPECT concrete-weapon gates (29) — verify / pin

- `AB_CastleMan_FlameWhip_AbilityGroup` → **Whip** (matched `Whip`)  <sub>Primary Attack</sub>
- `AB_CastleMan_FlameWhip_Hard_AbilityGroup` → **Whip** (matched `Whip`)  <sub>Primary Attack</sub>
- `AB_CastleMan_FlameWhip_SingleAttack_AbilityGroup` → **Whip** (matched `Whip`)  <sub>Primary Attack</sub>
- `AB_ChurchOfLight_SlaveMaster_KnockbackWhip_AbilityGroup` → **Whip** (matched `Whip`)  <sub>Church Of Light Slave Master Knockback Whip</sub>
- `AB_ChurchOfLight_SlaveMaster_VisualSlaveWhip_AbilityGroup` → **Whip** (matched `Whip`)  <sub>Church Of Light Slave Master Visual Slave Whip</sub>
- `AB_HighLordSword_SelfStun_AbilityGroup` → **Sword** (matched `Sword`)  <sub>Corrupted Skull</sub>
- `AB_Illusion_WraithSpear_AbilityGroup` → **Spear** (matched `Spear`)  <sub>Wraith Spear</sub>
- `AB_Interact_ThrowSword_AbilityGroup` → **Sword** (matched `Sword`)  <sub>ปาดาบ</sub>
- `AB_Paladin_DivineAngel_HolySpearField_AbilityGroup` → **Spear** (matched `Spear`)  <sub>Primary Attack</sub>
- `AB_Undead_CursedSmith_FloatingAxes_XStrikeXStrike_Toss_AbilityGroup` → **Axe** (matched `Axe`)  <sub>X-Strike</sub>
- `AB_Undead_CursedSmith_FloatingMace_HammerSlam_AbilityGroup` → **Mace** (matched `Mace`)  <sub>Arctic Leap</sub>
- `AB_Undead_CursedSmith_FloatingMace_MeleeAttack_AbilityGroup` → **Mace** (matched `Mace`)  <sub>Primary Attack</sub>
- `AB_Undead_CursedSmith_FloatingSlashers_MeleeAttack_AbilityGroup` → **Slashers** (matched `Slashers`)  <sub>Primary Attack</sub>
- `AB_Undead_CursedSmith_FloatingSpear_MeleeAttack_AbilityGroup` → **Spear** (matched `Spear`)  <sub>Primary Attack</sub>
- `AB_Undead_CursedSmith_FloatingSword_DashAttack_AbilityGroup` → **Sword** (matched `Sword`)  <sub>Frenzy</sub>
- `AB_Undead_CursedSmith_FloatingSword_MeleeAttack_AbilityGroup` → **Sword** (matched `Sword`)  <sub>Primary Attack</sub>
- `AB_Undead_CursedSmith_Summon_WeaponAxe_AbilityGroup` → **Axe** (matched `Axe`)  <sub>Arctic Leap</sub>
- `AB_Undead_CursedSmith_Summon_WeaponMace_AbilityGroup` → **Mace** (matched `Mace`)  <sub>Arctic Leap</sub>
- `AB_Undead_CursedSmith_Summon_WeaponSlashers_AbilityGroup` → **Slashers** (matched `Slashers`)  <sub>Arctic Leap</sub>
- `AB_Undead_CursedSmith_Summon_WeaponSpear_AbilityGroup` → **Spear** (matched `Spear`)  <sub>Arctic Leap</sub>
- `AB_Undead_CursedSmith_Summon_WeaponSword_AbilityGroup` → **Sword** (matched `Sword`)  <sub>Arctic Leap</sub>
- `AB_VHunter_CastleMan_KnockbackWhip_AbilityGroup` → **Whip** (matched `Whip`)  <sub>VHunter Castle Man Knockback Whip</sub>
- `AB_VHunter_CastleMan_KnockbackWhip_Fire_AbilityGroup` → **Whip** (matched `Whip`)  <sub>VHunter Castle Man Knockback Whip Fire</sub>
- `AB_Vampire_BloodKnight_BasicSpearDance_AbilityGroup` → **Spear** (matched `Spear`)  <sub>Primary Attack</sub>
- `AB_Vampire_BloodKnight_FieldOfSpears_AbilityGroup` → **Spear** (matched `Spear`)  <sub>Primary Attack</sub>
- `AB_Vampire_BloodKnight_HookingSpear_Abilitygroup` → **Spear** (matched `Spear`)  <sub>Vampire Blood Knight Hooking Spear Abilitygroup</sub>
- `AB_Vampire_BloodKnight_ThousandSpears_AbilityGroup` → **Spear** (matched `Spear`)  <sub>A Thousand Spears</sub>
- `AB_Vampire_Dracula_EtherialSword_Abilitygroup` → **Sword** (matched `Sword`)  <sub>Vampire Dracula Etherial Sword Abilitygroup</sub>
- `AB_Vampire_DualHammers_StormMace_AbilityGroup` → **Mace** (matched `Mace`)  <sub>Vampire Dual Hammers Storm Mace</sub>