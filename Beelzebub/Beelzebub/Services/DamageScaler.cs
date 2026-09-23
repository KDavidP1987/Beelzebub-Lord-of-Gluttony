using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Beelzebub.Logic;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.133.0 (P2) — per-hit damage for CAPTURED casts, driven from <c>DealDamageSystemPatch</c>.
///
/// Each <c>DealDamageEvent</c> is traced to the cast that caused it: its SpellSource's owner chain must reach a
/// player (≤ 8 hops, cycle-safe), the source prefab must be a node of the ability chain graph, and
/// owners(prefab) ∩ that player's retained casts must be exactly ONE captured group with ONE consistent
/// snapshot (<see cref="AttributionRule"/>). Anything else stays vanilla and is counted, never guessed.
///
/// <c>Damage_Mode</c>: Off = nothing here runs (legacy power window only). Telemetry = attribute + count + optional
/// truth-table trace, never mutate. Scale = mutate the power term (MainFactor; and RawDamage under
/// FlatPolicy=Scaled) of attributed hits whose cast took the per-hit path. The write is a modified COPY of the
/// event set with SetComponentData (no raw pointers). A sampled read-back probe trips a global circuit breaker on
/// any mismatch/exception → effective mode drops to Telemetry, logged once.
/// </summary>
internal static class DamageScaler
{
    // ---- mode + breaker ----------------------------------------------------------------------------------
    static bool _breaker;
    static string _breakerReason = "";
    static long _writes;
    static long _verifyFailures;
    static bool _boundsWarned;

    public static DamageMode ConfiguredMode => DamageMath.ParseMode(Beelzebub.Config.Settings.Damage_Mode?.Value);
    public static DamageMode EffectiveMode
    {
        get
        {
            var m = _breaker && ConfiguredMode == DamageMode.Scale ? DamageMode.Telemetry : ConfiguredMode;
            // Every mode change restarts write-probe sampling, so a return to Scale re-verifies its first writes.
            if (m != _lastMode) { _lastMode = m; _writes = 0; }
            return m;
        }
    }
    static DamageMode _lastMode = DamageMode.Off;
    public static bool BreakerTripped => _breaker;
    public static string BreakerReason => _breakerReason;
    public static bool Trace { get; set; }

    static void Trip(string reason)
    {
        if (_breaker) return;
        _breaker = true;
        _breakerReason = reason;
        Core.Log.LogError($"[Beelz DMG] per-hit scaling DISABLED for this session ({reason}). Damage stays vanilla for " +
            "captured casts; Damage_Mode behaves as Telemetry. Report this with the log — see docs/ABILITY_CONFIG.md.");
    }

    /// <summary>Snapshot for a captured cast starting now (called by CastHistoryService.RecordCast).</summary>
    public static DamageSnapshot SnapshotFor(string abilityName)
    {
        var mode = EffectiveMode;
        string pm = Beelzebub.Config.Settings.Grant_PowerScalingMode?.Value ?? "";
        bool boosted = pm.Trim().Equals("Boosted", StringComparison.OrdinalIgnoreCase);
        float scale = DamageMath.EffectiveScale(1f, Core.AbilityRules?.GetDamageScale(abilityName) ?? 1f, boosted,
            Beelzebub.Config.Settings.Grant_PowerScalingFactor?.Value ?? 1f);
        var (min, max, invalid) = DamageMath.NormalizeBounds(Beelzebub.Config.Settings.Damage_MinFactor?.Value ?? 0f,
            Beelzebub.Config.Settings.Damage_MaxFactor?.Value ?? 0f);
        if (invalid && !_boundsWarned)
        {
            _boundsWarned = true;
            Core.Log.LogWarning("[Beelz DMG] Damage_MinFactor > Damage_MaxFactor — both bounds ignored until fixed.");
        }
        return new DamageSnapshot(scale, min, max, DamageMath.ParseFlat(Beelzebub.Config.Settings.Damage_FlatPolicy?.Value),
            PerHit: mode == DamageMode.Scale);
    }

    // ---- counters -------------------------------------------------------------------------------------------
    public sealed class Counters
    {
        public long Attributed, Ambiguous, Unmatched, Scaled, WouldScale;
    }

    static readonly Dictionary<int, Counters> _counters = new();
    public static long Unsupported { get; private set; }   // source = the character itself / not a graph node
    public static long Truncated { get; private set; }     // owner chain longer than 8 hops
    public static long CacheSaturated { get; private set; }
    public static long Writes => _writes;
    public static long VerifyFailures => _verifyFailures;
    public static IReadOnlyDictionary<int, Counters> AllCounters => _counters;

    static Counters C(int group)
    {
        if (!_counters.TryGetValue(group, out var c)) _counters[group] = c = new Counters();
        return c;
    }

    /// <summary>"reliable" / "unreliable" (ambiguous+unmatched &gt; 20% over ≥ 50 hits) / "unknown" (too few hits).</summary>
    public static string Reliability(int group)
    {
        if (!_counters.TryGetValue(group, out var c)) return "unknown";
        long bad = c.Ambiguous + c.Unmatched, total = c.Attributed + bad;
        if (total < 50) return "unknown";
        return bad * 5 > total ? "unreliable" : "reliable";
    }

    public static void ResetCounters()
    {
        _counters.Clear();
        Unsupported = Truncated = CacheSaturated = 0;
    }

    // ---- provenance cache (ephemeral chain entities only) ---------------------------------------------------
    const int CacheCap = 4096;
    static readonly Dictionary<(int, int), (int group, DamageSnapshot snap)> _cache = new();
    static readonly List<CastCandidate> _scratch = new();
    static double _lastPrune;

    /// <summary>Drop cache entries whose entity is gone (called from the damage prefix, throttled).</summary>
    static void PruneCache()
    {
        double now = CastHistoryService.Now;
        if (now - _lastPrune < 5.0 || _cache.Count == 0) return;
        _lastPrune = now;
        List<(int, int)> dead = null;
        foreach (var kv in _cache)
        {
            var e = new Entity { Index = kv.Key.Item1, Version = kv.Key.Item2 };
            if (!e.Exists()) (dead ??= new()).Add(kv.Key);
        }
        if (dead != null) foreach (var k in dead) _cache.Remove(k);
    }

    // ---- truth-table probe (Telemetry/trace) ---------------------------------------------------------------
    struct HpProbe { public Entity Target; public float Before; public int Events; public float ExpectedRatio; public int Group; public string Path; public bool Mixed; }
    static readonly Dictionary<Entity, HpProbe> _probes = new();

    /// <summary>Owner walk (≤ 8 hops, visited set). <paramref name="truncated"/> when the bound was hit.</summary>
    public static Entity ResolveOwningPlayer(Entity start, out bool truncated)
    {
        truncated = false;
        Entity current = start;
        Span<Entity> seen = stackalloc Entity[8];
        for (int hop = 0; hop < 8; hop++)
        {
            if (!current.Exists()) return Entity.Null;
            if (current.IsPlayer()) return current;
            for (int k = 0; k < hop; k++) if (seen[k] == current) return Entity.Null;   // any cycle
            seen[hop] = current;
            if (!current.TryGetComponent<EntityOwner>(out var eo)) return Entity.Null;
            current = eo.Owner;
        }
        truncated = true;
        return Entity.Null;
    }

    /// <summary>Remember a vanilla first-sight verdict so an ephemeral source can't become attributed later
    /// (e.g. after older casts age out of retention).</summary>
    static void CacheVanilla(bool persistent, (int, int) key)
    {
        if (persistent) return;
        if (_cache.Count < CacheCap) _cache[key] = (0, DamageSnapshot.Identity);
        else CacheSaturated++;
    }

    /// <summary>Per damage event (prefix). No-op in Off mode (the caller checks).</summary>
    public static void Process(Entity eventEntity, DamageMode mode)
    {
        if (!eventEntity.TryGetComponent<DealDamageEvent>(out var evt)) return;
        int mainType = (int)evt.MainType;
        if (!DamageMath.IsPowerTyped(mainType)) return;
        Entity source = evt.SpellSource;
        if (!source.Exists()) return;

        Entity player = ResolveOwningPlayer(source, out bool truncated);
        if (truncated) { Truncated++; return; }
        if (!player.Exists()) return;                         // not a player's hit — none of our business
        if (source == player) { Unsupported++; return; }       // no correlation key (character-sourced)

        int prefab = source.GetPrefabGuid()._Value;
        if (!AbilityChainGraph.IsNode(prefab)) { Unsupported++; return; }
        ulong steamId = player.GetSteamId();
        if (steamId == 0) return;

        PruneCache();
        bool persistent = AbilityChainGraph.IsPersistentKind(prefab);
        var key = (source.Index, source.Version);
        int group;
        DamageSnapshot snap;
        if (!persistent && _cache.TryGetValue(key, out var cached))
        {
            (group, snap) = cached;
            if (group == 0) return;   // first sight was ambiguous/unmatched/native → stays vanilla for life
        }
        else
        {
            CastHistoryService.OwnedCandidates(steamId, prefab, out bool overflow, _scratch);
            var outcome = AttributionRule.Decide(_scratch, overflow, out int idx);
            switch (outcome)
            {
                case AttributionOutcome.Attributed:
                    group = _scratch[idx].Group;
                    snap = _scratch[idx].Snapshot;
                    if (!persistent)
                    {
                        if (_cache.Count < CacheCap) _cache[key] = (group, snap);
                        else CacheSaturated++;   // never evict a live entry; this entity is re-attributed per event
                    }
                    break;
                case AttributionOutcome.Ambiguous:
                    CacheVanilla(persistent, key);
                    foreach (var c in _scratch) if (c.Captured) C(c.Group).Ambiguous++;
                    if (Trace) Core.Log.LogInfo($"[Beelz DMG] ambiguous {AbilityChainGraph.Name(prefab)} for {steamId} — vanilla");
                    return;
                case AttributionOutcome.Unmatched:
                    CacheVanilla(persistent, key);
                    foreach (int g in AbilityChainGraph.Owners(prefab))
                        if (Core.AbilityRegistry.HasCaptured(steamId, g)) C(g).Unmatched++;
                    return;
                default:
                    CacheVanilla(persistent, key);
                    return;   // native ability — vanilla, not ours
            }
        }

        var counters = C(group);
        counters.Attributed++;
        bool changes = DamageMath.TryComputeHit(mainType, evt.MainFactor, evt.RawDamage, snap, out float newFactor, out float newRaw);
        string path = snap.PerHit ? "perhit" : (DamageMath.IsIdentity(snap) ? "none" : "window");
        if (changes) counters.WouldScale++;

        bool mutate = changes && mode == DamageMode.Scale && snap.PerHit && !_breaker;
        if (Trace || mode == DamageMode.Telemetry)
        {
            Core.Log.LogInfo($"[Beelz DMG] {(mutate ? "SCALE" : "trace")} {AbilityChainGraph.Name(group)} src={AbilityChainGraph.Name(prefab)} " +
                $"evt={eventEntity.Index}:{eventEntity.Version} type={mainType} factor={evt.MainFactor:0.###}->{newFactor:0.###} raw={evt.RawDamage:0.#}->{newRaw:0.#} " +
                $"pct={evt.RawDamagePercent:0.###} mod={evt.Modifier:0.###} res={evt.ResourceModifier:0.###} path={path} player={steamId}");
            RecordProbe(evt.Target, group, evt.MainFactor > 0f ? newFactor / evt.MainFactor : 1f, path);
        }
        if (!mutate) return;

        try
        {
            var copy = evt;
            Unsafe.AsRef(in copy.MainFactor) = newFactor;
            Unsafe.AsRef(in copy.RawDamage) = newRaw;
            Core.EntityManager.SetComponentData(eventEntity, copy);
            long n = _writes++;
            counters.Scaled++;
            if (DamageMath.ShouldVerify(n))
            {
                var back = Core.EntityManager.GetComponentData<DealDamageEvent>(eventEntity);
                if (Math.Abs(back.MainFactor - newFactor) > 1e-4f || Math.Abs(back.RawDamage - newRaw) > 1e-3f)
                {
                    _verifyFailures++;
                    Trip($"read-back mismatch on {AbilityChainGraph.Name(group)}: wrote {newFactor}, read {back.MainFactor}");
                }
            }
        }
        catch (Exception ex)
        {
            _verifyFailures++;
            Trip("write threw: " + ex.Message);
        }
    }

    static void RecordProbe(Entity target, int group, float ratio, string path)
    {
        if (!target.Exists() || !target.TryGetComponent<Health>(out var h)) return;
        if (_probes.TryGetValue(target, out var p))
        {
            p.Events++;
            // Several hits on one target in one update: only report a single ratio when they all agree.
            if (p.Group != group || p.Path != path || Math.Abs(p.ExpectedRatio - ratio) > 1e-4f) p.Mixed = true;
            _probes[target] = p;
            return;
        }
        _probes[target] = new HpProbe { Target = target, Before = h.Value, Events = 1, ExpectedRatio = ratio, Group = group, Path = path };
    }

    /// <summary>Postfix: report each probed target's HP drop across this system update (observational only).</summary>
    public static void FlushProbes()
    {
        if (_probes.Count == 0) return;
        foreach (var p in _probes.Values)
        {
            float after = p.Target.Exists() && p.Target.TryGetComponent<Health>(out var h) ? h.Value : float.NaN;
            Core.Log.LogInfo($"[Beelz DMG] observational {AbilityChainGraph.Name(p.Group)} target={p.Target.GetPrefabGuid().GetPrefabName()} " +
                $"hp {p.Before:0.#}->{after:0.#} (drop {p.Before - after:0.#}) events={p.Events} " +
                (p.Mixed ? "MIXED (hits from different casts/paths — see the per-hit lines above)" : $"factorRatio={p.ExpectedRatio:0.###} path={p.Path}"));
        }
        _probes.Clear();
    }

    // ---- reporting ----------------------------------------------------------------------------------------
    public static string Describe(int group)
    {
        var c = _counters.TryGetValue(group, out var x) ? x : new Counters();
        return $"damage: mode={EffectiveMode}{(_breaker ? " (breaker: " + _breakerReason + ")" : "")} attributed={c.Attributed} " +
            $"ambiguous={c.Ambiguous} unmatched={c.Unmatched} would-scale={c.WouldScale} scaled={c.Scaled} attribution={Reliability(group)}";
    }

    public static string Summary()
    {
        var sb = new StringBuilder();
        sb.Append($"Damage_Mode={ConfiguredMode} effective={EffectiveMode}");
        if (_breaker) sb.Append($" BREAKER: {_breakerReason}");
        sb.Append($" | writes={_writes} verify-fail={_verifyFailures} unsupported={Unsupported} truncated={Truncated} cache={_cache.Count}/{CacheCap} saturated={CacheSaturated} trace={(Trace ? "on" : "off")}");
        return sb.ToString();
    }

    /// <summary>Disconnect / reload hygiene.</summary>
    public static void Clear() { _cache.Clear(); _probes.Clear(); }
}
