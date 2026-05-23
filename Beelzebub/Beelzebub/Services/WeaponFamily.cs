namespace Beelzebub.Services;

/// <summary>
/// W1: classification of a captured ability by the weapon family that it
/// naturally belongs to. Determines how slot precedence resolves the ability:
///   - Magic / Unarmed: treated as universal — usable from any spell slot,
///     wins on slots that don't have a weapon-specific grant for the
///     currently-equipped weapon.
///   - A concrete weapon family (Sword, Crossbow, ...): wins ONLY when the
///     player is wielding that weapon. Lets a player build a "sword loadout"
///     that activates only with a sword.
///
/// Source: name-substring inference (see WeaponFamilyClassifier) + admin
/// curation in `ability_rules.json`'s `WeaponFamilyMap`. We can't read this
/// from prefab data — V Rising doesn't bake weapon classification onto
/// CHAR_ or AB_ prefabs (see W0 probe memory).
/// </summary>
internal enum WeaponFamily : byte
{
    None        = 0,   // unclassified — treated as Magic for grant purposes
    Magic       = 1,   // spells, projectiles, AoEs, blood / fire / frost / etc.
    Unarmed     = 2,   // base vampire fists / unarmed melee
    Sword       = 3,
    GreatSword  = 4,
    Axe         = 5,
    Mace        = 6,
    DualHammers = 7,
    Spear       = 8,
    Daggers     = 9,
    Crossbow    = 10,
    Longbow     = 11,
    Pistols     = 12,
    Reaper      = 13,
    Whip        = 14,
    Claws       = 15,
    // Added from prefab dump scan 2026-05-21:
    Pollaxe     = 16,
    Slashers    = 17,
    TwinBlades  = 18,
    FishingPole = 19,
}

/// <summary>
/// Native shapeshift forms Beelzebub can apply to the player on .beelz transform.
/// Matches the heuristic in ShapeshiftService (Wolf, Bear, Rat, Spider, Toad).
/// Used in `AbilityMap[*].Forms` to gate which abilities fire while transformed
/// into a given form.
/// </summary>
internal enum ShapeshiftForm : byte
{
    None   = 0,
    Wolf   = 1,
    Bear   = 2,
    Rat    = 3,
    Spider = 4,
    Toad   = 5,
}
