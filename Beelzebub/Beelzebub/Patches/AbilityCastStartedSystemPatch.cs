using System;
using System.Collections.Generic;
using Beelzebub.Services;
using HarmonyLib;
using ProjectM;
using ProjectM.Gameplay.Systems;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Beelzebub.Patches;

/// <summary>
/// v0.22.0 — manual summon-ally spawn at cast-start (Bloodcraft pattern).
/// v0.23.0 — adds per-ability stack-cap check, recent-cast attribution table
/// so <see cref="LinkMinionToOwnerOnSpawnSystemPatch"/> can credit natural-chain
/// spawns back to the cast that caused them, and routes setup through
/// <see cref="SummonAllyService"/>.
/// </summary>
[HarmonyPatch(typeof(AbilityCastStarted_SetupAbilityTargetSystem_Shared),
              nameof(AbilityCastStarted_SetupAbilityTargetSystem_Shared.OnUpdate))]
internal static class AbilityCastStartedSystemPatch
{
    /// <summary>Substring patterns identifying a summon-class ability (case-insensitive).</summary>
    static readonly string[] SummonNamePatterns = {
        // v0.125.0: broadened from "_Summon_" to bare "_Summon" so EVERY "_SummonX" boss ability is
        // recognized — _SummonMinions / _SummonTail / _SummonAide / _SummonOrb / _SummonAngel /
        // _SummonEyeOfGod / _SummonGhosts / _SummonBats / _Summoning_ all match this single token.
        // (Unmapped summons still produce nothing on their own — the natural chain spawns ownerless
        // minions — so a manual-spawn SummonTargets entry is what actually makes one work.)
        "_Summon",
        "_Reinforcement_", "_CallReinforcements_",
        "_RaiseDead_", "_RaiseHorde_",
    };

    /// <summary>
    /// v0.23.12: waypoint ability-group GUIDs. When a transformed player initiates
    /// a waygate cast, auto-stash their summons so V Rising's "subdued enemy"
    /// pre-check doesn't refuse. AbilityCastStartedEvent fires at cast-start
    /// (before the validation gate per our hypothesis); if validation actually
    /// runs first we'll know from testing and need a different hook.
    /// </summary>
    static readonly System.Collections.Generic.HashSet<int> WaypointCastAbilityGroups = new()
    {
        893332545,   // AB_Interact_UseWaypoint_AbilityGroup (world waygate)
        695067846,   // AB_Interact_UseWaypoint_Castle_AbilityGroup (castle waypoint)
    };

    /// <summary>
    /// Curated map: ability-group name → (target CHAR_ prefab GUID, spawn count).
    /// Add entries here as test data reveals each V-Blood's summon target.
    /// </summary>
    public static readonly Dictionary<string, (int targetGuid, int spawnCount)> SummonTargets =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "AB_Undead_BishopOfShadows_ShadowSoldier_AbilityGroup", (678628353, 2) },
            { "AB_Undead_BishopOfShadows_ShadowSoldier_Group",        (678628353, 2) },
            { "AB_Bandit_StoneBreaker_VBlood_Reinforcement_Group", (-2039670689, 2) },
            { "AB_Bandit_StoneBreaker_VBlood_Reinforcement_AbilityGroup", (-2039670689, 2) },
            { "AB_Bandit_Foreman_Reinforcement_Group", (-2039670689, 2) },
            { "AB_Bandit_Foreman_Reinforcement_AbilityGroup", (-2039670689, 2) },
            // v0.24.2 (#93): Priest RaiseDead — natural chain animates existing
            // corpses via Summon_Melee/Ranged HitTrigger carriers. When a player
            // casts, there are typically no corpses in range → nothing spawns.
            // Manual-spawn pattern bypasses this: directly instantiate skeleton
            // soldiers at the player's position.
            // v0.26.0 (#93 count): the Elite RaiseDead channel ticks 3x and spawns
            // BOTH a melee and a ranged skeleton per relevant tick — in the real
            // fight the boss raises ~3-4 (user-observed). We were spawning only 2,
            // so bump Elite→4 and base→3 to match the boss feel. (CHAR_Undead_
            // SkeletonSoldier_Base; a melee/ranged unit MIX is a later refinement —
            // would need SummonTargets to carry multiple (guid,count) pairs.)
            { "AB_Undead_Priest_RaiseDead_AbilityGroup", (-603934060, 3) }, // CHAR_Undead_SkeletonSoldier_Base
            { "AB_Undead_Priest_RaiseDead_Group",        (-603934060, 3) },
            { "AB_Undead_Priest_Elite_RaiseDead_AbilityGroup", (-603934060, 4) },
            { "AB_Undead_Priest_Elite_RaiseDead_Group",        (-603934060, 4) },
            // v0.30.0: Dracula summons — units identified by prefab-name audit
            // (the SpawnMinion unit ref is in a binary blob, but the CHAR_ prefabs
            // name-match unambiguously). SummonBats = transient attack swarm; the
            // natural chain spawns 2 bats UNOWNED (InheritOwner:false) so they don't
            // fight for the player — manual-spawn owned copies instead.
            { "AB_Vampire_Dracula_SummonBats_Abilitygroup", (-2092104425, 2) }, // CHAR_Dracula_ShadowBatSwarm ×2
            { "AB_Vampire_Dracula_SummonBats_Group",        (-2092104425, 2) },
            // BloodStones is gated for players (its SummonTrigger self-destructs
            // without a valid spell target) → produces nothing. Manual-spawn it.
            { "AB_Vampire_Dracula_BloodStones_Summon_AbilityGroup", (32692466, 1) }, // CHAR_Dracula_SpellStone_LargeBlood
            { "AB_Vampire_Dracula_BloodStones_Summon_Group",        (32692466, 1) },

            // v0.125.0 — tester-reported boss summons that produced nothing for a player caster. Their
            // natural chain spawns the unit UNOWNED (SpawnMinionOnGameplayEvent InheritOwner:False), so
            // LinkMinion can't claim it — manual-spawn an owned ally instead. Target units are name-matched
            // from the prefab dump (the exact unit GUID is sealed in a binary blob), so a pick may need a
            // tweak after live testing, but each now spawns a player-allied unit on cast.
            { "AB_Bandit_Tourok_VBlood_CallReinforcements_AbilityGroup", (-301730941, 2) }, // CHAR_Bandit_Thug
            { "AB_Bandit_Tourok_VBlood_CallReinforcements_Group",        (-301730941, 2) },
            { "AB_Bandit_Stalker_VBlood_Reinforcement_AbilityGroup", (-309264723, 2) }, // CHAR_Bandit_Stalker
            { "AB_Bandit_Stalker_VBlood_Reinforcement_Group",        (-309264723, 2) },
            { "AB_BatVampire_SummonMinions_AbilityGroup", (593505050, 3) }, // CHAR_Legion_BatSwarm
            { "AB_BatVampire_SummonMinions_Group",        (593505050, 3) },
            { "AB_Blackfang_Morgana_SummonTail_AbilityGroup", (-1075824048, 1) }, // CHAR_Blackfang_MorganasTail
            { "AB_Blackfang_Morgana_SummonTail_Group",        (-1075824048, 1) },
            { "AB_Cardinal_SummonAide_AbilityGroup", (1745498602, 1) }, // CHAR_ChurchOfLight_CardinalAide
            { "AB_Cardinal_SummonAide_Group",        (1745498602, 1) },
            { "AB_Cardinal_SummonOrb_AbilityGroup", (1917502536, 1) }, // CHAR_ChurchOfLight_SmiteOrb
            { "AB_Cardinal_SummonOrb_Group",        (1917502536, 1) },
            { "AB_ChurchOfLight_Paladin_SummonAngel_AbilityGroup", (-1737346940, 1) }, // CHAR_Paladin_DivineAngel
            { "AB_ChurchOfLight_Paladin_SummonAngel_Group",        (-1737346940, 1) },
            { "AB_HighLord_RaiseDead_AbilityGroup", (-603934060, 3) }, // CHAR_Undead_SkeletonSoldier_Base
            { "AB_HighLord_RaiseDead_Group",        (-603934060, 3) },
            { "AB_Militia_BishopOfDunley_SummonEyeOfGod_AbilityGroup", (-1254618756, 1) }, // CHAR_Militia_EyeOfGod
            { "AB_Militia_BishopOfDunley_SummonEyeOfGod_Group",        (-1254618756, 1) },
            { "AB_Undead_ZealousCultist_SummonGhosts_AbilityGroup", (128488545, 3) }, // CHAR_Undead_ZealousCultist_Ghost
            { "AB_Undead_ZealousCultist_SummonGhosts_Group",        (128488545, 3) },

            // v0.126.0 — second tester pass. More boss summons name-matched from the prefab dump.
            { "AB_Blackfang_CarverBoss_SummonCarvers_AbilityGroup", (-1508046438, 2) }, // CHAR_Blackfang_WoodCarver
            { "AB_Blackfang_CarverBoss_SummonCarvers_Group",        (-1508046438, 2) },
            // Bishop of Dunley "holy pillar" — no CHAR_*Pillar exists; the spawn unit is blob-locked, so this
            // is a best-guess to the stationary holy hazard (the Dunley enchanted cross). May need a tweak.
            { "AB_Militia_BishopOfDunley_SummonPillar_AbilityGroup", (-1449314709, 1) }, // CHAR_ChurchOfLight_EnchantedCross (best-guess)
            { "AB_Militia_BishopOfDunley_SummonPillar_Group",        (-1449314709, 1) },
            // Lightning Pillars — now mapped per tester request (it's a STATIONARY damage turret). Still
            // flagged "too OP": admins can tame it with `.beelz admin tune <id> damagescale/cooldown/summoncap`.
            { "AB_Monster_SummonLightningPillars_AbilityGroup", (-1977168943, 1) }, // CHAR_Monster_LightningPillar
            { "AB_Monster_SummonLightningPillars_Group",        (-1977168943, 1) },
            // NOTE: AB_Blackfang_Morgana_SummonTail spawns CHAR_Blackfang_MorganasTail (mapped above) but it is
            // an IMMOBILE boss-PART (BehaviourTreeInstance.Immobile=true) — it appears but its boss behaviour
            // tree doesn't drive combat standalone, so it won't actively attack. Left mapped (it spawns); making
            // a boss appendage fight for the player would need bespoke AI work, deferred.
        };

    /// <summary>
    /// v0.23.0: most recent cast event per (steamId, abilityGuid). Read by
    /// LinkMinionToOwnerOnSpawnSystemPatch to attribute natural-chain spawns
    /// (Undead Priest's _RaiseHorde_, etc.) back to the cast that caused them
    /// so stack-cap accounting includes them.
    /// </summary>
    public static readonly Dictionary<ulong, (int abilityGuid, DateTime when)> RecentSummonCast = new();
    // v0.23.16 (B1 fix): bumped from 3s → 10s. Priest's RaiseHorde / RaiseDead
    // channel for ~2s before V Rising's spawn chain produces the actual entities,
    // so a 3-second window only just catches the first spawn — subsequent
    // entities from the same cast (and the spawn-then-buff-then-rebuild chain
    // some abilities use) frequently land at 4-6s, missing the window entirely.
    // 10s gives ~8s of slack while staying well under the typical interval
    // between intentional re-casts of the same summon by a player.
    public static readonly TimeSpan AttributionWindow = TimeSpan.FromSeconds(10);

    /// <summary>Dedupe rapid repeat firings of the same cast event.</summary>
    static readonly Dictionary<(ulong, int), DateTime> _recentCasts = new();
    static readonly TimeSpan DedupeWindow = TimeSpan.FromMilliseconds(250);

    // v0.52.0 — UNTRANSFORMED chain-cast attribution. When an untransformed player casts a
    // CAPTURED (Beelzebub-granted) ability, we stamp the time here. The spawn patches
    // (ScriptSpawnServerPatch / BuffSpawnServerPatch) team/faction-fixup chain entities owned
    // by a player only when ShouldFixupChainFor is true — previously that was "is transformed",
    // which left untransformed captured chain abilities (projectiles/AoEs / teleport-detonates)
    // on the NPC team so they never hit enemies. Scoping to the attribution window + to captured
    // abilities keeps vanilla player abilities and other mods' entities untouched.
    public static readonly Dictionary<ulong, DateTime> RecentGrantCast = new();

    /// <summary>Stamp that this player just cast a captured ability (untransformed).</summary>
    public static void RecordGrantCast(ulong steamId)
    {
        if (steamId != 0) RecentGrantCast[steamId] = DateTime.UtcNow;
    }

    /// <summary>True if the player cast a captured ability within the attribution window.</summary>
    public static bool HasRecentGrantCast(ulong steamId)
        => steamId != 0 && RecentGrantCast.TryGetValue(steamId, out var when)
           && (DateTime.UtcNow - when) < AttributionWindow;

    /// <summary>
    /// v0.52.0: should the spawn patches rewrite team/faction on chain entities owned by this
    /// player? True while TRANSFORMED (boss-kit chains) OR briefly after a captured-ability cast
    /// in normal form (so untransformed captured chain abilities fire against enemies). Replaces
    /// the old transform-only gate at the chain-fixup sites.
    /// </summary>
    public static bool ShouldFixupChainFor(ulong steamId)
        => steamId != 0
           && (Core.AbilityRegistry?.GetActiveTransform(steamId) is not null || HasRecentGrantCast(steamId));

    [HarmonyPrefix]
    public static void OnUpdatePrefix(AbilityCastStarted_SetupAbilityTargetSystem_Shared __instance)
    {
        if (!Core.IsReady) return;
        Services.Heartbeat.Pulse();   // v0.81.0: drive periodic ticks on every cast (throttled)

        NativeArray<AbilityCastStartedEvent> events;
        try
        {
            events = __instance.EntityQueries[0].ToComponentDataArray<AbilityCastStartedEvent>(Allocator.Temp);
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[Beelz] AbilityCastStarted query failed: {ex}");
            return;
        }

        // v0.43.5: granted-ability power scaling runs for ANY player cast — independent of
        // the summons-allies config AND of being transformed. (The service self-gates to
        // captured abilities in normal form and no-ops unless an admin enabled scaling.)
        bool summonsAllies = Beelzebub.Config.Settings.Transform_SummonsAreAllies.Value;
        try
        {
            foreach (var evt in events)
            {
                try { Services.GrantPowerScalingService.OnGrantedCast(evt.Character, evt.AbilityGroup.GetPrefabGuid()); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz] grant power-scale failed: {ex.Message}"); }

                // v0.71.0: enforce a configured cooldown on a granted ability's live slot state
                // (the prefab cooldown edit doesn't reach granted casts). Records here; applied on tick.
                try { Services.AbilityCooldownEnforcer.OnCast(evt.Character, evt.AbilityGroup.GetPrefabGuid()); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz] cooldown-enforce record failed: {ex.Message}"); }

                // v0.52.0: attribute an untransformed CAPTURED-ability cast so the spawn patches
                // can team-fixup its chain entities (untransformed chain casting).
                try { RecordGrantCastIfCaptured(evt); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz] grant-cast attribution failed: {ex.Message}"); }

                // v0.91.0: selective form exit — if a player in a custom shapeshift form casts a NON-form,
                // non-assigned ability, exit the form (the form holds for assigned + native abilities).
                try { Services.ShapeshiftAbilityService.HandleFormCastExit(evt.Character, evt.AbilityGroup.GetPrefabGuid()._Value); }
                catch (Exception ex) { Core.Log.LogWarning($"[Beelz FORM] HandleFormCastExit failed: {ex.Message}"); }

                // Summon-ally cast handling (transform-only) stays gated by its config.
                if (summonsAllies)
                {
                    try { ProcessCast(evt); }
                    catch (Exception ex) { Core.Log.LogError($"[Beelz] ProcessCast failed: {ex}"); }
                }
            }
        }
        finally { events.Dispose(); }
    }

    /// <summary>
    /// v0.52.0: record attribution if an UNTRANSFORMED player cast a CAPTURED ability. Transformed
    /// players already satisfy the chain-fixup gate, so we only need the stamp in normal form.
    /// </summary>
    static void RecordGrantCastIfCaptured(AbilityCastStartedEvent evt)
    {
        Entity caster = evt.Character;
        if (!caster.Exists() || !caster.IsPlayer()) return;
        ulong steamId = caster.GetSteamId();
        if (steamId == 0) return;
        if (Core.AbilityRegistry.GetActiveTransform(steamId) is not null) return; // transform path already fixes up
        if (Core.AbilityRegistry.HasCaptured(steamId, evt.AbilityGroup.GetPrefabGuid()._Value))
            RecordGrantCast(steamId);
    }

    static void ProcessCast(AbilityCastStartedEvent evt)
    {
        Entity caster = evt.Character;
        if (!caster.Exists() || !caster.IsPlayer()) return;

        ulong steamId = caster.GetSteamId();
        if (steamId == 0) return;

        // v0.45.0: summons work untransformed. Resolve the EXISTING summon owner (transform
        // OR standalone) without creating one yet — we only materialize a standalone container
        // below, once we've confirmed this is actually a summon cast.
        var active = Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: false);
        bool transformed = active is ActiveTransform;

        PrefabGUID abilityPrefab = evt.AbilityGroup.GetPrefabGuid();

        // v0.23.16 (B1 diagnostic): log every cast from a transformed player.
        // Lets us see whether Priest's _RaiseHorde_ is being received by this
        // hook at all, and whether name-matching identifies it as a summon.
        if (Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            string castName = abilityPrefab.GetPrefabName() ?? "?";
            Core.Log.LogInfo($"[Beelz SUMMON][cast] steamId={steamId} ability={castName} guid={abilityPrefab._Value}");
        }

        // v0.25.0: open a chain-trace window for EVERY transformed-player cast
        // (not just summons) so the runtime audit can see what each ability's
        // chain actually spawns. See ChainTraceService. v0.45.0: transform-era
        // boss-kit diagnostic only — skip for untransformed casts.
        // v0.106.0: also trace MOUNTED casts (mounting isn't an ActiveTransform, so it misses the
        // `transformed` path above). Diagnosing why boss saddle abilities (Erwin's lightning) fire
        // only partially — the trace shows what their effect chain spawns + each entity's team.
        if (transformed || Services.ShapeshiftAbilityService.IsMounted(steamId))
            Services.ChainTraceService.BeginTrace(steamId, abilityPrefab.GetPrefabName() ?? "?");

        // v0.23.12: auto-stash on waygate cast. Fires BEFORE we check
        // SummonsDisabled because we want to stash regardless of toggle state
        // when the player is trying to teleport.
        if (WaypointCastAbilityGroups.Contains(abilityPrefab._Value))
        {
            if (active != null && active.SummonedMinions != null && active.SummonedMinions.Count > 0)
            {
                int stashed = Services.SummonAllyService.StashAll(active, caster);
                if (stashed > 0)
                {
                    active.SummonsDisabled = true;
                    Core.Log.LogInfo($"[Beelz SUMMON] auto-stash on waypoint cast: stashed {stashed} for player {steamId} (ability={abilityPrefab.GetPrefabName()}).");
                    try
                    {
                        Core.Chat.Send(caster, Verbosity.Summary,
                            $"Auto-stashed {stashed} summon(s) for waygate. They'll restore on arrival.");
                    }
                    catch { /* chat failures non-critical */ }
                }
            }
            return; // not a summon cast; nothing else to do
        }

        if (active != null && active.SummonsDisabled) return; // v0.23.0 toggle

        string abilityName = abilityPrefab.GetPrefabName() ?? "";
        if (string.IsNullOrEmpty(abilityName)) return;

        bool isSummon = false;
        foreach (string pat in SummonNamePatterns)
        {
            if (abilityName.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isSummon = true;
                break;
            }
        }
        if (!isSummon) return;

        // v0.45.0: confirmed a summon cast — ensure a tracking container exists now
        // (materializes the standalone owner for an untransformed summoner on first use).
        active ??= Core.AbilityRegistry.GetSummonOwner(steamId, createIfMissing: true);

        var dedupeKey = (steamId, abilityPrefab._Value);
        DateTime now = DateTime.UtcNow;
        if (_recentCasts.TryGetValue(dedupeKey, out var lastFire) && (now - lastFire) < DedupeWindow)
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz SUMMON][cast] dedupe-skip {abilityName} (within {DedupeWindow.TotalMilliseconds}ms)");
            return;
        }
        _recentCasts[dedupeKey] = now;

        // v0.23.6: record recent cast FIRST. Previously this happened after the
        // cap check, so refused casts couldn't attribute their natural-chain
        // spawns — and LinkMinion couldn't destroy them. Now: always update the
        // attribution window so over-cap spawns can be identified and destroyed
        // by Hook 2 even when our manual spawn is refused.
        RecentSummonCast[steamId] = (abilityPrefab._Value, now);

        // v0.23.6: cap check uses LiveCastCount (= number of live groups, where
        // each group is one cast). Refuses when N uses of the ability are still
        // alive. Different from v0.23.0-5 which counted live ENTITIES.
        int liveCount = SummonAllyService.LiveCastCount(active, abilityPrefab._Value);
        // v0.79.0: per-ability summon cap overrides the global default (precedence per-ability > global).
        int cap = Core.AbilityRules.ResolveSummonCap(abilityName, Beelzebub.Config.Settings.Transform_MaxStacksPerSummonAbility.Value);
        if (cap > 0 && liveCount >= cap)
        {
            Core.Log.LogInfo($"[Beelz SUMMON] {steamId} cast {abilityName} REFUSED — at use cap {liveCount}/{cap}. Wait for a previous use's minions to fully die.");
            try { Core.Chat.Send(caster, Verbosity.Summary, $"Summon use limit reached ({liveCount}/{cap}) for {abilityName.Humanize()}. Wait for an active use's minions to die."); }
            catch { /* chat failures non-critical */ }
            return;
        }

        // Cap passed → open a new cast group for tracking.
        SummonAllyService.BeginCastGroup(active, abilityPrefab._Value);
        if (Beelzebub.Config.Settings.VerboseLogging.Value)
        {
            int groupsAfter = active.SummonStacks != null
                && active.SummonStacks.TryGetValue(abilityPrefab._Value, out var gs)
                ? gs.Count : 0;
            Core.Log.LogInfo($"[Beelz SUMMON][cast] open cast-group for {abilityName} (now {groupsAfter} group(s) tracked, uses {liveCount}/{(cap > 0 ? cap.ToString() : "∞")} pre-cap)");
        }

        // Lookup target in map. If not present, this is a natural-chain ability
        // (e.g. Undead Priest _RaiseHorde_) — V Rising's spawner will produce
        // entities; Hook 2 catches them.
        if (!SummonTargets.TryGetValue(abilityName, out var entry))
        {
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz SUMMON] {steamId} cast {abilityName} — no manual-spawn entry; relying on natural chain + LinkMinion rebind. (Add entry to SummonTargets to enable manual spawn for unmapped abilities.)");
            return;
        }

        var targetPrefab = new PrefabGUID(entry.targetGuid);
        string targetName = targetPrefab.GetPrefabName() ?? "?";

        float3 spawnPos = float3.zero;
        if (caster.TryGetComponent<LocalToWorld>(out var ltw)) spawnPos = ltw.Position;

        int spawned = 0;
        for (int i = 0; i < entry.spawnCount; i++)
        {
            try
            {
                Entity minion = Core.ServerGameManager.InstantiateEntityImmediate(caster, targetPrefab);
                if (!minion.Exists()) continue;

                if (minion.Has<Translation>())
                {
                    float angle = (float)(i * (Math.PI * 2.0 / Math.Max(1, entry.spawnCount)));
                    float3 offset = new float3((float)Math.Cos(angle) * 1.5f, 0f, (float)Math.Sin(angle) * 1.5f);
                    minion.With((ref Translation t) => t.Value = spawnPos + offset);
                }

                if (!SummonAllyService.ApplyPlayerAllySetup(minion, caster)) continue;

                active.SummonedMinions ??= new List<Entity>();
                active.SummonedMinions.Add(minion);
                SummonAllyService.TrackInCurrentGroup(active, abilityPrefab._Value, minion);
                spawned++;

                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz SUMMON] manual-spawn ally {minion} prefab={targetName} for player {steamId} (ability={abilityName})");
            }
            catch (Exception ex)
            {
                Core.Log.LogError($"[Beelz SUMMON] InstantiateEntityImmediate failed for {targetName}: {ex}");
            }
        }

        if (spawned > 0)
        {
            int afterCount = SummonAllyService.LiveCastCount(active, abilityPrefab._Value);
            Core.Log.LogInfo($"[Beelz SUMMON] {steamId} cast {abilityName} → spawned {spawned} allied {targetName} unit(s) [uses {afterCount}/{(cap > 0 ? cap.ToString() : "∞")}]");
        }
    }
}
