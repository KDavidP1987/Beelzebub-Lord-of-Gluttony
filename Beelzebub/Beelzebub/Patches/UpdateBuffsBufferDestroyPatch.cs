using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// v0.23.14 — auto-restore on waygate arrival.
///
/// V Rising's <c>Buff_Waypoint_Travel</c> is the channel buff applied to the
/// player during waygate teleport. When the teleport completes, the buff is
/// destroyed (and per its prefab data, spawns <c>Buff_Waypoint_TravelEnd</c>
/// as a confirmation). Hooking <c>UpdateBuffsBuffer_Destroy.OnUpdate</c>
/// Postfix lets us detect that destruction reliably — Bloodcraft uses this
/// same system for combat-end detection.
///
/// When we see <c>Buff_Waypoint_Travel</c> destroyed on a transformed player
/// who has stashed summons, auto-restore them at the player's current position.
/// </summary>
[HarmonyPatch(typeof(UpdateBuffsBuffer_Destroy), nameof(UpdateBuffsBuffer_Destroy.OnUpdate))]
internal static class UpdateBuffsBufferDestroyPatch
{
    static readonly PrefabGUID WaypointTravelBuff = new(150521246);
    static readonly PrefabGUID WaypointTravelEndBuff = new(-1361133205);
    // v0.23.15: PvE combat buff. Destruction = combat ending = flip summons
    // back to leash mode for proper follow spacing.
    static readonly PrefabGUID PvECombatBuff = new(581443919);

    [HarmonyPostfix]
    public static void OnUpdatePostfix(UpdateBuffsBuffer_Destroy __instance)
    {
        if (!Core.IsReady) return;
        if (!Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value) return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] UpdateBuffsBufferDestroy query failed: {ex.Message}");
            return;
        }

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                if (!e.TryGetComponent<PrefabGUID>(out var prefab)) continue;
                if (!e.TryGetComponent<Buff>(out var buff)) continue;
                Entity target = buff.Target;
                if (!target.Exists() || !target.IsPlayer()) continue;

                // Either signal — Travel buff destroyed, OR TravelEnd buff destroyed
                // (TravelEnd is short-lived; it spawns then quickly destroys at the
                // moment the player visually arrives). Both indicate teleport done.
                if (prefab._Value == WaypointTravelBuff._Value
                    || prefab._Value == WaypointTravelEndBuff._Value)
                {
                    try { HandleArrival(target, prefab.GetPrefabName()); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] HandleArrival failed: {ex}");
                    }
                }
                else if (prefab._Value == PvECombatBuff._Value)
                {
                    // v0.23.15: combat ended → flip summons back to leash mode
                    // for proper follow spacing (no vortex out of combat).
                    ulong steamId = target.GetSteamId();
                    if (steamId != 0)
                    {
                        SummonAllyService.SetCombatMode(steamId, false);

                        // v0.27.0: clear combat flag and, in Auto phase mode, reset
                        // the transform to phase 1 — mirrors how a boss resets its
                        // phase when it leaves combat / leashes.
                        var activeT = Core.AbilityRegistry.GetActiveTransform(steamId);
                        if (activeT != null)
                        {
                            activeT.InCombat = false;
                            if (Beelzebub.Config.Settings.Transform_PhaseMode.Value
                                    == Beelzebub.Config.Settings.PhaseControlMode.Auto
                                && activeT.CurrentPhase > 1)
                            {
                                try { Core.Transforms.ApplyPhase(steamId, activeT, target, 1); }
                                catch (Exception ex)
                                {
                                    Core.Log.LogWarning($"[Beelz PHASE] combat-end phase reset failed: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static void HandleArrival(Entity playerCharacter, string buffName)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;
        if (active.StashedSummons == null || active.StashedSummons.Count == 0) return;

        int restored = SummonAllyService.RestoreAll(active, playerCharacter);
        if (restored > 0)
        {
            active.SummonsDisabled = false;
            Core.Log.LogInfo($"[Beelz SUMMON] auto-restore on waypoint arrival ({buffName}): restored {restored} for player {steamId}.");
            try
            {
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    $"Restored {restored} summon(s) at your destination.");
            }
            catch { /* chat failures non-critical */ }
        }
    }
}
