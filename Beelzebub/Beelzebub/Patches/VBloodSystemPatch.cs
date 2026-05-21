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
            if (!Core.AbilityFilter.ShouldCapture(abilityName, ability._Value, out string reason))
            {
                skipped++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] skip (VBlood) {abilityName}: {reason}");
                continue;
            }

            float chance = Settings.DropChance_Ability_VBlood.Value;
            if (chance < 1f && System.Random.Shared.NextDouble() > chance) continue;

            if (Core.AbilityRegistry.Add(steamId, vBloodGuid._Value, ability._Value, CaptureSource.VBlood))
            {
                captured++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] capture (VBlood) {abilityName} from {vBloodGuid.GetPrefabName()} for {steamId}");
                Core.Chat.Send(playerCharacter, Verbosity.Verbose, $"Acquired V-Blood ability: {abilityName} (from {vBloodGuid.GetPrefabName()}).");
            }
        }

        // V-Blood transform unlock roll (typically rarer than ability captures).
        float transformChance = Settings.DropChance_Transform_VBlood.Value;
        bool gotTransform = false;
        if (transformChance > 0f && System.Random.Shared.NextDouble() <= transformChance)
        {
            if (Core.AbilityRegistry.AddTransformUnlock(steamId, vBloodGuid._Value, CaptureSource.VBlood))
            {
                gotTransform = true;
                string unitName = vBloodGuid.GetPrefabName();
                Core.Log.LogInfo($"[Beelz] {steamId} unlocked V-Blood transform: {unitName}.");
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    $"Unlocked V-Blood transformation: {unitName}!");
            }
        }

        if (captured > 0 || gotTransform)
        {
            if (captured > 0)
            {
                string unitName = vBloodGuid.GetPrefabName();
                Core.Log.LogInfo($"[Beelz] {steamId} defeated V-Blood {unitName}: captured {captured} ability(ies), skipped {skipped}.");
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    captured == 1
                        ? $"Defeated V-Blood {unitName}: acquired 1 new ability."
                        : $"Defeated V-Blood {unitName}: acquired {captured} new abilities.");
            }
            Core.Persistence.SaveSync();
        }
    }
}
