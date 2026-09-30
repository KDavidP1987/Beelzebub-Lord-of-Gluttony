using System.Linq;
using Beelzebub.Logic;
using Xunit;

// bar-reset D16 — fixed-schema, single-line reset / bar / late-survivor log lines.
public class BarResetLogTests
{
    static BarResetResult Result(bool unreadable = false, params int[] survivorSlots)
    {
        var r = new BarResetResult
        {
            Readback = new BarReadback { Slots = survivorSlots.Select(s => new BarSlotReading { Slot = s, Other = 1 }).ToList() },
            Unreadable = unreadable,
        };
        r.Steps.Add(new BarResetStepResult(BarResetStep.ClearSavedBindings, 3, null));
        r.Steps.Add(new BarResetStepResult(BarResetStep.PopSlotMods, 0, "boom"));
        return r;
    }

    [Fact]
    public void Format_prints_every_field_in_order()
    {
        string line = BarResetLog.Format(Result(), BarResetScope.PlayerReset, "PerpetualChaos", 76561198039548286UL, 42, 7);
        Assert.Equal(
            "[Beelz RESET] run=7 scope=PlayerReset target=PerpetualChaos (76561198039548286) ms=42 " +
            "steps=ClearSavedBindings:3,PopSlotMods:ERR survivors=none clean=0", line);
    }

    [Fact]
    public void Format_fails_when_a_name_with_a_newline_yields_two_lines()
    {
        string line = BarResetLog.Format(Result(), BarResetScope.Purge, "Evil\n[Beelz RESET] forged\r", 1, 5, 1);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal(1, line.Split("[Beelz RESET]").Length - 1);
    }

    [Fact]
    public void Format_fails_when_a_step_is_missing()
    {
        string line = BarResetLog.Format(Result(), BarResetScope.Purge, "x", 1, 5, 1);
        Assert.Contains("ClearSavedBindings:3", line);
        Assert.Contains("PopSlotMods:ERR", line);
    }

    [Fact]
    public void Format_fails_when_unreadable_prints_survivors_none()
    {
        Assert.EndsWith("survivors=unreadable clean=0", BarResetLog.Format(Result(unreadable: true), BarResetScope.Purge, "x", 1, 5, 1));
        Assert.EndsWith("survivors=1,4 clean=0", BarResetLog.Format(Result(false, 1, 4), BarResetScope.Purge, "x", 1, 5, 1));
    }

    [Fact]
    public void Format_fails_when_a_slow_run_lacks_slow_flag()
    {
        Assert.EndsWith(" slow=1", BarResetLog.Format(Result(), BarResetScope.Purge, "x", 1, 300, 1));
        Assert.DoesNotContain("slow=", BarResetLog.Format(Result(), BarResetScope.Purge, "x", 1, 250, 1));
    }

    [Fact]
    public void Format_with_no_steps_is_still_one_line()
    {
        string line = BarResetLog.Format(new BarResetResult(), BarResetScope.Purge, "", 1, 0, 1);
        Assert.Contains("steps=none", line);
        Assert.Contains("target=? (1)", line);
    }

    [Fact]
    public void LogSafe_fails_when_brackets_or_long_names_pass_through()
    {
        Assert.Equal("abc", LogSafe.Field("[a]b\tc"));
        Assert.Equal(32, LogSafe.Field(new string('x', 100)).Length);
        Assert.Equal("?", LogSafe.Field("\n\n"));
    }

    [Fact]
    public void FormatBar_fails_when_an_unreadable_slot_prints_other_zero()
    {
        var rb = new BarReadback { Slots = { new BarSlotReading { Slot = 3, Unreadable = true } } };
        string line = BarResetLog.FormatBar(rb, "x", 1).Single();
        Assert.Contains("other=unreadable", line);
        Assert.Contains("3:none:0:unreadable", line);
    }

    [Fact]
    public void FormatBar_prints_binds_rows_and_slots()
    {
        var rb = new BarReadback
        {
            Slots =
            {
                new BarSlotReading { Slot = 1, Bind = "weapon:Sword", Row = true, Gear = 1 },
                new BarSlotReading { Slot = 4, Bind = "weapon:Sword", Row = true, Gear = 1 },
                new BarSlotReading { Slot = 5 },
            },
        };
        Assert.Equal(
            "[Beelz BAR] target=PerpetualChaos (9) binds=2 rows=2 gear=2 other=0 slots=1:bind:1:0,4:bind:1:0",
            BarResetLog.FormatBar(rb, "PerpetualChaos", 9).Single());
    }

    [Fact]
    public void FormatBar_of_a_clean_bar_says_slots_none()
    {
        Assert.EndsWith("binds=0 rows=0 gear=0 other=0 slots=none", BarResetLog.FormatBar(new BarReadback(), "x", 1).Single());
    }

    [Fact]
    public void FormatBar_fails_when_a_long_slot_list_is_not_split_into_parts()
    {
        var rb = new BarReadback();
        for (int i = 0; i < 296; i++) rb.Slots.Add(new BarSlotReading { Slot = i, Other = 2, Gear = 1 });
        var lines = BarResetLog.FormatBar(rb, "x", 1);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(l.Length <= BarResetLog.MaxBarLineChars, $"line of {l.Length} chars"));
        Assert.All(lines, l => Assert.StartsWith("[Beelz BAR] target=x (1) binds=0", l));
        Assert.EndsWith($"part={lines.Count}/{lines.Count}", lines[^1]);
        int cells = lines.Sum(l => l.Split("slots=")[1].Split(" part=")[0].Split(',').Length);
        Assert.Equal(296, cells);
    }

    [Fact]
    public void FormatLate_carries_target_and_run()
    {
        Assert.Equal("[Beelz RESET] late-survivor target=Bob (5) run=3 slot=4", BarResetLog.FormatLate("Bob", 5, 3, 4));
    }

    [Fact]
    public void Format_fails_when_an_unclean_reset_prints_clean_1()
    {
        // survivors=none but a failed step (Result() has PopSlotMods:ERR) — never clean=1
        Assert.Contains("survivors=none clean=0", BarResetLog.Format(Result(), BarResetScope.PlayerReset, "x", 1, 5, 1));
        var ok = new BarResetResult { Clean = true, Readback = new BarReadback() };
        Assert.Contains("survivors=none clean=1", BarResetLog.Format(ok, BarResetScope.PlayerReset, "x", 1, 5, 1));
    }

    [Fact]
    public void FormatForm_fails_when_an_emptied_bar_is_not_native_or_the_target_is_missing()
    {
        Assert.Equal("[Beelz FORM] target=7 form=Wolf source=native", BarResetLog.FormatForm(7, "Wolf", FormBarSource.Universal, 0));
        Assert.Equal("[Beelz FORM] target=7 form=Wolf source=universal", BarResetLog.FormatForm(7, "Wolf", FormBarSource.Universal, 2));
    }
}
