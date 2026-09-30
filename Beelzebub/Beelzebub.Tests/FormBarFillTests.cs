using System.Linq;
using Beelzebub.Logic;
using Xunit;

// bar-reset D4 — form binds > universal binds > captures (only with Forms_AutoFillFromCaptures).
public class FormBarFillTests
{
    static readonly (int, int)[] None = System.Array.Empty<(int, int)>();
    static readonly int[] Captures = { 101, 102, 103, 104, 105, 106, 107, 108 };

    [Fact]
    public void Fill_fails_when_captures_are_used_with_autoFill_off()
    {
        var r = FormBarFill.Build(None, None, Captures, _ => true, autoFillFromCaptures: false);
        Assert.Empty(r.Bar);
        Assert.Equal(FormBarSource.Native, r.Source);
    }

    [Fact]
    public void Fill_uses_six_usable_captures_from_slot_1_with_autoFill_on()
    {
        var r = FormBarFill.Build(None, None, Captures, a => a != 102, autoFillFromCaptures: true);
        Assert.Equal(FormBarSource.Captures, r.Source);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, r.Bar.Keys.OrderBy(k => k));
        Assert.Equal(new[] { 101, 103, 104, 105, 106, 107 }, r.Bar.OrderBy(kv => kv.Key).Select(kv => kv.Value));
    }

    [Fact]
    public void Fill_fails_when_a_form_bind_loses_to_a_universal_bind()
    {
        var r = FormBarFill.Build(new[] { (4, 500) }, new[] { (4, 900), (5, 901) }, Captures, _ => true, true);
        Assert.Equal(FormBarSource.Form, r.Source);
        Assert.Equal(500, r.Bar[4]);
        Assert.False(r.Bar.ContainsKey(5));
    }

    [Fact]
    public void Fill_falls_back_to_universal_binds_slot_accurately()
    {
        var r = FormBarFill.Build(None, new[] { (5, 901) }, Captures, _ => true, true);
        Assert.Equal(FormBarSource.Universal, r.Source);
        Assert.Equal(901, r.Bar[5]);
        Assert.Single(r.Bar);
    }

    [Fact]
    public void Fill_skips_unusable_form_binds_and_falls_through()
    {
        var r = FormBarFill.Build(new[] { (4, 500) }, new[] { (5, 901) }, Captures, a => a != 500, false);
        Assert.Equal(FormBarSource.Universal, r.Source);
    }

    [Fact]
    public void Fill_fails_when_empty_inputs_are_not_native()
    {
        var r = FormBarFill.Build(null, null, null, null, true);
        Assert.Empty(r.Bar);
        Assert.Equal(FormBarSource.Native, r.Source);
    }
}
