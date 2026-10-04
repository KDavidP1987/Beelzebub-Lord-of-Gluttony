using Beelzebub.Logic;
using Xunit;

// mounted-bar-reset A2 — a destroy issued this frame (DestroyTag not yet stamped) is never issued twice.
public class DestroyLedgerTests
{
    static readonly long Mount = DestroyLedger.Key(582145, 14);

    [Fact]
    public void TryIssue_fails_when_the_same_entity_is_destroyed_twice_in_one_frame()
    {
        var l = new DestroyLedger();
        Assert.True(l.TryIssue(Mount, 100));
        Assert.False(l.TryIssue(Mount, 100));
        Assert.True(l.IsIssued(Mount, 100));
    }

    [Fact]
    public void TryIssue_fails_when_a_second_destroy_inside_the_window_is_allowed()
    {
        var l = new DestroyLedger();
        l.TryIssue(Mount, 100);
        Assert.False(l.TryIssue(Mount, 100 + DestroyLedger.WindowFrames - 1));
    }

    [Fact]
    public void TryIssue_fails_when_another_entity_or_a_recycled_index_is_blocked()
    {
        var l = new DestroyLedger();
        l.TryIssue(Mount, 100);
        Assert.True(l.TryIssue(DestroyLedger.Key(582146, 14), 100));   // another entity
        Assert.True(l.TryIssue(DestroyLedger.Key(582145, 15), 100));   // same index, new version
        Assert.False(l.IsIssued(DestroyLedger.Key(1, 1), 100));       // never issued
    }

    [Fact]
    public void Forget_fails_when_a_destroy_that_threw_still_reads_as_issued()
    {
        var l = new DestroyLedger();
        l.TryIssue(Mount, 100);
        l.Forget(Mount);
        Assert.False(l.IsIssued(Mount, 100));
        Assert.True(l.TryIssue(Mount, 100));
    }

    [Fact]
    public void TryIssue_fails_when_an_old_destroy_blocks_forever_or_the_ledger_grows_unbounded()
    {
        var l = new DestroyLedger();
        l.TryIssue(Mount, 100);
        Assert.True(l.TryIssue(Mount, 100 + DestroyLedger.WindowFrames));
        for (int i = 0; i < 1000; i++) l.TryIssue(DestroyLedger.Key(i, 1), i);   // one per frame, old ones age out
        Assert.True(l.Count <= 300, $"ledger holds {l.Count} keys");
    }
}
