using System;

namespace Beelzebub.Logic;

// v0.134.0 (P3) — PURE cooldown precedence, shared by the native-bar enforcer and `.beelz cast` (unit-tested).

public static class CooldownMath
{
    /// <summary>
    /// The cooldown a CAPTURED cast should end up with. Precedence:
    ///   1. absolute <paramref name="absoluteSeconds"/> (the `cooldown` field) replaces everything;
    ///   2. else <paramref name="scale"/> (Defaults × entry `cooldownscale`) × the LIVE cooldown the engine set
    ///      (so gear/buff cooldown reduction still counts);
    ///   3. then the global floor (Grant_MinimumCooldownSeconds; 0 = off).
    /// Returns null when nothing should change: charge-based ability (skipped entirely — rev 6.1 #19), no live
    /// cooldown to scale, or the result equals the live value.
    /// </summary>
    public static float? Resolve(float liveSeconds, float? absoluteSeconds, float scale, float floorSeconds, bool hasCharges)
    {
        if (hasCharges) return null;
        float target;
        if (absoluteSeconds is float abs && float.IsFinite(abs) && abs >= 0f) target = abs;
        else
        {
            if (!float.IsFinite(liveSeconds) || liveSeconds <= 0f) return null;
            float s = float.IsFinite(scale) && scale > 0f ? scale : 1f;
            target = liveSeconds * s;
        }
        if (float.IsFinite(floorSeconds) && floorSeconds > 0f && target < floorSeconds) target = floorSeconds;
        if (Math.Abs(target - liveSeconds) < 0.01f) return null;
        return target;
    }

    /// <summary>Effective scale = Defaults.CooldownScale × entry.CooldownScale (non-positive/non-finite → 1).</summary>
    public static float EffectiveScale(float defaultsScale, float entryScale)
    {
        float a = float.IsFinite(defaultsScale) && defaultsScale > 0f ? defaultsScale : 1f;
        float b = float.IsFinite(entryScale) && entryScale > 0f ? entryScale : 1f;
        return a * b;
    }
}
