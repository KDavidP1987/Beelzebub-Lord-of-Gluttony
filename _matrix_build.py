# -*- coding: utf-8 -*-
"""One-off: fold tester feedback into TESTER_ABILITY_MATRIX.csv as 4 new columns.
Sources: docs/TESTER_ABILITY_BASELINE.md (v0.100 verdicts A-Z), hand-authored v0.131
threads (NEW131), the coded hard-block list, BEELZ Testing.xlsx NPC crash/stuck classes.
Decisions (per KDPen 2026-06-10): untested -> leave enabled (no flag); balance exploits
-> Soft; spreadsheet-only this pass."""
import csv, re, io, sys, os

ROOT = os.path.dirname(os.path.abspath(__file__))
CSV  = os.path.join(ROOT, 'Beelzebub', 'Beelzebub', 'docs', 'TESTER_ABILITY_MATRIX.csv')
BASE = os.path.join(ROOT, 'Beelzebub', 'Beelzebub', 'docs', 'TESTER_ABILITY_BASELINE.md')

SOFT = 'Soft (default config)'
HARD = 'Hard (code-locked)'

# --- coded hard-block list (AbilityRules.cs _hardBlockedGuids) = confirmed crash / permanent break
HARD_IDS = {
    -1623080868: 'Sir Erwin Militia Fabian Mountup - SERVER crash',
    1485838951:  'Gloomrot Technician Fiddle - GAME crash (vanilla sword-E while bound)',
    1322698651:  'Gaius Twinblade Throw - GAME crash',
    -485230865:  'Gaius Corpse Buff (buff) - permanent locked/phased/invisible',
    -89125940:   'Gaius Corpse Buff (capturable ability) - permanent locked/phased/invisible',
    938684260:   'Cassius High Lord Leap Strike - permanent T-pose (must self-kill)',
    -891106318:  'Spider Baneling Explode Poison - permanent invisibility on respawn',
}

# --- balance exploits (game-breaking but not crash/corrupting) -> Soft per KDPen
EXPLOIT_SOFT = {
    830495620:  'Rat Vanguard - 16s invuln + 6 permanent rats that never despawn (server-fill exploit)',
    1460741503: 'Gargoyle Wing Shield - near-immortality + heal',
    88850785:   'Gargoyle Wing Shield Emerge - spammable rapid AoE, too much dmg too fast',
    1637511208: 'Tailor Shapeshift - immortality + heal-to-threshold',
    2095802125: 'Corpse Pile / Skeleton Golem Dig - invincible + can attack + spam, no CD (exploit)',
    -1729075022:'Matka Explode Mosquito - one-shots anything incl. V-Bloods/self/allies (needs dmg cap)',
    -1220318405:'Voltatia Clone Warp Right - lvl90 clone self-heals constantly, OP',
}

# --- recoverable launch / stuck / off-map (functional but break-ish) -> Soft until neutralized
LAUNCH_STUCK_SOFT = {
    1431473799: 'Tower of Frost - launches you to space (shared Overseer launch buff)',
    2057952818: 'Tower of Frost Low Health - launches you to space',
    -1770586075:'Iva Jetpack Take Off - clips through terrain / out of bounds on land',
    1808492070: 'Toad Frog Split Travel - launches ~14 tiles, CD ends before landing',
    1790744720: 'Toad Poison Leap Travel - stuck crouch model + ~5.5s lock on landing',
    1292896032: 'Toad Swallow - feeding while swallowed launches you to space',
    -1238687119:'Toad Spit - paired launch race with Swallow',
    1563014858: 'Gargoyle Fly End - lands off-map / stuck in terrain',
    1551140710: 'Gargoyle Fly End Tailor Hard - lands off-map / stuck in terrain',
    -382913708: 'Gargoyle Fly Start - invisible air-walk, clip out of map',
    2030404176: 'Werewolf Chieftain Open The Cages - leaves speed buff needing admin removal',
    -140727030: 'Blackfang Livith Permanent Stealth - fully invisible until next attack',
}

# strong-positive markers that bump a REVIEW (works-but-unsure) up to Enable
POS = re.compile(r'(very usable|feels (very )?good|feels great|perfect|awesome|amazing|great '
                 r'|solid|usable as is|💯|🥳|recommend(ed)?\b|near-perfect|feels VERY good)', re.I)

def base_rec(verdict, finding):
    v = verdict.upper()
    if 'EXPLOIT' in v:                 return ('Soft-Disable', SOFT)
    if 'GOOD' in v or 'TUNE' in v:     return ('Enable', '')
    if 'WORKS-NOT-USABLE' in v:        return ('Soft-Disable', SOFT)
    if 'NEEDS-WORK' in v:              return ('Soft-Disable', SOFT)
    if 'NOT-USABLE' in v:              return ('Soft-Disable', SOFT)
    if 'REVIEW' in v:
        return ('Enable', '') if POS.search(finding or '') else ('Soft-Disable', SOFT)
    return ('Soft-Disable', SOFT)

# ---- parse the baseline markdown table -> records {id: (verdict, finding, label)} ----
feedback = {}   # id -> dict(verdict, finding, rec, dtype, source)
def put(i, verdict, finding, source, rec=None, dtype=None):
    i = int(i)
    if rec is None:
        rec, dtype = base_rec(verdict, finding)
    # global overrides (apply regardless of source verdict)
    if i in HARD_IDS:
        rec, dtype, finding = 'Hard-Disable', HARD, HARD_IDS[i]
    elif i in EXPLOIT_SOFT:
        rec, dtype = 'Soft-Disable', SOFT
        finding = (finding + ' | ' if finding else '') + EXPLOIT_SOFT[i]
    elif i in LAUNCH_STUCK_SOFT:
        rec, dtype = 'Soft-Disable', SOFT
        finding = (finding + ' | ' if finding else '') + LAUNCH_STUCK_SOFT[i]
    feedback[i] = dict(verdict=verdict, finding=finding.strip(), rec=rec, dtype=dtype or '', source=source)

base_txt = io.open(BASE, encoding='utf-8').read()
nrec = 0
for line in base_txt.splitlines():
    if not line.startswith('|'): continue
    cells = [c.strip() for c in line.strip().strip('|').split('|')]
    if len(cells) < 5: continue
    if cells[0] in ('Unit', '---') or set(cells[2]) <= set('-: '): continue
    unit, ability, idcell, verdict, finding = cells[0], cells[1], cells[2], cells[3], cells[4]
    ids = re.findall(r'-?\d{4,}', idcell)
    if not ids: continue
    for i in ids:
        put(i, verdict, f'[{unit}] {ability}: {finding}', 'baseline-v0100')
        nrec += 1
print(f'baseline rows parsed -> {nrec} id-entries, {len(feedback)} unique ids')

# ---- v0.131 threads not yet in the baseline doc (hand-authored from Reference Data/beelz-vblood) ----
E, S, H = 'Enable', 'Soft-Disable', 'Hard-Disable'
NEW131 = [
 (-1329409686,'Dantos','Phase Dual Bomber Duel Quake','needs-work',S,'30s CD; locked in place far too long to be practical'),
 (-98135680,'Dantos','Phase Single Bomber Quake','needs-work',S,'locked in place too long'),
 (-1015932492,'Dantos','Phase Dual Chase Target Buff','good',E,'6s move-speed boost to 8'),
 (68363809,'Dantos','Phase Dual Melee Attack','good',E,'two-hit combo, 2nd applies chill'),
 (-9078612,'Dantos','Phase Dual Seismic Jump','good',E,'slam wave forward, 8s CD'),
 (140047325,'Dantos','Phase Dual Tackle','good',E,'dash, damage + stun, 1s CD'),
 (-845442935,'Dantos','Phase Single Charge','needs-work',S,'120s CD; underwhelming for an ultimate'),
 (1633132226,'Dantos','Phase Single Melee Attack','good',E,'2-hit combo + charge'),
 (1590188261,'Dantos','Phase Single Smack','good',E,'larger Mace-E with knockback + incapacitate'),
 (-405003450,'Dantos','Phase Single Vault Attack','good',E,'jump + ice AoE (chill only)'),
 (221530600,'Dantos','Phase Swap','broken',S,'boss fight cue; leaves a stray speed boost'),
 (-1574580061,'Dantos','Walk To Pos','broken',S,'boss fight cue, not usable'),
 (-1710744334,'Elena','Ice Ranger Blood Mend','needs-work',S,'inferior to normal blood mend'),
 (-926355648,'Elena','Ice Ranger Cold Snap','good',E,'block + ice nova, matches desc minus shield'),
 (953519927,'Elena','Ice Ranger Ice Nova','works-unsure',E,'frost circle + freeze; no cooldown (tune)'),
 (1691254929,'Elena','Ice Ranger Ice Nova Large','good',E,'larger nova, 10s CD'),
 (1064969109,'Elena','Ice Ranger Jump Down','broken',S,'ledge-jump anim, no purpose'),
 (1766924388,'Elena','Ice Ranger Lurker Spikes','works-unsure',E,'great; needs CD, locked during'),
 (-808864212,'Elena','Ice Ranger Lurker Spikes Split','works-unsure',E,'3 icicle waves; needs CD'),
 (-1770479364,'Elena','V Blood Ice Ranger Primary','needs-work',S,'functional but lame, no status effects'),
 (-336633586,'Elena','V Blood Ice Ranger Primary Followup','needs-work',S,'functional but lame'),
 (-1454951943,'Elena','Vampire Crossbow Ice Shard Channel Barrage','broken',S,'shoots up, nothing lands'),
 (765386506,'Elena','Vampire Crossbow Ice Shard','broken',S,'shoots up, nothing lands'),
 (-354379679,'Elena','Vampire Ice Ranger Veil Of Frost','works-unsure',S,'no shield, underwhelming vs vanilla'),
 (98352404,'Henry','Professor Discharge','works-unsure',E,'counter warp; works, needs buff'),
 (1142491430,'Henry','Professor Electric Push','needs-work',S,'pushes enemies, no damage'),
 (1957366402,'Henry','Professor Few Beams','broken',S,'arena-only, no effect outside'),
 (-841469184,'Henry','Professor Many Beams','broken',S,'arena-only'),
 (1379672205,'Henry','Professor Many Beams Hard','broken',S,'arena-only'),
 (-727882902,'Henry','Professor Perma Beams','broken',S,'arena-only'),
 (-1904011556,'Henry','Professor Overload Orb','broken',S,'arena-only'),
 (-559662270,'Henry','Professor Teleport Mid','needs-work',S,'arena-teleport; fixable maybe'),
 (-2097618568,'Henry','Professor Multi Lazer','needs-work',E,'4 spinning beams; inconsistent hits (tune)'),
 (922412053,'Henry','Professor Multi Lazer Hard','needs-work',E,'4 spinning beams (tune)'),
 (-506564570,'Henry','Professor Imploding Orb','works-unsure',E,'electric ball, dmg + knockback'),
 (-941466362,'Jakira','Livith Aggro Players','broken',S,'no effect on player'),
 (987540308,'Jakira','Livith Circling Stance','broken',S,'no effect on player'),
 (-1337706250,'Jakira','Livith Clone Ninjutsu','broken',S,'disappear anim, locked, nothing'),
 (-315621824,'Jakira','Livith Cutting Wind','great',E,'amazing piercing sword projectile, NOT locked'),
 (1318543111,'Jakira','Livith Dash','good',E,'dash; make CD longer'),
 (139802405,'Jakira','Livith Melee Attack','works-unsure',E,'dash + slasher combo, a bit slow'),
 (168833229,'Jakira','Livith Shadow Dance','works-unsure',E,'counter warp + shadow clone; solid'),
 (-385314771,'Jakira','Livith Round Kick','works-unsure',E,'knockback + stun'),
 (-407341422,'Jakira','Livith Slicing Dash','works-unsure',E,'longer-range slasher Q'),
 (1261373899,'Jakira','Livith Slicing Dash Single','works-unsure',E,'single dash, needs target'),
 (1584749067,'Jakira','Livith Spirit Orbs','broken',S,'visual only'),
 (-1518495336,'Jakira','Livith Spirit Orbs Clear Check','broken',S,'no effect'),
 (1920281924,'Jakira','Livith Stealth','needs-work',S,'needs to chain with Stealth Attack'),
 (-1372642162,'Jakira','Livith Stealth Attack','needs-work',S,'needs to chain with Stealth'),
 (91941562,'Mairwyn','Arch Mage Arcane Imprisonment','works-unsure',E,'tornado CC lift; 5s locked'),
 (1520734123,'Mairwyn','Arch Mage Crystal Lance','good',E,'icicle + chill; no CD'),
 (1187623532,'Mairwyn','Arch Mage Crystal Lance Charged','good',E,'freeze + splitting icicles'),
 (-139137314,'Mairwyn','Arch Mage Emote Aggro','broken',S,'no effect on player'),
 (1217615468,'Mairwyn','Arch Mage Fire Spinner','good',E,'fire spinner shoots fireballs; no CD'),
 (-2025881745,'Mairwyn','Arch Mage Flaming Ice','works-unsure',E,'ice freeze nova; no CD'),
 (-1232816408,'Mairwyn','Arch Mage Lightning Arc','good',E,'3 light projectiles; good'),
 (1365358996,'Mairwyn','Arch Mage Lightning Curse','broken',S,'cool effect, no damage'),
 (-1897317770,'Mairwyn','Arch Mage Mirror Image','good',E,'directional teleport'),
 (886063983,'Mairwyn','Arch Mage Teleport','needs-work',S,'only goes north'),
 (898980277,'Matka','Cursed Witch Corrupted Ghouls','broken',S,'tiny visual, no damage'),
 (1628136011,'Matka','Cursed Witch Death Squad','broken',S,'7s invincible, nothing spawns'),
 (1294766036,'Matka','Cursed Witch Wicked Whisps','good',E,'fan of 7 unholy bolts; boring but works'),
 (1860769470,'Matka','Cursed Witch Hex V Blood','needs-work',S,'4s locked, polymorph; no ghosts'),
 (1511001063,'Matka','Cursed Witch Witch Bolt','good',E,'condemn + AoE; needs CD + lock fix'),
 (1541929232,'Sir Magnus','Overseer Hard Snow Lob','good',E,'snowball icicle burst; small chill dmg'),
 (1792964134,'Sir Magnus','Overseer Ice Recovery','works-unsure',E,'healing icicle barrier; no CD'),
 (1964067196,'Sir Magnus','Overseer Icicle Field','works-unsure',E,'ice wave + icicle field'),
 (-1973550282,'Sir Magnus','Overseer Piercing Charge','works-unsure',E,'dash 5 tiles + chill'),
 (493409764,'Sir Magnus','Overseer Piercing Charge Throw','broken',S,'boss grab follow-up; no effect on player'),
 (1823825002,'Sir Magnus','Overseer Reinforcement','broken',S,'3 ruffians not recognised as summons, dont follow'),
 (613355091,'Sir Magnus','Overseer Swing Attack','good',E,'ice spin + snowballs; melee chill combo'),
 (1798105602,'Stavros','Carver Body Slam','good',E,'dash + corruption trail DoT'),
 (-1796211602,'Stavros','Carver Chop Leap','needs-work',S,'leap + 3 hits; vulnerable; primary-like'),
 (1147924409,'Stavros','Carver Mega Chop Leap','needs-work',S,'locked 2s after'),
 (-1808411682,'Stavros','Carver Ultra Mega Chop Leap','needs-work',S,'splits into 3 streams'),
 (964878669,'Stavros','Carver Coat Weapon','broken',S,'purely visual, no effect'),
 (2102431676,'Stavros','Carver Mega Cleave','works-unsure',E,'wide corruption swing + projectiles; lock fix'),
 (-833255541,'Stavros','Carver Spew Corruption','works-unsure',E,'6 corruption projectiles; no CD, locked (tune)'),
 (684024768,'Stavros','Carver Spew Corruption Small','works-unsure',E,'2 projectiles; no CD'),
 (-1669199769,'Stavros','Carver Summon Carvers','works-unsure',E,'summons 2 lvl73 Carvers; underwhelming'),
 (1705726981,'Stavros','Carver Weapon Throw Intense','works-unsure',E,'sword throw; underwhelming'),
 (-1457510412,'Stavros','Carver Wild Swings','works-unsure',E,'single swing; underwhelming'),
 (2103931135,'Stavros','Carver Whirlwind Init','good',E,'10s beyblade travel + projectiles'),
 (1624827356,'Vincent','Militia Guard Frost Nova','tune',E,'large ice AoE; slow start (tune)'),
 (308360499,'Vincent','Militia Guard Frost Shield','tune',E,'frost barrier; CD too long?'),
 (-1110188840,'Vincent','Militia Guard Ice Bolts Throw','tune',E,'5 ice arrows + freeze; locked'),
 (-1634136571,'Vincent','Militia Guard Ice Bolts Throw Hard','good',E,'5 ice arrows + freeze'),
 (-743963442,'Vincent','Militia Guard Ice Breaker','good',E,'dash + swing + attack-speed buff'),
 (1607170837,'Vincent','Militia Guard Ice Breaker Init','tune',E,'2s frozen then haste 9s (tune)'),
 (-841402173,'Vincent','Militia Guard Melee Attack','good',E,'dash + icy swing, chill stacks to freeze'),
 (-948735477,'Voltatia','Railgun Sergeant Call Adds','broken',S,'nothing spawns'),
 (1623874343,'Voltatia','Railgun Sergeant Cloak Field','works-unsure',E,'invis field 4s; shield doesnt work'),
 (-1187406748,'Voltatia','Railgun Sergeant Energy Burst','works-unsure',E,'energy beam; 4s locked, no CD'),
 (-1733128159,'Voltatia','Railgun Sergeant Homing Orb','needs-work',S,'homing only targets vampires, unusable'),
 (978386280,'Voltatia','Railgun Sergeant Lightning Wall','works-unsure',E,'lightning wall; locked 5s, no CD'),
 (-1308520526,'Voltatia','Railgun Sergeant Warp','works-unsure',E,'great warp; 0.5s CD'),
 (-1717533555,'Voltatia','Railgun Sergeant Warp Init','broken',S,'does nothing'),
 (-900859351,'Voltatia','Railgun Sergeant Wide Shot','works-unsure',E,'4 electric projectiles + snare; locked, no CD'),
 (1113171592,'Willfred','Werewolf Chieftain Feed','broken',S,'animation only'),
 (1001804923,'Willfred','Werewolf Chieftain Feed Buff','broken',S,'no visible effect'),
 (1916767891,'Willfred','Werewolf Chieftain Human Melee Attack','works-not-usable',S,'weapon doesnt swing, anim broken'),
 (1445822330,'Willfred','Werewolf Chieftain Knockdown','good',E,'2-space charge + 3s damaging stun'),
 (-988264305,'Willfred','Werewolf Chieftain Melee Attack','works-unsure',E,'V-path damage; anim broken'),
 (-174926399,'Willfred','Werewolf Chieftain Multi Bite','good',E,'jump bite + DR-stun stacks; fast'),
 (-67893977,'Willfred','Werewolf Chieftain Multi Bite Buff','broken',S,'visual rage yell only'),
 (149010586,'Willfred','Werewolf Chieftain Multi Bite Buff Hard','tune',E,'rage yell + speed buff 8s'),
 (-566065717,'Willfred','Werewolf Chieftain Shadow Dash','good',E,'3-space charge + shadow DoT'),
 (-192549213,'Willfred','Werewolf Chieftain Stealth','great',E,'AWESOME full invis run; CD too long'),
 (-580940152,'Ziva','Iva Autofire','tune',E,'machine-gun fire; slight delay to start'),
 (870237772,'Ziva','Iva Ball Lightning','broken',S,'visual only, may need arena'),
 (1524772628,'Ziva','Iva BFG Lightning Fire','good',E,'green lightning; needs target'),
 (-786900742,'Ziva','Iva BFG Orb','broken',S,'visual only'),
 (-1715136284,'Ziva','Iva Bomber Jetpack Chase Target','good',E,'drops bombs in target circles (jetpack combo)'),
 (-577179884,'Ziva','Iva Bomber Jetpack Land','good',E,'land early; 2s stun on landing'),
 (-247423777,'Ziva','Iva Burn','unsure',S,'small fire stream from one weapon; janky'),
 (1517447626,'Ziva','Iva Burning Ring Of Fire','good',E,'ring of fire lasts 14s'),
 (-639593760,'Ziva','Iva Jet Dash','good',E,'~2-space jet dash'),
 (-309871879,'Ziva','Iva Jet Escape','good',E,'2.5-space backward jet dash'),
 (1450987113,'Ziva','Iva Shotgun Blast','good',E,'5-projectile shotgun, 3 rapid volleys'),
 (891644945,'Ziva','Iva Tazer','good',E,'bouncing lightning ball'),
 (948730951,'Ziva','Iva Weapon Equip BFG','broken',S,'visual only (gun drop)'),
 (-2020699185,'Ziva','Iva Weapon Equip Flamer','broken',S,'visual only'),
 (-420570478,'Ziva','Iva Weapon Equip Shotgun','broken',S,'visual only'),
 (-1409019914,'Ziva','Iva Weapon Equip Tazer','broken',S,'visual only'),
 (2013545793,'Ziva','Iva Weapon Malfunction BFG','broken',S,'visual explosion, no damage'),
 (49364370,'Ziva','Iva Weapon Malfunction Flamer','broken',S,'visual explosion, no damage'),
 (-680856467,'Ziva','Iva Weapon Malfunction Shotgun','broken',S,'visual explosion, no damage'),
 (1149489416,'Ziva','Iva Weapon Malfunction Tazer','broken',S,'visual explosion, no damage'),
 (-422683973,'Solarus','Paladin Angelic Ascent','works-unsure',E,'lunge through air; low dmg, anim frozen'),
 (1145861576,'Solarus','Paladin Centre Travel','needs-work',S,'slow short travel, stuck anim; useless'),
 (922419960,'Solarus','Paladin Charged Swing','works-not-usable',S,'stuck anim, dmg too high; needs work'),
 (-1720407974,'Solarus','Paladin Dash','tune',E,'awesome lightning-ball dash; low dmg (tune)'),
 (51772774,'Solarus','Paladin Divine Rays','tune',E,'ground DOT rays; needs CD, spammy (tune)'),
 (-1172099204,'Solarus','Paladin Hard Dash','tune',E,'bigger dash; tune speed/dmg'),
 (-552723816,'Solarus','Paladin Heal Angel','tune',E,'self/ally heal; stuck during, fix CD'),
 (998464144,'Solarus','Paladin Holy Bubble Beam','works-unsure',S,'worse Divine Rays; stuck whole duration'),
 (834886466,'Solarus','Paladin Holy Flack Cannon','tune',E,'spinning lightning balls; stuck 5s (tune)'),
 (699013677,'Solarus','Paladin Holy Flack Cannon Hard','tune',E,'stronger flack cannon (tune)'),
 (1673497547,'Solarus','Paladin Melee','tune',E,'spinner light rays 12s; tune dmg'),
 (1825130923,'Solarus','Paladin Summon Angel','tune',E,'summons up to 3 Divine Angels; tune dmg/cap'),
 (2095802125,'Corpse Pile','Undead Skeleton Golem Dig','exploit',S,'invincible + attack + spam, no CD (exploit)'),
]
DTYPE = {E:'', S:SOFT, H:HARD}
for i,unit,ab,vd,rec,find in NEW131:
    put(i, vd, f'[{unit}] {ab}: {find}', 'v0131', rec=rec, dtype=DTYPE[rec])
print(f'after v0131 -> {len(feedback)} unique feedback ids')

# seed ALL coded hard-blocks (some are crash threads/prose, not in the A-Z verdict table)
for i, desc in HARD_IDS.items():
    put(i, 'crash/break', desc, 'hard-block')   # put() forces Hard via HARD_IDS override
print(f'after hard-block seed -> {len(feedback)} unique feedback ids')

# ---- load CSV, resolve NPC-sheet stuck/useless CLASSES by name (tester-confirmed) ----
rows = list(csv.reader(io.open(CSV, encoding='utf-8-sig')))
hdr = rows[0]
IDX = {c:i for i,c in enumerate(hdr)}
id_to_name = {}
for r in rows[1:]:
    try:
        gid = int(r[IDX['ID']]); id_to_name[gid] = r[IDX['Ability']]
    except Exception:
        pass

# Sleep/Wake pair = stuck-or-useless solo (BEELZ Testing NPC sheet + Treant/Golem thread: "removal recommended")
sleepwake = 0
for gid, nm in id_to_name.items():
    low = nm.lower()
    if gid in feedback:
        continue
    if 'fall asleep' in low or low.endswith('wake up') or ' wake up' in low or 'sleeping idle' in low:
        put(gid, 'stuck', f'{nm}: sleep/wake pair - stuck or useless solo (tester-flagged class; needs wake-up to unstick)',
            'npc-sheet', rec=S, dtype=SOFT)
        sleepwake += 1
print(f'sleep/wake class flagged -> {sleepwake}')

# correct 4 source-data ID typos (sign flips / dropped digit / unit-id-as-ability-id) so they match the CSV
ID_FIX = {174118917: 1741189817, 609538743: -609538743, 898980277: 1898980277, -1669199769: -1592003626}
for wrong, right in ID_FIX.items():
    if wrong in feedback:
        feedback[right] = feedback.pop(wrong)

from collections import Counter
print('\nRecommendation distribution:', dict(Counter(v['rec'] for v in feedback.values())))
matched = [i for i in feedback if i in id_to_name]
unmatched = [i for i in feedback if i not in id_to_name]
print(f'feedback ids: {len(feedback)} | matched in CSV: {len(matched)} | UNMATCHED: {len(unmatched)}')
for i in unmatched:
    print('  UNMATCHED', i, '::', feedback[i]['finding'][:80])

if '--write' in sys.argv:
    new_cols = ['TesterVerdict', 'TesterFinding', 'Recommendation', 'DisableType']
    out = [hdr + new_cols]
    for r in rows[1:]:
        try:
            gid = int(r[IDX['ID']])
        except Exception:
            gid = None
        fb = feedback.get(gid)
        if fb:
            out.append(r + [fb['verdict'], fb['finding'], fb['rec'], fb['dtype']])
        else:
            out.append(r + ['', '', '', ''])
    import shutil
    shutil.copyfile(CSV, CSV + '.bak')
    with io.open(CSV, 'w', encoding='utf-8-sig', newline='') as f:
        csv.writer(f).writerows(out)
    print(f'\nWROTE {CSV} (+{len(new_cols)} cols, {len(out)-1} rows). Backup at {os.path.basename(CSV)}.bak')
