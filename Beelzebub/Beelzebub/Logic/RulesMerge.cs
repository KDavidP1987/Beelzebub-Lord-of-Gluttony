using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beelzebub.Logic;

// v0.135.0 — PURE 3-way merge of ability_rules.json for `.beelz admin reseed` (no game types; unit-tested).
//
//   base   = the shipped default the server was last seeded/merged from (ability_rules.baseline.json)
//   server = the live per-server ability_rules.json (admin edits live here)
//   ship   = the shipped default embedded in THIS build
//
// Rules (rev 6.1 #20-#25, rev 6.2 #13, rev 6.3 #2):
//   * Absence is a value (tombstone). For every compared value:
//       server == ship            → keep server (nothing to do)
//       server == base            → adopt ship (incl. a ship-side deletion) — the admin never touched it
//       ship   == base            → keep server — the admin changed it, ship didn't
//       otherwise                 → CONFLICT: keep server, report it
//   * AbilityMap / TransformMap are merged ENTRY by entry and, when both sides have the entry, FIELD by field.
//     Everything else (top-level lists, Defaults, ExclusionGroups, nested objects) is ATOMIC.
//   * Lists compare after normalising (trim, case-insensitive, sorted); numbers compare numerically.
//   * Entry identity = resolved ability GUID when the key resolves, else the key. Two SERVER keys resolving to
//     the same GUID → the merge is refused (the admin consolidates them first).
//   * BaselineHash is transaction metadata: stripped before diffing, never merged.
//   * No baseline (or a hash mismatch): SAFE mode — add entries missing on the server, and adopt a shipped
//     Enabled=false over a server Enabled=true (safety wins; each adoption is listed). Nothing else changes.

public sealed class MergeReport
{
    public JsonObject Merged;
    public bool Refused;
    public string RefuseReason = "";
    public bool SafeMode;
    public List<string> Added = new();      // entries added from ship
    public List<string> Adopted = new();    // values taken from ship (admin never changed them)
    public List<string> Removed = new();    // entries/fields deleted because ship deleted them
    public List<string> KeptAdmin = new();  // admin-changed values kept (ship unchanged)
    public List<string> Conflicts = new();  // both changed → admin value kept

    public bool HasChanges => Added.Count + Adopted.Count + Removed.Count > 0;
    public string Summary() =>
        $"{(SafeMode ? "SAFE mode (no valid baseline). " : "")}added {Added.Count}, adopted {Adopted.Count}, removed {Removed.Count}, " +
        $"kept admin {KeptAdmin.Count}, conflicts {Conflicts.Count}";
}

public static class RulesMerge
{
    public const string BaselineHashKey = "BaselineHash";
    static readonly string[] EntryMaps = { "AbilityMap", "TransformMap" };

    /// <summary>
    /// Merge. <paramref name="baseline"/> null → SAFE mode. <paramref name="resolveGuid"/> maps an AbilityMap
    /// key (name or numeric string) to a GUID, 0 when unresolvable (TransformMap keys are compared by key).
    /// Inputs are not modified.
    /// </summary>
    public static MergeReport Merge(JsonObject baseline, JsonObject server, JsonObject ship, Func<string, int> resolveGuid)
    {
        var rep = new MergeReport { SafeMode = baseline == null };
        var s = Clone(server) ?? new JsonObject();
        var t = Clone(ship) ?? new JsonObject();
        var b = Clone(baseline);
        s.Remove(BaselineHashKey); t.Remove(BaselineHashKey); b?.Remove(BaselineHashKey);

        // Refuse duplicate identities on the server side up front (rev 6.2 #13).
        if (s["AbilityMap"] is JsonObject sAbil && resolveGuid != null)
        {
            var seen = new Dictionary<int, string>();
            foreach (var kv in sAbil)
            {
                int g = resolveGuid(kv.Key);
                if (g == 0) continue;
                if (seen.TryGetValue(g, out var other))
                {
                    rep.Refused = true;
                    rep.RefuseReason = $"AbilityMap keys '{other}' and '{kv.Key}' are the same ability (GUID {g}) — remove one, then retry.";
                    rep.Merged = s;
                    return rep;
                }
                seen[g] = kv.Key;
            }
        }

        if (rep.SafeMode)
        {
            SafeMerge(s, t, resolveGuid, rep);
            rep.Merged = s;
            return rep;
        }

        // Top-level (atomic except the entry maps).
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var o in new[] { b, s, t }) if (o != null) foreach (var kv in o) keys.Add(kv.Key);
        foreach (var key in keys)
        {
            if (EntryMaps.Contains(key))
            {
                var merged = MergeEntryMap(key, b?[key] as JsonObject, s[key] as JsonObject, t[key] as JsonObject,
                    key == "AbilityMap" ? resolveGuid : null, rep);
                s[key] = merged;
                continue;
            }
            if (Decide(b?[key], s[key], t[key], key, rep, isEntry: false) == Pick.TakeShip)
            {
                if (t[key] == null) s.Remove(key);
                else s[key] = Copy(t[key]);
            }
        }
        rep.Merged = s;
        return rep;
    }

    enum Pick { KeepServer, TakeShip }

    static Pick Decide(JsonNode bv, JsonNode sv, JsonNode tv, string label, MergeReport rep, bool isEntry)
    {
        if (Eq(sv, tv)) return Pick.KeepServer;
        if (Eq(sv, bv))
        {
            if (tv == null) rep.Removed.Add(label);
            else if (sv == null && isEntry) rep.Added.Add(label);
            else rep.Adopted.Add(label);
            return Pick.TakeShip;
        }
        if (Eq(tv, bv)) { rep.KeptAdmin.Add(label); return Pick.KeepServer; }
        rep.Conflicts.Add(label);
        return Pick.KeepServer;
    }

    static JsonObject MergeEntryMap(string mapName, JsonObject bMap, JsonObject sMap, JsonObject tMap,
        Func<string, int> resolveGuid, MergeReport rep)
    {
        sMap ??= new JsonObject();
        var bById = Index(bMap, resolveGuid);
        var sById = Index(sMap, resolveGuid);
        var tById = Index(tMap, resolveGuid);
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in new[] { bById, sById, tById }) foreach (var id in d.Keys) ids.Add(id);

        foreach (var id in ids)
        {
            bById.TryGetValue(id, out var bE);
            sById.TryGetValue(id, out var sE);
            tById.TryGetValue(id, out var tE);
            string serverKey = sE.key ?? tE.key ?? bE.key;
            string label = $"{mapName}.{serverKey}";

            if (sE.node is JsonObject sObj && tE.node is JsonObject tObj)
            {
                // Both sides have it → field-wise.
                var bObj = bE.node as JsonObject;
                var fields = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var o in new[] { bObj, sObj, tObj }) if (o != null) foreach (var kv in o) fields.Add(kv.Key);
                foreach (var f in fields)
                {
                    var pick = Decide(bObj?[f], sObj[f], tObj[f], $"{label}.{f}", rep, isEntry: false);
                    if (pick == Pick.TakeShip)
                    {
                        if (tObj[f] == null) sObj.Remove(f);
                        else sObj[f] = Copy(tObj[f]);
                    }
                }
                continue;
            }
            // One side missing → whole-entry decision (absence is a value).
            var entryPick = Decide(bE.node, sE.node, tE.node, label, rep, isEntry: true);
            if (entryPick == Pick.TakeShip)
            {
                if (tE.node == null) sMap.Remove(serverKey);
                else sMap[serverKey] = Copy(tE.node);
            }
        }
        return sMap;
    }

    static void SafeMerge(JsonObject s, JsonObject t, Func<string, int> resolveGuid, MergeReport rep)
    {
        foreach (var mapName in EntryMaps)
        {
            if (t[mapName] is not JsonObject tMap) continue;
            if (s[mapName] is not JsonObject sMap) { sMap = new JsonObject(); s[mapName] = sMap; }
            var sById = Index(sMap, mapName == "AbilityMap" ? resolveGuid : null);
            foreach (var (id, tE) in Index(tMap, mapName == "AbilityMap" ? resolveGuid : null).OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!sById.TryGetValue(id, out var sE))
                {
                    sMap[tE.key] = Copy(tE.node);
                    rep.Added.Add($"{mapName}.{tE.key}");
                    continue;
                }
                if (tE.node is JsonObject tObj && sE.node is JsonObject sObj
                    && IsFalse(tObj["Enabled"]) && !IsFalse(sObj["Enabled"]))
                {
                    sObj["Enabled"] = false;
                    rep.Adopted.Add($"{mapName}.{sE.key}.Enabled=false");
                }
            }
        }
    }

    static bool IsFalse(JsonNode n) => n is JsonValue && n.ToJsonString() == "false";

    static JsonNode Copy(JsonNode n) => n == null ? null : JsonNode.Parse(n.ToJsonString());

    static Dictionary<string, (string key, JsonNode node)> Index(JsonObject map, Func<string, int> resolveGuid)
    {
        var d = new Dictionary<string, (string, JsonNode)>(StringComparer.Ordinal);
        if (map == null) return d;
        foreach (var kv in map)
        {
            int g = resolveGuid?.Invoke(kv.Key) ?? 0;
            string id = g != 0 ? "#" + g.ToString(CultureInfo.InvariantCulture) : "k:" + kv.Key;
            d.TryAdd(id, (kv.Key, kv.Value));
        }
        return d;
    }

    // ---- canonical equality --------------------------------------------------------------------------

    public static bool Eq(JsonNode a, JsonNode b)
    {
        if (a == null || b == null) return a == null && b == null;
        return Canon(a) == Canon(b);
    }

    /// <summary>Canonical string: objects key-sorted, lists normalised (trim, lower-case, sorted), numbers invariant.</summary>
    public static string Canon(JsonNode n)
    {
        switch (n)
        {
            case null: return "null";
            case JsonObject o:
                return "{" + string.Join(",", o.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => kv.Key + ":" + Canon(kv.Value))) + "}";
            case JsonArray arr:
                return "[" + string.Join(",", arr.Select(x => x is JsonValue ? CanonListItem(x) : Canon(x))
                    .OrderBy(x => x, StringComparer.Ordinal)) + "]";
            case JsonValue v:
                var el = JsonDocument.Parse(v.ToJsonString()).RootElement;
                return el.ValueKind switch
                {
                    JsonValueKind.Number => el.GetDouble().ToString("R", CultureInfo.InvariantCulture),
                    JsonValueKind.String => "\"" + el.GetString() + "\"",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => "null",
                };
        }
        return n.ToJsonString();
    }

    // List items compare trimmed + case-insensitively (weapon/form tokens are case-insensitive).
    static string CanonListItem(JsonNode x)
    {
        var el = JsonDocument.Parse(x.ToJsonString()).RootElement;
        return el.ValueKind == JsonValueKind.String
            ? "\"" + (el.GetString() ?? "").Trim().ToLowerInvariant() + "\""
            : Canon(x);
    }

    static JsonObject Clone(JsonObject o) => o == null ? null : (JsonObject)JsonNode.Parse(o.ToJsonString());

    public static string Sha256(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }
}
