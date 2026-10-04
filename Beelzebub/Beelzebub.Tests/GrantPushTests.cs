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

    // review round 1 (F1): a grant the engine applied at equip time carries another source — unslot must still pop it.
    [Fact]
    public void ModsToPop_fails_when_an_equip_time_grant_mod_survives_an_unslot()
    {
        var equipTime = new SlotModEntry(1580, 1621601748, 581913, 259);
        Assert.Equal(new[] { 1580 }, GrantPush.ModsToPop(Parse(equipTime), Buff, BuffVer, 1621601748, _ => false));
    }

    [Fact]
    public void ModsToPop_fails_when_a_spellbook_mod_or_an_unrelated_mod_is_popped()
    {
        var spellbook = new SlotModEntry(1600, 1621601748, 345239, 1);
        var weaponKit = new SlotModEntry(1561, -2029046970, 581913, 259);
        var ours = new SlotModEntry(1567, 1621601748, Buff, BuffVer);
        var ids = GrantPush.ModsToPop(Parse(spellbook, weaponKit, ours), Buff, BuffVer, 1621601748, e => e.ModId == 1600);
        Assert.Equal(new[] { 1567 }, ids);
        Assert.Empty(GrantPush.ModsToPop(Parse(weaponKit), Buff, BuffVer, 0, null));
    }

    [Fact]
    public void ModsToPop_fails_when_an_unreadable_dump_yields_an_id()
    {
        var p = Parse(new SlotModEntry(1580, 1621601748, 581913, 259));
        p.Readable = false;
        Assert.Empty(GrantPush.ModsToPop(p, Buff, BuffVer, 1621601748, null));
    }

    // review round 1 (F4): restore uses the highest-priority weapon row, like the reset's Reapply.
    [Fact]
    public void WeaponRow_fails_when_a_lower_priority_or_other_slot_row_wins()
    {
        var rows = new[] { (1, -2029046970, 0), (1, 111, 5), (4, 222, 9), (1, 333, 1) };
        Assert.Equal(111, GrantPush.WeaponRow(rows, 1));
        Assert.Equal(0, GrantPush.WeaponRow(rows, 5));
        Assert.Equal(0, GrantPush.WeaponRow(null, 1));
    }

    // Codex round 2 (finding 1): unslotting a weapon grant while a form maps the same ability must keep the form's mod.
    [Fact]
    public void OwnedByOtherBuff_fails_when_a_form_buffs_mod_is_popped_by_ability()
    {
        const int Form = 600100, FormVer = 3;
        var live = new System.Collections.Generic.HashSet<(int, int)> { (Form, FormVer), (Buff, BuffVer) };
        var formMod = new SlotModEntry(1590, 1621601748, Form, FormVer);
        var equipTime = new SlotModEntry(1580, 1621601748, 581913, 259);       // no prefab: not a live buff
        var ours = new SlotModEntry(1567, 1621601748, Buff, BuffVer);
        System.Func<SlotModEntry, bool> keep = e => GrantPush.OwnedByOtherBuff(e, live, Buff, BuffVer);
        Assert.Equal(new[] { 1580, 1567 },
            GrantPush.ModsToPop(Parse(formMod, equipTime, ours), Buff, BuffVer, 1621601748, keep));
        Assert.False(GrantPush.OwnedByOtherBuff(ours, live, Buff, BuffVer));
    }

    // Codex round 2 (finding 2): a re-grant A -> B pops A's equip-time mod too, or A resurfaces when B is unslotted.
    [Fact]
    public void ModsToPop_fails_when_a_replaced_equip_time_grant_survives_a_regrant()
    {
        const int A = 1621601748, Other = -1940289109;
        var equipTimeA = new SlotModEntry(1580, A, 581913, 259);
        var unrelated = new SlotModEntry(1600, Other, 581913, 259);
        Assert.Equal(new[] { 1580 }, GrantPush.ModsToPop(Parse(equipTimeA, unrelated), Buff, BuffVer, A, _ => false));
    }

    // form-bar-edits (v0.137.2): an unslot inside Wolf pops the form's two universal-fallback copies (diagnostic
    // [Beelz GRANTMODS] 2026-10-03: sources 582250:137 / 582258:206, no prefab) — only then is the weapon skill pushed.
    [Fact]
    public void SlotEmptyAfterPop_fails_when_a_kept_form_ability_still_gets_the_weapon_skill()
    {
        var copyA = new SlotModEntry(1590, 1621601748, 582250, 137);
        var copyB = new SlotModEntry(1609, 1621601748, 582258, 206);
        Assert.True(GrantPush.SlotEmptyAfterPop(Parse(copyA, copyB, copyA, copyB), new[] { 1590, 1609 }));
        var perForm = new SlotModEntry(1612, -1940289109, 600100, 3);
        Assert.False(GrantPush.SlotEmptyAfterPop(Parse(copyA, copyB, perForm), new[] { 1590, 1609 }));
    }

    [Fact]
    public void SlotEmptyAfterPop_fails_when_an_unreadable_dump_allows_a_push()
    {
        var p = Parse();
        p.Readable = false;
        Assert.False(GrantPush.SlotEmptyAfterPop(p, new int[0]));
        Assert.False(GrantPush.SlotEmptyAfterPop(null, new int[0]));
    }
}
