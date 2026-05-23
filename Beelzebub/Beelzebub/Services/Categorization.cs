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
        // v0.20.1: dropped the `_Ultimate_` check — zero V Rising prefabs use that
        // token (audit verified). Bosses' signature moves are named after the move
        // itself (MountainRumbler, ChainBolt, BloodCurse) — not flagged as ultimates
        // in the prefab system. Most-specific patterns still go first.
        if (Has(abilityName, "_Travel_") || Has(abilityName, "_Dash_") || Has(abilityName, "_Hop_")
            || Has(abilityName, "_Teleport_") || Has(abilityName, "_FleshWarp_")) return AbilityCategory.Travel;
        if (Has(abilityName, "_Summon_")) return AbilityCategory.Summon;
        if (Has(abilityName, "_GroundSlam_") || Has(abilityName, "_Slam_") || Has(abilityName, "_Stomp_")
            || Has(abilityName, "_Bomb_") || Has(abilityName, "_Explosion_") || Has(abilityName, "_CorpseParty_")
            || Has(abilityName, "_Aoe_") || Has(abilityName, "_Nova_")) return AbilityCategory.Aoe;
        if (Has(abilityName, "_Projectile_") || Has(abilityName, "_Throw_") || Has(abilityName, "_Bolt_")
            || Has(abilityName, "_Shoot_") || Has(abilityName, "_Volley_") || Has(abilityName, "_ChainBolt_")
            || Has(abilityName, "_Arrow_") || Has(abilityName, "_Spit_")) return AbilityCategory.Projectile;
        if (Has(abilityName, "_SelfBuff_") || Has(abilityName, "_Stance_")
            || (Has(abilityName, "_Buff_") && !Has(abilityName, "Debuff"))) return AbilityCategory.Buff;
        // WeaponSpell heuristic: ability prefab named after a vampire weapon
        // school (Chaos/Frost/Blood/Storm/Illusion/Unholy/Sword/Axe/etc.) and
        // not yet bucketed = the weapon's Q/E ability.
        if (HasAny(abilityName,
            "_ChaosVolley_", "_ChaosBarrier_", "_FrostBarrier_", "_FrostBat_", "_IceNova_",
            "_BloodRite_", "_BloodRage_", "_StormShield_", "_LightningStrike_",
            "_Mosquito_", "_MistTrance_", "_VeilOf", "_DeathKnight_",
            "_SwordSlash_", "_SwordCharge_", "_AxeAttack_", "_MaceStomp_",
            "_CrossbowSnipe_", "_LongbowAimedShot_", "_SpearLunge_", "_DaggerStab_")) return AbilityCategory.WeaponSpell;
        // Anything with "_Cast" but no narrower bucket = a spell.
        if (Has(abilityName, "_Cast") || Has(abilityName, "_Spell")) return AbilityCategory.Spell;
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
