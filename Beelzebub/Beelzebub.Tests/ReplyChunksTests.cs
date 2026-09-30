using System.Linq;
using System.Text;
using Beelzebub.Logic;
using Xunit;

// v0.137.0 — `[BEELZ:*]` chunking stays under VCF's 512-byte reply cap in BYTES (the in-game
// `api catalog abilities` crash on a v0.136 note with em dashes).
public class ReplyChunksTests
{
    static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);

    [Fact]
    public void Pack_fails_when_multibyte_text_pushes_a_line_over_the_byte_cap()
    {
        // the crash shape: many short fields plus a 256-character note full of 3-byte em dashes
        string note = "notes=" + string.Concat(Enumerable.Repeat("a_—_", 64));
        string body = string.Join(" ", Enumerable.Range(0, 30).Select(i => $"field{i}=value{i}")) + " " + note + " tail=1";
        var lines = ReplyChunks.Pack("catalog-ability", "an=AB_Bandit_StoneBreaker_MountainRumbler_Hard_AbilityGroup", body);
        Assert.All(lines, l => Assert.True(Bytes(l) <= ReplyChunks.MaxLineBytes, $"{Bytes(l)} bytes: {l}"));
    }

    [Fact]
    public void Pack_fails_when_a_token_is_split_lost_or_reordered()
    {
        string body = string.Join(" ", Enumerable.Range(0, 80).Select(i => $"k{i}=v{i}_—"));
        var lines = ReplyChunks.Pack("info", "i=5", body);
        Assert.True(lines.Count > 1);
        var tokens = lines.SelectMany((l, k) =>
        {
            string prefix = $"[BEELZ:info] i=5 part={k + 1}/{lines.Count} ";
            Assert.StartsWith(prefix, l);
            return l.Substring(prefix.Length).Split(' ');
        });
        Assert.Equal(body.Split(' '), tokens);
    }

    [Fact]
    public void Pack_fails_when_one_oversize_token_is_not_cut_under_the_cap()
    {
        string body = "notes=" + new string('—', 400);
        var lines = ReplyChunks.Pack("catalog-ability", "an=X", body);
        Assert.Single(lines);
        Assert.True(Bytes(lines[0]) <= ReplyChunks.MaxLineBytes);
        Assert.StartsWith("[BEELZ:catalog-ability] an=X part=1/1 notes=—", lines[0]);
    }

    [Fact]
    public void Pack_fails_when_a_short_line_is_not_a_single_part()
    {
        Assert.Equal(new[] { "[BEELZ:info] i=1 part=1/1 a=1 b=2" }, ReplyChunks.Pack("info", "i=1", "a=1 b=2"));
        Assert.Equal(new[] { "[BEELZ:info] i=1 part=1/1 " }, ReplyChunks.Pack("info", "i=1", ""));
    }
}
