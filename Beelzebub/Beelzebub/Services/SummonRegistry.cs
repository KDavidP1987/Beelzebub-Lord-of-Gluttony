using System;
using System.Collections.Generic;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.43.3 — signature-summon registry for the manual <c>.beelz summon</c> command.
///
/// Most V Rising bosses spawn adds NOT via a slot ability but as part of a
/// health-threshold "soft phase" (<c>Script_ApplyBuffUnderHealthThreshold*</c> →
/// phase buff → the boss's BehaviourTree casts the adds). Those summon abilities are
/// therefore NOT on the unit's <c>AbilityGroupSlotBuffer</c>, so the transform never
/// captures them and they can't be slotted. We surface them anyway by FORCE-CASTING
/// the summon ability directly (<see cref="ForceCastService"/>, no slot needed) — the
/// spawned minions are caught + allied by the normal summon pipeline
/// (LinkMinionToOwnerOnSpawn / <see cref="SummonAllyService"/>).
///
/// Entries are matched by a case-insensitive SUBSTRING of the transformed unit's CHAR_
/// prefab name (same approach as <see cref="ShapeshiftService.PickFormFor"/>), so we
/// don't need every CHAR_ GUID — just a distinctive token. Curated from a sweep of the
/// prefab name table; the GUIDs are the castable <c>_AbilityGroup</c>/<c>_Group</c>
/// forms. Several are heavily scripted boss casts and may misfire or no-op off the
/// player rig — verify per unit and prune as needed (this list is meant to be edited).
/// </summary>
internal static class SummonRegistry
{
    internal sealed class SummonDef
    {
        /// <summary>Case-insensitive substring matched against the unit's CHAR_ prefab name.</summary>
        public string UnitToken;
        /// <summary>The castable summon ability group GUID (force-cast, no slot).</summary>
        public int AbilityGuid;
        /// <summary>Friendly label for chat ("Frog Vomit — summons frogs").</summary>
        public string Label;
    }

    // Order: more specific tokens before broader ones. A unit can have several (the
    // command lists them and `.beelz summon <n>` picks one). Avoid over-broad tokens
    // (e.g. bare "Militia") that would attach a summon to unrelated units.
    static readonly SummonDef[] _defs =
    {
        // --- Beasts / animal forms ---
        new SummonDef { UnitToken = "Cursed_ToadKing",       AbilityGuid = 1421967280,  Label = "Frog Vomit — summons frogs" },
        new SummonDef { UnitToken = "WerewolfChieftain",     AbilityGuid = 2030404176,  Label = "Open the Cages — summons wolves" },
        new SummonDef { UnitToken = "Spider_Queen",          AbilityGuid = -1779071085, Label = "Spawn Adds — summons spiderlings" },
        new SummonDef { UnitToken = "Spider_Broodmother",    AbilityGuid = 983916408,   Label = "Spawn Adds — summons spiderlings" },

        // --- Bandits / militia bosses ---
        new SummonDef { UnitToken = "Bandit_Tourok",         AbilityGuid = -3835897,    Label = "Call Reinforcements — summons bandits" },
        new SummonDef { UnitToken = "Bandit_Leader",         AbilityGuid = -1854347593, Label = "Call Wolf — summons wolves" },
        new SummonDef { UnitToken = "Gloomrot_RailgunSergeant", AbilityGuid = -948735477, Label = "Call Adds — summons Gloomrot units" },

        // --- Church of Light ---
        new SummonDef { UnitToken = "ChurchOfLight_Paladin", AbilityGuid = 1825130923,  Label = "Summon Angel" },
        new SummonDef { UnitToken = "ChurchOfLight_Priest",  AbilityGuid = 943885425,   Label = "Summon Holy Pillar" },
        new SummonDef { UnitToken = "Militia_Cardinal",      AbilityGuid = -1381729810, Label = "Summon Aide" },
        new SummonDef { UnitToken = "Militia_Cardinal",      AbilityGuid = -16930230,   Label = "Summon Holy Orb" },
        new SummonDef { UnitToken = "BishopOfDunley",        AbilityGuid = 1674395967,  Label = "Summon Eye of God" },
        new SummonDef { UnitToken = "BishopOfDunley",        AbilityGuid = 296313928,   Label = "Summon Pillar" },

        // --- Undead ---
        new SummonDef { UnitToken = "Undead_Necromancer",    AbilityGuid = 1687310776,  Label = "Raise Dead" },
        new SummonDef { UnitToken = "Undead_Necromancer",    AbilityGuid = -1800699406, Label = "Summon Skulls" },
        new SummonDef { UnitToken = "Undead_Priest",         AbilityGuid = -1742500275, Label = "Raise Dead — summons skeletons" },
        new SummonDef { UnitToken = "Undead_ZealousCultist", AbilityGuid = -1784566500, Label = "Summon Ghosts" },
        new SummonDef { UnitToken = "Undead_CursedSmith",    AbilityGuid = 847476786,   Label = "Summon Weapons" },
        new SummonDef { UnitToken = "HighLord",              AbilityGuid = 416744805,   Label = "Raise Dead" },

        // --- Vampires / Blackfang ---
        new SummonDef { UnitToken = "Vampire_Dracula",       AbilityGuid = -1406000418, Label = "Summon Bats" },
        new SummonDef { UnitToken = "Vampire_Dracula",       AbilityGuid = 2121218473,  Label = "Blood Stones" },
        new SummonDef { UnitToken = "BloodKnight",           AbilityGuid = -1920828971, Label = "Summon Crimson Maiden" },
        new SummonDef { UnitToken = "BatVampire",            AbilityGuid = -597709516,  Label = "Summon Bat Minions" },
        new SummonDef { UnitToken = "Blackfang_CarverBoss",  AbilityGuid = -1592003626, Label = "Summon Carvers" },
        new SummonDef { UnitToken = "Blackfang_Morgana",     AbilityGuid = 1680946425,  Label = "Summon Corrupted Vines" },
    };

    /// <summary>All registered signature summons for a transformed unit (by name match).</summary>
    public static List<SummonDef> ForUnit(PrefabGUID unitGuid)
    {
        var result = new List<SummonDef>();
        string n = unitGuid.GetPrefabName() ?? "";
        if (n.Length == 0) return result;
        foreach (var d in _defs)
            if (n.IndexOf(d.UnitToken, StringComparison.OrdinalIgnoreCase) >= 0)
                result.Add(d);
        return result;
    }

    /// <summary>True if the unit has at least one registered signature summon.</summary>
    public static bool HasAny(PrefabGUID unitGuid) => ForUnit(unitGuid).Count > 0;

    /// <summary>
    /// v0.43.4: add a unit's signature summon(s) to a player's CAPTURED pool as standalone
    /// abilities, so they can be granted to a normal spell slot or bound to the custom
    /// hotkey bar and used WITHOUT transforming. Idempotent (AbilityRegistry.Add dedupes).
    /// Gated by <c>Capture_GrantSignatureSummons</c>. Returns the defs newly added.
    /// </summary>
    public static List<SummonDef> GrantStandaloneSummons(ulong steamId, PrefabGUID unitGuid, CaptureSource source)
    {
        var added = new List<SummonDef>();
        if (!Beelzebub.Config.Settings.Capture_GrantSignatureSummons.Value) return added;
        foreach (var d in ForUnit(unitGuid))
        {
            if (Core.AbilityRegistry.Add(steamId, unitGuid._Value, d.AbilityGuid, source))
                added.Add(d);
        }
        return added;
    }

    /// <summary>
    /// v0.43.4: grant a unit's signature summon(s) to the player AND notify them in chat +
    /// emit a [BEELZ:event] type=capture per newly-learned summon (so BCH refreshes the list).
    /// Saves if anything was added. Called from the transform-unlock paths.
    /// </summary>
    public static void GrantAndNotify(Entity playerCharacter, ulong steamId, PrefabGUID unitGuid, CaptureSource source)
    {
        var added = GrantStandaloneSummons(steamId, unitGuid, source);
        if (added.Count == 0) return;

        string sTag = source == CaptureSource.VBlood ? "V" : "R";
        string unitName = unitGuid.GetPrefabName();
        foreach (var d in added)
        {
            var ag = new PrefabGUID(d.AbilityGuid);
            Core.Chat.Send(playerCharacter, Verbosity.Summary,
                $"Learned signature summon: {d.Label} — grant it with .beelz grant or bind a key with .beelz hotkey set (usable without transforming).");
            try
            {
                Core.Chat.SendEvent(playerCharacter,
                    $"[BEELZ:event] type=capture s={sTag} u={unitGuid._Value} un={unitName} a={d.AbilityGuid} an={ag.GetPrefabName()}");
            }
            catch { /* event emit non-critical */ }
        }
        Core.Persistence.RequestSave();
    }

    /// <summary>
    /// v0.43.4: one-time backfill — ensure every player's ALREADY-unlocked units have their
    /// signature summons in their captured pool (so the feature applies retroactively to
    /// units unlocked before this build). Returns the number of summon abilities added.
    /// </summary>
    public static int BackfillAll()
    {
        if (!Beelzebub.Config.Settings.Capture_GrantSignatureSummons.Value) return 0;
        int added = 0;
        foreach (var (steamId, transforms) in Core.AbilityRegistry.TransformSnapshot())
        {
            foreach (var t in transforms)
                added += GrantStandaloneSummons(steamId, new PrefabGUID(t.UnitPrefabGuid), t.Source).Count;
        }
        return added;
    }
}
