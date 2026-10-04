using Beelzebub.Logic;
using Xunit;

// clearbar-fullreset D2 — which bind origins a clear keeps.
public class BarSetTests
{
    static readonly string[] Origins = { "universal", "weapon:Sword", "weapon:Spear", "form:Wolf", "form:Mounted" };

    [Fact]
    public void Keeps_fails_when_all_keeps_any_origin()
    {
        foreach (var o in Origins) Assert.False(BarSet.All.Keeps(o), o);
    }

    [Theory]
    [InlineData("universal")]
    [InlineData("weapon:Sword")]
    [InlineData("form:Wolf")]
    public void Keeps_fails_when_a_set_keeps_its_own_origin_or_drops_another(string own)
    {
        BarSet set = own == "universal" ? BarSet.Universal
            : own.StartsWith("weapon:") ? BarSet.Weapon(own[7..]) : BarSet.Form(own[5..]);
        foreach (var o in Origins)
            Assert.Equal(o != own, set.Keeps(o));
    }

    [Fact]
    public void Keeps_fails_when_none_is_kept_or_the_comparison_is_case_sensitive()
    {
        Assert.False(BarSet.Universal.Keeps("none"));
        Assert.False(BarSet.Universal.Keeps(""));
        Assert.False(BarSet.Universal.Keeps(null));
        Assert.False(BarSet.Weapon("Sword").Keeps("WEAPON:sword"));
        Assert.True(BarSet.Weapon("Sword").Keeps("weapon:Spear"));
    }

    [Theory]
    [InlineData("weapon:Sword:legacy")]
    [InlineData("weapon:")]
    [InlineData(":Sword")]
    [InlineData("mount:Horse")]
    [InlineData("transform:Beatrice")]
    [InlineData("universalx")]
    public void Keeps_fails_when_an_unknown_origin_is_kept(string origin)
    {
        Assert.False(BarSet.Universal.Keeps(origin), origin);
        Assert.False(BarSet.Weapon("Spear").Keeps(origin), origin);
        Assert.False(BarSet.IsKnownOrigin(origin), origin);
    }

    [Fact]
    public void Label_fails_when_a_label_differs()
    {
        Assert.Equal("all", BarSet.All.Label);
        Assert.Equal("universal", BarSet.Universal.Label);
        Assert.Equal("weapon:Sword", BarSet.Weapon("Sword").Label);
        Assert.Equal("form:Wolf", BarSet.Form("Wolf").Label);
    }
}
