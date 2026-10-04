using Beelzebub.Logic;
using System;
using System.Collections.Generic;
using System.Text;
using ProjectM;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Beelzebub.Services;

/// <summary>
/// modid-remap-errors (v0.137.6): finds and cleans the stale slot-override holders behind the startup "Couldn't remap old
/// Modification Id" errors — prefab-less ReplaceAbilityOnSlotBuff_AllInitialized entities whose GroupGuid mods our pops
/// removed, left alive by their CopyCooldown / SpellModsSource mods. Every decision is in <see cref="ModLeak"/>.
/// The sweep runs from the heartbeat after a boot or a pop (<see cref="MarkDue"/>): two reads at least
/// <see cref="ReadGap"/> apart, and only holders stale in both are cleaned (their leftover mods cleared through the
/// engine's ClearLooseSourceModifications, then the entity destroyed). The pops themselves are unchanged.
/// </summary>
internal static class ModLeakService
{
    /// <summary>One holder as read: its rows, its verdict, and the slot entities it targets.</summary>
    internal sealed record HolderScan(Entity Holder, string HolderName, HolderVerdict Verdict, List<RowScan> Rows);

    internal readonly record struct RowScan(int Slot, Entity Target, Entity SlotEntity, int ModId, int CopyCooldownId,
        int SpellModId, int NewGroup, LeakVerdict Verdict);

    internal static readonly TimeSpan ReadGap = TimeSpan.FromSeconds(2);

    static DateTime _dueAt = DateTime.MaxValue;   // first read of the next pass; MaxValue = no pass due
    static string _dueWhy;
    static HashSet<LeakHolder> _firstRead;         // stale holders of the first read, null between passes
    static int _cursor;                            // entity index the next capped page starts after (ModLeak.Page)
    static int _passCursor;                        // the page start both reads of one pass use

    /// <summary>Ask for a sweep pass (boot, bar reset, grant). Cheap: only arms the heartbeat; repeated calls coalesce.</summary>
    internal static void MarkDue(string why)
    {
        // the first read lands ReadGap after the LAST pop; a pop mid-pass restarts the pass with two fresh reads
        var at = DateTime.UtcNow + ReadGap;
        _dueAt = _dueAt == DateTime.MaxValue || at > _dueAt ? at : _dueAt;
        _dueWhy = why;
        _firstRead = null;
    }

    /// <summary>Heartbeat: first read, then (≥ ReadGap later) the confirming read of the same page and the clean.</summary>
    internal static void Tick()
    {
        if (!Core.IsReady || DateTime.UtcNow < _dueAt) return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (_firstRead == null) _passCursor = _cursor;
        int cursor = _passCursor;
        var scan = Scan(PlayerTarget, ModLeak.Cap, ref cursor, out bool capped);
        var verdicts = new List<(LeakHolder, HolderVerdict)>(scan.Count);
        int stale = 0, unknown = 0;
        foreach (var h in scan)
        {
            verdicts.Add((new LeakHolder(h.Holder.Index, h.Holder.Version), h.Verdict));
            if (h.Verdict == HolderVerdict.Stale) stale++;
            else if (h.Verdict == HolderVerdict.Unknown) unknown++;
        }
        if (_firstRead == null)
        {
            if (stale == 0 && unknown == 0 && !capped)
            {
                // nothing to do; a boot says so, so an idle sweep is told apart from one that never ran
                if (_dueWhy == "boot") Core.Log.LogInfo(ModLeak.IdleLine(_dueWhy, scan.Count, clock.ElapsedMilliseconds));
                _cursor = 0;
                _dueAt = DateTime.MaxValue;
                return;
            }
            _firstRead = StaleOf(verdicts);
            _dueAt = DateTime.UtcNow + ReadGap;
            return;
        }
        var confirmed = ModLeak.Select(_firstRead, verdicts);
        int cleaned = 0, mods = 0, failed = 0;
        foreach (var h in scan)
        {
            if (!confirmed.Contains(new LeakHolder(h.Holder.Index, h.Holder.Version))) continue;
            if (Clean(h, out int removed)) { cleaned++; mods += removed; } else failed++;
        }
        int keptOnce = stale - confirmed.Count;
        Core.Log.LogInfo(ModLeak.SweepLine(_dueWhy ?? "due", cleaned, mods, keptOnce, unknown, capped, clock.ElapsedMilliseconds));
        _firstRead = null;
        _cursor = cursor;
        // a holder seen stale once, a failed clean or a capped pass gets another pass; an unreadable slot waits for the
        // next pop or boot (re-reading it every 2 s would not make it readable); else the sweep sleeps
        _dueAt = keptOnce > 0 || capped || failed > 0 ? DateTime.UtcNow + ReadGap : DateTime.MaxValue;
    }

    static HashSet<LeakHolder> StaleOf(List<(LeakHolder H, HolderVerdict V)> verdicts)
    {
        var set = new HashSet<LeakHolder>();
        foreach (var (h, v) in verdicts) if (v == HolderVerdict.Stale) set.Add(h);
        return set;
    }

    static bool PlayerTarget(Entity t) => t.Exists() && Core.EntityManager.HasComponent<PlayerCharacter>(t);

    /// <summary>Clear the holder's leftover mods through the engine, re-resolve its slots, destroy it. False (and a
    /// warning) when anything throws; a holder already gone or already being destroyed is skipped.</summary>
    static bool Clean(HolderScan h, out int removed)
    {
        removed = 0;
        var em = Core.EntityManager;
        Entity holder = h.Holder;
        if (!holder.Exists() || em.HasComponent<DestroyTag>(holder)) return false;
        try
        {
            removed = Core.ServerGameManager.Modifications.ClearLooseSourceModifications(holder, ref em);
            var dirty = ComponentType.ReadWrite(Il2CppInterop.Runtime.Il2CppType.Of<AbilityGroupSlot.DirtyTag>());
            foreach (var r in h.Rows)
                if (r.SlotEntity.Exists() && !em.HasComponent(r.SlotEntity, dirty)) em.AddComponent(r.SlotEntity, dirty);
            DestroyUtility.Destroy(em, holder, DestroyDebugReason.None);
            var t = h.Rows.Count > 0 ? h.Rows[0].Target : Entity.Null;
            Core.Log.LogInfo($"[Beelz MODLEAK] cleaned holder={holder.Index}:{holder.Version} target={(t.Exists() ? t.GetSteamId().ToString() : "-")} rows={h.Rows.Count} leftover-mods={removed}");
            return true;
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz MODLEAK] clean holder={holder.Index}:{holder.Version} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Read every live holder (prefabs and disabled entities are not in the default query) whose rows ALL target
    /// an entity accepted by <paramref name="targetFilter"/>, and judge it. Read-only. At most <paramref name="cap"/>
    /// holders.</summary>
    internal static List<HolderScan> Scan(Func<Entity, bool> targetFilter, int cap, ref int cursor, out bool capped)
    {
        capped = false;
        var result = new List<HolderScan>();
        var em = Core.EntityManager;
        var reg = Core.ServerGameManager.Modifications;
        var allInit = ComponentType.ReadOnly(Il2CppInterop.Runtime.Il2CppType.Of<ReplaceAbilityOnSlotBuff_AllInitialized>());
        var query = em.CreateEntityQuery(ComponentType.ReadOnly<AbilityGroupSlotModificationBuffer>());
        var holders = query.ToEntityArray(Allocator.Temp);
        var slotCache = new Dictionary<Entity, SlotModParse>();
        try
        {
            // page in ascending entity index from the cursor (ModLeak.Page), so holders past the cap are reached too
            var byIndex = new Dictionary<int, Entity>(holders.Length);
            for (int h = 0; h < holders.Length; h++) byIndex[holders[h].Index] = holders[h];
            var sorted = new List<int>(byIndex.Keys);
            sorted.Sort();
            var (page, next) = ModLeak.Page(sorted, cursor, cap);
            capped = next != 0;
            cursor = next;
            foreach (int idx in page)
            {
                Entity holder = byIndex[idx];
                if (!holder.Exists()) continue;
                var buf = em.GetBuffer<AbilityGroupSlotModificationBuffer>(holder);
                if (buf.Length == 0) continue;
                var copy = new List<AbilityGroupSlotModificationBuffer>(buf.Length);
                for (int i = 0; i < buf.Length; i++) copy.Add(buf[i]);
                bool ours = true;
                foreach (var r in copy) if (targetFilter != null && !targetFilter(r.Target)) { ours = false; break; }
                if (!ours) continue;
                var lh = new LeakHolder(holder.Index, holder.Version);
                var rows = new List<RowScan>(copy.Count);
                var inputs = new List<HolderRowInput>(copy.Count);
                foreach (var r in copy)
                {
                    Entity slotEnt = ResolveSlot(r.Target, r.Slot);
                    if (!slotCache.TryGetValue(slotEnt, out var parse))
                    {
                        parse = slotEnt == Entity.Null || !slotEnt.Exists() ? SlotModParse.Failed() : ReadSlot(reg, em, slotEnt);
                        slotCache[slotEnt] = parse;
                    }
                    int id = r.ModificationId.Id;
                    inputs.Add(new HolderRowInput(id, parse));
                    rows.Add(new RowScan(r.Slot, r.Target, slotEnt, id, r.CopyCooldownModificationId.Id,
                        r.SpellModModificationId.Id, r.NewAbilityGroup._Value, ModLeak.Row(id, parse, lh)));
                }
                var verdict = ModLeak.Holder(lh, em.HasComponent(holder, allInit), inputs);
                result.Add(new HolderScan(holder, Describe(holder), verdict, rows));
            }
        }
        finally { holders.Dispose(); }
        return result;
    }

    /// <summary>The slot entity a row modifies: the target itself when it is a slot, else the target's slot buffer entry.</summary>
    static Entity ResolveSlot(Entity target, int slot)
    {
        var em = Core.EntityManager;
        if (!target.Exists()) return Entity.Null;
        if (em.HasComponent<AbilityGroupSlot>(target)) return target;
        if (!em.HasBuffer<AbilityGroupSlotBuffer>(target)) return Entity.Null;
        var slots = em.GetBuffer<AbilityGroupSlotBuffer>(target);
        return slot >= 0 && slot < slots.Length ? slots[slot].GroupSlotEntity._Entity : Entity.Null;
    }

    static SlotModParse ReadSlot(ModificationsRegistry reg, EntityManager em, Entity slotEnt)
    {
        try
        {
            var sb = new Il2CppSystem.Text.StringBuilder();
            reg.GetFormattedEntityModificationsMessage(sb, em, slotEnt);
            return SlotModDump.ParseGroupGuid(sb.ToString());
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[Beelz MODLEAK] dump of {slotEnt} failed: {ex.Message}");
            return SlotModParse.Failed();
        }
    }

    /// <summary>Prefab name, else "noprefab[<first component types>]" — the report names what holds the rows.</summary>
    internal static string Describe(Entity e)
    {
        var em = Core.EntityManager;
        if (!e.Exists()) return e == Entity.Null ? "null" : $"gone({e.Index}:{e.Version})";
        try
        {
            if (em.HasComponent<PlayerCharacter>(e)) return "PlayerCharacter";
            if (em.HasComponent<PrefabGUID>(e))
            {
                var pg = em.GetComponentData<PrefabGUID>(e);
                string n = pg.GetPrefabName();
                return string.IsNullOrEmpty(n) ? $"prefab#{pg._Value}" : n;
            }
            var types = em.GetComponentTypes(e, Allocator.Temp);
            try
            {
                var sb = new StringBuilder("noprefab[");
                for (int i = 0; i < types.Length && i < 8; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(types[i].GetManagedType()?.Name ?? types[i].TypeIndex.ToString());
                }
                return sb.Append(']').ToString();
            }
            finally { types.Dispose(); }
        }
        catch (Exception ex) { return $"unreadable({ex.GetType().Name})"; }
    }
}
