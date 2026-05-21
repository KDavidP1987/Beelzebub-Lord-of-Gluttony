using System.Collections.Generic;
using System.Text;
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
    sealed class KillAggregate
    {
        public Entity Character;
        public int Captured;
        public int Skipped;
        public bool AnySave;
        // Unit name → captures from that unit (preserves order of first appearance)
        public Dictionary<string, int> ByUnit = new();
        public List<string> TransformUnlocks = new();
    }

    [HarmonyPostfix]
    public static void OnUpdatePostfix(DeathEventListenerSystem __instance)
    {
        if (!Core.IsReady) Core.TryInitialize(nameof(DeathEventListenerSystemPatch));
        if (!Core.IsReady) return;

        // Phase 5: per-frame tick for auto-revert of Timed transforms.
        Core.Transforms.Tick();
        // Phase B1: drain any pending state save (debounced).
        Core.Persistence.MaybeSave();

        if (!Settings.CaptureOnKill.Value) return;

        NativeArray<DeathEvent> deathEvents = __instance._DeathEventQuery.ToComponentDataArray<DeathEvent>(Allocator.Temp);
        var aggregates = new Dictionary<ulong, KillAggregate>();
        try
        {
            for (int i = 0; i < deathEvents.Length; i++)
            {
                Process(deathEvents[i], aggregates);
            }
        }
        finally
        {
            deathEvents.Dispose();
        }

        FlushAggregates(aggregates);
    }

    static void Process(DeathEvent deathEvent, Dictionary<ulong, KillAggregate> aggregates)
    {
        Entity killer = deathEvent.Killer;
        Entity died = deathEvent.Died;

        if (killer == died) return;
        if (!killer.IsPlayer()) return;
        if (!died.Exists()) return;
        if (died.Has<VBloodConsumeSource>()) return; // V-Blood: separate hook.
        if (died.Has<Trader>()) return;
        if (died.Has<BlockFeedBuff>()) return;
        if (!died.Has<UnitLevel>()) return;

        PrefabGUID unitGuid = died.GetPrefabGuid();
        if (unitGuid._Value == 0) return;
        string unitName = unitGuid.GetPrefabName();

        // B2: who counts as participant?
        var participants = ResolveParticipants(killer, died);
        foreach (var participant in participants)
        {
            ProcessForParticipant(participant, died, unitGuid, unitName, aggregates);
        }
    }

    static List<Entity> ResolveParticipants(Entity killer, Entity died)
    {
        string mode = (Settings.Capture_ShareCreditMode.Value ?? "KillerOnly").Trim();
        if (string.Equals(mode, "Proximity", System.StringComparison.OrdinalIgnoreCase))
        {
            float radius = Settings.Capture_ShareCreditRadius.Value;
            if (radius > 0f && died.TryGetComponent<Unity.Transforms.LocalToWorld>(out var ltw))
            {
                var near = EntityExtensions.FindPlayersNear(ltw.Position, radius);
                // Always include the killer (they may be slightly outside the radius for ranged kills).
                if (!near.Contains(killer)) near.Add(killer);
                return near;
            }
        }
        return new List<Entity> { killer };
    }

    static void ProcessForParticipant(Entity participant, Entity died, PrefabGUID unitGuid, string unitName, Dictionary<ulong, KillAggregate> aggregates)
    {
        if (!participant.IsPlayer()) return;
        ulong steamId = participant.GetSteamId();
        if (steamId == 0) return;

        if (!aggregates.TryGetValue(steamId, out var agg))
        {
            agg = new KillAggregate { Character = participant };
            aggregates[steamId] = agg;
        }

        if (Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(died))
        {
            var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(died);
            for (int i = 0; i < slots.Length; i++)
            {
                PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
                if (ability._Value == 0) continue;

                string abilityName = ability.GetPrefabName();
                if (!Core.AbilityFilter.ShouldCapture(abilityName, ability._Value, out string reason))
                {
                    agg.Skipped++;
                    if (Settings.VerboseLogging.Value)
                        Core.Log.LogInfo($"[Beelz] skip {abilityName}: {reason}");
                    continue;
                }

                // C2: per-ability override beats the global default.
                float chance = Core.AbilityRules.TryGetRateOverride(abilityName, out var orR, out _)
                    ? orR
                    : Settings.DropChance_Ability_Regular.Value;
                // C3: scale by the unit's tier multiplier.
                chance *= died.ResolveTierMultiplier();
                if (chance <= 0f) continue;
                if (chance < 1f && System.Random.Shared.NextDouble() > chance) continue;

                if (Core.AbilityRegistry.Add(steamId, unitGuid._Value, ability._Value, CaptureSource.Regular))
                {
                    agg.Captured++;
                    agg.ByUnit[unitName] = agg.ByUnit.TryGetValue(unitName, out var n) ? n + 1 : 1;
                    agg.AnySave = true;
                    if (Settings.VerboseLogging.Value)
                        Core.Log.LogInfo($"[Beelz] capture {abilityName} from {unitName} for {steamId}");
                    // Verbose: per-ability chat line stays inline so the player sees individual unlocks.
                    Core.Chat.Send(participant, Verbosity.Verbose, $"Acquired ability: {abilityName} (from {unitName}).");
                    // Phase E4: parseable event for BCH (gated by per-player EmitApiEvents flag).
                    Core.Chat.SendEvent(participant,
                        $"[BEELZ:event] type=capture s=R u={unitGuid._Value} un={unitName} a={ability._Value} an={abilityName}");
                }
            }
        }

        // Transform-unlock roll happens once per kill, independent of ability captures.
        float transformChance = Settings.DropChance_Transform_Regular.Value * died.ResolveTierMultiplier();
        if (transformChance > 0f && System.Random.Shared.NextDouble() <= transformChance)
        {
            if (Core.AbilityRegistry.AddTransformUnlock(steamId, unitGuid._Value, CaptureSource.Regular))
            {
                agg.TransformUnlocks.Add(unitName);
                agg.AnySave = true;
                Core.Log.LogInfo($"[Beelz] {steamId} unlocked transform: {unitName} (Regular).");
                Core.Chat.SendEvent(participant,
                    $"[BEELZ:event] type=transform-unlock s=R u={unitGuid._Value} un={unitName}");
            }
        }
    }

    static void FlushAggregates(Dictionary<ulong, KillAggregate> aggregates)
    {
        foreach (var (steamId, agg) in aggregates)
        {
            if (!agg.AnySave) continue;

            if (agg.Captured > 0)
            {
                Core.Log.LogInfo(
                    $"[Beelz] {steamId} kill batch: captured {agg.Captured} ability(ies), skipped {agg.Skipped}, units={agg.ByUnit.Count}.");

                string summary = agg.ByUnit.Count == 1
                    ? BuildSingleUnitSummary(agg)
                    : BuildMultiUnitSummary(agg);
                Core.Chat.Send(agg.Character, Verbosity.Summary, summary);
            }

            if (agg.TransformUnlocks.Count > 0)
            {
                string names = string.Join(", ", agg.TransformUnlocks);
                Core.Chat.Send(agg.Character, Verbosity.Summary,
                    agg.TransformUnlocks.Count == 1
                        ? $"Unlocked transformation: {names}. Use .beelz transforms."
                        : $"Unlocked {agg.TransformUnlocks.Count} transformations: {names}.");
            }

            Core.Persistence.RequestSave();
        }
    }

    static string BuildSingleUnitSummary(KillAggregate agg)
    {
        string unitName = null;
        foreach (var k in agg.ByUnit.Keys) { unitName = k; break; }
        return agg.Captured == 1
            ? $"Acquired 1 new ability from {unitName}."
            : $"Acquired {agg.Captured} new abilities from {unitName}.";
    }

    static string BuildMultiUnitSummary(KillAggregate agg)
    {
        var sb = new StringBuilder();
        sb.Append("Acquired ").Append(agg.Captured).Append(" abilities across ").Append(agg.ByUnit.Count).Append(" units (");
        bool first = true;
        foreach (var (unit, n) in agg.ByUnit)
        {
            if (!first) sb.Append(", ");
            sb.Append(unit);
            if (n > 1) sb.Append('×').Append(n);
            first = false;
            if (sb.Length > 380) { sb.Append(", …"); break; } // stay under the 510-byte chat limit
        }
        sb.Append(").");
        return sb.ToString();
    }
}
