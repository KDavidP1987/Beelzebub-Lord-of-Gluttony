using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Logic;

// modid-remap-errors (v0.137.6) — PURE decisions for leaked slot-override holders.
// The engine applies slot-override rows through a prefab-less holder entity (ReplaceAbilityOnSlotBuff_AllInitialized +
// AbilityGroupSlotModificationBuffer). Its rows name the GroupGuid mod it set on each slot, plus CopyCooldown and
// SpellModsSource mods. Popping the GroupGuid mod by id (bar reset, grant) leaves the holder alive with its other mods,
// so it is saved, its dead ids fail to remap at every boot ("Couldn't remap old Modification Id …"), some get remapped
// onto OTHER mods' ids, and its stale CopyCooldown keeps overriding the slot (diagnostic 2026-10-04: slot 7 CopyCooldown
// False, base True, from six stale holders). A holder is stale when none of its rows still sets a slot: no row's id is
// among its target slot's GroupGuid mods WITH THIS HOLDER AS SOURCE (id alone is not enough — remapped ids collide).

public enum LeakVerdict { Live, Dangling, Unknown, Empty }

public enum HolderVerdict { Live, Stale, Unknown, Building, Empty }

/// <summary>One holder row: its GroupGuid mod id and the GroupGuid parse of the slot it targets (null = no slot read).</summary>
public readonly record struct HolderRowInput(int ModId, SlotModParse TargetSlot);

/// <summary>A holder entity, by index and version (a recycled index with another version is another holder).</summary>
public readonly record struct LeakHolder(int Index, int Version);

public static class ModLeak
{
    /// <summary>Most holders one sweep pass reads; the rest wait for the next pass (the line says so).</summary>
    public const int Cap = 4096;

    /// <summary>One row: Live when its slot's GroupGuid mods hold this id from this holder; Dangling when the slot was read
    /// and they do not; Unknown when the slot could not be read; Empty for id 0.</summary>
    public static LeakVerdict Row(int modId, SlotModParse slot, LeakHolder holder)
    {
        if (modId <= 0) return LeakVerdict.Empty;
        if (slot == null || !slot.Readable) return LeakVerdict.Unknown;
        return slot.Entries.Any(e => e.ModId == modId && e.SourceIndex == holder.Index && e.SourceVersion == holder.Version)
            ? LeakVerdict.Live : LeakVerdict.Dangling;
    }

    /// <summary>The holder as a whole. Building (never touched) until the engine marked it initialized; Live when any row
    /// still sets its slot; Unknown when any slot was unreadable; Empty when it has no row with an id; else Stale.</summary>
    public static HolderVerdict Holder(LeakHolder holder, bool allInitialized, IReadOnlyList<HolderRowInput> rows)
    {
        if (!allInitialized) return HolderVerdict.Building;
        if (rows == null || rows.Count == 0) return HolderVerdict.Empty;
        var v = rows.Select(r => Row(r.ModId, r.TargetSlot, holder)).ToList();
        if (v.Contains(LeakVerdict.Live)) return HolderVerdict.Live;
        if (v.Contains(LeakVerdict.Unknown)) return HolderVerdict.Unknown;
        return v.Contains(LeakVerdict.Dangling) ? HolderVerdict.Stale : HolderVerdict.Empty;
    }

    /// <summary>The holders stale in both reads. One seen stale once (a slot read mid-change) is kept for the next pass.</summary>
    public static HashSet<LeakHolder> Confirm(ICollection<LeakHolder> first, ICollection<LeakHolder> second)
    {
        var set = new HashSet<LeakHolder>();
        if (first == null || second == null) return set;
        foreach (var h in second) if (first.Contains(h)) set.Add(h);
        return set;
    }

    /// <summary>The one line per sweep pass that found anything.</summary>
    public static string SweepLine(string why, int cleaned, int modsCleared, int keptOnce, int unknown, bool capped) =>
        $"[Beelz MODLEAK] sweep ({Safe(why)}): cleaned {cleaned} stale holder(s), {modsCleared} leftover mod(s) removed; " +
        $"{keptOnce} seen stale once (rechecked next pass), {unknown} unreadable{(capped ? $"; capped at {Cap} holders, the rest next pass" : "")}.";

    /// <summary>One diagnostic line per holder row.</summary>
    public static string RowLine(string holder, LeakHolder h, HolderVerdict hv, int slot, int modId, int copyCooldownId,
                                 int spellModId, LeakVerdict rv, int newGroup, string target) =>
        $"[Beelz MODLEAK] holder={h.Index}:{h.Version} {Safe(holder)} holder-verdict={hv} target={Safe(target)} slot={slot} " +
        $"mod={modId} row={rv} copycd={copyCooldownId} spellmod={spellModId} group={newGroup}";

    static string Safe(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "-";
        var t = new string(s.Where(c => !char.IsControl(c) && c != ' ').ToArray());
        return t.Length == 0 ? "-" : (t.Length > 80 ? t.Substring(0, 80) : t);
    }
}
