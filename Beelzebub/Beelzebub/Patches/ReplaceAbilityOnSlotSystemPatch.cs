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

        NativeArray<Entity> entities = __instance.__query_1482480545_0.ToEntityArray(Allocator.Temp);
        try
        {
            foreach (Entity entity in entities)
            {
                if (!entity.TryGetComponent<EntityOwner>(out var entityOwner)) continue;
                Entity owner = entityOwner.Owner;
                if (!owner.IsPlayer()) continue;

                ulong steamId = owner.GetSteamId();
                if (steamId == 0) continue;

                if (!Core.EntityManager.HasBuffer<ReplaceAbilityOnSlotBuff>(entity)) continue;
                var buffer = Core.EntityManager.GetBuffer<ReplaceAbilityOnSlotBuff>(entity);

                // Transform takes precedence: it overrides slots 1-6 with the unit's
                // filtered ability list (up to six). Grant assignments are ignored while
                // a transform is active, so reverting cleanly restores them next equip.
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
                    continue;
                }

                var slots = Core.AbilityRegistry.GetSlots(steamId);
                if (slots.Count == 0) continue;
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
                        Core.Log.LogInfo($"[Beelz] inject slot={slot} ability={new PrefabGUID(abilityGuid).GetPrefabName()} for {steamId}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
