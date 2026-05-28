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

        // Z1 (v0.14.0): the active-transform injection branch is gone. Transforms now
        // own their slot overrides via a dedicated carrier buff (TransformBuffService).
        // That buff's own ReplaceAbilityOnSlotBuff entries (Target=BuffTarget, Priority=99)
        // win over weapon naturals AND any saved Beelzebub grant the player has — no
        // need to stamp transform overrides into the EquipBuff's buffer here.
        //
        // While transformed we still skip the W2 grant-injection branch below, since
        // those grants would just be replaced by the carrier buff anyway and would
        // re-flicker on the bar each weapon swap.
        if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return;

        // W2: no active transform. Inject Beelzebub's saved grants per slot based on
        // weapon-family compatibility — universal/Magic abilities fire for any weapon,
        // weapon-family-tagged abilities fire only when wielding that weapon. Replaces
        // the old "unarmed-only" gate.
        PrefabGUID eventPrefab = entity.GetPrefabGuid();
        string eventName = eventPrefab.GetPrefabName() ?? "";
        var weapon = Beelzebub.Services.SlotApply.DetectFamily(eventName);
        if (weapon == Beelzebub.Services.WeaponFamily.None)
        {
            // Unrecognized equip-buff (could be a non-weapon EquipBuff_*). Skip safely.
            return;
        }

        // W3: resolved slot map = universal bucket + weapon-specific overrides for the
        // currently-equipped weapon family, each tagged with its origin (v0.49.0).
        var slots = Core.AbilityRegistry.GetSlotsResolvedWithOrigin(steamId, weapon);
        if (slots.Count == 0) return;

        foreach (var (slot, entry) in slots)
        {
            // v0.49.0: an EXPLICIT weapon-bucket bind (the player put this on, say, the Reaper
            // bar) is honored regardless of the ability's name-derived family — only the admin
            // kill-switch / transform-only reservation can block it. Universal binds keep the
            // family-compatibility filter (a sword spell shouldn't auto-fire on a crossbow).
            bool ok = entry.weaponSpecific
                ? Beelzebub.Services.SlotApply.IsGrantUsable(entry.abilityGuid)
                : Beelzebub.Services.SlotApply.IsGrantCompatible(entry.abilityGuid, weapon);
            if (!ok) continue;
            buffer.Add(new ReplaceAbilityOnSlotBuff
            {
                Slot = slot,
                NewGroupId = new PrefabGUID(entry.abilityGuid),
                CopyCooldown = true,
                Priority = 0,
            });
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] inject slot={slot} ability={new PrefabGUID(entry.abilityGuid).GetPrefabName()} weapon={weapon} explicit={entry.weaponSpecific} for {steamId}");
        }
    }
}
