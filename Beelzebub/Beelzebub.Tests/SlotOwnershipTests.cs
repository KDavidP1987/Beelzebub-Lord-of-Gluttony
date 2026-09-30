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
    public void ClassifyEmpty_fails_when_a_weapon_Empty_on_a_non_weapon_slot_is_not_a_leak()
    {
        foreach (int slot in new[] { 2, 3, 5, 6, 7, 8 })
            Assert.Equal(EmptyVerdict.Leak, SlotOwnership.ClassifyEmpty(Empty, slot, weaponBuffSource: true, Sword));
    }

    [Fact]
    public void ClassifyEmpty_fails_when_a_legitimate_mod_is_called_a_leak()
    {
        Assert.Equal(EmptyVerdict.NotLeak, SlotOwnership.ClassifyEmpty(Empty, 1, true, Sword));        // owned slot
        Assert.Equal(EmptyVerdict.NotLeak, SlotOwnership.ClassifyEmpty(Whirlwind, 7, true, Sword));    // not Empty
        Assert.Equal(EmptyVerdict.NotLeak, SlotOwnership.ClassifyEmpty(Empty, 7, false, Sword));       // armour / item gear
        Assert.Equal(EmptyVerdict.NotLeak, SlotOwnership.ClassifyEmpty(Empty, 7, false, null));
    }

    [Fact]
    public void ClassifyEmpty_fails_when_an_unknown_owned_set_reads_as_known()
    {
        Assert.Equal(EmptyVerdict.Unknown, SlotOwnership.ClassifyEmpty(Empty, 7, true, null));
        Assert.Equal(EmptyVerdict.Unknown, SlotOwnership.ClassifyEmpty(Empty, 1, true, null));
    }

    [Fact]
    public void IsWeaponBuff_fails_when_armour_counts_or_a_weapon_buff_does_not()
    {
        Assert.True(SlotOwnership.IsWeaponBuff("EquipBuff_Weapon_Unarmed_Start01"));
        Assert.True(SlotOwnership.IsWeaponBuff("EquipBuff_Weapon_Sword_Ability03"));
        Assert.False(SlotOwnership.IsWeaponBuff("EquipBuff_Chest_Base"));
        Assert.False(SlotOwnership.IsWeaponBuff("Item_Cloak_Main_T01"));
        Assert.False(SlotOwnership.IsWeaponBuff(""));
        Assert.False(SlotOwnership.IsWeaponBuff(null));
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
