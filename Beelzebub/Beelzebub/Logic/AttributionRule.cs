using System.Collections.Generic;

namespace Beelzebub.Logic;

// v0.133.0 (P2) — PURE attribution rule for per-hit damage (no game types; unit-tested).

public enum AttributionOutcome
{
    Attributed,   // exactly one captured group, one consistent snapshot → scale with it
    Ambiguous,    // >1 candidate group, snapshots disagree, or the cast history overflowed → vanilla
    Unmatched,    // no retained cast owns this prefab → vanilla
    Native,       // exactly one group, but it's a native (not captured) ability → vanilla, not ours
}

/// <summary>One retained cast whose group OWNS the hit's source prefab (owners ∩ retained casts), oldest first.</summary>
public readonly record struct CastCandidate(int Group, bool Captured, DamageSnapshot Snapshot);

public static class AttributionRule
{
    /// <summary>
    /// owners(prefab) ∩ the player's retained casts (native included) must be exactly ONE distinct group; that
    /// group must be captured; and every retained cast of it must carry the SAME snapshot (else a delayed hit
    /// from an older cast could inherit a newer cast's path/scale — rev 6.2 #1). Never guesses by recency.
    /// <paramref name="index"/> is the newest matching candidate when attributed, else -1.
    /// </summary>
    public static AttributionOutcome Decide(IReadOnlyList<CastCandidate> owned, bool overflow, out int index)
    {
        index = -1;
        if (overflow) return AttributionOutcome.Ambiguous;
        if (owned == null || owned.Count == 0) return AttributionOutcome.Unmatched;
        int group = owned[0].Group;
        var snap = owned[0].Snapshot;
        for (int i = 1; i < owned.Count; i++)
        {
            if (owned[i].Group != group) return AttributionOutcome.Ambiguous;
            if (owned[i].Snapshot != snap) return AttributionOutcome.Ambiguous;
        }
        if (!owned[owned.Count - 1].Captured) return AttributionOutcome.Native;
        index = owned.Count - 1;
        return AttributionOutcome.Attributed;
    }
}
