using System.Collections.Generic;
using System.Linq;

namespace Beelzebub.Logic;

// v0.137.3 (mounted-bar-reset A2) — DestroyUtility.Destroy does not stamp DestroyTag in the same frame for every buff
// (the mount control buff read tagAfter=False right after the call), so a DestroyTag check alone let the orphan sweep
// destroy the same buff a second time in one frame — the double-destroy crash class. The ledger remembers each destroy
// issued for a few frames; an entity key is (index << 32) | version, so a recycled index is a different key.

public sealed class DestroyLedger
{
    /// <summary>Frames an issued destroy is remembered; the engine reaps a destroyed entity well within this.</summary>
    public const int WindowFrames = 30;
    const int PruneAbove = 256;

    readonly Dictionary<long, int> _issued = new();

    public static long Key(int index, int version) => ((long)index << 32) | (uint)version;

    /// <summary>True (and remembered) when no destroy of <paramref name="key"/> was issued within the window;
    /// false when one was, so the caller must not destroy it again.</summary>
    public bool TryIssue(long key, int frame)
    {
        if (IsIssued(key, frame)) return false;
        if (_issued.Count > PruneAbove) Prune(frame);
        _issued[key] = frame;
        return true;
    }

    /// <summary>A destroy of <paramref name="key"/> was issued within the window ending at <paramref name="frame"/>.</summary>
    public bool IsIssued(long key, int frame) =>
        _issued.TryGetValue(key, out int at) && frame - at >= 0 && frame - at < WindowFrames;

    public int Count => _issued.Count;

    void Prune(int frame)
    {
        foreach (var k in _issued.Where(kv => !(frame - kv.Value >= 0 && frame - kv.Value < WindowFrames)).Select(kv => kv.Key).ToList())
            _issued.Remove(k);
    }
}
