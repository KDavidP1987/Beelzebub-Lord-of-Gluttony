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
/// v0.101.0 — mount recovery + dismount primitives for the "Militia Fabian Mountup"
/// crash resolution (Sir Erwin). Casting that ability spawns CHAR_Militia_FabiansSteed as
/// an uncontrolled AI mount and leaves the player STUCK riding it; the next cast/move then
/// crashes the dedicated server with a Burst `AppendRemovedComponentRecordError` (the mount's
/// ability-bar replace collides with Beelz's slot-grant modifications on the same
/// AbilityGroupSlot). The corruption survives relog.
///
/// This service holds the two primitives that un-stick a player:
///   * <see cref="StripMountBuffs"/> — remove the rider/mount buffs from the player (detaches
///     them and reverses the mount's bar-replace). Reused later as the DISMOUNT primitive for
///     the despawn-on-unrelated-cast trigger (Stage 3).
///   * <see cref="DespawnNearbyFabianSteeds"/> — find + crash-safely despawn the orphaned steed
///     entity near the player (reuses <see cref="SummonAllyService"/>'s staged despawn).
///
/// Stage 1 of the Mountup resolution: drives the `.beelz admin unmount` recovery command.
/// Deliberately depends only on already-bound types (no UnitMount/Mountable) — the vanilla
/// mount-handshake work lands in Stage 2.
/// </summary>
internal static class MountRecovery
{
    // Buffs the Fabian Mountup chain (and the vanilla mount-control path) put on the RIDER.
    // Removing these detaches the player and reverses the mount's ability-bar replace.
    static readonly int[] _mountBuffGuids =
    {
        105565888,   // Buff_Militia_Fabian_MountRider_Attach
        1666453956,  // Buff_Militia_Fabian_MountRider_SharedHealthBuff
        1182509317,  // Buff_Militia_Fabian_MountRider_LaunchRider
        1542512910,  // Buff_Militia_Fabian_MountRider_LaunchLand
        -863496809,  // AB_Militia_FabiansSteed_RunClose_MounterBuff
        854656674,   // AB_Interact_Mount_Owner_Buff_Horse          (vanilla horse control)
        -978792376,  // AB_Interact_Mount_Owner_Buff_Horse_Vampire  (vanilla vampire-horse control)
    };

    // Belt-and-suspenders name match (catches skin/variant buffs we didn't enumerate).
    static readonly string[] _mountBuffNameFrags =
    { "MountRider", "Mountup", "Mount_Owner_Buff", "MounterBuff" };

    static readonly int[] _steedGuids =
    {
        826666431,    // CHAR_Militia_FabiansSteed
        -1509286426,  // CHAR_Militia_FabiansSteed_Minion
    };

    // The only mount prefab that is SAFE to spawn for a player. CHAR_Mount_Horse_Vampire (-1502865710)
    // and CHAR_Mount_Horse_Gloomrot instantly crash the dedicated server on spawn (per KindredCommands'
    // NOSPAWN list), so the resolution uses the plain horse.
    public const int CHAR_Mount_Horse = 1149585723;

    /// <summary>
    /// Remove every mount/rider buff from a player character. Snapshots the matching buff
    /// entities first (destroying a buff is a structural change that would invalidate a live
    /// BuffBuffer handle), then destroys them via V Rising's buff-removal path. Returns the
    /// count removed. Fully guarded — never throws into the caller.
    /// </summary>
    public static int StripMountBuffs(Entity character)
    {
        if (!character.Exists() || !Core.EntityManager.HasBuffer<BuffBuffer>(character)) return 0;

        var toRemove = new List<Entity>();
        try
        {
            var buffs = Core.EntityManager.GetBuffer<BuffBuffer>(character);
            for (int i = 0; i < buffs.Length; i++)
            {
                int guid = buffs[i].PrefabGuid._Value;
                bool match = false;
                foreach (var g in _mountBuffGuids) if (g == guid) { match = true; break; }
                if (!match)
                {
                    string nm = buffs[i].PrefabGuid.GetPrefabName() ?? "";
                    foreach (var f in _mountBuffNameFrags)
                        if (nm.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                }
                if (match && buffs[i].Entity.Exists()) toRemove.Add(buffs[i].Entity);
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz MOUNT] StripMountBuffs scan failed: {ex.Message}");
            return 0;
        }

        int removed = 0;
        foreach (var be in toRemove)
        {
            if (!be.Exists()) continue;
            try
            {
                DestroyUtility.Destroy(Core.EntityManager, be, DestroyDebugReason.TryRemoveBuff);
                removed++;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz MOUNT] mount-buff destroy failed: {ex.Message}"); }
        }
        if (removed > 0) Core.Log.LogInfo($"[Beelz MOUNT] stripped {removed} mount/rider buff(s) from {character}.");
        return removed;
    }

    /// <summary>
    /// Find orphaned Fabian steed entities near the player and despawn them via the crash-safe
    /// staged path (<see cref="SummonAllyService.EnqueueAdminDespawn"/> then an immediate drain).
    /// Queries by <c>Minion</c> (the steed spawns as one) and matches the steed prefab GUIDs /
    /// the "FabiansSteed" name, filtered to within ~60u of the player so other players' world
    /// state is never touched. Returns the count queued. Fully guarded.
    /// </summary>
    public static int DespawnNearbyFabianSteeds(Entity character)
    {
        float3 center = float3.zero;
        bool haveCenter = false;
        if (character.Exists() && character.TryGetComponent<LocalToWorld>(out var ltw))
        {
            center = ltw.Position;
            haveCenter = true;
        }

        int queued = 0;
        try
        {
            EntityQuery q = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Minion>(),
                ComponentType.ReadOnly<PrefabGUID>());
            NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    Entity e = arr[i];
                    if (!e.Exists()) continue;

                    int g = e.GetPrefabGuid()._Value;
                    bool isSteed = false;
                    foreach (var sg in _steedGuids) if (sg == g) { isSteed = true; break; }
                    if (!isSteed)
                    {
                        string nm = e.GetPrefabGuid().GetPrefabName() ?? "";
                        if (nm.IndexOf("FabiansSteed", StringComparison.OrdinalIgnoreCase) >= 0) isSteed = true;
                    }
                    if (!isSteed) continue;

                    // Only despawn steeds near the player so we never nuke unrelated world state.
                    if (haveCenter && e.TryGetComponent<LocalToWorld>(out var sl))
                    {
                        float dx = sl.Position.x - center.x, dz = sl.Position.z - center.z;
                        if (dx * dx + dz * dz > 60f * 60f) continue;
                    }

                    SummonAllyService.EnqueueAdminDespawn(e);
                    queued++;
                }
            }
            finally { arr.Dispose(); }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz MOUNT] steed sweep failed: {ex.Message}"); }

        if (queued > 0)
        {
            SummonAllyService.DrainAdminQueueImmediate();
            Core.Log.LogInfo($"[Beelz MOUNT] despawned {queued} Fabian steed(s) near {character}.");
        }
        return queued;
    }

    /// <summary>
    /// v0.101.0 (Stage 2): spawn a vanilla rideable CHAR_Mount_Horse at the player's position so we
    /// can test the mount handshake + crash-avoidance with a real player mount. Uses the same direct
    /// <c>UnitSpawnerUpdateSystem.SpawnUnit</c> call KindredCommands uses. The horse appears a frame or
    /// two later (the spawn is async), so the player mounts it via V Rising's own interact handshake —
    /// the most faithful test of whether a mount's ability-bar replace coexists with Beelz grants.
    /// Returns true if the spawn was requested.
    /// </summary>
    public static bool SpawnTestHorse(Entity character)
    {
        if (!character.Exists() || !character.TryGetComponent<LocalToWorld>(out var ltw)) return false;
        try
        {
            var usus = Core.Server.GetExistingSystemManaged<UnitSpawnerUpdateSystem>();
            usus.SpawnUnit(Entity.Null, new PrefabGUID(CHAR_Mount_Horse), ltw.Position, 1, 1f, 2f, -1f);
            Core.Log.LogInfo($"[Beelz MOUNT] spawned test horse (CHAR_Mount_Horse) near {character}.");
            return true;
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz MOUNT] SpawnTestHorse failed: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Despawn rideable horses (CHAR_Mount_Horse) near the player — queries by <c>Mountable</c>, the
    /// component a spawned horse carries (the Fabian steed has no Mountable, so this is separate from
    /// <see cref="DespawnNearbyFabianSteeds"/>). Crash-safe staged despawn. Returns count queued.
    /// </summary>
    public static int DespawnNearbyRideables(Entity character)
    {
        float3 center = float3.zero;
        bool haveCenter = false;
        if (character.Exists() && character.TryGetComponent<LocalToWorld>(out var ltw))
        {
            center = ltw.Position;
            haveCenter = true;
        }

        int queued = 0;
        try
        {
            EntityQuery q = Core.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Mountable>(),
                ComponentType.ReadOnly<PrefabGUID>());
            NativeArray<Entity> arr = q.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    Entity e = arr[i];
                    if (!e.Exists() || e.GetPrefabGuid()._Value != CHAR_Mount_Horse) continue;
                    if (haveCenter && e.TryGetComponent<LocalToWorld>(out var sl))
                    {
                        float dx = sl.Position.x - center.x, dz = sl.Position.z - center.z;
                        if (dx * dx + dz * dz > 60f * 60f) continue;
                    }
                    SummonAllyService.EnqueueAdminDespawn(e);
                    queued++;
                }
            }
            finally { arr.Dispose(); }
        }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz MOUNT] rideable sweep failed: {ex.Message}"); }

        if (queued > 0)
        {
            SummonAllyService.DrainAdminQueueImmediate();
            Core.Log.LogInfo($"[Beelz MOUNT] despawned {queued} test horse(s) near {character}.");
        }
        return queued;
    }

    // v0.105.0 — the ACTUAL mount-demount fix (after v0.101/0.102/0.103 each treated a wrong symptom).
    // GROUND TRUTH from the v0.104 instrumented test (server log): casting Erwin's Call Lightning from the
    // saddle spawns `Buff_Militia_Fabian_CastImpair` "for player" and the rider is thrown off the SAME tick.
    // Two earlier theories were DISPROVEN by that same log:
    //   * NOT the damage path — `DemountMinDamageFactor` read 1e6 (our v0.103 edit applied) yet it still demounted.
    //   * NOT (only) the StartCast→Unit_Mount spawn — v0.102 stripped that entry, yet the buff still reaches
    //     the player by another spawn path in the ability's chain.
    // The CastImpair buff is a 3s debuff whose ONLY active component is BuffModificationFlagData
    // (Fabian = 17213423616 = 2^34|2^25; FabiansSteed = 17179869184 = 2^34) — one of those flag bits breaks
    // the mount. Since the buff does nothing else, the robust + spawn-source-independent fix is to neutralize
    // the BUFF wherever it lands: zero its ModificationTypes at init. This makes Erwin's whole saddle kit
    // mount-safe regardless of how/where each cast applies the impair.
    //
    // Cross-ability impact (docs/ABILITY_CHANGE_IMPACT.md): GLOBAL prefab edit shared with the Erwin boss —
    // his CastImpair self-debuff goes inert (it only ever applied flag modifications, nothing else), a
    // negligible boss-side change. Scope is deliberately the two KNOWN Fabian CastImpair buffs, NOT a blind
    // "zero every buff with a mount bit" (we don't know which bit dismounts, and zeroing unrelated buffs
    // would be catastrophic) — this is the targeted, evidence-backed class fix for Erwin's kit.
    static readonly int[] _mountDisruptBuffs =
    {
        1671499645,   // Buff_Militia_Fabian_CastImpair        (Call Lightning / Lightning Orb)
        -1205493484,  // Buff_Militia_FabiansSteed_CastImpair   (Mounted Swing)
    };

    /// <summary>
    /// Init-time global prefab edit: zero the <c>BuffModificationFlagData.ModificationTypes</c> on the Fabian/
    /// Erwin <c>*_CastImpair</c> buffs so that, wherever a saddle cast applies one, it can no longer disrupt
    /// the rider's mount (the witnessed demount cause). The buffs carry no other active component, so this
    /// fully neutralizes them. Idempotent (zero stays zero). Runs at init next to AbilityTuningService.ApplyAll.
    /// Gated by Forms_CustomAbilities_Enabled. Fully guarded. Returns the number of buffs neutralized.
    /// </summary>
    public static int NeutralizeMountDisruptingCasts()
    {
        if (!Beelzebub.Config.Settings.Forms_CustomAbilities_Enabled.Value) return 0;
        int neutralized = 0;
        foreach (int guid in _mountDisruptBuffs)
        {
            try
            {
                if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(guid), out Entity buff) || !buff.Exists())
                    continue;
                if (!buff.Has<BuffModificationFlagData>()) continue;
                buff.With((ref BuffModificationFlagData m) =>
                    System.Runtime.CompilerServices.Unsafe.AsRef(in m.ModificationTypes) = 0L);
                neutralized++;
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz MOUNT] CastImpair neutralize failed on {guid}: {ex.Message}"); }
        }
        if (neutralized > 0)
            Core.Log.LogInfo($"[Beelz MOUNT] neutralized {neutralized} mount-disrupting CastImpair buff(s) (zeroed BuffModificationFlagData) — Erwin/Fabian saddle casts no longer throw the rider off the horse.");
        return neutralized;
    }

    /// <summary>
    /// Full recovery for a player stuck on a summoned mount: strip the rider/mount buffs,
    /// despawn the orphaned steed, and re-apply the player's Beelz slot grants (repairing the
    /// half-cleaned AbilityGroupSlot modification sources the crash left behind). Returns the
    /// per-step counts for the caller to report. Order matters: detach the player (buffs) before
    /// despawning the steed, then restore the bar.
    /// </summary>
    public static (int buffs, int steeds, int grants) Recover(Entity character)
    {
        int buffs = StripMountBuffs(character);
        int steeds = DespawnNearbyFabianSteeds(character);
        int grants = 0;
        try { grants = SlotApply.RestoreResolvedGrants(character); }
        catch (Exception ex) { Core.Log.LogWarning($"[Beelz MOUNT] grant restore failed: {ex.Message}"); }
        return (buffs, steeds, grants);
    }
}
