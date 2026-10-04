using System.Collections.Generic;
using Beelzebub.Logic;
using Xunit;

// modid-remap-errors — a slot-override holder is cleaned only when none of its rows still sets a slot (id AND source),
// only after the engine initialized it, never on an unreadable slot, and only when seen stale in two reads.
public class ModLeakTests
{
    static readonly LeakHolder H = new(327222, 1);
    static readonly LeakHolder Other = new(327223, 1);

    // the 2026-10-04 bar-raw dump of slot 2, GroupGuid section: one live mod from the spellbook holder 327215
    static SlotModParse Slot2() => SlotModDump.ParseGroupGuid(
        "- AbilityGroupSlot.GroupGuid: PrefabGuid(-433204738) (Base: PrefabGuid(-433204738))\n" +
        "    [ModId 1433] Set PrefabGuid(-433204738) from Entity(327215:1) (No PrefabGUID, Entity Name '')\n" +
        "- AbilityGroupSlot.CopyCooldown: True (Base: True)\n" +
        "    [ModId 1540] Set True from Entity(327223:1) (No PrefabGUID, Entity Name '')\n");

    static SlotModParse Live(int id, LeakHolder h)
    {
        var p = new SlotModParse();
        p.Entries.Add(new SlotModEntry(id, 375131842, h.Index, h.Version));
        return p;
    }

    [Fact]
    public void Row_fails_when_an_id_live_under_another_source_reads_live()
    {
        // 327222's dead id was remapped onto 1433 (another holder's GroupGuid mod): still dead for 327222
        Assert.Equal(LeakVerdict.Dangling, ModLeak.Row(1433, Slot2(), H));
        Assert.Equal(LeakVerdict.Live, ModLeak.Row(1433, Slot2(), new LeakHolder(327215, 1)));
    }

    [Fact]
    public void Row_fails_when_a_copycooldown_id_counts_as_a_slot_setter()
    {
        Assert.Equal(LeakVerdict.Dangling, ModLeak.Row(1540, Slot2(), Other));   // 1540 is a CopyCooldown mod
    }

    [Fact]
    public void Row_fails_when_an_unreadable_slot_or_empty_id_reads_dangling()
    {
        Assert.Equal(LeakVerdict.Unknown, ModLeak.Row(1587, null, H));
        Assert.Equal(LeakVerdict.Unknown, ModLeak.Row(1587, SlotModParse.Failed(), H));
        Assert.Equal(LeakVerdict.Empty, ModLeak.Row(0, Slot2(), H));
    }

    [Fact]
    public void Holder_fails_when_a_holder_with_one_live_row_reads_stale()
    {
        var rows = new List<HolderRowInput> { new(1540, Slot2()), new(1542, Live(1542, H)), new(1521, Slot2()) };
        Assert.Equal(HolderVerdict.Live, ModLeak.Holder(H, true, rows));
    }

    [Fact]
    public void Holder_fails_when_a_dead_holder_is_not_stale()
    {
        var rows = new List<HolderRowInput> { new(1540, Slot2()), new(1542, Slot2()), new(0, Slot2()) };
        Assert.Equal(HolderVerdict.Stale, ModLeak.Holder(H, true, rows));
    }

    [Fact]
    public void Holder_fails_when_an_unfinished_or_unreadable_holder_is_stale()
    {
        var dead = new List<HolderRowInput> { new(1540, Slot2()) };
        Assert.Equal(HolderVerdict.Building, ModLeak.Holder(H, false, dead));
        Assert.Equal(HolderVerdict.Unknown, ModLeak.Holder(H, true, new List<HolderRowInput> { new(1540, Slot2()), new(1587, null) }));
        Assert.Equal(HolderVerdict.Empty, ModLeak.Holder(H, true, new List<HolderRowInput>()));
        Assert.Equal(HolderVerdict.Empty, ModLeak.Holder(H, true, new List<HolderRowInput> { new(0, Slot2()) }));
        Assert.Equal(HolderVerdict.Empty, ModLeak.Holder(H, true, null));
    }

    [Fact]
    public void Confirm_fails_when_a_holder_seen_stale_once_is_cleaned()
    {
        var a = new LeakHolder(327217, 1);
        var first = new HashSet<LeakHolder> { a, H };
        var second = new HashSet<LeakHolder> { a, new LeakHolder(400000, 1), new LeakHolder(H.Index, 2) };   // H's index recycled
        Assert.Equal(new HashSet<LeakHolder> { a }, ModLeak.Confirm(first, second));
        Assert.Empty(ModLeak.Confirm(null, second));
    }

    [Fact]
    public void Select_fails_when_a_holder_that_is_not_stale_now_is_cleaned()
    {
        var a = new LeakHolder(1, 1); var b = new LeakHolder(2, 1); var c = new LeakHolder(3, 1); var d = new LeakHolder(4, 1);
        var e = new LeakHolder(5, 1);
        var first = new HashSet<LeakHolder> { a, b, c, d, e };   // all stale in the first read
        var second = new List<(LeakHolder, HolderVerdict)>
        {
            (a, HolderVerdict.Stale), (b, HolderVerdict.Live), (c, HolderVerdict.Unknown), (d, HolderVerdict.Building),
            (e, HolderVerdict.Empty),
        };
        Assert.Equal(new HashSet<LeakHolder> { a }, ModLeak.Select(first, second));
        Assert.Empty(ModLeak.Select(null, second));
        Assert.Empty(ModLeak.Select(first, null));
    }

    [Fact]
    public void Page_fails_when_a_holder_past_the_cap_is_never_read()
    {
        var idx = new List<int> { 10, 20, 30, 40, 50, 60, 70 };
        var seen = new HashSet<int>();
        int cursor = 0;
        for (int pass = 0; pass < 3; pass++)   // ceil(7 / 3) passes, every earlier holder live (never removed)
        {
            var (read, next) = ModLeak.Page(idx, cursor, 3);
            Assert.True(read.Count <= 3);
            seen.UnionWith(read);
            cursor = next;
        }
        Assert.Equal(idx.Count, seen.Count);
        var (all, end) = ModLeak.Page(new List<int> { 10, 20 }, 0, 3);   // everything fits: no cursor left over
        Assert.Equal(new List<int> { 10, 20 }, all);
        Assert.Equal(0, end);
        Assert.Empty(ModLeak.Page(new List<int>(), 5, 3).Read);
        Assert.Equal(new List<int> { 10, 20, 30 }, ModLeak.Page(idx, 70, 3).Read);   // a cursor at the end wraps
    }

    [Fact]
    public void MorePages_fails_when_a_server_over_the_cap_never_idles()
    {
        var idx = new List<int> { 10, 20, 30, 40, 50, 60, 70 };   // 7 holders, cap 3: never one page
        int cursor = 0, cycleRead = 0, passes = 0;
        bool more = true;
        while (more && passes < 10)
        {
            var (read, next) = ModLeak.Page(idx, cursor, 3);
            more = next != 0 && ModLeak.MorePages(cycleRead + read.Count, idx.Count);
            cycleRead = more ? cycleRead + read.Count : 0;
            cursor = more ? next : 0;
            passes++;
        }
        Assert.Equal(3, passes);   // ceil(7 / 3), then the sweep may sleep
        Assert.True(ModLeak.MorePages(3, 7));
        Assert.False(ModLeak.MorePages(9, 7));
    }

    [Fact]
    public void Arm_fails_when_repeated_pops_postpone_the_pass()
    {
        var t0 = new System.DateTime(2026, 10, 4, 12, 0, 0, System.DateTimeKind.Utc);
        var gap = System.TimeSpan.FromSeconds(2);
        var due = ModLeak.Arm(System.DateTime.MaxValue, t0, gap);
        Assert.Equal(t0 + gap, due);
        for (int i = 1; i <= 100; i++) due = ModLeak.Arm(due, t0.AddMilliseconds(500 * i), gap);   // a pop every 0.5 s
        Assert.Equal(t0 + gap, due);
    }

    [Fact]
    public void IdleLine_fails_when_an_idle_boot_is_silent_or_unnamed()
    {
        Assert.Equal("[Beelz MODLEAK] sweep (boot): nothing stale among 11 player holder(s); 4 ms.", ModLeak.IdleLine("boot", 11, 4));
    }

    [Fact]
    public void LogIdle_fails_when_a_pop_pass_is_silent_under_verbose()
    {
        Assert.True(ModLeak.LogIdle("boot", false));
        Assert.True(ModLeak.LogIdle("bar-reset", true));
        Assert.False(ModLeak.LogIdle("grant", false));   // production logs stay quiet on every grant
    }

    [Fact]
    public void SweepLine_fails_when_a_count_or_the_cap_is_not_named()
    {
        Assert.Equal("[Beelz MODLEAK] sweep (boot): cleaned 7 stale holder(s), 22 leftover mod(s) removed; 0 seen stale once (rechecked next pass), 0 unreadable; 12 ms.",
            ModLeak.SweepLine("boot", 7, 22, 0, 0, false, 12));
        Assert.Contains($"capped at {ModLeak.Cap} holders", ModLeak.SweepLine("boot", 0, 0, 2, 1, true, 3));
    }

    [Fact]
    public void RowLine_fails_when_a_name_breaks_the_line()
    {
        string line = ModLeak.RowLine("noprefab[a,\nb]", new LeakHolder(327217, 1), HolderVerdict.Stale, 4, 1587, 1538, 1191,
            LeakVerdict.Dangling, 1335008684, null);
        Assert.DoesNotContain("\n", line);
        Assert.Equal("[Beelz MODLEAK] holder=327217:1 noprefab[a,b] holder-verdict=Stale target=- slot=4 mod=1587 row=Dangling copycd=1538 spellmod=1191 group=1335008684", line);
    }
}
