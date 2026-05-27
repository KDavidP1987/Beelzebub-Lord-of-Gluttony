using System;
using System.Collections.Generic;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.48.0 — custom abilities on VANILLA shapeshift forms (Wolf/Bear test set).
///
/// The player unlocks forms from V-Blood bosses (Alpha Wolf → Wolf, Ferocious Bear → Bear, …)
/// and enters them via the in-game shapeshift wheel (Alt + cursor). When the form buff spawns on
/// the player, this service:
///   1. **Strips the break-on-cast trigger.** Vanilla travel forms auto-exit the moment you cast
///      a non-form ability — that exit is driven by <c>RemoveBuffOnGameplayEvent</c> /
///      <c>RemoveBuffOnGameplayEventEntry</c> on the form buff. Removing those components makes the
///      form HOLD through casting. (Technique proven in Bloodcraft's BuffSpawnServerPatches.)
///   2. **Injects the player's loadout** onto the form bar via <c>ReplaceAbilityOnSlotBuff</c>
///      (Priority 99, Target=BuffTarget, CastBlockType=WholeCast) — the same recipe Bloodcraft's
///      <c>Shapeshifts.ModifyShapeshiftBuff</c> uses for its ExoForms.
///
/// Phase-1.5 TEST: the loadout = the player's universal slot binds (or first captures); a later
/// phase adds a dedicated per-form bucket + auto-switch. Gated by <c>Forms_CustomAbilities_Enabled</c>
/// (default off). The form buff keeps its own RemoveOnDisconnect, so logout still exits cleanly,
/// and our overrides live on the form buff entity (destroyed with it) — it cannot strand the bar.
/// </summary>
internal static class ShapeshiftAbilityService
{
    // Phase-1.5 test set: Wolf + Bear only (per the user's "build wolf and bear as tests").
    static readonly HashSet<int> _testForms = new()
    {
        -351718282,   // AB_Shapeshift_Wolf_Buff
        -1569370346,  // AB_Shapeshift_Bear_Buff
    };

    /// <summary>Is this buff GUID one of the vanilla forms we currently inject abilities into?</summary>
    public static bool IsTestForm(int buffGuid) => _testForms.Contains(buffGuid);

    /// <summary>
    /// Called when a tracked vanilla form buff spawns on a player. Strips the break-on-cast
    /// trigger and injects the player's loadout abilities onto the form bar. Fully guarded.
    /// </summary>
    public static void ApplyFormLoadout(Entity buffEntity, Entity character, int formBuffGuid)
    {
        if (!Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value) return;
        if (!buffEntity.Exists() || !character.Exists()) return;
        ulong steamId = character.GetSteamId();
        if (steamId == 0) return;

        // Build the form bar (slots 0-7): prefer the player's universal loadout, else first captures.
        // (Phase-1.5 reuses the universal binds; a later phase adds a per-form bucket.)
        var set = new List<int>(8);
        foreach (var (slot, ability) in Core.AbilityRegistry.GetSlots(steamId))
        {
            if (ability != 0) set.Add(ability);
            if (set.Count >= 8) break;
        }
        if (set.Count == 0)
            foreach (var c in Core.AbilityRegistry.ListFor(steamId))
            {
                set.Add(c.AbilityPrefabGuid);
                if (set.Count >= 6) break;
            }
        if (set.Count == 0)
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz FORM] {steamId} entered {new PrefabGUID(formBuffGuid).GetPrefabName()} but has no loadout — nothing injected (set up .beelz grant first).");
            return;
        }

        try
        {
            // 1. Stop the form from auto-exiting when the player casts (the break-on-cast cause).
            if (buffEntity.Has<RemoveBuffOnGameplayEvent>()) Core.EntityManager.RemoveComponent<RemoveBuffOnGameplayEvent>(buffEntity);
            if (buffEntity.Has<RemoveBuffOnGameplayEventEntry>()) Core.EntityManager.RemoveComponent<RemoveBuffOnGameplayEventEntry>(buffEntity);

            // 2. Ensure the buff is allowed to replace ability slots.
            if (!buffEntity.Has<ReplaceAbilityOnSlotData>()) Core.EntityManager.AddComponent<ReplaceAbilityOnSlotData>(buffEntity);

            // 3. Inject the loadout (Priority 99 wins over the form's native bar; BuffTarget = the
            //    player wearing the form; WholeCast so the override holds for the entire cast).
            DynamicBuffer<ReplaceAbilityOnSlotBuff> buffer = Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(buffEntity)
                ? Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(buffEntity)
                : Core.EntityManager.AddBuffer<ReplaceAbilityOnSlotBuff>(buffEntity);

            for (int slot = 0; slot < set.Count && slot < 8; slot++)
            {
                buffer.Add(new ReplaceAbilityOnSlotBuff
                {
                    Target = ReplaceAbilityTarget.BuffTarget,
                    Slot = slot,
                    NewGroupId = new PrefabGUID(set[slot]),
                    Priority = 99,
                    CopyCooldown = true,
                    CastBlockType = GroupSlotModificationCastBlockType.WholeCast,
                });
            }

            if (Core.ReplaceAbilityOnSlotSystem != null) Core.ReplaceAbilityOnSlotSystem.OnUpdate();
            Core.Log.LogInfo($"[Beelz FORM] {steamId} → {new PrefabGUID(formBuffGuid).GetPrefabName()}: injected {set.Count} ability(ies) + stripped break-on-cast. TEST: cast one — does the form hold?");
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz FORM] ApplyFormLoadout failed for {steamId} ({new PrefabGUID(formBuffGuid).GetPrefabName()}): {ex.Message}");
        }
    }
}
