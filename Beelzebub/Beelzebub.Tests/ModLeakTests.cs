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
