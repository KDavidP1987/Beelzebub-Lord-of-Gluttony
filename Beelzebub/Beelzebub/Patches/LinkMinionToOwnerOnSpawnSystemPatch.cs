using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// v0.20.0 — Summon-as-ally support (task #74).
/// v0.23.0 — adds combat-AI components, lifts the 10-cap (staged despawn handles
/// the batch-destroy crash instead), attributes spawns to recent casts for
/// stack-accounting, and routes everything through <see cref="SummonAllyService"/>.
/// </summary>
[HarmonyPatch(typeof(LinkMinionToOwnerOnSpawnSystem), nameof(LinkMinionToOwnerOnSpawnSystem.OnUpdate))]
internal static class LinkMinionToOwnerOnSpawnSystemPatch
{
    [HarmonyPrefix]
    public static void OnUpdatePrefix(LinkMinionToOwnerOnSpawnSystem __instance)
    {
        if (!Core.IsReady) return;
        if (!Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value) return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance._Query.ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] LinkMinionToOwnerOnSpawnSystemPatch failed to read query: {ex}");
            return;
        }

        try
        {
            if (entities.Length > 0 && Beelzebub.Config.Settings.VerboseLogging.Value)
            {
                Core.Log.LogInfo($"[Beelz DIAG] LinkMinion tick: {entities.Length} entity(s) in query.");
            }

            foreach (Entity minion in entities)
            {
                try { ProcessMinion(minion); }
                catch (Exception ex)
                {
                    Core.Log.LogError($"[Beelz] LinkMinionToOwnerOnSpawnSystemPatch entity {minion} failed: {ex}");
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }

    static void ProcessMinion(Entity minion)
    {
        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;

        if (!minion.TryGetComponent<EntityOwner>(out var entityOwner))
        {
            if (verbose) Core.Log.LogInfo($"[Beelz SUMMON][link] skip {minion}: no EntityOwner component.");
            return;
        }
        Entity owner = entityOwner.Owner;
        if (!owner.Exists())
        {
            if (verbose) Core.Log.LogInfo($"[Beelz SUMMON][link] skip {minion}: owner does not exist.");
            return;
        }

        Entity playerCharacter = ResolveOwningPlayer(owner);
        if (!playerCharacter.Exists() || !playerCharacter.IsPlayer())
        {
            if (verbose)
            {
                string ownerName = owner.GetPrefabGuid().GetPrefabName() ?? "?";
                Core.Log.LogInfo($"[Beelz SUMMON][link] skip {minion} prefab={minion.GetPrefabGuid().GetPrefabName()}: owner chain did not reach a player (immediate owner={owner} prefab={ownerName}).");
            }
            return;
        }

        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        // v0.45.0: resolve the summon owner (active transform OR standalone untransformed
        // state). createIfMissing:true — a summon is being linked to this player right now.
        var active = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: true);
        if (active == null) return; // never null with createIfMissing, but guard anyway
        bool transformed = active is ActiveTransform;
        if (active.SummonsDisabled) return; // v0.23.0 toggle

        // Attribute to recent cast (v0.23.0). If a cast of a summon ability fired
        // within the attribution window, credit this spawn to that ability for
        // stack-cap accounting.
        // v0.23.16 (B1 diagnostic): log the attribution lookup so we can see
        // whether the Priest's natural-chain spawns arrive within the 3-second
        // window or are landing too late and being lost.
        int? abilityGuid = null;
        if (AbilityCastStartedSystemPatch.RecentSummonCast.TryGetValue(steamId, out var recent))
        {
            TimeSpan elapsed = DateTime.UtcNow - recent.when;
            bool inWindow = elapsed < AbilityCastStartedSystemPatch.AttributionWindow;
            if (inWindow)
            {
                abilityGuid = recent.abilityGuid;
            }
            if (verbose)
            {
                string abName = new Stunlock.Core.PrefabGUID(recent.abilityGuid).GetPrefabName() ?? "?";
                Core.Log.LogInfo($"[Beelz SUMMON][link] {minion} prefab={minion.GetPrefabGuid().GetPrefabName()} attribution: recent={abName} elapsed={elapsed.TotalMilliseconds:F0}ms inWindow={inWindow}");
            }
        }
        else if (verbose)
        {
            Core.Log.LogInfo($"[Beelz SUMMON][link] {minion} prefab={minion.GetPrefabGuid().GetPrefabName()} attribution: NO recent summon cast registered for this player.");
        }

        // v0.45.0: when NOT transformed, only adopt minions attributable to a recent
        // Beelzebub summon cast — otherwise we'd grab unrelated player minions (coffin
        // servants, other mods' familiars) that merely link to the player this frame.
        // While transformed we keep prior behaviour (boss-kit natural-chain spawns can
        // arrive late / unattributed and are still legitimately the player's).
        if (!transformed && !abilityGuid.HasValue)
        {
            if (verbose) Core.Log.LogInfo($"[Beelz SUMMON][link] skip {minion}: untransformed with no recent summon-cast attribution.");
            return;
        }

        // v0.23.6: over-cap natural-chain spawns — destroy IMMEDIATELY via the
        // multi-path destroy. Prior versions queued to DespawnQueue but the
        // Tick-driven drain didn't run reliably; switching to direct destroy.
        // Caps are now per CAST GROUP (LiveCastCount), so this only fires when
        // the spawn would push the player over their use-cap for this ability.
        // v0.23.17 (fix-fix): use LivePopulatedCastCount instead of LiveCastCount.
        // The previous (LiveCastCount) variant counts empty-within-window groups
        // toward the cap — which is correct for the cast-start cap check, but
        // WRONG here: this spawn is ABOUT TO populate one of those empty groups,
        // so counting it would double-count. Symptom prior to this fix: 3rd
        // admitted cast (cap=3) had its own spawns destroyed at LinkMinion
        // because the group it just opened pushed the empty-aware count to 3.
        // LivePopulatedCastCount counts only groups with at least one alive
        // entity → admitted-cast spawns proceed, refused-cast spawns (whose
        // groups don't exist) still get caught when populated counts reach cap.
        if (abilityGuid.HasValue)
        {
            int liveCount = SummonAllyService.LivePopulatedCastCount(active, abilityGuid.Value);
            int cap = Beelzebub.Config.Settings.Transform_MaxStacksPerSummonAbility.Value;
            if (cap > 0 && liveCount >= cap)
            {
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz SUMMON] over-cap natural-chain spawn {minion} for player {steamId} ability={new Stunlock.Core.PrefabGUID(abilityGuid.Value).GetPrefabName()} — destroying immediately (populated uses {liveCount}/{cap}).");
                SummonAllyService.EnqueueAdminDespawn(minion);
                SummonAllyService.DrainAdminQueueImmediate();
                return;
            }
        }

        if (!SummonAllyService.ApplyPlayerAllySetup(minion, playerCharacter)) return;

        active.SummonedMinions ??= new System.Collections.Generic.List<Entity>();
        active.SummonedMinions.Add(minion);
        if (abilityGuid.HasValue)
        {
            // v0.23.6: TrackInCurrentGroup — appends to the most recent cast
            // group opened by AbilityCastStartedSystemPatch. If no group is open
            // (e.g. orphan natural-chain arrived outside attribution window),
            // this no-ops and the entity is just in SummonedMinions general list.
            SummonAllyService.TrackInCurrentGroup(active, abilityGuid.Value, minion);
        }

        if (Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            string attribution = abilityGuid.HasValue
                ? $" (attributed to ability {new Stunlock.Core.PrefabGUID(abilityGuid.Value).GetPrefabName()})"
                : " (no recent cast — unattributed; counts toward master list only)";
            Core.Log.LogInfo($"[Beelz] summon-rebind {steamId}: minion={minion} ownerWas={entityOwner.Owner} -> player ({playerCharacter}){attribution}");
        }
    }

    /// <summary>
    /// Walk the EntityOwner chain up to 4 levels until we find a player character.
    /// </summary>
    static Entity ResolveOwningPlayer(Entity start)
    {
        Entity current = start;
        for (int hop = 0; hop < 4; hop++)
        {
            if (!current.Exists()) return Entity.Null;
            if (current.IsPlayer()) return current;
            if (!current.TryGetComponent<EntityOwner>(out var eo)) return Entity.Null;
            if (eo.Owner == current) return Entity.Null;
            current = eo.Owner;
        }
        return Entity.Null;
    }
}
