using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Behaviours;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Beelzebub.Services;

/// <summary>
/// v0.23.0 — consolidated lifecycle for player-allied summons.
///
/// Replaces the inline recipe that lived in <c>AbilityCastStartedSystemPatch</c> and
/// <c>LinkMinionToOwnerOnSpawnSystemPatch</c>. Encapsulates:
///   * The component recipe that flips a freshly-spawned NPC unit into a
///     player-allied combat ally (faction/follower/owner/marker + zero proximity
///     aggro so AI relies on manual aggro injection).
///   * The periodic <see cref="SyncAggroAll"/> tick that pushes the player's
///     active enemies into each summon's <c>AggroBuffer</c> — the piece our
///     v0.22.x recipes were missing, leaving summons standing around passively.
///   * Staged-despawn drain (<see cref="DrainDespawnQueues"/>) — V Rising crashes
///     when too many entities are destroyed in a single frame, so revert pushes
///     to <c>DespawnQueue</c> and we destroy <c>DespawnBudget</c>/frame.
///
/// Pattern lifted from Bloodcraft <c>FamiliarBindingSystem.ModifyFollowerFactionMinion</c>
/// + <c>Familiars.SyncAggro</c>.
/// </summary>
internal static class SummonAllyService
{
    static readonly PrefabGUID PlayerFaction = new(1106458752); // Faction_Players
    const float MaxAggroRange = 25f;
    const float DistanceAggroBase = 100f;

    /// <summary>
    /// v0.23.16 (B2): check whether the given entity is currently tracked as a
    /// summon by ANY active transform's SummonedMinions list. Used by
    /// <see cref="Patches.BehaviourStateChangedSystemPatch"/> to identify "our"
    /// entities for state-change interception (force Return/Idle → Follow).
    /// Sets <paramref name="owningPlayer"/> to the player entity for the
    /// active transform that owns this summon (Entity.Null if unfound).
    /// </summary>
    public static bool IsTrackedSummon(Entity entity, out Entity owningPlayer)
    {
        owningPlayer = Entity.Null;
        if (!entity.Exists()) return false;
        foreach (var (steamId, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            if (active.SummonedMinions == null) continue;
            for (int i = 0; i < active.SummonedMinions.Count; i++)
            {
                if (active.SummonedMinions[i] == entity)
                {
                    owningPlayer = EntityExtensions.FindCharacterBySteamId(steamId);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Apply the full player-ally setup to a freshly-spawned minion. Returns true
    /// if the minion is now in a state where it should fight alongside the player.
    /// Safe to call multiple times on the same entity (idempotent component writes).
    /// </summary>
    public static bool ApplyPlayerAllySetup(Entity minion, Entity playerCharacter)
    {
        if (!minion.Exists() || !playerCharacter.Exists()) return false;

        try
        {
            // 1. Owner.
            if (minion.Has<EntityOwner>())
            {
                minion.With((ref EntityOwner eo) => eo.Owner = playerCharacter);
            }

            // 2. Faction — flip to Faction_Players so other players + the owner
            // read as non-hostile to the minion's targeting.
            if (minion.Has<FactionReference>())
            {
                minion.With((ref FactionReference fr) => fr.FactionGuid._Value = PlayerFaction);
            }

            // 3. Follower — leash to the player. v0.23.15: ModeModifiable=0
            // (leash mode) on spawn. v0.23.6-8 toggled this to 1 because it
            // was the only way combat AI engaged — but mode=1 also caused
            // vortex follow (units clipping into player space). Bloodcraft's
            // pattern (Utilities/Familiars.cs:518 + StatChangeSystemPatch:204)
            // is: default mode=0, dynamically swap to mode=1 when the player
            // enters combat (PvE combat buff applies), back to mode=0 when
            // combat ends. We mirror that — see SetCombatMode + BuffSpawnServerPatch
            // and UpdateBuffsBufferDestroyPatch hooks for PvE combat buff.
            if (minion.Has<Follower>())
            {
                minion.With((ref Follower f) =>
                {
                    f.Followed._Value = playerCharacter;
                    f.ModeModifiable._Value = 0;
                });
            }

            // 4. Minion master-death-action — despawn when the owner dies.
            if (minion.Has<Minion>())
            {
                minion.With((ref Minion m) => m.MasterDeathAction = MinionMasterDeathAction.Kill);
            }

            // 5. BlockFeedBuff marker. Bloodcraft uses this as a "this is a player-
            // allied unit" tag — other systems (combat patches, feed/blood logic)
            // check for it. Add if missing.
            if (!minion.Has<BlockFeedBuff>())
            {
                try { Core.EntityManager.AddComponent<BlockFeedBuff>(minion); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] add BlockFeedBuff failed: {ex.Message}"); }
            }

            // v0.23.6: REMOVED aggro-modifier zeroing. v0.23.0-5 zeroed
            // AlertModifiers/AggroModifiers/GainAggroByVicinity/GainAlertByVicinity
            // following Bloodcraft's familiar pattern — but that pattern is for
            // familiars in dismissable/leashed mode where Bloodcraft INJECTS aggro
            // via SyncAggro per combat event. For our autonomous combat summons,
            // we need native proximity detection ON so the unit retaliates when
            // struck. Leaving these intact lets V Rising's natural NPC AI work.
            //
            // v0.23.6: REMOVED FollowerBuffer add. v0.23.1 added the summon to the
            // player's FollowerBuffer so other systems recognize them. But V Rising's
            // waygate "subdued unit" pre-check appears to count FollowerBuffer
            // entries — adding caused the teleport block. Follower.Followed._Value
            // alone is enough to keep the leash + ownership working.

            // 8. v0.23.1: strip ServantConvertable + CharmSource. V Rising's waygate
            // pre-check refuses teleport if the player has "subdued" units; any unit
            // carrying ServantConvertable that's also player-allied reads as subdued.
            // This was the root cause of "cannot use waygate while an enemy is subdued."
            try
            {
                if (minion.Has<ServantConvertable>()) Core.EntityManager.RemoveComponent<ServantConvertable>(minion);
                if (minion.Has<CharmSource>()) Core.EntityManager.RemoveComponent<CharmSource>(minion);
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] strip convertable failed: {ex.Message}"); }

            // 9. v0.23.1: zero the drop table so killed summons don't drop loot/items
            // V Rising would normally give. Matches Bloodcraft's RemoveDropTable.
            try
            {
                if (Core.EntityManager.HasBuffer<DropTableBuffer>(minion))
                {
                    var dropBuf = Core.EntityManager.GetBuffer<DropTableBuffer>(minion);
                    for (int i = 0; i < dropBuf.Length; i++)
                    {
                        var item = dropBuf[i];
                        item.DropTableGuid = PrefabGUID.Empty;
                        item.DropTrigger = DropTriggerType.OnSalvageDestroy;
                        item.RelicType = RelicType.None;
                        dropBuf[i] = item;
                    }
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] strip drop table failed: {ex.Message}"); }

            // 10. v0.23.1: relax collision so summons don't shove the player off cliffs.
            if (minion.Has<DynamicCollision>())
            {
                minion.With((ref DynamicCollision dc) =>
                {
                    dc.AgainstPlayers.RadiusOverride = -1f;
                    dc.AgainstPlayers.HardnessThreshold._Value = 0.1f;
                    dc.AgainstPlayers.PushStrengthMax._Value = 0f;
                    dc.AgainstPlayers.PushStrengthMin._Value = 0f;
                });
            }

            // 11. v0.23.1: prevent V Rising from disabling the minion when "no players in
            // range" — for player allies we want them to stick around even if the player
            // teleports far. CanPreventDisableWhenNoPlayersInRange.CanDisable = false.
            try
            {
                if (!minion.Has<CanPreventDisableWhenNoPlayersInRange>())
                {
                    Core.EntityManager.AddComponent<CanPreventDisableWhenNoPlayersInRange>(minion);
                }
                minion.With((ref CanPreventDisableWhenNoPlayersInRange cpd) => cpd.CanDisable = new ModifiableBool(false));
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] PreventDisable failed: {ex.Message}"); }

            // 12. v0.23.16 (B2 fix): ensure aggro systems are ACTIVE. Some V Rising
            // unit prefabs spawn with Aggroable._Value = false (units that are "not
            // currently a valid target" — graveyard skeletons before they're
            // animated, etc.) or AggroConsumer.Active._Value = false (the unit
            // doesn't process aggro events). Without these enabled, the unit
            // can't acquire targets or react to damage — explains why some
            // Priest horde summons engage and others stand idle (the inactive
            // ones never hear about the aggro pushed via SyncAggro/InjectAggro).
            try
            {
                if (minion.Has<Aggroable>())
                {
                    minion.With((ref Aggroable a) =>
                    {
                        a.Value._Value = true;
                        a.DistanceFactor._Value = 1f;
                        a.AggroFactor._Value = 1f;
                    });
                }
                if (minion.Has<AggroConsumer>())
                {
                    minion.With((ref AggroConsumer ac) => ac.Active._Value = true);
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] Aggroable/AggroConsumer enable failed: {ex.Message}"); }

            // 13. v0.23.16 (B2 fix): set BehaviourTreeState to Follow as the initial
            // state. Bloodcraft observed that freshly-spawned familiar units sometimes
            // initialized into Idle or Return — which then required user damage to
            // trigger a state transition out. Forcing Follow at spawn means the
            // unit immediately walks to the player and the BehaviourStateChanged
            // patch (new v0.23.16) catches any future bad transitions.
            try
            {
                if (minion.Has<BehaviourTreeState>())
                {
                    minion.With((ref BehaviourTreeState s) => s.Value = GenericEnemyState.Follow);
                }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] BehaviourTreeState init failed: {ex.Message}"); }

            // 14. Seed initial aggro — push the player's current combat targets
            // straight into the minion's AggroBuffer so it starts engaging
            // immediately rather than waiting for the next SyncAggroAll tick.
            SyncAggro(playerCharacter, minion);

            // 15. v0.43.6: scale the summon to the player so it stays relevant.
            // A summoned add keeps the boss's BAKED level/stats by default — a
            // level-40-boss add stays level-40-weak as the player out-levels it.
            // Match the player's UnitLevel (survivability + level-appropriate
            // damage modifier) + apply the admin power factor to its stats/health.
            ScaleSummonToPlayer(minion, playerCharacter);

            // v0.23.16 (B2 diagnostic): log post-setup component state per summon
            // so we can confirm setup was complete when verbose-logging is on.
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
            {
                int aggroLen = -1;
                try
                {
                    if (Core.EntityManager.HasBuffer<AggroBuffer>(minion))
                        aggroLen = Core.EntityManager.GetBuffer<AggroBuffer>(minion).Length;
                }
                catch { /* diagnostic; non-critical */ }

                string state = "n/a";
                if (minion.TryGetComponent<BehaviourTreeState>(out var btsLog)) state = btsLog.Value.ToString();

                int faction = 0;
                if (minion.TryGetComponent<FactionReference>(out var frLog)) faction = frLog.FactionGuid._Value._Value;

                int mode = -1;
                if (minion.TryGetComponent<Follower>(out var fLog)) mode = fLog.ModeModifiable._Value;

                Core.Log.LogInfo($"[Beelz SUMMON][setup-ok] {minion} prefab={minion.GetPrefabGuid().GetPrefabName()} faction={faction} followerMode={mode} state={state} aggroLen={aggroLen}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] ApplyPlayerAllySetup failed on {minion}: {ex}");
            return false;
        }
    }

    /// <summary>
    /// v0.43.6: scale a summoned ally to its owning player so it doesn't go stale.
    ///   * <c>Transform_SummonMatchPlayerLevel</c> (default true): set the summon's
    ///     UnitLevel to the player's — V Rising's level-difference modifier then keeps
    ///     its damage/defense appropriate against the player's tier of enemies.
    ///   * <c>Transform_SummonPowerFactor</c> (default 1.0): multiply the summon's
    ///     PhysicalPower / SpellPower / MaxHealth (admin dial; the lever for raw
    ///     damage-output, since UnitLevel mainly drives the level-difference modifier).
    /// Best-effort + fully guarded — never throws into the caller.
    /// </summary>
    static void ScaleSummonToPlayer(Entity minion, Entity playerCharacter)
    {
        try
        {
            if (Beelzebub.Config.Settings.Transform_SummonMatchPlayerLevel.Value
                && minion.Has<UnitLevel>()
                && playerCharacter.TryGetComponent<UnitLevel>(out var playerLevel))
            {
                int lvl = playerLevel.Level._Value;
                minion.With((ref UnitLevel ul) => ul.Level._Value = lvl);
            }

            float factor = Beelzebub.Config.Settings.Transform_SummonPowerFactor.Value;
            if (factor > 0f && System.Math.Abs(factor - 1f) > 0.001f)
            {
                if (minion.Has<UnitStats>())
                {
                    minion.With((ref UnitStats s) =>
                    {
                        s.PhysicalPower._Value *= factor;
                        s.SpellPower._Value *= factor;
                    });
                }
                if (minion.Has<Health>())
                {
                    minion.With((ref Health h) =>
                    {
                        h.MaxHealth._Value *= factor;
                        h.Value *= factor;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] ScaleSummonToPlayer failed on {minion}: {ex.Message}");
        }
    }

    /// <summary>
    /// Copy the player's <c>InverseAggroBufferElement</c> targets (i.e. units
    /// currently aggroing the player) into the summon's <c>AggroBuffer</c>.
    /// Lifted from Bloodcraft <c>Familiars.SyncAggro</c>.
    /// </summary>
    public static void SyncAggro(Entity playerCharacter, Entity summon)
    {
        if (!playerCharacter.Exists() || !summon.Exists()) return;
        if (!Core.EntityManager.HasBuffer<InverseAggroBufferElement>(playerCharacter)) return;
        if (!Core.EntityManager.HasBuffer<AggroBuffer>(summon)) return;

        var inverse = Core.EntityManager.GetBuffer<InverseAggroBufferElement>(playerCharacter);
        if (inverse.Length == 0) return;

        var existing = Core.EntityManager.GetBuffer<AggroBuffer>(summon);
        var present = new HashSet<Entity>();
        for (int i = 0; i < existing.Length; i++) present.Add(existing[i].Entity);

        float3 playerPos = float3.zero;
        if (playerCharacter.TryGetComponent<LocalToWorld>(out var ltw)) playerPos = ltw.Position;

        for (int i = 0; i < inverse.Length; i++)
        {
            Entity target = inverse[i].Entity;
            if (!target.Exists()) continue;
            if (present.Contains(target)) continue;

            float3 targetPos = float3.zero;
            if (target.TryGetComponent<LocalToWorld>(out var ttw)) targetPos = ttw.Position;
            float dx = playerPos.x - targetPos.x;
            float dz = playerPos.z - targetPos.z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            float factor = Math.Max(0f, 1f - distance / MaxAggroRange);
            float aggroValue = 100f + DistanceAggroBase * factor;

            existing.Add(new AggroBuffer { DamageValue = aggroValue, Entity = target, Weight = 1f });
        }
    }

    /// <summary>
    /// Periodic tick (called from <c>TransformService.Tick</c>): for every active
    /// transform with live summons, push the player's enemies into each summon's
    /// aggro buffer. This is what turns the summons from "standing around" to
    /// "actually fighting alongside the player".
    /// </summary>
    public static void SyncAggroAll()
    {
        foreach (var (steamId, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            if (active.SummonsDisabled) continue;
            if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) continue;

            Entity player = EntityExtensions.FindCharacterBySteamId(steamId);
            if (!player.Exists()) continue;

            for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
            {
                Entity summon = active.SummonedMinions[i];
                if (!summon.Exists())
                {
                    active.SummonedMinions.RemoveAt(i);
                    continue;
                }
                SyncAggro(player, summon);
            }
        }
    }

    /// <summary>
    /// v0.23.2: global admin-driven despawn queue. Populated by `.beelz admin
    /// desummon` / `desummon-all` commands which query the world for orphan
    /// Beelzebub-tagged ally entities. Drained on the same staged budget as
    /// transform queues so big sweeps don't crash V Rising's destroy path.
    /// </summary>
    static readonly Queue<Entity> _adminQueue = new();
    static readonly HashSet<Entity> _adminQueueSet = new(); // v0.23.7: dedupe
    public static void EnqueueAdminDespawn(Entity e)
    {
        if (!e.Exists()) return;
        if (_adminQueueSet.Add(e))
        {
            _adminQueue.Enqueue(e);
        }
    }
    public static int AdminQueueSize => _adminQueue.Count;

    /// <summary>
    /// v0.23.8: manual stash. v0.23.9 update: now follows Bloodcraft's exact
    /// DismissFamiliar pattern — clears <c>Follower.Followed</c> to Entity.Null
    /// BEFORE adding &lt;Disabled&gt;, and defensively removes the entity from
    /// the player's <c>FollowerBuffer</c>. V Rising's waygate "subdued enemy"
    /// check looks at entities with Follower.Followed pointing to the player,
    /// regardless of Disabled state — just adding Disabled isn't enough.
    /// Returns count stashed.
    /// </summary>
    public static int StashAll(ActiveTransform active, Entity playerCharacter)
    {
        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return 0;
        int stashed = 0;
        bool hasFollowerBuffer = playerCharacter.Exists()
            && Core.EntityManager.HasBuffer<FollowerBuffer>(playerCharacter);

        for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
        {
            Entity e = active.SummonedMinions[i];
            if (!e.Exists()) { active.SummonedMinions.RemoveAt(i); continue; }
            try
            {
                // v0.23.9 KEY: nullify Follower.Followed first. Without this,
                // V Rising still sees the entity as a player follower and the
                // waygate pre-check refuses teleport.
                if (e.Has<Follower>())
                {
                    e.With((ref Follower f) => f.Followed._Value = Entity.Null);
                }

                // Defensive: remove from player's FollowerBuffer if V Rising
                // added the entity there via any of its own systems.
                if (hasFollowerBuffer)
                {
                    var buf = Core.EntityManager.GetBuffer<FollowerBuffer>(playerCharacter);
                    for (int b = buf.Length - 1; b >= 0; b--)
                    {
                        if (buf[b].Entity._Entity == e) { buf.RemoveAt(b); break; }
                    }
                }

                if (!e.Has<Disabled>()) Core.EntityManager.AddComponent<Disabled>(e);
                active.StashedSummons.Add(e);
                active.SummonedMinions.RemoveAt(i);
                stashed++;
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz SUMMON] stash failed on {e}: {ex.Message}");
            }
        }
        return stashed;
    }

    /// <summary>
    /// v0.23.8: manual restore. For each stashed summon, remove &lt;Disabled&gt;,
    /// teleport to player's current position (radial scatter), and move back
    /// into <c>SummonedMinions</c>. Returns count restored.
    /// v0.23.17 (B2 fix): also fully re-primes combat readiness. Without this,
    /// units stayed in whatever <c>BehaviourTreeState</c> they had at stash
    /// time (usually Idle, since stashing happens at waygate cast = out of
    /// combat), with stale/empty <c>AggroBuffer</c>, and our <c>BehaviourState
    /// ChangedSystemPatch</c> never fires on a unit whose state isn't currently
    /// CHANGING — so they sit idle post-arrival even when the player engages
    /// a new enemy. Restore now explicitly: forces state to Follow, mode to
    /// leash (0), re-enables Aggroable/AggroConsumer (in case they got knocked
    /// out during the Disabled period), and re-seeds aggro from the player's
    /// current InverseAggroBuffer so any immediate combat at arrival lands
    /// in the buffer right away.
    /// </summary>
    public static int RestoreAll(ActiveTransform active, Entity playerCharacter)
    {
        if (active.StashedSummons == null || active.StashedSummons.Count == 0) return 0;
        float3 destPos = float3.zero;
        if (playerCharacter.Exists() && playerCharacter.TryGetComponent<LocalToWorld>(out var ltw))
            destPos = ltw.Position;

        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;
        int restored = 0;
        int total = active.StashedSummons.Count;
        for (int i = active.StashedSummons.Count - 1; i >= 0; i--)
        {
            Entity e = active.StashedSummons[i];
            if (!e.Exists()) { active.StashedSummons.RemoveAt(i); continue; }
            try
            {
                if (e.Has<Disabled>()) Core.EntityManager.RemoveComponent<Disabled>(e);

                // Radial scatter so all units don't unstash on top of one another.
                float angle = (float)(restored * (Math.PI * 2.0 / Math.Max(1, total)));
                float3 offset = new float3((float)Math.Cos(angle) * 2f, 0f, (float)Math.Sin(angle) * 2f);
                float3 spot = destPos + offset;
                if (e.Has<Translation>()) e.With((ref Translation t) => t.Value = spot);
                if (e.Has<LastTranslation>()) e.With((ref LastTranslation lt) => lt.Value = spot);

                // Rebind Follower to the (possibly new) player entity in case the
                // player's ECS ID changed during the stash period.
                // v0.23.17: default to leash mode (0). v0.23.15 dynamic toggle
                // handles flipping to combat (1) when PvE combat buff applies.
                if (e.Has<Follower>() && playerCharacter.Exists())
                {
                    e.With((ref Follower f) =>
                    {
                        f.Followed._Value = playerCharacter;
                        f.ModeModifiable._Value = 0;
                    });
                }

                // v0.23.17: re-enable aggro systems. While Disabled, V Rising
                // doesn't tick aggro components — they may be stuck in whatever
                // state they had at stash, including Aggroable._Value = false
                // for some prefab variants. Re-prime explicitly.
                if (e.Has<Aggroable>())
                {
                    e.With((ref Aggroable a) =>
                    {
                        a.Value._Value = true;
                        a.DistanceFactor._Value = 1f;
                        a.AggroFactor._Value = 1f;
                    });
                }
                if (e.Has<AggroConsumer>())
                {
                    e.With((ref AggroConsumer ac) => ac.Active._Value = true);
                }

                // v0.23.17: force BehaviourTreeState to Follow. Units stashed
                // mid-Idle/Return remain in that state through Disabled; the
                // state-change patch only intercepts active transitions, not
                // stuck-from-before states. Reset on restore.
                if (e.Has<BehaviourTreeState>())
                {
                    e.With((ref BehaviourTreeState s) => s.Value = GenericEnemyState.Follow);
                }

                // v0.23.17: re-seed aggro from player's current targets so any
                // ongoing combat at arrival lands in this summon's buffer
                // immediately (don't wait for SyncAggroAll tick or the next
                // damage event to populate from a cold buffer).
                SyncAggro(playerCharacter, e);

                active.SummonedMinions ??= new List<Entity>();
                active.SummonedMinions.Add(e);
                active.StashedSummons.RemoveAt(i);
                restored++;

                if (verbose)
                {
                    int aggroLen = -1;
                    try
                    {
                        if (Core.EntityManager.HasBuffer<AggroBuffer>(e))
                            aggroLen = Core.EntityManager.GetBuffer<AggroBuffer>(e).Length;
                    }
                    catch { /* diagnostic; non-critical */ }
                    Core.Log.LogInfo($"[Beelz SUMMON][restore] {e} prefab={e.GetPrefabGuid().GetPrefabName()} re-primed combat (followerMode=0, state=Follow, aggroLen={aggroLen})");
                }
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz SUMMON] restore failed on {e}: {ex.Message}");
            }
        }
        return restored;
    }

    /// <summary>
    /// v0.23.5: immediate-drain mode for admin commands. Skips the staged queue,
    /// destroys all queued entities right now in one batch. Safe for admin
    /// cleanup of orphan stale summons (no combat-frame pressure). Tries multiple
    /// destroy paths because some persistent entities resist standard destroy:
    ///   1. DestroyUtility.Destroy(EntityManager, e) — Bloodcraft's pattern
    ///   2. EntityManager.DestroyEntity(e) — direct destroy
    ///   3. Add &lt;Disabled&gt; component — fallback that hides + neutralizes
    /// Returns count successfully processed (any path).
    /// </summary>
    public static int DrainAdminQueueImmediate()
    {
        int processed = 0;
        int failed = 0;
        while (_adminQueue.Count > 0)
        {
            Entity e = _adminQueue.Dequeue();
            if (!e.Exists()) continue;

            bool destroyed = TryDestroyAllPaths(e);
            if (destroyed) processed++;
            else failed++;
        }
        _adminQueueSet.Clear(); // v0.23.7: reset dedupe set after drain
        Core.Log.LogInfo($"[Beelz SUMMON] admin immediate-drain: processed={processed} failed={failed}");
        return processed;
    }

    static bool TryDestroyAllPaths(Entity e)
    {
        // v0.23.7: DestroyUtility.Destroy is V Rising's preferred entity destroy
        // path (used by Bloodcraft for familiars). It's asynchronous — entity
        // remains briefly in EntityManager until end-of-frame, so we can't
        // verify via .Exists() right after. Trust the call if it doesn't throw.
        // Only fall through to fallback paths if it actually throws.
        try
        {
            DestroyUtility.Destroy(Core.EntityManager, e);
            return true;
        }
        catch (Exception ex)
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz SUMMON] DestroyUtility.Destroy({e}) threw: {ex.Message}");
        }

        // Fallback 1: direct DestroyEntity.
        try
        {
            Core.EntityManager.DestroyEntity(e);
            return true;
        }
        catch (Exception ex)
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz SUMMON] EntityManager.DestroyEntity({e}) threw: {ex.Message}");
        }

        // Fallback 2: add Disabled component as last resort (hides + neutralizes).
        try
        {
            if (!e.Has<Disabled>())
            {
                Core.EntityManager.AddComponent<Disabled>(e);
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz SUMMON] fallback: added <Disabled> to {e} (destroy paths failed)");
            }
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] all destroy paths failed for {e}: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Sweep the world for orphan player-allied units. Uses TWO independent
    /// identification paths to catch both v0.22.x summons (no BlockFeedBuff)
    /// and v0.23.x summons (have BlockFeedBuff):
    ///   Path A — query Follower + walk EntityOwner chain to a player. Catches
    ///            anything that looks like a pet (any version of our code).
    ///   Path B — query BlockFeedBuff + Follower. Catches v0.23.x+ summons.
    /// If <paramref name="targetPlayer"/> is non-null/non-Entity.Null, only
    /// units owned by that specific player are queued.
    /// Returns the count enqueued. Verbose logging shows what was inspected.
    /// </summary>
    public static int SweepOrphans(Entity targetPlayer)
    {
        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;
        var seen = new HashSet<Entity>();
        int count = 0;
        int inspected = 0;

        // Path A: query by Follower alone, broadest sweep.
        EntityQuery queryA;
        try
        {
            queryA = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Follower>(),
                ComponentType.ReadOnly<EntityOwner>());
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] SweepOrphans queryA create failed: {ex.Message}");
            return 0;
        }

        NativeArray<Entity> entitiesA;
        try { entitiesA = queryA.ToEntityArray(Allocator.Temp); }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] SweepOrphans toArray failed: {ex.Message}");
            return 0;
        }

        try
        {
            for (int i = 0; i < entitiesA.Length; i++)
            {
                Entity e = entitiesA[i];
                if (!e.Exists() || e.IsPlayer() || seen.Contains(e)) continue;
                inspected++;

                // EntityOwner.Owner should walk to a player. Skip otherwise.
                if (!e.TryGetComponent<EntityOwner>(out var eo)) continue;
                Entity playerOwner = ResolveOwningPlayer(eo.Owner);
                if (!playerOwner.Exists() || !playerOwner.IsPlayer()) continue;

                // If targeting a specific player, filter.
                if (targetPlayer.Exists() && playerOwner != targetPlayer) continue;

                // Belt-and-suspenders: at least one of these has to be true
                // to plausibly be a Beelzebub summon (not a servant, not some
                // other random follower).
                bool factionPlayers = e.TryGetComponent<FactionReference>(out var fr)
                    && fr.FactionGuid._Value._Value == PlayerFaction._Value;
                bool hasBlockFeed = e.Has<BlockFeedBuff>();
                bool hasMinion = e.Has<Minion>();
                if (!(factionPlayers || hasBlockFeed || hasMinion)) continue;

                string prefabName = e.GetPrefabGuid().GetPrefabName() ?? "?";
                if (verbose)
                {
                    Core.Log.LogInfo($"[Beelz SUMMON] sweep match: {e} prefab={prefabName} owner={playerOwner} (faction={factionPlayers} blockFeed={hasBlockFeed} minion={hasMinion})");
                }

                EnqueueAdminDespawn(e);
                seen.Add(e);
                count++;
            }
        }
        finally { entitiesA.Dispose(); }

        if (verbose)
        {
            Core.Log.LogInfo($"[Beelz SUMMON] sweep complete: inspected={inspected} matched={count} targetPlayer={(targetPlayer.Exists() ? targetPlayer.ToString() : "ALL")}");
        }

        return count;
    }

    /// <summary>
    /// Walk EntityOwner chain up to 4 hops to find a player character.
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

    static int _drainTickCounter = 0;
    /// <summary>
    /// Drain at most <paramref name="budget"/> entities from each active transform's
    /// despawn queue this frame. Called from <c>TransformService.Tick</c>.
    /// Staged so V Rising doesn't choke on a batch destroy.
    /// v0.23.5: added diagnostic logging to confirm Tick is firing.
    /// </summary>
    public static void DrainDespawnQueues(int budget)
    {
        if (budget <= 0) return;

        // v0.23.5 diagnostic: log every 60th call when there's queue work, so we
        // can see whether Tick is actually firing during admin operations.
        _drainTickCounter++;
        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;
        int adminBefore = _adminQueue.Count;
        if (verbose && adminBefore > 0 && (_drainTickCounter % 30 == 0))
        {
            Core.Log.LogInfo($"[Beelz SUMMON] DrainDespawnQueues tick #{_drainTickCounter}: adminQueue={adminBefore} budget={budget}");
        }

        foreach (var (_, active) in Core.AbilityRegistry.AllActiveTransforms()) DrainOne(active, budget);
        foreach (var active in Core.AbilityRegistry.AllPendingDespawns()) DrainOne(active, budget);

        int n = Math.Min(budget, _adminQueue.Count);
        for (int i = 0; i < n; i++)
        {
            Entity e = _adminQueue.Dequeue();
            if (!e.Exists()) continue;
            bool ok = TryDestroyAllPaths(e);
            if (!ok && verbose)
            {
                Core.Log.LogInfo($"[Beelz SUMMON] staged-drain: all destroy paths failed for {e}");
            }
        }
    }

    static void DrainOne(ActiveTransform active, int budget)
    {
        if (active.DespawnQueue == null || active.DespawnQueue.Count == 0) return;
        int n = Math.Min(budget, active.DespawnQueue.Count);
        for (int i = 0; i < n; i++)
        {
            Entity e = active.DespawnQueue.Dequeue();
            if (!e.Exists()) continue;
            try { DestroyUtility.Destroy(Core.EntityManager, e); }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz SUMMON] staged-despawn failed on {e}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// v0.23.19: full horde re-prime when combat starts. Mirrors Bloodcraft's
    /// <c>HandleFamiliarEnteringCombat</c> + <c>SyncAggro</c> pair called on
    /// PvE combat buff application. Setting mode=1 alone (the v0.23.15 fix)
    /// is necessary but not sufficient — for the horde to ENGAGE rather than
    /// "stand near the player while in combat mode," each summon also needs:
    /// 1. Its <c>AggroConsumer.PreCombatPosition</c> set to the current player
    ///    position. This is V Rising's combat-anchor field; the AI uses it
    ///    as a leash centerpoint for combat decisions. A stale value (set
    ///    where the summon spawned, 100 units away) makes the AI think it's
    ///    "too far from combat" and refuse to engage.
    /// 2. <c>SyncAggro</c> — push everything currently aggro'd on the player
    ///    into the summon's <c>AggroBuffer</c>. Without this, summons that
    ///    miss the precise tick when offensive aggro is injected sit with
    ///    empty buffers.
    /// 3. Leash teleport if beyond <c>Transform_SummonLeashRadius</c>. Brings
    ///    far-flung summons back to the player so they can actually reach
    ///    the combat. This is the missing piece for "horde unison" — without
    ///    it, summons spawned at different player positions stay in their
    ///    spatial cluster and only react to threats nearby.
    /// 4. <c>BehaviourTreeState = Combat</c> on the spot to wake the AI from
    ///    Follow/Idle.
    /// 5. Mode = 1 (combat autonomy).
    /// Called from every combat trigger: PvE combat buff application,
    /// player-deals-damage offensive aggro, summon-takes-damage defensive
    /// reaction. Throttled to once per second per player to avoid expensive
    /// re-prime during sustained damage ticks.
    /// </summary>
    public static void HandleHordeEnteringCombat(ulong steamId, Entity playerCharacter)
    {
        if (steamId == 0 || !playerCharacter.Exists()) return;
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;
        if (active.SummonsDisabled) return;
        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

        // Throttle: skip if we re-primed for this player within the last second.
        DateTime now = DateTime.UtcNow;
        if (_lastHordeReprime.TryGetValue(steamId, out var last) && (now - last) < ReprimeThrottle) return;
        _lastHordeReprime[steamId] = now;

        float3 playerPos = float3.zero;
        if (playerCharacter.TryGetComponent<LocalToWorld>(out var pltw)) playerPos = pltw.Position;

        float leashRadius = Beelzebub.Config.Settings.Transform_SummonLeashRadius.Value;
        float leashSq = leashRadius * leashRadius;

        int wokeUp = 0, teleported = 0, aggroSeeded = 0;
        for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
        {
            Entity summon = active.SummonedMinions[i];
            if (!summon.Exists()) { active.SummonedMinions.RemoveAt(i); continue; }

            try
            {
                // Mode = 1 (combat autonomy).
                if (summon.Has<Follower>())
                {
                    summon.With((ref Follower f) => f.ModeModifiable._Value = 1);
                }

                // AggroConsumer pre-combat anchor = current player position.
                // Without this, the AI's combat-range checks use a stale
                // anchor and the unit refuses to chase targets.
                if (summon.Has<AggroConsumer>())
                {
                    summon.With((ref AggroConsumer ac) => ac.PreCombatPosition = playerPos);
                }

                // Leash teleport: pull far units back to the player.
                if (leashRadius > 0f && summon.TryGetComponent<LocalToWorld>(out var sltw))
                {
                    float dx = sltw.Position.x - playerPos.x;
                    float dz = sltw.Position.z - playerPos.z;
                    if (dx * dx + dz * dz > leashSq)
                    {
                        float angle = (float)(i * (Math.PI * 2.0 / Math.Max(1, active.SummonedMinions.Count)));
                        float3 offset = new float3((float)Math.Cos(angle) * 1.8f, 0f, (float)Math.Sin(angle) * 1.8f);
                        float3 spot = playerPos + offset;
                        if (summon.Has<Translation>()) summon.With((ref Translation t) => t.Value = spot);
                        if (summon.Has<LastTranslation>()) summon.With((ref LastTranslation lt) => lt.Value = spot);
                        teleported++;
                    }
                }

                // Re-seed AggroBuffer from player's InverseAggroBuffer
                // (= units currently aggro'd on the player). Bloodcraft's pattern.
                int beforeLen = 0;
                if (Core.EntityManager.HasBuffer<AggroBuffer>(summon))
                    beforeLen = Core.EntityManager.GetBuffer<AggroBuffer>(summon).Length;
                SyncAggro(playerCharacter, summon);
                int afterLen = 0;
                if (Core.EntityManager.HasBuffer<AggroBuffer>(summon))
                    afterLen = Core.EntityManager.GetBuffer<AggroBuffer>(summon).Length;
                if (afterLen > beforeLen) aggroSeeded++;

                // Wake to Combat state immediately.
                if (summon.Has<BehaviourTreeState>())
                {
                    summon.With((ref BehaviourTreeState s) => s.Value = GenericEnemyState.Combat);
                }
                wokeUp++;
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz SUMMON] HandleHordeEnteringCombat mutation failed on {summon}: {ex.Message}");
            }
        }

        if (Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            Core.Log.LogInfo($"[Beelz SUMMON][horde-combat] player {steamId} re-primed {wokeUp} summon(s) (teleported {teleported}, aggro-seeded {aggroSeeded})");
        }
    }

    static readonly Dictionary<ulong, DateTime> _lastHordeReprime = new();
    static readonly TimeSpan ReprimeThrottle = TimeSpan.FromSeconds(1);

    /// <summary>
    /// v0.23.15: toggle every live summon's <c>Follower.ModeModifiable</c> for
    /// the given player. Mode 1 = combat autonomy (units engage targets but
    /// "vortex follow" — clip into player). Mode 0 = leash mode (proper
    /// spacing, passive aggro). Bloodcraft pattern: combat=1, peace=0.
    /// Called from buff hooks when the player enters/exits combat.
    /// </summary>
    public static void SetCombatMode(ulong steamId, bool inCombat)
    {
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;
        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

        int newMode = inCombat ? 1 : 0;
        int updated = 0;
        for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
        {
            Entity summon = active.SummonedMinions[i];
            if (!summon.Exists()) { active.SummonedMinions.RemoveAt(i); continue; }
            if (!summon.Has<Follower>()) continue;
            try
            {
                summon.With((ref Follower f) => f.ModeModifiable._Value = newMode);
                updated++;
            }
            catch (Exception ex)
            {
                Core.Log.LogWarning($"[Beelz SUMMON] SetCombatMode mutation failed on {summon}: {ex.Message}");
            }
        }
        if (updated > 0 && Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            Core.Log.LogInfo($"[Beelz SUMMON] SetCombatMode steamId={steamId} inCombat={inCombat} updated={updated}");
        }
    }

    /// <summary>
    /// v0.23.18 (B2 finalize): a tracked summon took damage. React per
    /// Bloodcraft's pattern (StatChangeSystemPatch.ReactToUnitDamage):
    /// 1. Push attacker into the ENTIRE horde's AggroBuffer (not just the
    ///    struck unit) — this is the "horde" behavior that makes hitting
    ///    one priest skeleton draw the whole 6-unit raise-horde onto the
    ///    attacker. The struck unit was already going to engage via V
    ///    Rising's native one-tick residual; the cohesion comes from
    ///    propagating to its siblings.
    /// 2. Set <c>Follower.ModeModifiable = 1</c> on the struck unit
    ///    (combat autonomy). BuffSpawnServerPatch sets this on every
    ///    summon when the player enters combat, but the player may not
    ///    be in combat when only the summons are being attacked — so
    ///    we set it here defensively for the struck unit.
    /// </summary>
    public static void ReactToDamageOnSummon(Entity struckSummon, Entity attacker, Entity owningPlayer)
    {
        if (!struckSummon.Exists() || !attacker.Exists() || !owningPlayer.Exists()) return;

        ulong steamId = owningPlayer.GetSteamId();
        if (steamId == 0) return;

        // 1. Push attacker into every live summon's AggroBuffer.
        // This is what makes horde cohesion work — one unit gets hit, all
        // engage. Reuses the existing offensive-aggro push helper which
        // also flips BehaviourTreeState → Combat on each summon as a side
        // effect of InjectAggroTarget.
        PushTargetToAllSummons(steamId, attacker);

        // 2. v0.23.19: full horde re-prime (Bloodcraft pattern). Pulls
        // far units to the player, sets combat anchors, mode=1 on all,
        // syncs aggro from player. Throttled to once per second internally,
        // so this is cheap even if called per damage tick. Without it,
        // we observed "per-group response" — only the cluster near the
        // attack engages, far units stay in leash mode and ignore the
        // pushed aggro entry.
        HandleHordeEnteringCombat(steamId, owningPlayer);

        if (Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            string attackerName = attacker.GetPrefabGuid().GetPrefabName() ?? "?";
            string summonName = struckSummon.GetPrefabGuid().GetPrefabName() ?? "?";
            Core.Log.LogInfo($"[Beelz SUMMON] react-to-damage: {summonName} struck by {attackerName} (root attacker resolved) → horde aggro broadcast for player {steamId}");
        }
    }

    /// <summary>
    /// v0.23.1: push one specific target into every live summon's AggroBuffer for
    /// the given player. Called from <see cref="Patches.DealDamageSystemPatch"/>
    /// when the player damages someone — that's the offensive trigger that v0.23.0
    /// was missing (we only had defensive SyncAggro reading the player's
    /// InverseAggroBuffer). Also flips BehaviourTreeState to Combat to wake the
    /// minion's AI out of Idle.
    /// v0.23.18: also called from <see cref="ReactToDamageOnSummon"/> when a
    /// summon takes damage — to broadcast the attacker to the rest of the horde.
    /// </summary>
    public static void PushTargetToAllSummons(ulong steamId, Entity target)
    {
        if (!target.Exists()) return;
        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;
        if (active.SummonsDisabled) return;
        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

        for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
        {
            Entity summon = active.SummonedMinions[i];
            if (!summon.Exists())
            {
                active.SummonedMinions.RemoveAt(i);
                continue;
            }
            InjectAggroTarget(summon, target);
        }
    }

    /// <summary>
    /// Insert a single high-priority target into the summon's AggroBuffer and
    /// nudge its BehaviourTreeState toward Combat so the AI actually engages.
    /// Skips if the target is already in the buffer (no duplicates).
    /// </summary>
    static void InjectAggroTarget(Entity summon, Entity target)
    {
        try
        {
            if (!Core.EntityManager.HasBuffer<AggroBuffer>(summon)) return;
            var buf = Core.EntityManager.GetBuffer<AggroBuffer>(summon);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].Entity == target) return; // already in
            }
            buf.Add(new AggroBuffer { DamageValue = 500f, Entity = target, Weight = 1f });

            // v0.23.1: force the unit out of Idle. Bloodcraft's BehaviourTreeState
            // pattern — flip to Combat so the AI evaluates the aggro buffer instead
            // of sitting in follow-idle.
            if (summon.Has<BehaviourTreeState>())
            {
                summon.With((ref BehaviourTreeState s) =>
                    s.Value = GenericEnemyState.Combat);
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] InjectAggroTarget on {summon} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// v0.23.1: leash check — for every live summon, if it's too far from its
    /// player OR sitting in Idle/Return state, teleport it back to the player.
    /// Mirrors Bloodcraft's BehaviourStateChangedSystem + TryReturnFamiliar
    /// patterns. Called from <see cref="TransformService.Tick"/> on a throttle.
    /// </summary>
    public static void LeashCheckAll(float leashRadius)
    {
        float leashSq = leashRadius * leashRadius;
        foreach (var (steamId, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            if (active.SummonsDisabled) continue;
            if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) continue;

            Entity player = EntityExtensions.FindCharacterBySteamId(steamId);
            if (!player.Exists()) continue;
            if (!player.TryGetComponent<LocalToWorld>(out var pltw)) continue;
            float3 pPos = pltw.Position;

            for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
            {
                Entity summon = active.SummonedMinions[i];
                if (!summon.Exists()) { active.SummonedMinions.RemoveAt(i); continue; }

                bool needsTeleport = false;
                if (summon.TryGetComponent<LocalToWorld>(out var sltw))
                {
                    float dx = sltw.Position.x - pPos.x;
                    float dz = sltw.Position.z - pPos.z;
                    if (dx * dx + dz * dz > leashSq) needsTeleport = true;
                }
                if (!needsTeleport && summon.Has<BehaviourTreeState>())
                {
                    if (summon.TryGetComponent<BehaviourTreeState>(out var bts))
                    {
                        if (bts.Value == GenericEnemyState.Return)
                        {
                            // Bloodcraft pattern: Return state = unit gave up on combat,
                            // heading back to a leash anchor. Force-flip to Follow.
                            summon.With((ref BehaviourTreeState s) =>
                                s.Value = GenericEnemyState.Follow);
                        }
                    }
                }

                if (needsTeleport)
                {
                    float angle = (float)(i * (Math.PI * 2.0 / Math.Max(1, active.SummonedMinions.Count)));
                    float3 offset = new float3((float)Math.Cos(angle) * 1.8f, 0f, (float)Math.Sin(angle) * 1.8f);
                    float3 spot = pPos + offset;
                    try
                    {
                        if (summon.Has<Translation>()) summon.With((ref Translation t) => t.Value = spot);
                        if (summon.Has<LastTranslation>()) summon.With((ref LastTranslation lt) => lt.Value = spot);
                        if (summon.Has<AggroConsumer>()) summon.With((ref AggroConsumer ac) => ac.PreCombatPosition = spot);
                    }
                    catch (Exception ex)
                    {
                        Core.Log.LogWarning($"[Beelz SUMMON] leash teleport failed for {summon}: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// v0.23.6: count of LIVE CAST GROUPS for this (transform, ability).
    /// v0.23.10: reverted the Health.Value check — Value is briefly 0 on
    /// freshly-spawned units before MaxHealth init, causing ALL groups to be
    /// marked dead → cap returned 0 → infinite summons. Instead we use
    /// <c>.Exists()</c> and rely on <see cref="PruneDeadSummon"/> being called
    /// from <c>DeathEventListenerSystemPatch</c> to drop entities the moment
    /// V Rising fires their death event — better timing than waiting for
    /// EntityManager destruction.
    /// v0.23.16 (B1 fix): an EMPTY group is no longer pruned immediately —
    /// it's kept alive until the attribution window has expired. Reason:
    /// channeled summons (Priest's RaiseHorde/RaiseDead) take longer than the
    /// attribution window's old 3s value to produce spawns, so the group was
    /// being pruned BEFORE attribution arrived. Pruning empty groups within
    /// the window meant the cap counted 0 active uses and never refused. With
    /// the empty-group TTL: a freshly-opened group stays alive (and counts
    /// against the cap) until either attribution succeeds (entities arrive
    /// and populate it) or the attribution window expires (entities never
    /// arrived; confirmed orphan; safe to prune).
    /// v0.23.17 (B1 fix-fix): two call-sites need DIFFERENT semantics. Cast-
    /// start uses <see cref="LiveCastCount"/> (includes empty-within-window
    /// to refuse rapid re-casts before any spawns land). LinkMinion uses
    /// <see cref="LivePopulatedCastCount"/> (excludes empty groups so the
    /// incoming spawn — which is about to populate the freshly-opened empty
    /// group — is not over-counted as "another use" and incorrectly destroyed).
    /// Without this split, an admitted 3rd cast saw its own group counted
    /// toward the cap at LinkMinion → over-cap → destroyed its own spawns →
    /// the 3rd cast silently produced nothing despite passing the cast-start
    /// cap check.
    /// </summary>
    public static int LiveCastCount(ActiveTransform active, int abilityGuid)
        => CountAliveGroups(active, abilityGuid, includeEmptyWithinWindow: true);

    /// <summary>
    /// v0.23.17 (B1 fix-fix): count only groups that have at least one alive
    /// entity. Used by LinkMinion's over-cap destroy check — empty groups
    /// are pending destinations for the current spawn batch, not separate
    /// "uses" that the spawn would push past the cap. See <see cref="LiveCastCount"/>
    /// for the full rationale.
    /// </summary>
    public static int LivePopulatedCastCount(ActiveTransform active, int abilityGuid)
        => CountAliveGroups(active, abilityGuid, includeEmptyWithinWindow: false);

    static int CountAliveGroups(ActiveTransform active, int abilityGuid, bool includeEmptyWithinWindow)
    {
        if (active.SummonStacks == null) return 0;
        if (!active.SummonStacks.TryGetValue(abilityGuid, out var groups)) return 0;
        active.SummonStackTimes.TryGetValue(abilityGuid, out var times);
        DateTime now = DateTime.UtcNow;

        int aliveCount = 0;
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            var grp = groups[i];
            bool anyAlive = false;
            for (int j = grp.Count - 1; j >= 0; j--)
            {
                if (grp[j].Exists()) { anyAlive = true; break; }
            }
            if (anyAlive)
            {
                aliveCount++;
                continue;
            }

            // Empty/dead. Decide based on window + caller's preference.
            bool isEmptyWithinWindow = grp.Count == 0
                && times != null
                && i < times.Count
                && (now - times[i]) < Patches.AbilityCastStartedSystemPatch.AttributionWindow;

            if (isEmptyWithinWindow)
            {
                // Group is still "pending" — don't prune. Count it only if
                // the caller wants pending groups (= cast-start cap check).
                if (includeEmptyWithinWindow) aliveCount++;
                continue;
            }

            // Stale or fully-dead. Prune.
            groups.RemoveAt(i);
            if (times != null && i < times.Count) times.RemoveAt(i);
        }
        return aliveCount;
    }

    /// <summary>
    /// v0.29.0 (#4): maintain an on-player buff whose stack count = the player's
    /// live summon "uses" (toward the cap), so they can see cap status at a glance
    /// without typing <c>.beelz summons</c>. The buff prefab is admin-chosen
    /// (Transform_SummonCounterBuffGuid) for its icon; we strip its gameplay
    /// effects and force it permanent + stacking. 0 = feature off.
    /// </summary>
    public static void UpdateSummonCounter(ActiveTransform active, Entity character)
    {
        int guidInt = Beelzebub.Config.Settings.Transform_SummonCounterBuffGuid.Value;
        if (guidInt == 0 || active == null || !character.Exists()) return;

        var buffGuid = new PrefabGUID(guidInt);

        int count = 0;
        if (active.SummonStacks != null)
            foreach (var abilityGuid in active.SummonStacks.Keys)
                count += LiveCastCount(active, abilityGuid);

        bool hasBuff = Core.ServerGameManager.TryGetBuff(character, buffGuid.ToIdentifier(), out Entity buffEntity)
            && buffEntity.Exists();

        if (count <= 0)
        {
            if (hasBuff)
                try { DestroyUtility.Destroy(Core.EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff); }
                catch { /* best-effort */ }
            return;
        }

        if (!hasBuff)
        {
            if (!Core.ServerGameManager.TryInstantiateBuffEntityImmediate(character, character, buffGuid, out buffEntity)
                || !buffEntity.Exists())
                return;
            NeuterCounterBuff(buffEntity);
        }

        int cap = Beelzebub.Config.Settings.Transform_MaxStacksPerSummonAbility.Value;
        int maxStacks = System.Math.Max(count, cap > 0 ? cap : count);
        try
        {
            buffEntity.With((ref Buff b) =>
            {
                b.IncreaseStacks = true;
                b.MaxStacks = (byte)System.Math.Min(maxStacks, 250);
                b.Stacks = (byte)System.Math.Min(count, 250);
            });
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] counter stack-set failed: {ex.Message}"); }
    }

    /// <summary>v0.29.0: strip a buff's gameplay effects so only its icon + stacks
    /// remain, and make it permanent (Bloodcraft's persistent-buff recipe).</summary>
    static void NeuterCounterBuff(Entity buffEntity)
    {
        try
        {
            if (buffEntity.Has<RemoveBuffOnGameplayEvent>()) Core.EntityManager.RemoveComponent<RemoveBuffOnGameplayEvent>(buffEntity);
            if (buffEntity.Has<CreateGameplayEventsOnSpawn>()) Core.EntityManager.RemoveComponent<CreateGameplayEventsOnSpawn>(buffEntity);
            if (buffEntity.Has<GameplayEventListeners>()) Core.EntityManager.RemoveComponent<GameplayEventListeners>(buffEntity);
            if (buffEntity.Has<DestroyOnGameplayEvent>()) Core.EntityManager.RemoveComponent<DestroyOnGameplayEvent>(buffEntity);
            if (buffEntity.Has<LifeTime>())
                buffEntity.With((ref LifeTime lt) => { lt.Duration = 0f; lt.EndAction = LifeTimeEndAction.None; });
            if (buffEntity.Has<BuffCategory>())
                buffEntity.With((ref BuffCategory bc) => bc.Groups = BuffCategoryFlag.None);
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz SUMMON] NeuterCounterBuff failed: {ex.Message}"); }
    }

    /// <summary>v0.29.0: remove the summon-count indicator buff (on revert).</summary>
    public static void RemoveSummonCounter(Entity character)
    {
        int guidInt = Beelzebub.Config.Settings.Transform_SummonCounterBuffGuid.Value;
        if (guidInt == 0 || !character.Exists()) return;
        var buffGuid = new PrefabGUID(guidInt);
        if (Core.ServerGameManager.TryGetBuff(character, buffGuid.ToIdentifier(), out Entity buffEntity) && buffEntity.Exists())
            try { DestroyUtility.Destroy(Core.EntityManager, buffEntity, DestroyDebugReason.TryRemoveBuff); }
            catch { /* best-effort */ }
    }

    /// <summary>
    /// v0.26.0: auto-despawn summon cast-groups older than <paramref name="lifetimeSeconds"/>.
    /// Each group's creation time lives in <c>SummonStackTimes[abilityGuid][i]</c>
    /// (index-aligned with <c>SummonStacks</c>). Expired groups have their entities
    /// enqueued for the crash-safe staged despawn (<see cref="EnqueueAdminDespawn"/>)
    /// and are removed from the tracking dictionaries + <c>SummonedMinions</c>.
    /// Returns the number of entities queued. No-op when lifetime &lt;= 0.
    /// </summary>
    public static int DespawnExpiredGroups(ActiveTransform active, float lifetimeSeconds)
    {
        if (lifetimeSeconds <= 0f) return 0;
        if (active?.SummonStacks == null || active.SummonStacks.Count == 0) return 0;

        DateTime now = DateTime.UtcNow;
        var maxAge = TimeSpan.FromSeconds(lifetimeSeconds);
        int queued = 0;

        // Snapshot the ability keys — we mutate the inner group lists below.
        foreach (var abilityGuid in new List<int>(active.SummonStacks.Keys))
        {
            var groups = active.SummonStacks[abilityGuid];
            active.SummonStackTimes.TryGetValue(abilityGuid, out var times);

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                // Need a timestamp to judge age; without one we can't tell, so skip.
                if (times == null || i >= times.Count) continue;
                if (now - times[i] < maxAge) continue;

                var grp = groups[i];
                for (int j = 0; j < grp.Count; j++)
                {
                    var e = grp[j];
                    if (e.Exists()) { EnqueueAdminDespawn(e); queued++; }
                    active.SummonedMinions?.Remove(e);
                }
                groups.RemoveAt(i);
                times.RemoveAt(i);
            }
        }

        if (queued > 0)
            Core.Log.LogInfo($"[Beelz SUMMON] lifespan: queued {queued} summon(s) for staged despawn (lifetime {lifetimeSeconds:0}s).");
        return queued;
    }

    /// <summary>
    /// v0.23.10: invoked from <c>DeathEventListenerSystemPatch</c> when V Rising
    /// fires a death event. Scans all active transforms and stashed-summon lists,
    /// removes the dying entity from <c>SummonedMinions</c> and all per-ability
    /// <c>SummonStacks</c> groups. Decrements the cap immediately (next cast can
    /// fire as soon as a group is fully cleared) rather than waiting for V
    /// Rising's destroy phase.
    /// </summary>
    public static void PruneDeadSummon(Entity diedEntity)
    {
        foreach (var (_, active) in Core.AbilityRegistry.AllActiveTransforms())
        {
            PruneFromActive(active, diedEntity);
        }
        foreach (var active in Core.AbilityRegistry.AllPendingDespawns())
        {
            PruneFromActive(active, diedEntity);
        }
    }

    static void PruneFromActive(ActiveTransform active, Entity diedEntity)
    {
        if (active.SummonedMinions != null)
        {
            for (int i = active.SummonedMinions.Count - 1; i >= 0; i--)
            {
                if (active.SummonedMinions[i] == diedEntity) active.SummonedMinions.RemoveAt(i);
            }
        }
        if (active.StashedSummons != null)
        {
            for (int i = active.StashedSummons.Count - 1; i >= 0; i--)
            {
                if (active.StashedSummons[i] == diedEntity) active.StashedSummons.RemoveAt(i);
            }
        }
        if (active.SummonStacks != null)
        {
            foreach (var groups in active.SummonStacks.Values)
            {
                foreach (var grp in groups)
                {
                    for (int i = grp.Count - 1; i >= 0; i--)
                    {
                        if (grp[i] == diedEntity) grp.RemoveAt(i);
                    }
                }
            }
        }
    }

    /// <summary>
    /// v0.23.6: start a new cast group. Called by AbilityCastStartedSystemPatch
    /// AFTER the cap check passes. Subsequent <see cref="TrackInCurrentGroup"/>
    /// calls (from both Hook 1 manual spawn and Hook 2 natural-chain rebind)
    /// append to this group.
    /// v0.23.16 (B1 fix): also records the creation time in SummonStackTimes
    /// so <see cref="LiveCastCount"/> can keep empty groups alive while
    /// attribution is still in flight.
    /// </summary>
    public static void BeginCastGroup(ActiveTransform active, int abilityGuid)
    {
        if (active.SummonStacks == null)
            active.SummonStacks = new Dictionary<int, List<List<Entity>>>();
        if (!active.SummonStacks.TryGetValue(abilityGuid, out var groups))
        {
            groups = new List<List<Entity>>();
            active.SummonStacks[abilityGuid] = groups;
        }
        groups.Add(new List<Entity>());

        if (active.SummonStackTimes == null)
            active.SummonStackTimes = new Dictionary<int, List<DateTime>>();
        if (!active.SummonStackTimes.TryGetValue(abilityGuid, out var times))
        {
            times = new List<DateTime>();
            active.SummonStackTimes[abilityGuid] = times;
        }
        times.Add(DateTime.UtcNow);
    }

    /// <summary>
    /// v0.23.6: append a spawned entity to the most recent (current) cast group
    /// for this ability. No-op if no group is open (e.g. orphan natural-chain
    /// spawn that arrived outside any attribution window).
    /// </summary>
    public static void TrackInCurrentGroup(ActiveTransform active, int abilityGuid, Entity minion)
    {
        bool verbose = Beelzebub.Config.Settings.VerboseLogging.Value;
        if (active.SummonStacks == null)
        {
            if (verbose) Core.Log.LogInfo($"[Beelz SUMMON][track] {minion}: no SummonStacks dictionary; skipped.");
            return;
        }
        if (!active.SummonStacks.TryGetValue(abilityGuid, out var groups) || groups.Count == 0)
        {
            if (verbose)
                Core.Log.LogInfo($"[Beelz SUMMON][track] {minion} for ability={new PrefabGUID(abilityGuid).GetPrefabName()}: no open group (BeginCastGroup may not have fired); skipped.");
            return;
        }
        groups[groups.Count - 1].Add(minion);
        if (verbose)
            Core.Log.LogInfo($"[Beelz SUMMON][track] {minion} added to current group of ability={new PrefabGUID(abilityGuid).GetPrefabName()} (group now has {groups[groups.Count - 1].Count} entities, {groups.Count} group(s) total)");
    }
}
