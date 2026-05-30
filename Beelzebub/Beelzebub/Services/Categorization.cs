using System;

namespace Beelzebub.Services;

/// <summary>
/// IN2 (v0.15.1): pattern-based derivations BCH can use as badges in its
/// collection UI. Pure substring classification — no AbilityMap lookup, no
/// dependencies on Core. Safe to call before Core.IsReady.
///
/// These exist because the raw `AB_*` / `CHAR_*` name doesn't tell BCH much
/// without parsing. The classifier shrinks each name to a short enum value
/// BCH can switch on for icons / colors / tooltip lines.
///
/// Ordering inside each method goes most-specific → least-specific so a
/// `Wolf_VBlood` lands in Beast before Vampire, etc.
/// </summary>
internal enum AbilityCategory : byte
{
    Other = 0,
    Travel = 1,         // dash / teleport / hop
    // Note: value 2 (`Ultimate`) was removed in v0.20.1. Zero V Rising prefabs
    // use the `_Ultimate_` token, so the classifier never emitted it. If a
    // future heuristic identifies signature/boss-tier abilities (e.g. via
    // `_VBlood_` infix), reintroduce as a renamed value here; gap value 2
    // intentionally left as a reservation for future signal.
    Aoe = 3,            // ground slam, corpse explosion, bombs
    Projectile = 4,     // ranged spells, bolts, throws, shots
    Summon = 5,         // _Summon_ / _Reinforcement_ / _CallReinforcements_
    Buff = 6,           // self-buff / stance toggles
    WeaponSpell = 7,    // Q/E weapon-tied spell (sword, axe, mace school spells)
    Spell = 8,          // catch-all spell that didn't match a narrower bucket
    Melee = 9,          // v0.51.0: melee/weapon strike (slash/cleave/strike/smash/bite/…)
}

internal enum TransformType : byte
{
    Other = 0,
    Vampire = 1,
    Undead = 2,         // skeletons, banshees, wraiths, bone-creatures
    Beast = 3,          // wolf, bear, spider, rat, toad, boar, forest mobs
    Humanoid = 4,       // bandits, villagers, church/cultist humans, militia
    Construct = 5,      // golem, treant, statue
    Demon = 6,          // dracula, solarus, named demonic bosses
}

/// <summary>
/// TX7 (v0.17.0): how a transform sources its power profile. Set globally via
/// the `Transform_PowerScalingMode` config, overridable per-unit via the
/// `PowerScalingMode` field on a TransformMap entry. See AbilityRules.
/// </summary>
internal enum PowerScalingMode : byte
{
    /// <summary>
    /// Apply admin-curated DamageScale / CooldownScale / HealthScale /
    /// MovementSpeedScale from the TransformMap entry as
    /// ModifyUnitStatBuff_DOTS overlays on the carrier buff. The TX6
    /// (v0.15.0) behavior. Player's natural stats still multiply the
    /// captured ability's base damage on top.
    /// </summary>
    CuratedScales = 0,

    /// <summary>
    /// Read the CHAR_ prefab's UnitStats component (PhysicalPower,
    /// SpellPower, MaxHealth, MovementSpeed, etc.) and apply matching
    /// boss-tier stats to the player via the carrier buff. Player hits
    /// as hard as the boss. Use for "true boss form" servers.
    /// </summary>
    PrefabAbsolute = 1,

    /// <summary>
    /// Apply no overrides at all. V Rising's vanilla damage formula
    /// scales the captured ability's base damage by the player's own
    /// PhysicalPower/SpellPower, automatically including any Bloodcraft
    /// expertise / blood-quality / level buffs the player carries. The
    /// ability's effective output naturally tracks player progression
    /// and Bloodcraft prestige resets.
    /// </summary>
    PlayerScaled = 2,

    /// <summary>
    /// v0.19.0: hybrid of PrefabAbsolute + player-level scaling. Reads the
    /// CHAR_ prefab's UnitStats (PhysicalPower / SpellPower / MaxHealth /
    /// resistances) AND the player's current UnitLevel, then applies the
    /// boss stats multiplied by a level-progression factor. At low player
    /// level the bonus is small (transforms feel weak), at max level
    /// the bonus approaches the full boss-tier value (transforms hit
    /// boss-tier hard). The factor curve is:
    /// <code>factor = clamp(player_level / Transform_PlayerLeveled_MaxLevel, 0, 1)</code>
    /// Bloodcraft sets the player's effective UnitLevel via its own buffs,
    /// so on a Bloodcraft server the factor tracks Bloodcraft progression
    /// (and prestige resets reduce it). On a vanilla server it tracks
    /// gear level. The mode admins want when they say "I want my
    /// transformation to be powerful but ONLY if my character is
    /// powerful too."
    /// </summary>
    PlayerLeveled = 3,
}

internal static class Categorization
{
    public static AbilityCategory ClassifyAbility(string abilityName)
    {
        if (string.IsNullOrEmpty(abilityName)) return AbilityCategory.Other;
        // v0.51.0: broadened heuristics + a magic-school fallback to drastically cut the
        // share of abilities that landed in `Other`. Many V Rising abilities are named after
        // the MOVE (CrossWindSlash, LoomingMists, MountainRumbler) rather than a tagged token,
        // and don't use strict `_Token_` boundaries — so we match common substrings (un-anchored
        // where unambiguous) and, most-specific first, fall back to the school name. An admin
        // `Category` override (ability_rules.json) takes precedence at the call site.

        // Travel / movement.
        if (HasAny(abilityName, "Travel", "_Dash", "_Hop", "Teleport", "FleshWarp", "Leap",
            "Blink", "Waypoint", "Recall", "PhaseShift", "_Roll_", "Vanish")) return AbilityCategory.Travel;

        // Summons / reinforcements.
        if (HasAny(abilityName, "Summon", "Reinforcement", "RaiseDead", "RaiseHorde",
            "Conjure", "CallBats")) return AbilityCategory.Summon;

        // v0.57.0: primary / basic / heavy attacks — route by weapon CLASS so a ranged-weapon
        // primary (crossbow / longbow / pistols / a ranger's bow) reads as a Projectile while a
        // melee-weapon or creature primary reads as Melee. This rescues a big chunk of the old
        // `Other` bucket (bare "Attack"/"Primary" names) without the weapon-blind mis-bucketing
        // of dumping every "Attack" into Melee. Checked before the Aoe/Projectile/Melee token
        // sweeps; more-specific names like "_SlamAttack" still hit "_Slam" → Aoe first only if
        // they carry that token (they don't match the primary tokens here).
        if (HasAny(abilityName, "Primary", "_Attack", "AutoAttack", "BasicAttack", "HeavyAttack"))
        {
            return HasAny(abilityName, "Crossbow", "Longbow", "Pistol", "_Bow", "Rifle",
                "Railgun", "Blowpipe", "Ranger", "Archer", "Gunner")
                ? AbilityCategory.Projectile
                : AbilityCategory.Melee;
        }

        // Area effects (checked before Melee so a "Slam"/"Stomp" lands as AoE).
        // v0.57.0: "_Field" catches ground fields (FieldOfSpears, ElectricField, ShockField).
        if (HasAny(abilityName, "GroundSlam", "_Slam", "Stomp", "Bomb", "Explosion", "CorpseParty",
            "_Aoe", "Nova", "Eruption", "Quake", "Rumble", "Meteor", "_Cone", "_Ring", "Crater",
            "Detonate", "Implo", "ShockWave", "Shockwave", "_Field")) return AbilityCategory.Aoe;

        // Projectiles / ranged.
        // v0.57.0: "Pistol"/"Fireball"/"Discharge" added — "Discharge" also pulls electric-discharge
        // moves OUT of Melee (the existing "Charge" token used to swallow "Discharge").
        if (HasAny(abilityName, "Projectile", "Throw", "Bolt", "Shoot", "Volley", "Arrow", "Spit",
            "Beam", "Breath", "Barrage", "Snipe", "_Shot", "Javelin", "Missile", "Dart", "_Spear_Lunge",
            "Spike", "Shard", "Pistol", "Fireball", "Discharge")) return AbilityCategory.Projectile;

        // Melee / weapon strikes (v0.51.0 new bucket — was the biggest source of "Other").
        // v0.57.0: bare "Melee" + unarmed strikes (Kick/Punch/Headbutt) added.
        if (HasAny(abilityName, "MeleeAttack", "Melee", "Slash", "Cleave", "Strike", "Swing", "Smash",
            "Stab", "Thrust", "Whirl", "Slice", "_Chop", "Bash", "Hack", "Rend", "Gore", "_Bite",
            "Maul", "Sweep", "Spin", "Charge", "Claw", "Pierce", "Impale", "Lunge",
            "Kick", "Punch", "Headbutt")) return AbilityCategory.Melee;

        // Self-buffs / defensive / stances.
        if (HasAny(abilityName, "SelfBuff", "Stance", "Aura", "Shield", "Barrier", "Ward",
            "Empower", "_Rage", "Heal", "VeilOf", "Cloak", "Bless", "Regen", "Fortify", "Frenzy",
            "Berserk", "Guard", "Block", "Parry")
            && !Has(abilityName, "Debuff")) return AbilityCategory.Buff;

        // WeaponSpell heuristic: ability prefab named after a vampire weapon school's Q/E.
        if (HasAny(abilityName,
            "ChaosVolley", "ChaosBarrier", "FrostBarrier", "FrostBat", "IceNova",
            "BloodRite", "BloodRage", "StormShield", "LightningStrike",
            "Mosquito", "MistTrance", "DeathKnight",
            "SwordSlash", "SwordCharge", "AxeAttack", "MaceStomp",
            "CrossbowSnipe", "LongbowAimedShot", "SpearLunge", "DaggerStab")) return AbilityCategory.WeaponSpell;

        // Anything with "_Cast"/"Spell" but no narrower bucket = a spell.
        if (Has(abilityName, "_Cast") || Has(abilityName, "Spell")) return AbilityCategory.Spell;

        // Magic-school fallback: most remaining boss/V-Blood signature moves are themed spells.
        if (HasAny(abilityName, "Frost", "_Fire", "Blood", "Chaos", "Storm", "Unholy", "_Holy",
            "Illusion", "Shadow", "Curse", "_Hex", "_Ice", "Lightning", "Mist", "_Bone", "Poison",
            "Plague", "Soul", "Void", "Spectral", "Corrupt", "Wisp", "Bat", "Crystal", "Sun",
            "Light")) return AbilityCategory.Spell;

        return AbilityCategory.Other;
    }

    public static TransformType ClassifyTransform(string charName)
    {
        if (string.IsNullOrEmpty(charName)) return TransformType.Other;

        // Demon bosses first — named-boss patterns supersede their broader categories
        // (Dracula is a Vampire by family, but Demon-tier in the UI; Solarus is a
        // Humanoid by frame, but Demon-tier mechanically).
        if (HasAny(charName, "_Dracula_", "_Solarus_", "_Adam_", "_Manticore_")) return TransformType.Demon;

        // Construct: golem, treant, statue
        if (HasAny(charName, "_Golem_", "_Treant_", "_Statue_", "_Geomancer_")) return TransformType.Construct;

        // Undead: skeleton/bone/banshee/wraith/ghost/zombie/lich
        if (HasAny(charName, "_Undead_", "_Skeleton_", "_Bone_", "_Banshee_",
            "_Wraith_", "_Ghost_", "_Zombie_", "_Lich_", "_Ghoul_", "_Mummy_")) return TransformType.Undead;

        // Beast (also catches Forest_Wolf_VBlood and similar)
        if (HasAny(charName, "_Wolf_", "_Werewolf_", "_Bear_", "_Boar_", "_Spider_",
            "_Rat_", "_Toad_", "_Frog_", "_Forest_", "_Beast_", "_Pig_",
            "_Crow_", "_Mosquito_", "_Wisp_")) return TransformType.Beast;

        // Vampire (covers V Rising "Vampire_" prefab names + a few demonic bloodline ones)
        if (Has(charName, "_Vampire_")) return TransformType.Vampire;

        // Humanoid (after Vampire so Vampire_Bandit_ stays Vampire)
        if (HasAny(charName, "_Bandit_", "_Villager_", "_ChurchOfLight_", "_Paladin_",
            "_Priest_", "_Cultist_", "_Militia_", "_Soldier_", "_Guard_",
            "_Crossbower_", "_Inquisitor_", "_Mercenary_", "_Brigand_")) return TransformType.Humanoid;

        return TransformType.Other;
    }

    static bool Has(string source, string token) =>
        source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    static bool HasAny(string source, params string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
            if (Has(source, tokens[i])) return true;
        return false;
    }
}
