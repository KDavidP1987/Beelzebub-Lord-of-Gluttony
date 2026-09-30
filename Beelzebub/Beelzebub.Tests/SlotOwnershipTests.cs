using System.Collections.Generic;
using Beelzebub.Logic;
using Xunit;

// bar-reset D32 — the Empty push only touches the weapon's own slots; a weapon-buff Empty anywhere else is a leak.
public class SlotOwnershipTests
{
    static readonly HashSet<int> Sword = new() { 0, 1, 4 };
    static SlotModEntry Empty => new(1440, 0, 346303, 1);
    static SlotModEntry Whirlwind => new(1441, -2029046970, 346303, 1);

    [Fact]
    public void PushTargets_fails_when_a_slot_without_a_prefab_row_is_pushed()
    {
        Assert.Equal(new[] { 0, 1, 4 }, SlotOwnership.PushTargets(new[] { 4, 0, 1 }, 8));
    }

    [Fact]
    public void PushTargets_fails_when_a_slot_above_the_bar_or_a_duplicate_is_pushed()
    {
        Assert.Equal(new[] { 0, 4 }, SlotOwnership.PushTargets(new[] { 0, 4, 4, 9, 12 }, 8));
        Assert.Empty(SlotOwnership.PushTargets(null, 8));
    }

    [Fact]
    public void IsLeakedEmpty_fails_when_a_gear_Empty_on_a_non_weapon_slot_is_not_leaked()
    {
        foreach (int slot in new[] { 2, 3, 5, 6, 7, 8 })
            Assert.True(SlotOwnership.IsLeakedEmpty(Empty, slot, gearSource: true, Sword));
    }

    [Fact]
    public void IsLeakedEmpty_fails_when_a_legitimate_mod_is_called_a_leak()
    {
        Assert.False(SlotOwnership.IsLeakedEmpty(Empty, 1, gearSource: true, Sword));       // owned slot
        Assert.False(SlotOwnership.IsLeakedEmpty(Whirlwind, 7, gearSource: true, Sword));   // not Empty
        Assert.False(SlotOwnership.IsLeakedEmpty(Empty, 7, gearSource: false, Sword));      // not gear
        Assert.False(SlotOwnership.IsLeakedEmpty(Empty, 7, gearSource: true, null));        // owned set unknown
    }

    [Fact]
    public void PopAgain_fails_when_the_loop_stops_while_the_count_still_falls()
    {
        Assert.True(SlotOwnership.PopAgain(previousCount: 6, currentCount: 5, roundsDone: 1));
    }

    [Fact]
    public void PopAgain_fails_when_the_loop_runs_without_progress_or_past_the_cap()
    {
        Assert.False(SlotOwnership.PopAgain(6, 6, 1));                             // no progress
        Assert.False(SlotOwnership.PopAgain(6, 0, 1));                             // cleared
        Assert.False(SlotOwnership.PopAgain(6, 5, SlotOwnership.MaxPopRounds));    // cap reached
    }
}
