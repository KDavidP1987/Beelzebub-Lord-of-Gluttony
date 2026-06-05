# B5 — Source-unit + tier mapping (auto-generated)

Primary source NPC + that unit's level/VBlood, with a level-derived tier band (`T1<30 · T2 30-46 · T3 47-63 · T4 64+`). Use for "captured from X", per-unit grouping, and tier-gating. Level comes from the unit prefab's `UnitLevel`.

- **Mapped (has source):** 1004 / 1813
- **No source NPC (coverage gap):** 809
- **VBlood-sourced abilities:** 591

## Tier distribution

| tier | abilities |
|---|---|
| T1 | 92 |
| T2 | 146 |
| T3 | 342 |
| T4 | 420 |
| - | 4 |

## Top source units (by ability count)

-  41  Primal Blood Soul
-  36  Dracula the Immortal King
-  24  Lesser Blood Soul
-  24  Simon Belmont the Vampire Hunter
-  20  Adam the Firstborn
-  19  Ziva the Engineer
-  16  Terrorclaw the Ogre
-  15  Ghost Assassin
-  14  General Elena the Hollow
-  14  Talzur the Winged Horror
-  13  Octavian the Militia Captain
-  12  Villager
-  12  Jade the Vampire Hunter
-  11  Finn the Fisherman
-  11  Gorecrusher the Behemoth
-  11  Henry Blackbrew the Doctor
-  11  Cyril the Cursed Smith
-  11  Tristan the Vampire Hunter
-  11  Domina the Blade Dancer
-  11  Frostmaw the Mountain Terror
-  10  Lord Styx the Night Champion
-  10  Solarus the Immaculate
-  10  Terah the Geomancer
-  10  General Valencia the Depraved
-  10  Willfred the Village Elder
-   9  Errol the Stonebreaker
-   9  Quincey the Bandit King
-   9  Slave Master
-   9  General Cassius the Betrayer
-   9  Exsanguinator

## Note on the coverage gap

809 abilities have no `sourceNpcs` in the metadata (many are name-only backfill stubs or shared/sequence prefabs). These show `tier=-`; B8 (coverage report) tracks closing this. Not an error — just unmapped.