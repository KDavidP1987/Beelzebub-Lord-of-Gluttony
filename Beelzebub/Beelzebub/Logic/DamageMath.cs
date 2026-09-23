using System;

namespace Beelzebub.Logic;

// v0.133.0 (P2) — PURE damage math (no game types), shared by DealDamageSystemPatch and Beelzebub.Tests.

/// <summary>Which damage path is live. Off = legacy power window only; Telemetry = compute + count per hit but
/// never mutate; Scale = mutate the power term of attributed hits (and stop opening legacy windows).</summary>
public enum DamageMode { Off, Telemetry, Scale }

/// <summary>What happens to the flat (RawDamage) term of an attributed hit. %-HP is never scaled.</summary>
public enum FlatPolicy { Unchanged, Scaled }

/// <summary>Per-cast damage values, frozen when the cast starts so a reload mid-fight never changes an
/// in-flight hit. <see cref="PerHit"/> records which path the cast took (true = no legacy window was opened).</summary>
public readonly record struct DamageSnapshot(float Scale, float MinFactor, float MaxFactor, FlatPolicy Flat, bool PerHit)
{
    public static readonly DamageSnapshot Identity = new(1f, 0f, 0f, FlatPolicy.Unchanged, false);
}

public static class DamageMath
{
    // MainDamageType: Physical = 0, Spell = 1 (the power-scaled types). Fire/Holy/Silver/Garlic/... are
    // environmental and never scaled.
    public const int MainTypePhysical = 0;
    public const int MainTypeSpell = 1;

    public static DamageMode ParseMode(string s)
        => Enum.TryParse<DamageMode>((s ?? "").Trim(), ignoreCase: true, out var m) ? m : DamageMode.Off;

    public static FlatPolicy ParseFlat(string s)
        => Enum.TryParse<FlatPolicy>((s ?? "").Trim(), ignoreCase: true, out var f) ? f : FlatPolicy.Unchanged;

    /// <summary>Defaults × entry × (Boosted ? factor : 1). A non-finite or non-positive result means "no change".</summary>
    public static float EffectiveScale(float defaultsScale, float entryScale, bool boosted, float boostedFactor)
    {
        float s = Sane(defaultsScale) * Sane(entryScale) * (boosted ? Sane(boostedFactor) : 1f);
        return float.IsFinite(s) && s > 0f ? s : 1f;
    }

    static float Sane(float v) => float.IsFinite(v) && v > 0f ? v : 1f;

    /// <summary>Normalise the admin clamp band: negative/non-finite → 0 (off); if both set and min &gt; max, both
    /// are dropped (reported by the caller) rather than guessing which one the admin meant.</summary>
    public static (float min, float max, bool invalid) NormalizeBounds(float min, float max)
    {
        float lo = float.IsFinite(min) && min > 0f ? min : 0f;
        float hi = float.IsFinite(max) && max > 0f ? max : 0f;
        if (lo > 0f && hi > 0f && lo > hi) return (0f, 0f, true);
        return (lo, hi, false);
    }

    public static bool IsPowerTyped(int mainType) => mainType == MainTypePhysical || mainType == MainTypeSpell;

    /// <summary>Scale the power coefficient, then clamp the FINAL factor into [min, max] (0 = that bound off).</summary>
    public static float ScaleFactor(float mainFactor, in DamageSnapshot s)
    {
        float f = mainFactor * s.Scale;
        if (s.MinFactor > 0f && f < s.MinFactor) f = s.MinFactor;
        if (s.MaxFactor > 0f && f > s.MaxFactor) f = s.MaxFactor;
        return f;
    }

    /// <summary>Would this snapshot change anything at all? (lets the hot path early-out)</summary>
    public static bool IsIdentity(in DamageSnapshot s)
        => Math.Abs(s.Scale - 1f) < 1e-4f && s.MinFactor <= 0f && s.MaxFactor <= 0f;

    /// <summary>
    /// The per-hit decision. Only Physical/Spell hits are touched; a non-positive or non-finite factor is left
    /// alone entirely, flat term included (not a power-scaled hit). The flat term follows <see cref="FlatPolicy"/>; RawDamagePercent is
    /// never an input. Returns true when either output differs from its input.
    /// </summary>
    public static bool TryComputeHit(int mainType, float mainFactor, float rawDamage, in DamageSnapshot s,
        out float newFactor, out float newRaw)
    {
        newFactor = mainFactor;
        newRaw = rawDamage;
        // A non-positive/non-finite power factor means this entry is not a power-scaled hit: leave BOTH terms alone.
        if (!IsPowerTyped(mainType) || !float.IsFinite(mainFactor) || mainFactor <= 0f) return false;
        newFactor = ScaleFactor(mainFactor, s);
        bool changed = Math.Abs(newFactor - mainFactor) > 1e-6f;
        if (s.Flat == FlatPolicy.Scaled && float.IsFinite(rawDamage) && rawDamage > 0f)
        {
            newRaw = rawDamage * s.Scale;
            changed |= Math.Abs(newRaw - rawDamage) > 1e-6f;
        }
        return changed;
    }

    /// <summary>Write-probe sampling: verify the first <paramref name="always"/> writes, then 1 in <paramref name="every"/>.</summary>
    public static bool ShouldVerify(long writeIndex, int always = 20, int every = 256)
        => writeIndex < always || (every > 0 && writeIndex % every == 0);
}
