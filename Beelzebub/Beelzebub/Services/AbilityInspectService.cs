using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using ProjectM;
using ProjectM.Gameplay.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.132.0 (P1 step 2) — read-only view of every tunable number in an ability's chain, including the hidden
/// damage terms. Backs <c>.beelz admin ability-inspect &lt;ability&gt; [export]</c>.
///
/// Walks <see cref="AbilityChainGraph"/> and reads, per prefab: cooldown, cast time, charges, max range,
/// projectile speed/range, LifeTime, TargetAoE, HitColliderCast (radius, target count), DealDamage parameters,
/// heals, applied-buff duration/stacks, knockback, Buff.MaxStacks, projectile-fan count, minion count and
/// SpellMod targets — plus "shared with" (other ability groups reaching the prefab) and warnings for damage
/// terms a power-relative damage scale would NOT scale (flat, %-of-HP).
///
/// <c>export</c> writes EVERY ability to <c>BepInEx/config/kdpen.Beelzebub/inspect_export.csv</c>: invariant
/// culture, RFC-4180 quoting, deterministic order (group GUID, then walk order), a status/error column per
/// row, written to a temp file and atomically swapped in. If the required DealDamage decode fails on any row
/// the export is reported FAILED rather than a plausible-but-incomplete file.
/// </summary>
internal static class AbilityInspectService
{
    public static readonly string[] Columns =
    {
        "group", "group_guid", "node", "node_guid", "depth", "via", "shared_with",
        "cooldown", "cast_time", "post_cast", "charges", "charge_time", "max_range",
        "proj_speed", "proj_range", "lifetime", "aoe_range", "hit_radius", "hit_targets",
        "dmg_type", "dmg_main", "dmg_raw", "dmg_pct", "dmg_resource", "dmg_per_hit",
        "heal_flat", "heal_pct", "heal_per_sp", "buff_duration", "buff_stacks",
        "knockback_range", "knockback_duration", "buff_maxstacks", "fan_count", "minion_count",
        "spellmod_targets", "tuned_orig", "warnings", "status", "error",
    };

    public sealed class Row
    {
        public readonly Dictionary<string, string> V = new();
        public readonly List<string> Warnings = new();
        public readonly List<string> Errors = new();
        public bool DamageDecodeFailed;
        public string Get(string c) => V.TryGetValue(c, out var v) ? v : "";
    }

    static string N(float f) => float.IsFinite(f) ? f.ToString("0.###", CultureInfo.InvariantCulture) : "NaN";
    static string N(double f) => N((float)f);
    static void Add(Row r, string col, string val)
    {
        if (string.IsNullOrEmpty(val)) return;
        r.V[col] = r.V.TryGetValue(col, out var old) && old.Length > 0 ? old + "|" + val : val;
    }

    /// <summary>Decode one chain node. Never throws; per-component failures land in Row.Errors.</summary>
    public static Row ReadNode(AbilityChainGraph.Chain chain, AbilityChainGraph.Node node)
    {
        var r = new Row();
        r.V["group"] = AbilityChainGraph.Name(chain.Root);
        r.V["group_guid"] = chain.Root.ToString(CultureInfo.InvariantCulture);
        r.V["node"] = AbilityChainGraph.Name(node.Guid);
        r.V["node_guid"] = node.Guid.ToString(CultureInfo.InvariantCulture);
        r.V["depth"] = node.Depth.ToString(CultureInfo.InvariantCulture);
        r.V["via"] = node.Via.ToString();
        var others = AbilityChainGraph.OtherOwners(node.Guid, chain.Root);
        r.V["shared_with"] = others.Count.ToString(CultureInfo.InvariantCulture);
        if (node.IsUnit) { r.V["status"] = "unit"; return r; }
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(new PrefabGUID(node.Guid), out Entity e) || !e.Exists())
        { r.V["status"] = "missing"; return r; }
        var em = Core.EntityManager;

        void Try(string what, Action a, bool required = false)
        {
            try { a(); }
            catch (Exception ex)
            {
                r.Errors.Add($"{what}: {ex.Message}");
                if (required) r.DamageDecodeFailed = true;
            }
        }

        Try("cooldown", () => { if (e.TryGetComponent<AbilityCooldownData>(out var c)) Add(r, "cooldown", N(c.Cooldown._Value)); });
        Try("casttime", () =>
        {
            if (e.TryGetComponent<AbilityCastTimeData>(out var c))
            { Add(r, "cast_time", N(c.MaxCastTime._Value)); Add(r, "post_cast", N(c.PostCastTime._Value)); }
        });
        Try("charges", () =>
        {
            if (e.TryGetComponent<AbilityChargesData>(out var c))
            { Add(r, "charges", c.MaxCharges.ToString(CultureInfo.InvariantCulture)); Add(r, "charge_time", N(c.ChargeUpTime._Value)); }
        });
        Try("groupinfo", () => { if (e.TryGetComponent<AbilityGroupInfo>(out var c)) Add(r, "max_range", N(c.MaxRange)); });
        Try("projectile", () =>
        {
            if (e.TryGetComponent<Projectile>(out var c)) { Add(r, "proj_speed", N(c.Speed)); Add(r, "proj_range", N(c.Range)); }
        });
        Try("lifetime", () =>
        {
            if (e.TryGetComponent<LifeTime>(out var c))
                Add(r, "lifetime", c.Duration > 0f && c.EndAction != LifeTimeEndAction.None ? N(c.Duration) : "indefinite");
            else if (e.Has<Buff>()) Add(r, "lifetime", "indefinite");
        });
        Try("targetaoe", () => { if (e.TryGetComponent<TargetAoE>(out var c)) Add(r, "aoe_range", N(c.MaxRange)); });
        Try("hitcollider", () =>
        {
            if (!em.HasBuffer<HitColliderCast>(e)) return;
            var b = em.GetBuffer<HitColliderCast>(e);
            for (int i = 0; i < b.Length; i++)
            {
                Add(r, "hit_radius", N(b[i].Shape.RadiusOrWidth));
                Add(r, "hit_targets", b[i].PrimaryTargets_Count.ToString(CultureInfo.InvariantCulture));
            }
        });
        Try("dealdamage", () =>
        {
            if (!em.HasBuffer<DealDamageOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<DealDamageOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++)
            {
                var p = b[i].Parameters;
                Add(r, "dmg_type", p.MainType.ToString());
                Add(r, "dmg_main", N(p.MainFactor));
                Add(r, "dmg_raw", N(p.RawDamageValue));
                Add(r, "dmg_pct", N(p.RawDamagePercent));
                Add(r, "dmg_resource", N(p.ResourceModifier));
                Add(r, "dmg_per_hit", N(b[i].DamageModifierPerHit));
                if (p.RawDamageValue != 0f) r.Warnings.Add($"flat damage {N(p.RawDamageValue)} (not power-relative)");
                if (p.RawDamagePercent != 0f) r.Warnings.Add($"%-of-HP damage {N(p.RawDamagePercent)} (never scaled)");
            }
        }, required: true);
        Try("heal", () =>
        {
            if (!em.HasBuffer<HealOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<HealOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++)
            { Add(r, "heal_flat", N(b[i].Health)); Add(r, "heal_pct", N(b[i].HealthPercent)); Add(r, "heal_per_sp", N(b[i].HealthPerSpellPower)); }
        });
        Try("applybuff", () =>
        {
            if (!em.HasBuffer<ApplyBuffOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<ApplyBuffOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++)
            {
                var od = b[i].OverrideDuration;
                Add(r, "buff_duration", od.HasValue ? N(od.Value) : "default");
                Add(r, "buff_stacks", b[i].Stacks.ToString(CultureInfo.InvariantCulture));
            }
        });
        Try("knockback", () =>
        {
            if (!em.HasBuffer<ApplyKnockbackOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<ApplyKnockbackOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++) { Add(r, "knockback_range", N(b[i].Range)); Add(r, "knockback_duration", N(b[i].Duration)); }
        });
        Try("buff", () => { if (e.TryGetComponent<Buff>(out var c)) Add(r, "buff_maxstacks", c.MaxStacks.ToString(CultureInfo.InvariantCulture)); });
        Try("fan", () =>
        {
            if (e.TryGetComponent<AbilityProjectileFanOnGameplayEvent_DataServer>(out var c)) Add(r, "fan_count", c.Count.ToString(CultureInfo.InvariantCulture));
            if (e.TryGetComponent<AbilityProjectileFanOnTick_DataServer>(out var t)) Add(r, "fan_count", $"{t.Count.ToString(CultureInfo.InvariantCulture)}/tick x{t.TickCount.ToString(CultureInfo.InvariantCulture)}");
            if (e.TryGetComponent<Script_MultiShot_Cast_DataServer>(out var m)) Add(r, "fan_count", $"multishot {m.Count.ToString(CultureInfo.InvariantCulture)}");
            if (e.TryGetComponent<EvenSpreadCluster_DataServer>(out var cl)) Add(r, "fan_count", $"cluster {cl.Count.ToString(CultureInfo.InvariantCulture)}");
        });
        Try("minion", () =>
        {
            if (!em.HasBuffer<SpawnMinionOnGameplayEvent>(e)) return;
            var b = em.GetBuffer<SpawnMinionOnGameplayEvent>(e);
            for (int i = 0; i < b.Length; i++) Add(r, "minion_count", b[i].Count.ToString(CultureInfo.InvariantCulture));
        });
        Try("spellmod", () =>
        {
            var targets = new SortedSet<string>(StringComparer.Ordinal);
            if (em.HasBuffer<SpellModArithmetic>(e))
            {
                var b = em.GetBuffer<SpellModArithmetic>(e);
                for (int i = 0; i < b.Length; i++) targets.Add(b[i].Target.ToString());
            }
            if (em.HasBuffer<SpellModArithmeticModifiable>(e))
            {
                var b = em.GetBuffer<SpellModArithmeticModifiable>(e);
                for (int i = 0; i < b.Length; i++) targets.Add(b[i].Target.ToString());
            }
            if (targets.Count > 0) r.V["spellmod_targets"] = string.Join("|", targets);
        });
        string orig = AbilityTuningService.DescribeOriginal(node.Guid);
        if (orig != null) r.V["tuned_orig"] = orig;
        if (r.Warnings.Count > 0) r.V["warnings"] = string.Join("; ", r.Warnings);
        r.V["status"] = r.Errors.Count == 0 ? "ok" : "error";
        if (r.Errors.Count > 0) r.V["error"] = string.Join("; ", r.Errors);
        return r;
    }

    /// <summary>Chat/log report for one ability. Returns the lines to send (short) and logs the full detail.</summary>
    public static List<string> Describe(int rootGuid, int maxNodeLines = 14)
    {
        var lines = new List<string>();
        var chain = AbilityChainGraph.GetOrWalk(rootGuid);
        string rootName = AbilityChainGraph.Name(rootGuid);
        lines.Add($"{rootName} ({rootGuid}): {chain.Nodes.Count} prefab(s) in chain"
            + (chain.Complete ? "" : " — INCOMPLETE (" + string.Join(", ", chain.IncompleteReasons) + "): shared edits disabled")
            + (chain.HasMinionSpawn ? "; spawns minions" : ""));
        if (AbilityChainGraph.UnsafeClasses.Count > 0)
            lines.Add(Clip("⚠ baked edits disabled for " + string.Join(",", AbilityChainGraph.UnsafeClasses) + " prefabs: " + string.Join("; ", AbilityChainGraph.UnsafeReasons)));
        if (!AbilityChainGraph.Built) lines.Add("⚠ chain graph failed to build — baked tuning is off (see server log).");
        lines.Add(DamageScaler.Describe(rootGuid));   // v0.133.0: per-hit attribution counters
        // v0.134.0: cooldown rules are runtime (captured bar casts only); casttime is experimental.
        lines.Add(AbilityCooldownEnforcer.HasCharges(rootGuid)
            ? "cooldown: n/a (charges) — cooldown/cooldownscale/floor are skipped; tune 'chargetime'"
            : $"cooldown: runtime on captured bar casts (applied {AbilityCooldownEnforcer.Applied}, started {AbilityCooldownEnforcer.Created}, expired {AbilityCooldownEnforcer.Expired} this session)");
        string ctRefusal = AbilityTuningService.CastTimeRefusal(rootGuid);
        lines.Add("casttime: EXPERIMENTAL" + (ctRefusal == null ? "" : $" — not available ({ctRefusal})"));

        int shown = 0, withValues = 0;
        bool anyDamage = false;
        var warnings = new List<string>();
        var log = new StringBuilder($"[Beelz INSPECT] {rootName} ({rootGuid})\n");
        foreach (var node in chain.Nodes)
        {
            var r = ReadNode(chain, node);
            var parts = new List<string>();
            foreach (var c in Columns)
            {
                if (c is "group" or "group_guid" or "node" or "node_guid" or "depth" or "via" or "shared_with" or "status" or "warnings" or "error") continue;
                string v = r.Get(c);
                if (v.Length > 0) parts.Add($"{c}={v}");
            }
            if (r.V.ContainsKey("dmg_main")) anyDamage = true;
            var others = AbilityChainGraph.OtherOwners(node.Guid, rootGuid);
            string shared = others.Count == 0 ? "" : $" [shared with {others.Count}: {AbilityChainGraph.Name(others[0])}{(others.Count > 1 ? ", …" : "")}]";
            foreach (var w in r.Warnings) warnings.Add($"{r.Get("node")}: {w}");
            foreach (var er in r.Errors) warnings.Add($"{r.Get("node")}: decode error {er}");
            string line = $"d{node.Depth} {r.Get("node")} ({node.Via}){shared}: {(parts.Count == 0 ? "-" : string.Join(" ", parts))}";
            log.AppendLine("  " + line + (r.Warnings.Count > 0 ? "  !! " + string.Join("; ", r.Warnings) : ""));
            if (parts.Count == 0 && shared.Length == 0) continue;
            withValues++;
            if (shown < maxNodeLines) { lines.Add(Clip(line)); shown++; }
        }
        if (withValues > shown) lines.Add($"… {withValues - shown} more prefab(s) — full detail in the server log.");
        if (!anyDamage) warnings.Add("no DealDamage component in the chain — damage (if any) comes from scripts/buffs not listed here");
        foreach (var w in warnings) lines.Add(Clip("⚠ " + w));
        foreach (var s in AbilityTuningService.SkippedFor(chain)) lines.Add(Clip("skipped tune: " + s));
        Core.Log.LogInfo(log.ToString());
        return lines;
    }

    static string Clip(string s) => s.Length <= 420 ? s : s.Substring(0, 417) + "...";

    /// <summary>Export every ability group's chain to CSV. Returns (ok, message).</summary>
    public static (bool ok, string message) ExportAll()
    {
        string dir = Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID);
        string path = Path.Combine(dir, "inspect_export.csv");
        string tmp = path + ".tmp";
        int rows = 0, damageFailures = 0, errorRows = 0;
        var failuresByComponent = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            Directory.CreateDirectory(dir);
            using (var w = new StreamWriter(tmp, false, new UTF8Encoding(false)))
            {
                w.NewLine = "\r\n";   // RFC-4180
                w.WriteLine(string.Join(",", Columns));
                foreach (int g in AbilityChainGraph.AllGroups())
                {
                    var chain = AbilityChainGraph.GetGroup(g);
                    if (chain == null) continue;
                    foreach (var node in chain.Nodes)
                    {
                        var r = ReadNode(chain, node);
                        if (r.DamageDecodeFailed) damageFailures++;
                        if (r.Errors.Count > 0)
                        {
                            errorRows++;
                            foreach (var er in r.Errors)
                            {
                                string comp = er.Split(':')[0];
                                failuresByComponent[comp] = failuresByComponent.TryGetValue(comp, out int n) ? n + 1 : 1;
                            }
                        }
                        var cells = new string[Columns.Length];
                        for (int i = 0; i < Columns.Length; i++) cells[i] = Csv(r.Get(Columns[i]));
                        w.WriteLine(string.Join(",", cells));
                        rows++;
                    }
                }
            }
            File.Move(tmp, path, overwrite: true);   // atomic swap on the same volume
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return (false, $"FAILED: export could not be written ({ex.Message}).");
        }
        var fails = new List<string>();
        foreach (var kv in failuresByComponent) fails.Add($"{kv.Key}={kv.Value}");
        string summary = $"{rows} row(s) from {AbilityChainGraph.GroupCount} ability group(s) → {path}"
            + (errorRows > 0 ? $"; {errorRows} row(s) with decode errors ({string.Join(", ", fails)})" : "; no decode errors");
        if (damageFailures > 0)
            return (false, $"FAILED: DealDamage decode failed on {damageFailures} row(s) — damage columns are incomplete, don't rely on this export. {summary}");
        return (true, "Export OK: " + summary);
    }

    static string Csv(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        bool quote = v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        return quote ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
