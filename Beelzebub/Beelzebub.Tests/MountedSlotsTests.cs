using System.Collections.Generic;
using Beelzebub.Logic;
using Xunit;

// mounted-bar-reset D1 D2 — the saddle keys are R, C, T (5, 6, 7); an old slot-3 bind moves to R once.
public class MountedSlotsTests
{
    [Fact]
    public void IsValid_fails_when_slot_3_or_a_riding_key_is_allowed()
    {
        Assert.Equal(new[] { 5, 6, 7 }, MountedSlots.Allowed);
        foreach (int s in new[] { 5, 6, 7 }) Assert.True(MountedSlots.IsValid(s), $"slot {s} must be a saddle slot");
        foreach (int s in new[] { 0, 1, 2, 3, 4, 8 }) Assert.False(MountedSlots.IsValid(s), $"slot {s} must not be a saddle slot");
    }

    [Fact]
    public void RejectMessage_fails_when_it_names_slot_3()
    {
        Assert.Equal("5, 6, 7 (the R, C, and Ultimate keys)", MountedSlots.Hint);
        Assert.Equal("Mounted form only uses slots 5, 6, 7 (the R, C, and Ultimate keys) — the other slots are riding controls (Q/E/space) and can't hold a saddle ability. Re-grant this to slot 5, 6, or 7.",
            MountedSlots.RejectMessage());
    }

    [Fact]
    public void Migrate_fails_when_a_slot_3_bind_survives()
    {
        var m = new Dictionary<int, int> { [3] = 1621601748, [6] = 42 };
        Assert.Equal(MountedMigration.Moved, MountedSlots.Migrate(m));
        Assert.Equal(new Dictionary<int, int> { [5] = 1621601748, [6] = 42 }, m);
    }

    [Fact]
    public void Migrate_fails_when_a_taken_slot_5_is_overwritten()
    {
        var m = new Dictionary<int, int> { [3] = 1621601748, [5] = 99, [7] = 7 };
        Assert.Equal(MountedMigration.Dropped, MountedSlots.Migrate(m));
        Assert.Equal(new Dictionary<int, int> { [5] = 99, [7] = 7 }, m);
    }

    [Fact]
    public void Migrate_fails_when_a_second_call_or_an_empty_map_reports_a_change()
    {
        var m = new Dictionary<int, int> { [3] = 1621601748 };
        MountedSlots.Migrate(m);
        Assert.Equal(MountedMigration.None, MountedSlots.Migrate(m));
        var empty = new Dictionary<int, int>();
        Assert.Equal(MountedMigration.None, MountedSlots.Migrate(empty));
        Assert.Empty(empty);
        Assert.Equal(MountedMigration.None, MountedSlots.Migrate(null));
    }

    [Fact]
    public void Migrate_fails_when_10000_players_take_over_200ms()
    {
        var maps = new List<Dictionary<int, int>>();
        for (int i = 0; i < 10000; i++) maps.Add(new Dictionary<int, int> { [3] = i + 1, [6] = 42 });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int moved = 0;
        foreach (var m in maps) if (MountedSlots.Migrate(m) == MountedMigration.Moved) moved++;
        sw.Stop();
        Assert.Equal(10000, moved);
        Assert.True(sw.ElapsedMilliseconds < 200, $"10000 migrations took {sw.ElapsedMilliseconds} ms");
    }
}
