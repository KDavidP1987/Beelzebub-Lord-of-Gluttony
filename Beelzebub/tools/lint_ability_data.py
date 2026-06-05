"""
C2 (prep-roadmap) — ability-data VALIDATION / LINT.

Catches inconsistencies in the two shipped ability-data sources BEFORE packaging, so a
bad token or an orphan rule never ships. Meant to run in preflight (non-zero exit on any
ERROR; WARNINGs are advisory and never fail the build).

Checks:
  ERROR
    - orphan rule        : an AbilityMap key (prefab name) that resolves to NO known prefab
                           GUID and has no metadata entry (typo / renamed / cut ability).
    - invalid weapon     : a Weapons token not in the WeaponFamily set (after stripping '!').
    - invalid form       : a Forms token not in the ShapeshiftForm set (after stripping '!').
    - invalid status     : a ReviewStatus not in the canonical set.
    - invalid category   : a Category override not in the AbilityCategory set.
    - invalid difficulty : a Difficulty not Basic|Brutal.
    - bad phase          : a Phase < 1.
    - duplicate key      : the same AbilityMap key appears twice in the raw JSON.
  WARNING
    - broken+enabled     : metadata.incompatible == true but the rule leaves Enabled == true
                           (a flagged-broken ability still shippable — confirm intent).
    - dangling condition : conditionModifiers/conditionSource present but condition empty.

Mirrors the C# token sets in Services/AbilityRules.cs / WeaponFamily / ShapeshiftForm /
Categorization. Keep them in sync if the enums change.

Usage:  python lint_ability_data.py        # exit 0 = clean, 1 = errors found
Read-only.
"""
import json, os, re, sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(__file__)
RES  = os.path.join(HERE, '..', 'Beelzebub', 'Resources')
META = os.path.join(RES, 'ability_metadata.json')
RULES = os.path.join(RES, 'ability_rules.default.json')
NAMES = os.path.join(RES, 'prefab_names.tsv')

WEAPONS = {'Sword', 'GreatSword', 'Axe', 'Mace', 'Spear', 'Daggers', 'Crossbow', 'Longbow',
           'Pistols', 'Reaper', 'Whip', 'Claws', 'Pollaxe', 'Slashers', 'TwinBlades',
           'FishingPole', 'Unarmed', 'Magic'}
FORMS = {'Wolf', 'Bear', 'Rat', 'Spider', 'Toad', 'Werewolf', 'Gargoyle', 'Mounted'}
STATUSES = {'Unreviewed', 'Reviewed', 'Approved', 'Blocked', 'Hidden'}
# Canonical ReviewTags (mirror AbilityRules.ReviewTags). Free-text is allowed, so an unknown tag is a
# WARNING (a coined use-case group), not an error — but a typo of a known tag is worth surfacing.
REVIEW_TAGS = {'emote', 'feed', 'idle_flee', 'variant_hard', 'variant_gateboss', 'variant_minion',
               'basic_attack', 'reaction', 'combo', 'summon', 'lifecycle', 'dev',
               'crash', 'stuck', 'broken', 'exploit'}
CATEGORIES = {'Travel', 'Aoe', 'Projectile', 'Melee', 'Summon', 'Buff', 'WeaponSpell', 'Spell', 'Other'}
DIFFICULTIES = {'Basic', 'Brutal'}

# lower-case set membership (tokens are accepted case-insensitively by the C# parser)
WEAPONS_L = {w.lower() for w in WEAPONS}
FORMS_L = {f.lower() for f in FORMS}
STATUSES_L = {s.lower() for s in STATUSES}
CATEGORIES_L = {c.lower() for c in CATEGORIES}
DIFFICULTIES_L = {d.lower() for d in DIFFICULTIES}


def load_names(path):
    names = set()
    with open(path, encoding='utf-8') as f:
        for line in f:
            if '\t' in line:
                names.add(line.rstrip('\n').split('\t', 1)[1].strip())
    return names


def find_duplicate_keys(path):
    """Raw-scan AbilityMap for duplicate prefab keys (json.load silently keeps the last)."""
    text = open(path, encoding='utf-8').read()
    m = re.search(r'"AbilityMap"\s*:\s*\{', text)
    if not m:
        return []
    keys = re.findall(r'"([^"]+)"\s*:\s*\{', text[m.end():])
    return [k for k, n in Counter(keys).items() if n > 1]


def main():
    meta = json.load(open(META, encoding='utf-8'))['abilities']
    rules = json.load(open(RULES, encoding='utf-8'))
    valid_names = load_names(NAMES)
    name_to_guid = {}
    # build prefab-name -> guid from the dump for the broken+enabled cross-check
    with open(NAMES, encoding='utf-8') as f:
        for line in f:
            if '\t' in line:
                g, n = line.rstrip('\n').split('\t', 1)
                name_to_guid.setdefault(n.strip(), g.strip())

    amap = rules.get('AbilityMap', {})
    errors, warnings = [], []

    for k in find_duplicate_keys(RULES):
        errors.append(f'duplicate key: "{k}" appears more than once in AbilityMap')

    for name, e in amap.items():
        e = e or {}
        # orphan: key resolves to no real prefab and has no metadata row by that name's guid
        guid = name_to_guid.get(name)
        if name not in valid_names and (guid is None or guid not in meta):
            errors.append(f'orphan rule: "{name}" matches no known prefab GUID and no metadata entry')

        for w in e.get('Weapons', []) or []:
            tok = w[1:] if w.startswith('!') else w
            if tok.lower() not in WEAPONS_L:
                errors.append(f'invalid weapon: "{w}" on "{name}" (valid: {", ".join(sorted(WEAPONS))})')
        for fm in e.get('Forms', []) or []:
            tok = fm[1:] if fm.startswith('!') else fm
            if tok.lower() not in FORMS_L:
                errors.append(f'invalid form: "{fm}" on "{name}" (valid: {", ".join(sorted(FORMS))})')

        rs = e.get('ReviewStatus')
        if rs is not None and rs.lower() not in STATUSES_L:
            errors.append(f'invalid status: "{rs}" on "{name}" (valid: {", ".join(sorted(STATUSES))})')

        rt = e.get('ReviewTag')
        if rt and rt not in REVIEW_TAGS:
            warnings.append(f'non-canonical reviewTag: "{rt}" on "{name}" (known: {", ".join(sorted(REVIEW_TAGS))})')

        cat = e.get('Category')
        if cat and cat.lower() not in CATEGORIES_L:
            errors.append(f'invalid category: "{cat}" on "{name}" (valid: {", ".join(sorted(CATEGORIES))})')

        diff = e.get('Difficulty')
        if diff is not None and diff.lower() not in DIFFICULTIES_L:
            errors.append(f'invalid difficulty: "{diff}" on "{name}" (valid: Basic, Brutal)')

        if e.get('Phase') is not None and isinstance(e['Phase'], int) and e['Phase'] < 1:
            errors.append(f'bad phase: {e["Phase"]} on "{name}" (must be >= 1)')

        # WARNING: flagged broken in metadata but still capturable (Enabled AND not gated by reviewStatus).
        # reviewStatus Blocked/Hidden gates capture via Curation_EnforceReviewStatus (v0.115), so it counts
        # as "not capturable" here — only warn when the ability is genuinely still reachable by players.
        if (guid and guid in meta and meta[guid].get('incompatible')
                and e.get('Enabled', True)
                and e.get('ReviewStatus') not in ('Blocked', 'Hidden')):
            warnings.append(f'broken+enabled: "{name}" is metadata.incompatible but still capturable '
                            f'(Enabled, reviewStatus={e.get("ReviewStatus","Unreviewed")}) — confirm intent')

    # dangling-condition warnings (metadata side)
    for guid, m in meta.items():
        if not m.get('condition') and (m.get('conditionModifiers') or m.get('conditionSource')):
            warnings.append(f'dangling condition: GUID {guid} ("{m.get("name","")}") has '
                            f'conditionModifiers/conditionSource but no condition')

    print('=== ability-data lint ===')
    print(f'  AbilityMap entries: {len(amap)}   metadata abilities: {len(meta)}')
    if errors:
        print(f'\nERRORS ({len(errors)}):')
        for e in errors:
            print(f'  ✗ {e}')
    if warnings:
        print(f'\nWARNINGS ({len(warnings)}):')
        for w in warnings:
            print(f'  ! {w}')
    if not errors and not warnings:
        print('  clean — no issues.')
    elif not errors:
        print(f'\nOK (no errors; {len(warnings)} warning(s)).')

    sys.exit(1 if errors else 0)


if __name__ == '__main__':
    main()
