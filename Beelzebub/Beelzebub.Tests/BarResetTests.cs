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

    // Every required layer on its own: a plan short of any ONE of them is never clean (not only a plan missing all).
    [Theory]
    [InlineData(S.ClearSavedBindings)]
    [InlineData(S.SaveBindings)]
    [InlineData(S.ClearEquipEntries)]
    [InlineData(S.DestroyOverrideSources)]
    [InlineData(S.PopSlotMods)]
    [InlineData(S.EmptyPush)]
    [InlineData(S.Reapply)]
    [InlineData(S.Readback)]
    public void Runner_fails_when_a_plan_missing_one_required_layer_is_clean(S missing)
    {
        var plan = BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false).Where(s => s != missing).ToList();
        var r = BarResetRunner.Run(new FakeOps(), plan, true, true);
        Assert.False(r.Clean, $"a plan without {missing} was reported clean");
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

    [Fact]
    public void Runner_fails_when_a_surviving_override_buff_is_clean()
    {
        var ops = new FakeOps { Reading = new BarReadback { Slots = { new BarSlotReading { Slot = 0 } }, OverrideBuffs = { "AB_Shapeshift_Wolf_Buff" } } };
        var r = BarResetRunner.Run(ops, BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false), true, true);
        Assert.False(r.Clean);
        Assert.Equal(new[] { "AB_Shapeshift_Wolf_Buff" }, r.OverrideBuffsLeft);
        Assert.Contains(BarResetReply.ForReset(r, BarResetScope.PlayerReset, "P"), l => l.Contains("override buff still on: AB_Shapeshift_Wolf_Buff"));
    }

    [Fact]
    public void Runner_fails_when_a_failed_revert_drops_the_transform_record()
    {
        var plan = BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, true);
        var failed = new FakeOps { Throw = S.RevertTransform };
        var r = BarResetRunner.Run(failed, plan, true, true);
        Assert.True(failed.KeptRecord, "a failed RevertTransform must keep the transform record for the retry");
        Assert.False(r.Clean);

        var ok = new FakeOps();
        BarResetRunner.Run(ok, plan, true, true);
        Assert.False(ok.KeptRecord, "a reverted transform's record is dropped");
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

    // ── mounted-bar-reset D4: a reset while riding dismounts on purpose ───────────────────────────────────

    [Theory]
    [InlineData(BarResetScope.PlayerReset)]
    [InlineData(BarResetScope.AdminLoadouts)]
    [InlineData(BarResetScope.Purge)]
    public void Plan_fails_when_Dismount_is_missing_or_after_the_orphan_sweep(BarResetScope scope)
    {
        var plan = BarResetPlanner.Plan(scope, true, true, false, mounted: true);
        int d = plan.IndexOf(S.Dismount);
        Assert.True(d >= 0, "Dismount missing for a mounted live reset");
        Assert.True(d > plan.IndexOf(S.SaveBindings), "Dismount before SaveBindings");
        Assert.True(d < plan.IndexOf(S.DestroyOverrideSources), "Dismount must come before DestroyOverrideSources");
        Assert.Equal(1, plan.Count(s => s == S.Dismount));
    }

    [Theory]
    [InlineData(false, true, true)]    // not mounted
    [InlineData(true, false, true)]    // offline
    [InlineData(true, true, false)]    // live bar not reachable
    public void Plan_fails_when_Dismount_is_planned_without_a_live_mounted_character(bool mounted, bool online, bool liveReady)
    {
        Assert.DoesNotContain(S.Dismount, BarResetPlanner.Plan(BarResetScope.PlayerReset, online, liveReady, false, mounted));
    }

    [Fact]
    public void Run_fails_when_a_mounted_reset_is_not_clean_or_a_failed_Dismount_is_clean()
    {
        var plan = BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false, mounted: true);
        var ok = BarResetRunner.Run(new FakeOps(), plan, true, true);
        Assert.True(ok.Clean, "a mounted reset with every step ok must be clean");
        Assert.Equal(1, ok.CountOf(S.Dismount));
        Assert.DoesNotContain(S.Dismount, BarResetRunner.RequiredForClean);

        var bad = BarResetRunner.Run(new FakeOps { Throw = S.Dismount }, plan, true, true);
        Assert.False(bad.Clean, "a failed Dismount must not be clean");
        Assert.Contains(S.DestroyOverrideSources, bad.Steps.Select(s => s.Step));
    }

    // ── fakes ───────────────────────────────────────────────────────────────────────────────────────────

    static BarReadback Bar(params BarSlotReading[] slots) => new() { Slots = slots.ToList() };

    // ── clearbar-fullreset D1: the ClearSet plan ──────────────────────────────────────────────────────────

    static readonly S[] ClearSetOrder =
    {
        S.RevertTransform, S.ClearSavedBindings, S.SaveBindings, S.Dismount, S.ClearEquipEntries,
        S.DestroyOverrideSources, S.PopSlotMods, S.EmptyPush, S.Reapply, S.RestoreKept, S.Readback,
    };

    [Fact]
    public void ClearSet_plan_fails_when_RestoreKept_is_missing_or_not_right_after_Reapply()
    {
        Assert.Equal(ClearSetOrder, BarResetPlanner.Plan(BarResetScope.ClearSet, true, true, true, mounted: true));
        var foot = BarResetPlanner.Plan(BarResetScope.ClearSet, true, true, false);
        Assert.DoesNotContain(S.RevertTransform, foot);
        Assert.DoesNotContain(S.Dismount, foot);
        Assert.Equal(foot.IndexOf(S.Reapply) + 1, foot.IndexOf(S.RestoreKept));
    }

    [Theory]
    [InlineData(BarResetScope.PlayerReset)]
    [InlineData(BarResetScope.AdminLoadouts)]
    [InlineData(BarResetScope.Purge)]
    public void Plan_fails_when_RestoreKept_leaks_into_another_scope(BarResetScope scope)
    {
        Assert.DoesNotContain(S.RestoreKept, BarResetPlanner.Plan(scope, true, true, true, mounted: true));
    }

    [Fact]
    public void ClearSet_plan_fails_when_it_clears_hotkeys_or_an_offline_plan_has_a_live_step()
    {
        Assert.DoesNotContain(S.ClearHotkeys, BarResetPlanner.Plan(BarResetScope.ClearSet, true, true, true, true));
        Assert.Equal(new[] { S.ClearSavedBindings, S.SaveBindings }, BarResetPlanner.Plan(BarResetScope.ClearSet, false, false, true, true));
        Assert.Equal(new[] { S.ClearSavedBindings, S.SaveBindings, S.Readback }, BarResetPlanner.Plan(BarResetScope.ClearSet, true, false, true, true));
    }

    // ── clearbar-fullreset D3: kept binds are not survivors ─────────────────────────────────────────────────

    static BarResetResult RunClear(BarSet set, BarReadback reading, S? fail = null, bool dropRestoreKept = false)
    {
        var plan = BarResetPlanner.Plan(BarResetScope.ClearSet, true, true, false);
        if (dropRestoreKept) plan.Remove(S.RestoreKept);
        return BarResetRunner.Run(new FakeOps { Reading = reading, Throw = fail }, plan, true, true, set);
    }

    [Fact]
    public void ClearSet_fails_when_a_kept_universal_bind_makes_clearbar_sword_unclean()
    {
        var r = RunClear(BarSet.Weapon("Sword"), Bar(new BarSlotReading { Slot = 1, Bind = "universal", Row = true },
                                                      new BarSlotReading { Slot = 2, Bind = "form:Wolf" }));
        Assert.Empty(r.Survivors);
        Assert.True(r.Clean);
    }

    [Fact]
    public void ClearSet_fails_when_a_bind_of_the_cleared_set_reads_as_kept()
    {
        var r = RunClear(BarSet.Weapon("Sword"), Bar(new BarSlotReading { Slot = 4, Bind = "weapon:Sword", Row = true }));
        Assert.Equal(new List<int> { 4 }, r.Survivors);
        Assert.False(r.Clean);
    }

    [Fact]
    public void ClearSet_fails_when_a_kept_slot_with_an_other_mod_reads_clean()
    {
        var r = RunClear(BarSet.Universal, Bar(new BarSlotReading { Slot = 5, Bind = "form:Mounted", Other = 1 }));
        Assert.Equal(new List<int> { 5 }, r.Survivors);
        Assert.False(r.Clean);
    }

    [Fact]
    public void ClearSet_fails_when_a_run_without_RestoreKept_or_with_a_thrown_RestoreKept_reads_clean()
    {
        var bar = Bar(new BarSlotReading { Slot = 0, Gear = 1 });
        Assert.False(RunClear(BarSet.Universal, bar, dropRestoreKept: true).Clean);
        var thrown = RunClear(BarSet.Universal, bar, fail: S.RestoreKept);
        Assert.False(thrown.Clean);
        Assert.Contains(thrown.Steps, s => s.Step == S.RestoreKept && s.Failed);
    }

    [Fact]
    public void ClearSet_fails_when_clearbar_all_keeps_a_bind_of_any_set()
    {
        var r = RunClear(BarSet.All, Bar(new BarSlotReading { Slot = 1, Bind = "universal", Row = true },
                                         new BarSlotReading { Slot = 4, Bind = "weapon:Sword", Row = true },
                                         new BarSlotReading { Slot = 5, Bind = "form:Mounted" }));
        Assert.Equal(new List<int> { 1, 4, 5 }, r.Survivors);
        Assert.False(r.Clean);
    }

    [Fact]
    public void Run_fails_when_a_reset_without_a_clear_set_keeps_any_bind()
    {
        var plan = BarResetPlanner.Plan(BarResetScope.PlayerReset, true, true, false);
        var r = BarResetRunner.Run(new FakeOps { Reading = Bar(new BarSlotReading { Slot = 1, Bind = "universal" }) }, plan, true, true);
        Assert.Equal(new List<int> { 1 }, r.Survivors);
        Assert.False(r.Clean);
    }

    sealed class FakeOps : IBarResetOps
    {
        public S? Throw;
        public bool SaveResult = true;
        public BarReadback Reading = new() { Slots = { new BarSlotReading { Slot = 0, Gear = 2 } } };
        public readonly List<S> Called = new();

        int Hit(S s) { Called.Add(s); if (Throw == s) throw new InvalidOperationException($"{s} boom"); return 1; }

        public int RevertTransform() => Hit(S.RevertTransform);
        public bool? KeptRecord;
        public int ClearSavedBindings(bool keepTransformRecord) { KeptRecord = keepTransformRecord; return Hit(S.ClearSavedBindings); }
        public int ClearHotkeys() => Hit(S.ClearHotkeys);
        public bool SaveBindings() { Hit(S.SaveBindings); return SaveResult; }
        public int ClearEquipEntries() => Hit(S.ClearEquipEntries);
        public int Dismount() => Hit(S.Dismount);
        public int DestroyOverrideSources() => Hit(S.DestroyOverrideSources);
        public int PopSlotMods() => Hit(S.PopSlotMods);
        public int EmptyPush() => Hit(S.EmptyPush);
        public int Reapply() => Hit(S.Reapply);
        public int RestoreKept() => Hit(S.RestoreKept);
        public BarReadback Readback() { Hit(S.Readback); return Reading; }
    }
}
