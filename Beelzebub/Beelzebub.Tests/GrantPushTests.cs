using Beelzebub.Logic;
using Xunit;

// grant-refresh — a live grant is pushed through the engine setter; only the held buff's own earlier mods are popped.
public class GrantPushTests
{
    const int Buff = 581914, BuffVer = 252;

    static SlotModParse Parse(params SlotModEntry[] entries)
    {
        var p = new SlotModParse();
        p.Entries.AddRange(entries);
        return p;
    }

    [Fact]
    public void OwnModIds_fails_when_a_mod_from_another_source_is_popped()
    {
        var weaponKit = new SlotModEntry(1561, -2029046970, 581913, 259);   // another entity (the GRANTRAW dump)
        var spellbook = new SlotModEntry(1600, 1621601748, 345239, 1);
        var ours = new SlotModEntry(1567, 1621601748, Buff, BuffVer);
        Assert.Equal(new[] { 1567 }, GrantPush.OwnModIds(Parse(weaponKit, spellbook, ours), Buff, BuffVer));
    }

    [Fact]
    public void OwnModIds_fails_when_a_recycled_buff_index_with_another_version_is_popped()
    {
        Assert.Empty(GrantPush.OwnModIds(Parse(new SlotModEntry(1567, 1, Buff, BuffVer + 1)), Buff, BuffVer));
    }

    [Fact]
    public void OwnModIds_fails_when_a_repeated_mod_id_is_popped_twice()
    {
        var ours = new SlotModEntry(1567, 1621601748, Buff, BuffVer);
        var older = new SlotModEntry(1570, -1940289109, Buff, BuffVer);
        Assert.Equal(new[] { 1567, 1570 }, GrantPush.OwnModIds(Parse(ours, ours, older), Buff, BuffVer));
    }

    [Fact]
    public void OwnModIds_fails_when_an_unreadable_dump_yields_an_id()
    {
        var p = Parse(new SlotModEntry(1567, 1621601748, Buff, BuffVer));
        p.Readable = false;
        Assert.Empty(GrantPush.OwnModIds(p, Buff, BuffVer));
        Assert.Empty(GrantPush.OwnModIds(null, Buff, BuffVer));
    }

    [Fact]
    public void ShouldPush_fails_when_an_Empty_base_is_pushed()
    {
        Assert.False(GrantPush.ShouldPush(0));
        Assert.True(GrantPush.ShouldPush(1621601748));
    }
}
