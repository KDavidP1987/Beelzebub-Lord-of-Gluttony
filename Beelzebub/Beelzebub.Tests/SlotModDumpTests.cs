using Beelzebub.Logic;
using Xunit;

// bar-reset D1 — the dump parser reads GroupGuid modifications only and flags text it cannot parse.
public class SlotModDumpTests
{
    const string GroupGuidOnly =
        "Entity(1234:1) modifications:\n" +
        "- AbilityGroupSlot.GroupGuid: PrefabGuid(-1591532957) (Base: PrefabGuid(0))\n" +
        "    [ModId 5066] Set PrefabGuid(-1591532957) from Entity(326806:1) (No PrefabGUID, Entity Name '')\n" +
        "    [ModId 5070] Set PrefabGuid(862477668) from Entity(326900:3) (EquipBuff_Weapon_Sword_Base, Entity Name '')\n";

    [Fact]
    public void GroupGuid_entries_are_parsed_with_their_sources()
    {
        var p = SlotModDump.ParseGroupGuid(GroupGuidOnly);
        Assert.True(p.Readable);
        Assert.Equal(2, p.Entries.Count);
        Assert.Equal(new SlotModEntry(5066, -1591532957, 326806, 1), p.Entries[0]);
        Assert.Equal(new SlotModEntry(5070, 862477668, 326900, 3), p.Entries[1]);
    }

    [Fact]
    public void Parse_fails_when_a_CopyCooldown_mod_is_returned_as_GroupGuid()
    {
        string dump =
            "- AbilityGroupSlot.CopyCooldown: False (Base: False)\n" +
            "    [ModId 900] Set PrefabGuid(111) from Entity(5:1) (x)\n" +
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(0) (Base: PrefabGuid(0))\n" +
            "- AbilityGroupSlot.SpellModsSource: Entity(0:0)\n" +
            "    [ModId 901] Set PrefabGuid(222) from Entity(6:1) (y)\n";
        var p = SlotModDump.ParseGroupGuid(dump);
        Assert.True(p.Readable);
        Assert.Empty(p.Entries);
    }

    [Fact]
    public void Parse_fails_when_an_empty_dump_yields_entries()
    {
        foreach (var dump in new[] { "", null, "\n\n" })
        {
            var p = SlotModDump.ParseGroupGuid(dump);
            Assert.Empty(p.Entries);
            Assert.True(p.Readable);
            Assert.False(p.HasMods);
        }
    }

    [Fact]
    public void Failed_read_fails_when_it_is_readable_or_lets_the_slot_be_purged()
    {
        // A formatter exception must never look like an empty (readable, unmodified) dump.
        var p = SlotModParse.Failed();
        Assert.False(p.Readable);
        Assert.Empty(p.Entries);
        var d = SlotPurgeDecision.Decide(p, _ => "AB_Werewolf_Buff", _ => false);
        Assert.True(d.Skipped);
        Assert.Empty(d.ModIdsToPop);
        Assert.Empty(d.SourcesToDestroy);
    }

    [Fact]
    public void Parse_fails_when_an_unparseable_ModId_line_is_readable()
    {
        string dump =
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(1) (Base: PrefabGuid(0))\n" +
            "    [ModId 5066] Set PrefabGuid(1) from Entity(326806:1) (ok)\n" +
            "    [ModId 5067] Replaced by something new\n";
        var p = SlotModDump.ParseGroupGuid(dump);
        Assert.False(p.Readable);
        Assert.Single(p.Entries);
    }

    [Fact]
    public void Parse_fails_when_ModId_zero_is_accepted()
    {
        var p = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(1) (Base: PrefabGuid(0))\n" +
            "    [ModId 0] Set PrefabGuid(1) from Entity(2:1) (x)\n");
        Assert.False(p.Readable);
        Assert.Empty(p.Entries);
    }

    [Fact]
    public void Parse_fails_when_a_GroupGuid_prefixed_field_is_read_as_GroupGuid()
    {
        var p = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuidBackup: PrefabGuid(1)\n" +
            "    [ModId 77] Set PrefabGuid(1) from Entity(2:1) (x)\n");
        Assert.Empty(p.Entries);
        Assert.True(p.Readable);
    }

    [Fact]
    public void Parse_fails_when_another_component_field_after_GroupGuid_is_read()
    {
        var p = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(1) (Base: PrefabGuid(0))\n" +
            "    [ModId 5066] Set PrefabGuid(1) from Entity(326806:1) (ok)\n" +
            "- OtherComponent.Value: 3\n" +
            "    [ModId 12] Added 3 by something\n");
        Assert.True(p.Readable);
        Assert.Single(p.Entries);
    }

    [Fact]
    public void Parse_fails_when_a_dash_sub_line_ends_the_GroupGuid_section()
    {
        var p = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(1) (Base: PrefabGuid(0))" + NL +
            "    [ModId 5066] Set PrefabGuid(1) from Entity(326806:1) (ok)" + NL +
            "    - note: stacked" + NL +
            "    [ModId 5067] Set PrefabGuid(2) from Entity(326807:1) (ok)" + NL);
        Assert.True(p.Readable);
        Assert.Equal(2, p.Entries.Count);
    }

    const string NL = "\n";
}
