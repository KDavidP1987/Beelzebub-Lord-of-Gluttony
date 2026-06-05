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
        if (!Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(entity)) return;

        // v0.89.0 (forms render fix): if this modification-source entity is a shapeshift FORM buff
        // resolving its slot bar right now, inject the player's per-form loadout into its buffer at THIS
        // moment — the same in-resolution timing the weapon path uses. The v0.86 heartbeat path set the
        // same data but AFTER V Rising had already resolved + synced the form bar to the client, so the
        // abilities never appeared. Here the system resolves our edit immediately. (Uses Buff.Target for
        // the player — more reliable than EntityOwner on a form buff.)
        int prefabId = entity.GetPrefabGuid()._Value;
        if (Services.ShapeshiftAbilityService.IsSupportedForm(prefabId, entity.GetPrefabGuid().GetPrefabName())
            && entity.TryGetComponent<Buff>(out var formBuff) && formBuff.Target.IsPlayer())
        {
            Services.ShapeshiftAbilityService.ApplyFormLoadout(entity, formBuff.Target, prefabId, triggerUpdate: false);
            return;
        }

        // v0.101.0: the SAME in-resolve injection for the MOUNTED form. The horse's mount-control buff
        // carries ReplaceAbilityOnSlotBuff, so it flows through THIS system's query the moment the player
        // mounts — inject the saddle loadout onto its free slots (3/6/7) here, at resolve time, so it
        // actually renders (the heartbeat path set the data AFTER resolve, so the abilities never showed).
        if (Services.ShapeshiftAbilityService.IsMountBuff(prefabId, entity.GetPrefabGuid().GetPrefabName())
            && entity.TryGetComponent<Buff>(out var mountBuff) && mountBuff.Target.IsPlayer())
        {
            Services.ShapeshiftAbilityService.ApplyMountedLoadout(entity, mountBuff.Target, triggerUpdate: false);
            return;
        }

        if (!entity.TryGetComponent<EntityOwner>(out var entityOwner)) return;
        Entity owner = entityOwner.Owner;
        if (!owner.IsPlayer()) return;

        ulong steamId = owner.GetSteamId();
        if (steamId == 0) return;

        if (!Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(entity)) return;

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

        // W2: no active transform. The equip-buff event entity (`entity`) is the player's
        // EquipBuff_Weapon_* — both the modification source and the carrier of the
        // ReplaceAbilityOnSlotBuff buffer. Inject saved grants per slot by weapon-family
        // compatibility (universal/Magic fire on any weapon; weapon-tagged only on that weapon),
        // OR auto-yield a slot the player has re-claimed via the in-game spellbook (v0.56.0).
        PrefabGUID eventPrefab = entity.GetPrefabGuid();
        string eventName = eventPrefab.GetPrefabName() ?? "";
        var weapon = Beelzebub.Services.SlotApply.DetectFamily(eventName);
        if (weapon == Beelzebub.Services.WeaponFamily.None)
        {
            // Unrecognized equip-buff (could be a non-weapon EquipBuff_*). Skip safely.
            return;
        }

        var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(entity);
        // owner = the player character entity; entity = the equip-buff modification source.
        Beelzebub.Services.SlotApply.ResolveAndInjectGrants(owner, entity, weapon, buffer);
    }
}
