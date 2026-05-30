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
    // v0.41.1: debounce for the form-exit re-apply. Re-applying a native-FORM transform
    // re-adds its own shapeshift buff, whose later teardown re-fires this destroy hook —
    // skip rapid re-fires per player to avoid a loop.
    static readonly System.Collections.Generic.Dictionary<ulong, DateTime> _lastFormExitReapply = new();

    [HarmonyPostfix]
    public static void OnUpdatePostfix(UpdateBuffsBuffer_Destroy __instance)
    {
        if (!Core.IsReady) return;
        // v0.41.1: summons-allies gates only the summon-restore branch now — the
        // transform-bar re-apply on form-exit must run regardless of that setting.
        bool summonsAllies = Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value;

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
                // v0.41.1: a native shapeshift form (wolf/bear/etc.) was destroyed = the
                // player EXITED that form. If they're still transformed, re-apply the
                // transform bar (travel/bat forms are handled separately on arrival).
                if (Services.ShapeshiftAbilityService.IsSupportedForm(prefab._Value, prefab.GetPrefabName()))
                {
                    // v0.78.0: a vanilla wheel-form we inject custom abilities into (Wolf/Bear/Rat/…)
                    // was exited. Forms-as-group: revert the bar to the active WEAPON group's loadout
                    // (the form's overrides died with its buff). Handles the transform case too.
                    try { HandleCustomFormExit(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz FORM] custom-form-exit revert failed: {ex}");
                    }
                }
                else if (Services.BossFormRegistry.IsNativeShapeshiftBuff(prefab._Value))
                {
                    try { HandleShapeshiftExit(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz] shapeshift-exit re-apply failed: {ex}");
                    }
                }
                else if (summonsAllies && (prefab._Value == WaypointTravelBuff._Value
                    || prefab._Value == WaypointTravelEndBuff._Value))
                {
                    try { HandleArrival(target, prefab.GetPrefabName()); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] HandleArrival failed: {ex}");
                    }
                }
                else if (Patches.BuffSpawnServerPatch.IsMountBuff(e, prefab._Value))
                {
                    // v0.42.0: the rider's mount buff was destroyed = the player DISMOUNTED.
                    // The mount overrode the spell bar, so re-apply a transformed player's bar;
                    // and in Stash mode, restore the summons we stashed on mount. Runs regardless
                    // of summonsAllies (the bar re-apply must), the summon part self-gates.
                    try { HandleDismount(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] HandleDismount failed: {ex}");
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

    // v0.41.1: player exited a native shapeshift (wolf/bear/etc.). If still transformed,
    // re-apply the transform's spell bar (it was overridden by the form). Debounced to
    // avoid a re-entrant loop when re-applying a native-form transform re-adds its buff.
    static void HandleShapeshiftExit(Entity playerCharacter)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return; // not transformed — nothing to restore

        var now = DateTime.UtcNow;
        if (_lastFormExitReapply.TryGetValue(steamId, out var last) && (now - last).TotalSeconds < 1.5)
            return;
        _lastFormExitReapply[steamId] = now;

        if (Core.Transforms.ReapplyActiveTransform(steamId, active, playerCharacter)
            && Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            Core.Log.LogInfo($"[Beelz] re-applied transform bar after native shapeshift exit for {steamId}.");
        }
    }

    /// <summary>
    /// v0.78.0 (forms-as-group): a vanilla wheel-form into which we injected a custom loadout was
    /// exited. The form's ability overrides lived on the form buff (now destroyed), so re-resolve the
    /// player's bar: if they're in a Beelz transform restore THAT bar; otherwise re-inject the active
    /// WEAPON group's loadout so the bar reverts to whatever weapon they're holding — the form behaving
    /// like a weapon family (enter → form loadout, exit → weapon loadout). Debounced against re-fires.
    /// </summary>
    static void HandleCustomFormExit(Entity playerCharacter)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;
        // If the custom-form feature is off, fall back to the transform-only re-apply behavior.
        if (!Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value) { HandleShapeshiftExit(playerCharacter); return; }

        var now = DateTime.UtcNow;
        if (_lastFormExitReapply.TryGetValue(steamId, out var last) && (now - last).TotalSeconds < 1.5) return;
        _lastFormExitReapply[steamId] = now;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active != null)
        {
            try { Core.Transforms.ReapplyActiveTransform(steamId, active, playerCharacter); }
            catch (Exception ex) { Core.Log.LogError($"[Beelz FORM] transform re-apply on form exit failed: {ex}"); }
            return;
        }

        try
        {
            int n = Services.SlotApply.RestoreResolvedGrants(playerCharacter);
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz FORM] custom-form exit for {steamId} → reverted to the active weapon-group bar (re-applied {n} grant(s)).");
        }
        catch (Exception ex) { Core.Log.LogError($"[Beelz FORM] weapon-bar revert on form exit failed: {ex}"); }
    }

    // v0.42.0: a transformed player dismounted a horse. The mount buff had overridden the
    // spell bar (gallop/leap/thrust), so re-apply the transform's bar — same fix as form-exit
    // / waygate arrival, sharing the same per-player debounce. Then, in Stash mode, restore the
    // summons stashed on mount. A non-transformed player is ignored (V Rising restores their bar).
    static void HandleDismount(Entity playerCharacter)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;
        // Re-apply the transform bar if the player is transformed (the mount overrode it).
        // Debounced. v0.45.0: standalone (untransformed) summoners skip this.
        var transform = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (transform != null)
        {
            var now = DateTime.UtcNow;
            if (!(_lastFormExitReapply.TryGetValue(steamId, out var last) && (now - last).TotalSeconds < 1.5))
            {
                _lastFormExitReapply[steamId] = now;
                if (Core.Transforms.ReapplyActiveTransform(steamId, transform, playerCharacter)
                    && Beelzebub.Config.Settings.VerboseLogging.Value)
                {
                    Core.Log.LogInfo($"[Beelz] re-applied transform bar after horse dismount for {steamId}.");
                }
            }
        }

        // Stash mode: bring back the summons (transform OR standalone) we stashed on mount.
        var owner = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
        if (Beelzebub.Config.Settings.Transform_MountedSummonMode.Value
                == Beelzebub.Config.Settings.MountedSummonMode.Stash
            && Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value
            && owner != null && owner.StashedSummons != null && owner.StashedSummons.Count > 0)
        {
            int restored = SummonAllyService.RestoreAll(owner, playerCharacter);
            if (restored > 0)
            {
                owner.SummonsDisabled = false;
                Core.Log.LogInfo($"[Beelz SUMMON] auto-restore on dismount: restored {restored} for player {steamId}.");
            }
        }
    }

    static void HandleArrival(Entity playerCharacter, string buffName)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var active = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
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
