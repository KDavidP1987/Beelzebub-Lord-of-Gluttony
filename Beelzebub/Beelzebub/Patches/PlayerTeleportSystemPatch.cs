using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Systems;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Beelzebub.Patches;

/// <summary>
/// v0.23.0 — keep player-allied summons with the player across waygate teleports,
/// bat-form landings, and admin teleports. Pattern lifted from Bloodcraft's
/// <c>PlayerTeleportSystemPatch</c>: postfix on <c>PlayerTeleportSystem.OnUpdate</c>,
/// detect a <c>PlayerTeleportDebugEvent</c> for a transformed player, then move
/// every live summon's <c>Translation</c> / <c>LastTranslation</c> to the player's
/// new position. The aggro buffer is re-seeded automatically on the next
/// <c>SummonAllyService.SyncAggroAll</c> tick (~333ms).
/// </summary>
[HarmonyPatch]
internal static class PlayerTeleportSystemPatch
{
    [HarmonyPatch(typeof(PlayerTeleportSystem), nameof(PlayerTeleportSystem.OnUpdate))]
    [HarmonyPostfix]
    public static void OnUpdatePostfix(PlayerTeleportSystem __instance)
    {
        if (!Core.IsReady) return;
        if (!Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value) return;

        NativeArray<Entity> entities;
        try
        {
            // Bloodcraft uses EntityQueries[1] — that's the query holding the
            // PlayerTeleportDebugEvent + FromCharacter events.
            entities = __instance.EntityQueries[1].ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] PlayerTeleportSystem query failed: {ex.Message}");
            return;
        }

        try
        {
            foreach (Entity entity in entities)
            {
                try { HandleTeleportEvent(entity); }
                catch (Exception ex)
                {
                    Core.Log.LogError($"[Beelz SUMMON] teleport-follow failed for event {entity}: {ex}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static void HandleTeleportEvent(Entity eventEntity)
    {
        if (!eventEntity.Has<PlayerTeleportDebugEvent>()) return;
        if (!eventEntity.TryGetComponent<FromCharacter>(out var fromChar)) return;

        Entity playerCharacter = fromChar.Character;
        if (!playerCharacter.Exists()) return;

        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;

        // v0.23.11: auto-restore stashed summons after teleport completes.
        // BuffSpawnServerPatch auto-stashed them when the waypoint channel buff
        // applied; now we're past the subdued check and post-teleport, so bring
        // them back at the new position.
        if (active.StashedSummons != null && active.StashedSummons.Count > 0)
        {
            int restored = Beelzebub.Services.SummonAllyService.RestoreAll(active, playerCharacter);
            if (restored > 0)
            {
                Core.Log.LogInfo($"[Beelz SUMMON] auto-restore after teleport: {restored} summon(s) for player {steamId}");
                active.SummonsDisabled = false;
            }
            return;
        }

        if (active.SummonsDisabled) return;
        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

        if (!playerCharacter.TryGetComponent<LocalToWorld>(out var ltw)) return;
        float3 destPos = ltw.Position;

        int moved = 0;
        int total = active.SummonedMinions.Count;
        for (int i = total - 1; i >= 0; i--)
        {
            Entity minion = active.SummonedMinions[i];
            if (!minion.Exists())
            {
                active.SummonedMinions.RemoveAt(i);
                continue;
            }

            // Radial scatter so minions don't all stack on the same tile after teleport.
            float angle = (float)(i * (Math.PI * 2.0 / Math.Max(1, total)));
            float3 offset = new float3((float)Math.Cos(angle) * 2f, 0f, (float)Math.Sin(angle) * 2f);
            float3 spot = destPos + offset;

            try
            {
                if (minion.Has<Translation>())
                {
                    minion.With((ref Translation t) => t.Value = spot);
                }
                if (minion.Has<LastTranslation>())
                {
                    minion.With((ref LastTranslation lt) => lt.Value = spot);
                }
                if (minion.Has<AggroConsumer>())
                {
                    minion.With((ref AggroConsumer ac) => ac.PreCombatPosition = spot);
                }
                moved++;
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz SUMMON] failed to reposition minion {minion} on teleport: {ex.Message}");
            }
        }

        if (moved > 0)
        {
            Core.Log.LogInfo($"[Beelz SUMMON] teleport-follow: moved {moved}/{total} ally minion(s) with player {steamId} to {destPos}");
        }
    }
}
