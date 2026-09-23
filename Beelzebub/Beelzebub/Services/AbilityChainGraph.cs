using System;
using System.Collections.Generic;
using ProjectM;
using ProjectM.Gameplay.Scripting;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.132.0 (P1 step 1) — ONE ability chain graph, the single traversal used by `ability-inspect`,
/// shared-prefab detection, tuning and restore.
///
/// Built at init from EVERY ability group in the prefab collection (not just captured ones), so
/// bosses and unregistered abilities count when deciding "shared". Edges (decoded live):
///   group → <c>AbilityGroupStartAbilitiesBuffer</c> casts; any node → <c>AbilitySpawnPrefabOnCast</c>,
///   <c>AbilitySpawnPrefabOnStartCast</c>, <c>SpawnPrefabOnGameplayEvent</c>, <c>SpawnPrefabOnDestroy</c>,
///   <c>ApplyBuffOnGameplayEvent</c> Buff0–3, <c>ApplyKnockbackOnGameplayEvent.CustomKnockbackBuff</c>,
///   projectile fan / fan-on-tick / multishot / cluster, <c>SpawnMinionOnGameplayEvent</c> death buff.
/// Plus the long tail of scripted edges from the embedded <c>script_edges.tsv</c>
/// (generated offline from the prefab dump by <c>tools/build_script_edges.py</c>).
///
/// Bounded BFS (depth ≤ <see cref="MaxDepth"/>, visited set, cycle-safe). Completeness is tracked per
/// group: depth truncation or a decode failure marks the group INCOMPLETE, and every node reachable
/// from an incomplete group is treated as shared/unsafe for baked edits (no override).
///
/// Unit prefabs (<c>CHAR_*</c>) are leaves and are never baked-edit targets — this is what makes the
/// undecodable minion list inside <c>SpawnMinionOnGameplayEvent.BlobData</c> safe to leave undecoded
/// (its targets are always units).
/// </summary>
internal static class AbilityChainGraph
{
    public const int MaxDepth = 8;

    public enum EdgeKind
    {
        Root, StartAbility, SpawnOnCast, SpawnOnStartCast, SpawnOnEvent, SpawnOnDestroy, ApplyBuff,
        KnockbackBuff, ProjectileFan, MultiShot, Cluster, MinionDeathBuff, Script,
    }

    public sealed class Node
    {
        public int Guid;
        public int Depth;
        public int Parent;
        public EdgeKind Via;
        public bool IsUnit;
    }

    public sealed class Chain
    {
        public int Root;
        public readonly List<Node> Nodes = new();
        public readonly HashSet<int> NodeSet = new();
        public bool Complete = true;
        public readonly List<string> IncompleteReasons = new();
        public bool HasMinionSpawn;   // SpawnMinion blob list present (targets are units — never baked)
    }

    static readonly Dictionary<int, Chain> _groups = new();
    static readonly Dictionary<int, HashSet<int>> _owners = new();
    // v0.133.0: group + cast prefabs. Their INSTANCES are per-player ability state entities that live across
    // many casts, so damage attribution must be decided per event for them, never cached (rev 6 P2).
    static readonly HashSet<int> _persistentKinds = new();
    static Dictionary<int, List<(int tgt, string kind)>> _scriptEdges;
    static readonly Dictionary<string, int> _decodeFailures = new();
    static readonly HashSet<string> _failedKinds = new();
    static bool _scriptTableFailed;

    public static bool Built { get; private set; }
    public static int Generation { get; private set; }
    public static int GroupCount => _groups.Count;
    public static int IncompleteCount { get; private set; }
    public static int ScriptEdgeDrift { get; private set; }
    public static IReadOnlyDictionary<string, int> DecodeFailures => _decodeFailures;

    /// <summary>
    /// Component classes a failed decode could have produced unknown targets of (post-build inspection #1).
    /// An unresolved edge can point at a prefab that some OTHER group reaches normally and therefore looks
    /// "complete"; so a decode failure of an edge class disables baked edits to EVERY prefab of the class that
    /// edge produces, globally — fail closed, no override. "Any" = the edge can produce any prefab.
    /// </summary>
    public static readonly HashSet<string> UnsafeClasses = new();
    public static readonly List<string> UnsafeReasons = new();

    static string ProducedClass(string what) => what switch
    {
        "ApplyBuff" or "KnockbackBuff" or "SpawnMinion" => "Buff",
        "ProjectileFan" or "MultiShot" => "Projectile",
        _ => "Any",   // StartAbilities / SpawnOn* / Cluster / script table
    };

    /// <summary>Is this prefab in a class a failed decode could have produced? (then no baked edit.)</summary>
    public static bool IsInUnsafeClass(Entity prefab, out string reason)
    {
        reason = null;
        if (UnsafeClasses.Count == 0) return false;
        if (UnsafeClasses.Contains("Any")) { reason = "graph has unresolved edges (" + string.Join("; ", UnsafeReasons) + ")"; return true; }
        if (UnsafeClasses.Contains("Buff") && prefab.Has<Buff>()) { reason = "unresolved buff edges (" + string.Join("; ", UnsafeReasons) + ")"; return true; }
        if (UnsafeClasses.Contains("Projectile") && prefab.Has<Projectile>()) { reason = "unresolved projectile edges (" + string.Join("; ", UnsafeReasons) + ")"; return true; }
        return false;
    }

    /// <summary>(Re)build the whole graph from the live prefab collection. Never throws. Built into temporaries
    /// and published only on success; a failed build leaves <see cref="Built"/> false so baked tuning fails closed.</summary>
    public static void Build()
    {
        var started = DateTime.UtcNow;
        Built = false;   // published again only after the whole rebuild (table + graph) succeeds
        _decodeFailures.Clear();
        _failedKinds.Clear();
        var groups = new Dictionary<int, Chain>();
        var owners = new Dictionary<int, HashSet<int>>();
        var persistent = new HashSet<int>();
        int incomplete = 0;
        try
        {
            LoadScriptEdges();
            var em = Core.EntityManager;
            foreach (var kv in Core.PrefabNames)
            {
                if (!TryPrefab(kv.Key, out Entity e)) continue;
                if (!em.HasBuffer<AbilityGroupStartAbilitiesBuffer>(e)) continue;
                var chain = Walk(kv.Key);
                groups[kv.Key] = chain;
                if (!chain.Complete) incomplete++;
                foreach (var n in chain.Nodes)
                {
                    if (!owners.TryGetValue(n.Guid, out var set)) owners[n.Guid] = set = new HashSet<int>();
                    set.Add(kv.Key);
                    if (n.Via is EdgeKind.Root or EdgeKind.StartAbility) persistent.Add(n.Guid);
                }
            }
        }
        catch (Exception ex)
        {
            Built = false;
            Core.Log.LogError($"[Beelz GRAPH] build FAILED — baked ability tuning is disabled until a successful build: {ex.Message}");
            return;
        }
        _groups.Clear(); foreach (var kv in groups) _groups[kv.Key] = kv.Value;
        _owners.Clear(); foreach (var kv in owners) _owners[kv.Key] = kv.Value;
        _persistentKinds.Clear(); _persistentKinds.UnionWith(persistent);
        IncompleteCount = incomplete;
        UnsafeClasses.Clear();
        UnsafeReasons.Clear();
        foreach (var what in _failedKinds) { UnsafeClasses.Add(ProducedClass(what)); UnsafeReasons.Add($"decode {what} failed"); }
        if (_scriptTableFailed) { UnsafeClasses.Add("Any"); UnsafeReasons.Add("script-edge table missing/unreadable"); }
        if (ScriptEdgeDrift > 0) { UnsafeClasses.Add("Any"); UnsafeReasons.Add($"script-edge table stale ({ScriptEdgeDrift} source prefab(s) gone — game update? re-run tools/build_script_edges.py)"); }
        Built = true;
        Generation++;
        Core.Log.LogInfo($"[Beelz GRAPH] built: {_groups.Count} ability groups, {_owners.Count} chain prefabs, "
            + $"{IncompleteCount} incomplete group(s), script-edge drift {ScriptEdgeDrift}, "
            + $"{(DateTime.UtcNow - started).TotalMilliseconds:F0}ms (gen {Generation}).");
        if (_decodeFailures.Count > 0)
            Core.Log.LogWarning("[Beelz GRAPH] decode failures: " + string.Join(", ", DescribeFailures()));
        if (UnsafeClasses.Count > 0)
            Core.Log.LogWarning($"[Beelz GRAPH] baked edits DISABLED for class(es) {string.Join(",", UnsafeClasses)}: {string.Join("; ", UnsafeReasons)}");
    }

    public static IEnumerable<string> DescribeFailures()
    {
        foreach (var kv in _decodeFailures) yield return $"{kv.Key}×{kv.Value}";
    }

    /// <summary>The chain of an ability group (null if the GUID is not a group).</summary>
    /// <summary>Every ability-group GUID in the graph, ascending (deterministic).</summary>
    public static List<int> AllGroups()
    {
        var l = new List<int>(_groups.Keys);
        l.Sort();
        return l;
    }

    public static Chain GetGroup(int groupGuid) => _groups.TryGetValue(groupGuid, out var c) ? c : null;

    /// <summary>The chain rooted at ANY prefab (a group from the cache, or a fresh walk for a non-group key).</summary>
    public static Chain GetOrWalk(int rootGuid) => GetGroup(rootGuid) ?? Walk(rootGuid);

    /// <summary>Is this prefab a chain node at all (reached by some ability group)?</summary>
    public static bool IsNode(int prefabGuid) => _owners.ContainsKey(prefabGuid);

    /// <summary>v0.133.0: group or cast prefab (instances are long-lived per-player state → per-event attribution).</summary>
    public static bool IsPersistentKind(int prefabGuid) => _persistentKinds.Contains(prefabGuid);

    /// <summary>Every ability group whose chain reaches this prefab.</summary>
    public static IReadOnlyCollection<int> Owners(int prefabGuid)
        => _owners.TryGetValue(prefabGuid, out var s) ? s : (IReadOnlyCollection<int>)Array.Empty<int>();

    /// <summary>Does <paramref name="group"/>'s chain reach this prefab?</summary>
    public static bool IsOwner(int prefabGuid, int group)
        => _owners.TryGetValue(prefabGuid, out var s) && s.Contains(group);

    /// <summary>True when every owning group's chain was fully decoded (no truncation / decode failure).</summary>
    public static bool IsOwnershipComplete(int prefabGuid)
    {
        foreach (int g in Owners(prefabGuid))
            if (_groups.TryGetValue(g, out var c) && !c.Complete) return false;
        return true;
    }

    /// <summary>Shared = reached by more than one group, or by any incomplete group.</summary>
    public static bool IsShared(int prefabGuid)
        => Owners(prefabGuid).Count > 1 || !IsOwnershipComplete(prefabGuid);

    /// <summary>Groups other than <paramref name="self"/> that reach this prefab.</summary>
    public static List<int> OtherOwners(int prefabGuid, int self)
    {
        var list = new List<int>();
        foreach (int g in Owners(prefabGuid)) if (g != self) list.Add(g);
        list.Sort();
        return list;
    }

    /// <summary>Bounded BFS from one root. Decode failures and depth truncation mark the chain incomplete.</summary>
    public static Chain Walk(int rootGuid)
    {
        var chain = new Chain { Root = rootGuid };
        var queue = new Queue<Node>();
        var root = new Node { Guid = rootGuid, Depth = 0, Parent = 0, Via = EdgeKind.Root, IsUnit = IsUnitGuid(rootGuid) };
        chain.Nodes.Add(root);
        chain.NodeSet.Add(rootGuid);
        queue.Enqueue(root);
        var edges = new List<(int tgt, EdgeKind kind)>();
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            if (n.IsUnit) continue;   // units are leaves (and never baked targets)
            if (!TryPrefab(n.Guid, out Entity e)) continue;   // dangling GUID: nothing to follow
            edges.Clear();
            CollectEdges(e, n.Guid, edges, chain);
            foreach (var (tgt, kind) in edges)
            {
                if (tgt == 0 || chain.NodeSet.Contains(tgt)) continue;
                if (n.Depth + 1 > MaxDepth)
                {
                    if (chain.Complete) chain.IncompleteReasons.Add($"depth>{MaxDepth} at {Name(n.Guid)}");
                    chain.Complete = false;
                    continue;
                }
                var child = new Node { Guid = tgt, Depth = n.Depth + 1, Parent = n.Guid, Via = kind, IsUnit = IsUnitGuid(tgt) };
                chain.Nodes.Add(child);
                chain.NodeSet.Add(tgt);
                queue.Enqueue(child);
            }
        }
        return chain;
    }

    static void CollectEdges(Entity e, int guid, List<(int, EdgeKind)> edges, Chain chain)
    {
        var em = Core.EntityManager;
        Try("StartAbilities", chain, () =>
        {
            if (!em.HasBuffer<AbilityGroupStartAbilitiesBuffer>(e)) return;
            var b = em.GetBuffer<AbilityGroupStartAbilitiesBuffer>(e);
            for (int i = 0; i < b.Length; i++) edges.Add((b[i].PrefabGUID._Value, EdgeKind.StartAbility));
        });
        Try("SpawnOnCast", chain, () =>
        {
            if (!em.HasBuffer<AbilitySpawnPrefabOnCast>(e)) return;
            var b = em.GetBuffer<AbilitySpawnPrefabOnCast>(e);
            for (int i = 0; i < b.Length; i++) edges.Add((b[i].SpawnPrefab._Value, EdgeKind.SpawnOnCast));
        });
        Try("SpawnOnStartCast", chain, () =>
        {
            if (!em.HasBuffer<AbilitySpawnPrefabOnStartCast>(e)) return;
            var b = em.GetBuffer<AbilitySpawnPrefabOnStartCast>(e);
            for (int i = 0; i < b.Length; i++) edges.Add((b[i].SpawnPrefab._Value, EdgeKind.SpawnOnStartCast));
        });
        Try("SpawnOnEvent", chain, () =>
        {
            if (!em.HasBuffer<SpawnPrefabOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<SpawnPrefabOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++) edges.Add((b[i].SpawnPrefab._Value, EdgeKind.SpawnOnEvent));
        });
        Try("SpawnOnDestroy", chain, () =>
        {
            if (e.TryGetComponent<SpawnPrefabOnDestroy>(out var d)) edges.Add((d.SpawnPrefab._Value, EdgeKind.SpawnOnDestroy));
        });
        Try("ApplyBuff", chain, () =>
        {
            if (!em.HasBuffer<ApplyBuffOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<ApplyBuffOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++)
            {
                edges.Add((b[i].Buff0._Value, EdgeKind.ApplyBuff));
                edges.Add((b[i].Buff1._Value, EdgeKind.ApplyBuff));
                edges.Add((b[i].Buff2._Value, EdgeKind.ApplyBuff));
                edges.Add((b[i].Buff3._Value, EdgeKind.ApplyBuff));
            }
        });
        Try("KnockbackBuff", chain, () =>
        {
            if (!em.HasBuffer<ApplyKnockbackOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<ApplyKnockbackOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++) edges.Add((b[i].CustomKnockbackBuff._Value, EdgeKind.KnockbackBuff));
        });
        Try("SpawnMinion", chain, () =>
        {
            if (!em.HasBuffer<SpawnMinionOnGameplayEvent>(e)) return;
            chain.HasMinionSpawn = true;   // blob minion list: targets are units (never baked) — rule (a)
            var b = em.GetBuffer<SpawnMinionOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++) edges.Add((b[i].MasterDeathBuffPrefabGuid._Value, EdgeKind.MinionDeathBuff));
        });
        Try("ProjectileFan", chain, () =>
        {
            if (e.TryGetComponent<AbilityProjectileFanOnGameplayEvent_DataServer>(out var f))
            {
                edges.Add((f.NewProjectileEntity._Value, EdgeKind.ProjectileFan));
                edges.Add((f.NewProjectileEntityAlternate._Value, EdgeKind.ProjectileFan));
                edges.Add((f.BoostBuffType._Value, EdgeKind.ProjectileFan));
                edges.Add((f.BoostPerStackPrefab1._Value, EdgeKind.ProjectileFan));
                edges.Add((f.BoostPerStackPrefab2._Value, EdgeKind.ProjectileFan));
                edges.Add((f.BoostPerStackPrefab3._Value, EdgeKind.ProjectileFan));
            }
            if (e.TryGetComponent<AbilityProjectileFanOnTick_DataServer>(out var t))
            {
                edges.Add((t.NewProjectileEntity._Value, EdgeKind.ProjectileFan));
                edges.Add((t.NewProjectileEntityAlternate._Value, EdgeKind.ProjectileFan));
            }
        });
        Try("MultiShot", chain, () =>
        {
            if (e.TryGetComponent<Script_MultiShot_Cast_DataServer>(out var m)) edges.Add((m.NewProjectile._Value, EdgeKind.MultiShot));
        });
        Try("Cluster", chain, () =>
        {
            if (e.TryGetComponent<EvenSpreadCluster_DataServer>(out var c)) edges.Add((c.NewThrowEntity._Value, EdgeKind.Cluster));
        });
        if (_scriptEdges != null && _scriptEdges.TryGetValue(guid, out var extra))
            foreach (var (tgt, _) in extra) edges.Add((tgt, EdgeKind.Script));
    }

    static void Try(string what, Chain chain, Action a)
    {
        try { a(); }
        catch (Exception)
        {
            _decodeFailures[what] = _decodeFailures.TryGetValue(what, out int n) ? n + 1 : 1;
            _failedKinds.Add(what);
            if (chain.Complete || chain.IncompleteReasons.Count < 4) chain.IncompleteReasons.Add($"decode {what} failed");
            chain.Complete = false;
        }
    }

    /// <summary>The script-table edge kinds leaving a prefab (for inspect).</summary>
    public static IEnumerable<(int tgt, string kind)> ScriptEdgesFrom(int guid)
    {
        if (_scriptEdges != null && _scriptEdges.TryGetValue(guid, out var list))
            foreach (var x in list) yield return x;
    }

    static void LoadScriptEdges()
    {
        if (_scriptEdges != null && !_scriptTableFailed) { ScriptEdgeDrift = CountDrift(); return; }
        _scriptTableFailed = false;
        _scriptEdges = new Dictionary<int, List<(int, string)>>();
        const string resource = "Beelzebub.Resources.script_edges.tsv";
        try
        {
            using var stream = typeof(AbilityChainGraph).Assembly.GetManifestResourceStream(resource);
            if (stream is null) { _scriptTableFailed = true; Core.Log.LogWarning($"[Beelz GRAPH] embedded '{resource}' missing — scripted edges not decoded."); return; }
            using var reader = new System.IO.StreamReader(stream);
            string line;
            int rows = 0, expected = -1, lineNo = 0;
            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    if (line.StartsWith("# edges=", StringComparison.Ordinal)) int.TryParse(line.Substring(8), out expected);
                    continue;
                }
                var parts = line.Split('\t');
                if (parts.Length < 3 || !int.TryParse(parts[0], out int src) || !int.TryParse(parts[1], out int tgt) || parts[2].Length == 0)
                {
                    // Integrity (post-build inspection r2 #4): a malformed row means a corrupt table — fail closed.
                    _scriptTableFailed = true;
                    Core.Log.LogWarning($"[Beelz GRAPH] script-edge table corrupt at line {lineNo} — treating the table as failed.");
                    return;
                }
                if (!_scriptEdges.TryGetValue(src, out var list)) _scriptEdges[src] = list = new List<(int, string)>();
                list.Add((tgt, parts[2]));
                rows++;
            }
            if (expected < 0 || rows != expected || rows == 0)
            {
                _scriptTableFailed = true;
                Core.Log.LogWarning($"[Beelz GRAPH] script-edge table integrity check failed (rows {rows}, header edges={expected}) — treating the table as failed.");
                return;
            }
        }
        catch (Exception ex) { _scriptTableFailed = true; Core.Log.LogWarning($"[Beelz GRAPH] script-edge table load failed: {ex.Message}"); }
        ScriptEdgeDrift = CountDrift();
    }

    // Table sources that no longer exist in the live game (a patch changed the prefabs) → re-run the tool.
    static int CountDrift()
    {
        int drift = 0;
        foreach (var src in _scriptEdges.Keys) if (!TryPrefab(src, out _)) drift++;
        return drift;
    }

    public static bool IsUnitGuid(int guid)
    {
        string n = Name(guid);
        return n.StartsWith("CHAR_", StringComparison.Ordinal);
    }

    public static string Name(int guid)
        => Core.PrefabNames.TryGetValue(guid, out var n) && !string.IsNullOrEmpty(n) ? n : guid.ToString();

    static bool TryPrefab(int guid, out Entity e)
    {
        e = Entity.Null;
        return guid != 0
            && Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(guid), out e)
            && e.Exists();
    }
}
