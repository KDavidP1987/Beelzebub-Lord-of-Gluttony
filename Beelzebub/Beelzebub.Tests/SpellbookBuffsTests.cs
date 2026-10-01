using System.Collections.Generic;
using System.Linq;
using Beelzebub.Logic;
using Xunit;

// bar-reset D33 — a Buff_VBlood_Ability_Replace a spellbook entry references is the player's spell, never our carrier;
// dangling spellbook entries are repaired; each mod id is popped once.
public class SpellbookBuffsTests
{
    static readonly long Shadowbolt = SpellbookBuffs.Id(345239, 1);
    static readonly long BloodRite = SpellbookBuffs.Id(345240, 1);
    static readonly long Carrier = SpellbookBuffs.Id(346372, 1);

    [Fact]
    public void IsOwnCarrier_fails_when_a_spellbook_referenced_buff_counts_as_ours()
    {
        var referenced = new HashSet<long> { Shadowbolt, BloodRite };
        Assert.False(SpellbookBuffs.IsOwnCarrier(Shadowbolt, referenced));
        Assert.False(SpellbookBuffs.IsOwnCarrier(BloodRite, referenced));
        Assert.True(SpellbookBuffs.IsOwnCarrier(Carrier, referenced));
        Assert.True(SpellbookBuffs.IsOwnCarrier(Carrier, new HashSet<long>()));   // empty spellbook: an unreferenced carrier is ours
    }

    [Fact]
    public void IsOwnCarrier_fails_when_an_unreadable_spellbook_makes_any_buff_ours()
    {
        Assert.False(SpellbookBuffs.IsOwnCarrier(Carrier, null));
        Assert.False(SpellbookBuffs.IsOwnCarrier(Shadowbolt, null));
    }

    [Fact]
    public void Id_fails_when_two_entities_share_an_id()
    {
        Assert.NotEqual(SpellbookBuffs.Id(345239, 1), SpellbookBuffs.Id(345239, 2));
        Assert.NotEqual(SpellbookBuffs.Id(1, 0), SpellbookBuffs.Id(0, 1));
    }

    [Fact]
    public void Dangling_fails_when_a_live_spell_is_repaired_or_a_missing_one_is_skipped()
    {
        var entries = new[]
        {
            new SpellbookEntry(0, 7, 375131842, BuffLive: false),    // T Crimson Beam
            new SpellbookEntry(1, 2, -433204738, BuffLive: false),   // Space Veil of Shadows
            new SpellbookEntry(2, 5, -880131926, BuffLive: false),   // R Shadowbolt
            new SpellbookEntry(3, 6, 1191439206, BuffLive: true),    // C Blood Rite
        };
        var d = SpellbookBuffs.Dangling(entries);
        Assert.Equal(new[] { 2, 1, 0 }, d.Select(e => e.Index));      // highest index first: removing one never shifts the next
        Assert.DoesNotContain(d, e => e.SlotId == 6);
        Assert.Empty(SpellbookBuffs.Dangling(null));
    }

    [Fact]
    public void Decide_fails_when_a_duplicated_mod_id_is_popped_twice()
    {
        var parse = SlotModDump.ParseGroupGuid(
            "- AbilityGroupSlot.GroupGuid: PrefabGuid(0) (Base: PrefabGuid(-433204738))\n" +
            "    [ModId 1440] Set PrefabGuid(0) from Entity(50:1) (a)\n" +
            "    [ModId 1440] Set PrefabGuid(0) from Entity(50:1) (a)\n" +
            "    [ModId 1441] Set PrefabGuid(0) from Entity(50:1) (a)\n" +
            "    [ModId 1440] Set PrefabGuid(0) from Entity(50:1) (a)\n");
        var d = SlotPurgeDecision.Decide(parse, _ => "EquipBuff_Weapon_Unarmed_Start01", _ => false);
        Assert.Equal(new[] { 1440, 1441 }, d.ModIdsToPop);
    }
}
