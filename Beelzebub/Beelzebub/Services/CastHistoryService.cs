using System;
using System.Collections.Generic;
using System.Diagnostics;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.132.0 (P1 step 4) — per-player record of recent ability casts + the runtime <c>forcetimeout</c> tracker.
///
/// CAST HISTORY: every PLAYER cast (captured or native) is recorded from <c>AbilityCastStartedSystemPatch</c>
/// as <c>{group, time, captured, forceTimeout snapshot}</c> and kept for <see cref="RetentionSeconds"/>. Native
/// casts are kept on purpose: a buff whose prefab a native ability also reaches is then AMBIGUOUS and is left
/// alone instead of being blamed on the captured one. (P2's damage attribution extends this service.)
///
/// FORCETIMEOUT (runtime since v0.132.0): the old build added a <c>LifeTime</c> to the ability's indefinite
/// buff PREFABS, which leaked to the source boss and was a structural prefab edit. Now nothing is written to
/// prefabs: when a buff INSTANCE spawns, it's attributed to a cast — owners(buff prefab) ∩ that player's
/// retained casts must give exactly ONE group, and it must be captured — and if that group has a
/// forcetimeout and the buff has no finite lifetime, the instance is destroyed after the timeout. The timeout
/// is snapshotted at cast, so a reload doesn't change a running timer. Trackers are dropped when the buff
/// is gone or its player disconnects.
/// </summary>
internal static class CastHistoryService
{
    // v0.133.0: Damage_ProvenanceRetentionSeconds (floor 30 s; default 120 s).
    public static float RetentionSeconds
    {
        get
        {
            float v = Beelzebub.Config.Settings.Damage_ProvenanceRetentionSeconds?.Value ?? 120f;
            return float.IsFinite(v) ? Math.Max(30f, v) : 120f;
        }
    }
    const int MaxCastsPerPlayer = 512;   // memory bound only; overflow makes attribution ambiguous (never unique by omission)
    const int MaxTrackedBuffs = 4096;

    public sealed class CastRecord
    {
        public long CastId;           // v0.133.0: monotonic, unique per cast
        public int Group;
        public double Time;
        public bool Captured;
        public float? ForceTimeout;   // snapshot at cast
        public Entity GroupEntity;    // the player's ability-group instance that was cast (AbilityCastStartedEvent has
                                      // no slot field)
        public bool FromSlot;         // v0.133.0: GroupEntity IS one of the caster's bar-slot state entities
                                      // (structural — a `.beelz cast` force-cast is not; rev 6.3 #1)
        public Logic.DamageSnapshot Damage = Logic.DamageSnapshot.Identity;   // v0.133.0: frozen at cast (captured only)
    }

    static long _nextCastId;

    sealed class Tracked
    {
        public Entity Buff;
        public ulong SteamId;
        public int Group;
        public int BuffGuid;
        public double ExpireAt;
    }

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    public static double Now => _clock.Elapsed.TotalSeconds;

    static readonly Dictionary<ulong, List<CastRecord>> _casts = new();
    // Per player: until this time, casts were dropped by the cap inside the retention window, so the retained
    // list is not the full picture — attribution must answer "ambiguous" instead of a possibly-false unique match.
    static readonly Dictionary<ulong, double> _overflowUntil = new();
    static readonly Dictionary<(int index, int version), Tracked> _tracked = new();

    // Counters for ability-inspect / logs.
    public static int Expired { get; private set; }
    public static int SkippedAmbiguous { get; private set; }

    // Groups with a forcetimeout rule. Refreshed at most every 2 s so the buff-spawn hot path is a set lookup.
    static readonly HashSet<int> _timeoutGroups = new();
    static double _timeoutGroupsAt = -999;

    /// <summary>Any forcetimeout rule configured? (the buff-spawn scan is skipped entirely when not.)</summary>
    public static bool AnyForceTimeout
    {
        get { RefreshTimeoutGroups(); return _timeoutGroups.Count > 0 || _tracked.Count > 0; }
    }

    static void RefreshTimeoutGroups()
    {
        double now = Now;
        if (now - _timeoutGroupsAt < 2.0) return;
        _timeoutGroupsAt = now;
        _timeoutGroups.Clear();
        var map = Core.AbilityRules?.Current?.AbilityMap;
        if (map == null) return;
        foreach (var kv in map)
            if (kv.Value?.ForceTimeoutSeconds is float t && t > 0f)
            {
                int g = AbilityRules.ResolveAbilityGuid(kv.Key);
                if (g != 0) _timeoutGroups.Add(g);
            }
    }

    /// <summary>Force the forcetimeout rule set to refresh on the next check (after an edit/reload).</summary>
    public static void Invalidate() => _timeoutGroupsAt = -999;

    /// <summary>Record a player cast. Called for every AbilityCastStartedEvent.</summary>
    public static CastRecord RecordCast(Entity character, PrefabGUID group, Entity groupEntity)
    {
        if (!character.Exists() || !character.IsPlayer()) return null;
        ulong steamId = character.GetSteamId();
        if (steamId == 0 || group._Value == 0) return null;
        bool captured = Core.AbilityRegistry.HasCaptured(steamId, group._Value);
        string name = AbilityChainGraph.Name(group._Value);
        var rec = new CastRecord
        {
            CastId = ++_nextCastId,
            Group = group._Value,
            Time = Now,
            Captured = captured,
            ForceTimeout = captured ? Core.AbilityRules?.GetForceTimeoutSeconds(name) : null,
            GroupEntity = groupEntity,
            FromSlot = IsSlotState(character, groupEntity),
            // Transformed casts are scaled by the transform's own settings, never per-hit here.
            Damage = captured && Core.AbilityRegistry.GetActiveTransform(steamId) is null
                ? DamageScaler.SnapshotFor(name) : Logic.DamageSnapshot.Identity,
        };
        if (!_casts.TryGetValue(steamId, out var list)) _casts[steamId] = list = new List<CastRecord>();
        Prune(list, rec.Time);
        list.Add(rec);
        if (list.Count > MaxCastsPerPlayer)
        {
            int drop = list.Count - MaxCastsPerPlayer;
            double lastDropped = list[drop - 1].Time;
            list.RemoveRange(0, drop);
            _overflowUntil[steamId] = lastDropped + RetentionSeconds;
            if (Beelzebub.Config.Settings.VerboseLogging.Value)
                Core.Log.LogInfo($"[Beelz TIMEOUT] cast history for {steamId} over {MaxCastsPerPlayer} in {RetentionSeconds:0}s — attribution ambiguous until the window clears");
        }
        return rec;
    }

    /// <summary>The cast that just started for this player+group (newest record), or null.</summary>
    public static CastRecord Latest(ulong steamId, int group)
    {
        if (!_casts.TryGetValue(steamId, out var list)) return null;
        for (int i = list.Count - 1; i >= 0; i--) if (list[i].Group == group) return list[i];
        return null;
    }

    /// <summary>v0.133.0: is this ability-group instance one of the character's bar-slot state entities?</summary>
    static bool IsSlotState(Entity character, Entity groupEntity)
    {
        try
        {
            if (!groupEntity.Exists() || !Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(character)) return false;
            var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(character);
            for (int i = 0; i < slots.Length; i++)
            {
                Entity slotEnt = slots[i].GroupSlotEntity._Entity;
                if (!slotEnt.Exists() || !Core.EntityManager.HasComponent<AbilityGroupSlot>(slotEnt)) continue;
                if (Core.EntityManager.GetComponentData<AbilityGroupSlot>(slotEnt).StateEntity._Entity == groupEntity) return true;
            }
        }
        catch { /* unknown → not from a slot (fail safe: no cooldown enforcement) */ }
        return false;
    }

    /// <summary>
    /// v0.133.0: the player's retained casts whose group OWNS <paramref name="prefabGuid"/> (native included),
    /// oldest first, as attribution candidates. <paramref name="overflow"/> = the history was capped inside the
    /// retention window, so the list is incomplete (→ ambiguous).
    /// </summary>
    public static List<Logic.CastCandidate> OwnedCandidates(ulong steamId, int prefabGuid, out bool overflow, List<Logic.CastCandidate> into)
    {
        into.Clear();
        overflow = _overflowUntil.TryGetValue(steamId, out double until) && Now < until;
        foreach (var c in RecentCasts(steamId))
            if (AbilityChainGraph.IsOwner(prefabGuid, c.Group))
                into.Add(new Logic.CastCandidate(c.Group, c.Captured, c.Damage));
        return into;
    }

    static void Prune(List<CastRecord> list, double now)
    {
        int drop = 0;
        while (drop < list.Count && now - list[drop].Time > RetentionSeconds) drop++;
        if (drop > 0) list.RemoveRange(0, drop);
    }

    /// <summary>Retained casts for a player (newest last). Read-only view.</summary>
    public static IReadOnlyList<CastRecord> RecentCasts(ulong steamId)
    {
        if (!_casts.TryGetValue(steamId, out var list)) return Array.Empty<CastRecord>();
        Prune(list, Now);
        return list;
    }

    /// <summary>
    /// Attribute a chain prefab to exactly one of the player's retained casts: owners(prefab) ∩ retained cast
    /// groups (native included) must be ONE distinct group. Returns the newest matching cast, or null when
    /// there's no match or it's ambiguous. Never guesses by recency between different groups.
    /// </summary>
    public static CastRecord Attribute(ulong steamId, int prefabGuid, out bool ambiguous)
    {
        ambiguous = false;
        if (AbilityChainGraph.Owners(prefabGuid).Count == 0) return null;
        if (_overflowUntil.TryGetValue(steamId, out double until) && Now < until) { ambiguous = true; return null; }
        CastRecord match = null;
        int matchGroup = 0;
        foreach (var c in RecentCasts(steamId))
        {
            if (!AbilityChainGraph.IsOwner(prefabGuid, c.Group)) continue;
            if (matchGroup != 0 && matchGroup != c.Group) { ambiguous = true; return null; }
            matchGroup = c.Group;
            match = c;   // list is oldest→newest, so this ends on the newest cast of that group
        }
        return match;
    }

    /// <summary>Buff-spawn hook: start a forcetimeout timer on an attributed, indefinite buff instance.</summary>
    public static void OnBuffSpawned(Entity buffEntity)
    {
        if (!buffEntity.TryGetComponent<PrefabGUID>(out var pg)) return;
        int buffGuid = pg._Value;
        var owners = AbilityChainGraph.Owners(buffGuid);
        if (owners.Count == 0) return;
        bool relevant = false;
        foreach (int g in owners) if (_timeoutGroups.Contains(g)) { relevant = true; break; }
        if (!relevant) return;

        // Only buffs with no finite lifetime (the "stuck forever" class forcetimeout exists for).
        if (buffEntity.TryGetComponent<LifeTime>(out var lt) && lt.Duration > 0f && lt.EndAction != LifeTimeEndAction.None) return;

        Entity player = Patches.DealDamageSystemPatch.ResolveOwningPlayer(buffEntity);
        // No Buff.Target fallback: a boss-cast copy of the same buff landing ON the player must not be
        // attributed to the player's captured cast — only the owner chain proves who cast it.
        if (!player.Exists() || !player.IsPlayer()) return;
        ulong steamId = player.GetSteamId();
        if (steamId == 0) return;

        var cast = Attribute(steamId, buffGuid, out bool ambiguous);
        if (cast == null || !cast.Captured || cast.ForceTimeout is not float t || t <= 0f)
        {
            if (ambiguous)
            {
                SkippedAmbiguous++;
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz TIMEOUT] skip {AbilityChainGraph.Name(buffGuid)} for {steamId}: ambiguous (reached by more than one recent cast)");
            }
            return;
        }
        if (_tracked.ContainsKey((buffEntity.Index, buffEntity.Version))) return;   // already timing it — never extend
        if (_tracked.Count >= MaxTrackedBuffs) { Core.Log.LogWarning("[Beelz TIMEOUT] tracker full — not timing a new buff."); return; }
        _tracked[(buffEntity.Index, buffEntity.Version)] = new Tracked
        {
            Buff = buffEntity, SteamId = steamId, Group = cast.Group, BuffGuid = buffGuid, ExpireAt = Now + t,
        };
        if (Beelzebub.Config.Settings.VerboseLogging.Value)
            Core.Log.LogInfo($"[Beelz TIMEOUT] {AbilityChainGraph.Name(buffGuid)} ({AbilityChainGraph.Name(cast.Group)}) on {steamId} expires in {t:0.##}s");
    }

    static readonly List<(int, int)> _drop = new();

    /// <summary>Per-frame (HeartbeatBehaviour.Update): destroy timed buffs whose timeout passed.</summary>
    public static void Tick()
    {
        if (_tracked.Count == 0) return;
        double now = Now;
        _drop.Clear();
        foreach (var kv in _tracked)
        {
            var t = kv.Value;
            if (!t.Buff.Exists() || !t.Buff.TryGetComponent<PrefabGUID>(out var pg) || pg._Value != t.BuffGuid)
            { _drop.Add(kv.Key); continue; }   // already gone (or the index was reused)
            if (now < t.ExpireAt) continue;
            try
            {
                DestroyUtility.Destroy(Core.EntityManager, t.Buff, DestroyDebugReason.TryRemoveBuff);
                Expired++;
                if (Beelzebub.Config.Settings.VerboseLogging.Value)
                    Core.Log.LogInfo($"[Beelz TIMEOUT] expired {AbilityChainGraph.Name(t.BuffGuid)} ({AbilityChainGraph.Name(t.Group)}) on {t.SteamId}");
            }
            catch (Exception ex) { Core.Log.LogWarning($"[Beelz TIMEOUT] destroy failed: {ex.Message}"); }
            _drop.Add(kv.Key);
        }
        foreach (var k in _drop) _tracked.Remove(k);
    }

    /// <summary>Disconnect: forget the player's casts and timers.</summary>
    public static void OnDisconnect(ulong steamId)
    {
        _casts.Remove(steamId);
        _overflowUntil.Remove(steamId);
        _drop.Clear();
        foreach (var kv in _tracked) if (kv.Value.SteamId == steamId) _drop.Add(kv.Key);
        foreach (var k in _drop) _tracked.Remove(k);
    }

    public static int TrackedCount => _tracked.Count;
}
