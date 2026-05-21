using Beelzebub.Config;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

[HarmonyPatch(typeof(DeathEventListenerSystem), nameof(DeathEventListenerSystem.OnUpdate))]
internal static class DeathEventListenerSystemPatch
{
    [HarmonyPostfix]
    public static void OnUpdatePostfix(DeathEventListenerSystem __instance)
    {
        if (!Core.IsReady || !Settings.CaptureOnKill.Value) return;

        NativeArray<DeathEvent> deathEvents = __instance._DeathEventQuery.ToComponentDataArray<DeathEvent>(Allocator.Temp);
        try
        {
            for (int i = 0; i < deathEvents.Length; i++)
            {
                var deathEvent = deathEvents[i];
                Process(deathEvent);
            }
        }
        finally
        {
            deathEvents.Dispose();
        }
    }

    static void Process(DeathEvent deathEvent)
    {
        Entity killer = deathEvent.Killer;
        Entity died = deathEvent.Died;

        if (killer == died) return;
        if (!killer.IsPlayer()) return;
        if (!died.Exists()) return;
        if (died.Has<VBloodConsumeSource>()) return; // V-Blood: separate hook in a future drop
        if (died.Has<Trader>()) return;
        if (died.Has<BlockFeedBuff>()) return;
        if (!died.Has<UnitLevel>()) return;

        ulong steamId = killer.GetSteamId();
        if (steamId == 0) return;

        PrefabGUID unitGuid = died.GetPrefabGuid();
        if (unitGuid._Value == 0) return;

        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(died))
        {
            if (Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz] {unitGuid.GetPrefabName()} has no AbilityGroupSlotBuffer; nothing to capture.");
            return;
        }

        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(died);
        int captured = 0, skipped = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            PrefabGUID ability = slot.BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;

            string abilityName = ability.GetPrefabName();
            if (!Core.AbilityFilter.ShouldCapture(abilityName, out string reason))
            {
                skipped++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] skip {abilityName}: {reason}");
                continue;
            }

            if (Core.AbilityRegistry.Add(steamId, unitGuid._Value, ability._Value))
            {
                captured++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] capture {abilityName} from {unitGuid.GetPrefabName()} for {steamId}");
            }
        }

        if (captured > 0)
        {
            Core.Log.LogInfo($"[Beelz] {steamId} killed {unitGuid.GetPrefabName()}: captured {captured} ability(ies), skipped {skipped}.");
            Core.Persistence.SaveSync();
        }
    }
}
