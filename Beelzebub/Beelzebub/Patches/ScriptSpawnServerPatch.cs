using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using ProjectM.Shared;
using ProjectM.Shared.Systems;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Patches;

/// <summary>
/// v0.24.3 — engine hook for AOE/projectile chain ownership fix.
///
/// V Rising's NPC ability chains (Priest's Projectile Nova, Bishop's chained
/// projectiles, etc.) work by spawning intermediate entities (ChannelBuff →
/// ProxySpawner → Projectiles) that carry hard-coded NPC team metadata. When
/// a Beelzebub-transformed PLAYER casts such an ability, the chain produces
/// the right entities but they're stamped with <c>Team.Value=1</c> (NPC team)
/// and <c>TeamReference</c> pointing to NPC team singleton — so the
/// projectiles' collision/hit filter says "don't hit my own team" and they
/// miss the actual targets (which are also Team=1).
///
/// Bloodcraft solves an analogous problem for familiars by setting
/// <c>Team = 2</c> and <c>TeamReference = _unitTeamSingleton</c> on the
/// familiar itself (FamiliarBindingSystem.cs:413-415). We can't do that to
/// the player (the player has their own clan team). Instead, we hook
/// ScriptSpawnServer.OnUpdate at Prefix — the same system Bloodcraft uses
/// for buff stat injection — and walk the owner chain of every spawned
/// entity. If owner resolves to a Beelzebub-transformed player, we rewrite
/// the entity's Team / TeamReference / FactionReference to match the player
/// (so spawned children inherit player team) and set EntityOwner so
/// attribution + aggro broadcasts work downstream.
///
/// This addresses chain failure classes 3 + 4 from the chain audit
/// (project_ability_chain_audit.md). Class 1 (hardcoded offset) and class 2
/// (animation-bound) are separate problems; class 2 is likely unfixable
/// from a server mod.
/// </summary>
[HarmonyPatch(typeof(ScriptSpawnServer), nameof(ScriptSpawnServer.OnUpdate))]
internal static class ScriptSpawnServerPatch
{
    static readonly PrefabGUID PlayerFaction = new(1106458752); // Faction_Players

    static int _hookFireCounter = 0;

    [HarmonyPrefix]
    public static void OnUpdatePrefix(ScriptSpawnServer __instance)
    {
        if (!Core.IsReady) return;

        // v0.43.1 CRASH FIX: fallback enricher for pending async form buffs
        // (LifeTime-less shapeshift forms). BuffSpawnServerPatch catches them in the
        // spawn query first; this polls via TryGetBuff in case the buff isn't in that
        // query on a given tick. Idempotent (registry self-clears). No-op when nothing
        // is pending — runs before the AbilityRegistry guard below so it isn't skipped.
        if (TransformBuffService.HasPendingForms)
        {
            try { TransformBuffService.TryEnrichPendingByPoll(); }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz] form-enrich poll failed: {ex.Message}"); }
        }

        if (Core.AbilityRegistry is null) return;

        // v0.24.8 DIAGNOSTIC: watch the Undead Priest ProjectileNova chain
        // GUIDs and log each one's owner + team REGARDLESS of whether the
        // owner resolves to a player. Earlier diagnostics only logged
        // entities that already resolved to a player, so we could never tell
        // WHICH link in the chain breaks (channel buff → proxy spawner →
        // projectiles). This watch closes that blind spot. Strip once the
        // chain failure point is identified.
        try { WatchNovaChain(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN][watch] failed: {ex.Message}"); }

        // v0.24.7 diagnostic: confirm the hook is being invoked at all.
        // Throttle to every 100th call to avoid log spam but still show up
        // during a test session.
        _hookFireCounter++;
        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;
        if (verbose && _hookFireCounter % 100 == 1)
        {
            Core.Log.LogInfo($"[Beelz CHAIN] ScriptSpawnServer hook fired (count={_hookFireCounter})");
        }

        NativeArray<Entity> entities;
        try
        {
            entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN] ScriptSpawnServer query failed: {ex.Message}");
            return;
        }
        if (verbose && entities.Length > 0 && _hookFireCounter % 50 == 1)
        {
            Core.Log.LogInfo($"[Beelz CHAIN] ScriptSpawnServer query has {entities.Length} entities");
        }

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                if (!e.Exists()) continue;
                try { ProcessSpawnedEntity(e); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN] ProcessSpawnedEntity({e}) failed: {ex.Message}"); }
            }
        }
        finally
        {
            entities.Dispose();
        }

        // v0.24.6: ALSO sweep for non-buff chain entities that escape the
        // ScriptSpawnServer query. The Mistwalk chain spawns a ProxySpawner
        // via SpawnPrefabOnGameplayEvent on the Travel_End buff — the
        // ProxySpawner is NOT a buff and isn't in this system's query, so
        // its Team/Faction stays at the boss's NPC defaults. Same pattern
        // applies to Bishop TrippleBolt children and other deep chains.
        //
        // The sweep runs every time ScriptSpawnServer fires (very often
        // during combat). It walks a broader query for entities with both
        // EntityOwner AND Team, filters by owner-chain to player, and
        // applies the team fixup. Already-processed entities are skipped
        // via a HashSet to keep performance bounded.
        try { SweepNonBuffChainEntities(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN] SweepNonBuffChainEntities failed: {ex.Message}"); }
    }

    // v0.24.6: process-once tracking for chain entities so we don't re-fixup
    // each tick. Entries clear when entities cease to exist.
    static readonly HashSet<Entity> _alreadyFixed = new();
    static int _sweepTickCounter = 0;

    /// <summary>
    /// v0.24.7: public entry point for the sweep, callable from other patches
    /// (e.g., BuffSpawnServerPatch which we know fires reliably).
    /// </summary>
    public static void RunSweepIfPossible()
    {
        if (!Core.IsReady) return;
        if (Core.AbilityRegistry is null) return;
        try { SweepNonBuffChainEntities(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN][sweep] RunSweepIfPossible failed: {ex.Message}"); }
    }

    /// <summary>
    /// v0.24.8: public entry for the Nova-chain watch so it can run from a
    /// system that actually fires per-frame. ScriptSpawnServer.OnUpdate turned
    /// out to fire only rarely (often once per session), so the watch placed
    /// in OnUpdatePrefix never saw the chain entities. The buff-spawn hook
    /// fires reliably during combat, so we relay the watch from there.
    /// </summary>
    public static void RunWatchIfPossible()
    {
        if (!Core.IsReady) return;
        try { WatchNovaChain(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN][watch] RunWatchIfPossible failed: {ex.Message}"); }
    }

    /// <summary>
    /// v0.24.6: broader sweep for non-buff chain entities. Runs alongside
    /// the buff-focused ScriptSpawnServer pass to catch entities spawned by
    /// SpawnPrefabOnGameplayEvent / SpawnPrefabOnDestroy mechanics that
    /// don't go through ScriptSpawnServer's query.
    /// </summary>
    static void SweepNonBuffChainEntities()
    {
        _sweepTickCounter++;

        // Throttle: only run the full sweep every Nth tick to keep
        // performance reasonable. ScriptSpawnServer fires often; we don't
        // need the sweep at full rate. Even at 1/4 rate we still catch
        // entities within 4 frames of spawn — well before the 1s LifeTime
        // of typical proxy spawners.
        if (_sweepTickCounter % 2 != 0) return;

        // Periodic GC on the processed set: every 600 ticks (~10 seconds
        // assuming 60 ticks/sec, more realistically ~30s on dedicated),
        // drop entries for entities that no longer exist so the set
        // doesn't grow unbounded.
        if (_sweepTickCounter % 600 == 0)
        {
            _alreadyFixed.RemoveWhere(e => !e.Exists());
        }

        EntityQuery query;
        try
        {
            query = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<EntityOwner>(),
                ComponentType.ReadOnly<Team>(),
                ComponentType.Exclude<PlayerCharacter>(),
                ComponentType.Exclude<Minion>(),
                ComponentType.Exclude<BlockFeedBuff>(),
                // v0.24.6: exclude Buff entities — those are caught by the
                // buff-focused ScriptSpawnServer + BuffSpawnServer hooks.
                // Excluding here avoids double-processing.
                ComponentType.Exclude<Buff>());
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN] sweep query create failed: {ex.Message}");
            return;
        }

        NativeArray<Entity> sweepEntities;
        try { sweepEntities = query.ToEntityArray(Allocator.Temp); }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN] sweep toEntityArray failed: {ex.Message}");
            return;
        }

        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;
        int candidateCount = 0, ownerResolved = 0, fixedThisTick = 0;

        try
        {
            for (int i = 0; i < sweepEntities.Length; i++)
            {
                Entity e = sweepEntities[i];
                if (!e.Exists()) continue;
                if (_alreadyFixed.Contains(e)) continue;
                candidateCount++;

                if (!e.TryGetComponent<EntityOwner>(out var eo)) continue;
                Entity playerCharacter = ResolveOwningPlayer(eo.Owner);
                if (!playerCharacter.Exists() || !playerCharacter.IsPlayer())
                {
                    // v0.24.7 fix: do NOT mark as "already fixed" if owner-chain
                    // didn't resolve yet. Owner may be set to Entity.Null in the
                    // same frame the entity spawns and get backfilled later.
                    // Marking it as fixed would lock it out forever.
                    continue;
                }

                ulong steamId = playerCharacter.GetSteamId();
                if (steamId == 0) continue;
                // v0.52.0: fix up chain entities for a transformed player OR one who just cast a
                // captured ability untransformed (so captured chain abilities fire vs. enemies).
                if (!AbilityCastStartedSystemPatch.ShouldFixupChainFor(steamId)) continue;

                ownerResolved++;

                // v0.25.0: feed the runtime chain-trace (non-buff spawns).
                Services.ChainTraceService.Observe(e, steamId, "spawn");

                try
                {
                    ProcessSpawnedEntity(e);
                    _alreadyFixed.Add(e);
                    fixedThisTick++;

                    // v0.24.7 diagnostic: log every fix from the sweep
                    // (separate from ProcessSpawnedEntity's own log, which
                    // only fires if components were actually rewritten).
                    if (verbose)
                    {
                        string n = e.GetPrefabGuid().GetPrefabName() ?? "?";
                        Core.Log.LogInfo($"[Beelz CHAIN][sweep] processed {e} prefab={n} for player {steamId}");
                    }
                }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[Beelz CHAIN] sweep fix failed on {e}: {ex.Message}");
                }
            }
        }
        finally
        {
            sweepEntities.Dispose();
        }

        if (verbose && (candidateCount > 0 || fixedThisTick > 0) && _sweepTickCounter % 30 == 0)
        {
            Core.Log.LogInfo($"[Beelz CHAIN][sweep] tick {_sweepTickCounter}: candidates={candidateCount} ownerResolved={ownerResolved} fixed={fixedThisTick} alreadyFixedSize={_alreadyFixed.Count}");
        }
    }

    // v0.24.8 DIAGNOSTIC: the full Undead Priest ProjectileNova chain GUIDs.
    // Each entity carrying one of these prefabs is logged once with its owner
    // + team so we can see exactly where the chain breaks for a player cast.
    static readonly Dictionary<int, string> _novaWatch = new()
    {
        { -1001091278, "ProjectileNova_ChannelBuff" },   // channel buff (C-key); ticks 3x
        {  1249580491, "ProjectileNova_ProxySpawner" },  // fans 12 projectiles
        {  -766139646, "ProjectileNova_Projectile" },    // the AoE projectile
        {   493463494, "Teleport_Travel_End" },          // Mist Walk arrival buff
        {  -461513750, "ProjectileNova_Hard_ChannelBuff" },
        { -1435808132, "ProjectileNova_Hard_ProxySpawner" },
        { -1865623176, "ProjectileNova_Hard_Projectile" },
    };
    static readonly HashSet<Entity> _watchSeen = new();
    static int _watchTick = 0;

    /// <summary>
    /// v0.24.8 DIAGNOSTIC: scan for any Nova-chain entity and log its owner +
    /// team, whether or not the owner resolves to a player. Deduped so each
    /// entity logs once. Verbose-gated.
    /// </summary>
    static void WatchNovaChain()
    {
        if (!Beelzebub.Config.Settings.VerboseLogging.Value) return;

        _watchTick++;
        // GC the seen-set occasionally so it can't grow unbounded.
        if (_watchTick % 600 == 0) _watchSeen.RemoveWhere(e => !e.Exists());

        EntityQuery query;
        try
        {
            query = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PrefabGUID>(),
                ComponentType.ReadOnly<EntityOwner>());
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN][watch] query create failed: {ex.Message}");
            return;
        }

        NativeArray<Entity> ents;
        try { ents = query.ToEntityArray(Allocator.Temp); }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN][watch] toEntityArray failed: {ex.Message}");
            return;
        }

        try
        {
            for (int i = 0; i < ents.Length; i++)
            {
                Entity e = ents[i];
                if (!e.Exists()) continue;
                if (!e.TryGetComponent<PrefabGUID>(out var pg)) continue;
                if (!_novaWatch.TryGetValue(pg._Value, out string label)) continue;
                if (!_watchSeen.Add(e)) continue; // already logged

                // Owner chain.
                Entity ownerRaw = Entity.Null;
                if (e.TryGetComponent<EntityOwner>(out var eo)) ownerRaw = eo.Owner;
                Entity player = ResolveOwningPlayer(ownerRaw);
                bool playerResolved = player.Exists() && player.IsPlayer();
                ulong steamId = playerResolved ? player.GetSteamId() : 0;
                bool transformed = playerResolved
                    && Core.AbilityRegistry?.GetActiveTransform(steamId) is not null;

                // Team / faction snapshot.
                string team = e.TryGetComponent<Team>(out var t) ? t.Value.ToString() : "none";
                string faction = "none";
                if (e.TryGetComponent<FactionReference>(out var fr))
                    faction = fr.FactionGuid._Value._Value.ToString();

                Core.Log.LogInfo(
                    $"[Beelz CHAIN][watch] {label} {e} ownerRaw={ownerRaw} " +
                    $"resolvedPlayer={(playerResolved ? steamId.ToString() : "NO")} " +
                    $"transformed={transformed} team={team} faction={faction}");
            }
        }
        finally
        {
            ents.Dispose();
        }
    }

    static void ProcessSpawnedEntity(Entity entity)
    {
        // We only care about entities with an owner chain we can walk.
        if (!entity.TryGetComponent<EntityOwner>(out var eo)) return;
        Entity playerCharacter = ResolveOwningPlayer(eo.Owner);
        if (!playerCharacter.Exists() || !playerCharacter.IsPlayer()) return;

        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        // v0.52.0: transformed OR recently cast a captured ability untransformed (chain fixup).
        if (!AbilityCastStartedSystemPatch.ShouldFixupChainFor(steamId)) return;

        // Skip entities we already track via the summon-ally path — that
        // pipeline already sets faction + follower correctly. Detect by
        // BlockFeedBuff or Follower presence; if either, it's a summon.
        if (entity.Has<BlockFeedBuff>()) return;
        if (entity.Has<Minion>()) return; // LinkMinionToOwnerOnSpawn handles minion case

        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;

        // Apply the player-team fixup to the spawned entity. Pull team data
        // FROM the player so spawned children read as friendly to the player
        // and hostile to the player's enemies.
        try
        {
            bool rewroteTeam = false;
            bool rewroteFaction = false;
            bool rewroteOwner = false;

            // 1. EntityOwner: ensure the owner points at the player directly
            // so any system reading "who owns this projectile" gets a player
            // (not an intermediate proxy that loses player context).
            if (eo.Owner != playerCharacter)
            {
                entity.With((ref EntityOwner o) => o.Owner = playerCharacter);
                rewroteOwner = true;
            }

            // 2. Team + TeamReference: copy from the player. The player's
            // Team value drives who's friendly vs hostile for collision /
            // hit-filter purposes.
            if (entity.Has<Team>() && playerCharacter.TryGetComponent<Team>(out var playerTeam))
            {
                entity.With((ref Team t) =>
                {
                    if (t.Value != playerTeam.Value)
                    {
                        t.Value = playerTeam.Value;
                        rewroteTeam = true;
                    }
                });
            }
            if (entity.Has<TeamReference>() && playerCharacter.TryGetComponent<TeamReference>(out var playerTeamRef))
            {
                entity.With((ref TeamReference tr) => tr.Value._Value = playerTeamRef.Value._Value);
                rewroteTeam = true;
            }

            // 3. FactionReference: set to Faction_Players so faction-based
            // aggression checks see the spawned entity as player-allied.
            if (entity.Has<FactionReference>())
            {
                entity.With((ref FactionReference fr) =>
                {
                    if (fr.FactionGuid._Value._Value != PlayerFaction._Value)
                    {
                        fr.FactionGuid._Value = PlayerFaction;
                        rewroteFaction = true;
                    }
                });
            }

            if (verbose && (rewroteTeam || rewroteFaction || rewroteOwner))
            {
                string name = entity.GetPrefabGuid().GetPrefabName() ?? "?";
                Core.Log.LogInfo($"[Beelz CHAIN] fixup {entity} prefab={name} for player {steamId} (team={rewroteTeam} faction={rewroteFaction} owner={rewroteOwner})");
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN] mutation failed on {entity}: {ex.Message}");
        }
    }

    /// <summary>
    /// Walk EntityOwner chain up to 4 hops to find a player character.
    /// Mirrors the helper in SummonAllyService / LinkMinion patch.
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
