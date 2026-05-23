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
        if (!Beelzebub.Config.Settings.Transform_DespawnSummonsOnDisconnect.Value) return;

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
            if (active == null) return;
            if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

            // v0.23.7: immediate-drain on disconnect (matches revert path).
            int queued = 0;
            foreach (Entity minion in active.SummonedMinions)
            {
                if (minion.Exists()) { Services.SummonAllyService.EnqueueAdminDespawn(minion); queued++; }
            }
            active.SummonedMinions.Clear();
            active.SummonStacks?.Clear();

            if (queued > 0)
            {
                int processed = Services.SummonAllyService.DrainAdminQueueImmediate();
                Core.Log.LogInfo($"[Beelz SUMMON] disconnect cleanup: queued {queued}, processed {processed} from player {steamId}.");
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] disconnect cleanup failed: {ex.Message}");
        }
    }
}
