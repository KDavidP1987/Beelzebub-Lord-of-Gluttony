using Beelzebub.Config;
using Beelzebub.Services;
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
        if (!Core.IsReady) Core.TryInitialize(nameof(DeathEventListenerSystemPatch));
        if (!Core.IsReady) return;

        // Phase 5: per-frame tick for auto-revert of Timed transforms.
        Core.Transforms.Tick();

        if (!Settings.CaptureOnKill.Value) return;

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
            if (!Core.AbilityFilter.ShouldCapture(abilityName, ability._Value, out string reason))
            {
                skipped++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] skip {abilityName}: {reason}");
                continue;
            }

            float chance = Settings.DropChance_Ability_Regular.Value;
            if (chance < 1f && System.Random.Shared.NextDouble() > chance) continue;

            if (Core.AbilityRegistry.Add(steamId, unitGuid._Value, ability._Value, CaptureSource.Regular))
            {
                captured++;
                if (Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz] capture {abilityName} from {unitGuid.GetPrefabName()} for {steamId}");
                Core.Chat.Send(killer, Verbosity.Verbose, $"Acquired ability: {abilityName} (from {unitGuid.GetPrefabName()}).");
            }
        }

        // Transform-unlock roll happens once per kill, independent of ability captures.
        float transformChance = Settings.DropChance_Transform_Regular.Value;
        bool gotTransform = false;
        if (transformChance > 0f && System.Random.Shared.NextDouble() <= transformChance)
        {
            if (Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid._Value, CaptureSource.Regular))
            {
                gotTransform = true;
                string unitName = unitGuid.GetPrefabName();
                Core.Log.LogInfo($"[Beelz] {steamId} unlocked transform: {unitName} (Regular).");
                Core.Chat.Send(killer, Verbosity.Summary,
                    $"Unlocked transformation: {unitName}. Use .beelz transforms to see your unlocks.");
            }
        }

        if (captured > 0 || gotTransform)
        {
            if (captured > 0)
            {
                string unitName = unitGuid.GetPrefabName();
                Core.Log.LogInfo($"[Beelz] {steamId} killed {unitName}: captured {captured} ability(ies), skipped {skipped}.");
                Core.Chat.Send(killer, Verbosity.Summary,
                    captured == 1
                        ? $"Acquired 1 new ability from {unitName}."
                        : $"Acquired {captured} new abilities from {unitName}.");
            }
            Core.Persistence.SaveSync();
        }
    }
}

