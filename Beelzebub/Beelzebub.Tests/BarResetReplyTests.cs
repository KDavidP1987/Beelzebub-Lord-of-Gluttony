using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Beelzebub.Logic;
using Xunit;
using S = Beelzebub.Logic.BarResetStep;

// bar-reset D6 D11 D24 D27 + Design › UX — the chat replies: at most two lines per reset, under VCF's 512-byte cap,
// no `<` `>`, player slot labels, and every not-clean cause named.
public class BarResetReplyTests
{
    static BarResetResult Run(BarResetScope scope, bool online = true, bool liveReady = true,
        bool saved = true, S? fail = null, BarReadback reading = null, bool mounted = false)
    {
        var ops = new Ops { SaveResult = saved, Throw = fail, Reading = reading ?? new BarReadback { Slots = { new BarSlotReading { Slot = 0 } } } };
        return BarResetRunner.Run(ops, BarResetPlanner.Plan(scope, online, liveReady, false, mounted), online, liveReady);
    }

    static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    [Fact]
    public void Clean_reset_says_vanilla_and_resetbar_points_at_admin_bar()
    {
        var lines = BarResetReply.ForReset(Run(BarResetScope.PlayerReset), BarResetScope.PlayerReset, "Perpetual");
        Assert.Equal(2, lines.Count);
        Assert.Contains("back to vanilla", lines[0]);
        Assert.Equal(BarResetReply.AskAdmin, lines[1]);
    }

    [Fact]
    public void ForReset_fails_when_a_surviving_override_buff_is_not_named()
    {
        var bar = new BarReadback { Slots = { new BarSlotReading { Slot = 0 } }, OverrideBuffs = { "AB_Shapeshift_Wolf_Buff" } };
        var lines = BarResetReply.ForReset(Run(BarResetScope.PlayerReset, reading: bar), BarResetScope.PlayerReset, "P");
        Assert.Contains("NOT clean", lines[0]);
        Assert.Contains("override buff still on: AB_Shapeshift_Wolf_Buff", lines[1]);
    }

    [Fact]
    public void ForReset_fails_when_a_not_reachable_bar_is_reported_clean()
    {
        var lines = BarResetReply.ForReset(Run(BarResetScope.PlayerReset, liveReady: false), BarResetScope.PlayerReset, "P");
        Assert.Contains(BarResetReply.NotReachable, lines[0]);
        Assert.DoesNotContain(lines, l => l.Contains("back to vanilla"));
    }

    [Fact]
    public void ForReset_fails_when_an_offline_target_is_not_told_the_bar_resets_on_login()
    {
        var lines = BarResetReply.ForReset(Run(BarResetScope.Purge, online: false), BarResetScope.Purge, "P");
        Assert.Contains("next login", lines[0]);
        Assert.Contains("hotkey(s)", lines[0]);
        Assert.DoesNotContain(lines, l => l.Contains("back to vanilla"));
    }

    [Fact]
    public void ForReset_fails_when_an_unsaved_reset_lacks_the_not_saved_line()
    {
        var lines = BarResetReply.ForReset(Run(BarResetScope.AdminLoadouts, saved: false), BarResetScope.AdminLoadouts, "P");
        Assert.Contains("NOT clean", lines[0]);
        Assert.Contains(BarResetReply.NotSaved, lines[1]);
    }

    [Fact]
    public void ForReset_fails_when_an_unreadable_bar_lacks_could_not_read()
    {
        var bar = new BarReadback { Slots = { new BarSlotReading { Slot = 3, Unreadable = true } } };
        var lines = BarResetReply.ForReset(Run(BarResetScope.PlayerReset, reading: bar), BarResetScope.PlayerReset, "P");
        Assert.Contains(BarResetReply.CouldNotRead, lines[1]);
    }

    [Fact]
    public void ForReset_fails_when_survivors_lack_the_next_ladder_rung()
    {
        var bar = new BarReadback { Slots = { new BarSlotReading { Slot = 0, Other = 1 }, new BarSlotReading { Slot = 7, Row = true } } };
        var self = BarResetReply.ForReset(Run(BarResetScope.PlayerReset, reading: bar), BarResetScope.PlayerReset, "P");
        Assert.Contains("still overridden: primary, ultimate", self[1]);
        Assert.Contains("ask an admin for .beelz admin bar", self[1]);
        var admin = BarResetReply.ForReset(Run(BarResetScope.AdminLoadouts, reading: bar), BarResetScope.AdminLoadouts, "Perp");
        Assert.Contains(".beelz admin purge Perp CONFIRM", admin[1]);
        var purge = BarResetReply.ForReset(Run(BarResetScope.Purge, reading: bar), BarResetScope.Purge, "Perp");
        Assert.Contains(".beelz admin reset-character Perp CONFIRM-RESET", purge[1]);
    }

    [Fact]
    public void ForReset_fails_when_a_reset_replies_in_more_than_two_lines()
    {
        var bar = new BarReadback { Slots = Enumerable.Range(0, 9).Select(i => new BarSlotReading { Slot = i, Other = 1 }).ToList() };
        bar.Slots.Add(new BarSlotReading { Slot = 5, Unreadable = true });
        var r = Run(BarResetScope.Purge, saved: false, fail: S.PopSlotMods, reading: bar);
        var lines = BarResetReply.ForReset(r, BarResetScope.Purge, "P");
        Assert.True(lines.Count <= 2, string.Join(" | ", lines));
        Assert.Contains(BarResetReply.NotSaved, lines[1]);
        Assert.Contains("PopSlotMods", lines[1]);
        Assert.Contains(BarResetReply.CouldNotRead, lines[1]);
    }

    [Fact]
    public void Replies_fail_when_a_line_has_angle_brackets_or_breaks_the_byte_cap()
    {
        string name = "<b>" + new string('é', 40) + "</b>";
        var bar = new BarReadback
        {
            Slots = Enumerable.Range(0, 9).Select(i => new BarSlotReading
            {
                Slot = i, Ability = "<AB_" + new string('x', 200), Bind = "weapon:Sword", Gear = 3,
                GearSources = { new string('g', 300), "EquipBuff_Weapon_Sword_Base", "<Item_Ring>" },
            }).ToList(),
            OverrideBuffs = Enumerable.Range(0, 30).Select(i => $"AB_Shapeshift_{i}_{new string('z', 40)}").ToList(),
        };
        var all = BarResetReply.ForReset(Run(BarResetScope.AdminLoadouts, reading: bar), BarResetScope.AdminLoadouts, name)
            .Concat(BarResetReply.ForBar(bar, name)).ToList();
        Assert.All(all, l =>
        {
            Assert.True(Bytes(l) <= BarResetReply.MaxReplyBytes, $"{Bytes(l)} bytes: {l}");
            Assert.DoesNotContain("<", l);
            Assert.DoesNotContain(">", l);
            Assert.DoesNotContain("\n", l);
        });
    }

    [Fact]
    public void Name_fails_when_markup_control_characters_or_long_names_pass_through()
    {
        string n = BarResetReply.Name("<color=red>\n[b]" + new string('x', 80));
        Assert.DoesNotContain("<", n);
        Assert.DoesNotContain(">", n);
        Assert.DoesNotContain("\n", n);
        Assert.DoesNotContain("[", n);
        Assert.True(n.Length <= LogSafe.MaxNameLength, n);
        Assert.Equal("?", BarResetReply.Name(null));
    }

    [Fact]
    public void ForBar_fails_when_offline_does_not_print_the_saved_state()
    {
        var rb = new BarReadback { Offline = true, SavedSets = { "universal: slots 2" }, Transform = "none", Hotkeys = 1 };
        var lines = BarResetReply.ForBar(rb, "Alt");
        Assert.Equal(BarResetReply.OfflineBar, lines[0]);
        Assert.Contains("universal: slots 2", lines[1]);
        Assert.Equal("transform: none · hotkeys=1", lines[2]);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void ForBar_fails_when_slots_are_not_labelled_as_the_player_sees_them()
    {
        var rb = new BarReadback
        {
            Slots = Enumerable.Range(0, 9).Select(i => new BarSlotReading { Slot = i, Ability = $"AB_{i}" }).ToList(),
        };
        rb.Slots[1] = new BarSlotReading { Slot = 1, Ability = "AB_1", Bind = "weapon:Sword", Row = true, Other = 2 };
        rb.Slots[4] = new BarSlotReading { Slot = 4, Unreadable = true };
        var lines = BarResetReply.ForBar(rb, "P");
        Assert.Equal(2 + 9, lines.Count);                       // header, override buffs, 9 slot lines
        Assert.Equal("override buffs: none", lines[1]);
        Assert.StartsWith("primary: AB_0", lines[2]);
        Assert.StartsWith("slot 1: AB_1 · bind=weapon:Sword · row=yes · gear=0 · other=2", lines[3]);
        Assert.StartsWith("slot 4: ? ", lines[6]);
        Assert.EndsWith("other=unreadable", lines[6]);
        Assert.StartsWith("ultimate: AB_7", lines[9]);
        Assert.StartsWith("slot 8: AB_8", lines[10]);
        Assert.Contains("other=unreadable", lines[0]);
    }

    [Fact]
    public void Cap_fails_when_it_splits_a_surrogate_pair()
    {
        // 474 ASCII bytes: a lone high surrogate (3 bytes as UTF-8) would still fit under the 477-byte budget.
        string s = new string('a', BarResetReply.MaxReplyBytes - 6) + string.Concat(Enumerable.Repeat("😀", 10));
        string c = BarResetReply.Cap(s);
        Assert.True(Bytes(c) <= BarResetReply.MaxReplyBytes);
        Assert.EndsWith("...", c);
        Assert.False(char.IsHighSurrogate(c[c.Length - 4]), "a lone high surrogate precedes the ellipsis");
        Assert.Equal("", BarResetReply.Cap(null));
    }

    sealed class Ops : IBarResetOps
    {
        public S? Throw;
        public bool SaveResult = true;
        public BarReadback Reading;
        int Hit(S s) => Throw == s ? throw new InvalidOperationException($"{s} boom") : 1;
        public int RevertTransform() => Hit(S.RevertTransform);
        public int ClearSavedBindings(bool keepTransformRecord) => Hit(S.ClearSavedBindings);
        public int ClearHotkeys() => Hit(S.ClearHotkeys);
        public bool SaveBindings() => SaveResult;
        public int ClearEquipEntries() => Hit(S.ClearEquipEntries);
        public int Dismount() => Hit(S.Dismount);
        public int DestroyOverrideSources() => Hit(S.DestroyOverrideSources);
        public int PopSlotMods() => Hit(S.PopSlotMods);
        public int EmptyPush() => Hit(S.EmptyPush);
        public int Reapply() => Hit(S.Reapply);
        public BarReadback Readback() => Reading;
    }
}
