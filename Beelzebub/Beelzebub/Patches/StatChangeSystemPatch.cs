using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// v0.23.18 — defensive aggro reaction. KEY fix for "horde engages briefly
/// then disengages."
///
/// V Rising's native combat AI does NOT auto-inject attackers into the
/// <c>AggroBuffer</c> of player-allied units (units whose <c>Follower.Followed</c>
/// points at a player). The engine reserves that auto-injection for free NPCs.
/// Without a manual hook:
///
/// * Summon takes damage → no entry in its AggroBuffer.
/// * Behaviour tree sees empty AggroBuffer → drops to Follow/Idle.
/// * The struck unit may flail at the attacker for one tick via residual
///   state, then disengage.
///
/// This is why the user reported "they reacted briefly when the creature
/// attacked them, then stopped reacting" — the brief reaction was V Rising's
/// one-tick residual; the stop was the AggroBuffer being empty.
///
/// Pattern lifted from Bloodcraft <c>StatChangeSystemPatch.ReactToUnitDamage</c>
/// (Bloodcraft-main/Patches/StatChangeSystemPatch.cs:196). On every
/// <c>DamageTakenEvent</c>, if the damaged entity is a tracked summon, we:
/// 1. Resolve the attacker (walk EntityOwner chain from the spell source).
/// 2. Push the attacker into EVERY summon of the owning player's horde
///    (so a hit on one priest skeleton aggros the whole horde — matches the
///    "horde" feel).
/// 3. Set <c>Follower.ModeModifiable = 1</c> on the struck unit (defensive;
///    BuffSpawnServerPatch handles the same flip on PvE combat buff, but
///    that buff applies to the PLAYER, not the summon — if the player is
///    not yet in combat, only the summons are, the player-side flip never
///    fires).
/// 4. Force <c>BehaviourTreeState = Combat</c> so the AI evaluates the
///    fresh AggroBuffer this tick instead of waiting for the next cycle.
/// </summary>
[HarmonyPatch(typeof(StatChangeSystem), nameof(StatChangeSystem.OnUpdate))]
internal static class StatChangeSystemPatch
{
    [HarmonyPrefix]
    public static void OnUpdatePrefix(StatChangeSystem __instance)
    {
        if (!Core.IsReady) return;
        if (!Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value) return;

        NativeArray<Entity> entities;
        NativeArray<DamageTakenEvent> events;
        try
        {
            entities = __instance._DamageTakenEventQuery.ToEntityArray(Allocator.Temp);
            events = __instance._DamageTakenEventQuery.ToComponentDataArray<DamageTakenEvent>(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] StatChange query failed: {ex.Message}");
            return;
        }

        try
        {
            for (int i = 0; i < events.Length; i++)
            {
                DamageTakenEvent evt = events[i];
                Entity target = evt.Entity;
                if (!target.Exists()) continue;

                // Only react if the damaged entity is one of our tracked summons.
                if (!SummonAllyService.IsTrackedSummon(target, out Entity owningPlayer)) continue;
                if (!owningPlayer.Exists()) continue;

                // Resolve attacker (walk EntityOwner chain from the spell source).
                Entity attacker = ResolveAttackerRoot(evt.Source);
                if (!attacker.Exists()) continue;
                if (attacker == target) continue; // ignore self-damage (e.g. DoTs)
                if (attacker == owningPlayer) continue; // ignore owner-on-self (shouldn't happen for summons but defensive)

                // Don't aggro on a friendly hit — if the attacker is itself a
                // tracked summon of the same player, this is friendly-fire
                // (rare but possible with AoE).
                if (SummonAllyService.IsTrackedSummon(attacker, out Entity attackerOwner)
                    && attackerOwner == owningPlayer) continue;

                try
                {
                    SummonAllyService.ReactToDamageOnSummon(target, attacker, owningPlayer);
                }
                catch (Exception ex)
                {
                    Core.Log.LogError($"[Beelz SUMMON] ReactToDamageOnSummon failed: {ex}");
                }
            }
        }
        finally
        {
            entities.Dispose();
            events.Dispose();
        }
    }

    /// <summary>
    /// Walk EntityOwner chain up to 4 hops to find the root entity (the
    /// NPC or player that ultimately spawned this damage source). Stops at
    /// the first entity with no EntityOwner or one whose owner is itself
    /// (self-owned root). Mirrors Bloodcraft's <c>Entity.GetOwner</c>.
    /// </summary>
    static Entity ResolveAttackerRoot(Entity source)
    {
        Entity current = source;
        for (int hop = 0; hop < 4; hop++)
        {
            if (!current.Exists()) return Entity.Null;
            if (!current.TryGetComponent<EntityOwner>(out var eo)) return current;
            if (!eo.Owner.Exists() || eo.Owner == current) return current;
            current = eo.Owner;
        }
        return current;
    }
}
