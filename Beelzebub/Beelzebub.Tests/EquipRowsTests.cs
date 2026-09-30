using Beelzebub.Logic;
using Xunit;

// bar-reset D31 — only rows the equip-buff prefab does not carry are injected; missing prefab rows are put back.
public class EquipRowsTests
{
    // EquipBuff_Weapon_Sword_Base's own rows (Reference Data/Prefabs).
    static readonly EquipRow[] Sword = { new(0, -2097352908), new(1, -2029046970), new(4, 1335008684) };

    [Fact]
    public void Injected_fails_when_a_vanilla_weapon_row_is_removed()
    {
        var live = new[] { Sword[0], Sword[1], new EquipRow(1, 555), Sword[2], new EquipRow(3, 777) };
        Assert.Equal(new[] { 2, 4 }, EquipRowDiff.InjectedIndices(live, Sword));
    }

    [Fact]
    public void Injected_fails_when_an_extra_copy_of_a_vanilla_row_is_kept()
    {
        var live = new[] { Sword[0], Sword[1], Sword[1], Sword[2] };
        Assert.Equal(new[] { 2 }, EquipRowDiff.InjectedIndices(live, Sword));
    }

    [Fact]
    public void Injected_fails_when_a_row_outside_slots_0_to_7_is_removed()
    {
        var live = new[] { Sword[0], new EquipRow(8, 1), new EquipRow(7, 2) };
        Assert.Equal(new[] { 2 }, EquipRowDiff.InjectedIndices(live, Sword));
    }

    [Fact]
    public void Injected_fails_when_unknown_prefab_rows_remove_anything()
    {
        Assert.Null(EquipRowDiff.InjectedIndices(new[] { Sword[0], new EquipRow(2, 9) }, null));
        Assert.Null(EquipRowDiff.MissingPrefabIndices(new[] { Sword[0] }, null));
    }

    [Fact]
    public void Missing_fails_when_a_stripped_vanilla_row_is_not_put_back()
    {
        var live = new[] { Sword[0], new EquipRow(1, 555) };   // a pre-0.137 reset stripped slots 1 and 4
        Assert.Equal(new[] { 1, 2 }, EquipRowDiff.MissingPrefabIndices(live, Sword));
        Assert.Empty(EquipRowDiff.MissingPrefabIndices(Sword, Sword));
    }
}
