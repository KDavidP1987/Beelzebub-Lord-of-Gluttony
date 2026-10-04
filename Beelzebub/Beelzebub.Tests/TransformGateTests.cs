using System;
using System.Text;
using Beelzebub.Logic;
using Xunit;

// transform-chain-guard D1-D3 — the one gate every transform route asks before it touches a form.
public class TransformGateTests
{
    const int Dracula = -327335305, Morgana = 591725925;
    static readonly TransformRoute[] Routes =
        { TransformRoute.Activate, TransformRoute.PhaseSwitch, TransformRoute.Reapply, TransformRoute.FormTest };

    [Fact]
    public void Decide_fails_when_a_pending_form_allows_any_route()
    {
        foreach (var r in Routes)
        {
            Assert.Equal(TransformGateVerdict.RefusePending, TransformGate.Decide(r, true, Morgana, Dracula));
            Assert.Equal(TransformGateVerdict.RefusePending, TransformGate.Decide(r, true, Morgana, Morgana));
            Assert.Equal(TransformGateVerdict.RefusePending, TransformGate.Decide(r, true, 0, Dracula));
        }
    }

    [Fact]
    public void Decide_fails_when_an_active_transform_allows_a_second_activate()
    {
        Assert.Equal(TransformGateVerdict.RefuseActive, TransformGate.Decide(TransformRoute.Activate, false, Dracula, Morgana));
    }

    [Fact]
    public void Decide_fails_when_the_same_unit_reads_as_another_unit()
    {
        Assert.Equal(TransformGateVerdict.RefuseSameUnit, TransformGate.Decide(TransformRoute.Activate, false, Dracula, Dracula));
    }

    [Fact]
    public void Decide_fails_when_a_free_player_is_refused()
    {
        Assert.Equal(TransformGateVerdict.Allow, TransformGate.Decide(TransformRoute.Activate, false, 0, Dracula));
        Assert.Equal(TransformGateVerdict.Allow, TransformGate.Decide(TransformRoute.FormTest, false, 0, 0));
        // a settled transform may switch phase / be re-applied; with none there is nothing to switch
        Assert.Equal(TransformGateVerdict.Allow, TransformGate.Decide(TransformRoute.PhaseSwitch, false, Dracula, 0));
        Assert.Equal(TransformGateVerdict.Allow, TransformGate.Decide(TransformRoute.Reapply, false, Dracula, 0));
        Assert.Equal(TransformGateVerdict.RefuseInactive, TransformGate.Decide(TransformRoute.PhaseSwitch, false, 0, 0));
        Assert.Equal(TransformGateVerdict.RefuseInactive, TransformGate.Decide(TransformRoute.Reapply, false, 0, 0));
    }

    [Fact]
    public void Decide_fails_when_formtest_chains_over_an_active_transform()
    {
        Assert.Equal(TransformGateVerdict.RefuseActive, TransformGate.Decide(TransformRoute.FormTest, false, Dracula, 0));
    }

    [Fact]
    public void Decide_fails_when_an_unknown_route_is_allowed()
    {
        Assert.Equal(TransformGateVerdict.RefuseUnknown, TransformGate.Decide((TransformRoute)99, false, 0, Dracula));
        Assert.Equal(TransformGateVerdict.RefuseUnknown, TransformGate.Decide((TransformRoute)99, false, Dracula, Dracula));
    }

    [Fact]
    public void Message_fails_when_a_refusal_text_drifts()
    {
        Assert.Null(TransformGate.Message(TransformGateVerdict.Allow, TransformRoute.Activate, "X"));
        Assert.Equal("Still transforming — give it a moment, then try again.",
            TransformGate.Message(TransformGateVerdict.RefusePending, TransformRoute.PhaseSwitch, "X"));
        Assert.Equal("You're already transformed as that unit. Use .beelz revert to return to normal.",
            TransformGate.Message(TransformGateVerdict.RefuseSameUnit, TransformRoute.Activate, "X"));
        Assert.Equal("You're already transformed as Dracula. Use .beelz revert first, then transform again.",
            TransformGate.Message(TransformGateVerdict.RefuseActive, TransformRoute.Activate, "Dracula"));
        Assert.Equal("You're already transformed as Dracula. Use .beelz revert first, then run testform again.",
            TransformGate.Message(TransformGateVerdict.RefuseActive, TransformRoute.FormTest, "Dracula"));
        Assert.Equal("You are not currently transformed. Use .beelz transform <unit> first.",
            TransformGate.Message(TransformGateVerdict.RefuseInactive, TransformRoute.PhaseSwitch, "X"));
        Assert.Equal("That transform action is not allowed.",
            TransformGate.Message(TransformGateVerdict.RefuseUnknown, (TransformRoute)99, "X"));
    }

    [Fact]
    public void Message_fails_when_a_text_exceeds_the_chat_cap()
    {
        string name = new string('W', 64);
        foreach (TransformGateVerdict v in Enum.GetValues(typeof(TransformGateVerdict)))
            foreach (var r in Routes)
            {
                string m = TransformGate.Message(v, r, name);
                if (m == null) continue;
                Assert.True(Encoding.UTF8.GetByteCount(m) <= 480, $"{v}/{r}: {m.Length}");
            }
    }

    [Theory]
    [InlineData(null, "another unit")]
    [InlineData("", "another unit")]
    [InlineData("   ", "another unit")]
    [InlineData("Dra\ncula\r", "Dracula")]
    [InlineData("<color=red>Dracula</color>", "color=redDracula/color")]
    [InlineData("\u0007<>", "another unit")]
    public void Message_fails_when_a_name_breaks_the_line(string raw, string shown)
    {
        string m = TransformGate.Message(TransformGateVerdict.RefuseActive, TransformRoute.Activate, raw);
        Assert.Equal($"You're already transformed as {shown}. Use .beelz revert first, then transform again.", m);
        Assert.DoesNotContain('\n', m);
        string longName = TransformGate.SafeName(new string('é', 500));
        Assert.Equal(TransformGate.MaxNameChars + 1, longName.Length);   // cut + ellipsis
        Assert.True(Encoding.UTF8.GetByteCount(
            TransformGate.Message(TransformGateVerdict.RefuseActive, TransformRoute.FormTest, new string('é', 500))) <= 480);
    }

    [Fact]
    public void GuardLog_fails_when_a_repeat_refusal_logs_again()
    {
        var log = new TransformGuardLog();
        Assert.True(log.ShouldLog(1, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending));
        for (int i = 0; i < 100; i++)
            Assert.False(log.ShouldLog(1, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending));
        // another reason, route or player is its own first line
        Assert.True(log.ShouldLog(1, TransformRoute.Activate, TransformGateVerdict.RefuseActive));
        Assert.True(log.ShouldLog(1, TransformRoute.Activate, TransformGateVerdict.RefuseSameUnit));
        Assert.True(log.ShouldLog(2, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending));
        Assert.False(log.ShouldLog(3, TransformRoute.Activate, TransformGateVerdict.Allow));
    }

    [Fact]
    public void GuardLog_fails_when_allow_or_forget_does_not_reset()
    {
        var log = new TransformGuardLog();
        log.ShouldLog(1, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending);
        log.ShouldLog(2, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending);
        log.Allowed(1);
        Assert.True(log.ShouldLog(1, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending));
        Assert.False(log.ShouldLog(2, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending));
        log.Forget(2);
        Assert.Equal(1, log.Count);
        Assert.True(log.ShouldLog(2, TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending));
    }

    [Fact]
    public void GuardLog_fails_when_it_grows_past_its_cap()
    {
        var log = new TransformGuardLog();
        for (ulong p = 1; p <= 10_000; p++)   // 100x a full server, each refusing once and never returning
        {
            Assert.True(log.ShouldLog(p, TransformRoute.Reapply, TransformGateVerdict.RefusePending));
            Assert.True(log.Count <= TransformGuardLog.Cap);
        }
    }

    [Fact]
    public void PhaseResetDue_fails_when_a_refused_reset_is_dropped()
    {
        Assert.True(TransformGate.PhaseResetDue(resetDue: true, inCombat: false, currentPhase: 2));
        Assert.True(TransformGate.PhaseResetDue(resetDue: true, inCombat: false, currentPhase: 3));
        Assert.False(TransformGate.PhaseResetDue(resetDue: true, inCombat: true, currentPhase: 2));    // auto-advance owns combat
        Assert.False(TransformGate.PhaseResetDue(resetDue: true, inCombat: false, currentPhase: 1));   // already reset
        Assert.False(TransformGate.PhaseResetDue(resetDue: false, inCombat: false, currentPhase: 2));  // a manual phase stays
    }

    [Theory]
    [InlineData(TransformRoute.Activate, TransformGateVerdict.RefuseActive, "route=Activate reason=Active")]
    [InlineData(TransformRoute.Activate, TransformGateVerdict.RefuseSameUnit, "route=Activate reason=SameUnit")]
    [InlineData(TransformRoute.PhaseSwitch, TransformGateVerdict.RefusePending, "route=PhaseSwitch reason=Pending")]
    [InlineData(TransformRoute.Reapply, TransformGateVerdict.RefuseInactive, "route=Reapply reason=Inactive")]
    [InlineData(TransformRoute.FormTest, TransformGateVerdict.RefuseActive, "route=FormTest reason=Active")]
    public void LogLine_fails_when_route_or_reason_is_missing(TransformRoute route, TransformGateVerdict v, string want)
    {
        Assert.Equal($"[Beelz TXGUARD] refused {want} steamId=76561198039548286 unit={Dracula}",
            TransformGate.LogLine(route, v, 76561198039548286UL, Dracula));
        Assert.Null(TransformGate.LogLine(route, TransformGateVerdict.Allow, 76561198039548286UL, Dracula));
    }
}
