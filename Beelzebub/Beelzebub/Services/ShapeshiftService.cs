using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// A4: visual shapeshift heuristic. V Rising has no generic "morph into arbitrary CHAR_"
/// API — only 8 native shapeshift forms (Bear, Golem, Human, NormalForm, Rat, Spider,
/// Toad, Wolf) actually swap the player model. This service maps a captured unit name
/// to the closest native form and applies that buff alongside the .beelz transform
/// spell-bar swap. See `project_a4_shapeshift_research` for the full reasoning.
/// </summary>
internal static class ShapeshiftService
{
    // V Rising-shipped shapeshift form buffs we map to. Prefab dump audit (v0.21.1):
    //   AB_Shapeshift_Bear_Buff                            -1569370346
    //   AB_Shapeshift_Rat_Buff                              902394170
    //   AB_Shapeshift_Spider_Buff                           124832551
    //   AB_Shapeshift_Toad_Buff                           -1038422434
    //   AB_Shapeshift_Wolf_Buff                            -351718282
    //   Buff_General_Shapeshift_Werewolf_Standard          (Bloodcraft helper)
    //   Buff_General_Shapeshift_Werewolf_VBlood            (Bloodcraft helper)
    //   AB_Tailor_Shapeshift_Gargoyle_Buff                 -395216184
    //
    // V Rising's shapeshift system supports specific shipped models. The EXO
    // form Bloodcraft uses (Evolved Vampire / Corrupted Serpent) is also one of
    // these shipped forms — that's why EXO works mechanically but Stonebreaker
    // doesn't (no shipped Stonebreaker shapeshift buff exists).
    //
    // Intentionally NOT used (player feedback 2026-05-21):
    //   AB_Shapeshift_Human_Buff (-53860211)     — moves much slower than actual humanoid NPCs
    //   AB_Shapeshift_Golem_T02_Buff (914043867) — not reliably player-usable
    static readonly PrefabGUID Wolf            = new(-351718282);
    static readonly PrefabGUID Bear            = new(-1569370346);
    static readonly PrefabGUID Rat             = new(902394170);
    static readonly PrefabGUID Spider          = new(124832551);
    static readonly PrefabGUID Toad            = new(-1038422434);
    // v0.21.1: expanded form roster — partial answer to user's "EXO form works,
    // why don't bosses?" question. We can't add NEW models, but we weren't using
    // all the models V Rising already ships.
    static readonly PrefabGUID WerewolfStandard = new(-1158884666); // AB_Shapeshift_Wolf_Skin01_Buff (Werewolf-like skin)
    static readonly PrefabGUID TailorGargoyle   = new(-395216184);  // AB_Tailor_Shapeshift_Gargoyle_Buff (Tailor phase-2)

    /// <summary>
    /// Heuristic: pick the closest V-Rising-shipped shapeshift form for a captured
    /// CHAR_ unit. Returns PrefabGUID.Empty if no native form matches (player keeps
    /// their base vampire model; only the spell bar + stats change).
    ///
    /// v0.21.1: Werewolf (skin variant of Wolf) and Tailor Gargoyle added —
    /// Werewolf for werewolf-themed V-Bloods, Gargoyle for the Tailor specifically.
    /// </summary>
    public static PrefabGUID PickFormFor(PrefabGUID unitGuid)
    {
        string n = unitGuid.GetPrefabName() ?? "";
        // Strip CHAR_ prefix for cleaner matching.
        if (n.StartsWith("CHAR_", StringComparison.OrdinalIgnoreCase)) n = n.Substring(5);

        // Specific named matches first.
        if (Contains(n, "Tailor")) return TailorGargoyle;
        if (Contains(n, "WerewolfChieftain") || Contains(n, "Werewolf")) return WerewolfStandard;
        // Generic family matches.
        if (Contains(n, "Wolf")) return Wolf;
        if (Contains(n, "Bear")) return Bear;
        if (Contains(n, "Rat") || Contains(n, "Vermin")) return Rat;
        if (Contains(n, "Spider") || Contains(n, "Arachnid")) return Spider;
        if (Contains(n, "Toad") || Contains(n, "Frog")) return Toad;

        // Everything else (humanoid bandits/cultists/militia, banshees, Treant,
        // Manticore, golems, skeletons, etc.) keeps the player's base model.
        // V Rising doesn't ship shapeshift forms for those — hard engine limit.
        return PrefabGUID.Empty;
    }

    static bool Contains(string s, string token) =>
        !string.IsNullOrEmpty(s) && s.Contains(token, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Apply a shapeshift form buff to the player. Returns the chosen form (or Empty
    /// if no good match — in which case nothing is applied and only the spell bar
    /// will change). Caller should stash the returned GUID on ActiveTransform so
    /// revert knows what to remove.
    /// </summary>
    public static PrefabGUID Apply(Entity character, PrefabGUID unitGuid, bool forceEnabled = false)
    {
        if (!character.Exists()) return PrefabGUID.Empty;
        // Z2: opt-in. Native shapeshift forms auto-exit on any non-form cast, so the
        // visual breaks the moment a captured ability fires. Default off; admins
        // who want the wolf-on-wolf cosmetic can enable in config.
        // TX3 (v0.16.0): `forceEnabled` lets a per-transformation FullReplace flag
        // override the global config. Caller pulls the flag from the TransformMap entry.
        if (!forceEnabled && !Beelzebub.Config.Settings.Transform_NativeShapeshift_Enabled.Value) return PrefabGUID.Empty;
        var form = PickFormFor(unitGuid);
        if (form._Value == 0) return PrefabGUID.Empty;

        try
        {
            if (HasBuff(character, form))
            {
                // Already applied (e.g. transform-switch path): nothing to do.
                return form;
            }

            var debugEvent = new ApplyBuffDebugEvent
            {
                BuffPrefabGUID = form,
                Who = GetNetworkId(character),
            };
            var from = new FromCharacter
            {
                Character = character,
                User = GetUserEntity(character),
            };
            Core.DebugEventsSystem.ApplyBuff(from, debugEvent);
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] shapeshift apply form={form.GetPrefabName()} unit={unitGuid.GetPrefabName()}");
            return form;
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ShapeshiftService.Apply failed unit={unitGuid._Value} form={form._Value}: {ex}");
            return PrefabGUID.Empty;
        }
    }

    /// <summary>
    /// Remove a previously-applied shapeshift form buff. Safe to call with PrefabGUID.Empty
    /// (no-op) — useful when ActiveTransform.AppliedShapeshiftForm was never set because
    /// the captured unit didn't match any heuristic bucket.
    /// </summary>
    public static void Remove(Entity character, PrefabGUID form)
    {
        if (!character.Exists() || form._Value == 0) return;
        try
        {
            if (TryGetBuff(character, form, out Entity buffEntity) && buffEntity.Exists())
            {
                DestroyUtility.Destroy(Core.EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff);
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] shapeshift remove form={form.GetPrefabName()}");
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ShapeshiftService.Remove failed form={form._Value}: {ex}");
        }
    }

    static bool HasBuff(Entity character, PrefabGUID form) =>
        Core.ServerGameManager.HasBuff(character, form.ToIdentifier());

    static bool TryGetBuff(Entity character, PrefabGUID form, out Entity buff) =>
        Core.ServerGameManager.TryGetBuff(character, form.ToIdentifier(), out buff);

    static NetworkId GetNetworkId(Entity entity) =>
        entity.TryGetComponent<NetworkId>(out var id) ? id : default;

    static Entity GetUserEntity(Entity entity)
    {
        if (entity.TryGetComponent<PlayerCharacter>(out var pc)) return pc.UserEntity;
        return entity;
    }
}
