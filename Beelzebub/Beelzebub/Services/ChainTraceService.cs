using System;
using System.Collections.Generic;
using ProjectM;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.25.0 — runtime chain-completion tracer.
///
/// The boss/V-Blood ability-chain audit is observation-driven (decided after
/// 8 versions of static-theory fixes failed and a single live observation —
/// "Fear lands but the AoE doesn't" — cracked the Mist Walk Nova). This
/// service is the observation engine.
///
/// When a transformed player casts an ability, <see cref="BeginTrace"/> opens
/// a short window for that player. Every entity that subsequently spawns and
/// whose owner-chain resolves back to that player is logged via
/// <see cref="Observe"/> — prefab name, team, and time-since-cast. The result
/// is a readable per-cast trace:
///
/// <code>
/// [Beelz TRACE] === cast AB_Foo_Bar by 765... — tracing 6s ===
/// [Beelz TRACE] 765... AB_Foo_Bar +12ms  → AB_Foo_Bar_ChannelBuff [buff] team=2
/// [Beelz TRACE] 765... AB_Foo_Bar +540ms → AB_Foo_Bar_ProxySpawner [spawn] team=2
/// [Beelz TRACE] 765... AB_Foo_Bar +560ms → AB_Foo_Bar_Projectile  [spawn] team=2
/// </code>
///
/// When the player reports "ability X half-works" we read its trace and see
/// exactly which link is missing (vs. what the boss's chain produces / what
/// the prefab says should be there). A missing payload → add a
/// <c>{trigger → payload}</c> entry to the curation map
/// (<see cref="Beelzebub.Patches.BuffSpawnServerPatch"/>'s
/// <c>TeleportDetonateTargets</c>), the same fix that resolved Mist Walk.
///
/// A team value still reading <c>1</c> (NPC) on a resolved-player entity is a
/// second red flag (team/collision class). Entirely log-only; no behavior.
/// </summary>
internal static class ChainTraceService
{
    sealed class Window
    {
        public string Ability;
        public DateTime Started;
        public readonly HashSet<Entity> Seen = new();
        public int Logged;
    }

    static readonly Dictionary<ulong, Window> _windows = new();
    static readonly TimeSpan TraceDuration = TimeSpan.FromSeconds(6);
    // Noise cap per cast — a single fan can spawn dozens of projectiles; we
    // only need to see that the payload class appeared, not every instance.
    const int MaxLoggedPerWindow = 50;

    static bool Enabled => Beelzebub.Config.Settings.VerboseLogging.Value;

    /// <summary>Open (or restart) a trace window for a transformed player's cast.</summary>
    public static void BeginTrace(ulong steamId, string abilityName)
    {
        if (!Enabled || steamId == 0) return;

        // Opportunistic cleanup of stale windows so the dict can't grow.
        if (_windows.Count > 16)
        {
            var now = DateTime.UtcNow;
            var stale = new List<ulong>();
            foreach (var (id, w) in _windows)
                if (now - w.Started > TraceDuration) stale.Add(id);
            foreach (var id in stale) _windows.Remove(id);
        }

        _windows[steamId] = new Window { Ability = abilityName, Started = DateTime.UtcNow };
        Core.Log.LogInfo($"[Beelz TRACE] === cast {abilityName} by {steamId} — tracing chain for {TraceDuration.TotalSeconds:0}s ===");
    }

    /// <summary>
    /// Record a spawned entity owned (via chain) by a transformed player.
    /// Call sites already resolve the owning player; they pass its steamId.
    /// <paramref name="kind"/> is a short tag for the spawn source ("buff" /
    /// "spawn"). Deduped per entity, capped per window.
    /// </summary>
    public static void Observe(Entity e, ulong steamId, string kind)
    {
        if (!Enabled || steamId == 0) return;
        if (!_windows.TryGetValue(steamId, out var w)) return;

        var age = DateTime.UtcNow - w.Started;
        if (age > TraceDuration) { _windows.Remove(steamId); return; }
        if (w.Logged >= MaxLoggedPerWindow) return;
        if (!e.Exists()) return;
        if (!w.Seen.Add(e)) return;

        string name;
        try { name = e.GetPrefabGuid().GetPrefabName() ?? "?"; }
        catch { name = "?"; }

        int team = -1;
        try { if (e.TryGetComponent<Team>(out var t)) team = t.Value; }
        catch { /* team optional */ }

        w.Logged++;
        Core.Log.LogInfo($"[Beelz TRACE] {steamId} {w.Ability} +{(int)age.TotalMilliseconds}ms → {name} [{kind}] team={team}");
    }
}
