using Beelzebub.Logic;
using Xunit;

namespace Beelzebub.Tests;

public class DamageMathTests
{
    static DamageSnapshot S(float scale, float min = 0, float max = 0, FlatPolicy flat = FlatPolicy.Unchanged, bool perHit = true)
        => new(scale, min, max, flat, perHit);

    [Theory]
    [InlineData(0.5f, 1.2f, 0.6f)]   // nerf
    [InlineData(2.0f, 1.2f, 2.4f)]   // boost
    public void ScalesPowerTermTwoWay(float scale, float factor, float expected)
    {
        Assert.True(DamageMath.TryComputeHit(DamageMath.MainTypeSpell, factor, 0f, S(scale), out var f, out _));
        Assert.Equal(expected, f, 4);
    }

    [Fact]
    public void ClampBoundsApplyToFinalFactor_ZeroMeansOff()
    {
        Assert.Equal(3f, DamageMath.ScaleFactor(2f, S(2f, max: 3f)), 4);       // capped
        Assert.Equal(0.5f, DamageMath.ScaleFactor(0.2f, S(1f, min: 0.5f)), 4); // floored
        Assert.Equal(8f, DamageMath.ScaleFactor(4f, S(2f)), 4);                 // both off
    }

    [Fact]
    public void NonPowerTypesAndNonPositiveFactorsUntouched()
    {
        Assert.False(DamageMath.TryComputeHit(2 /*Fire*/, 1f, 10f, S(2f, flat: FlatPolicy.Scaled), out _, out _));
        Assert.False(DamageMath.TryComputeHit(DamageMath.MainTypePhysical, 0f, 0f, S(2f), out var f, out _));
        Assert.Equal(0f, f);
        Assert.False(DamageMath.TryComputeHit(DamageMath.MainTypePhysical, float.NaN, 0f, S(2f), out _, out _));
        // factor <= 0 leaves the flat term alone too, even under FlatPolicy.Scaled
        Assert.False(DamageMath.TryComputeHit(DamageMath.MainTypePhysical, 0f, 50f, S(2f, flat: FlatPolicy.Scaled), out _, out var raw));
        Assert.Equal(50f, raw);
    }

    [Fact]
    public void FlatPolicyTerm()
    {
        DamageMath.TryComputeHit(DamageMath.MainTypePhysical, 1f, 50f, S(2f), out _, out var raw1);
        Assert.Equal(50f, raw1);   // Unchanged
        DamageMath.TryComputeHit(DamageMath.MainTypePhysical, 1f, 50f, S(2f, flat: FlatPolicy.Scaled), out _, out var raw2);
        Assert.Equal(100f, raw2);
    }

    [Fact]
    public void IdentityAndEffectiveScale()
    {
        Assert.True(DamageMath.IsIdentity(DamageSnapshot.Identity));
        Assert.False(DamageMath.IsIdentity(S(1f, max: 3f)));
        Assert.Equal(3f, DamageMath.EffectiveScale(1.5f, 2f, false, 9f), 4);
        Assert.Equal(6f, DamageMath.EffectiveScale(1.5f, 2f, true, 2f), 4);
        Assert.Equal(1f, DamageMath.EffectiveScale(float.NaN, -1f, true, 0f), 4);
    }

    [Fact]
    public void BoundsNormalization()
    {
        Assert.Equal((0f, 0f, true), DamageMath.NormalizeBounds(5f, 2f));
        Assert.Equal((0f, 3f, false), DamageMath.NormalizeBounds(-1f, 3f));
    }

    [Fact]
    public void ModesParse()
    {
        Assert.Equal(DamageMode.Scale, DamageMath.ParseMode(" scale "));
        Assert.Equal(DamageMode.Off, DamageMath.ParseMode("garbage"));
        Assert.Equal(FlatPolicy.Scaled, DamageMath.ParseFlat("SCALED"));
    }

    [Fact]
    public void VerifySampling()
    {
        Assert.True(DamageMath.ShouldVerify(0));
        Assert.True(DamageMath.ShouldVerify(19));
        Assert.False(DamageMath.ShouldVerify(21));
        Assert.True(DamageMath.ShouldVerify(256));
    }
}

public class AttributionRuleTests
{
    static readonly DamageSnapshot A = new(2f, 0, 0, FlatPolicy.Unchanged, true);
    static readonly DamageSnapshot B = new(0.5f, 0, 0, FlatPolicy.Unchanged, true);

    [Fact]
    public void NoCandidates_Unmatched() => Assert.Equal(AttributionOutcome.Unmatched, AttributionRule.Decide(new CastCandidate[0], false, out _));

    [Fact]
    public void Overflow_Ambiguous() => Assert.Equal(AttributionOutcome.Ambiguous,
        AttributionRule.Decide(new[] { new CastCandidate(1, true, A) }, true, out _));

    [Fact]
    public void TwoGroups_Ambiguous() => Assert.Equal(AttributionOutcome.Ambiguous,
        AttributionRule.Decide(new[] { new CastCandidate(1, true, A), new CastCandidate(2, true, A) }, false, out _));

    [Fact]
    public void SameGroupDifferentSnapshots_Ambiguous()   // window cast then per-hit cast (rev 6.2 #1)
    {
        var old = A with { PerHit = false };
        Assert.Equal(AttributionOutcome.Ambiguous,
            AttributionRule.Decide(new[] { new CastCandidate(1, true, old), new CastCandidate(1, true, A) }, false, out _));
        Assert.Equal(AttributionOutcome.Ambiguous,
            AttributionRule.Decide(new[] { new CastCandidate(1, true, A), new CastCandidate(1, true, B) }, false, out _));
    }

    [Fact]
    public void SingleCapturedGroup_AttributedNewest()
    {
        var r = AttributionRule.Decide(new[] { new CastCandidate(1, true, A), new CastCandidate(1, true, A) }, false, out int i);
        Assert.Equal(AttributionOutcome.Attributed, r);
        Assert.Equal(1, i);
    }

    [Fact]
    public void NativeGroup_Native() => Assert.Equal(AttributionOutcome.Native,
        AttributionRule.Decide(new[] { new CastCandidate(7, false, DamageSnapshot.Identity) }, false, out _));
}

public class CooldownMathTests
{
    [Fact] public void AbsoluteWins() => Assert.Equal(12f, CooldownMath.Resolve(8f, 12f, 0.5f, 0f, false));
    [Fact] public void ScaleKeepsLiveCdr() => Assert.Equal(4f, CooldownMath.Resolve(8f, null, 0.5f, 0f, false));
    [Fact] public void FloorAppliesAfterScale() => Assert.Equal(6f, CooldownMath.Resolve(8f, null, 0.5f, 6f, false));
    [Fact] public void FloorAppliesToAbsolute() => Assert.Equal(6f, CooldownMath.Resolve(8f, 2f, 1f, 6f, false));
    [Fact] public void ChargesSkipped() => Assert.Null(CooldownMath.Resolve(8f, 12f, 0.5f, 6f, true));
    [Fact] public void NoLiveCooldownNoScale() => Assert.Null(CooldownMath.Resolve(0f, null, 2f, 0f, false));
    [Fact] public void NoChangeReturnsNull() => Assert.Null(CooldownMath.Resolve(8f, null, 1f, 0f, false));
    [Fact] public void FloorAloneRaisesShortCooldown() => Assert.Equal(5f, CooldownMath.Resolve(2f, null, 1f, 5f, false));
    [Fact] public void FloorCannotCreateACooldownFromNothing() => Assert.Null(CooldownMath.Resolve(0f, null, 1f, 5f, false));
    [Fact] public void BadScaleTreatedAsIdentity() => Assert.Null(CooldownMath.Resolve(8f, null, float.NaN, 0f, false));
    [Fact] public void EffectiveScale() => Assert.Equal(1.5f, CooldownMath.EffectiveScale(3f, 0.5f), 4);
}
