# Per-ability tester baseline (v0.100.0) — worksheet

Every ability the testers reviewed, with their verdict + finding. Source: `Reference Data/beelz-vblood`.
Verdict legend: **GOOD** (✅ ship as-is) · **TUNE** (✅🔧) · **REVIEW** (❓ / ✅❓ keep?) · **WORKS-NOT-USABLE**
(✅❌ e.g. animation-only) · **NEEDS-WORK** (🔧) · **NOT-USABLE** (❌ broken/nothing/crash/stuck).

**How to use:** work a unit at a time; set our curation field per verdict —
`GOOD→Approved`, `TUNE/NEEDS-WORK/REVIEW→Reviewed` (+ note), `NOT-USABLE(broken)→Blocked/Hidden`.
Crash/stuck rows are also in `TESTER_BASELINE_v0100.md` Part A (the block list).

> Many NEEDS-WORK verdicts are purely "locked in place during cast" → fixable with
> `.beelz admin ability <id> freelymove <sec>` (no code change). See Part C #1 of the baseline doc.

---

## Units A–L

| Unit | Ability | ID | Verdict | Finding |
|---|---|---|---|---|
| Albert (Toad King) | Double Tongue Slap | 1718898538 | REVIEW | 0.9s cast, no CD, locks in place 2-3s — just stop the lock |
| Albert (Toad King) | Frog Split Travel | 1808492070 | NEEDS-WORK | launches you 14 tiles/12s; 8s CD ends before landing |
| Albert (Toad King) | Frog Vomit | 1421967280 | REVIEW | locked 2.5s, 20s CD, spawns 1 toad; fix lock + more frogs |
| Albert (Toad King) | Poison Leap Travel | 1790744720 | REVIEW | **stuck crouch model on landing** (~5.5s lock) — see block list |
| Albert (Toad King) | Poison Rain | -1864096491 | REVIEW | 4s locked cast, 16s CD; acid DoT rain; fix lock |
| Albert (Toad King) | Tongue Grab | -514415940 | REVIEW | two-hit; weak first hit; ~2s lock |
| Albert (Toad King) | Swallow | 1292896032 | REVIEW | eats enemy; **feeding while swallowed launches you to space** — see block list |
| Albert (Toad King) | Spit | -1238687119 | REVIEW | pairs w/ Swallow; wants recast swap; fix lock |
| Alpha (Wolf) | Boss Bite | -1654954396 | REVIEW | works; same dmg as Enraged — dup? |
| Alpha (Wolf) | Boss Bite Enraged | 1558429115 | GOOD | works, 0.6s cast (faster) |
| Alpha (Wolf) | Dash Attack Double | 280173050 | REVIEW | dup of single/triple |
| Alpha (Wolf) | Dash Attack Single | 920051497 | REVIEW | dup |
| Alpha (Wolf) | Dash Attack Tripple | -1161539168 | REVIEW | dup |
| Alpha (Wolf) | Boss Howl | 247740796 | NOT-USABLE | should summon wolves, summons nothing |
| Alpha (Wolf) | Maul Hard | 2145136774 | GOOD | dash, low dmg; missed hits stack DR-stun |
| Alpha (Wolf) | Pounce | -821573025 | GOOD | works; lock-in cast |
| Alpha (Wolf) | Speed Buff | 544754926 | GOOD | +2 MS 8s but hard-locks too long |
| Alpha (Wolf) | Step Back | 1456629565 | GOOD | smooth dodge-back, best one |
| Bane | Infiltrator AfterImage | -1532726733 | NOT-USABLE | only works if cursor on model; no direction |
| Bane | Infiltrator ArmyOfShadows | 1986068244 | NOT-USABLE | anim but no clones |
| Bane | Infiltrator DoubleStrike | 1456136938 | REVIEW | works; not better than base |
| Bane | Infiltrator HeavyAttack | -1422240708 | REVIEW | works; not worth |
| Bane | Infiltrator KnifeThrow | 1621601748 | GOOD | works, feels good |
| Bane | Infiltrator KnifeThrow Hard | -39228843 | GOOD | works, faster |
| Bane | Infiltrator ShadowFlurry | 94933870 | REVIEW | big AoE but ~2s windup+endlag |
| Bane | Infiltrator ShadowStep | -664569083 | GOOD | needs target, feels VERY good |
| Beatrice | Gargoyle Fly End | 1563014858 | REVIEW | lands at cursor; off-map/terrain stuck — block list |
| Beatrice | Gargoyle Fly End Tailor Hard | 1551140710 | REVIEW | same; AoE missing; off-map — block list |
| Beatrice | Gargoyle Fly Start | -382913708 | REVIEW | invis air-walk, clip out of map — block list |
| Beatrice | Gargoyle Wing Shield | 1460741503 | GOOD/EXPLOIT | near-immortality + heal — OP |
| Beatrice | Gargoyle Wing Shield Emerge | 88850785 | GOOD/EXPLOIT | spammable AoE, too much dmg too fast |
| Beatrice | Legion Gargoyle Forward Swipe | -915929892 | NOT-USABLE | no player swing anim |
| Beatrice | Legion Gargoyle Relocate Forward Init | 701450949 | NOT-USABLE | nothing |
| Beatrice | Legion Gargoyle Relocate Apply Targetbuff | 183752077 | NOT-USABLE | cursor-on-self jump only |
| Beatrice | Legion Gargoyle Relocate Forward Travel | 174118917 | NOT-USABLE | nothing |
| Beatrice | Tailor Shapeshift | 1637511208 | NOT-USABLE/EXPLOIT | immortality + heal-to-threshold |
| Ben | Cursed Wanderer Fake Melee | -1512616160 | NOT-USABLE | nothing |
| Ben | Cursed Wanderer Melee | -127603789 | GOOD | single hit, snares |
| Ben | Cursed Wanderer Gossip | -1137336047 | REVIEW | entangle balls (junk-flagged) |
| Cassius | High Lord Corpse Storm | 1006960825 | TUNE | good ult; duration long, AoE dmg low |
| Cassius | High Lord Leap Strike | 938684260 | NOT-USABLE | **T-pose, must self-kill** — block list |
| Cassius | High Lord PBAE | -1139777657 | REVIEW | anim only, no dmg |
| Cassius | High Lord Pull Players | 851287157 | NOT-USABLE | anim only |
| Cassius | High Lord Raise Dead | 416744805 | NOT-USABLE | anim only, no summon |
| Cassius | High Lord Sword Dash Cleave | -2126197617 | TUNE | great for GS; locks after |
| Cassius | High Lord Sword Primary | -328302080 | GOOD | wide swing combo, good |
| Cassius | High Lord Unholy Skill | -1298764600 | GOOD | skull projectile + 1 skeleton |
| Cassius | High Lord Unholy Warp | -604910466 | GOOD | fast dash; DR-immunity stack; very usable |
| Christina (Nun) | Vblood Melee | 607644390 | GOOD | basic arc; 1.4s lock feels bad |
| Christina (Nun) | Counter | -1725804558 | NEEDS-WORK | 0.6s locked + still take dmg; remove cast time |
| Christina (Nun) | AoE | 1267698255 | TUNE | beam heal+dmg; 1.8s lock; doesn't heal over duration |
| Christina (Nun) | HARD Holy Bolt Spray | 605530795 | NEEDS-WORK | dmg+DoT; bad endlag |
| Christina (Nun) | Healing Channel | 54266570 | TUNE | self-heal 8s; locks for duration |
| Christina (Nun) | Healing Channel Long | 1090673078 | NOT-USABLE | locks; doesn't heal allies/summons |
| Christina (Nun) | Healing Channel Short | 397860291 | NOT-USABLE | locks; doesn't heal allies/summons |
| Christina (Nun) | Holy Projectile | -1581417695 | REVIEW | fan of 5; no heal/DoT |
| Christina (Nun) | V Blood Heal Command | -1756529149 | NOT-USABLE | does nothing |
| Christina (Nun) | V Blood Spawn Minions | -1754021382 | NOT-USABLE | doesn't work; locks you |
| Clive | Bandit Bomber Elite Roll | 192866635 | GOOD | dash + delayed bomb |
| Clive | Bandit Cluster Bomb Throw | -444905742 | REVIEW | 2.5s lock; 3→9 bombs; fix lock |
| Clive | Bandit Sticky Bomb | 1479036818 | NEEDS-WORK | wrong desc; stands still ~2s |
| Cyril | Cursed Smith Hammer Slam | -1502523453 | REVIEW | 3s locked+immune; slam AoE |
| Cyril | Cursed Smith Melee | 1700777111 | REVIEW | slasher combo + dash |
| Cyril | Cursed Smith Multi Dash | -329078808 | NOT-USABLE | doesn't dash, does nothing |
| Cyril | Cursed Smith Multi Dash Charge | 340003308 | NEEDS-WORK | charges fwd, unimpressive |
| Cyril | Cursed Smith Summon Weapon All | 847476786 | REVIEW | spawns 5; 7s lock; can't raise count |
| Cyril | Summon Weapon Axe | -1889094547 | NEEDS-WORK | 1 weapon, 2.5s lock, count fixed |
| Cyril | Summon Weapon Mace | 546685538 | NEEDS-WORK | as above |
| Cyril | Summon Weapon Slashers | 151173850 | NEEDS-WORK | as above |
| Cyril | Summon Weapon Spear | 1162725480 | NEEDS-WORK | as above |
| Cyril | Summon Weapon Sword | -332863103 | NEEDS-WORK | as above |
| Cyril | Cursed Smith Sword Dash | -668888775 | REVIEW | ~1.5 tile slash AoE |
| Domina | Voltage Electric Rod | -1548349308 | TUNE | big AoE ult; stationary 12s too long |
| Domina | Voltage Electric Sprint | -177268000 | NOT-USABLE | not triggering |
| Domina | Voltage Kick Combo | 1878838878 | WORKS-NOT-USABLE | works but anim stuck |
| Domina | Voltage Kick Combo Final | 1851411882 | WORKS-NOT-USABLE | works, anim broken |
| Domina | Voltage Leap Attack | 2016544872 | REVIEW | great slam; stuck ~1s after |
| Domina | Voltage Leap Attack Hard | -1853889696 | REVIEW | as above, harder |
| Domina | Voltage Sprint Kick | 788963695 | REVIEW | fast dash; anim unnatural |
| Domina | Voltage Sprint Kick Antip | -1143650386 | REVIEW | unclear effect |
| Domina | Voltage Swap | -1372140488 | REVIEW | long-range kick; anim broken |
| Domina | Voltage Whip | 1191517048 | NOT-USABLE | stuck when active |
| Domina | Voltage Whirlwind | -1243386667 | TUNE | cast too long, fully stuck |
| Errol | Stone Breaker Mountain Rumbler Hard | 1688748795 | NEEDS-WORK | 6s locked, NO CD |
| Errol | Rock Smash Hard | 2078583785 | NEEDS-WORK | 4s locked, no CD |
| Errol | Swing Attack Hard | 1684865572 | NEEDS-WORK | dash stun/KB; works on V-Bloods, keep |
| Errol | V Blood Mountain Rumbler | 593979922 | NEEDS-WORK | 6s locked, no CD |
| Errol | V Blood Reinforcement | 1267543813 | NEEDS-WORK | 2 weak lv14 miners |
| Errol | V Blood Rock Smash | -96620724 | NEEDS-WORK | 4s locked, no CD |
| Errol | V Blood Spin Attack | -47403235 | NEEDS-WORK | 3s locked, 5s CD |
| Errol | V Blood Swing Attack | -331113507 | NEEDS-WORK | dash KB, no CD |
| Finn | Feed Serpent Lineup 01-03 / Hard | -1601441951 / -1657698157 / -228936316 / 514628041 | NOT-USABLE | boss-positioning; just a dash on self |
| Finn | Fishing Lineup | -82547339 | NEEDS-WORK | +2.4 MS 10s; cursor-on-self |
| Finn | Fish Hook | 643209588 | NEEDS-WORK | reels in, no stun; self-stun; 4s lock |
| Finn | Fishing Blowfish / Piranhas / Old Boot | -2096565232 / 647582679 / 396802528 | NOT-USABLE | anim only on fishing spot |
| Finn | Swing Attack | 1516349451 | REVIEW | 2-hit; ≥2s locked |
| Finn | Spin Attack | -1807544727 | REVIEW | rod-spin AoE; 4s build-up |
| Finn | Serpent Feed / Hard | 571532506 / -405467076 | NEEDS-WORK | spawns serpent; hides underwater, no aggro |
| Foulrot | Ghastly Mockery | -1208888966 | REVIEW | 4s locked; on-hit teleport+fear |
| Foulrot | Shadow Meld | -1441325084 | NEEDS-WORK | 5s invis; incapacitate doesn't work |
| Foulrot | Shadow Meld Melee | -597391921 | NEEDS-WORK | incapacitate doesn't work |
| Foulrot | Slice 01 | -116587794 | GOOD | primary; dash + swipe |
| Foulrot | Slice 02 | -280574043 | NEEDS-WORK | multi-hit combo not functional |
| Foulrot | Summon Ghosts | -1784566500 | NOT-USABLE | doesn't summon banshees |
| Frostmaw | Frost Nova | 1238313774 | GOOD | big ice nova + chill |
| Frostmaw | Ice Beam First | 495259674 | GOOD | beam, dmg + freeze snare; cursor-on-self |
| Frostmaw | Ice Beam Second | 1390611238 | GOOD | counter-clockwise beam |
| Frostmaw | Ice Dash Init | 54541608 | WORKS-NOT-USABLE | start-anim only |
| Frostmaw | Ice Dash Strike | 203761859 | NEEDS-WORK | swing visual broken |
| Frostmaw | Ice Dash Strike Hard | 929369353 | NEEDS-WORK | swing visual broken; 2x dmg |
| Frostmaw | Ice Dash Travel | -496233120 | GOOD | dash 2 tiles |
| Frostmaw | Ice Dash Travel Hard | -2029387940 | GOOD | dash 3 tiles |
| Frostmaw | Icicle Throw | -1683730497 | REVIEW | chill+freeze; cursor-on-target |
| Frostmaw | Leap Attack | -1986179833 | TUNE | KB, low dmg; targeting tricky |
| Frostmaw | Melee Attack | 1568904735 | REVIEW | anim disconnect, low dmg |
| Frostmaw | Snow Storm | -2090815764 | WORKS-NOT-USABLE | visual only, no dmg |
| General Elena | Ice Ranger Tower Of Frost | 1431473799 | NOT-USABLE | **sends you to space** — block list |
| General Elena | Tower Of Frost Low Health | 2057952818 | NOT-USABLE | **sends you to space** — block list |
| Goreswine | Chain Bolt Hard | -323939446 | NEEDS-WORK | 2s locked, 3s CD |
| Goreswine | Corpse Explosion | -1272200774 | REVIEW | 2s locked; small AoE; remove lock |
| Goreswine | Corpse Explosion Hard | 1971375367 | REVIEW | 3s locked; large AoE; remove lock |
| Goreswine | Corpse Party | 622287046 | NOT-USABLE | 7s invinc+immobile; doesn't spawn |
| Goreswine | Corpse Party Hard | -184530068 | NOT-USABLE | doesn't spawn |
| Goreswine | Flesh Warp Travel | 2145809434 | GOOD | travel + green explosion |
| Goreswine | Projectile | 780432315 | NEEDS-WORK | 3s locked, single proj |
| Goreswine | Small Explosion | 852659329 | NEEDS-WORK | 2s locked; acid puddle |
| Grayson | Charge Attack | 65616384 | REVIEW | ~2.5s locked; hammer slam; visual flicker |
| Grayson | Emote Aggro | -146108512 | NOT-USABLE | aggro emote, skip |
| Grayson | Melee Attack | -66951019 | NEEDS-WORK | locked full 5s |
| Grayson | Projectile | 226929260 | NEEDS-WORK | ~3.5s locked, 3 chains |
| Grayson | Projectile Hard | -1865487078 | NEEDS-WORK | 5 chains |
| Grayson | Reinforcement | 1106149033 | NOT-USABLE | doesn't summon |
| Grayson | Spike Trap | 19031114 | REVIEW | 3 waves; traps last ~2.5min |
| Grayson | Spin Attack / Hard | -2121210997 / 609538743 | REVIEW | 3s locked; 5 hits |
| Grethel | Glassblower Approach | -719487446 | NOT-USABLE | does nothing |
| Grethel | Cyclone / Hard | -29584300 / 317399990 | TUNE | cone dmg + discs; Storm Shield didn't activate |
| Grethel | Deep Breath / Hard | 800186323 / -1174644772 | NOT-USABLE | anim, no dmg |
| Grethel | Glass Rain | -212014657 | GOOD | molten pools, wide ground AoE |
| Grethel | Mirror Shield | 890007919 | GOOD | frontal shield; on-hit cone AoE |
| Grethel | Wind Dash | -225237355 | REVIEW | glide+launch line AoE; doesn't freeze |
| Jade | Blast Vault | 76767983 | GOOD | works |
| Jade | Caltrops / Hard | -1294182358 / 1372353064 | REVIEW | works; no range limit |
| Jade | Disabling Shot | -526118698 | GOOD | works |
| Jade | Explosive Shells | -453760177 | GOOD | works |
| Jade | Revolvers 1-4 | -1783782358 / -2013216961 / 1196517991 / 688350142 | REVIEW | works; single-shot dups |
| Jade | Snipe / Hard | -1884688827 / -1030552412 | GOOD | works |
| Jade | Stealth | 501615608 | GOOD | works |
| Keely | Deadeye Camouflage | 403340165 | GOOD | 4s invis, 15s CD |
| Keely | Deadeye Roll | -912372242 | GOOD | short dash; boring |
| Keely | Frost Arrow Camouflage HARD | 1015070299 | GOOD | 4s invis + frost AoE; great |
| Keely | Rain Of Arrows / Hard | 766284586 / -328617085 | NEEDS-WORK | ~2s locked too long |
| Keely | Multi Shot / Hard | 2134585360 / -1871956083 | NEEDS-WORK | ~3s cast, 5s CD, locked |
| Kodia | Bear Dire Area Attack | -1972594588 | REVIEW | rock-fall; 5s locked |
| Kodia | Bear Dire Dash | -1709055711 | GOOD | dash dmg (= Bloodcraft bear) |
| Kodia | Bear Dire Melee / Hasted | 1814567660 / -217583794 | REVIEW | single basic; locked |
| Kodia | Bear Dire On Aggro Emote | -1155840114 | NOT-USABLE | no effect on player |
| Kodia | Bear Dire Roar | -1559349374 | REVIEW | 4s locked; KB/snare; MS ramp |
| Kodia | Bear Dire Target Switch | -1748125028 | NOT-USABLE | no effect |
| Kodia | Bear Fall Asleep | -193432841 | NOT-USABLE | useless (aggro anim) |
| Kodia | Bear Wake Up | -388636006 | NOT-USABLE | useless (aggro anim) |
| Kriig | Undead Leader AreaAttack | 211628325 | GOOD | works |
| Kriig | ChainHookProjectile | 234226418 | GOOD | works |
| Kriig | MeleeStandard | 346834991 | NOT-USABLE | huge windup/endlag |
| Kriig | SpinningDash | 948587795 | GOOD | works; agony to allied skeletons |
| Kriig | WardOfTheDamned | -1504629350 | REVIEW | slow-moving; maybe exclude |
| Leandra | EternalDarkness | 1583944483 | NOT-USABLE | long invis glide; speed up to use |
| Leandra | ShadowSoldier | 461701172 | NOT-USABLE | doesn't work |
| Leandra | Projectile | 787702249 | GOOD | low dmg, 1 piercing |
| Leandra | Projectile Hard | 651637774 | GOOD | low dmg, 3 piercing |
| Leandra | ShadowStep | 1325722355 | NOT-USABLE | **kills caster** — block list |
| Leandra | TrippleBolt | -1795148379 | NOT-USABLE | **kills caster** — block list |
| Lidia | Deadeye Camouflage | 403340165 | GOOD | 4s invis; perfect |
| Lidia | Chaos Nuke Hard | -1181691042 | NOT-USABLE | arrows never land; AoE never happens |
| Lidia | Chaosbarrage / Hard | 1996370390 / 642767950 | REVIEW | 5s locked; 4 arrows; chaos puddles |
| Lidia | Chaosstorm / Hard | -1230681995 / -836774616 | REVIEW | 3 arrows; 3s locked |
| Lidia | Deadeye Roll | -912372242 | REVIEW | short dash; no invuln |

## Units M–Z

| Unit | Ability | ID | Verdict | Finding |
|---|---|---|---|---|
| Maja | Cutting Parchment 02 | -198012170 | REVIEW | little dmg; don't recommend |
| Maja | Quick Attack | -1104025162 | GOOD | works; = Ranged, add one |
| Maja | Ranged Attack | -186690512 | GOOD | = Quick, add one |
| Maja | Razor Parchment | 2019689688 | REVIEW | high endlag, low dmg |
| Maja | Whack-A-Scribe Init | -1332977411 | NOT-USABLE | nothing alone (chain) |
| Maja | Whack-A-Scribe Travel | -1008562275 | REVIEW | long cast/endlag locks you |
| Maja | Ink Explosion | -592301846 | GOOD | works; 2.5s cast long |
| Maja | Ink Fuel | -1922948406 | GOOD | consume minion to heal |
| Meredith | Light Arrow Basic Shot | 764619540 | REVIEW | strong; 1.6s root too long |
| Meredith | Light Arrow Dash | -202704002 | REVIEW | dash through enemies |
| Meredith | Hard Guided Arrow | 1181965808 | REVIEW | huge dmg; 2.5s root |
| Meredith | Heal Shot | -1509483896 | GOOD | heals allies, bounces 5; great |
| Meredith | Multi Shot Hard | 1574404388 | GOOD | cone dmg + heals |
| Meredith | Quick Shot | -1282136902 | REVIEW | 0.6s root |
| Meredith | Snipe | -1286987800 | REVIEW | massive dmg; 2.5s root, no CD |
| Meredith | Spawn Minions | 336380612 | NOT-USABLE | no minions spawn |
| Meredith | Throw | -494771621 | REVIEW | big AoE at feet; long CD |
| Nibbles (Rat) | Gnaw | -1199198217 | GOOD | 1s lock, one hard hit |
| Nibbles (Rat) | Multi Gnaw | -70575944 | GOOD | spammable dash-bite |
| Nibbles (Rat) | Multi Gnaw Buff | 1450204157 | NOT-USABLE | no buff effect found |
| Nibbles (Rat) | Poison Burst | -1024883556 | GOOD | ~2s lock; AoE poison DoT |
| Nibbles (Rat) | Rat Vanguard | 830495620 | NEEDS-WORK/OP | 16s invuln + 6 permanent rats |
| Nicholaus | Elite Emote Aggro | -1153380978 | NOT-USABLE | nothing |
| Nicholaus | Elite Projectile | 14728131 | GOOD | proj + AoE (use Hard) |
| Nicholaus | Elite Projectile Hard | 1837385563 | GOOD | faster; recommended |
| Nicholaus | Elite Projectile Nova / Hard | 1988754598 / 1619461812 | TUNE | locked ~8s during waves |
| Nicholaus | Elite Teleport Travel | -1125894381 | GOOD | drop at cursor + skull wave |
| Nicholaus | Elite Raise Dead | -2034290170 | GOOD | 4 armored skeletons, up to 12 |
| Nicholaus | Elite Raise Horde | 1414167161 | GOOD | 12 skeletons, up to ~25 |
| Nicholaus | Raise Dead | -1742500275 | GOOD | 6 armored, follow player |
| Octavian | Militia Call Adds | 1870073064 | NOT-USABLE | summon does nothing |
| Polora | Hard Projectile | -595482115 | GOOD | wisps; dmg + heal allies |
| Polora | Otherside | 1795809188 | GOOD | invis+invuln+MS; spammable; great |
| Polora | Otherside Ally | 2033547790 | GOOD | same on ally; long range |
| Polora | Pixie | -1036943907 | NOT-USABLE | couldn't trigger |
| Polora | Projectile | -914903899 | GOOD | 3 wisps; dmg + heal |
| Polora | Spirit Rift | -939006306 | TUNE | AoE Fear, no dmg; lock too long |
| Polora | Wolf | -321651703 | REVIEW | rooted 1.7s; redundant w/ vanilla |
| Quincey (Tourok) | Call Reinforcements | -3835897 | NOT-USABLE | does nothing |
| Quincey (Tourok) | Chaos Charge | 840159262 | REVIEW | charge + ignite + KB; usable |
| Quincey (Tourok) | Chaos Parry | 1452056926 | REVIEW | barrier; spins, counters |
| Quincey (Tourok) | Chaos Wave | 2003603501 | GOOD | large AoE waves |
| Quincey (Tourok) | Double Swing (×2) | 455219779 / -453755281 | NOT-USABLE | anim broken; still dmg |
| Quincey (Tourok) | Rage Of Chaos | 69086718 | NOT-USABLE | anim only |
| Quincey (Tourok) | Rage Of Chaos Charge | 82350674 | GOOD | charge + fire path; usable |
| Quincey (Tourok) | Weapon Throw Hard | 358187796 | REVIEW | throw; snare+slow; slow start |
| Raziel (WIP) | Bishop kit (8 abilities) | 1611191665 / -1049539886 / -1957047297 / -545560531 / 368122726 / -88118341 / 1674395967 / 296313928 | REVIEW | channel In-Progress, no verdicts yet |
| Rufus | Blood Rag | -892431821 | NEEDS-WORK | 3s rooted; MS only |
| Rufus | Bolt Storm | 1187864883 | NEEDS-WORK | 5s rooted; no snare/mark |
| Rufus | Crossbow (+ variants) | -2010697707 / -1100933071 / 46962343 / -1432555386 | NEEDS-WORK | 3-4s rooted; piercing line |
| Rufus | Rapid Shot / Hard | -1326540020 / -1696612225 | NEEDS-WORK | 5s rooted; piercing + CC-resist |
| Rufus | Rapid Shot Init / Hard | -871701576 / -1568783706 | NOT-USABLE | enrage-start anim only |
| Rufus | Reinforcement | 1914101495 | NEEDS-WORK | 2 weak miners |
| Rufus | Roll | -1773431654 | GOOD | dash, 3 charges; wants i-frames |
| Rufus | Throw Net | 2130985273 | GOOD | 3s snare; remove lock |
| Terah (Geomancer) | Awake | -394948183 | NOT-USABLE | anim only, locks |
| Terah (Geomancer) | Enrage | -1204505053 | NOT-USABLE | anim/sound only |
| Terah (Geomancer) | Enraged Smash | 1079488801 | GOOD | cone AoE rocks; very usable |
| Terah (Geomancer) | Golem Raise Guardians | -598112885 | NOT-USABLE | sound only |
| Terah (Geomancer) | Ground Slam | 2106422510 | TUNE | AoE shards; review cast time |
| Terah (Geomancer) | Human Projectile | -1635892700 | NEEDS-WORK | low dmg; locks too long |
| Terah (Geomancer) | Melee Attack | 1500843923 | NOT-USABLE | low dmg, not useful |
| Terah (Geomancer) | Rock Slam | -221719333 | REVIEW | hard to aim |
| Terah (Geomancer) | Underground Tremmors | -1148606177 | NOT-USABLE | sound only, long lock |
| Terah (Geomancer) | Dracula Quick Teleport | -1940289109 | GOOD | ~4-tile teleport; great |
| Terah (Geomancer) | Transform To Golem (×2) | 1925592126 / -2084016434 | GOOD | works; very low dmg |
| Terah (Geomancer) | Transform To Human | -716557249 | GOOD | reverts golem→human |
| Tristan | Crossbow (+ Fast/Hard) | -737556373 / -23281706 / -1330984343 | GOOD | works; add Hard only |
| Tristan | Fire Rain | 985201241 | GOOD | good; fire puddles hurt self |
| Tristan | Leap Thrust | 1501188283 | REVIEW | broken anim; don't add |
| Tristan | Melee Horizontal (+ Hard) | 1552780633 / -1417141477 | REVIEW | broken anim; worse than auto |
| Tristan | Melee Straight | 224496996 | REVIEW | good if windup/endlag cut |
| Tristan | Roll | -175257190 | REVIEW | awkward anim; short CD |
| Tristan | Whirlwind Init | -343320686 | GOOD | pairs w/ Whirlwind |
| Tristan | Whirlwind | -1503146131 | GOOD | feels very good |
| Ungora (Spider Queen) | AoE | 1573712465 | REVIEW | acid ball → 6 pools; solid |
| Ungora (Spider Queen) | Melee Attack | 1274762079 | REVIEW | too slow; raise dmg |
| Ungora (Spider Queen) | PB AoE | -2109003637 | REVIEW | underwhelming; needs buff |
| Ungora (Spider Queen) | Projectile Loop | -1586810476 | REVIEW | 5×5 acid waves; solid |
| Ungora (Spider Queen) | Range Attack | 1132262199 | GOOD | near-perfect; 5 acid proj |
| Ungora (Spider Queen) | Spawn Adds | -1779071085 | NOT-USABLE | doesn't function on player |
| Ungora (Spider Queen) | Web Hook | 1499629710 | GOOD | pulls enemy; PvP |
| Ungora (Spider Queen) | Web Projectile | 193592408 | GOOD | slowing web; underwhelming |
| Ungora (Spider Queen) | Cocoonify | 2130811227 | NOT-USABLE | no effect |
| Ziva (Iva) | Bomber Jetpack Take Off | -1770586075 | NEEDS-WORK | **terrain clip / OOB** — block list |
| Ziva (Iva) | Bat Vampire Jump Strike | -66831677 | GOOD | works well |
| Ziva (Iva) | Weapon Equip BFG | 948730951 | REVIEW | +MS after use |
| Ziva (Iva) | BFG Lightning Fire | 1524772628 | REVIEW | needs a target |
| Morgana/Megara | Travel To Position Swarm | -1980019894 | NOT-USABLE | **server crash** combo — block list |
| Morgana/Megara | Traveling Orb Barrage | 1242557903 | NOT-USABLE | **server crash** combo — block list |

---

## Notable extras flagged in cross-cutting channels

- **Gloomrot Professor** Discharge `98352404` + Multi Lazer `-2097618568`/`922412053` — praised GOOD; Electric
  Push `1142491430` useless.
- **Corpse Pile / Undead Skeleton Golem Dig `2095802125`** — invincible + attack + no CD = exploit, block/tune.
- **Undead Flying Skull Self Destruct** — no self-damage, low value.
