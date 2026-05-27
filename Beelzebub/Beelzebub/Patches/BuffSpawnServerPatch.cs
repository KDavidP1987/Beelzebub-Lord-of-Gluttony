using System;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Beelzebub.Patches;

/// <summary>
/// v0.23.11 — auto-stash summons when a player initiates a waypoint teleport.
///
/// V Rising's waygate has a pre-check that refuses teleport while the player
/// has "subdued enemies" (their flagged followers). The Bloodcraft-style stash
/// solves this — clear <c>Follower.Followed</c>, add <c>&lt;Disabled&gt;</c>,
/// remove from <c>FollowerBuffer</c>. Manual `.beelz summons stash` works but
/// requires the player to type a command before each teleport.
///
/// This patch hooks <c>BuffSystem_Spawn_Server.OnUpdate</c> Prefix and watches
/// for <c>Buff_Waypoint_Travel</c> (PrefabGuid 150521246) being applied to a
/// player — the V Rising channel buff that fires when a waypoint teleport
/// begins. We auto-stash at that moment, so V Rising's own subdued check (run
/// as part of the channel) sees no followers and lets the teleport proceed.
///
/// Auto-restore happens in <see cref="PlayerTeleportSystemPatch"/> after the
/// teleport completes.
/// </summary>
[HarmonyPatch(typeof(BuffSystem_Spawn_Server), nameof(BuffSystem_Spawn_Server.OnUpdate))]
internal static class BuffSpawnServerPatch
{
    static readonly PrefabGUID WaypointTravelBuff = new(150521246);
    // v0.23.13: Buff_Waypoint_TravelEnd spawns on the player when Buff_Waypoint_Travel
    // ends (teleport complete). Per the Buff_Waypoint_Travel prefab data:
    //   ProjectM.SpawnPrefabOnDestroy SpawnPrefab: Buff_Waypoint_TravelEnd (-1361133205)
    // This is our arrival signal — restore stashed summons here.
    static readonly PrefabGUID WaypointTravelEndBuff = new(-1361133205);
    // v0.23.15: PvE combat buff — when this applies to a player, they're in combat.
    // Per Bloodcraft's UpdateBuffsBufferDestroyPatch GetBuffType: GuidHash 581443919.
    static readonly PrefabGUID PvECombatBuff = new(581443919);

    // v0.26.0: bat-form landing signal. ShapeshiftSystemPatch stashes summons on
    // bat-form ENTER; this buff (AB_Shapeshift_Bat_Landing_Travel) marks the
    // player touching down → restore. NOTE: Bloodcraft's older code caught this
    // via Spawn_TravelBuffSystem, so if testing shows no restore here, the buff
    // routes through that system instead and we move detection there. Manual
    // `.beelz summons restore` is the fallback meanwhile.
    static readonly PrefabGUID BatLandingTravelBuff = new(-371745443);

    // v0.42.0: the "rider" buff applied to a PLAYER while mounted on a horse
    // (AB_Interact_Mount_Owner_Buff_*). It carries BuffCategoryFlag.Mount and a
    // MountBuff component, LifeTime 999999 (lives until dismount), and overrides the
    // player's spell bar with the mounted abilities. We watch its SPAWN (mount) here
    // and its DESTROY (dismount) in UpdateBuffsBufferDestroyPatch — the same enter/exit
    // pattern as bat form. The GUID set covers the known horse variants; the
    // BuffCategoryFlag.Mount fallback in IsMountBuff catches any other skins.
    static readonly System.Collections.Generic.HashSet<int> MountOwnerBuffs = new()
    {
        -978792376,   // AB_Interact_Mount_Owner_Buff_Horse_Vampire
        -2000859158,  // ..._Horse_Vampire_Blackfang
        -1936451181,  // ..._Horse_Vampire_Gloomrot
        -1627838818,  // ..._Horse_Vampire_PMKSkeleton
        854656674,    // AB_Interact_Mount_Owner_Buff_Horse
        2112789321,   // AB_Interact_Mount_Owner_Buff (generic)
    };

    /// <summary>
    /// v0.42.0: is this buff entity the rider-side "mounted on a horse" buff? Matches a
    /// known mount-owner-buff GUID, or any buff carrying <c>BuffCategoryFlag.Mount</c>
    /// (catches horse skins not in the GUID list). Shared with the dismount detector.
    /// </summary>
    internal static bool IsMountBuff(Entity buffEntity, int prefabGuid)
    {
        if (MountOwnerBuffs.Contains(prefabGuid)) return true;
        if (buffEntity.TryGetComponent<BuffCategory>(out var cat)
            && (cat.Groups & BuffCategoryFlag.Mount) != BuffCategoryFlag.None)
            return true;
        return false;
    }

    // v0.24.8: "teleport-and-detonate" map. When one of these arrival buffs is
    // applied to a transformed player, V Rising's NATIVE chain refuses to spawn
    // the detonation because the SpawnPrefab gameplay event is gated by a
    // ConditionBlob the NPC satisfies but a player does not. (Proof from live
    // testing: the Priest's Mist Walk arrival buff DOES fire its un-gated
    // effects — Fear applies at the destination — but the gated AoE never
    // spawns.) We bypass the gate by instantiating the detonation prefab
    // ourselves, owned by the player. The detonation prefab is fully
    // self-contained: DestroyOnSpawn → CreateGameplayEventsOnDestroy →
    // RunScript fan, plus GetOwnerTeamOnSpawn + GetTranslationOnSpawn(Owner),
    // so spawning it owned by the player auto-positions it at the player and
    // stamps the projectiles with the player's team. No manual team fixup
    // needed.
    //   key   = arrival buff GUID, value = detonation prefab GUID
    static readonly Dictionary<int, int> TeleportDetonateTargets = new()
    {
        // Undead Priest (Elite) Mist Walk: Teleport_Travel_End → ProjectileNova_ProxySpawner
        { 493463494, 1249580491 },
    };

    // #3 (v0.36.0) MANUAL detonation: transformed UNIT (CHAR_ guid) → its signature
    // detonation prefab, fired on demand via `.beelz detonate`. Bosses only auto-fire
    // these on teleport; this lets the player trigger the same self-contained AoE
    // whenever they like. Same spawn path as TeleportDetonateTargets (the prefab is
    // self-contained: spawn owned-by-player → auto-positions + player-teams + fans).
    // Add more units here as their detonation AoEs are confirmed.
    static readonly Dictionary<int, int> ManualDetonateTargets = new()
    {
        { 153390636, 1249580491 },    // CHAR_Undead_Priest_VBlood (Foulrot) → ProjectileNova_ProxySpawner
        { -1653554504, 1249580491 },  // CHAR_Undead_Priest → ProjectileNova_ProxySpawner
    };
    // EXPANSION CANDIDATES (unverified — need the prefab component dump or a [Beelz TRACE]
    // runtime check to confirm the payload is self-contained like the Priest's ProxySpawner,
    // i.e. positions on its owner + stamps the owner's team + fans/explodes on spawn).
    // The Priest is the ONLY confirmed self-contained "_ProxySpawner" in the prefab name table.
    // Other boss novas use different structures (_Area / _Throw / _RingArea / ChannelBuff) and may
    // NOT self-contain when spawned owned-by-player — verify before adding to avoid no-op detonates:
    //   Dracula  CrimsonNova   (AB_Dracula_Final_CrimsonNova_Area -346299479)        unit CHAR_Vampire_Dracula_VBlood -327335305
    //   Cardinal LightNova     (AB_Cardinal_LightNova_Area 1826627256)               unit CHAR_Militia_Cardinal_VBlood (verify guid)
    //   Professor ImplodingOrb (AB_Gloomrot_TheProfessor_ImplodingOrb_Area 1592022037) unit CHAR_Gloomrot_TheProfessor_VBlood (verify guid)
    //   IceNova family (IceRanger / Militia_Guard_VBlood) — _RingArea payloads.

    /// <summary>#3: does this transformed unit have a manual-detonation AoE registered?</summary>
    public static bool HasManualDetonation(int unitGuid) => ManualDetonateTargets.ContainsKey(unitGuid);

    /// <summary>
    /// #3: fire a transformed unit's signature detonation AoE on demand. Spawns the
    /// self-contained detonation prefab owned by the player (auto-positions + stamps
    /// the player's team + fans the projectiles). Returns the detonation prefab name,
    /// or null if the unit has none / the spawn failed.
    /// </summary>
    public static string TrySpawnManualDetonation(Entity playerCharacter, int unitGuid)
    {
        if (!ManualDetonateTargets.TryGetValue(unitGuid, out int detonateGuid)) return null;
        Entity e = SpawnDetonationOwnedByPlayer(playerCharacter, detonateGuid, "manual");
        return e.Exists() ? new PrefabGUID(detonateGuid).GetPrefabName() : null;
    }

    [HarmonyPrefix]
    public static void OnUpdatePrefix(BuffSystem_Spawn_Server __instance)
    {
        if (!Core.IsReady) return;

        // v0.43.1 CRASH FIX / v0.43.2 revert-orphan guard: when a form buff appears in the
        // spawn query, either ENRICH it (pending async form like Morgana's SnakePhase) or
        // DESTROY it as an orphan (it spawned just after a revert, from a queued async apply,
        // and would otherwise strand the player in an untracked form). Runs BEFORE the
        // summons-ally gate because the form-apply path depends on it regardless of config.
        // Cheap no-op unless a form is pending or a revert just happened.
        if (TransformBuffService.HasPendingForms || TransformBuffService.HasRecentReverts)
        {
            try
            {
                var formEnts = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
                try
                {
                    for (int i = 0; i < formEnts.Length; i++)
                    {
                        Entity fe = formEnts[i];
                        if (!fe.Exists()) continue;
                        if (!fe.TryGetComponent<Buff>(out var fb)) continue;
                        if (!fb.Target.Exists() || !fb.Target.IsPlayer()) continue;
                        if (TransformBuffService.TryEnrichSpawnedForm(fe, fb.Target)) continue;
                        TransformBuffService.TryDestroyOrphanForm(fe, fb.Target);
                    }
                }
                finally { formEnts.Dispose(); }
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz] form-enrich/orphan scan failed: {ex.Message}"); }
        }

        if (!Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value) return;

        NativeArray<Entity> entities;
        try
        {
            entities = __instance.EntityQueries[0].ToEntityArray(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz SUMMON] BuffSpawnServer query failed: {ex.Message}");
            return;
        }

        try
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity e = entities[i];
                // v0.23.14: read components per-entity (TryGetComponent). Earlier
                // code used ToComponentDataArray<PrefabGUID> which may not include
                // PrefabGUID in the archetypes — that returned empty arrays and
                // no detections fired. Per-entity is safer.
                if (!e.TryGetComponent<PrefabGUID>(out var prefab)) continue;
                if (!e.TryGetComponent<Buff>(out var buff)) continue;

                // v0.24.4: GENERIC chain-ownership fixup runs FIRST, before the
                // waygate/combat-specific switch below. For every buff entity
                // being spawned, walk the EntityOwner chain — if the buff was
                // ultimately caused by a Beelzebub-transformed player, rewrite
                // its Team / TeamReference / FactionReference so downstream
                // effects (AOE freezes, damage carriers, etc.) read as
                // player-allied. Complements ScriptSpawnServerPatch which
                // catches the same buff at a different point in V Rising's
                // spawn pipeline — both hooks together give us better coverage
                // of chain entities at varying spawn depths.
                try
                {
                    if (e.TryGetComponent<EntityOwner>(out var eo))
                    {
                        Entity playerOwner = ResolveOwningPlayer(eo.Owner);
                        if (playerOwner.Exists()
                            && playerOwner.IsPlayer()
                            && Core.AbilityRegistry is not null
                            && Core.AbilityRegistry.GetActiveTransform(playerOwner.GetSteamId()) is not null)
                        {
                            // v0.25.0: trace EVERY owned buff (incl. trigger /
                            // channel buffs) so the runtime audit sees the chain.
                            Services.ChainTraceService.Observe(e, playerOwner.GetSteamId(), "buff");

                            if (!e.Has<BlockFeedBuff>() && !e.Has<Minion>())
                                FixupBuffOwnership(e, playerOwner);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Core.Log.LogWarning($"[Beelz CHAIN][buff-spawn] fixup failed on {e}: {ex.Message}");
                }

                Entity target = buff.Target;
                if (!target.Exists() || !target.IsPlayer()) continue;

                if (prefab._Value == WaypointTravelBuff._Value)
                {
                    try { HandleWaypointTravelStart(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] HandleWaypointTravelStart failed: {ex}");
                    }
                }
                else if (prefab._Value == WaypointTravelEndBuff._Value)
                {
                    try { HandleWaypointTravelEnd(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] HandleWaypointTravelEnd failed: {ex}");
                    }
                }
                else if (prefab._Value == PvECombatBuff._Value)
                {
                    // v0.23.15: player entered combat → flip summons to mode=1
                    // (combat autonomy). They'll engage targets aggressively.
                    // v0.23.19: also call HandleHordeEnteringCombat — Bloodcraft's
                    // pattern. Without the full re-prime (AggroConsumer anchor +
                    // SyncAggro + leash teleport + BTS=Combat), mode=1 alone is
                    // not enough to make units engage. See HandleHordeEnteringCombat
                    // for full rationale.
                    ulong steamId = target.GetSteamId();
                    if (steamId != 0)
                    {
                        if (Beelzebub.Config.Settings.VerboseLogging.Value)
                            Core.Log.LogInfo($"[Beelz SUMMON][combat-on] PvE combat buff applied to player {steamId} → HandleHordeEnteringCombat");
                        SummonAllyService.SetCombatMode(steamId, true);
                        SummonAllyService.HandleHordeEnteringCombat(steamId, target);

                        // v0.27.0: mark combat + capture the player entity so the
                        // Auto-mode phase monitor (TransformService.Tick) can read
                        // Health without a steamId→entity lookup.
                        var activeT = Core.AbilityRegistry.GetActiveTransform(steamId);
                        if (activeT != null)
                        {
                            activeT.InCombat = true;
                            activeT.Character = target;
                        }
                    }
                }
                else if (TeleportDetonateTargets.TryGetValue(prefab._Value, out int detonateGuid))
                {
                    // v0.24.8: arrival buff of a teleport-and-detonate ability
                    // applied to a (transformed) player → spawn the detonation
                    // ourselves, bypassing the native ConditionBlob gate.
                    try { HandleTeleportDetonate(target, prefab._Value, detonateGuid); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz NOVA] HandleTeleportDetonate failed: {ex}");
                    }
                }
                else if (prefab._Value == BatLandingTravelBuff._Value)
                {
                    // v0.26.0: player landed from bat form → restore stashed summons.
                    if (Beelzebub.Config.Settings.VerboseLogging.Value)
                        Core.Log.LogInfo($"[Beelz SUMMON] bat-landing buff seen on player {target.GetSteamId()} → restore");
                    try { HandleWaypointTravelEnd(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] bat-land restore failed: {ex}");
                    }
                }
                else if (IsMountBuff(e, prefab._Value))
                {
                    // v0.42.0: player mounted a horse. In Stash mode, stash their summons
                    // (dismount restore lives in UpdateBuffsBufferDestroyPatch). Follow mode
                    // is a no-op — the summons keep following via the existing leash plumbing.
                    try { HandleMount(target); }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[Beelz SUMMON] HandleMount failed: {ex}");
                    }
                }
            }
        }
        finally
        {
            entities.Dispose();
        }

        // v0.24.7: belt-and-suspenders — also run the broader non-buff sweep
        // from this hook since it fires reliably (confirmed in user logs).
        // ScriptSpawnServerPatch runs the same sweep, but our test showed it
        // may not catch ProxySpawner-class entities every time. Running from
        // both hooks gives double coverage at minor performance cost.
        try { Patches.ScriptSpawnServerPatch.RunSweepIfPossible(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN][buff-spawn] sweep relay failed: {ex.Message}"); }

        // v0.24.8: relay the Nova-chain watch from here too — this hook fires
        // reliably per-frame during combat, unlike ScriptSpawnServer.OnUpdate
        // (which fired ~once per session). Lets us confirm whether the manually
        // spawned detonation produces player-team projectiles.
        try { Patches.ScriptSpawnServerPatch.RunWatchIfPossible(); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz CHAIN][buff-spawn] watch relay failed: {ex.Message}"); }
    }

    /// <summary>
    /// v0.24.8: spawn a teleport-and-detonate ability's detonation prefab
    /// ourselves, owned by the player, because V Rising's native chain gates
    /// the spawn behind a ConditionBlob that a transformed player fails.
    ///
    /// The detonation prefab (e.g. ProjectileNova_ProxySpawner) is fully
    /// self-contained — GetTranslationOnSpawn(Owner) positions it at the
    /// player, GetOwnerTeamOnSpawn stamps it (and its fanned projectiles) with
    /// the player's team, and DestroyOnSpawn → RunScript fans the projectiles.
    /// So all we have to do is instantiate it with the player as owner; V
    /// Rising does the rest. This mirrors the manual-spawn pattern in
    /// <see cref="AbilityCastStartedSystemPatch"/> for Priest RaiseDead.
    /// </summary>
    static void HandleTeleportDetonate(Entity playerCharacter, int arrivalBuffGuid, int detonateGuid)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        // Only transformed players get boss kits — guard so a vanilla player
        // who somehow carries this buff doesn't trigger a spawn.
        if (Core.AbilityRegistry?.GetActiveTransform(steamId) is null) return;

        SpawnDetonationOwnedByPlayer(playerCharacter, detonateGuid,
            $"arrival:{new PrefabGUID(arrivalBuffGuid).GetPrefabName()}");
    }

    /// <summary>
    /// Spawn a self-contained detonation prefab owned by the player. The prefab's
    /// own GetTranslationOnSpawn(Owner)/GetOwnerTeamOnSpawn processors position it at
    /// the player and stamp the player's team, then its DestroyOnSpawn chain fans the
    /// projectiles — so owning it by the player is all it takes. We also anchor
    /// owner+position explicitly as belt-and-suspenders. Shared by the teleport
    /// auto-detonate and the `.beelz detonate` manual trigger. Returns the spawned
    /// entity (or Entity.Null on failure).
    /// </summary>
    static Entity SpawnDetonationOwnedByPlayer(Entity playerCharacter, int detonateGuid, string sourceLabel)
    {
        var detonatePrefab = new PrefabGUID(detonateGuid);
        Entity proxy;
        try
        {
            proxy = Core.ServerGameManager.InstantiateEntityImmediate(playerCharacter, detonatePrefab);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz NOVA] InstantiateEntityImmediate({detonateGuid}) failed for player {playerCharacter.GetSteamId()}: {ex}");
            return Entity.Null;
        }
        if (!proxy.Exists()) return Entity.Null;

        try
        {
            proxy.With((ref EntityOwner o) => o.Owner = playerCharacter);
            if (proxy.Has<Translation>() && playerCharacter.TryGetComponent<Translation>(out var pt))
                proxy.With((ref Translation t) => t.Value = pt.Value);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz NOVA] anchor owner/pos failed on {proxy}: {ex.Message}");
        }

        Core.Log.LogInfo($"[Beelz NOVA] spawned detonation {proxy} prefab={detonatePrefab.GetPrefabName()} ({sourceLabel}) for player {playerCharacter.GetSteamId()}.");
        return proxy;
    }

    /// <summary>v0.24.4: walk EntityOwner chain up to 4 hops to find a player.</summary>
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

    /// <summary>
    /// v0.24.4: apply player-team/faction to a buff entity that was spawned
    /// downstream of a Beelzebub-transformed player's cast. Mirrors the
    /// fixup in ScriptSpawnServerPatch — same write, different spawn hook.
    /// </summary>
    static readonly PrefabGUID PlayerFaction = new(1106458752);

    static void FixupBuffOwnership(Entity buffEntity, Entity playerCharacter)
    {
        try
        {
            // EntityOwner direct to player.
            buffEntity.With((ref EntityOwner o) => o.Owner = playerCharacter);

            // Team + TeamReference from player.
            if (buffEntity.Has<Team>() && playerCharacter.TryGetComponent<Team>(out var playerTeam))
            {
                buffEntity.With((ref Team t) => t.Value = playerTeam.Value);
            }
            if (buffEntity.Has<TeamReference>() && playerCharacter.TryGetComponent<TeamReference>(out var playerTeamRef))
            {
                buffEntity.With((ref TeamReference tr) => tr.Value._Value = playerTeamRef.Value._Value);
            }
            // FactionReference = Players.
            if (buffEntity.Has<FactionReference>())
            {
                buffEntity.With((ref FactionReference fr) => fr.FactionGuid._Value = PlayerFaction);
            }

            if (Beelzebub.Config.Settings.VerboseLogging.Value)
            {
                Core.Log.LogInfo($"[Beelz CHAIN][buff-spawn] fixup {buffEntity} prefab={buffEntity.GetPrefabGuid().GetPrefabName()} for player {playerCharacter.GetSteamId()}");
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz CHAIN][buff-spawn] mutation failed on {buffEntity}: {ex.Message}");
        }
    }

    static void HandleWaypointTravelStart(Entity playerCharacter)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;
        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

        int stashed = SummonAllyService.StashAll(active, playerCharacter);
        if (stashed > 0)
        {
            active.SummonsDisabled = true;
            Core.Log.LogInfo($"[Beelz SUMMON] auto-stash on waypoint travel (safety net): stashed {stashed} for player {steamId}.");
        }
    }

    /// <summary>
    /// v0.42.0: a transformed player mounted a horse. In Stash mode, stash their active
    /// summons (Bloodcraft-style: clear Follower, add Disabled) so they don't try to chase
    /// the mount; the dismount handler restores them. Follow mode does nothing here — the
    /// summons stay in-world and the existing leash/combat plumbing keeps them with the
    /// (now mounted) player.
    /// </summary>
    static void HandleMount(Entity playerCharacter)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return; // only transformed players have Beelzebub summons

        if (Beelzebub.Config.Settings.Transform_MountedSummonMode.Value
                != Beelzebub.Config.Settings.MountedSummonMode.Stash)
        {
            // Follow mode — leave the summons alone (leash plumbing carries them along).
            if (Beelzebub.Config.Settings.VerboseLogging.Value
                && active.SummonedMinions is { Count: > 0 })
                Core.Log.LogInfo($"[Beelz SUMMON] horse mount (Follow mode): {active.SummonedMinions.Count} summon(s) keep leashing to player {steamId}.");
            return;
        }

        if (active.SummonedMinions == null || active.SummonedMinions.Count == 0) return;

        int stashed = SummonAllyService.StashAll(active, playerCharacter);
        if (stashed > 0)
        {
            active.SummonsDisabled = true;
            Core.Log.LogInfo($"[Beelz SUMMON] auto-stash on horse mount: stashed {stashed} for player {steamId}. Will restore on dismount.");
        }
    }

    static void HandleWaypointTravelEnd(Entity playerCharacter)
    {
        ulong steamId = playerCharacter.GetSteamId();
        if (steamId == 0) return;

        var active = Core.AbilityRegistry.GetActiveTransform(steamId);
        if (active == null) return;

        // v0.41.0 BUG FIX (#2): a travel/bat form overrode the transform's spell bar.
        // On arrival the player is still transformed, so re-apply the active transform —
        // otherwise the bar drops to weapon-naturals until a weapon swap. Runs regardless
        // of whether summons were stashed (the summon restore below early-returns without it).
        try { Core.Transforms.ReapplyActiveTransform(steamId, active, playerCharacter); }
        catch (Exception ex) { Core.Log.LogError($"[Beelz] re-apply transform on travel-end failed: {ex}"); }

        if (active.StashedSummons == null || active.StashedSummons.Count == 0) return;

        int restored = SummonAllyService.RestoreAll(active, playerCharacter);
        if (restored > 0)
        {
            active.SummonsDisabled = false;
            Core.Log.LogInfo($"[Beelz SUMMON] auto-restore on waypoint arrival: restored {restored} for player {steamId}.");
            try
            {
                Core.Chat.Send(playerCharacter, Verbosity.Summary,
                    $"Restored {restored} summon(s) at your destination.");
            }
            catch { /* chat failures non-critical */ }
        }
    }
}
