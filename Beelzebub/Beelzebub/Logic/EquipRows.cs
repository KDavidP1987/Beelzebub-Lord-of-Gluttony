using System.Collections.Generic;

namespace Beelzebub.Logic;

// v0.137.0 (bar-reset D31) — PURE: which ReplaceAbilityOnSlotBuff rows on the live weapon equip buff are injected.
// The equip-buff PREFAB carries the weapon's own rows (EquipBuff_Weapon_Sword_Base: slot 0 primary, 1 Whirlwind,
// 4 Shockwave). The pre-0.137 resetbar removed EVERY row on slots 0-7 (SlotApply.ClearGrant), stripping those
// vanilla rows — the weapon's skills then only came back after a weapon swap re-created the buff. An injected row is
// one the prefab does not carry (matched on slot + ability, counting copies); a prefab row missing from the live
// buffer is put back.

public readonly record struct EquipRow(int Slot, int GroupGuid);

public static class EquipRowDiff
{
    public const int MinSlot = 0;
    public const int MaxSlot = 7;

    /// <summary>Indices into <paramref name="live"/> of injected rows on slots 0-7, in ascending order (remove them
    /// back to front). <paramref name="prefab"/> null = the prefab's rows are unknown → null (the caller must then
    /// remove nothing and never report the slot clean).</summary>
    public static List<int> InjectedIndices(IReadOnlyList<EquipRow> live, IReadOnlyList<EquipRow> prefab)
    {
        if (prefab == null) return null;
        var budget = Counts(prefab);
        var injected = new List<int>();
        for (int i = 0; i < (live?.Count ?? 0); i++)
        {
            var r = live[i];
            if (budget.TryGetValue(r, out int n) && n > 0) { budget[r] = n - 1; continue; }
            if (r.Slot >= MinSlot && r.Slot <= MaxSlot) injected.Add(i);
        }
        return injected;
    }

    /// <summary>Indices into <paramref name="prefab"/> of the prefab's own rows absent from <paramref name="live"/>
    /// (e.g. stripped by a pre-0.137 reset). Null prefab → null.</summary>
    public static List<int> MissingPrefabIndices(IReadOnlyList<EquipRow> live, IReadOnlyList<EquipRow> prefab)
    {
        if (prefab == null) return null;
        var have = Counts(live ?? new List<EquipRow>());
        var missing = new List<int>();
        for (int i = 0; i < prefab.Count; i++)
        {
            var r = prefab[i];
            if (have.TryGetValue(r, out int n) && n > 0) { have[r] = n - 1; continue; }
            missing.Add(i);
        }
        return missing;
    }

    static Dictionary<EquipRow, int> Counts(IReadOnlyList<EquipRow> rows)
    {
        var d = new Dictionary<EquipRow, int>();
        foreach (var r in rows) d[r] = d.TryGetValue(r, out int n) ? n + 1 : 1;
        return d;
    }
}
