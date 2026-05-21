using System;
using System.Collections.Generic;
using Beelzebub.Config;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(VBloodSystem), nameof(VBloodSystem.OnUpdate))]
internal static class VBloodSystemPatch
{
    static readonly Dictionary<ulong, DateTime> _lastCaptureAt = new();
    static readonly TimeSpan _dedupeWindow = TimeSpan.FromSeconds(5);

    [HarmonyPrefix]
    public static void OnUpdatePrefix(VBloodSystem __instance)
    {
        if (!Core.IsReady || !Settings.CaptureOnKill.Value) return;

        try
        {
            var events = __instance.EventList;
            for (int i = 0; i < events.Length; i++)
            {
                ProcessVBloodConsumed(events[i]);
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] VBloodSystemPatch failed: {ex}");
        }
    }

    static void ProcessVBloodConsumed(VBloodConsumed evt)
    {
        Entity playerCharacter = evt.Target;
        if (!playerCharacter.IsPlayer()) return;

        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var now = DateTime.UtcNow;
        if (_lastCaptureAt.TryGetValue(steamId, out var last) && now - last < _dedupeWindow)
        {
            return;
        }
        _lastCaptureAt[steamId] = now;

        PrefabGUID vBloodGuid = evt.Source;
        if (vBloodGuid._Value == 0) return;

        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(vBloodGuid, out Entity vBloodPrefabEntity))
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] V-Blood {vBloodGuid.GetPrefabName()} not found in prefab map.");
            return;
        }

        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(vBloodPrefabEntity))
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] V-Blood {vBloodGuid.GetPrefabName()} has no AbilityGroupSlotBuffer.");
            return;
        }

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(vBloodPrefabEntity);
        int captured = 0, skipped = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;

            string abilityName = ability.GetPrefabName();
            if (!Core.AbilityFilter.ShouldCapture(abilityName, out string reason))
            {
                skipped++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] skip (VBlood) {abilityName}: {reason}");
                continue;
            }

            if (Core.AbilityRegistry.Add(steamId, vBloodGuid._Value, ability._Value, CaptureSource.VBlood))
            {
                captured++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] capture (VBlood) {abilityName} from {vBloodGuid.GetPrefabName()} for {steamId}");
            }
        }

        if (captured > 0)
        {
            Core.Log.LogInfo($"[Beelz] {steamId} defeated V-Blood {vBloodGuid.GetPrefabName()}: captured {captured} ability(ies), skipped {skipped}.");
            Core.Persistence.SaveSync();
        }
    }
}
