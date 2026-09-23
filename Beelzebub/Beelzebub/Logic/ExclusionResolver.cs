using System;
using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Logic;

// v0.135.0 — PURE incompatibility-lock resolver (no game types; unit-tested).
//
// An admin-defined EXCLUSION GROUP says "at most Max of these abilities may be in one player's active loadout
// at the same time". A loadout is an ORDERED list of bindings (bar slots in slot order, then hotkeys in name
// order); earlier bindings win. Limits count DISTINCT abilities, so the same ability bound twice occupies one
// place (rev 6.1 #6). An ability that belongs to several groups must fit in ALL of them.

/// <summary>One resolved group: its members as ability GUIDs and/or category names.</summary>
public sealed class ExclusionGroupDef
{
    public string Name = "";
    public int Max = 1;
    public HashSet<int> Guids = new();
    public HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase);

    public bool Contains(int guid, Func<int, string> categoryOf)
    {
        if (Guids.Contains(guid)) return true;
        if (Categories.Count == 0 || categoryOf == null) return false;
        string c = categoryOf(guid);
        return !string.IsNullOrEmpty(c) && Categories.Contains(c);
    }
}

/// <summary>A binding in a loadout: <see cref="Key"/> identifies it (e.g. "slot:3", "hotkey:dash").</summary>
public readonly record struct LoadoutBinding(string Key, int Guid);

public sealed class Suppression
{
    public string Key = "";
    public int Guid;
    public List<string> Groups = new();   // every rejecting group, sorted by name (first = primary for messages)
    public string Primary => Groups.Count > 0 ? Groups[0] : "";
}

public sealed class ExclusionResult
{
    public HashSet<string> KeptKeys = new(StringComparer.Ordinal);
    public List<Suppression> Suppressed = new();
    public HashSet<int> KeptGuids = new();

    public bool IsKept(string key) => KeptKeys.Contains(key);
    public Suppression Find(string key) => Suppressed.FirstOrDefault(s => s.Key == key);
}

public static class ExclusionResolver
{
    public static ExclusionResult Resolve(IReadOnlyList<LoadoutBinding> ordered, IReadOnlyList<ExclusionGroupDef> groups,
        Func<int, string> categoryOf = null)
    {
        var result = new ExclusionResult();
        if (ordered == null) return result;
        var active = (groups ?? Array.Empty<ExclusionGroupDef>())
            .Where(g => g != null && (g.Guids.Count > 0 || g.Categories.Count > 0))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var occupancy = active.ToDictionary(g => g, _ => new HashSet<int>());
        var suppressedGuids = new Dictionary<int, List<string>>();

        foreach (var b in ordered)
        {
            if (b.Guid == 0) { result.KeptKeys.Add(b.Key); continue; }
            if (result.KeptGuids.Contains(b.Guid)) { result.KeptKeys.Add(b.Key); continue; }   // duplicate of a kept ability
            if (suppressedGuids.TryGetValue(b.Guid, out var prior))
            {
                result.Suppressed.Add(new Suppression { Key = b.Key, Guid = b.Guid, Groups = new List<string>(prior) });
                continue;
            }
            List<string> rejecting = null;
            foreach (var g in active)
            {
                if (!g.Contains(b.Guid, categoryOf)) continue;
                if (occupancy[g].Count >= Math.Max(1, g.Max)) (rejecting ??= new List<string>()).Add(g.Name);
            }
            if (rejecting == null)
            {
                foreach (var g in active)
                    if (g.Contains(b.Guid, categoryOf)) occupancy[g].Add(b.Guid);
                result.KeptGuids.Add(b.Guid);
                result.KeptKeys.Add(b.Key);
            }
            else
            {
                suppressedGuids[b.Guid] = rejecting;
                result.Suppressed.Add(new Suppression { Key = b.Key, Guid = b.Guid, Groups = rejecting });
            }
        }
        return result;
    }

    /// <summary>
    /// Prospective check for adding one binding: would <paramref name="candidate"/> survive if inserted at its
    /// natural position? <paramref name="orderedWithCandidate"/> must already contain it. Returns the
    /// suppression (with the groups that reject it) or null when it would be allowed.
    /// </summary>
    public static Suppression Check(IReadOnlyList<LoadoutBinding> orderedWithCandidate, string candidateKey,
        IReadOnlyList<ExclusionGroupDef> groups, Func<int, string> categoryOf = null)
        => Resolve(orderedWithCandidate, groups, categoryOf).Find(candidateKey);
}
