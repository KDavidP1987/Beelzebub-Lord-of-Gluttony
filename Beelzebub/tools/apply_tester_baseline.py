"""
v0.136.0 — apply the Discord tester baseline to the SHIPPED default rules (Resources/ability_rules.default.json).

Input (read-only): Reference Data/TESTING DATA/TESTER_ABILITY_MATRIX.xlsx — the `FINAL: ShipDecision` verdict and
the `Phase 2: Cfg …` knob recommendations from the V-Blood + NPC tester audit (88 Discord threads + NPC sheet).

What it writes (decisions D1–D7 of the v0.136 plan):
  • Hard-Disable (crash / character-break) → Enabled=false, ReviewStatus=Blocked, ReviewTag=crash|stuck.
    (The same GUIDs are ALSO in the C# _hardBlockedGuids in Services/AbilityRules.cs.)
  • Soft-Disable WITH a shippable config fix → stays enabled, fix pre-set (FreeMoveAfterSeconds / CooldownSeconds /
    CooldownScale / DamageScale / LeapHeight / SummonCap / SummonTimeoutSeconds / MaxRangeOverride).
  • Soft-Disable with NO shippable fix → Enabled=false (soft blacklist — an admin can re-enable).
  • Enable WITH a recommendation → fix pre-set, stays enabled.
Existing knob values are never overwritten (only added). Already curation-blocked entries stay blocked.
Idempotent: re-running produces no change. DRY-RUN by default; --write commits.
"""
import os, sys, json, re, argparse
import openpyxl

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
HERE = os.path.dirname(os.path.abspath(__file__))
RES = os.path.join(HERE, '..', 'Beelzebub', 'Resources')
RULES = os.path.join(RES, 'ability_rules.default.json')
NAMES = os.path.join(RES, 'prefab_names.tsv')
META = os.path.join(RES, 'ability_metadata.json')
MATRIX = os.path.join(HERE, '..', '..', 'Reference Data', 'TESTING DATA', 'TESTER_ABILITY_MATRIX.xlsx')

TAG = '[v0.136 baseline]'

# D1 — tester-confirmed crash / character-break abilities (all also C#-hard-blocked).
HARD_TAGS = {
    1074442576: ('crash', 'destroys neighbouring plots (Sommelier Barrel Dance)'),
    22199616: ('crash', 'destroys neighbouring plots (Sommelier Horizontal Barrel)'),
    1129782597: ('crash', 'destroys neighbouring plots (Sommelier Horizontal Barrel Hard)'),
    2073002423: ('crash', 'GAME CRASH with vanilla sword-E (Bell Ringer Ring Bell)'),
    1431473799: ('stuck', 'stuck in the sky; leapheight clamp cannot reach its shared launch buff (Ice Ranger Tower Of Frost)'),
    2057952818: ('stuck', 'stuck in the sky; leapheight clamp cannot reach its shared launch buff (Ice Ranger Tower Of Frost Low Health)'),
    906463896: ('stuck', 'effect only removable by dying (Castle Man Holy Beam)'),
    583436571: ('stuck', 'effect only removable by dying (Castle Man Holy Beam Hard)'),
    874909393: ('stuck', 'effect only removable by dying (Castle Man Tripple Spinning Cross)'),
    2030404176: ('stuck', 'needs admin removal (Werewolf Chieftain Open The Cages)'),
}
# D3 exception — kept enabled under review per KDPen (V-Blood audit 2026-06-14).
KEEP_ENABLED = {1957691133}   # Dracula Bolt Spray
# D7 — explicit summon / range presets.
SUMMON = {
    -195933675: {'SummonCap': 1, 'SummonTimeoutSeconds': 30.0},   # Morgana Summon Tail
    1212947823: {'SummonTimeoutSeconds': 30.0},                   # Harpy Matriarch Soar
    -2034290170: {'SummonTimeoutSeconds': 60.0},                  # Undead Priest Raise Dead
}
RANGE = {1372353064: 20.0}   # Jade Caltrops Hard

# Re-audit (2026-09-23) — hand-read every tester finding again; these rows the matrix recs missed or under-fixed.
# Applied AFTER the matrix pass, so they win. enable: True = promote (tester finding is fixable by config),
# False = soft-off. FM = unroot at base cast time (same rule as fix_for). Knobs are only ADDED, never overwrite.
FM = 'FM'
REAUDIT = {
    # — promoted: blacklisted by the no-rec rule, but the tester finding is a config fix —
    -2010697707: (True,  {FM: 1, 'CooldownSeconds': 10.0}, 'Bandit Foreman Crossbow: rooted 3-4s, spammable (siblings already fixed)'),
    -1100933071: (True,  {FM: 1, 'CooldownSeconds': 10.0}, 'Bandit Foreman Crossbow BloodRage: rooted, spammable'),
    46962343:    (True,  {FM: 1, 'CooldownSeconds': 10.0}, 'Bandit Foreman Crossbow Hard: rooted, spammable'),
    -1326540020: (True,  {FM: 1, 'CooldownSeconds': 10.0}, 'Bandit Foreman Rapid Shot: rooted, spammable'),
    -833255541:  (True,  {FM: 1, 'CooldownSeconds': 10.0}, 'Carver Spew Corruption: no cooldown, locked (sibling Small already fixed)'),
    -1329409686: (True,  {FM: 1}, 'Valyr Bomber Duel Quake: locked in place too long'),
    51772774:    (True,  {FM: 1, 'CooldownSeconds': 10.0, 'DamageScale': 1.5}, 'Paladin Divine Rays: spammable, stuck, damage a bit low'),
    -1720407974: (True,  {'DamageScale': 1.5}, 'Paladin Dash: "awesome, highly recommended", damage very low'),
    -424388071:  (True,  {'CooldownSeconds': 20.0}, 'Matriarch AoE: ultimate-like, wants a longer cooldown'),
    953519927:   (True,  {'CooldownSeconds': 10.0}, 'Ice Ranger Ice Nova: no cooldown (sibling Large already fixed)'),
    919394375:   (True,  {FM: 1}, 'Nun AoE: locked during the cast'),
    1181965808:  (True,  {FM: 1, 'CooldownSeconds': 10.0}, 'Light Arrow Guided Arrow Hard: huge damage, 2.5s root, no cooldown'),
    -1243386667: (True,  {FM: 1}, 'Voltage Whirlwind: "could work", stuck while spinning'),
    840159262:   (True,  {}, 'Tourok Chaos Charge: "feels very usable" (sibling Rage Of Chaos enabled)'),
    -66831677:   (True,  {'CooldownScale': 0.5}, 'Bat Vampire Jump Strike: underwhelming for a 30s cooldown'),
    -845442935:  (True,  {'CooldownScale': 0.25}, 'Valyr Charge: underwhelming for a 120s cooldown (-> 30s)'),
    -212014657:  (True,  {'CooldownSeconds': 20.0}, 'Glassblower Glass Rain: GOOD, spammable'),
    890007919:   (True,  {'CooldownSeconds': 10.0}, 'Glassblower Mirror Shield: GOOD, spammable'),
    -225237355:  (True,  {}, 'Glassblower Wind Dash: works'),
    -1294182358: (True,  {'MaxRangeOverride': 20.0}, 'Jade Caltrops: no range limit (Hard sibling already clamped)'),
    1946293901:  (True,  {FM: 1}, 'Morgana Spectral Snake: stuck, "usable if tweaked"'),
    -496233120:  (True,  {'CooldownSeconds': 5.0}, 'Wendigo Ice Dash: GOOD, spammable'),
    -2029387940: (True,  {'CooldownSeconds': 5.0}, 'Wendigo Ice Dash Hard: GOOD, spammable'),
    1690919489:  (True,  {'CooldownSeconds': 6.0}, 'Manticore Jump Back: working dash, no cooldown'),
    1006960825:  (True,  {'DamageScale': 1.5}, 'High Lord Corpse Storm: "great ultimate", damage a bit low'),
    # — enabled, issue reported, no preset yet —
    1520734123:  (None,  {'CooldownSeconds': 10.0}, 'ArchMage Crystal Lance: no cooldown (sibling Charged already fixed)'),
    -820078889:  (None,  {'CooldownSeconds': 120.0}, 'Lightning Storm: tester "needs 100 or 120 cooldown"'),
    1795809188:  (None,  {'CooldownSeconds': 8.0}, 'Poloma Otherside: spammable invisibility + invulnerability'),
    2033547790:  (None,  {'CooldownSeconds': 8.0}, 'Poloma Otherside (ally): spammable invisibility + invulnerability'),
    1238313774:  (None,  {'CooldownSeconds': 8.0}, 'Wendigo Frost Nova: no cooldown'),
    495259674:   (None,  {'CooldownSeconds': 8.0}, 'Wendigo Ice Beam: no cooldown'),
    1387986806:  (None,  {'CooldownSeconds': 12.0}, 'Yeti Avalanche Hard 01: no cooldown (match Avalanche 12s)'),
    -1839296163: (None,  {'CooldownSeconds': 12.0}, 'Yeti Avalanche Hard 02: no cooldown (match Avalanche 12s)'),
    -1015932492: (None,  {'CooldownSeconds': 15.0}, 'Valyr Chase Target: 6s speed boost every 0.5s'),
    698366326:   (None,  {'CooldownSeconds': 5.0}, 'Purifier Jet Punch: "kinda powerful", no real cooldown'),
    -2015856259: (None,  {'CooldownSeconds': 8.0}, 'Militia Leader Leap Attack: knocks back even V-Bloods, no real cooldown'),
    # — configured, but a requested fix was missed —
    -552723816:  (None,  {'CooldownSeconds': 10.0}, 'Paladin Heal Angel: heal can be spammed'),
    88850785:    (None,  {'CooldownSeconds': 6.0}, 'Gargoyle Wing Shield Emerge: spammable'),
    -2109003637: (None,  {'DamageScale': 1.5}, 'Spider Queen PBAoE: "definitely needs some buffs"'),
    # — kept off, but prepared —
    830495620:   (None,  {'SummonTimeoutSeconds': 30.0}, 'Rat Vanguard: stays exploit-blocked; summon timeout prepared'),
    # — matrix row keyed by the UNIT id, so the verdict never landed —
    -281508142:  (False, {}, 'Zealous Cultist Ghastly Mockery: counter-stance lock (Soft verdict)'),
    -1749428209: (False, {}, 'Bandit Stalker Reinforcement: summons nothing (Soft verdict)'),
}


def load_names():
    g2n = {}
    for line in open(NAMES, encoding='utf-8'):
        p = line.rstrip('\n').split('\t')
        if len(p) >= 2:
            try: g2n[int(p[0])] = p[1]
            except ValueError: pass
    return g2n


def num(s):
    m = re.search(r'(\d+(?:\.\d+)?)', str(s))
    return float(m.group(1)) if m else None


def fix_for(g, gid, meta):
    """Return (knobs dict, skipped-reason list) from one matrix row's recommendations."""
    k, skipped = {}, []
    base = meta.get(str(gid), {})
    cd = g('Phase 2: Cfg Cooldown')
    if cd:
        if 'reduce' in str(cd): k['CooldownScale'] = 0.5
        else:
            v = num(cd)
            bcd = base.get('baseCooldown') or 0
            if v and v > bcd: k['CooldownSeconds'] = v
    if g('Phase 2: Cfg FreelyMove'):
        ct = base.get('baseCastTime')
        k['FreeMoveAfterSeconds'] = round(max(0.5, float(ct)), 2) if ct else 1.0
    if g('Phase 2: Cfg LeapHeight'):
        k['LeapHeight'] = 12.0
    ds = g('Phase 2: Cfg DamageScale')
    if ds:
        k['DamageScale'] = num(ds)
    ot = str(g('Phase 2: Cfg Other') or '')
    if gid in SUMMON: k.update(SUMMON[gid])
    if gid in RANGE: k['MaxRangeOverride'] = RANGE[gid]
    if 'i-frame' in ot: skipped.append('i-frames need code')
    if 'Duration' in ot: skipped.append('shorter effect needs code (duration knob is buff duration)')
    if 'CastSpeed' in ot: skipped.append('faster wind-up not shipped (casttime is experimental and would also speed up the boss)')
    return k, skipped


def set_note(e, text):
    old = e.get('Notes', '') or ''
    i = old.find(TAG)
    if i >= 0: old = old[:i].rstrip()
    e['Notes'] = (old + ' ' if old else '') + f'{TAG} {text}'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--write', action='store_true')
    a = ap.parse_args()

    raw = open(RULES, encoding='utf-8', newline='').read()   # newline='': keep CRLF so the diff stays exact
    rules = json.loads(raw)
    amap = rules['AbilityMap']
    g2n = load_names()
    meta = json.load(open(META, encoding='utf-8'))['abilities']
    rows = list(openpyxl.load_workbook(MATRIX, read_only=True).worksheets[0].iter_rows(values_only=True))
    ix = {h: i for i, h in enumerate(rows[0])}

    stats = {k: 0 for k in ('hard', 'configured', 'configured_but_gated', 'blacklisted', 'enable_tuned', 'unresolved')}
    changes, unresolved = [], []
    for r in rows[1:]:
        g = lambda c: r[ix[c]]
        fin = g('FINAL: ShipDecision')
        if not fin: continue
        try: gid = int(g('ID'))
        except (TypeError, ValueError): continue
        name = g2n.get(gid)
        if not name or not name.startswith('AB_'):
            unresolved.append((gid, name, fin)); stats['unresolved'] += 1; continue
        before = json.dumps(amap.get(name), sort_keys=True)
        e = amap.setdefault(name, {})
        finding = (str(g('Phase 2: Finding') or g('Phase 1: TesterFinding') or '')).replace('\n', ' ').strip()
        finding = finding[:160] + ('…' if len(finding) > 160 else '')
        gated = e.get('Enabled') is False or e.get('ReviewStatus') in ('Blocked', 'Hidden')

        if fin == 'Hard-Disable' or gid in HARD_TAGS:
            if gid not in HARD_TAGS and gated: continue   # already blocked by an earlier pass — leave it
            tag, why = HARD_TAGS.get(gid, ('stuck', finding))
            e['Enabled'] = False; e['ReviewStatus'] = 'Blocked'; e['ReviewTag'] = tag
            set_note(e, f'HARD-BLOCKED (tester): {why}')
            stats['hard'] += 1
        else:
            knobs, skipped = fix_for(g, gid, meta)
            for kk, vv in knobs.items():
                if kk not in e: e[kk] = vv
            if fin == 'Soft-Disable':
                if knobs:
                    stats['configured_but_gated' if gated else 'configured'] += 1
                    set_note(e, 'Soft verdict fixed by preset: ' + ', '.join(f'{kk}={vv}' for kk, vv in knobs.items())
                             + (f"; not shipped: {'; '.join(skipped)}" if skipped else '') + f'. Tester: {finding}')
                elif gid in KEEP_ENABLED:
                    set_note(e, f'Soft verdict — kept ENABLED under review. Tester: {finding}')
                else:
                    if not gated: stats['blacklisted'] += 1
                    e['Enabled'] = False
                    set_note(e, 'Soft-disabled (admin can re-enable)' + (f"; needs: {'; '.join(skipped)}" if skipped else '')
                             + f'. Tester: {finding}')
            elif knobs:
                stats['enable_tuned'] += 1
                set_note(e, 'Tester-suggested preset: ' + ', '.join(f'{kk}={vv}' for kk, vv in knobs.items())
                         + (f"; not shipped: {'; '.join(skipped)}" if skipped else ''))
            elif not e:
                del amap[name]; continue
        if json.dumps(e, sort_keys=True) != before: changes.append(name)

    stats['reaudit'] = 0
    for gid, (enable, knobs, why) in REAUDIT.items():
        name = g2n.get(gid)
        if not name or not name.startswith('AB_'):
            unresolved.append((gid, name, 'REAUDIT')); continue
        before = json.dumps(amap.get(name), sort_keys=True)
        e = amap.setdefault(name, {})
        k = {}
        for kk, vv in knobs.items():
            if kk == FM:
                ct = meta.get(str(gid), {}).get('baseCastTime')
                k['FreeMoveAfterSeconds'] = round(max(0.5, float(ct)), 2) if ct else 1.0
            else: k[kk] = vv
        for kk, vv in k.items():
            if kk not in e: e[kk] = vv
            elif e[kk] != vv: print(f'  REAUDIT keeps existing {name}.{kk}={e[kk]} (wanted {vv})')
        if enable is True and e.get('ReviewStatus') not in ('Blocked', 'Hidden'): e.pop('Enabled', None)
        elif enable is False: e['Enabled'] = False
        preset = ', '.join(f'{kk}={vv}' for kk, vv in k.items())
        verb = {True: 'Re-audit: enabled with preset', None: 'Re-audit preset', False: 'Re-audit: soft-disabled'}[enable]
        set_note(e, f'{verb}' + (f': {preset}' if preset else '') + f'. Tester: {why}')
        stats['reaudit'] += 1
        if json.dumps(e, sort_keys=True) != before and name not in changes: changes.append(name)

    print('stats:', stats)
    print(f'entries changed: {len(changes)}')
    for u in unresolved: print('  unresolved (not an AB_ group):', u)
    out = json.dumps(rules, indent=2, ensure_ascii=False)
    crlf = '\r\n' in raw
    if crlf: out = out.replace('\n', '\r\n')   # keep the file line endings
    if raw.endswith('\n') and not out.endswith('\n'): out += '\r\n' if crlf else '\n'
    if out == raw: print('no change (idempotent)'); return
    if a.write:
        open(RULES, 'w', encoding='utf-8', newline='').write(out)
        print('WRITTEN', RULES)
    else:
        print('dry run — pass --write to commit')


if __name__ == '__main__':
    main()
