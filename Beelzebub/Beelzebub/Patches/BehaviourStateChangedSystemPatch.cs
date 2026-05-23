using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using ProjectM.Behaviours;
using ProjectM.Gameplay.Systems;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// v0.23.16 — combat-AI fix for inconsistent summon engagement (B2).
///
/// Pattern lifted from Bloodcraft <c>BehaviourStateChangedSystemPatch</c>.
/// V Rising's <c>CreateGameplayEventOnBehaviourStateChangedSystem</c> fires
/// whenever an NPC AI transitions between behaviour-tree states (Follow,
/// Combat, Return, Idle, etc.). For free-roaming NPCs the natural transitions
/// to <c>Return</c> (give up on combat, head back to leash anchor) or
/// <c>Idle</c> (no combat target, nothing to do) are correct — but for our
/// player-allied summons those transitions get the unit stuck:
///
///   * <b>Return</b> — the unit tries to walk back to its NPC spawn anchor
///     instead of following the player. Bloodcraft observed the same issue
///     with familiars; their fix is to force-flip Return → Follow whenever
///     the event fires for a familiar.
///   * <b>Idle</b> — the unit just stops. Empty AggroBuffer + no follow
///     target = it sits where it is. Familiars route through TryReturnFamiliar
///     to teleport back to the player. We do the same via the existing
///     LeashCheckAll path but force-flipping the state here prevents the
///     unit from getting stuck mid-state-transition.
///
/// Without this patch, the user reported "some summons engage, others
/// stand idle even when struck" — the inconsistency is precisely because
/// some summons happened to transition through Return/Idle and never came
/// back to Follow, while others (e.g. the ones that took damage first)
/// kept their Combat state.
///
/// Identification: an entity is "ours" if it's tracked in any active
/// transform's <see cref="ActiveTransform.SummonedMinions"/> list. See
/// <see cref="SummonAllyService.IsTrackedSummon"/>.
/// </summary>
[HarmonyPatch(typeof(CreateGameplayEventOnBehaviourStateChangedSystem),
              nameof(CreateGameplayEventOnBehaviourStateChangedSystem.OnUpdate))]
internal static class BehaviourStateChangedSystemPatch
{
    [HarmonyPrefix]
    public static void OnUpdatePrefix(CreateGameplayEventOnBehaviourStateChangedSystem __instance)
    {
        if (!Core.IsReady) return;
        if (!Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value) return;

        NativeArray<Entity> entities;
        NativeArray<BehaviourTreeStateChangedEvent> events;
        try
        {
            entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
            events = __instance.EntityQueries[0].ToComponentDataArray<BehaviourTreeStateChangedEvent>(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] BehaviourStateChanged query failed: {ex.Message}");
            return;
        }

        try
        {
            for (int i = 0; i < events.Length; i++)
            {
                Entity source = entities[i];
                BehaviourTreeStateChangedEvent evt = events[i];
                Entity target = evt.Entity;
                if (!target.Exists()) continue;
                if (!SummonAllyService.IsTrackedSummon(target, out Entity owningPlayer)) continue;

                bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;

                // Force-flip Return → Follow. Without this, the unit walks back
                // toward its NPC spawn anchor and never engages again.
                if (evt.NewState == GenericEnemyState.Return)
                {
                    try
                    {
                        target.With((ref BehaviourTreeState s) => s.Value = GenericEnemyState.Follow);
                        // Also update the event itself so downstream systems
                        // (any other state-change reactor) see the corrected state.
                        if (source.Has<BehaviourTreeStateChangedEvent>())
                        {
                            source.With((ref BehaviourTreeStateChangedEvent e) => e.NewState = GenericEnemyState.Follow);
                        }
                        if (verbose)
                            Core.Log.LogInfo($"[Beelz SUMMON] state-fix: {target} Return→Follow (owner={owningPlayer})");
                    }
                    catch (Exception ex)
                    {
                        Core.Log.LogWarning($"[Beelz SUMMON] state-flip Return→Follow failed on {target}: {ex.Message}");
                    }
                }
                // Force-flip Idle → Follow for the same reason. If the unit
                // genuinely has no target, Follow at least keeps it near the
                // player so a future enemy contact will trigger combat.
                else if (evt.NewState == GenericEnemyState.Idle)
                {
                    try
                    {
                        target.With((ref BehaviourTreeState s) => s.Value = GenericEnemyState.Follow);
                        if (source.Has<BehaviourTreeStateChangedEvent>())
                        {
                            source.With((ref BehaviourTreeStateChangedEvent e) => e.NewState = GenericEnemyState.Follow);
                        }
                        if (verbose)
                            Core.Log.LogInfo($"[Beelz SUMMON] state-fix: {target} Idle→Follow (owner={owningPlayer})");
                    }
                    catch (Exception ex)
                    {
                        Core.Log.LogWarning($"[Beelz SUMMON] state-flip Idle→Follow failed on {target}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            entities.Dispose();
            events.Dispose();
        }
    }
}
