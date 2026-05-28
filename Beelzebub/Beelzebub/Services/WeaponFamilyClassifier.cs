using System;
using System.Collections.Generic;

namespace Beelzebub.Services;

/// <summary>
/// W1: classify an ability prefab name into a WeaponFamily by name-substring.
/// Used as the fallback when `AbilityMap` in ability_rules.json doesn't have
/// an admin entry for the ability. Admin entries (via <see cref="AbilityRules.ClassifyWeaponFamilies"/>)
/// can return multiple families; this classifier returns a single family.
///
/// W0 finding: V Rising does NOT bake a weapon-type field onto AB_ or CHAR_
/// prefabs, so we can't read this directly. See `project_w0_weapon_data_probe`
/// memory. Themed bosses (Paladin, Solarus, Beatrice, ...) need admin curation.
/// </summary>
internal static class WeaponFamilyClassifier
{
    // Order matters — earlier entries are tested first. Always test the more
    // specific token before the more generic one (e.g. GreatSword before Sword).
    // NOTE (v0.49.0): no DualHammers rows — that weapon is unobtainable cut content
    // (see WeaponFamily.DualHammers), so its abilities fall through to Magic (universal)
    // and stay usable on any bar rather than being gated to a weapon nobody can equip.
    static readonly (string Token, WeaponFamily Family)[] _heuristics =
    {
        ("_GreatSword_", WeaponFamily.GreatSword),
        ("GreatSword",   WeaponFamily.GreatSword),
        ("_Crossbow_",   WeaponFamily.Crossbow),
        ("Crossbow",     WeaponFamily.Crossbow),
        ("_Longbow_",    WeaponFamily.Longbow),
        ("Longbow",      WeaponFamily.Longbow),
        ("_Pistols_",    WeaponFamily.Pistols),
        ("Pistols",      WeaponFamily.Pistols),
        ("_Pistol_",     WeaponFamily.Pistols),
        ("_Daggers_",    WeaponFamily.Daggers),
        ("Daggers",      WeaponFamily.Daggers),
        ("_Dagger_",     WeaponFamily.Daggers),
        ("_Reaper_",     WeaponFamily.Reaper),
        ("Reaper",       WeaponFamily.Reaper),
        ("_Scythe_",     WeaponFamily.Reaper),
        ("_Whip_",       WeaponFamily.Whip),
        ("Whip",         WeaponFamily.Whip),
        ("_Claws_",      WeaponFamily.Claws),
        ("Claws",        WeaponFamily.Claws),
        ("_Spear_",      WeaponFamily.Spear),
        ("Spear",        WeaponFamily.Spear),
        ("_Lance_",      WeaponFamily.Spear),
        ("_Axe_",        WeaponFamily.Axe),
        ("Axe",          WeaponFamily.Axe),
        ("_Cleaver_",    WeaponFamily.Axe),
        ("_Mace_",       WeaponFamily.Mace),
        ("Mace",         WeaponFamily.Mace),
        ("_Hammer_",     WeaponFamily.Mace),
        ("_Sword_",      WeaponFamily.Sword),
        ("Sword",        WeaponFamily.Sword),
        ("_Slash_",      WeaponFamily.Sword),
        ("_Bow_",        WeaponFamily.Longbow),
        ("Shortbow",     WeaponFamily.Longbow),
        ("_Bow",         WeaponFamily.Longbow),
        // Magic / spell tells
        ("_Spell_",      WeaponFamily.Magic),
        ("_Magic_",      WeaponFamily.Magic),
        ("_Nuke_",       WeaponFamily.Magic),
        ("_Bolt_",       WeaponFamily.Magic),
        ("_Frost_",      WeaponFamily.Magic),
        ("_Fire_",       WeaponFamily.Magic),
        ("_Holy_",       WeaponFamily.Magic),
        ("_Unholy_",     WeaponFamily.Magic),
        ("_Blood",       WeaponFamily.Magic),
        ("_Storm_",      WeaponFamily.Magic),
        ("_Lightning_",  WeaponFamily.Magic),
        ("_Curse_",      WeaponFamily.Magic),
        ("_Hex_",        WeaponFamily.Magic),
        // Unarmed / fists
        ("_Unarmed_",    WeaponFamily.Unarmed),
        ("_Fist_",       WeaponFamily.Unarmed),
        ("_Punch_",      WeaponFamily.Unarmed),
    };

    public static WeaponFamily Classify(
        string abilityPrefabName,
        IReadOnlyDictionary<string, string> adminOverrides = null)
    {
        if (string.IsNullOrEmpty(abilityPrefabName)) return WeaponFamily.None;

        // Tier 1 (deprecated path; kept so older test callers compile):
        // simple Dictionary<string,string> override.
        if (adminOverrides is not null
            && adminOverrides.TryGetValue(abilityPrefabName, out string overrideValue)
            && Enum.TryParse(overrideValue, ignoreCase: true, out WeaponFamily resolved))
        {
            return resolved;
        }

        // Tier 2: name-substring heuristic.
        foreach (var (token, family) in _heuristics)
        {
            if (abilityPrefabName.Contains(token, StringComparison.OrdinalIgnoreCase))
                return family;
        }

        // Tier 3: default. Magic = universal, fits the existing "spells live on 5/6" behavior.
        return WeaponFamily.Magic;
    }
}
