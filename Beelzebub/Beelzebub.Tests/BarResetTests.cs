using System;
using System.Collections.Generic;
using System.Linq;
using Beelzebub.Logic;
using Xunit;
using S = Beelzebub.Logic.BarResetStep;

// bar-reset D2 D3 D11 D12 D24 D27 — plan order, scope, offline / not-live targets, runner resilience,
// unreadable and unsaved never clean.
public class BarResetTests
{
    static readonly S[] LiveOrder =
    {
        S.RevertTransform, S.ClearSavedBindings, S.ClearHotkeys, S.SaveBindings, S.ClearEquipEntries,
        S.DestroyOverrideSources, S.PopSlotMods, S.EmptyPush, S.Reapply, S.Readback,
    };

    // ── D2: one fixed order ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_is_the_documented_order_for_a_transformed_purge()
    {
        var plan = BarResetPlanner.Plan(BarResetScope.Purge, online: true, liveReady: true, transformActiveOrParked: true);
        Assert.Equal(LiveOrder, plan);
    }

    [Theory]
    [InlineData(BarResetScope.PlayerReset)]
    [InlineData(BarResetScope.AdminLoadouts)]
    [InlineData(BarResetScope.Purge)]
    public void Plan_fails_when_equip_entries_are_missing_or_after_the_pop(BarResetScope scope)
    {
        var plan = BarResetPlanner.Plan(scope, true, true, false);
        int eq = plan.IndexOf(S.ClearEquipEntries), pop = plan.IndexOf(S.PopSlotMods);
        Assert.True(eq >= 0, "ClearEquipEntries missing");
        Assert.True(eq < pop, "ClearEquipEntries must come before PopSlotMods");
        Assert.True(plan.IndexOf(S.ClearSavedBindings) < plan.IndexOf(S.SaveBindings), "SaveBindings before ClearSavedBindings");
        Assert.Equal(1, plan.Count(s => s == S.EmptyPush));
        Assert.True(plan.IndexOf(S.EmptyPush) > pop, "EmptyPush must follow the pop");
        Assert.Equal(S.Readback, plan[^1]);
    }

    [Fact]
    public void Plan_fails_when_RevertTransform_runs_without_a_transform()
    {
        Assert.DoesNotContain(S.RevertTransform, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false));
        Assert.Equal(S.RevertTransform, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, true)[0]);
    }

    [Fact]
    public void Plan_fails_when_an_unknown_scope_throws_instead_of_an_empty_plan()
    {
        var plan = BarResetPlanner.Plan((BarResetScope)99, true, true, true);
        Assert.Empty(plan);
    }

    [Fact]
    public void Run_fails_when_an_empty_plan_is_reported_clean()
    {
        var r = BarResetRunner.Run(new FakeOps(), new List<S>(), online: true, liveReady: true);
        Assert.False(r.Clean);
    }

    // ── D3: scope keeps the right data ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Scope_fails_when_PlayerReset_clears_hotkeys_or_Purge_does_not()
    {
        Assert.DoesNotContain(S.ClearHotkeys, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false));
        Assert.DoesNotContain(S.ClearHotkeys, BarResetPlanner.Plan(BarResetScope.AdminLoadouts, true, true, false));
        Assert.Contains(S.ClearHotkeys, BarResetPlanner.Plan(BarResetScope.Purge, true, true, false));
    }

    // ── D11: offline and not-live targets ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(BarResetScope.PlayerReset, false)]
    [InlineData(BarResetScope.Purge, true)]
    [InlineData(BarResetScope.Purge, false)]
    public void Offline_plan_fails_when_a_live_step_is_planned(BarResetScope scope, bool parkedTransform)
    {
        var plan = BarResetPlanner.Plan(scope, online: false, liveReady: false, transformActiveOrParked: parkedTransform);
        var expected = scope == BarResetScope.Purge
            ? new[] { S.ClearSavedBindings, S.ClearHotkeys, S.SaveBindings }
            : new[] { S.ClearSavedBindings, S.SaveBindings };
        Assert.Equal(expected, plan);
    }

    [Fact]
    public void NotLive_plan_fails_when_a_live_step_is_planned_or_the_result_is_clean()
    {
        var plan = BarResetPlanner.Plan(BarResetScope.PlayerReset, online: true, liveReady: false, transformActiveOrParked: true);
        Assert.Equal(new[] { S.ClearSavedBindings, S.SaveBindings, S.Readback }, plan);

        var r = BarResetRunner.Run(new FakeOps(), plan, online: true, liveReady: false);
        Assert.False(r.Clean);
        Assert.True(r.Unreadable);
    }

    // ── D12: a failing step does not stop the rest ──────────────────────────────────────────────────────

    [Fact]
    public void Runner_fails_when_a_throwing_ClearEquipEntries_stops_later_steps()
    {
        var ops = new FakeOps { Throw = S.ClearEquipEntries };
        var plan = BarResetPlanner.Plan(BarResetScope.Purge, true, true, true);
        var r = BarResetRunner.Run(ops, plan, true, true);

        Assert.Equal(plan, ops.Called);
        Assert.Contains(S.PopSlotMods, ops.Called);
        Assert.Contains(S.Readback, ops.Called);
        Assert.True(r.Steps.Single(s => s.Step == S.ClearEquipEntries).Failed);
        Assert.False(r.Clean);
    }

    [Fact]
    public void Runner_fails_when_a_null_readback_is_clean()
    {
        var ops = new FakeOps { Reading = null };
        var r = BarResetRunner.Run(ops, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false), true, true);
        Assert.False(r.Clean);
        Assert.True(r.Unreadable);
        Assert.Contains(r.Steps, s => s.Step == S.Readback && s.Failed);
    }

    [Fact]
    public void Runner_fails_when_a_plan_without_live_layers_is_clean()
    {
        var r = BarResetRunner.Run(new FakeOps(), new[] { S.ClearSavedBindings, S.SaveBindings, S.Readback }, true, true);
        Assert.False(r.Clean);
    }

    [Fact]
    public void Runner_is_clean_for_a_good_reset()
    {
        var r = BarResetRunner.Run(new FakeOps(), BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false), true, true);
        Assert.True(r.Clean);
        Assert.Empty(r.Survivors);
    }

    [Fact]
    public void Runner_fails_when_a_surviving_slot_is_reported_clean()
    {
        var ops = new FakeOps { Reading = Bar(new BarSlotReading { Slot = 4, Other = 1 }) };
        var r = BarResetRunner.Run(ops, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false), true, true);
        Assert.False(r.Clean);
        Assert.Equal(new[] { 4 }, r.Survivors);
    }

    // ── D24: unreadable is never clean ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Runner_fails_when_an_unreadable_readback_is_clean()
    {
        var ops = new FakeOps { Reading = Bar(new BarSlotReading { Slot = 2, Unreadable = true }) };
        var r = BarResetRunner.Run(ops, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false), true, true);
        Assert.False(r.Clean);
        Assert.True(r.Unreadable);
    }

    [Fact]
    public void Decide_fails_when_an_unreadable_slot_gets_a_pop_or_destroy()
    {
        var parse = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(1) (Base: PrefabGuid(0))\n" +
            "    [ModId 10] Set PrefabGuid(1) from Entity(50:1) (x)\n" +
            "    [ModId 11] garbled\n");
        var d = SlotPurgeDecision.Decide(parse, _ => "AB_Werewolf_Buff", _ => false);
        Assert.True(d.Skipped);
        Assert.Empty(d.ModIdsToPop);
        Assert.Empty(d.SourcesToDestroy);
    }

    [Fact]
    public void Decide_fails_when_a_gear_or_protected_source_is_destroyed()
    {
        var parse = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(1) (Base: PrefabGuid(0))\n" +
            "    [ModId 10] Set PrefabGuid(1) from Entity(50:1) (a)\n" +
            "    [ModId 11] Set PrefabGuid(2) from Entity(60:1) (b)\n" +
            "    [ModId 12] Set PrefabGuid(3) from Entity(70:2) (c)\n" +
            "    [ModId 13] Set PrefabGuid(4) from Entity(70:2) (c)\n");
        var names = new Dictionary<int, string> { [50] = "EquipBuff_Weapon_Sword_Base", [60] = "CHAR_VampireMale", [70] = "AB_Shapeshift_Wolf_Buff" };
        var d = SlotPurgeDecision.Decide(parse, e => names[e.SourceIndex], e => e.SourceIndex == 60);

        Assert.Equal(new[] { 10, 11, 12, 13 }, d.ModIdsToPop);   // every GroupGuid mod is popped, gear included
        Assert.Equal(new[] { (70, 2) }, d.SourcesToDestroy);     // only the non-gear, non-protected source, once
    }

    // ── D27: a failed save is never clean ───────────────────────────────────────────────────────────────

    [Fact]
    public void Runner_fails_when_a_failed_save_is_clean_or_stops_live_steps()
    {
        var ops = new FakeOps { SaveResult = false };
        var plan = BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false);
        var r = BarResetRunner.Run(ops, plan, true, true);

        Assert.False(r.Clean);
        Assert.False(r.Saved);
        Assert.Equal("not saved", r.Steps.Single(s => s.Step == S.SaveBindings).Error);
        Assert.Contains(S.ClearEquipEntries, ops.Called);
        Assert.Contains(S.Readback, ops.Called);
    }

    [Fact]
    public void Runner_fails_when_a_throwing_save_is_clean()
    {
        var ops = new FakeOps { Throw = S.SaveBindings };
        var r = BarResetRunner.Run(ops, BarResetPlanner.Plan(BarResetScope.Purge, true, true, false), true, true);
        Assert.False(r.Clean);
        Assert.True(r.Steps.Single(s => s.Step == S.SaveBindings).Failed);
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────────

    static BarReadback Bar(params BarSlotReading[] slots) => new() { Slots = slots.ToList() };

    sealed class FakeOps : IBarResetOps
    {
        public S? Throw;
        public bool SaveResult = true;
        public BarReadback Reading = new() { Slots = { new BarSlotReading { Slot = 0, Gear = 2 } } };
        public readonly List<S> Called = new();

        int Hit(S s) { Called.Add(s); if (Throw == s) throw new InvalidOperationException($"{s} boom"); return 1; }

        public int RevertTransform() => Hit(S.RevertTransform);
        public int ClearSavedBindings() => Hit(S.ClearSavedBindings);
        public int ClearHotkeys() => Hit(S.ClearHotkeys);
        public bool SaveBindings() { Hit(S.SaveBindings); return SaveResult; }
        public int ClearEquipEntries() => Hit(S.ClearEquipEntries);
        public int DestroyOverrideSources() => Hit(S.DestroyOverrideSources);
        public int PopSlotMods() => Hit(S.PopSlotMods);
        public int EmptyPush() => Hit(S.EmptyPush);
        public int Reapply() => Hit(S.Reapply);
        public BarReadback Readback() { Hit(S.Readback); return Reading; }
    }
}
