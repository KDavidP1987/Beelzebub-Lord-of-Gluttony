using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Network;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// v0.23.0 — despawn a player's active summons when they disconnect.
///
/// Mirrors Bloodcraft's familiar lifecycle: log out / disconnect → familiars
/// are unbound. Otherwise players' summons can persist in-world while their
/// owner is offline, leaving wandering Faction_Players mobs that nobody can
/// control. Also prevents save bloat (each lingering minion is more state V
/// Rising's persistence has to track).
///
/// Gated by <see cref="Beelzebub.Config.Settings.Transform_DespawnSummonsOnDisconnect"/>.
/// Default true. Admins can set false to leave summons in-world during DC —
/// useful for short disconnects where summons could "guard" the offline
/// player's body (though Beelzebub doesn't actually wire bodyguard behavior;
/// summons just stay where they were).
/// </summary>
[HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserDisconnected))]
internal static class ServerBootstrapSystemPatch
{
    [HarmonyPrefix]
    public static void OnUserDisconnectedPrefix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
    {
        if (!Core.IsReady) return;

        try
        {
            // Resolve the disconnecting connection back to a steamId. Pattern lifted
            // from Bloodcraft ServerBootstrapSystemPatches.OnUserDisconnectedPrefix —
            // _NetEndPointToApprovedUserIndex → _ApprovedUsersLookup → UserEntity → User.
            if (!__instance._NetEndPointToApprovedUserIndex.ContainsKey(netConnectionId)) return;
            int userIndex = __instance._NetEndPointToApprovedUserIndex[netConnectionId];
            ServerBootstrapSystem.ServerClient serverClient = __instance._ApprovedUsersLookup[userIndex];
            Entity userEntity = serverClient.UserEntity;
            if (!userEntity.Exists()) return;
            if (!userEntity.TryGetComponent<User>(out var user)) return;
            ulong steamId = user.PlatformId;
            if (steamId == 0) return;

            var active = Core.AbilityRegistry.GetActiveTransform(steamId);
            if (active == null)
            {
                // v0.45.0: not transformed — but the player may have STANDALONE summons
                // (a summon ability cast in normal form). Despawn them so they don't linger
                // ownerless while the player is offline. Gated by the same config as the
                // transform path. (Reconnect-grace for standalone summons is a future
                // enhancement; for now they're cleaned on disconnect.)
                if (!Beelzebub.Config.Settings.Transform_DespawnSummonsOnDisconnect.Value) return;
                try
                {
                    var owner = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
                    if (owner != null)
                    {
                        int queued = 0;
                        if (owner.SummonedMinions != null)
                            foreach (var m in owner.SummonedMinions) if (m.Exists()) { Beelzebub.Services.SummonAllyService.EnqueueAdminDespawn(m); queued++; }
                        if (owner.StashedSummons != null)
                            foreach (var m in owner.StashedSummons) if (m.Exists()) { Beelzebub.Services.SummonAllyService.EnqueueAdminDespawn(m); queued++; }
                        if (queued > 0)
                        {
                            Beelzebub.Services.SummonAllyService.DrainAdminQueueImmediate();
                            Core.Log.LogInfo($"[Beelz SUMMON] disconnect: despawned {queued} standalone summon(s) for player {steamId}.");
                        }
                        Core.AbilityRegistry.ClearStandaloneSummons(steamId);
                    }
                }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[Beelz] disconnect standalone-summon cleanup failed for {steamId}: {ex.Message}");
                }
                return; // not transformed — transform grace logic below doesn't apply
            }

            // v0.43.7: disconnect policy is governed by Transform_ReconnectGraceSeconds.
            //
            // CORRECTION to the v0.43.2 note: the active-transform registry is RUNTIME-ONLY
            // (never persisted). A form/carrier buff, however, can outlive it (a Toggle form
            // buff has infinite lifetime; async-applied native forms can miss RemoveOnDisconnect)
            // and survive a disconnect or a server restart — which is what stranded players in
            // an un-revertable, bar-frozen state. The grace window + the on-connect reconcile
            // (TransformService) together close that gap.
            //
            //   grace == 0  → revert immediately (clean): despawn summons, destroy the buff,
            //                 clear the record. (The v0.43.2 behavior, now one branch.)
            //   grace  > 0  → park the transform + stash its summons; resume on reconnect, or
            //                 revert when the window elapses (Tick.ExpireReconnectGrace).
            //   grace  < 0  → park indefinitely (manual revert only).
            try
            {
                float grace = Beelzebub.Config.Settings.Transform_ReconnectGraceSeconds.Value;
                string unitName = new Stunlock.Core.PrefabGUID(active.UnitPrefabGuid).GetPrefabName();
                if (grace == 0f)
                {
                    Core.Transforms.Revert(steamId, "disconnect", restoreBar: false);
                    Core.Log.LogInfo($"[Beelz] disconnect: immediate revert for player {steamId} (was {unitName}; grace=0).");
                }
                else
                {
                    Core.Transforms.BeginReconnectGrace(steamId, active);
                    string window = grace < 0f ? "indefinite" : $"{grace:F0}s";
                    Core.Log.LogInfo($"[Beelz] disconnect: parked transform for player {steamId} (was {unitName}; grace window {window}).");
                }
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz] disconnect transform handler failed for {steamId}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz] disconnect handler failed: {ex.Message}");
        }
    }
}

/// <summary>
/// v0.43.7 — on (re)connect, reconcile the player's transform state. This is the safety
/// net that guarantees a player never logs in to a broken ability bar:
///   * if they have a transform parked in the reconnect-grace window, it resumes;
///   * if a form/carrier buff was stranded from a previous session (e.g. a server
///     restart wiped the runtime-only transform registry but the buff survived on the
///     saved character), it is detected and cleared, restoring the base/granted bar.
///
/// The hook itself only QUEUES the steamId — the actual work happens in
/// TransformService.Tick once the player's character entity has spawned (it isn't ready
/// at OnUserConnected; Bloodcraft defers the same way). Everything is wrapped so a fault
/// here can never slow or block the login path.
/// </summary>
[HarmonyPatch(typeof(ServerBootstrapSystem), nameof(ServerBootstrapSystem.OnUserConnected))]
internal static class ServerBootstrapSystemConnectPatch
{
    [HarmonyPostfix]
    public static void OnUserConnectedPostfix(ServerBootstrapSystem __instance, NetConnectionId netConnectionId)
    {
        if (!Core.IsReady) return;
        try
        {
            if (!__instance._NetEndPointToApprovedUserIndex.ContainsKey(netConnectionId)) return;
            int userIndex = __instance._NetEndPointToApprovedUserIndex[netConnectionId];
            ServerBootstrapSystem.ServerClient serverClient = __instance._ApprovedUsersLookup[userIndex];
            Entity userEntity = serverClient.UserEntity;
            if (!userEntity.Exists()) return;
            if (!userEntity.TryGetComponent<User>(out var user)) return;
            ulong steamId = user.PlatformId;
            if (steamId == 0) return;

            Core.Transforms.QueueReconnectReconcile(steamId);
        }
        catch (Exception ex)
        {
            // NEVER let a reconcile fault touch the login path.
            Core.Log.LogWarning($"[Beelz] connect handler failed: {ex.Message}");
        }
    }
}
