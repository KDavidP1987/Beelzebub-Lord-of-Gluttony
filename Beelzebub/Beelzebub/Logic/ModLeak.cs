using System;
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

    /// <summary>When the armed pass runs: an idle sweep (<see cref="DateTime.MaxValue"/>) is armed one gap from now; an armed
    /// one keeps its time, so repeated pops can never postpone a pass.</summary>
    public static DateTime Arm(DateTime dueAt, DateTime now, TimeSpan gap) => dueAt == DateTime.MaxValue ? now + gap : dueAt;

    /// <summary>The holders a pass may clean: Stale in this read AND in the first read. Live, Unknown, Building and
    /// Empty holders are never selected, whatever the first read said.</summary>
    public static HashSet<LeakHolder> Select(ICollection<LeakHolder> firstRead, IEnumerable<(LeakHolder Holder, HolderVerdict Verdict)> secondRead)
    {
        var now = new HashSet<LeakHolder>();
        if (secondRead != null)
            foreach (var (h, v) in secondRead) if (v == HolderVerdict.Stale) now.Add(h);
        return Confirm(firstRead, now);
    }

    /// <summary>Which holders one pass reads: up to <paramref name="cap"/> of the ascending entity indices, starting at the
    /// first index above <paramref name="cursor"/> and wrapping. NextCursor is the last index read when the pass did not
    /// read them all (the next pass continues after it), else 0 — so every holder is read within ceil(n / cap) passes
    /// even when every earlier one stays live.</summary>
    public static (List<int> Read, int NextCursor) Page(IReadOnlyList<int> sortedIndices, int cursor, int cap)
    {
        var read = new List<int>();
        int n = sortedIndices?.Count ?? 0;
        if (n == 0 || cap <= 0) return (read, 0);
        int start = 0;
        while (start < n && sortedIndices[start] <= cursor) start++;
        if (start == n) start = 0;
        int take = Math.Min(cap, n);
        for (int i = 0; i < take; i++) read.Add(sortedIndices[(start + i) % n]);
        return (read, take < n ? read[^1] : 0);
    }

    /// <summary>Whether the sweep cycle still has unread holders: it has read <paramref name="readThisCycle"/> (this page
    /// included) of <paramref name="total"/>. Once every holder was read the sweep may sleep, even when the holders never
    /// fit one page — so a server with more than <see cref="Cap"/> holders still goes idle.</summary>
    public static bool MorePages(int readThisCycle, int total) => readThisCycle < total;

    /// <summary>Whether an idle pass logs <see cref="IdleLine"/>: a boot always does; a pop-armed pass only with verbose logging.</summary>
    public static bool LogIdle(string why, bool verbose) => why == "boot" || verbose;

    /// <summary>The line when the sweep found nothing to do, so an idle sweep is told apart from one that never ran.</summary>
    public static string IdleLine(string why, int holdersRead, long ms) =>
        $"[Beelz MODLEAK] sweep ({Safe(why)}): nothing stale among {holdersRead} player holder(s); {ms} ms.";

    /// <summary>The one line per sweep pass that found anything. A capped pass read only the first <see cref="Cap"/>
    /// player holders in query order; the rest are not read until earlier ones are cleaned (the line says so).</summary>
    public static string SweepLine(string why, int cleaned, int modsCleared, int keptOnce, int unknown, bool capped, long ms) =>
        $"[Beelz MODLEAK] sweep ({Safe(why)}): cleaned {cleaned} stale holder(s), {modsCleared} leftover mod(s) removed; " +
        $"{keptOnce} seen stale once (rechecked next pass), {unknown} unreadable{(capped ? $"; capped at {Cap} holders, the rest not read" : "")}; {ms} ms.";

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
