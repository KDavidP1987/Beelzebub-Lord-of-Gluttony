using System;
using System.Collections.Generic;
using System.Linq;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// v0.37.0 — Vision "collection book". A read-only aggregation that turns the flat
/// per-ability capture list into a per-UNIT collection view: how many of a unit's
/// capturable abilities the player has collected (X / Y), whether they've unlocked
/// its transform, and the source (V-Blood / regular).
///
/// "Y" (total) uses the SAME enumeration the capture path uses — the unit prefab's
/// <see cref="AbilityGroupSlotBuffer"/> filtered by <see cref="AbilityFilter.ShouldCapture"/>
/// — so X/Y matches what the player can actually collect. The total is unioned with
/// what the player already holds, so rule changes can never produce X &gt; Y.
///
/// Powers <c>.beelz bestiary</c> (human) and <c>.beelz api bestiary</c> (BCH).
/// </summary>
internal static class BestiaryService
{
    internal sealed class Entry
    {
        public int UnitPrefabGuid;
        public string UnitName;
        public CaptureSource Source;
        public int CapturedCount;
        public int TotalCount;
        public bool TransformUnlocked;
        /// <summary>Per-unit detail only (null in the list view): the ability GUIDs the player holds.</summary>
        public List<int> CapturedAbilityGuids;
        /// <summary>Per-unit detail only: the full capturable ability set (∪ held), for ✓/· rendering.</summary>
        public List<int> AllAbilityGuids;
        public bool Complete => TotalCount > 0 && CapturedCount >= TotalCount;
    }

    /// <summary>
    /// Distinct ability GUIDs a player COULD capture from this unit — the unit prefab's
    /// AbilityGroupSlotBuffer filtered by the capture rules. Empty if the prefab isn't
    /// loaded or has no ability bar.
    /// </summary>
    public static List<int> GetCapturableAbilities(int unitGuid)
    {
        var result = new List<int>();
        var pg = new PrefabGUID(unitGuid);
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(pg, out Entity prefabEntity)) return result;
        if (!Core.EntityManager.HasBuffer<AbilityGroupSlotBuffer>(prefabEntity)) return result;

        var seen = new HashSet<int>();
        var slots = Core.EntityManager.GetBuffer<AbilityGroupSlotBuffer>(prefabEntity);
        for (int i = 0; i < slots.Length; i++)
        {
            PrefabGUID ability = slots[i].BaseAbilityGroupOnSlot;
            if (ability._Value == 0) continue;
            if (!seen.Add(ability._Value)) continue;
            if (!Core.AbilityFilter.ShouldCapture(ability.GetPrefabName(), ability._Value, out _)) continue;
            result.Add(ability._Value);
        }
        return result;
    }

    /// <summary>One entry per unit the player has captured from OR unlocked a transform for, sorted by name.</summary>
    public static List<Entry> Build(ulong steamId)
    {
        var capturedByUnit = new Dictionary<int, HashSet<int>>();
        var vbloodUnits = new HashSet<int>();
        foreach (var c in Core.AbilityRegistry.ListFor(steamId))
        {
            if (!capturedByUnit.TryGetValue(c.UnitPrefabGuid, out var set))
            {
                set = new HashSet<int>();
                capturedByUnit[c.UnitPrefabGuid] = set;
            }
            set.Add(c.AbilityPrefabGuid);
            if (c.Source == CaptureSource.VBlood) vbloodUnits.Add(c.UnitPrefabGuid);
        }

        var transformUnits = new Dictionary<int, CaptureSource>();
        foreach (var t in Core.AbilityRegistry.ListTransforms(steamId))
        {
            transformUnits[t.UnitPrefabGuid] = t.Source;
            if (t.Source == CaptureSource.VBlood) vbloodUnits.Add(t.UnitPrefabGuid);
        }

        var unitGuids = new HashSet<int>(capturedByUnit.Keys);
        foreach (int u in transformUnits.Keys) unitGuids.Add(u);

        var entries = new List<Entry>(unitGuids.Count);
        foreach (int unit in unitGuids)
        {
            capturedByUnit.TryGetValue(unit, out var held);
            int capturedCount = held?.Count ?? 0;

            var all = new HashSet<int>(GetCapturableAbilities(unit));
            if (held != null) foreach (int a in held) all.Add(a);

            entries.Add(new Entry
            {
                UnitPrefabGuid = unit,
                UnitName = Core.AbilityMetadata?.ResolveUnitName(unit) ?? new PrefabGUID(unit).GetPrefabName(),
                Source = vbloodUnits.Contains(unit) ? CaptureSource.VBlood : CaptureSource.Regular,
                CapturedCount = capturedCount,
                TotalCount = all.Count,
                TransformUnlocked = transformUnits.ContainsKey(unit),
            });
        }

        entries.Sort((a, b) => string.Compare(a.UnitName, b.UnitName, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>Per-unit detail: includes the held + full ability GUID lists for ✓/· rendering.</summary>
    public static Entry BuildForUnit(ulong steamId, int unitGuid)
    {
        var held = new HashSet<int>();
        bool vblood = false;
        foreach (var c in Core.AbilityRegistry.ListFor(steamId))
        {
            if (c.UnitPrefabGuid != unitGuid) continue;
            held.Add(c.AbilityPrefabGuid);
            if (c.Source == CaptureSource.VBlood) vblood = true;
        }

        bool tx = Core.AbilityRegistry.HasTransformUnlock(steamId, unitGuid);
        if (tx && !vblood)
        {
            foreach (var t in Core.AbilityRegistry.ListTransforms(steamId))
                if (t.UnitPrefabGuid == unitGuid) { vblood = t.Source == CaptureSource.VBlood; break; }
        }

        var all = new HashSet<int>(GetCapturableAbilities(unitGuid));
        foreach (int a in held) all.Add(a);

        return new Entry
        {
            UnitPrefabGuid = unitGuid,
            UnitName = Core.AbilityMetadata?.ResolveUnitName(unitGuid) ?? new PrefabGUID(unitGuid).GetPrefabName(),
            Source = vblood ? CaptureSource.VBlood : CaptureSource.Regular,
            CapturedCount = held.Count,
            TotalCount = all.Count,
            TransformUnlocked = tx,
            CapturedAbilityGuids = held.ToList(),
            AllAbilityGuids = all.ToList(),
        };
    }

    // v0.86.0 (Bug B): server-wide count of DISTINCT capturable abilities in the game — the honest
    // denominator for collection %. The old denominator was the curated AbilityMap.Count (~453), but
    // captures aren't gated on the curated map (they use AbilityFilter.ShouldCapture), so a player who
    // devoured full kits could hold MORE distinct abilities (605) than the curated count → 133%. This
    // mirrors ApiCommands.BuildCatalogSnapshot's "full capturable universe": every AB_*_AbilityGroup/_Group
    // that's curated OR passes ShouldCapture. Counted by GUID so it's directly comparable to
    // AbilityRegistry.CapturedCount (also distinct GUIDs) → captured ⊆ universe, so % ≤ 100.
    static int _totalCapturableCache = -1;

    /// <summary>
    /// Total distinct capturable ability GUIDs across the whole catalog (cached after first build —
    /// stable once prefabs are loaded; call <see cref="InvalidateTotals"/> after a rules reload). Returns
    /// 0 until prefabs are available, so callers should fall back to the curated count when it's 0.
    /// </summary>
    public static int TotalCapturableAbilities()
    {
        if (_totalCapturableCache > 0) return _totalCapturableCache;
        if (!Core.IsReady || Core.PrefabNames == null || Core.AbilityFilter == null) return 0;

        var map = Core.AbilityRules?.Current?.AbilityMap;
        var seen = new HashSet<int>();
        foreach (var (guid, name) in Core.PrefabNames)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!name.StartsWith("AB_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith("_AbilityGroup", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith("_Group", StringComparison.OrdinalIgnoreCase)) continue;

            bool curated = map != null && map.ContainsKey(name);
            bool capturable = Core.AbilityFilter.ShouldCapture(name, guid, out _);
            if (!curated && !capturable) continue;

            seen.Add(guid);
        }

        if (seen.Count > 0) _totalCapturableCache = seen.Count;
        return seen.Count;
    }

    /// <summary>v0.86.0: drop the cached capturable total (call after an admin rules reload, since
    /// deny/allow-pattern changes can shift what passes ShouldCapture).</summary>
    public static void InvalidateTotals() => _totalCapturableCache = -1;
}
