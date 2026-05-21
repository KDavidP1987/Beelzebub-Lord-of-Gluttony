using System;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(ReplaceAbilityOnSlotSystem), nameof(ReplaceAbilityOnSlotSystem.OnUpdate))]
internal static class ReplaceAbilityOnSlotSystemPatch
{
    [HarmonyPrefix]
    public static void OnUpdatePrefix(ReplaceAbilityOnSlotSystem __instance)
    {
        if (!Core.IsReady) return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance.__query_1482480545_0.ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] ReplaceAbilityOnSlotSystemPatch failed to read query: {ex}");
            return;
        }

        try
        {
            foreach (Entity entity in entities)
            {
                try
                {
                    ProcessEvent(entity);
                }
                catch (Exception ex)
                {
                    Core.Log.LogError($"[Beelz] ReplaceAbilityOnSlotSystemPatch entity {entity} failed: {ex}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static void ProcessEvent(Entity entity)
    {
        if (!entity.TryGetComponent<EntityOwner>(out var entityOwner)) return;
        Entity owner = entityOwner.Owner;
        if (!owner.IsPlayer()) return;

        ulong steamId = owner.GetSteamId();
        if (steamId == 0) return;

        if (!Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(entity)) return;
        var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(entity);

        // Transform: full override of slots 1-N with the unit's filtered abilities.
        // This is the EXO-style "you ARE the unit" mode and intentionally wins over weapons.
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active is not null)
        {
            var unitGuid = new PrefabGUID(active.UnitPrefabGuid);
            var abilities = Core.Transforms.GetTransformAbilities(unitGuid);
            for (int i = 0; i < abilities.Count; i++)
            {
                buffer.Add(new ReplaceAbilityOnSlotBuff
                {
                    Slot = i + 1,
                    NewGroupId = new PrefabGUID(abilities[i]),
                    CopyCooldown = true,
                    Priority = 0,
                });
            }
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] transform-inject {steamId} as {unitGuid.GetPrefabName()} ({abilities.Count} slots)");
            return;
        }

        // No active transform. Only inject Beelzebub's saved grants when the event
        // entity is the player's UNARMED (or fishing-pole) slot setup. Weapon equip
        // events should leave the weapon's natural abilities alone.
        // Mirrors Bloodcraft's ReplaceAbilityOnSlotSystemPatch gating.
        PrefabGUID eventPrefab = entity.GetPrefabGuid();
        string eventName = eventPrefab.GetPrefabName() ?? "";
        bool isUnarmed = eventName.Contains("unarmed", StringComparison.OrdinalIgnoreCase)
                      || eventName.Contains("fishingpole", StringComparison.OrdinalIgnoreCase);
        if (!isUnarmed) return;

        var slots = Core.AbilityRegistry.GetSlots(steamId);
        if (slots.Count == 0) return;

        foreach (var (slot, abilityGuid) in slots)
        {
            buffer.Add(new ReplaceAbilityOnSlotBuff
            {
                Slot = slot,
                NewGroupId = new PrefabGUID(abilityGuid),
                CopyCooldown = true,
                Priority = 0,
            });
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] inject slot={slot} ability={new PrefabGUID(abilityGuid).GetPrefabName()} (unarmed) for {steamId}");
        }
    }
}
